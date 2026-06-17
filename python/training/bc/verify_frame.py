"""Forensics: for a given (jsonl, frame_index), where was the robot and
how close was it to a maze wall?

Answers questions like "did the robot actually clip into a wall at second 8
of the left-U demo, or is the see-through-wall thing just a Unity camera
near-plane rendering artifact?".

Requires the JSONL to contain `pose.snapshot` events (auto_record writes
these alongside each `command.outgoing`). The `step_index` in each
pose.snapshot lines up 1:1 with the MP4 frame written by auto_record's
video writer.

Usage:
    python -m training.bc.verify_frame \\
        --jsonl docs/.../session_..._bc-maze-curated-04-left-U.jsonl \\
        --frame 64

The wall distance is computed by checking the robot's position against
each path-cell center: cells in the path are corridor, every other cell
is wall. A robot inside a path cell is "in the corridor"; one that has
strayed into a non-path cell is "in a wall" (which would be a real
physics violation, not just a render artifact).
"""
from __future__ import annotations

import argparse
import json
import math
from pathlib import Path

_CELL_M = 0.45
_START = (20, 20)


def _load_pose_snapshots(jsonl_path: Path) -> list[dict]:
    snaps: list[dict] = []
    for raw_line in jsonl_path.read_text(encoding="utf-8").splitlines():
        line = raw_line.lstrip("\ufeff").strip()
        if not line:
            continue
        try:
            ev = json.loads(line)
        except json.JSONDecodeError:
            continue
        if ev.get("type") == "pose.snapshot":
            snaps.append(ev["payload"])
    return snaps


def _load_path_cells_from_manifest(manifest_json: Path) -> set[tuple[int, int]]:
    d = json.loads(manifest_json.read_text())
    encoded = d.get("maze", {}).get("path_encoded", "")
    return {tuple(int(v) for v in p.split(",")) for p in encoded.split(";") if p}


def _classify(px: float, pz: float, path_cells: set[tuple[int, int]]) -> dict:
    """Where is the robot relative to maze cells?

    Returns dict with:
      cell                   — (gx, gz) grid cell containing the robot
      in_corridor            — True if that cell is a path cell
      dist_to_nearest_path_m — closest path-cell center
      dist_to_nearest_wall_m — closest non-path-cell center (gives "wall ahead" radius)
      offset_in_cell_m       — (dx, dz) from the cell center, magnitude < ~0.32 m if inside
    """
    gx = round(px / _CELL_M) + _START[0]
    gz = round(pz / _CELL_M) + _START[1]
    cx_world = (gx - _START[0]) * _CELL_M
    cz_world = (gz - _START[1]) * _CELL_M
    offset = (px - cx_world, pz - cz_world)
    nearest_path = min(
        (math.hypot(px - (cx - _START[0]) * _CELL_M, pz - (cz - _START[1]) * _CELL_M)
         for (cx, cz) in path_cells),
        default=float("inf"),
    )
    # Walls live at cell centers that are NOT path cells, but only consider
    # cells that are immediately adjacent (within 2 cells of the robot) —
    # walls far from the robot are not "near".
    nearest_wall = float("inf")
    for dx in range(-2, 3):
        for dz in range(-2, 3):
            c = (gx + dx, gz + dz)
            if c in path_cells:
                continue
            wx = (c[0] - _START[0]) * _CELL_M
            wz = (c[1] - _START[1]) * _CELL_M
            d = math.hypot(px - wx, pz - wz)
            nearest_wall = min(nearest_wall, d)
    return {
        "cell": (gx, gz),
        "in_corridor": (gx, gz) in path_cells,
        "dist_to_nearest_path_m": nearest_path,
        "dist_to_nearest_wall_m": nearest_wall,
        "offset_in_cell_m": offset,
    }


def main() -> None:
    p = argparse.ArgumentParser(description=__doc__)
    p.add_argument("--jsonl", type=Path, required=True)
    p.add_argument("--manifest", type=Path,
                   help="manifest_<tag>.json paired with this session — auto-detected if omitted")
    p.add_argument("--frame", type=int, help="Single frame index to inspect")
    p.add_argument("--range", type=str, help="Frame range, e.g. 60-70")
    args = p.parse_args()

    snaps = _load_pose_snapshots(args.jsonl)
    if not snaps:
        raise SystemExit(f"No pose.snapshot events in {args.jsonl} — was the demo recorded with the pose-logging build?")

    if args.manifest is None:
        # Tag is the trailing portion of the JSONL stem after the timestamp.
        parts = args.jsonl.stem.split("_")
        tag = "_".join(parts[3:]) if len(parts) >= 4 else None
        if tag:
            cand = args.jsonl.parent / f"manifest_{tag}.json"
            if cand.exists():
                args.manifest = cand
    if args.manifest is None or not args.manifest.exists():
        raise SystemExit("Could not locate manifest.json — pass --manifest explicitly")

    path_cells = _load_path_cells_from_manifest(args.manifest)

    if args.range:
        a, b = args.range.split("-")
        frames = list(range(int(a), int(b) + 1))
    elif args.frame is not None:
        frames = [args.frame]
    else:
        frames = [s["step_index"] for s in snaps[::max(1, len(snaps) // 10)]]

    print("Frame | cell  | in_path | pos              | yaw   | nearest_wall | nearest_path | offset_in_cell    | phase  | cmd_target")
    print("------|-------|---------|------------------|-------|--------------|--------------|--------------------|--------|-----------")
    for f in frames:
        snap = next((s for s in snaps if s["step_index"] == f), None)
        if snap is None:
            print(f"{f:5d} | <no snapshot>")
            continue
        c = _classify(snap["pos_x"], snap["pos_z"], path_cells)
        marker = "  " if c["in_corridor"] else "⚠️ "
        print(f"{f:5d} | {c['cell'][0]:2d},{c['cell'][1]:2d} | {marker}{c['in_corridor']!s:5s} | "
              f"({snap['pos_x']:+.3f}, {snap['pos_z']:+.3f}) | "
              f"{snap['yaw_deg']:+6.1f} | "
              f"{c['dist_to_nearest_wall_m']:.3f} m      | "
              f"{c['dist_to_nearest_path_m']:.3f} m      | "
              f"({c['offset_in_cell_m'][0]:+.2f}, {c['offset_in_cell_m'][1]:+.2f}) | "
              f"{snap['phase']:6s} | wp{snap['target_wp_index']} ({snap['target_x']:+.2f},{snap['target_z']:+.2f})")


if __name__ == "__main__":
    main()
