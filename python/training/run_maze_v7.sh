#!/bin/bash
# Sprint 3 overnight training: maze curriculum with transfer from v6 (100% SR on L-corridor).
# Run under caffeinate to prevent Mac sleep.
#
# Stage schedule (see maze_curriculum.py):
#   0–40k   stage-A-easy   : length 3-4, 0-1 turns, width 0.58-0.62 (near-L-corridor)
#   40k–100k stage-B-medium : length 4-6, 0-2 turns, width 0.55-0.65
#   100k–170k stage-C-hard  : length 5-8, 1-3 turns, width 0.52-0.70
#   170k+    stage-D-full   : length 5-12, 1-4 turns, width 0.50-0.80
set -euo pipefail

ROOT="<repo>"
cd "$ROOT"

V6_CHECKPOINT="$ROOT/python/training/artifacts/cardboard-corridor-ppo-v6/1.0.0/checkpoints/cardboard_cnn_ppo_250000_steps.zip"
LOG_FILE="$ROOT/python/training/logs/maze_v7_$(date +%Y%m%d_%H%M%S).log"

mkdir -p "$(dirname "$LOG_FILE")"

echo "=== Sprint 3: cardboard-maze-ppo-v7 (attempt 3) ==="
echo "log: $LOG_FILE"
echo "transfer source: $V6_CHECKPOINT"
echo "curriculum stage-A: 5 cells, 1 right turn (L-shape like v6, but long enough for stable gradients)"
echo

exec python3 python/training/train_cardboard_corridor.py \
  --scenario configs/scenarios/cardboard-maze-v1.yaml \
  --resume "$V6_CHECKPOINT" \
  --total-timesteps 100000 \
  --max-ep-steps 400 \
  --time-scale 3.0 \
  --checkpoint-freq 5000 \
  --device mps \
  --model-name cardboard-maze-ppo-v7 \
  --model-version 1.0.0 \
  --curriculum \
  --maze-regen-every 5 \
  --learning-rate 1e-4 \
  --clip-range 0.15 \
  --ent-coef 0.01 \
  --aruco-goal \
  --aruco-distance-m 0.50
