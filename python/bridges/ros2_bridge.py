#!/usr/bin/env python3
from __future__ import annotations

import argparse
import base64
import json
import os
import sys
import time
from pathlib import Path
from typing import Any, Dict, Mapping

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

_ROS2_IMPORT_ERROR: Exception | None = None
try:
    import rclpy
    from rclpy.node import Node
    from sensor_msgs.msg import CompressedImage
    from std_msgs.msg import Float32MultiArray
    from std_msgs.msg import String
except ModuleNotFoundError as exc:
    _ROS2_IMPORT_ERROR = exc
    rclpy = None  # type: ignore[assignment]
    Node = object  # type: ignore[assignment,misc]
    CompressedImage = object  # type: ignore[assignment,misc]
    Float32MultiArray = object  # type: ignore[assignment,misc]
    String = object  # type: ignore[assignment,misc]


def _now_ms() -> int:
    return int(time.time() * 1000)


def _safe_ns(value: str) -> str:
    ns = (value or "/uavsim/ks0223").strip()
    if not ns.startswith("/"):
        ns = "/" + ns
    return ns.rstrip("/")


class UavSimRos2Bridge(Node):
    def __init__(
        self,
        client: SimClient,
        ros_namespace: str,
        rate_hz: float,
        vehicle_id: str,
        track_id: str,
        reset_on_start: bool,
    ) -> None:
        super().__init__("uavsim_ros2_bridge")
        self._client = client
        self._ns = _safe_ns(ros_namespace)
        self._left_pwm = 0.0
        self._right_pwm = 0.0
        self._brake = 0.0

        self._state_pub = self.create_publisher(String, f"{self._ns}/state_json", 10)
        self._telemetry_pub = self.create_publisher(String, f"{self._ns}/telemetry_json", 10)
        self._camera_pub = self.create_publisher(
            CompressedImage, f"{self._ns}/camera/front/image_raw/compressed", 3
        )
        self.create_subscription(Float32MultiArray, f"{self._ns}/cmd_drive", self._on_cmd_drive_array, 10)
        self.create_subscription(String, f"{self._ns}/cmd_drive_json", self._on_cmd_drive_json, 10)

        if reset_on_start:
            self._reset(vehicle_id=vehicle_id, track_id=track_id)

        timer_period_sec = 1.0 / max(1.0, rate_hz)
        self.create_timer(timer_period_sec, self._tick)
        self.get_logger().info(
            f"ROS2 bridge started: ns={self._ns}, base_url={self._client.base_url}, rate_hz={rate_hz:0.2f}"
        )

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

    def _on_cmd_drive_array(self, msg: Float32MultiArray) -> None:
        if len(msg.data) >= 2:
            self._left_pwm = float(max(-1.0, min(1.0, msg.data[0])))
            self._right_pwm = float(max(-1.0, min(1.0, msg.data[1])))
        if len(msg.data) >= 3:
            self._brake = float(max(0.0, min(1.0, msg.data[2])))

    def _on_cmd_drive_json(self, msg: String) -> None:
        try:
            payload = json.loads(msg.data)
        except json.JSONDecodeError:
            self.get_logger().warning("Invalid JSON in cmd_drive_json topic.")
            return

        if not isinstance(payload, Mapping):
            return

        left = payload.get("left_pwm_norm", self._left_pwm)
        right = payload.get("right_pwm_norm", self._right_pwm)
        brake = payload.get("brake", self._brake)
        self._left_pwm = float(max(-1.0, min(1.0, float(left))))
        self._right_pwm = float(max(-1.0, min(1.0, float(right))))
        self._brake = float(max(0.0, min(1.0, float(brake))))

    def _tick(self) -> None:
        try:
            cmd = Ks0223Command(
                left_pwm_norm=self._left_pwm,
                right_pwm_norm=self._right_pwm,
                brake=self._brake,
                timestamp=_now_ms(),
                time_base="unix_ms",
            ).to_step_command()
            step_result = self._client.step(cmd)
            self._publish_state(step_result)
            self._publish_telemetry(step_result)
            self._publish_camera(step_result)
        except Exception as exc:
            self.get_logger().warning(f"Bridge tick failed: {exc}")

    def _publish_state(self, step_result: Mapping[str, Any]) -> None:
        msg = String()
        msg.data = json.dumps(step_result.get("state", {}), ensure_ascii=False)
        self._state_pub.publish(msg)

    def _publish_telemetry(self, step_result: Mapping[str, Any]) -> None:
        telemetry = parse_telemetry(step_result)
        msg = String()
        msg.data = json.dumps(telemetry, ensure_ascii=False)
        self._telemetry_pub.publish(msg)

    def _publish_camera(self, step_result: Mapping[str, Any]) -> None:
        frame = step_result.get("frame")
        if not isinstance(frame, Mapping):
            return

        encoding = str(frame.get("encoding", "")).lower()
        data_base64 = frame.get("dataBase64")
        if encoding != "base64" or not isinstance(data_base64, str) or not data_base64:
            return

        try:
            image_bytes = base64.b64decode(data_base64)
        except Exception:
            return

        cam_msg = CompressedImage()
        cam_msg.header.stamp = self.get_clock().now().to_msg()
        cam_msg.header.frame_id = str(frame.get("frameId", "camera/front/image_raw"))
        cam_msg.format = str(frame.get("format", "jpeg"))
        cam_msg.data = image_bytes
        self._camera_pub.publish(cam_msg)


def parse_args() -> argparse.Namespace:
    parser = argparse.ArgumentParser(description="ROS2 bridge for UAV simulator HTTP API.")
    parser.add_argument("--base-url", default=os.getenv("UAVSIM_BASE_URL", "http://127.0.0.1:8000"))
    parser.add_argument("--namespace", default=os.getenv("UAVSIM_ROS_NAMESPACE", "/uavsim/ks0223"))
    parser.add_argument("--rate-hz", type=float, default=float(os.getenv("UAVSIM_ROS_RATE_HZ", "15.0")))
    parser.add_argument("--vehicle-id", default=os.getenv("UAVSIM_VEHICLE_ID", ""))
    parser.add_argument("--track-id", default=os.getenv("UAVSIM_TRACK_ID", ""))
    parser.add_argument("--reset-on-start", action="store_true")
    parser.add_argument("--mock-ros2", action="store_true", help="Run bridge loop without ROS2 and print payloads.")
    parser.add_argument("--mock-steps", type=int, default=25)
    return parser.parse_args()


def run_mock_bridge(
    client: SimClient,
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
        phase = i % 12
        if phase in (4, 5):
            left, right = 0.45, 0.75
        elif phase in (9, 10):
            left, right = 0.75, 0.45
        else:
            left, right = 0.65, 0.65

        cmd = Ks0223Command(
            left_pwm_norm=left,
            right_pwm_norm=right,
            brake=0.0,
            timestamp=_now_ms(),
            time_base="unix_ms",
        ).to_step_command()
        step_result = client.step(cmd)
        telemetry = parse_telemetry(step_result)
        frame = step_result.get("frame") if isinstance(step_result, Mapping) else None
        frame_len = 0
        if isinstance(frame, Mapping):
            data_base64 = frame.get("dataBase64")
            if isinstance(data_base64, str):
                frame_len = len(data_base64)

        print(
            json.dumps(
                {
                    "tick": i,
                    "publish.state_json": bool(step_result.get("state")),
                    "publish.telemetry_json.keys": sorted(list(telemetry.keys()))[:6],
                    "publish.camera.frame_base64_len": frame_len,
                },
                ensure_ascii=False,
            )
        )
        time.sleep(period)

    return 0


def main() -> int:
    args = parse_args()

    if _SDK_IMPORT_ERROR is not None:
        print(
            f"Python SDK dependency missing: {_SDK_IMPORT_ERROR}. Install dependencies from python/requirements.txt.",
            file=sys.stderr,
        )
        return 4

    client = SimClient(base_url=args.base_url)

    try:
        _ = client.health()
    except Exception as exc:
        print(f"Failed to reach simulator at {args.base_url}: {exc}", file=sys.stderr)
        return 2

    if args.mock_ros2:
        return run_mock_bridge(
            client=client,
            rate_hz=args.rate_hz,
            reset_on_start=args.reset_on_start,
            vehicle_id=args.vehicle_id,
            track_id=args.track_id,
            steps=args.mock_steps,
        )

    if _ROS2_IMPORT_ERROR is not None:
        print(
            f"ROS2 bridge dependency missing: {_ROS2_IMPORT_ERROR}. Install ROS2 Python environment (rclpy, std_msgs, sensor_msgs).",
            file=sys.stderr,
        )
        return 3

    rclpy.init(args=None)
    node = UavSimRos2Bridge(
        client=client,
        ros_namespace=args.namespace,
        rate_hz=args.rate_hz,
        vehicle_id=args.vehicle_id,
        track_id=args.track_id,
        reset_on_start=args.reset_on_start,
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
