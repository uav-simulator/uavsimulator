#!/usr/bin/env python3
"""Dump first-frame camera + telemetry from corridor and maze scenarios for comparison."""
from __future__ import annotations

import base64
import json
import sys
from io import BytesIO
from pathlib import Path

from PIL import Image

ROOT = Path(__file__).resolve().parents[2]
PYTHON_ROOT = ROOT / "python"
sys.path.insert(0, str(PYTHON_ROOT))

from sim_client.http_client import SimClient
from sim_client.scenario import load_scenario_file, scenario_to_reset_config


def dump(scenario_path: Path, out_dir: Path, tag: str):
    out_dir.mkdir(parents=True, exist_ok=True)
    client = SimClient("http://127.0.0.1:8000", timeout_s=30.0)
    cfg = scenario_to_reset_config(load_scenario_file(scenario_path))
    cfg["seed"] = 42
    step = client.reset(cfg)

    frame_b64 = step.get("frame", {}).get("dataBase64", "")
    if frame_b64:
        img = Image.open(BytesIO(base64.b64decode(frame_b64)))
        img.save(out_dir / f"{tag}_frame.png")
        print(f"  {tag}: frame saved ({img.size})")
    else:
        print(f"  {tag}: WARNING no frame in /step response (camera streaming disabled?)")

    telemetry = {
        "tag": tag,
        "scenario": str(scenario_path),
        "raw_telemetry": step.get("state", {}),
        "trackParams": cfg.get("trackParams", []),
    }
    with (out_dir / f"{tag}_telemetry.json").open("w") as f:
        json.dump(telemetry, f, indent=2)
    pose = step.get("state", {}).get("pose", {})
    print(f"  {tag}: pose={pose}")


def main() -> int:
    out_dir = ROOT / "docs/report/prediploma-practice/evidence/spawn_compare"
    dump(ROOT / "configs/scenarios/cardboard-corridor-v1.yaml", out_dir, "corridor")
    dump(ROOT / "configs/scenarios/cardboard-maze-stageA-eval.yaml", out_dir, "maze_stageA")
    print(f"\nArtifacts in {out_dir}")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
