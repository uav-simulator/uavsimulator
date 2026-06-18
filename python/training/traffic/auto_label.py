"""Automatically label camera frames with ground-truth traffic-light state.

Approach:
  1. Connect to running rusim runtime via SimClient.
  2. Step the sim under a city scenario; vehicle is controlled by an internal
     WaypointFollowerVehicle controller (autonomous).
  3. On each step, read VehicleState.extensions.nearestTrafficLight.
  4. Save (modelFrame_jpeg, label_int, distance_m) into JSONL + frames/.

Usage:
  python -m training.traffic.auto_label \
    --scenario configs/scenarios/showcase-city.yaml \
    --duration-min 90 \
    --output python/training/artifacts/traffic-light/dataset/
"""
from __future__ import annotations

import argparse
import base64
import io
import json
import time
from collections.abc import Mapping
from dataclasses import dataclass
from pathlib import Path
from typing import Any

GROUND_TRUTH_LABELS = ["Red", "Yellow", "Green"]


@dataclass(frozen=True)
class TlSample:
    frame_bytes: bytes
    label: int
    distance_m: float


# The traffic-light ground-truth is published by CityVehicleTelemetryExtender
# through the IVehicleStateExtender plug-in interface, aggregated by
# VehicleBase.ReadState into VehicleState.telemetry. The real wire format is
# ConfigKeyValue[] (a flat list of {key, value} string pairs).
# Legacy test fixtures use a nested-dict form under either `telemetry` or
# `extensions`; this parser accepts all three shapes.
def parse_state_sample(state: dict) -> TlSample | None:
    """Extract (frame_bytes_empty, label, distance) from a VehicleState dict.

    Returns None if no traffic light is in view. Frame bytes are filled by the
    caller using the companion frame stream — this function just produces the label.
    """
    # Real wire field is `telemetry`; older fixtures may use `extensions`.
    payload = state.get("telemetry")
    if payload is None:
        payload = state.get("extensions")
    if payload is None:
        return None

    if isinstance(payload, dict):
        nlt = payload.get("nearestTrafficLight")
        if not nlt or not nlt.get("hasLight"):
            return None
        label_str = nlt.get("state", "None")
        try:
            distance = float(nlt.get("distanceM", 0.0))
        except (TypeError, ValueError):
            distance = 0.0
    elif isinstance(payload, list):
        flat: dict[str, str] = {}
        for kv in payload:
            if not isinstance(kv, dict):
                continue
            k = kv.get("key")
            v = kv.get("value")
            if k is None:
                continue
            flat[k] = v
        has = (flat.get("nearestTrafficLight.hasLight", "false") or "").lower() == "true"
        if not has:
            return None
        label_str = flat.get("nearestTrafficLight.state", "None")
        try:
            distance = float(flat.get("nearestTrafficLight.distanceM", "0"))
        except (TypeError, ValueError):
            distance = 0.0
    else:
        return None

    if label_str not in GROUND_TRUTH_LABELS:
        return None
    return TlSample(
        frame_bytes=b"",
        label=GROUND_TRUTH_LABELS.index(label_str),
        distance_m=distance,
    )


def _extract_state(step_result: Mapping[str, Any]) -> Mapping[str, Any]:
    state = step_result.get("state")
    if isinstance(state, Mapping):
        return state
    return step_result


def _extract_frame_b64(
    step_result: Mapping[str, Any],
    *,
    preferred_frame: str = "modelFrame",
) -> str | None:
    for frame_name in _frame_lookup_order(preferred_frame):
        data = _extract_named_frame_b64(step_result, frame_name)
        if data is not None:
            return data
    return None


def _frame_lookup_order(preferred_frame: str) -> tuple[str, ...]:
    preferred = preferred_frame.strip() or "modelFrame"
    if preferred == "modelFrame":
        return ("modelFrame", "frame")
    if preferred == "frame":
        return ("frame", "modelFrame")
    return (preferred, "modelFrame", "frame")


def _extract_named_frame_b64(step_result: Mapping[str, Any], frame_name: str) -> str | None:
    top_frame = step_result.get(frame_name)
    if isinstance(top_frame, Mapping):
        top_data = top_frame.get("dataBase64")
        if isinstance(top_data, str) and top_data:
            return top_data

    state = _extract_state(step_result)
    state_frame = state.get(frame_name) if isinstance(state, Mapping) else None
    data = state_frame.get("dataBase64") if isinstance(state_frame, Mapping) else None
    if isinstance(data, str) and data:
        return data

    if frame_name == "frame":
        camera = state.get("camera") if isinstance(state, Mapping) else None
        frame = camera.get("frame") if isinstance(camera, Mapping) else None
        data = frame.get("dataBase64") if isinstance(frame, Mapping) else None
        if isinstance(data, str) and data:
            return data
    return None


def _build_step_command(agent_id: str, model_capture_mode: str) -> dict[str, Any]:
    command: dict[str, Any] = {
        "throttle": 0.0,
        "steer": 0.0,
        "brake": 0.0,
        "extensions": [
            {"key": "camera.model_capture_mode", "value": model_capture_mode},
        ],
    }
    if agent_id.strip():
        command["targetAgentId"] = agent_id.strip()
    return command


def main():
    p = argparse.ArgumentParser()
    p.add_argument("--scenario", required=True)
    p.add_argument("--duration-min", type=float, default=60.0)
    p.add_argument("--output", type=Path, required=True)
    p.add_argument("--base-url", default="http://127.0.0.1:8000")
    p.add_argument("--agent-id", default="ego")
    p.add_argument("--frame-source", choices=("modelFrame", "frame"), default="modelFrame")
    p.add_argument("--model-capture-mode", default="driver")
    args = p.parse_args()

    # Late import — keeps unit tests free of network deps.
    from PIL import Image  # noqa: WPS433 (intentional late import)

    from sim_client.http_client import SimClient
    from sim_client.scenario import load_scenario_file, scenario_to_reset_config

    args.output.mkdir(parents=True, exist_ok=True)
    frames_dir = args.output / "frames"
    frames_dir.mkdir(exist_ok=True)
    jsonl_path = args.output / "dataset.jsonl"

    scenario_payload = load_scenario_file(args.scenario)
    reset_config = scenario_to_reset_config(scenario_payload)

    client = SimClient(args.base_url, timeout_s=30.0)
    client.reset(reset_config)

    deadline = time.monotonic() + args.duration_min * 60
    tick = 0
    counts = [0, 0, 0]
    with jsonl_path.open("w") as out:
        while time.monotonic() < deadline:
            tick += 1
            response = client.step(_build_step_command(args.agent_id, args.model_capture_mode))
            state = _extract_state(response)
            sample = parse_state_sample(dict(state))
            if sample is None:
                continue
            frame_b64 = _extract_frame_b64(response, preferred_frame=args.frame_source)
            if not frame_b64:
                continue
            frame_bytes = base64.b64decode(frame_b64)
            img = Image.open(io.BytesIO(frame_bytes)).convert("RGB").resize((84, 84))
            frame_name = f"frame_{tick:08d}.jpg"
            img.save(frames_dir / frame_name, "JPEG", quality=85)
            row = {
                "tick": tick,
                "frame": f"frames/{frame_name}",
                "label_idx": sample.label,
                "label_name": GROUND_TRUTH_LABELS[sample.label],
                "distance_m": sample.distance_m,
                "frame_source": args.frame_source,
                "model_capture_mode": args.model_capture_mode,
            }
            out.write(json.dumps(row) + "\n")
            counts[sample.label] += 1
            if tick % 100 == 0:
                print(
                    f"[auto_label] tick={tick} counts={dict(zip(GROUND_TRUTH_LABELS, counts))}",
                    flush=True,
                )

    print(
        f"Done. Total samples: {sum(counts)}; "
        f"distribution: {dict(zip(GROUND_TRUTH_LABELS, counts))}"
    )


if __name__ == "__main__":
    main()
