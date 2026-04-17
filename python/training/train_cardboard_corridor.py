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
from stable_baselines3.common.vec_env import DummyVecEnv, SubprocVecEnv, VecMonitor

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
    p.add_argument("--maze-randomize", action="store_true",
                   help="Randomize maze params each episode (for track.cardboard_maze.v1)")
    p.add_argument("--maze-regen-every", type=int, default=1,
                   help="Regenerate maze only every N resets (default 1 = every reset). "
                        "Higher values let the robot train multiple episodes on the same maze.")
    p.add_argument("--aruco-goal", action="store_true",
                   help="Enable ArUco marker as parallel goal signal (+20 bonus if detected)")
    p.add_argument("--aruco-distance-m", type=float, default=0.50,
                   help="Distance threshold for ArUco goal detection (meters)")
    p.add_argument(
        "--device",
        default="auto",
        help="PyTorch device: 'auto' (default), 'cpu', 'cuda', 'cuda:0', 'mps'",
    )
    p.add_argument(
        "--num-envs",
        type=int,
        default=1,
        help="Number of parallel envs (vectorized training). Each env needs its own "
             "Unity runtime on consecutive ports starting from --base-url port.",
    )
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


def _parse_base_port(base_url: str) -> tuple[str, int]:
    """Extract (scheme+host, port) from base_url like 'http://127.0.0.1:8000'."""
    from urllib.parse import urlparse
    parsed = urlparse(base_url)
    port = parsed.port or 8000
    scheme_host = f"{parsed.scheme}://{parsed.hostname}"
    return scheme_host, port


def _make_env(
    base_url: str,
    scenario_path: str,
    max_ep_steps: int,
    time_scale: float,
    img_size: int,
    rank: int,
    seed: int,
    maze_randomize: bool = False,
    aruco_goal: bool = False,
    aruco_goal_distance_m: float = 0.50,
    maze_regen_every: int = 1,
):
    """Factory for creating a single env (used by SubprocVecEnv)."""
    def _init():
        env = ABCorridorVisionEnv(
            base_url=base_url,
            scenario_path=scenario_path,
            max_steps=max_ep_steps,
            oob_margin_m=0.10,
            time_scale=time_scale,
            img_size=img_size,
            maze_randomize=maze_randomize,
            aruco_goal=aruco_goal,
            aruco_goal_distance_m=aruco_goal_distance_m,
            maze_regen_every=maze_regen_every,
        )
        env.reset(seed=seed + rank)
        return env
    return _init


def main() -> int:
    args = parse_args()
    output_dir = resolve_artifact_dir(ROOT, args.output_dir, args.model_name, args.model_version)
    log_dir = Path(args.log_dir)
    output_dir.mkdir(parents=True, exist_ok=True)
    log_dir.mkdir(parents=True, exist_ok=True)

    num_envs = max(1, args.num_envs)
    device_str = args.device

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
    print(f"  device:          {device_str}")
    print(f"  num_envs:        {num_envs}")
    print()

    # Probe env (always single, for printing info and ONNX export)
    probe_env = ABCorridorVisionEnv(
        base_url=args.base_url,
        scenario_path=args.scenario,
        max_steps=args.max_ep_steps,
        oob_margin_m=0.10,
        time_scale=args.time_scale,
        img_size=args.img_size,
        maze_randomize=args.maze_randomize,
        aruco_goal=args.aruco_goal,
        aruco_goal_distance_m=args.aruco_distance_m,
        maze_regen_every=args.maze_regen_every,
    )
    print(f"  track:           {probe_env._reset_config['selectedTrackId']}")
    print(f"  corridor_width:  {probe_env.corridor_width_m:.2f}m")
    print(f"  goal_radius:     {probe_env.goal_radius_m:.2f}m")
    print(f"  waypoint_radius: {probe_env.waypoint_reach_radius_m:.2f}m")
    print(f"  waypoints:       {probe_env.waypoints}")
    print()

    # Build training env: single or vectorized
    if num_envs == 1:
        train_env = Monitor(probe_env, filename=str(log_dir / "train_cardboard_monitor"))
    else:
        scheme_host, base_port = _parse_base_port(args.base_url)
        env_urls = [f"{scheme_host}:{base_port + i}" for i in range(num_envs)]
        print(f"  Vectorized envs ({num_envs}):")
        for i, url in enumerate(env_urls):
            print(f"    env[{i}]: {url}")
        print()
        vec_env = SubprocVecEnv([
            _make_env(
                base_url=env_urls[i],
                scenario_path=args.scenario,
                max_ep_steps=args.max_ep_steps,
                time_scale=args.time_scale,
                img_size=args.img_size,
                rank=i,
                seed=args.seed,
                maze_randomize=args.maze_randomize,
                aruco_goal=args.aruco_goal,
                aruco_goal_distance_m=args.aruco_distance_m,
                maze_regen_every=args.maze_regen_every,
            )
            for i in range(num_envs)
        ])
        train_env = VecMonitor(vec_env, filename=str(log_dir / "train_cardboard_monitor"))

    # PPO model
    if args.resume:
        print(f"Resuming from checkpoint: {args.resume}")
        model = PPO.load(args.resume, env=train_env, device=device_str)
    else:
        print(f"Creating new PPO model with MultiInputPolicy (device={device_str})...")
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
            device=device_str,
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
            export_to_onnx(model, onnx_path, probe_env)
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
            "track": probe_env._reset_config["selectedTrackId"],
            "algorithm": "PPO",
            "framework": "stable-baselines3",
            "totalTimesteps": args.total_timesteps,
            "trainTimeSeconds": round(train_time, 1),
            "corridorWidthM": probe_env.corridor_width_m,
            "goalRadiusM": probe_env.goal_radius_m,
            "waypoints": probe_env.waypoints,
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
        maze_randomize=args.maze_randomize,
        aruco_goal=args.aruco_goal,
        aruco_goal_distance_m=args.aruco_distance_m,
        maze_regen_every=args.maze_regen_every,
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
