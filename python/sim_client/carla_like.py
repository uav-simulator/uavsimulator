from __future__ import annotations

from dataclasses import dataclass
from fnmatch import fnmatch
from typing import Any, Dict, Iterable, List, Mapping, Optional, Sequence

from .http_client import SimClient


def _kv(key: str, value: str) -> Dict[str, str]:
    return {"key": key, "value": value}


@dataclass(frozen=True)
class Waypoint:
    x: float
    z: float
    y: float = 0.0

    def encode(self) -> str:
        return f"{self.x:.3f},{self.y:.3f},{self.z:.3f}"


@dataclass(frozen=True)
class Map:
    id: str
    name: str

    def generate_waypoints_grid(
        self,
        x_from: float,
        x_to: float,
        z_from: float,
        z_to: float,
        step_m: float,
    ) -> List[Waypoint]:
        if step_m <= 0:
            raise ValueError("step_m must be > 0")

        waypoints: List[Waypoint] = []
        x = x_from
        while x <= x_to + 1e-6:
            z = z_from
            while z <= z_to + 1e-6:
                waypoints.append(Waypoint(x=x, y=0.0, z=z))
                z += step_m
            x += step_m
        return waypoints


@dataclass(frozen=True)
class VehicleBlueprint:
    id: str
    display_name: str
    raw: Mapping[str, Any]


class BlueprintLibrary:
    def __init__(self, blueprints: Sequence[VehicleBlueprint]):
        self._blueprints = list(blueprints)

    def all(self) -> List[VehicleBlueprint]:
        return list(self._blueprints)

    def filter(self, pattern: str) -> List[VehicleBlueprint]:
        if not pattern or pattern == "*":
            return self.all()
        return [bp for bp in self._blueprints if fnmatch(bp.id, pattern)]

    def find(self, blueprint_id: str) -> VehicleBlueprint:
        for blueprint in self._blueprints:
            if blueprint.id == blueprint_id:
                return blueprint
        raise KeyError(f"Unknown blueprint id: '{blueprint_id}'.")


class World:
    def __init__(self, http_client: SimClient, contract: Mapping[str, Any]):
        self._http_client = http_client
        self._contract = dict(contract)
        self._active_track_id = self._first_track_id(self._contract)
        self._active_vehicle_id = self._first_vehicle_id(self._contract)

    def get_map(self) -> Map:
        maps = self.get_available_maps()
        for map_item in maps:
            if map_item.id == self._active_track_id:
                return map_item
        return maps[0] if maps else Map(id="", name="default")

    def get_available_maps(self) -> List[Map]:
        tracks = self._contract.get("availableTracks") or []
        maps: List[Map] = []
        for item in tracks:
            if not isinstance(item, Mapping):
                continue
            track_id = str(item.get("trackId") or "")
            if not track_id:
                continue
            maps.append(Map(id=track_id, name=str(item.get("displayName") or track_id)))
        return maps

    def get_blueprint_library(self) -> BlueprintLibrary:
        vehicles = self._contract.get("availableVehicles") or []
        blueprints: List[VehicleBlueprint] = []
        for item in vehicles:
            if not isinstance(item, Mapping):
                continue
            vehicle_id = str(item.get("deviceId") or "")
            if not vehicle_id:
                continue
            blueprints.append(
                VehicleBlueprint(
                    id=vehicle_id,
                    display_name=str(item.get("deviceType") or vehicle_id),
                    raw=item,
                )
            )
        return BlueprintLibrary(blueprints)

    def load_world(
        self,
        map_id: str,
        *,
        vehicle_id: str = "",
        seed: int = 0,
        time_scale: float = 1.0,
    ) -> Dict[str, Any]:
        return self.spawn_actor(
            vehicle_id=vehicle_id or self._active_vehicle_id,
            map_id=map_id,
            seed=seed,
            time_scale=time_scale,
        )

    def spawn_actor(
        self,
        vehicle_id: str,
        *,
        map_id: str = "",
        seed: int = 0,
        time_scale: float = 1.0,
        waypoints: Optional[Sequence[Waypoint]] = None,
        route_loop: bool = False,
        route_reach_distance_m: float = 1.0,
        track_params: Optional[Iterable[Mapping[str, Any]]] = None,
        vehicle_params: Optional[Iterable[Mapping[str, Any]]] = None,
    ) -> Dict[str, Any]:
        selected_track_id = map_id or self._active_track_id
        selected_vehicle_id = vehicle_id or self._active_vehicle_id

        normalized_track_params = _normalize_params(track_params)
        if waypoints:
            normalized_track_params.extend(_waypoint_params(waypoints, route_loop, route_reach_distance_m))

        config = {
            "seed": int(seed),
            "timeScale": float(time_scale),
            "selectedTrackId": selected_track_id,
            "selectedVehicleId": selected_vehicle_id,
            "trackParams": normalized_track_params,
            "vehicleParams": _normalize_params(vehicle_params),
            "flags": [],
        }

        result = self._http_client.reset(config)
        if selected_track_id:
            self._active_track_id = selected_track_id
        if selected_vehicle_id:
            self._active_vehicle_id = selected_vehicle_id
        return result

    def tick(self, command: Optional[Mapping[str, Any]] = None) -> Dict[str, Any]:
        return self._http_client.step(dict(command) if command is not None else default_command())

    @staticmethod
    def _first_track_id(contract: Mapping[str, Any]) -> str:
        tracks = contract.get("availableTracks") or []
        for item in tracks:
            if isinstance(item, Mapping):
                candidate = str(item.get("trackId") or "")
                if candidate:
                    return candidate
        return ""

    @staticmethod
    def _first_vehicle_id(contract: Mapping[str, Any]) -> str:
        vehicles = contract.get("availableVehicles") or []
        for item in vehicles:
            if isinstance(item, Mapping):
                candidate = str(item.get("deviceId") or "")
                if candidate:
                    return candidate
        return ""


class Client:
    def __init__(
        self,
        *,
        base_url: str = "http://127.0.0.1:8000",
        timeout_s: float = 10.0,
    ) -> None:
        self._http_client = SimClient(base_url=base_url, timeout_s=timeout_s)

    def health(self) -> Mapping[str, Any]:
        return self._http_client.health()

    def get_world(self) -> World:
        contract = self._http_client.get_contract()
        return World(self._http_client, contract)


def default_command() -> Dict[str, Any]:
    return {
        "throttle": 0.0,
        "steer": 0.0,
        "brake": 0.0,
        "timestamp": 0,
        "timeBase": "unix_ms",
        "extensions": [],
    }


def _waypoint_params(
    waypoints: Sequence[Waypoint],
    route_loop: bool,
    route_reach_distance_m: float,
) -> List[Dict[str, str]]:
    encoded = ";".join(wp.encode() for wp in waypoints)
    params = [_kv("route.waypoints", encoded)]
    params.append(_kv("route.loop", "true" if route_loop else "false"))
    params.append(_kv("route.reach_distance_m", f"{float(route_reach_distance_m):.3f}"))
    return params


def _normalize_params(params: Optional[Iterable[Mapping[str, Any]]]) -> List[Dict[str, str]]:
    if params is None:
        return []

    normalized: List[Dict[str, str]] = []
    for item in params:
        if not isinstance(item, Mapping):
            continue
        key = item.get("key")
        value = item.get("value")
        if key is None or value is None:
            continue
        normalized.append(_kv(str(key), str(value)))
    return normalized
