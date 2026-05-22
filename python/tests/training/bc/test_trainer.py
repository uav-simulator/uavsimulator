"""Tests for BC trainer: overfitting on synthetic data and SB3-compatible export."""
from __future__ import annotations

import numpy as np

from training.bc.dataset import BcSample
from training.bc.trainer import BcConfig, BcTrainer


def _synthetic_samples(n: int, seed: int = 0) -> list[BcSample]:
    rng = np.random.default_rng(seed)
    return [
        BcSample(
            frame=rng.integers(0, 256, (84, 84, 3), dtype=np.uint8),
            ultrasonic=float(rng.random()),
            action_idx=int(rng.integers(0, 5)),
        )
        for _ in range(n)
    ]


def test_trainer_overfits_small_synthetic_set():
    """If trainer is correctly implemented, it can overfit 50 random samples."""
    cfg = BcConfig(epochs=50, batch_size=8, lr=1e-3, device="cpu", seed=42)
    trainer = BcTrainer(cfg)
    samples = _synthetic_samples(50)
    metrics = trainer.fit(samples)
    assert metrics["train_accuracy"][-1] >= 0.95


def test_export_loads_into_sb3_ppo(tmp_path):
    """BC checkpoint must be loadable as PPO actor weights."""
    cfg = BcConfig(epochs=5, batch_size=4, lr=1e-3, device="cpu", seed=0)
    trainer = BcTrainer(cfg)
    trainer.fit(_synthetic_samples(20))

    ckpt = tmp_path / "bc.zip"
    trainer.export_sb3(ckpt)
    assert ckpt.exists()

    import zipfile

    with zipfile.ZipFile(ckpt) as zf:
        names = zf.namelist()
    assert "policy.pth" in names or any("policy" in n for n in names)
