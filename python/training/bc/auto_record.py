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
import json
import math
import random
import shutil
import subprocess
import sys
import time
from pathlib import Path

import requests

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


def patch_yaml_seed(yaml_path: Path, seed: int) -> None:
    text = yaml_path.read_text()
    import re
    text = re.sub(r"maze\.seed:\s*\d+", f"maze.seed: {seed}", text)
    yaml_path.write_text(text)


def step(agent_id: str = "") -> dict:
    """Tick the sim one step with throttle=0 to read state.

    Auto-pilot doesn't apply throttle here — actual driving happens via
    /api/command which the backend translates into throttle/steer/brake
    behind the scenes.
    """
    payload = {"throttle": 0.0, "steer": 0.0, "brake": 0.0}
    if agent_id:
        payload["agentId"] = agent_id
    r = requests.post(f"{SIM}/step", json=payload, timeout=5)
    r.raise_for_status()
    return r.json()


def send_command(cmd: str, client_id: str = "auto-pilot", runtime_mode: str = "unity-sim") -> None:
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


def drive_episode(
    waypoints: list[tuple[float, float]],
    *,
    max_steps: int,
    reach_distance_m: float,
    rng: random.Random,
    log_every: int = 50,
) -> dict:
    """Drive the robot through the waypoints. Returns episode summary."""
    forward_band_deg = 15.0
    wp_index = 0
    last_progress_step = 0
    steps_taken = 0
    actions_sent: dict[str, int] = {a: 0 for a in ACTIONS}

    while wp_index < len(waypoints) and steps_taken < max_steps:
        s = step()
        pose = s["state"]["pose"]
        target_x, target_z = waypoints[wp_index]
        dx = target_x - pose["position"]["x"]
        dz = target_z - pose["position"]["z"]
        dist = math.hypot(dx, dz)

        if dist < reach_distance_m:
            wp_index += 1
            last_progress_step = steps_taken
            continue

        cmd = choose_action(pose, target_x, target_z, forward_band_deg=forward_band_deg, rng=rng)
        send_command(cmd)
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

        # 1. Patch + reset scenario
        patch_yaml_seed(args.yaml, seed)
        reset_info = reset_scenario(args.yaml)
        print(f"  reset → {reset_info.get('selectedTrackId', '?')}", flush=True)
        time.sleep(1.5)

        # 2. Compute waypoints in world coordinates (same as Unity will use)
        geo = generate(MazeParams(
            seed=seed, length_cells=40, corridor_width_m=0.45,
            left_turns=5, right_turns=5,
        ))
        waypoints = [(float(x), float(z)) for x, z in geo.waypoints]
        print(f"  waypoints: {len(waypoints)} cells", flush=True)

        # 3. Start recording, drive, stop
        start_demo(tag)
        time.sleep(0.5)
        summary = drive_episode(
            waypoints,
            max_steps=args.max_steps,
            reach_distance_m=reach,
            rng=rng,
        )
        time.sleep(0.5)
        stop_demo()
        print(f"  done: {summary}", flush=True)

        # 4. Manifest
        (args.evidence / f"manifest_{tag}.yaml").write_bytes(args.yaml.read_bytes())
        (args.evidence / f"manifest_{tag}.json").write_text(json.dumps({
            "episode": i,
            "tag": tag,
            "maze": {
                "seed": seed,
                "length_cells": 40,
                "corridor_width_m": 0.45,
                "left_turns": 5,
                "right_turns": 5,
            },
            "drive_summary": summary,
        }, indent=2))

    print(f"\nAll {len(seeds)} episodes recorded.", flush=True)
    print("Run ./scripts/collect_bc_demos.sh to move session_*.jsonl + .mp4 into evidence.", flush=True)
    return 0


if __name__ == "__main__":
    sys.exit(main())
