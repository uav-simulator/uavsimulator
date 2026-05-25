"""Smoke test for the ONNX export with frame-stacking (k>1).

Verifies that `export_to_onnx_discrete` reads channel count from the
model's saved obs_space (not hard-coded to 3), so a k=4 model exports
to ONNX with the correct (1, 84, 84, 12) image input shape.
"""
from __future__ import annotations

from pathlib import Path

import numpy as np
import gymnasium as gym
import pytest
from gymnasium import spaces
from stable_baselines3 import PPO
from stable_baselines3.common.vec_env import DummyVecEnv, VecFrameStack

from training.train_cardboard_corridor_v9 import export_to_onnx_discrete


class _Stub(gym.Env):
    """Tiny Dict-obs env matching the cardboard-corridor obs structure."""
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


@pytest.mark.parametrize("n_stack", [1, 4])
def test_onnx_export_handles_frame_stack(n_stack, tmp_path):
    """Round-trip: train tiny PPO, frame-stack k, export ONNX, verify the
    ONNX graph's image input has the correct channel count."""
    venv = DummyVecEnv([lambda: _Stub()])
    if n_stack > 1:
        venv = VecFrameStack(venv, n_stack=n_stack, channels_order="last")
    model = PPO("MultiInputPolicy", venv, n_steps=8, batch_size=8, device="cpu")
    out = tmp_path / f"stub-fs{n_stack}.onnx"

    export_to_onnx_discrete(model, out)
    venv.close()

    assert out.exists(), "ONNX file not created"

    # Read the ONNX graph and check input shapes match k.
    import onnx
    graph = onnx.load(str(out)).graph
    image_input = next(i for i in graph.input if i.name == "image")
    image_shape = [
        dim.dim_value if dim.HasField("dim_value") else -1
        for dim in image_input.type.tensor_type.shape.dim
    ]
    # Expected (batch, 84, 84, n_stack*3) — first dim is dynamic per
    # dynamic_axes={'image': {0: 'batch'}}, so it shows up as 0 or -1.
    assert image_shape[1:] == [84, 84, n_stack * 3], (
        f"Expected ONNX image input HWC with C={n_stack * 3}; got {image_shape}"
    )

    ultra_input = next(i for i in graph.input if i.name == "ultrasonic")
    ultra_shape = [
        dim.dim_value if dim.HasField("dim_value") else -1
        for dim in ultra_input.type.tensor_type.shape.dim
    ]
    assert ultra_shape[1] == n_stack, (
        f"Expected ONNX ultrasonic input dim 1 = {n_stack}; got {ultra_shape}"
    )
