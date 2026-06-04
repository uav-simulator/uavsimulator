"""Shadow evaluation for the camera traffic-light gate.

This module evaluates the future visual gate without switching the runtime
driving gate. It runs the deterministic detector on source camera frames,
classifies the detected crop with a supplied crop classifier, and reports the
final decision against curated labels.
"""
from __future__ import annotations

import argparse
import json
from pathlib import Path
from typing import Callable, Sequence

import numpy as np
from PIL import Image

from training.traffic.vision_gate import crop_detection, detect_traffic_light_state

LABEL_NAMES = ("Red", "Yellow", "Green", "None")
LABEL_IDS = {label: index for index, label in enumerate(LABEL_NAMES)}
ACTION_NAMES = ("GO", "STOP")
ACTION_IDS = {label: index for index, label in enumerate(ACTION_NAMES)}
PredictProbabilities = Callable[[np.ndarray], Sequence[float] | np.ndarray]


class OnnxTrafficLightCropClassifier:
    """Small onnxruntime wrapper for 84x84 RGB traffic-light crops."""

    def __init__(self, onnx_path: Path):
        import onnxruntime as ort

        self.onnx_path = Path(onnx_path)
        self.session = ort.InferenceSession(str(self.onnx_path), providers=["CPUExecutionProvider"])
        self.input_name = self.session.get_inputs()[0].name
        self.output_name = self.session.get_outputs()[0].name

    def __call__(self, crop_rgb: np.ndarray) -> np.ndarray:
        model_input = _prepare_crop_tensor(crop_rgb)
        output = self.session.run([self.output_name], {self.input_name: model_input})[0]
        return np.asarray(output, dtype=np.float32).reshape(-1)


def evaluate_shadow_dataset(
    dataset_path: Path,
    predict_probabilities: PredictProbabilities,
    *,
    root_dir: Path | None = None,
    crop_size: tuple[int, int] = (84, 84),
) -> dict:
    """Evaluate source frames as ``detector -> crop classifier -> final state``."""
    dataset_path = Path(dataset_path)
    dataset_dir = dataset_path.parent
    root = Path.cwd() if root_dir is None else Path(root_dir)

    rows = _read_jsonl(dataset_path)
    confusion = _empty_confusion()
    detector_confusion = _empty_confusion()
    samples: list[dict] = []
    correct = 0
    detector_correct = 0
    classifier_invocations = 0

    for index, row in enumerate(rows):
        label_id = _row_label_id(row)
        label_name = LABEL_NAMES[label_id]
        source_path = _resolve_source_image(row, dataset_dir, root)
        image = np.array(Image.open(source_path).convert("RGB"))

        detector_result = detect_traffic_light_state(image)
        detector_id = LABEL_IDS[detector_result.state]
        detector_confusion[label_id][detector_id] += 1
        detector_correct += int(detector_id == label_id)

        probabilities: list[float] | None = None
        classifier_state: str | None = None
        final_id = detector_id
        if detector_result.state != "None":
            crop = crop_detection(image, detector_result)
            if crop is not None:
                crop = _resize_crop(crop, crop_size)
                probabilities = _normalize_probabilities(predict_probabilities(crop))
                classifier_id = int(np.argmax(probabilities))
                classifier_state = LABEL_NAMES[classifier_id]
                final_id = classifier_id
                classifier_invocations += 1

        prediction = LABEL_NAMES[final_id]
        is_correct = final_id == label_id
        correct += int(is_correct)
        confusion[label_id][final_id] += 1
        samples.append(
            {
                "index": index,
                "sourceImage": str(source_path),
                "label": label_name,
                "labelId": label_id,
                "detectorState": detector_result.state,
                "detectorConfidence": detector_result.confidence,
                "detectorBBox": list(detector_result.bbox_xyxy)
                if detector_result.bbox_xyxy is not None
                else None,
                "classifierState": classifier_state,
                "probabilities": probabilities,
                "prediction": prediction,
                "predictionId": final_id,
                "correct": is_correct,
            }
        )

    n = len(rows)
    return {
        "dataset": str(dataset_path),
        "n": n,
        "labels": list(LABEL_NAMES),
        "accuracy": correct / max(n, 1),
        "detectorAccuracy": detector_correct / max(n, 1),
        "classifierInvocations": classifier_invocations,
        "confusion": confusion,
        "detectorConfusion": detector_confusion,
        "samples": samples,
    }


def evaluate_trace_frame_directory(
    trace_path: Path,
    frames_dir: Path,
    predict_probabilities: PredictProbabilities,
    *,
    frame_glob: str = "*.jpg",
    crop_size: tuple[int, int] = (84, 84),
    temporal_min_state_frames: int = 1,
    temporal_none_frames: int = 1,
    max_bbox_area_px: int | None = None,
    max_bbox_width_px: int | None = None,
    pose_gate_lane_x: float | None = None,
    pose_gate_lane_z: float | None = None,
    pose_gate_lane_tolerance_m: float = 1.5,
    pose_gate_min_x: float | None = None,
    pose_gate_max_x: float | None = None,
    pose_gate_min_z: float | None = None,
    pose_gate_max_z: float | None = None,
    pose_actionable_labels: bool = False,
    action_decisions: bool = False,
) -> dict:
    """Evaluate extracted runtime video frames against trace traffic-light telemetry."""
    trace_path = Path(trace_path)
    frames_dir = Path(frames_dir)
    trace_samples = _read_trace_samples(trace_path)
    frame_paths = sorted(frames_dir.glob(frame_glob))
    if not trace_samples:
        raise ValueError(f"Trace is empty: {trace_path}")
    if not frame_paths:
        raise ValueError(f"No frames matched {frame_glob} in {frames_dir}")

    confusion = _empty_confusion()
    detector_confusion = _empty_confusion()
    samples: list[dict] = []
    correct = 0
    detector_correct = 0
    classifier_invocations = 0

    for frame_index, frame_path in enumerate(frame_paths):
        trace_index = _map_frame_to_trace_index(frame_index, len(frame_paths), len(trace_samples))
        trace_sample = trace_samples[trace_index]
        label_name = _trace_state(trace_sample)
        if label_name not in LABEL_IDS:
            continue
        label_id = LABEL_IDS[label_name]
        image = np.array(Image.open(frame_path).convert("RGB"))
        detector_result = detect_traffic_light_state(image)
        detector_id = LABEL_IDS[detector_result.state]
        detector_confusion[label_id][detector_id] += 1
        detector_correct += int(detector_id == label_id)

        probabilities: list[float] | None = None
        classifier_state: str | None = None
        final_id = detector_id
        if detector_result.state != "None":
            crop = crop_detection(image, detector_result)
            if crop is not None:
                crop = _resize_crop(crop, crop_size)
                probabilities = _normalize_probabilities(predict_probabilities(crop))
                final_id = int(np.argmax(probabilities))
                classifier_state = LABEL_NAMES[final_id]
                classifier_invocations += 1

        prediction = LABEL_NAMES[final_id]
        is_correct = final_id == label_id
        correct += int(is_correct)
        confusion[label_id][final_id] += 1
        samples.append(
            {
                "frameIndex": frame_index,
                "framePath": str(frame_path),
                "traceIndex": trace_index,
                "traceSample": trace_sample.get("sample"),
                "label": label_name,
                "labelId": label_id,
                "trafficLightDistanceM": trace_sample.get("trafficLightDistanceM"),
                "detectorState": detector_result.state,
                "detectorConfidence": detector_result.confidence,
                "detectorBBox": list(detector_result.bbox_xyxy)
                if detector_result.bbox_xyxy is not None
                else None,
                "classifierState": classifier_state,
                "probabilities": probabilities,
                "prediction": prediction,
                "predictionId": final_id,
                "correct": is_correct,
            }
        )

    geometry_gate = None
    temporal_prediction_key = "prediction"
    if max_bbox_area_px is not None or max_bbox_width_px is not None:
        geometry_gate = apply_geometry_gate(
            samples,
            max_bbox_area_px=max_bbox_area_px,
            max_bbox_width_px=max_bbox_width_px,
        )
        samples = geometry_gate.pop("samples")
        temporal_prediction_key = "geometryPrediction"

    pose_relevance_gate = None
    if _pose_gate_enabled(
        pose_gate_lane_x,
        pose_gate_lane_z,
        pose_gate_min_x,
        pose_gate_max_x,
        pose_gate_min_z,
        pose_gate_max_z,
    ):
        pose_relevance_gate = apply_pose_relevance_gate(
            samples,
            lane_x=pose_gate_lane_x,
            lane_z=pose_gate_lane_z,
            lane_tolerance_m=pose_gate_lane_tolerance_m,
            min_x=pose_gate_min_x,
            max_x=pose_gate_max_x,
            min_z=pose_gate_min_z,
            max_z=pose_gate_max_z,
            prediction_key=temporal_prediction_key,
        )
        samples = pose_relevance_gate.pop("samples")
        temporal_prediction_key = "poseRelevancePrediction"

    temporal_gate = None
    if temporal_min_state_frames > 1 or temporal_none_frames > 1:
        temporal_gate = apply_temporal_gate(
            samples,
            min_state_frames=temporal_min_state_frames,
            none_reset_frames=temporal_none_frames,
            prediction_key=temporal_prediction_key,
        )
        samples = temporal_gate.pop("samples")
        temporal_prediction_key = "temporalPrediction"

    pose_actionable_label_gate = None
    if pose_actionable_labels:
        pose_actionable_label_gate = apply_pose_actionable_label_gate(
            samples,
            lane_x=pose_gate_lane_x,
            lane_z=pose_gate_lane_z,
            lane_tolerance_m=pose_gate_lane_tolerance_m,
            min_x=pose_gate_min_x,
            max_x=pose_gate_max_x,
            min_z=pose_gate_min_z,
            max_z=pose_gate_max_z,
            prediction_key=temporal_prediction_key,
        )
        samples = pose_actionable_label_gate.pop("samples")

    n = len(samples)
    summary = {
        "trace": str(trace_path),
        "framesDir": str(frames_dir),
        "frameGlob": frame_glob,
        "traceSamples": len(trace_samples),
        "videoFrames": len(frame_paths),
        "n": n,
        "labels": list(LABEL_NAMES),
        "accuracy": correct / max(n, 1),
        "detectorAccuracy": detector_correct / max(n, 1),
        "classifierInvocations": classifier_invocations,
        "confusion": confusion,
        "detectorConfusion": detector_confusion,
        "distanceBuckets": _distance_buckets(samples),
        "samples": samples,
    }
    if geometry_gate is not None:
        summary["geometryGate"] = geometry_gate
    if pose_relevance_gate is not None:
        summary["poseRelevanceGate"] = pose_relevance_gate
    if temporal_gate is not None:
        summary["temporalGate"] = temporal_gate
    if pose_actionable_label_gate is not None:
        summary["poseActionableLabelGate"] = pose_actionable_label_gate
    if action_decisions:
        label_key = "poseActionableLabel" if pose_actionable_label_gate is not None else "label"
        summary["actionDecisionEval"] = apply_action_decision_eval(
            samples,
            label_key=label_key,
            prediction_key=temporal_prediction_key,
        )
    return summary


def evaluate_recorder_trace(
    trace_path: Path,
    predict_probabilities: PredictProbabilities,
    *,
    crop_size: tuple[int, int] = (84, 84),
    temporal_min_state_frames: int = 1,
    temporal_none_frames: int = 1,
    max_bbox_area_px: int | None = None,
    max_bbox_width_px: int | None = None,
    pose_gate_lane_x: float | None = None,
    pose_gate_lane_z: float | None = None,
    pose_gate_lane_tolerance_m: float = 1.5,
    pose_gate_min_x: float | None = None,
    pose_gate_max_x: float | None = None,
    pose_gate_min_z: float | None = None,
    pose_gate_max_z: float | None = None,
    pose_actionable_labels: bool = False,
    action_decisions: bool = False,
) -> dict:
    """Evaluate recorder trace rows that already contain ``modelFramePath``/``cropPath``.

    Unlike ``evaluate_trace_frame_directory``, this keeps the exact recorder step
    alignment and does not map a video frame index back to telemetry samples.
    """
    trace_path = Path(trace_path)
    rows = _read_jsonl(trace_path)
    confusion = _empty_confusion()
    detector_confusion = _empty_confusion()
    samples: list[dict] = []
    correct = 0
    detector_correct = 0
    classifier_invocations = 0

    for index, row in enumerate(rows):
        label_name = _recorder_trace_state(row)
        if label_name not in LABEL_IDS:
            continue
        label_id = LABEL_IDS[label_name]
        detector_state = str(row.get("detectorState", "None"))
        if detector_state not in LABEL_IDS:
            detector_state = "None"
        detector_id = LABEL_IDS[detector_state]
        detector_confusion[label_id][detector_id] += 1
        detector_correct += int(detector_id == label_id)

        probabilities: list[float] | None = None
        classifier_state: str | None = None
        final_id = detector_id
        crop_path = _resolve_existing_path(row.get("cropPath"))
        if crop_path is not None and detector_state != "None":
            crop = np.array(Image.open(crop_path).convert("RGB"))
            crop = _resize_crop(crop, crop_size)
            probabilities = _normalize_probabilities(predict_probabilities(crop))
            final_id = int(np.argmax(probabilities))
            classifier_state = LABEL_NAMES[final_id]
            classifier_invocations += 1
        elif isinstance(row.get("classifierProbabilities"), list) and detector_state != "None":
            probabilities = _normalize_probabilities(row["classifierProbabilities"])
            final_id = int(np.argmax(probabilities))
            classifier_state = LABEL_NAMES[final_id]

        prediction = LABEL_NAMES[final_id]
        is_correct = final_id == label_id
        correct += int(is_correct)
        confusion[label_id][final_id] += 1
        samples.append(
            {
                "index": index,
                "step": row.get("step"),
                "route": row.get("route"),
                "sourceImage": row.get("modelFramePath"),
                "cropPath": str(crop_path) if crop_path is not None else None,
                "label": label_name,
                "labelId": label_id,
                "trafficLightDistanceM": row.get("gtDistanceM"),
                "speedMps": row.get("speedMps"),
                "detectorState": detector_state,
                "detectorConfidence": row.get("visionConfidence"),
                "detectorBBox": row.get("detectorBBox"),
                "classifierState": classifier_state,
                "probabilities": probabilities,
                "prediction": prediction,
                "predictionId": final_id,
                "correct": is_correct,
                "pose": row.get("pose"),
            }
        )

    geometry_gate = None
    temporal_prediction_key = "prediction"
    if max_bbox_area_px is not None or max_bbox_width_px is not None:
        geometry_gate = apply_geometry_gate(
            samples,
            max_bbox_area_px=max_bbox_area_px,
            max_bbox_width_px=max_bbox_width_px,
        )
        samples = geometry_gate.pop("samples")
        temporal_prediction_key = "geometryPrediction"

    pose_relevance_gate = None
    if _pose_gate_enabled(
        pose_gate_lane_x,
        pose_gate_lane_z,
        pose_gate_min_x,
        pose_gate_max_x,
        pose_gate_min_z,
        pose_gate_max_z,
    ):
        pose_relevance_gate = apply_pose_relevance_gate(
            samples,
            lane_x=pose_gate_lane_x,
            lane_z=pose_gate_lane_z,
            lane_tolerance_m=pose_gate_lane_tolerance_m,
            min_x=pose_gate_min_x,
            max_x=pose_gate_max_x,
            min_z=pose_gate_min_z,
            max_z=pose_gate_max_z,
            prediction_key=temporal_prediction_key,
        )
        samples = pose_relevance_gate.pop("samples")
        temporal_prediction_key = "poseRelevancePrediction"

    temporal_gate = None
    if temporal_min_state_frames > 1 or temporal_none_frames > 1:
        temporal_gate = apply_temporal_gate(
            samples,
            min_state_frames=temporal_min_state_frames,
            none_reset_frames=temporal_none_frames,
            prediction_key=temporal_prediction_key,
        )
        samples = temporal_gate.pop("samples")
        temporal_prediction_key = "temporalPrediction"

    pose_actionable_label_gate = None
    if pose_actionable_labels:
        pose_actionable_label_gate = apply_pose_actionable_label_gate(
            samples,
            lane_x=pose_gate_lane_x,
            lane_z=pose_gate_lane_z,
            lane_tolerance_m=pose_gate_lane_tolerance_m,
            min_x=pose_gate_min_x,
            max_x=pose_gate_max_x,
            min_z=pose_gate_min_z,
            max_z=pose_gate_max_z,
            prediction_key=temporal_prediction_key,
        )
        samples = pose_actionable_label_gate.pop("samples")

    n = len(samples)
    summary = {
        "trace": str(trace_path),
        "n": n,
        "labels": list(LABEL_NAMES),
        "accuracy": correct / max(n, 1),
        "detectorAccuracy": detector_correct / max(n, 1),
        "classifierInvocations": classifier_invocations,
        "confusion": confusion,
        "detectorConfusion": detector_confusion,
        "distanceBuckets": _distance_buckets(samples),
        "samples": samples,
    }
    if geometry_gate is not None:
        summary["geometryGate"] = geometry_gate
    if pose_relevance_gate is not None:
        summary["poseRelevanceGate"] = pose_relevance_gate
    if temporal_gate is not None:
        summary["temporalGate"] = temporal_gate
    if pose_actionable_label_gate is not None:
        summary["poseActionableLabelGate"] = pose_actionable_label_gate
    if action_decisions:
        label_key = "poseActionableLabel" if pose_actionable_label_gate is not None else "label"
        summary["actionDecisionEval"] = apply_action_decision_eval(
            samples,
            label_key=label_key,
            prediction_key=temporal_prediction_key,
        )
    return summary


def apply_geometry_gate(
    samples: Sequence[dict],
    *,
    max_bbox_area_px: int | None = None,
    max_bbox_width_px: int | None = None,
) -> dict:
    """Reject visual predictions whose detector bbox is implausibly large."""
    if max_bbox_area_px is None and max_bbox_width_px is None:
        raise ValueError("At least one bbox geometry limit must be provided")
    if max_bbox_area_px is not None and max_bbox_area_px <= 0:
        raise ValueError("max_bbox_area_px must be > 0")
    if max_bbox_width_px is not None and max_bbox_width_px <= 0:
        raise ValueError("max_bbox_width_px must be > 0")

    gated_samples: list[dict] = []
    confusion = _empty_confusion()
    correct = 0
    rejected = 0

    for sample in samples:
        prediction = _sample_prediction(sample)
        reject = prediction != "None" and _bbox_exceeds_geometry(
            sample.get("detectorBBox"),
            max_bbox_area_px=max_bbox_area_px,
            max_bbox_width_px=max_bbox_width_px,
        )
        if reject:
            prediction = "None"
            rejected += 1

        label_id = _sample_label_id(sample)
        prediction_id = LABEL_IDS[prediction]
        is_correct = prediction_id == label_id
        correct += int(is_correct)
        confusion[label_id][prediction_id] += 1

        enriched = dict(sample)
        enriched["geometryPrediction"] = prediction
        enriched["geometryPredictionId"] = prediction_id
        enriched["geometryCorrect"] = is_correct
        enriched["geometryRejected"] = reject
        gated_samples.append(enriched)

    n = len(gated_samples)
    return {
        "maxBboxAreaPx": max_bbox_area_px,
        "maxBboxWidthPx": max_bbox_width_px,
        "rejected": rejected,
        "n": n,
        "accuracy": correct / max(n, 1),
        "confusion": confusion,
        "distanceBuckets": _distance_buckets(gated_samples, correct_key="geometryCorrect"),
        "samples": gated_samples,
    }


def apply_pose_relevance_gate(
    samples: Sequence[dict],
    *,
    lane_x: float | None = None,
    lane_z: float | None = None,
    lane_tolerance_m: float = 1.5,
    min_x: float | None = None,
    max_x: float | None = None,
    min_z: float | None = None,
    max_z: float | None = None,
    prediction_key: str = "prediction",
) -> dict:
    """Reject visual predictions outside the route-relevant approach window.

    This is a shadow-eval helper for the city camera gate. It uses only the
    ego pose/route context, not ``nearestTrafficLight.state``. For the current
    v9 south-approach route, it lets us measure the missing relevance stage:
    a visible distant/side light should not become an actionable stop/go gate.
    """
    if lane_tolerance_m <= 0:
        raise ValueError("lane_tolerance_m must be > 0")
    if not _pose_gate_enabled(lane_x, lane_z, min_x, max_x, min_z, max_z):
        raise ValueError("At least one pose relevance limit must be provided")

    gated_samples: list[dict] = []
    confusion = _empty_confusion()
    correct = 0
    rejected = 0

    for sample in samples:
        prediction = _sample_prediction(sample, prediction_key=prediction_key)
        reject = prediction != "None" and not _pose_is_relevant(
            sample.get("pose"),
            lane_x=lane_x,
            lane_z=lane_z,
            lane_tolerance_m=lane_tolerance_m,
            min_x=min_x,
            max_x=max_x,
            min_z=min_z,
            max_z=max_z,
        )
        if reject:
            prediction = "None"
            rejected += 1

        label_id = _sample_label_id(sample)
        prediction_id = LABEL_IDS[prediction]
        is_correct = prediction_id == label_id
        correct += int(is_correct)
        confusion[label_id][prediction_id] += 1

        enriched = dict(sample)
        enriched["poseRelevancePrediction"] = prediction
        enriched["poseRelevancePredictionId"] = prediction_id
        enriched["poseRelevanceCorrect"] = is_correct
        enriched["poseRelevanceRejected"] = reject
        gated_samples.append(enriched)

    n = len(gated_samples)
    return {
        "laneX": lane_x,
        "laneZ": lane_z,
        "laneToleranceM": lane_tolerance_m,
        "minX": min_x,
        "maxX": max_x,
        "minZ": min_z,
        "maxZ": max_z,
        "predictionKey": prediction_key,
        "rejected": rejected,
        "n": n,
        "accuracy": correct / max(n, 1),
        "confusion": confusion,
        "distanceBuckets": _distance_buckets(gated_samples, correct_key="poseRelevanceCorrect"),
        "samples": gated_samples,
    }


def apply_pose_actionable_label_gate(
    samples: Sequence[dict],
    *,
    lane_x: float | None = None,
    lane_z: float | None = None,
    lane_tolerance_m: float = 1.5,
    min_x: float | None = None,
    max_x: float | None = None,
    min_z: float | None = None,
    max_z: float | None = None,
    prediction_key: str = "prediction",
) -> dict:
    """Evaluate predictions against pose-actionable labels.

    Runtime telemetry may keep reporting a light color when the ego vehicle is
    already at or past the stop-line decision point. For camera-gate evaluation
    those labels are not actionable: the visual gate should no longer stop on
    a side/back/close light after the route window has ended.
    """
    if lane_tolerance_m <= 0:
        raise ValueError("lane_tolerance_m must be > 0")
    if not _pose_gate_enabled(lane_x, lane_z, min_x, max_x, min_z, max_z):
        raise ValueError("At least one pose actionable-label limit must be provided")

    gated_samples: list[dict] = []
    confusion = _empty_confusion()
    correct = 0
    changed_labels = 0

    for sample in samples:
        original_label = _sample_label(sample)
        is_relevant = _pose_is_relevant(
            sample.get("pose"),
            lane_x=lane_x,
            lane_z=lane_z,
            lane_tolerance_m=lane_tolerance_m,
            min_x=min_x,
            max_x=max_x,
            min_z=min_z,
            max_z=max_z,
        )
        actionable_label = original_label if original_label == "None" or is_relevant else "None"
        changed = actionable_label != original_label
        changed_labels += int(changed)

        prediction = _sample_prediction(sample, prediction_key=prediction_key)
        label_id = LABEL_IDS[actionable_label]
        prediction_id = LABEL_IDS[prediction]
        is_correct = prediction_id == label_id
        correct += int(is_correct)
        confusion[label_id][prediction_id] += 1

        enriched = dict(sample)
        enriched["poseActionableLabel"] = actionable_label
        enriched["poseActionableLabelId"] = label_id
        enriched["poseActionablePrediction"] = prediction
        enriched["poseActionablePredictionId"] = prediction_id
        enriched["poseActionableCorrect"] = is_correct
        enriched["poseActionableLabelChanged"] = changed
        gated_samples.append(enriched)

    n = len(gated_samples)
    return {
        "laneX": lane_x,
        "laneZ": lane_z,
        "laneToleranceM": lane_tolerance_m,
        "minX": min_x,
        "maxX": max_x,
        "minZ": min_z,
        "maxZ": max_z,
        "predictionKey": prediction_key,
        "changedLabels": changed_labels,
        "n": n,
        "accuracy": correct / max(n, 1),
        "confusion": confusion,
        "distanceBuckets": _distance_buckets(gated_samples, correct_key="poseActionableCorrect"),
        "samples": gated_samples,
    }


def apply_action_decision_eval(
    samples: Sequence[dict],
    *,
    label_key: str = "label",
    prediction_key: str = "prediction",
) -> dict:
    """Evaluate traffic-light color predictions as driving decisions.

    The current showcase rule treats only actionable Red as STOP. Yellow,
    Green, and None are GO because the demo policy continues on Yellow and
    should continue when no relevant light is visible.
    """
    evaluated_samples: list[dict] = []
    confusion = [[0 for _ in ACTION_NAMES] for _ in ACTION_NAMES]
    correct = 0

    for sample in samples:
        action_label = _color_to_action(str(sample.get(label_key, "None")))
        action_prediction = _color_to_action(_sample_prediction(sample, prediction_key=prediction_key))
        label_id = ACTION_IDS[action_label]
        prediction_id = ACTION_IDS[action_prediction]
        is_correct = label_id == prediction_id
        correct += int(is_correct)
        confusion[label_id][prediction_id] += 1

        enriched = dict(sample)
        enriched["actionLabel"] = action_label
        enriched["actionLabelId"] = label_id
        enriched["actionPrediction"] = action_prediction
        enriched["actionPredictionId"] = prediction_id
        enriched["actionCorrect"] = is_correct
        evaluated_samples.append(enriched)

    n = len(evaluated_samples)
    return {
        "labels": list(ACTION_NAMES),
        "labelKey": label_key,
        "predictionKey": prediction_key,
        "n": n,
        "accuracy": correct / max(n, 1),
        "confusion": confusion,
        "samples": evaluated_samples,
    }


def apply_temporal_gate(
    samples: Sequence[dict],
    *,
    min_state_frames: int = 2,
    none_reset_frames: int = 1,
    prediction_key: str = "prediction",
) -> dict:
    """Apply a camera-only consecutive-frame gate to raw visual predictions."""
    if min_state_frames < 1:
        raise ValueError("min_state_frames must be >= 1")
    if none_reset_frames < 1:
        raise ValueError("none_reset_frames must be >= 1")

    current_state = "None"
    pending_state: str | None = None
    pending_count = 0
    none_count = 0
    temporal_samples: list[dict] = []
    confusion = _empty_confusion()
    correct = 0

    for sample in samples:
        raw_state = _sample_prediction(sample, prediction_key=prediction_key)
        if raw_state == "None":
            none_count += 1
            pending_state = None
            pending_count = 0
            if none_count >= none_reset_frames:
                current_state = "None"
        else:
            none_count = 0
            if raw_state == current_state:
                pending_state = None
                pending_count = 0
            elif raw_state == pending_state:
                pending_count += 1
            else:
                pending_state = raw_state
                pending_count = 1

            if raw_state != current_state and pending_count >= min_state_frames:
                current_state = raw_state
                pending_state = None
                pending_count = 0

        label_id = _sample_label_id(sample)
        prediction_id = LABEL_IDS[current_state]
        is_correct = prediction_id == label_id
        correct += int(is_correct)
        confusion[label_id][prediction_id] += 1
        enriched = dict(sample)
        enriched["temporalPrediction"] = current_state
        enriched["temporalPredictionId"] = prediction_id
        enriched["temporalCorrect"] = is_correct
        temporal_samples.append(enriched)

    n = len(temporal_samples)
    return {
        "minStateFrames": min_state_frames,
        "noneResetFrames": none_reset_frames,
        "predictionKey": prediction_key,
        "n": n,
        "accuracy": correct / max(n, 1),
        "confusion": confusion,
        "distanceBuckets": _distance_buckets(temporal_samples, correct_key="temporalCorrect"),
        "samples": temporal_samples,
    }


def _read_jsonl(dataset_path: Path) -> list[dict]:
    rows = []
    for line in dataset_path.read_text(encoding="utf-8").splitlines():
        if line.strip():
            rows.append(json.loads(line))
    return rows


def _read_trace_samples(trace_path: Path) -> list[dict]:
    samples: list[dict] = []
    for line in trace_path.read_text(encoding="utf-8").splitlines():
        if not line.strip():
            continue
        row = json.loads(line)
        sample = row.get("sample", row)
        if isinstance(sample, dict):
            samples.append(sample)
    return samples


def _empty_confusion() -> list[list[int]]:
    return [[0 for _ in LABEL_NAMES] for _ in LABEL_NAMES]


def _row_label_id(row: dict) -> int:
    if row.get("labelId") is not None:
        label_id = int(row["labelId"])
    elif row.get("label_idx") is not None:
        label_id = int(row["label_idx"])
    else:
        label = str(row.get("label", "None"))
        label_id = LABEL_IDS[label]
    if label_id < 0 or label_id >= len(LABEL_NAMES):
        raise ValueError(f"Label id {label_id} is outside 0..{len(LABEL_NAMES) - 1}")
    return label_id


def _resolve_source_image(row: dict, dataset_dir: Path, root_dir: Path) -> Path:
    value = row.get("sourceImage") or row.get("sourcePath") or row.get("image") or row.get("modelInputPath")
    if not isinstance(value, str) or not value:
        raise KeyError("dataset row has no source image path")

    path = Path(value)
    if path.is_absolute():
        return path
    candidates = (dataset_dir / path, root_dir / path)
    for candidate in candidates:
        if candidate.exists():
            return candidate
    return candidates[0]


def _map_frame_to_trace_index(frame_index: int, frame_count: int, trace_count: int) -> int:
    if frame_count <= 1 or trace_count <= 1:
        return 0
    mapped = round(frame_index * (trace_count - 1) / (frame_count - 1))
    return int(np.clip(mapped, 0, trace_count - 1))


def _trace_state(trace_sample: dict) -> str:
    state = trace_sample.get("trafficLightState")
    if isinstance(state, str) and state:
        return state
    telemetry = trace_sample.get("telemetry")
    if isinstance(telemetry, list):
        for item in telemetry:
            if item.get("key") == "nearestTrafficLight.state":
                return str(item.get("value", "None"))
    return "None"


def _recorder_trace_state(row: dict) -> str:
    state = row.get("gtState") or row.get("trafficLightState")
    return str(state) if isinstance(state, str) and state else "None"


def _sample_prediction(sample: dict, *, prediction_key: str = "prediction") -> str:
    prediction = str(sample.get(prediction_key, "None"))
    if prediction not in LABEL_IDS:
        return "None"
    return prediction


def _sample_label(sample: dict) -> str:
    label = str(sample.get("label", "None"))
    return label if label in LABEL_IDS else "None"


def _color_to_action(color: str) -> str:
    return "STOP" if color == "Red" else "GO"


def _bbox_exceeds_geometry(
    bbox_xyxy: object,
    *,
    max_bbox_area_px: int | None,
    max_bbox_width_px: int | None,
) -> bool:
    if not isinstance(bbox_xyxy, Sequence) or len(bbox_xyxy) != 4:
        return False
    x1, y1, x2, y2 = [int(value) for value in bbox_xyxy]
    width = max(0, x2 - x1)
    height = max(0, y2 - y1)
    area = width * height
    if max_bbox_area_px is not None and area > max_bbox_area_px:
        return True
    return max_bbox_width_px is not None and width > max_bbox_width_px


def _pose_gate_enabled(
    lane_x: float | None,
    lane_z: float | None,
    min_x: float | None,
    max_x: float | None,
    min_z: float | None,
    max_z: float | None,
) -> bool:
    return any(value is not None for value in (lane_x, lane_z, min_x, max_x, min_z, max_z))


def _pose_is_relevant(
    pose: object,
    *,
    lane_x: float | None,
    lane_z: float | None,
    lane_tolerance_m: float,
    min_x: float | None,
    max_x: float | None,
    min_z: float | None,
    max_z: float | None,
) -> bool:
    parsed = _parse_pose_xz(pose)
    if parsed is None:
        return False
    x, z = parsed
    if lane_x is not None and abs(x - lane_x) > lane_tolerance_m:
        return False
    if lane_z is not None and abs(z - lane_z) > lane_tolerance_m:
        return False
    if min_x is not None and x < min_x:
        return False
    if max_x is not None and x > max_x:
        return False
    if min_z is not None and z < min_z:
        return False
    return max_z is None or z <= max_z


def _parse_pose_xz(pose: object) -> tuple[float, float] | None:
    if isinstance(pose, dict):
        if "x" not in pose or "z" not in pose:
            return None
        try:
            return float(pose["x"]), float(pose["z"])
        except (TypeError, ValueError):
            return None
    if isinstance(pose, Sequence) and not isinstance(pose, (str, bytes)) and len(pose) >= 3:
        try:
            return float(pose[0]), float(pose[2])
        except (TypeError, ValueError):
            return None
    return None


def _sample_label_id(sample: dict) -> int:
    if sample.get("labelId") is not None:
        label_id = int(sample["labelId"])
    else:
        label_id = LABEL_IDS[str(sample.get("label", "None"))]
    if label_id < 0 or label_id >= len(LABEL_NAMES):
        raise ValueError(f"Label id {label_id} is outside 0..{len(LABEL_NAMES) - 1}")
    return label_id


def _distance_buckets(samples: Sequence[dict], *, correct_key: str = "correct") -> list[dict]:
    specs = (
        ("far", lambda distance: distance >= 10.0),
        ("approach", lambda distance: 5.0 <= distance < 10.0),
        ("close", lambda distance: 0.0 <= distance < 5.0),
        ("none", lambda distance: distance < 0.0),
    )
    out = []
    for name, predicate in specs:
        bucket_samples = [
            sample
            for sample in samples
            if predicate(_parse_distance(sample.get("trafficLightDistanceM")))
        ]
        n = len(bucket_samples)
        correct = sum(1 for sample in bucket_samples if sample.get(correct_key))
        out.append(
            {
                "name": name,
                "n": n,
                "accuracy": correct / max(n, 1),
                "correct": correct,
                "misses": n - correct,
            }
        )
    return out


def _parse_distance(value: object) -> float:
    try:
        return float(value)
    except (TypeError, ValueError):
        return -1.0


def _resize_crop(crop_rgb: np.ndarray, crop_size: tuple[int, int]) -> np.ndarray:
    return np.array(Image.fromarray(crop_rgb).resize(crop_size, Image.Resampling.BILINEAR))


def _normalize_probabilities(probabilities: Sequence[float] | np.ndarray) -> list[float]:
    values = np.asarray(probabilities, dtype=np.float32).reshape(-1)
    if values.size < len(LABEL_NAMES):
        raise ValueError(f"Classifier returned {values.size} probabilities, expected {len(LABEL_NAMES)}")
    values = values[: len(LABEL_NAMES)]
    return [float(value) for value in values]


def _prepare_crop_tensor(crop_rgb: np.ndarray) -> np.ndarray:
    crop = _resize_crop(np.asarray(crop_rgb, dtype=np.uint8), (84, 84))
    tensor = crop.transpose(2, 0, 1).astype(np.float32) / 255.0
    return tensor[None, ...]


def _resolve_existing_path(value: object) -> Path | None:
    if not isinstance(value, str) or not value:
        return None
    path = Path(value)
    return path if path.exists() else None


def main(argv: Sequence[str] | None = None) -> None:
    parser = argparse.ArgumentParser(description="Shadow-evaluate camera traffic-light gate.")
    parser.add_argument("--dataset", type=Path)
    parser.add_argument("--trace", type=Path)
    parser.add_argument("--recorder-trace", type=Path)
    parser.add_argument("--frames-dir", type=Path)
    parser.add_argument("--frame-glob", default="*.jpg")
    parser.add_argument("--onnx", type=Path, required=True)
    parser.add_argument("--output", type=Path)
    parser.add_argument("--root", type=Path, default=Path.cwd())
    parser.add_argument("--temporal-min-state-frames", type=int, default=1)
    parser.add_argument("--temporal-none-frames", type=int, default=1)
    parser.add_argument("--max-bbox-area-px", type=int)
    parser.add_argument("--max-bbox-width-px", type=int)
    parser.add_argument("--pose-gate-lane-x", type=float)
    parser.add_argument("--pose-gate-lane-z", type=float)
    parser.add_argument("--pose-gate-lane-tolerance-m", type=float, default=1.5)
    parser.add_argument("--pose-gate-min-x", type=float)
    parser.add_argument("--pose-gate-max-x", type=float)
    parser.add_argument("--pose-gate-min-z", type=float)
    parser.add_argument("--pose-gate-max-z", type=float)
    parser.add_argument(
        "--pose-actionable-labels",
        action="store_true",
        help="Evaluate final predictions against pose-actionable labels by treating labels outside the pose window as None.",
    )
    parser.add_argument(
        "--action-decisions",
        action="store_true",
        help="Also evaluate color predictions as GO/STOP driving decisions.",
    )
    args = parser.parse_args(argv)

    classifier = OnnxTrafficLightCropClassifier(args.onnx)
    if args.recorder_trace is not None:
        summary = evaluate_recorder_trace(
            args.recorder_trace,
            classifier,
            temporal_min_state_frames=args.temporal_min_state_frames,
            temporal_none_frames=args.temporal_none_frames,
            max_bbox_area_px=args.max_bbox_area_px,
            max_bbox_width_px=args.max_bbox_width_px,
            pose_gate_lane_x=args.pose_gate_lane_x,
            pose_gate_lane_z=args.pose_gate_lane_z,
            pose_gate_lane_tolerance_m=args.pose_gate_lane_tolerance_m,
            pose_gate_min_x=args.pose_gate_min_x,
            pose_gate_max_x=args.pose_gate_max_x,
            pose_gate_min_z=args.pose_gate_min_z,
            pose_gate_max_z=args.pose_gate_max_z,
            pose_actionable_labels=args.pose_actionable_labels,
            action_decisions=args.action_decisions,
        )
    elif args.trace is not None or args.frames_dir is not None:
        if args.trace is None or args.frames_dir is None:
            raise ValueError("--trace and --frames-dir must be provided together")
        summary = evaluate_trace_frame_directory(
            args.trace,
            args.frames_dir,
            classifier,
            frame_glob=args.frame_glob,
            temporal_min_state_frames=args.temporal_min_state_frames,
            temporal_none_frames=args.temporal_none_frames,
            max_bbox_area_px=args.max_bbox_area_px,
            max_bbox_width_px=args.max_bbox_width_px,
            pose_gate_lane_x=args.pose_gate_lane_x,
            pose_gate_lane_z=args.pose_gate_lane_z,
            pose_gate_lane_tolerance_m=args.pose_gate_lane_tolerance_m,
            pose_gate_min_x=args.pose_gate_min_x,
            pose_gate_max_x=args.pose_gate_max_x,
            pose_gate_min_z=args.pose_gate_min_z,
            pose_gate_max_z=args.pose_gate_max_z,
            pose_actionable_labels=args.pose_actionable_labels,
            action_decisions=args.action_decisions,
        )
    elif args.dataset is not None:
        summary = evaluate_shadow_dataset(args.dataset, classifier, root_dir=args.root)
    else:
        raise ValueError("Provide --dataset, --recorder-trace, or both --trace and --frames-dir")
    summary["classifier"] = str(args.onnx)

    text = json.dumps(summary, ensure_ascii=False, indent=2)
    if args.output is not None:
        args.output.parent.mkdir(parents=True, exist_ok=True)
        args.output.write_text(text + "\n", encoding="utf-8")
    print(text)


if __name__ == "__main__":
    main()
