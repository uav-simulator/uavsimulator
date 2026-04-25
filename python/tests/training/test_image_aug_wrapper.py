"""Tests for ImageAugObservationWrapper."""

from __future__ import annotations

import sys
from pathlib import Path

import gymnasium as gym
import numpy as np
import pytest
from gymnasium import spaces

PYTHON_ROOT = Path(__file__).resolve().parents[2]
if str(PYTHON_ROOT) not in sys.path:
    sys.path.insert(0, str(PYTHON_ROOT))

from training.image_aug_wrapper import ImageAugObservationWrapper  # noqa: E402


class _DictObsStub(gym.Env):
    metadata = {"render_modes": []}

    def __init__(self, image_shape=(84, 84, 3)):
        self.image_shape = image_shape
        self.observation_space = spaces.Dict(
            {
                "image": spaces.Box(0, 255, image_shape, dtype=np.uint8),
                "ultrasonic": spaces.Box(0, 1, (1,), dtype=np.float32),
            }
        )
        self.action_space = spaces.Box(-1, 1, (2,), dtype=np.float32)

    def _make_obs(self):
        rng = np.random.default_rng(42)
        return {
            "image": rng.integers(0, 256, size=self.image_shape, dtype=np.uint8),
            "ultrasonic": np.array([0.5], dtype=np.float32),
        }

    def reset(self, *, seed=None, options=None):
        return self._make_obs(), {}

    def step(self, action):
        return self._make_obs(), 0.0, False, False, {}


def test_disabled_passthrough():
    env = ImageAugObservationWrapper(_DictObsStub(), enable=False)
    obs, _ = env.reset()
    obs2, _, _, _, _ = env.step(np.zeros(2))
    assert obs["image"].shape == (84, 84, 3)
    assert obs["image"].dtype == np.uint8


def test_enabled_modifies_image():
    base = _DictObsStub()
    env = ImageAugObservationWrapper(
        base,
        enable=True,
        noise_sigma=0.05,
        brightness_range=0.2,
        contrast_range=0.2,
        hue_shift_range=10,
        blur_prob=1.0,  # always blur for determinism
        blur_radius_max=1.0,
        jpeg_recompress_prob=0.0,  # disable for shape stability
        seed=123,
    )
    raw_obs = base._make_obs()
    aug = env.observation(raw_obs)
    assert aug["image"].shape == raw_obs["image"].shape
    assert aug["image"].dtype == np.uint8
    # Augmentation should change at least some pixels
    diff = np.abs(aug["image"].astype(int) - raw_obs["image"].astype(int)).mean()
    assert diff > 0.5, f"expected non-trivial augmentation, mean abs diff={diff}"


def test_preserves_ultrasonic_unchanged():
    env = ImageAugObservationWrapper(_DictObsStub(), enable=True, seed=1)
    raw_obs = {
        "image": np.zeros((84, 84, 3), dtype=np.uint8),
        "ultrasonic": np.array([0.42], dtype=np.float32),
    }
    aug = env.observation(raw_obs)
    np.testing.assert_array_equal(aug["ultrasonic"], raw_obs["ultrasonic"])


def test_clips_to_uint8_range():
    base = _DictObsStub()
    # Strong augmentation, ensure no overflow
    env = ImageAugObservationWrapper(
        base,
        enable=True,
        noise_sigma=0.5,
        brightness_range=0.5,
        contrast_range=0.5,
        seed=7,
    )
    for _ in range(10):
        obs = env.observation(base._make_obs())
        assert obs["image"].min() >= 0
        assert obs["image"].max() <= 255


def test_jpeg_recompress_keeps_shape():
    base = _DictObsStub()
    env = ImageAugObservationWrapper(
        base,
        enable=True,
        noise_sigma=0.0,
        brightness_range=0.0,
        contrast_range=0.0,
        hue_shift_range=0.0,
        blur_prob=0.0,
        jpeg_recompress_prob=1.0,
        seed=2,
    )
    raw = base._make_obs()
    aug = env.observation(raw)
    assert aug["image"].shape == raw["image"].shape


def test_non_dict_obs_passthrough():
    """If obs isn't a dict (or no 'image' key), wrapper should not crash."""

    class _ArrayObsStub(gym.Env):
        observation_space = spaces.Box(0, 255, (4,), dtype=np.float32)
        action_space = spaces.Box(-1, 1, (2,), dtype=np.float32)

        def reset(self, *, seed=None, options=None):
            return np.zeros(4, dtype=np.float32), {}

        def step(self, a):
            return np.zeros(4, dtype=np.float32), 0.0, False, False, {}

    env = ImageAugObservationWrapper(_ArrayObsStub(), enable=True, seed=0)
    obs, _ = env.reset()
    assert obs.shape == (4,)
