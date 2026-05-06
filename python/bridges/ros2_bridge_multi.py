#!/usr/bin/env python3
"""Multi-agent ROS2 bridge for UAV simulator HTTP API.

Extends ``ros2_bridge.py`` to N agents. Each agent gets its own ROS namespace
(``{topic_prefix}/{agent_id}``) with the same publisher/subscriber set as the
single-agent bridge. The step-loop drives Unity once per agent per tick using
``targetAgentId`` routing on ``/step``.

The single-agent ``ros2_bridge.py`` is intentionally not modified; this file
runs as a separate entry point so existing flows (``make demo-*``) keep
working unchanged.
"""

from __future__ import annotations

import argparse
import base64
import io
import json
import math
import os
import re
import sys
import time
from pathlib import Path
from typing import Any, Dict, Iterable, List, Mapping, Optional, Sequence

import requests

# Allow running as standalone script from any working directory.
_PYTHON_DIR = Path(__file__).resolve().parent.parent
if str(_PYTHON_DIR) not in sys.path:
    sys.path.insert(0, str(_PYTHON_DIR))

_SDK_IMPORT_ERROR: Exception | None = None
try:
    from sim_client.http_client import SimClient
    from sim_client.ks0223 import Ks0223Command, parse_telemetry
except ModuleNotFoundError as exc:
    _SDK_IMPORT_ERROR = exc
    SimClient = Any  # type: ignore[assignment,misc]
    Ks0223Command = Any  # type: ignore[assignment,misc]

    def parse_telemetry(step_result: Mapping[str, Any]) -> Dict[str, str]:  # type: ignore[no-redef]
        _ = step_result
        return {}

_PIL_IMPORT_ERROR: Exception | None = None
try:
    from PIL import Image as PilImage
except ModuleNotFoundError as exc:
    _PIL_IMPORT_ERROR = exc
    PilImage = None  # type: ignore[assignment,misc]

_ROS2_IMPORT_ERROR: Exception | None = None
try:
    import rclpy
    from geometry_msgs.msg import Twist
    from nav_msgs.msg import Odometry
    from rclpy.node import Node
    from rclpy.qos import qos_profile_sensor_data
    from sensor_msgs.msg import BatteryState
    from sensor_msgs.msg import CompressedImage
    from sensor_msgs.msg import Image as RosImage
    from sensor_msgs.msg import Range
    from std_msgs.msg import Float32
    from std_msgs.msg import Float32MultiArray
    from std_msgs.msg import String
except ModuleNotFoundError as exc:
    _ROS2_IMPORT_ERROR = exc
    rclpy = None  # type: ignore[assignment]
    Node = object  # type: ignore[assignment,misc]
    qos_profile_sensor_data = None  # type: ignore[assignment,misc]
    Twist = object  # type: ignore[assignment,misc]
    Odometry = object  # type: ignore[assignment,misc]
    BatteryState = object  # type: ignore[assignment,misc]
    CompressedImage = object  # type: ignore[assignment,misc]
    RosImage = object  # type: ignore[assignment,misc]
    Range = object  # type: ignore[assignment,misc]
    Float32 = object  # type: ignore[assignment,misc]
    Float32MultiArray = object  # type: ignore[assignment,misc]
    String = object  # type: ignore[assignment,misc]


# ---------------------------------------------------------------------------
# Helpers (mirror ros2_bridge.py — kept here so this file stands on its own).
# ---------------------------------------------------------------------------


def _now_ms() -> int:
    return int(time.time() * 1000)


def _safe_prefix(value: str) -> str:
    prefix = (value or "/uavsim").strip()
    if not prefix.startswith("/"):
        prefix = "/" + prefix
    return prefix.rstrip("/")


def _safe_agent_id(value: str) -> str:
    text = (value or "").strip().strip("/")
    text = re.sub(r"[^a-zA-Z0-9_\-]", "_", text)
    return text or "agent"


def _clamp(value: float, lo: float, hi: float) -> float:
    return max(lo, min(hi, value))


def _as_float(value: Any, default: float = 0.0) -> float:
    try:
        return float(value)
    except (TypeError, ValueError):
        return default


def _telemetry_float(telemetry: Mapping[str, str], key: str, default: float = 0.0) -> float:
    value = telemetry.get(key)
    if value is None:
        return default
    try:
        return float(value)
    except ValueError:
        return default


def _decode_frame_bytes(frame: Mapping[str, Any]) -> bytes:
    data_base64 = frame.get("dataBase64")
    if not isinstance(data_base64, str) or not data_base64:
        return b""
    try:
        return base64.b64decode(data_base64)
    except Exception:
        return b""


def _decode_rgb8(image_bytes: bytes) -> tuple[bytes, int, int] | None:
    if PilImage is None or not image_bytes:
        return None
    try:
        with PilImage.open(io.BytesIO(image_bytes)) as image:
            rgb = image.convert("RGB")
            width, height = rgb.size
            return rgb.tobytes(), int(width), int(height)
    except Exception:
        return None


def _sanitize_frame_id(value: str, default: str = "camera_front_optical") -> str:
    text = (value or "").strip()
    if not text:
        return default
    text = text.replace("/", "_")
    text = re.sub(r"[^a-zA-Z0-9_]", "_", text)
    return text or default


def parse_agents_arg(raw: Optional[str]) -> Optional[List[str]]:
    """Parse --agents CLI argument.

    Returns:
      * None  — caller should auto-detect from /state.
      * []    — explicit empty (treated like None / auto).
      * [...] — explicit list of agent ids.
    """
    if raw is None:
        return None
    text = raw.strip()
    if not text or text.lower() == "auto":
        return None
    parts = [p.strip() for p in re.split(r"[,\s]+", text) if p.strip()]
    return [_safe_agent_id(p) for p in parts]


def discover_agent_ids(client: "SimClient", fallback: Sequence[str] = ("ego",)) -> List[str]:
    """Try to extract agent ids from a zero-action /step response.

    Falls back to ``fallback`` if the response does not include an ``agents``
    field (older Unity builds).
    """
    try:
        probe = client.step(
            {
                "throttle": 0.0,
                "steer": 0.0,
                "brake": 0.0,
                "timestamp": _now_ms(),
                "timeBase": "unix_ms",
                "extensions": [],
            }
        )
    except Exception:
        return list(fallback)

    agents = probe.get("agents") if isinstance(probe, Mapping) else None
    ids: List[str] = []
    if isinstance(agents, Iterable):
        for item in agents:
            if not isinstance(item, Mapping):
                continue
            agent_id = item.get("agentId")
            if isinstance(agent_id, str) and agent_id.strip():
                ids.append(_safe_agent_id(agent_id))

    if not ids:
        active = probe.get("activeAgentId") if isinstance(probe, Mapping) else None
        if isinstance(active, str) and active.strip():
            ids.append(_safe_agent_id(active))

    return ids or list(fallback)


# ---------------------------------------------------------------------------
# AgentChannel — per-agent publishers/subscribers + last cmd_vel cache.
# ---------------------------------------------------------------------------


class AgentChannel:
    """Per-agent ROS topics (publishers + cmd_vel subscriber)."""

    def __init__(self, node: Node, agent_id: str, topic_prefix: str) -> None:
        self.agent_id = agent_id
        self.ns = f"{_safe_prefix(topic_prefix)}/{_safe_agent_id(agent_id)}"
        self._node = node

        self.left_pwm = 0.0
        self.right_pwm = 0.0
        self.brake = 0.0
        self._warned_missing_pillow = False

        # Backward-compatible JSON channels.
        self._state_json_pub = node.create_publisher(String, f"{self.ns}/state_json", 10)
        self._telemetry_json_pub = node.create_publisher(String, f"{self.ns}/telemetry_json", 10)

        # Typed channels.
        self._odom_pub = node.create_publisher(Odometry, f"{self.ns}/odom", 10)
        self._speed_pub = node.create_publisher(Float32, f"{self.ns}/speedometer/mps", 10)
        self._battery_pub = node.create_publisher(BatteryState, f"{self.ns}/battery_state", 10)
        self._ultrasonic_pub = node.create_publisher(Range, f"{self.ns}/ultrasonic/front", 10)
        self._line_tracker_pub = node.create_publisher(
            Float32MultiArray, f"{self.ns}/line_tracker/front_norm", 10
        )
        self._powertrain_pub = node.create_publisher(
            Float32MultiArray, f"{self.ns}/powertrain/estimate", 10
        )
        self._camera_raw_pub = node.create_publisher(
            RosImage,
            f"{self.ns}/camera/front/image_raw",
            qos_profile_sensor_data,
        )
        self._camera_compressed_pub = node.create_publisher(
            CompressedImage,
            f"{self.ns}/camera/front/image_raw/compressed",
            qos_profile_sensor_data,
        )

        # Control channels (per-agent).
        node.create_subscription(
            Float32MultiArray, f"{self.ns}/cmd_drive", self._on_cmd_drive_array, 10
        )
        node.create_subscription(String, f"{self.ns}/cmd_drive_json", self._on_cmd_drive_json, 10)
        node.create_subscription(Twist, f"{self.ns}/cmd_vel", self._on_cmd_vel, 10)

    # ------------------------- subscriber callbacks ------------------------

    def _on_cmd_drive_array(self, msg: Float32MultiArray) -> None:
        if len(msg.data) >= 2:
            self.left_pwm = _clamp(_as_float(msg.data[0]), -1.0, 1.0)
            self.right_pwm = _clamp(_as_float(msg.data[1]), -1.0, 1.0)
        if len(msg.data) >= 3:
            self.brake = _clamp(_as_float(msg.data[2]), 0.0, 1.0)

    def _on_cmd_drive_json(self, msg: String) -> None:
        try:
            payload = json.loads(msg.data)
        except json.JSONDecodeError:
            self._node.get_logger().warning(
                f"[{self.agent_id}] Invalid JSON on cmd_drive_json topic."
            )
            return
        if not isinstance(payload, Mapping):
            return
        left = payload.get("left_pwm_norm", self.left_pwm)
        right = payload.get("right_pwm_norm", self.right_pwm)
        brake = payload.get("brake", self.brake)
        self.left_pwm = _clamp(_as_float(left), -1.0, 1.0)
        self.right_pwm = _clamp(_as_float(right), -1.0, 1.0)
        self.brake = _clamp(_as_float(brake), 0.0, 1.0)

    def _on_cmd_vel(self, msg: Twist) -> None:
        linear = _clamp(_as_float(msg.linear.x), -1.0, 1.0)
        angular = _clamp(_as_float(msg.angular.z), -1.0, 1.0)
        half_track = 0.5
        self.left_pwm = _clamp(linear - angular * half_track, -1.0, 1.0)
        self.right_pwm = _clamp(linear + angular * half_track, -1.0, 1.0)
        self.brake = _clamp(_as_float(msg.linear.z), 0.0, 1.0)

    # --------------------------- publish helpers ---------------------------

    def consume_pending_command(self) -> "Ks0223Command":
        return Ks0223Command(
            left_pwm_norm=self.left_pwm,
            right_pwm_norm=self.right_pwm,
            brake=self.brake,
            timestamp=_now_ms(),
            time_base="unix_ms",
        )

    def publish_step_result(self, step_result: Mapping[str, Any], stamp: Any) -> None:
        state = step_result.get("state")
        if not isinstance(state, Mapping):
            state = {}
        telemetry = parse_telemetry(step_result)

        self._publish_state_json(state)
        self._publish_telemetry_json(telemetry)
        self._publish_odometry(state, stamp)
        self._publish_speedometer(state)
        self._publish_battery(telemetry, stamp)
        self._publish_ultrasonic(telemetry, stamp)
        self._publish_line_tracker(telemetry)
        self._publish_powertrain(telemetry)
        self._publish_camera(step_result, stamp)

    def _publish_state_json(self, state: Mapping[str, Any]) -> None:
        msg = String()
        msg.data = json.dumps(state, ensure_ascii=False)
        self._state_json_pub.publish(msg)

    def _publish_telemetry_json(self, telemetry: Mapping[str, str]) -> None:
        msg = String()
        msg.data = json.dumps(dict(telemetry), ensure_ascii=False)
        self._telemetry_json_pub.publish(msg)

    def _publish_odometry(self, state: Mapping[str, Any], stamp: Any) -> None:
        pose = state.get("pose")
        if not isinstance(pose, Mapping):
            pose = {}
        position = pose.get("position") if isinstance(pose.get("position"), Mapping) else {}
        rotation = pose.get("rotation") if isinstance(pose.get("rotation"), Mapping) else {}

        linear_velocity = state.get("linearVelocity")
        if not isinstance(linear_velocity, Mapping):
            linear_velocity = {}
        angular_velocity = state.get("angularVelocity")
        if not isinstance(angular_velocity, Mapping):
            angular_velocity = {}

        odom = Odometry()
        odom.header.stamp = stamp
        odom.header.frame_id = "odom"
        odom.child_frame_id = f"base_link_{_safe_agent_id(self.agent_id)}"

        odom.pose.pose.position.x = _as_float(position.get("x"))
        odom.pose.pose.position.y = _as_float(position.get("y"))
        odom.pose.pose.position.z = _as_float(position.get("z"))

        odom.pose.pose.orientation.x = _as_float(rotation.get("x"))
        odom.pose.pose.orientation.y = _as_float(rotation.get("y"))
        odom.pose.pose.orientation.z = _as_float(rotation.get("z"))
        odom.pose.pose.orientation.w = _as_float(rotation.get("w"), 1.0)

        odom.twist.twist.linear.x = _as_float(linear_velocity.get("x"))
        odom.twist.twist.linear.y = _as_float(linear_velocity.get("y"))
        odom.twist.twist.linear.z = _as_float(linear_velocity.get("z"))

        odom.twist.twist.angular.x = _as_float(angular_velocity.get("x"))
        odom.twist.twist.angular.y = _as_float(angular_velocity.get("y"))
        odom.twist.twist.angular.z = _as_float(angular_velocity.get("z"))

        self._odom_pub.publish(odom)

    def _publish_speedometer(self, state: Mapping[str, Any]) -> None:
        msg = Float32()
        msg.data = _as_float(state.get("speed"))
        self._speed_pub.publish(msg)

    def _publish_battery(self, telemetry: Mapping[str, str], stamp: Any) -> None:
        voltage = _telemetry_float(telemetry, "power.battery.voltage_v", math.nan)
        current = _telemetry_float(telemetry, "power.battery.current_a", math.nan)
        msg = BatteryState()
        msg.header.stamp = stamp
        msg.header.frame_id = f"base_link_{_safe_agent_id(self.agent_id)}"
        msg.voltage = float(voltage)
        msg.current = float(current)
        msg.charge = math.nan
        msg.capacity = math.nan
        msg.design_capacity = math.nan
        msg.percentage = math.nan
        msg.power_supply_status = BatteryState.POWER_SUPPLY_STATUS_UNKNOWN
        msg.power_supply_health = BatteryState.POWER_SUPPLY_HEALTH_UNKNOWN
        msg.power_supply_technology = BatteryState.POWER_SUPPLY_TECHNOLOGY_UNKNOWN
        msg.present = True
        self._battery_pub.publish(msg)

    def _publish_ultrasonic(self, telemetry: Mapping[str, str], stamp: Any) -> None:
        value = _telemetry_float(telemetry, "sensor.ultrasonic.front.m", 0.0)
        msg = Range()
        msg.header.stamp = stamp
        msg.header.frame_id = f"ultrasonic_front_{_safe_agent_id(self.agent_id)}"
        msg.radiation_type = Range.ULTRASOUND
        msg.field_of_view = 0.26
        msg.min_range = 0.02
        msg.max_range = 4.0
        msg.range = _clamp(value, msg.min_range, msg.max_range)
        self._ultrasonic_pub.publish(msg)

    def _publish_line_tracker(self, telemetry: Mapping[str, str]) -> None:
        values = [
            _telemetry_float(telemetry, "sensor.line_tracker.s1_norm", 0.0),
            _telemetry_float(telemetry, "sensor.line_tracker.s2_norm", 0.0),
            _telemetry_float(telemetry, "sensor.line_tracker.s3_norm", 0.0),
            _telemetry_float(telemetry, "sensor.line_tracker.s4_norm", 0.0),
            _telemetry_float(telemetry, "sensor.line_tracker.s5_norm", 0.0),
        ]
        msg = Float32MultiArray()
        msg.data = [float(v) for v in values]
        self._line_tracker_pub.publish(msg)

    def _publish_powertrain(self, telemetry: Mapping[str, str]) -> None:
        values = [
            _telemetry_float(telemetry, "power.battery.voltage_v", 0.0),
            _telemetry_float(telemetry, "power.battery.current_a", 0.0),
            _telemetry_float(telemetry, "power.motor.estimated_w", 0.0),
        ]
        msg = Float32MultiArray()
        msg.data = [float(v) for v in values]
        self._powertrain_pub.publish(msg)

    def _publish_camera(self, step_result: Mapping[str, Any], stamp: Any) -> None:
        frame = step_result.get("frame")
        if not isinstance(frame, Mapping):
            return
        image_bytes = _decode_frame_bytes(frame)
        if not image_bytes:
            return

        frame_id = _sanitize_frame_id(str(frame.get("frameId", "camera_front_optical")))
        image_format = str(frame.get("format", "jpeg")).lower()

        compressed = CompressedImage()
        compressed.header.stamp = stamp
        compressed.header.frame_id = frame_id
        compressed.format = image_format
        compressed.data = image_bytes
        self._camera_compressed_pub.publish(compressed)

        decoded = _decode_rgb8(image_bytes)
        if decoded is None:
            if PilImage is None and not self._warned_missing_pillow:
                self._node.get_logger().warning(
                    f"[{self.agent_id}] Pillow is not installed. sensor_msgs/Image topic "
                    "will be skipped; compressed image still published."
                )
                self._warned_missing_pillow = True
            return

        rgb_bytes, width, height = decoded
        raw = RosImage()
        raw.header.stamp = stamp
        raw.header.frame_id = frame_id
        raw.height = int(height)
        raw.width = int(width)
        raw.encoding = "rgb8"
        raw.is_bigendian = 0
        raw.step = int(width * 3)
        raw.data = rgb_bytes
        self._camera_raw_pub.publish(raw)


# ---------------------------------------------------------------------------
# MultiAgentRos2Bridge — owns SimClient + N AgentChannels + step timer.
# ---------------------------------------------------------------------------


class MultiAgentRos2Bridge(Node):
    def __init__(
        self,
        client: "SimClient",
        agent_ids: Sequence[str],
        topic_prefix: str = "/uavsim",
        rate_hz: float = 30.0,
        reset_on_start: bool = False,
        vehicle_id: str = "",
        track_id: str = "",
    ) -> None:
        super().__init__("uavsim_ros2_multi_bridge")
        if not agent_ids:
            raise ValueError("agent_ids must not be empty")

        self._client = client
        self._topic_prefix = _safe_prefix(topic_prefix)
        self._vehicle_id = vehicle_id
        self._track_id = track_id
        self._last_auto_reset_attempt_sec = 0.0

        # Build per-agent channel registry.
        self.agents: Dict[str, AgentChannel] = {}
        for agent_id in agent_ids:
            safe_id = _safe_agent_id(agent_id)
            if safe_id in self.agents:
                self.get_logger().warning(
                    f"Duplicate agent id '{safe_id}' ignored."
                )
                continue
            self.agents[safe_id] = AgentChannel(self, safe_id, self._topic_prefix)

        if reset_on_start:
            try:
                self._reset(vehicle_id=vehicle_id, track_id=track_id)
            except Exception as exc:
                self.get_logger().warning(
                    f"Initial reset failed: {exc}. Bridge will retry automatically."
                )

        timer_period_sec = 1.0 / max(1.0, float(rate_hz))
        self.create_timer(timer_period_sec, self.step_all)
        self.get_logger().info(
            f"Multi-agent ROS2 bridge started: prefix={self._topic_prefix}, "
            f"agents={list(self.agents.keys())}, "
            f"base_url={self._client.base_url}, rate_hz={rate_hz:0.2f}"
        )

    # -------------------------------------------------------------------

    def _reset(self, vehicle_id: str, track_id: str) -> None:
        config = {
            "seed": 1,
            "timeScale": 1.0,
            "selectedTrackId": track_id,
            "selectedVehicleId": vehicle_id,
            "trackParams": [],
            "vehicleParams": [],
            "flags": [],
        }
        self._client.reset(config)

    def _is_vehicle_not_initialized_error(self, exc: Exception) -> bool:
        if "Active vehicle is not initialized" in str(exc):
            return True
        if isinstance(exc, requests.HTTPError):
            response = exc.response
            if response is None:
                return False
            body = (response.text or "").strip()
            if "Active vehicle is not initialized" in body:
                return True
            try:
                payload = json.loads(body)
            except json.JSONDecodeError:
                return False
            if isinstance(payload, Mapping):
                error_value = payload.get("error")
                return isinstance(error_value, str) and "Active vehicle is not initialized" in error_value
        return False

    def _try_auto_reset(self) -> None:
        try:
            self._reset(vehicle_id=self._vehicle_id, track_id=self._track_id)
            self.get_logger().info("Auto-reset succeeded after uninitialized vehicle error.")
        except Exception as exc:
            self.get_logger().warning(f"Auto-reset attempt failed: {exc}")

    # -------------------------------------------------------------------

    def step_all(self) -> None:
        """One timer tick — step Unity once per agent."""
        stamp = self.get_clock().now().to_msg()
        for agent_id, channel in self.agents.items():
            try:
                cmd = channel.consume_pending_command().to_step_command()
                cmd["targetAgentId"] = agent_id
                step_result = self._client.step(cmd)
                channel.publish_step_result(step_result, stamp)
            except Exception as exc:
                if self._is_vehicle_not_initialized_error(exc):
                    now = time.monotonic()
                    if now - self._last_auto_reset_attempt_sec >= 1.0:
                        self._last_auto_reset_attempt_sec = now
                        self._try_auto_reset()
                    return
                self.get_logger().warning(
                    f"[{agent_id}] Bridge tick failed: {exc}"
                )


# ---------------------------------------------------------------------------
# Mock loop (no ROS2) — useful for smoke testing the step routing.
# ---------------------------------------------------------------------------


def run_mock_bridge(
    client: "SimClient",
    agent_ids: Sequence[str],
    rate_hz: float,
    reset_on_start: bool,
    vehicle_id: str,
    track_id: str,
    steps: int,
) -> int:
    if reset_on_start:
        client.reset(
            {
                "seed": 1,
                "timeScale": 1.0,
                "selectedTrackId": track_id,
                "selectedVehicleId": vehicle_id,
                "trackParams": [],
                "vehicleParams": [],
                "flags": [],
            }
        )

    period = 1.0 / max(1.0, rate_hz)
    for i in range(max(1, steps)):
        per_agent: List[Dict[str, Any]] = []
        for agent_id in agent_ids:
            cmd = Ks0223Command(
                left_pwm_norm=0.5,
                right_pwm_norm=0.5,
                brake=0.0,
                timestamp=_now_ms(),
                time_base="unix_ms",
            ).to_step_command()
            cmd["targetAgentId"] = agent_id
            step_result = client.step(cmd)
            telemetry = parse_telemetry(step_result)
            frame = step_result.get("frame") if isinstance(step_result, Mapping) else None
            frame_len = 0
            if isinstance(frame, Mapping):
                data_base64 = frame.get("dataBase64")
                if isinstance(data_base64, str):
                    frame_len = len(data_base64)
            per_agent.append(
                {
                    "agent": agent_id,
                    "telemetry_keys": len(telemetry),
                    "frame_b64_len": frame_len,
                }
            )
        print(json.dumps({"tick": i, "agents": per_agent}, ensure_ascii=False))
        time.sleep(period)

    return 0


# ---------------------------------------------------------------------------
# CLI.
# ---------------------------------------------------------------------------


def parse_args(argv: Optional[Sequence[str]] = None) -> argparse.Namespace:
    parser = argparse.ArgumentParser(
        description="Multi-agent ROS2 bridge for UAV simulator HTTP API."
    )
    parser.add_argument(
        "--base-url",
        default=os.getenv("UAVSIM_BASE_URL", "http://127.0.0.1:8000"),
    )
    parser.add_argument(
        "--agents",
        default=os.getenv("UAVSIM_AGENTS", "auto"),
        help=(
            "Comma- or space-separated list of agent ids "
            "(e.g. 'ego,npc-01,npc-02'). 'auto' or empty: query Unity /step."
        ),
    )
    parser.add_argument(
        "--topic-prefix",
        default=os.getenv("UAVSIM_ROS_PREFIX", "/uavsim"),
        help="ROS topic prefix; final namespace = <prefix>/<agent_id>.",
    )
    parser.add_argument(
        "--rate-hz",
        type=float,
        default=float(os.getenv("UAVSIM_ROS_RATE_HZ", "15.0")),
    )
    parser.add_argument("--vehicle-id", default=os.getenv("UAVSIM_VEHICLE_ID", ""))
    parser.add_argument("--track-id", default=os.getenv("UAVSIM_TRACK_ID", ""))
    parser.add_argument("--reset-on-start", action="store_true")
    parser.add_argument(
        "--mock-ros2",
        action="store_true",
        help="Run the multi-agent loop without ROS2 and print payloads.",
    )
    parser.add_argument("--mock-steps", type=int, default=25)
    return parser.parse_args(argv)


def main(argv: Optional[Sequence[str]] = None) -> int:
    args = parse_args(argv)

    if _SDK_IMPORT_ERROR is not None:
        print(
            f"Python SDK dependency missing: {_SDK_IMPORT_ERROR}. "
            "Install dependencies from python/requirements.txt.",
            file=sys.stderr,
        )
        return 4

    client = SimClient(base_url=args.base_url)

    try:
        _ = client.health()
    except Exception as exc:
        print(f"Failed to reach simulator at {args.base_url}: {exc}", file=sys.stderr)
        return 2

    parsed_agents = parse_agents_arg(args.agents)
    if parsed_agents is None:
        agent_ids = discover_agent_ids(client)
        print(
            f"Auto-detected agents: {agent_ids}",
            file=sys.stderr,
        )
    else:
        agent_ids = parsed_agents

    if not agent_ids:
        print("No agents resolved — refusing to start.", file=sys.stderr)
        return 5

    if args.mock_ros2:
        return run_mock_bridge(
            client=client,
            agent_ids=agent_ids,
            rate_hz=args.rate_hz,
            reset_on_start=args.reset_on_start,
            vehicle_id=args.vehicle_id,
            track_id=args.track_id,
            steps=args.mock_steps,
        )

    if _ROS2_IMPORT_ERROR is not None:
        print(
            f"ROS2 bridge dependency missing: {_ROS2_IMPORT_ERROR}. "
            "Install ROS2 Python environment "
            "(rclpy, geometry_msgs, nav_msgs, sensor_msgs, std_msgs).",
            file=sys.stderr,
        )
        return 3

    rclpy.init(args=None)
    node = MultiAgentRos2Bridge(
        client=client,
        agent_ids=agent_ids,
        topic_prefix=args.topic_prefix,
        rate_hz=args.rate_hz,
        reset_on_start=args.reset_on_start,
        vehicle_id=args.vehicle_id,
        track_id=args.track_id,
    )
    try:
        rclpy.spin(node)
    except KeyboardInterrupt:
        pass
    finally:
        node.destroy_node()
        rclpy.shutdown()

    return 0


if __name__ == "__main__":
    raise SystemExit(main())
