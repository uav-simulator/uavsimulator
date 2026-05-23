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
    """Load BC checkpoint into PPO.

    Most hyperparameters (n_steps, batch_size, n_epochs, gamma, clip_range,
    ent_coef, target_kl) are baked into the BC checkpoint at export time
    (see BcTrainer.export_sb3) because PPO.load() allocates the rollout_buffer
    based on n_steps stored in the archive. Overriding n_steps after load
    leaves the buffer at its previous (potentially default 2048) size, which
    causes an AssertionError in PPO.train() on the first cycle.

    Only learning_rate is safe to override after load (it is applied as a
    schedule callable at .learn() time, not during construction).

    Args:
        bc_zip: Path to BC checkpoint produced by BcTrainer.export_sb3.
        config: Hyperparameters; checked against the checkpoint for drift.
        env: Optional vec_env. If None, a stub env is constructed (matches the
             BC checkpoint's observation / action spaces). Pass a real env when
             continuing training.

    Returns:
        PPO instance ready for .learn(...).
    """
    if env is None:
        env = _build_stub_env()

    model = PPO.load(str(bc_zip), env=env, device="cpu")

    # Sanity: validate the BC checkpoint's hyperparameters match config (catches
    # the case where someone trained a BC with stale hyperparameters).
    expected_mismatch = []
    if model.n_steps != config.n_steps:
        expected_mismatch.append(f"n_steps: bc={model.n_steps}, config={config.n_steps}")
    if model.batch_size != config.batch_size:
        expected_mismatch.append(f"batch_size: bc={model.batch_size}, config={config.batch_size}")
    if model.n_epochs != config.n_epochs:
        expected_mismatch.append(f"n_epochs: bc={model.n_epochs}, config={config.n_epochs}")
    if expected_mismatch:
        import warnings

        warnings.warn(
            f"BC checkpoint hyperparameters differ from BcToPpoConfig: {expected_mismatch}. "
            "This usually means the BC was trained with stale defaults; consider re-exporting. "
            "Using checkpoint values to avoid rollout_buffer reallocation bug.",
            stacklevel=2,
        )

    # Safe overrides (do not require buffer reallocation):
    model.learning_rate = config.learning_rate
    # SB3 stores clip_range as a schedule callable internally; wrapping as a
    # constant lambda ensures .learn() reads the overridden value rather than
    # the schedule baked into the archive.
    model.clip_range = lambda _: config.clip_range
    model.seed = config.seed
    return model
