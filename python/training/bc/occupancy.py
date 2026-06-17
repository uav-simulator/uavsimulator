"""Ego-centric occupancy map builder for spatial memory.

Maintains a world-frame occupancy grid that accumulates information across an
episode and exports an ego-centric window centered on the robot, rotated so
the robot's heading points "up" in the extracted view. The window is the
extra observation modality consumed by the multi-modal BC + PPO policy
(see bc/trainer.py).

Three channels per cell:
  0 - visited           1.0 where the robot has been, 0 elsewhere
  1 - free_observed     [0..1] saturating count of times a raycast passed through this cell
  2 - wall_observed     [0..1] saturating count of times a raycast terminated at this cell

World extent: 18 m × 18 m at 0.225 m resolution → 80 × 80 grid. The maze
generator places the start at world (0, 0) and never strays beyond ±4 m;
this is comfortable margin.

Ego window: 21 × 21 cells (≈ 4.7 m × 4.7 m around the robot) at the same
0.225 m resolution. With the robot's local frame +X = right, +Z = forward,
we rotate so cell index `[0, 10]` is the cell directly behind the robot and
`[20, 10]` is the cell directly in front; cell `[10, 10]` is the robot's
own cell.

Sources of map updates:
  * `update_from_pose`     - mark the robot's current cell as visited.
  * `update_from_raycast`  - mark cells along a front ray as free up to the
                             ultrasonic-reported obstacle distance, and the
                             cell at the hit point as wall.

Offline reconstruction (`reconstruct_for_demo`) replays a recorded demo's
pose snapshots, computing a synthetic ultrasonic raycast against the
maze's known wall geometry at each tick — produces the same map a runtime
agent equipped with a perfect frontal ultrasonic would have observed,
without needing the snapshots themselves to log ultrasonic readings.
"""
from __future__ import annotations

import json
import math
from dataclasses import dataclass
from pathlib import Path

import numpy as np

_CELL_M = 0.225          # map resolution (half of corridor width 0.45 m)
_MAZE_CELL_M = 0.45      # the maze track's corridor width
_GRID_SIZE = 80          # 80 × 0.225 = 18 m world extent
_GRID_ORIGIN = 40        # world (0, 0) lands at map cell (40, 40)
_EGO_SIZE = 21           # 21 × 0.225 = 4.7 m ego window
_EGO_HALF = _EGO_SIZE // 2
_FREE_INCREMENT = 0.20   # per-raycast accumulation rate for free_observed
_WALL_INCREMENT = 0.40
_ULTRASONIC_MAX_M = 2.0  # KS0223 ultrasonic range


@dataclass
class OccupancyMap:
    """World-frame map state. 3 × _GRID_SIZE × _GRID_SIZE float32."""
    grid: np.ndarray  # shape (3, GRID_SIZE, GRID_SIZE)

    @classmethod
    def empty(cls) -> OccupancyMap:
        return cls(grid=np.zeros((3, _GRID_SIZE, _GRID_SIZE), dtype=np.float32))

    def _world_to_grid(self, world_x: float, world_z: float) -> tuple[int, int]:
        gx = int(round(world_x / _CELL_M)) + _GRID_ORIGIN
        gz = int(round(world_z / _CELL_M)) + _GRID_ORIGIN
        return gx, gz

    def update_from_pose(self, world_x: float, world_z: float) -> None:
        """Mark the cell currently occupied by the robot as visited."""
        gx, gz = self._world_to_grid(world_x, world_z)
        if 0 <= gx < _GRID_SIZE and 0 <= gz < _GRID_SIZE:
            self.grid[0, gz, gx] = 1.0

    def update_from_raycast(self, world_x: float, world_z: float, yaw_deg: float,
                            ultrasonic_distance_m: float) -> None:
        """Mark cells along a forward ray as free up to the obstacle distance,
        and the cell at the hit point as wall.

        The ray starts at the robot's current world position and points along
        the robot's heading (Unity yaw=0 → +Z). Cells are visited in order;
        when the ray runs out of free distance, the next cell is marked as
        wall (unless the ultrasonic returned its max range, in which case all
        cells along the ray stay free and no wall is recorded).
        """
        # Cap at the sensor's max range so we don't claim "free" for cells the
        # sensor never actually observed.
        d_total = min(ultrasonic_distance_m, _ULTRASONIC_MAX_M)
        # Direction vector in world frame. Unity yaw=0 → +Z, +90 → +X.
        yaw_rad = math.radians(yaw_deg)
        dx = math.sin(yaw_rad)
        dz = math.cos(yaw_rad)
        # March along ray in small steps (less than a cell to never skip a cell).
        step_m = _CELL_M * 0.5
        n_steps = int(math.ceil(d_total / step_m))
        last_cell: tuple[int, int] | None = None
        for k in range(1, n_steps + 1):
            t = min(k * step_m, d_total)
            wx = world_x + dx * t
            wz = world_z + dz * t
            gx, gz = self._world_to_grid(wx, wz)
            if not (0 <= gx < _GRID_SIZE and 0 <= gz < _GRID_SIZE):
                break
            if t >= d_total:
                # Terminal cell at ray-hit point — bypasses the last_cell dedup
                # so the wall mark isn't dropped when the ray's final step lands
                # in the same cell as the previous step (e.g. d_total = 1.0 m
                # along +Z: steps k=8 at t=0.9 and k=9 at t=1.0 both round to
                # the same grid cell).
                if ultrasonic_distance_m < _ULTRASONIC_MAX_M - 1e-3:
                    self.grid[2, gz, gx] = min(1.0, self.grid[2, gz, gx] + _WALL_INCREMENT)
                elif (gx, gz) != last_cell:
                    # No obstacle within range — treat last cell as free too
                    self.grid[1, gz, gx] = min(1.0, self.grid[1, gz, gx] + _FREE_INCREMENT)
                break
            if (gx, gz) == last_cell:
                continue
            last_cell = (gx, gz)
            # Free cell along ray
            self.grid[1, gz, gx] = min(1.0, self.grid[1, gz, gx] + _FREE_INCREMENT)

    def ego_window(self, world_x: float, world_z: float, yaw_deg: float) -> np.ndarray:
        """Extract a 21 × 21 × 3 window centered on the robot, rotated so the
        robot's heading is "up" in the returned view.

        Output shape: (3, _EGO_SIZE, _EGO_SIZE). The channel order matches the
        world grid (visited, free, wall). Returned dtype is float32.

        Rotation is by nearest-neighbour — for the BC student a coarse
        directional map is sufficient and the alternative (bilinear via torch
        grid_sample) blurs the visited/wall channels' sharp transitions in a
        way that hurts interpretability.
        """
        out = np.zeros((3, _EGO_SIZE, _EGO_SIZE), dtype=np.float32)
        gx0, gz0 = self._world_to_grid(world_x, world_z)
        cos_y = math.cos(math.radians(yaw_deg))
        sin_y = math.sin(math.radians(yaw_deg))
        # For each ego cell (ex, ez), figure out which world cell it maps to.
        # Ego frame: ex grows to the robot's right, ez grows forward.
        # We want the window oriented so ez=+EGO_HALF is directly in front.
        for ez in range(_EGO_SIZE):
            forward_offset = ez - _EGO_HALF        # negative = behind robot
            for ex in range(_EGO_SIZE):
                right_offset = ex - _EGO_HALF      # negative = to robot's left
                # Rotate offsets by yaw to get world-frame offsets.
                wx_off =  forward_offset * sin_y + right_offset * cos_y
                wz_off =  forward_offset * cos_y - right_offset * sin_y
                gx = gx0 + int(round(wx_off))
                gz = gz0 + int(round(wz_off))
                if 0 <= gx < _GRID_SIZE and 0 <= gz < _GRID_SIZE:
                    out[:, ez, ex] = self.grid[:, gz, gx]
        return out


def _synthetic_ultrasonic(
    world_x: float, world_z: float, yaw_deg: float, wall_cells: set[tuple[int, int]],
) -> float:
    """Compute the distance the front ultrasonic would report given a perfect
    forward ray traced against the maze's known wall cells.

    `wall_cells` are *maze* cells (45 cm pitch starting from the (20, 20)
    convention shared with CardboardMazeTrack and path_encoded). Returns the
    distance in meters in [0, _ULTRASONIC_MAX_M].
    """
    return _raycast_in_direction(world_x, world_z, yaw_deg, wall_cells)


def _raycast_in_direction(
    world_x: float, world_z: float, ray_yaw_deg: float, wall_cells: set[tuple[int, int]],
) -> float:
    """Single ray from (world_x, world_z) in world-frame heading ray_yaw_deg.
    Returns the distance in meters to the first wall cell hit, capped at
    _ULTRASONIC_MAX_M."""
    yaw_rad = math.radians(ray_yaw_deg)
    dx = math.sin(yaw_rad)
    dz = math.cos(yaw_rad)
    step_m = 0.05
    n_steps = int(_ULTRASONIC_MAX_M / step_m)
    for k in range(1, n_steps + 1):
        t = k * step_m
        wx = world_x + dx * t
        wz = world_z + dz * t
        cx = int(round(wx / _MAZE_CELL_M)) + 20
        cz = int(round(wz / _MAZE_CELL_M)) + 20
        if (cx, cz) in wall_cells:
            return t
    return _ULTRASONIC_MAX_M


# Eight directional offsets in robot-ego frame, in degrees.
# 0° = front, +45° each rotation clockwise:
#   front, front-right, right, back-right, back, back-left, left, front-left
_DIST_8_OFFSETS_DEG = (0.0, 45.0, 90.0, 135.0, 180.0, -135.0, -90.0, -45.0)


def directional_distances_8(
    world_x: float, world_z: float, yaw_deg: float, wall_cells: set[tuple[int, int]],
) -> np.ndarray:
    """8-direction raycast in the robot's ego frame, normalised to [0, 1] of
    _ULTRASONIC_MAX_M.

    Order of the returned vector:
        [0] front          (0°)
        [1] front-right    (+45°)
        [2] right          (+90°)
        [3] back-right     (+135°)
        [4] back           (180°)
        [5] back-left      (-135° = +225°)
        [6] left           (-90°)
        [7] front-left     (-45°)

    This gives the policy a complete instantaneous "lidar ring" of distances
    around the robot — a structured-perception alternative to having the
    network reverse-engineer the same information from raw pixels.
    """
    out = np.zeros(8, dtype=np.float32)
    for i, offset_deg in enumerate(_DIST_8_OFFSETS_DEG):
        d = _raycast_in_direction(world_x, world_z, yaw_deg + offset_deg, wall_cells)
        out[i] = min(1.0, d / _ULTRASONIC_MAX_M)
    return out


def _load_wall_cells_from_manifest(manifest_path: Path) -> set[tuple[int, int]]:
    """Read maze.path_encoded from a demo manifest and derive a 3-cell padded
    wall set around the path. Shared helper used by both the occupancy
    reconstructor and the directional-distance reconstructor."""
    manifest = json.loads(manifest_path.read_text())
    path_encoded = manifest["maze"]["path_encoded"]
    path_cells: set[tuple[int, int]] = {
        tuple(int(v) for v in p.split(",")) for p in path_encoded.split(";") if p
    }
    wall_cells: set[tuple[int, int]] = set()
    for (cx, cz) in path_cells:
        for dx in range(-3, 4):
            for dz in range(-3, 4):
                n = (cx + dx, cz + dz)
                if n not in path_cells:
                    wall_cells.add(n)
    return wall_cells


def reconstruct_distances_8_for_demo(
    jsonl_path: Path, manifest_path: Path,
) -> np.ndarray:
    """Replay a demo's pose snapshots and return a (T, 8) array of
    distances_8 vectors (front, FR, R, BR, back, BL, L, FL — normalised
    to [0, 1] of 2 m max range), aligned 1-to-1 with the MP4 frames.

    Used by the BC dataset loader to pair each demo command with the
    same 8-direction raycast the policy will see at runtime (which the
    EgoOccupancyMapWrapper computes online).
    """
    wall_cells = _load_wall_cells_from_manifest(manifest_path)
    out: list[np.ndarray] = []
    for raw in jsonl_path.read_text(encoding="utf-8").splitlines():
        line = raw.lstrip("﻿").strip()
        if not line:
            continue
        try:
            ev = json.loads(line)
        except json.JSONDecodeError:
            continue
        if ev.get("type") != "pose.snapshot":
            continue
        p = ev["payload"]
        wx, wz, yaw = float(p["pos_x"]), float(p["pos_z"]), float(p["yaw_deg"])
        out.append(directional_distances_8(wx, wz, yaw, wall_cells))
    if not out:
        return np.zeros((0, 8), dtype=np.float32)
    return np.stack(out, axis=0)


def reconstruct_for_demo(
    jsonl_path: Path, manifest_path: Path,
) -> np.ndarray:
    """Replay a recorded demo's pose snapshots and return a sequence of
    ego-centric occupancy windows aligned with the demo's MP4 frames.

    Output shape: (T, 3, _EGO_SIZE, _EGO_SIZE), where T equals the number
    of pose.snapshot events (= the number of MP4 frames the recorder wrote).
    """
    # Load manifest → path_cells, then derive wall_cells as the rectangular
    # neighbourhood of the path that the maze track *didn't* keep open.
    manifest = json.loads(manifest_path.read_text())
    path_encoded = manifest["maze"]["path_encoded"]
    path_cells: set[tuple[int, int]] = {
        tuple(int(v) for v in p.split(",")) for p in path_encoded.split(";") if p
    }
    # Wall set = every cell in a 5-cell padding around the path that isn't
    # itself in the path. We don't need the entire 40×40 maze grid populated
    # — only cells near the robot's possible trajectory matter for raycasts.
    wall_cells: set[tuple[int, int]] = set()
    for (cx, cz) in path_cells:
        for dx in range(-3, 4):
            for dz in range(-3, 4):
                neighbour = (cx + dx, cz + dz)
                if neighbour not in path_cells:
                    wall_cells.add(neighbour)

    # Stream pose snapshots in order, accumulating the world-frame map and
    # extracting the ego window at each tick.
    occ = OccupancyMap.empty()
    windows: list[np.ndarray] = []
    for raw in jsonl_path.read_text(encoding="utf-8").splitlines():
        line = raw.lstrip("﻿").strip()
        if not line:
            continue
        try:
            ev = json.loads(line)
        except json.JSONDecodeError:
            continue
        if ev.get("type") != "pose.snapshot":
            continue
        p = ev["payload"]
        wx, wz, yaw = float(p["pos_x"]), float(p["pos_z"]), float(p["yaw_deg"])
        occ.update_from_pose(wx, wz)
        d = _synthetic_ultrasonic(wx, wz, yaw, wall_cells)
        occ.update_from_raycast(wx, wz, yaw, d)
        windows.append(occ.ego_window(wx, wz, yaw))
    if not windows:
        return np.zeros((0, 3, _EGO_SIZE, _EGO_SIZE), dtype=np.float32)
    return np.stack(windows, axis=0)
