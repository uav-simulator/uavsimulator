#!/usr/bin/env python3
"""Register existing Windows Unity build in rusim local registry."""

import sys
from pathlib import Path
from datetime import datetime

# rusim helpers expect to be invoked from repo root
REPO_ROOT = Path(r"<repo>")
sys.path.insert(0, str(REPO_ROOT / "python"))

from sim_client.cli import _register_external_runtime_build  # type: ignore

app_dir = REPO_ROOT / "build" / "runtime" / "windows"
exe = app_dir / "uav-simulator.exe"

if not exe.exists():
    raise SystemExit(f"Executable not found: {exe}")

build_id = f"local-windows-{datetime.now().strftime('%Y%m%d-%H%M%S')}"
entry = _register_external_runtime_build(
    build_id=build_id,
    version_label="local-windows-build",
    app_path=app_dir,
    executable_path=exe,
    unity_version="6000.1.8f1",
    source="local-batch-build",
    source_project_path=REPO_ROOT / "src" / "UnityProject" / "uav-simulator",
    metadata={"platform": "windows", "buildSource": "RuntimeBuildPipeline.BuildWindowsRuntime"},
)

# Mark as favorite for `--build favorite`
from sim_client.cli import _load_runtime_registry, _save_runtime_registry  # type: ignore
reg = _load_runtime_registry()
reg["favoriteBuildId"] = entry["buildId"]
_save_runtime_registry(reg)

print(f"Registered: buildId={entry['buildId']}")
print(f"  appPath: {entry['appPath']}")
print(f"  executablePath: {entry['executablePath']}")
print(f"  favorite: yes")
