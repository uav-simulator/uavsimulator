"""Multi-agent VecEnv with camera frames — N agents in 1 Unity instance.

Cuts Unity-process duplication overhead vs N parallel SubprocVecEnv:
- One Unity engine instance, one physics simulation, one HTTP server
- N agents step independently via Unity multi-agent endpoint (targetAgentId)
- Each agent gets its own camera frame + ultrasonic per tick
- Episodes are independently terminated; SB3 auto-resets per agent

Drop-in replacement for SubprocVecEnv with N envs but uses 1/N of the Unity
processes. Same observation space as ab_corridor_vision_env.ABCorridorVisionEnv.
"""

from __future__ import annotations

import base64
import io
import math
import sys
from pathlib import Path
from typing import Any, Optional

import numpy as np
from gymnasium import spaces
from PIL import Image
from stable_baselines3.common.vec_env import VecEnv
from stable_baselines3.common.vec_env.base_vec_env import VecEnvObs, VecEnvStepReturn

ROOT = Path(__file__).resolve().parents[2]
PYTHON_ROOT = ROOT / "python"
if str(PYTHON_ROOT) not in sys.path:
    sys.path.insert(0, str(PYTHON_ROOT))

from sim_client.http_client import SimClient

IMG_SIZE = 84


def _route_length(waypoints: list[tuple[float, float]]) -> float:
    total = 0.0
    for i in range(len(waypoints) - 1):
        ax, az = waypoints[i]
        bx, bz = waypoints[i + 1]
        total += math.hypot(bx - ax, bz - az)
    return max(total, 1.0)


def _project_t(px: float, pz: float, ax: float, az: float, bx: float, bz: float) -> float:
    dx, dz = bx - ax, bz - az
    seg2 = dx * dx + dz * dz
    if seg2 <= 0.0:
        return 0.0
    return max(0.0, min(1.0, ((px - ax) * dx + (pz - az) * dz) / seg2))


def _seg_dist(px: float, pz: float, ax: float, az: float, bx: float, bz: float) -> float:
    t = _project_t(px, pz, ax, az, bx, bz)
    cx = ax + t * (bx - ax)
    cz = az + t * (bz - az)
    return math.hypot(px - cx, pz - cz)


def _route_progress(px: float, pz: float, waypoints: list[tuple[float, float]], total_length: float) -> float:
    best_t = 0.0
    best_dist = float("inf")
    cum = 0.0
    progress = 0.0
    for i in range(len(waypoints) - 1):
        ax, az = waypoints[i]
        bx, bz = waypoints[i + 1]
        seg_len = math.hypot(bx - ax, bz - az)
        d = _seg_dist(px, pz, ax, az, bx, bz)
        if d < best_dist:
            best_dist = d
            t = _project_t(px, pz, ax, az, bx, bz)
            progress = (cum + t * seg_len) / total_length
        cum += seg_len
    return max(0.0, min(1.0, progress))


def _nearest_dist(px: float, pz: float, waypoints: list[tuple[float, float]]) -> float:
    best = float("inf")
    for i in range(len(waypoints) - 1):
        ax, az = waypoints[i]
        bx, bz = waypoints[i + 1]
        d = _seg_dist(px, pz, ax, az, bx, bz)
        best = min(best, d)
    return best


def _extract_position(step: dict[str, Any]) -> tuple[float, float, float]:
    pos = ((step.get("state") or {}).get("pose") or {}).get("position") or {}
    return float(pos.get("x", 0.0)), float(pos.get("y", 0.0)), float(pos.get("z", 0.0))


def _extract_yaw(step: dict[str, Any]) -> float:
    rot = ((step.get("state") or {}).get("pose") or {}).get("rotation") or {}
    qx = float(rot.get("x", 0.0))
    qy = float(rot.get("y", 0.0))
    qz = float(rot.get("z", 0.0))
    qw = float(rot.get("w", 1.0))
    return math.atan2(2.0 * (qw * qy + qx * qz), 1.0 - 2.0 * (qy * qy + qz * qz))


def _telemetry_map(step: dict[str, Any]) -> dict[str, str]:
    out: dict[str, str] = {}
    for entry in (step.get("state") or {}).get("telemetry") or []:
        if isinstance(entry, dict):
            out[str(entry.get("key", ""))] = str(entry.get("value", ""))
    return out


def _parse_float(m: dict[str, str], *keys: str, default: float = 0.0) -> float:
    for key in keys:
        if key in m:
            try:
                return float(m[key])
            except (TypeError, ValueError):
                continue
    return default


class _AgentState:
    __slots__ = ("prev_progress", "stalled_steps", "step_count", "done",
                 "reached_waypoints", "prev_steer")

    def __init__(self) -> None:
        self.prev_progress = 0.0
        self.stalled_steps = 0
        self.step_count = 0
        self.done = False
        self.reached_waypoints: set[int] = set()
        self.prev_steer = 0.0


class MultiAgentVisionVecEnv(VecEnv):
    """N camera-equipped agents in one Unity instance, SB3 VecEnv interface."""

    def __init__(
        self,
        n_agents: int = 8,
        base_url: str = "http://127.0.0.1:8000",
        scenario_path: str | Path | None = None,
        max_steps: int = 400,
        time_scale: float = 3.0,
        img_size: int = IMG_SIZE,
        corridor_width_m: float = 0.60,
        oob_margin_m: float = 0.10,
        goal_radius_m: float = 0.25,
        waypoints: Optional[list[tuple[float, float]]] = None,
        track_id: str = "track.cardboard_corridor.v1",
        vehicle_id: str = "vehicle.ks0223.v1",
        real_cam_postprocess: bool = False,
    ) -> None:
        self.n_agents = n_agents
        self.client = SimClient(base_url, timeout_s=60.0)
        self.max_steps = max_steps
        self.time_scale = time_scale
        self.img_size = img_size
        self.corridor_width_m = corridor_width_m
        self.oob_threshold_m = corridor_width_m * 0.5 + oob_margin_m
        self.goal_radius_m = goal_radius_m
        self.track_id = track_id
        self.vehicle_id = vehicle_id

        if waypoints is not None:
            self.waypoints = waypoints
        else:
            # Fallback default — caller should pass scenario waypoints.
            self.waypoints = [(0.0, 0.0), (0.0, 1.0), (1.0, 1.0)]
        self.total_route_length = _route_length(self.waypoints)

        self.agent_ids = ["ego"] + [f"agent-{i + 1}" for i in range(1, n_agents)]

        obs_space = spaces.Dict({
            "image": spaces.Box(0, 255, shape=(img_size, img_size, 3), dtype=np.uint8),
            "ultrasonic": spaces.Box(0.0, 1.0, shape=(1,), dtype=np.float32),
        })
        act_space = spaces.Discrete(5)
        super().__init__(n_agents, obs_space, act_space)

        self._states = [_AgentState() for _ in range(n_agents)]
        self._step_count = 0
        self._pending_actions: Optional[np.ndarray] = None
        self._episode_seed = 0
        self._real_cam_postprocess = bool(real_cam_postprocess)

    def _build_reset_config(self) -> dict[str, Any]:
        flags = [
            {"key": "agents.isolated", "value": "true"},
            {"key": "agents.see_each_other", "value": "false"},
            {"key": "agents.collisions_enabled", "value": "false"},
        ]
        agents = []
        for i, agent_id in enumerate(self.agent_ids):
            entry = {
                "agentId": agent_id,
                "vehicleId": self.vehicle_id,
                "isPrimary": i == 0,
                "trackParams": [],
                # Each agent needs a camera since policy is vision-based;
                # primary keeps "high" profile, secondaries use default.
                "vehicleParams": [{"key": "camera.profile", "value": "high"}] if i == 0 else [],
                "flags": [],
            }
            agents.append(entry)
        return {
            "seed": self._episode_seed,
            "timeScale": self.time_scale,
            "selectedTrackId": self.track_id,
            "selectedVehicleId": self.vehicle_id,
            "trackParams": [],
            "vehicleParams": [],
            "flags": flags,
            "agents": agents,
        }

    def reset(self) -> VecEnvObs:
        self._episode_seed += 1
        config = self._build_reset_config()
        resp = self.client.reset(config)
        for state in self._states:
            state.prev_progress = 0.0
            state.stalled_steps = 0
            state.step_count = 0
            state.done = False
            state.reached_waypoints = set()
        self._step_count = 0

        # Per-agent first observations: Unity reset returns frame for primary
        # only; for non-primary, request a zero-action step with that agent
        # targeted to retrieve its frame + state.
        images = np.zeros((self.n_agents, self.img_size, self.img_size, 3), dtype=np.uint8)
        ultrasonics = np.zeros((self.n_agents, 1), dtype=np.float32)

        primary_obs = self._extract_obs(resp)
        images[0] = primary_obs["image"]
        ultrasonics[0] = primary_obs["ultrasonic"]

        for i in range(1, self.n_agents):
            agent_resp = self.client.step({
                "discreteAction": "DirStop",
                "throttle": 0.0,
                "steer": 0.0,
                "brake": 0.0,
                "targetAgentId": self.agent_ids[i],
                "timestamp": 0,
                "timeBase": "unix_ms",
                "extensions": [],
            })
            o = self._extract_obs(agent_resp)
            images[i] = o["image"]
            ultrasonics[i] = o["ultrasonic"]

        return {"image": images, "ultrasonic": ultrasonics}

    def step_async(self, actions: np.ndarray) -> None:
        self._pending_actions = actions

    def step_wait(self) -> VecEnvStepReturn:
        assert self._pending_actions is not None
        actions = self._pending_actions
        self._step_count += 1

        images = np.zeros((self.n_agents, self.img_size, self.img_size, 3), dtype=np.uint8)
        ultrasonics = np.zeros((self.n_agents, 1), dtype=np.float32)
        rewards = np.zeros(self.n_agents, dtype=np.float32)
        dones = np.zeros(self.n_agents, dtype=bool)
        infos: list[dict[str, Any]] = [{} for _ in range(self.n_agents)]

        for i, agent_id in enumerate(self.agent_ids):
            if self._states[i].done:
                dones[i] = True
                continue

            action_idx = int(actions[i])
            cmd = ["DirStop", "DirForward", "DirBack", "DirLeft", "DirRight"][action_idx]

            resp = self.client.step({
                "discreteAction": cmd,
                "targetAgentId": agent_id,
                "timestamp": 0,
                "timeBase": "unix_ms",
                "extensions": [],
            })

            obs = self._extract_obs(resp)
            images[i] = obs["image"]
            ultrasonics[i] = obs["ultrasonic"]

            px, _, pz = _extract_position(resp)
            yaw = _extract_yaw(resp)
            reward, terminated, term_reason, breakdown = self._compute_reward(
                i, px, pz, yaw, action_idx
            )
            rewards[i] = reward
            self._states[i].step_count += 1
            infos[i]["reward_breakdown"] = breakdown
            infos[i]["action_idx"] = int(action_idx)

            if terminated or self._states[i].step_count >= self.max_steps:
                self._states[i].done = True
                dones[i] = True
                infos[i]["terminal_observation"] = {
                    "image": images[i].copy(),
                    "ultrasonic": ultrasonics[i].copy(),
                }
                infos[i]["termination_reason"] = term_reason or "truncated"

        if all(s.done for s in self._states):
            fresh = self.reset()
            images = fresh["image"]
            ultrasonics = fresh["ultrasonic"]
            dones[:] = True

        return {"image": images, "ultrasonic": ultrasonics}, rewards, dones, infos

    def _compute_reward(
        self, agent_idx: int, px: float, pz: float, yaw: float, action_idx: int
    ) -> tuple[float, bool, str, dict[str, float]]:
        state = self._states[agent_idx]
        progress = _route_progress(px, pz, self.waypoints, self.total_route_length)
        delta = progress - state.prev_progress
        state.prev_progress = progress

        progress_reward = delta * 100.0
        lateral_dist = _nearest_dist(px, pz, self.waypoints)
        half_corridor = self.corridor_width_m * 0.5
        wall_proximity = lateral_dist / half_corridor if half_corridor > 0 else 0.0
        lateral_penalty = -1.0 * wall_proximity ** 2
        survival_bonus = 0.1
        time_penalty = -0.02
        backward_penalty = -0.5 if action_idx == 2 else 0.0  # DirBack

        waypoint_bonus = 0.0
        for wi in range(len(self.waypoints) - 1):  # last is goal, handled separately
            if wi in state.reached_waypoints:
                continue
            wx, wz = self.waypoints[wi]
            if math.hypot(px - wx, pz - wz) < max(self.goal_radius_m, 0.5):
                state.reached_waypoints.add(wi)
                waypoint_bonus += 5.0

        heading_bonus = 0.0
        next_wp = None
        for wi in range(len(self.waypoints)):
            if wi not in state.reached_waypoints:
                next_wp = self.waypoints[wi]
                break
        if next_wp is not None:
            dx, dz = next_wp[0] - px, next_wp[1] - pz
            d = math.hypot(dx, dz)
            if d > 1e-3:
                fx, fz = math.sin(yaw), math.cos(yaw)
                heading_bonus = (fx * dx + fz * dz) / d  # cos(angle), [-1, +1]

        goal_bonus = 0.0
        oob_penalty = 0.0
        stall_penalty = 0.0
        terminated = False
        term_reason = "running"

        gx, gz = self.waypoints[-1]
        if math.hypot(px - gx, pz - gz) < self.goal_radius_m:
            goal_bonus = 100.0
            terminated = True
            term_reason = "goal_reached"
        elif lateral_dist > self.oob_threshold_m:
            oob_penalty = -30.0
            terminated = True
            term_reason = "out_of_bounds"
        else:
            if abs(delta) < 1e-4 and state.step_count > 20:
                state.stalled_steps += 1
            else:
                state.stalled_steps = 0
            if state.stalled_steps >= 30:
                stall_penalty = -10.0
                terminated = True
                term_reason = "stalled"

        if terminated and term_reason == "goal_reached":
            reward = progress_reward + goal_bonus + survival_bonus + heading_bonus
        elif terminated and term_reason == "out_of_bounds":
            reward = progress_reward + lateral_penalty + oob_penalty
        elif terminated and term_reason == "stalled":
            reward = progress_reward + lateral_penalty + stall_penalty
        else:
            reward = (progress_reward + waypoint_bonus + lateral_penalty +
                      survival_bonus + time_penalty + backward_penalty + heading_bonus)

        breakdown = {
            "progress": float(progress_reward),
            "waypoint_bonus": float(waypoint_bonus),
            "lateral_penalty": float(lateral_penalty),
            "survival_bonus": float(survival_bonus),
            "time": float(time_penalty),
            "backward_penalty": float(backward_penalty),
            "heading_bonus": float(heading_bonus),
            "goal_bonus": float(goal_bonus),
            "oob_penalty": float(oob_penalty),
            "stall_penalty": float(stall_penalty),
        }
        return float(reward), terminated, term_reason, breakdown

    def _extract_obs(self, step: dict[str, Any]) -> dict[str, np.ndarray]:
        frame = step.get("frame") or {}
        b64 = frame.get("dataBase64", "")
        if b64:
            raw = base64.b64decode(b64)
            pil = Image.open(io.BytesIO(raw)).convert("RGB").resize(
                (self.img_size, self.img_size), Image.BILINEAR
            )
            image = np.asarray(pil, dtype=np.uint8)
        else:
            image = np.zeros((self.img_size, self.img_size, 3), dtype=np.uint8)

        if self._real_cam_postprocess and image.ndim == 3 and image.shape[2] == 3:
            x = image.astype(np.float32) * 0.85
            luma = (x * np.array([0.299, 0.587, 0.114], dtype=np.float32)).sum(
                axis=-1, keepdims=True
            )
            x = x * 0.75 + luma * 0.25
            x = np.clip(x, 0.0, 255.0).astype(np.uint8)
            buf = io.BytesIO()
            Image.fromarray(x).save(buf, format="JPEG", quality=60)
            buf.seek(0)
            image = np.asarray(Image.open(buf).convert("RGB"))

        tm = _telemetry_map(step)
        front_m = _parse_float(tm, "sensor.ultrasonic.front.m")
        ultrasonic = np.array([np.clip(front_m / 5.0, 0.0, 1.0)], dtype=np.float32)
        return {"image": image, "ultrasonic": ultrasonic}

    def close(self) -> None:
        pass

    def get_attr(self, attr_name: str, indices=None):
        return [getattr(self, attr_name, None) for _ in range(self.n_agents)]

    def set_attr(self, attr_name: str, value: Any, indices=None) -> None:
        setattr(self, attr_name, value)

    def env_method(self, method_name, *args, indices=None, **kwargs):
        return [None for _ in range(self.n_agents)]

    def env_is_wrapped(self, wrapper_class, indices=None):
        return [False] * self.n_agents

    def get_images(self):
        return [None] * self.n_agents

    def seed(self, seed=None):
        return [seed] * self.n_agents
