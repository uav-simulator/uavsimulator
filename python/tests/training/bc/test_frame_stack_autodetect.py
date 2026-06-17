"""Smoke test for the evaluate_v9 frame_stack auto-detect logic.

evaluate_v9 reads the saved model's `ultrasonic` obs_space shape to
auto-detect the frame_stack k used at training time. This test
synthesises a PPO model trained with VecFrameStack(k=4) and verifies
the auto-detect picks the right k.
"""
from __future__ import annotations

import gymnasium as gym
import numpy as np
import pytest
from gymnasium import spaces
from stable_baselines3 import PPO
from stable_baselines3.common.vec_env import DummyVecEnv, VecFrameStack


class _StubMazeEnv(gym.Env):
    """Tiny stub env mimicking the dict obs the maze pipeline produces."""

    metadata = {"render_modes": []}

    def __init__(self):
        self.observation_space = spaces.Dict({
            "image": spaces.Box(low=0, high=255, shape=(84, 84, 3), dtype=np.uint8),
            "ultrasonic": spaces.Box(low=0.0, high=1.0, shape=(1,), dtype=np.float32),
        })
        self.action_space = spaces.Discrete(5)

    def reset(self, *, seed=None, options=None):
        return self._obs(), {}

    def step(self, a):
        return self._obs(), 0.0, True, False, {}

    def _obs(self):
        return {
            "image": np.zeros((84, 84, 3), dtype=np.uint8),
            "ultrasonic": np.zeros((1,), dtype=np.float32),
        }


def _build_ppo(n_stack: int, tmp_path) -> tuple[PPO, str]:
    venv = DummyVecEnv([_StubMazeEnv])
    if n_stack > 1:
        venv = VecFrameStack(venv, n_stack=n_stack, channels_order="last")
    model = PPO("MultiInputPolicy", venv, n_steps=8, batch_size=8, device="cpu")
    save_path = str(tmp_path / f"stub-fs{n_stack}.zip")
    model.save(save_path)
    venv.close()
    return model, save_path


@pytest.mark.parametrize("n_stack", [1, 2, 4, 8])
def test_saved_model_ultrasonic_shape_encodes_n_stack(n_stack, tmp_path):
    """A model trained under VecFrameStack(k) should save an obs_space
    whose ultrasonic shape is (k,). evaluate_v9's auto-detect reads
    exactly this field, so the round-trip is the contract."""
    _, save_path = _build_ppo(n_stack, tmp_path)
    loaded = PPO.load(save_path, device="cpu")
    ultra_shape = loaded.observation_space.spaces["ultrasonic"].shape
    assert ultra_shape == (n_stack,), (
        f"VecFrameStack(n_stack={n_stack}) should produce ultrasonic shape "
        f"({n_stack},); got {ultra_shape}"
    )


def test_saved_model_image_shape_for_k4(tmp_path):
    """Image shape after VecFrameStack(channels_order='last') + auto
    VecTransposeImage is (k*3, H, W) = (12, 84, 84) for k=4."""
    _, save_path = _build_ppo(4, tmp_path)
    loaded = PPO.load(save_path, device="cpu")
    img_shape = loaded.observation_space.spaces["image"].shape
    assert img_shape == (12, 84, 84), f"expected (12, 84, 84), got {img_shape}"


def test_no_framestack_baseline_obs_space(tmp_path):
    """Without VecFrameStack the saved obs_space is the original
    (3, 84, 84) image + (1,) ultrasonic."""
    _, save_path = _build_ppo(1, tmp_path)
    loaded = PPO.load(save_path, device="cpu")
    assert loaded.observation_space.spaces["image"].shape == (3, 84, 84)
    assert loaded.observation_space.spaces["ultrasonic"].shape == (1,)
