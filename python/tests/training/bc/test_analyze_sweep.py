"""Tests for sweep evidence analysis: per-branch summaries gating on sb3.zip."""
import json

from training.bc.analyze_sweep import BranchSummary, summarize_branch


def test_summarize_branch_reads_completed_seeds(tmp_path):
    branch = tmp_path / "bc-ppo"
    branch.mkdir()
    for sr, seed in [(0.75, 10), (0.60, 20), (0.80, 30)]:
        sd = branch / f"seed-{seed}"
        sd.mkdir()
        (sd / "sb3.zip").write_bytes(b"fake")
        (sd / "metrics.json").write_text(json.dumps({"success_rate": sr, "seed": seed}))

    summary = summarize_branch(branch)
    assert isinstance(summary, BranchSummary)
    assert summary.n == 3
    assert 0.71 < summary.mean < 0.72
    assert summary.values == [0.75, 0.60, 0.80]


def test_summarize_branch_skips_incomplete(tmp_path):
    branch = tmp_path / "pure-ppo"
    branch.mkdir()
    (branch / "seed-10").mkdir()
    (branch / "seed-10" / "metrics.json").write_text(json.dumps({"success_rate": 0.5}))
    summary = summarize_branch(branch)
    assert summary.n == 0


def test_metrics_json_schema_round_trips(tmp_path):
    """A metrics.json written by run_single is consumed by analyze_sweep.

    Pins the shared schema between run_single (the producer) and
    analyze_sweep.summarize_branch (the consumer). The fields here mirror
    what run_single.py writes after the evaluate_v9 subprocess succeeds.
    """
    branch = tmp_path / "bc-ppo"
    branch.mkdir()
    sd = branch / "seed-42"
    sd.mkdir()
    (sd / "sb3.zip").write_bytes(b"fake")
    (sd / "metrics.json").write_text(json.dumps({
        "success_rate": 0.85,
        "seed": 42,
        "episodes": 20,
        "mean_reward": 234.5,
        "evaluated_at_unix": 1735689600,
    }))
    summary = summarize_branch(branch)
    assert summary.n == 1
    assert summary.values == [0.85]
    assert summary.seeds == [42]
