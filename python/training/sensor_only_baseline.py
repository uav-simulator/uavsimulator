#!/usr/bin/env python3
"""Sensor-only heuristic baseline для KS0223 L-corridor traversal.

Не использует камеру — только ультразвук (front + scan.left/right) для навигации.
Гарантированно проходит L-trace при правильно собранной геометрии.

Цель: proof-of-infrastructure для sprint 3 — показать что физическая трасса
проходима, telemetry/control/safety pipeline работают, без зависимости от
неработающей RL policy.

Алгоритм:
    while not done:
        d_front = ultrasonic.distance_cm
        d_left  = ultrasonic.scan.left_cm
        d_right = ultrasonic.scan.right_cm

        if d_front < 25cm and d_right > 35cm:
            # дошли до конца сегмента А, поворачиваем направо в сегмент B
            DirRight × 4 ticks (~0.5 сек)
            DirForward × N
        elif d_front < 25cm and d_left > 35cm:
            # ситуация: edгe up to wall, но right blocked — поворот налево
            DirLeft × 4 ticks
        elif d_front < 25cm:
            # упёрся, обе стороны заблокированы — E-stop
            DirStop
        else:
            DirForward

Запуск: requires backend running + robot connected.
    python python/training/sensor_only_baseline.py --runs 5 --output-dir <evidence_dir>
"""

from __future__ import annotations

import argparse
import json
import sys
import time
import urllib.request
from pathlib import Path

BACKEND_DEFAULT = "http://localhost:5287"
CLIENT = "sim2real-cli"
MODE = "real-robot"


def http_json(backend: str, path: str, method: str = "GET", payload: dict | None = None):
    headers = {"Content-Type": "application/json"}
    data = json.dumps(payload).encode() if payload else None
    req = urllib.request.Request(f"{backend}{path}", data=data, headers=headers, method=method)
    return json.loads(urllib.request.urlopen(req, timeout=4).read())


def http_bytes(backend: str, path: str) -> bytes:
    return urllib.request.urlopen(f"{backend}{path}", timeout=4).read()


def cmd(backend: str, command: str):
    return http_json(backend, "/api/command", "POST",
                     {"clientId": CLIENT, "runtimeMode": MODE, "command": command})


def telemetry(backend: str) -> dict:
    return http_json(backend, f"/api/sensors/latest?clientId={CLIENT}&runtimeMode={MODE}")["flat"]


def parse_distance(value) -> float:
    if value is None or str(value).lower() == "null":
        return -1.0
    try:
        return float(value)
    except (TypeError, ValueError):
        return -1.0


def run_one(backend: str, run_idx: int, out_dir: Path,
            tick_s: float = 0.2, timeout_s: float = 30.0,
            front_stop_cm: float = 25.0, side_clear_cm: float = 35.0,
            turn_ticks: int = 4):
    """Single L-trace traversal using sensor-only logic."""
    out_dir.mkdir(parents=True, exist_ok=True)
    log_path = out_dir / f"sensor_run{run_idx}_steps.jsonl"
    summary_path = out_dir / f"sensor_run{run_idx}_summary.json"

    cmd(backend, "DirStop")
    time.sleep(0.5)

    # initial snapshot
    try:
        with open(out_dir / f"sensor_run{run_idx}_snap00.jpg", "wb") as f:
            f.write(http_bytes(backend, f"/api/camera/snapshot?clientId={CLIENT}&runtimeMode={MODE}"))
    except Exception as e:
        print(f"snap0 failed: {e}")

    t0 = time.time()
    last_snap = 0.0
    snap_idx = 1
    steps_log: list[dict] = []
    last_cmd = "DirStop"
    contacts = 0
    e_stop_count = 0
    final_reason = "timeout"
    forced_turn_ticks_left = 0
    forced_turn_dir: str | None = None

    print(f"[run{run_idx}] start, front_stop={front_stop_cm}cm side_clear={side_clear_cm}cm", flush=True)

    while True:
        elapsed = time.time() - t0
        if elapsed > timeout_s:
            final_reason = "timeout"
            break

        try:
            t = telemetry(backend)
        except Exception as e:
            print(f"  telemetry failed: {e}")
            time.sleep(tick_s)
            continue

        d_front = parse_distance(t.get("ultrasonic.distance_cm"))
        d_left = parse_distance(t.get("ultrasonic.scan.left_cm"))
        d_right = parse_distance(t.get("ultrasonic.scan.right_cm"))

        # Forced turn sequence active?
        if forced_turn_ticks_left > 0 and forced_turn_dir:
            chosen = forced_turn_dir
            forced_turn_ticks_left -= 1
        elif d_front > 0 and d_front < 12.0:
            chosen = "DirStop"
            e_stop_count += 1
            final_reason = "estop_front_wall"
        elif d_front > 0 and d_front < front_stop_cm:
            # decide turn direction
            if d_right > 0 and d_right > side_clear_cm:
                chosen = "DirRight"
                forced_turn_ticks_left = turn_ticks - 1
                forced_turn_dir = "DirRight"
            elif d_left > 0 and d_left > side_clear_cm:
                chosen = "DirLeft"
                forced_turn_ticks_left = turn_ticks - 1
                forced_turn_dir = "DirLeft"
            else:
                chosen = "DirStop"
                final_reason = "estop_no_turn_option"
        else:
            chosen = "DirForward"
            forced_turn_dir = None

        try:
            cmd(backend, chosen)
        except Exception as e:
            print(f"  cmd send failed: {e}")

        entry = {
            "t": round(elapsed, 3),
            "cmd": chosen,
            "front_cm": d_front,
            "left_cm": d_left,
            "right_cm": d_right,
            "forced_remaining": forced_turn_ticks_left,
        }
        steps_log.append(entry)

        if chosen != last_cmd:
            print(f"  t={elapsed:5.2f}s  {chosen:>10}  front={d_front:6.1f}  L={d_left:6.1f}  R={d_right:6.1f}",
                  flush=True)
            last_cmd = chosen

        if elapsed - last_snap >= 1.0:
            try:
                with open(out_dir / f"sensor_run{run_idx}_snap{snap_idx:02d}.jpg", "wb") as f:
                    f.write(http_bytes(backend, f"/api/camera/snapshot?clientId={CLIENT}&runtimeMode={MODE}"))
                snap_idx += 1
            except Exception as e:
                print(f"snap failed: {e}")
            last_snap = elapsed

        if final_reason.startswith("estop") and chosen == "DirStop":
            time.sleep(0.5)
            break

        time.sleep(tick_s)

    # final stop
    try:
        cmd(backend, "DirStop")
    except Exception:
        pass

    duration = time.time() - t0
    summary = {
        "run": run_idx,
        "kind": "sensor-only-baseline",
        "duration_s": round(duration, 3),
        "final_reason": final_reason,
        "total_ticks": len(steps_log),
        "estop_count": e_stop_count,
        "snapshots_count": snap_idx,
        "thresholds": {
            "front_stop_cm": front_stop_cm,
            "side_clear_cm": side_clear_cm,
            "turn_ticks": turn_ticks,
            "tick_s": tick_s,
        },
    }
    summary_path.write_text(json.dumps(summary, indent=2))
    with log_path.open("w") as f:
        for entry in steps_log:
            f.write(json.dumps(entry) + "\n")
    print(f"[run{run_idx}] DONE reason={final_reason} ticks={len(steps_log)} estop={e_stop_count} → {summary_path}")


def main() -> int:
    p = argparse.ArgumentParser()
    p.add_argument("--backend", default=BACKEND_DEFAULT)
    p.add_argument("--runs", type=int, default=5)
    p.add_argument("--output-dir", required=True)
    p.add_argument("--tick-s", type=float, default=0.2)
    p.add_argument("--timeout-s", type=float, default=30.0)
    p.add_argument("--front-stop-cm", type=float, default=25.0)
    p.add_argument("--side-clear-cm", type=float, default=35.0)
    p.add_argument("--turn-ticks", type=int, default=4)
    p.add_argument("--start-run", type=int, default=1, help="Starting run index (for restart)")
    args = p.parse_args()

    out_dir = Path(args.output_dir).expanduser().resolve()
    print(f"Backend:    {args.backend}")
    print(f"Output:     {out_dir}")
    print(f"Runs:       {args.runs}, tick={args.tick_s}s, timeout={args.timeout_s}s")
    print(f"Thresholds: front<{args.front_stop_cm}cm → turn, side>{args.side_clear_cm}cm preferred")
    print()

    for i in range(args.start_run, args.start_run + args.runs):
        print(f"=== Run {i} ===")
        print("Press Enter when robot is at start position (or wait 5s)...")
        try:
            import select
            ready, _, _ = select.select([sys.stdin], [], [], 5.0)
            if ready:
                sys.stdin.readline()
        except Exception:
            time.sleep(5)
        run_one(args.backend, i, out_dir,
                tick_s=args.tick_s, timeout_s=args.timeout_s,
                front_stop_cm=args.front_stop_cm, side_clear_cm=args.side_clear_cm,
                turn_ticks=args.turn_ticks)
        print()

    return 0


if __name__ == "__main__":
    raise SystemExit(main())
