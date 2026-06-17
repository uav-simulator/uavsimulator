"""Gymnasium ObservationWrapper that adds the ego-centric occupancy map to
the observation dict.

The wrapper maintains an OccupancyMap instance for the lifetime of an
episode. On each `step`, it reads (x, z, yaw_deg) from the env's `info`
dict (populated by AbCorridorVisionEnv._build_info), updates the map with
the current pose + a synthetic frontal raycast against the maze geometry
(via training.bc.occupancy._synthetic_ultrasonic) — or, when wall
geometry is not available, against the env's reported ultrasonic
distance — and appends the ego-centric crop to the observation under
the key `occupancy`.

Observation space gains the `occupancy` Box (3, 21, 21) float32. Other
keys (`image`, `ultrasonic`) pass through unchanged. The wrapper plays
nicely with the existing wrapper stack:

    AbCorridorVisionEnv
        → ImageAugObservationWrapper
        → AntiSpinRewardWrapper
        → DelayedActionWrapper
        → DiscreteActionWrapper
        → EgoOccupancyMapWrapper      ← here, outermost so map is
                                         built on the original (un-
                                         augmented) physics positions.

The wrapper also exposes `current_ego_map()` and
`current_world_map()` so the WebUI / a third-party visualiser can pull
the latest map state for live overlay without coupling to the env step
loop.
"""
from __future__ import annotations

from typing import Any

import gymnasium as gym
import numpy as np
from gymnasium import spaces

from .occupancy import OccupancyMap, _synthetic_ultrasonic, directional_distances_8

_EGO_SIZE = 21
_ULTRASONIC_MAX_M = 2.0


class EgoOccupancyMapWrapper(gym.Wrapper):
    """Adds an ego-centric occupancy map to the env's observation dict."""

    def __init__(
        self,
        env: gym.Env,
        wall_cells: set[tuple[int, int]] | None = None,
        include_distances_8: bool = False,
    ):
        """
        Args:
            env: the underlying env (must produce Dict obs with at least
                 `image` and `ultrasonic` keys, and `info["position"]`,
                 `info["yaw_deg"]` per AbCorridorVisionEnv).
            wall_cells: optional set of (cell_x, cell_z) tuples identifying
                 maze wall cells (NOT path cells) in the 0.45 m grid frame
                 with origin at (20, 20). When provided, the wrapper uses
                 a synthetic perfect-knowledge ultrasonic against this set
                 for map updates. When None, falls back to the env-reported
                 ultrasonic value (which is already normalised to [0, 1]
                 and noisy under sim-to-real wrappers).
        """
        super().__init__(env)
        if not isinstance(env.observation_space, spaces.Dict):
            raise ValueError(
                f"EgoOccupancyMapWrapper expects Dict obs_space, got {type(env.observation_space)}"
            )
        new_spaces = dict(env.observation_space.spaces)
        new_spaces["occupancy"] = spaces.Box(
            low=0.0, high=1.0, shape=(3, _EGO_SIZE, _EGO_SIZE), dtype=np.float32,
        )
        if include_distances_8:
            new_spaces["distances_8"] = spaces.Box(
                low=0.0, high=1.0, shape=(8,), dtype=np.float32,
            )
        self.observation_space = spaces.Dict(new_spaces)
        self._wall_cells = wall_cells
        self._include_distances_8 = bool(include_distances_8)
        self._occupancy = OccupancyMap.empty()
        # Cached last-pose for the third-party `current_*_map()` accessors.
        # When the env hasn't been stepped yet, returns the zero map.
        self._last_pose: tuple[float, float, float] = (0.0, 0.0, 0.0)

    def reset(self, **kwargs):
        obs, info = self.env.reset(**kwargs)
        # Re-create map state for the new episode.
        self._occupancy = OccupancyMap.empty()
        wx, wz, yaw = self._read_pose(info)
        self._last_pose = (wx, wz, yaw)
        ultra_m = self._raycast_distance(wx, wz, yaw, obs)
        self._occupancy.update_from_pose(wx, wz)
        self._occupancy.update_from_raycast(wx, wz, yaw, ultra_m)
        obs = dict(obs)
        obs["occupancy"] = self._occupancy.ego_window(wx, wz, yaw)
        if self._include_distances_8:
            obs["distances_8"] = self._distances_8(wx, wz, yaw)
        return obs, info

    def step(self, action):
        obs, reward, terminated, truncated, info = self.env.step(action)
        wx, wz, yaw = self._read_pose(info)
        self._last_pose = (wx, wz, yaw)
        ultra_m = self._raycast_distance(wx, wz, yaw, obs)
        self._occupancy.update_from_pose(wx, wz)
        self._occupancy.update_from_raycast(wx, wz, yaw, ultra_m)
        obs = dict(obs)
        obs["occupancy"] = self._occupancy.ego_window(wx, wz, yaw)
        if self._include_distances_8:
            obs["distances_8"] = self._distances_8(wx, wz, yaw)
        return obs, reward, terminated, truncated, info

    # ── third-party accessors for live visualisation ──

    def current_ego_map(self) -> np.ndarray:
        """Return the latest 21×21×3 ego window (shape compatible with obs)."""
        wx, wz, yaw = self._last_pose
        return self._occupancy.ego_window(wx, wz, yaw)

    def current_world_map(self) -> np.ndarray:
        """Return the full world-frame map (3, 80, 80). For top-down
        visualisation showing the entire explored area at once.
        """
        return self._occupancy.grid.copy()

    def current_pose(self) -> tuple[float, float, float]:
        return self._last_pose

    # ── internal ──

    def _read_pose(self, info: dict[str, Any]) -> tuple[float, float, float]:
        pos = info.get("position") or {}
        wx = float(pos.get("x", 0.0))
        wz = float(pos.get("z", 0.0))
        yaw = float(info.get("yaw_deg", 0.0))
        return wx, wz, yaw

    def _raycast_distance(self, wx: float, wz: float, yaw: float, obs: dict) -> float:
        """Pick the most-informative ultrasonic estimate for the map update.

        Priority:
          1. Synthetic perfect raycast against `self._wall_cells` (clean,
             deterministic — used when the wrapper knows the maze topology).
          2. Env-reported ultrasonic in obs["ultrasonic"] (noisy under
             sim-to-real wrappers, but real-world realistic).
        """
        if self._wall_cells is not None:
            return _synthetic_ultrasonic(wx, wz, yaw, self._wall_cells)
        u = obs.get("ultrasonic")
        if u is not None:
            # obs["ultrasonic"] is normalised [0, 1] of range 0..5m in the env.
            return float(u[0]) * 5.0
        return _ULTRASONIC_MAX_M

    def _distances_8(self, wx: float, wz: float, yaw: float) -> np.ndarray:
        if self._wall_cells is not None:
            return directional_distances_8(wx, wz, yaw, self._wall_cells)
        return np.ones((8,), dtype=np.float32)
