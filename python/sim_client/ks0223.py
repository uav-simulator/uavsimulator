from __future__ import annotations

from dataclasses import dataclass
from typing import Any, Dict, Iterable, Mapping


def _kv(key: str, value: float) -> Dict[str, str]:
    return {"key": key, "value": f"{value:.6f}"}


@dataclass(frozen=True)
class Ks0223Command:
    left_pwm_norm: float
    right_pwm_norm: float
    brake: float = 0.0
    timestamp: int = 0
    time_base: str = "unix_ms"

    def to_step_command(self) -> Dict[str, Any]:
        left = max(-1.0, min(1.0, float(self.left_pwm_norm)))
        right = max(-1.0, min(1.0, float(self.right_pwm_norm)))
        brake = max(0.0, min(1.0, float(self.brake)))

        return {
            "throttle": 0.0,
            "steer": 0.0,
            "brake": brake,
            "timestamp": int(self.timestamp),
            "timeBase": self.time_base,
            "extensions": [
                _kv("drive.left_pwm_norm", left),
                _kv("drive.right_pwm_norm", right),
            ],
        }


def parse_telemetry(step_result: Mapping[str, Any]) -> Dict[str, str]:
    state = step_result.get("state") if isinstance(step_result, Mapping) else None
    telemetry = state.get("telemetry") if isinstance(state, Mapping) else None
    if not isinstance(telemetry, Iterable):
        return {}

    parsed: Dict[str, str] = {}
    for item in telemetry:
        if not isinstance(item, Mapping):
            continue
        key = item.get("key")
        value = item.get("value")
        if isinstance(key, str) and isinstance(value, str):
            parsed[key] = value

    return parsed
