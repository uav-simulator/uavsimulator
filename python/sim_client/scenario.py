from __future__ import annotations

import json
from pathlib import Path
from typing import Any, Dict, Iterable, List, Mapping, Tuple

try:
    import yaml
except ModuleNotFoundError:  # pragma: no cover - optional until installed
    yaml = None


def load_scenario_file(path: str | Path) -> Dict[str, Any]:
    file_path = Path(path)
    if not file_path.exists():
        raise FileNotFoundError(f"Scenario file not found: {file_path}")

    suffix = file_path.suffix.lower()
    raw = file_path.read_text(encoding="utf-8")

    if suffix == ".json":
        payload = json.loads(raw)
    elif suffix in {".yaml", ".yml"}:
        if yaml is None:
            raise RuntimeError("PyYAML is required to load YAML scenarios.")
        payload = yaml.safe_load(raw)
    else:
        raise ValueError(f"Unsupported scenario file extension: {suffix}")

    if not isinstance(payload, dict):
        raise ValueError("Scenario document must be a JSON/YAML object.")

    return payload


def validate_scenario(payload: Mapping[str, Any]) -> Tuple[bool, List[str]]:
    errors: List[str] = []

    if not isinstance(payload, Mapping):
        return False, ["scenario must be an object"]

    runtime = _expect_mapping(payload, "runtime", errors)
    world = _expect_mapping(payload, "world", errors)
    vehicle = _expect_mapping(payload, "vehicle", errors)

    if runtime is not None:
        _expect_string(runtime, "runtimeMode", errors)
        _expect_bool_like(runtime, "headless", errors)
    if world is not None:
        _expect_string(world, "trackId", errors)
    if vehicle is not None:
        _expect_string(vehicle, "vehicleId", errors)

    sensors = payload.get("sensors")
    if sensors is not None and not isinstance(sensors, Mapping):
        errors.append("sensors must be an object when present")

    route = payload.get("route")
    if route is not None:
        if not isinstance(route, Mapping):
            errors.append("route must be an object when present")
        else:
            waypoints = route.get("waypoints")
            if waypoints is not None and not isinstance(waypoints, list):
                errors.append("route.waypoints must be a list when present")

    agents = payload.get("agents")
    if agents is not None and not isinstance(agents, Mapping):
        errors.append("agents must be an object when present")

    return len(errors) == 0, errors


def scenario_to_reset_config(payload: Mapping[str, Any]) -> Dict[str, Any]:
    world = _as_mapping(payload.get("world"))
    vehicle = _as_mapping(payload.get("vehicle"))
    route = _as_mapping(payload.get("route"))
    runtime = _as_mapping(payload.get("runtime"))

    track_params: List[Dict[str, str]] = []
    vehicle_params: List[Dict[str, str]] = []
    flags: List[Dict[str, str]] = []

    for item in _mapping_items(route, "params"):
        track_params.append(_kv(item[0], item[1]))
    for item in _mapping_items(vehicle, "params"):
        vehicle_params.append(_kv(item[0], item[1]))

    waypoints = route.get("waypoints")
    if isinstance(waypoints, list) and waypoints:
        encoded_waypoints = ";".join(_encode_waypoint(item) for item in waypoints)
        track_params.append(_kv("route.waypoints", encoded_waypoints))
        track_params.append(_kv("route.loop", _bool_str(route.get("loop", False))))
        reach_distance = route.get("reachDistanceM", route.get("reach_distance_m", 1.0))
        track_params.append(_kv("route.reach_distance_m", str(float(reach_distance))))

    headless = runtime.get("headless")
    if headless is not None:
        flags.append(_kv("runtime.headless", _bool_str(headless)))

    return {
        "seed": int(runtime.get("seed", payload.get("seed", 0)) or 0),
        "timeScale": float(runtime.get("timeScale", payload.get("timeScale", 1.0)) or 1.0),
        "selectedTrackId": str(world.get("trackId", "")),
        "selectedVehicleId": str(vehicle.get("vehicleId", "")),
        "trackParams": track_params,
        "vehicleParams": vehicle_params,
        "flags": flags,
    }


def _expect_mapping(payload: Mapping[str, Any], key: str, errors: List[str]) -> Mapping[str, Any] | None:
    value = payload.get(key)
    if not isinstance(value, Mapping):
        errors.append(f"{key} must be an object")
        return None
    return value


def _expect_string(payload: Mapping[str, Any], key: str, errors: List[str]) -> None:
    value = payload.get(key)
    if not isinstance(value, str) or not value.strip():
        errors.append(f"{key} must be a non-empty string")


def _expect_bool_like(payload: Mapping[str, Any], key: str, errors: List[str]) -> None:
    value = payload.get(key)
    if not isinstance(value, bool):
        errors.append(f"{key} must be a boolean")


def _encode_waypoint(item: Any) -> str:
    if isinstance(item, Mapping):
        x = float(item.get("x", 0.0))
        y = float(item.get("y", 0.0))
        z = float(item.get("z", 0.0))
        return f"{x:.3f},{y:.3f},{z:.3f}"

    if isinstance(item, (list, tuple)):
        if len(item) == 2:
            x, z = item
            return f"{float(x):.3f},0.000,{float(z):.3f}"
        if len(item) == 3:
            x, y, z = item
            return f"{float(x):.3f},{float(y):.3f},{float(z):.3f}"

    raise ValueError(f"Unsupported waypoint format: {item!r}")


def _mapping_items(payload: Mapping[str, Any], key: str) -> Iterable[Tuple[str, str]]:
    value = payload.get(key)
    if not isinstance(value, Mapping):
        return []
    return [(str(k), str(v)) for k, v in value.items()]


def _as_mapping(value: Any) -> Mapping[str, Any]:
    return value if isinstance(value, Mapping) else {}


def _bool_str(value: Any) -> str:
    return "true" if bool(value) else "false"


def _kv(key: str, value: str) -> Dict[str, str]:
    return {"key": key, "value": value}
