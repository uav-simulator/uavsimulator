import json

import numpy as np
from PIL import Image

from training.traffic.vision_shadow_eval import (
    apply_action_decision_eval,
    apply_geometry_gate,
    apply_pose_actionable_label_gate,
    apply_pose_relevance_gate,
    apply_temporal_gate,
    evaluate_recorder_trace,
    evaluate_shadow_dataset,
    evaluate_trace_frame_directory,
)


def _synthetic_frame(active_state: str | None) -> np.ndarray:
    image = np.full((240, 320, 3), (108, 132, 148), dtype=np.uint8)
    image[150:, :, :] = (60, 62, 58)
    if active_state is None:
        return image

    image[40:132, 148:174, :] = (142, 122, 34)
    bulb_colors = {
        "Red": (245, 24, 20),
        "Yellow": (248, 210, 34),
        "Green": (38, 230, 64),
    }
    centers = {
        "Red": (161, 58),
        "Yellow": (161, 86),
        "Green": (161, 114),
    }
    yy, xx = np.ogrid[: image.shape[0], : image.shape[1]]
    for state, (cx, cy) in centers.items():
        mask = (xx - cx) ** 2 + (yy - cy) ** 2 <= 8**2
        image[mask] = bulb_colors[state] if state == active_state else (52, 46, 34)
    return image


def _write_dataset(dataset_path, rows):
    dataset_path.parent.mkdir(parents=True, exist_ok=True)
    dataset_path.write_text(
        "\n".join(json.dumps(row) for row in rows),
        encoding="utf-8",
    )


def test_shadow_eval_resolves_source_frames_and_reports_confusion(tmp_path):
    frames = tmp_path / "frames"
    frames.mkdir()
    Image.fromarray(_synthetic_frame("Red")).save(frames / "red.jpg")
    Image.fromarray(_synthetic_frame(None)).save(frames / "none.jpg")
    dataset_path = tmp_path / "dataset" / "dataset.jsonl"
    _write_dataset(
        dataset_path,
        [
            {"sourceImage": "frames/red.jpg", "label": "Red", "labelId": 0},
            {"sourceImage": "frames/none.jpg", "label": "None", "labelId": 3},
        ],
    )
    classifier_calls = []

    def predict_red(crop_rgb):
        classifier_calls.append(crop_rgb.shape)
        return [0.93, 0.02, 0.03, 0.02]

    summary = evaluate_shadow_dataset(dataset_path, predict_red, root_dir=tmp_path)

    assert summary["n"] == 2
    assert summary["accuracy"] == 1.0
    assert summary["classifierInvocations"] == 1
    assert summary["confusion"][0][0] == 1
    assert summary["confusion"][3][3] == 1
    assert summary["samples"][0]["detectorState"] == "Red"
    assert summary["samples"][0]["prediction"] == "Red"
    assert summary["samples"][1]["detectorState"] == "None"
    assert summary["samples"][1]["classifierState"] is None
    assert classifier_calls and classifier_calls[0][2] == 3


def test_shadow_eval_counts_classifier_result_instead_of_detector_hint(tmp_path):
    frames = tmp_path / "frames"
    frames.mkdir()
    Image.fromarray(_synthetic_frame("Green")).save(frames / "green.jpg")
    dataset_path = tmp_path / "dataset" / "dataset.jsonl"
    _write_dataset(
        dataset_path,
        [{"sourceImage": "frames/green.jpg", "label": "Green", "labelId": 2}],
    )

    def predict_red(_crop_rgb):
        return [0.90, 0.05, 0.03, 0.02]

    summary = evaluate_shadow_dataset(dataset_path, predict_red, root_dir=tmp_path)

    assert summary["n"] == 1
    assert summary["accuracy"] == 0.0
    assert summary["confusion"][2][0] == 1
    assert summary["samples"][0]["detectorState"] == "Green"
    assert summary["samples"][0]["classifierState"] == "Red"
    assert summary["samples"][0]["prediction"] == "Red"


def test_trace_frame_shadow_eval_maps_video_frames_to_trace_samples(tmp_path):
    frames = tmp_path / "frames"
    frames.mkdir()
    for index, state in enumerate(["Red", "Yellow", "Green"]):
        Image.fromarray(_synthetic_frame(state)).save(frames / f"frame_{index:04d}.jpg")
    trace_path = tmp_path / "trace.jsonl"
    trace_rows = []
    for sample_index, state in enumerate(["Red", "Red", "Yellow", "Yellow", "Green"]):
        trace_rows.append(
            {
                "sample": {
                    "sample": sample_index,
                    "trafficLightState": state,
                    "trafficLightDistanceM": f"{10 - sample_index:.3f}",
                }
            }
        )
    trace_path.write_text("\n".join(json.dumps(row) for row in trace_rows), encoding="utf-8")

    predictions = iter(
        [
            [0.95, 0.02, 0.02, 0.01],
            [0.02, 0.95, 0.02, 0.01],
            [0.02, 0.02, 0.95, 0.01],
        ]
    )

    def predict_from_sequence(_crop_rgb):
        return next(predictions)

    summary = evaluate_trace_frame_directory(trace_path, frames, predict_from_sequence)

    assert summary["n"] == 3
    assert [sample["traceIndex"] for sample in summary["samples"]] == [0, 2, 4]
    assert [sample["label"] for sample in summary["samples"]] == ["Red", "Yellow", "Green"]
    assert summary["accuracy"] == 1.0
    assert summary["classifierInvocations"] == 3


def test_trace_frame_shadow_eval_uses_classifier_prediction_not_trace_label(tmp_path):
    frames = tmp_path / "frames"
    frames.mkdir()
    Image.fromarray(_synthetic_frame("Green")).save(frames / "frame_0000.jpg")
    trace_path = tmp_path / "trace.jsonl"
    trace_path.write_text(
        json.dumps({"sample": {"sample": 7, "trafficLightState": "Green"}}),
        encoding="utf-8",
    )

    def predict_red(_crop_rgb):
        return [0.90, 0.05, 0.03, 0.02]

    summary = evaluate_trace_frame_directory(trace_path, frames, predict_red)

    assert summary["n"] == 1
    assert summary["accuracy"] == 0.0
    assert summary["confusion"][2][0] == 1
    assert summary["samples"][0]["detectorState"] == "Green"
    assert summary["samples"][0]["classifierState"] == "Red"
    assert summary["samples"][0]["prediction"] == "Red"


def test_trace_frame_shadow_eval_reports_distance_buckets(tmp_path):
    frames = tmp_path / "frames"
    frames.mkdir()
    for index in range(4):
        Image.fromarray(_synthetic_frame("Green")).save(frames / f"frame_{index:04d}.jpg")
    trace_path = tmp_path / "trace.jsonl"
    rows = [
        {"sample": {"sample": 0, "trafficLightState": "Green", "trafficLightDistanceM": "12.0"}},
        {"sample": {"sample": 1, "trafficLightState": "Green", "trafficLightDistanceM": "8.0"}},
        {"sample": {"sample": 2, "trafficLightState": "Green", "trafficLightDistanceM": "4.0"}},
        {"sample": {"sample": 3, "trafficLightState": "None", "trafficLightDistanceM": "-1.000"}},
    ]
    trace_path.write_text("\n".join(json.dumps(row) for row in rows), encoding="utf-8")
    predictions = iter(
        [
            [0.90, 0.05, 0.03, 0.02],
            [0.02, 0.03, 0.93, 0.02],
            [0.02, 0.03, 0.93, 0.02],
            [0.90, 0.05, 0.03, 0.02],
        ]
    )

    summary = evaluate_trace_frame_directory(trace_path, frames, lambda _crop: next(predictions))

    buckets = {bucket["name"]: bucket for bucket in summary["distanceBuckets"]}
    assert buckets["far"]["n"] == 1
    assert buckets["approach"]["n"] == 1
    assert buckets["close"]["n"] == 1
    assert buckets["none"]["n"] == 1
    assert buckets["far"]["accuracy"] == 0.0
    assert buckets["approach"]["accuracy"] == 1.0
    assert buckets["close"]["accuracy"] == 1.0
    assert buckets["none"]["accuracy"] == 0.0


def test_recorder_trace_shadow_eval_uses_exact_crop_paths(tmp_path):
    crops = tmp_path / "crops"
    crops.mkdir()
    Image.fromarray(_synthetic_frame("Red")).save(crops / "red.jpg")
    Image.fromarray(_synthetic_frame("Yellow")).save(crops / "yellow.jpg")
    trace_path = tmp_path / "red-stop-trace.jsonl"
    rows = [
        {
            "step": 21,
            "route": "red-stop",
            "gtState": "Red",
            "gtDistanceM": "3.5",
            "detectorState": "Red",
            "visionConfidence": 0.96,
            "detectorBBox": [10, 20, 40, 80],
            "modelFramePath": "model/red.jpg",
            "cropPath": str(crops / "red.jpg"),
        },
        {
            "step": 24,
            "route": "red-stop",
            "gtState": "Yellow",
            "gtDistanceM": "6.5",
            "detectorState": "Yellow",
            "detectorBBox": [10, 20, 40, 80],
            "modelFramePath": "model/yellow.jpg",
            "cropPath": str(crops / "yellow.jpg"),
        },
        {
            "step": 27,
            "route": "red-stop",
            "gtState": "None",
            "gtDistanceM": "-1.0",
            "detectorState": "None",
            "detectorBBox": None,
            "modelFramePath": "model/none.jpg",
        },
    ]
    trace_path.write_text("\n".join(json.dumps(row) for row in rows), encoding="utf-8")
    classifier_calls = []

    def predict_from_crop(crop_rgb):
        classifier_calls.append(crop_rgb.shape)
        if len(classifier_calls) == 1:
            return [0.97, 0.01, 0.01, 0.01]
        return [0.01, 0.97, 0.01, 0.01]

    summary = evaluate_recorder_trace(trace_path, predict_from_crop)

    assert summary["n"] == 3
    assert summary["accuracy"] == 1.0
    assert summary["classifierInvocations"] == 2
    assert [sample["step"] for sample in summary["samples"]] == [21, 24, 27]
    assert [sample["prediction"] for sample in summary["samples"]] == ["Red", "Yellow", "None"]
    assert classifier_calls and classifier_calls[0] == (84, 84, 3)
    buckets = {bucket["name"]: bucket for bucket in summary["distanceBuckets"]}
    assert buckets["close"]["correct"] == 1
    assert buckets["approach"]["correct"] == 1
    assert buckets["none"]["correct"] == 1


def test_temporal_gate_requires_consecutive_camera_predictions():
    samples = [
        {"prediction": "None", "label": "None", "trafficLightDistanceM": "-1.0"},
        {"prediction": "Red", "label": "None", "trafficLightDistanceM": "-1.0"},
        {"prediction": "None", "label": "None", "trafficLightDistanceM": "-1.0"},
        {"prediction": "Green", "label": "Green", "trafficLightDistanceM": "6.0"},
        {"prediction": "Green", "label": "Green", "trafficLightDistanceM": "4.0"},
    ]

    result = apply_temporal_gate(samples, min_state_frames=2, none_reset_frames=1)

    assert [sample["temporalPrediction"] for sample in result["samples"]] == [
        "None",
        "None",
        "None",
        "None",
        "Green",
    ]
    assert result["accuracy"] == 0.8
    assert result["confusion"][3][3] == 3
    assert result["confusion"][2][3] == 1
    assert result["confusion"][2][2] == 1
    buckets = {bucket["name"]: bucket for bucket in result["distanceBuckets"]}
    assert buckets["none"]["correct"] == 3
    assert buckets["approach"]["misses"] == 1
    assert buckets["close"]["correct"] == 1


def test_geometry_gate_rejects_oversized_camera_detections():
    samples = [
        {
            "prediction": "Red",
            "label": "None",
            "labelId": 3,
            "detectorBBox": [20, 20, 220, 220],
            "trafficLightDistanceM": "-1.0",
        },
        {
            "prediction": "Green",
            "label": "Green",
            "labelId": 2,
            "detectorBBox": [100, 50, 150, 130],
            "trafficLightDistanceM": "4.0",
        },
        {
            "prediction": "None",
            "label": "None",
            "labelId": 3,
            "detectorBBox": None,
            "trafficLightDistanceM": "-1.0",
        },
    ]

    result = apply_geometry_gate(samples, max_bbox_area_px=10_000, max_bbox_width_px=125)

    assert [sample["geometryPrediction"] for sample in result["samples"]] == [
        "None",
        "Green",
        "None",
    ]
    assert [sample["geometryRejected"] for sample in result["samples"]] == [True, False, False]
    assert result["accuracy"] == 1.0
    assert result["confusion"][3][3] == 2
    assert result["confusion"][2][2] == 1
    buckets = {bucket["name"]: bucket for bucket in result["distanceBuckets"]}
    assert buckets["none"]["correct"] == 2
    assert buckets["close"]["correct"] == 1


def test_pose_relevance_gate_rejects_predictions_outside_approach_window():
    samples = [
        {
            "prediction": "Red",
            "label": "None",
            "labelId": 3,
            "pose": {"x": -4.0, "z": -35.0},
            "trafficLightDistanceM": "-1.0",
        },
        {
            "prediction": "Red",
            "label": "Red",
            "labelId": 0,
            "pose": {"x": -4.1, "z": -12.0},
            "trafficLightDistanceM": "4.0",
        },
        {
            "prediction": "Green",
            "label": "None",
            "labelId": 3,
            "pose": {"x": -4.0, "z": -5.0},
            "trafficLightDistanceM": "-1.0",
        },
    ]

    result = apply_pose_relevance_gate(
        samples,
        lane_x=-4.0,
        lane_tolerance_m=1.5,
        min_z=-24.0,
        max_z=-7.0,
    )

    assert [sample["poseRelevancePrediction"] for sample in result["samples"]] == [
        "None",
        "Red",
        "None",
    ]
    assert [sample["poseRelevanceRejected"] for sample in result["samples"]] == [True, False, True]
    assert result["accuracy"] == 1.0
    assert result["rejected"] == 2


def test_pose_relevance_gate_supports_westbound_lane_z_and_x_window():
    samples = [
        {
            "prediction": "Red",
            "label": "None",
            "labelId": 3,
            "pose": {"x": -60.88, "z": 1.2},
            "trafficLightDistanceM": "-1.0",
        },
        {
            "prediction": "Red",
            "label": "Red",
            "labelId": 0,
            "pose": {"x": -67.48, "z": 1.2},
            "trafficLightDistanceM": "1.920",
        },
        {
            "prediction": "Green",
            "label": "None",
            "labelId": 3,
            "pose": {"x": -72.50, "z": 1.1},
            "trafficLightDistanceM": "-1.0",
        },
    ]

    result = apply_pose_relevance_gate(
        samples,
        lane_z=1.2,
        lane_tolerance_m=1.5,
        min_x=-69.5,
        max_x=-61.0,
    )

    assert [sample["poseRelevancePrediction"] for sample in result["samples"]] == [
        "None",
        "Red",
        "None",
    ]
    assert [sample["poseRelevanceRejected"] for sample in result["samples"]] == [True, False, True]
    assert result["laneZ"] == 1.2
    assert result["minX"] == -69.5
    assert result["maxX"] == -61.0
    assert result["accuracy"] == 1.0
    assert result["rejected"] == 2


def test_pose_actionable_label_gate_marks_after_stopline_label_as_none():
    samples = [
        {
            "poseRelevancePrediction": "None",
            "label": "Green",
            "labelId": 2,
            "pose": {"x": -4.0, "z": -8.7},
            "trafficLightDistanceM": "0.5",
        },
        {
            "poseRelevancePrediction": "Green",
            "label": "Green",
            "labelId": 2,
            "pose": {"x": -4.0, "z": -12.0},
            "trafficLightDistanceM": "4.0",
        },
        {
            "poseRelevancePrediction": "None",
            "label": "None",
            "labelId": 3,
            "pose": {"x": 2.0, "z": -6.4},
            "trafficLightDistanceM": "-1.0",
        },
    ]

    result = apply_pose_actionable_label_gate(
        samples,
        lane_x=-4.0,
        lane_tolerance_m=1.5,
        min_z=-24.0,
        max_z=-9.0,
        prediction_key="poseRelevancePrediction",
    )

    assert [sample["poseActionableLabel"] for sample in result["samples"]] == [
        "None",
        "Green",
        "None",
    ]
    assert [sample["poseActionableLabelChanged"] for sample in result["samples"]] == [
        True,
        False,
        False,
    ]
    assert result["changedLabels"] == 1
    assert result["accuracy"] == 1.0


def test_pose_actionable_label_gate_supports_westbound_lane_z_and_x_window():
    samples = [
        {
            "poseRelevancePrediction": "None",
            "label": "Green",
            "labelId": 2,
            "pose": {"x": -70.2, "z": 1.2},
            "trafficLightDistanceM": "-1.0",
        },
        {
            "poseRelevancePrediction": "Red",
            "label": "Red",
            "labelId": 0,
            "pose": {"x": -67.4, "z": 1.2},
            "trafficLightDistanceM": "1.9",
        },
        {
            "poseRelevancePrediction": "None",
            "label": "None",
            "labelId": 3,
            "pose": {"x": -60.8, "z": 1.2},
            "trafficLightDistanceM": "-1.0",
        },
    ]

    result = apply_pose_actionable_label_gate(
        samples,
        lane_z=1.2,
        lane_tolerance_m=1.5,
        min_x=-69.5,
        max_x=-61.0,
        prediction_key="poseRelevancePrediction",
    )

    assert [sample["poseActionableLabel"] for sample in result["samples"]] == [
        "None",
        "Red",
        "None",
    ]
    assert [sample["poseActionableLabelChanged"] for sample in result["samples"]] == [
        True,
        False,
        False,
    ]
    assert result["changedLabels"] == 1
    assert result["accuracy"] == 1.0


def test_action_decision_eval_groups_green_yellow_none_as_go_and_red_as_stop():
    samples = [
        {"poseActionableLabel": "Green", "poseRelevancePrediction": "Yellow"},
        {"poseActionableLabel": "Yellow", "poseRelevancePrediction": "Green"},
        {"poseActionableLabel": "None", "poseRelevancePrediction": "None"},
        {"poseActionableLabel": "Red", "poseRelevancePrediction": "Red"},
        {"poseActionableLabel": "Red", "poseRelevancePrediction": "Yellow"},
    ]

    result = apply_action_decision_eval(
        samples,
        label_key="poseActionableLabel",
        prediction_key="poseRelevancePrediction",
    )

    assert [sample["actionLabel"] for sample in result["samples"]] == [
        "GO",
        "GO",
        "GO",
        "STOP",
        "STOP",
    ]
    assert [sample["actionPrediction"] for sample in result["samples"]] == [
        "GO",
        "GO",
        "GO",
        "STOP",
        "GO",
    ]
    assert result["accuracy"] == 0.8
    assert result["confusion"] == [[3, 0], [1, 1]]


def test_recorder_trace_shadow_eval_applies_pose_relevance_gate(tmp_path):
    crops = tmp_path / "crops"
    crops.mkdir()
    Image.fromarray(_synthetic_frame("Red")).save(crops / "red.jpg")
    Image.fromarray(_synthetic_frame("Green")).save(crops / "green.jpg")
    trace_path = tmp_path / "pose-trace.jsonl"
    rows = [
        {
            "step": 10,
            "gtState": "None",
            "gtDistanceM": "-1.0",
            "detectorState": "Red",
            "detectorBBox": [10, 20, 40, 80],
            "cropPath": str(crops / "red.jpg"),
            "pose": {"x": -4.0, "z": -35.0},
        },
        {
            "step": 20,
            "gtState": "Red",
            "gtDistanceM": "4.0",
            "detectorState": "Red",
            "detectorBBox": [10, 20, 40, 80],
            "cropPath": str(crops / "red.jpg"),
            "pose": {"x": -4.0, "z": -12.0},
        },
        {
            "step": 30,
            "gtState": "None",
            "gtDistanceM": "-1.0",
            "detectorState": "Green",
            "detectorBBox": [10, 20, 40, 80],
            "cropPath": str(crops / "green.jpg"),
            "pose": {"x": -4.0, "z": -5.0},
        },
    ]
    trace_path.write_text("\n".join(json.dumps(row) for row in rows), encoding="utf-8")
    predictions = iter(
        [
            [0.97, 0.01, 0.01, 0.01],
            [0.97, 0.01, 0.01, 0.01],
            [0.01, 0.01, 0.97, 0.01],
        ]
    )

    summary = evaluate_recorder_trace(
        trace_path,
        lambda _crop: next(predictions),
        pose_gate_lane_x=-4.0,
        pose_gate_lane_tolerance_m=1.5,
        pose_gate_min_z=-24.0,
        pose_gate_max_z=-7.0,
    )

    assert summary["accuracy"] == 1 / 3
    assert summary["poseRelevanceGate"]["accuracy"] == 1.0
    assert [sample["poseRelevancePrediction"] for sample in summary["samples"]] == [
        "None",
        "Red",
        "None",
    ]
