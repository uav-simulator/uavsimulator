"""Plan 2 / rev38 ONNX export — supports frame-stacking k=4.

Input shape changes from (1, 84, 84, 3) to (1, 84, 84, 12) when policy was
trained with --frame-stack 4. Backend onnxruntime path needs the same shape
to feed observations.

Usage:
  python python/training/export_v9_rev38_onnx.py [--frame-stack 4]
"""
import argparse
import sys
from pathlib import Path

sys.path.insert(0, str(Path('python').resolve()))
import torch
from stable_baselines3 import PPO


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--frame-stack", type=int, default=4,
                        help="Frame stacking k used during training (default 4)")
    parser.add_argument("--rev", default="rev38",
                        help="Rev tag for paths (default rev38)")
    args = parser.parse_args()

    rev = args.rev
    k = args.frame_stack
    channels = 3 * k

    zip_path = Path(f'python/training/artifacts/cardboard-corridor-ppo-v9-{rev}/1.0.0/cardboard-corridor-ppo-v9-{rev}_sb3.zip')
    out_path = Path(f'python/training/artifacts/cardboard-corridor-ppo-v9-{rev}/1.0.0/cardboard-corridor-ppo-v9-{rev}.onnx')

    if not zip_path.exists():
        raise FileNotFoundError(f"SB3 checkpoint not found: {zip_path}")

    model = PPO.load(str(zip_path), device='cpu')
    policy = model.policy
    dummy_img = torch.zeros(1, 84, 84, channels, dtype=torch.float32)
    # VecFrameStack stacks ultrasonic too: (1,) -> (k,) per timestep.
    dummy_ultra = torch.zeros(1, k, dtype=torch.float32)


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
    print(f"  Input shape: image=(B,84,84,{channels}), ultrasonic=(B,{k})")
    print(f"  Frame stacking: k={k}")


if __name__ == "__main__":
    main()
