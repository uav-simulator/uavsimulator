"""CLI: python -m training.bc fit --demos <dir> --output <bc.zip>."""
from __future__ import annotations

import argparse
from pathlib import Path

from .dataset import discover_pairs, load_dataset
from .trainer import BcConfig, BcTrainer


def main() -> None:
    p = argparse.ArgumentParser()
    sub = p.add_subparsers(dest="command", required=True)

    fit = sub.add_parser("fit", help="Train BC on operator demos.")
    fit.add_argument("--demos", type=Path, required=True, help="Directory with session_*.jsonl + .mp4")
    fit.add_argument("--output", type=Path, required=True, help="Output bc.zip (SB3-compatible).")
    fit.add_argument("--epochs", type=int, default=30)
    fit.add_argument("--batch-size", type=int, default=64)
    fit.add_argument("--lr", type=float, default=3e-4)
    fit.add_argument("--device", default="cpu")
    fit.add_argument("--seed", type=int, default=42)
    fit.add_argument("--frame-stack", type=int, default=1,
                     help="Number of consecutive demo frames to concatenate channel-wise.")
    fit.add_argument(
        "--class-balanced",
        action="store_true",
        help="Use WeightedRandomSampler so each batch has class-uniform expectation. "
             "Prevents mode collapse on imbalanced action corpora (e.g. when ~38%% of "
             "labels are DirForward and the model would otherwise just learn the prior).",
    )
    fit.add_argument(
        "--use-occupancy",
        action="store_true",
        help="Train a multi-modal policy that consumes the ego-centric occupancy map "
             "alongside (image, ultrasonic). Each demo MP4 must have a paired "
             "occupancy_<tag>.npy in the same directory (see training.bc.occupancy "
             "for the offline reconstructor).",
    )
    fit.add_argument(
        "--use-distances-8",
        action="store_true",
        help="Append the structured 8-ray context vector alongside occupancy. "
             "Requires --use-occupancy and paired distances_8_<tag>.npy files.",
    )

    args = p.parse_args()
    if args.command == "fit":
        pairs = discover_pairs(args.demos)
        samples = load_dataset(pairs, frame_stack=args.frame_stack)
        print(f"Loaded {len(samples)} samples from {len(pairs)} sessions")
        cfg = BcConfig(
            epochs=args.epochs,
            batch_size=args.batch_size,
            lr=args.lr,
            device=args.device,
            seed=args.seed,
            frame_stack=args.frame_stack,
            class_balanced=args.class_balanced,
            use_occupancy=args.use_occupancy,
            use_distances_8=args.use_distances_8,
        )
        trainer = BcTrainer(cfg)
        history = trainer.fit(samples)
        args.output.parent.mkdir(parents=True, exist_ok=True)
        trainer.export_sb3(args.output)
        print(f"Saved BC checkpoint: {args.output}")
        final_acc = history["train_accuracy"][-1]
        final_loss = history["train_loss"][-1]
        print(f"Final train: loss={final_loss:.4f}  accuracy={final_acc:.3f}")


if __name__ == "__main__":
    main()
