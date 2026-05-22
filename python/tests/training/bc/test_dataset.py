"""Tests for BC dataset loader (session JSONL + MP4 → (frame, ultrasonic, action) samples)."""
from __future__ import annotations

from pathlib import Path

import numpy as np
import pytest

from training.bc.dataset import (
    ACTION_NAMES,
    ACTION_TO_INDEX,
    discover_pairs,
    load_dataset,
    load_session,
)

FIXTURES = Path(__file__).parent / "fixtures"


def test_action_index_matches_env_wrapper():
    """BC dataset uses same action indexing as DiscreteActionWrapper."""
    assert ACTION_TO_INDEX == {
        "DirStop": 0,
        "DirForward": 1,
        "DirBack": 2,
        "DirLeft": 3,
        "DirRight": 4,
    }
    # Sanity: keep names list aligned with the dict order.
    assert ACTION_NAMES == ["DirStop", "DirForward", "DirBack", "DirLeft", "DirRight"]


def test_load_session_returns_aligned_arrays():
    samples = load_session(
        jsonl_path=FIXTURES / "mini_session.jsonl",
        video_path=FIXTURES / "mini_session.mp4",
    )
    assert len(samples) > 0, "Mini fixture must yield at least one (frame, ultrasonic, action) sample"

    s = samples[0]
    assert s.frame.shape == (84, 84, 3), f"frame shape {s.frame.shape} != (84,84,3)"
    assert s.frame.dtype == np.uint8, f"frame dtype {s.frame.dtype} != uint8"
    assert 0.0 <= s.ultrasonic <= 1.0, f"ultrasonic {s.ultrasonic} outside [0,1]"
    assert 0 <= s.action_idx < len(ACTION_NAMES), f"action_idx {s.action_idx} outside [0,5)"


def test_load_dataset_aggregates_multiple_sessions():
    """`load_dataset` flattens samples from multiple (jsonl, mp4) pairs."""
    samples = load_dataset(
        [
            (FIXTURES / "mini_session.jsonl", FIXTURES / "mini_session.mp4"),
            (FIXTURES / "mini_session.jsonl", FIXTURES / "mini_session.mp4"),
        ]
    )
    single = load_dataset([(FIXTURES / "mini_session.jsonl", FIXTURES / "mini_session.mp4")])
    assert len(samples) == 2 * len(single), (
        f"two-copy load ({len(samples)}) must be exactly 2× single load ({len(single)})"
    )


def test_discover_pairs_matches_jsonl_with_autopilot_mp4(tmp_path: Path):
    """Pair `session_<ts>_*.jsonl` with `autopilot_<ts>_*.mp4` (sprint-3 naming)."""
    # Two paired sessions
    (tmp_path / "session_20260101_010101_runA.jsonl").write_text("{}\n", encoding="utf-8")
    (tmp_path / "autopilot_20260101_010101_runA.mp4").write_bytes(b"\x00")
    # And one same-stem MP4 (newer sprint-4 naming convention)
    (tmp_path / "session_20260202_020202_runB.jsonl").write_text("{}\n", encoding="utf-8")
    (tmp_path / "session_20260202_020202_runB.mp4").write_bytes(b"\x00")
    # And one orphan JSONL with no matching MP4 — should be skipped
    (tmp_path / "session_20260303_030303_runC.jsonl").write_text("{}\n", encoding="utf-8")

    pairs = discover_pairs(tmp_path)
    paired_stems = {p[0].stem for p in pairs}
    assert paired_stems == {
        "session_20260101_010101_runA",
        "session_20260202_020202_runB",
    }
