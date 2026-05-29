"""Tests for ``ABCorridorVisionEnv`` construction that do not require Unity."""

from __future__ import annotations

from pathlib import Path

import pytest

pytest.importorskip("PIL")

from training.ab_corridor_vision_env import ABCorridorVisionEnv  # noqa: E402


def test_cardboard_maze_scenario_uses_explicit_path_encoded(tmp_path: Path) -> None:
    scenario_path = tmp_path / "maze-explicit-path.yaml"
    scenario_path.write_text(
        """
scenarioId: maze-explicit-path
runtime:
  runtimeMode: unity-sim
  headless: false
  timeScale: 1.0
  seed: 42
world:
  trackId: track.cardboard_maze.v1
  params:
    maze.seed: 42
    maze.length_cells: 30
    maze.corridor_width_m: 0.45
    maze.left_turns: 6
    maze.right_turns: 6
    maze.path_encoded: "20,20;20,21;20,22;21,22;22,22"
    maze.wall_height_m: 0.25
vehicle:
  vehicleId: vehicle.ks0223.v1
route:
  params:
    goal.radius_m: 0.24
""",
        encoding="utf-8",
    )

    env = ABCorridorVisionEnv(scenario_path=scenario_path)

    assert env.goal_radius_m == pytest.approx(0.24)
    assert env.waypoints == [
        (0.0, 0.0),
        (0.0, 0.45),
        (0.0, 0.9),
        (0.45, 0.9),
        (0.9, 0.9),
    ]
    assert [
        item["value"]
        for item in env._reset_config["trackParams"]
        if item.get("key") == "maze.path_encoded"
    ] == ["20,20;20,21;20,22;21,22;22,22"]
