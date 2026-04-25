#!/usr/bin/env python3
"""Train v9 CNN-PPO with sim-to-real fixes from Run 1/2 lessons.

Stack of wrappers around ABCorridorVisionEnv:
1. DiscreteActionWrapper — Discrete(5) action space matching ks0223 commands
2. DelayedActionWrapper — 1-step latency to match real 7Hz inference loop
3. AntiSpinRewardWrapper — penalty for repeating same action 5+ times
4. ImageAugObservationWrapper — color/blur/jpeg augmentation for visual robustness

Output: PPO with MultiInputPolicy + Categorical(5) action distribution.
ONNX export produces (image, ultrasonic) → action_logits (1, 5).
Backend Phase 7.1 will detect this output dim and argmax it directly.

Usage:
    ./rusim server up --count 3
    caffeinate python python/training/train_cardboard_corridor_v9.py \\
        --total-timesteps 200000 --num-envs 3 \\
        --output-dir python/training/artifacts
"""

from __future__ import annotations

import argparse
import importlib.util
import json
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
from stable_baselines3.common.vec_env import SubprocVecEnv, VecMonitor

from training.ab_corridor_vision_env import ABCorridorVisionEnv
from training.anti_spin_reward import AntiSpinRewardWrapper
from training.discrete_action_wrapper import ACTION_NAMES, DiscreteActionWrapper
from training.image_aug_wrapper import ImageAugObservationWrapper
from training.latency_wrapper import DelayedActionWrapper
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
DEFAULT_MODEL_NAME = "cardboard-corridor-ppo-v9"
DEFAULT_MODEL_VERSION = "1.0.0"


def parse_args() -> argparse.Namespace:
    p = argparse.ArgumentParser(description="Train v9 with sim-to-real wrappers")
    p.add_argument("--base-url", default="http://127.0.0.1:8000")
    p.add_argument("--total-timesteps", type=int, default=200_000)
    p.add_argument("--max-ep-steps", type=int, default=400)
    p.add_argument("--time-scale", type=float, default=3.0)
    p.add_argument("--scenario", default=str(DEFAULT_SCENARIO))
    p.add_argument("--output-dir", default="")
    p.add_argument("--log-dir", default=str(DEFAULT_LOG_DIR))
    p.add_argument("--model-name", default=DEFAULT_MODEL_NAME)
    p.add_argument("--model-version", default=DEFAULT_MODEL_VERSION)
    p.add_argument("--checkpoint-freq", type=int, default=10000)
    p.add_argument("--learning-rate", type=float, default=3e-4)
    p.add_argument("--n-steps", type=int, default=256)
    p.add_argument("--batch-size", type=int, default=64)
    p.add_argument("--n-epochs", type=int, default=4)
    p.add_argument("--gamma", type=float, default=0.99)
    p.add_argument("--clip-range", type=float, default=0.2)
    p.add_argument("--ent-coef", type=float, default=0.02)
    p.add_argument("--seed", type=int, default=42)
    p.add_argument("--img-size", type=int, default=84)
    p.add_argument("--num-envs", type=int, default=1)
    p.add_argument("--device", default="auto")
    p.add_argument("--latency-steps", type=int, default=1,
                   help="Action latency in env ticks (real loop ~140ms = ~1 tick at fast train speed)")
    p.add_argument("--disable-aug", action="store_true",
                   help="Disable image augmentation (for ablation / debugging)")
    p.add_argument("--disable-anti-spin", action="store_true")
    p.add_argument("--disable-latency", action="store_true")
    return p.parse_args()


class ProgressCallback(BaseCallback):
    def __init__(self, total_timesteps: int):
        super().__init__()
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


def _parse_base_port(base_url: str) -> tuple[str, int]:
    from urllib.parse import urlparse
    parsed = urlparse(base_url)
    port = parsed.port or 8000
    return f"{parsed.scheme}://{parsed.hostname}", port


def _wrap_env(
    base_env,
    *,
    enable_aug: bool,
    enable_anti_spin: bool,
    enable_latency: bool,
    latency_steps: int,
    seed: int,
):
    """Apply v9 wrapper stack: Discrete → Latency → AntiSpin → ImageAug."""
    env = DiscreteActionWrapper(base_env)
    if enable_latency and latency_steps > 0:
        env = DelayedActionWrapper(env, delay_steps=latency_steps)
    if enable_anti_spin:
        env = AntiSpinRewardWrapper(env)
    if enable_aug:
        env = ImageAugObservationWrapper(env, enable=True, seed=seed)
    return env


def _make_env(
    base_url: str,
    scenario_path: str,
    max_ep_steps: int,
    time_scale: float,
    img_size: int,
    rank: int,
    seed: int,
    enable_aug: bool,
    enable_anti_spin: bool,
    enable_latency: bool,
    latency_steps: int,
):
    def _init():
        base_env = ABCorridorVisionEnv(
            base_url=base_url,
            scenario_path=scenario_path,
            max_steps=max_ep_steps,
            oob_margin_m=0.10,
            time_scale=time_scale,
            img_size=img_size,
        )
        wrapped = _wrap_env(
            base_env,
            enable_aug=enable_aug,
            enable_anti_spin=enable_anti_spin,
            enable_latency=enable_latency,
            latency_steps=latency_steps,
            seed=seed + rank,
        )
        wrapped.reset(seed=seed + rank)
        return wrapped
    return _init


def export_to_onnx_discrete(model: PPO, output_path: Path, img_size: int = 84) -> None:
    """Export discrete-action PPO policy to ONNX with output (1, 5) logits."""
    policy = model.policy
    policy.eval()

    dummy_img = torch.zeros(1, img_size, img_size, 3, dtype=torch.float32)
    dummy_ultra = torch.zeros(1, 1, dtype=torch.float32)

    class DiscretePolicyWrapper(torch.nn.Module):
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
            # Categorical action net outputs raw logits — no tanh
            return self.action_net(latent_pi)

    wrapper = DiscretePolicyWrapper(policy)
    wrapper.eval()

    output_path.parent.mkdir(parents=True, exist_ok=True)
    torch.onnx.export(
        wrapper,
        (dummy_img, dummy_ultra),
        str(output_path),
        input_names=["image", "ultrasonic"],
        output_names=["action_logits"],
        external_data=False,
        dynamic_axes={
            "image": {0: "batch"},
            "ultrasonic": {0: "batch"},
            "action_logits": {0: "batch"},
        },
        opset_version=11,
    )
    print(f"Exported ONNX (discrete): {output_path}")


def main() -> int:
    args = parse_args()
    output_dir = resolve_artifact_dir(ROOT, args.output_dir, args.model_name, args.model_version)
    log_dir = Path(args.log_dir)
    output_dir.mkdir(parents=True, exist_ok=True)
    log_dir.mkdir(parents=True, exist_ok=True)

    num_envs = max(1, args.num_envs)
    enable_aug = not args.disable_aug
    enable_anti_spin = not args.disable_anti_spin
    enable_latency = not args.disable_latency

    tensorboard_log = None
    if importlib.util.find_spec("tensorboard") is not None:
        tensorboard_log = str(log_dir / "tb_v9")

    print("=" * 60)
    print("  Cardboard Corridor v9 CNN-PPO Training (sim-to-real fixes)")
    print("=" * 60)
    print(f"  base_url:         {args.base_url}")
    print(f"  total_timesteps:  {args.total_timesteps}")
    print(f"  num_envs:         {num_envs}")
    print(f"  scenario:         {args.scenario}")
    print(f"  output_dir:       {output_dir}")
    print(f"  device:           {args.device}")
    print()
    print("  Wrappers stack:")
    print(f"    DiscreteAction:    enabled (Discrete(5) → ks0223 cmds)")
    print(f"    DelayedAction:     {'enabled' if enable_latency else 'disabled'} (delay={args.latency_steps})")
    print(f"    AntiSpinReward:    {'enabled' if enable_anti_spin else 'disabled'}")
    print(f"    ImageAug:          {'enabled' if enable_aug else 'disabled'}")
    print(f"  Discrete actions: {dict(enumerate(ACTION_NAMES))}")
    print()

    if num_envs == 1:
        base_env = ABCorridorVisionEnv(
            base_url=args.base_url,
            scenario_path=args.scenario,
            max_steps=args.max_ep_steps,
            oob_margin_m=0.10,
            time_scale=args.time_scale,
            img_size=args.img_size,
        )
        wrapped = _wrap_env(
            base_env,
            enable_aug=enable_aug,
            enable_anti_spin=enable_anti_spin,
            enable_latency=enable_latency,
            latency_steps=args.latency_steps,
            seed=args.seed,
        )
        train_env = Monitor(wrapped, filename=str(log_dir / "train_v9_monitor"))
        probe_track = base_env._reset_config["selectedTrackId"]
        probe_corridor_w = base_env.corridor_width_m
        probe_goal_r = base_env.goal_radius_m
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
                enable_aug=enable_aug,
                enable_anti_spin=enable_anti_spin,
                enable_latency=enable_latency,
                latency_steps=args.latency_steps,
            )
            for i in range(num_envs)
        ])
        train_env = VecMonitor(vec_env, filename=str(log_dir / "train_v9_monitor"))
        # quick probe on env 0 metadata via sync env
        probe_env = ABCorridorVisionEnv(
            base_url=env_urls[0],
            scenario_path=args.scenario,
            max_steps=args.max_ep_steps,
            oob_margin_m=0.10,
            time_scale=args.time_scale,
            img_size=args.img_size,
        )
        probe_track = probe_env._reset_config["selectedTrackId"]
        probe_corridor_w = probe_env.corridor_width_m
        probe_goal_r = probe_env.goal_radius_m
        probe_env.close() if hasattr(probe_env, "close") else None

    print(f"  track:           {probe_track}")
    print(f"  corridor_width:  {probe_corridor_w:.2f}m")
    print(f"  goal_radius:     {probe_goal_r:.2f}m")
    print()

    print(f"Creating new PPO model with MultiInputPolicy (device={args.device})...")
    model = PPO(
        "MultiInputPolicy",
        train_env,
        learning_rate=args.learning_rate,
        n_steps=args.n_steps,
        batch_size=args.batch_size,
        n_epochs=args.n_epochs,
        gamma=args.gamma,
        clip_range=args.clip_range,
        ent_coef=args.ent_coef,
        verbose=1,
        seed=args.seed,
        device=args.device,
        tensorboard_log=tensorboard_log,
        policy_kwargs=dict(
            net_arch=dict(pi=[128, 64], vf=[128, 64]),
        ),
    )
    print(f"  Action dist: {type(model.policy.action_dist).__name__}")
    assert "Categorical" in type(model.policy.action_dist).__name__, \
        f"Expected Categorical action dist for Discrete action_space, got {type(model.policy.action_dist)}"

    callbacks = [
        ProgressCallback(args.total_timesteps),
        CheckpointCallback(
            save_freq=args.checkpoint_freq,
            save_path=str(output_dir / "checkpoints"),
            name_prefix="cardboard_v9_ppo",
        ),
    ]

    print(f"\nStarting training for {args.total_timesteps} timesteps...")
    t0 = time.time()
    model.learn(total_timesteps=args.total_timesteps, callback=callbacks, progress_bar=False)
    train_time = time.time() - t0
    print(f"\nTraining completed in {train_time:.1f}s")

    # Save SB3
    model_path = output_dir / default_sb3_stem(args.model_name)
    model.save(str(model_path))
    print(f"Saved SB3 model: {model_path}")

    # ONNX export (discrete output)
    onnx_name = default_onnx_file_name(args.model_name)
    onnx_path = output_dir / onnx_name
    exported_onnx = False
    try:
        export_to_onnx_discrete(model, onnx_path, img_size=args.img_size)
        exported_onnx = True
    except Exception as e:
        print(f"ONNX export failed (non-fatal): {e}")

    # Metadata
    compatibility = build_compatibility(
        runtime_modes=["unity-sim", "real-robot"],
        vehicle_ids=["vehicle.prometeo.sport.v1", "vehicle.ks0223.arcade.blue.v1"],
        robot_kinds=["ks0223"],
    )
    metadata = build_model_metadata(
        model_name=args.model_name,
        model_version=args.model_version,
        model_source="python-rl-api",
        policy_format="onnx" if exported_onnx else "sb3",
        artifact_file_name=onnx_name if exported_onnx else f"{model_path.name}.zip",
        compatibility=compatibility,
        observation_schema={
            "image": {"shape": [args.img_size, args.img_size, 3], "dtype": "uint8"},
            "ultrasonic": {"shape": [1], "dtype": "float32"},
        },
        action_schema={
            "size": 5,
            "outputs": ACTION_NAMES,
            "type": "discrete-categorical",
            "mapping": {str(i): name for i, name in enumerate(ACTION_NAMES)},
        },
        extra={
            "scenario": str(Path(args.scenario).resolve()),
            "track": probe_track,
            "algorithm": "PPO",
            "framework": "stable-baselines3",
            "totalTimesteps": args.total_timesteps,
            "trainTimeSeconds": round(train_time, 1),
            "corridorWidthM": probe_corridor_w,
            "goalRadiusM": probe_goal_r,
            "wrappers": {
                "discreteAction": True,
                "delayedAction": enable_latency,
                "antiSpinReward": enable_anti_spin,
                "imageAug": enable_aug,
                "latencySteps": args.latency_steps,
            },
            "hyperparameters": {
                "learningRate": args.learning_rate,
                "nSteps": args.n_steps,
                "batchSize": args.batch_size,
                "nEpochs": args.n_epochs,
                "gamma": args.gamma,
                "clipRange": args.clip_range,
                "entCoef": args.ent_coef,
            },
        },
    )
    write_json(output_dir / "metadata.json", metadata)
    print(f"Saved metadata: {output_dir / 'metadata.json'}")

    return 0


if __name__ == "__main__":
    raise SystemExit(main())
