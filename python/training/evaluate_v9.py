#!/usr/bin/env python3
"""Evaluate v9 (discrete-action) cardboard corridor policy against unity-sim."""

from __future__ import annotations

import argparse
import json
import sys
from pathlib import Path
from typing import Any

import numpy as np

ROOT = Path(__file__).resolve().parents[2]
PYTHON_ROOT = ROOT / "python"
if str(PYTHON_ROOT) not in sys.path:
    sys.path.insert(0, str(PYTHON_ROOT))

from training.ab_corridor_vision_env import ABCorridorVisionEnv
from training.discrete_action_wrapper import ACTION_NAMES, ACTION_TABLE, DiscreteActionWrapper
from training.latency_wrapper import DelayedActionWrapper
from training.model_artifacts import default_artifact_dir, default_onnx_file_name, default_sb3_stem

DEFAULT_SCENARIO = ROOT / "configs/scenarios/cardboard-corridor-v1.yaml"
DEFAULT_MODEL_NAME = "cardboard-corridor-ppo-v9"
DEFAULT_MODEL_VERSION = "1.0.0"


def parse_args() -> argparse.Namespace:
    p = argparse.ArgumentParser(description="Evaluate v9 discrete-action policy")
    p.add_argument("--base-url", default="http://127.0.0.1:8000")
    p.add_argument("--scenario", default=str(DEFAULT_SCENARIO))
    p.add_argument("--model", default="")
    p.add_argument("--model-name", default=DEFAULT_MODEL_NAME)
    p.add_argument("--model-version", default=DEFAULT_MODEL_VERSION)
    p.add_argument("--model-format", choices=("onnx", "ppo"), default="onnx")
    p.add_argument("--episodes", type=int, default=10)
    p.add_argument("--max-steps", type=int, default=400)
    p.add_argument("--seed-offset", type=int, default=3000)
    p.add_argument("--img-size", type=int, default=84)
    # Plan 1 (rev37): random maze evaluation support
    p.add_argument("--track-id", default="",
                   help="Override scenario track (e.g. track.cardboard_maze.v1)")
    p.add_argument("--maze-randomize", action="store_true",
                   help="Per-episode random maze geometry (only for cardboard_maze.v1)")
    p.add_argument("--latency-steps", type=int, default=0,
                   help="Apply DelayedActionWrapper with N-tick action delay (matches training)")
    # Plan 2 (rev38): frame-stack support — eval-time observation must match
    # train-time channel count. Default 1 keeps back-compat with rev16-rev37.
    p.add_argument("--frame-stack", type=int, default=1,
                   help="Number of frames to stack channel-wise (must match training)")
    p.add_argument("--output-json", default="")
    return p.parse_args()


def build_discrete_predictor(model_path: Path):
    suffix = model_path.suffix.lower()
    if suffix == ".onnx":
        import onnxruntime as ort

        session = ort.InferenceSession(str(model_path), providers=["CPUExecutionProvider"])
        input_names = [item.name for item in session.get_inputs()]
        image_name = input_names[0]
        ultrasonic_name = input_names[1]

        def predict(obs):
            image = obs["image"].astype(np.float32)[np.newaxis, ...]
            ultrasonic = obs["ultrasonic"].astype(np.float32)[np.newaxis, ...]
            logits = session.run(None, {image_name: image, ultrasonic_name: ultrasonic})[0][0]
            return int(np.argmax(logits))

        return predict, "onnx"

    if suffix == ".zip":
        from stable_baselines3 import PPO
        # Plan 5: try PPO first; if it fails, try RecurrentPPO (sb3-contrib).
        model = None
        kind = "ppo"
        try:
            model = PPO.load(str(model_path))
        except (KeyError, RuntimeError, AttributeError, ValueError):
            try:
                from sb3_contrib import RecurrentPPO
                model = RecurrentPPO.load(str(model_path))
                kind = "recurrent_ppo"
            except Exception as e:
                raise RuntimeError(
                    f"Failed to load checkpoint as PPO or RecurrentPPO: {e}"
                )

        if kind == "recurrent_ppo":
            # RecurrentPPO carries LSTM hidden state across steps. Reset on
            # each new episode by external call to predict.reset().
            class _RecurrentPredictor:
                def __init__(self, m):
                    self.model = m
                    self.lstm_states = None
                    self.episode_start = True
                def __call__(self, obs):
                    action, self.lstm_states = self.model.predict(
                        obs, state=self.lstm_states,
                        episode_start=np.array([self.episode_start]),
                        deterministic=True,
                    )
                    self.episode_start = False
                    return int(action)
                def reset(self):
                    self.lstm_states = None
                    self.episode_start = True
            return _RecurrentPredictor(model), kind
        else:
            def predict(obs):
                action, _ = model.predict(obs, deterministic=True)
                return int(action)
            return predict, kind

    raise ValueError(f"Unsupported model format: {model_path}")


def evaluate(args):
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

    predict, model_kind = build_discrete_predictor(model_path)

    base_env = ABCorridorVisionEnv(
        base_url=args.base_url,
        scenario_path=args.scenario,
        max_steps=args.max_steps,
        oob_margin_m=0.10,
        time_scale=1.0,
        img_size=args.img_size,
        track_id=args.track_id if args.track_id else None,
        maze_randomize=args.maze_randomize,
    )
    if args.track_id:
        print(f"  Track override: {args.track_id} (maze_randomize={args.maze_randomize})")
    # Wrap env in discrete adapter so we can env.step(int)
    env = DiscreteActionWrapper(base_env)
    if args.latency_steps > 0:
        env = DelayedActionWrapper(env, delay_steps=args.latency_steps)
        print(f"  DelayedActionWrapper enabled: delay_steps={args.latency_steps}")

    # Plan 2 (rev38): frame stacking buffer — concat last k frames channel-wise
    # for image AND last k ultrasonic readings concat-axis-0 for sonar.
    # SB3's VecFrameStack does this by default for *all* dict obs keys, so
    # eval-time policy.predict expects matching shapes.
    from collections import deque
    frame_stack_k = max(1, int(args.frame_stack))
    image_buffer: deque | None = deque(maxlen=frame_stack_k) if frame_stack_k > 1 else None
    ultra_buffer: deque | None = deque(maxlen=frame_stack_k) if frame_stack_k > 1 else None

    def stack_obs(raw_obs):
        """Apply frame stacking to obs.image and obs.ultrasonic if k>1; passthrough otherwise."""
        if image_buffer is None:
            return raw_obs
        stacked_image = np.concatenate(list(image_buffer), axis=-1)
        stacked_ultra = np.concatenate(list(ultra_buffer), axis=-1).astype(np.float32)
        return {"image": stacked_image, "ultrasonic": stacked_ultra}

    action_counts = {name: 0 for name in ACTION_NAMES}
    episodes: list[dict[str, Any]] = []
    if frame_stack_k > 1:
        print(f"  Frame stacking k={frame_stack_k} -> obs image (84, 84, {3*frame_stack_k}) + ultrasonic ({frame_stack_k},)")

    for ep_idx in range(args.episodes):
        obs, info = env.reset(seed=args.seed_offset + ep_idx)
        # Plan 5: reset LSTM hidden state for recurrent predictors at episode start.
        if hasattr(predict, "reset"):
            predict.reset()
        if image_buffer is not None:
            image_buffer.clear()
            ultra_buffer.clear()
            for _ in range(frame_stack_k):
                image_buffer.append(obs["image"])
                ultra_buffer.append(obs["ultrasonic"])
        total_reward = 0.0
        steps = 0
        ep_actions: list[int] = []
        done = False
        while not done:
            action_idx = predict(stack_obs(obs))
            ep_actions.append(action_idx)
            action_counts[ACTION_NAMES[action_idx]] += 1
            obs, reward, terminated, truncated, info = env.step(action_idx)
            if image_buffer is not None:
                image_buffer.append(obs["image"])
                ultra_buffer.append(obs["ultrasonic"])
            total_reward += reward
            steps += 1
            done = terminated or truncated

        termination = info.get("termination_reason", "timeout")
        if truncated and termination == "running":
            termination = "timeout"

        unique_actions = len(set(ep_actions))
        most_common_action = max(set(ep_actions), key=ep_actions.count) if ep_actions else -1
        most_common_pct = (
            ep_actions.count(most_common_action) / len(ep_actions) if ep_actions else 0.0
        )

        result = {
            "episode": ep_idx,
            "seed": args.seed_offset + ep_idx,
            "steps": steps,
            "reward": round(total_reward, 3),
            "progress": round(float(info.get("progress", 0.0)), 4),
            "termination": termination,
            "uniqueActions": unique_actions,
            "mostCommonAction": ACTION_NAMES[most_common_action] if most_common_action >= 0 else None,
            "mostCommonPct": round(most_common_pct, 3),
        }
        episodes.append(result)
        print(
            f"ep={ep_idx:02d} steps={steps:03d} reward={total_reward:7.2f} "
            f"progress={result['progress']:.2%} term={termination:>14} "
            f"common={result['mostCommonAction']}({result['mostCommonPct']:.0%})",
            flush=True,
        )

    success_count = sum(1 for e in episodes if e["termination"] == "goal_reached")
    summary = {
        "kind": "cardboard-corridor-v9-eval",
        "modelName": args.model_name,
        "modelVersion": args.model_version,
        "modelKind": model_kind,
        "modelPath": str(model_path),
        "episodes": args.episodes,
        "successCount": success_count,
        "successRate": round(success_count / max(args.episodes, 1), 4),
        "avgReward": round(float(np.mean([e["reward"] for e in episodes])), 3),
        "avgProgress": round(float(np.mean([e["progress"] for e in episodes])), 4),
        "avgSteps": round(float(np.mean([e["steps"] for e in episodes])), 2),
        "actionCounts": action_counts,
        "actionDistribution": {
            name: round(action_counts[name] / max(sum(action_counts.values()), 1), 3)
            for name in ACTION_NAMES
        },
        "terminationCounts": {
            key: sum(1 for e in episodes if e["termination"] == key)
            for key in sorted({e["termination"] for e in episodes})
        },
        "episodeData": episodes,
    }

    if args.output_json:
        out = Path(args.output_json)
        out.parent.mkdir(parents=True, exist_ok=True)
        out.write_text(json.dumps(summary, indent=2, ensure_ascii=False), encoding="utf-8")
        print(f"saved-json {out}")

    print(json.dumps(summary, ensure_ascii=False, indent=2))
    return summary


def main() -> int:
    evaluate(parse_args())
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
