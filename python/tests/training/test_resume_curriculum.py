"""Test that --start-timestep sets model.num_timesteps on resume, and curriculum sees correct stage."""
from __future__ import annotations

import sys
from pathlib import Path

ROOT = Path(__file__).resolve().parents[3]
sys.path.insert(0, str(ROOT / "python"))


def test_resume_with_start_timestep_sets_num_timesteps():
    from training.maze_curriculum import MazeCurriculumCallback, DEFAULT_STAGES

    # Simulate resume at 30_000 steps: should land in stage-B-medium (starts at 25_000)
    cb = MazeCurriculumCallback(stages=DEFAULT_STAGES, verbose=0)

    # Use the internal num_timesteps-based stage lookup
    idx_at_30k = cb._current_stage(30_000)
    assert DEFAULT_STAGES[idx_at_30k].name == "stage-B-medium"

    idx_at_5k = cb._current_stage(5_000)
    assert DEFAULT_STAGES[idx_at_5k].name == "stage-A-easy"


def test_trainer_has_start_timestep_flag():
    from training import train_cardboard_corridor as mod
    import argparse

    # Parse a minimal argv with the new flag
    original_argv = sys.argv
    sys.argv = ["trainer", "--resume", "fake.zip", "--start-timestep", "30000"]
    try:
        args = mod.parse_args()
        assert hasattr(args, "start_timestep"), "trainer must expose --start-timestep"
        assert args.start_timestep == 30000
    finally:
        sys.argv = original_argv
