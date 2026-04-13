#!/usr/bin/env python3
"""Train CNN-PPO policy for cardboard corridor (sim-to-real).

Uses ABCorridorVisionEnv with cardboard corridor track settings:
  - track: track.cardboard_corridor.v1
  - corridor width: 0.40m (matches real cardboard walls)
  - L-shaped route: straight → 90° right turn → straight → ArUco finish
"""

from __future__ import annotations

import argparse
import importlib.util
import sys
import time
from pathlib import Path

import numpy as np
import torch

ROOT = Path(__file__).resolve().parents[2]
PYTHON_ROOT = ROOT / "python"
if str(PYTHON_ROOT) not in sys.path:
    sys.path.insert(0, str(PYTHON_ROOT))

from stable_baselines3 import PPO
from stable_baselines3.common.callbacks import BaseCallback, CheckpointCallback
from stable_baselines3.common.monitor import Monitor

from training.ab_corridor_vision_env import ABCorridorVisionEnv
from training.model_artifacts import (
    build_compatibility,
    build_model_metadata,
    default_onnx_file_name,
    default_sb3_stem,
    resolve_artifact_dir,
    write_json,
)

DEFAULT_LOG_DIR = ROOT / "python/training/logs"
DEFAULT_SCENARIO = ROOT / "configs/scenarios/cardboard-corridor-v1.yaml"
DEFAULT_MODEL_NAME = "cardboard-corridor-policy"
DEFAULT_MODEL_VERSION = "v1.0.0"
DEFAULT_MODEL_SOURCE = "python-rl-api"
DEFAULT_COMPAT_RUNTIME_MODES = "unity-sim,real-robot"
DEFAULT_COMPAT_VEHICLE_IDS = "vehicle.prometeo.sport.v1"
DEFAULT_COMPAT_ROBOT_KINDS = "ks0223"


def parse_args() -> argparse.Namespace:
    p = argparse.ArgumentParser(description="Train CNN-PPO on cardboard corridor (sim-to-real)")
    p.add_argument("--base-url", default="http://127.0.0.1:8000")
    p.add_argument("--total-timesteps", type=int, default=200_000)
    p.add_argument("--max-ep-steps", type=int, default=400)
    p.add_argument("--time-scale", type=float, default=3.0)
    p.add_argument("--scenario", default=str(DEFAULT_SCENARIO))
    p.add_argument("--output-dir", default="")
    p.add_argument("--log-dir", default=str(DEFAULT_LOG_DIR))
    p.add_argument("--model-name", default=DEFAULT_MODEL_NAME)
    p.add_argument("--model-version", default=DEFAULT_MODEL_VERSION)
    p.add_argument("--model-source", default=DEFAULT_MODEL_SOURCE)
    p.add_argument("--compat-runtime-modes", default=DEFAULT_COMPAT_RUNTIME_MODES)
    p.add_argument("--compat-vehicle-ids", default=DEFAULT_COMPAT_VEHICLE_IDS)
    p.add_argument("--compat-robot-kinds", default=DEFAULT_COMPAT_ROBOT_KINDS)
    p.add_argument("--checkpoint-freq", type=int, default=5000)
    p.add_argument("--learning-rate", type=float, default=3e-4)
    p.add_argument("--n-steps", type=int, default=256)
    p.add_argument("--batch-size", type=int, default=64)
    p.add_argument("--n-epochs", type=int, default=5)
    p.add_argument("--gamma", type=float, default=0.99)
    p.add_argument("--gae-lambda", type=float, default=0.95)
    p.add_argument("--clip-range", type=float, default=0.2)
    p.add_argument("--ent-coef", type=float, default=0.02)
    p.add_argument("--seed", type=int, default=42)
    p.add_argument("--img-size", type=int, default=84)
    p.add_argument("--resume", default="", help="Path to SB3 checkpoint .zip to resume from")
    p.add_argument("--no-export-onnx", action="store_true")
    return p.parse_args()


class ProgressCallback(BaseCallback):
    def __init__(self, total_timesteps: int, verbose: int = 0):
        super().__init__(verbose)
        self.total = total_timesteps
        self._last = 0
        self._t0 = time.time()

    def _on_step(self) -> bool:
        if self.num_timesteps - self._last >= 500:
            elapsed = time.time() - self._t0
            pct = self.num_timesteps / self.total * 100
            fps = self.num_timesteps / max(elapsed, 1)
            print(
                f"  [{pct:5.1f}%] steps={self.num_timesteps}/{self.total}  "
                f"elapsed={elapsed:.0f}s  fps={fps:.1f}",
                flush=True,
            )
            self._last = self.num_timesteps
        return True


def export_to_onnx(model: PPO, output_path: Path, env: ABCorridorVisionEnv) -> None:
    policy = model.policy
    policy.eval()

    img_shape = env.observation_space["image"].shape
    dummy_img = torch.zeros(1, *img_shape, dtype=torch.float32)
    dummy_ultra = torch.zeros(1, 1, dtype=torch.float32)

    class VisionPolicyWrapper(torch.nn.Module):
        def __init__(self, sb3_policy):
            super().__init__()
            self.features_extractor = sb3_policy.features_extractor
            self.mlp_extractor = sb3_policy.mlp_extractor
            self.action_net = sb3_policy.action_net

        def forward(self, image: torch.Tensor, ultrasonic: torch.Tensor):
            image_chw = image.permute(0, 3, 1, 2).contiguous()
            obs = {"image": image_chw, "ultrasonic": ultrasonic}
            features = self.features_extractor(obs)
            latent_pi, _ = self.mlp_extractor(features)
            return torch.tanh(self.action_net(latent_pi))

    wrapper = VisionPolicyWrapper(policy)
    wrapper.eval()

    output_path.parent.mkdir(parents=True, exist_ok=True)
    torch.onnx.export(
        wrapper,
        (dummy_img, dummy_ultra),
        str(output_path),
        input_names=["image", "ultrasonic"],
        output_names=["action"],
        external_data=False,
        dynamic_axes={
            "image": {0: "batch"},
            "ultrasonic": {0: "batch"},
            "action": {0: "batch"},
        },
        opset_version=11,
    )
    print(f"Exported ONNX model: {output_path}")


def main() -> int:
    args = parse_args()
    output_dir = resolve_artifact_dir(ROOT, args.output_dir, args.model_name, args.model_version)
    log_dir = Path(args.log_dir)
    output_dir.mkdir(parents=True, exist_ok=True)
    log_dir.mkdir(parents=True, exist_ok=True)

    tensorboard_log = None
    if importlib.util.find_spec("tensorboard") is not None:
        tensorboard_log = str(log_dir / "tb_cardboard")
    else:
        print("  tensorboard:     disabled (package not installed)")

    print("=" * 60)
    print("  Cardboard Corridor CNN-PPO Training (Sim-to-Real)")
    print("=" * 60)
    print(f"  base_url:        {args.base_url}")
    print(f"  total_timesteps: {args.total_timesteps}")
    print(f"  time_scale:      {args.time_scale}")
    print(f"  scenario:        {args.scenario}")
    print(f"  model_name:      {args.model_name}")
    print(f"  model_version:   {args.model_version}")
    print(f"  output_dir:      {output_dir}")
    print()

    env = ABCorridorVisionEnv(
        base_url=args.base_url,
        scenario_path=args.scenario,
        max_steps=args.max_ep_steps,
        oob_margin_m=0.10,
        time_scale=args.time_scale,
        img_size=args.img_size,
    )
    print(f"  track:           {env._reset_config['selectedTrackId']}")
    print(f"  corridor_width:  {env.corridor_width_m:.2f}m")
    print(f"  goal_radius:     {env.goal_radius_m:.2f}m")
    print(f"  waypoint_radius: {env.waypoint_reach_radius_m:.2f}m")
    print(f"  waypoints:       {env.waypoints}")
    print()
    train_env = Monitor(env, filename=str(log_dir / "train_cardboard_monitor"))

    # PPO model
    if args.resume:
        print(f"Resuming from checkpoint: {args.resume}")
        model = PPO.load(args.resume, env=train_env)
    else:
        print("Creating new PPO model with MultiInputPolicy...")
        model = PPO(
            "MultiInputPolicy",
            train_env,
            learning_rate=args.learning_rate,
            n_steps=args.n_steps,
            batch_size=args.batch_size,
            n_epochs=args.n_epochs,
            gamma=args.gamma,
            gae_lambda=args.gae_lambda,
            clip_range=args.clip_range,
            ent_coef=args.ent_coef,
            verbose=1,
            seed=args.seed,
            tensorboard_log=tensorboard_log,
            policy_kwargs=dict(
                net_arch=dict(pi=[128, 64], vf=[128, 64]),
            ),
        )

    callbacks = [
        ProgressCallback(args.total_timesteps),
        CheckpointCallback(
            save_freq=args.checkpoint_freq,
            save_path=str(output_dir / "checkpoints"),
            name_prefix="cardboard_cnn_ppo",
        ),
    ]

    print(f"\nStarting training for {args.total_timesteps} timesteps...")
    t0 = time.time()
    model.learn(total_timesteps=args.total_timesteps, callback=callbacks, progress_bar=False)
    train_time = time.time() - t0
    print(f"\nTraining completed in {train_time:.1f}s")

    # Save
    model_path = output_dir / default_sb3_stem(args.model_name)
    model.save(str(model_path))
    print(f"Saved SB3 model: {model_path}")

    # ONNX export
    onnx_name = default_onnx_file_name(args.model_name)
    onnx_path = output_dir / onnx_name
    exported_onnx = False
    if not args.no_export_onnx:
        try:
            export_to_onnx(model, onnx_path, env)
            exported_onnx = True
        except Exception as e:
            print(f"ONNX export failed (non-fatal): {e}")

    # Metadata
    compatibility = build_compatibility(
        runtime_modes=[item.strip() for item in args.compat_runtime_modes.split(",")],
        vehicle_ids=[item.strip() for item in args.compat_vehicle_ids.split(",")],
        robot_kinds=[item.strip() for item in args.compat_robot_kinds.split(",")],
    )
    metadata = build_model_metadata(
        model_name=args.model_name,
        model_version=args.model_version,
        model_source=args.model_source,
        policy_format="onnx" if exported_onnx else "sb3",
        artifact_file_name=onnx_name if exported_onnx else f"{model_path.name}.zip",
        compatibility=compatibility,
        observation_schema={
            "image": {"shape": [args.img_size, args.img_size, 3], "dtype": "uint8"},
            "ultrasonic": {"shape": [1], "dtype": "float32"},
        },
        action_schema={
            "size": 2,
            "outputs": ["throttle", "steer"],
            "range": [-1.0, 1.0],
        },
        extra={
            "scenario": str(Path(args.scenario).resolve()),
            "track": env._reset_config["selectedTrackId"],
            "algorithm": "PPO",
            "framework": "stable-baselines3",
            "totalTimesteps": args.total_timesteps,
            "trainTimeSeconds": round(train_time, 1),
            "corridorWidthM": env.corridor_width_m,
            "goalRadiusM": env.goal_radius_m,
            "waypoints": env.waypoints,
            "hyperparameters": {
                "learningRate": args.learning_rate,
                "nSteps": args.n_steps,
                "batchSize": args.batch_size,
                "nEpochs": args.n_epochs,
                "gamma": args.gamma,
                "gaeLambda": args.gae_lambda,
                "clipRange": args.clip_range,
                "entCoef": args.ent_coef,
            },
        },
    )
    write_json(output_dir / "metadata.json", metadata)

    # Quick eval
    print("\n=== Quick evaluation (5 episodes) ===")
    eval_env = ABCorridorVisionEnv(
        base_url=args.base_url,
        scenario_path=args.scenario,
        max_steps=args.max_ep_steps,
        oob_margin_m=0.10,
        time_scale=1.0,
        img_size=args.img_size,
    )
    results = []
    for ep in range(5):
        obs, info = eval_env.reset(seed=2000 + ep)
        total_reward = 0.0
        done = False
        steps = 0
        while not done:
            action, _ = model.predict(obs, deterministic=True)
            obs, reward, terminated, truncated, info = eval_env.step(action)
            total_reward += reward
            done = terminated or truncated
            steps += 1
        progress = info.get("progress", 0.0)
        termination = info.get("termination_reason", "timeout")
        if truncated and termination == "running":
            termination = "timeout"
        results.append({
            "episode": ep,
            "steps": steps,
            "reward": round(total_reward, 2),
            "progress": round(progress, 4),
            "termination": termination,
        })
        status = "GOAL" if termination == "goal_reached" else f"FAIL ({termination})"
        print(f"  ep={ep}: steps={steps}, reward={total_reward:.2f}, progress={progress:.2%}, {status}")

    metrics = {
        "kind": "cardboard-cnn-ppo-trained",
        "modelName": args.model_name,
        "modelVersion": args.model_version,
        "evalEpisodes": results,
        "successCount": sum(1 for r in results if r["termination"] == "goal_reached"),
        "successRate": sum(1 for r in results if r["termination"] == "goal_reached") / max(len(results), 1),
        "avgReward": round(np.mean([r["reward"] for r in results]), 2),
        "avgProgress": round(np.mean([r["progress"] for r in results]), 4),
    }
    write_json(output_dir / "metrics.json", metrics)
    print(f"\nSuccess rate: {metrics['successRate']:.0%}")
    print(f"Avg progress: {metrics['avgProgress']:.2%}")

    return 0


if __name__ == "__main__":
    raise SystemExit(main())
