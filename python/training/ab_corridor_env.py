"""Gymnasium environment wrapping uav-simulator runtime for A->B corridor task."""

from __future__ import annotations

import math
from pathlib import Path
from typing import Any

import gymnasium as gym
import numpy as np
from gymnasium import spaces

import sys

ROOT = Path(__file__).resolve().parents[2]
PYTHON_ROOT = ROOT / "python"
if str(PYTHON_ROOT) not in sys.path:
    sys.path.insert(0, str(PYTHON_ROOT))

from sim_client.http_client import SimClient
from sim_client.scenario import load_scenario_file, scenario_to_reset_config


class ABCorridorEnv(gym.Env):
    """
    Observation (8-dim continuous):
        0: line_tracker s1 (left)
        1: line_tracker s2
        2: line_tracker s3 (center)
        3: line_tracker s4
        4: line_tracker s5 (right)
        5: ultrasonic front distance (normalized, /5.0)
        6: speed (normalized, /3.0)
        7: heading_error (normalized, /pi)

    Action (2-dim continuous [-1, 1]):
        0: throttle (forward/backward)
        1: steer (left/right)
    """

    metadata = {"render_modes": ["human"]}

    def __init__(
        self,
        base_url: str = "http://127.0.0.1:8000",
        scenario_path: str | None = None,
        max_steps: int = 300,
        corridor_width_m: float = 3.0,
        oob_margin_m: float = 0.3,
        goal_radius_m: float = 1.0,
        time_scale: float = 2.0,
    ):
        super().__init__()

        self.client = SimClient(base_url, timeout_s=30.0)
        self.max_steps = max_steps
        self.corridor_width_m = corridor_width_m
        self.oob_margin_m = oob_margin_m
        self.oob_threshold_m = (corridor_width_m * 0.5) + oob_margin_m
        self.goal_radius_m = goal_radius_m
        self.time_scale = time_scale

        # Use direct reset to place robot on-road with correct track geometry.
        # The ab-corridor-v1.yaml scenario spawns off-road at x=-6, so we bypass it.
        self._reset_config = {
            "seed": 0,
            "timeScale": time_scale,
            "selectedTrackId": "track.basic_arena.v1",
            "selectedVehicleId": "vehicle.prometeo.sport.v1",
            "trackParams": [],
            "vehicleParams": [{"key": "camera.profile", "value": "high"}],
            "flags": [],
        }

        # Waypoints matching actual BasicArenaTrack S-shape geometry:
        # Seg A: x=0, z=-8 to z=-1 (forward)
        # Seg B: x=0 to x=6, z=-1 (right turn)
        # Seg C: x=6, z=-1 to z=5 (forward)
        self.waypoints: list[tuple[float, float]] = [
            (0.0, -7.5),   # Start of segment A
            (0.0, -1.0),   # End of segment A / start of turn
            (6.0, -1.0),   # End of segment B / start of turn
            (6.0, 5.0),    # End of segment C (goal)
        ]

        self.total_route_length = self._compute_route_length()

        self.observation_space = spaces.Box(
            low=-1.0, high=1.0, shape=(8,), dtype=np.float32
        )
        self.action_space = spaces.Box(
            low=-1.0, high=1.0, shape=(2,), dtype=np.float32
        )

        self._step_count = 0
        self._prev_progress = 0.0
        self._prev_steer = 0.0
        self._episode_seed = 0
        self._last_step: dict[str, Any] = {}
        self._prev_pos: dict[str, float] = {"x": 0.0, "y": 0.0, "z": 0.0}
        self._computed_speed = 0.0
        self._computed_heading = 0.0  # radians, 0 = +z direction
        self._reached_waypoints: set[int] = set()  # track which waypoints reached

    def _compute_route_length(self) -> float:
        total = 0.0
        for i in range(len(self.waypoints) - 1):
            ax, az = self.waypoints[i]
            bx, bz = self.waypoints[i + 1]
            total += math.hypot(bx - ax, bz - az)
        return max(total, 1.0)

    def reset(self, *, seed: int | None = None, options: dict | None = None):
        super().reset(seed=seed)
        if seed is not None:
            self._episode_seed = seed
        else:
            self._episode_seed += 1

        config = dict(self._reset_config)
        config["seed"] = self._episode_seed

        step = self.client.reset(config)
        self._last_step = step
        self._step_count = 0
        self._prev_progress = 0.0
        self._prev_steer = 0.0
        self._prev_pos = self._current_position(step)
        self._computed_speed = 0.0
        self._computed_heading = 0.0
        self._reached_waypoints = set()

        obs = self._build_observation(step)
        info = self._build_info(step)
        return obs, info

    def step(self, action: np.ndarray):
        throttle = float(np.clip(action[0], -1.0, 1.0))
        steer = float(np.clip(action[1], -1.0, 1.0))

        # Use native throttle+steer which Unity internally maps to differential drive
        step = self.client.step({
            "throttle": throttle,
            "steer": steer,
            "brake": 0.0,
            "targetAgentId": "ego",
            "timestamp": 0,
            "timeBase": "unix_ms",
            "extensions": [],
        })
        self._last_step = step
        self._step_count += 1
        self._update_kinematics(step)

        obs = self._build_observation(step)
        reward, terminated, truncated = self._compute_reward(step, steer)
        info = self._build_info(step)

        self._prev_steer = steer

        if self._step_count >= self.max_steps and not terminated:
            truncated = True

        return obs, reward, terminated, truncated, info

    def _update_kinematics(self, step: dict[str, Any]) -> None:
        """Compute speed and heading from position delta (sensors report 0)."""
        pos = self._current_position(step)
        dx = pos["x"] - self._prev_pos["x"]
        dz = pos["z"] - self._prev_pos["z"]
        dist = math.hypot(dx, dz)

        self._computed_speed = dist  # distance per step (not m/s, but proportional)
        if dist > 0.001:
            self._computed_heading = math.atan2(dx, dz)  # 0=+z, pi/2=+x

        self._prev_pos = pos

    def _build_observation(self, step: dict[str, Any]) -> np.ndarray:
        tm = self._telemetry_map(step)
        pos = self._current_position(step)

        s1 = self._parse_float(tm, "sensor.line_tracker.s1_norm")
        s2 = self._parse_float(tm, "sensor.line_tracker.s2_norm")
        s3 = self._parse_float(tm, "sensor.line_tracker.s3_norm")
        s4 = self._parse_float(tm, "sensor.line_tracker.s4_norm")
        s5 = self._parse_float(tm, "sensor.line_tracker.s5_norm")
        front_dist = self._parse_float(tm, "sensor.ultrasonic.front.m") / 5.0

        # Speed from position delta (normalized, typical max ~0.1 per step)
        speed_norm = min(self._computed_speed / 0.1, 1.0)

        # Heading error to next waypoint
        heading_error = self._compute_heading_error(pos)

        obs = np.array([
            np.clip(s1, -1, 1),
            np.clip(s2, -1, 1),
            np.clip(s3, -1, 1),
            np.clip(s4, -1, 1),
            np.clip(s5, -1, 1),
            np.clip(front_dist, -1, 1),
            np.clip(speed_norm, -1, 1),
            np.clip(heading_error / math.pi, -1, 1),
        ], dtype=np.float32)
        return obs

    def _compute_heading_error(self, pos: dict[str, float]) -> float:
        """Compute angle between current heading and direction to next waypoint."""
        _, closest_idx = self._closest_segment(pos["x"], pos["z"])
        target_idx = min(closest_idx + 1, len(self.waypoints) - 1)
        tx, tz = self.waypoints[target_idx]
        dx = tx - pos["x"]
        dz = tz - pos["z"]

        target_angle = math.atan2(dx, dz)
        error = target_angle - self._computed_heading

        while error > math.pi:
            error -= 2 * math.pi
        while error < -math.pi:
            error += 2 * math.pi
        return error

    def _compute_reward(self, step: dict[str, Any], steer: float):
        pos = self._current_position(step)
        px, pz = pos["x"], pos["z"]

        # 1. Progress reward: scaled by route length
        progress = self._route_progress(px, pz)
        delta_progress = progress - self._prev_progress
        self._prev_progress = progress
        progress_reward = delta_progress * 20.0

        # 2. Waypoint bonuses: reward for reaching intermediate waypoints
        waypoint_bonus = 0.0
        for wi in range(len(self.waypoints)):
            if wi not in self._reached_waypoints:
                wx, wz = self.waypoints[wi]
                if math.hypot(px - wx, pz - wz) < 1.5:
                    self._reached_waypoints.add(wi)
                    waypoint_bonus += 10.0

        # 3. Heading alignment reward: bonus for facing the right direction
        heading_error = self._compute_heading_error(pos)
        heading_reward = 0.05 * (1.0 - abs(heading_error) / math.pi)

        # 4. Lateral deviation penalty
        lateral_dist = self._nearest_route_distance(px, pz)
        lateral_penalty = -0.3 * (lateral_dist / self.oob_threshold_m) ** 2

        # 5. Steer jerk penalty (gentler)
        steer_jerk = abs(steer - self._prev_steer)
        jerk_penalty = -0.05 * steer_jerk

        # 6. Speed reward (from position delta)
        speed_reward = 0.1 * min(self._computed_speed / 0.05, 1.0)

        # 7. Time penalty (encourage finishing faster)
        time_penalty = -0.02

        # 8. Goal reached bonus
        goal_bonus = 0.0
        terminated = False
        goal_x, goal_z = self.waypoints[-1]
        dist_to_goal = math.hypot(px - goal_x, pz - goal_z)
        if dist_to_goal < self.goal_radius_m:
            goal_bonus = 100.0
            terminated = True

        # 9. Out of bounds penalty
        oob_penalty = 0.0
        if lateral_dist > self.oob_threshold_m:
            oob_penalty = -30.0
            terminated = True

        # 10. Done from runtime
        if bool(step.get("done")) and not terminated:
            oob_penalty = -15.0
            terminated = True

        reward = (progress_reward + waypoint_bonus + heading_reward +
                  lateral_penalty + jerk_penalty + speed_reward +
                  time_penalty + goal_bonus + oob_penalty)
        truncated = False

        return float(reward), terminated, truncated

    def _route_progress(self, px: float, pz: float) -> float:
        """Fraction of route completed (0..1) based on projection onto polyline."""
        best_dist = float("inf")
        best_progress = 0.0
        cumulative = 0.0

        for i in range(len(self.waypoints) - 1):
            ax, az = self.waypoints[i]
            bx, bz = self.waypoints[i + 1]
            seg_len = math.hypot(bx - ax, bz - az)
            if seg_len < 1e-9:
                continue

            t = self._project_t(px, pz, ax, az, bx, bz)
            proj_x = ax + t * (bx - ax)
            proj_z = az + t * (bz - az)
            dist = math.hypot(px - proj_x, pz - proj_z)

            if dist < best_dist:
                best_dist = dist
                best_progress = (cumulative + t * seg_len) / self.total_route_length

            cumulative += seg_len

        return np.clip(best_progress, 0.0, 1.0)

    def _closest_segment(self, px: float, pz: float) -> tuple[float, int]:
        best_dist = float("inf")
        best_idx = 0
        for i in range(len(self.waypoints) - 1):
            ax, az = self.waypoints[i]
            bx, bz = self.waypoints[i + 1]
            dist = self._point_to_segment_distance(px, pz, ax, az, bx, bz)
            if dist < best_dist:
                best_dist = dist
                best_idx = i
        return best_dist, best_idx

    def _nearest_route_distance(self, px: float, pz: float) -> float:
        return self._closest_segment(px, pz)[0]

    @staticmethod
    def _project_t(px, pz, ax, az, bx, bz) -> float:
        abx, abz = bx - ax, bz - az
        apx, apz = px - ax, pz - az
        ab2 = abx * abx + abz * abz
        if ab2 < 1e-9:
            return 0.0
        return max(0.0, min(1.0, (apx * abx + apz * abz) / ab2))

    @staticmethod
    def _point_to_segment_distance(px, pz, ax, az, bx, bz) -> float:
        abx, abz = bx - ax, bz - az
        apx, apz = px - ax, pz - az
        ab2 = abx * abx + abz * abz
        if ab2 < 1e-9:
            return math.hypot(px - ax, pz - az)
        t = max(0.0, min(1.0, (apx * abx + apz * abz) / ab2))
        cx = ax + t * abx
        cz = az + t * abz
        return math.hypot(px - cx, pz - cz)

    @staticmethod
    def _telemetry_map(step: dict[str, Any]) -> dict[str, str]:
        state = step.get("state") or {}
        result: dict[str, str] = {}
        for item in state.get("telemetry") or []:
            if isinstance(item, dict):
                key = str(item.get("key", "")).strip()
                if key:
                    result[key] = str(item.get("value", ""))
        return result

    @staticmethod
    def _current_position(step: dict[str, Any]) -> dict[str, float]:
        pos = ((step.get("state") or {}).get("pose") or {}).get("position") or {}
        return {"x": float(pos.get("x", 0)), "y": float(pos.get("y", 0)), "z": float(pos.get("z", 0))}

    @staticmethod
    def _parse_float(mapping: dict[str, str], *keys: str) -> float:
        for key in keys:
            raw = mapping.get(key)
            if raw is not None:
                try:
                    return float(raw)
                except ValueError:
                    continue
        return 0.0

    def _build_info(self, step: dict[str, Any]) -> dict[str, Any]:
        pos = self._current_position(step)
        return {
            "position": pos,
            "progress": self._route_progress(pos["x"], pos["z"]),
            "lateral_distance": self._nearest_route_distance(pos["x"], pos["z"]),
            "step_count": self._step_count,
        }
