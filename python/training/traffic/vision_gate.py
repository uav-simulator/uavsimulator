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
            )
        )

    if not candidates:
        return TrafficLightVisionResult(
            state="None",
            confidence=0.0,
            bbox_xyxy=None,
        )

    best = max(candidates, key=lambda candidate: candidate.score)
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
        return cv2.inRange(hsv, (15, 70, 90), (40, 255, 255))
    if state == "Green":
        return cv2.inRange(hsv, (42, 65, 80), (96, 255, 255))
    raise ValueError(f"Unsupported state: {state}")


def _components_to_candidates(
    image_rgb: np.ndarray,
    hsv: np.ndarray,
    mask: np.ndarray,
    state: str,
    *,
    min_area_px: int,
    max_area_px: int,
    max_component_width_px: int,
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
        if strength < 0.22:
            continue

        mean_hsv = hsv[component_mask].mean(axis=0)
        value = float(mean_hsv[2]) / 255.0
        center_y = y + component_height * 0.5
        upper_weight = _upper_image_weight(center_y, height)
        size_weight = min(1.0, area / max(1.0, min_area_px * 12.0))
        score = area * (0.75 + strength) * (0.65 + value * 0.35) * upper_weight
        confidence = float(np.clip(strength * 0.65 + size_weight * 0.25 + value * 0.10, 0.0, 1.0))

        blob_bbox = (x, y, x + component_width, y + component_height)
        candidates.append(
            _Candidate(
                state=state,
                score=float(score),
                confidence=confidence,
                bbox_xyxy=_expand_to_traffic_light_bbox(blob_bbox, state, image_rgb.shape),
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


def _upper_image_weight(center_y: float, image_height: int) -> float:
    normalized_y = center_y / max(1.0, float(image_height))
    if normalized_y <= 0.62:
        return 1.0
    return float(np.clip(1.0 - (normalized_y - 0.62) / 0.30, 0.25, 1.0))


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
