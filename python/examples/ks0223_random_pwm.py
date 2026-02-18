from __future__ import annotations

import argparse
import random

from sim_client.http_client import SimClient
from sim_client.ks0223 import Ks0223Command, parse_telemetry


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
    print({"vehicles": len(contract.get("availableVehicles", [])), "tracks": len(contract.get("availableTracks", []))})

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
    print({"reset": True, "speed": (reset_result.get("state") or {}).get("speed")})

    for i in range(args.steps):
        cmd = Ks0223Command(
            left_pwm_norm=random.uniform(-0.9, 0.9),
            right_pwm_norm=random.uniform(-0.9, 0.9),
            brake=0.0,
            timestamp=i,
        )
        step = client.step(cmd.to_step_command())
        telemetry = parse_telemetry(step)
        print(
            {
                "i": i,
                "speed": (step.get("state") or {}).get("speed"),
                "left": telemetry.get("drive.left_pwm_norm"),
                "right": telemetry.get("drive.right_pwm_norm"),
            }
        )


if __name__ == "__main__":
    main()
