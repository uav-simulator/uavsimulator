# v8 run notes

Launched: 2026-04-25 01:28:20 (local)
Branch: develop @ cc88ba9 (B3 head before v8 launcher commit)
Runtime: 3× Unity windowed on :8000, :8001, :8002
Command: `bash python/training/run_maze_v8.sh` (under `caffeinate -is`, `nohup`)
PID file: `/tmp/maze_v8_train.pid` (PID=74984)
Log file: `python/training/logs/maze_v8_20260425_012820.log`

## Outcome

Training **completed** end-to-end at 150 000 steps in 4 159 s (≈ 69 min wall-clock).
Final FPS = 36 (over the whole run; first 7k steps at ~85 fps before SubprocVecEnv fully throttled to Unity step rate).
ep_rew_mean trajectory: -16 → -90 (early exploration) → stabilised at -30 by 12k steps → -22 at 150k.
0 Tracebacks, 0 NaNs.

ONNX export at finalisation failed non-fatally (FakeTensor cpu/mps device-propagation in the `VisionPolicyWrapper` export path — separate issue, does not affect SB3 training output). SB3 `.zip` saved fine.

Built-in 5-episode quick eval at training end:
- successRate 0%, avgProgress 56.8%, avgReward 13.3.
- All 5 episodes terminated as `runtime_done` after ~25 steps with progress 52–60% — the policy moves forward but stalls before reaching the goal.

This matches the from-scratch baseline expectation for sprint-3 success criterion (≥ 3/10 on robustness sweep) — final verdict comes from Task 5.

## Hyperparameters

- total_timesteps: 150000
- num_envs: 3 (SubprocVecEnv, ports 8000/8001/8002)
- device: mps
- lr: 3e-4, clip_range: 0.2, ent_coef: 0.02
- curriculum: enabled, `--maze-regen-every 5`
- aruco goal at 0.50 m

## Curriculum schedule

0–25k stage-A-easy, 25k–60k stage-B-medium, 60k–100k stage-C-hard, 100k+ stage-D-full.

## Artifacts

- `python/training/artifacts/cardboard-maze-ppo-v8/1.0.0/cardboard-maze-ppo-v8_sb3.zip` — final model.
- `python/training/artifacts/cardboard-maze-ppo-v8/1.0.0/checkpoints/cardboard_cnn_ppo_{15..150}000_steps.zip` — 10 intermediate checkpoints at 15k spacing.
- `python/training/artifacts/cardboard-maze-ppo-v8/1.0.0/metrics.json` — quick eval metrics.

## Next step

Task 5 picks up from here: pick best checkpoint (default = latest, `cardboard_cnn_ppo_150000_steps.zip`), restart Unity in single-runtime mode, run `bash python/training/eval_robustness.sh` against the 10 robustness scenarios, and produce `docs/report/prediploma-practice/evidence/v8_vs_v7_vs_v6.md`.
