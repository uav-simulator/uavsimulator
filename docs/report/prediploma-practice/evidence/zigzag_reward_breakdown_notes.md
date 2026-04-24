# Zigzag reward-anomaly investigation (v7 @ 35k)

**Task:** Sprint-3 Task 3 — investigate the −206 reward reported by the robustness
sweep on the `zigzag_6c_RL` scenario (6 cells, 1 left + 1 right turn budget).

**Instrumentation:** `ABCorridorVisionEnv.step()` now emits
`info["reward_breakdown"]` with per-component floats. Smoke run on the corridor
scenario confirmed the breakdown sums to the step total within float precision
(|r − Σbreakdown| ≈ 0 across 10 steps).

**Diagnostic run:** `python3 python/diagnostics/diagnose_zigzag_reward.py`
(3 episodes, seeds 9000/9001/9002, model
`cardboard-maze-ppo-v7_sb3.zip` loaded on CPU, env kwargs match Step 4 of the
plan). CSV at `zigzag_reward_breakdown.csv`.

## Per-episode totals

| ep | seed | steps | termination    | total reward |
|---:|-----:|------:|:---------------|-------------:|
| 0  | 9000 | 23    | out_of_bounds  | −17.89       |
| 1  | 9001 | 26    | out_of_bounds  | −17.84       |
| 2  | 9002 | 25    | out_of_bounds  | −17.68       |

Mean: **−17.80**. The −206 outlier from the robustness sweep was **not**
reproduced in this run.

## Aggregate per-component sums (3 episodes, Step 6 output)

```
  oob_penalty               -90.0
  total                     -53.4
  time                       -1.5
  lateral_penalty            -1.3
  steer_jerk                 -0.3
  goal_bonus                  0.0
  stall_penalty               0.0
  termination                 0.0
  speed                       2.0
  progress_fraction           2.8     (env progress 0..1, not a reward component)
  progress                    7.7     (reward component: Δprogress × 20)
  waypoint_bonus             30.0
  episode                    76.0    (aggregate of ep index 0+1+2 × steps)
  step                      878.0    (aggregate of step indices)
```

## Dominant negative contributor

**`oob_penalty` (−30 per episode, −90 total, ~56 % of negative reward).** Every
episode terminated early (~23–26 steps) because the agent drove outside the
corridor. Secondary contributors — `time`, `lateral_penalty`, `steer_jerk` —
together sum to only ~−3 across all three episodes.

Per-episode arithmetic also matches:
`-30 (oob) + 10 (one waypoint bonus) + ≈2.5 (progress) + ≈0.7 (speed) + ≈-0.5 (time) + ≈-0.4 (lateral) + ≈-0.1 (jerk) ≈ -17.8` ✓.

## Is this a legitimate signal or a reward-function bug?

**Short answer: ambiguous. In this run, the reward profile is a legitimate
"agent can't do zigzags yet" signal — no single component fires pathologically.
We did not reproduce the −206 outlier, so we cannot confirm/refute a bug in it.**

Why this specific run looks clean:

- `oob_penalty` is a one-shot `-30` on termination — by design, not a per-step
  multiplier, so it cannot compound to −206.
- `lateral_penalty` is bounded at `−3.0` per step (squared wall proximity
  clamped at 1.0). Even a full 400-step run pressed against the wall would be
  `400 × −3.0 = −1200`, but typical wall grazing averages much less. In our
  three episodes, lateral averaged `−1.3/75 steps ≈ −0.02/step`.
- `progress` reward is unbounded in theory but the progress value itself is
  clipped to `[0, 1]` in `_route_progress`, so the sum `Σ Δprogress × 20` over
  a whole episode is bounded by `±20`.
- The agent never spent long enough in the maze to accumulate a large negative
  from any component — OOB kills it in ~25 steps.

How could −206 arise? The theoretical worst case is a long episode that stays
in-bounds but grazes walls repeatedly while the corridor direction keeps
changing. If the policy fails to track center in a zigzag, `lateral_penalty`
can realistically contribute `−1.5 to −2.5` per step. Over a `max_steps=400`
evaluation, that alone is `−600 to −1000`. **So −206 is reachable if the
agent survives ≈80–130 steps of wall grazing without triggering OOB or stall.**

Why our run didn't see it: the OOB threshold is very tight on a 0.60 m
corridor (`oob_threshold = 0.60/2 − 0.3 = 0.0` then clamped up to 0.05 m).
The agent almost immediately falls out. The robustness sweep may have used
a different `oob_margin_m`, a different `max_steps`, or simply hit seeds
where the policy survived longer.

## Proposed fix (do NOT implement in this plan)

No urgent reward-function bug confirmed. Still, two hardening candidates
worth considering for a separate patch plan:

1. **Cap `lateral_penalty` by episode** — e.g., clip the cumulative per-episode
   lateral penalty to `−2 × oob_penalty` (≈−60). Prevents long-survival tail
   where wall grazing dwarfs the termination penalty and destabilises
   policy gradients.
2. **Clamp per-step lateral to `−1.0`** instead of `−3.0 × wall_proximity²`.
   Current shaping gives a large negative spike whenever the agent is near the
   wall — fine for "don't hug walls" signal, potentially too punishing when
   corridor width is small and the agent is forced close to walls during
   turns anyway (e.g., 0.60 m corridor + 0.3 m body ≈ agent _always_ has high
   `wall_proximity`).

Both are shaping-stability concerns, not correctness bugs.

## Recommended follow-up experiment

Re-run the diagnostic with the **same seeds and env kwargs the robustness
sweep used** — most importantly, check whether the sweep used
`oob_margin_m > 0.3` (softer OOB, longer episodes) or a different
`max_steps`. If we can reproduce the −206 under those conditions and see
`lateral_penalty` dominating, that confirms the shaping hypothesis. If
`oob_penalty` still dominates and the sweep simply logged `-30 × 10` truncated
weirdly, the "anomaly" is an aggregation artifact rather than a per-step
signal. Either outcome is cheap to confirm once we recover the sweep
config — surface as a ~30 min follow-up before the next retrain.
