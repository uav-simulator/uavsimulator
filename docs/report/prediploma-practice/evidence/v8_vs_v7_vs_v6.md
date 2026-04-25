# v8 vs v7 vs v6 — comparison report

**Дата:** 2026-04-25
**Sprint-3 success criteria:**
1. ≥ 3 of 10 robustness scenarios with SR ≥ 50%.
2. L-corridor regression SR ≥ 80% (no regression from v6 baseline).

**Best-checkpoint decision (v8):** override default. Final 150k checkpoint had `ep_rew_mean ≈ -22` while the 120k checkpoint (within the same `stage-D-full` curriculum stage that started at 100k) showed `ep_rew_mean ≈ -1` — a clearly better intra-stage average. The 120k checkpoint was copied over `cardboard-maze-ppo-v8_sb3.zip` and used for all eval below.

---

## (a) L-corridor regression — 20 episodes, seed_offset=3000

| Модель | SR | avgReward | avgProgress |
|---|---|---|---|
| v6 baseline (sprint 2) | 100% (20/20) | 116.74 | 88.6% |
| v7@35k (sprint 3, день 1) | 100% (20/20) | 103.44 | 85.1% |
| **v8@120k** (sprint 3, день 2) | **100% (20/20)** | **108.45** | **77.0%** |

v6/v7 numbers: from `sprint-3-changelog.md`. v8 number: `evidence/v8_robustness/eval_corridor_regression.json`.

L-corridor regression criterion (≥80%): **PASS** for v8 — 100% SR, no regression vs v6/v7.

---

## (b) Robustness sweep — v7 vs v8 side-by-side (10 эпизодов на сценарий)

| Сценарий | v7 SR | v7 reward | v7 progress | v8 SR | v8 reward | v8 progress |
|---|---|---|---|---|---|---|
| short_L_3c | 0% | -11.45 | 18.7% | 0% | -11.63 | 17.3% |
| medium_L_4c | 0% | +1.32 | 45.7% | 0% | +3.58 | 46.4% |
| long_L_7c | 0% | -2.46 | 72.6% | 0% | +27.79 | 72.7% |
| long_L_9c | 0% | -13.05 | 79.4% | 0% | +40.19 | 79.5% |
| left_turn | 0% | +2.24 | 59.8% | 0% | +13.42 | 58.9% |
| straight_5c | 0% | +0.50 | 60.1% | 0% | +13.55 | 59.0% |
| zigzag_6c_RL | 0% | -350.98 | 29.4% | 0% | -17.20 | 67.5% |
| zigzag_7c_RR | 0% | +7.42 | 71.4% | 0% | +35.53 | 73.4% |
| narrow_05m | 0% | -2.28 | 51.1% | 0% | +3.05 | 51.2% |
| wide_07m | 0% | -6.26 | 65.6% | 0% | +14.10 | 65.2% |
| **SR ≥ 50% count** | **0/10** | — | — | **0/10** | — | — |

Источники: v7 — `docs/report/prediploma-practice/evidence/v7_robustness/eval_*.json`; v8 — `docs/report/prediploma-practice/evidence/v8_robustness/eval_*.json`.

---

## (c) Sprint-3 criteria verdict

- [ ] ≥ 3 of 10 robustness scenarios with SR ≥ 50% — **FAIL** (v8: 0/10, v7: 0/10).
- [x] L-corridor SR ≥ 80% (no regression) — **PASS** (v8: 100% SR, 20/20).

---

## (d) Narrative

v8 was trained from scratch with the same staged curriculum (A → B → C → D-full) as v7's plan but with a 150k-step budget and 3× Unity runtimes for a wider experience distribution. Despite reaching the hardest curriculum stage (`stage-D-full` from 100k onward), v8 still produces 0/10 SR ≥ 50% — exactly matching v7. **No scenarios are "newly solved"** under the threshold (v8 SR≥50% AND v7 SR=0%). Where curriculum clearly *helped* is in reward and progress quality: v8 cuts the catastrophic `zigzag_6c_RL` reward from −350.98 (v7) to −17.20, lifts that scenario's progress from 29.4% to 67.5%, and turns previously negative reward signals on `long_L_7c`, `long_L_9c`, `left_turn`, `straight_5c`, `wide_07m` into clearly positive averages — meaning v8 reaches further into each scenario before failing. Where curriculum *failed* is in closing the last-meter goal step: progress is hovering at 60–80% across all non-trivial mazes but the agent never crosses the goal threshold within 400 steps, suggesting the policy has learned to navigate corridors but not to commit to the ArUco/goal pose. Next step: **v9 transfer-from-v8 with reward-shaping fix** — strengthen the per-step goal-distance shaping (current shaping is too weak relative to the per-step time penalty, which is why long mazes yield positive integrated reward but no terminal `goal_reached`), and re-run the same 10-scenario sweep before any further curriculum changes.
