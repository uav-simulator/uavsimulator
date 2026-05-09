from __future__ import annotations

import json
from collections.abc import Iterable, Mapping
from pathlib import Path
from typing import Any

try:
    import yaml
except ModuleNotFoundError:  # pragma: no cover - optional until installed
    yaml = None


def load_scenario_file(path: str | Path) -> dict[str, Any]:
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


def validate_scenario(payload: Mapping[str, Any]) -> tuple[bool, list[str]]:
    errors: list[str] = []

    if not isinstance(payload, Mapping):
        return False, ["scenario must be an object"]

    runtime = _expect_mapping(payload, "runtime", errors)
    world = _expect_mapping(payload, "world", errors)
    vehicle = payload.get("vehicle")
    if vehicle is not None and not isinstance(vehicle, Mapping):
        errors.append("vehicle must be an object when present")
        vehicle = None

    if runtime is not None:
        _expect_string(runtime, "runtimeMode", errors)
        _expect_bool_like(runtime, "headless", errors)
    if world is not None:
        _expect_string(world, "trackId", errors)
    if isinstance(vehicle, Mapping):
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
    agent_vehicle_entries = 0
    if agents is not None:
        if not isinstance(agents, Mapping):
            errors.append("agents must be an object when present")
        else:
            for bool_key in ("isolated", "seeEachOther", "collisionsEnabled"):
                if bool_key in agents:
                    _expect_bool_like(agents, bool_key, errors)

            if "count" in agents and not isinstance(agents.get("count"), int):
                errors.append("agents.count must be an integer when present")

            vehicles = agents.get("vehicles")
            if vehicles is not None:
                if not isinstance(vehicles, list):
                    errors.append("agents.vehicles must be a list when present")
                else:
                    agent_vehicle_entries = len(vehicles)
                    for index, item in enumerate(vehicles):
                        if not isinstance(item, Mapping):
                            errors.append(f"agents.vehicles[{index}] must be an object")
                            continue

                        if "agentId" in item and (not isinstance(item.get("agentId"), str) or not str(item.get("agentId")).strip()):
                            errors.append(f"agents.vehicles[{index}].agentId must be a non-empty string when present")
                        if "vehicleId" in item and (
                            not isinstance(item.get("vehicleId"), str) or not str(item.get("vehicleId")).strip()
                        ):
                            errors.append(f"agents.vehicles[{index}].vehicleId must be a non-empty string when present")

    if not isinstance(vehicle, Mapping) and agent_vehicle_entries == 0:
        errors.append("vehicle must be an object unless agents.vehicles is provided")

    return len(errors) == 0, errors


def scenario_to_reset_config(payload: Mapping[str, Any]) -> dict[str, Any]:
    world = _as_mapping(payload.get("world"))
    vehicle = _as_mapping(payload.get("vehicle"))
    route = _as_mapping(payload.get("route"))
    runtime = _as_mapping(payload.get("runtime"))
    agents = _as_mapping(payload.get("agents"))
    sensors = _as_mapping(payload.get("sensors"))

    track_params: list[dict[str, str]] = []
    vehicle_params: list[dict[str, str]] = []
    flags: list[dict[str, str]] = []

    for item in _mapping_items(route, "params"):
        track_params.append(_kv(item[0], item[1]))
    for item in _mapping_items(world, "params"):
        track_params.append(_kv(item[0], item[1]))
    for item in _mapping_items(vehicle, "params"):
        vehicle_params.append(_kv(item[0], item[1]))

    camera = _as_mapping(sensors.get("camera"))
    camera_profile = camera.get("profile")
    if isinstance(camera_profile, str) and camera_profile.strip():
        _upsert_param(vehicle_params, "camera.profile", camera_profile.strip())

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

    if "isolated" in agents:
        flags.append(_kv("agents.isolated", _bool_str(agents.get("isolated"))))
    if "seeEachOther" in agents:
        flags.append(_kv("agents.see_each_other", _bool_str(agents.get("seeEachOther"))))
    if "collisionsEnabled" in agents:
        flags.append(_kv("agents.collisions_enabled", _bool_str(agents.get("collisionsEnabled"))))

    agent_entries = _build_agents_payload(agents, vehicle, vehicle_params)
    selected_vehicle_id = str(vehicle.get("vehicleId", ""))
    if agent_entries:
        primary_agent = next((item for item in agent_entries if item.get("isPrimary")), agent_entries[0])
        selected_vehicle_id = str(primary_agent.get("vehicleId", selected_vehicle_id))

    return {
        "seed": int(runtime.get("seed", payload.get("seed", 0)) or 0),
        "timeScale": float(runtime.get("timeScale", payload.get("timeScale", 1.0)) or 1.0),
        "selectedTrackId": str(world.get("trackId", "")),
        "selectedVehicleId": selected_vehicle_id,
        "trackParams": track_params,
        "vehicleParams": vehicle_params,
        "flags": flags,
        "agents": agent_entries,
    }


def _expect_mapping(payload: Mapping[str, Any], key: str, errors: list[str]) -> Mapping[str, Any] | None:
    value = payload.get(key)
    if not isinstance(value, Mapping):
        errors.append(f"{key} must be an object")
        return None
    return value


def _expect_string(payload: Mapping[str, Any], key: str, errors: list[str]) -> None:
    value = payload.get(key)
    if not isinstance(value, str) or not value.strip():
        errors.append(f"{key} must be a non-empty string")


def _expect_bool_like(payload: Mapping[str, Any], key: str, errors: list[str]) -> None:
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


def _mapping_items(payload: Mapping[str, Any], key: str) -> Iterable[tuple[str, str]]:
    value = payload.get(key)
    if not isinstance(value, Mapping):
        return []
    return [(str(k), str(v)) for k, v in value.items()]


def _as_mapping(value: Any) -> Mapping[str, Any]:
    return value if isinstance(value, Mapping) else {}


def _bool_str(value: Any) -> str:
    return "true" if bool(value) else "false"


def _kv(key: str, value: str) -> dict[str, str]:
    return {"key": key, "value": value}


def _upsert_param(items: list[dict[str, str]], key: str, value: str) -> None:
    for item in items:
        if item.get("key") == key:
            item["value"] = value
            return
    items.append(_kv(key, value))


def _build_agents_payload(
    agents: Mapping[str, Any],
    vehicle: Mapping[str, Any],
    base_vehicle_params: list[dict[str, str]],
) -> list[dict[str, Any]]:
    vehicles = agents.get("vehicles")
    if isinstance(vehicles, list) and vehicles:
        result: list[dict[str, Any]] = []
        has_primary = any(isinstance(item, Mapping) and bool(item.get("primary")) for item in vehicles)
        for index, item in enumerate(vehicles):
            if not isinstance(item, Mapping):
                continue
            agent_id = str(item.get("agentId") or f"agent-{index + 1}")
            vehicle_id = str(item.get("vehicleId") or vehicle.get("vehicleId") or "")
            agent_vehicle_params = list(_mapping_items(item, "params"))
            payload = {
                "agentId": agent_id,
                "vehicleId": vehicle_id,
                "isPrimary": bool(item.get("primary")) if has_primary else index == 0,
                "trackParams": _agent_track_params(item),
                "vehicleParams": [_kv(key, value) for key, value in agent_vehicle_params],
                "flags": [],
            }
            result.append(payload)
        return result

    count = int(agents.get("count", 0) or 0)
    if count <= 1:
        return []

    vehicle_id = str(vehicle.get("vehicleId", ""))
    result = []
    for index in range(count):
        result.append(
            {
                "agentId": "ego" if index == 0 else f"agent-{index + 1}",
                "vehicleId": vehicle_id,
                "isPrimary": index == 0,
                "trackParams": [],
                "vehicleParams": list(base_vehicle_params) if index == 0 else [],
                "flags": [],
            }
        )
    return result


def _agent_track_params(payload: Mapping[str, Any]) -> list[dict[str, str]]:
    result = [_kv(key, value) for key, value in _mapping_items(payload, "trackParams")]
    spawn_pose = payload.get("spawnPose")
    if not isinstance(spawn_pose, Mapping):
        return result

    position = spawn_pose.get("position")
    encoded_position = _encode_spawn_position(position)
    if encoded_position is not None:
        result.append(_kv("spawn.position", encoded_position))

    yaw_value = spawn_pose.get("yawDeg", spawn_pose.get("yaw_deg"))
    if yaw_value is not None:
        result.append(_kv("spawn.yaw_deg", str(float(yaw_value))))

    return result


def _encode_spawn_position(value: Any) -> str | None:
    if isinstance(value, Mapping):
        x = float(value.get("x", 0.0))
        y = float(value.get("y", 0.2))
        z = float(value.get("z", 0.0))
        return f"{x:.3f},{y:.3f},{z:.3f}"

    if isinstance(value, (list, tuple)):
        if len(value) == 2:
            x, z = value
            return f"{float(x):.3f},0.200,{float(z):.3f}"
        if len(value) == 3:
            x, y, z = value
            return f"{float(x):.3f},{float(y):.3f},{float(z):.3f}"

    return None
