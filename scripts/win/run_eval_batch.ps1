# Run policy eval batch on Win headless Unity sim.
#
# Lessons learned 2026-04-27:
#   - `-batchmode -nographics` makes HTTP work but produces broken/black camera
#     frames (URP shaders fail on null gfx device). Eval results are meaningless.
#   - `-batchmode` alone (no -nographics) works: AMD iGPU renders, takes ~60s
#     to come up. NVIDIA dGPU does NOT get selected from SSH session 0 even
#     with HKCU\Software\Microsoft\DirectX\UserGpuPreferences hint.
#   - Unity gets killed when the spawning SSH session closes. Start the sim
#     AND run the eval in a single SSH-invoked .ps1.
#
# Usage from Mac:
#   ssh win 'powershell -NoProfile -ExecutionPolicy Bypass -File <repo>\scripts\win\run_eval_batch.ps1 -Revs rev16,rev18,rev19d -Episodes 20 -SeedOffset 3000'

param(
  [string[]]$Revs = @('rev16'),
  [int]$Episodes = 20,
  [int]$SeedOffset = 3000,
  [int]$LatencySteps = 1,
  [string]$OutDir = 'docs\report\prediploma-practice\sprint-3-reeval-2026-04-27'
)

Set-Location <repo>
$Env:PYTHONPATH = 'python'

Get-Process uav-simulator -ErrorAction SilentlyContinue | Stop-Process -Force
Start-Sleep -Seconds 2

$exe = '.\build\runtime\windows\uav-simulator.exe'
$logfile = "C:\Users\<user>\sim-eval-batch-$(Get-Date -Format yyyyMMdd-HHmmss).log"
Write-Host "=== Starting Unity (-batchmode, render via iGPU) ==="
$proc = Start-Process -FilePath $exe -ArgumentList @('-batchmode','-logFile',$logfile,'-uavsimApiPort','8000','-force-d3d12') -PassThru -WindowStyle Hidden
$ok = $false
for ($i=1; $i -le 24; $i++) {
  Start-Sleep -Seconds 5
  try {
    if ((Invoke-WebRequest -Uri 'http://127.0.0.1:8000/health' -UseBasicParsing -TimeoutSec 1).StatusCode -eq 200) {
      $ok = $true; break
    }
  } catch {}
}
if (-not $ok) { Write-Host 'Sim never came up'; Get-Content $logfile -Tail 8; exit 1 }
Write-Host "Sim ready (PID=$($proc.Id))"

New-Item -ItemType Directory -Force -Path $OutDir | Out-Null
foreach ($rev in $Revs) {
    $onnx = "python\training\artifacts\cardboard-corridor-ppo-v9-$rev\1.0.0\cardboard-corridor-ppo-v9-$rev.onnx"
    $out = "$OutDir\eval-rendered-$rev.json"
    if (-not (Test-Path $onnx)) { Write-Host "[skip] $onnx missing"; continue }
    Write-Host ""
    Write-Host "=== EVAL $rev (eps=$Episodes) ==="
    & .\.venv\Scripts\python.exe python\training\evaluate_v9.py `
        --base-url http://127.0.0.1:8000 `
        --model $onnx `
        --episodes $Episodes `
        --seed-offset $SeedOffset `
        --latency-steps $LatencySteps `
        --output-json $out 2>&1 | Select-Object -Last 8
}

Stop-Process -Id $proc.Id -Force -ErrorAction SilentlyContinue
Write-Host ''
Write-Host '=== outputs ==='
Get-ChildItem $OutDir -Filter '*.json' | Select-Object Name, Length, LastWriteTime
