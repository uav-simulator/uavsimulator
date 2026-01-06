from __future__ import annotations

import argparse
import random

from sim_client.http_client import SimClient


def main() -> None:
    ap = argparse.ArgumentParser()
    ap.add_argument("--base-url", required=True)
    ap.add_argument("--steps", type=int, default=50)
    ap.add_argument("--seed", type=int, default=0)
    ap.add_argument("--vehicle-id", default="")
    ap.add_argument("--track-id", default="")
    args = ap.parse_args()

    random.seed(args.seed)
    client = SimClient(args.base_url)

    print(client.health())
    contract = client.get_contract()
    print({"contractVersion": contract.get("contractVersion")})

    cfg = {
        "seed": args.seed,
        "timeScale": 1.0,
        "selectedTrackId": args.track_id,
        "selectedVehicleId": args.vehicle_id,
        "trackParams": [],
        "vehicleParams": [],
        "flags": [],
    }

    reset_result = client.reset(cfg)
    print({"reset": True, "state": bool(reset_result.get("state"))})

    for _ in range(args.steps):
        cmd = {
            "throttle": random.uniform(0.0, 1.0),
            "steer": random.uniform(-1.0, 1.0),
            "brake": 0.0,
            "timestamp": 0,
            "timeBase": "unix_ms",
        }
        step = client.step(cmd)
        print({"done": step.get("done"), "reward": step.get("reward")})


if __name__ == "__main__":
    main()
