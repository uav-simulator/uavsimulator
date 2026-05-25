"""Multi-seed sweep runner: orchestrates N seeds x M branches with idempotent resume.

Each run writes evidence to:
  evidence_root/<branch>/seed-<N>/
    sweep_metadata.json - sweep-level snapshot (branch, seed, started_at_unix)
    metadata.json       - SB3 model metadata written by train_cardboard_corridor_v9
    metrics.json        - final eval results (success rate, mean reward, ...)
    sb3.zip             - trained SB3 checkpoint (existence == run completed)
    train.log           - full training log

The sweep snapshot is intentionally named `sweep_metadata.json` (not
`metadata.json`) because the underlying training script writes its own
`metadata.json` (the SB3 model metadata) into the same output_dir, and we
must not let it clobber the sweep evidence.

Idempotency contract: a seed is considered "done" iff sb3.zip exists in its
run directory. This lets the sweep survive SIGTERM mid-run (the partial run
directory will have sweep_metadata.json but no sb3.zip, so the next
invocation will re-execute it).
"""
from __future__ import annotations

import json
import signal
import subprocess
import sys
import time
from dataclasses import dataclass, field
from pathlib import Path

import yaml


@dataclass
class SweepPlan:
    """One branch of the sweep (e.g. 'bc-ppo' or 'pure-ppo-baseline')."""

    branch_name: str
    evidence_root: Path
    seeds: list[int]
    init_from: Path | None = None
    total_timesteps: int = 200_000
    scenario: str = ""
    env_kwargs: dict = field(default_factory=dict)
    eval_kwargs: dict = field(default_factory=dict)
    with_occupancy: bool = False
    # Frame-stacking k (1 = no stacking, single frame). Plumbed through
    # train_cardboard_corridor_v9 which wraps train_env in SB3's VecFrameStack.
    # Camera-only alternative to with_occupancy — feasible on KS0223 (which
    # has no pose sensor and would need pose estimation for the occupancy
    # accumulator).
    frame_stack: int = 1


@dataclass
class PendingRun:
    """One pending (branch, seed) pair that the runner still needs to execute."""

    branch: str
    seed: int
    run_dir: Path


def plan_pending_runs(plan: SweepPlan) -> list[PendingRun]:
    """Return runs whose sb3.zip does not yet exist (== not completed)."""
    out: list[PendingRun] = []
    for seed in plan.seeds:
        run_dir = plan.evidence_root / plan.branch_name / f"seed-{seed}"
        if (run_dir / "sb3.zip").exists():
            continue
        out.append(PendingRun(branch=plan.branch_name, seed=seed, run_dir=run_dir))
    return out


def execute_run(plan: SweepPlan, run: PendingRun) -> int:
    """Spawn one training run as a subprocess, capturing log to disk.

    Writes sweep_metadata.json before the subprocess starts so partial-failure
    runs are still traceable (you can see when they started even if no
    sb3.zip got produced). We intentionally do NOT call this file
    `metadata.json` because train_cardboard_corridor_v9 writes its own
    `metadata.json` (the SB3 model metadata) into output_dir, which would
    silently overwrite the sweep snapshot.
    """
    run.run_dir.mkdir(parents=True, exist_ok=True)

    metadata = {
        "branch": run.branch,
        "seed": run.seed,
        "total_timesteps": plan.total_timesteps,
        "init_from": str(plan.init_from) if plan.init_from else None,
        "scenario": plan.scenario,
        "ent_coef": 0.1,
        "started_at_unix": int(time.time()),
    }
    (run.run_dir / "sweep_metadata.json").write_text(json.dumps(metadata, indent=2))

    cmd = [
        sys.executable,
        "-m", "training.bc.run_single",
        "--seed", str(run.seed),
        "--total-timesteps", str(plan.total_timesteps),
        "--output-dir", str(run.run_dir),
        "--scenario", plan.scenario,
        "--ent-coef", "0.1",
    ]
    if plan.init_from:
        cmd += ["--bc-init", str(plan.init_from)]
    if plan.with_occupancy:
        cmd += ["--with-occupancy"]
    if plan.frame_stack > 1:
        cmd += ["--frame-stack", str(plan.frame_stack)]
    if plan.env_kwargs:
        cmd += ["--env-kwargs-json", json.dumps(plan.env_kwargs)]
    if plan.eval_kwargs:
        cmd += ["--eval-kwargs-json", json.dumps(plan.eval_kwargs)]

    log_path = run.run_dir / "train.log"
    with log_path.open("w") as log:
        log.write(f"# command: {' '.join(cmd)}\n")
        log.flush()
        proc = subprocess.run(cmd, stdout=log, stderr=subprocess.STDOUT)
    return proc.returncode


_STOP = False


def _sigterm_handler(signum, frame):
    """SIGTERM/SIGINT handler: set a flag so the loop stops after current run."""
    global _STOP
    print(f"[sweep] received signal {signum}; will stop after current run.", flush=True)
    _STOP = True


def run_sweep(plans: list[SweepPlan]) -> None:
    """Run all pending runs across all plans, sequentially, until interrupted."""
    signal.signal(signal.SIGTERM, _sigterm_handler)
    signal.signal(signal.SIGINT, _sigterm_handler)
    for plan in plans:
        pending = plan_pending_runs(plan)
        print(f"[sweep] branch={plan.branch_name} pending={len(pending)}", flush=True)
        for run in pending:
            if _STOP:
                print("[sweep] graceful stop.", flush=True)
                return
            print(f"[sweep] starting branch={run.branch} seed={run.seed}", flush=True)
            rc = execute_run(plan, run)
            print(f"[sweep] finished branch={run.branch} seed={run.seed} rc={rc}", flush=True)
            if rc != 0:
                print(
                    f"[sweep] WARNING: branch={run.branch} seed={run.seed} failed with rc={rc}; "
                    f"sb3.zip not written, will retry on next run.",
                    flush=True,
                )


def main():
    import argparse
    p = argparse.ArgumentParser(description=__doc__)
    p.add_argument("--config", type=Path, required=True,
                   help="Path to sweep YAML (configs/sweeps/*.yaml).")
    args = p.parse_args()

    cfg = yaml.safe_load(args.config.read_text())["sweep"]
    plans: list[SweepPlan] = []
    for branch in cfg["branches"]:
        plans.append(SweepPlan(
            branch_name=branch["name"],
            evidence_root=Path(cfg["evidence_root"]),
            seeds=cfg["seeds"],
            init_from=Path(branch["init_from"]) if branch.get("init_from") else None,
            total_timesteps=cfg["total_timesteps"],
            scenario=cfg["scenario"],
            env_kwargs=cfg.get("env", {}),
            eval_kwargs=cfg.get("eval", {}),
            with_occupancy=bool(branch.get("with_occupancy", False)),
            frame_stack=int(branch.get("frame_stack", 1)),
        ))
    run_sweep(plans)


if __name__ == "__main__":
    main()
