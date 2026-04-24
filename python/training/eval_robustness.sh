#!/bin/bash
# Evaluate a model on the 10 robustness scenarios in configs/scenarios/robustness/.
# Usage: bash python/training/eval_robustness.sh <model_path> <out_dir> [episodes]
#   model_path: absolute or repo-relative path to SB3 .zip
#   out_dir   : directory to write per-scenario JSONs (created)
#   episodes  : default 10
#
# Assumes Unity runtime on :8000 (single-runtime mode — eval is fast enough serial).
set -euo pipefail

MODEL="${1:?model path required}"
OUT_DIR="${2:?output dir required}"
EPISODES="${3:-10}"

ROOT="<repo>"
cd "$ROOT"
mkdir -p "$OUT_DIR"

SCENARIOS=(short_L_3c medium_L_4c long_L_7c long_L_9c left_turn straight_5c zigzag_6c_RL zigzag_7c_RR narrow_05m wide_07m)

for sc in "${SCENARIOS[@]}"; do
  echo "=== $sc ==="
  python3 python/training/evaluate_cardboard_corridor.py \
    --scenario "configs/scenarios/robustness/${sc}.yaml" \
    --model "$MODEL" \
    --model-format ppo \
    --episodes "$EPISODES" \
    --max-steps 400 \
    --seed-offset 9000 \
    --output-json "$OUT_DIR/eval_${sc}.json" > "$OUT_DIR/log_${sc}.txt" 2>&1
  python3 -c "
import json
with open('$OUT_DIR/eval_${sc}.json') as f: d = json.load(f)
print(f'  {\"${sc}\":<18s} SR={d[\"successRate\"]:>4.0%}  reward={d[\"avgReward\"]:>+8.1f}  progress={d[\"avgProgress\"]:>5.1%}')
"
done

echo
echo "Summary table:"
python3 python/training/summarize_eval_results.py "$OUT_DIR"
