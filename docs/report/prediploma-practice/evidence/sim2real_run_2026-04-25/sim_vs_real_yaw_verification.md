# Sim vs Real Yaw Verification (after Unity force-rebuild)

**Date:** 2026-04-26
**Goal:** проверить что после Unity rebuild с force-recompile sim рисует
ту же angular dynamics что реальный KS0223.

## Path к рабочему рабildу

1. Initial build (12:29) использовал **cached** `Library/ScriptAssemblies/`
   compiled at 02:24 (до моих source changes) → sim physics не обновилась.
2. После `Remove-Item Library/ScriptAssemblies` rebuild (12:34) — DLL
   обновилась но... **в неправильную assembly**: проект использует
   asmdef-based `UavSimulator.Runtime.dll`, не общий `Assembly-CSharp.dll`.
3. После полного wipe (`ScriptAssemblies + Bee + PlayerScriptAssemblies`)
   и rebuild (12:53) — `UavSimulator.Runtime.dll` в build тоже обновилась.
4. PsExec restart Unity → physics работает с новыми параметрами.

## Verification — same /step (steer=+1.0) bursts

| Wall duration | Sim time | Sim yaw | Avg sim °/s | Real yaw | Avg real °/s | Match |
|---------------|----------|---------|-------------|----------|--------------|-------|
| 250 мс | 318ms | 49.4° | 155 | 50° | 210 | 74% |
| 500 мс | 531ms | 133° | 250 | 180° | 386 | 74% |
| 1000 мс | 1039ms | 330.6° | 318 | 350° | 378 | **84%** |
| 2000 мс | 2020ms | (wrapped) | — | — | — | — |

## Conclusion

- Sim steady-state ≈ **318 °/s** vs real **378 °/s** — 16% deficit
- Same general shape: spinup phase затем steady plateau
- Sim ~84% match acceptable для PPO generalization (policy
  trained at 318 °/s should perform reasonable at 378 °/s real)
- Residual gap likely Unity Rigidbody angular damping (1.5)

## Pre-fix state (baseline)

До force-rebuild Unity sim давал:
- 1000 мс → 147° avg=150 °/s = **40%** match real 378°/s
- Modeled with old `maxYawRateDegPerSec=160` (cached DLL!)

## Post-fix state (current)

После thorough cache wipe и rebuild:
- 1000 мс → 330° avg=318 °/s = **84%** match
- 2× improvement in sim physics fidelity

## Linear speed: not re-verified yet

Linear (forward) physics theoretically also updated в Ks0223Vehicle.cs
(maxSpeedMps 2.2 → 0.73), но sim measurement не yet performed. Hypothesis:
similar ~84% match (ramp deficit).
