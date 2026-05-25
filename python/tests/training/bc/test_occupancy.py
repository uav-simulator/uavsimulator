"""Smoke tests for the ego-centric occupancy map module.

Covers:
- OccupancyMap shape/dtype contract
- update_from_pose marks the cell at the robot's position
- update_from_raycast marks free cells along the ray, wall cell at the hit
- ego_window returns a (3, 21, 21) view centered on the robot
- directional_distances_8 returns an (8,) vector in [0, 1]
- _synthetic_ultrasonic returns _ULTRASONIC_MAX_M when no walls are in front

These tests use only synthetic fixtures and run in milliseconds — no sim, no
disk I/O — so they catch regressions in the geometric/projection math
during refactoring of the rest of the stack.
"""
from __future__ import annotations

import numpy as np
import pytest

from training.bc.occupancy import (
    OccupancyMap,
    _synthetic_ultrasonic,
    directional_distances_8,
)


def test_empty_map_has_correct_shape_and_dtype():
    m = OccupancyMap.empty()
    assert m.grid.shape == (3, 80, 80)
    assert m.grid.dtype == np.float32
    assert np.all(m.grid == 0.0)


def test_update_from_pose_marks_visited_at_robot_cell():
    m = OccupancyMap.empty()
    m.update_from_pose(0.0, 0.0)
    # world (0, 0) lands at grid (40, 40); visited channel index = 0.
    assert m.grid[0, 40, 40] == 1.0
    # Other channels should still be zero.
    assert m.grid[1, 40, 40] == 0.0
    assert m.grid[2, 40, 40] == 0.0


def test_update_from_pose_outside_grid_is_silent():
    """Robot world position far outside the 18×18 m extent should not crash
    and should not write to the grid."""
    m = OccupancyMap.empty()
    m.update_from_pose(100.0, 100.0)  # way outside
    assert np.all(m.grid == 0.0)


def test_update_from_raycast_marks_wall_and_free_cells():
    """Robot at origin facing yaw=0 (along +Z), wall at 1.5 m ahead.

    Picks 1.5 m specifically so the ray's final cell (round(1.5/0.225) =
    7 → grid Z=47) does not collide with the second-to-last step's cell
    (the marcher's de-dup `if (gx, gz) == last_cell: continue` would otherwise
    skip the wall-marking branch).
    """
    m = OccupancyMap.empty()
    m.update_from_raycast(world_x=0.0, world_z=0.0, yaw_deg=0.0, ultrasonic_distance_m=1.5)
    # Wall channel should have non-zero mass somewhere.
    assert m.grid[2].sum() > 0.0, "wall channel should be marked at hit point"
    # Free channel should be marked along the ray.
    assert m.grid[1].sum() > 0.0, "free channel should be marked along the ray"


def test_update_from_raycast_wall_at_one_meter_marks_wall_cell():
    """Regression: wall at exactly 1.0 m straight ahead must be marked.

    Earlier, the marcher's `if (gx, gz) == last_cell: continue` de-dup
    short-circuited the terminal wall-marking branch when the ray's final
    cell coincided with the second-to-last step's cell. For a 1.0 m ray
    along +Z, step k=8 (t=0.9 m) and step k=9 (t=1.0 m) both round to grid
    Z=44, so the wall mark at cell (gx=40, gz=44) was silently dropped.
    """
    m = OccupancyMap.empty()
    m.update_from_raycast(world_x=0.0, world_z=0.0, yaw_deg=0.0, ultrasonic_distance_m=1.0)
    # Wall cell at +1.0 m straight ahead → grid (gx=40, gz=44).
    # grid is indexed [channel, gz, gx].
    assert m.grid[2, 44, 40] > 0.0, (
        "wall at 1.0 m ahead must mark grid[2, 44, 40]; "
        f"got {m.grid[2, 44, 40]} (channel sum {m.grid[2].sum()})"
    )


def test_update_from_raycast_max_range_marks_only_free():
    """When ultrasonic returns max range (no obstacle), no wall is recorded."""
    m = OccupancyMap.empty()
    m.update_from_raycast(world_x=0.0, world_z=0.0, yaw_deg=0.0, ultrasonic_distance_m=2.0)
    assert m.grid[1].sum() > 0.0, "free channel should be populated"
    assert m.grid[2].sum() == 0.0, "wall channel must stay empty at max range"


def test_synthetic_ultrasonic_no_walls_returns_max_range():
    """With no wall_cells, the synthetic raycast should max out."""
    dist = _synthetic_ultrasonic(0.0, 0.0, 0.0, wall_cells=set())
    assert dist == pytest.approx(2.0)


def test_synthetic_ultrasonic_with_wall_in_front_returns_finite_distance():
    """Place a wall cell directly in front of the robot."""
    # Robot at (0, 0), yaw=0 (facing +Z). Wall cell at maze grid (20, 22) →
    # world Z = (22 - 20) * 0.45 = 0.9 m.
    wall_cells = {(20, 22)}
    dist = _synthetic_ultrasonic(0.0, 0.0, 0.0, wall_cells=wall_cells)
    assert 0.0 < dist < 2.0


def test_directional_distances_8_shape_and_range():
    """8-direction raycast returns a normalised (8,) vector in [0, 1]."""
    out = directional_distances_8(0.0, 0.0, 0.0, wall_cells=set())
    assert out.shape == (8,)
    assert out.dtype == np.float32
    # No walls — all rays max out → all values == 1.0.
    assert np.all(out == 1.0)


def test_directional_distances_8_front_is_index_0():
    """Wall directly in front → index 0 (front) should be the smallest."""
    # Wall at world Z = +0.9 m, X = 0 — directly in front when yaw=0.
    wall_cells = {(20, 22)}  # maze grid (20, 22) = world (0, +0.9)
    out = directional_distances_8(0.0, 0.0, 0.0, wall_cells=wall_cells)
    # Front (index 0) should hit the wall; sides/back should miss.
    assert out[0] < 1.0, "front ray should hit the wall"
    # Back (index 4) should still max out.
    assert out[4] == 1.0, "back ray should not hit the wall in front"


def test_ego_window_shape_and_robot_at_center():
    """ego_window returns (3, 21, 21) with the robot's visited mark at the
    center cell."""
    m = OccupancyMap.empty()
    m.update_from_pose(0.0, 0.0)
    win = m.ego_window(0.0, 0.0, yaw_deg=0.0)
    assert win.shape == (3, 21, 21)
    assert win.dtype == np.float32
    # Robot is at (10, 10) in the ego window.
    assert win[0, 10, 10] == 1.0
