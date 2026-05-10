"""PPO trainer for ``CityAutonomyEnv`` — Phase 1 of the city autonomy plan.

Mirrors the existing cardboard-corridor training pipeline (Stable-Baselines3
PPO, ``MultiInputPolicy``, periodic checkpoints + EvalCallback) but targets
``track.city_polygon.v1`` and the city-curriculum scenario set.

Operator startup is documented in
``2026-05-10-city-autonomy-training.md``.

Quickstart::

    # Unity Editor must be open on the city polygon scene, in PlayMode.
    pip install -e "python[test,dev,training]"
    python python/training/train_city_autonomy.py \
        --scenario configs/scenarios/city/city-1-straight.yaml \
        --total-timesteps 50000 --device mps

Defaults are conservative for a single autonomous proof-of-life run on a
M4 Max. For a real training campaign, bump ``--total-timesteps`` to several
million and consider running on the Windows + RTX 5080 box.
"""

from __future__ import annotations

import argparse
import json
import sys
import time
from pathlib import Path

PYTHON_ROOT = Path(__file__).resolve().parents[1]
if str(PYTHON_ROOT) not in sys.path:
    sys.path.insert(0, str(PYTHON_ROOT))


DEFAULT_TOTAL_TIMESTEPS = 50_000
DEFAULT_CHECKPOINT_INTERVAL = 25_000
DEFAULT_EVAL_INTERVAL = 25_000
DEFAULT_EVAL_EPISODES = 5
DEFAULT_LEARNING_RATE = 3e-4
DEFAULT_N_STEPS = 2048
DEFAULT_BATCH_SIZE = 256
DEFAULT_GAMMA = 0.995
DEFAULT_GAE_LAMBDA = 0.95
DEFAULT_ENT_COEF = 0.01
DEFAULT_CLIP_RANGE = 0.2

ARTIFACT_FAMILY = "city-autonomy-v1"


def parse_args(argv: list[str] | None = None) -> argparse.Namespace:
    parser = argparse.ArgumentParser(description=__doc__.split("\n")[0])
    parser.add_argument(
        "--scenario",
        type=Path,
        default=Path("configs/scenarios/city/city-1-straight.yaml"),
        help="Training scenario YAML (curriculum stage 1 by default).",
    )
    parser.add_argument(
        "--eval-scenario",
        type=Path,
        default=Path("configs/scenarios/city/city-5-mixed.yaml"),
        help="Evaluation scenario for EvalCallback (curriculum stage 5 by default).",
    )
    parser.add_argument(
        "--base-url",
        default="http://127.0.0.1:8000",
        help="Unity HTTP API base URL.",
    )
    parser.add_argument(
        "--total-timesteps",
        type=int,
        default=DEFAULT_TOTAL_TIMESTEPS,
        help="PPO total environment steps (default: 50k for proof-of-life run).",
    )
    parser.add_argument("--learning-rate", type=float, default=DEFAULT_LEARNING_RATE)
    parser.add_argument("--n-steps", type=int, default=DEFAULT_N_STEPS)
    parser.add_argument("--batch-size", type=int, default=DEFAULT_BATCH_SIZE)
    parser.add_argument("--gamma", type=float, default=DEFAULT_GAMMA)
    parser.add_argument("--gae-lambda", type=float, default=DEFAULT_GAE_LAMBDA)
    parser.add_argument("--ent-coef", type=float, default=DEFAULT_ENT_COEF)
    parser.add_argument("--clip-range", type=float, default=DEFAULT_CLIP_RANGE)
    parser.add_argument("--max-steps-per-episode", type=int, default=800)
    parser.add_argument("--time-scale", type=float, default=2.0)
    parser.add_argument(
        "--device",
        default="mps",
        help="torch device: mps (M-series GPU), cuda, or cpu.",
    )
    parser.add_argument(
        "--rev",
        default=time.strftime("rev%Y%m%d-%H%M%S"),
        help="Revision tag — used as the artifact subdir name.",
    )
    parser.add_argument(
        "--resume",
        type=Path,
        default=None,
        help="Checkpoint .zip to resume from.",
    )
    parser.add_argument(
        "--checkpoint-interval",
        type=int,
        default=DEFAULT_CHECKPOINT_INTERVAL,
    )
    parser.add_argument(
        "--eval-interval",
        type=int,
        default=DEFAULT_EVAL_INTERVAL,
    )
    parser.add_argument(
        "--eval-episodes",
        type=int,
        default=DEFAULT_EVAL_EPISODES,
    )
    parser.add_argument(
        "--seed",
        type=int,
        default=None,
        help="PPO random seed (default: derived from rev tag).",
    )
    parser.add_argument(
        "--dry-run",
        action="store_true",
        help="Build env + model, log config, and exit. Does NOT call .learn().",
    )
    return parser.parse_args(argv)


def main(argv: list[str] | None = None) -> int:  # pragma: no cover — needs torch + Unity
    args = parse_args(argv)

    # Heavy imports deferred so --help / --dry-run smoke don't pull torch.
    from stable_baselines3 import PPO
    from stable_baselines3.common.callbacks import CheckpointCallback, EvalCallback
    from stable_baselines3.common.monitor import Monitor
    from stable_baselines3.common.vec_env import DummyVecEnv

    from training.city_autonomy_env import CityAutonomyEnv

    artifact_dir = (
        PYTHON_ROOT / "training" / "artifacts" / ARTIFACT_FAMILY / args.rev / "1.0.0"
    )
    artifact_dir.mkdir(parents=True, exist_ok=True)
    (artifact_dir / "config.json").write_text(
        json.dumps(
            {
                "scenario": str(args.scenario),
                "eval_scenario": str(args.eval_scenario),
                "base_url": args.base_url,
                "total_timesteps": args.total_timesteps,
                "learning_rate": args.learning_rate,
                "n_steps": args.n_steps,
                "batch_size": args.batch_size,
                "gamma": args.gamma,
                "gae_lambda": args.gae_lambda,
                "ent_coef": args.ent_coef,
                "clip_range": args.clip_range,
                "max_steps_per_episode": args.max_steps_per_episode,
                "time_scale": args.time_scale,
                "device": args.device,
                "rev": args.rev,
                "seed": args.seed,
            },
            indent=2,
        )
    )

    def make_env(scenario: Path) -> Monitor:
        return Monitor(
            CityAutonomyEnv(
                base_url=args.base_url,
                scenario_path=scenario,
                max_steps=args.max_steps_per_episode,
                time_scale=args.time_scale,
            )
        )

    train_env = DummyVecEnv([lambda: make_env(args.scenario)])
    eval_env = DummyVecEnv([lambda: make_env(args.eval_scenario)])

    if args.dry_run:
        print(f"[city-autonomy] dry-run OK: artifact_dir={artifact_dir}")
        return 0

    if args.resume is not None:
        model = PPO.load(str(args.resume), env=train_env, device=args.device)
    else:
        model = PPO(
            "MultiInputPolicy",
            train_env,
            learning_rate=args.learning_rate,
            n_steps=args.n_steps,
            batch_size=args.batch_size,
            gamma=args.gamma,
            gae_lambda=args.gae_lambda,
            ent_coef=args.ent_coef,
            clip_range=args.clip_range,
            verbose=1,
            device=args.device,
            seed=args.seed,
            tensorboard_log=str(artifact_dir / "tb"),
        )

    callbacks = [
        CheckpointCallback(
            save_freq=args.checkpoint_interval,
            save_path=str(artifact_dir / "checkpoints"),
            name_prefix="ppo",
        ),
        EvalCallback(
            eval_env,
            best_model_save_path=str(artifact_dir / "best"),
            log_path=str(artifact_dir / "eval"),
            eval_freq=args.eval_interval,
            n_eval_episodes=args.eval_episodes,
            deterministic=True,
            render=False,
        ),
    ]

    print(f"[city-autonomy] starting PPO.learn() — total_timesteps={args.total_timesteps}")
    model.learn(total_timesteps=args.total_timesteps, callback=callbacks)

    final_path = artifact_dir / f"{ARTIFACT_FAMILY}-{args.rev}_sb3.zip"
    model.save(str(final_path))
    print(f"[city-autonomy] saved final checkpoint: {final_path}")
    return 0


if __name__ == "__main__":  # pragma: no cover
    raise SystemExit(main())
