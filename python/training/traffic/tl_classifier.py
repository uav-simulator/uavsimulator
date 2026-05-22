"""3-class image classifier for traffic-light state recognition.

Trained on (frame_84x84_RGB, label_idx) dataset from auto_label.py.
Exports to ONNX consumable by OnnxClassifierService (Unity Sentis).
"""
from __future__ import annotations

import json
from dataclasses import dataclass
from pathlib import Path

import numpy as np
import torch
import torch.nn as nn
import torch.nn.functional as F
from torch.utils.data import DataLoader, Dataset


@dataclass
class TlClassifierConfig:
    epochs: int = 20
    batch_size: int = 64
    lr: float = 1e-3
    device: str = "cpu"
    seed: int = 42


class _TlDataset(Dataset):
    def __init__(self, samples: list[tuple[np.ndarray, int]]):
        self.samples = samples

    def __len__(self):
        return len(self.samples)

    def __getitem__(self, idx):
        frame, label = self.samples[idx]
        t = torch.from_numpy(frame.transpose(2, 0, 1)).float() / 255.0
        return t, torch.tensor(label, dtype=torch.long)


class _SmallCnn(nn.Module):
    def __init__(self, n_classes: int = 3):
        super().__init__()
        self.body = nn.Sequential(
            nn.Conv2d(3, 16, 5, 2, 2), nn.ReLU(),
            nn.Conv2d(16, 32, 5, 2, 2), nn.ReLU(),
            nn.Conv2d(32, 64, 3, 2, 1), nn.ReLU(),
            nn.AdaptiveAvgPool2d(4),
            nn.Flatten(),
            nn.Linear(64 * 4 * 4, 128), nn.ReLU(),
            nn.Linear(128, n_classes),
        )

    def forward(self, x):
        return self.body(x)


class TlClassifier:
    def __init__(self, cfg: TlClassifierConfig):
        self.cfg = cfg
        torch.manual_seed(cfg.seed)
        np.random.seed(cfg.seed)
        self.device = torch.device(cfg.device)
        self.model = _SmallCnn().to(self.device)
        self.opt = torch.optim.Adam(self.model.parameters(), lr=cfg.lr)

    def fit(self, samples: list[tuple[np.ndarray, int]]) -> dict:
        ds = _TlDataset(samples)
        loader = DataLoader(ds, batch_size=self.cfg.batch_size, shuffle=True)
        history = {"train_loss": [], "train_accuracy": []}
        for _epoch in range(self.cfg.epochs):
            self.model.train()
            ep_loss, correct, total = 0.0, 0, 0
            for frame, label in loader:
                frame, label = frame.to(self.device), label.to(self.device)
                logits = self.model(frame)
                loss = F.cross_entropy(logits, label)
                self.opt.zero_grad()
                loss.backward()
                self.opt.step()
                ep_loss += loss.item() * label.size(0)
                correct += (logits.argmax(-1) == label).sum().item()
                total += label.size(0)
            history["train_loss"].append(ep_loss / total)
            history["train_accuracy"].append(correct / total)
        return history

    def export_onnx(self, output_path: Path) -> None:
        self.model.eval()
        dummy = torch.zeros(1, 3, 84, 84)
        torch.onnx.export(
            self.model,
            dummy,
            str(output_path),
            input_names=["image"],
            output_names=["logits"],
            dynamic_axes={"image": {0: "batch"}, "logits": {0: "batch"}},
            opset_version=11,
        )

    def save_pt(self, output_path: Path) -> None:
        torch.save(self.model.state_dict(), str(output_path))


def load_dataset_from_jsonl(jsonl_path: Path) -> list[tuple[np.ndarray, int]]:
    from PIL import Image

    base = jsonl_path.parent
    out: list[tuple[np.ndarray, int]] = []
    for line in jsonl_path.read_text().splitlines():
        if not line.strip():
            continue
        row = json.loads(line)
        frame = np.array(Image.open(base / row["frame"]).convert("RGB"))
        out.append((frame, int(row["label_idx"])))
    return out


def main():
    import argparse

    p = argparse.ArgumentParser()
    sub = p.add_subparsers(dest="command", required=True)
    fit = sub.add_parser("fit")
    fit.add_argument("--dataset", type=Path, required=True, help="dataset.jsonl path")
    fit.add_argument("--output", type=Path, required=True, help="output .onnx path")
    fit.add_argument("--epochs", type=int, default=20)
    fit.add_argument("--device", default="cpu")
    fit.add_argument("--seed", type=int, default=42)
    args = p.parse_args()

    if args.command == "fit":
        samples = load_dataset_from_jsonl(args.dataset)
        rng = np.random.default_rng(args.seed)
        idx = rng.permutation(len(samples))
        train_n = int(0.8 * len(samples))
        train = [samples[i] for i in idx[:train_n]]
        val = [samples[i] for i in idx[train_n:]]

        cfg = TlClassifierConfig(epochs=args.epochs, device=args.device, seed=args.seed)
        clf = TlClassifier(cfg)
        history = clf.fit(train)

        clf.model.eval()
        correct = total = 0
        with torch.no_grad():
            for frame, label in val:
                t = torch.from_numpy(frame.transpose(2, 0, 1)).float().unsqueeze(0) / 255.0
                pred = clf.model(t).argmax(-1).item()
                correct += int(pred == label)
                total += 1
        val_acc = correct / max(total, 1)

        args.output.parent.mkdir(parents=True, exist_ok=True)
        clf.export_onnx(args.output)
        clf.save_pt(args.output.with_suffix(".pt"))
        metrics_path = args.output.with_suffix(".metrics.json")
        metrics_path.write_text(
            json.dumps(
                {
                    "train_loss": history["train_loss"],
                    "train_accuracy": history["train_accuracy"],
                    "val_accuracy": val_acc,
                    "n_train": len(train),
                    "n_val": len(val),
                },
                indent=2,
            )
        )
        print(f"Train acc {history['train_accuracy'][-1]:.3f}, Val acc {val_acc:.3f}")
        print(f"ONNX: {args.output}; pt: {args.output.with_suffix('.pt')}; metrics: {metrics_path}")


if __name__ == "__main__":
    main()
