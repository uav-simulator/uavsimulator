from __future__ import annotations

import argparse
import json
import time

from sim_client.http_client import SimClient


def main() -> None:
    ap = argparse.ArgumentParser()
    ap.add_argument("--base-url", required=True)
    ap.add_argument("--out", required=True)
    ap.add_argument("--steps", type=int, default=200)
    ap.add_argument("--seed", type=int, default=0)
    ap.add_argument("--vehicle-id", default="")
    ap.add_argument("--track-id", default="")
    args = ap.parse_args()

    client = SimClient(args.base_url)
    cfg = {
        "seed": args.seed,
        "timeScale": 1.0,
        "selectedTrackId": args.track_id,
        "selectedVehicleId": args.vehicle_id,
        "trackParams": [],
        "vehicleParams": [],
        "flags": [],
    }
    client.reset(cfg)

    with open(args.out, "w", encoding="utf-8") as f:
        for i in range(args.steps):
            cmd = {
                "throttle": 0.5,
                "steer": 0.0,
                "brake": 0.0,
                "timestamp": int(time.time() * 1000),
                "timeBase": "unix_ms",
            }
            step = client.step(cmd)
            f.write(json.dumps({"i": i, "command": cmd, "step": step}, ensure_ascii=False) + "\n")


if __name__ == "__main__":
    main()
