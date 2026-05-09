from __future__ import annotations

from typing import Any


def validate_contract(contract: dict[str, Any]) -> tuple[bool, list[str]]:
    errors: list[str] = []

    if not isinstance(contract, dict):
        return False, ["contract must be an object"]

    for key in ("simulatorId", "simulatorName", "contractVersion"):
        if key not in contract:
            errors.append(f"missing field: {key}")

    vehicles = contract.get("availableVehicles", [])
    if vehicles is not None and not isinstance(vehicles, list):
        errors.append("availableVehicles must be a list")

    tracks = contract.get("availableTracks", [])
    if tracks is not None and not isinstance(tracks, list):
        errors.append("availableTracks must be a list")

    return len(errors) == 0, errors

