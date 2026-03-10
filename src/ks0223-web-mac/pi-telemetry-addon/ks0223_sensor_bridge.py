#!/usr/bin/env python3
"""
KS0223 sensor telemetry bridge.
Publishes local GPIO sensor state as HTTP JSON on Raspberry Pi.

No third-party packages required beyond RPi.GPIO.
"""

from __future__ import annotations

import argparse
import json
import signal
import socket
import threading
import time
from dataclasses import dataclass, field
from datetime import datetime, timezone
from http.server import BaseHTTPRequestHandler, ThreadingHTTPServer
from typing import Any

import RPi.GPIO as GPIO

VERSION = "1.1.0"

# Pins from Keyestudio sample scripts.
ULTRASONIC_TRIG_PIN = 14
ULTRASONIC_ECHO_PIN = 4
ULTRASONIC_SERVO_PIN = 5
VALID_ULTRASONIC_SERVO_PINS = {5, 6, 7}

TRACK_LEFT_PIN = 17
TRACK_CENTER_PIN = 18
TRACK_RIGHT_PIN = 19

IR_PIN = 15

LED_SCLK_PIN = 8
LED_DIO_PIN = 9


@dataclass
class RuntimeState:
    started_at: float = field(default_factory=time.monotonic)
    seq: int = 0
    ultrasonic_distance_cm: float | None = None
    ultrasonic_scan_left_cm: float | None = None
    ultrasonic_scan_center_cm: float | None = None
    ultrasonic_scan_right_cm: float | None = None
    ultrasonic_servo_angle_deg: int = 90
    ultrasonic_manual_angle_deg: int = 90
    ultrasonic_servo_pin: int = ULTRASONIC_SERVO_PIN
    auto_scan_enabled: bool = True
    tracking_left: int = 0
    tracking_center: int = 0
    tracking_right: int = 0
    ir_last_code_dec: int | None = None
    ir_last_code_hex: str | None = None
    ir_last_seen_at: str | None = None
    ir_signal_level: int = 1
    sample_interval_ms: int = 150
    scan_interval_sec: float = 2.0
    scan_settle_ms: int = 90
    drive_speed_percent: int = 80
    camera_speed_percent: int = 70
    led_mode: str = "pattern"
    led_pattern: str = "smile"
    led_frame_hex: str = ""
    led_last_update_at: str | None = None
    lock: threading.Lock = field(default_factory=threading.Lock)
    servo_lock: threading.Lock = field(default_factory=threading.Lock)

    def snapshot(self) -> dict[str, Any]:
        with self.lock:
            now = datetime.now(timezone.utc).isoformat()
            uptime_sec = round(time.monotonic() - self.started_at, 3)
            return {
                "timestamp": now,
                "ultrasonic": {
                    "distance_cm": self.ultrasonic_distance_cm,
                    "scan": {
                        "left_cm": self.ultrasonic_scan_left_cm,
                        "center_cm": self.ultrasonic_scan_center_cm,
                        "right_cm": self.ultrasonic_scan_right_cm,
                    },
                    "scan_servo_angle_deg": self.ultrasonic_servo_angle_deg,
                    "manual_angle_deg": self.ultrasonic_manual_angle_deg,
                },
                "tracking": {
                    "left": self.tracking_left,
                    "center": self.tracking_center,
                    "right": self.tracking_right,
                },
                "ir": {
                    "last_code_dec": self.ir_last_code_dec,
                    "last_code_hex": self.ir_last_code_hex,
                    "last_seen_at": self.ir_last_seen_at,
                    "signal_level": self.ir_signal_level,
                },
                "system": {
                    "hostname": socket.gethostname(),
                    "cpu_temp_c": read_cpu_temp_c(),
                    "uptime_sec": uptime_sec,
                },
                "config": {
                    "auto_scan_enabled": self.auto_scan_enabled,
                    "sample_interval_ms": self.sample_interval_ms,
                    "scan_interval_sec": self.scan_interval_sec,
                    "scan_settle_ms": self.scan_settle_ms,
                    "drive_speed_percent": self.drive_speed_percent,
                    "camera_speed_percent": self.camera_speed_percent,
                    "ultrasonic_servo_pin": self.ultrasonic_servo_pin,
                },
                "led": {
                    "mode": self.led_mode,
                    "pattern": self.led_pattern,
                    "frame_hex": self.led_frame_hex,
                    "last_update_at": self.led_last_update_at,
                },
                "meta": {
                    "seq": self.seq,
                    "version": VERSION,
                },
            }


class LedMatrixController:
    smile = (0x00, 0x00, 0x38, 0x40, 0x40, 0x40, 0x3A, 0x02, 0x02, 0x3A, 0x40, 0x40, 0x40, 0x38, 0x00, 0x00)
    matrix_forward = (0x00, 0x00, 0x00, 0x00, 0x12, 0x24, 0x48, 0x90, 0x90, 0x48, 0x24, 0x12, 0x00, 0x00, 0x00, 0x00)
    matrix_back = (0x00, 0x00, 0x00, 0x00, 0x48, 0x24, 0x12, 0x09, 0x09, 0x12, 0x24, 0x48, 0x00, 0x00, 0x00, 0x00)
    matrix_left = (0x00, 0x00, 0x00, 0x00, 0x18, 0x24, 0x42, 0x99, 0x24, 0x42, 0x81, 0x00, 0x00, 0x00, 0x00, 0x00)
    matrix_right = (0x00, 0x00, 0x00, 0x00, 0x00, 0x81, 0x42, 0x24, 0x99, 0x42, 0x24, 0x18, 0x00, 0x00, 0x00, 0x00)
    matrix_stop = (0x00, 0x00, 0x3C, 0x42, 0x81, 0xA1, 0x91, 0x89, 0x89, 0x91, 0xA1, 0x81, 0x42, 0x3C, 0x00, 0x00)
    matrix_heart = (0x00, 0x00, 0x0C, 0x1E, 0x3F, 0x7E, 0xFC, 0xF8, 0xF8, 0xFC, 0x7E, 0x3F, 0x1E, 0x0C, 0x00, 0x00)
    matrix_clear = tuple([0x00] * 16)

    PATTERNS = {
        "smile": smile,
        "forward": matrix_forward,
        "back": matrix_back,
        "left": matrix_left,
        "right": matrix_right,
        "stop": matrix_stop,
        "heart": matrix_heart,
        "clear": matrix_clear,
    }

    def __init__(self, sclk_pin: int, dio_pin: int):
        self.sclk_pin = sclk_pin
        self.dio_pin = dio_pin
        self.lock = threading.Lock()
        GPIO.setup(self.sclk_pin, GPIO.OUT)
        GPIO.setup(self.dio_pin, GPIO.OUT)

    @staticmethod
    def _nop() -> None:
        time.sleep(0.00003)

    def _start(self) -> None:
        GPIO.output(self.sclk_pin, 0)
        self._nop()
        GPIO.output(self.sclk_pin, 1)
        self._nop()
        GPIO.output(self.dio_pin, 1)
        self._nop()
        GPIO.output(self.dio_pin, 0)
        self._nop()

    def _send_data(self, value: int) -> None:
        data = value & 0xFF
        for _ in range(8):
            GPIO.output(self.sclk_pin, 0)
            self._nop()
            GPIO.output(self.dio_pin, 1 if data & 0x01 else 0)
            self._nop()
            GPIO.output(self.sclk_pin, 1)
            self._nop()
            data >>= 1
            GPIO.output(self.sclk_pin, 0)

    def _end(self) -> None:
        GPIO.output(self.sclk_pin, 0)
        self._nop()
        GPIO.output(self.dio_pin, 0)
        self._nop()
        GPIO.output(self.sclk_pin, 1)
        self._nop()
        GPIO.output(self.dio_pin, 1)
        self._nop()

    def display(self, frame: tuple[int, ...]) -> None:
        if len(frame) != 16:
            raise ValueError("frame must contain exactly 16 bytes")

        with self.lock:
            self._start()
            self._send_data(0xC0)
            for value in frame:
                self._send_data(value)
            self._end()

            self._start()
            self._send_data(0x8A)
            self._end()

    def set_pattern(self, pattern: str) -> tuple[int, ...]:
        key = pattern.strip().lower()
        if key not in self.PATTERNS:
            raise ValueError(f"Unknown pattern: {pattern}")

        frame = self.PATTERNS[key]
        self.display(frame)
        return frame

    def set_custom_hex(self, frame_hex: str) -> tuple[int, ...]:
        frame = parse_frame_hex(frame_hex)
        self.display(frame)
        return frame

    def clear(self) -> tuple[int, ...]:
        frame = self.PATTERNS["clear"]
        self.display(frame)
        return frame


def clamp_angle(value: int) -> int:
    return max(0, min(180, int(value)))


def clamp_percent(value: int) -> int:
    return max(0, min(100, int(value)))


def clamp_servo_pin(value: int) -> int:
    pin = int(value)
    return pin if pin in VALID_ULTRASONIC_SERVO_PINS else ULTRASONIC_SERVO_PIN


def parse_bool(value: Any, default: bool = False) -> bool:
    if value is None:
        return default

    if isinstance(value, bool):
        return value

    if isinstance(value, (int, float)):
        return value != 0

    text = str(value).strip().lower()
    if text in ("1", "true", "yes", "on", "enable", "enabled"):
        return True
    if text in ("0", "false", "no", "off", "disable", "disabled"):
        return False

    return default


def read_cpu_temp_c() -> float | None:
    path = "/sys/class/thermal/thermal_zone0/temp"
    try:
        with open(path, "r", encoding="utf-8") as handle:
            raw = handle.read().strip()
        return round(int(raw) / 1000.0, 2)
    except Exception:
        return None


def setup_gpio() -> None:
    GPIO.setwarnings(False)
    GPIO.setmode(GPIO.BCM)

    GPIO.setup(ULTRASONIC_TRIG_PIN, GPIO.OUT)
    GPIO.setup(ULTRASONIC_ECHO_PIN, GPIO.IN)
    GPIO.output(ULTRASONIC_TRIG_PIN, GPIO.LOW)

    for pin in VALID_ULTRASONIC_SERVO_PINS:
        GPIO.setup(pin, GPIO.OUT)
        GPIO.output(pin, GPIO.LOW)

    GPIO.setup(TRACK_LEFT_PIN, GPIO.IN)
    GPIO.setup(TRACK_CENTER_PIN, GPIO.IN)
    GPIO.setup(TRACK_RIGHT_PIN, GPIO.IN)

    GPIO.setup(IR_PIN, GPIO.IN, pull_up_down=GPIO.PUD_UP)


def servo_pulse(pin: int, angle: int) -> None:
    pulse_width_us = (angle * 11) + 500
    GPIO.output(pin, GPIO.HIGH)
    time.sleep(pulse_width_us / 1_000_000.0)
    GPIO.output(pin, GPIO.LOW)
    time.sleep(max(0.0, (20.0 / 1000.0) - (pulse_width_us / 1_000_000.0)))


def set_ultrasonic_servo(angle: int, state: RuntimeState, pulses: int = 50) -> None:
    bounded = clamp_angle(angle)
    with state.servo_lock:
        with state.lock:
            servo_pin = clamp_servo_pin(state.ultrasonic_servo_pin)
        for _ in range(max(1, pulses)):
            servo_pulse(servo_pin, bounded)
        with state.lock:
            state.ultrasonic_servo_angle_deg = bounded


def move_ultrasonic_servo_to(target_angle: int, state: RuntimeState) -> None:
    target = clamp_angle(target_angle)
    # Follow Keyestudio sample style: apply many pulses to lock servo into target angle.
    set_ultrasonic_servo(target, state, pulses=50)


def measure_distance_cm(timeout_sec: float = 0.03) -> float | None:
    GPIO.output(ULTRASONIC_TRIG_PIN, GPIO.LOW)
    time.sleep(0.0002)
    GPIO.output(ULTRASONIC_TRIG_PIN, GPIO.HIGH)
    time.sleep(0.00001)
    GPIO.output(ULTRASONIC_TRIG_PIN, GPIO.LOW)

    start_wait_deadline = time.monotonic() + timeout_sec
    while GPIO.input(ULTRASONIC_ECHO_PIN) == 0:
        if time.monotonic() > start_wait_deadline:
            return None

    pulse_start = time.monotonic()
    pulse_end_deadline = pulse_start + timeout_sec
    while GPIO.input(ULTRASONIC_ECHO_PIN) == 1:
        if time.monotonic() > pulse_end_deadline:
            return None

    pulse_end = time.monotonic()
    distance_cm = ((pulse_end - pulse_start) * 34300.0) / 2.0
    if distance_cm <= 0 or distance_cm > 450:
        return None
    return round(distance_cm, 2)


def read_tracking_values() -> tuple[int, int, int]:
    return (
        int(GPIO.input(TRACK_LEFT_PIN)),
        int(GPIO.input(TRACK_CENTER_PIN)),
        int(GPIO.input(TRACK_RIGHT_PIN)),
    )


def read_ir_code_once() -> int | None:
    if GPIO.input(IR_PIN) == 1:
        return None

    count = 0
    while GPIO.input(IR_PIN) == 0 and count < 200:
        count += 1
        time.sleep(0.00006)
    if count >= 200:
        return None

    count = 0
    while GPIO.input(IR_PIN) == 1 and count < 80:
        count += 1
        time.sleep(0.00006)
    if count >= 80:
        return None

    data = [0, 0, 0, 0]
    idx = 0
    bit_idx = 0

    for _ in range(32):
        count = 0
        while GPIO.input(IR_PIN) == 0 and count < 15:
            count += 1
            time.sleep(0.00006)
        if count >= 15:
            return None

        count = 0
        while GPIO.input(IR_PIN) == 1 and count < 40:
            count += 1
            time.sleep(0.00006)

        if count > 8:
            data[idx] |= 1 << bit_idx

        if bit_idx == 7:
            bit_idx = 0
            idx += 1
            if idx >= 4:
                break
        else:
            bit_idx += 1

    if data[0] + data[1] == 0xFF and data[2] + data[3] == 0xFF:
        return data[2]

    return None


def parse_frame_hex(frame_hex: str) -> tuple[int, ...]:
    cleaned = frame_hex.replace(",", " ").replace(";", " ").replace("|", " ")
    parts = [part for part in cleaned.split() if part]

    if len(parts) == 1 and len(parts[0]) == 32:
        parts = [parts[0][i : i + 2] for i in range(0, 32, 2)]

    if len(parts) != 16:
        raise ValueError("frame_hex must contain 16 bytes")

    values: list[int] = []
    for part in parts:
        p = part.lower()
        if p.startswith("0x"):
            p = p[2:]
        value = int(p, 16)
        if value < 0 or value > 255:
            raise ValueError("byte out of range")
        values.append(value)

    return tuple(values)


def frame_to_hex(frame: tuple[int, ...]) -> str:
    return " ".join(f"{value:02X}" for value in frame)


def pick_value(data: dict[str, Any], *keys: str, default: Any = None) -> Any:
    for key in keys:
        if key in data:
            return data[key]

    lowered = {key.lower(): value for key, value in data.items()}
    for key in keys:
        value = lowered.get(key.lower())
        if value is not None:
            return value

    return default


def sampling_loop(state: RuntimeState, stop_event: threading.Event) -> None:
    set_ultrasonic_servo(90, state)

    next_scan_at = time.monotonic() + state.scan_interval_sec

    while not stop_event.is_set():
        now = time.monotonic()

        with state.lock:
            sample_interval_ms = max(50, state.sample_interval_ms)
            scan_interval_sec = max(0.5, state.scan_interval_sec)
            scan_settle_sec = max(0.02, state.scan_settle_ms / 1000.0)
            auto_scan_enabled = state.auto_scan_enabled
            manual_angle = state.ultrasonic_manual_angle_deg

        distance = measure_distance_cm()
        track_left, track_center, track_right = read_tracking_values()
        ir_code = read_ir_code_once()
        ir_signal = int(GPIO.input(IR_PIN))

        if auto_scan_enabled and now >= next_scan_at:
            scan_left = None
            scan_center = None
            scan_right = None

            move_ultrasonic_servo_to(180, state)
            time.sleep(scan_settle_sec)
            scan_left = measure_distance_cm()

            move_ultrasonic_servo_to(90, state)
            time.sleep(scan_settle_sec)
            scan_center = measure_distance_cm()

            move_ultrasonic_servo_to(0, state)
            time.sleep(scan_settle_sec)
            scan_right = measure_distance_cm()

            move_ultrasonic_servo_to(90, state)

            with state.lock:
                state.ultrasonic_scan_left_cm = scan_left
                state.ultrasonic_scan_center_cm = scan_center
                state.ultrasonic_scan_right_cm = scan_right

            next_scan_at = now + scan_interval_sec
        elif not auto_scan_enabled:
            with state.lock:
                current_angle = state.ultrasonic_servo_angle_deg
            if current_angle != manual_angle:
                move_ultrasonic_servo_to(manual_angle, state)

        with state.lock:
            state.seq += 1
            state.ultrasonic_distance_cm = distance
            state.tracking_left = track_left
            state.tracking_center = track_center
            state.tracking_right = track_right
            state.ir_signal_level = ir_signal

            if ir_code is not None:
                state.ir_last_code_dec = ir_code
                state.ir_last_code_hex = f"0x{ir_code:02x}"
                state.ir_last_seen_at = datetime.now(timezone.utc).isoformat()

        stop_event.wait(sample_interval_ms / 1000.0)


class Handler(BaseHTTPRequestHandler):
    runtime_state: RuntimeState
    led_controller: LedMatrixController

    def _send_json(self, payload: dict[str, Any], status: int = 200) -> None:
        body = json.dumps(payload, ensure_ascii=False).encode("utf-8")
        self.send_response(status)
        self.send_header("Content-Type", "application/json; charset=utf-8")
        self.send_header("Content-Length", str(len(body)))
        self.send_header("Cache-Control", "no-store")
        self.send_header("Access-Control-Allow-Origin", "*")
        self.send_header("Access-Control-Allow-Headers", "Content-Type")
        self.send_header("Access-Control-Allow-Methods", "GET,POST,OPTIONS")
        self.end_headers()
        self.wfile.write(body)

    def do_OPTIONS(self) -> None:  # noqa: N802
        self.send_response(204)
        self.send_header("Access-Control-Allow-Origin", "*")
        self.send_header("Access-Control-Allow-Headers", "Content-Type")
        self.send_header("Access-Control-Allow-Methods", "GET,POST,OPTIONS")
        self.end_headers()

    def do_GET(self) -> None:  # noqa: N802
        if self.path in ("/", "/api/telemetry"):
            self._send_json(self.runtime_state.snapshot())
            return

        if self.path == "/healthz":
            snap = self.runtime_state.snapshot()
            self._send_json(
                {
                    "status": "ok",
                    "timestamp": snap["timestamp"],
                    "version": VERSION,
                    "seq": snap["meta"]["seq"],
                    "hostname": snap["system"]["hostname"],
                }
            )
            return

        self._send_json({"error": "not found"}, status=404)

    def do_POST(self) -> None:  # noqa: N802
        data = self._read_json_body()
        if data is None:
            self._send_json({"error": "invalid json body"}, status=400)
            return

        try:
            if self.path == "/api/config":
                self._handle_config(data)
                self._send_json({"ok": True, "config": self.runtime_state.snapshot().get("config", {})})
                return

            if self.path == "/api/ultrasonic/position":
                angle = int(pick_value(data, "angle_deg", "angleDeg", default=90))
                disable_auto_scan = parse_bool(pick_value(data, "disable_auto_scan", "disableAutoScan", default=True), default=True)
                servo_pin = pick_value(data, "servo_pin", "servoPin", "ultrasonic_servo_pin", "ultrasonicServoPin")
                if disable_auto_scan:
                    with self.runtime_state.lock:
                        self.runtime_state.auto_scan_enabled = False
                with self.runtime_state.lock:
                    if servo_pin is not None:
                        self.runtime_state.ultrasonic_servo_pin = clamp_servo_pin(int(servo_pin))
                    self.runtime_state.ultrasonic_manual_angle_deg = clamp_angle(angle)
                    target_angle = self.runtime_state.ultrasonic_manual_angle_deg
                move_ultrasonic_servo_to(target_angle, self.runtime_state)
                with self.runtime_state.lock:
                    current_pin = self.runtime_state.ultrasonic_servo_pin
                    current_auto_scan = self.runtime_state.auto_scan_enabled
                self._send_json({"ok": True, "angle_deg": target_angle, "auto_scan_enabled": current_auto_scan, "servo_pin": current_pin})
                return

            if self.path == "/api/ultrasonic/auto-scan":
                enabled = parse_bool(pick_value(data, "enabled", default=True), default=True)
                with self.runtime_state.lock:
                    self.runtime_state.auto_scan_enabled = enabled
                self._send_json({"ok": True, "auto_scan_enabled": enabled})
                return

            if self.path == "/api/led/pattern":
                pattern = str(pick_value(data, "pattern", "Pattern", default="smile")).strip().lower()
                frame = self.led_controller.set_pattern(pattern)
                now = datetime.now(timezone.utc).isoformat()
                with self.runtime_state.lock:
                    self.runtime_state.led_mode = "pattern"
                    self.runtime_state.led_pattern = pattern
                    self.runtime_state.led_frame_hex = frame_to_hex(frame)
                    self.runtime_state.led_last_update_at = now
                self._send_json({"ok": True, "pattern": pattern, "frame_hex": frame_to_hex(frame)})
                return

            if self.path == "/api/led/custom":
                frame_hex = str(pick_value(data, "frame_hex", "frameHex", default=""))
                frame = self.led_controller.set_custom_hex(frame_hex)
                now = datetime.now(timezone.utc).isoformat()
                with self.runtime_state.lock:
                    self.runtime_state.led_mode = "custom"
                    self.runtime_state.led_pattern = "custom"
                    self.runtime_state.led_frame_hex = frame_to_hex(frame)
                    self.runtime_state.led_last_update_at = now
                self._send_json({"ok": True, "frame_hex": frame_to_hex(frame)})
                return

            if self.path == "/api/led/clear":
                frame = self.led_controller.clear()
                now = datetime.now(timezone.utc).isoformat()
                with self.runtime_state.lock:
                    self.runtime_state.led_mode = "pattern"
                    self.runtime_state.led_pattern = "clear"
                    self.runtime_state.led_frame_hex = frame_to_hex(frame)
                    self.runtime_state.led_last_update_at = now
                self._send_json({"ok": True, "frame_hex": frame_to_hex(frame)})
                return

            self._send_json({"error": "not found"}, status=404)
        except Exception as exc:
            self._send_json({"error": str(exc)}, status=400)

    def _read_json_body(self) -> dict[str, Any] | None:
        try:
            content_length = int(self.headers.get("Content-Length", "0"))
        except ValueError:
            return None

        body = self.rfile.read(content_length) if content_length > 0 else b"{}"
        try:
            decoded = json.loads(body.decode("utf-8"))
            return decoded if isinstance(decoded, dict) else None
        except Exception:
            return None

    def _handle_config(self, data: dict[str, Any]) -> None:
        with self.runtime_state.lock:
            auto_scan_enabled = pick_value(data, "auto_scan_enabled", "autoScanEnabled")
            sample_interval_ms = pick_value(data, "sample_interval_ms", "sampleIntervalMs")
            scan_interval_sec = pick_value(data, "scan_interval_sec", "scanIntervalSec")
            scan_settle_ms = pick_value(data, "scan_settle_ms", "scanSettleMs")
            drive_speed_percent = pick_value(data, "drive_speed_percent", "driveSpeedPercent")
            camera_speed_percent = pick_value(data, "camera_speed_percent", "cameraSpeedPercent")
            ultrasonic_servo_pin = pick_value(data, "ultrasonic_servo_pin", "ultrasonicServoPin")

            if auto_scan_enabled is not None:
                self.runtime_state.auto_scan_enabled = parse_bool(auto_scan_enabled, default=self.runtime_state.auto_scan_enabled)

            if sample_interval_ms is not None:
                self.runtime_state.sample_interval_ms = max(50, int(sample_interval_ms))

            if scan_interval_sec is not None:
                self.runtime_state.scan_interval_sec = max(0.5, float(scan_interval_sec))

            if scan_settle_ms is not None:
                self.runtime_state.scan_settle_ms = max(20, int(scan_settle_ms))

            if drive_speed_percent is not None:
                self.runtime_state.drive_speed_percent = clamp_percent(int(drive_speed_percent))

            if camera_speed_percent is not None:
                self.runtime_state.camera_speed_percent = clamp_percent(int(camera_speed_percent))

            if ultrasonic_servo_pin is not None:
                self.runtime_state.ultrasonic_servo_pin = clamp_servo_pin(int(ultrasonic_servo_pin))

    def log_message(self, fmt: str, *args: Any) -> None:
        # Keep output quiet; service logs are enough.
        return


def create_handler(state: RuntimeState, led: LedMatrixController):
    class BoundHandler(Handler):
        runtime_state = state
        led_controller = led

    return BoundHandler


def parse_args() -> argparse.Namespace:
    parser = argparse.ArgumentParser(description="KS0223 sensor telemetry HTTP bridge")
    parser.add_argument("--host", default="0.0.0.0")
    parser.add_argument("--port", type=int, default=8765)
    parser.add_argument("--sample-interval-ms", type=int, default=150)
    parser.add_argument("--scan-interval-sec", type=float, default=2.0)
    parser.add_argument("--scan-settle-ms", type=int, default=90)
    return parser.parse_args()


def main() -> int:
    args = parse_args()
    if args.port < 1 or args.port > 65535:
        raise ValueError("port must be in range 1..65535")

    setup_gpio()

    led_controller = LedMatrixController(LED_SCLK_PIN, LED_DIO_PIN)

    state = RuntimeState(
        sample_interval_ms=max(50, int(args.sample_interval_ms)),
        scan_interval_sec=max(0.5, float(args.scan_interval_sec)),
        scan_settle_ms=max(20, int(args.scan_settle_ms)),
    )
    stop_event = threading.Event()

    # Default startup LED indicator.
    try:
        frame = led_controller.set_pattern("smile")
        with state.lock:
            state.led_mode = "pattern"
            state.led_pattern = "smile"
            state.led_frame_hex = frame_to_hex(frame)
            state.led_last_update_at = datetime.now(timezone.utc).isoformat()
    except Exception:
        pass

    sampler = threading.Thread(target=sampling_loop, args=(state, stop_event), daemon=True)
    sampler.start()

    server = ThreadingHTTPServer((args.host, args.port), create_handler(state, led_controller))

    def shutdown_handler(signum: int, frame: Any) -> None:
        del signum, frame
        stop_event.set()
        server.shutdown()

    signal.signal(signal.SIGTERM, shutdown_handler)
    signal.signal(signal.SIGINT, shutdown_handler)

    print(f"ks0223_sensor_bridge started on {args.host}:{args.port}")

    try:
        server.serve_forever(poll_interval=0.5)
    finally:
        stop_event.set()
        sampler.join(timeout=2.0)
        try:
            server.server_close()
        finally:
            GPIO.cleanup()

    return 0


if __name__ == "__main__":
    raise SystemExit(main())
