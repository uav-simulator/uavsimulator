"""Hand-curated maze layouts for BC demo recording.

The procedural maze-gen + algorithmic auto-pilot pilot produced
data where the BC student couldn't learn turn timing (the auto-pilot
decides on ground-truth pose, the BC sees only camera + ultrasonic;
visually similar frames map to different actions depending on a pose
variable the student can't see).

This module replaces the procedural layouts with a small fixed set
of designed layouts covering a variety of turn topologies. Each
layout is short enough (≤13 cells × 0.45 m = ≤5.85 m) for the
auto-pilot to complete in well under 400 steps so every recorded
demo reaches the goal, and varied enough that the dataset has
balanced (Forward, Left, Right) action coverage across episodes.

Coordinate frame: the C# maze generator places start at grid cell
(20, 20) with the agent facing +Z (North). Cells are
0.45 m × 0.45 m on the world plane.

Adding a new layout: pick a connected cell sequence starting at
(20, 20), ensure no cell is repeated, keep length ≤25 cells, and
prefer trajectories with at least one direction change so the BC
sees turning labels.
"""
from __future__ import annotations


# 10 curated layouts. Each is a (name, encoded-path) pair; the name
# becomes the demo tag (filename suffix) and the encoded-path is
# what CardboardMazeTrack reads as maze.path_encoded.
CURATED_MAZE_PATHS: list[tuple[str, str]] = [
    # 1. Simple right L — straight then one right turn.
    ("01-right-L", "20,20;20,21;20,22;20,23;20,24;20,25;21,25;22,25;23,25;24,25;25,25"),
    # 2. Simple left L — mirror of #1.
    ("02-left-L", "20,20;20,21;20,22;20,23;20,24;20,25;19,25;18,25;17,25;16,25;15,25"),
    # 3. Right U — straight, right, straight, right (180° reversal).
    ("03-right-U", "20,20;20,21;20,22;20,23;21,23;22,23;22,22;22,21;22,20;22,19;22,18"),
    # 4. Left U — mirror of #3.
    ("04-left-U", "20,20;20,21;20,22;20,23;19,23;18,23;18,22;18,21;18,20;18,19;18,18"),
    # 5. Right-then-left S — first right, recover with left.
    ("05-S-curve", "20,20;20,21;20,22;21,22;22,22;22,23;22,24;22,25;22,26;22,27"),
    # 6. Left-then-right Z — mirror of #5.
    ("06-Z-curve", "20,20;20,21;20,22;19,22;18,22;18,23;18,24;18,25;18,26;18,27"),
    # 7. Right-leaning stair — RLRL (stairstep up-right).
    ("07-right-stair", "20,20;20,21;20,22;20,23;20,24;21,24;22,24;22,25;22,26;23,26;24,26;24,27;24,28"),
    # 8. Left-leaning stair — LRLR (mirror of #7).
    ("08-left-stair", "20,20;20,21;20,22;20,23;20,24;19,24;18,24;18,25;18,26;17,26;16,26;16,27;16,28"),
    # 9. Right loop ~270° — three consecutive right turns, loops back near start.
    ("09-right-loop", "20,20;20,21;20,22;20,23;21,23;22,23;23,23;23,22;23,21;22,21;21,21"),
    # 10. Long winding LLRR — long straight then two-left U then two-left return.
    ("10-long-winding", "20,20;20,21;20,22;20,23;20,24;20,25;19,25;18,25;18,24;18,23;17,23;16,23"),
]


def turn_sequence(path: str) -> list[str]:
    """Decode a path string into the sequence of cardinal moves between cells.

    Useful for diagnostics: returns ['N', 'N', 'E', ...] where each letter is
    the move from cell i to cell i+1. Mismatch with the expected adjacency
    (diagonal jumps, repeated cells) raises ValueError.
    """
    cells = [tuple(int(v) for v in pair.split(",")) for pair in path.split(";")]
    moves: list[str] = []
    for (x1, z1), (x2, z2) in zip(cells, cells[1:]):
        dx, dz = x2 - x1, z2 - z1
        if (dx, dz) == (0, 1): moves.append("N")
        elif (dx, dz) == (1, 0): moves.append("E")
        elif (dx, dz) == (0, -1): moves.append("S")
        elif (dx, dz) == (-1, 0): moves.append("W")
        else:
            raise ValueError(f"non-adjacent or diagonal move: ({x1},{z1}) -> ({x2},{z2})")
    return moves


def turn_count(path: str) -> tuple[int, int]:
    """Return (left_turn_count, right_turn_count) for a path."""
    moves = turn_sequence(path)
    # Cardinal-to-index: N=0, E=1, S=2, W=3. Right = +1, Left = -1, Forward = 0.
    cardinal = {"N": 0, "E": 1, "S": 2, "W": 3}
    left = right = 0
    for prev, cur in zip(moves, moves[1:]):
        delta = (cardinal[cur] - cardinal[prev]) % 4
        if delta == 1: right += 1
        elif delta == 3: left += 1
    return left, right
