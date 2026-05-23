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


def test_prepare_ppo_from_bc_rollout_buffer_size_matches_n_steps(tmp_path, synthetic_samples):
    """Critical: rollout_buffer must be sized n_steps, not SB3 default 2048.

    Regression test for the bug where bc_to_ppo overrode model.n_steps AFTER
    PPO.load() but the buffer was already allocated, causing
    AssertionError in train() because buffer was 2048 but only 256 steps collected.
    """
    from training.bc.trainer import BcConfig, BcTrainer

    cfg = BcConfig(epochs=2, batch_size=4, device="cpu", seed=0)
    trainer = BcTrainer(cfg)
    trainer.fit(synthetic_samples(8))
    bc_zip = tmp_path / "bc.zip"
    trainer.export_sb3(bc_zip)

    out = prepare_ppo_from_bc(bc_zip=bc_zip, config=BcToPpoConfig())
    assert out.n_steps == 256
    assert out.rollout_buffer.buffer_size == 256, (
        f"Expected buffer_size=256, got {out.rollout_buffer.buffer_size}. "
        "This is the rev30+ rollout-buffer-allocation bug."
    )
