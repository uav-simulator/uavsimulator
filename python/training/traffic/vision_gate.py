"""Traffic-light state extraction from RGB camera frames.

This module is intentionally small and deterministic. It is the bridge between
the current ground-truth traffic-light gate and the later learned ONNX gate:
first prove that the visible signal can be localized in real camera frames,
then use the same crops/labels to train and validate a model.
"""
from __future__ import annotations

import argparse
import json
from dataclasses import asdict, dataclass
from pathlib import Path
from typing import Sequence

import cv2
import numpy as np
from PIL import Image

TRAFFIC_LIGHT_STATES = ("Red", "Yellow", "Green", "None")


@dataclass(frozen=True)
class TrafficLightVisionResult:
    state: str
    confidence: float
    bbox_xyxy: tuple[int, int, int, int] | None
    blob_bbox_xyxy: tuple[int, int, int, int] | None = None
    area_px: int = 0
    score: float = 0.0

    def to_dict(self) -> dict:
        data = asdict(self)
        if self.bbox_xyxy is not None:
            data["bbox_xyxy"] = list(self.bbox_xyxy)
        if self.blob_bbox_xyxy is not None:
            data["blob_bbox_xyxy"] = list(self.blob_bbox_xyxy)
        return data


@dataclass(frozen=True)
class _Candidate:
    state: str
    score: float
    confidence: float
    bbox_xyxy: tuple[int, int, int, int]
    blob_bbox_xyxy: tuple[int, int, int, int]
    area_px: int


def detect_traffic_light_state(
    image_rgb: np.ndarray,
    *,
    min_area_px: int | None = None,
    max_area_fraction: float = 0.005,
    max_component_width_fraction: float = 0.07,
    roi_bottom_fraction: float = 0.78,
    min_candidate_score: float = 0.12,
) -> TrafficLightVisionResult:
    """Detect the active traffic-light color in an RGB frame.

    The detector looks for small saturated red/yellow/green blobs in the upper
    part of the camera image. Large colored regions such as grass, buildings,
    or road signs are filtered out by component size.
    """
    image = _as_uint8_rgb(image_rgb)
    height, width = image.shape[:2]
    hsv = cv2.cvtColor(image, cv2.COLOR_RGB2HSV)

    if min_area_px is None:
        min_area_px = max(3, int(width * height * 0.000005))
    max_area_px = max(min_area_px + 1, int(width * height * max_area_fraction))

    candidates: list[_Candidate] = []
    housing_mask = _traffic_light_housing_mask(hsv)
    for state in ("Red", "Yellow", "Green"):
        mask = _state_mask(hsv, state)
        cutoff_y = int(np.clip(roi_bottom_fraction, 0.1, 1.0) * height)
        mask[cutoff_y:, :] = 0
        candidates.extend(
            _components_to_candidates(
                image,
                hsv,
                mask,
                state,
                min_area_px=min_area_px,
                max_area_px=max_area_px,
                max_component_width_px=max(32, int(width * max_component_width_fraction)),
                housing_mask=housing_mask,
            )
        )

    if not candidates:
        return TrafficLightVisionResult(
            state="None",
            confidence=0.0,
            bbox_xyxy=None,
        )

    best = max(candidates, key=lambda candidate: candidate.score)
    if best.state == "Yellow":
        best = _prefer_active_bulb_inside_yellow_housing(best, candidates)
    if best.score < min_candidate_score:
        return TrafficLightVisionResult(
            state="None",
            confidence=0.0,
            bbox_xyxy=None,
        )
    return TrafficLightVisionResult(
        state=best.state,
        confidence=best.confidence,
        bbox_xyxy=best.bbox_xyxy,
        blob_bbox_xyxy=best.blob_bbox_xyxy,
        area_px=best.area_px,
        score=best.score,
    )


def crop_detection(
    image_rgb: np.ndarray,
    result: TrafficLightVisionResult,
) -> np.ndarray | None:
    """Return the expanded detected traffic-light crop, or None."""
    if result.bbox_xyxy is None:
        return None
    image = _as_uint8_rgb(image_rgb)
    x1, y1, x2, y2 = result.bbox_xyxy
    x1 = int(np.clip(x1, 0, image.shape[1]))
    x2 = int(np.clip(x2, 0, image.shape[1]))
    y1 = int(np.clip(y1, 0, image.shape[0]))
    y2 = int(np.clip(y2, 0, image.shape[0]))
    if x2 <= x1 or y2 <= y1:
        return None
    return image[y1:y2, x1:x2].copy()


def render_detection_overlay(
    image_rgb: np.ndarray,
    result: TrafficLightVisionResult,
) -> np.ndarray:
    """Draw the traffic-light detection over an RGB frame."""
    image = _as_uint8_rgb(image_rgb).copy()
    color = {
        "Red": (255, 40, 40),
        "Yellow": (255, 214, 48),
        "Green": (40, 230, 80),
        "None": (190, 190, 190),
    }.get(result.state, (190, 190, 190))

    if result.bbox_xyxy is None:
        label = "Traffic light: None"
        cv2.putText(image, label, (18, 34), cv2.FONT_HERSHEY_SIMPLEX, 0.7, color, 2, cv2.LINE_AA)
        return image

    x1, y1, x2, y2 = result.bbox_xyxy
    cv2.rectangle(image, (x1, y1), (x2, y2), color, 2)
    if result.blob_bbox_xyxy is not None:
        bx1, by1, bx2, by2 = result.blob_bbox_xyxy
        cv2.rectangle(image, (bx1, by1), (bx2, by2), color, 1)
    label = f"{result.state} {result.confidence:.2f}"
    text_y = max(22, y1 - 8)
    cv2.putText(image, label, (x1, text_y), cv2.FONT_HERSHEY_SIMPLEX, 0.65, color, 2, cv2.LINE_AA)
    return image


def analyze_image_file(
    image_path: Path,
    *,
    output_overlay: Path | None = None,
    output_crop: Path | None = None,
    summary_path: Path | None = None,
) -> TrafficLightVisionResult:
    image = np.array(Image.open(image_path).convert("RGB"))
    result = detect_traffic_light_state(image)

    if output_overlay is not None:
        output_overlay.parent.mkdir(parents=True, exist_ok=True)
        Image.fromarray(render_detection_overlay(image, result)).save(output_overlay)

    if output_crop is not None:
        crop = crop_detection(image, result)
        if crop is not None:
            output_crop.parent.mkdir(parents=True, exist_ok=True)
            Image.fromarray(crop).save(output_crop)

    if summary_path is not None:
        summary_path.parent.mkdir(parents=True, exist_ok=True)
        summary = {
            "image": str(image_path),
            "width": int(image.shape[1]),
            "height": int(image.shape[0]),
            "result": result.to_dict(),
        }
        summary_path.write_text(json.dumps(summary, indent=2), encoding="utf-8")

    return result


def _as_uint8_rgb(image_rgb: np.ndarray) -> np.ndarray:
    image = np.asarray(image_rgb)
    if image.ndim != 3 or image.shape[2] != 3:
        raise ValueError("image_rgb must have shape (H, W, 3)")
    if image.dtype == np.uint8:
        return np.ascontiguousarray(image)
    image_float = image.astype(np.float32)
    if image_float.max(initial=0.0) <= 1.0:
        image_float *= 255.0
    return np.ascontiguousarray(np.clip(image_float, 0, 255).astype(np.uint8))


def _state_mask(hsv: np.ndarray, state: str) -> np.ndarray:
    if state == "Red":
        low_red = cv2.inRange(hsv, (0, 85, 80), (12, 255, 255))
        high_red = cv2.inRange(hsv, (168, 85, 80), (179, 255, 255))
        return cv2.bitwise_or(low_red, high_red)
    if state == "Yellow":
        return cv2.inRange(hsv, (15, 70, 165), (40, 255, 255))
    if state == "Green":
        return cv2.inRange(hsv, (42, 65, 80), (96, 255, 255))
    raise ValueError(f"Unsupported state: {state}")


def _traffic_light_housing_mask(hsv: np.ndarray) -> np.ndarray:
    return cv2.inRange(hsv, (18, 35, 70), (48, 255, 255))


def _components_to_candidates(
    image_rgb: np.ndarray,
    hsv: np.ndarray,
    mask: np.ndarray,
    state: str,
    *,
    min_area_px: int,
    max_area_px: int,
    max_component_width_px: int,
    housing_mask: np.ndarray,
) -> list[_Candidate]:
    label_count, labels, stats, _centroids = cv2.connectedComponentsWithStats(mask, connectivity=8)
    height, width = image_rgb.shape[:2]
    candidates: list[_Candidate] = []

    for label_id in range(1, label_count):
        area = int(stats[label_id, cv2.CC_STAT_AREA])
        if area < min_area_px or area > max_area_px:
            continue

        x = int(stats[label_id, cv2.CC_STAT_LEFT])
        y = int(stats[label_id, cv2.CC_STAT_TOP])
        component_width = int(stats[label_id, cv2.CC_STAT_WIDTH])
        component_height = int(stats[label_id, cv2.CC_STAT_HEIGHT])
        if component_width <= 0 or component_height <= 0:
            continue
        if component_width > max_component_width_px:
            continue

        aspect = component_width / max(1, component_height)
        if aspect < 0.25 or aspect > 4.0:
            continue

        component_mask = labels == label_id
        mean_rgb = image_rgb[component_mask].mean(axis=0)
        strength = _color_strength(state, mean_rgb)
        min_strength = 0.17 if state == "Green" else 0.22
        if strength < min_strength:
            continue

        mean_hsv = hsv[component_mask].mean(axis=0)
        value = float(mean_hsv[2]) / 255.0
        center_y = y + component_height * 0.5
        center_x = x + component_width * 0.5
        upper_weight = _signal_height_weight(center_y, height)
        center_weight = _center_image_weight(center_x, width)
        size_weight = _signal_size_weight(area)
        confidence = float(np.clip(strength * 0.65 + size_weight * 0.25 + value * 0.10, 0.0, 1.0))

        blob_bbox = (x, y, x + component_width, y + component_height)
        expanded_bbox = _expand_to_traffic_light_bbox(blob_bbox, state, image_rgb.shape)
        if _looks_like_status_overlay(image_rgb, expanded_bbox):
            continue
        if _looks_like_construction_barricade(image_rgb, expanded_bbox):
            continue
        housing_support = _housing_support(housing_mask, expanded_bbox, blob_bbox)
        if state in {"Red", "Green"} and housing_support < 0.08:
            continue
        state_weight = 0.72 if state == "Yellow" else 1.0
        score = (
            confidence
            * (0.45 + size_weight)
            * upper_weight
            * center_weight
            * (0.2 + housing_support * 0.8)
            * state_weight
        )
        candidates.append(
            _Candidate(
                state=state,
                score=float(score),
                confidence=confidence,
                bbox_xyxy=expanded_bbox,
                blob_bbox_xyxy=blob_bbox,
                area_px=area,
            )
        )

    return candidates


def _color_strength(state: str, mean_rgb: Sequence[float]) -> float:
    r, g, b = [float(channel) for channel in mean_rgb]
    if state == "Red":
        return max(0.0, (r - max(g, b)) / 255.0)
    if state == "Yellow":
        return max(0.0, (min(r, g) - b) / 255.0)
    if state == "Green":
        return max(0.0, (g - max(r, b)) / 255.0)
    return 0.0


def _signal_height_weight(center_y: float, image_height: int) -> float:
    normalized_y = center_y / max(1.0, float(image_height))
    if normalized_y <= 0.40:
        return 1.0
    return float(np.clip(1.0 - (normalized_y - 0.40) / 0.12 * 0.85, 0.15, 1.0))


def _center_image_weight(center_x: float, image_width: int) -> float:
    normalized_distance = abs(center_x / max(1.0, float(image_width)) - 0.5) / 0.5
    return float(np.clip(1.0 - normalized_distance * 0.65, 0.35, 1.0))


def _prefer_active_bulb_inside_yellow_housing(
    yellow_candidate: _Candidate,
    candidates: Sequence[_Candidate],
) -> _Candidate:
    x1, y1, x2, y2 = yellow_candidate.bbox_xyxy
    nested = []
    for candidate in candidates:
        if candidate.state not in {"Red", "Green"}:
            continue
        bx1, by1, bx2, by2 = candidate.blob_bbox_xyxy
        cx = (bx1 + bx2) * 0.5
        cy = (by1 + by2) * 0.5
        if x1 <= cx <= x2 and y1 <= cy <= y2 and candidate.score >= yellow_candidate.score * 0.25:
            nested.append(candidate)
    if not nested:
        return yellow_candidate
    return max(nested, key=lambda candidate: candidate.score)


def _signal_size_weight(area_px: int) -> float:
    if area_px <= 0:
        return 0.0
    target_area = 80.0
    distance = abs(np.log(max(1.0, float(area_px)) / target_area))
    return float(np.clip(np.exp(-distance * 0.65), 0.15, 1.0))


def _housing_support(
    housing_mask: np.ndarray,
    expanded_bbox_xyxy: tuple[int, int, int, int],
    blob_bbox_xyxy: tuple[int, int, int, int],
) -> float:
    x1, y1, x2, y2 = expanded_bbox_xyxy
    bx1, by1, bx2, by2 = blob_bbox_xyxy
    crop = housing_mask[y1:y2, x1:x2].copy()
    if crop.size == 0:
        return 0.0
    rx1 = max(0, bx1 - x1)
    ry1 = max(0, by1 - y1)
    rx2 = min(crop.shape[1], bx2 - x1)
    ry2 = min(crop.shape[0], by2 - y1)
    if rx2 > rx1 and ry2 > ry1:
        crop[ry1:ry2, rx1:rx2] = 0
    housing_px = int(np.count_nonzero(crop))
    return float(np.clip(housing_px / 180.0, 0.0, 1.0))


def _looks_like_construction_barricade(
    image_rgb: np.ndarray,
    bbox_xyxy: tuple[int, int, int, int],
) -> bool:
    x1, y1, x2, y2 = bbox_xyxy
    crop = image_rgb[y1:y2, x1:x2]
    if crop.size == 0:
        return False

    hsv = cv2.cvtColor(crop, cv2.COLOR_RGB2HSV)
    orange = cv2.inRange(hsv, (4, 55, 95), (28, 255, 255))
    white = cv2.inRange(hsv, (0, 0, 165), (179, 75, 255))
    dark = cv2.inRange(hsv, (0, 0, 0), (179, 120, 80))
    total = float(crop.shape[0] * crop.shape[1])
    orange_fraction = np.count_nonzero(orange) / total
    white_fraction = np.count_nonzero(white) / total
    dark_fraction = np.count_nonzero(dark) / total

    if orange_fraction >= 0.08 and white_fraction >= 0.04 and orange_fraction + white_fraction >= 0.13:
        return True

    return (
        dark_fraction >= 0.28
        and orange_fraction >= 0.025
        and white_fraction >= 0.02
        and orange_fraction + white_fraction >= 0.08
    )


def _looks_like_status_overlay(
    image_rgb: np.ndarray,
    bbox_xyxy: tuple[int, int, int, int],
) -> bool:
    x1, y1, x2, y2 = bbox_xyxy
    crop = image_rgb[y1:y2, x1:x2]
    if crop.size == 0:
        return False

    touches_screen_edge = x1 <= 2 or y1 <= 2
    if not touches_screen_edge:
        return False

    crop_height, crop_width = crop.shape[:2]
    if crop_width < 80 or crop_height < 80:
        return False

    hsv = cv2.cvtColor(crop, cv2.COLOR_RGB2HSV)
    dark = cv2.inRange(hsv, (0, 0, 0), (179, 120, 55))
    white = cv2.inRange(hsv, (0, 0, 175), (179, 70, 255))
    total = float(crop_width * crop_height)
    dark_fraction = np.count_nonzero(dark) / total
    white_fraction = np.count_nonzero(white) / total

    return dark_fraction >= 0.12 and white_fraction >= 0.025


def _expand_to_traffic_light_bbox(
    blob_bbox_xyxy: tuple[int, int, int, int],
    state: str,
    image_shape: tuple[int, int, int],
) -> tuple[int, int, int, int]:
    x1, y1, x2, y2 = blob_bbox_xyxy
    height, width = image_shape[:2]
    blob_width = max(1, x2 - x1)
    blob_height = max(1, y2 - y1)
    unit = max(blob_width, blob_height, 6)
    pad_x = int(max(unit * 2.5, 12))

    if state == "Red":
        pad_top = int(max(unit * 2.0, 12))
        pad_bottom = int(max(unit * 6.5, 32))
    elif state == "Green":
        pad_top = int(max(unit * 6.5, 32))
        pad_bottom = int(max(unit * 2.0, 12))
    else:
        pad_top = int(max(unit * 4.0, 24))
        pad_bottom = int(max(unit * 4.0, 24))

    return (
        int(np.clip(x1 - pad_x, 0, width)),
        int(np.clip(y1 - pad_top, 0, height)),
        int(np.clip(x2 + pad_x, 0, width)),
        int(np.clip(y2 + pad_bottom, 0, height)),
    )


def main(argv: Sequence[str] | None = None) -> None:
    parser = argparse.ArgumentParser(description="Detect traffic-light state from an RGB image.")
    parser.add_argument("--image", type=Path, required=True)
    parser.add_argument("--output-overlay", type=Path)
    parser.add_argument("--output-crop", type=Path)
    parser.add_argument("--summary", type=Path)
    args = parser.parse_args(argv)

    result = analyze_image_file(
        args.image,
        output_overlay=args.output_overlay,
        output_crop=args.output_crop,
        summary_path=args.summary,
    )
    print(json.dumps(result.to_dict(), ensure_ascii=False))


if __name__ == "__main__":
    main()
