from __future__ import annotations

import argparse
import json
import random

from sim_client import Client, Waypoint, default_command


def main() -> int:
    parser = argparse.ArgumentParser(description="CARLA-like quickstart for uav-simulator.")
    parser.add_argument("--base-url", default="http://127.0.0.1:8000")
    parser.add_argument("--map-id", default="")
    parser.add_argument("--vehicle-id", default="")
    parser.add_argument("--steps", type=int, default=30)
    args = parser.parse_args()

    client = Client(base_url=args.base_url)
    print("health:", client.health())

    world = client.get_world()
    maps = world.get_available_maps()
    print("maps:", [item.id for item in maps])

    blueprints = world.get_blueprint_library().all()
    print("vehicles:", [item.id for item in blueprints])

    map_id = args.map_id or (maps[0].id if maps else "")
    vehicle_id = args.vehicle_id or (blueprints[0].id if blueprints else "")

    route = [
        Waypoint(x=-6.0, z=-8.0),
        Waypoint(x=-6.0, z=-1.0),
        Waypoint(x=4.0, z=2.0),
        Waypoint(x=6.0, z=8.0),
    ]

    reset_result = world.spawn_actor(
        vehicle_id=vehicle_id,
        map_id=map_id,
        waypoints=route,
        route_reach_distance_m=1.2,
    )
    print("reset:", json.dumps({"map_id": map_id, "vehicle_id": vehicle_id, "has_frame": bool(reset_result.get("frame"))}))

    for idx in range(args.steps):
        cmd = default_command()
        cmd["throttle"] = random.uniform(0.05, 0.35)
        cmd["steer"] = random.uniform(-0.35, 0.35)

        step = world.tick(cmd)
        info = {item.get("key"): item.get("value") for item in step.get("info", []) if isinstance(item, dict)}
        print(
            json.dumps(
                {
                    "step": idx,
                    "speed": (step.get("state") or {}).get("speed"),
                    "route_index": info.get("route.current_index"),
                    "route_remaining": info.get("route.remaining_waypoints"),
                    "route_completed": info.get("route.completed"),
                }
            )
        )

    return 0


if __name__ == "__main__":
    raise SystemExit(main())
