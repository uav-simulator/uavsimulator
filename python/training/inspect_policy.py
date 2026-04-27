"""Inspect a trained policy: feed it an image + ultrasonic value, see what
action it picks and with what confidence. Useful for debugging sim2real gaps.

Examples:
    # Single image, default ultrasonic = 0.5 (mid-range)
    python python/training/inspect_policy.py \\
        --model python/training/artifacts/cardboard-corridor-ppo-v9-rev16/1.0.0/cardboard-corridor-ppo-v9-rev16.onnx \\
        --image some_camera_frame.jpg

    # Multiple images, varying ultrasonic
    python python/training/inspect_policy.py \\
        --model rev16.onnx \\
        --image-glob 'real_frames/*.jpg' \\
        --ultrasonic 0.05

    # Live: connect to backend MJPEG stream and run inference per frame
    python python/training/inspect_policy.py \\
        --model rev16.onnx \\
        --mjpeg http://127.0.0.1:5287/api/camera/mjpeg?clientId=web&runtimeMode=real-robot \\
        --ultrasonic-from-backend
"""
from __future__ import annotations

import argparse
import io
import sys
import time
from pathlib import Path
from typing import Optional

import numpy as np
from PIL import Image

ACTION_NAMES = ["DirStop", "DirForward", "DirBack", "DirLeft", "DirRight"]


def softmax(x: np.ndarray) -> np.ndarray:
    e = np.exp(x - x.max())
    return e / e.sum()


def load_predictor(model_path: Path):
    """Returns (predict_fn, expected_img_size). predict_fn(image_uint8, ultra_norm) -> logits."""
    suffix = model_path.suffix.lower()
    if suffix == ".onnx":
        import onnxruntime as ort
        sess = ort.InferenceSession(str(model_path), providers=["CPUExecutionProvider"])
        in_names = [i.name for i in sess.get_inputs()]
        in_shapes = [i.shape for i in sess.get_inputs()]
        # First input is image: shape (batch, H, W, C); H may be dynamic str
        img_shape = in_shapes[0]
        H = img_shape[1] if isinstance(img_shape[1], int) else 84
        def predict(image_u8: np.ndarray, ultra_norm: float) -> np.ndarray:
            img = image_u8.astype(np.float32)[np.newaxis, ...]
            ult = np.array([[ultra_norm]], dtype=np.float32)
            return sess.run(None, {in_names[0]: img, in_names[1]: ult})[0][0]
        return predict, H
    if suffix == ".zip":
        from stable_baselines3 import PPO
        model = PPO.load(str(model_path), device="cpu")
        H = model.observation_space.spaces["image"].shape[0]
        def predict(image_u8: np.ndarray, ultra_norm: float) -> np.ndarray:
            obs = {
                "image": image_u8[np.newaxis, ...].astype(np.uint8),
                "ultrasonic": np.array([[ultra_norm]], dtype=np.float32),
            }
            with __import__("torch").no_grad():
                import torch
                obs_t = {k: torch.tensor(v) for k, v in obs.items()}
                _, values, log_probs = model.policy.evaluate_actions(
                    obs_t, torch.tensor([0])
                )
                # actually we want logits not log_probs at action 0 — easier path:
                features = model.policy.extract_features(obs_t)
                latent_pi, _ = model.policy.mlp_extractor(features)
                logits = model.policy.action_net(latent_pi)
                return logits[0].numpy()
        return predict, H
    raise ValueError(f"Unsupported model format: {suffix}")


def load_image(path: Path, size: int) -> np.ndarray:
    img = Image.open(path).convert("RGB").resize((size, size), Image.BILINEAR)
    return np.asarray(img, dtype=np.uint8)


def print_decision(logits: np.ndarray, label: str = ""):
    probs = softmax(logits)
    chosen = int(np.argmax(logits))
    if label:
        print(f"=== {label} ===")
    for i, (name, p) in enumerate(zip(ACTION_NAMES, probs)):
        marker = "  ★" if i == chosen else "   "
        bar = "█" * int(p * 40)
        print(f"  {name:<11} {p*100:>5.1f}%  {bar} {marker}")
    print(f"  → CHOSEN: {ACTION_NAMES[chosen]}")
    print()


def main() -> int:
    ap = argparse.ArgumentParser()
    ap.add_argument("--model", required=True, help="ONNX or SB3 zip")
    ap.add_argument("--image", help="Single image path")
    ap.add_argument("--image-glob", help="Glob pattern for many images, e.g. 'frames/*.jpg'")
    ap.add_argument("--ultrasonic", type=float, default=0.5,
                    help="Normalized ultrasonic distance (0..1, real_distance_m / 5.0). Default 0.5 = 2.5m")
    ap.add_argument("--ultrasonic-cm", type=float,
                    help="Convenience: pass ultrasonic in cm instead of normalized")
    ap.add_argument("--mjpeg", help="MJPEG URL for live inference")
    ap.add_argument("--mjpeg-fps", type=float, default=5.0)
    ap.add_argument("--max-frames", type=int, default=0,
                    help="For --mjpeg, stop after N frames (0 = forever)")
    args = ap.parse_args()

    model_path = Path(args.model).expanduser().resolve()
    if not model_path.exists():
        print(f"Model not found: {model_path}", file=sys.stderr)
        return 2

    predict, img_size = load_predictor(model_path)
    print(f"Loaded {model_path.name}")
    print(f"  expected image size: {img_size}x{img_size}")

    ultra_norm = args.ultrasonic if args.ultrasonic_cm is None else (args.ultrasonic_cm / 100.0) / 5.0
    ultra_norm = float(np.clip(ultra_norm, 0.0, 1.0))
    print(f"  ultrasonic input: {ultra_norm:.3f} (~{ultra_norm*5*100:.0f} cm)")
    print()

    if args.image:
        img = load_image(Path(args.image), img_size)
        logits = predict(img, ultra_norm)
        print_decision(logits, label=args.image)
        return 0

    if args.image_glob:
        from glob import glob
        paths = sorted(glob(args.image_glob))
        if not paths:
            print(f"No images matched glob: {args.image_glob}")
            return 1
        for p in paths:
            img = load_image(Path(p), img_size)
            logits = predict(img, ultra_norm)
            print_decision(logits, label=Path(p).name)
        return 0

    if args.mjpeg:
        import requests
        print(f"Streaming from {args.mjpeg}; ctrl-C to stop")
        sess = requests.Session()
        period = 1.0 / max(args.mjpeg_fps, 0.1)
        n = 0
        boundary = None
        with sess.get(args.mjpeg, stream=True, timeout=10) as resp:
            buf = b""
            for chunk in resp.iter_content(8192):
                buf += chunk
                while True:
                    soi = buf.find(b"\xff\xd8")  # JPEG start
                    eoi = buf.find(b"\xff\xd9", soi + 2)  # JPEG end
                    if soi < 0 or eoi < 0:
                        break
                    frame_bytes = buf[soi:eoi + 2]
                    buf = buf[eoi + 2:]
                    img = Image.open(io.BytesIO(frame_bytes)).convert("RGB").resize(
                        (img_size, img_size), Image.BILINEAR
                    )
                    arr = np.asarray(img, dtype=np.uint8)
                    logits = predict(arr, ultra_norm)
                    print_decision(logits, label=f"frame #{n} t={time.strftime('%H:%M:%S')}")
                    n += 1
                    if args.max_frames > 0 and n >= args.max_frames:
                        return 0
                    time.sleep(period)
        return 0

    print("ERROR: pass --image, --image-glob, or --mjpeg", file=sys.stderr)
    return 1


if __name__ == "__main__":
    raise SystemExit(main())
