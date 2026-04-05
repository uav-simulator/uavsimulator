#!/usr/bin/env python3
"""Train PPO policy with CNN vision for A->B corridor navigation.

Uses ABCorridorVisionEnv:
  observation = Dict{"image": (84,84,3) uint8, "ultrasonic": (1,) float32}
  action = Box[-1,1] shape (2,)  — throttle, steer

SB3's MultiInputPolicy automatically applies NatureCNN to the image key
and a small MLP to the ultrasonic key, then combines them.
"""

from __future__ import annotations

import argparse
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

from training.ab_corridor_vision_env import ABCorridorVisionEnv

DEFAULT_ARTIFACT_DIR = ROOT / "python/training/artifacts/ab_corridor_cnn_ppo_v1"
DEFAULT_LOG_DIR = ROOT / "python/training/logs"


def parse_args() -> argparse.Namespace:
    p = argparse.ArgumentParser(description="Train CNN-PPO on A->B corridor (vision)")
    p.add_argument("--base-url", default="http://127.0.0.1:8000")
    p.add_argument("--total-timesteps", type=int, default=100_000)
    p.add_argument("--max-ep-steps", type=int, default=600)
    p.add_argument("--time-scale", type=float, default=3.0)
    p.add_argument("--output-dir", default=str(DEFAULT_ARTIFACT_DIR))
    p.add_argument("--log-dir", default=str(DEFAULT_LOG_DIR))
    p.add_argument("--checkpoint-freq", type=int, default=5000)
    p.add_argument("--learning-rate", type=float, default=3e-4)
    p.add_argument("--n-steps", type=int, default=256)
    p.add_argument("--batch-size", type=int, default=64)
    p.add_argument("--n-epochs", type=int, default=5)
    p.add_argument("--gamma", type=float, default=0.99)
    p.add_argument("--gae-lambda", type=float, default=0.95)
    p.add_argument("--clip-range", type=float, default=0.2)
    p.add_argument("--ent-coef", type=float, default=0.01)
    p.add_argument("--seed", type=int, default=42)
    p.add_argument("--grayscale", action="store_true", help="Use grayscale images (1 channel)")
    p.add_argument("--img-size", type=int, default=84)
    p.add_argument("--no-export-onnx", action="store_true")
    return p.parse_args()


class ProgressCallback(BaseCallback):
    """Prints training progress."""

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
    """Export trained CNN policy to ONNX.

    SB3 internally transposes images from (H, W, C) to (C, H, W) via VecTransposeImage.
    The wrapper accepts (N, H, W, C) and transposes before feeding to the CNN.
    """
    policy = model.policy
    policy.eval()

    img_shape = env.observation_space["image"].shape  # (H, W, C)
    # ONNX input: (N, H, W, C) — same as raw env output
    dummy_img = torch.zeros(1, *img_shape, dtype=torch.float32)
    dummy_ultra = torch.zeros(1, 1, dtype=torch.float32)

    class VisionPolicyWrapper(torch.nn.Module):
        def __init__(self, sb3_policy):
            super().__init__()
            self.features_extractor = sb3_policy.features_extractor
            self.mlp_extractor = sb3_policy.mlp_extractor
            self.action_net = sb3_policy.action_net

        def forward(self, image: torch.Tensor, ultrasonic: torch.Tensor):
            # Transpose (N, H, W, C) → (N, C, H, W) as SB3's VecTransposeImage does
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
    output_dir = Path(args.output_dir)
    log_dir = Path(args.log_dir)
    output_dir.mkdir(parents=True, exist_ok=True)
    log_dir.mkdir(parents=True, exist_ok=True)

    print("=== A->B Corridor CNN-PPO Training (Vision) ===")
    print(f"  base_url:        {args.base_url}")
    print(f"  total_timesteps: {args.total_timesteps}")
    print(f"  time_scale:      {args.time_scale}")
    print(f"  img_size:        {args.img_size}")
    print(f"  grayscale:       {args.grayscale}")
    print(f"  output_dir:      {output_dir}")
    print()

    # Create environment
    print("Creating vision environment...")
    env = ABCorridorVisionEnv(
        base_url=args.base_url,
        max_steps=args.max_ep_steps,
        time_scale=args.time_scale,
        grayscale=args.grayscale,
        img_size=args.img_size,
    )
    train_env = Monitor(env, filename=str(log_dir / "train_vision_monitor"))

    # PPO with MultiInputPolicy (handles Dict obs: CNN for images, MLP for scalars)
    print("Creating PPO model with MultiInputPolicy...")
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
        tensorboard_log=str(log_dir / "tb_vision"),
        policy_kwargs=dict(
            net_arch=dict(pi=[128, 64], vf=[128, 64]),
        ),
    )

    # Callbacks
    callbacks = [
        ProgressCallback(args.total_timesteps),
        CheckpointCallback(
            save_freq=args.checkpoint_freq,
            save_path=str(output_dir / "checkpoints"),
            name_prefix="cnn_ppo_ab",
        ),
    ]

    # Train
    print(f"\nStarting training for {args.total_timesteps} timesteps...")
    t0 = time.time()
    model.learn(total_timesteps=args.total_timesteps, callback=callbacks, progress_bar=False)
    train_time = time.time() - t0
    print(f"\nTraining completed in {train_time:.1f}s")

    # Save
    model_path = output_dir / "cnn_ppo_ab_corridor_final"
    model.save(str(model_path))
    print(f"Saved SB3 model: {model_path}")

    # ONNX export
    onnx_path = output_dir / "ab_corridor_cnn_ppo_v1.onnx"
    if not args.no_export_onnx:
        try:
            export_to_onnx(model, onnx_path, env)
        except Exception as e:
            print(f"ONNX export failed (non-fatal): {e}")

    # Metadata
    metadata = {
        "policyId": "ab_corridor_cnn_ppo_v1",
        "format": "onnx",
        "version": "1.0.0",
        "scenario": "A->B corridor (vision)",
        "algorithm": "PPO",
        "framework": "stable-baselines3",
        "totalTimesteps": args.total_timesteps,
        "trainTimeSeconds": round(train_time, 1),
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
        "observationSchema": {
            "image": {"shape": [args.img_size, args.img_size, 1 if args.grayscale else 3], "dtype": "uint8"},
            "ultrasonic": {"shape": [1], "dtype": "float32"},
        },
        "actionSchema": {
            "size": 2,
            "outputs": ["throttle", "steer"],
            "range": [-1.0, 1.0],
        },
    }
    (output_dir / "metadata.json").write_text(json.dumps(metadata, indent=2, ensure_ascii=False))

    # Quick eval
    print("\n=== Quick evaluation (5 episodes) ===")
    eval_env = ABCorridorVisionEnv(
        base_url=args.base_url,
        max_steps=args.max_ep_steps,
        time_scale=1.0,
        grayscale=args.grayscale,
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
        results.append({"episode": ep, "steps": steps, "reward": round(total_reward, 2), "progress": round(progress, 4)})
        status = "GOAL" if progress > 0.95 else "FAIL"
        print(f"  ep={ep}: steps={steps}, reward={total_reward:.2f}, progress={progress:.2%}, {status}")

    metrics = {
        "kind": "cnn-ppo-trained",
        "evalEpisodes": results,
        "successCount": sum(1 for r in results if r["progress"] > 0.95),
        "successRate": sum(1 for r in results if r["progress"] > 0.95) / max(len(results), 1),
        "avgReward": round(np.mean([r["reward"] for r in results]), 2),
        "avgProgress": round(np.mean([r["progress"] for r in results]), 4),
    }
    (output_dir / "metrics.json").write_text(json.dumps(metrics, indent=2, ensure_ascii=False))
    print(f"\nSuccess rate: {metrics['successRate']:.0%}")
    print(f"Avg progress: {metrics['avgProgress']:.2%}")

    return 0


if __name__ == "__main__":
    raise SystemExit(main())
