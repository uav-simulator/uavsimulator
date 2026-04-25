"""Tests for AntiSpinRewardWrapper."""

from __future__ import annotations

import sys
from pathlib import Path

import gymnasium as gym
import numpy as np
import pytest
from gymnasium import spaces

PYTHON_ROOT = Path(__file__).resolve().parents[2]
if str(PYTHON_ROOT) not in sys.path:
    sys.path.insert(0, str(PYTHON_ROOT))

from training.anti_spin_reward import AntiSpinRewardWrapper  # noqa: E402


class _PassThroughEnv(gym.Env):
    """Mock that returns reward=1.0 every step."""

    def __init__(self):
        self.action_space = spaces.Discrete(5)
        self.observation_space = spaces.Box(0, 1, (4,), dtype=np.float32)

    def reset(self, *, seed=None, options=None):
        return np.zeros(4, dtype=np.float32), {}

    def step(self, action):
        return np.zeros(4, dtype=np.float32), 1.0, False, False, {}


def test_no_penalty_for_short_run():
    env = AntiSpinRewardWrapper(_PassThroughEnv(), repeat_threshold=5, repeat_penalty=0.5)
    env.reset()
    rewards = [env.step(3)[1] for _ in range(4)]  # 4 same → no penalty (threshold=5)
    # No penalty until streak hits threshold
    assert all(r == 1.0 for r in rewards), rewards


def test_penalty_kicks_in_at_threshold():
    env = AntiSpinRewardWrapper(_PassThroughEnv(), repeat_threshold=5, repeat_penalty=0.5)
    env.reset()
    rewards = [env.step(3)[1] for _ in range(7)]
    # Steps 1-4: no penalty (1.0 each); steps 5-7: -0.5 penalty (0.5 each)
    assert rewards[:4] == [1.0, 1.0, 1.0, 1.0]
    assert rewards[4:] == [0.5, 0.5, 0.5]


def test_variety_bonus_after_streak():
    env = AntiSpinRewardWrapper(
        _PassThroughEnv(),
        repeat_threshold=5,
        repeat_penalty=0.5,
        variety_bonus=0.2,
        variety_after_streak=3,
    )
    env.reset()
    # 4 of action 3, then switch to action 1
    for _ in range(4):
        env.step(3)
    _, r_change, _, _, info = env.step(1)
    # streak was 4 (>= 3), so variety bonus applies
    assert r_change == 1.0 + 0.2
    assert info["anti_spin"]["variety_bonus"] == pytest.approx(0.2)


def test_no_variety_bonus_below_streak_threshold():
    env = AntiSpinRewardWrapper(
        _PassThroughEnv(),
        repeat_threshold=5,
        repeat_penalty=0.5,
        variety_bonus=0.2,
        variety_after_streak=3,
    )
    env.reset()
    env.step(3)  # streak=1
    _, r, _, _, _ = env.step(1)  # streak was 1 < 3, no bonus
    assert r == 1.0


def test_reset_clears_state():
    env = AntiSpinRewardWrapper(_PassThroughEnv(), repeat_threshold=5)
    env.reset()
    for _ in range(6):
        env.step(3)  # build up streak with penalty
    env.reset()
    rewards = [env.step(3)[1] for _ in range(4)]
    # streak must reset; no penalties for first 4
    assert all(r == 1.0 for r in rewards)


def test_works_with_continuous_action_tuple():
    """Discrete-wrapped env passes int. But raw env may pass (throttle, steer)."""
    env = AntiSpinRewardWrapper(_PassThroughEnv(), repeat_threshold=3, repeat_penalty=0.5)
    env.reset()
    rewards = [env.step(np.array([0.7, 0.0]))[1] for _ in range(5)]
    # streak should still detect repeated tuples
    # First 2: no penalty; 3rd onwards: -0.5
    assert rewards[0] == 1.0
    assert rewards[1] == 1.0
    assert rewards[2] == 0.5
    assert rewards[3] == 0.5
    assert rewards[4] == 0.5
