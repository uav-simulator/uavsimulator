"""
Multi-agent VecEnv for A→B corridor task.

N agents run simultaneously in one Unity simulator instance.
Agents are isolated (don't see each other, don't collide).
Episode resets when all agents are done or max_steps is reached.
SB3-compatible VecEnv interface.
"""

from __future__ import annotations

import math
from pathlib import Path
from typing import Any, Optional

import numpy as np
from gymnasium import spaces

import sys

ROOT = Path(__file__).resolve().parents[2]
PYTHON_ROOT = ROOT / "python"
if str(PYTHON_ROOT) not in sys.path:
    sys.path.insert(0, str(PYTHON_ROOT))

from sim_client.http_client import SimClient
from stable_baselines3.common.vec_env import VecEnv
from stable_baselines3.common.vec_env.base_vec_env import VecEnvObs, VecEnvStepReturn


# Corridor route waypoints (same as single-agent env)
WAYPOINTS: list[tuple[float, float]] = [
    (0.0, -7.5),
    (0.0, -1.0),
    (6.0, -1.0),
    (6.0, 5.0),
]
WAYPOINT_BONUS_TABLE = [5.0, 15.0, 25.0, 0.0]  # goal handled separately

VEHICLE_ID = "vehicle.ks0223.v1"
TRACK_ID = "track.basic_arena.v1"


def _route_length(waypoints: list[tuple[float, float]]) -> float:
    total = 0.0
    for i in range(len(waypoints) - 1):
        ax, az = waypoints[i]
        bx, bz = waypoints[i + 1]
        total += math.hypot(bx - ax, bz - az)
    return max(total, 1.0)


TOTAL_ROUTE_LEN = _route_length(WAYPOINTS)


def _project_t(px: float, pz: float, ax: float, az: float, bx: float, bz: float) -> float:
    abx, abz = bx - ax, bz - az
    ab2 = abx * abx + abz * abz
    if ab2 < 1e-9:
        return 0.0
    return max(0.0, min(1.0, ((px - ax) * abx + (pz - az) * abz) / ab2))


def _seg_dist(px: float, pz: float, ax: float, az: float, bx: float, bz: float) -> float:
    t = _project_t(px, pz, ax, az, bx, bz)
    return math.hypot(px - (ax + t * (bx - ax)), pz - (az + t * (bz - az)))


def _route_progress(px: float, pz: float) -> float:
    best_dist = float("inf")
    best_prog = 0.0
    cum = 0.0
    for i in range(len(WAYPOINTS) - 1):
        ax, az = WAYPOINTS[i]
        bx, bz = WAYPOINTS[i + 1]
        seg_len = math.hypot(bx - ax, bz - az)
        t = _project_t(px, pz, ax, az, bx, bz)
        d = _seg_dist(px, pz, ax, az, bx, bz)
        if d < best_dist:
            best_dist = d
            best_prog = (cum + t * seg_len) / TOTAL_ROUTE_LEN
        cum += seg_len
    return float(np.clip(best_prog, 0.0, 1.0))


def _nearest_dist(px: float, pz: float) -> float:
    best = float("inf")
    for i in range(len(WAYPOINTS) - 1):
        ax, az = WAYPOINTS[i]
        bx, bz = WAYPOINTS[i + 1]
        d = _seg_dist(px, pz, ax, az, bx, bz)
        if d < best:
            best = d
    return best


def _closest_segment_idx(px: float, pz: float) -> int:
    best_dist = float("inf")
    best_idx = 0
    for i in range(len(WAYPOINTS) - 1):
        ax, az = WAYPOINTS[i]
        bx, bz = WAYPOINTS[i + 1]
        d = _seg_dist(px, pz, ax, az, bx, bz)
        if d < best_dist:
            best_dist = d
            best_idx = i
    return best_idx


def _heading_error(px: float, pz: float, current_heading: float) -> float:
    seg_idx = _closest_segment_idx(px, pz)
    target_idx = min(seg_idx + 1, len(WAYPOINTS) - 1)
    tx, tz = WAYPOINTS[target_idx]
    target_angle = math.atan2(tx - px, tz - pz)
    err = target_angle - current_heading
    while err > math.pi:
        err -= 2 * math.pi
    while err < -math.pi:
        err += 2 * math.pi
    return err


def _extract_position(step: dict[str, Any]) -> tuple[float, float, float]:
    pos = ((step.get("state") or {}).get("pose") or {}).get("position") or {}
    return float(pos.get("x", 0)), float(pos.get("y", 0)), float(pos.get("z", 0))


def _telemetry_map(step: dict[str, Any]) -> dict[str, str]:
    result: dict[str, str] = {}
    for item in (step.get("state") or {}).get("telemetry") or []:
        if isinstance(item, dict):
            key = str(item.get("key", "")).strip()
            if key:
                result[key] = str(item.get("value", ""))
    return result


def _parse_float(m: dict[str, str], *keys: str) -> float:
    for k in keys:
        raw = m.get(k)
        if raw is not None:
            try:
                return float(raw)
            except ValueError:
                continue
    return 0.0


class _AgentState:
    """Per-agent tracking state."""

    __slots__ = (
        "prev_progress", "prev_steer", "prev_x", "prev_z",
        "speed", "heading", "reached_waypoints", "done", "stall_steps",
    )

    def __init__(self) -> None:
        self.prev_progress = 0.0
        self.prev_steer = 0.0
        self.prev_x = 0.0
        self.prev_z = -7.5
        self.speed = 0.0
        self.heading = 0.0
        self.reached_waypoints: set[int] = set()
        self.done = False
        self.stall_steps = 0

    def reset(self) -> None:
        self.prev_progress = 0.0
        self.prev_steer = 0.0
        self.speed = 0.0
        self.heading = 0.0
        self.reached_waypoints = set()
        self.done = False
        self.stall_steps = 0

    def update_kinematics(self, px: float, pz: float) -> None:
        dx = px - self.prev_x
        dz = pz - self.prev_z
        dist = math.hypot(dx, dz)
        self.speed = dist
        if dist > 0.001:
            self.heading = math.atan2(dx, dz)
        self.prev_x = px
        self.prev_z = pz


class ABCorridorMultiAgentVecEnv(VecEnv):
    """
    N agents running simultaneously in one Unity simulator instance.

    Agents are isolated: they don't see each other and don't collide.
    Episode boundaries are synchronized: when all agents are done (or
    max_steps reached), all are reset together.

    SB3's PPO auto-resets individual envs when done=True. We implement
    this by immediately resetting internally and returning the fresh obs
    when done is signalled.
    """

    def __init__(
        self,
        n_agents: int = 4,
        base_url: str = "http://127.0.0.1:8000",
        max_steps: int = 400,
        corridor_width_m: float = 3.0,
        oob_margin_m: float = 0.3,
        goal_radius_m: float = 1.0,
        time_scale: float = 2.0,
    ) -> None:
        self.n_agents = n_agents
        self.client = SimClient(base_url, timeout_s=60.0)
        self.max_steps = max_steps
        self.oob_threshold_m = corridor_width_m * 0.5 + oob_margin_m
        self.goal_radius_m = goal_radius_m
        self.time_scale = time_scale

        # agent IDs: "ego", "agent-2", "agent-3", ...
        self.agent_ids = ["ego"] + [f"agent-{i + 1}" for i in range(1, n_agents)]

        obs_space = spaces.Box(low=-1.0, high=1.0, shape=(8,), dtype=np.float32)
        act_space = spaces.Box(low=-1.0, high=1.0, shape=(2,), dtype=np.float32)
        super().__init__(n_agents, obs_space, act_space)

        self._states: list[_AgentState] = [_AgentState() for _ in range(n_agents)]
        self._step_count = 0
        self._pending_actions: Optional[np.ndarray] = None
        self._episode_seed = 0

    # ------------------------------------------------------------------ #
    #  Reset config                                                         #
    # ------------------------------------------------------------------ #

    def _build_reset_config(self) -> dict[str, Any]:
        flags = [
            {"key": "agents.isolated", "value": "true"},
            {"key": "agents.see_each_other", "value": "false"},
            {"key": "agents.collisions_enabled", "value": "false"},
        ]
        agents = []
        for i, agent_id in enumerate(self.agent_ids):
            entry: dict[str, Any] = {
                "agentId": agent_id,
                "vehicleId": VEHICLE_ID,
                "isPrimary": i == 0,
                "trackParams": [],
                "vehicleParams": [{"key": "camera.profile", "value": "high"}] if i == 0 else [],
                "flags": [],
            }
            agents.append(entry)

        return {
            "seed": self._episode_seed,
            "timeScale": self.time_scale,
            "selectedTrackId": TRACK_ID,
            "selectedVehicleId": VEHICLE_ID,
            "trackParams": [],
            "vehicleParams": [],
            "flags": flags,
            "agents": agents,
        }

    # ------------------------------------------------------------------ #
    #  VecEnv interface                                                     #
    # ------------------------------------------------------------------ #

    def reset(self) -> VecEnvObs:
        self._episode_seed += 1
        config = self._build_reset_config()
        resp = self.client.reset(config)

        for state in self._states:
            state.reset()
        self._step_count = 0

        obs = np.zeros((self.n_agents, 8), dtype=np.float32)
        # Primary agent (ego) obs comes from reset response
        px, _, pz = _extract_position(resp)
        self._states[0].prev_x = px
        self._states[0].prev_z = pz
        obs[0] = self._extract_obs(resp, self._states[0])

        # Non-primary agents: send a zero-action step to retrieve their state
        for i in range(1, self.n_agents):
            agent_resp = self.client.step({
                "throttle": 0.0,
                "steer": 0.0,
                "brake": 0.0,
                "targetAgentId": self.agent_ids[i],
                "timestamp": 0,
                "timeBase": "unix_ms",
                "extensions": [],
            })
            apx, _, apz = _extract_position(agent_resp)
            self._states[i].prev_x = apx
            self._states[i].prev_z = apz
            obs[i] = self._extract_obs(agent_resp, self._states[i])

        return obs

    def step_async(self, actions: np.ndarray) -> None:
        self._pending_actions = actions

    def step_wait(self) -> VecEnvStepReturn:
        assert self._pending_actions is not None
        actions = self._pending_actions
        self._step_count += 1

        obs_buf = np.zeros((self.n_agents, 8), dtype=np.float32)
        rew_buf = np.zeros(self.n_agents, dtype=np.float32)
        done_buf = np.zeros(self.n_agents, dtype=bool)
        info_buf: list[dict[str, Any]] = [{} for _ in range(self.n_agents)]

        for i, agent_id in enumerate(self.agent_ids):
            if self._states[i].done:
                # Agent already done this episode — keep returning zeros
                done_buf[i] = True
                continue

            throttle = float(np.clip(actions[i, 0], -1.0, 1.0))
            steer = float(np.clip(actions[i, 1], -1.0, 1.0))

            resp = self.client.step({
                "throttle": throttle,
                "steer": steer,
                "brake": 0.0,
                "targetAgentId": agent_id,
                "timestamp": 0,
                "timeBase": "unix_ms",
                "extensions": [],
            })

            px, _, pz = _extract_position(resp)
            self._states[i].update_kinematics(px, pz)

            obs_buf[i] = self._extract_obs(resp, self._states[i])
            reward, terminated = self._compute_reward(resp, steer, i, px, pz)
            rew_buf[i] = reward

            if terminated or self._step_count >= self.max_steps:
                self._states[i].done = True
                done_buf[i] = True

            info_buf[i] = {
                "agent_id": agent_id,
                "progress": _route_progress(px, pz),
                "lateral_dist": _nearest_dist(px, pz),
                "step": self._step_count,
            }
            self._states[i].prev_steer = steer

        # Episode over when all agents done or max_steps reached
        all_done = all(s.done for s in self._states)
        if self._step_count >= self.max_steps:
            all_done = True

        if all_done:
            # SB3 expects obs[i] = first obs of NEW episode when done[i]=True
            # Store terminal obs in infos, then auto-reset
            for i in range(self.n_agents):
                if done_buf[i]:
                    info_buf[i]["terminal_observation"] = obs_buf[i].copy()
            done_buf[:] = True
            fresh_obs = self.reset()
            obs_buf = fresh_obs

        return obs_buf, rew_buf, done_buf, info_buf

    # ------------------------------------------------------------------ #
    #  Reward                                                               #
    # ------------------------------------------------------------------ #

    def _compute_reward(
        self,
        step: dict[str, Any],
        steer: float,
        agent_idx: int,
        px: float,
        pz: float,
    ) -> tuple[float, bool]:
        state = self._states[agent_idx]

        # Progress reward (forward only)
        progress = _route_progress(px, pz)
        delta_progress = max(0.0, progress - state.prev_progress)
        state.prev_progress = progress
        progress_reward = delta_progress * 100.0

        # Waypoint bonuses
        waypoint_bonus = 0.0
        for wi in range(len(WAYPOINTS)):
            if wi not in state.reached_waypoints:
                wx, wz = WAYPOINTS[wi]
                if math.hypot(px - wx, pz - wz) < 1.5:
                    state.reached_waypoints.add(wi)
                    waypoint_bonus += WAYPOINT_BONUS_TABLE[wi]

        # Heading reward
        heading_err = _heading_error(px, pz, state.heading)
        heading_reward = 0.3 * (1.0 - abs(heading_err) / math.pi)

        # Velocity reward: speed in direction of next waypoint
        aligned_speed = state.speed * max(0.0, math.cos(heading_err))
        velocity_reward = 0.5 * min(aligned_speed / 0.05, 1.0)

        # Lateral penalty
        lateral_dist = _nearest_dist(px, pz)
        lateral_penalty = -0.5 * (lateral_dist / self.oob_threshold_m) ** 2

        # Jerk penalty
        jerk_penalty = -0.01 * abs(steer - state.prev_steer)

        # Stuck penalty
        if delta_progress < 0.001:
            state.stall_steps += 1
        else:
            state.stall_steps = 0
        stall_penalty = -0.05 * min(state.stall_steps / 20.0, 1.0)

        # Goal
        goal_bonus = 0.0
        terminated = False
        gx, gz = WAYPOINTS[-1]
        if math.hypot(px - gx, pz - gz) < self.goal_radius_m:
            goal_bonus = 200.0
            terminated = True

        # OOB
        oob_penalty = 0.0
        if lateral_dist > self.oob_threshold_m:
            oob_penalty = -30.0
            terminated = True

        if bool(step.get("done")) and not terminated:
            oob_penalty = -15.0
            terminated = True

        reward = (progress_reward + waypoint_bonus + heading_reward +
                  velocity_reward + lateral_penalty + jerk_penalty +
                  stall_penalty + goal_bonus + oob_penalty)
        return float(reward), terminated

    # ------------------------------------------------------------------ #
    #  Observation                                                          #
    # ------------------------------------------------------------------ #

    def _extract_obs(self, step: dict[str, Any], state: _AgentState) -> np.ndarray:
        tm = _telemetry_map(step)
        s1 = _parse_float(tm, "sensor.line_tracker.s1_norm")
        s2 = _parse_float(tm, "sensor.line_tracker.s2_norm")
        s3 = _parse_float(tm, "sensor.line_tracker.s3_norm")
        s4 = _parse_float(tm, "sensor.line_tracker.s4_norm")
        s5 = _parse_float(tm, "sensor.line_tracker.s5_norm")
        front_dist = _parse_float(tm, "sensor.ultrasonic.front.m") / 5.0
        speed_norm = min(state.speed / 0.1, 1.0)
        px, _, pz = _extract_position(step)
        heading_err = _heading_error(px, pz, state.heading) / math.pi
        return np.array([
            np.clip(s1, -1, 1),
            np.clip(s2, -1, 1),
            np.clip(s3, -1, 1),
            np.clip(s4, -1, 1),
            np.clip(s5, -1, 1),
            np.clip(front_dist, -1, 1),
            np.clip(speed_norm, -1, 1),
            np.clip(heading_err, -1, 1),
        ], dtype=np.float32)

    # ------------------------------------------------------------------ #
    #  Required VecEnv abstract methods (minimal implementations)           #
    # ------------------------------------------------------------------ #

    def close(self) -> None:
        pass

    def get_attr(self, attr_name: str, indices=None):
        return [getattr(self, attr_name)] * self.n_agents

    def set_attr(self, attr_name: str, value: Any, indices=None) -> None:
        setattr(self, attr_name, value)

    def env_method(self, method_name: str, *method_args, indices=None, **method_kwargs):
        return [getattr(self, method_name)(*method_args, **method_kwargs)]

    def env_is_wrapped(self, wrapper_class, indices=None):
        return [False] * self.n_agents

    def get_images(self):
        return [None] * self.n_agents

    def seed(self, seed=None):
        if seed is not None:
            self._episode_seed = seed
        return [seed] * self.n_agents
