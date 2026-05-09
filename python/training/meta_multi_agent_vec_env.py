"""rev20: Meta-VecEnv that fans out across N Unity processes.

Each inner env is a MultiAgentVisionVecEnv driving a single Unity instance with
K agents. The meta env wraps N inners (one per Unity port 8000..8000+N-1) and
exposes total = N*K agents to SB3. Per-step inner calls run in a thread pool so
HTTP latency to each Unity process overlaps (the Python GIL releases while
sockets block).

This sidesteps the Win Python 3.13 SubprocVecEnv deterministic broken-pipe
crash at ~116k steps (which is what motivated the multi-agent path in rev18)
while still scaling beyond what a single Unity instance can handle.
"""

from __future__ import annotations

from concurrent.futures import ThreadPoolExecutor
from typing import Any

import numpy as np
from stable_baselines3.common.vec_env import VecEnv
from stable_baselines3.common.vec_env.base_vec_env import VecEnvObs, VecEnvStepReturn

from training.multi_agent_vision_env import MultiAgentVisionVecEnv


class MetaMultiAgentVecEnv(VecEnv):
    """Parallel wrap of N MultiAgentVisionVecEnv instances on different Unity ports.

    Total agents = n_unity * agents_per_unity. Inner step calls run in a thread
    pool so Unity-side HTTP latency overlaps across processes.
    """

    def __init__(
        self,
        n_unity: int,
        agents_per_unity: int,
        base_url_template: str,
        scenario_path: str,
        max_steps: int,
        time_scale: float,
        img_size: int,
        corridor_width_m: float,
        goal_radius_m: float,
        waypoints: list[tuple[float, float]] | None,
        real_cam_postprocess: bool = False,
        base_port: int = 8000,
    ) -> None:
        self.n_unity = int(n_unity)
        self.agents_per_unity = int(agents_per_unity)
        self.inner_envs: list[MultiAgentVisionVecEnv] = []
        for i in range(self.n_unity):
            url = base_url_template.format(port=base_port + i)
            env = MultiAgentVisionVecEnv(
                n_agents=self.agents_per_unity,
                base_url=url,
                scenario_path=scenario_path,
                max_steps=max_steps,
                time_scale=time_scale,
                img_size=img_size,
                corridor_width_m=corridor_width_m,
                goal_radius_m=goal_radius_m,
                waypoints=waypoints,
                real_cam_postprocess=real_cam_postprocess,
            )
            self.inner_envs.append(env)

        total_agents = self.n_unity * self.agents_per_unity
        self._executor = ThreadPoolExecutor(max_workers=self.n_unity)
        super().__init__(
            total_agents,
            self.inner_envs[0].observation_space,
            self.inner_envs[0].action_space,
        )
        self._pending_actions: np.ndarray | None = None

    def reset(self) -> VecEnvObs:
        futures = [self._executor.submit(env.reset) for env in self.inner_envs]
        results = [f.result() for f in futures]
        return self._stack_obs(results)

    def step_async(self, actions: np.ndarray) -> None:
        self._pending_actions = actions

    def step_wait(self) -> VecEnvStepReturn:
        assert self._pending_actions is not None
        a = self.agents_per_unity
        actions = self._pending_actions
        # Slice actions per inner env and dispatch step_async on each.
        for i, env in enumerate(self.inner_envs):
            env.step_async(actions[i * a : (i + 1) * a])
        # Run step_wait in parallel — HTTP I/O dominates and releases the GIL.
        futures = [self._executor.submit(env.step_wait) for env in self.inner_envs]
        results = [f.result() for f in futures]

        obs_list = [r[0] for r in results]
        rewards = np.concatenate([r[1] for r in results])
        dones = np.concatenate([r[2] for r in results])
        infos: list[dict[str, Any]] = []
        for r in results:
            infos.extend(r[3])
        return self._stack_obs(obs_list), rewards, dones, infos

    def _stack_obs(self, obs_list: list[dict[str, np.ndarray]]) -> dict[str, np.ndarray]:
        return {
            "image": np.concatenate([o["image"] for o in obs_list], axis=0),
            "ultrasonic": np.concatenate([o["ultrasonic"] for o in obs_list], axis=0),
        }

    def close(self) -> None:
        try:
            for env in self.inner_envs:
                try:
                    env.close()
                except Exception:
                    pass
        finally:
            self._executor.shutdown(wait=True)

    # No-op VecEnv API ────────────────────────────────────────────
    def get_attr(self, attr_name: str, indices=None):
        return [getattr(self, attr_name, None)] * self.num_envs

    def set_attr(self, attr_name: str, value: Any, indices=None) -> None:
        setattr(self, attr_name, value)

    def env_method(self, method_name: str, *args, indices=None, **kwargs):
        return [None] * self.num_envs

    def env_is_wrapped(self, wrapper_class, indices=None):
        return [False] * self.num_envs

    def get_images(self):
        return [None] * self.num_envs

    def seed(self, seed=None):
        return [seed] * self.num_envs
