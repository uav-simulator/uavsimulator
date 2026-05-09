#!/usr/bin/env python3
"""Evaluate A->B baseline policy in unity-sim and export KPI evidence."""

from __future__ import annotations

import argparse
import json
import math
import sys
from collections import Counter
from dataclasses import dataclass
from pathlib import Path
from typing import Any

import numpy as np
import onnxruntime as ort

ROOT = Path(__file__).resolve().parents[2]
PYTHON_ROOT = ROOT / "python"
if str(PYTHON_ROOT) not in sys.path:
    sys.path.insert(0, str(PYTHON_ROOT))

from sim_client.http_client import SimClient  # noqa: E402
from sim_client.scenario import load_scenario_file, scenario_to_reset_config  # noqa: E402


@dataclass(frozen=True)
class ScenarioGeometry:
    waypoints: list[tuple[float, float, float]]
    corridor_width_m: float
    goal_radius_m: float
    reach_distance_m: float
    oob_margin_m: float

    @property
    def oob_threshold_m(self) -> float:
        return (self.corridor_width_m * 0.5) + self.oob_margin_m


@dataclass
class EpisodeResult:
    episode_index: int
    seed: int
    termination: str
    steps: int
    route_index: int
    remaining_waypoints: int
    distance_to_target_m: float
    max_route_distance_m: float
    max_speed_mps: float
    final_position: dict[str, float]
    trajectory: list[tuple[float, float]]


def parse_args() -> argparse.Namespace:
    parser = argparse.ArgumentParser(description="Evaluate baseline A->B ONNX policy on unity-sim.")
    parser.add_argument("--base-url", default="http://127.0.0.1:8000")
    parser.add_argument(
        "--scenario",
        default=str(ROOT / "configs/scenarios/ab-corridor-v1.yaml"),
    )
    parser.add_argument(
        "--model",
        default=str(ROOT / "python/training/artifacts/ab_corridor_policy_v1/ab_corridor_policy_v1.onnx"),
    )
    parser.add_argument("--episodes", type=int, default=20)
    parser.add_argument("--max-steps", type=int, default=220)
    parser.add_argument("--seed-offset", type=int, default=0)
    parser.add_argument("--target-agent-id", default="ego")
    parser.add_argument("--oob-margin-m", type=float, default=0.15)
    parser.add_argument("--include-trajectories", action="store_true")
    parser.add_argument("--output-json", default="")
    parser.add_argument("--output-svg", default="")
    return parser.parse_args()


def parse_geometry(payload: dict[str, Any], oob_margin_m: float) -> ScenarioGeometry:
    route = payload.get("route") or {}
    params = route.get("params") or {}

    waypoints: list[tuple[float, float, float]] = []
    for item in route.get("waypoints") or []:
        if isinstance(item, dict):
            waypoints.append((float(item["x"]), float(item.get("y", 0.0)), float(item["z"])))
        elif isinstance(item, (list, tuple)) and len(item) == 2:
            waypoints.append((float(item[0]), 0.0, float(item[1])))
        elif isinstance(item, (list, tuple)) and len(item) == 3:
            waypoints.append((float(item[0]), float(item[1]), float(item[2])))

    return ScenarioGeometry(
        waypoints=waypoints,
        corridor_width_m=float(params.get("corridor.width_m", 1.2)),
        goal_radius_m=float(params.get("goal.radius_m", route.get("reachDistanceM", 1.0))),
        reach_distance_m=float(route.get("reachDistanceM", 1.0)),
        oob_margin_m=float(oob_margin_m),
    )


def build_vision_observation(step: dict[str, Any], img_size: int = 84) -> dict[str, np.ndarray]:
    """Build multi-input observation for vision CNN-PPO model (image + ultrasonic)."""
    flat = telemetry_map(step)
    distance_m = parse_float(flat, "sensor.ultrasonic.front.m")
    ultrasonic = np.array([[clamp01(distance_m / 5.0)]], dtype=np.float32)

    frame = step.get("frame") or {}
    data_b64 = frame.get("dataBase64") or ""
    if data_b64:
        import base64
        import io
        try:
            from PIL import Image
        except ImportError as exc:
            raise RuntimeError("Pillow is required for vision eval: pip install Pillow") from exc
        raw = base64.b64decode(data_b64)
        img = Image.open(io.BytesIO(raw)).convert("RGB").resize((img_size, img_size), Image.BILINEAR)
        image = np.array(img, dtype=np.float32)[np.newaxis]  # (1, H, W, 3)
    else:
        image = np.zeros((1, img_size, img_size, 3), dtype=np.float32)

    return {"image": image, "ultrasonic": ultrasonic}


def build_observation(step: dict[str, Any]) -> np.ndarray:
    flat = telemetry_map(step)
    s1 = parse_float(flat, "sensor.line_tracker.s1_norm", "tracking.left")
    s2 = parse_float(flat, "sensor.line_tracker.s2_norm")
    s3 = parse_float(flat, "sensor.line_tracker.s3_norm", "tracking.center")
    s4 = parse_float(flat, "sensor.line_tracker.s4_norm")
    s5 = parse_float(flat, "sensor.line_tracker.s5_norm", "tracking.right")
    distance_m = parse_float(flat, "sensor.ultrasonic.front.m")
    speed_mps = parse_float(flat, "sensor.speedometer.mps")
    if speed_mps == 0.0:
        speed_mps = float((step.get("state") or {}).get("speed") or 0.0)
    heading_error = parse_float(flat, "nav.heading_error_rad")

    obs = np.array(
        [[
            clamp01(s1),
            clamp01(s2),
            clamp01(s3),
            clamp01(s4),
            clamp01(s5),
            clamp01(distance_m / 5.0),
            clamp01(speed_mps / 3.0),
            np.clip(heading_error / np.pi, -1.0, 1.0),
        ]],
        dtype=np.float32,
    )
    return obs


def telemetry_map(step: dict[str, Any]) -> dict[str, str]:
    state = step.get("state") or {}
    telemetry = state.get("telemetry") or []
    result: dict[str, str] = {}
    for item in telemetry:
        if not isinstance(item, dict):
            continue
        key = str(item.get("key") or "").strip()
        if not key:
            continue
        result[key] = str(item.get("value") or "")
    return result


def info_map(step: dict[str, Any]) -> dict[str, str]:
    info = step.get("info") or []
    result: dict[str, str] = {}
    for item in info:
        if not isinstance(item, dict):
            continue
        key = str(item.get("key") or "").strip()
        if not key:
            continue
        result[key] = str(item.get("value") or "")
    return result


def parse_float(mapping: dict[str, str], *keys: str) -> float:
    for key in keys:
        raw = mapping.get(key)
        if raw is None:
            continue
        try:
            return float(raw)
        except ValueError:
            continue
    return 0.0


def clamp01(value: float) -> float:
    return max(0.0, min(1.0, float(value)))


def nearest_route_distance_m(position: dict[str, Any], geometry: ScenarioGeometry) -> float:
    px = float(position["x"])
    pz = float(position["z"])
    return min(
        point_to_segment_distance(px, pz, first[0], first[2], second[0], second[2])
        for first, second in zip(geometry.waypoints[:-1], geometry.waypoints[1:])
    )


def point_to_segment_distance(px: float, pz: float, ax: float, az: float, bx: float, bz: float) -> float:
    abx = bx - ax
    abz = bz - az
    apx = px - ax
    apz = pz - az
    ab2 = (abx * abx) + (abz * abz)
    if ab2 <= 1e-9:
        return math.hypot(px - ax, pz - az)
    projection = (apx * abx) + (apz * abz)
    t = max(0.0, min(1.0, projection / ab2))
    cx = ax + (t * abx)
    cz = az + (t * abz)
    return math.hypot(px - cx, pz - cz)


def current_position(step: dict[str, Any]) -> dict[str, float]:
    position = (((step.get("state") or {}).get("pose") or {}).get("position") or {})
    return {
        "x": float(position.get("x", 0.0)),
        "y": float(position.get("y", 0.0)),
        "z": float(position.get("z", 0.0)),
    }


def evaluate_episode(
    client: SimClient,
    session: ort.InferenceSession,
    input_name: str,
    reset_payload: dict[str, Any],
    geometry: ScenarioGeometry,
    *,
    episode_index: int,
    seed: int,
    max_steps: int,
    target_agent_id: str,
    vision_mode: bool = False,
    img_size: int = 84,
) -> EpisodeResult:
    payload = dict(reset_payload)
    payload["seed"] = seed
    step = client.reset(payload)

    trajectory: list[tuple[float, float]] = []
    max_route_distance_m = 0.0
    max_speed_mps = 0.0
    termination = "timeout"

    route_index = 0
    remaining_waypoints = len(geometry.waypoints)
    distance_to_target_m = 0.0
    final_position = current_position(step)

    for step_index in range(1, max_steps + 1):
        if vision_mode:
            feed = build_vision_observation(step, img_size)
            action = session.run(None, feed)[0][0]
        else:
            observation = build_observation(step)
            action = session.run(None, {input_name: observation})[0][0]

        step = client.step(
            {
                "throttle": float(action[0]),
                "steer": float(action[1]),
                "brake": 0.0,
                "targetAgentId": target_agent_id,
                "timestamp": 0,
                "timeBase": "unix_ms",
                "extensions": [],
            }
        )

        info = info_map(step)
        final_position = current_position(step)
        trajectory.append((final_position["x"], final_position["z"]))

        route_index = int(info.get("route.current_index", "0") or 0)
        remaining_waypoints = int(info.get("route.remaining_waypoints", str(len(geometry.waypoints))) or 0)
        distance_to_target_m = float(info.get("route.distance_to_target_m", "0") or 0.0)
        max_route_distance_m = max(max_route_distance_m, nearest_route_distance_m(final_position, geometry))
        max_speed_mps = max(max_speed_mps, float((step.get("state") or {}).get("speed") or 0.0))

        if bool(step.get("done")) or info.get("route.completed") == "true":
            termination = "goal_reached"
            return EpisodeResult(
                episode_index=episode_index,
                seed=seed,
                termination=termination,
                steps=step_index,
                route_index=route_index,
                remaining_waypoints=remaining_waypoints,
                distance_to_target_m=distance_to_target_m,
                max_route_distance_m=max_route_distance_m,
                max_speed_mps=max_speed_mps,
                final_position=final_position,
                trajectory=trajectory,
            )

        if max_route_distance_m > geometry.oob_threshold_m:
            termination = "out_of_bounds"
            return EpisodeResult(
                episode_index=episode_index,
                seed=seed,
                termination=termination,
                steps=step_index,
                route_index=route_index,
                remaining_waypoints=remaining_waypoints,
                distance_to_target_m=distance_to_target_m,
                max_route_distance_m=max_route_distance_m,
                max_speed_mps=max_speed_mps,
                final_position=final_position,
                trajectory=trajectory,
            )

    return EpisodeResult(
        episode_index=episode_index,
        seed=seed,
        termination=termination,
        steps=max_steps,
        route_index=route_index,
        remaining_waypoints=remaining_waypoints,
        distance_to_target_m=distance_to_target_m,
        max_route_distance_m=max_route_distance_m,
        max_speed_mps=max_speed_mps,
        final_position=final_position,
        trajectory=trajectory,
    )


def build_summary(
    results: list[EpisodeResult],
    *,
    scenario_path: Path,
    model_path: Path,
    base_url: str,
    geometry: ScenarioGeometry,
    max_steps: int,
    include_trajectories: bool,
) -> dict[str, Any]:
    counts = Counter(item.termination for item in results)
    success_count = counts.get("goal_reached", 0)
    episodes = len(results)
    steps_avg = (sum(item.steps for item in results) / episodes) if episodes else 0.0

    return {
        "scenario": str(scenario_path),
        "model": str(model_path),
        "baseUrl": base_url,
        "episodes": episodes,
        "maxSteps": max_steps,
        "successCount": success_count,
        "successRate": round(success_count / episodes, 4) if episodes else 0.0,
        "terminationCounts": dict(counts),
        "corridor": {
            "widthM": geometry.corridor_width_m,
            "oobMarginM": geometry.oob_margin_m,
            "oobThresholdM": geometry.oob_threshold_m,
            "goalRadiusM": geometry.goal_radius_m,
            "reachDistanceM": geometry.reach_distance_m,
        },
        "limitations": [
            "goal_reached и timeout считаются строго по StepResult.done/maxSteps",
            "out_of_bounds вычисляется внешне по расстоянию до маршрута и corridor.width_m",
            "collision не экспонируется текущим runtime-контрактом и в эту сводку не входит",
        ],
        "averages": {"steps": round(steps_avg, 2)},
        "episodesData": [
            {
                "episodeIndex": item.episode_index,
                "seed": item.seed,
                "termination": item.termination,
                "steps": item.steps,
                "routeIndex": item.route_index,
                "remainingWaypoints": item.remaining_waypoints,
                "distanceToTargetM": round(item.distance_to_target_m, 4),
                "maxRouteDistanceM": round(item.max_route_distance_m, 4),
                "finalPosition": item.final_position,
                **(
                    {"trajectory": [[round(x, 4), round(z, 4)] for x, z in item.trajectory]}
                    if include_trajectories
                    else {}
                ),
            }
            for item in results
        ],
    }


def write_svg(path: Path, geometry: ScenarioGeometry, results: list[EpisodeResult]) -> None:
    all_points: list[tuple[float, float]] = [(wp[0], wp[2]) for wp in geometry.waypoints]
    for result in results:
        all_points.extend(result.trajectory)

    xs = [point[0] for point in all_points]
    zs = [point[1] for point in all_points]
    min_x = min(xs) - 1.0
    max_x = max(xs) + 1.0
    min_z = min(zs) - 1.0
    max_z = max(zs) + 1.0

    width = 960
    height = 720
    scale_x = width / max(1e-6, max_x - min_x)
    scale_z = height / max(1e-6, max_z - min_z)
    scale = min(scale_x, scale_z)
    padding = 36

    def project(point: tuple[float, float]) -> tuple[float, float]:
        x, z = point
        px = padding + ((x - min_x) * scale)
        pz = height - padding - ((z - min_z) * scale)
        return px, pz

    route_points = [project((wp[0], wp[2])) for wp in geometry.waypoints]
    corridor_px = geometry.corridor_width_m * scale

    lines: list[str] = [
        f'<svg xmlns="http://www.w3.org/2000/svg" width="{width + (padding * 2)}" height="{height + (padding * 2)}" viewBox="0 0 {width + (padding * 2)} {height + (padding * 2)}">',
        '<rect width="100%" height="100%" fill="#0b1220"/>',
        f'<polyline points="{" ".join(f"{x:.2f},{y:.2f}" for x, y in route_points)}" fill="none" stroke="#21354c" stroke-width="{corridor_px:.2f}" stroke-linecap="round" stroke-linejoin="round" opacity="0.45"/>',
        f'<polyline points="{" ".join(f"{x:.2f},{y:.2f}" for x, y in route_points)}" fill="none" stroke="#7dd3fc" stroke-width="4" stroke-linecap="round" stroke-linejoin="round"/>',
    ]

    for result in results:
        if not result.trajectory:
            continue
        color = {
            "goal_reached": "#22c55e",
            "out_of_bounds": "#ef4444",
            "timeout": "#f59e0b",
        }.get(result.termination, "#94a3b8")
        projected = [project(point) for point in result.trajectory]
        lines.append(
            f'<polyline points="{" ".join(f"{x:.2f},{y:.2f}" for x, y in projected)}" fill="none" stroke="{color}" stroke-width="2.5" opacity="0.75"/>'
        )

    start = project((geometry.waypoints[0][0], geometry.waypoints[0][2]))
    goal = project((geometry.waypoints[-1][0], geometry.waypoints[-1][2]))
    lines.extend(
        [
            f'<circle cx="{start[0]:.2f}" cy="{start[1]:.2f}" r="8" fill="#38bdf8"/>',
            f'<circle cx="{goal[0]:.2f}" cy="{goal[1]:.2f}" r="10" fill="#22c55e"/>',
            f'<text x="{start[0] + 12:.2f}" y="{start[1] - 12:.2f}" font-family="monospace" font-size="20" fill="#e2e8f0">A</text>',
            f'<text x="{goal[0] + 12:.2f}" y="{goal[1] - 12:.2f}" font-family="monospace" font-size="20" fill="#e2e8f0">B</text>',
            '<text x="28" y="36" font-family="monospace" font-size="24" fill="#f8fafc">A→B KPI trajectories</text>',
            f'<text x="28" y="64" font-family="monospace" font-size="16" fill="#94a3b8">corridor.width={geometry.corridor_width_m:.2f}m, oob.threshold={geometry.oob_threshold_m:.2f}m</text>',
        ]
    )

    lines.append("</svg>")
    path.write_text("\n".join(lines), encoding="utf-8")


def ensure_parent(path: str) -> Path:
    resolved = Path(path).expanduser().resolve()
    resolved.parent.mkdir(parents=True, exist_ok=True)
    return resolved


def main() -> int:
    args = parse_args()

    scenario_path = Path(args.scenario).expanduser().resolve()
    model_path = Path(args.model).expanduser().resolve()
    scenario_payload = load_scenario_file(scenario_path)
    geometry = parse_geometry(scenario_payload, oob_margin_m=args.oob_margin_m)
    reset_payload = scenario_to_reset_config(scenario_payload)

    client = SimClient(args.base_url, timeout_s=30.0)
    _ = client.health()

    session = ort.InferenceSession(model_path.as_posix())
    input_names = [inp.name for inp in session.get_inputs()]
    input_name = input_names[0]
    vision_mode = "image" in input_names and "ultrasonic" in input_names
    if vision_mode:
        print(f"  Vision model detected (inputs: {input_names})")

    base_seed = int(reset_payload.get("seed", 0) or 0) + int(args.seed_offset)
    results: list[EpisodeResult] = []
    for index in range(args.episodes):
        results.append(
            evaluate_episode(
                client,
                session,
                input_name,
                reset_payload,
                geometry,
                episode_index=index,
                seed=base_seed + index,
                max_steps=args.max_steps,
                target_agent_id=args.target_agent_id,
                vision_mode=vision_mode,
            )
        )

    summary = build_summary(
        results,
        scenario_path=scenario_path,
        model_path=model_path,
        base_url=args.base_url,
        geometry=geometry,
        max_steps=args.max_steps,
        include_trajectories=bool(args.include_trajectories),
    )

    if args.output_json:
        output_json = ensure_parent(args.output_json)
        output_json.write_text(json.dumps(summary, ensure_ascii=False, indent=2), encoding="utf-8")

    if args.output_svg:
        output_svg = ensure_parent(args.output_svg)
        write_svg(output_svg, geometry, results)

    print(json.dumps(summary, ensure_ascii=False, indent=2))
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
