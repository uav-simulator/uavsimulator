"""Tests for the single-run sweep wrapper command plumbing."""
from __future__ import annotations

import json
import sys

from training.bc import run_single


def test_run_single_passes_maze_env_flags_to_train_and_eval(tmp_path, monkeypatch):
    """run_single must translate sweep env kwargs into real train/eval flags."""
    output_dir = tmp_path / "run"
    captured: list[list[str]] = []

    def fake_call(cmd):
        captured.append(list(cmd))
        if "train_cardboard_corridor_v9.py" in cmd[1]:
            (output_dir / "cardboard-corridor-ppo-v9_sb3.zip").write_bytes(b"fake sb3")
        elif "evaluate_v9.py" in cmd[1]:
            (output_dir / "eval_full.json").write_text(
                json.dumps({"successRate": 0.0, "episodes": 1, "avgReward": 0.0})
            )
        return 0

    monkeypatch.setattr(run_single.subprocess, "call", fake_call)
    monkeypatch.setattr(
        sys,
        "argv",
        [
            "run_single.py",
            "--seed",
            "42",
            "--total-timesteps",
            "30000",
            "--output-dir",
            str(output_dir),
            "--scenario",
            "configs/scenarios/cardboard-maze-bc.yaml",
            "--env-kwargs-json",
            json.dumps(
                {
                    "base_url": "http://127.0.0.1:8000",
                    "max_steps": 600,
                    "sim_time_scale": 4.0,
                    "maze_randomize": True,
                    "maze_regen_every": 1,
                }
            ),
            "--frame-stack",
            "4",
        ],
    )

    run_single.main()

    train_cmd = captured[0]
    eval_cmd = captured[1]

    assert "--base-url" in train_cmd
    assert train_cmd[train_cmd.index("--base-url") + 1] == "http://127.0.0.1:8000"
    assert "--max-ep-steps" in train_cmd
    assert train_cmd[train_cmd.index("--max-ep-steps") + 1] == "600"
    assert "--time-scale" in train_cmd
    assert train_cmd[train_cmd.index("--time-scale") + 1] == "4.0"
    assert "--maze-randomize" in train_cmd
    assert "--maze-regen-every" in train_cmd
    assert train_cmd[train_cmd.index("--maze-regen-every") + 1] == "1"
    assert "--frame-stack" in train_cmd
    assert train_cmd[train_cmd.index("--frame-stack") + 1] == "4"

    assert "--base-url" in eval_cmd
    assert eval_cmd[eval_cmd.index("--base-url") + 1] == "http://127.0.0.1:8000"
    assert "--max-steps" in eval_cmd
    assert eval_cmd[eval_cmd.index("--max-steps") + 1] == "600"
    assert "--maze-randomize" in eval_cmd
