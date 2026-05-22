"""Statistical analysis + plots over completed sweep evidence.

Reads `metrics.json` of every completed seed (gated by `sb3.zip` existence,
matching the multi_seed_sweep idempotency contract), computes per-branch
mean/std SR, runs pairwise Welch's t-tests, and renders a markdown report
plus two PNG plots.

matplotlib.use("Agg") is set before pyplot import so this module works on
headless CI / SSH sessions without a display.
"""
from __future__ import annotations

import matplotlib

matplotlib.use("Agg")

import json
import statistics
from dataclasses import dataclass, field
from pathlib import Path

import matplotlib.pyplot as plt
from scipy import stats


@dataclass
class BranchSummary:
    name: str = ""
    n: int = 0
    mean: float = 0.0
    std: float = 0.0
    values: list[float] = field(default_factory=list)
    seeds: list[int] = field(default_factory=list)


def summarize_branch(branch_dir: Path) -> BranchSummary:
    """Aggregate metrics.json of every completed seed in a branch directory.

    A seed is considered completed iff `seed-N/sb3.zip` exists (matches the
    sweep runner's idempotency contract). Returns an empty BranchSummary
    (n=0) if no seeds are completed.
    """
    values: list[float] = []
    seeds: list[int] = []
    for seed_dir in sorted(branch_dir.glob("seed-*")):
        if not (seed_dir / "sb3.zip").exists():
            continue
        metrics_path = seed_dir / "metrics.json"
        if not metrics_path.exists():
            continue
        m = json.loads(metrics_path.read_text())
        sr = m.get("success_rate")
        if sr is None:
            continue
        seed = m.get("seed", int(seed_dir.name.split("-")[1]))
        values.append(float(sr))
        seeds.append(int(seed))
    if not values:
        return BranchSummary(name=branch_dir.name)
    return BranchSummary(
        name=branch_dir.name,
        n=len(values),
        mean=statistics.fmean(values),
        std=statistics.pstdev(values) if len(values) > 1 else 0.0,
        values=values,
        seeds=seeds,
    )


def welch_test(branch_a: BranchSummary, branch_b: BranchSummary) -> dict:
    """Welch's t-test (unequal-variance) between two branches.

    Returns dict with t_statistic, p_value, cohens_d, n_a, n_b, mean_diff.
    If either branch has n<2, returns {"insufficient_data": True}.
    """
    if branch_a.n < 2 or branch_b.n < 2:
        return {"insufficient_data": True}
    t, p = stats.ttest_ind(branch_a.values, branch_b.values, equal_var=False)
    pooled_std = ((branch_a.std ** 2 + branch_b.std ** 2) / 2) ** 0.5
    cohens_d = (branch_a.mean - branch_b.mean) / pooled_std if pooled_std > 0 else 0.0
    return {
        "t_statistic": float(t),
        "p_value": float(p),
        "cohens_d": float(cohens_d),
        "n_a": branch_a.n,
        "n_b": branch_b.n,
        "mean_diff": branch_a.mean - branch_b.mean,
    }


def render_report(evidence_root: Path, output_dir: Path) -> None:
    """Render variance-table.md + welch-t-test.md + 2 PNG plots to output_dir."""
    output_dir.mkdir(parents=True, exist_ok=True)
    branches: list[BranchSummary] = []
    for branch_dir in sorted(evidence_root.iterdir()):
        if not branch_dir.is_dir():
            continue
        branches.append(summarize_branch(branch_dir))
    branches = [b for b in branches if b.n > 0]

    table = [
        "# Variance table\n",
        "| Branch | N | Mean SR | Std SR | Seeds |",
        "|---|---|---|---|---|",
    ]
    for b in branches:
        table.append(f"| {b.name} | {b.n} | {b.mean:.3f} | {b.std:.3f} | {b.seeds} |")
    (output_dir / "variance-table.md").write_text("\n".join(table) + "\n")

    if len(branches) >= 2:
        lines = ["# Welch t-test (pairwise)\n"]
        for i in range(len(branches)):
            for j in range(i + 1, len(branches)):
                a, b = branches[i], branches[j]
                t = welch_test(a, b)
                lines.append(f"## {a.name} vs {b.name}")
                for k, v in t.items():
                    lines.append(f"- {k}: {v}")
                lines.append("")
        (output_dir / "welch-t-test.md").write_text("\n".join(lines) + "\n")
    else:
        (output_dir / "welch-t-test.md").write_text(
            "# Welch t-test (pairwise)\n\n_Insufficient branches for pairwise comparison._\n"
        )

    fig, ax = plt.subplots(figsize=(6, 4))
    names = [b.name for b in branches]
    means = [b.mean for b in branches]
    stds = [b.std for b in branches]
    colors = ["#1f77b4", "#ff7f0e", "#2ca02c", "#d62728", "#9467bd"]
    ax.bar(names, means, yerr=stds, capsize=8, color=colors[: len(names)])
    ax.set_ylabel("Success rate")
    ax.set_ylim(0, 1)
    ax.set_title("Mean SR ± std by branch")
    fig.tight_layout()
    fig.savefig(output_dir / "plot-mean-std.png", dpi=160)
    plt.close(fig)

    fig, ax = plt.subplots(figsize=(8, 4))
    for b in branches:
        ax.scatter(b.seeds, b.values, label=b.name, s=80)
    ax.set_xlabel("Seed")
    ax.set_ylabel("Success rate")
    ax.set_ylim(0, 1)
    if branches:
        ax.legend()
    ax.set_title("Per-seed SR by branch")
    fig.tight_layout()
    fig.savefig(output_dir / "plot-per-seed-trajectories.png", dpi=160)
    plt.close(fig)


def main():
    import argparse

    p = argparse.ArgumentParser(description=__doc__)
    p.add_argument("--evidence-root", type=Path, required=True)
    p.add_argument("--output", type=Path, required=True)
    args = p.parse_args()
    render_report(args.evidence_root, args.output)
    print(f"Report written: {args.output}")


if __name__ == "__main__":
    main()
