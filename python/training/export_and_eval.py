#!/usr/bin/env python3
"""Export saved PPO model to ONNX and run evaluation."""

from __future__ import annotations

import json
import sys
from pathlib import Path

import numpy as np
import torch

ROOT = Path(__file__).resolve().parents[2]
PYTHON_ROOT = ROOT / "python"
if str(PYTHON_ROOT) not in sys.path:
    sys.path.insert(0, str(PYTHON_ROOT))

from stable_baselines3 import PPO
from training.ab_corridor_env import ABCorridorEnv

ARTIFACT_DIR = ROOT / "python/training/artifacts/ab_corridor_ppo_v1"
MODEL_PATH = ARTIFACT_DIR / "ppo_ab_corridor_final.zip"
ONNX_PATH = ARTIFACT_DIR / "ab_corridor_ppo_v1.onnx"
SCENARIO = str(ROOT / "configs/scenarios/ab-corridor-v1.yaml")


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


def export_onnx():
    print("Loading saved model...")
    model = PPO.load(str(MODEL_PATH))
    policy = model.policy
    policy.eval()

    wrapper = PolicyWrapper(policy)
    wrapper.eval()

    dummy = torch.zeros(1, 8, dtype=torch.float32)
    ONNX_PATH.parent.mkdir(parents=True, exist_ok=True)

    torch.onnx.export(
        wrapper,
        dummy,
        str(ONNX_PATH),
        input_names=["obs"],
        output_names=["action"],
        dynamic_axes={"obs": {0: "batch"}, "action": {0: "batch"}},
        opset_version=11,
    )
    print(f"Exported ONNX: {ONNX_PATH}")
    return model


def evaluate(model: PPO, episodes: int = 20):
    print(f"\n=== Evaluation ({episodes} episodes) ===")
    env = ABCorridorEnv(
        scenario_path=SCENARIO,
        max_steps=300,
        time_scale=1.0,
    )

    results = []
    for ep in range(episodes):
        obs, info = env.reset(seed=1000 + ep)
        total_reward = 0.0
        done = False
        steps = 0
        terminated = False
        while not done:
            action, _ = model.predict(obs, deterministic=True)
            obs, reward, terminated, truncated, info = env.step(action)
            total_reward += reward
            done = terminated or truncated
            steps += 1

        progress = info.get("progress", 0.0)
        goal_reached = progress > 0.95
        termination = "goal_reached" if goal_reached else ("timeout" if not terminated else "out_of_bounds")

        results.append({
            "episode": ep,
            "steps": steps,
            "reward": round(total_reward, 2),
            "progress": round(progress, 4),
            "termination": termination,
        })
        status = "GOAL" if goal_reached else f"FAIL ({termination})"
        print(f"  ep={ep:2d}: steps={steps:3d}, reward={total_reward:7.2f}, "
              f"progress={progress:6.2%}, {status}")

    success_count = sum(1 for r in results if r["termination"] == "goal_reached")
    success_rate = success_count / len(results)
    avg_reward = sum(r["reward"] for r in results) / len(results)
    avg_progress = sum(r["progress"] for r in results) / len(results)
    avg_steps = sum(r["steps"] for r in results) / len(results)

    summary = {
        "kind": "ppo-trained",
        "episodes": episodes,
        "successCount": success_count,
        "successRate": round(success_rate, 4),
        "avgReward": round(avg_reward, 2),
        "avgProgress": round(avg_progress, 4),
        "avgSteps": round(avg_steps, 1),
        "terminationCounts": {
            "goal_reached": sum(1 for r in results if r["termination"] == "goal_reached"),
            "out_of_bounds": sum(1 for r in results if r["termination"] == "out_of_bounds"),
            "timeout": sum(1 for r in results if r["termination"] == "timeout"),
        },
        "episodesData": results,
    }

    metrics_path = ARTIFACT_DIR / "metrics.json"
    metrics_path.write_text(json.dumps(summary, indent=2, ensure_ascii=False))
    print(f"\nSaved metrics: {metrics_path}")
    print(f"\n{'='*40}")
    print(f"Success rate: {success_rate:.0%} ({success_count}/{episodes})")
    print(f"Avg reward:   {avg_reward:.2f}")
    print(f"Avg progress: {avg_progress:.2%}")
    print(f"Avg steps:    {avg_steps:.1f}")
    print(f"{'='*40}")

    return summary


def main():
    model = export_onnx()
    evaluate(model)


if __name__ == "__main__":
    main()
