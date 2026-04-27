"""Compute Grad-CAM saliency heatmap for a CNN policy + given image.

Shows what the CNN 'looks at' to make its action decision. Useful for
debugging sim2real failures: if the heatmap focuses on irrelevant parts of
the image (e.g. random background pixels rather than the wall edge), the
model has learned a spurious feature — likely cause of mode collapse.

Usage:
    # Single image
    python python/training/policy_saliency.py \\
        --model python/training/artifacts/cardboard-corridor-ppo-v9-rev16/1.0.0/cardboard-corridor-ppo-v9-rev16_sb3.zip \\
        --image some_frame.jpg \\
        --output saliency.png \\
        --ultrasonic-cm 30

    # Batch over many images (real-robot frames)
    python python/training/policy_saliency.py \\
        --model rev16.zip \\
        --image-glob 'real_frames/*.jpg' \\
        --out-dir saliency_outputs/

    # From MJPEG stream (live)
    python python/training/policy_saliency.py \\
        --model rev16.zip \\
        --mjpeg http://127.0.0.1:5287/api/camera/mjpeg?clientId=web&runtimeMode=real-robot \\
        --out-dir saliency_live/ \\
        --max-frames 30
"""
from __future__ import annotations

import argparse
import io
import sys
import time
from pathlib import Path

import numpy as np
import torch
import torch.nn.functional as F
from PIL import Image, ImageDraw, ImageFont

ACTION_NAMES = ["DirStop", "DirForward", "DirBack", "DirLeft", "DirRight"]


def load_sb3_model(path: Path):
    from stable_baselines3 import PPO
    return PPO.load(str(path), device="cpu")


def compute_saliency(model, image_u8: np.ndarray, ultra_norm: float):
    """Returns (chosen_action_idx, action_probs, saliency_map_HxW)."""
    H, W = image_u8.shape[:2]
    image_chw = torch.tensor(image_u8.transpose(2, 0, 1).astype(np.float32))
    image_in = image_chw.unsqueeze(0).clone().detach().requires_grad_(True)
    image_in.retain_grad()
    ultra_t = torch.tensor([[ultra_norm]], dtype=torch.float32)
    obs = {"image": image_in, "ultrasonic": ultra_t}

    features = model.policy.features_extractor(obs)
    latent_pi, _ = model.policy.mlp_extractor(features)
    logits = model.policy.action_net(latent_pi)  # (1, 5)

    probs = F.softmax(logits, dim=-1)[0].detach().numpy()
    chosen = int(np.argmax(probs))

    # Backprop the chosen logit to image
    target_logit = logits[0, chosen]
    target_logit.backward()

    grad = image_in.grad
    if grad is None:
        return chosen, probs, np.zeros((H, W), dtype=np.float32)
    grad_np = grad.detach().numpy()[0]  # (3, H, W)

    sal = np.abs(grad_np).sum(axis=0)  # (H, W)
    if sal.max() > 0:
        sal = sal / sal.max()
    return chosen, probs, sal


def overlay_heatmap(image_u8: np.ndarray, sal: np.ndarray, alpha: float = 0.55) -> np.ndarray:
    """Blend a viridis-like heatmap of `sal` onto image_u8 (HxWx3 uint8)."""
    H, W = image_u8.shape[:2]
    if sal.shape != (H, W):
        sal_pil = Image.fromarray((sal * 255).astype(np.uint8)).resize((W, H), Image.BILINEAR)
        sal = np.asarray(sal_pil, dtype=np.float32) / 255.0

    # Simple yellow-red colormap (no matplotlib dep)
    sal_clip = np.clip(sal, 0, 1)
    r = np.clip(sal_clip * 2.0, 0, 1) * 255
    g = np.clip((1.0 - sal_clip) * 1.2 + sal_clip * 0.5, 0, 1) * 0.6 * 255
    b = np.clip(1.0 - sal_clip, 0, 1) * 0.2 * 255
    heat = np.stack([r, g, b], axis=-1).astype(np.uint8)

    blended = (image_u8.astype(np.float32) * (1 - alpha * sal_clip[..., None])
               + heat.astype(np.float32) * (alpha * sal_clip[..., None])).astype(np.uint8)
    return blended


def annotate_decision(img_u8: np.ndarray, probs: np.ndarray, chosen: int, ultra_norm: float) -> np.ndarray:
    pil = Image.fromarray(img_u8)
    draw = ImageDraw.Draw(pil)
    try:
        font = ImageFont.truetype("/System/Library/Fonts/Menlo.ttc", 11)
    except Exception:
        font = ImageFont.load_default()
    lines = [f"{name}: {p*100:5.1f}%" + (" ★" if i == chosen else "") for i, (name, p) in enumerate(zip(ACTION_NAMES, probs))]
    lines.append(f"sonar: {ultra_norm*5*100:.0f} cm  →  {ACTION_NAMES[chosen]}")
    y = 4
    for line in lines:
        draw.rectangle([(2, y - 1), (170, y + 12)], fill=(0, 0, 0))
        draw.text((4, y), line, fill=(220, 230, 250), font=font)
        y += 13
    return np.asarray(pil)


def process_one(model, image_u8: np.ndarray, ultra_norm: float, out_path: Path):
    chosen, probs, sal = compute_saliency(model, image_u8, ultra_norm)
    overlay = overlay_heatmap(image_u8, sal)
    side_by_side = np.concatenate([image_u8, overlay], axis=1)
    annotated = annotate_decision(side_by_side, probs, chosen, ultra_norm)
    out_path.parent.mkdir(parents=True, exist_ok=True)
    Image.fromarray(annotated).save(out_path)
    print(f"  → {out_path.name}: {ACTION_NAMES[chosen]} (probs: " +
          ", ".join(f"{n}={p*100:.0f}%" for n, p in zip(ACTION_NAMES, probs)) + ")")


def main() -> int:
    ap = argparse.ArgumentParser()
    ap.add_argument("--model", required=True, help="SB3 zip path (need torch model for gradients)")
    ap.add_argument("--image", help="Single image path")
    ap.add_argument("--image-glob", help="Glob pattern of many images")
    ap.add_argument("--mjpeg", help="MJPEG URL for live capture")
    ap.add_argument("--max-frames", type=int, default=20, help="For --mjpeg mode")
    ap.add_argument("--ultrasonic-cm", type=float, default=50.0,
                    help="Mock ultrasonic reading in cm (since saliency tool doesn't have live telemetry)")
    ap.add_argument("--output", help="Output PNG (single-image mode)")
    ap.add_argument("--out-dir", default="saliency_outputs", help="Output dir for batch/live mode")
    args = ap.parse_args()

    model_path = Path(args.model).expanduser().resolve()
    if not model_path.exists() or model_path.suffix.lower() != ".zip":
        print(f"Error: --model must be SB3 .zip; got {model_path}", file=sys.stderr)
        return 2
    print(f"Loading {model_path.name}...")
    model = load_sb3_model(model_path)
    # observation_space.spaces["image"].shape is (C, H, W) — channels first.
    # Take the largest dim as spatial size.
    img_shape = model.observation_space.spaces["image"].shape
    img_size = max(img_shape)
    print(f"  expected image: {img_size}x{img_size}")

    ultra_norm = float(np.clip(args.ultrasonic_cm / 100.0 / 5.0, 0.0, 1.0))

    def load(path: Path) -> np.ndarray:
        pil = Image.open(path).convert("RGB").resize((img_size, img_size), Image.BILINEAR)
        return np.asarray(pil, dtype=np.uint8)

    if args.image:
        img = load(Path(args.image))
        out = Path(args.output or "saliency.png")
        process_one(model, img, ultra_norm, out)
        return 0

    if args.image_glob:
        from glob import glob
        out_dir = Path(args.out_dir)
        for p in sorted(glob(args.image_glob)):
            img = load(Path(p))
            process_one(model, img, ultra_norm, out_dir / f"sal_{Path(p).stem}.png")
        return 0

    if args.mjpeg:
        import requests
        out_dir = Path(args.out_dir)
        out_dir.mkdir(parents=True, exist_ok=True)
        n = 0
        with requests.Session().get(args.mjpeg, stream=True, timeout=10) as r:
            buf = b""
            for chunk in r.iter_content(8192):
                buf += chunk
                while True:
                    soi = buf.find(b"\xff\xd8")
                    eoi = buf.find(b"\xff\xd9", soi + 2)
                    if soi < 0 or eoi < 0:
                        break
                    frame_bytes = buf[soi:eoi + 2]
                    buf = buf[eoi + 2:]
                    pil = Image.open(io.BytesIO(frame_bytes)).convert("RGB").resize(
                        (img_size, img_size), Image.BILINEAR)
                    img = np.asarray(pil, dtype=np.uint8)
                    process_one(model, img, ultra_norm, out_dir / f"sal_{n:04d}.png")
                    n += 1
                    if n >= args.max_frames:
                        return 0
                    time.sleep(0.5)
        return 0

    print("ERROR: pass --image, --image-glob, or --mjpeg", file=sys.stderr)
    return 1


if __name__ == "__main__":
    raise SystemExit(main())
