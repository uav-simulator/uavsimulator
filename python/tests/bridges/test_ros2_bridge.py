"""Tests for the pure helper layer of `bridges/ros2_bridge.py`.

Mirrors the existing test_ros2_bridge_multi.py pattern (rclpy import in
the source file is wrapped in try/except, so importing the module on a
host without ROS2 succeeds and `rclpy` ends up `None`).

These tests focus on the small pure utilities that the planned
single-↔ multi-bridge dedup will extract into a shared `bridges/_common.py`
module. Locking them now means the dedup refactor can prove byte-for-byte
parity instead of trusting that "it looks the same".
"""

from __future__ import annotations

import base64
import sys
from pathlib import Path

import pytest

PYTHON_ROOT = Path(__file__).resolve().parents[2]
if str(PYTHON_ROOT) not in sys.path:
    sys.path.insert(0, str(PYTHON_ROOT))

from bridges import ros2_bridge as bridge  # noqa: E402

# ---------------------------------------------------------------------------
# _safe_ns
# ---------------------------------------------------------------------------


@pytest.mark.parametrize(
    ("raw", "expected"),
    [
        ("/uavsim/ks0223", "/uavsim/ks0223"),
        ("uavsim/ks0223", "/uavsim/ks0223"),
        ("/uavsim/ks0223/", "/uavsim/ks0223"),
        ("  /uavsim/ks0223  ", "/uavsim/ks0223"),
        ("", "/uavsim/ks0223"),
        (None, "/uavsim/ks0223"),
    ],
)
def test_safe_ns_normalises_namespace(raw: str | None, expected: str) -> None:
    assert bridge._safe_ns(raw) == expected  # type: ignore[arg-type]


# ---------------------------------------------------------------------------
# _clamp
# ---------------------------------------------------------------------------


@pytest.mark.parametrize(
    ("value", "lo", "hi", "expected"),
    [
        (0.5, 0.0, 1.0, 0.5),
        (-1.0, 0.0, 1.0, 0.0),
        (2.0, 0.0, 1.0, 1.0),
        (0.0, -1.0, 1.0, 0.0),
        (-2.0, -1.0, 1.0, -1.0),
    ],
)
def test_clamp(value: float, lo: float, hi: float, expected: float) -> None:
    assert bridge._clamp(value, lo, hi) == expected


# ---------------------------------------------------------------------------
# _as_float
# ---------------------------------------------------------------------------


@pytest.mark.parametrize(
    ("raw", "default", "expected"),
    [
        ("1.5", 0.0, 1.5),
        (1.5, 0.0, 1.5),
        (1, 0.0, 1.0),
        ("not a number", 7.0, 7.0),
        (None, 3.14, 3.14),
        ("", 0.0, 0.0),  # float("") raises ValueError
    ],
)
def test_as_float_falls_back_to_default(raw: object, default: float, expected: float) -> None:
    assert bridge._as_float(raw, default) == expected


# ---------------------------------------------------------------------------
# _telemetry_float
# ---------------------------------------------------------------------------


def test_telemetry_float_returns_value_when_parseable() -> None:
    telemetry = {"ultrasonic.distance_cm": "42.5"}
    assert bridge._telemetry_float(telemetry, "ultrasonic.distance_cm") == 42.5


def test_telemetry_float_returns_default_when_missing() -> None:
    assert bridge._telemetry_float({}, "missing", default=99.0) == 99.0


def test_telemetry_float_returns_default_when_unparseable() -> None:
    telemetry = {"key": "not a float"}
    assert bridge._telemetry_float(telemetry, "key", default=7.0) == 7.0


# ---------------------------------------------------------------------------
# _decode_frame_bytes
# ---------------------------------------------------------------------------


def test_decode_frame_bytes_round_trips_base64() -> None:
    raw = b"\x89PNG\r\n\x1a\n"  # PNG magic header
    encoded = base64.b64encode(raw).decode("ascii")
    assert bridge._decode_frame_bytes({"dataBase64": encoded}) == raw


def test_decode_frame_bytes_returns_empty_when_missing() -> None:
    assert bridge._decode_frame_bytes({}) == b""
    assert bridge._decode_frame_bytes({"dataBase64": ""}) == b""
    assert bridge._decode_frame_bytes({"dataBase64": None}) == b""  # type: ignore[dict-item]


def test_decode_frame_bytes_returns_empty_on_invalid_base64() -> None:
    assert bridge._decode_frame_bytes({"dataBase64": "$$$ not base64 $$$"}) == b""


# ---------------------------------------------------------------------------
# _sanitize_frame_id
# ---------------------------------------------------------------------------


@pytest.mark.parametrize(
    ("raw", "expected"),
    [
        ("camera_front_optical", "camera_front_optical"),
        ("camera/front", "camera_front"),
        ("camera-front-optical", "camera_front_optical"),
        ("Front Optical 0", "Front_Optical_0"),
        ("", "camera_front_optical"),  # falls back to default
        (None, "camera_front_optical"),
        ("***", "___"),  # all special → underscores, but non-empty
    ],
)
def test_sanitize_frame_id(raw: str | None, expected: str) -> None:
    assert bridge._sanitize_frame_id(raw) == expected  # type: ignore[arg-type]


# ---------------------------------------------------------------------------
# parse_args
# ---------------------------------------------------------------------------


def test_parse_args_defaults(monkeypatch: pytest.MonkeyPatch) -> None:
    """Default invocation reads from env or hard-coded fallbacks."""
    monkeypatch.delenv("UAVSIM_BASE_URL", raising=False)
    monkeypatch.delenv("UAVSIM_ROS_NAMESPACE", raising=False)
    monkeypatch.delenv("UAVSIM_ROS_RATE_HZ", raising=False)
    monkeypatch.delenv("UAVSIM_VEHICLE_ID", raising=False)
    monkeypatch.delenv("UAVSIM_TRACK_ID", raising=False)
    monkeypatch.setattr(sys, "argv", ["ros2_bridge.py"])

    args = bridge.parse_args()
    assert args.base_url == "http://127.0.0.1:8000"
    assert args.namespace == "/uavsim/ks0223"
    assert args.rate_hz == pytest.approx(15.0)
    assert args.reset_on_start is False
    assert args.mock_ros2 is False
    assert args.mock_steps == 25


def test_parse_args_env_overrides(monkeypatch: pytest.MonkeyPatch) -> None:
    monkeypatch.setenv("UAVSIM_BASE_URL", "http://10.0.0.5:9001")
    monkeypatch.setenv("UAVSIM_ROS_NAMESPACE", "/team_blue/agent_1")
    monkeypatch.setenv("UAVSIM_ROS_RATE_HZ", "30")
    monkeypatch.setattr(sys, "argv", ["ros2_bridge.py"])

    args = bridge.parse_args()
    assert args.base_url == "http://10.0.0.5:9001"
    assert args.namespace == "/team_blue/agent_1"
    assert args.rate_hz == pytest.approx(30.0)


def test_parse_args_explicit_flags_win_over_env(monkeypatch: pytest.MonkeyPatch) -> None:
    monkeypatch.setenv("UAVSIM_BASE_URL", "http://from-env:8000")
    monkeypatch.setattr(
        sys,
        "argv",
        [
            "ros2_bridge.py",
            "--base-url",
            "http://from-flag:9999",
            "--rate-hz",
            "5.5",
            "--reset-on-start",
        ],
    )

    args = bridge.parse_args()
    assert args.base_url == "http://from-flag:9999"
    assert args.rate_hz == pytest.approx(5.5)
    assert args.reset_on_start is True
