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
    1 -> DirForward   -> (throttle=+1.0, steer=0)
    2 -> DirBack      -> (throttle=-1.0, steer=0)
    3 -> DirLeft      -> (throttle=+0.5, steer=+1.0)   # forward+turn arc
    4 -> DirRight     -> (throttle=+0.5, steer=-1.0)   # forward+turn arc

DirLeft/DirRight интентsionально включают forward throttle: в Unity vehicle
plugin (Ackermann-like) pure steer без throttle даёт NO motion (колёса
повёрнуты, но нет forward force). Это вызывало collapse v9: модель видела
DirLeft/Right как "пустое действие без progress" → никогда не использовала.

С (0.5, ±1.0) в sim policy учится "проехать+повернуть arc". На реальном
ks0223 эти же значения через AutopilotService.ResolveCommand маппятся в
DirLeft/DirRight (rotation in place), потому что |throttle|>=0.15 +
|steer|>0.45 → команда поворота. Это известный sim-to-real gap (в sim arc,
на реале in-place rotate), но он acceptable — главное policy УМЕЕТ
поворачивать.

DirForward/Back используют ±1.0 (не ±0.7 как раньше): дают maximum forward
force, чтобы DirForward в sim был distinguishable от DirLeft/Right. На
реальном ks0223 throttle clipped до ThrottleMax=0.5 в safety filter перед
ResolveCommand, который всё равно вернёт DirForward/Back.
"""

from __future__ import annotations

import gymnasium as gym
import numpy as np
from gymnasium import spaces

# Action index -> (throttle, steer).
# Unity vehicle.ks0223.v1 (Ks0223Vehicle.cs) is a true differential-drive:
# linear and angular velocities are *decoupled*, exactly matching real KS0223
# motor commands. So pure-steer (0, ±1) gives in-place rotation in BOTH
# sim and real — no sim-to-real gap on this dimension.
#
# Earlier experiment with (0.5, ±1) "forward+turn arc" was a misdiagnosis:
# the v9 collapse was caused by env stall_penalty terminating rotation
# episodes (in-place rotation → linear speed = 0 → stall trigger), not by
# the action mapping itself. See AbCorridorVisionEnv._compute_reward.
ACTION_TABLE = np.array(
    [
        [0.0, 0.0],    # 0: DirStop
        [+1.0, 0.0],   # 1: DirForward
        [-1.0, 0.0],   # 2: DirBack
        [0.0, +1.0],   # 3: DirLeft  — pure in-place rotation (left)
        [0.0, -1.0],   # 4: DirRight — pure in-place rotation (right)
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
