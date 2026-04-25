"""Discrete action wrapper for KS0223 sim-to-real alignment.

Real KS0223 supports only 5 discrete commands (DirForward/Back/Left/Right/Stop)
via Pi MainControl.py. Continuous-action models (v6/v7/v8) collapsed to
constant steer=+1 on real, mapping to in-place rotation through
backend ResolveCommand → wall slam.

This wrapper exposes a Discrete(5) action space to PPO and internally maps
each action to a (throttle, steer) tuple matching what backend.ResolveCommand
will route to the same command on the real robot. That keeps sim policy
distribution aligned with real-robot mechanics.

Action mapping (matches AutopilotService.ResolveCommand thresholds):
    0 -> DirStop      -> (throttle=0,    steer=0)
    1 -> DirForward   -> (throttle=+0.7, steer=0)
    2 -> DirBack      -> (throttle=-0.7, steer=0)
    3 -> DirLeft      -> (throttle=0,    steer=+0.9)
    4 -> DirRight     -> (throttle=0,    steer=-0.9)

Note: throttle for DirForward/Back is +/-0.7 (not +/-1.0) to leave headroom
for safety-wrapper throttle clip on real robot (ThrottleMax=0.5 → 0.35
effective forward), which still maps to DirForward via ResolveCommand
(threshold |throttle|>=0.15 + |steer|<0.45 → DirForward).
"""

from __future__ import annotations

import gymnasium as gym
import numpy as np
from gymnasium import spaces

# Action index → (throttle, steer)
ACTION_TABLE = np.array(
    [
        [0.0, 0.0],   # 0: DirStop
        [+0.7, 0.0],  # 1: DirForward
        [-0.7, 0.0],  # 2: DirBack
        [0.0, +0.9],  # 3: DirLeft
        [0.0, -0.9],  # 4: DirRight
    ],
    dtype=np.float32,
)

ACTION_NAMES = ["DirStop", "DirForward", "DirBack", "DirLeft", "DirRight"]


class DiscreteActionWrapper(gym.ActionWrapper):
    """Convert Discrete(5) → continuous (throttle, steer) for KS0223 alignment."""

    def __init__(self, env: gym.Env):
        super().__init__(env)
        if not isinstance(env.action_space, spaces.Box):
            raise ValueError(
                f"DiscreteActionWrapper expects Box action_space, got {type(env.action_space)}"
            )
        if env.action_space.shape != (2,):
            raise ValueError(
                f"DiscreteActionWrapper expects shape (2,), got {env.action_space.shape}"
            )
        self.action_space = spaces.Discrete(5)

    def action(self, action: int) -> np.ndarray:
        idx = int(action)
        if idx < 0 or idx >= len(ACTION_TABLE):
            raise ValueError(f"Discrete action {idx} out of range [0, {len(ACTION_TABLE)})")
        return ACTION_TABLE[idx].copy()
