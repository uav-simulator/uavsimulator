#!/usr/bin/env python3
"""Read a directory of eval_*.json files and print a markdown summary table."""
from __future__ import annotations

import argparse
import json
import sys
from pathlib import Path


def main() -> int:
    p = argparse.ArgumentParser()
    p.add_argument("results_dir")
    p.add_argument("--out-md", default="", help="also write table to this markdown file")
    args = p.parse_args()

    d = Path(args.results_dir)
    results = []
    for f in sorted(d.glob("eval_*.json")):
        with f.open() as fp:
            data = json.load(fp)
        scenario = f.stem.removeprefix("eval_")
        results.append({
            "scenario": scenario,
            "sr": data.get("successRate", 0.0),
            "reward": data.get("avgReward", 0.0),
            "progress": data.get("avgProgress", 0.0),
            "steps": data.get("avgSteps", 0),
        })

    if not results:
        print(f"no eval_*.json found in {d}", file=sys.stderr)
        return 1

    lines = [
        "| Scenario | SR | avgReward | avgProgress | avgSteps |",
        "|---|---|---|---|---|",
    ]
    for r in results:
        lines.append(
            f"| {r['scenario']} | {r['sr']:.0%} | {r['reward']:+.2f} | {r['progress']:.1%} | {r['steps']:.0f} |"
        )

    succ = sum(1 for r in results if r["sr"] >= 0.5)
    lines.append(f"\n**Scenarios with SR ≥ 50%: {succ}/{len(results)}**")

    out = "\n".join(lines)
    print(out)
    if args.out_md:
        Path(args.out_md).write_text(out + "\n")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
