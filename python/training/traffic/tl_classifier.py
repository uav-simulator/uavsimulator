"""Image classifier for traffic-light state recognition.

Trained on (frame_84x84_RGB, label_idx) dataset from auto_label.py or
runtime city mini-datasets captured from ``modelFrame``.
Exports to ONNX consumable by OnnxClassifierService (Unity Sentis).
"""
from __future__ import annotations

import json
from dataclasses import dataclass
from pathlib import Path
from typing import Any

import numpy as np
import torch
import torch.nn.functional as F
from torch import nn
from torch.utils.data import DataLoader, Dataset


@dataclass
class TlClassifierConfig:
    epochs: int = 20
    batch_size: int = 64
    lr: float = 1e-3
    device: str = "cpu"
    seed: int = 42
    n_classes: int = 3


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
        self.model = _SmallCnn(n_classes=cfg.n_classes).to(self.device)
        self.opt = torch.optim.Adam(self.model.parameters(), lr=cfg.lr)

    def fit(self, samples: list[tuple[np.ndarray, int]]) -> dict:
        ds = _TlDataset(samples)
        loader = DataLoader(ds, batch_size=self.cfg.batch_size, shuffle=True)
        history = {"train_loss": [], "train_accuracy": []}
        for _epoch in range(self.cfg.epochs):
            self.model.train()
            ep_loss, correct, total = 0.0, 0, 0
            for frame, label in loader:
                frames = frame.to(self.device)
                labels = label.to(self.device)
                logits = self.model(frames)
                loss = F.cross_entropy(logits, labels)
                self.opt.zero_grad()
                loss.backward()
                self.opt.step()
                ep_loss += loss.item() * labels.size(0)
                correct += (logits.argmax(-1) == labels).sum().item()
                total += labels.size(0)
            history["train_loss"].append(ep_loss / total)
            history["train_accuracy"].append(correct / total)
        return history

    def export_onnx(self, output_path: Path) -> None:
        self.model.eval()
        original_device = self.device
        self.model.to("cpu")
        # Wrap with softmax — Unity Sentis consumer (OnnxClassifierService) expects
        # probabilities, not raw logits; tests assert sum-to-one.
        export_model = nn.Sequential(self.model, nn.Softmax(dim=-1)).cpu()
        export_model.eval()
        dummy = torch.zeros(1, 3, 84, 84)
        try:
            torch.onnx.export(
                export_model,
                dummy,
                str(output_path),
                input_names=["image"],
                output_names=["probabilities"],
                dynamic_axes={"image": {0: "batch"}, "probabilities": {0: "batch"}},
                opset_version=18,
            )
            # Inline external-data sidecar back into the .onnx file. PyTorch's exporter
            # writes tensors >1024 bytes to a sibling .data file; Unity Sentis 2.x
            # can't follow the sidecar inside StreamingAssets, so we round-trip via
            # onnx.load (loads sidecar into memory) + clear EXTERNAL markers + save.
            import onnx
            model = onnx.load(str(output_path))  # loads sidecar data automatically
            for tensor in model.graph.initializer:
                if tensor.data_location == onnx.TensorProto.EXTERNAL:
                    tensor.data_location = onnx.TensorProto.DEFAULT
                    tensor.ClearField("external_data")
            onnx.save(model, str(output_path), save_as_external_data=False)
            sidecar = output_path.with_suffix(output_path.suffix + ".data")
            if sidecar.exists():
                sidecar.unlink()
        finally:
            self.model.to(original_device)

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
        frame_rel = _resolve_frame_path(row)
        label = _resolve_label(row)
        frame = Image.open(base / frame_rel).convert("RGB")
        if frame.size != (84, 84):
            frame = frame.resize((84, 84))
        out.append((np.array(frame), label))
    return out


def _resolve_frame_path(row: dict[str, Any]) -> str:
    for key in ("modelInputPath", "frame", "framePath"):
        value = row.get(key)
        if isinstance(value, str) and value:
            return value
    raise KeyError("dataset row has no frame path: expected modelInputPath, frame, or framePath")


def _resolve_label(row: dict[str, Any]) -> int:
    for key in ("label_idx", "labelId"):
        value = row.get(key)
        if value is not None:
            return int(value)
    raise KeyError("dataset row has no label id: expected label_idx or labelId")


def _class_counts(samples: list[tuple[np.ndarray, int]], n_classes: int) -> list[int]:
    counts = [0] * n_classes
    for _, label in samples:
        if 0 <= label < n_classes:
            counts[label] += 1
    return counts


def _eval_classifier(clf: TlClassifier, samples: list[tuple[np.ndarray, int]]) -> dict[str, Any]:
    n_classes = clf.cfg.n_classes
    confusion = [[0 for _ in range(n_classes)] for _ in range(n_classes)]
    correct = total = 0
    clf.model.eval()
    with torch.no_grad():
        for frame, label in samples:
            t = torch.from_numpy(frame.transpose(2, 0, 1)).float().unsqueeze(0) / 255.0
            logits = clf.model(t.to(clf.device))
            pred = int(logits.argmax(-1).item())
            if 0 <= label < n_classes and 0 <= pred < n_classes:
                confusion[label][pred] += 1
            correct += int(pred == label)
            total += 1
    return {
        "accuracy": correct / max(total, 1),
        "confusion": confusion,
        "n": total,
    }


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
    fit.add_argument("--classes", type=int, default=3)
    fit.add_argument("--label-names", default="Red,Yellow,Green")
    args = p.parse_args()

    if args.command == "fit":
        samples = load_dataset_from_jsonl(args.dataset)
        if not samples:
            raise ValueError(f"Dataset is empty: {args.dataset}")
        bad_labels = sorted({label for _, label in samples if label < 0 or label >= args.classes})
        if bad_labels:
            raise ValueError(f"Labels {bad_labels} outside configured class range 0..{args.classes - 1}")

        rng = np.random.default_rng(args.seed)
        idx = rng.permutation(len(samples))
        train_n = int(0.8 * len(samples))
        train = [samples[i] for i in idx[:train_n]]
        val = [samples[i] for i in idx[train_n:]]

        cfg = TlClassifierConfig(epochs=args.epochs, device=args.device, seed=args.seed, n_classes=args.classes)
        clf = TlClassifier(cfg)
        history = clf.fit(train)

        train_eval = _eval_classifier(clf, train)
        val_eval = _eval_classifier(clf, val)
        label_names = [x.strip() for x in args.label_names.split(",") if x.strip()]
        if len(label_names) != args.classes:
            label_names = [str(i) for i in range(args.classes)]

        args.output.parent.mkdir(parents=True, exist_ok=True)
        clf.export_onnx(args.output)
        clf.save_pt(args.output.with_suffix(".pt"))
        metrics_path = args.output.with_suffix(".metrics.json")
        metrics_path.write_text(
            json.dumps(
                {
                    "train_loss": history["train_loss"],
                    "train_accuracy": history["train_accuracy"],
                    "train_eval_accuracy": train_eval["accuracy"],
                    "val_accuracy": val_eval["accuracy"],
                    "train_confusion": train_eval["confusion"],
                    "val_confusion": val_eval["confusion"],
                    "class_counts": _class_counts(samples, args.classes),
                    "label_names": label_names,
                    "classes": args.classes,
                    "n_train": len(train),
                    "n_val": len(val),
                    "dataset": str(args.dataset),
                    "seed": args.seed,
                    "epochs": args.epochs,
                },
                indent=2,
            )
        )
        print(f"Train acc {history['train_accuracy'][-1]:.3f}, Val acc {val_eval['accuracy']:.3f}")
        print(f"ONNX: {args.output}; pt: {args.output.with_suffix('.pt')}; metrics: {metrics_path}")


if __name__ == "__main__":
    main()
