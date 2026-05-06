"""Tests for ros2_bridge_multi (mocked rclpy)."""

from __future__ import annotations

import sys
from pathlib import Path
from unittest.mock import MagicMock

import pytest

PYTHON_ROOT = Path(__file__).resolve().parents[2]
if str(PYTHON_ROOT) not in sys.path:
    sys.path.insert(0, str(PYTHON_ROOT))

from bridges import ros2_bridge_multi as bridge_multi  # noqa: E402


# ---------------------------------------------------------------------------
# parse_agents_arg
# ---------------------------------------------------------------------------


def test_parse_agents_arg_none_means_auto():
    assert bridge_multi.parse_agents_arg(None) is None


def test_parse_agents_arg_auto_keyword_means_auto():
    assert bridge_multi.parse_agents_arg("auto") is None
    assert bridge_multi.parse_agents_arg("  AUTO  ") is None
    assert bridge_multi.parse_agents_arg("") is None


def test_parse_agents_arg_comma_and_whitespace():
    assert bridge_multi.parse_agents_arg("ego,npc-01, npc-02") == ["ego", "npc-01", "npc-02"]
    assert bridge_multi.parse_agents_arg("ego npc-01\tnpc-02") == ["ego", "npc-01", "npc-02"]


def test_parse_agents_arg_sanitizes_unsafe_chars():
    # Slashes and other unsafe chars get replaced with underscore.
    assert bridge_multi.parse_agents_arg("ego, weird/id, npc.bot") == ["ego", "weird_id", "npc_bot"]


# ---------------------------------------------------------------------------
# discover_agent_ids
# ---------------------------------------------------------------------------


def test_discover_agent_ids_uses_agents_array():
    client = MagicMock()
    client.step.return_value = {
        "agents": [
            {"agentId": "ego", "vehicleId": "ks0223"},
            {"agentId": "npc-01", "vehicleId": "ks0223"},
            {"agentId": "npc-02", "vehicleId": "ks0223"},
        ],
        "activeAgentId": "ego",
    }
    ids = bridge_multi.discover_agent_ids(client)
    assert ids == ["ego", "npc-01", "npc-02"]


def test_discover_agent_ids_falls_back_to_active_agent():
    client = MagicMock()
    client.step.return_value = {"activeAgentId": "ego"}
    assert bridge_multi.discover_agent_ids(client) == ["ego"]


def test_discover_agent_ids_falls_back_to_default_on_error():
    client = MagicMock()
    client.step.side_effect = RuntimeError("connection refused")
    assert bridge_multi.discover_agent_ids(client, fallback=("primary",)) == ["primary"]


# ---------------------------------------------------------------------------
# MultiAgentRos2Bridge — namespace creation + step routing
# ---------------------------------------------------------------------------


def _make_node_stub():
    """Build a stand-in for rclpy Node so we can construct AgentChannel + bridge."""
    node = MagicMock()
    node.create_publisher.return_value = MagicMock()
    node.create_subscription.return_value = MagicMock()
    node.create_timer.return_value = MagicMock()
    node.get_logger.return_value = MagicMock()

    clock = MagicMock()
    clock.now.return_value.to_msg.return_value = MagicMock()
    node.get_clock.return_value = clock
    return node


def test_multi_agent_creates_one_namespace_per_agent(monkeypatch):
    """Each agent must get its own AgentChannel keyed by sanitized id."""
    # Patch Node base + rclpy guard so the bridge subclass acts on the stub.
    node_stub = _make_node_stub()
    monkeypatch.setattr(bridge_multi.MultiAgentRos2Bridge, "__init__", _no_init)

    bridge = bridge_multi.MultiAgentRos2Bridge.__new__(bridge_multi.MultiAgentRos2Bridge)
    bridge._client = MagicMock()
    bridge._topic_prefix = "/uavsim"
    bridge._vehicle_id = ""
    bridge._track_id = ""
    bridge._last_auto_reset_attempt_sec = 0.0
    bridge.agents = {
        agent_id: _build_channel_with_stub_node(node_stub, agent_id, "/uavsim")
        for agent_id in ("ego", "npc01", "npc02")
    }
    bridge.get_logger = node_stub.get_logger
    bridge.get_clock = node_stub.get_clock

    assert set(bridge.agents.keys()) == {"ego", "npc01", "npc02"}
    assert bridge.agents["ego"].ns == "/uavsim/ego"
    assert bridge.agents["npc01"].ns == "/uavsim/npc01"
    assert bridge.agents["npc02"].ns == "/uavsim/npc02"


def test_step_all_routes_target_agent_id_per_agent(monkeypatch):
    """step_all must call SimClient.step exactly once per agent with the right targetAgentId."""
    node_stub = _make_node_stub()
    monkeypatch.setattr(bridge_multi.MultiAgentRos2Bridge, "__init__", _no_init)

    client = MagicMock()
    # Return a minimal but well-shaped response so publish_step_result doesn't blow up.
    client.step.return_value = {
        "state": {
            "pose": {
                "position": {"x": 1.0, "y": 0.0, "z": 2.0},
                "rotation": {"x": 0.0, "y": 0.0, "z": 0.0, "w": 1.0},
            },
            "linearVelocity": {"x": 0.1, "y": 0.0, "z": 0.0},
            "angularVelocity": {"x": 0.0, "y": 0.0, "z": 0.0},
            "speed": 0.1,
            "telemetry": [],
        },
        "frame": None,
    }

    bridge = bridge_multi.MultiAgentRos2Bridge.__new__(bridge_multi.MultiAgentRos2Bridge)
    bridge._client = client
    bridge._topic_prefix = "/uavsim"
    bridge._vehicle_id = ""
    bridge._track_id = ""
    bridge._last_auto_reset_attempt_sec = 0.0
    bridge.get_logger = node_stub.get_logger
    bridge.get_clock = node_stub.get_clock
    bridge.agents = {
        agent_id: _build_channel_with_stub_node(node_stub, agent_id, "/uavsim")
        for agent_id in ("ego", "npc-01")
    }
    # publish_step_result builds typed ROS messages whose classes are stubbed
    # when rclpy isn't installed; replace with no-ops so we can exercise routing.
    for ch in bridge.agents.values():
        ch.publish_step_result = MagicMock()

    bridge.step_all()

    assert client.step.call_count == 2
    sent_target_ids = [call.args[0]["targetAgentId"] for call in client.step.call_args_list]
    assert sent_target_ids == ["ego", "npc-01"]
    # Each command must carry KS0223 PWM extensions.
    for call in client.step.call_args_list:
        payload = call.args[0]
        ext_keys = {kv["key"] for kv in payload["extensions"]}
        assert ext_keys == {"drive.left_pwm_norm", "drive.right_pwm_norm"}


def test_agent_channel_cmd_vel_updates_pending_command(monkeypatch):
    """Twist cmd_vel must convert into per-agent left/right PWM."""
    node_stub = _make_node_stub()
    channel = _build_channel_with_stub_node(node_stub, "ego", "/uavsim")

    twist = MagicMock()
    twist.linear.x = 0.6
    twist.linear.z = 0.0
    twist.angular.z = 0.4
    channel._on_cmd_vel(twist)

    cmd = channel.consume_pending_command()
    payload = cmd.to_step_command()
    ext = {kv["key"]: float(kv["value"]) for kv in payload["extensions"]}
    # linear=0.6, angular=0.4, half_track=0.5 -> left=0.4, right=0.8
    assert ext["drive.left_pwm_norm"] == pytest.approx(0.4, abs=1e-3)
    assert ext["drive.right_pwm_norm"] == pytest.approx(0.8, abs=1e-3)


def test_namespace_safe_for_unsafe_input():
    """An agent id with slashes or dots must not produce a malformed ROS namespace."""
    node_stub = _make_node_stub()
    channel = _build_channel_with_stub_node(node_stub, "weird/id.bot", "/uavsim/")
    assert channel.ns == "/uavsim/weird_id_bot"


# ---------------------------------------------------------------------------
# helpers
# ---------------------------------------------------------------------------


def _no_init(self, *args, **kwargs):
    """Skip rclpy.Node.__init__ during tests."""
    return None


def _build_channel_with_stub_node(node_stub, agent_id, prefix):
    """Construct AgentChannel against a MagicMock node (so we don't hit rclpy)."""
    channel = bridge_multi.AgentChannel.__new__(bridge_multi.AgentChannel)
    channel.agent_id = agent_id
    channel.ns = f"{bridge_multi._safe_prefix(prefix)}/{bridge_multi._safe_agent_id(agent_id)}"
    channel._node = node_stub
    channel.left_pwm = 0.0
    channel.right_pwm = 0.0
    channel.brake = 0.0
    channel._warned_missing_pillow = False
    channel._state_json_pub = MagicMock()
    channel._telemetry_json_pub = MagicMock()
    channel._odom_pub = MagicMock()
    channel._speed_pub = MagicMock()
    channel._battery_pub = MagicMock()
    channel._ultrasonic_pub = MagicMock()
    channel._line_tracker_pub = MagicMock()
    channel._powertrain_pub = MagicMock()
    channel._camera_raw_pub = MagicMock()
    channel._camera_compressed_pub = MagicMock()
    return channel
