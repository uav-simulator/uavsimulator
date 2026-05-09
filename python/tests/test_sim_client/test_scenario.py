"""Unit tests for ``sim_client.scenario`` (loader / validator / reset-config builder).

The shipped YAML scenarios under ``configs/scenarios/`` exercise this module
in production; these tests pin the public contract and cover edge cases that
real scenario files do not — invalid types, missing fields, multi-agent
shapes, waypoint encoding variants.
"""

from __future__ import annotations

import json
from pathlib import Path

import pytest

from sim_client.scenario import (
    load_scenario_file,
    scenario_to_reset_config,
    validate_scenario,
)

# ---------------------------------------------------------------------------
# load_scenario_file
# ---------------------------------------------------------------------------


def test_load_json_scenario(tmp_path: Path) -> None:
    payload = {"scenarioId": "x", "runtime": {"runtimeMode": "unity-sim", "headless": False}}
    p = tmp_path / "scn.json"
    p.write_text(json.dumps(payload), encoding="utf-8")
    assert load_scenario_file(p) == payload


def test_load_yaml_scenario(tmp_path: Path) -> None:
    pytest.importorskip("yaml")
    p = tmp_path / "scn.yaml"
    p.write_text("scenarioId: x\nruntime:\n  runtimeMode: unity-sim\n  headless: false\n", encoding="utf-8")
    payload = load_scenario_file(p)
    assert payload["scenarioId"] == "x"
    assert payload["runtime"]["runtimeMode"] == "unity-sim"
    assert payload["runtime"]["headless"] is False


def test_load_yaml_alias_yml_extension(tmp_path: Path) -> None:
    pytest.importorskip("yaml")
    p = tmp_path / "scn.yml"
    p.write_text("scenarioId: x\n", encoding="utf-8")
    assert load_scenario_file(p) == {"scenarioId": "x"}


def test_load_missing_file_raises(tmp_path: Path) -> None:
    with pytest.raises(FileNotFoundError, match="Scenario file not found"):
        load_scenario_file(tmp_path / "nope.json")


def test_load_unsupported_extension_raises(tmp_path: Path) -> None:
    p = tmp_path / "scn.txt"
    p.write_text("hello", encoding="utf-8")
    with pytest.raises(ValueError, match="Unsupported scenario file extension"):
        load_scenario_file(p)


def test_load_non_object_root_raises(tmp_path: Path) -> None:
    p = tmp_path / "scn.json"
    p.write_text(json.dumps([1, 2, 3]), encoding="utf-8")
    with pytest.raises(ValueError, match="must be a JSON/YAML object"):
        load_scenario_file(p)


def test_load_accepts_string_path(tmp_path: Path) -> None:
    p = tmp_path / "scn.json"
    p.write_text(json.dumps({"scenarioId": "x"}), encoding="utf-8")
    assert load_scenario_file(str(p)) == {"scenarioId": "x"}


# ---------------------------------------------------------------------------
# validate_scenario
# ---------------------------------------------------------------------------


_MINIMAL_VALID = {
    "scenarioId": "x",
    "runtime": {"runtimeMode": "unity-sim", "headless": False},
    "world": {"trackId": "track.basic_arena.v1"},
    "vehicle": {"vehicleId": "vehicle.ks0223.v1"},
}


def test_validate_minimal_payload() -> None:
    ok, errors = validate_scenario(_MINIMAL_VALID)
    assert ok, errors
    assert errors == []


def test_validate_rejects_non_mapping() -> None:
    ok, errors = validate_scenario("not a dict")  # type: ignore[arg-type]
    assert ok is False
    assert "scenario must be an object" in errors


def test_validate_rejects_missing_runtime_mode() -> None:
    payload = {**_MINIMAL_VALID, "runtime": {"headless": False}}
    ok, errors = validate_scenario(payload)
    assert ok is False
    assert any("runtimeMode" in e for e in errors)


def test_validate_rejects_non_bool_headless() -> None:
    payload = {**_MINIMAL_VALID, "runtime": {"runtimeMode": "unity-sim", "headless": "yes"}}
    ok, errors = validate_scenario(payload)
    assert ok is False
    assert any("headless" in e for e in errors)


def test_validate_rejects_missing_track_id() -> None:
    payload = {**_MINIMAL_VALID, "world": {}}
    ok, errors = validate_scenario(payload)
    assert ok is False
    assert any("trackId" in e for e in errors)


def test_validate_accepts_agents_vehicles_in_lieu_of_top_level_vehicle() -> None:
    payload = {
        "scenarioId": "x",
        "runtime": {"runtimeMode": "unity-sim", "headless": False},
        "world": {"trackId": "track.basic_arena.v1"},
        "agents": {"vehicles": [{"agentId": "ego", "vehicleId": "vehicle.ks0223.v1"}]},
    }
    ok, errors = validate_scenario(payload)
    assert ok, errors


def test_validate_rejects_blank_agent_id() -> None:
    payload = {
        **_MINIMAL_VALID,
        "agents": {"vehicles": [{"agentId": "  ", "vehicleId": "v"}]},
    }
    ok, errors = validate_scenario(payload)
    assert ok is False
    assert any("agentId" in e for e in errors)


def test_validate_rejects_non_int_agents_count() -> None:
    payload = {**_MINIMAL_VALID, "agents": {"count": "4"}}
    ok, errors = validate_scenario(payload)
    assert ok is False
    assert any("count must be an integer" in e for e in errors)


def test_validate_rejects_non_list_waypoints() -> None:
    payload = {**_MINIMAL_VALID, "route": {"waypoints": "0,0; 1,1"}}
    ok, errors = validate_scenario(payload)
    assert ok is False
    assert any("waypoints" in e for e in errors)


# ---------------------------------------------------------------------------
# scenario_to_reset_config
# ---------------------------------------------------------------------------


def test_reset_config_carries_seed_time_scale_track_vehicle() -> None:
    payload = {
        **_MINIMAL_VALID,
        "runtime": {"runtimeMode": "unity-sim", "headless": True, "seed": 73, "timeScale": 1.5},
    }
    cfg = scenario_to_reset_config(payload)
    assert cfg["seed"] == 73
    assert cfg["timeScale"] == 1.5
    assert cfg["selectedTrackId"] == "track.basic_arena.v1"
    assert cfg["selectedVehicleId"] == "vehicle.ks0223.v1"


def test_reset_config_seed_defaults_to_zero_when_missing() -> None:
    cfg = scenario_to_reset_config(_MINIMAL_VALID)
    assert cfg["seed"] == 0
    assert cfg["timeScale"] == 1.0


def test_reset_config_emits_runtime_headless_flag() -> None:
    payload = {**_MINIMAL_VALID, "runtime": {"runtimeMode": "unity-sim", "headless": True}}
    cfg = scenario_to_reset_config(payload)
    assert {"key": "runtime.headless", "value": "true"} in cfg["flags"]


def test_reset_config_encodes_2d_waypoints_with_zero_y() -> None:
    payload = {
        **_MINIMAL_VALID,
        "route": {
            "waypoints": [[0.0, -7.5], [0.0, -1.0], [6.0, 5.0]],
            "reachDistanceM": 1.5,
        },
    }
    cfg = scenario_to_reset_config(payload)
    waypoints_kv = next(p for p in cfg["trackParams"] if p["key"] == "route.waypoints")
    assert waypoints_kv["value"] == "0.000,0.000,-7.500;0.000,0.000,-1.000;6.000,0.000,5.000"
    assert {"key": "route.reach_distance_m", "value": "1.5"} in cfg["trackParams"]
    assert {"key": "route.loop", "value": "false"} in cfg["trackParams"]


def test_reset_config_encodes_3d_and_dict_waypoints() -> None:
    payload = {
        **_MINIMAL_VALID,
        "route": {"waypoints": [[1.0, 2.0, 3.0], {"x": 4.0, "y": 5.0, "z": 6.0}]},
    }
    cfg = scenario_to_reset_config(payload)
    waypoints_kv = next(p for p in cfg["trackParams"] if p["key"] == "route.waypoints")
    assert waypoints_kv["value"] == "1.000,2.000,3.000;4.000,5.000,6.000"


def test_reset_config_carries_camera_profile_into_vehicle_params() -> None:
    payload = {
        **_MINIMAL_VALID,
        "sensors": {"camera": {"profile": "high"}},
    }
    cfg = scenario_to_reset_config(payload)
    assert {"key": "camera.profile", "value": "high"} in cfg["vehicleParams"]


def test_reset_config_emits_agent_flags() -> None:
    payload = {
        **_MINIMAL_VALID,
        "agents": {"isolated": True, "seeEachOther": False, "collisionsEnabled": False},
    }
    cfg = scenario_to_reset_config(payload)
    assert {"key": "agents.isolated", "value": "true"} in cfg["flags"]
    assert {"key": "agents.see_each_other", "value": "false"} in cfg["flags"]
    assert {"key": "agents.collisions_enabled", "value": "false"} in cfg["flags"]


def test_reset_config_explicit_agents_become_payload_entries() -> None:
    payload = {
        **_MINIMAL_VALID,
        "agents": {
            "vehicles": [
                {"agentId": "ego", "vehicleId": "vehicle.ks0223.v1", "primary": True},
                {"agentId": "npc-01", "vehicleId": "vehicle.arcade.green.v1"},
            ],
        },
    }
    cfg = scenario_to_reset_config(payload)
    assert len(cfg["agents"]) == 2
    assert cfg["agents"][0]["agentId"] == "ego"
    assert cfg["agents"][0]["isPrimary"] is True
    assert cfg["agents"][1]["agentId"] == "npc-01"
    assert cfg["agents"][1]["isPrimary"] is False
    # selected vehicle follows the primary agent
    assert cfg["selectedVehicleId"] == "vehicle.ks0223.v1"


def test_reset_config_count_only_agents_get_default_ids() -> None:
    payload = {**_MINIMAL_VALID, "agents": {"count": 3}}
    cfg = scenario_to_reset_config(payload)
    ids = [a["agentId"] for a in cfg["agents"]]
    assert ids == ["ego", "agent-2", "agent-3"]
    assert cfg["agents"][0]["isPrimary"] is True
    assert cfg["agents"][1]["isPrimary"] is False


def test_reset_config_count_one_emits_no_extra_agents() -> None:
    """count<=1 → single-agent legacy path; agents array stays empty."""
    payload = {**_MINIMAL_VALID, "agents": {"count": 1}}
    cfg = scenario_to_reset_config(payload)
    assert cfg["agents"] == []


def test_reset_config_agent_spawn_pose_position_dict_and_yaw() -> None:
    payload = {
        **_MINIMAL_VALID,
        "agents": {
            "vehicles": [
                {
                    "agentId": "ego",
                    "vehicleId": "v1",
                    "primary": True,
                    "spawnPose": {"position": {"x": 1.0, "y": 0.2, "z": 2.0}, "yawDeg": 90.0},
                }
            ],
        },
    }
    cfg = scenario_to_reset_config(payload)
    track_params = cfg["agents"][0]["trackParams"]
    assert {"key": "spawn.position", "value": "1.000,0.200,2.000"} in track_params
    assert {"key": "spawn.yaw_deg", "value": "90.0"} in track_params


def test_reset_config_agent_spawn_pose_position_2d_list_uses_default_y() -> None:
    payload = {
        **_MINIMAL_VALID,
        "agents": {"vehicles": [{"agentId": "ego", "vehicleId": "v1", "spawnPose": {"position": [3.0, 4.0]}}]},
    }
    cfg = scenario_to_reset_config(payload)
    track_params = cfg["agents"][0]["trackParams"]
    assert {"key": "spawn.position", "value": "3.000,0.200,4.000"} in track_params


def test_reset_config_world_and_route_params_collated_into_track_params() -> None:
    payload = {
        **_MINIMAL_VALID,
        "world": {"trackId": "t1", "params": {"world.foo": "bar"}},
        "route": {"params": {"route.alpha": "1.5"}},
    }
    cfg = scenario_to_reset_config(payload)
    keys = {p["key"] for p in cfg["trackParams"]}
    assert {"world.foo", "route.alpha"} <= keys
