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
# Sign convention (verified empirically against the live sim, 2026-05-25):
#   Ks0223Vehicle.cs maps (throttle, steer) → (leftPwm = throttle − steer,
#                                              rightPwm = throttle + steer).
#   So (throttle=0, steer=+1) → leftPwm=-1, rightPwm=+1 → right wheel
#   forward + left wheel reverse → CLOCKWISE rotation from above (Unity yaw
#   increases) → physical RIGHT turn.
#   And (throttle=0, steer=-1) → leftPwm=+1, rightPwm=-1 → physical LEFT.
#
# The earlier version of this table had rows 3 and 4 mislabelled (it set
# DirLeft = (0, +1) and DirRight = (0, -1) with comments claiming "in-place
# rotation (left)" and "(right)" respectively). That contradicted the
# physical direction the sim produces, and it contradicted the backend
# UnityKs0223RuntimeProvider.cs which already maps DirLeft → LeftPwm=+,
# RightPwm=- (= physical left turn). Policies trained with the old table
# learned action_idx 3 to mean physical RIGHT turn — they ran fine in sim
# (PPO is indifferent to label semantics) but deployed inverted onto real
# hardware, and any BC trained on operator demos (where the operator
# intuitively labels DirLeft for physical-left moves) ended up with the
# label-to-physics map flipped. Fixed here by swapping the steer signs in
# rows 3 and 4 so DirLeft = physical left and DirRight = physical right
# end-to-end (operator → backend → sim → PPO → BC → real robot).
ACTION_TABLE = np.array(
    [
        [0.0, 0.0],    # 0: DirStop
        [+1.0, 0.0],   # 1: DirForward
        [-1.0, 0.0],   # 2: DirBack
        # DirLeft / DirRight use throttle=+0.5 (not 0) so the agent can
        # actually make forward progress through corners — empirically, with
        # throttle=0 in this Unity vehicle the wheels rotate but the chassis
        # doesn't translate (no longitudinal force component), the auto-pilot
        # gets stuck at the first corner and PPO sees turn-actions as
        # "progress-killing" and refuses to use them ("v9 collapse"). The
        # corresponding sim-to-real translation through backend ResolveCommand
        # routes (throttle≥0.15, |steer|>0.45) → DirLeft/Right on the real
        # robot, so the +0.5 throttle does not leak forward motion through
        # to the physical platform — it stays mapped to in-place rotation.
        [+0.5, -1.0],  # 3: DirLeft  — leftPwm=throttle-steer=+1.5→clamp+1, rightPwm=-0.5 → physical LEFT
        [+0.5, +1.0],  # 4: DirRight — leftPwm=-0.5, rightPwm=+1.5→clamp+1 → physical RIGHT
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
