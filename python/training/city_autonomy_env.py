"""City autonomy RL environment: waypoint-following on `track.city_polygon.v1`.

Phase 1 of the city autonomy training pipeline:

  - Vision-only observation (84x84x3 RGB) + 4-d navigation vector.
  - Continuous action: ``[throttle, steer]``, both in ``[-1, 1]``.
  - Reward: per-step waypoint progress + alive bonus + smooth-steering penalty,
    big bonuses on waypoint reach / route completion, big penalty on out-of-route.
  - Episode terminates on full-route completion, OOB, or step-budget exhaustion.

Traffic-light compliance is Phase 2 and requires Unity-side telemetry changes
(`traffic.next_light_state`, `traffic.next_light_distance_m`,
`traffic.violated_red_this_step`). See plan doc for the operator startup flow.
"""

from __future__ import annotations

import io
import math
import sys
from collections.abc import Mapping
from pathlib import Path
from typing import Any

import gymnasium as gym
import numpy as np
from gymnasium import spaces

PYTHON_ROOT = Path(__file__).resolve().parents[1]
if str(PYTHON_ROOT) not in sys.path:
    sys.path.insert(0, str(PYTHON_ROOT))

from sim_client.http_client import SimClient
from sim_client.scenario import load_scenario_file, scenario_to_reset_config

try:
    from PIL import Image
except ImportError:  # pragma: no cover — Pillow is in the `training` extra
    Image = None


IMG_SIZE = 84
DEFAULT_DISTANCE_NORMALISER_M = 30.0
WAYPOINT_REACH_RADIUS_M = 1.5
OOB_RADIUS_M = 5.0  # > this from any route waypoint → episode terminates
PROGRESS_CLIP_M = 1.0
DEFAULT_MAX_STEPS = 800


class CityAutonomyEnv(gym.Env):
    """Single-agent waypoint-following env on the city polygon scene.

    The agent reads its primary camera and a compact navigation vector
    (cos/sin of yaw error to next waypoint, normalised distance, fraction
    of waypoints remaining) and outputs throttle + steer. Reward is dense
    waypoint-progress with terminal bonuses and an out-of-route penalty.
    """

    metadata = {"render_modes": ["human"]}

    def __init__(
        self,
        base_url: str = "http://127.0.0.1:8000",
        scenario_path: str | Path | None = None,
        max_steps: int = DEFAULT_MAX_STEPS,
        distance_normaliser_m: float = DEFAULT_DISTANCE_NORMALISER_M,
        waypoint_reach_radius_m: float = WAYPOINT_REACH_RADIUS_M,
        oob_radius_m: float = OOB_RADIUS_M,
        time_scale: float = 2.0,
        client: SimClient | None = None,
        track_id: str = "track.city_polygon.v1",
        vehicle_id: str = "vehicle.arcade.blue.v1",
    ) -> None:
        super().__init__()
        if Image is None:
            raise RuntimeError("Pillow required: pip install -e 'python[training]'")

        self.client = client if client is not None else SimClient(base_url, timeout_s=30.0)
        self.max_steps = int(max_steps)
        self.distance_normaliser_m = float(distance_normaliser_m)
        self.waypoint_reach_radius_m = float(waypoint_reach_radius_m)
        self.oob_radius_m = float(oob_radius_m)

        # Scenario → reset config + waypoints.
        scenario_waypoints: list[tuple[float, float]] = []
        if scenario_path is not None:
            payload = load_scenario_file(scenario_path)
            self._reset_config = scenario_to_reset_config(payload)
            scenario_waypoints = self._parse_waypoints((payload.get("route") or {}).get("waypoints"))
        else:
            self._reset_config = {
                "seed": 0,
                "timeScale": time_scale,
                "selectedTrackId": track_id,
                "selectedVehicleId": vehicle_id,
                "trackParams": [],
                "vehicleParams": [{"key": "camera.profile", "value": "high"}],
                "flags": [],
            }

        if not scenario_waypoints:
            raise ValueError(
                "CityAutonomyEnv requires `scenario_path` with route.waypoints; "
                "supply one of configs/scenarios/city/city-*.yaml"
            )

        self._reset_config["timeScale"] = time_scale
        self._reset_config["selectedTrackId"] = track_id
        self._reset_config["selectedVehicleId"] = vehicle_id
        self._waypoints: np.ndarray = np.asarray(scenario_waypoints, dtype=np.float32)

        self.observation_space = spaces.Dict(
            {
                "image": spaces.Box(0, 255, (IMG_SIZE, IMG_SIZE, 3), dtype=np.uint8),
                "nav": spaces.Box(-1.0, 1.0, (4,), dtype=np.float32),
            }
        )
        self.action_space = spaces.Box(-1.0, 1.0, (2,), dtype=np.float32)

        # Per-episode state.
        self._step_idx = 0
        self._next_wp_idx = 0
        self._last_distance_to_next: float | None = None

    # ------------------------------------------------------------------
    # Gym surface
    # ------------------------------------------------------------------

    def reset(self, *, seed: int | None = None, options: dict | None = None):
        super().reset(seed=seed)
        if seed is not None:
            self._reset_config["seed"] = int(seed)

        step_result = self.client.reset(self._reset_config)

        self._step_idx = 0
        self._next_wp_idx = 0
        self._last_distance_to_next = self._distance_to_next_waypoint(step_result)

        obs = self._build_observation(step_result)
        info = {
            "nav.next_wp_idx": self._next_wp_idx,
            "nav.distance_m": self._last_distance_to_next,
            "nav.total_waypoints": int(self._waypoints.shape[0]),
        }
        return obs, info

    def step(self, action: np.ndarray):
        action = np.asarray(action, dtype=np.float32).reshape(-1)
        throttle = float(np.clip(action[0], -1.0, 1.0))
        steer = float(np.clip(action[1], -1.0, 1.0))

        cmd = {"throttle": throttle, "steer": steer}
        step_result = self.client.step(cmd)

        pose = self._extract_pose(step_result)
        # Advance waypoint index when we're inside the reach radius.
        distance_now = self._distance_to_next_waypoint(step_result)
        waypoint_reached = (
            distance_now is not None and distance_now <= self.waypoint_reach_radius_m
        )

        reward, term_reason = self._compute_reward(
            distance_now=distance_now,
            steer=steer,
            waypoint_reached=waypoint_reached,
            pose=pose,
        )

        if waypoint_reached:
            self._next_wp_idx += 1
            distance_now = self._distance_to_next_waypoint(step_result)

        self._last_distance_to_next = distance_now
        self._step_idx += 1

        terminated = term_reason in ("route_complete", "out_of_route")
        truncated = self._step_idx >= self.max_steps and not terminated

        obs = self._build_observation(step_result)
        info = {
            "nav.next_wp_idx": self._next_wp_idx,
            "nav.distance_m": distance_now,
            "nav.total_waypoints": int(self._waypoints.shape[0]),
            "termination": term_reason,
        }
        return obs, float(reward), bool(terminated), bool(truncated), info

    # ------------------------------------------------------------------
    # Observation
    # ------------------------------------------------------------------

    def _build_observation(self, step_result: Mapping[str, Any]) -> dict[str, np.ndarray]:
        return {
            "image": self._extract_image(step_result),
            "nav": self._build_nav_vector(step_result),
        }

    def _extract_image(self, step_result: Mapping[str, Any]) -> np.ndarray:
        state = step_result.get("state") if isinstance(step_result, Mapping) else None
        camera = state.get("camera") if isinstance(state, Mapping) else None
        frame = camera.get("frame") if isinstance(camera, Mapping) else None
        data_b64 = frame.get("dataBase64") if isinstance(frame, Mapping) else None
        if not isinstance(data_b64, str) or not data_b64:
            return np.zeros((IMG_SIZE, IMG_SIZE, 3), dtype=np.uint8)
        import base64

        try:
            raw = base64.b64decode(data_b64)
            with Image.open(io.BytesIO(raw)) as image:
                rgb = image.convert("RGB").resize((IMG_SIZE, IMG_SIZE), Image.BILINEAR)
                return np.asarray(rgb, dtype=np.uint8)
        except Exception:
            return np.zeros((IMG_SIZE, IMG_SIZE, 3), dtype=np.uint8)

    def _build_nav_vector(self, step_result: Mapping[str, Any]) -> np.ndarray:
        if self._next_wp_idx >= len(self._waypoints):
            return np.zeros(4, dtype=np.float32)

        pose = self._extract_pose(step_result)
        if pose is None:
            return np.zeros(4, dtype=np.float32)

        agent_x, agent_z, yaw_rad = pose
        wp_x, wp_z = self._waypoints[self._next_wp_idx]

        # Yaw error: angle from agent's forward to vector pointing at next waypoint.
        # In Unity convention, yaw=0 means facing +Z, so atan2(dx, dz) gives bearing.
        bearing = math.atan2(wp_x - agent_x, wp_z - agent_z)
        yaw_error = self._wrap_angle(bearing - yaw_rad)
        distance = math.hypot(wp_x - agent_x, wp_z - agent_z)
        normalised_distance = float(np.clip(distance / self.distance_normaliser_m, 0.0, 1.0))

        total = max(1, len(self._waypoints))
        remaining_frac = float(np.clip(1.0 - (self._next_wp_idx / total), 0.0, 1.0))

        return np.array(
            [
                math.cos(yaw_error),
                math.sin(yaw_error),
                normalised_distance,
                remaining_frac,
            ],
            dtype=np.float32,
        )

    # ------------------------------------------------------------------
    # Reward
    # ------------------------------------------------------------------

    def _compute_reward(
        self,
        *,
        distance_now: float | None,
        steer: float,
        waypoint_reached: bool,
        pose: tuple[float, float, float] | None,
    ) -> tuple[float, str | None]:
        # Route fully completed.
        if self._next_wp_idx >= len(self._waypoints):
            return 5.0, "route_complete"

        # Default per-step reward: progress + alive – steering penalty.
        progress = 0.0
        if distance_now is not None and self._last_distance_to_next is not None:
            delta = self._last_distance_to_next - distance_now
            progress = float(np.clip(delta, -PROGRESS_CLIP_M, PROGRESS_CLIP_M))

        reward = progress + 0.05 - 0.02 * (steer * steer)

        if waypoint_reached:
            reward += 1.0
            if self._next_wp_idx + 1 >= len(self._waypoints):
                reward += 5.0
                return reward, "route_complete"
            return reward, None

        # Out-of-route: min distance to ANY route waypoint exceeds threshold.
        # Being closer to a future waypoint than the nominal next one still
        # counts as on-route — useful when we naturally overshoot a corner.
        if pose is not None:
            min_dist = self._min_distance_to_any_waypoint_from(pose[0], pose[1])
            if min_dist > self.oob_radius_m:
                return reward - 5.0, "out_of_route"

        return reward, None

    # ------------------------------------------------------------------
    # Helpers (pure — easy to unit-test)
    # ------------------------------------------------------------------

    def _distance_to_next_waypoint(self, step_result: Mapping[str, Any]) -> float | None:
        if self._next_wp_idx >= len(self._waypoints):
            return None
        pose = self._extract_pose(step_result)
        if pose is None:
            return None
        wp_x, wp_z = self._waypoints[self._next_wp_idx]
        return float(math.hypot(wp_x - pose[0], wp_z - pose[1]))

    def _min_distance_to_any_waypoint_from(self, agent_x: float, agent_z: float) -> float:
        """Smallest distance from the agent to ANY waypoint on the route.

        Used by the OOB termination check — being closer to a downstream
        waypoint still counts as on-route, even if we overshot the nominal
        next one. ``inf`` returned only when the route is empty.
        """
        if len(self._waypoints) == 0:
            return float("inf")
        diffs = self._waypoints - np.array([agent_x, agent_z], dtype=np.float32)
        return float(np.min(np.hypot(diffs[:, 0], diffs[:, 1])))

    @staticmethod
    def _extract_pose(step_result: Mapping[str, Any]) -> tuple[float, float, float] | None:
        if not isinstance(step_result, Mapping):
            return None
        state = step_result.get("state")
        if not isinstance(state, Mapping):
            return None
        pose = state.get("pose")
        if not isinstance(pose, Mapping):
            return None
        position = pose.get("position") or {}
        rotation = pose.get("rotation") or {}
        try:
            x = float(position.get("x", 0.0))
            z = float(position.get("z", 0.0))
            # Unity Quaternion → yaw around Y. step result publishes Euler angles
            # under rotation.eulerY (deg) when present; fall back to 0.
            yaw_deg = float(rotation.get("eulerY", rotation.get("y", 0.0)))
            yaw_rad = math.radians(yaw_deg)
        except (TypeError, ValueError):
            return None
        return x, z, yaw_rad

    @staticmethod
    def _wrap_angle(angle: float) -> float:
        while angle > math.pi:
            angle -= 2 * math.pi
        while angle < -math.pi:
            angle += 2 * math.pi
        return angle

    @staticmethod
    def _parse_waypoints(raw: Any) -> list[tuple[float, float]]:
        if not isinstance(raw, list):
            return []
        out: list[tuple[float, float]] = []
        for item in raw:
            if isinstance(item, (list, tuple)) and len(item) >= 2:
                try:
                    out.append((float(item[0]), float(item[-1])))
                except (TypeError, ValueError):
                    continue
            elif isinstance(item, Mapping):
                try:
                    x = float(item.get("x", 0.0))
                    z = float(item.get("z", 0.0))
                    out.append((x, z))
                except (TypeError, ValueError):
                    continue
        return out
