import json

import numpy as np
from PIL import Image

from training.traffic.vision_dataset import (
    LABEL_IDS,
    build_clear_visual_dataset_from_candidates,
    build_hard_case_dataset_from_shadow_eval,
    build_hard_negative_dataset_from_shadow_eval,
    build_visual_dataset,
    build_visual_label_candidates_from_trace,
)


def _synthetic_frame(active_state: str) -> np.ndarray:
    image = np.full((240, 320, 3), (108, 132, 148), dtype=np.uint8)
    image[150:, :, :] = (60, 62, 58)
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


def test_build_visual_dataset_writes_crops_and_city_schema(tmp_path):
    source_dir = tmp_path / "src"
    source_dir.mkdir()
    red_path = source_dir / "red.jpg"
    green_path = source_dir / "green.jpg"
    Image.fromarray(_synthetic_frame("Red")).save(red_path)
    Image.fromarray(_synthetic_frame("Green")).save(green_path)

    dataset_path = build_visual_dataset([red_path, green_path], tmp_path / "dataset")

    rows = [json.loads(line) for line in dataset_path.read_text().splitlines()]
    assert [row["label"] for row in rows] == ["Red", "Green"]
    assert [row["labelId"] for row in rows] == [LABEL_IDS["Red"], LABEL_IDS["Green"]]
    for row in rows:
        crop_path = dataset_path.parent / row["modelInputPath"]
        assert crop_path.exists()
        assert Image.open(crop_path).size == (84, 84)
        assert row["bbox_xyxy"] is not None


def test_build_visual_dataset_can_include_none_negative_sample(tmp_path):
    source = tmp_path / "empty.jpg"
    Image.fromarray(np.full((180, 260, 3), 92, dtype=np.uint8)).save(source)

    dataset_path = build_visual_dataset([source], tmp_path / "dataset", include_none=True)

    rows = [json.loads(line) for line in dataset_path.read_text().splitlines()]
    assert len(rows) == 1
    assert rows[0]["label"] == "None"
    assert rows[0]["labelId"] == LABEL_IDS["None"]
    assert rows[0]["bbox_xyxy"] is None
    assert Image.open(dataset_path.parent / rows[0]["modelInputPath"]).size == (84, 84)


def test_build_visual_dataset_skips_none_when_disabled(tmp_path):
    source = tmp_path / "empty.jpg"
    Image.fromarray(np.full((180, 260, 3), 92, dtype=np.uint8)).save(source)

    dataset_path = build_visual_dataset([source], tmp_path / "dataset", include_none=False)

    assert dataset_path.read_text() == ""


def test_build_hard_negative_dataset_from_shadow_eval_crops_false_positive_detections(tmp_path):
    frame_dir = tmp_path / "frames"
    frame_dir.mkdir()
    frame = np.full((120, 180, 3), (20, 30, 40), dtype=np.uint8)
    frame[20:80, 50:120] = (240, 210, 30)
    frame_path = frame_dir / "frame_0001.jpg"
    Image.fromarray(frame).save(frame_path)
    summary_path = tmp_path / "shadow-summary.json"
    summary_path.write_text(
        json.dumps(
            {
                "samples": [
                    {
                        "framePath": str(frame_path),
                        "label": "None",
                        "labelId": LABEL_IDS["None"],
                        "prediction": "Yellow",
                        "detectorBBox": [50, 20, 120, 80],
                        "detectorConfidence": 0.91,
                    },
                    {
                        "framePath": str(frame_path),
                        "label": "Green",
                        "labelId": LABEL_IDS["Green"],
                        "prediction": "Red",
                        "detectorBBox": [0, 0, 20, 20],
                    },
                    {
                        "framePath": str(frame_path),
                        "label": "None",
                        "labelId": LABEL_IDS["None"],
                        "prediction": "None",
                        "detectorBBox": None,
                    },
                ]
            }
        ),
        encoding="utf-8",
    )

    dataset_path = build_hard_negative_dataset_from_shadow_eval(
        summary_path,
        tmp_path / "hard-negatives",
    )

    rows = [json.loads(line) for line in dataset_path.read_text().splitlines()]
    assert len(rows) == 1
    assert rows[0]["label"] == "None"
    assert rows[0]["labelId"] == LABEL_IDS["None"]
    assert rows[0]["hardNegative"] is True
    assert rows[0]["sourcePrediction"] == "Yellow"
    assert rows[0]["bbox_xyxy"] == [50, 20, 120, 80]
    crop_path = dataset_path.parent / rows[0]["modelInputPath"]
    assert crop_path.exists()
    assert Image.open(crop_path).size == (84, 84)


def test_build_hard_case_dataset_from_shadow_eval_keeps_true_runtime_labels(tmp_path):
    frame_dir = tmp_path / "frames"
    frame_dir.mkdir()
    frame = np.full((120, 180, 3), (20, 30, 40), dtype=np.uint8)
    frame[20:80, 50:120] = (240, 210, 30)
    frame_path = frame_dir / "frame_0064.jpg"
    Image.fromarray(frame).save(frame_path)
    summary_path = tmp_path / "shadow-summary.json"
    summary_path.write_text(
        json.dumps(
            {
                "samples": [
                    {
                        "framePath": str(frame_path),
                        "label": "Yellow",
                        "labelId": LABEL_IDS["Yellow"],
                        "prediction": "Red",
                        "detectorState": "Red",
                        "classifierState": "Red",
                        "detectorBBox": [50, 20, 120, 80],
                        "detectorConfidence": 0.91,
                        "correct": False,
                    },
                    {
                        "framePath": str(frame_path),
                        "label": "Green",
                        "labelId": LABEL_IDS["Green"],
                        "prediction": "Green",
                        "detectorBBox": [0, 0, 20, 20],
                        "correct": True,
                    },
                ]
            }
        ),
        encoding="utf-8",
    )

    dataset_path = build_hard_case_dataset_from_shadow_eval(
        summary_path,
        tmp_path / "hard-cases",
    )

    rows = [json.loads(line) for line in dataset_path.read_text().splitlines()]
    assert len(rows) == 1
    assert rows[0]["label"] == "Yellow"
    assert rows[0]["labelId"] == LABEL_IDS["Yellow"]
    assert rows[0]["hardCase"] is True
    assert rows[0]["sourcePrediction"] == "Red"
    assert rows[0]["sourceClassifierState"] == "Red"
    assert rows[0]["bbox_xyxy"] == [50, 20, 120, 80]
    crop_path = dataset_path.parent / rows[0]["modelInputPath"]
    assert crop_path.exists()
    assert Image.open(crop_path).size == (84, 84)


def test_build_hard_case_dataset_from_shadow_eval_can_use_temporal_prediction_key(tmp_path):
    frame_dir = tmp_path / "frames"
    frame_dir.mkdir()
    frame = np.full((120, 180, 3), (20, 30, 40), dtype=np.uint8)
    frame[20:80, 50:120] = (40, 230, 70)
    frame_path = frame_dir / "frame_0100.jpg"
    Image.fromarray(frame).save(frame_path)
    summary_path = tmp_path / "shadow-summary.json"
    summary_path.write_text(
        json.dumps(
            {
                "samples": [
                    {
                        "framePath": str(frame_path),
                        "label": "Green",
                        "labelId": LABEL_IDS["Green"],
                        "prediction": "Green",
                        "temporalPrediction": "None",
                        "detectorState": "Green",
                        "classifierState": "Green",
                        "detectorBBox": [50, 20, 120, 80],
                        "detectorConfidence": 0.85,
                    },
                    {
                        "framePath": str(frame_path),
                        "label": "Yellow",
                        "labelId": LABEL_IDS["Yellow"],
                        "prediction": "Green",
                        "temporalPrediction": "Yellow",
                        "detectorState": "Green",
                        "classifierState": "Green",
                        "detectorBBox": [50, 20, 120, 80],
                    },
                ]
            }
        ),
        encoding="utf-8",
    )

    dataset_path = build_hard_case_dataset_from_shadow_eval(
        summary_path,
        tmp_path / "temporal-hard-cases",
        prediction_key="temporalPrediction",
    )

    rows = [json.loads(line) for line in dataset_path.read_text().splitlines()]
    assert len(rows) == 1
    assert rows[0]["label"] == "Green"
    assert rows[0]["sourcePrediction"] == "None"
    assert rows[0]["sourcePredictionKey"] == "temporalPrediction"


def test_build_hard_case_dataset_from_shadow_eval_can_use_pose_actionable_label_key(tmp_path):
    frame_dir = tmp_path / "frames"
    frame_dir.mkdir()
    frame = np.full((120, 180, 3), (20, 30, 40), dtype=np.uint8)
    frame[20:80, 50:120] = (245, 30, 30)
    frame_path = frame_dir / "frame_0200.jpg"
    Image.fromarray(frame).save(frame_path)
    summary_path = tmp_path / "shadow-summary.json"
    summary_path.write_text(
        json.dumps(
            {
                "samples": [
                    {
                        "framePath": str(frame_path),
                        "label": "Red",
                        "poseActionableLabel": "None",
                        "poseActionablePrediction": "None",
                        "detectorState": "Red",
                        "detectorBBox": [50, 20, 120, 80],
                    },
                    {
                        "framePath": str(frame_path),
                        "label": "Red",
                        "poseActionableLabel": "Red",
                        "poseActionablePrediction": "None",
                        "detectorState": "Red",
                        "detectorBBox": [50, 20, 120, 80],
                    },
                ]
            }
        ),
        encoding="utf-8",
    )

    dataset_path = build_hard_case_dataset_from_shadow_eval(
        summary_path,
        tmp_path / "pose-actionable-hard-cases",
        prediction_key="poseActionablePrediction",
        label_key="poseActionableLabel",
    )

    rows = [json.loads(line) for line in dataset_path.read_text().splitlines()]
    assert len(rows) == 1
    assert rows[0]["label"] == "Red"
    assert rows[0]["labelId"] == LABEL_IDS["Red"]
    assert rows[0]["sourceLabelKey"] == "poseActionableLabel"
    assert rows[0]["sourcePrediction"] == "None"


def test_build_visual_label_candidates_from_trace_copies_crop_and_marks_review(tmp_path):
    source_dir = tmp_path / "source"
    source_dir.mkdir()
    crop = source_dir / "crop_0001.jpg"
    model = source_dir / "model_0001.jpg"
    Image.fromarray(_synthetic_frame("Yellow")).save(crop)
    Image.fromarray(_synthetic_frame("Yellow")).save(model)
    trace_path = tmp_path / "trace.jsonl"
    trace_path.write_text(
        "\n".join(
            [
                json.dumps(
                    {
                        "step": 12,
                        "gtState": "Yellow",
                        "visionPrediction": "Green",
                        "visionConfidence": 0.99,
                        "gtDistanceM": "5.0",
                        "speedMps": "1.0",
                        "detectorBBox": [10, 20, 40, 80],
                        "classifierProbabilities": [0.0, 0.1, 0.9, 0.0],
                        "modelFramePath": str(model),
                        "cropPath": str(crop),
                    }
                ),
                json.dumps(
                    {
                        "step": 15,
                        "gtState": "None",
                        "visionPrediction": "None",
                        "cropPath": str(crop),
                    }
                ),
            ]
        ),
        encoding="utf-8",
    )

    candidates_path = build_visual_label_candidates_from_trace(trace_path, tmp_path / "candidates")

    rows = [json.loads(line) for line in candidates_path.read_text().splitlines()]
    assert len(rows) == 1
    assert rows[0]["telemetryLabel"] == "Yellow"
    assert rows[0]["candidateVisualLabel"] == "Green"
    assert rows[0]["needsHumanReview"] is True
    assert rows[0]["bbox_xyxy"] == [10, 20, 40, 80]
    assert Image.open(candidates_path.parent / rows[0]["modelInputPath"]).size == (84, 84)


def test_build_clear_visual_dataset_from_candidates_keeps_high_confidence_agreement(tmp_path):
    candidate_dir = tmp_path / "candidates"
    crops_dir = candidate_dir / "crops"
    crops_dir.mkdir(parents=True)
    yellow_crop = crops_dir / "yellow.jpg"
    red_crop = crops_dir / "red.jpg"
    Image.fromarray(_synthetic_frame("Yellow")).save(yellow_crop)
    Image.fromarray(_synthetic_frame("Red")).save(red_crop)
    candidates_path = candidate_dir / "candidates.jsonl"
    candidates_path.write_text(
        "\n".join(
            [
                json.dumps(
                    {
                        "step": 20,
                        "telemetryLabel": "Yellow",
                        "candidateVisualLabel": "Yellow",
                        "candidateConfidence": 0.98,
                        "modelInputPath": "crops/yellow.jpg",
                        "distanceM": "4.0",
                    }
                ),
                json.dumps(
                    {
                        "step": 21,
                        "telemetryLabel": "Red",
                        "candidateVisualLabel": "Red",
                        "candidateConfidence": 0.80,
                        "modelInputPath": "crops/red.jpg",
                    }
                ),
                json.dumps(
                    {
                        "step": 22,
                        "telemetryLabel": "Yellow",
                        "candidateVisualLabel": "Green",
                        "candidateConfidence": 1.0,
                        "modelInputPath": "crops/yellow.jpg",
                    }
                ),
            ]
        ),
        encoding="utf-8",
    )

    dataset_path = build_clear_visual_dataset_from_candidates(
        candidates_path,
        tmp_path / "clear",
        min_confidence=0.95,
    )

    rows = [json.loads(line) for line in dataset_path.read_text().splitlines()]
    assert len(rows) == 1
    assert rows[0]["label"] == "Yellow"
    assert rows[0]["labelId"] == LABEL_IDS["Yellow"]
    assert rows[0]["visualLabelSource"] == "telemetry-visual-agreement"
    assert Image.open(dataset_path.parent / rows[0]["modelInputPath"]).size == (84, 84)
