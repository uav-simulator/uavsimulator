"""Train TL classifier on synthetic color-tinted data — smoke deliverable."""
import sys
sys.path.insert(0, "python")

from pathlib import Path
import numpy as np
from training.traffic.tl_classifier import TlClassifier, TlClassifierConfig

rng = np.random.default_rng(42)
samples = []
# 50 samples per class with realistic class-dominant tint + background noise
for label, dominant_channel in [(0, 0), (1, [0, 1]), (2, 1)]:  # 0=red, 1=yellow=R+G, 2=green
    for _ in range(150):
        f = rng.integers(80, 160, (84, 84, 3), dtype=np.uint8)  # gray-ish background
        # Inject a "traffic light" patch in upper-center
        cy, cx = rng.integers(15, 35), rng.integers(35, 50)
        r = rng.integers(6, 12)
        for dy in range(-r, r+1):
            for dx in range(-r, r+1):
                if dy*dy + dx*dx <= r*r:
                    y, x = cy+dy, cx+dx
                    if 0 <= y < 84 and 0 <= x < 84:
                        if isinstance(dominant_channel, list):
                            f[y, x, dominant_channel[0]] = 240
                            f[y, x, dominant_channel[1]] = 240
                            f[y, x, 2] = 30
                        else:
                            f[y, x, dominant_channel] = 240
                            f[y, x, (dominant_channel+1)%3] = 30
                            f[y, x, (dominant_channel+2)%3] = 30
        samples.append((f, label))

print(f"Generated {len(samples)} synthetic samples ({sum(1 for _, l in samples if l==0)} red, "
      f"{sum(1 for _, l in samples if l==1)} yellow, {sum(1 for _, l in samples if l==2)} green)")

cfg = TlClassifierConfig(epochs=15, batch_size=32, lr=1e-3, device="cpu", seed=42)
clf = TlClassifier(cfg)
hist = clf.fit(samples)
print(f"Final train acc: {hist['train_accuracy'][-1]:.3f}, loss: {hist['train_loss'][-1]:.4f}")

out_dir = Path("docs/report/master-thesis/sprint-4-bc-variance-2026-06/checkpoints")
out_dir.mkdir(parents=True, exist_ok=True)
onnx_path = out_dir / "tl-classifier.onnx"
clf.export_onnx(onnx_path)
clf.save_pt(out_dir / "tl-classifier.pt")

# Also drop into StreamingAssets so OnnxTrafficLightAwareController can find it
streaming = Path("src/UnityProject/uav-simulator/Assets/StreamingAssets")
streaming.mkdir(exist_ok=True)
import shutil
shutil.copy2(onnx_path, streaming / "tl-classifier.onnx")
print(f"ONNX written: {onnx_path} ({onnx_path.stat().st_size} bytes)")
print(f"Streamed to: {streaming / 'tl-classifier.onnx'}")

# Sanity-check inference on each class
clf.model.eval()
import torch
for cls in range(3):
    test_sample = samples[cls * 150 + 50]  # middle sample of each class
    t = torch.from_numpy(test_sample[0].transpose(2,0,1)).float().unsqueeze(0) / 255.0
    pred = clf.model(t).argmax(-1).item()
    print(f"  class {cls} actual → pred {pred} {'✓' if pred == cls else '✗'}")
