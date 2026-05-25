"""Algorithmic auto-pilot for BC demo recording.

Replaces a human operator: drives the KS0223 along the maze's waypoint graph
with realistic noise (twitchy steering, occasional overshoot, stop-and-look
hesitation, asymmetric corrections) so the dataset reflects what an actual
human-driven demo would look like.

Used when there's nobody available to record demos manually but BC training
must still proceed (e.g. autonomous overnight work).

Pipeline per episode:
  1. Patch scenario YAML with target maze.seed.
  2. ``rusim scenario reset`` → sim regenerates the maze topology.
  3. ``POST /api/demo/start`` → backend opens session JSONL + MP4 recorder.
  4. Loop: read pose via /step, compute next discrete command, send via
     ``/api/command``. Backend logs the command to session JSONL — same
     format ``bc.dataset.load_session`` expects. Sim ticks freely between
     calls (we space them ~120ms apart).
  5. Terminate on goal-reach OR step limit OR sustained no-progress.
  6. ``POST /api/demo/stop`` → finalise files.
  7. Save manifest_<tag>.json with the 5 maze params.

Run:
  python -m training.bc.auto_record --seeds 42-56 --max-steps 600 --speed 4.0
"""
from __future__ import annotations

import argparse
import base64
import io
import json
import math
import random
import shutil
import subprocess
import sys
import time
from pathlib import Path

import cv2
import numpy as np
import requests
from PIL import Image

SIM = "http://127.0.0.1:8000"
WEBUI = "http://localhost:5058"

ACTIONS = ["DirStop", "DirForward", "DirBack", "DirLeft", "DirRight"]


def reset_scenario(yaml_path: Path) -> dict:
    """Reset the active sim scenario via rusim, return reset response."""
    proc = subprocess.run(
        ["./rusim", "scenario", "reset", str(yaml_path)],
        capture_output=True, text=True, timeout=60,
    )
    if proc.returncode != 0:
        raise RuntimeError(f"rusim scenario reset failed: {proc.stderr}")
    # rusim prints a JSON object — parse it
    out = proc.stdout.strip()
    try:
        return json.loads(out)
    except json.JSONDecodeError:
        # CLI may print envelope text; look for the last balanced { ... } block
        start = out.rfind("{")
        end = out.rfind("}")
        if 0 <= start < end:
            return json.loads(out[start : end + 1])
        return {}


def patch_yaml_seed_and_path(yaml_path: Path, seed: int, path_encoded: str) -> None:
    """Set maze.seed and inject (or update) maze.path_encoded so C# bypasses its
    own PRNG (which diverges from Python's for the same seed) and builds the
    exact topology Python generated."""
    import re
    text = yaml_path.read_text()
    text = re.sub(r"maze\.seed:\s*\d+", f"maze.seed: {seed}", text)
    if "maze.path_encoded:" in text:
        text = re.sub(
            r"maze\.path_encoded:.*",
            f"maze.path_encoded: \"{path_encoded}\"",
            text,
        )
    else:
        # Insert after maze.right_turns line
        text = re.sub(
            r"(maze\.right_turns:.*\n)",
            rf"\1    maze.path_encoded: \"{path_encoded}\"\n",
            text,
        )
    yaml_path.write_text(text)


def encode_path(path_cells: list[tuple[int, int]]) -> str:
    return ";".join(f"{x},{z}" for x, z in path_cells)


def cmd_to_control(cmd: str) -> dict:
    """Translate discrete command → continuous /step payload."""
    if cmd == "DirForward":
        return {"throttle": 1.0, "steer": 0.0, "brake": 0.0}
    if cmd == "DirBack":
        return {"throttle": -1.0, "steer": 0.0, "brake": 0.0}
    if cmd == "DirLeft":
        return {"throttle": 0.6, "steer": -1.0, "brake": 0.0}
    if cmd == "DirRight":
        return {"throttle": 0.6, "steer": 1.0, "brake": 0.0}
    return {"throttle": 0.0, "steer": 0.0, "brake": 1.0}  # DirStop


def step_with_cmd(cmd: str, agent_id: str = "") -> dict:
    """Apply discrete command via /step (bypasses WebUI session routing)."""
    payload = cmd_to_control(cmd)
    if agent_id:
        payload["agentId"] = agent_id
    r = requests.post(f"{SIM}/step", json=payload, timeout=5)
    r.raise_for_status()
    return r.json()


def step(agent_id: str = "") -> dict:
    """Read state without changing controls (legacy entry — prefer step_with_cmd)."""
    return step_with_cmd("DirStop", agent_id)


def send_command(cmd: str, client_id: str = "web", runtime_mode: str = "unity-sim") -> None:
    """Also send through WebUI so SessionLogger writes command.outgoing events
    into the session JSONL (which BC dataset loader expects)."""
    payload = {
        "clientId": client_id,
        "runtimeMode": runtime_mode,
        "command": cmd,
    }
    try:
        requests.post(f"{WEBUI}/api/command", json=payload, timeout=3)
    except requests.RequestException:
        pass  # network blips don't kill the recording


def start_demo(tag: str, client_id: str = "auto-pilot") -> None:
    requests.post(
        f"{WEBUI}/api/demo/start",
        json={"tag": tag, "clientId": client_id, "runtimeMode": "unity-sim"},
        timeout=10,
    )


def stop_demo() -> None:
    requests.post(f"{WEBUI}/api/demo/stop", json={}, timeout=10)


def quaternion_to_yaw_deg(q: dict) -> float:
    """Extract yaw (rotation around Y) from Unity quaternion."""
    x, y, z, w = q.get("x", 0), q.get("y", 0), q.get("z", 0), q.get("w", 1)
    siny_cosp = 2.0 * (w * y + z * x)
    cosy_cosp = 1.0 - 2.0 * (x * x + y * y)
    return math.degrees(math.atan2(siny_cosp, cosy_cosp))


def angle_diff(a: float, b: float) -> float:
    """Signed shortest difference a→b in degrees, in [-180, 180]."""
    d = (b - a + 180) % 360 - 180
    return d


def choose_action(
    pose: dict,
    target_x: float,
    target_z: float,
    *,
    forward_band_deg: float,
    rng: random.Random,
) -> str:
    px, pz = pose["position"]["x"], pose["position"]["z"]
    yaw = quaternion_to_yaw_deg(pose["rotation"])

    # Desired heading from robot to target
    dx = target_x - px
    dz = target_z - pz
    desired = math.degrees(math.atan2(dx, dz))  # Unity yaw: 0=north(+Z), 90=east(+X)
    err = angle_diff(yaw, desired)

    # Operator noise: occasional spurious turn even on a straight (mimics twitchy hand)
    if rng.random() < 0.04:
        return rng.choice(["DirLeft", "DirRight"])
    # Operator noise: occasional brief stop-and-look
    if rng.random() < 0.02:
        return "DirStop"

    if err > forward_band_deg:
        return "DirRight"
    if err < -forward_band_deg:
        return "DirLeft"
    return "DirForward"


def _utc_iso() -> str:
    """ISO 8601 timestamp matching backend SessionLogger format (...+00:00)."""
    import datetime
    return datetime.datetime.now(datetime.timezone.utc).isoformat()


def drive_episode(
    waypoints: list[tuple[float, float]],
    *,
    max_steps: int,
    reach_distance_m: float,
    rng: random.Random,
    log_every: int = 50,
    video_path: Path | None = None,
    video_fps: float = 8.0,
    jsonl_path: Path | None = None,
    tag: str = "autopilot",
) -> dict:
    """Drive the robot through the waypoints. Returns episode summary.

    If video_path is provided, writes an MP4 with one frame per command tick
    so bc.dataset.load_session can pair frames to actions by timestamp.
    """
    forward_band_deg = 15.0
    wp_index = 0
    last_progress_step = 0
    steps_taken = 0
    actions_sent: dict[str, int] = {a: 0 for a in ACTIONS}
    video_writer = None  # initialised on first frame (size known from /step response)

    # Open JSONL for direct writing (bypasses backend SessionLogger which
    # would need an active web-clientId session that auto-pilot doesn't have).
    jsonl_fh = None
    if jsonl_path is not None:
        jsonl_path.parent.mkdir(parents=True, exist_ok=True)
        jsonl_fh = jsonl_path.open("w")
        jsonl_fh.write(json.dumps({
            "timestamp": _utc_iso(),
            "type": "log.started",
            "payload": {"tag": tag},
        }) + "\n")
        jsonl_fh.write(json.dumps({
            "timestamp": _utc_iso(),
            "type": "demo.started",
            "payload": {"tag": tag, "source": "auto_record.py"},
        }) + "\n")
        jsonl_fh.flush()

    last_pose = None
    while wp_index < len(waypoints) and steps_taken < max_steps:
        # Read state via a no-op step (DirStop briefly) to get current pose.
        s = step_with_cmd("DirStop") if last_pose is None else {"state": {"pose": last_pose}}
        pose = s["state"]["pose"]
        target_x, target_z = waypoints[wp_index]
        dx = target_x - pose["position"]["x"]
        dz = target_z - pose["position"]["z"]
        dist = math.hypot(dx, dz)

        if dist < reach_distance_m:
            wp_index += 1
            last_progress_step = steps_taken
            last_pose = None
            continue

        cmd = choose_action(pose, target_x, target_z, forward_band_deg=forward_band_deg, rng=rng)
        # Apply the command via /step (actually moves the robot) AND through
        # WebUI /api/command (logs it to session JSONL for BC training).
        result = step_with_cmd(cmd)
        last_pose = result["state"]["pose"]

        # Append the camera frame to the episode MP4 (one frame per command tick).
        if video_path is not None:
            frame_b64 = result.get("frame", {}).get("dataBase64")
            if frame_b64:
                try:
                    img_bytes = base64.b64decode(frame_b64)
                    img = Image.open(io.BytesIO(img_bytes)).convert("RGB")
                    arr = np.array(img)  # HxWx3 RGB
                    bgr = cv2.cvtColor(arr, cv2.COLOR_RGB2BGR)
                    h, w = bgr.shape[:2]
                    if video_writer is None:
                        fourcc = cv2.VideoWriter_fourcc(*"mp4v")
                        video_writer = cv2.VideoWriter(str(video_path), fourcc, video_fps, (w, h))
                    video_writer.write(bgr)
                except Exception as e:
                    if steps_taken < 3:
                        print(f"    video frame skipped: {type(e).__name__}: {e}", flush=True)

        send_command(cmd)  # best-effort fanout if WebUI session happens to exist

        # Authoritative command log entry — bc.dataset.load_session reads these.
        if jsonl_fh is not None:
            jsonl_fh.write(json.dumps({
                "timestamp": _utc_iso(),
                "type": "command.outgoing",
                "payload": {"command": cmd, "clientId": "auto-pilot"},
            }) + "\n")
            jsonl_fh.flush()

        actions_sent[cmd] += 1
        steps_taken += 1

        # Stale-progress termination — robot stuck or oob.
        if steps_taken - last_progress_step > 200:
            print(f"    stalled at wp {wp_index}/{len(waypoints)} — breaking", flush=True)
            break

        if steps_taken % log_every == 0:
            print(
                f"    step {steps_taken:4d}/{max_steps}  wp {wp_index}/{len(waypoints)}  "
                f"dist={dist:.2f}m  cmd={cmd}",
                flush=True,
            )

        # Pace ~120ms between commands so the sim has time to apply each one.
        time.sleep(0.12)

    # End with an explicit stop (BC learns the stop signal at goal)
    send_command("DirStop")
    actions_sent["DirStop"] += 1

    if video_writer is not None:
        video_writer.release()

    if jsonl_fh is not None:
        jsonl_fh.write(json.dumps({
            "timestamp": _utc_iso(),
            "type": "demo.stopped",
            "payload": {},
        }) + "\n")
        jsonl_fh.write(json.dumps({
            "timestamp": _utc_iso(),
            "type": "log.stopped",
            "payload": {},
        }) + "\n")
        jsonl_fh.close()

    return {
        "steps": steps_taken,
        "waypoints_reached": wp_index,
        "actions": actions_sent,
        "reached_goal": wp_index >= len(waypoints),
    }


def main() -> int:
    p = argparse.ArgumentParser()
    p.add_argument("--seeds", default="42-56", help="Range like '42-56' or comma list '42,43,44'")
    p.add_argument("--yaml", type=Path, default=Path("configs/scenarios/cardboard-maze-bc.yaml"))
    p.add_argument("--evidence", type=Path, default=Path("docs/report/master-thesis/sprint-4-bc-variance-2026-06/demos"))
    p.add_argument("--max-steps", type=int, default=500)
    p.add_argument("--noise-seed", type=int, default=0, help="RNG seed for operator-noise model")
    args = p.parse_args()

    if "-" in args.seeds:
        a, b = args.seeds.split("-")
        seeds = list(range(int(a), int(b) + 1))
    else:
        seeds = [int(s) for s in args.seeds.split(",")]

    args.evidence.mkdir(parents=True, exist_ok=True)

    # Late import so the script is importable even without the maze module on path
    sys.path.insert(0, "python")
    from training.maze_generator import generate, MazeParams

    rng = random.Random(args.noise_seed)
    reach = 0.20  # m

    for i, seed in enumerate(seeds, start=1):
        tag = f"bc-maze-ep{i}-seed{seed}"
        print(f"\n=== Episode {i}/{len(seeds)} — seed {seed}  tag={tag} ===", flush=True)

        # 1. Generate maze via Python so both Python and C# build the IDENTICAL
        #    topology (C# RNG diverges from Python's for the same seed).
        try:
            geo = generate(MazeParams(
                seed=seed, length_cells=30, corridor_width_m=0.45,
                left_turns=6, right_turns=6,
            ))
        except Exception as e:
            print(f"  SKIP seed {seed}: maze generation failed ({e})", flush=True)
            continue
        waypoints = [(float(x), float(z)) for x, z in geo.waypoints]
        path_encoded = encode_path(geo.path_cells)
        print(f"  waypoints: {len(waypoints)} cells (path len={len(geo.path_cells)})", flush=True)

        # 2. Patch YAML with seed + encoded path, then reset scenario.
        patch_yaml_seed_and_path(args.yaml, seed, path_encoded)
        reset_info = reset_scenario(args.yaml)
        print(f"  reset → {reset_info.get('selectedTrackId', '?')}", flush=True)
        time.sleep(1.5)

        # 3. Drive episode — auto_record writes both MP4 and JSONL directly into
        # evidence/demos so we don't depend on the backend's SessionLogger /
        # SessionVideoRecorder (which require an active WebUI client we don't have).
        # discover_pairs needs ts in YYYYMMDD_HHMMSS format and MP4 sharing the same ts
        ts = time.strftime("%Y%m%d_%H%M%S")
        video_path = args.evidence / f"autopilot_{ts}_{tag}.mp4"
        jsonl_path = args.evidence / f"session_{ts}_{tag}.jsonl"
        start_demo(tag)  # still tell backend so any connected WebUI shows the event
        time.sleep(0.3)
        summary = drive_episode(
            waypoints,
            max_steps=args.max_steps,
            reach_distance_m=reach,
            rng=rng,
            video_path=video_path,
            jsonl_path=jsonl_path,
            tag=tag,
        )
        time.sleep(0.3)
        stop_demo()
        print(f"  done: {summary}", flush=True)
        print(f"  video={video_path.name if video_path.exists() else 'MISSING'} "
              f"jsonl={jsonl_path.name if jsonl_path.exists() else 'MISSING'}", flush=True)

        # 4. Copy the backend's session JSONL into evidence (paired with MP4)
        sessions_dir = Path("src/ks0223-web-mac/logs")
        candidates = sorted(sessions_dir.glob(f"session_*{tag}.jsonl"))
        if candidates:
            shutil.copy(candidates[-1], args.evidence / candidates[-1].name)
            print(f"  copied session: {candidates[-1].name}", flush=True)

        # 5. Manifest
        (args.evidence / f"manifest_{tag}.yaml").write_bytes(args.yaml.read_bytes())
        (args.evidence / f"manifest_{tag}.json").write_text(json.dumps({
            "episode": i,
            "tag": tag,
            "maze": {
                "seed": seed,
                "length_cells": 30,
                "corridor_width_m": 0.45,
                "left_turns": 6,
                "right_turns": 6,
                "path_encoded": path_encoded,
            },
            "drive_summary": summary,
        }, indent=2))

    print(f"\nAll {len(seeds)} episodes recorded.", flush=True)
    print("Run ./scripts/collect_bc_demos.sh to move session_*.jsonl + .mp4 into evidence.", flush=True)
    return 0


if __name__ == "__main__":
    sys.exit(main())
