"""Integration-style check that 3 Unity runtimes respond healthily.

Gated behind env var RUSIM_MULTI_RUNTIME_TEST=1 so normal CI skips it (no Unity).
Assumes operator has already run `./rusim server up --count 3`.
"""
from __future__ import annotations

import os
import sys
from pathlib import Path

import pytest
import requests

ROOT = Path(__file__).resolve().parents[3]
sys.path.insert(0, str(ROOT / "python"))


@pytest.mark.skipif(
    os.environ.get("RUSIM_MULTI_RUNTIME_TEST") != "1",
    reason="requires 3 live Unity runtimes on :8000-:8002",
)
def test_three_runtimes_healthy():
    for port in (8000, 8001, 8002):
        r = requests.get(f"http://127.0.0.1:{port}/health", timeout=5)
        assert r.ok, f"port {port} returned {r.status_code}"
        body = r.json()
        assert body.get("status") == "ok", f"port {port} unhealthy: {body}"


@pytest.mark.skipif(
    os.environ.get("RUSIM_MULTI_RUNTIME_TEST") != "1",
    reason="requires 3 live Unity runtimes on :8000-:8002",
)
def test_three_runtimes_accept_reset():
    from sim_client.http_client import SimClient
    for port in (8000, 8001, 8002):
        client = SimClient(f"http://127.0.0.1:{port}", timeout_s=30.0)
        step = client.reset({
            "seed": 42,
            "timeScale": 1.0,
            "selectedTrackId": "track.cardboard_maze.v1",
            "selectedVehicleId": "vehicle.ks0223.v1",
            "trackParams": [
                {"key": "maze.seed", "value": "42"},
                {"key": "maze.length_cells", "value": "5"},
                {"key": "maze.left_turns", "value": "0"},
                {"key": "maze.right_turns", "value": "1"},
                {"key": "maze.corridor_width_m", "value": "0.60"},
                {"key": "maze.wall_height_m", "value": "0.25"},
            ],
            "vehicleParams": [{"key": "camera.profile", "value": "high"}],
            "flags": [],
        })
        # NOTE: plan's spec said `step.get("telemetry")` but the actual /reset
        # response has no top-level `telemetry` key — telemetry items live at
        # step["state"]["telemetry"]. Assert on `agents` (populated on success)
        # to verify the runtime accepted the reset on each port.
        assert step.get("agents"), f"port {port} reset returned no agents: {step}"
