#!/usr/bin/env python3
"""Generate a circular "swarm" agent layout for showcase visualisation.

Emits either a YAML fragment for ``agents.vehicles[]`` (default) or a full
scenario document (``--full``). Each agent sits on a circle of given radius
and faces tangent counter-clockwise.

Yaw convention follows Unity (Y-up, left-handed): yaw=0 points along +Z,
yaw increases towards +X. For a position at angle ``theta`` on the x-z circle,
the CCW tangent direction is ``(-sin theta, cos theta)`` in (x, z), so
``yawDeg = atan2(-sin theta, cos theta) = -theta``.

Example:
    python python/sim_client/scripts/generate_swarm_scenario.py \
        --n 16 --radius 8 --colors blue,red,gray,purple > /tmp/agents.yaml
"""
from __future__ import annotations

import argparse
import math
import sys
from typing import Any

try:
    import yaml  # type: ignore
except ModuleNotFoundError:  # pragma: no cover - fallback path
    yaml = None  # noqa: N816


SPAWN_HEIGHT = 0.2


def _round(x: float, n: int = 3) -> float:
    """Round to ``n`` decimals and normalise -0.0 -> 0.0."""
    r = round(x, n)
    return 0.0 if r == 0 else r


def build_vehicles(
    n: int, radius: float, colors: list[str]
) -> list[dict[str, Any]]:
    if n < 1:
        raise ValueError("n must be >= 1")
    if radius <= 0:
        raise ValueError("radius must be > 0")
    if not colors:
        raise ValueError("colors must not be empty")

    vehicles: list[dict[str, Any]] = []
    for i in range(n):
        theta = 2.0 * math.pi * i / n  # radians, CCW from +X axis
        x = radius * math.cos(theta)
        z = radius * math.sin(theta)
        # CCW tangent direction: (-sin theta, cos theta) in (x, z).
        yaw_deg = math.degrees(math.atan2(-math.sin(theta), math.cos(theta)))

        color = colors[i % len(colors)]
        vehicle_id = f"vehicle.arcade.{color}.v1"

        if i == 0:
            agent_id = "ego"
            primary = True
        else:
            agent_id = f"agent-{i:02d}"
            primary = False

        entry: dict[str, Any] = {
            "agentId": agent_id,
            "vehicleId": vehicle_id,
        }
        if primary:
            entry["primary"] = True
        entry["spawnPose"] = {
            "position": [_round(x), _round(SPAWN_HEIGHT), _round(z)],
            "yawDeg": _round(yaw_deg, 2),
        }
        vehicles.append(entry)
    return vehicles


def build_full_scenario(
    n: int,
    radius: float,
    colors: list[str],
    track_id: str,
) -> dict[str, Any]:
    vehicles = build_vehicles(n, radius, colors)
    primary_vehicle_id = vehicles[0]["vehicleId"]
    return {
        "scenarioId": "demo-swarm",
        "displayName": "Multi-Agent Swarm Demo",
        "description": (
            f"{n} arcade cars on a radius-{radius:g}m circle for "
            "showcase visualisation of parallel-training swarm rendering."
        ),
        "version": 1,
        "runtime": {
            "runtimeMode": "unity-sim",
            "headless": False,
            "timeScale": 1.0,
            "seed": 42,
        },
        "world": {"trackId": track_id},
        "vehicle": {
            "vehicleId": primary_vehicle_id,
            "params": {"camera.profile": "high"},
        },
        "agents": {
            "isolated": False,
            "seeEachOther": True,
            "collisionsEnabled": False,
            "vehicles": vehicles,
        },
        "logging": {"enabled": True, "tag": "demo-swarm"},
    }


# ---------------------------------------------------------------------------
# Pure-string YAML fallback (no PyYAML).
# ---------------------------------------------------------------------------
def _emit_scalar(v: Any) -> str:
    if isinstance(v, bool):
        return "true" if v else "false"
    if isinstance(v, (int, float)):
        # Avoid trailing .0 for whole-number floats stored as int-valued floats.
        if isinstance(v, float) and v.is_integer():
            return str(int(v))
        return str(v)
    if v is None:
        return "null"
    return str(v)


def _emit_list_inline(items: list[Any]) -> str:
    return "[" + ", ".join(_emit_scalar(x) for x in items) + "]"


def _emit_vehicles_fragment(vehicles: list[dict[str, Any]]) -> str:
    """Emit just the list-of-mapping content under ``agents.vehicles:``."""
    lines: list[str] = []
    for v in vehicles:
        lines.append(f"  - agentId: {v['agentId']}")
        lines.append(f"    vehicleId: {v['vehicleId']}")
        if v.get("primary"):
            lines.append("    primary: true")
        sp = v["spawnPose"]
        lines.append("    spawnPose:")
        lines.append(f"      position: {_emit_list_inline(sp['position'])}")
        lines.append(f"      yawDeg: {_emit_scalar(sp['yawDeg'])}")
    return "\n".join(lines) + "\n"


def emit_fragment(vehicles: list[dict[str, Any]]) -> str:
    if yaml is not None:
        return yaml.safe_dump(
            vehicles, sort_keys=False, allow_unicode=True, default_flow_style=False
        )
    return _emit_vehicles_fragment(vehicles)


def emit_full(scenario: dict[str, Any]) -> str:
    if yaml is not None:
        return yaml.safe_dump(
            scenario, sort_keys=False, allow_unicode=True, default_flow_style=None
        )
    # Minimal hand-rolled emitter for the full doc — only used if PyYAML is
    # missing. Parses round-trip via PyYAML at load time anyway.
    raise RuntimeError(
        "PyYAML is required for --full output; install with `pip install PyYAML`."
    )


def parse_args(argv: list[str]) -> argparse.Namespace:
    p = argparse.ArgumentParser(
        description="Generate circular swarm agent layout for rusim scenarios."
    )
    p.add_argument("--n", type=int, default=16, help="number of agents (default: 16)")
    p.add_argument(
        "--radius",
        type=float,
        default=8.0,
        help="circle radius in metres (default: 8.0)",
    )
    p.add_argument(
        "--colors",
        type=str,
        default="blue,red,gray,purple",
        help="comma-separated arcade colors to rotate through "
        "(default: blue,red,gray,purple)",
    )
    p.add_argument(
        "--track",
        type=str,
        default="track.basic_arena.v1",
        help="trackId for --full output (default: track.basic_arena.v1)",
    )
    p.add_argument(
        "--full",
        action="store_true",
        help="emit a full scenario document (requires PyYAML), not just the "
        "agents.vehicles[] fragment",
    )
    return p.parse_args(argv)


def main(argv: list[str]) -> int:
    args = parse_args(argv)
    colors = [c.strip() for c in args.colors.split(",") if c.strip()]
    if not colors:
        print("error: --colors produced an empty list", file=sys.stderr)
        return 2
    if args.full:
        scenario = build_full_scenario(args.n, args.radius, colors, args.track)
        sys.stdout.write(emit_full(scenario))
    else:
        vehicles = build_vehicles(args.n, args.radius, colors)
        sys.stdout.write(emit_fragment(vehicles))
    return 0


if __name__ == "__main__":  # pragma: no cover
    raise SystemExit(main(sys.argv[1:]))
