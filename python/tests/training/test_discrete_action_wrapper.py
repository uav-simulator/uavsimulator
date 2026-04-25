"""Tests for DiscreteActionWrapper."""

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

from training.discrete_action_wrapper import (  # noqa: E402
    ACTION_NAMES,
    ACTION_TABLE,
    DiscreteActionWrapper,
)


class _ContinuousActionStub(gym.Env):
    """Mock env with continuous Box(2,) action_space matching ABCorridorVisionEnv."""

    metadata = {"render_modes": []}

    def __init__(self):
        self.action_space = spaces.Box(low=-1.0, high=1.0, shape=(2,), dtype=np.float32)
        self.observation_space = spaces.Box(low=0, high=1, shape=(4,), dtype=np.float32)
        self.last_action: np.ndarray | None = None

    def reset(self, *, seed=None, options=None):
        self.last_action = None
        return np.zeros(4, dtype=np.float32), {}

    def step(self, action):
        self.last_action = np.asarray(action, dtype=np.float32).copy()
        return np.zeros(4, dtype=np.float32), 0.0, False, False, {}


def test_action_space_is_discrete5():
    env = DiscreteActionWrapper(_ContinuousActionStub())
    assert isinstance(env.action_space, spaces.Discrete)
    assert env.action_space.n == 5


@pytest.mark.parametrize("idx,expected_name", list(enumerate(ACTION_NAMES)))
def test_action_index_maps_to_correct_continuous(idx, expected_name):
    base = _ContinuousActionStub()
    env = DiscreteActionWrapper(base)
    env.reset()
    env.step(idx)
    np.testing.assert_array_almost_equal(base.last_action, ACTION_TABLE[idx])


def test_invalid_action_raises():
    env = DiscreteActionWrapper(_ContinuousActionStub())
    env.reset()
    with pytest.raises(ValueError):
        env.step(5)
    with pytest.raises(ValueError):
        env.step(-1)


def test_rejects_non_box_action_space():
    class _DiscreteStub(_ContinuousActionStub):
        def __init__(self):
            super().__init__()
            self.action_space = spaces.Discrete(3)

    with pytest.raises(ValueError):
        DiscreteActionWrapper(_DiscreteStub())


def test_action_table_thresholds_match_resolve_command():
    """Verify each (throttle,steer) routes to the expected command per backend
    ResolveCommand thresholds. DirLeft/Right include forward throttle 0.5 so
    Unity Ackermann vehicle can move-and-turn during training; ResolveCommand
    (throttle>0.15 + steer>0.45 → DirLeft) routes them to expected real cmd.
    """

    def resolve(throttle, steer):
        if throttle < -0.25:
            return "DirBack"
        if abs(throttle) < 0.15:
            if steer > 0.55:
                return "DirLeft"
            if steer < -0.55:
                return "DirRight"
            return "DirStop"
        if steer > 0.45:
            return "DirLeft"
        if steer < -0.45:
            return "DirRight"
        return "DirForward"

    for idx, name in enumerate(ACTION_NAMES):
        throttle, steer = ACTION_TABLE[idx]
        actual = resolve(throttle, steer)
        assert actual == name, (
            f"Action {idx} ({name}): throttle={throttle}, steer={steer} → "
            f"resolves to {actual}, expected {name}"
        )


def test_action_table_distinguishability_in_unity_sim():
    """Каждое действие должно давать distinct физическое поведение в Unity:
    либо ненулевой throttle (forward/backward motion), либо пара (throttle>0,
    |steer|>0) для arc turn. DirStop единственное с zero throttle+zero steer.
    """
    table = ACTION_TABLE
    # DirStop: zero throttle AND zero steer
    assert table[0][0] == 0.0 and table[0][1] == 0.0

    # DirForward: forward throttle, zero steer
    assert table[1][0] > 0.5 and table[1][1] == 0.0

    # DirBack: reverse throttle, zero steer
    assert table[2][0] < -0.5 and table[2][1] == 0.0

    # DirLeft: forward throttle (movement!) + positive steer (turn)
    assert table[3][0] > 0.0, "DirLeft must have non-zero throttle else Unity gives no motion"
    assert table[3][1] > 0.5

    # DirRight: forward throttle + negative steer
    assert table[4][0] > 0.0, "DirRight must have non-zero throttle else Unity gives no motion"
    assert table[4][1] < -0.5
