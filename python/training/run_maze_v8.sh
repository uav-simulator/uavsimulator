#!/bin/bash
# Sprint 3 overnight run: v8 from scratch, no transfer.
#
# Curriculum schedule (maze_curriculum.py DEFAULT_STAGES):
#   0–25k     stage-A-easy   : 5 cells, 1R turn, width 0.58-0.62
#   25k–60k   stage-B-medium : 5-7 cells, 0-1L / 1-2R, width 0.55-0.65
#   60k–100k  stage-C-hard   : 6-8 cells, 1-2L / 1-3R, width 0.52-0.70
#   100k+     stage-D-full   : 6-10 cells, 1-3L / 1-3R, width 0.50-0.80
#
# Hyperparams: standard PPO defaults for from-scratch.
# Log redirection: handled by the caller (nohup ... > $LOG). Don't duplicate here.
set -euo pipefail

ROOT="<repo>"
cd "$ROOT"

echo "=== Sprint 3: cardboard-maze-ppo-v8 (from scratch, 3× runtime) ==="

exec python3 python/training/train_cardboard_corridor.py \
  --scenario configs/scenarios/cardboard-maze-v1.yaml \
  --total-timesteps 150000 \
  --max-ep-steps 400 \
  --time-scale 3.0 \
  --checkpoint-freq 5000 \
  --device mps \
  --num-envs 3 \
  --base-url http://127.0.0.1:8000 \
  --model-name cardboard-maze-ppo-v8 \
  --model-version 1.0.0 \
  --curriculum \
  --maze-regen-every 5 \
  --learning-rate 3e-4 \
  --clip-range 0.2 \
  --ent-coef 0.02 \
  --aruco-goal \
  --aruco-distance-m 0.50
