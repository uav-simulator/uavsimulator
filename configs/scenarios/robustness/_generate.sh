#!/bin/bash
# Generate the 10 robustness eval scenarios. Idempotent — safe to re-run.
set -euo pipefail
OUT_DIR="$(dirname "$0")"
mkdir -p "$OUT_DIR"

# Parameter table: name | length | left_turns | right_turns | corridor_width
declare -a SCENARIOS=(
  "short_L_3c    3 0 1 0.60"
  "medium_L_4c   4 0 1 0.60"
  "long_L_7c     7 0 1 0.60"
  "long_L_9c     9 0 1 0.60"
  "left_turn     5 1 0 0.60"
  "straight_5c   5 0 0 0.60"
  "zigzag_6c_RL  6 1 1 0.60"
  "zigzag_7c_RR  7 0 2 0.60"
  "narrow_05m    5 0 1 0.50"
  "wide_07m      5 0 1 0.70"
)

for row in "${SCENARIOS[@]}"; do
  read -r name L LT RT W <<< "$row"
  cat > "$OUT_DIR/${name}.yaml" <<EOF
scenarioId: robustness-${name}
displayName: Robustness ${name}
version: 1
runtime: {runtimeMode: unity-sim, headless: false, timeScale: 1.0, seed: 42}
world:
  trackId: track.cardboard_maze.v1
  params:
    maze.seed: 42
    maze.length_cells: ${L}
    maze.corridor_width_m: ${W}
    maze.left_turns: ${LT}
    maze.right_turns: ${RT}
    maze.wall_height_m: 0.25
vehicle: {vehicleId: vehicle.ks0223.v1, params: {camera.profile: high}}
sensors:
  camera: {enabled: true, profile: high}
  telemetry: {profile: default}
  lineTracker: {enabled: false}
  ultrasonic: {enabled: true}
route: {params: {goal.radius_m: 0.24}, loop: false, reachDistanceM: 0.24}
agents: {count: 1, isolated: true, seeEachOther: false}
logging: {enabled: true, tag: robustness-${name}}
EOF
done
echo "generated $(ls "$OUT_DIR"/*.yaml | wc -l | tr -d ' ') scenarios in $OUT_DIR"
