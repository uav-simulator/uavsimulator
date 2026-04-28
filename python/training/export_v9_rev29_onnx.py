import sys
from pathlib import Path
sys.path.insert(0, str(Path('python').resolve()))
import torch
from stable_baselines3 import PPO

zip_path = Path('python/training/artifacts/cardboard-corridor-ppo-v9-rev29/1.0.0/cardboard-corridor-ppo-v9-rev29_sb3.zip')
out_path = Path('python/training/artifacts/cardboard-corridor-ppo-v9-rev29/1.0.0/cardboard-corridor-ppo-v9-rev29.onnx')

model = PPO.load(str(zip_path), device='cpu')
policy = model.policy
dummy_img = torch.zeros(1, 84, 84, 3, dtype=torch.float32)
dummy_ultra = torch.zeros(1, 1, dtype=torch.float32)


class DiscretePolicyWrapper(torch.nn.Module):
    def __init__(self, p):
        super().__init__()
        self.features_extractor = p.features_extractor
        self.mlp_extractor = p.mlp_extractor
        self.action_net = p.action_net

    def forward(self, image, ultrasonic):
        image_norm = image / 255.0
        image_chw = image_norm.permute(0, 3, 1, 2).contiguous()
        obs = {"image": image_chw, "ultrasonic": ultrasonic}
        f = self.features_extractor(obs)
        latent_pi, _ = self.mlp_extractor(f)
        return self.action_net(latent_pi)


w = DiscretePolicyWrapper(policy).eval()
torch.onnx.export(
    w, (dummy_img, dummy_ultra), str(out_path),
    input_names=["image", "ultrasonic"], output_names=["action_logits"],
    external_data=False, dynamo=False,
    dynamic_axes={"image": {0: "batch"}, "ultrasonic": {0: "batch"}, "action_logits": {0: "batch"}},
    opset_version=11,
)
print(f"Exported {out_path} ({out_path.stat().st_size/1024/1024:.2f} MB)")
