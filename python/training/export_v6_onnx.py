#!/usr/bin/env python3
"""Export v6 SB3 checkpoint to ONNX for backend upload."""

from __future__ import annotations

import sys
from pathlib import Path

import torch

ROOT = Path(__file__).resolve().parents[2]
PYTHON_ROOT = ROOT / "python"
if str(PYTHON_ROOT) not in sys.path:
    sys.path.insert(0, str(PYTHON_ROOT))

from stable_baselines3 import PPO  # noqa: E402

MODEL_PATH = ROOT / "python/training/artifacts/cardboard-corridor-ppo-v6/1.0.0/checkpoints/cardboard_cnn_ppo_250000_steps.zip"
ONNX_PATH = ROOT / "python/training/artifacts/cardboard-corridor-ppo-v6/1.0.0/cardboard-corridor-ppo-v6.onnx"


class VisionPolicyWrapper(torch.nn.Module):
    def __init__(self, sb3_policy):
        super().__init__()
        self.features_extractor = sb3_policy.features_extractor
        self.mlp_extractor = sb3_policy.mlp_extractor
        self.action_net = sb3_policy.action_net

    def forward(self, image: torch.Tensor, ultrasonic: torch.Tensor):
        image_chw = image.permute(0, 3, 1, 2).contiguous()
        obs = {"image": image_chw, "ultrasonic": ultrasonic}
        features = self.features_extractor(obs)
        latent_pi, _ = self.mlp_extractor(features)
        return torch.tanh(self.action_net(latent_pi))


def main():
    print(f"Loading {MODEL_PATH}...")
    model = PPO.load(str(MODEL_PATH))
    policy = model.policy
    policy.eval()

    wrapper = VisionPolicyWrapper(policy)
    wrapper.eval()

    dummy_img = torch.zeros(1, 84, 84, 3, dtype=torch.float32)
    dummy_ultra = torch.zeros(1, 1, dtype=torch.float32)

    ONNX_PATH.parent.mkdir(parents=True, exist_ok=True)
    torch.onnx.export(
        wrapper,
        (dummy_img, dummy_ultra),
        str(ONNX_PATH),
        input_names=["image", "ultrasonic"],
        output_names=["action"],
        external_data=False,
        dynamic_axes={
            "image": {0: "batch"},
            "ultrasonic": {0: "batch"},
            "action": {0: "batch"},
        },
        opset_version=11,
    )
    size_mb = ONNX_PATH.stat().st_size / 1024 / 1024
    print(f"Exported ONNX: {ONNX_PATH} ({size_mb:.1f} MB)")
    print("OK")


if __name__ == "__main__":
    main()
