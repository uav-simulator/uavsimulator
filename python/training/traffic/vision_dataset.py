"""Build traffic-light image datasets from camera frames.

The dataset format intentionally matches ``tl_classifier.load_dataset_from_jsonl``:
each JSONL row stores ``modelInputPath`` and ``labelId`` relative to the JSONL
folder. Crops are 84x84 RGB JPEGs, ready for the current small CNN/ONNX path.
"""
from __future__ import annotations

import argparse
import json
from pathlib import Path
from typing import Iterable, Sequence

import numpy as np
from PIL import Image

from training.traffic.vision_gate import (
    TrafficLightVisionResult,
    crop_detection,
    detect_traffic_light_state,
)

LABEL_IDS = {"Red": 0, "Yellow": 1, "Green": 2, "None": 3}


def build_visual_dataset(
    image_paths: Iterable[Path],
    output_dir: Path,
    *,
    crop_size: tuple[int, int] = (84, 84),
    include_none: bool = True,
    min_confidence: float = 0.0,
) -> Path:
    """Detect traffic lights in ``image_paths`` and write a classifier JSONL dataset."""
    output_dir.mkdir(parents=True, exist_ok=True)
    crops_dir = output_dir / "crops"
    crops_dir.mkdir(exist_ok=True)
    dataset_path = output_dir / "dataset.jsonl"

    rows: list[dict] = []
    for index, image_path in enumerate(image_paths):
        image_path = Path(image_path)
        image = np.array(Image.open(image_path).convert("RGB"))
        result = detect_traffic_light_state(image)
        if result.state == "None" and not include_none:
            continue
        if result.state != "None" and result.confidence < min_confidence:
            continue

        crop = crop_detection(image, result)
        if crop is None:
            crop = image

        label_id = LABEL_IDS[result.state]
        crop_name = f"{index:05d}-{image_path.stem}-{result.state.lower()}.jpg"
        crop_path = crops_dir / crop_name
        Image.fromarray(crop).resize(crop_size, Image.Resampling.BILINEAR).save(
            crop_path,
            "JPEG",
            quality=90,
        )
        rows.append(_row_for_sample(image_path, crop_path, output_dir, result, label_id))

    with dataset_path.open("w", encoding="utf-8") as out:
        for row in rows:
            out.write(json.dumps(row, ensure_ascii=False) + "\n")
    return dataset_path


def build_hard_negative_dataset_from_shadow_eval(
    shadow_summary_path: Path,
    output_dir: Path,
    *,
    crop_size: tuple[int, int] = (84, 84),
    max_samples: int | None = None,
) -> Path:
    """Build ``None`` crops from false-positive camera-gate shadow samples."""
    shadow_summary_path = Path(shadow_summary_path)
    output_dir = Path(output_dir)
    output_dir.mkdir(parents=True, exist_ok=True)
    crops_dir = output_dir / "crops"
    crops_dir.mkdir(exist_ok=True)
    dataset_path = output_dir / "dataset.jsonl"

    summary = json.loads(shadow_summary_path.read_text(encoding="utf-8"))
    rows: list[dict] = []
    for sample in summary.get("samples", []):
        if max_samples is not None and len(rows) >= max_samples:
            break
        if not _is_safe_hard_negative(sample):
            continue

        image_path = Path(sample.get("framePath") or sample.get("sourceImage"))
        image = Image.open(image_path).convert("RGB")
        bbox_xyxy = [int(value) for value in sample["detectorBBox"]]
        crop = _crop_bbox(image, bbox_xyxy)
        crop_name = f"{len(rows):05d}-{image_path.stem}-hard-none.jpg"
        crop_path = crops_dir / crop_name
        crop.resize(crop_size, Image.Resampling.BILINEAR).save(crop_path, "JPEG", quality=90)
        rows.append(
            {
                "sourceImage": str(image_path),
                "modelInputPath": crop_path.relative_to(output_dir).as_posix(),
                "label": "None",
                "labelId": LABEL_IDS["None"],
                "hardNegative": True,
                "sourcePrediction": sample.get("prediction"),
                "detectorState": sample.get("detectorState"),
                "confidence": sample.get("detectorConfidence"),
                "bbox_xyxy": bbox_xyxy,
            }
        )

    with dataset_path.open("w", encoding="utf-8") as out:
        for row in rows:
            out.write(json.dumps(row, ensure_ascii=False) + "\n")
    return dataset_path


def build_hard_case_dataset_from_shadow_eval(
    shadow_summary_path: Path,
    output_dir: Path,
    *,
    crop_size: tuple[int, int] = (84, 84),
    max_samples: int | None = None,
    prediction_key: str = "prediction",
    label_key: str = "label",
) -> Path:
    """Build labeled crops for incorrect camera-gate shadow samples."""
    shadow_summary_path = Path(shadow_summary_path)
    output_dir = Path(output_dir)
    output_dir.mkdir(parents=True, exist_ok=True)
    crops_dir = output_dir / "crops"
    crops_dir.mkdir(exist_ok=True)
    dataset_path = output_dir / "dataset.jsonl"

    summary = json.loads(shadow_summary_path.read_text(encoding="utf-8"))
    rows: list[dict] = []
    for sample in summary.get("samples", []):
        if max_samples is not None and len(rows) >= max_samples:
            break
        if not _is_hard_case(sample, prediction_key=prediction_key, label_key=label_key):
            continue

        label = str(sample[label_key])
        label_id = LABEL_IDS[label]
        image_path = Path(sample.get("framePath") or sample.get("sourceImage"))
        image = Image.open(image_path).convert("RGB")
        bbox_xyxy = [int(value) for value in sample["detectorBBox"]]
        crop = _crop_bbox(image, bbox_xyxy)
        prediction = str(sample.get(prediction_key, "None"))
        crop_name = f"{len(rows):05d}-{image_path.stem}-{label.lower()}-from-{prediction.lower()}.jpg"
        crop_path = crops_dir / crop_name
        crop.resize(crop_size, Image.Resampling.BILINEAR).save(crop_path, "JPEG", quality=90)
        rows.append(
            {
                "sourceImage": str(image_path),
                "modelInputPath": crop_path.relative_to(output_dir).as_posix(),
                "label": label,
                "labelId": label_id,
                "hardCase": True,
                "sourceLabelKey": label_key,
                "sourcePrediction": prediction,
                "sourcePredictionKey": prediction_key,
                "sourceDetectorState": sample.get("detectorState"),
                "sourceClassifierState": sample.get("classifierState"),
                "confidence": sample.get("detectorConfidence"),
                "bbox_xyxy": bbox_xyxy,
                "trafficLightDistanceM": sample.get("trafficLightDistanceM"),
            }
        )

    with dataset_path.open("w", encoding="utf-8") as out:
        for row in rows:
            out.write(json.dumps(row, ensure_ascii=False) + "\n")
    return dataset_path


def build_visual_label_candidates_from_trace(
    trace_path: Path,
    output_dir: Path,
    *,
    target_labels: Sequence[str] = ("Red", "Yellow"),
    crop_size: tuple[int, int] = (84, 84),
    max_samples: int | None = None,
) -> Path:
    """Extract visual-label review candidates from recorder trace rows.

    This keeps camera labels separate from driving telemetry: ``telemetryLabel`` is
    the ground-truth gate state, while ``candidateVisualLabel`` is what the visual
    branch predicted from ``modelFrame``/crop.
    """
    trace_path = Path(trace_path)
    output_dir = Path(output_dir)
    output_dir.mkdir(parents=True, exist_ok=True)
    crops_dir = output_dir / "crops"
    crops_dir.mkdir(exist_ok=True)
    dataset_path = output_dir / "candidates.jsonl"
    target = set(target_labels)

    rows: list[dict] = []
    for sample in _read_jsonl(trace_path):
        if max_samples is not None and len(rows) >= max_samples:
            break
        telemetry_label = str(sample.get("gtState", "None"))
        visual_label = str(sample.get("visionPrediction", "None"))
        if telemetry_label not in target and visual_label not in target:
            continue
        crop_path = _resolve_existing_path(sample.get("cropPath"))
        if crop_path is None:
            continue

        crop_name = f"{len(rows):05d}-step-{int(sample.get('step', sample.get('tick', len(rows)))):04d}-{visual_label.lower()}.jpg"
        copied_crop = crops_dir / crop_name
        Image.open(crop_path).convert("RGB").resize(crop_size, Image.Resampling.BILINEAR).save(
            copied_crop,
            "JPEG",
            quality=90,
        )
        confidence = _safe_float(sample.get("visionConfidence"))
        rows.append(
            {
                "sourceTrace": str(trace_path),
                "sourceImage": sample.get("modelFramePath"),
                "sourceCrop": str(crop_path),
                "modelInputPath": copied_crop.relative_to(output_dir).as_posix(),
                "telemetryLabel": telemetry_label,
                "candidateVisualLabel": visual_label,
                "candidateConfidence": confidence,
                "needsHumanReview": telemetry_label != visual_label or confidence < 0.95,
                "step": sample.get("step"),
                "tick": sample.get("tick"),
                "distanceM": sample.get("gtDistanceM"),
                "speedMps": sample.get("speedMps"),
                "bbox_xyxy": sample.get("detectorBBox"),
                "classifierProbabilities": sample.get("classifierProbabilities"),
            }
        )

    _write_jsonl(dataset_path, rows)
    return dataset_path


def build_clear_visual_dataset_from_candidates(
    candidates_path: Path,
    output_dir: Path,
    *,
    target_labels: Sequence[str] = ("Red", "Yellow"),
    min_confidence: float = 0.95,
    per_label_limit: int | None = None,
    crop_size: tuple[int, int] = (84, 84),
) -> Path:
    """Build a conservative training seed from agreed telemetry/visual candidates."""
    candidates_path = Path(candidates_path)
    output_dir = Path(output_dir)
    output_dir.mkdir(parents=True, exist_ok=True)
    crops_dir = output_dir / "crops"
    crops_dir.mkdir(exist_ok=True)
    dataset_path = output_dir / "dataset.jsonl"
    target = set(target_labels)

    rows: list[dict] = []
    per_label_counts = {label: 0 for label in target}
    for candidate in _read_jsonl(candidates_path):
        label = str(candidate.get("candidateVisualLabel", "None"))
        if label not in target or label not in LABEL_IDS:
            continue
        if candidate.get("telemetryLabel") != label:
            continue
        if _safe_float(candidate.get("candidateConfidence")) < min_confidence:
            continue
        if per_label_limit is not None and per_label_counts[label] >= per_label_limit:
            continue

        source_crop = _resolve_candidate_crop(candidates_path, candidate)
        if source_crop is None:
            continue
        crop_name = f"{len(rows):05d}-step-{int(candidate.get('step') or candidate.get('tick') or len(rows)):04d}-{label.lower()}.jpg"
        copied_crop = crops_dir / crop_name
        Image.open(source_crop).convert("RGB").resize(crop_size, Image.Resampling.BILINEAR).save(
            copied_crop,
            "JPEG",
            quality=90,
        )
        per_label_counts[label] += 1
        rows.append(
            {
                "sourceImage": candidate.get("sourceImage"),
                "sourceCandidate": str(candidates_path),
                "modelInputPath": copied_crop.relative_to(output_dir).as_posix(),
                "label": label,
                "labelId": LABEL_IDS[label],
                "visualLabelSource": "telemetry-visual-agreement",
                "candidateConfidence": candidate.get("candidateConfidence"),
                "step": candidate.get("step"),
                "tick": candidate.get("tick"),
                "distanceM": candidate.get("distanceM"),
                "speedMps": candidate.get("speedMps"),
                "bbox_xyxy": candidate.get("bbox_xyxy"),
            }
        )

    _write_jsonl(dataset_path, rows)
    return dataset_path


def _row_for_sample(
    source_image_path: Path,
    crop_path: Path,
    output_dir: Path,
    result: TrafficLightVisionResult,
    label_id: int,
) -> dict:
    return {
        "sourceImage": str(source_image_path),
        "modelInputPath": crop_path.relative_to(output_dir).as_posix(),
        "label": result.state,
        "labelId": label_id,
        "confidence": result.confidence,
        "bbox_xyxy": list(result.bbox_xyxy) if result.bbox_xyxy is not None else None,
        "blob_bbox_xyxy": list(result.blob_bbox_xyxy) if result.blob_bbox_xyxy is not None else None,
        "area_px": result.area_px,
        "score": result.score,
    }


def _is_safe_hard_negative(sample: dict) -> bool:
    return (
        sample.get("label") == "None"
        and sample.get("prediction") not in {None, "None"}
        and isinstance(sample.get("detectorBBox"), list)
        and bool(sample.get("framePath") or sample.get("sourceImage"))
    )


def _is_hard_case(sample: dict, *, prediction_key: str = "prediction", label_key: str = "label") -> bool:
    label = str(sample.get(label_key, "None"))
    if prediction_key not in sample:
        return False
    prediction = str(sample.get(prediction_key, "None"))
    return (
        label in LABEL_IDS
        and prediction in LABEL_IDS
        and prediction != label
        and isinstance(sample.get("detectorBBox"), list)
        and bool(sample.get("framePath") or sample.get("sourceImage"))
    )


def _crop_bbox(image: Image.Image, bbox_xyxy: Sequence[int]) -> Image.Image:
    width, height = image.size
    x1, y1, x2, y2 = bbox_xyxy
    x1 = max(0, min(width, x1))
    x2 = max(0, min(width, x2))
    y1 = max(0, min(height, y1))
    y2 = max(0, min(height, y2))
    if x2 <= x1 or y2 <= y1:
        return image.copy()
    return image.crop((x1, y1, x2, y2))


def _read_jsonl(path: Path) -> list[dict]:
    return [json.loads(line) for line in Path(path).read_text(encoding="utf-8").splitlines() if line.strip()]


def _write_jsonl(path: Path, rows: Sequence[dict]) -> None:
    with Path(path).open("w", encoding="utf-8") as out:
        for row in rows:
            out.write(json.dumps(row, ensure_ascii=False) + "\n")


def _safe_float(value: object) -> float:
    try:
        return float(value)  # type: ignore[arg-type]
    except Exception:
        return 0.0


def _resolve_existing_path(value: object) -> Path | None:
    if not value:
        return None
    path = Path(str(value))
    return path if path.exists() else None


def _resolve_candidate_crop(candidates_path: Path, candidate: dict) -> Path | None:
    raw = candidate.get("modelInputPath")
    if not raw:
        return _resolve_existing_path(candidate.get("sourceCrop"))
    path = Path(str(raw))
    if not path.is_absolute():
        path = Path(candidates_path).parent / path
    return path if path.exists() else _resolve_existing_path(candidate.get("sourceCrop"))


def _parse_target_labels(raw: str) -> tuple[str, ...]:
    labels = tuple(label.strip() for label in raw.split(",") if label.strip())
    bad = [label for label in labels if label not in LABEL_IDS]
    if bad:
        raise ValueError(f"Unknown labels: {', '.join(bad)}")
    return labels


def main(argv: Sequence[str] | None = None) -> None:
    parser = argparse.ArgumentParser(description="Build a traffic-light visual dataset from images.")
    parser.add_argument("--output", type=Path, required=True)
    parser.add_argument("--shadow-summary", type=Path)
    parser.add_argument("--hard-cases", action="store_true")
    parser.add_argument("--trace-candidates", type=Path)
    parser.add_argument("--clear-from-candidates", type=Path)
    parser.add_argument("--prediction-key", default="prediction")
    parser.add_argument("--label-key", default="label")
    parser.add_argument("--include-none", action="store_true")
    parser.add_argument("--min-confidence", type=float, default=0.0)
    parser.add_argument("--target-labels", default="Red,Yellow")
    parser.add_argument("--per-label-limit", type=int)
    parser.add_argument("--max-samples", type=int)
    parser.add_argument("images", nargs="*", type=Path)
    args = parser.parse_args(argv)

    if args.trace_candidates is not None:
        dataset_path = build_visual_label_candidates_from_trace(
            args.trace_candidates,
            args.output,
            target_labels=_parse_target_labels(args.target_labels),
            max_samples=args.max_samples,
        )
    elif args.clear_from_candidates is not None:
        dataset_path = build_clear_visual_dataset_from_candidates(
            args.clear_from_candidates,
            args.output,
            target_labels=_parse_target_labels(args.target_labels),
            min_confidence=args.min_confidence,
            per_label_limit=args.per_label_limit,
        )
    elif args.shadow_summary is not None:
        if args.hard_cases:
            dataset_path = build_hard_case_dataset_from_shadow_eval(
                args.shadow_summary,
                args.output,
                max_samples=args.max_samples,
                prediction_key=args.prediction_key,
                label_key=args.label_key,
            )
        else:
            dataset_path = build_hard_negative_dataset_from_shadow_eval(
                args.shadow_summary,
                args.output,
                max_samples=args.max_samples,
            )
    else:
        dataset_path = build_visual_dataset(
            args.images,
            args.output,
            include_none=args.include_none,
            min_confidence=args.min_confidence,
        )
    print(dataset_path)


if __name__ == "__main__":
    main()
