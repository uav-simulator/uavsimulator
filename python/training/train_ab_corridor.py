#!/usr/bin/env python3
"""Train PPO policy for A->B corridor navigation using stable-baselines3."""

from __future__ import annotations

import argparse
import json
import sys
import time
from pathlib import Path

import torch

ROOT = Path(__file__).resolve().parents[2]
PYTHON_ROOT = ROOT / "python"
if str(PYTHON_ROOT) not in sys.path:
    sys.path.insert(0, str(PYTHON_ROOT))

from stable_baselines3 import PPO
from stable_baselines3.common.callbacks import (
    BaseCallback,
    CheckpointCallback,
)
from stable_baselines3.common.monitor import Monitor
from stable_baselines3.common.vec_env import VecMonitor

from training.ab_corridor_env import ABCorridorEnv
from training.multi_agent_ab_corridor_env import ABCorridorMultiAgentVecEnv

DEFAULT_ARTIFACT_DIR = ROOT / "python/training/artifacts/ab_corridor_ppo_v2"
DEFAULT_LOG_DIR = ROOT / "python/training/logs"
DEFAULT_SCENARIO = str(ROOT / "configs/scenarios/ab-corridor-v1.yaml")


def parse_args() -> argparse.Namespace:
    parser = argparse.ArgumentParser(description="Train PPO on A->B corridor")
    parser.add_argument("--base-url", default="http://127.0.0.1:8000")
    parser.add_argument("--scenario", default=DEFAULT_SCENARIO)
    parser.add_argument("--n-agents", type=int, default=4,
                        help="Number of parallel agents (1=single, >1=multi-agent VecEnv)")
    parser.add_argument("--total-timesteps", type=int, default=200_000)
    parser.add_argument("--max-ep-steps", type=int, default=400)
    parser.add_argument("--time-scale", type=float, default=2.0)
    parser.add_argument("--output-dir", default=str(DEFAULT_ARTIFACT_DIR))
    parser.add_argument("--log-dir", default=str(DEFAULT_LOG_DIR))
    parser.add_argument("--checkpoint-freq", type=int, default=5000)
    parser.add_argument("--eval-freq", type=int, default=5000)
    parser.add_argument("--eval-episodes", type=int, default=5)
    parser.add_argument("--learning-rate", type=float, default=3e-4)
    parser.add_argument("--n-steps", type=int, default=1024)
    parser.add_argument("--batch-size", type=int, default=256)
    parser.add_argument("--n-epochs", type=int, default=10)
    parser.add_argument("--gamma", type=float, default=0.99)
    parser.add_argument("--gae-lambda", type=float, default=0.95)
    parser.add_argument("--clip-range", type=float, default=0.2)
    parser.add_argument("--ent-coef", type=float, default=0.02)
    parser.add_argument("--seed", type=int, default=42)
    parser.add_argument("--no-export-onnx", action="store_true")
    return parser.parse_args()


class ProgressCallback(BaseCallback):
    """Prints training progress to stdout."""

    def __init__(self, total_timesteps: int, verbose: int = 0):
        super().__init__(verbose)
        self.total_timesteps = total_timesteps
        self._last_print = 0
        self._start_time = time.time()

    def _on_step(self) -> bool:
        if self.num_timesteps - self._last_print >= 1000:
            elapsed = time.time() - self._start_time
            pct = self.num_timesteps / self.total_timesteps * 100
            fps = self.num_timesteps / max(elapsed, 1)
            print(
                f"  [{pct:5.1f}%] steps={self.num_timesteps}/{self.total_timesteps}  "
                f"elapsed={elapsed:.0f}s  fps={fps:.1f}",
                flush=True,
            )
            self._last_print = self.num_timesteps
        return True


def export_to_onnx(model: PPO, output_path: Path, obs_size: int = 8) -> None:
    """Export the trained policy network to ONNX format."""
    policy = model.policy
    policy.eval()

    dummy_obs = torch.zeros(1, obs_size, dtype=torch.float32)

    class PolicyWrapper(torch.nn.Module):
        def __init__(self, sb3_policy):
            super().__init__()
            self.features_extractor = sb3_policy.features_extractor
            self.mlp_extractor = sb3_policy.mlp_extractor
            self.action_net = sb3_policy.action_net

        def forward(self, obs):
            features = self.features_extractor(obs)
            latent_pi, _ = self.mlp_extractor(features)
            mean_actions = self.action_net(latent_pi)
            return torch.tanh(mean_actions)

    wrapper = PolicyWrapper(policy)
    wrapper.eval()

    output_path.parent.mkdir(parents=True, exist_ok=True)
    torch.onnx.export(
        wrapper,
        dummy_obs,
        str(output_path),
        input_names=["obs"],
        output_names=["action"],
        external_data=False,
        dynamic_axes={"obs": {0: "batch"}, "action": {0: "batch"}},
        opset_version=11,
    )
    print(f"Exported ONNX model: {output_path}")


def main() -> int:
    args = parse_args()
    output_dir = Path(args.output_dir)
    log_dir = Path(args.log_dir)
    output_dir.mkdir(parents=True, exist_ok=True)
    log_dir.mkdir(parents=True, exist_ok=True)

    print("=== A->B Corridor PPO Training ===")
    print(f"  base_url:        {args.base_url}")
    print(f"  n_agents:        {args.n_agents}")
    print(f"  total_timesteps: {args.total_timesteps}")
    print(f"  time_scale:      {args.time_scale}")
    print(f"  output_dir:      {output_dir}")
    print()

    # Create training environment
    print("Creating training environment...")
    if args.n_agents > 1:
        raw_env = ABCorridorMultiAgentVecEnv(
            n_agents=args.n_agents,
            base_url=args.base_url,
            max_steps=args.max_ep_steps,
            time_scale=args.time_scale,
        )
        train_env = VecMonitor(raw_env, str(log_dir / "train_monitor"))
        print(f"  Multi-agent VecEnv: {args.n_agents} agents")
    else:
        train_env = Monitor(
            ABCorridorEnv(
                base_url=args.base_url,
                scenario_path=args.scenario,
                max_steps=args.max_ep_steps,
                time_scale=args.time_scale,
            ),
            filename=str(log_dir / "train_monitor"),
        )

    # Create PPO model
    print("Creating PPO model...")
    model = PPO(
        "MlpPolicy",
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
        tensorboard_log=str(log_dir / "tb"),
        policy_kwargs=dict(
            net_arch=dict(pi=[64, 64], vf=[64, 64]),
        ),
    )

    # Callbacks
    callbacks = [
        ProgressCallback(args.total_timesteps),
        CheckpointCallback(
            save_freq=args.checkpoint_freq,
            save_path=str(output_dir / "checkpoints"),
            name_prefix="ppo_ab",
        ),
    ]

    # Train
    print(f"\nStarting training for {args.total_timesteps} timesteps...")
    start_time = time.time()
    model.learn(
        total_timesteps=args.total_timesteps,
        callback=callbacks,
        progress_bar=False,
    )
    train_time = time.time() - start_time
    print(f"\nTraining completed in {train_time:.1f}s")

    # Save model
    model_save_path = output_dir / "ppo_ab_corridor_final"
    model.save(str(model_save_path))
    print(f"Saved SB3 model: {model_save_path}")

    # Export to ONNX
    onnx_path = output_dir / "ab_corridor_ppo_v2.onnx"
    if not args.no_export_onnx:
        export_to_onnx(model, onnx_path)

    # Save training metadata
    metadata = {
        "policyId": "ab_corridor_ppo_v2",
        "format": "onnx",
        "version": "2.0.0",
        "scenario": "A->B corridor",
        "runtime": "backend-pc",
        "algorithm": "PPO",
        "framework": "stable-baselines3",
        "totalTimesteps": args.total_timesteps,
        "trainTimeSeconds": round(train_time, 1),
        "hyperparameters": {
            "nAgents": args.n_agents,
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
            "size": 8,
            "features": [
                "line_tracker.s1_norm",
                "line_tracker.s2_norm",
                "line_tracker.s3_norm",
                "line_tracker.s4_norm",
                "line_tracker.s5_norm",
                "ultrasonic.front_norm",
                "speed_norm",
                "heading_error_norm",
            ],
        },
        "actionSchema": {
            "size": 2,
            "outputs": ["throttle", "steer"],
            "range": [-1.0, 1.0],
        },
    }
    metadata_path = output_dir / "metadata.json"
    metadata_path.write_text(json.dumps(metadata, indent=2, ensure_ascii=False))
    print(f"Saved metadata: {metadata_path}")

    # Quick eval
    print("\n=== Quick evaluation (5 episodes) ===")
    env = ABCorridorEnv(
        base_url=args.base_url,
        scenario_path=args.scenario,
        max_steps=args.max_ep_steps,
        time_scale=1.0,
    )
    results = []
    for ep in range(5):
        obs, info = env.reset(seed=1000 + ep)
        total_reward = 0.0
        done = False
        steps = 0
        while not done:
            action, _ = model.predict(obs, deterministic=True)
            obs, reward, terminated, truncated, info = env.step(action)
            total_reward += reward
            done = terminated or truncated
            steps += 1
        results.append({
            "episode": ep,
            "steps": steps,
            "reward": round(total_reward, 2),
            "progress": round(info.get("progress", 0.0), 4),
            "terminated": terminated,
        })
        print(f"  ep={ep}: steps={steps}, reward={total_reward:.2f}, "
              f"progress={info.get('progress', 0):.2%}, "
              f"{'GOAL' if info.get('progress', 0) > 0.95 else 'FAIL'}")

    metrics = {
        "kind": "ppo-trained",
        "evalEpisodes": results,
        "successCount": sum(1 for r in results if r["progress"] > 0.95),
        "successRate": sum(1 for r in results if r["progress"] > 0.95) / len(results),
        "avgReward": round(sum(r["reward"] for r in results) / len(results), 2),
        "avgProgress": round(sum(r["progress"] for r in results) / len(results), 4),
    }
    metrics_path = output_dir / "metrics.json"
    metrics_path.write_text(json.dumps(metrics, indent=2, ensure_ascii=False))
    print(f"\nSaved metrics: {metrics_path}")
    print(f"Success rate: {metrics['successRate']:.0%}")

    return 0


if __name__ == "__main__":
    raise SystemExit(main())
