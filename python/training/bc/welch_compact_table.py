"""Re-format analyze_sweep's welch-t-test markdown as a compact table.

analyze_sweep produces per-pair ## subsection headings + bullet lists.
When that content is inlined into an article it shows up in the TOC
and breaks the article's heading hierarchy. This helper takes the
verbose welch markdown and produces a single compact markdown table:

  | Pair | t | p-value | Cohen's d | mean diff |
  |---|---|---|---|---|
  | bc-ppo vs bc-ppo-occ | -0.83 | 0.55 | -1.17 | -17.84 |
  ...
"""
from __future__ import annotations

import argparse
import re
from pathlib import Path


def reformat(welch_md_path: Path) -> str:
    text = welch_md_path.read_text()

    pairs: list[tuple[str, dict[str, str]]] = []
    current_pair = None
    current_fields: dict[str, str] = {}
    for line in text.splitlines():
        m = re.match(r"^##\s+(.+vs.+)$", line)
        if m:
            if current_pair is not None:
                pairs.append((current_pair, current_fields))
            current_pair = m.group(1).strip()
            current_fields = {}
            continue
        m = re.match(r"^-\s+(\w+):\s*(.+)$", line)
        if m and current_pair is not None:
            current_fields[m.group(1)] = m.group(2).strip()
    if current_pair is not None:
        pairs.append((current_pair, current_fields))

    if not pairs:
        return "_No pairs found in welch output._\n"

    def _fmt(v: str | None, kind: str) -> str:
        if v is None or v.lower() == "true":
            return "—"
        try:
            f = float(v)
        except (TypeError, ValueError):
            return v
        if kind == "p":
            return f"{f:.3f}"
        return f"{f:.2f}"

    lines = [
        "| Пара ветвей | t | p-value | Cohen's d | Δ среднего |",
        "|---|---|---|---|---|",
    ]
    for pair, fields in pairs:
        if fields.get("insufficient_data") == "True":
            lines.append(f"| {pair} | — | — | — | — |")
            continue
        lines.append(
            f"| {pair} | {_fmt(fields.get('t_statistic'), 't')} | "
            f"{_fmt(fields.get('p_value'), 'p')} | "
            f"{_fmt(fields.get('cohens_d'), 'd')} | "
            f"{_fmt(fields.get('mean_diff'), 'm')} |"
        )
    return "\n".join(lines) + "\n"


def main():
    p = argparse.ArgumentParser(description=__doc__)
    p.add_argument("--input", type=Path, required=True)
    p.add_argument("--output", type=Path, default=None,
                   help="If set, write to file; otherwise print to stdout.")
    args = p.parse_args()

    out = reformat(args.input)
    if args.output:
        args.output.parent.mkdir(parents=True, exist_ok=True)
        args.output.write_text(out)
        print(f"Wrote {args.output}")
    else:
        print(out)


if __name__ == "__main__":
    main()
