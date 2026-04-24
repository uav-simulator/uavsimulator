"""Staged curriculum for maze training.

Sprint 2 takeaway: naive full-range maze randomization failed to converge
(v2 stuck at -42, v3 stuck at -300, v4/v5 transfer forgot catastrophically).
Stage difficulty so the agent gets positive reward from the very first episode.

Stages are keyed by cumulative training timesteps. Each stage defines the
ranges fed into ABCorridorVisionEnv._apply_maze_randomization.
"""

from __future__ import annotations

from dataclasses import dataclass
from typing import List

from stable_baselines3.common.callbacks import BaseCallback


@dataclass(frozen=True)
class CurriculumStage:
    name: str
    start_step: int
    ranges: dict


DEFAULT_STAGES: List[CurriculumStage] = [
    # Stage A: longer L-shape (5 cells = 3m path, 1 right turn) — matches v6 shape,
    # long enough (~80 steps/episode) that PPO gets stable gradient per rollout.
    CurriculumStage(
        name="stage-A-easy",
        start_step=0,
        ranges={
            "length_cells": (5, 5),
            "left_turns": (0, 0),
            "right_turns": (1, 1),
            "corridor_width_m": (0.58, 0.62),
            "wall_height_m": (0.24, 0.26),
        },
    ),
    CurriculumStage(
        name="stage-B-medium",
        start_step=25_000,
        ranges={
            "length_cells": (5, 7),
            "left_turns": (0, 1),
            "right_turns": (1, 2),
            "corridor_width_m": (0.55, 0.65),
            "wall_height_m": (0.22, 0.28),
        },
    ),
    CurriculumStage(
        name="stage-C-hard",
        start_step=60_000,
        ranges={
            "length_cells": (6, 8),
            "left_turns": (1, 2),
            "right_turns": (1, 3),
            "corridor_width_m": (0.52, 0.70),
            "wall_height_m": (0.20, 0.30),
        },
    ),
    CurriculumStage(
        name="stage-D-full",
        start_step=100_000,
        ranges={
            "length_cells": (6, 10),
            "left_turns": (1, 3),
            "right_turns": (1, 3),
            "corridor_width_m": (0.50, 0.80),
            "wall_height_m": (0.20, 0.30),
        },
    ),
]


class MazeCurriculumCallback(BaseCallback):
    """Updates maze param ranges on underlying envs as training progresses."""

    def __init__(self, stages: List[CurriculumStage] = DEFAULT_STAGES, verbose: int = 1):
        super().__init__(verbose)
        self._stages = sorted(stages, key=lambda s: s.start_step)
        self._current_idx = -1

    def _current_stage(self, step: int) -> int:
        idx = 0
        for i, st in enumerate(self._stages):
            if step >= st.start_step:
                idx = i
        return idx

    def _apply_stage(self, stage: CurriculumStage) -> None:
        vec_env = self.training_env
        applied = 0
        try:
            vec_env.env_method("set_maze_param_ranges", stage.ranges)
            applied = getattr(vec_env, "num_envs", 1)
        except Exception as e:
            if self.verbose:
                print(f"  [curriculum] env_method failed ({e}); trying attribute setter", flush=True)
            try:
                for env in vec_env.envs:
                    inner = env
                    while hasattr(inner, "env"):
                        inner = inner.env
                    if hasattr(inner, "set_maze_param_ranges"):
                        inner.set_maze_param_ranges(stage.ranges)
                        applied += 1
            except Exception as e2:
                print(f"  [curriculum] failed to apply stage: {e2}", flush=True)
                return
        if self.verbose:
            print(
                f"  [curriculum] → {stage.name} at step={self.num_timesteps} "
                f"(envs updated: {applied}) ranges={stage.ranges}",
                flush=True,
            )

    def _on_training_start(self) -> None:
        idx = self._current_stage(self.num_timesteps)
        self._current_idx = idx
        self._apply_stage(self._stages[idx])

    def _on_step(self) -> bool:
        idx = self._current_stage(self.num_timesteps)
        if idx != self._current_idx:
            self._current_idx = idx
            self._apply_stage(self._stages[idx])
        return True
