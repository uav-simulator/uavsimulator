"""Unit tests for ``sim_client.ks0223`` — the KS0223 command and telemetry helpers."""

from __future__ import annotations

import pytest

from sim_client.ks0223 import Ks0223Command, parse_telemetry

# ---------------------------------------------------------------------------
# Ks0223Command.to_step_command
# ---------------------------------------------------------------------------


def test_to_step_command_emits_zero_throttle_and_steer() -> None:
    """KS0223 is driven by per-wheel PWM extensions, NOT by throttle/steer.
    The legacy fields must always be 0 so a backend that ignores extensions
    doesn't accidentally drive the car."""
    cmd = Ks0223Command(left_pwm_norm=0.5, right_pwm_norm=-0.3)
    step = cmd.to_step_command()
    assert step["throttle"] == 0.0
    assert step["steer"] == 0.0


def test_to_step_command_round_trips_pwm_into_extensions() -> None:
    cmd = Ks0223Command(left_pwm_norm=0.5, right_pwm_norm=-0.25)
    step = cmd.to_step_command()
    extensions = {item["key"]: item["value"] for item in step["extensions"]}
    assert extensions["drive.left_pwm_norm"] == "0.500000"
    assert extensions["drive.right_pwm_norm"] == "-0.250000"


def test_to_step_command_clips_pwm_to_unit_interval() -> None:
    cmd = Ks0223Command(left_pwm_norm=2.5, right_pwm_norm=-7.0)
    step = cmd.to_step_command()
    extensions = {item["key"]: item["value"] for item in step["extensions"]}
    assert extensions["drive.left_pwm_norm"] == "1.000000"
    assert extensions["drive.right_pwm_norm"] == "-1.000000"


def test_to_step_command_clips_brake_to_zero_one() -> None:
    over = Ks0223Command(left_pwm_norm=0, right_pwm_norm=0, brake=2.0).to_step_command()
    under = Ks0223Command(left_pwm_norm=0, right_pwm_norm=0, brake=-0.5).to_step_command()
    assert over["brake"] == 1.0
    assert under["brake"] == 0.0


def test_to_step_command_carries_timestamp_and_time_base() -> None:
    cmd = Ks0223Command(
        left_pwm_norm=0.0, right_pwm_norm=0.0, timestamp=1_700_000_000_000, time_base="unix_ms"
    )
    step = cmd.to_step_command()
    assert step["timestamp"] == 1_700_000_000_000
    assert step["timeBase"] == "unix_ms"


def test_ks0223_command_is_frozen() -> None:
    """Frozen dataclass — instances must not be mutated mid-flight, every
    new command is a new object."""
    cmd = Ks0223Command(left_pwm_norm=0.0, right_pwm_norm=0.0)
    with pytest.raises((AttributeError, Exception)):
        cmd.left_pwm_norm = 0.5  # type: ignore[misc]


# ---------------------------------------------------------------------------
# parse_telemetry
# ---------------------------------------------------------------------------


def test_parse_telemetry_extracts_string_kv_pairs() -> None:
    step = {
        "state": {
            "telemetry": [
                {"key": "ultrasonic.distance_cm", "value": "42"},
                {"key": "ir.last_code_hex", "value": "0xFF00"},
            ]
        }
    }
    assert parse_telemetry(step) == {
        "ultrasonic.distance_cm": "42",
        "ir.last_code_hex": "0xFF00",
    }


def test_parse_telemetry_skips_non_string_values() -> None:
    """Backend conventionally encodes telemetry values as strings; numeric
    or null values are dropped rather than silently coerced — this gives
    callers a clean type contract on the returned dict."""
    step = {
        "state": {
            "telemetry": [
                {"key": "good", "value": "ok"},
                {"key": "numeric", "value": 42},
                {"key": "null", "value": None},
                {"key": 123, "value": "bad-key"},
            ]
        }
    }
    assert parse_telemetry(step) == {"good": "ok"}


def test_parse_telemetry_returns_empty_dict_when_state_missing() -> None:
    assert parse_telemetry({}) == {}
    assert parse_telemetry({"state": None}) == {}
    assert parse_telemetry({"state": {"telemetry": None}}) == {}


def test_parse_telemetry_skips_non_mapping_items() -> None:
    step = {"state": {"telemetry": ["not-a-mapping", 42, None, {"key": "k", "value": "v"}]}}
    assert parse_telemetry(step) == {"k": "v"}


def test_parse_telemetry_handles_non_mapping_input() -> None:
    """Defensive: if the step result is the wrong shape entirely, return
    an empty dict instead of raising."""
    assert parse_telemetry("not a dict") == {}  # type: ignore[arg-type]
