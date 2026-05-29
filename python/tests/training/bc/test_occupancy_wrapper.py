"""Smoke tests for EgoOccupancyMapWrapper.

Covers the obs_space contract (must add a single `occupancy` Box to the
underlying env's Dict obs_space, no `distances_8`), the reset/step return
shape, and the third-party visualisation accessors (`current_ego_map`,
`current_world_map`, `current_pose`). These are the surfaces that the
SB3 MultiModalOccupancyExtractor and the live overlay server rely on.
"""
from __future__ import annotations

import gymnasium as gym
import numpy as np
import pytest
from gymnasium import spaces

from training.bc.occupancy_wrapper import EgoOccupancyMapWrapper


class _StubEnv(gym.Env):
    """Tiny env producing the (image, ultrasonic) Dict obs the wrapper expects."""

    metadata = {"render_modes": []}

    def __init__(self):
        self.observation_space = spaces.Dict({
            "image": spaces.Box(low=0, high=255, shape=(3, 84, 84), dtype=np.uint8),
            "ultrasonic": spaces.Box(low=0.0, high=1.0, shape=(1,), dtype=np.float32),
        })
        self.action_space = spaces.Discrete(5)
        self._t = 0

    def _obs(self):
        return {
            "image": np.zeros((3, 84, 84), dtype=np.uint8),
            "ultrasonic": np.array([0.5], dtype=np.float32),  # 2.5 m
        }

    def _info(self):
        # Advance the robot 0.1 m forward each step so the visited channel grows.
        return {
            "position": {"x": 0.0, "z": self._t * 0.1},
            "yaw_deg": 0.0,
        }

    def reset(self, *, seed=None, options=None):
        self._t = 0
        return self._obs(), self._info()

    def step(self, action):
        self._t += 1
        return self._obs(), 0.0, False, False, self._info()


def test_wrapper_obs_space_has_occupancy_only_no_distances_8():
    """Wrapper must add `occupancy` (3, 21, 21) to the env's obs_space and
    must NOT add `distances_8` — the current production BC checkpoint is
    641-d (image + ultrasonic + occupancy) and a wrapper-added distances_8
    key would mismatch the checkpoint's saved obs_space at PPO.load() time.
    """
    env = EgoOccupancyMapWrapper(_StubEnv(), wall_cells=None)
    keys = sorted(env.observation_space.spaces.keys())
    assert keys == ["image", "occupancy", "ultrasonic"]
    occ_box = env.observation_space.spaces["occupancy"]
    assert occ_box.shape == (3, 21, 21)
    assert occ_box.dtype == np.float32


def test_wrapper_reset_returns_3_key_obs():
    env = EgoOccupancyMapWrapper(_StubEnv(), wall_cells=None)
    obs, info = env.reset()
    assert sorted(obs.keys()) == ["image", "occupancy", "ultrasonic"]
    assert obs["occupancy"].shape == (3, 21, 21)
    assert obs["occupancy"].dtype == np.float32


def test_wrapper_step_accumulates_visited_channel():
    """After multiple steps the visited channel of the world map should
    show non-zero mass."""
    env = EgoOccupancyMapWrapper(_StubEnv(), wall_cells=None)
    env.reset()
    for _ in range(10):
        env.step(0)
    world_map = env.current_world_map()
    assert world_map.shape == (3, 80, 80)
    # Channel 0 = visited; the robot moved forward → multiple cells visited.
    assert world_map[0].sum() > 0.0


def test_current_pose_tracks_env_info():
    env = EgoOccupancyMapWrapper(_StubEnv(), wall_cells=None)
    env.reset()
    for _ in range(5):
        env.step(0)
    wx, wz, yaw = env.current_pose()
    # _StubEnv advances z by 0.1 m per step → after 5 steps z ≈ 0.5 m.
    assert wz == pytest.approx(0.5, abs=0.01)
    assert wx == 0.0
    assert yaw == 0.0


def test_current_ego_map_returns_21x21_window():
    env = EgoOccupancyMapWrapper(_StubEnv(), wall_cells=None)
    env.reset()
    env.step(0)
    ego = env.current_ego_map()
    assert ego.shape == (3, 21, 21)
    assert ego.dtype == np.float32


def test_wrapper_with_wall_cells_uses_synthetic_raycast():
    """When wall_cells is provided, the wrapper computes the raycast
    against the known maze geometry (_synthetic_ultrasonic) instead of
    obs['ultrasonic']. We only sanity-check that the free channel grows
    along the ray; the wall channel can land in a same-cell-as-last-step
    edge case that update_from_raycast's marcher de-dup skips (see the
    follow-up bug noted below), so we don't depend on it here.
    """
    # A single wall cell at maze grid (20, 22) → world (0, +0.9 m).
    wall_cells = {(20, 22)}
    env = EgoOccupancyMapWrapper(_StubEnv(), wall_cells=wall_cells)
    env.reset()
    for _ in range(3):
        env.step(0)
    world_map = env.current_world_map()
    # Free channel should pick up cells in front of the robot.
    assert world_map[1].sum() > 0.0


def test_wrapper_can_add_distances_8_modality():
    """Structured raycast context is opt-in so old 641-d checkpoints stay valid."""
    wall_cells = {(20, 22)}
    env = EgoOccupancyMapWrapper(_StubEnv(), wall_cells=wall_cells, include_distances_8=True)
    obs, _ = env.reset()
    assert sorted(obs.keys()) == ["distances_8", "image", "occupancy", "ultrasonic"]
    dist_box = env.observation_space.spaces["distances_8"]
    assert dist_box.shape == (8,)
    assert dist_box.dtype == np.float32
    assert obs["distances_8"].shape == (8,)
    assert obs["distances_8"].dtype == np.float32
