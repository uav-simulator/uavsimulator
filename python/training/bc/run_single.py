"""One PPO training run with explicit seed + optional BC initialization.

Wraps the existing train_cardboard_corridor_v9 invocation so the sweep runner
spawns it as a subprocess and reads exit code + sb3.zip. The sweep runner
considers a run "done" iff `<output-dir>/sb3.zip` exists, so this wrapper
ensures the SB3 checkpoint is named exactly `sb3.zip` regardless of what
the underlying script wrote (the script writes `<model-name>_sb3.zip`).
"""
from __future__ import annotations

import argparse
import shutil
import subprocess
import sys
from pathlib import Path


def main():
    p = argparse.ArgumentParser(description=__doc__)
    p.add_argument("--seed", type=int, required=True)
    p.add_argument("--total-timesteps", type=int, required=True)
    p.add_argument("--output-dir", type=Path, required=True)
    p.add_argument("--scenario", type=str, required=True)
    p.add_argument("--ent-coef", type=float, default=0.1)
    p.add_argument("--bc-init", type=Path, default=None)
    # Reserved for future use by sweep runner; currently passed through but
    # not consumed by train_cardboard_corridor_v9 (which takes flat args).
    p.add_argument("--env-kwargs-json", type=str, default="{}")
    p.add_argument("--eval-kwargs-json", type=str, default="{}")
    args = p.parse_args()

    args.output_dir.mkdir(parents=True, exist_ok=True)

    repo_root = Path(__file__).resolve().parents[3]
    train_script = repo_root / "python" / "training" / "train_cardboard_corridor_v9.py"

    cmd = [
        sys.executable,
        str(train_script),
        "--seed", str(args.seed),
        "--total-timesteps", str(args.total_timesteps),
        "--scenario", args.scenario,
        "--ent-coef", str(args.ent_coef),
        "--output-dir", str(args.output_dir),
    ]
    if args.bc_init:
        cmd += ["--bc-init", str(args.bc_init)]

    rc = subprocess.call(cmd)
    if rc != 0:
        sys.exit(rc)

    # Ensure sb3.zip exists at expected name (sweep idempotency contract).
    # train_cardboard_corridor_v9 saves as <slug>_sb3.zip via default_sb3_stem;
    # rename whatever it produced to plain sb3.zip.
    sb3_zip = args.output_dir / "sb3.zip"
    if not sb3_zip.exists():
        candidates = list(args.output_dir.glob("*.zip"))
        if candidates:
            shutil.move(str(candidates[0]), str(sb3_zip))
        else:
            print(f"FATAL: no SB3 zip produced in {args.output_dir}", file=sys.stderr)
            sys.exit(2)


if __name__ == "__main__":
    main()
