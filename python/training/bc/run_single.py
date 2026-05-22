"""One PPO training run with explicit seed + optional BC initialization.

Wraps the existing train_cardboard_corridor_v9 invocation so the sweep runner
spawns it as a subprocess and reads exit code + sb3.zip. The sweep runner
considers a run "done" iff `<output-dir>/sb3.zip` exists, so this wrapper
ensures the SB3 checkpoint is named exactly `sb3.zip` regardless of what
the underlying script wrote (the script writes `<model-name>_sb3.zip`).

After training succeeds, this wrapper invokes evaluate_v9 to produce
`metrics.json` with `success_rate` so analyze_sweep.summarize_branch can
aggregate the run. evaluate_v9 (not evaluate_ab_policy) is used because it
loads SB3 zip checkpoints directly via `--model-format ppo` — the AB-policy
evaluator only handles ONNX exports.
"""
from __future__ import annotations

import argparse
import json
import shutil
import subprocess
import sys
import time
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
        # Crucial: do NOT rename any zip on failure. A partial checkpoint
        # left behind by a crashed train script would be silently treated
        # as a completed run on the next sweep invocation, breaking
        # idempotency. Just bubble up the train script's RC.
        sys.exit(rc)

    # Rename the canonical SB3 zip to a name the sweep runner recognises
    # (sweep idempotency contract: sb3.zip exists == run completed).
    # train_cardboard_corridor_v9 saves as <default_sb3_stem(model_name)>.zip
    # — match it by exact name rather than glob('*.zip'), which is
    # ordering-unsafe and could grab unrelated zips (e.g. best_model.zip
    # if EvalCallback config changes).
    sb3_zip = args.output_dir / "sb3.zip"
    if not sb3_zip.exists():
        from training.model_artifacts import default_sb3_stem
        expected = args.output_dir / f"{default_sb3_stem('cardboard-corridor-ppo-v9')}.zip"
        if expected.exists():
            shutil.move(str(expected), str(sb3_zip))
        else:
            # Last-resort: pick the largest zip in output_dir that's not
            # a best_model.zip (those come from EvalCallback, not the
            # final checkpoint).
            candidates = [p for p in args.output_dir.glob("*.zip") if "best_model" not in p.name]
            if candidates:
                best = max(candidates, key=lambda p: p.stat().st_size)
                shutil.move(str(best), str(sb3_zip))
            else:
                print(f"FATAL: no SB3 zip produced in {args.output_dir}", file=sys.stderr)
                sys.exit(2)

    # Run final eval to produce metrics.json — analyze_sweep.summarize_branch
    # needs this file to aggregate per-branch success-rate. Without it the
    # 60h sweep finishes with n=0 across all branches and the article fills
    # with `?`. Eval failures don't fail the run: we write a degenerate
    # metrics.json with success_rate=null + eval_error so the failure is
    # visible later instead of masquerading as a missing file.
    eval_kwargs = json.loads(args.eval_kwargs_json) if args.eval_kwargs_json else {}
    eval_base_url = eval_kwargs.get("base_url", "http://127.0.0.1:8000")
    eval_episodes = int(eval_kwargs.get("episodes", 20))
    eval_seed_offset = int(eval_kwargs.get("seed_offset", 3000))

    eval_script = repo_root / "python" / "training" / "evaluate_v9.py"
    eval_output_path = args.output_dir / "eval_full.json"
    eval_cmd = [
        sys.executable,
        str(eval_script),
        "--model", str(sb3_zip),
        "--base-url", eval_base_url,
        "--episodes", str(eval_episodes),
        "--seed-offset", str(eval_seed_offset),
        "--scenario", args.scenario,
        "--output-json", str(eval_output_path),
    ]

    print(f"[run_single] starting eval: {' '.join(eval_cmd)}", flush=True)
    eval_rc = subprocess.call(eval_cmd)
    if eval_rc != 0:
        print(
            f"WARNING: eval subprocess failed rc={eval_rc}; writing degenerate metrics.json",
            file=sys.stderr,
        )
        metrics = {
            "success_rate": None,
            "seed": args.seed,
            "eval_error": f"evaluate_v9 exit code {eval_rc}",
            "episodes_attempted": eval_episodes,
            "evaluated_at_unix": int(time.time()),
        }
    elif eval_output_path.exists():
        eval_data = json.loads(eval_output_path.read_text())
        success_rate = float(eval_data.get("successRate", eval_data.get("success_rate", 0.0)))
        metrics = {
            "success_rate": success_rate,
            "seed": args.seed,
            "episodes": int(eval_data.get("episodes", eval_episodes)),
            "mean_reward": float(eval_data.get("avgReward", eval_data.get("mean_reward", 0.0))),
            "evaluated_at_unix": int(time.time()),
        }
    else:
        # rc=0 but no JSON file — eval ran but failed to write output.
        # Mark as error so analyze_sweep can flag it explicitly.
        metrics = {
            "success_rate": None,
            "seed": args.seed,
            "eval_error": "evaluate_v9 returned 0 but no output file",
            "episodes_attempted": eval_episodes,
            "evaluated_at_unix": int(time.time()),
        }

    (args.output_dir / "metrics.json").write_text(json.dumps(metrics, indent=2))
    print(
        f"[run_single] wrote {args.output_dir / 'metrics.json'}: "
        f"success_rate={metrics.get('success_rate')}",
        flush=True,
    )


if __name__ == "__main__":
    main()
