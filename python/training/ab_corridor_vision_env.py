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
        max_steps: int = 600,
        corridor_width_m: float = 3.0,
        oob_margin_m: float = 0.3,
        goal_radius_m: float = 1.5,
        time_scale: float = 2.0,
        grayscale: bool = False,
        img_size: int = IMG_SIZE,
    ):
        super().__init__()

        assert Image is not None, "Pillow is required: pip install Pillow"

        self.client = SimClient(base_url, timeout_s=30.0)
        self.max_steps = max_steps
        self.corridor_width_m = corridor_width_m
        self.oob_margin_m = oob_margin_m
        self.oob_threshold_m = (corridor_width_m * 0.5) + oob_margin_m
        self.goal_radius_m = goal_radius_m
        self.time_scale = time_scale
        self.grayscale = grayscale
        self.img_size = img_size
        self._channels = 1 if grayscale else 3

        self._reset_config = {
            "seed": 0,
            "timeScale": time_scale,
            "selectedTrackId": "track.basic_arena.v1",
            "selectedVehicleId": "vehicle.prometeo.sport.v1",
            "trackParams": [],
            "vehicleParams": [{"key": "camera.profile", "value": "high"}],
            "flags": [],
        }

        # S-shape route waypoints matching BasicArenaTrack geometry
        self.waypoints: list[tuple[float, float]] = [
            (0.0, -7.5),
            (0.0, -1.0),
            (6.0, -1.0),
            (6.0, 5.0),
        ]
        self.total_route_length = self._compute_route_length()

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

    # ── route geometry ──

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
        waypoint_bonus = 0.0
        for wi in range(len(self.waypoints)):
            if wi not in self._reached_waypoints:
                wx, wz = self.waypoints[wi]
                if math.hypot(px - wx, pz - wz) < 1.5:
                    self._reached_waypoints.add(wi)
                    waypoint_bonus += 10.0

        # Lateral deviation penalty
        lateral_dist = self._nearest_route_distance(px, pz)
        lateral_penalty = -0.3 * (lateral_dist / self.oob_threshold_m) ** 2

        # Steer jerk penalty
        jerk_penalty = -0.05 * abs(steer - self._prev_steer)

        # Speed reward
        speed_reward = 0.1 * min(self._computed_speed / 0.05, 1.0)

        # Time penalty
        time_penalty = -0.02

        # Goal
        terminated = False
        goal_bonus = 0.0
        goal_x, goal_z = self.waypoints[-1]
        if math.hypot(px - goal_x, pz - goal_z) < self.goal_radius_m:
            goal_bonus = 100.0
            terminated = True

        # OOB
        oob_penalty = 0.0
        if lateral_dist > self.oob_threshold_m:
            oob_penalty = -30.0
            terminated = True

        if bool(step.get("done")) and not terminated:
            oob_penalty = -15.0
            terminated = True

        reward = (progress_reward + waypoint_bonus + lateral_penalty +
                  jerk_penalty + speed_reward + time_penalty +
                  goal_bonus + oob_penalty)
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
        return {
            "position": pos,
            "progress": self._route_progress(pos["x"], pos["z"]),
            "lateral_distance": self._nearest_route_distance(pos["x"], pos["z"]),
            "step_count": self._step_count,
        }
