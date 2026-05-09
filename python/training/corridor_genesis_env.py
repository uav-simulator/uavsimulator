"""Corridor environment in Genesis (GPU-parallel sim).

Replaces Unity / single-process bottleneck. Genesis natively runs N envs
in parallel on the GPU; each env has its own corridor + robot + camera.
KS0223 is modelled as a kinematic box with directly-set linear & angular
velocity (matches real robot's diff-drive math without wheel physics).

Observation per env (matches v9 Unity env):
- "image": (84, 84, 3) uint8 — front-facing camera
- "ultrasonic": (1,) float32 — front distance / 5.0 (clipped 0..1)

Action: Discrete(5) — DirStop / DirForward / DirBack / DirLeft / DirRight.

Reward shaping mirrors ab_corridor_vision_env._compute_reward:
- progress×100, lateral_penalty, heading_alignment_bonus, survival, time,
  backward_penalty, goal_bonus, oob_penalty, stall_penalty.

Throughput target on RTX 5080: 5000+ steps/sec at n_envs=512 (vs Unity ~200).
"""

from __future__ import annotations

import math
from typing import Any

import genesis as gs
import numpy as np
import torch
from gymnasium import spaces
from stable_baselines3.common.vec_env import VecEnv
from stable_baselines3.common.vec_env.base_vec_env import VecEnvObs, VecEnvStepReturn

# Matches ks0223 vehicle calibration: 0.73 m/s linear, 380 deg/s yaw.
ROBOT_MAX_SPEED = 0.73
ROBOT_MAX_YAW_RATE = math.radians(380.0)
DEFAULT_DT = 0.14  # 7Hz tick to match real loop interval

# Discrete action table — same encoding as DiscreteActionWrapper in
# python/training/discrete_action_wrapper.py.
# action_idx: (throttle, steer)
ACTION_TABLE = np.array([
    [0.0, 0.0],     # 0 DirStop
    [1.0, 0.0],     # 1 DirForward
    [-1.0, 0.0],    # 2 DirBack
    [0.0, 1.0],     # 3 DirLeft  (rotate in place)
    [0.0, -1.0],    # 4 DirRight
], dtype=np.float32)


def _build_corridor_walls(scene: gs.Scene, waypoints: list[tuple[float, float]],
                          corridor_width_m: float, wall_height_m: float = 0.20,
                          wall_thickness_m: float = 0.05) -> None:
    """Place box walls flanking the route defined by waypoints (x,z plane)."""
    half_w = corridor_width_m * 0.5
    for i in range(len(waypoints) - 1):
        ax, az = waypoints[i]
        bx, bz = waypoints[i + 1]
        dx, dz = bx - ax, bz - az
        seg_len = math.hypot(dx, dz)
        if seg_len < 1e-3:
            continue
        # midpoint, yaw of segment
        mx, mz = (ax + bx) * 0.5, (az + bz) * 0.5
        yaw = math.atan2(dx, dz)  # rotation around Y axis
        # Two walls — left and right of segment center.
        for side in (+1, -1):
            ox = mx + side * (half_w + wall_thickness_m * 0.5) * math.cos(yaw)
            oz = mz - side * (half_w + wall_thickness_m * 0.5) * math.sin(yaw)
            scene.add_entity(
                morph=gs.morphs.Box(
                    size=(wall_thickness_m, wall_height_m, seg_len + wall_thickness_m),
                    pos=(ox, wall_height_m * 0.5, oz),
                    euler=(0.0, math.degrees(yaw), 0.0),
                    fixed=True,
                ),
            )


class CorridorGenesisVecEnv(VecEnv):
    """Genesis-backed VecEnv for L-corridor navigation."""

    def __init__(
        self,
        n_envs: int = 64,
        max_steps: int = 400,
        corridor_width_m: float = 0.60,
        oob_margin_m: float = 0.10,
        goal_radius_m: float = 0.25,
        waypoints: list[tuple[float, float]] | None = None,
        img_size: int = 84,
        env_spacing: tuple[float, float] = (12.0, 12.0),
        backend: str = "auto",
        dt: float = DEFAULT_DT,
        show_viewer: bool = False,
    ) -> None:
        if waypoints is None:
            waypoints = [(0.0, 0.0), (0.0, 4.0), (3.0, 4.0)]
        self.waypoints = waypoints
        self.max_steps = max_steps
        self.corridor_width_m = corridor_width_m
        self.oob_threshold_m = corridor_width_m * 0.5 + oob_margin_m
        self.goal_radius_m = goal_radius_m
        self.img_size = img_size
        self.dt = dt
        self.total_route_length = sum(
            math.hypot(b[0] - a[0], b[1] - a[1])
            for a, b in zip(waypoints[:-1], waypoints[1:])
        )

        # Initialize Genesis once per process — guarded by gs.is_initialized().
        backend_token = {
            "auto": gs.gpu if torch.cuda.is_available() else gs.cpu,
            "cpu": gs.cpu,
            "gpu": gs.gpu,
        }.get(backend, gs.gpu if torch.cuda.is_available() else gs.cpu)
        if not getattr(gs, "_initialized", False):
            gs.init(backend=backend_token, logging_level="warning")

        self.scene = gs.Scene(
            show_viewer=show_viewer,
            sim_options=gs.options.SimOptions(dt=self.dt, substeps=2),
        )
        # Ground plane.
        self.scene.add_entity(morph=gs.morphs.Plane())
        # Corridor walls.
        _build_corridor_walls(self.scene, waypoints, corridor_width_m)
        # Robot — kinematic box matching ks0223 chassis (~20×15×10 cm).
        self.robot = self.scene.add_entity(
            morph=gs.morphs.Box(
                size=(0.15, 0.10, 0.20),
                pos=(waypoints[0][0], 0.05, waypoints[0][1]),
                fixed=False,
            ),
        )
        # Per-env front camera (forward = robot's +Z local).
        self.camera = self.scene.add_camera(
            res=(img_size, img_size),
            pos=(waypoints[0][0], 0.10, waypoints[0][1] + 0.12),
            lookat=(waypoints[0][0], 0.10, waypoints[0][1] + 1.0),
            fov=72,
            GUI=False,
        )

        self.scene.build(n_envs=n_envs, env_spacing=env_spacing)

        # Per-env state buffers.
        self._n_envs = n_envs
        self._step_count = np.zeros(n_envs, dtype=np.int32)
        self._prev_progress = np.zeros(n_envs, dtype=np.float32)
        self._stalled = np.zeros(n_envs, dtype=np.int32)
        self._reached = [set() for _ in range(n_envs)]
        self._yaw = np.zeros(n_envs, dtype=np.float32)

        obs_space = spaces.Dict({
            "image": spaces.Box(0, 255, shape=(img_size, img_size, 3), dtype=np.uint8),
            "ultrasonic": spaces.Box(0.0, 1.0, shape=(1,), dtype=np.float32),
        })
        super().__init__(n_envs, obs_space, spaces.Discrete(5))

        self._pending_actions: np.ndarray | None = None

    # ---- VecEnv API ----------------------------------------------------- #

    def reset(self) -> VecEnvObs:
        # Place robot at start waypoint, zero velocity, zero yaw.
        start = self.waypoints[0]
        pos = torch.tensor(
            [[start[0], 0.05, start[1]] for _ in range(self._n_envs)],
            device=gs.device, dtype=torch.float32,
        )
        self.robot.set_pos(pos)
        self.robot.zero_all_dofs_velocity()
        self._step_count[:] = 0
        self._prev_progress[:] = 0.0
        self._stalled[:] = 0
        self._yaw[:] = 0.0
        self._reached = [set() for _ in range(self._n_envs)]
        return self._build_obs()

    def step_async(self, actions: np.ndarray) -> None:
        self._pending_actions = actions

    def step_wait(self) -> VecEnvStepReturn:
        assert self._pending_actions is not None
        actions = self._pending_actions

        throttles = ACTION_TABLE[actions, 0]  # (N,)
        steers = ACTION_TABLE[actions, 1]
        # Update yaw (kinematic). steer in [-1,+1] -> yaw rate.
        d_yaw = steers * ROBOT_MAX_YAW_RATE * self.dt
        self._yaw += d_yaw
        # Linear motion in current heading direction.
        v = throttles * ROBOT_MAX_SPEED
        dx = v * np.sin(self._yaw) * self.dt
        dz = v * np.cos(self._yaw) * self.dt

        # Read current pos from scene, apply delta, write back.
        pos = self.robot.get_pos().cpu().numpy()  # (N, 3)
        pos[:, 0] += dx
        pos[:, 2] += dz
        new_pos = torch.tensor(pos, device=gs.device, dtype=torch.float32)
        self.robot.set_pos(new_pos)

        self.scene.step()

        rewards = np.zeros(self._n_envs, dtype=np.float32)
        dones = np.zeros(self._n_envs, dtype=bool)
        infos: list[dict[str, Any]] = []

        new_pos_np = self.robot.get_pos().cpu().numpy()
        for i in range(self._n_envs):
            self._step_count[i] += 1
            r, term, reason = self._reward(i, new_pos_np[i, 0], new_pos_np[i, 2],
                                            self._yaw[i], int(actions[i]))
            rewards[i] = r
            if term or self._step_count[i] >= self.max_steps:
                dones[i] = True
                infos.append({"termination_reason": reason or "truncated"})
            else:
                infos.append({})

        obs = self._build_obs()

        # Auto-reset done envs.
        if dones.any():
            for i in np.where(dones)[0]:
                self._reset_one(i)

        return obs, rewards, dones, infos

    def _reset_one(self, i: int) -> None:
        start = self.waypoints[0]
        # Set just env i back to start (Genesis API: env_idx parameter).
        pos = self.robot.get_pos().cpu().numpy()
        pos[i, 0] = start[0]
        pos[i, 1] = 0.05
        pos[i, 2] = start[1]
        self.robot.set_pos(torch.tensor(pos, device=gs.device, dtype=torch.float32))
        self._step_count[i] = 0
        self._prev_progress[i] = 0.0
        self._stalled[i] = 0
        self._yaw[i] = 0.0
        self._reached[i] = set()

    def _build_obs(self) -> dict[str, np.ndarray]:
        # Render all env cameras: returns (N, H, W, 3) uint8 batch.
        rgb = self.camera.render()
        if isinstance(rgb, tuple):
            rgb = rgb[0]
        if hasattr(rgb, "cpu"):
            rgb = rgb.cpu().numpy()
        rgb = np.asarray(rgb, dtype=np.uint8)
        # Genesis camera with build(n_envs=N) returns batch dim 0 = N
        # (or single image when n_envs=0); make sure shape is (N, H, W, 3).
        if rgb.ndim == 3:
            rgb = rgb[np.newaxis, ...]

        # Ultrasonic — front distance via raycast or analytical estimate.
        # For now: distance from robot to nearest wall along forward heading.
        pos = self.robot.get_pos().cpu().numpy()
        front_m = np.zeros(self._n_envs, dtype=np.float32)
        for i in range(self._n_envs):
            front_m[i] = self._estimate_front_distance(
                pos[i, 0], pos[i, 2], self._yaw[i]
            )
        ultrasonic = np.clip(front_m / 5.0, 0.0, 1.0).reshape(-1, 1).astype(np.float32)
        return {"image": rgb, "ultrasonic": ultrasonic}

    def _estimate_front_distance(self, px: float, pz: float, yaw: float) -> float:
        """Analytical front-distance to nearest wall along robot heading.

        Approximates HC-SR04 front cone as a single forward ray. Computes
        intersection with corridor wall segments; returns nearest hit.
        """
        fx, fz = math.sin(yaw), math.cos(yaw)
        best = 5.0  # max reading
        half = self.corridor_width_m * 0.5
        for i in range(len(self.waypoints) - 1):
            ax, az = self.waypoints[i]
            bx, bz = self.waypoints[i + 1]
            seg_len = math.hypot(bx - ax, bz - az)
            if seg_len < 1e-3:
                continue
            tx, tz = (bx - ax) / seg_len, (bz - az) / seg_len
            nx, nz = -tz, tx  # left normal
            # Two walls, at ±half along normal.
            for side in (+1, -1):
                wax = ax + side * half * nx
                waz = az + side * half * nz
                # Ray (px,pz)+(fx,fz)*t intersects segment (wax,waz)→
                # (wax+tx*seg_len, waz+tz*seg_len).
                den = fx * (-tz) - fz * (-tx)  # cross(forward, segment)
                if abs(den) < 1e-6:
                    continue
                ux = wax - px
                uz = waz - pz
                t = (ux * (-tz) - uz * (-tx)) / den
                s = (ux * fz - uz * fx) / -den
                if t > 0 and 0 <= s <= seg_len:
                    best = min(best, t)
        return float(best)

    def _route_progress(self, px: float, pz: float) -> float:
        if self.total_route_length <= 0:
            return 0.0
        cum = 0.0
        best_dist = float("inf")
        progress = 0.0
        for i in range(len(self.waypoints) - 1):
            ax, az = self.waypoints[i]
            bx, bz = self.waypoints[i + 1]
            seg_len = math.hypot(bx - ax, bz - az)
            dx, dz = bx - ax, bz - az
            seg2 = dx * dx + dz * dz
            t = max(0.0, min(1.0, ((px - ax) * dx + (pz - az) * dz) / seg2)) if seg2 > 0 else 0.0
            cx, cz = ax + t * dx, az + t * dz
            d = math.hypot(px - cx, pz - cz)
            if d < best_dist:
                best_dist = d
                progress = (cum + t * seg_len) / self.total_route_length
            cum += seg_len
        return max(0.0, min(1.0, progress))

    def _nearest_wall_dist(self, px: float, pz: float) -> float:
        best = float("inf")
        for i in range(len(self.waypoints) - 1):
            ax, az = self.waypoints[i]
            bx, bz = self.waypoints[i + 1]
            dx, dz = bx - ax, bz - az
            seg2 = dx * dx + dz * dz
            t = max(0.0, min(1.0, ((px - ax) * dx + (pz - az) * dz) / seg2)) if seg2 > 0 else 0.0
            cx, cz = ax + t * dx, az + t * dz
            d = math.hypot(px - cx, pz - cz)
            best = min(best, d)
        return best

    def _reward(self, i: int, px: float, pz: float, yaw: float,
                action_idx: int) -> tuple[float, bool, str]:
        progress = self._route_progress(px, pz)
        delta = progress - float(self._prev_progress[i])
        self._prev_progress[i] = progress

        progress_reward = delta * 100.0
        lateral = self._nearest_wall_dist(px, pz)
        half = self.corridor_width_m * 0.5
        wall_prox = lateral / half if half > 0 else 0.0
        lateral_penalty = -1.0 * wall_prox * wall_prox
        survival = 0.1
        time_penalty = -0.02
        backward = -0.5 if action_idx == 2 else 0.0

        # Waypoint bonuses
        wp_bonus = 0.0
        for wi in range(len(self.waypoints) - 1):
            if wi in self._reached[i]:
                continue
            wx, wz = self.waypoints[wi]
            if math.hypot(px - wx, pz - wz) < max(self.goal_radius_m, 0.5):
                self._reached[i].add(wi)
                wp_bonus += 5.0

        # Heading bonus
        heading = 0.0
        next_wp = None
        for wi in range(len(self.waypoints)):
            if wi not in self._reached[i]:
                next_wp = self.waypoints[wi]
                break
        if next_wp is not None:
            dx, dz = next_wp[0] - px, next_wp[1] - pz
            d = math.hypot(dx, dz)
            if d > 1e-3:
                fx, fz = math.sin(yaw), math.cos(yaw)
                heading = (fx * dx + fz * dz) / d

        # Goal
        gx, gz = self.waypoints[-1]
        if math.hypot(px - gx, pz - gz) < self.goal_radius_m:
            return float(progress_reward + 100.0 + survival + heading), True, "goal_reached"
        # OOB
        if lateral > self.oob_threshold_m:
            return float(progress_reward + lateral_penalty - 30.0), True, "out_of_bounds"
        # Stall
        if abs(delta) < 1e-4 and self._step_count[i] > 20:
            self._stalled[i] += 1
        else:
            self._stalled[i] = 0
        if self._stalled[i] >= 30:
            return float(progress_reward + lateral_penalty - 10.0), True, "stalled"

        r = (progress_reward + wp_bonus + lateral_penalty + survival
             + time_penalty + backward + heading)
        return float(r), False, "running"

    # Required VecEnv noop methods --------------------------------------- #

    def close(self) -> None:
        pass

    def get_attr(self, attr_name, indices=None):
        return [getattr(self, attr_name, None)] * self._n_envs

    def set_attr(self, attr_name, value, indices=None) -> None:
        setattr(self, attr_name, value)

    def env_method(self, method_name, *args, indices=None, **kwargs):
        return [None] * self._n_envs

    def env_is_wrapped(self, wrapper_class, indices=None):
        return [False] * self._n_envs

    def get_images(self):
        return [None] * self._n_envs

    def seed(self, seed=None):
        return [seed] * self._n_envs
