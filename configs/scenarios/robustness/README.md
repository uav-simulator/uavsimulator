# Robustness eval scenarios

Fixed maze configurations covering length, turn-count, turn-direction, and width
variations around the stage-A training distribution. Each scenario uses seed=42
for reproducibility.

| Scenario | Purpose |
|---|---|
| short_L_3c | In-distribution lower bound (trained min length) |
| medium_L_4c | Between train and eval distributions |
| long_L_7c / long_L_9c | Length generalization |
| left_turn | Turn-direction generalization (train was all right turns) |
| straight_5c | Zero-turn generalization |
| zigzag_6c_RL | Alternating L+R turns (produced −206 reward for v7@35k, flag) |
| zigzag_7c_RR | Multiple same-direction turns |
| narrow_05m | Width generalization down |
| wide_07m | Width generalization up |

Success metric for sprint 3: v8 should achieve SR > 50% on at least 3 of these 10.
