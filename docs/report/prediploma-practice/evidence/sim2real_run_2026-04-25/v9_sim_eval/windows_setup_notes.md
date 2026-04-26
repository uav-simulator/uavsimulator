# Windows compute node bringup — notes (2026-04-26)

## Summary

End-to-end edge-cloud setup готов: Mac (edge: robot control) ↔ Windows ПК (cloud: training).

| Layer | Status |
|---|---|
| OpenSSH server on Windows + key auth | ✅ |
| Mac SSH ControlMaster (~0.14s reuse) | ✅ |
| Python 3.13 + venv + sb3 + onnx + opencv | ✅ |
| **PyTorch 2.11.0+cu128 + RTX 5080 (sm_120 Blackwell)** | ✅ |
| Repo synced (git pull from origin) | ✅ |
| Unity 6000.1.8f1 + Windows Standalone build | ✅ (32s через batch mode) |
| `rusim` Windows runtime registered as favorite | ✅ |
| Mac → Win Unity HTTP API (`/reset`, `/step`) | ✅ (1280×720 JPEG @ 15 Hz) |
| PPO training on Win GPU from Mac SSH | ✅ (1000 steps in 32s) |

## Issues encountered & resolutions

### 1. PyTorch CUDA replaced by CPU build
**Issue:** После `pip install stable-baselines3 ...` torch перешёл с `2.11.0+cu128` на `2.11.0+cpu`.
**Cause:** sb3 имеет `torch>=1.x` dependency, pip overrode без CUDA index URL.
**Fix:**
```powershell
pip install --upgrade --force-reinstall torch torchvision --index-url https://download.pytorch.org/whl/cu128
```
Версии запинены в `windows_torch_pin.txt` для reproducibility.

### 2. SSH Session 0 isolation blocks Unity Standalone
**Issue:** Unity Standalone exit'ит с code -1 после Mono init при запуске через SSH (Session 0 services session).
**Cause:** Windows OpenSSH запускается как service в Session 0 — нет display/GPU device context. Unity нуждается в graphics device для D3D12 init, даже с `-batchmode -nographics`.
**Fix:** PsExec из Sysinternals — запускает процесс в активной user session (Session 1).

```powershell
# Download once:
Invoke-WebRequest -Uri "https://download.sysinternals.com/files/PSTools.zip" -OutFile C:\Tools\PSTools.zip
Expand-Archive C:\Tools\PSTools.zip -DestinationPath C:\Tools\PSTools

# Launch Unity in Session 1 (user desktop) detached:
C:\Tools\PSTools\PsExec64.exe -accepteula -i 1 -d "<unity-exe>" -logFile "<log>"
```

Долгосрочное решение: Unity Server Build target (отдельный headless build без graphics) — отнесено в backlog.

### 3. Unity build artefact landed in wrong directory
**Issue:** `RuntimeBuildPipeline.BuildWindowsRuntime()` через batch mode создал файлы в `<UnityProject>/build/...` а не `<repo>/build/...`.
**Cause:** `Path.GetFullPath(relative)` использует Unity Editor's CWD = Unity project dir, не repo root.
**Fix (manual move):** `Move-Item <UnityProject>/build/runtime/windows/* <repo>/build/runtime/windows/`.
**Long-term:** Modify pipeline to compute path relative to repo root (Application.dataPath/.../..). Backlogged.

### 4. cp1252 codec on Windows console fails on Unicode arrows
**Issue:** `print(f"... → ...")` падает с `UnicodeEncodeError` на Windows.
**Fix:** заменил `→` на `->` в launcher print statements + `$env:PYTHONIOENCODING="utf-8"` в SSH calls для full Unicode support.

### 5. rusim CLI hardcoded для macOS .app bundles
**Issue:** `_resolve_runtime_executable()` ожидал `.app` suffix, отказывал на Windows.
**Fix:** Расширен до cross-platform — handles `.app` (macOS), Windows directory с `.exe` files, Linux directory с executable файлом.

## Performance preview

- GPU benchmark (separate test): RTX 5080 vs Ryzen CPU = **34× speedup** на PPO-style CNN backprop
- Initial training smoke test (1 env, single Unity instance): 31 fps wall-clock на CUDA
- Expected with 16 envs (16-core Ryzen): ≥500 fps total → 200k steps in **~7 minutes** (vs 36 минут на Mac)

## Sample frame from Windows Unity
[windows_unity_sample_frame.jpg](./windows_unity_sample_frame.jpg) — 1280×720 JPEG получен с Mac через HTTP API на стартовой позиции cardboard_corridor_v1.

## Next

- Patch v9 launcher с `--use-psexec` flag для transparent Win SSH workflow
- Run v9-rev5 full 200k training на 16 envs CUDA
- Eval v9-rev5 в sim, проверить action distribution diversity (был collapse 100% DirForward на Mac runs)
- Deploy ONNX обратно на Mac → backend → реальный робот
