"""Extract per-branch action distribution table from a sweep's eval_full.json
files. Output as markdown matching the Article 2 Table 2 placeholder.

Action distribution is a key indicator of policy quality on pilot-length
training:
- A mode-collapsed branch shows 95-100 % concentrated in one action
  (and 0 % in the others). Indicates that BC-initialisation carried over
  the BC mode-collapse failure, OR that PPO converged to a degenerate
  always-stop / always-forward policy under heavy-DR.
- A diversified branch shows action mass distributed across multiple
  classes — the policy uses turns and stops alongside forward motion.

We aggregate across seeds within a branch by averaging the per-seed
actionDistribution dicts (each is already normalised to sum to 1.0).
"""
from __future__ import annotations

import argparse
import json
import statistics
from pathlib import Path

ACTION_NAMES = ["DirStop", "DirForward", "DirBack", "DirLeft", "DirRight"]


def summarize_branch(branch_dir: Path) -> dict[str, float] | None:
    """Return per-action mean of actionDistribution across all completed seeds.
    Returns None if no seed has a usable eval_full.json."""
    per_seed_dists: list[dict[str, float]] = []
    for seed_dir in sorted(branch_dir.glob("seed-*")):
        sb3 = seed_dir / "sb3.zip"
        eval_path = seed_dir / "eval_full.json"
        if not sb3.exists() or not eval_path.exists():
            continue
        try:
            data = json.loads(eval_path.read_text())
        except (json.JSONDecodeError, OSError):
            continue
        ad = data.get("actionDistribution") or {}
        if not ad:
            continue
        per_seed_dists.append({k: float(ad.get(k, 0.0)) for k in ACTION_NAMES})
    if not per_seed_dists:
        return None
    return {
        k: statistics.fmean(d[k] for d in per_seed_dists)
        for k in ACTION_NAMES
    }


def render_markdown_table(evidence_root: Path) -> str:
    branches: list[tuple[str, dict[str, float]]] = []
    for branch_dir in sorted(evidence_root.iterdir()):
        if not branch_dir.is_dir():
            continue
        summary = summarize_branch(branch_dir)
        if summary is None:
            continue
        branches.append((branch_dir.name, summary))

    if not branches:
        return "_No completed eval data found._\n"

    lines = [
        "| Branch | DirStop | DirForward | DirLeft | DirRight | DirBack |",
        "|---|---|---|---|---|---|",
    ]
    for name, s in branches:
        lines.append(
            f"| {name} | {s['DirStop']:.2f} | {s['DirForward']:.2f} | "
            f"{s['DirLeft']:.2f} | {s['DirRight']:.2f} | {s['DirBack']:.2f} |"
        )
    return "\n".join(lines) + "\n"


def main():
    p = argparse.ArgumentParser(description=__doc__)
    p.add_argument("--evidence-root", type=Path, required=True)
    p.add_argument("--output", type=Path, default=None,
                   help="Optional output file (markdown). Prints to stdout if not set.")
    args = p.parse_args()

    table = render_markdown_table(args.evidence_root)
    if args.output:
        args.output.parent.mkdir(parents=True, exist_ok=True)
        args.output.write_text(table)
        print(f"Wrote {args.output}")
    else:
        print(table)


if __name__ == "__main__":
    main()
