"""Standalone ONNX export — uses float32 dummy input + dynamo=False."""
import sys
from pathlib import Path
sys.path.insert(0, str(Path('python').resolve()))
import torch
from stable_baselines3 import PPO

zip_path = Path('python/training/artifacts/cardboard-corridor-ppo-v9-rev24/1.0.0/cardboard-corridor-ppo-v9-rev24_sb3.zip')
out_path = Path('python/training/artifacts/cardboard-corridor-ppo-v9-rev24/1.0.0/cardboard-corridor-ppo-v9-rev24.onnx')

print("Loading PPO checkpoint")
model = PPO.load(str(zip_path), device='cpu')
policy = model.policy

# CRITICAL: use the same dtype the network actually accepts.
# rev16 export used uint8 dummy because it worked with the older opset/exporter.
# Newer torch+onnx stricter. Cast inside the wrapper instead.
dummy_img = torch.zeros(1, 84, 84, 3, dtype=torch.uint8)
dummy_ultra = torch.zeros(1, 1, dtype=torch.float32)

class DiscretePolicyWrapper(torch.nn.Module):
    def __init__(self, sb3_policy):
        super().__init__()
        self.features_extractor = sb3_policy.features_extractor
        self.mlp_extractor = sb3_policy.mlp_extractor
        self.action_net = sb3_policy.action_net

    def forward(self, image, ultrasonic):
        # uint8 (B, H, W, C) -> float32 (B, C, H, W). The SB3 features_extractor
        # internally casts uint8 to float and divides by 255, but only if the
        # input is already (B, C, H, W) and the obs space says uint8. Doing it
        # explicitly here matches the training-time pipeline exactly.
        image_f = image.float() / 255.0
        image_chw = image_f.permute(0, 3, 1, 2).contiguous()
        obs = {"image": image_chw, "ultrasonic": ultrasonic}
        features = self.features_extractor(obs)
        latent_pi, _ = self.mlp_extractor(features)
        return self.action_net(latent_pi)

wrapper = DiscretePolicyWrapper(policy)
wrapper.eval()

# Test forward to verify
with torch.no_grad():
    out = wrapper(dummy_img, dummy_ultra)
    print(f"forward OK, logits shape: {out.shape}, values: {out[0].tolist()}")

out_path.parent.mkdir(parents=True, exist_ok=True)
print(f"Exporting (dynamo=False) to {out_path}")
torch.onnx.export(
    wrapper, (dummy_img, dummy_ultra), str(out_path),
    input_names=["image", "ultrasonic"], output_names=["action_logits"],
    external_data=False, dynamo=False,
    dynamic_axes={"image": {0: "batch"}, "ultrasonic": {0: "batch"}, "action_logits": {0: "batch"}},
    opset_version=11,
)
print(f"Exported. Size: {out_path.stat().st_size / 1024 / 1024:.2f} MB")
