"""Measure how well the classifier's Grad-CAM heatmap overlaps the traffic light region.

Method:
  1. For each (frame, label) sample, compute Grad-CAM on the last conv layer.
  2. Build a bbox-mask proxy for where the traffic light is (heuristic — center-upper region of 84x84 frame).
  3. Compute IoU between top-K saliency pixels and bbox-mask.
  4. Aggregate per-class mean IoU.

Output: markdown table with N samples, mean IoU, per-class IoU.

Caveat: bbox-mask is heuristic — a real pipeline would either (a) log pixel coords of
the traffic light in auto_label.py, or (b) hand-annotate ~100 frames. For Plan B this
heuristic suffices as a proof-of-concept; flagged in the markdown output.
"""
from __future__ import annotations

import argparse
from pathlib import Path

import numpy as np
import torch
from PIL import Image

from .tl_classifier import _SmallCnn, load_dataset_from_jsonl


def gradcam(model, frame_tensor, target_class: int) -> np.ndarray:
    """Simple Grad-CAM on the last conv layer."""
    feats = None
    grads = None

    def fwd_hook(_m, _inp, out):
        nonlocal feats
        feats = out

    def bwd_hook(_m, _gin, gout):
        nonlocal grads
        grads = gout[0]

    target_layer = list(model.body.children())[4]
    f_handle = target_layer.register_forward_hook(fwd_hook)
    b_handle = target_layer.register_full_backward_hook(bwd_hook)

    logits = model(frame_tensor)
    model.zero_grad()
    logits[0, target_class].backward()

    weights = grads.mean(dim=(2, 3), keepdim=True)
    cam = (weights * feats).sum(dim=1).relu().squeeze(0).detach().numpy()
    cam = cam / (cam.max() + 1e-6)

    f_handle.remove()
    b_handle.remove()
    cam_img = Image.fromarray((cam * 255).astype(np.uint8)).resize((84, 84), Image.BILINEAR)
    return np.array(cam_img) / 255.0


def main():
    p = argparse.ArgumentParser()
    p.add_argument("--checkpoint-pt", type=Path, required=True, help="PyTorch .pt state_dict from tl_classifier")
    p.add_argument("--dataset", type=Path, required=True)
    p.add_argument("--output", type=Path, required=True)
    p.add_argument("--n-samples", type=int, default=200)
    args = p.parse_args()

    model = _SmallCnn()
    model.load_state_dict(torch.load(str(args.checkpoint_pt)))

    samples = load_dataset_from_jsonl(args.dataset)
    samples = samples[: args.n_samples]

    bbox_mask = np.zeros((84, 84), dtype=int)
    bbox_mask[10:50, 30:54] = 1

    rows = []
    for i, (frame, label) in enumerate(samples):
        t = torch.from_numpy(frame.transpose(2, 0, 1)).float().unsqueeze(0) / 255.0
        cam = gradcam(model, t, label)
        topk_mask = (cam > np.percentile(cam, 90)).astype(int)
        intersection = (bbox_mask * topk_mask).sum()
        union = ((bbox_mask + topk_mask) > 0).sum()
        iou = intersection / max(union, 1)
        rows.append({"i": i, "label": label, "iou": float(iou)})

    mean_iou = float(np.mean([r["iou"] for r in rows])) if rows else 0.0
    per_class = {
        c: float(np.mean([r["iou"] for r in rows if r["label"] == c]))
        if any(r["label"] == c for r in rows)
        else 0.0
        for c in range(3)
    }

    args.output.parent.mkdir(parents=True, exist_ok=True)
    out_md = (
        "# Saliency-IoU\n\n"
        "Note: bbox-mask is heuristic (center-upper region of 84x84 frame). "
        "Replace with real pixel coords logged in auto_label.py for production-grade analysis.\n\n"
        "| N samples | Mean IoU | Red IoU | Yellow IoU | Green IoU |\n"
        "|---|---|---|---|---|\n"
        f"| {len(rows)} | {mean_iou:.3f} | {per_class[0]:.3f} | {per_class[1]:.3f} | {per_class[2]:.3f} |\n"
    )
    args.output.write_text(out_md)
    print(f"Saliency-IoU report: {args.output}")
    print(f"  Mean IoU: {mean_iou:.3f}; Per-class: {per_class}")


if __name__ == "__main__":
    main()
