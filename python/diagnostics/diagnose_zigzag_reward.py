#!/usr/bin/env python3
"""Run v7@35k on a zigzag maze, log per-step reward components to CSV.

Usage: python3 python/diagnostics/diagnose_zigzag_reward.py
Output: docs/report/prediploma-practice/evidence/zigzag_reward_breakdown.csv
"""
from __future__ import annotations

import csv
import sys
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
sys.path.insert(0, str(ROOT / "python"))

from stable_baselines3 import PPO

from training.ab_corridor_vision_env import ABCorridorVisionEnv

MODEL = ROOT / "python/training/artifacts/cardboard-maze-ppo-v7/1.0.0/cardboard-maze-ppo-v7_sb3.zip"
SCENARIO = ROOT / "configs/scenarios/cardboard-maze-v1.yaml"
OUT_CSV = ROOT / "docs/report/prediploma-practice/evidence/zigzag_reward_breakdown.csv"


def main() -> int:
    env = ABCorridorVisionEnv(
        scenario_path=str(SCENARIO),
        max_steps=400,
        time_scale=1.0,
        maze_randomize=True,
        maze_param_ranges={
            "length_cells": (6, 6),
            "left_turns": (1, 1),
            "right_turns": (1, 1),
            "corridor_width_m": (0.60, 0.60),
            "wall_height_m": (0.25, 0.25),
        },
        maze_regen_every=1,
    )
    model = PPO.load(str(MODEL), env=env, device="cpu")

    OUT_CSV.parent.mkdir(parents=True, exist_ok=True)
    with OUT_CSV.open("w", newline="") as f:
        writer = None
        ep_totals: list[float] = []
        for ep in range(3):
            obs, info = env.reset(seed=9000 + ep)
            if ep == 0:
                # Verify the layout is a true 1L+1R zigzag: print waypoints so we
                # can eyeball the turn sequence in the run log.
                print(f"ep={ep}: waypoints={env.waypoints}")
                print(f"ep={ep}: corridor_width_m={env.corridor_width_m:.3f}  goal_radius_m={env.goal_radius_m:.3f}")
            step = 0
            ep_total = 0.0
            while True:
                action, _ = model.predict(obs, deterministic=True)
                obs, reward, term, trunc, info = env.step(action)
                ep_total += float(reward)
                row = {"episode": ep, "step": step, "total": round(float(reward), 4)}
                row.update({k: round(float(v), 4) for k, v in info.get("reward_breakdown", {}).items()})
                row["termination"] = info.get("termination_reason", "")
                # Note: reward_breakdown already has "progress" (reward component).
                # We log the env's progress fraction (0..1) under a distinct name.
                row["progress_fraction"] = round(float(info.get("progress", 0.0)), 4)
                if writer is None:
                    writer = csv.DictWriter(f, fieldnames=list(row.keys()))
                    writer.writeheader()
                writer.writerow(row)
                step += 1
                if term or trunc:
                    break
            ep_totals.append(ep_total)
            print(f"ep={ep}: steps={step}  total_reward={ep_total:.2f}  termination={info.get('termination_reason','')}")

    print(f"\nEpisode totals: {[round(t, 2) for t in ep_totals]}")
    print(f"Mean: {sum(ep_totals) / len(ep_totals):.2f}")
    print(f"Wrote {OUT_CSV}")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
