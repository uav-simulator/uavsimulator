"""Unified ONNX exporter for cardboard-corridor-PPO-v9 policies.

Replaces the historical ``export_v9_rev{24,25,26,29,30,37,38,39,41,42}_onnx.py``
scripts that differed only in two path strings (and, in two cases, in
optional verification / frame-stack channel count).

Usage::

    python python/training/export_onnx.py --rev rev42
    python python/training/export_onnx.py --rev rev38 --frame-stack 4
    python python/training/export_onnx.py --rev rev24 --verify-signature --sanity-forward
    python python/training/export_onnx.py --rev rev42 --artifacts-root ./other-artifacts

Default artifact layout (matches all historical revisions)::

    python/training/artifacts/cardboard-corridor-ppo-v9-{rev}/1.0.0/
        cardboard-corridor-ppo-v9-{rev}_sb3.zip   (input)
        cardboard-corridor-ppo-v9-{rev}.onnx      (output)

The script intentionally keeps a tiny surface so it can run on the training
Windows box without pulling the whole project layout — only ``torch`` and
``stable_baselines3`` are imported lazily inside :func:`main`.
"""

from __future__ import annotations

import argparse
import sys
from pathlib import Path

PYTHON_ROOT = Path(__file__).resolve().parents[1]
if str(PYTHON_ROOT) not in sys.path:
    sys.path.insert(0, str(PYTHON_ROOT))


def _build_wrapper(policy):  # noqa: ANN001 — SB3 type intentionally untyped
    """Return a torch.nn.Module that takes (image_HWC_uint8_or_float, ultrasonic) and emits action logits.

    Image is normalised by /255 inside the wrapper so the ONNX graph
    accepts whatever the backend feeds (uint8 OR float32 in [0, 255]).
    Channels are permuted from HWC → CHW to match the SB3 features
    extractor expectation.
    """
    import torch  # local import: torch is heavy and only needed at export time

    class DiscretePolicyWrapper(torch.nn.Module):
        def __init__(self, sb3_policy):  # noqa: ANN001
            super().__init__()
            self.features_extractor = sb3_policy.features_extractor
            self.mlp_extractor = sb3_policy.mlp_extractor
            self.action_net = sb3_policy.action_net

        def forward(self, image, ultrasonic):  # noqa: ANN001
            image_norm = image / 255.0
            image_chw = image_norm.permute(0, 3, 1, 2).contiguous()
            obs = {"image": image_chw, "ultrasonic": ultrasonic}
            features = self.features_extractor(obs)
            latent_pi, _ = self.mlp_extractor(features)
            return self.action_net(latent_pi)

    return DiscretePolicyWrapper(policy).eval()


def parse_args(argv: list[str] | None = None) -> argparse.Namespace:
    parser = argparse.ArgumentParser(description="Export a trained cardboard-corridor PPO policy to ONNX.")
    parser.add_argument(
        "--rev",
        required=True,
        help="Revision tag, e.g. 'rev42'. Used to locate the SB3 checkpoint and name the .onnx output.",
    )
    parser.add_argument(
        "--artifacts-root",
        type=Path,
        default=Path("python/training/artifacts"),
        help="Root directory under which `cardboard-corridor-ppo-v9-{rev}/1.0.0/` lives.",
    )
    parser.add_argument(
        "--model-family",
        default="cardboard-corridor-ppo-v9",
        help="Artifact name prefix (default matches the v9 family used by every shipped rev).",
    )
    parser.add_argument(
        "--version",
        default="1.0.0",
        help="Artifact version subdir under the rev directory (default 1.0.0).",
    )
    parser.add_argument(
        "--frame-stack",
        type=int,
        default=1,
        help="Frame stacking k used during training. Most revs trained with k=1; rev38 uses k=4.",
    )
    parser.add_argument(
        "--opset",
        type=int,
        default=11,
        help="ONNX opset version (default 11 to match the backend onnxruntime build).",
    )
    parser.add_argument(
        "--sanity-forward",
        action="store_true",
        help="Before export, run one forward pass on a constant image and print the logits "
        "(useful when a new rev has unusual reward shaping and you want to confirm the wrapper hasn't broken).",
    )
    parser.add_argument(
        "--verify-signature",
        action="store_true",
        help="After export, load the .onnx with the `onnx` package and print every input's "
        "[shape] [dtype] — handy when migrating a backend that expects a specific signature.",
    )
    return parser.parse_args(argv)


def main(argv: list[str] | None = None) -> int:
    args = parse_args(argv)

    import torch
    from stable_baselines3 import PPO

    rev_dir = args.artifacts_root / f"{args.model_family}-{args.rev}" / args.version
    zip_path = rev_dir / f"{args.model_family}-{args.rev}_sb3.zip"
    out_path = rev_dir / f"{args.model_family}-{args.rev}.onnx"

    if not zip_path.exists():
        raise FileNotFoundError(f"SB3 checkpoint not found: {zip_path}")

    model = PPO.load(str(zip_path), device="cpu")
    wrapper = _build_wrapper(model.policy)

    k = args.frame_stack
    channels = 3 * k
    dummy_img = torch.zeros(1, 84, 84, channels, dtype=torch.float32)
    dummy_ultra = torch.zeros(1, k, dtype=torch.float32)

    if args.sanity_forward:
        with torch.no_grad():
            test_img = torch.full((1, 84, 84, channels), 128.0)
            test_ultra = torch.full((1, k), 0.5)
            test_logits = wrapper(test_img, test_ultra)
            print(f"sanity forward: logits = {test_logits[0].tolist()}")

    torch.onnx.export(
        wrapper,
        (dummy_img, dummy_ultra),
        str(out_path),
        input_names=["image", "ultrasonic"],
        output_names=["action_logits"],
        external_data=False,
        dynamo=False,
        dynamic_axes={
            "image": {0: "batch"},
            "ultrasonic": {0: "batch"},
            "action_logits": {0: "batch"},
        },
        opset_version=args.opset,
    )

    size_mb = out_path.stat().st_size / 1024 / 1024
    print(f"Exported {out_path} ({size_mb:.2f} MB)")
    print(f"  Input shape: image=(B,84,84,{channels}), ultrasonic=(B,{k})")
    if k > 1:
        print(f"  Frame stacking: k={k}")

    if args.verify_signature:
        import onnx

        m = onnx.load(str(out_path))
        for inp in m.graph.input:
            dims = [d.dim_value if d.dim_value > 0 else (d.dim_param or "?") for d in inp.type.tensor_type.shape.dim]
            elem = onnx.TensorProto.DataType.Name(inp.type.tensor_type.elem_type)
            print(f"  input {inp.name}: {dims} {elem}")

    return 0


if __name__ == "__main__":
    raise SystemExit(main())
