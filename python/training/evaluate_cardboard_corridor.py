#!/usr/bin/env python3
"""Evaluate cardboard corridor vision policy against unity-sim."""

from __future__ import annotations

import argparse
import json
from pathlib import Path
import sys
from typing import Any

import numpy as np

ROOT = Path(__file__).resolve().parents[2]
PYTHON_ROOT = ROOT / "python"
if str(PYTHON_ROOT) not in sys.path:
    sys.path.insert(0, str(PYTHON_ROOT))

from training.ab_corridor_vision_env import ABCorridorVisionEnv
from training.model_artifacts import default_artifact_dir, default_onnx_file_name, default_sb3_stem

DEFAULT_SCENARIO = ROOT / "configs/scenarios/cardboard-corridor-v1.yaml"
DEFAULT_MODEL_NAME = "cardboard-corridor-policy"
DEFAULT_MODEL_VERSION = "v1.0.0"


def parse_args() -> argparse.Namespace:
    parser = argparse.ArgumentParser(description="Evaluate cardboard corridor vision policy.")
    parser.add_argument("--base-url", default="http://127.0.0.1:8000")
    parser.add_argument("--scenario", default=str(DEFAULT_SCENARIO))
    parser.add_argument("--model", default="")
    parser.add_argument("--model-name", default=DEFAULT_MODEL_NAME)
    parser.add_argument("--model-version", default=DEFAULT_MODEL_VERSION)
    parser.add_argument("--model-format", choices=("onnx", "ppo"), default="onnx")
    parser.add_argument("--episodes", type=int, default=10)
    parser.add_argument("--max-steps", type=int, default=120)
    parser.add_argument("--seed-offset", type=int, default=3000)
    parser.add_argument("--img-size", type=int, default=84)
    parser.add_argument("--output-json", default="")
    return parser.parse_args()


def build_predictor(model_path: Path):
    suffix = model_path.suffix.lower()
    if suffix == ".onnx":
        import onnxruntime as ort

        session = ort.InferenceSession(str(model_path), providers=["CPUExecutionProvider"])
        input_names = [item.name for item in session.get_inputs()]
        image_name = input_names[0]
        ultrasonic_name = input_names[1]

        def predict(obs: dict[str, Any]) -> np.ndarray:
            image = obs["image"].astype(np.float32)[np.newaxis, ...]
            ultrasonic = obs["ultrasonic"].astype(np.float32)[np.newaxis, ...]
            action = session.run(None, {image_name: image, ultrasonic_name: ultrasonic})[0][0]
            return np.asarray(action, dtype=np.float32)

        return predict, "onnx"

    if suffix == ".zip":
        from stable_baselines3 import PPO

        model = PPO.load(str(model_path))

        def predict(obs: dict[str, Any]) -> np.ndarray:
            action, _ = model.predict(obs, deterministic=True)
            return np.asarray(action, dtype=np.float32)

        return predict, "ppo"

    raise ValueError(f"Unsupported model format: {model_path}")


def evaluate(args: argparse.Namespace) -> dict[str, Any]:
    if args.model.strip():
        model_path = Path(args.model).expanduser().resolve()
    else:
        artifact_dir = default_artifact_dir(ROOT, args.model_name, args.model_version)
        if args.model_format == "ppo":
            model_path = (artifact_dir / f"{default_sb3_stem(args.model_name)}.zip").resolve()
        else:
            model_path = (artifact_dir / default_onnx_file_name(args.model_name)).resolve()
    if not model_path.exists():
        raise FileNotFoundError(f"Model file not found: {model_path}")

    predict, model_kind = build_predictor(model_path)
    env = ABCorridorVisionEnv(
        base_url=args.base_url,
        scenario_path=args.scenario,
        max_steps=args.max_steps,
        oob_margin_m=0.10,
        time_scale=1.0,
        img_size=args.img_size,
    )

    episodes: list[dict[str, Any]] = []
    for episode_index in range(args.episodes):
        obs, info = env.reset(seed=args.seed_offset + episode_index)
        total_reward = 0.0
        steps = 0
        done = False

        while not done:
            action = predict(obs)
            obs, reward, terminated, truncated, info = env.step(action)
            total_reward += reward
            steps += 1
            done = terminated or truncated

        termination = info.get("termination_reason", "timeout")
        if truncated and termination == "running":
            termination = "timeout"

        result = {
            "episode": episode_index,
            "seed": args.seed_offset + episode_index,
            "steps": steps,
            "reward": round(total_reward, 3),
            "progress": round(float(info.get("progress", 0.0)), 4),
            "lateralDistance": round(float(info.get("lateral_distance", 0.0)), 4),
            "termination": termination,
            "position": info.get("position"),
        }
        episodes.append(result)
        print(
            f"ep={episode_index:02d} steps={steps:03d} reward={total_reward:7.2f} "
            f"progress={result['progress']:.2%} termination={result['termination']}",
            flush=True,
        )

    success_count = sum(1 for item in episodes if item["termination"] == "goal_reached")
    summary = {
        "kind": "cardboard-corridor-eval",
        "modelName": args.model_name,
        "modelVersion": args.model_version,
        "modelKind": model_kind,
        "modelPath": str(model_path),
        "scenario": str(Path(args.scenario).resolve()),
        "episodes": args.episodes,
        "successCount": success_count,
        "successRate": round(success_count / max(args.episodes, 1), 4),
        "avgReward": round(float(np.mean([item["reward"] for item in episodes])), 3),
        "avgProgress": round(float(np.mean([item["progress"] for item in episodes])), 4),
        "avgSteps": round(float(np.mean([item["steps"] for item in episodes])), 2),
        "terminationCounts": {
            key: sum(1 for item in episodes if item["termination"] == key)
            for key in sorted({item["termination"] for item in episodes})
        },
        "episodeData": episodes,
    }

    if args.output_json:
        output_path = Path(args.output_json)
        output_path.parent.mkdir(parents=True, exist_ok=True)
        output_path.write_text(json.dumps(summary, indent=2, ensure_ascii=False), encoding="utf-8")
        print(f"saved-json {output_path}")

    print(json.dumps(summary, ensure_ascii=False, indent=2))
    return summary


def main() -> int:
    evaluate(parse_args())
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
