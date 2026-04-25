"""Tests for DelayedActionWrapper."""

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

from training.latency_wrapper import DelayedActionWrapper  # noqa: E402


class _RecordingDiscreteEnv(gym.Env):
    def __init__(self):
        self.action_space = spaces.Discrete(5)
        self.observation_space = spaces.Box(0, 1, (4,), dtype=np.float32)
        self.executed: list = []

    def reset(self, *, seed=None, options=None):
        self.executed = []
        return np.zeros(4, dtype=np.float32), {}

    def step(self, action):
        self.executed.append(int(action))
        return np.zeros(4, dtype=np.float32), 0.0, False, False, {}


class _RecordingBoxEnv(gym.Env):
    def __init__(self):
        self.action_space = spaces.Box(-1, 1, (2,), dtype=np.float32)
        self.observation_space = spaces.Box(0, 1, (4,), dtype=np.float32)
        self.executed: list = []

    def reset(self, *, seed=None, options=None):
        self.executed = []
        return np.zeros(4, dtype=np.float32), {}

    def step(self, action):
        self.executed.append(np.asarray(action, dtype=np.float32).copy())
        return np.zeros(4, dtype=np.float32), 0.0, False, False, {}


def test_delay_zero_passthrough():
    base = _RecordingDiscreteEnv()
    env = DelayedActionWrapper(base, delay_steps=0)
    env.reset()
    for a in [1, 2, 3, 4]:
        env.step(a)
    assert base.executed == [1, 2, 3, 4]


def test_delay_one_step():
    base = _RecordingDiscreteEnv()
    env = DelayedActionWrapper(base, delay_steps=1)
    env.reset()
    for a in [1, 2, 3, 4]:
        env.step(a)
    # First step uses neutral (0), then 1, 2, 3 — last submitted (4) stays in queue
    assert base.executed == [0, 1, 2, 3]


def test_delay_two_steps():
    base = _RecordingDiscreteEnv()
    env = DelayedActionWrapper(base, delay_steps=2)
    env.reset()
    for a in [1, 2, 3, 4, 5]:
        env.step(a)
    # First 2 steps: neutral (0); then 1, 2, 3; last 2 (4, 5) still queued
    assert base.executed == [0, 0, 1, 2, 3]


def test_box_neutral_is_zeros():
    base = _RecordingBoxEnv()
    env = DelayedActionWrapper(base, delay_steps=1)
    env.reset()
    env.step(np.array([0.7, 0.0]))
    np.testing.assert_array_almost_equal(base.executed[0], np.zeros(2))


def test_reset_clears_queue():
    base = _RecordingDiscreteEnv()
    env = DelayedActionWrapper(base, delay_steps=1)
    env.reset()
    env.step(1)
    env.step(2)
    env.reset()
    env.step(3)
    # After reset, first step should be neutral again
    # base.executed got cleared in reset, so check just first new step
    assert base.executed == [0]


def test_negative_delay_rejected():
    with pytest.raises(ValueError):
        DelayedActionWrapper(_RecordingDiscreteEnv(), delay_steps=-1)
