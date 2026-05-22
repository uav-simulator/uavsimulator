"""Tests for multi-seed sweep runner.

The sweep is idempotent: a run is considered completed iff sb3.zip exists in
its evidence directory. plan_pending_runs() must skip completed runs so that
SIGTERM-interrupted sweeps can resume without re-running already-finished
seeds.
"""
from __future__ import annotations

from pathlib import Path

from training.bc.multi_seed_sweep import SweepPlan, plan_pending_runs


def test_plan_pending_skips_completed_seeds(tmp_path):
    """If seed-N/sb3.zip exists for a seed, that seed is skipped."""
    branch_dir = tmp_path / "bc-ppo"
    (branch_dir / "seed-10").mkdir(parents=True)
    (branch_dir / "seed-10" / "sb3.zip").write_bytes(b"fake")
    (branch_dir / "seed-20").mkdir()
    (branch_dir / "seed-20" / "metadata.json").write_text("{}")

    plan = SweepPlan(
        branch_name="bc-ppo",
        evidence_root=tmp_path,
        seeds=[10, 20, 30],
    )
    pending = plan_pending_runs(plan)
    pending_seeds = sorted(p.seed for p in pending)
    assert pending_seeds == [20, 30]


def test_plan_pending_all_pending_on_empty_evidence(tmp_path):
    """No prior runs == every seed is pending."""
    plan = SweepPlan(
        branch_name="pure-ppo",
        evidence_root=tmp_path,
        seeds=[1, 2, 3],
    )
    pending = plan_pending_runs(plan)
    pending_seeds = sorted(p.seed for p in pending)
    assert pending_seeds == [1, 2, 3]
