#!/bin/bash
# Morning eval for v7 — compares maze performance vs corridor baseline.
# Run from repo root. Assumes Unity runtime up on :8000.
set -euo pipefail

ROOT="<repo>"
cd "$ROOT"

V7_DIR="$ROOT/python/training/artifacts/cardboard-maze-ppo-v7/1.0.0"
OUT_DIR="$ROOT/docs/report/prediploma-practice/evidence"
mkdir -p "$OUT_DIR"

# Prefer final PPO zip; fall back to latest checkpoint if training died mid-run.
if [[ -f "$V7_DIR/cardboard-maze-ppo-v7_sb3.zip" ]]; then
  MODEL="$V7_DIR/cardboard-maze-ppo-v7_sb3.zip"
else
  MODEL=$(ls -t "$V7_DIR/checkpoints/"*.zip 2>/dev/null | head -1 || true)
fi

if [[ -z "${MODEL:-}" ]]; then
  echo "No v7 model found — did training run?" >&2
  exit 1
fi

echo "=== v7 eval (maze) ==="
echo "model: $MODEL"
python3 python/training/evaluate_cardboard_corridor.py \
  --scenario configs/scenarios/cardboard-maze-v1.yaml \
  --model "$MODEL" \
  --model-format ppo \
  --episodes 20 \
  --max-steps 400 \
  --seed-offset 5000 \
  --output-json "$OUT_DIR/eval_v7_maze_20ep.json"

echo
echo "=== v7 eval (corridor regression) ==="
python3 python/training/evaluate_cardboard_corridor.py \
  --scenario configs/scenarios/cardboard-corridor-v1.yaml \
  --model "$MODEL" \
  --model-format ppo \
  --episodes 20 \
  --max-steps 400 \
  --seed-offset 3000 \
  --output-json "$OUT_DIR/eval_v7_corridor_20ep.json"

echo
echo "=== Summary ==="
python3 -c "
import json
for label, path in [('maze', '$OUT_DIR/eval_v7_maze_20ep.json'), ('corridor', '$OUT_DIR/eval_v7_corridor_20ep.json')]:
    with open(path) as f:
        d = json.load(f)
    print(f'{label:10s}: SR={d.get(\"successRate\", 0):.0%}  avgReward={d.get(\"avgReward\", 0):.2f}  avgProgress={d.get(\"avgProgress\", 0):.2%}')
"
