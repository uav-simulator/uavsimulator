"""Tests for BC->PPO transfer."""
from __future__ import annotations

from stable_baselines3 import PPO

from training.bc.bc_to_ppo import BcToPpoConfig, prepare_ppo_from_bc


def test_prepare_ppo_from_bc_returns_loaded_model(tmp_path, synthetic_samples):
    """Sanity: BC->PPO loader returns a PPO with overridden hyperparameters."""
    from training.bc.trainer import BcConfig, BcTrainer

    cfg = BcConfig(epochs=2, batch_size=4, device="cpu", seed=0)
    trainer = BcTrainer(cfg)
    trainer.fit(synthetic_samples(8))
    bc_zip = tmp_path / "bc.zip"
    trainer.export_sb3(bc_zip)

    out = prepare_ppo_from_bc(
        bc_zip=bc_zip,
        config=BcToPpoConfig(ent_coef=0.1, learning_rate=3e-4, seed=42),
    )
    assert isinstance(out, PPO)
    assert out.ent_coef == 0.1
    assert out.learning_rate == 3e-4
