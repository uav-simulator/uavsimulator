"""Mock-based tests for ``CityAutonomyEnv`` — no live Unity required.

Pins the gym surface (observation/action spaces, reset/step contracts),
the navigation-vector math, the waypoint-progress reward shaping, and the
out-of-route termination guard. The trainer + Unity integration lives
behind ``pytest.importorskip`` because it pulls torch + SB3 + a live
``track.city_polygon.v1`` runtime.
"""

from __future__ import annotations

import base64
import math
import sys
from pathlib import Path
from unittest.mock import MagicMock

import pytest

PYTHON_ROOT = Path(__file__).resolve().parents[2]
if str(PYTHON_ROOT) not in sys.path:
    sys.path.insert(0, str(PYTHON_ROOT))

# Pillow is in the ``training`` extra; skip the whole module without it.
pytest.importorskip("PIL")

import numpy as np  # noqa: E402

from training.city_autonomy_env import (  # noqa: E402
    DEFAULT_DISTANCE_NORMALISER_M,
    IMG_SIZE,
    CityAutonomyEnv,
)

CITY_SCENARIO_DIR = PYTHON_ROOT.parent / "configs" / "scenarios" / "city"
SCENARIO_STRAIGHT = CITY_SCENARIO_DIR / "city-1-straight.yaml"
SCENARIO_RIGHT = CITY_SCENARIO_DIR / "city-2-right-turns.yaml"
SCENARIO_U_TURN = CITY_SCENARIO_DIR / "city-4-u-turn.yaml"


# ---------------------------------------------------------------------------
# helpers
# ---------------------------------------------------------------------------


def _step_result(x: float, z: float, *, yaw_deg: float = 0.0, image_b64: str = "") -> dict:
    """Synthetic step result mimicking what SimClient returns."""
    return {
        "state": {
            "pose": {
                "position": {"x": x, "y": 0.2, "z": z},
                "rotation": {"eulerY": yaw_deg},
            },
            "camera": {"frame": {"dataBase64": image_b64}} if image_b64 else {},
        },
    }


def _make_env(scenario: Path = SCENARIO_STRAIGHT) -> CityAutonomyEnv:
    client = MagicMock()
    return CityAutonomyEnv(client=client, scenario_path=scenario)


# ---------------------------------------------------------------------------
# construction
# ---------------------------------------------------------------------------


def test_env_construction_loads_scenario_waypoints() -> None:
    env = _make_env(SCENARIO_STRAIGHT)
    # city-1-straight has 5 waypoints in (x, z).
    assert env._waypoints.shape == (5, 2)


def test_env_requires_a_scenario() -> None:
    with pytest.raises(ValueError, match="route.waypoints"):
        CityAutonomyEnv(client=MagicMock(), scenario_path=None)


def test_observation_space_shape() -> None:
    env = _make_env()
    image_space = env.observation_space["image"]
    nav_space = env.observation_space["nav"]
    assert image_space.shape == (IMG_SIZE, IMG_SIZE, 3)
    assert image_space.dtype == np.uint8
    assert nav_space.shape == (4,)
    assert nav_space.low.tolist() == [-1.0, -1.0, -1.0, -1.0]
    assert nav_space.high.tolist() == [1.0, 1.0, 1.0, 1.0]


def test_action_space_is_2d_continuous() -> None:
    env = _make_env()
    assert env.action_space.shape == (2,)
    assert env.action_space.low.tolist() == [-1.0, -1.0]
    assert env.action_space.high.tolist() == [1.0, 1.0]


# ---------------------------------------------------------------------------
# reset / step
# ---------------------------------------------------------------------------


def test_reset_calls_client_reset_and_returns_obs() -> None:
    env = _make_env(SCENARIO_STRAIGHT)
    env.client.reset.return_value = _step_result(0.0, -20.0)

    obs, info = env.reset()

    env.client.reset.assert_called_once()
    assert obs["image"].shape == (IMG_SIZE, IMG_SIZE, 3)
    assert obs["nav"].shape == (4,)
    assert info["nav.next_wp_idx"] == 0
    assert info["nav.total_waypoints"] == 5


def test_step_calls_client_step_with_clipped_action() -> None:
    env = _make_env(SCENARIO_STRAIGHT)
    env.client.reset.return_value = _step_result(0.0, -20.0)
    env.client.step.return_value = _step_result(0.0, -19.5)
    env.reset()

    env.step(np.array([2.0, -3.0], dtype=np.float32))  # out-of-range

    cmd = env.client.step.call_args.args[0]
    assert cmd["throttle"] == 1.0
    assert cmd["steer"] == -1.0


# ---------------------------------------------------------------------------
# navigation vector math
# ---------------------------------------------------------------------------


def test_nav_vector_zero_at_waypoint_along_forward() -> None:
    """Agent at origin facing +Z, next waypoint dead ahead → cos(yaw_err)=1, sin=0."""
    env = _make_env(SCENARIO_STRAIGHT)
    env.client.reset.return_value = _step_result(0.0, -20.0, yaw_deg=0.0)
    env.reset()
    # next waypoint after reset is (0, -20) — distance 0.
    nav = env._build_nav_vector(_step_result(0.0, -20.0, yaw_deg=0.0))
    assert nav[0] == pytest.approx(1.0, abs=1e-3) or math.isclose(nav[0], 0.0, abs_tol=1.0)


def test_nav_vector_yaw_error_when_misaligned() -> None:
    """Agent facing +Z, waypoint to the EAST (+x) → bearing=π/2 → sin(yaw_err)=+1."""
    env = _make_env(SCENARIO_STRAIGHT)
    env.client.reset.return_value = _step_result(0.0, 0.0, yaw_deg=0.0)
    env.reset()
    # Force next waypoint to the east.
    env._waypoints = np.array([[10.0, 0.0]], dtype=np.float32)
    env._next_wp_idx = 0
    nav = env._build_nav_vector(_step_result(0.0, 0.0, yaw_deg=0.0))
    # Bearing east when facing north → +π/2 → cos=0, sin=+1
    assert nav[0] == pytest.approx(0.0, abs=1e-3)
    assert nav[1] == pytest.approx(1.0, abs=1e-3)


def test_nav_vector_distance_normalised_to_unit() -> None:
    env = _make_env(SCENARIO_STRAIGHT)
    env.client.reset.return_value = _step_result(0.0, 0.0)
    env.reset()
    env._waypoints = np.array([[0.0, DEFAULT_DISTANCE_NORMALISER_M]], dtype=np.float32)
    env._next_wp_idx = 0
    nav = env._build_nav_vector(_step_result(0.0, 0.0))
    assert nav[2] == pytest.approx(1.0, abs=1e-3)


def test_nav_vector_remaining_fraction_decreases() -> None:
    env = _make_env(SCENARIO_STRAIGHT)
    env.client.reset.return_value = _step_result(0.0, -20.0)
    env.reset()
    nav_at_start = env._build_nav_vector(_step_result(0.0, -20.0))
    env._next_wp_idx = 3  # 2 of 5 remaining
    nav_later = env._build_nav_vector(_step_result(0.0, -20.0))
    assert nav_at_start[3] == 1.0
    assert nav_later[3] == pytest.approx(0.4, abs=1e-3)


# ---------------------------------------------------------------------------
# reward shaping
# ---------------------------------------------------------------------------


def test_reward_progress_positive_when_closing_distance() -> None:
    env = _make_env(SCENARIO_STRAIGHT)
    env.client.reset.return_value = _step_result(0.0, -20.0)  # at first waypoint
    env.reset()
    # Move closer to second waypoint (0, -10).
    env._waypoints = np.array([[0.0, -10.0]], dtype=np.float32)
    env._next_wp_idx = 0
    env._last_distance_to_next = 5.0
    reward, term = env._compute_reward(
        distance_now=4.0, steer=0.0, waypoint_reached=False, pose=(0.0, -14.0, 0.0)
    )
    # +1 progress + 0.05 alive
    assert reward == pytest.approx(1.05, abs=1e-3)
    assert term is None


def test_reward_progress_negative_when_regressing() -> None:
    env = _make_env(SCENARIO_STRAIGHT)
    env.client.reset.return_value = _step_result(0.0, -20.0)
    env.reset()
    env._last_distance_to_next = 5.0
    # Use the full straight-route waypoints so the agent stays on-route even
    # when slightly regressing on the immediate-next-waypoint distance.
    env._next_wp_idx = 1  # next is (0, -10), straight-route has 5 wps along x=0.
    reward, term = env._compute_reward(
        distance_now=6.0, steer=0.0, waypoint_reached=False, pose=(0.0, -16.0, 0.0)
    )
    # -1 progress (clipped) + 0.05 alive, no OOB because (0,-16) is 4m
    # from waypoint (0,-20) which is well within the 5m threshold.
    assert reward == pytest.approx(-0.95, abs=1e-3)
    assert term is None


def test_reward_steering_penalty_quadratic() -> None:
    env = _make_env(SCENARIO_STRAIGHT)
    env.client.reset.return_value = _step_result(0.0, -20.0)
    env.reset()
    env._last_distance_to_next = 5.0
    env._waypoints = np.array([[0.0, -10.0]], dtype=np.float32)
    env._next_wp_idx = 0
    no_steer, _ = env._compute_reward(
        distance_now=5.0, steer=0.0, waypoint_reached=False, pose=(0.0, -15.0, 0.0)
    )
    full_steer, _ = env._compute_reward(
        distance_now=5.0, steer=1.0, waypoint_reached=False, pose=(0.0, -15.0, 0.0)
    )
    assert no_steer - full_steer == pytest.approx(0.02, abs=1e-3)


def test_reward_waypoint_reach_bonus() -> None:
    env = _make_env(SCENARIO_STRAIGHT)
    env.client.reset.return_value = _step_result(0.0, -20.0)
    env.reset()
    env._last_distance_to_next = 5.0
    reward, term = env._compute_reward(
        distance_now=1.0, steer=0.0, waypoint_reached=True, pose=(0.0, -19.0, 0.0)
    )
    # progress(+1, clipped) + 0.05 alive + 1.0 waypoint bonus
    assert reward >= 2.0
    assert term is None  # not last waypoint


def test_reward_route_complete_terminates() -> None:
    env = _make_env(SCENARIO_STRAIGHT)
    env.client.reset.return_value = _step_result(0.0, -20.0)
    env.reset()
    # Force agent on the LAST waypoint.
    env._next_wp_idx = len(env._waypoints) - 1  # 4 (zero-indexed last of 5)
    env._last_distance_to_next = 1.5
    reward, term = env._compute_reward(
        distance_now=0.5, steer=0.0, waypoint_reached=True, pose=(0.0, 19.5, 0.0)
    )
    assert term == "route_complete"
    assert reward >= 6.0  # progress + alive + 1.0 wp + 5.0 route


def test_reward_out_of_route_terminates() -> None:
    env = _make_env(SCENARIO_STRAIGHT)
    env.client.reset.return_value = _step_result(0.0, -20.0)
    env.reset()
    env._last_distance_to_next = 5.0
    # Far from any waypoint on the straight (waypoints lie on x=0, z in [-20, 20]).
    reward, term = env._compute_reward(
        distance_now=50.0, steer=0.0, waypoint_reached=False, pose=(50.0, 0.0, 0.0)
    )
    assert term == "out_of_route"
    assert reward < 0  # at least the -5 OOB penalty


def test_min_distance_to_any_waypoint_picks_nearest_off_route() -> None:
    env = _make_env(SCENARIO_STRAIGHT)
    # Straight has waypoints at x=0, z ∈ {-20, -10, 0, 10, 20}.
    # Position (1, 11) is closest to (0, 10) → distance hypot(1, 1) = √2.
    d = env._min_distance_to_any_waypoint_from(1.0, 11.0)
    assert d == pytest.approx(math.hypot(1.0, 1.0), abs=1e-4)


# ---------------------------------------------------------------------------
# end-to-end mocked rollout (one full episode)
# ---------------------------------------------------------------------------


def test_full_mock_episode_terminates_on_route_complete() -> None:
    env = _make_env(SCENARIO_STRAIGHT)
    waypoints = env._waypoints.tolist()
    # Mock SimClient: reset places agent at the spawn, each step teleports
    # closer to next waypoint, eventually reaching the last.
    env.client.reset.return_value = _step_result(*waypoints[0])

    # Sequence of step results: just-past each waypoint.
    step_seq = [_step_result(x, z) for x, z in waypoints]
    env.client.step.side_effect = step_seq

    obs, _ = env.reset()
    rewards = []
    terminated = False
    for _ in range(len(step_seq)):
        obs, reward, terminated, truncated, info = env.step(np.array([0.5, 0.0], dtype=np.float32))
        rewards.append(reward)
        if terminated or truncated:
            break

    assert terminated is True
    assert sum(rewards) > 0
    assert env._next_wp_idx >= len(env._waypoints) - 1


# ---------------------------------------------------------------------------
# image extraction
# ---------------------------------------------------------------------------


def test_image_extraction_handles_missing_frame() -> None:
    env = _make_env(SCENARIO_STRAIGHT)
    env.client.reset.return_value = _step_result(0.0, 0.0)
    env.reset()
    img = env._extract_image(_step_result(0.0, 0.0))  # no image_b64
    assert img.shape == (IMG_SIZE, IMG_SIZE, 3)
    assert img.dtype == np.uint8
    assert img.sum() == 0  # all zeros


def test_image_extraction_handles_invalid_base64() -> None:
    env = _make_env(SCENARIO_STRAIGHT)
    env.client.reset.return_value = _step_result(0.0, 0.0)
    env.reset()
    img = env._extract_image(_step_result(0.0, 0.0, image_b64="$$$ not base64 $$$"))
    assert img.shape == (IMG_SIZE, IMG_SIZE, 3)
    assert img.sum() == 0


def test_image_extraction_decodes_valid_png() -> None:
    """Round-trip a small PNG through the env's decoder."""
    pytest.importorskip("PIL")
    import io

    from PIL import Image as PilImage

    src = PilImage.new("RGB", (16, 16), color=(200, 100, 50))
    buf = io.BytesIO()
    src.save(buf, format="PNG")
    encoded = base64.b64encode(buf.getvalue()).decode("ascii")

    env = _make_env(SCENARIO_STRAIGHT)
    env.client.reset.return_value = _step_result(0.0, 0.0)
    env.reset()
    img = env._extract_image(_step_result(0.0, 0.0, image_b64=encoded))
    assert img.shape == (IMG_SIZE, IMG_SIZE, 3)
    # Output should be tinted toward source colour after resize.
    avg = img.mean(axis=(0, 1))
    assert avg[0] > avg[2]  # red > blue
