"""Live ego-occupancy overlay browser-demo server.

A standalone Flask-based HTTP server that, in real time, builds the
ego-centric occupancy map for whatever the simulator is doing right
now — operator-driven session, PPO-driven inference, or auto-pilot
recording — and renders a four-panel browser dashboard so a defence-
audience can see "what the robot sees AND what it remembers" side
by side.

Architecture (no backend C# changes required):

  ┌─ Unity sim @ :8000 ───────────────────┐    ┌─ this server @ :5100 ─┐
  │  /step    (real driving by              │    │  GET /                │
  │           operator / sweep / etc)      │    │   → HTML+JS page      │
  │  /step    (passive read by overlay     │←──┤  GET /overlay.png     │
  │           with throttle=0, brake=0)    │    │   → composite PNG     │
  └─────────────────────────────────────────┘    │  background thread:  │
                                                  │   poll /step @ 5 Hz  │
                                                  │   update OccupancyMap│
                                                  └──────────────────────┘

Run:
  python -m training.bc.live_overlay_server --sim-base http://127.0.0.1:8000 \
                                            --port 5100

Then open http://localhost:5100 in a browser alongside the WebUI
(http://localhost:5058). The browser will auto-refresh the overlay
PNG every 200 ms — about as fast as the matplotlib figure renders.

Passive-observer caveat: this server polls /step with all-zero
controls. On Unity sims that honour brake=0 + throttle=0 + steer=0
as a no-op, this does not affect the robot's motion. If the sim
under test reacts to /step calls regardless of the control payload,
do NOT run this server concurrently with operator driving — instead,
play back recorded demos with `replay_demo_overlay.py` (companion).
"""
from __future__ import annotations

import argparse
import io
import math
import threading
import time
from dataclasses import dataclass
from pathlib import Path

import numpy as np
import requests

from .occupancy import OccupancyMap


_HTML_PAGE = """<!DOCTYPE html>
<html lang="ru">
<head>
  <meta charset="UTF-8">
  <title>Память агента — live overlay</title>
  <style>
    body { font-family: -apple-system, system-ui, sans-serif; background: #1a1a1a; color: #eee; margin: 0; padding: 20px; }
    h1 { font-weight: 300; margin: 0 0 20px 0; font-size: 1.5em; }
    .frame { display: block; margin: 0 auto; max-width: 100%; border-radius: 12px; box-shadow: 0 8px 32px rgba(0,0,0,0.4); }
    .info { text-align: center; color: #888; margin-top: 12px; font-size: 0.9em; }
    .stale { opacity: 0.5; }
  </style>
</head>
<body>
  <h1>🧠 Память агента (ego-centric occupancy map) — live</h1>
  <img id="overlay" class="frame" src="/overlay.png" alt="loading...">
  <div class="info">
    обновление каждые 200 мс · если изображение становится тусклым — sim не отвечает
    · pose поток: <span id="ts">—</span>
  </div>
  <script>
    const img = document.getElementById('overlay');
    const ts = document.getElementById('ts');
    let lastUpdate = Date.now();
    function refresh() {
      img.src = '/overlay.png?t=' + Date.now();
    }
    img.onload = () => {
      lastUpdate = Date.now();
      ts.textContent = new Date().toLocaleTimeString();
      img.classList.remove('stale');
    };
    setInterval(refresh, 200);
    setInterval(() => {
      if (Date.now() - lastUpdate > 2000) img.classList.add('stale');
    }, 500);
  </script>
</body>
</html>
"""


@dataclass
class _SharedState:
    occupancy: OccupancyMap
    last_pose: tuple[float, float, float] = (0.0, 0.0, 0.0)
    last_camera_jpeg: bytes | None = None
    last_update_ts: float = 0.0
    pose_log: list[tuple[float, float]] = None  # x,z trail for the world map


def _yaw_from_quat(q: dict) -> float:
    x, y, z, w = float(q.get("x", 0)), float(q.get("y", 0)), float(q.get("z", 0)), float(q.get("w", 1))
    return math.degrees(math.atan2(2 * (w * y + z * x), 1 - 2 * (x * x + y * y)))


def _poll_loop(sim_base: str, state: _SharedState, interval_s: float = 0.2,
               wall_cells: set | None = None) -> None:
    """Background thread: poll /step at `interval_s`, update occupancy state."""
    import base64
    session = requests.Session()
    while True:
        try:
            r = session.post(
                f"{sim_base}/step",
                json={"throttle": 0.0, "steer": 0.0, "brake": 0.0,
                       "targetAgentId": "ego"},
                timeout=2,
            )
            r.raise_for_status()
            step = r.json()
            pose = step.get("state", {}).get("pose", {})
            pos = pose.get("position", {})
            wx, wz = float(pos.get("x", 0.0)), float(pos.get("z", 0.0))
            yaw = _yaw_from_quat(pose.get("rotation", {}))
            state.last_pose = (wx, wz, yaw)
            state.pose_log.append((wx, wz))
            if len(state.pose_log) > 2000:
                state.pose_log.pop(0)
            # Camera
            frame_b64 = step.get("frame", {}).get("dataBase64", "")
            if frame_b64:
                state.last_camera_jpeg = base64.b64decode(frame_b64)
            # Update occupancy from raycast
            from .occupancy import _synthetic_ultrasonic
            if wall_cells is not None:
                d = _synthetic_ultrasonic(wx, wz, yaw, wall_cells)
            else:
                # Fallback: scrape ultrasonic from telemetry
                d = 2.0
                for kv in step.get("state", {}).get("telemetry", []):
                    if kv.get("key", "").startswith("sensor.ultrasonic.front"):
                        try:
                            d = float(kv.get("value", 2.0))
                        except (ValueError, TypeError):
                            pass
                        break
            state.occupancy.update_from_pose(wx, wz)
            state.occupancy.update_from_raycast(wx, wz, yaw, d)
            state.last_update_ts = time.time()
        except (requests.RequestException, ValueError):
            pass
        time.sleep(interval_s)


def _render_overlay_png(state: _SharedState) -> bytes:
    """Render the four-panel composite as a PNG byte stream."""
    import matplotlib
    matplotlib.use("Agg")
    import matplotlib.pyplot as plt
    from matplotlib.backends.backend_agg import FigureCanvasAgg
    from matplotlib.figure import Figure
    fig = Figure(figsize=(12, 8), dpi=110, facecolor="#1a1a1a")
    canvas = FigureCanvasAgg(fig)
    gs = fig.add_gridspec(2, 2, height_ratios=[1, 1], width_ratios=[1, 1], hspace=0.25, wspace=0.18)

    # Camera (top-left)
    ax_cam = fig.add_subplot(gs[0, 0]); ax_cam.set_facecolor("#1a1a1a")
    if state.last_camera_jpeg:
        from PIL import Image
        img = Image.open(io.BytesIO(state.last_camera_jpeg))
        ax_cam.imshow(np.asarray(img))
    ax_cam.set_title("Камера от 1-го лица", color="#eee", fontsize=11)
    ax_cam.set_xticks([]); ax_cam.set_yticks([])

    # World map with trail (top-right)
    ax_world = fig.add_subplot(gs[0, 1]); ax_world.set_facecolor("#252525")
    if state.pose_log:
        xs = [p[0] for p in state.pose_log]
        zs = [p[1] for p in state.pose_log]
        ax_world.plot(xs, zs, color="#4dabf7", linewidth=2, alpha=0.7)
        ax_world.scatter([xs[0]], [zs[0]], color="#2ca02c", s=100, marker="o",
                          edgecolors="black", linewidth=1.5, zorder=3)
    wx, wz, yaw = state.last_pose
    dx, dz = 0.3 * math.sin(math.radians(yaw)), 0.3 * math.cos(math.radians(yaw))
    ax_world.arrow(wx, wz, dx, dz, head_width=0.12, head_length=0.10,
                    fc="#fab005", ec="black", linewidth=1.5, zorder=4)
    if state.pose_log:
        bx = max(max(xs) - min(xs), max(zs) - min(zs), 2.0) * 0.6
        cx, cz = (max(xs) + min(xs)) / 2, (max(zs) + min(zs)) / 2
        ax_world.set_xlim(cx - bx, cx + bx); ax_world.set_ylim(cz - bx, cz + bx)
    ax_world.set_aspect("equal", "box")
    ax_world.tick_params(colors="#aaa")
    ax_world.set_title("Карта мира + trail", color="#eee", fontsize=11)
    ax_world.grid(True, alpha=0.3, color="#444")

    # Ego occupancy (bottom-left)
    ax_ego = fig.add_subplot(gs[1, 0]); ax_ego.set_facecolor("#000")
    win = state.occupancy.ego_window(wx, wz, yaw)
    comp = np.zeros((21, 21, 3), dtype=np.float32)
    comp[..., 2] = win[0]; comp[..., 1] = win[1]; comp[..., 0] = win[2]
    ax_ego.imshow(np.clip(comp, 0, 1), origin="lower")
    ax_ego.plot(10, 10, "w^", markersize=14, markeredgecolor="black", markeredgewidth=1.5)
    ax_ego.set_xticks([]); ax_ego.set_yticks([])
    ax_ego.set_title("Ego occupancy 21×21\nR=стена G=свободно B=посещено", color="#eee", fontsize=10)

    # Pose info (bottom-right)
    ax_info = fig.add_subplot(gs[1, 1]); ax_info.set_facecolor("#252525")
    ax_info.axis("off")
    age_ms = int(1000 * (time.time() - state.last_update_ts))
    info_text = (
        f"pose:  x = {wx:+.3f} м,  z = {wz:+.3f} м\n"
        f"yaw:   {yaw:+.1f}°\n\n"
        f"посещённых клеток: {int((state.occupancy.grid[0] > 0).sum())}\n"
        f"свободных:         {int((state.occupancy.grid[1] > 0).sum())}\n"
        f"стенных:           {int((state.occupancy.grid[2] > 0).sum())}\n\n"
        f"trail длина:       {len(state.pose_log)} тиков\n"
        f"последний update:  {age_ms} мс назад"
    )
    ax_info.text(0.05, 0.5, info_text, transform=ax_info.transAxes,
                  fontsize=12, color="#eee", family="monospace",
                  verticalalignment="center")

    buf = io.BytesIO()
    canvas.print_png(buf)
    plt.close(fig)
    return buf.getvalue()


def make_app(sim_base: str, wall_cells: set | None = None):
    try:
        from flask import Flask, Response
    except ImportError as e:
        raise SystemExit("Flask required for live overlay server: pip install flask") from e

    state = _SharedState(occupancy=OccupancyMap.empty(), pose_log=[])
    thread = threading.Thread(target=_poll_loop, args=(sim_base, state, 0.2, wall_cells),
                              daemon=True)
    thread.start()

    app = Flask(__name__)

    @app.route("/")
    def index():
        return Response(_HTML_PAGE, mimetype="text/html")

    @app.route("/overlay.png")
    def overlay_png():
        return Response(_render_overlay_png(state), mimetype="image/png")

    @app.route("/reset")
    def reset_state():
        state.occupancy = OccupancyMap.empty()
        state.pose_log.clear()
        return "ok"

    return app


def _load_wall_cells_from_scenario(yaml_path: Path | None) -> set | None:
    if yaml_path is None or not yaml_path.exists():
        return None
    try:
        import yaml
    except ImportError:
        return None
    data = yaml.safe_load(yaml_path.read_text())
    params = ((data.get("world") or {}).get("params") or {})
    encoded = params.get("maze.path_encoded")
    if not encoded:
        return None
    path_cells = {tuple(int(v) for v in p.split(",")) for p in encoded.split(";") if p}
    walls = set()
    for (cx, cz) in path_cells:
        for dx in range(-3, 4):
            for dz in range(-3, 4):
                n = (cx + dx, cz + dz)
                if n not in path_cells:
                    walls.add(n)
    return walls


def main() -> None:
    p = argparse.ArgumentParser(description=__doc__)
    p.add_argument("--sim-base", default="http://127.0.0.1:8000",
                   help="Unity sim base URL (default: http://127.0.0.1:8000)")
    p.add_argument("--port", type=int, default=5100)
    p.add_argument("--scenario", type=Path, default=Path("configs/scenarios/cardboard-maze-bc.yaml"),
                   help="Scenario YAML (used to derive wall cells for synthetic raycasts)")
    args = p.parse_args()
    wall_cells = _load_wall_cells_from_scenario(args.scenario)
    if wall_cells:
        print(f"[live-overlay] using {len(wall_cells)} wall cells from {args.scenario}")
    else:
        print(f"[live-overlay] no wall cells loaded — falling back to sim telemetry ultrasonic")
    app = make_app(args.sim_base, wall_cells)
    print(f"[live-overlay] open http://localhost:{args.port}/ in browser")
    app.run(host="0.0.0.0", port=args.port, threaded=True)


if __name__ == "__main__":
    main()
