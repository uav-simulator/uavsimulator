"""Python port of the C# MazeGenerator — identical output for same seed/params.

Used by training env to compute waypoints and goal locally, matching the
geometry built by Unity's CardboardMazeTrack.

Grid-based drunk-walk with turn budget and backtracking.
"""

from __future__ import annotations

import random
from dataclasses import dataclass, field

# Directions: 0=North (+Z), 1=East (+X), 2=South (-Z), 3=West (-X)
_DELTAS = [(0, 1), (1, 0), (0, -1), (-1, 0)]
_GRID_SIZE = 40
_START_X = 20
_START_Z = 20
_MAX_BACKTRACKS = 20


@dataclass
class MazeParams:
    seed: int = 42
    length_cells: int = 8
    corridor_width_m: float = 0.60
    left_turns: int = 2
    right_turns: int = 2
    wall_height_m: float = 0.25
    wall_thickness_m: float = 0.02


@dataclass
class MazeGeometry:
    path_cells: list[tuple[int, int]] = field(default_factory=list)
    waypoints: list[tuple[float, float]] = field(default_factory=list)  # world (x, z)
    spawn_xyz: tuple[float, float, float] = (0.0, 0.01, 0.0)
    spawn_yaw_deg: float = 0.0
    corridor_width_m: float = 0.60
    goal_radius_m: float = 0.24


def generate(params: MazeParams) -> MazeGeometry:
    """Generate maze geometry for given params. Deterministic by seed."""
    rng = random.Random(params.seed)
    path = [(_START_X, _START_Z)]
    occupied = {(_START_X, _START_Z)}
    direction = 0  # North
    left_budget = max(0, params.left_turns)
    right_budget = max(0, params.right_turns)
    turn_history: list[int] = []  # 0/+1/-1 per step
    fail_streak = 0
    max_length_seen = len(path)
    since_progress = 0
    target = max(2, min(200, params.length_cells))
    max_iter = target * 50
    iter_count = 0

    while len(path) < target:
        iter_count += 1
        if iter_count > max_iter:
            raise RuntimeError(
                f"Maze generation aborted after {iter_count} iterations for "
                f"seed={params.seed} length={target} turns=L{params.left_turns}/R{params.right_turns}"
            )
        last_x, last_z = path[-1]
        # Candidates: forward (0), left (-1) if budget, right (+1) if budget
        candidates: list[tuple[int, int]] = [(direction, 0)]
        if left_budget > 0:
            candidates.append(((direction + 3) % 4, -1))
        if right_budget > 0:
            candidates.append(((direction + 1) % 4, +1))

        # Filter by validity
        valid: list[tuple[int, int]] = []
        for d, t in candidates:
            dx, dz = _DELTAS[d]
            nx, nz = last_x + dx, last_z + dz
            if 0 <= nx < _GRID_SIZE and 0 <= nz < _GRID_SIZE and (nx, nz) not in occupied:
                valid.append((d, t))

        if not valid:
            if len(path) <= 1:
                raise RuntimeError("Maze generation failed: immediate dead end")
            # Backtrack
            dropped_cell = path.pop()
            occupied.discard(dropped_cell)
            dropped_turn = turn_history.pop()
            if dropped_turn == -1:
                left_budget += 1
            elif dropped_turn == +1:
                right_budget += 1
            # Rewind direction
            direction = 0
            for t in turn_history:
                if t == -1:
                    direction = (direction + 3) % 4
                elif t == +1:
                    direction = (direction + 1) % 4
            fail_streak += 1
            since_progress += 1
            if fail_streak > _MAX_BACKTRACKS:
                raise RuntimeError("Maze generation failed: too many backtracks")
            if since_progress > _MAX_BACKTRACKS * 2:
                raise RuntimeError(
                    f"Maze generation stalled: reached {max_length_seen}/{target} cells, "
                    f"no progress in {since_progress} steps"
                )
            continue

        # Weighted pick: forward=2, turn=1
        weights = [2 if t == 0 else 1 for _, t in valid]
        total = sum(weights)
        roll = rng.randint(0, total - 1)
        cursor = 0
        chosen = valid[0]
        for (d, t), w in zip(valid, weights):
            cursor += w
            if roll < cursor:
                chosen = (d, t)
                break

        d, t = chosen
        dx, dz = _DELTAS[d]
        nx, nz = last_x + dx, last_z + dz
        path.append((nx, nz))
        occupied.add((nx, nz))
        turn_history.append(t)
        direction = d
        if t == -1:
            left_budget -= 1
        elif t == +1:
            right_budget -= 1
        fail_streak = 0
        if len(path) > max_length_seen:
            max_length_seen = len(path)
            since_progress = 0

    return _build_geometry(path, params)


def build_from_encoded_path(encoded: str, params: MazeParams) -> MazeGeometry:
    """Build maze geometry from explicit ``x,z;x,z;...`` path cells."""
    path: list[tuple[int, int]] = []
    for part in encoded.split(";"):
        if not part.strip():
            continue
        coords = part.split(",")
        if len(coords) != 2:
            continue
        try:
            path.append((int(coords[0].strip()), int(coords[1].strip())))
        except ValueError:
            continue

    if len(path) < 2:
        raise ValueError(f"path_encoded has too few cells: {encoded!r}")

    return _build_geometry(path, params)


def _build_geometry(path: list[tuple[int, int]], p: MazeParams) -> MazeGeometry:
    cell = p.corridor_width_m
    waypoints = [
        ((gx - _START_X) * cell, (gz - _START_Z) * cell) for (gx, gz) in path
    ]

    # Spawn
    first = path[0]
    second = path[1] if len(path) > 1 else first
    spawn_xyz = ((first[0] - _START_X) * cell, 0.01, (first[1] - _START_Z) * cell)
    spawn_direction = _direction_from_cells(first, second)
    spawn_yaw_deg = _dir_to_yaw_deg(spawn_direction)

    return MazeGeometry(
        path_cells=path,
        waypoints=waypoints,
        spawn_xyz=spawn_xyz,
        spawn_yaw_deg=spawn_yaw_deg,
        corridor_width_m=cell,
        goal_radius_m=cell * 0.4,
    )


def _direction_from_cells(a: tuple[int, int], b: tuple[int, int]) -> int:
    if b[1] > a[1]: return 0  # North
    if b[0] > a[0]: return 1  # East
    if b[1] < a[1]: return 2  # South
    return 3  # West


def _dir_to_yaw_deg(d: int) -> float:
    return {0: 0.0, 1: 90.0, 2: 180.0, 3: 270.0}[d]
