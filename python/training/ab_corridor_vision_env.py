"""Gymnasium environment with camera-based observation for A->B corridor task.

Observation: Dict with:
  - "image": (84, 84, 3) uint8 RGB camera frame
  - "ultrasonic": (1,) float32 normalized front distance

Action: Box[-1, 1] shape (2,):
  - throttle, steer → mapped internally to differential drive
"""

from __future__ import annotations

import base64
import io
import math
from pathlib import Path
from typing import Any

import gymnasium as gym
import numpy as np
from gymnasium import spaces

try:
    from PIL import Image
except ImportError:
    Image = None

import sys

ROOT = Path(__file__).resolve().parents[2]
PYTHON_ROOT = ROOT / "python"
if str(PYTHON_ROOT) not in sys.path:
    sys.path.insert(0, str(PYTHON_ROOT))

from sim_client.http_client import SimClient
from sim_client.scenario import load_scenario_file, scenario_to_reset_config

# Camera image size for observation
IMG_SIZE = 84


class ABCorridorVisionEnv(gym.Env):
    """
    Camera-based A->B corridor environment.

    Observation: Dict space
        - "image": Box(0, 255, (84, 84, 3), uint8) — resized camera frame
        - "ultrasonic": Box(0, 1, (1,), float32) — front distance / 5.0

    Action: Box(-1, 1, (2,), float32)
        - [0] throttle
        - [1] steer
    """

    metadata = {"render_modes": ["human"]}

    def __init__(
        self,
        base_url: str = "http://127.0.0.1:8000",
        scenario_path: str | Path | None = None,
        max_steps: int = 600,
        corridor_width_m: float | None = None,
        oob_margin_m: float = 0.3,
        goal_radius_m: float | None = None,
        time_scale: float | None = 2.0,
        grayscale: bool = False,
        img_size: int = IMG_SIZE,
        track_id: str | None = "track.basic_arena.v1",
        vehicle_id: str | None = "vehicle.prometeo.sport.v1",
        waypoints: list[tuple[float, float]] | None = None,
        aruco_goal: bool = False,
        aruco_goal_distance_m: float = 0.40,
    ):
        super().__init__()

        assert Image is not None, "Pillow is required: pip install Pillow"

        self.client = SimClient(base_url, timeout_s=30.0)
        self.max_steps = max_steps
        scenario_waypoints: list[tuple[float, float]] = []
        scenario_corridor_width = 3.0
        scenario_goal_radius = 1.5
        resolved_track_id = track_id
        resolved_vehicle_id = vehicle_id
        resolved_time_scale = 2.0 if time_scale is None else time_scale

        if scenario_path is not None:
            scenario_payload = load_scenario_file(scenario_path)
            scenario_reset = scenario_to_reset_config(scenario_payload)
            route = scenario_payload.get("route") or {}
            params = route.get("params") or {}
            scenario_waypoints = self._parse_waypoints(route.get("waypoints"))
            scenario_corridor_width = float(params.get("corridor.width_m", scenario_corridor_width))
            scenario_goal_radius = float(params.get("goal.radius_m", route.get("reachDistanceM", scenario_goal_radius)))
            resolved_track_id = scenario_reset.get("selectedTrackId") or resolved_track_id
            resolved_vehicle_id = scenario_reset.get("selectedVehicleId") or resolved_vehicle_id
            if time_scale is None:
                resolved_time_scale = float(scenario_reset.get("timeScale", resolved_time_scale))
            self._reset_config = scenario_reset
        else:
            self._reset_config = {
                "seed": 0,
                "timeScale": resolved_time_scale,
                "selectedTrackId": resolved_track_id,
                "selectedVehicleId": resolved_vehicle_id,
                "trackParams": [],
                "vehicleParams": [{"key": "camera.profile", "value": "high"}],
                "flags": [],
            }

        self.corridor_width_m = scenario_corridor_width if corridor_width_m is None else corridor_width_m
        self.oob_margin_m = oob_margin_m
        self.oob_threshold_m = (self.corridor_width_m * 0.5) + oob_margin_m
        self.goal_radius_m = scenario_goal_radius if goal_radius_m is None else goal_radius_m
        self.waypoint_reach_radius_m = max(
            self.goal_radius_m,
            min(self.corridor_width_m * 0.75, 1.5),
        )
        self.time_scale = resolved_time_scale
        self.grayscale = grayscale
        self.img_size = img_size
        self._channels = 1 if grayscale else 3

        self._reset_config["timeScale"] = self.time_scale
        if resolved_track_id is not None:
            self._reset_config["selectedTrackId"] = resolved_track_id
        if resolved_vehicle_id is not None:
            self._reset_config["selectedVehicleId"] = resolved_vehicle_id

        if waypoints is not None:
            self.waypoints: list[tuple[float, float]] = waypoints
        elif scenario_waypoints:
            self.waypoints = scenario_waypoints
        else:
            self.waypoints = [
                (0.0, -7.5),
                (0.0, -1.0),
                (6.0, -1.0),
                (6.0, 5.0),
            ]
        self.total_route_length = self._compute_route_length()

        # ArUco goal detection (optional — runs alongside policy)
        self._aruco_detector = None
        self._aruco_goal = aruco_goal
        if aruco_goal:
            try:
                from sim_client.aruco_detector import ArucoGoalDetector
                self._aruco_detector = ArucoGoalDetector(
                    marker_size_m=0.12,
                    goal_distance_m=aruco_goal_distance_m,
                )
            except ImportError:
                pass  # OpenCV not installed — ArUco disabled

        # Observation space: dict with image + ultrasonic
        self.observation_space = spaces.Dict({
            "image": spaces.Box(
                low=0, high=255,
                shape=(self.img_size, self.img_size, self._channels),
                dtype=np.uint8,
            ),
            "ultrasonic": spaces.Box(
                low=0.0, high=1.0, shape=(1,), dtype=np.float32,
            ),
        })

        self.action_space = spaces.Box(
            low=-1.0, high=1.0, shape=(2,), dtype=np.float32
        )

        self._step_count = 0
        self._prev_progress = 0.0
        self._prev_steer = 0.0
        self._episode_seed = 0
        self._prev_pos = {"x": 0.0, "y": 0.0, "z": 0.0}
        self._computed_speed = 0.0
        self._reached_waypoints: set[int] = set()
        self._stalled_steps = 0
        self._last_termination_reason = "running"

    # ── route geometry ──

    @staticmethod
    def _parse_waypoints(items: Any) -> list[tuple[float, float]]:
        waypoints: list[tuple[float, float]] = []
        if not isinstance(items, list):
            return waypoints

        for item in items:
            if isinstance(item, dict):
                x = float(item.get("x", 0.0))
                z = float(item.get("z", 0.0))
                waypoints.append((x, z))
                continue

            if isinstance(item, (list, tuple)):
                if len(item) == 2:
                    waypoints.append((float(item[0]), float(item[1])))
                    continue
                if len(item) >= 3:
                    waypoints.append((float(item[0]), float(item[2])))

        return waypoints

    def _compute_route_length(self) -> float:
        total = 0.0
        for i in range(len(self.waypoints) - 1):
            ax, az = self.waypoints[i]
            bx, bz = self.waypoints[i + 1]
            total += math.hypot(bx - ax, bz - az)
        return max(total, 1.0)

    # ── gym interface ──

    def reset(self, *, seed: int | None = None, options: dict | None = None):
        super().reset(seed=seed)
        if seed is not None:
            self._episode_seed = seed
        else:
            self._episode_seed += 1

        config = dict(self._reset_config)
        config["seed"] = self._episode_seed

        step = self.client.reset(config)
        self._step_count = 0
        self._prev_progress = 0.0
        self._prev_steer = 0.0
        self._prev_pos = self._current_position(step)
        self._computed_speed = 0.0
        self._reached_waypoints = set()
        self._stalled_steps = 0
        self._last_termination_reason = "running"
        self._prime_reached_waypoints(self._prev_pos["x"], self._prev_pos["z"])
        self._prev_progress = self._route_progress(self._prev_pos["x"], self._prev_pos["z"])

        obs = self._build_observation(step)
        info = self._build_info(step)
        return obs, info

    def step(self, action: np.ndarray):
        throttle = float(np.clip(action[0], -1.0, 1.0))
        steer = float(np.clip(action[1], -1.0, 1.0))

        step = self.client.step({
            "throttle": throttle,
            "steer": steer,
            "brake": 0.0,
            "targetAgentId": "ego",
            "timestamp": 0,
            "timeBase": "unix_ms",
            "extensions": [],
        })
        self._step_count += 1
        self._update_kinematics(step)

        obs = self._build_observation(step)
        reward, terminated, truncated = self._compute_reward(step, steer)
        info = self._build_info(step)

        self._prev_steer = steer
        if self._step_count >= self.max_steps and not terminated:
            truncated = True

        return obs, reward, terminated, truncated, info

    # ── observation ──

    def _build_observation(self, step: dict[str, Any]) -> dict[str, Any]:
        # Camera image
        frame = step.get("frame") or {}
        image_b64 = frame.get("dataBase64", "")
        if image_b64:
            image = self._decode_and_resize(image_b64)
        else:
            image = np.zeros(
                (self.img_size, self.img_size, self._channels), dtype=np.uint8
            )

        # Ultrasonic
        tm = self._telemetry_map(step)
        front_dist = self._parse_float(tm, "sensor.ultrasonic.front.m") / 5.0
        ultrasonic = np.array([np.clip(front_dist, 0.0, 1.0)], dtype=np.float32)

        return {"image": image, "ultrasonic": ultrasonic}

    def _decode_and_resize(self, b64: str) -> np.ndarray:
        """Decode base64 PNG/JPG to numpy array, resize to img_size."""
        raw = base64.b64decode(b64)
        pil_img = Image.open(io.BytesIO(raw))

        if self.grayscale:
            pil_img = pil_img.convert("L")
        else:
            pil_img = pil_img.convert("RGB")

        pil_img = pil_img.resize((self.img_size, self.img_size), Image.BILINEAR)
        arr = np.array(pil_img, dtype=np.uint8)

        if self.grayscale:
            arr = arr[:, :, np.newaxis]  # (H, W, 1)

        return arr

    # ── kinematics ──

    def _update_kinematics(self, step: dict[str, Any]) -> None:
        pos = self._current_position(step)
        dx = pos["x"] - self._prev_pos["x"]
        dz = pos["z"] - self._prev_pos["z"]
        self._computed_speed = math.hypot(dx, dz)
        self._prev_pos = pos

    # ── reward ──

    def _compute_reward(self, step: dict[str, Any], steer: float):
        pos = self._current_position(step)
        px, pz = pos["x"], pos["z"]

        progress = self._route_progress(px, pz)
        delta_progress = progress - self._prev_progress
        self._prev_progress = progress
        progress_reward = delta_progress * 20.0

        # Waypoint bonuses
        waypoint_bonus = self._collect_waypoint_bonus(px, pz)

        # Lateral deviation penalty — harsh near walls to prevent wall-riding
        lateral_dist = self._nearest_route_distance(px, pz)
        half_corridor = self.corridor_width_m * 0.5
        wall_proximity = lateral_dist / half_corridor if half_corridor > 0 else 0.0
        # Quadratic base + sharp exponential penalty near walls (>70% to edge)
        lateral_penalty = -0.5 * wall_proximity ** 2
        if wall_proximity > 0.7:
            lateral_penalty -= 2.0 * (wall_proximity - 0.7) ** 2

        # Steer jerk penalty
        jerk_penalty = -0.05 * abs(steer - self._prev_steer)

        # Speed reward — only when moving away from walls (centered driving)
        center_bonus = max(0.0, 1.0 - wall_proximity * 2.0)  # 1.0 at center, 0 at halfway
        speed_reward = 0.1 * min(self._computed_speed / 0.05, 1.0) * (0.3 + 0.7 * center_bonus)

        # Time penalty
        time_penalty = -0.02

        # Goal
        terminated = False
        goal_bonus = 0.0
        termination_reason = "running"
        goal_x, goal_z = self.waypoints[-1]
        if math.hypot(px - goal_x, pz - goal_z) < self.goal_radius_m:
            goal_bonus = 100.0
            terminated = True
            termination_reason = "goal_reached"

        # OOB
        oob_penalty = 0.0
        if lateral_dist > self.oob_threshold_m:
            oob_penalty = -30.0
            terminated = True
            termination_reason = "out_of_bounds"

        if bool(step.get("done")) and not terminated:
            oob_penalty = -15.0
            terminated = True
            termination_reason = "runtime_done"

        # Stalled against wall / no useful movement
        stall_penalty = 0.0
        if not terminated:
            if self._step_count > 20 and self._computed_speed < 0.0015 and abs(delta_progress) < 1e-4:
                self._stalled_steps += 1
            else:
                self._stalled_steps = 0

            if self._stalled_steps >= 30:
                stall_penalty = -10.0
                terminated = True
                termination_reason = "stalled"
        else:
            self._stalled_steps = 0

        reward = (progress_reward + waypoint_bonus + lateral_penalty +
                  jerk_penalty + speed_reward + time_penalty +
                  goal_bonus + oob_penalty + stall_penalty)
        self._last_termination_reason = termination_reason
        return float(reward), terminated, False

    # ── route math ──

    def _route_progress(self, px: float, pz: float) -> float:
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

    def _nearest_route_distance(self, px: float, pz: float) -> float:
        best = float("inf")
        for i in range(len(self.waypoints) - 1):
            ax, az = self.waypoints[i]
            bx, bz = self.waypoints[i + 1]
            best = min(best, self._pt_seg_dist(px, pz, ax, az, bx, bz))
        return best

    @staticmethod
    def _project_t(px, pz, ax, az, bx, bz) -> float:
        abx, abz = bx - ax, bz - az
        ab2 = abx * abx + abz * abz
        if ab2 < 1e-9:
            return 0.0
        return max(0.0, min(1.0, ((px - ax) * abx + (pz - az) * abz) / ab2))

    @staticmethod
    def _pt_seg_dist(px, pz, ax, az, bx, bz) -> float:
        abx, abz = bx - ax, bz - az
        ab2 = abx * abx + abz * abz
        if ab2 < 1e-9:
            return math.hypot(px - ax, pz - az)
        t = max(0.0, min(1.0, ((px - ax) * abx + (pz - az) * abz) / ab2))
        return math.hypot(px - (ax + t * abx), pz - (az + t * abz))

    # ── helpers ──

    @staticmethod
    def _telemetry_map(step: dict[str, Any]) -> dict[str, str]:
        result = {}
        for item in ((step.get("state") or {}).get("telemetry") or []):
            if isinstance(item, dict):
                k = str(item.get("key", "")).strip()
                if k:
                    result[k] = str(item.get("value", ""))
        return result

    @staticmethod
    def _current_position(step: dict[str, Any]) -> dict[str, float]:
        p = ((step.get("state") or {}).get("pose") or {}).get("position") or {}
        return {"x": float(p.get("x", 0)), "y": float(p.get("y", 0)), "z": float(p.get("z", 0))}

    @staticmethod
    def _parse_float(m: dict[str, str], *keys: str) -> float:
        for k in keys:
            v = m.get(k)
            if v is not None:
                try:
                    return float(v)
                except ValueError:
                    pass
        return 0.0

    def _build_info(self, step: dict[str, Any]) -> dict[str, Any]:
        pos = self._current_position(step)
        info = {
            "position": pos,
            "progress": self._route_progress(pos["x"], pos["z"]),
            "lateral_distance": self._nearest_route_distance(pos["x"], pos["z"]),
            "step_count": self._step_count,
            "termination_reason": self._last_termination_reason,
            "reached_waypoints": len(self._reached_waypoints),
            "waypoint_reach_radius_m": self.waypoint_reach_radius_m,
            "stall_steps": self._stalled_steps,
        }

        # ArUco detection from camera frame (if enabled)
        if self._aruco_detector is not None:
            frame_b64 = (step.get("frame") or {}).get("dataBase64", "")
            if frame_b64:
                aruco = self._aruco_detector.detect_from_base64(frame_b64)
                info["aruco_detected"] = aruco.detected
                info["aruco_marker_id"] = aruco.marker_id
                info["aruco_distance_m"] = aruco.distance_m
                info["aruco_goal_reached"] = aruco.goal_reached
            else:
                info["aruco_detected"] = False
                info["aruco_goal_reached"] = False

        return info

    def _prime_reached_waypoints(self, px: float, pz: float) -> None:
        for wi in range(len(self.waypoints)):
            wx, wz = self.waypoints[wi]
            if math.hypot(px - wx, pz - wz) <= self.waypoint_reach_radius_m:
                self._reached_waypoints.add(wi)

    def _collect_waypoint_bonus(self, px: float, pz: float) -> float:
        waypoint_bonus = 0.0
        for wi in range(len(self.waypoints)):
            if wi in self._reached_waypoints:
                continue

            wx, wz = self.waypoints[wi]
            if math.hypot(px - wx, pz - wz) <= self.waypoint_reach_radius_m:
                self._reached_waypoints.add(wi)
                waypoint_bonus += 10.0

        return waypoint_bonus
