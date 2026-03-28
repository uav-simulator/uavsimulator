#!/usr/bin/env python3
"""Build baseline A->B telemetry policy artifact (ONNX + metadata + metrics)."""

from __future__ import annotations

from dataclasses import dataclass
from pathlib import Path
import json
import sys

import numpy as np

try:
    import onnx
    from onnx import TensorProto, helper
except Exception as exc:  # pragma: no cover - runtime dependency check
    raise SystemExit(
        "onnx package is required. Install with: pip install onnx numpy"
    ) from exc


ROOT = Path(__file__).resolve().parents[2]
OUTPUT_DIR = ROOT / "python/training/artifacts/ab_corridor_policy_v1"
MODEL_FILENAME = "ab_corridor_policy_v1.onnx"
METADATA_FILENAME = "metadata.json"
METRICS_FILENAME = "metrics.json"


@dataclass(frozen=True)
class PolicyConfig:
    input_size: int = 6
    output_size: int = 2


def create_linear_policy_model(config: PolicyConfig) -> onnx.ModelProto:
    # Features:
    # 0 left_line_norm, 1 center_line_norm, 2 right_line_norm,
    # 3 front_range_norm, 4 speed_norm, 5 bias
    # Output:
    # 0 throttle, 1 steer
    weights = np.array(
        [
            [-0.30, -0.80],
            [0.70, 0.00],
            [-0.30, 0.80],
            [0.70, 0.00],
            [-0.45, 0.00],
            [0.10, 0.00],
        ],
        dtype=np.float32,
    )
    bias = np.array([0.10, 0.0], dtype=np.float32)

    x = helper.make_tensor_value_info("obs", TensorProto.FLOAT, [1, config.input_size])
    y = helper.make_tensor_value_info("action", TensorProto.FLOAT, [1, config.output_size])

    w_tensor = helper.make_tensor(
        "W",
        TensorProto.FLOAT,
        dims=[config.input_size, config.output_size],
        vals=weights.flatten().tolist(),
    )
    b_tensor = helper.make_tensor(
        "B",
        TensorProto.FLOAT,
        dims=[config.output_size],
        vals=bias.flatten().tolist(),
    )

    matmul_node = helper.make_node("MatMul", ["obs", "W"], ["hidden"])
    add_node = helper.make_node("Add", ["hidden", "B"], ["logits"])
    tanh_node = helper.make_node("Tanh", ["logits"], ["action"])

    graph = helper.make_graph(
        [matmul_node, add_node, tanh_node],
        "ab_corridor_policy_v1",
        [x],
        [y],
        [w_tensor, b_tensor],
    )
    model = helper.make_model(
        graph,
        producer_name="uav-simulator/python-training",
        opset_imports=[helper.make_operatorsetid("", 13)],
    )
    onnx.checker.check_model(model)
    return model


def build_metadata(config: PolicyConfig) -> dict:
    return {
        "policyId": "ab_corridor_policy_v1",
        "format": "onnx",
        "version": "1.0.0",
        "scenario": "A->B corridor",
        "runtime": "backend-pc",
        "observationSchema": {
            "size": config.input_size,
            "features": [
                "line.left_norm",
                "line.center_norm",
                "line.right_norm",
                "range.front_norm",
                "speed.norm",
                "bias",
            ],
        },
        "actionSchema": {
            "size": config.output_size,
            "outputs": ["throttle", "steer"],
            "range": [-1.0, 1.0],
        },
        "rewardV1": {
            "progressToGoal": "+",
            "lateralDeviationPenalty": "-",
            "steerJerkPenalty": "-",
            "collisionOrOutOfBounds": "large negative",
            "goalReached": "positive terminal reward",
        },
    }


def build_metrics() -> dict:
    return {
        "kind": "baseline-handcrafted-linear-policy",
        "note": "Synthetic baseline artifact for train/install/run pipeline bring-up",
        "kpiTarget": {
            "simSuccessRateTarget": 0.70,
            "evaluationEpisodes": 20,
        },
    }


def main() -> int:
    OUTPUT_DIR.mkdir(parents=True, exist_ok=True)
    config = PolicyConfig()

    model = create_linear_policy_model(config)
    model_path = OUTPUT_DIR / MODEL_FILENAME
    onnx.save(model, model_path.as_posix())

    metadata_path = OUTPUT_DIR / METADATA_FILENAME
    metrics_path = OUTPUT_DIR / METRICS_FILENAME
    metadata_path.write_text(json.dumps(build_metadata(config), ensure_ascii=False, indent=2), encoding="utf-8")
    metrics_path.write_text(json.dumps(build_metrics(), ensure_ascii=False, indent=2), encoding="utf-8")

    print(f"Saved model: {model_path}")
    print(f"Saved metadata: {metadata_path}")
    print(f"Saved metrics: {metrics_path}")
    return 0


if __name__ == "__main__":
    sys.exit(main())
