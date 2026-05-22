"""Tests for BC trainer: overfitting on synthetic data and SB3-compatible export."""
from __future__ import annotations

import numpy as np

from training.bc.trainer import BcConfig, BcTrainer


def test_trainer_overfits_small_synthetic_set(synthetic_samples):
    """If trainer is correctly implemented, it can overfit 50 random samples."""
    cfg = BcConfig(epochs=50, batch_size=8, lr=1e-3, device="cpu", seed=42)
    trainer = BcTrainer(cfg)
    samples = synthetic_samples(50)
    metrics = trainer.fit(samples)
    assert metrics["train_accuracy"][-1] >= 0.95


def test_export_loads_into_sb3_ppo(tmp_path, synthetic_samples):
    """BC checkpoint must round-trip through PPO.load and predict the same action."""
    from stable_baselines3 import PPO
    import torch

    cfg = BcConfig(epochs=20, batch_size=4, lr=1e-3, device="cpu", seed=0)
    trainer = BcTrainer(cfg)
    samples = synthetic_samples(40, seed=1)
    trainer.fit(samples)

    ckpt = tmp_path / "bc.zip"
    trainer.export_sb3(ckpt)
    assert ckpt.exists()

    # Load and verify policy predicts the same actions as the trainer's model.
    loaded = PPO.load(str(ckpt), device="cpu")

    # Pick a few samples and compare.
    mismatches = 0
    n_check = min(10, len(samples))
    for i in range(n_check):
        s = samples[i]
        obs = {
            "image": s.frame.transpose(2, 0, 1)[None, :, :, :],   # add batch dim, CHW uint8
            "ultrasonic": np.array([[s.ultrasonic]], dtype=np.float32),
        }
        action, _ = loaded.predict(obs, deterministic=True)
        # Compute trainer's argmax for the same sample
        with torch.no_grad():
            frame_t = torch.from_numpy(s.frame.transpose(2, 0, 1)).float().unsqueeze(0) / 255.0
            ultra_t = torch.tensor([[s.ultrasonic]], dtype=torch.float32)
            expected = int(trainer.model(frame_t, ultra_t).argmax(-1).item())
        if int(action[0]) != expected:
            mismatches += 1
    assert mismatches == 0, f"round-trip mismatch: {mismatches}/{n_check}"
