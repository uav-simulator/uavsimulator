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
    import torch
    from stable_baselines3 import PPO

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


def test_multimodal_export_preserves_trainer_logits(tmp_path, synthetic_samples):
    """Occupancy SB3 export must match the raw PyTorch BC head numerically."""
    import torch
    from stable_baselines3 import PPO

    cfg = BcConfig(epochs=1, batch_size=4, lr=1e-3, device="cpu", seed=0, use_occupancy=True)
    trainer = BcTrainer(cfg)
    samples = synthetic_samples(4, seed=2)
    samples = [
        type(s)(
            frame=s.frame,
            ultrasonic=s.ultrasonic,
            action_idx=s.action_idx,
            occupancy=np.full((3, 21, 21), fill_value=(i + 1) / 4, dtype=np.float32),
        )
        for i, s in enumerate(samples)
    ]

    ckpt = tmp_path / "bc-occ.zip"
    trainer.export_sb3(ckpt)
    loaded = PPO.load(str(ckpt), device="cpu")

    for s in samples:
        obs = {
            "image": s.frame.transpose(2, 0, 1)[None, :, :, :],
            "ultrasonic": np.array([[s.ultrasonic]], dtype=np.float32),
            "occupancy": s.occupancy[None, :, :, :],
        }
        obs_tensor, _ = loaded.policy.obs_to_tensor(obs)
        with torch.no_grad():
            sb3_logits = loaded.policy.get_distribution(obs_tensor).distribution.logits
            frame_t = torch.from_numpy(s.frame.transpose(2, 0, 1)).float().unsqueeze(0) / 255.0
            ultra_t = torch.tensor([[s.ultrasonic]], dtype=torch.float32)
            occ_t = torch.from_numpy(s.occupancy).float().unsqueeze(0)
            trainer_logits = trainer.model(frame_t, ultra_t, occ_t).log_softmax(dim=-1)
        assert torch.allclose(sb3_logits, trainer_logits, atol=1e-5)


def test_multimodal_export_with_distances_8_preserves_trainer_logits(tmp_path, synthetic_samples):
    """649-d occupancy+distances export must round-trip through PPO.load()."""
    import torch
    from stable_baselines3 import PPO

    cfg = BcConfig(
        epochs=1,
        batch_size=4,
        lr=1e-3,
        device="cpu",
        seed=0,
        use_occupancy=True,
        use_distances_8=True,
    )
    trainer = BcTrainer(cfg)
    samples = synthetic_samples(4, seed=3)
    samples = [
        type(s)(
            frame=s.frame,
            ultrasonic=s.ultrasonic,
            action_idx=s.action_idx,
            occupancy=np.full((3, 21, 21), fill_value=(i + 1) / 4, dtype=np.float32),
            distances_8=np.full((8,), fill_value=(i + 1) / 10, dtype=np.float32),
        )
        for i, s in enumerate(samples)
    ]
    trainer.fit(samples)

    ckpt = tmp_path / "bc-occ-dist.zip"
    trainer.export_sb3(ckpt)
    loaded = PPO.load(str(ckpt), device="cpu")

    for s in samples:
        obs = {
            "image": s.frame.transpose(2, 0, 1)[None, :, :, :],
            "ultrasonic": np.array([[s.ultrasonic]], dtype=np.float32),
            "occupancy": s.occupancy[None, :, :, :],
            "distances_8": s.distances_8[None, :],
        }
        obs_tensor, _ = loaded.policy.obs_to_tensor(obs)
        with torch.no_grad():
            sb3_logits = loaded.policy.get_distribution(obs_tensor).distribution.logits
            frame_t = torch.from_numpy(s.frame.transpose(2, 0, 1)).float().unsqueeze(0) / 255.0
            ultra_t = torch.tensor([[s.ultrasonic]], dtype=torch.float32)
            occ_t = torch.from_numpy(s.occupancy).float().unsqueeze(0)
            dist_t = torch.from_numpy(s.distances_8).float().unsqueeze(0)
            trainer_logits = trainer.model(frame_t, ultra_t, occ_t, dist_t).log_softmax(dim=-1)
        assert torch.allclose(sb3_logits, trainer_logits, atol=1e-5)


def test_frame_stacked_export_loads_into_sb3_ppo(tmp_path, synthetic_samples):
    """Camera-only BC export must preserve k-stacked image/ultrasonic obs shapes."""
    import torch
    from stable_baselines3 import PPO

    cfg = BcConfig(
        epochs=5,
        batch_size=4,
        lr=1e-3,
        device="cpu",
        seed=0,
        frame_stack=4,
    )
    trainer = BcTrainer(cfg)
    base_samples = synthetic_samples(12, seed=4)
    samples = [
        type(s)(
            frame=np.concatenate([s.frame] * 4, axis=2),
            ultrasonic=np.asarray([s.ultrasonic] * 4, dtype=np.float32),
            action_idx=s.action_idx,
        )
        for s in base_samples
    ]
    trainer.fit(samples)

    ckpt = tmp_path / "bc-fs4.zip"
    trainer.export_sb3(ckpt)
    loaded = PPO.load(str(ckpt), device="cpu")

    assert loaded.observation_space.spaces["image"].shape == (12, 84, 84)
    assert loaded.observation_space.spaces["ultrasonic"].shape == (4,)

    s = samples[0]
    obs = {
        "image": s.frame.transpose(2, 0, 1)[None, :, :, :],
        "ultrasonic": np.asarray(s.ultrasonic, dtype=np.float32)[None, :],
    }
    action, _ = loaded.predict(obs, deterministic=True)
    with torch.no_grad():
        frame_t = torch.from_numpy(s.frame.transpose(2, 0, 1)).float().unsqueeze(0) / 255.0
        ultra_t = torch.from_numpy(np.asarray(s.ultrasonic, dtype=np.float32)).float().unsqueeze(0)
        expected = int(trainer.model(frame_t, ultra_t).argmax(-1).item())
    assert int(action[0]) == expected


def test_frame_stacked_multimodal_export_with_distances_8_preserves_trainer_logits(
    tmp_path, synthetic_samples
):
    """Occupancy+distances_8 export must also support temporal image/sonar context."""
    import torch
    from stable_baselines3 import PPO

    cfg = BcConfig(
        epochs=1,
        batch_size=4,
        lr=1e-3,
        device="cpu",
        seed=0,
        frame_stack=4,
        use_occupancy=True,
        use_distances_8=True,
    )
    trainer = BcTrainer(cfg)
    base_samples = synthetic_samples(4, seed=5)
    samples = [
        type(s)(
            frame=np.concatenate([s.frame] * 4, axis=2),
            ultrasonic=np.asarray([s.ultrasonic] * 4, dtype=np.float32),
            action_idx=s.action_idx,
            occupancy=np.full((3, 21, 21), fill_value=(i + 1) / 4, dtype=np.float32),
            distances_8=np.full((8,), fill_value=(i + 1) / 10, dtype=np.float32),
        )
        for i, s in enumerate(base_samples)
    ]
    trainer.fit(samples)

    ckpt = tmp_path / "bc-occ-dist-fs4.zip"
    trainer.export_sb3(ckpt)
    loaded = PPO.load(str(ckpt), device="cpu")

    assert loaded.observation_space.spaces["image"].shape == (12, 84, 84)
    assert loaded.observation_space.spaces["ultrasonic"].shape == (4,)
    assert loaded.observation_space.spaces["occupancy"].shape == (3, 21, 21)
    assert loaded.observation_space.spaces["distances_8"].shape == (8,)

    for s in samples:
        obs = {
            "image": s.frame.transpose(2, 0, 1)[None, :, :, :],
            "ultrasonic": np.asarray(s.ultrasonic, dtype=np.float32)[None, :],
            "occupancy": s.occupancy[None, :, :, :],
            "distances_8": s.distances_8[None, :],
        }
        obs_tensor, _ = loaded.policy.obs_to_tensor(obs)
        with torch.no_grad():
            sb3_logits = loaded.policy.get_distribution(obs_tensor).distribution.logits
            frame_t = torch.from_numpy(s.frame.transpose(2, 0, 1)).float().unsqueeze(0) / 255.0
            ultra_t = torch.from_numpy(np.asarray(s.ultrasonic, dtype=np.float32)).float().unsqueeze(0)
            occ_t = torch.from_numpy(s.occupancy).float().unsqueeze(0)
            dist_t = torch.from_numpy(s.distances_8).float().unsqueeze(0)
            trainer_logits = trainer.model(frame_t, ultra_t, occ_t, dist_t).log_softmax(dim=-1)
        assert torch.allclose(sb3_logits, trainer_logits, atol=1e-5)
