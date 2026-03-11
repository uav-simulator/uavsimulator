from __future__ import annotations

import argparse
import json
import sys
from typing import Any, Dict

from .contract import validate_contract
from .http_client import SimClient
from .scenario import load_scenario_file, scenario_to_reset_config, validate_scenario


def build_parser() -> argparse.ArgumentParser:
    parser = argparse.ArgumentParser(description="CLI for uav-simulator operator/runtime flows.")
    subparsers = parser.add_subparsers(dest="command", required=True)

    doctor = subparsers.add_parser("doctor", help="Check simulator health and contract.")
    doctor.add_argument("--base-url", default="http://127.0.0.1:8000")

    contract = subparsers.add_parser("contract", help="Print simulator contract.")
    contract.add_argument("--base-url", default="http://127.0.0.1:8000")

    scenario = subparsers.add_parser("scenario", help="Scenario file operations.")
    scenario_sub = scenario.add_subparsers(dest="scenario_command", required=True)

    validate_cmd = scenario_sub.add_parser("validate", help="Validate scenario file.")
    validate_cmd.add_argument("file")

    reset_cmd = scenario_sub.add_parser("reset", help="Reset simulator from scenario file.")
    reset_cmd.add_argument("file")
    reset_cmd.add_argument("--base-url", default="http://127.0.0.1:8000")

    print_reset_cmd = scenario_sub.add_parser("print-reset", help="Print reset payload derived from scenario.")
    print_reset_cmd.add_argument("file")

    step = subparsers.add_parser("step", help="Send a single control step.")
    step.add_argument("--base-url", default="http://127.0.0.1:8000")
    step.add_argument("--throttle", type=float, default=0.0)
    step.add_argument("--steer", type=float, default=0.0)
    step.add_argument("--brake", type=float, default=0.0)

    return parser


def main(argv: list[str] | None = None) -> int:
    parser = build_parser()
    args = parser.parse_args(argv)

    try:
        if args.command == "doctor":
            return _doctor(args.base_url)
        if args.command == "contract":
            return _contract(args.base_url)
        if args.command == "scenario":
            return _scenario(args)
        if args.command == "step":
            return _step(args.base_url, args.throttle, args.steer, args.brake)
    except Exception as exc:  # pragma: no cover - CLI boundary
        print(f"error: {exc}", file=sys.stderr)
        return 1

    parser.error("Unknown command")
    return 2


def _doctor(base_url: str) -> int:
    client = SimClient(base_url=base_url)
    health = client.health()
    contract = client.get_contract()
    ok, errors = validate_contract(contract)

    result = {
        "baseUrl": base_url,
        "healthStatus": health.get("status"),
        "contractValid": ok,
        "contractErrors": errors,
        "simulatorId": contract.get("simulatorId"),
        "simulatorName": contract.get("simulatorName"),
        "vehicles": len(contract.get("availableVehicles") or []),
        "tracks": len(contract.get("availableTracks") or []),
    }
    print(json.dumps(result, ensure_ascii=False, indent=2))
    return 0 if ok else 1


def _contract(base_url: str) -> int:
    client = SimClient(base_url=base_url)
    print(json.dumps(client.get_contract(), ensure_ascii=False, indent=2))
    return 0


def _scenario(args: argparse.Namespace) -> int:
    payload = load_scenario_file(args.file)
    ok, errors = validate_scenario(payload)
    if args.scenario_command == "validate":
        print(
            json.dumps(
                {
                    "file": args.file,
                    "valid": ok,
                    "errors": errors,
                    "scenarioId": payload.get("scenarioId"),
                },
                ensure_ascii=False,
                indent=2,
            )
        )
        return 0 if ok else 1

    reset_payload = scenario_to_reset_config(payload)

    if args.scenario_command == "print-reset":
        print(json.dumps(reset_payload, ensure_ascii=False, indent=2))
        return 0

    if not ok:
        print(json.dumps({"file": args.file, "valid": False, "errors": errors}, ensure_ascii=False, indent=2))
        return 1

    client = SimClient(base_url=args.base_url)
    response = client.reset(reset_payload)
    print(
        json.dumps(
            {
                "baseUrl": args.base_url,
                "scenarioId": payload.get("scenarioId"),
                "selectedTrackId": reset_payload.get("selectedTrackId"),
                "selectedVehicleId": reset_payload.get("selectedVehicleId"),
                "done": response.get("done"),
                "hasFrame": bool(response.get("frame")),
            },
            ensure_ascii=False,
            indent=2,
        )
    )
    return 0


def _step(base_url: str, throttle: float, steer: float, brake: float) -> int:
    client = SimClient(base_url=base_url)
    payload: Dict[str, Any] = {
        "throttle": throttle,
        "steer": steer,
        "brake": brake,
        "timestamp": 0,
        "timeBase": "unix_ms",
        "extensions": [],
    }
    response = client.step(payload)
    print(
        json.dumps(
            {
                "speed": (response.get("state") or {}).get("speed"),
                "reward": response.get("reward"),
                "done": response.get("done"),
                "hasFrame": bool(response.get("frame")),
            },
            ensure_ascii=False,
            indent=2,
        )
    )
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
