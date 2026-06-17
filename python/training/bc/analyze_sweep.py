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


def _read_metric(seed_dir: Path, metric_key: str) -> float | None:
    """Pull a numeric metric for one completed seed.

    Looks first in metrics.json (sweep-produced summary), then falls back to
    eval_full.json (evaluate_v9 raw output) so we can also surface fields
    that the sweep didn't bother to copy into metrics.json — typically
    `avgProgress`, `avgSteps`, `actionCounts`. Returns None if neither has
    the field (or if the seed didn't complete training).
    """
    if not (seed_dir / "sb3.zip").exists():
        return None
    metrics_path = seed_dir / "metrics.json"
    if metrics_path.exists():
        m = json.loads(metrics_path.read_text())
        if metric_key in m and m[metric_key] is not None:
            return float(m[metric_key])
    eval_path = seed_dir / "eval_full.json"
    if eval_path.exists():
        ef = json.loads(eval_path.read_text())
        # eval_full.json uses camelCase; try both spellings.
        camel = _snake_to_camel(metric_key)
        for k in (metric_key, camel):
            if k in ef and ef[k] is not None:
                return float(ef[k])
    return None


def _snake_to_camel(s: str) -> str:
    parts = s.split("_")
    return parts[0] + "".join(p.title() for p in parts[1:])


def summarize_branch(branch_dir: Path, metric_key: str = "success_rate") -> BranchSummary:
    """Aggregate `metric_key` of every completed seed in a branch directory.

    A seed is considered completed iff `seed-N/sb3.zip` exists (matches the
    sweep runner's idempotency contract). Returns an empty BranchSummary
    (n=0) if no seeds are completed. Default metric is success_rate to
    preserve backwards compatibility with the original call sites.
    """
    values: list[float] = []
    seeds: list[int] = []
    for seed_dir in sorted(branch_dir.glob("seed-*")):
        v = _read_metric(seed_dir, metric_key)
        if v is None:
            continue
        seed = int(seed_dir.name.split("-")[1])
        values.append(v)
        seeds.append(seed)
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


# Metrics we'd like to surface in the report. success_rate is the headline
# claim; the others (mean_reward, avgProgress, avgSteps) are surfaced as
# auxiliary signals so a pilot at short total_timesteps — where SR is
# typically 0 for all branches — can still show whether BC initialization
# changes the *shape* of the learning curve even when it hasn't yet reached
# the success threshold.
_AUX_METRICS = [
    ("success_rate", "Success rate", (0.0, 1.0), "%.3f"),
    ("mean_reward", "Mean episode reward", None, "%.2f"),
    ("avgProgress", "Mean route progress", (0.0, 1.0), "%.3f"),
    ("avgSteps", "Mean steps to terminal", None, "%.1f"),
]


def _render_one_metric(
    evidence_root: Path,
    output_dir: Path,
    metric_key: str,
    display_name: str,
    y_limits: tuple[float, float] | None,
    val_fmt: str,
    is_headline: bool,
) -> None:
    """Render variance table + Welch t-test + 2 plots for one metric.

    Headline metric (success_rate) is written to the file names the existing
    article placeholders expect (variance-table.md, welch-t-test.md, plot-*.png);
    auxiliary metrics get suffixed file names so they coexist.
    """
    suffix = "" if is_headline else f"-{metric_key.replace('_', '-')}"
    branches: list[BranchSummary] = []
    for branch_dir in sorted(evidence_root.iterdir()):
        if not branch_dir.is_dir():
            continue
        branches.append(summarize_branch(branch_dir, metric_key))
    branches = [b for b in branches if b.n > 0]

    if not branches:
        return

    table = [
        f"# Variance table ({display_name})\n",
        "| Branch | N | Mean SR | Std SR | Seeds |"
        if is_headline else
        "| Branch | N | Mean | Std | Seeds |",
        "|---|---|---|---|---|",
    ]
    for b in branches:
        table.append(
            f"| {b.name} | {b.n} | {val_fmt % b.mean} | {val_fmt % b.std} | {b.seeds} |"
        )
    (output_dir / f"variance-table{suffix}.md").write_text("\n".join(table) + "\n")

    if len(branches) >= 2:
        lines = [f"# Welch t-test (pairwise, {display_name})\n"]
        for i in range(len(branches)):
            for j in range(i + 1, len(branches)):
                a, b = branches[i], branches[j]
                t = welch_test(a, b)
                lines.append(f"## {a.name} vs {b.name}")
                for k, v in t.items():
                    lines.append(f"- {k}: {v}")
                lines.append("")
        (output_dir / f"welch-t-test{suffix}.md").write_text("\n".join(lines) + "\n")
    else:
        (output_dir / f"welch-t-test{suffix}.md").write_text(
            f"# Welch t-test ({display_name})\n\n_Insufficient branches for pairwise comparison._\n"
        )

    colors = ["#1f77b4", "#ff7f0e", "#2ca02c", "#d62728", "#9467bd"]

    fig, ax = plt.subplots(figsize=(6, 4))
    names = [b.name for b in branches]
    means = [b.mean for b in branches]
    stds = [b.std for b in branches]
    ax.bar(names, means, yerr=stds, capsize=8, color=colors[: len(names)])
    ax.set_ylabel(display_name)
    if y_limits is not None:
        ax.set_ylim(*y_limits)
    ax.set_title(f"Mean {display_name} ± std by branch")
    fig.tight_layout()
    fig.savefig(output_dir / f"plot-mean-std{suffix}.png", dpi=160)
    plt.close(fig)

    fig, ax = plt.subplots(figsize=(8, 4))
    for b in branches:
        ax.scatter(b.seeds, b.values, label=b.name, s=80)
    ax.set_xlabel("Seed")
    ax.set_ylabel(display_name)
    if y_limits is not None:
        ax.set_ylim(*y_limits)
    ax.legend()
    ax.set_title(f"Per-seed {display_name} by branch")
    fig.tight_layout()
    fig.savefig(output_dir / f"plot-per-seed-trajectories{suffix}.png", dpi=160)
    plt.close(fig)


def _parse_train_log_curve(log_path: Path) -> tuple[list[int], list[float]]:
    """Pull (timestep, ep_rew_mean) series from a train_cardboard_corridor_v9 log.

    The training script writes SB3's default tabular logger format:
        |    ep_rew_mean     | -244     |
        |    total_timesteps | 512      |
    Each rollout block has both fields; we pair them in lock-step.

    Returns ([], []) if the log doesn't yet have any rollout blocks (early
    crash / training not started).
    """
    import re

    ts_pat = re.compile(r"\|\s*total_timesteps\s*\|\s*([0-9.eE+-]+)\s*\|")
    rew_pat = re.compile(r"\|\s*ep_rew_mean\s*\|\s*(-?[0-9.eE+-]+)\s*\|")
    timesteps: list[int] = []
    rewards: list[float] = []
    if not log_path.exists():
        return timesteps, rewards
    text = log_path.read_text()
    ts_matches = ts_pat.findall(text)
    rew_matches = rew_pat.findall(text)
    n = min(len(ts_matches), len(rew_matches))
    for i in range(n):
        try:
            timesteps.append(int(float(ts_matches[i])))
            rewards.append(float(rew_matches[i]))
        except ValueError:
            continue
    return timesteps, rewards


def _render_learning_curves(evidence_root: Path, output_dir: Path) -> None:
    """Per-branch learning-curve plot: ep_rew_mean over total_timesteps.

    Each seed is drawn as a thin line; the per-branch mean (interpolated to
    a common grid) is overlaid as a bold line. Makes the variance story
    visual even when end-of-training SR is zero — you can see *whether* the
    BC-initialised curves are tighter around their mean than the pure-PPO
    curves.
    """
    import numpy as np

    by_branch: dict[str, list[tuple[list[int], list[float]]]] = {}
    for branch_dir in sorted(evidence_root.iterdir()):
        if not branch_dir.is_dir():
            continue
        curves: list[tuple[list[int], list[float]]] = []
        for seed_dir in sorted(branch_dir.glob("seed-*")):
            ts, rew = _parse_train_log_curve(seed_dir / "train.log")
            if ts and rew:
                curves.append((ts, rew))
        if curves:
            by_branch[branch_dir.name] = curves
    if not by_branch:
        return

    fig, ax = plt.subplots(figsize=(8, 5))
    colors = {"pure-ppo-baseline": "#1f77b4", "bc-ppo": "#ff7f0e"}
    for branch_name, curves in by_branch.items():
        color = colors.get(branch_name, "#666666")
        # Plot each seed as a thin transparent line.
        for ts, rew in curves:
            ax.plot(ts, rew, color=color, alpha=0.35, linewidth=1)
        # Interpolate all curves to a common grid for the bold mean.
        max_ts = max(max(ts) for ts, _ in curves)
        grid = np.linspace(0, max_ts, num=100)
        means_at_grid = []
        for ts, rew in curves:
            means_at_grid.append(np.interp(grid, ts, rew, left=rew[0], right=rew[-1]))
        mean_curve = np.mean(means_at_grid, axis=0)
        ax.plot(grid, mean_curve, color=color, linewidth=2.5,
                label=f"{branch_name} (n={len(curves)})")
    ax.set_xlabel("Total timesteps")
    ax.set_ylabel("Mean episode reward (training)")
    ax.set_title("Learning curves: per-seed (thin) + per-branch mean (bold)")
    ax.legend(loc="lower right")
    ax.grid(True, alpha=0.3)
    fig.tight_layout()
    fig.savefig(output_dir / "plot-learning-curves.png", dpi=160)
    plt.close(fig)


def render_report(evidence_root: Path, output_dir: Path) -> None:
    """Render variance-table.md + welch-t-test.md + plots for headline SR plus
    auxiliary mean_reward / avgProgress / avgSteps tables, so a pilot run with
    all-zero SR still produces interpretable variance numbers."""
    output_dir.mkdir(parents=True, exist_ok=True)
    for metric_key, display_name, y_limits, val_fmt in _AUX_METRICS:
        is_headline = metric_key == "success_rate"
        _render_one_metric(
            evidence_root, output_dir, metric_key, display_name, y_limits, val_fmt, is_headline,
        )
    _render_learning_curves(evidence_root, output_dir)


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
