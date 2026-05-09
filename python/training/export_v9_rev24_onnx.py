"""rev24 ONNX export with FLOAT image input (matches rev16 signature)."""
import sys
from pathlib import Path

sys.path.insert(0, str(Path('python').resolve()))
import torch
from stable_baselines3 import PPO

zip_path = Path('python/training/artifacts/cardboard-corridor-ppo-v9-rev24/1.0.0/cardboard-corridor-ppo-v9-rev24_sb3.zip')
out_path = Path('python/training/artifacts/cardboard-corridor-ppo-v9-rev24/1.0.0/cardboard-corridor-ppo-v9-rev24.onnx')

model = PPO.load(str(zip_path), device='cpu')
policy = model.policy

# FLOAT input (matches rev16 ONNX signature so backend can swap models seamlessly).
dummy_img = torch.zeros(1, 84, 84, 3, dtype=torch.float32)
dummy_ultra = torch.zeros(1, 1, dtype=torch.float32)

class DiscretePolicyWrapper(torch.nn.Module):
    def __init__(self, sb3_policy):
        super().__init__()
        self.features_extractor = sb3_policy.features_extractor
        self.mlp_extractor = sb3_policy.mlp_extractor
        self.action_net = sb3_policy.action_net

    def forward(self, image, ultrasonic):
        # image is float32 in [0, 255] range from caller; SB3 features_extractor
        # internally divides by 255 only when obs space is uint8. We do it here
        # explicitly so we can use float-typed ONNX input for backend compat.
        image_norm = image / 255.0
        image_chw = image_norm.permute(0, 3, 1, 2).contiguous()
        obs = {"image": image_chw, "ultrasonic": ultrasonic}
        features = self.features_extractor(obs)
        latent_pi, _ = self.mlp_extractor(features)
        return self.action_net(latent_pi)

wrapper = DiscretePolicyWrapper(policy).eval()

# Sanity-check forward
with torch.no_grad():
    test = wrapper(torch.full((1, 84, 84, 3), 128.0), torch.tensor([[0.5]]))
    print(f"forward: logits = {test[0].tolist()}")

torch.onnx.export(
    wrapper, (dummy_img, dummy_ultra), str(out_path),
    input_names=["image", "ultrasonic"], output_names=["action_logits"],
    external_data=False, dynamo=False,
    dynamic_axes={"image": {0: "batch"}, "ultrasonic": {0: "batch"}, "action_logits": {0: "batch"}},
    opset_version=11,
)
print(f"Exported: {out_path} ({out_path.stat().st_size/1024/1024:.2f} MB)")

# Verify signature
import onnx

m = onnx.load(str(out_path))
for inp in m.graph.input:
    dims = [d.dim_value if d.dim_value > 0 else (d.dim_param or '?') for d in inp.type.tensor_type.shape.dim]
    elem = onnx.TensorProto.DataType.Name(inp.type.tensor_type.elem_type)
    print(f"  input {inp.name}: {dims} {elem}")
