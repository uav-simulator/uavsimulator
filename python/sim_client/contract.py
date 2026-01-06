from __future__ import annotations

from typing import Any, Dict, List, Tuple


def validate_contract(contract: Dict[str, Any]) -> Tuple[bool, List[str]]:
    errors: List[str] = []

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

