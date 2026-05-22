"""Automatically label camera frames with ground-truth traffic-light state.

Approach:
  1. Connect to running rusim runtime via SimClient.
  2. Step the sim under a city scenario; vehicle is controlled by an internal
     WaypointFollowerVehicle controller (autonomous).
  3. On each step, read VehicleState.extensions.nearestTrafficLight.
  4. Save (frame_jpeg, label_int, distance_m) into JSONL + frames/.

Usage:
  python -m training.traffic.auto_label \
    --scenario configs/scenarios/showcase-city.yaml \
    --duration-min 90 \
    --output docs/report/master-thesis/sprint-4-bc-variance-2026-06/traffic-light/dataset/
"""
from __future__ import annotations

import argparse
import base64
import io
import json
import time
from dataclasses import dataclass
from pathlib import Path
from typing import Any, Mapping

GROUND_TRUTH_LABELS = ["Red", "Yellow", "Green"]


@dataclass(frozen=True)
class TlSample:
    frame_bytes: bytes
    label: int
    distance_m: float


# NOTE: `state.extensions` is populated by CityVehicleTelemetryExtender
# (Plan B Task 1) — wiring into the runtime telemetry pipeline is the
# remaining piece. Until that lands, this function will return None
# for all states (consistent with the "no light visible" case).
def parse_state_sample(state: dict) -> TlSample | None:
    """Extract (frame_bytes_empty, label, distance) from a VehicleState dict.

    Returns None if no traffic light is in view. Frame bytes are filled by the
    caller using the companion frame stream — this function just produces the label.
    """
    extensions = state.get("extensions") or {}
    nlt = extensions.get("nearestTrafficLight")
    if not nlt or not nlt.get("hasLight"):
        return None
    label_str = nlt.get("state", "None")
    if label_str not in GROUND_TRUTH_LABELS:
        return None
    return TlSample(
        frame_bytes=b"",
        label=GROUND_TRUTH_LABELS.index(label_str),
        distance_m=float(nlt.get("distanceM", 0.0)),
    )


def _extract_state(step_result: Mapping[str, Any]) -> Mapping[str, Any]:
    state = step_result.get("state")
    if isinstance(state, Mapping):
        return state
    return step_result


def _extract_frame_b64(step_result: Mapping[str, Any]) -> str | None:
    state = _extract_state(step_result)
    camera = state.get("camera") if isinstance(state, Mapping) else None
    frame = camera.get("frame") if isinstance(camera, Mapping) else None
    data = frame.get("dataBase64") if isinstance(frame, Mapping) else None
    if isinstance(data, str) and data:
        return data
    # Fallback: some response shapes put frame at the top level.
    top_frame = step_result.get("frame") if isinstance(step_result, Mapping) else None
    if isinstance(top_frame, Mapping):
        top_data = top_frame.get("dataBase64")
        if isinstance(top_data, str) and top_data:
            return top_data
    return None


def main():
    p = argparse.ArgumentParser()
    p.add_argument("--scenario", required=True)
    p.add_argument("--duration-min", type=float, default=60.0)
    p.add_argument("--output", type=Path, required=True)
    p.add_argument("--base-url", default="http://127.0.0.1:8000")
    p.add_argument("--agent-id", default="ai-car")
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
            response = client.step({"throttle": 0.0, "steer": 0.0, "brake": 0.0})
            state = _extract_state(response)
            sample = parse_state_sample(dict(state))
            if sample is None:
                continue
            frame_b64 = _extract_frame_b64(response)
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
