"""Tests for multi-seed sweep runner.

The sweep is idempotent: a run is considered completed iff sb3.zip exists in
its evidence directory. plan_pending_runs() must skip completed runs so that
SIGTERM-interrupted sweeps can resume without re-running already-finished
seeds.
"""
from __future__ import annotations

from pathlib import Path

from training.bc.multi_seed_sweep import PendingRun, SweepPlan, execute_run, plan_pending_runs


def test_plan_pending_skips_completed_seeds(tmp_path):
    """If seed-N/sb3.zip exists for a seed, that seed is skipped.

    seed-20 has a sweep_metadata.json snapshot (started but never
    produced sb3.zip — e.g. SIGTERM mid-run): it must still be pending.
    The snapshot file is named `sweep_metadata.json` (not
    `metadata.json`) because the underlying train script writes its own
    `metadata.json` and we must not let it clobber the sweep evidence.
    """
    branch_dir = tmp_path / "bc-ppo"
    (branch_dir / "seed-10").mkdir(parents=True)
    (branch_dir / "seed-10" / "sb3.zip").write_bytes(b"fake")
    (branch_dir / "seed-20").mkdir()
    (branch_dir / "seed-20" / "sweep_metadata.json").write_text("{}")

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


def test_execute_run_passes_maze_env_kwargs_as_flat_flags(tmp_path, monkeypatch):
    """Sweep env settings must affect the actual training command.

    The YAML sweep files set `max_steps`, `sim_time_scale`, and
    `maze_randomize`; passing them only as opaque JSON is insufficient unless
    run_single converts them to train_cardboard_corridor_v9 flags.
    """
    captured: list[list[str]] = []

    def fake_run(cmd, stdout=None, stderr=None):
        captured.append(list(cmd))

        class Result:
            returncode = 0

        return Result()

    monkeypatch.setattr("training.bc.multi_seed_sweep.subprocess.run", fake_run)

    plan = SweepPlan(
        branch_name="ppo-fs4",
        evidence_root=tmp_path,
        seeds=[42],
        total_timesteps=30_000,
        scenario="configs/scenarios/cardboard-maze-bc.yaml",
        env_kwargs={
            "base_url": "http://127.0.0.1:8000",
            "max_steps": 600,
            "sim_time_scale": 4.0,
            "maze_randomize": True,
            "maze_regen_every": 1,
        },
        frame_stack=4,
    )

    rc = execute_run(plan, PendingRun(branch="ppo-fs4", seed=42, run_dir=tmp_path / "ppo-fs4" / "seed-42"))

    assert rc == 0
    assert len(captured) == 1
    cmd = captured[0]
    assert "--base-url" in cmd
    assert cmd[cmd.index("--base-url") + 1] == "http://127.0.0.1:8000"
    assert "--max-ep-steps" in cmd
    assert cmd[cmd.index("--max-ep-steps") + 1] == "600"
    assert "--time-scale" in cmd
    assert cmd[cmd.index("--time-scale") + 1] == "4.0"
    assert "--maze-randomize" in cmd
    assert "--maze-regen-every" in cmd
    assert cmd[cmd.index("--maze-regen-every") + 1] == "1"
