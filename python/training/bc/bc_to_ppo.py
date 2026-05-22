"""Load BC checkpoint into PPO and prepare for continued training on the same env."""
from __future__ import annotations

from dataclasses import dataclass
from pathlib import Path

from stable_baselines3 import PPO


@dataclass
class BcToPpoConfig:
    learning_rate: float = 3e-4
    n_steps: int = 256
    batch_size: int = 64
    n_epochs: int = 4
    gamma: float = 0.99
    clip_range: float = 0.2
    # ent_coef pinned to 0.1 to avoid the silent rev24-rev28 default drift.
    ent_coef: float = 0.1
    target_kl: float = 0.02
    seed: int = 42


def _build_stub_env():
    import gymnasium as gym
    import numpy as np
    from stable_baselines3.common.vec_env import DummyVecEnv

    class _Stub(gym.Env):
        metadata = {"render_modes": []}

        def __init__(self) -> None:
            super().__init__()
            self.observation_space = gym.spaces.Dict(
                {
                    "image": gym.spaces.Box(0, 255, (3, 84, 84), dtype=np.uint8),
                    "ultrasonic": gym.spaces.Box(0.0, 1.0, (1,), dtype=np.float32),
                }
            )
            self.action_space = gym.spaces.Discrete(5)

        def _zero_obs(self):
            return {
                "image": np.zeros((3, 84, 84), dtype=np.uint8),
                "ultrasonic": np.zeros((1,), dtype=np.float32),
            }

        def reset(self, *, seed=None, options=None):
            super().reset(seed=seed)
            return self._zero_obs(), {}

        def step(self, action):
            return self._zero_obs(), 0.0, True, False, {}

    return DummyVecEnv([lambda: _Stub()])


def prepare_ppo_from_bc(bc_zip: Path, config: BcToPpoConfig, env=None) -> PPO:
    """Load BC weights into a PPO instance and override hyperparameters.

    Args:
        bc_zip: Path to BC checkpoint produced by BcTrainer.export_sb3.
        config: Hyperparameters to install on the loaded PPO instance.
        env: Optional vec_env. If None, a stub env is constructed (matches the
             BC checkpoint's observation / action spaces). Pass a real env when
             continuing training.

    Returns:
        PPO instance ready for .learn(...).
    """
    if env is None:
        env = _build_stub_env()

    model = PPO.load(str(bc_zip), env=env, device="cpu")

    model.learning_rate = config.learning_rate
    model.n_steps = config.n_steps
    model.batch_size = config.batch_size
    model.n_epochs = config.n_epochs
    model.gamma = config.gamma
    # SB3 stores clip_range as a schedule callable internally; wrapping as a
    # constant lambda ensures .learn() reads the overridden value rather than
    # the schedule baked into the archive.
    model.clip_range = lambda _: config.clip_range
    model.ent_coef = config.ent_coef
    model.target_kl = config.target_kl
    model.seed = config.seed
    return model
