"""Render a side-by-side composite video showing what the robot sees and
what the agent's spatial memory contains, frame-by-frame.

For each recorded demo (autopilot_*.mp4 + session_*.jsonl + occupancy_*.npy
+ manifest_*.json), produces a composite MP4 with four panels:

  ┌──────────────────┬──────────────────┐
  │ 📷 Камера         │ 🗺️ World map     │
  │ (от 1-го лица)   │ (топ-даун, trail)│
  ├──────────────────┼──────────────────┤
  │ 🧠 Ego occupancy │ 📊 Action label  │
  │ (3×21×21 stack)  │ + per-frame log  │
  └──────────────────┴──────────────────┘

For the master's-thesis defense and the "what does the model remember"
storytelling part of the supporting article.

Usage:
    python -m training.bc.render_demo_overlay \\
        --demos-dir docs/.../curated-demos \\
        --output-dir docs/.../curated-demos/overlays

Produces `overlay_<tag>.mp4` per demo. With --single-frame F, also dumps
`preview_overlay_<tag>_f<F>.png` as a single-frame snapshot for quick
visual verification in chat / docs.
"""
from __future__ import annotations

import argparse
import json
import math
from pathlib import Path

import cv2
import matplotlib

matplotlib.use("Agg")

import matplotlib.pyplot as plt
import numpy as np
from matplotlib.backends.backend_agg import FigureCanvasAgg
from matplotlib.figure import Figure

_CELL_M = 0.225
_MAZE_CELL_M = 0.45
_GRID_SIZE = 80
_GRID_ORIGIN = 40
_ACTION_NAMES = ["DirStop", "DirForward", "DirBack", "DirLeft", "DirRight"]
_ACTION_COLORS = {
    "DirStop": "#888888",
    "DirForward": "#4dabf7",
    "DirBack": "#ff8787",
    "DirLeft": "#69db7c",
    "DirRight": "#ffc078",
}


def _load_session_actions(jsonl_path: Path) -> list[tuple[float, str]]:
    """Read (timestamp, command) tuples in stream order from a session JSONL."""
    out: list[tuple[float, str]] = []
    for raw in jsonl_path.read_text(encoding="utf-8").splitlines():
        line = raw.lstrip("﻿").strip()
        if not line:
            continue
        try:
            ev = json.loads(line)
        except json.JSONDecodeError:
            continue
        if ev.get("type") == "command.outgoing":
            cmd = ev.get("payload", {}).get("command")
            ts_raw = ev.get("timestamp")
            if cmd and ts_raw:
                from datetime import datetime
                ts = datetime.fromisoformat(ts_raw.replace("Z", "+00:00")).timestamp()
                out.append((ts, cmd))
    return out


def _load_pose_snapshots(jsonl_path: Path) -> list[dict]:
    snaps: list[dict] = []
    for raw in jsonl_path.read_text(encoding="utf-8").splitlines():
        line = raw.lstrip("﻿").strip()
        if not line:
            continue
        try:
            ev = json.loads(line)
        except json.JSONDecodeError:
            continue
        if ev.get("type") == "pose.snapshot":
            snaps.append(ev["payload"])
    return snaps


def _render_one_frame(
    *,
    camera_bgr: np.ndarray,
    occupancy: np.ndarray,
    world_visited: np.ndarray,
    pose: tuple[float, float, float],
    action: str,
    frame_index: int,
    total_frames: int,
    waypoints_world: list[tuple[float, float]],
    path_world: list[tuple[float, float]],
) -> np.ndarray:
    """Render one frame of the overlay and return as RGB ndarray."""
    fig = Figure(figsize=(14, 9), dpi=110)
    canvas = FigureCanvasAgg(fig)
    gs = fig.add_gridspec(2, 2, width_ratios=[1.0, 1.0], height_ratios=[1.0, 1.0],
                          hspace=0.20, wspace=0.15)

    # ── Camera (top-left) ──
    ax_cam = fig.add_subplot(gs[0, 0])
    cam_rgb = cv2.cvtColor(camera_bgr, cv2.COLOR_BGR2RGB)
    ax_cam.imshow(cam_rgb)
    ax_cam.set_title(f"Камера от 1-го лица  (кадр {frame_index}/{total_frames})", fontsize=11)
    ax_cam.set_xticks([]); ax_cam.set_yticks([])

    # ── World map with trail (top-right) ──
    ax_world = fig.add_subplot(gs[0, 1])
    # Plot path waypoints
    if path_world:
        xs = [p[0] for p in path_world]
        zs = [p[1] for p in path_world]
        ax_world.plot(xs, zs, color="#cccccc", linewidth=4, zorder=1, label="Path")
    # Plot visited cells from world map (channel 0)
    visited_y, visited_x = np.where(world_visited > 0)
    if len(visited_x) > 0:
        wx_world = (visited_x - _GRID_ORIGIN) * _CELL_M
        wz_world = (visited_y - _GRID_ORIGIN) * _CELL_M
        ax_world.scatter(wx_world, wz_world, c="#4c6ef5", s=20, alpha=0.7, zorder=2, label="Visited")
    # Waypoints
    if waypoints_world:
        wx, wz = waypoints_world[0]
        ax_world.scatter([wx], [wz], color="#2ca02c", s=200, marker="o", zorder=4,
                          edgecolors="black", linewidth=1.5, label="Start")
        gx, gz = waypoints_world[-1]
        ax_world.scatter([gx], [gz], color="#d62728", s=200, marker="*", zorder=4,
                          edgecolors="black", linewidth=1.5, label="Goal")
    # Robot pose
    px, pz, yaw_deg = pose
    yaw_rad = math.radians(yaw_deg)
    dx, dz = math.sin(yaw_rad) * 0.3, math.cos(yaw_rad) * 0.3
    ax_world.arrow(px, pz, dx, dz, head_width=0.12, head_length=0.10,
                    fc="#fab005", ec="black", linewidth=1.5, zorder=5)
    # Bounds with margin
    if path_world:
        all_xs = [p[0] for p in path_world] + [px]
        all_zs = [p[1] for p in path_world] + [pz]
        ax_world.set_xlim(min(all_xs) - 0.6, max(all_xs) + 0.6)
        ax_world.set_ylim(min(all_zs) - 0.6, max(all_zs) + 0.6)
    ax_world.set_aspect("equal", "box")
    ax_world.set_xlabel("X (м)")
    ax_world.set_ylabel("Z (м)")
    ax_world.set_title("Карта мира (память маршрута)", fontsize=11)
    ax_world.legend(loc="upper left", fontsize=8)
    ax_world.grid(True, alpha=0.3)

    # ── Ego occupancy composite (bottom-left) ──
    ax_ego = fig.add_subplot(gs[1, 0])
    composite = np.zeros((21, 21, 3), dtype=np.float32)
    composite[..., 2] = occupancy[0]  # visited → blue
    composite[..., 1] = occupancy[1]  # free → green
    composite[..., 0] = occupancy[2]  # wall → red
    ax_ego.imshow(np.clip(composite, 0, 1), origin="lower", extent=(-10.5, 10.5, -10.5, 10.5))
    ax_ego.plot(0, 0, "w^", markersize=14, markeredgecolor="black", markeredgewidth=1.5)
    ax_ego.set_title("Ego occupancy 21×21 (память локального пространства)\n"
                      "R=стена  G=свободно  B=посещено  ▲=робот (смотрит вверх)",
                      fontsize=10)
    ax_ego.set_xlim(-10.5, 10.5); ax_ego.set_ylim(-10.5, 10.5)
    ax_ego.set_xticks([]); ax_ego.set_yticks([])

    # ── Action panel (bottom-right) ──
    ax_act = fig.add_subplot(gs[1, 1])
    color = _ACTION_COLORS.get(action, "#888888")
    ax_act.barh([0], [1], color=color, edgecolor="black", linewidth=1.5, height=0.7)
    ax_act.text(0.5, 0, action, ha="center", va="center", fontsize=24,
                 fontweight="bold", color="#222222")
    ax_act.set_xlim(0, 1); ax_act.set_ylim(-1, 1)
    ax_act.set_xticks([]); ax_act.set_yticks([])
    ax_act.set_title("Действие на этом тике", fontsize=11)
    for spine in ax_act.spines.values():
        spine.set_visible(False)
    # Compact progress text
    pct = 100 * frame_index / max(total_frames, 1)
    ax_act.text(0.5, -0.65,
                 f"pose = ({px:+.2f}, {pz:+.2f})  yaw = {yaw_deg:+.0f}°\n"
                 f"прогресс эпизода: {pct:.0f}%",
                 ha="center", va="center", fontsize=10, color="#666666")

    canvas.draw()
    buf = np.asarray(canvas.buffer_rgba())[:, :, :3]  # drop alpha
    plt.close(fig)
    return buf


def _world_path_from_manifest(manifest_path: Path) -> tuple[list[tuple[float, float]], list[tuple[float, float]]]:
    """Return (waypoints_world, path_world) — both in world (x, z) meters."""
    m = json.loads(manifest_path.read_text())
    encoded = m["maze"]["path_encoded"]
    cells = [tuple(int(v) for v in p.split(",")) for p in encoded.split(";") if p]
    path_world = [((cx - 20) * _MAZE_CELL_M, (cz - 20) * _MAZE_CELL_M) for (cx, cz) in cells]
    return path_world, path_world  # waypoints = same list


def render_overlay(
    *,
    mp4_path: Path,
    jsonl_path: Path,
    occupancy_path: Path,
    manifest_path: Path,
    output_path: Path,
    single_frame: int | None = None,
) -> None:
    cap = cv2.VideoCapture(str(mp4_path))
    if not cap.isOpened():
        raise FileNotFoundError(f"cannot open MP4: {mp4_path}")
    occupancy = np.load(occupancy_path)
    T = occupancy.shape[0]
    snaps = _load_pose_snapshots(jsonl_path)
    actions = _load_session_actions(jsonl_path)
    waypoints_world, path_world = _world_path_from_manifest(manifest_path)

    fps = cap.get(cv2.CAP_PROP_FPS) or 8.0
    out_fps = fps

    if single_frame is not None:
        cap.set(cv2.CAP_PROP_POS_FRAMES, single_frame)
        ok, bgr = cap.read()
        if not ok:
            raise RuntimeError(f"failed to read frame {single_frame}")
        # World visited up to single_frame
        world_visited = np.zeros((_GRID_SIZE, _GRID_SIZE), dtype=np.float32)
        for s in snaps[:single_frame + 1]:
            gx = int(round(s["pos_x"] / _CELL_M)) + _GRID_ORIGIN
            gz = int(round(s["pos_z"] / _CELL_M)) + _GRID_ORIGIN
            if 0 <= gx < _GRID_SIZE and 0 <= gz < _GRID_SIZE:
                world_visited[gz, gx] = 1.0
        snap = snaps[min(single_frame, len(snaps) - 1)]
        pose = (snap["pos_x"], snap["pos_z"], snap["yaw_deg"])
        action = actions[min(single_frame, len(actions) - 1)][1] if actions else "DirStop"
        occ = occupancy[min(single_frame, T - 1)]
        rgb = _render_one_frame(
            camera_bgr=bgr, occupancy=occ, world_visited=world_visited,
            pose=pose, action=action, frame_index=single_frame, total_frames=T,
            waypoints_world=waypoints_world, path_world=path_world,
        )
        bgr_out = cv2.cvtColor(rgb, cv2.COLOR_RGB2BGR)
        cv2.imwrite(str(output_path), bgr_out)
        cap.release()
        return

    # Full video
    output_path.parent.mkdir(parents=True, exist_ok=True)
    writer = None
    world_visited = np.zeros((_GRID_SIZE, _GRID_SIZE), dtype=np.float32)
    for f in range(T):
        ok, bgr = cap.read()
        if not ok:
            break
        snap = snaps[min(f, len(snaps) - 1)]
        wx, wz = snap["pos_x"], snap["pos_z"]
        gx = int(round(wx / _CELL_M)) + _GRID_ORIGIN
        gz = int(round(wz / _CELL_M)) + _GRID_ORIGIN
        if 0 <= gx < _GRID_SIZE and 0 <= gz < _GRID_SIZE:
            world_visited[gz, gx] = 1.0
        pose = (wx, wz, snap["yaw_deg"])
        action = actions[min(f, len(actions) - 1)][1] if actions else "DirStop"
        rgb = _render_one_frame(
            camera_bgr=bgr, occupancy=occupancy[f], world_visited=world_visited,
            pose=pose, action=action, frame_index=f, total_frames=T,
            waypoints_world=waypoints_world, path_world=path_world,
        )
        if writer is None:
            h, w = rgb.shape[:2]
            fourcc = cv2.VideoWriter_fourcc(*"avc1")
            writer = cv2.VideoWriter(str(output_path), fourcc, out_fps, (w, h))
        writer.write(cv2.cvtColor(rgb, cv2.COLOR_RGB2BGR))
    cap.release()
    if writer is not None:
        writer.release()


def main() -> None:
    p = argparse.ArgumentParser(description=__doc__)
    p.add_argument("--demos-dir", type=Path, required=True)
    p.add_argument("--output-dir", type=Path, default=None,
                   help="Where to write overlay_*.mp4 files (default: demos-dir/overlays)")
    p.add_argument("--single-frame", type=int, default=None,
                   help="Render only one frame as PNG instead of full MP4. Useful for "
                        "quick visual verification in chat / docs.")
    p.add_argument("--tag-prefix", default="bc-maze-curated-",
                   help="Render only demos whose tag begins with this prefix")
    args = p.parse_args()

    out_dir = args.output_dir or (args.demos_dir / "overlays")
    out_dir.mkdir(parents=True, exist_ok=True)

    demos = args.demos_dir
    for mp4 in sorted(demos.glob("autopilot_*.mp4")):
        # Recover tag from mp4 filename
        parts = mp4.stem.split("_")
        if len(parts) < 4 or parts[0] != "autopilot":
            continue
        tag = "_".join(parts[3:])
        if not tag.startswith(args.tag_prefix):
            continue
        # Find paired jsonl (largest — direct-write), manifest, occupancy
        jsonls = sorted([j for j in demos.glob(f"session_*{tag}.jsonl")],
                        key=lambda p: p.stat().st_size, reverse=True)
        if not jsonls:
            print(f"  SKIP {tag}: no session JSONL"); continue
        manifest = demos / f"manifest_{tag}.json"
        occupancy = demos / f"occupancy_{tag}.npy"
        if not manifest.exists() or not occupancy.exists():
            print(f"  SKIP {tag}: missing manifest or occupancy"); continue
        if args.single_frame is not None:
            outp = out_dir / f"preview_overlay_{tag}_f{args.single_frame}.png"
        else:
            outp = out_dir / f"overlay_{tag}.mp4"
        print(f"  rendering {tag} -> {outp.name}", flush=True)
        render_overlay(
            mp4_path=mp4, jsonl_path=jsonls[0],
            occupancy_path=occupancy, manifest_path=manifest,
            output_path=outp,
            single_frame=args.single_frame,
        )


if __name__ == "__main__":
    main()
