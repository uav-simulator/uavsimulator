"""Tests for BC dataset loader (session JSONL + MP4 → (frame, ultrasonic, action) samples)."""
from __future__ import annotations

from pathlib import Path

import cv2
import numpy as np

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
    assert ACTION_NAMES == ["DirStop", "DirForward", "DirBack", "DirLeft", "DirRight"]
    assert ACTION_TO_INDEX == {n: i for i, n in enumerate(ACTION_NAMES)}


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


def test_load_session_direct_written_autopilot_pairs_frame_with_next_command(tmp_path: Path):
    """Direct auto_record demos write one MP4 frame per command tick.

    Their wall-clock command cadence is not exactly the MP4 fps, so timestamp
    alignment drifts and eventually clamps many labels to the final frame.
    The frame returned by `/step(command_i)` is the post-action observation.
    It is therefore the state used to choose command_{i+1}; frame i must pair
    with the next command label. The final frame has no next command and is
    dropped.
    """
    video_path = tmp_path / "autopilot_20260101_010101_direct.mp4"
    jsonl_path = tmp_path / "session_20260101_010101_direct.jsonl"

    writer = cv2.VideoWriter(
        str(video_path),
        cv2.VideoWriter_fourcc(*"mp4v"),
        8.0,
        (96, 96),
    )
    assert writer.isOpened()
    # BGR input. Loader returns RGB, so these become red, green, blue samples.
    for bgr in [(0, 0, 255), (0, 255, 0), (255, 0, 0)]:
        writer.write(np.full((96, 96, 3), bgr, dtype=np.uint8))
    writer.release()

    lines = [
        '{"timestamp":"2026-01-01T00:00:00.000000+00:00","type":"demo.started","payload":{"tag":"direct","source":"auto_record.py"}}',
    ]
    for i, command in enumerate(["DirStop", "DirForward", "DirLeft"]):
        # 0.16 s cadence at 8 fps gives timestamp targets 1, 3, 4 for the
        # three commands. Timestamp alignment would skip frame 0 and clamp the
        # later samples to the final frame, which is the bug this test guards.
        ts = f"2026-01-01T00:00:00.{(i + 1) * 160000:06d}+00:00"
        lines.append(
            f'{{"timestamp":"{ts}","type":"command.outgoing",'
            f'"payload":{{"command":"{command}","clientId":"auto-pilot"}}}}'
        )
        lines.append(
            f'{{"timestamp":"{ts}","type":"pose.snapshot",'
            f'"payload":{{"step_index":{i},"phase":"DRIVE"}}}}'
        )
    jsonl_path.write_text("\n".join(lines) + "\n", encoding="utf-8")

    samples = load_session(jsonl_path=jsonl_path, video_path=video_path)

    assert [s.action_idx for s in samples] == [
        ACTION_TO_INDEX["DirForward"],
        ACTION_TO_INDEX["DirLeft"],
    ]
    dominant_channels = [int(np.argmax(s.frame.mean(axis=(0, 1)))) for s in samples]
    assert dominant_channels == [0, 1]


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


def test_load_session_aligns_distances_8_to_direct_step_frames(tmp_path: Path):
    """Direct-step alignment must keep distances_8 on the paired MP4 frame.

    Regression guard for the new structured-context path: when the loader
    shifts frame i to command i+1, the aligned distances_8 vector must shift
    with the frame rather than stay on the original command index.
    """
    video_path = tmp_path / "autopilot_20260101_010101_direct.mp4"
    jsonl_path = tmp_path / "session_20260101_010101_direct.jsonl"
    dist_path = tmp_path / "distances_8_direct.npy"

    writer = cv2.VideoWriter(
        str(video_path),
        cv2.VideoWriter_fourcc(*"mp4v"),
        8.0,
        (96, 96),
    )
    assert writer.isOpened()
    for bgr in [(0, 0, 255), (0, 255, 0), (255, 0, 0)]:
        writer.write(np.full((96, 96, 3), bgr, dtype=np.uint8))
    writer.release()

    np.save(
        dist_path,
        np.asarray(
            [
                np.full((8,), 0.11, dtype=np.float32),
                np.full((8,), 0.22, dtype=np.float32),
                np.full((8,), 0.33, dtype=np.float32),
            ]
        ),
    )

    lines = [
        '{"timestamp":"2026-01-01T00:00:00.000000+00:00","type":"demo.started","payload":{"tag":"direct","source":"auto_record.py"}}',
    ]
    for i, command in enumerate(["DirStop", "DirForward", "DirLeft"]):
        ts = f"2026-01-01T00:00:00.{(i + 1) * 160000:06d}+00:00"
        lines.append(
            f'{{"timestamp":"{ts}","type":"command.outgoing",'
            f'"payload":{{"command":"{command}","clientId":"auto-pilot"}}}}'
        )
        lines.append(
            f'{{"timestamp":"{ts}","type":"pose.snapshot",'
            f'"payload":{{"step_index":{i},"phase":"DRIVE"}}}}'
        )
    jsonl_path.write_text("\n".join(lines) + "\n", encoding="utf-8")

    samples = load_session(jsonl_path=jsonl_path, video_path=video_path)

    assert len(samples) == 2
    assert np.allclose(samples[0].distances_8, np.full((8,), 0.11, dtype=np.float32))
    assert np.allclose(samples[1].distances_8, np.full((8,), 0.22, dtype=np.float32))


def test_load_session_frame_stack_builds_temporal_context(tmp_path: Path):
    """frame_stack=k must concatenate the previous k RGB frames and sonar reads."""
    video_path = tmp_path / "autopilot_20260101_010101_direct.mp4"
    jsonl_path = tmp_path / "session_20260101_010101_direct.jsonl"

    writer = cv2.VideoWriter(
        str(video_path),
        cv2.VideoWriter_fourcc(*"mp4v"),
        8.0,
        (96, 96),
    )
    assert writer.isOpened()
    for bgr in [(0, 0, 255), (0, 255, 0), (255, 0, 0), (0, 255, 255)]:
        writer.write(np.full((96, 96, 3), bgr, dtype=np.uint8))
    writer.release()

    lines = [
        '{"timestamp":"2026-01-01T00:00:00.000000+00:00","type":"demo.started","payload":{"tag":"direct","source":"auto_record.py"}}',
    ]
    commands = ["DirStop", "DirForward", "DirLeft", "DirRight"]
    ultras = [0.4, 0.8, 1.2, 1.6]
    for i, (command, ultra_m) in enumerate(zip(commands, ultras)):
        ts = f"2026-01-01T00:00:00.{(i + 1) * 160000:06d}+00:00"
        lines.append(
            f'{{"timestamp":"{ts}","type":"telemetry.snapshot",'
            f'"payload":{{"sensors":{{"ultrasonic_m":{ultra_m}}}}}}}'
        )
        lines.append(
            f'{{"timestamp":"{ts}","type":"command.outgoing",'
            f'"payload":{{"command":"{command}","clientId":"auto-pilot"}}}}'
        )
        lines.append(
            f'{{"timestamp":"{ts}","type":"pose.snapshot",'
            f'"payload":{{"step_index":{i},"phase":"DRIVE"}}}}'
        )
    jsonl_path.write_text("\n".join(lines) + "\n", encoding="utf-8")

    samples = load_session(jsonl_path=jsonl_path, video_path=video_path, frame_stack=3)

    assert len(samples) == 3
    first = samples[0]
    second = samples[1]
    assert first.frame.shape == (84, 84, 9)
    assert second.frame.shape == (84, 84, 9)
    assert np.allclose(first.ultrasonic, np.asarray([0.2, 0.2, 0.2], dtype=np.float32))
    assert np.allclose(second.ultrasonic, np.asarray([0.2, 0.2, 0.3], dtype=np.float32))
    first_channel_means = [
        int(np.argmax(first.frame[:, :, offset:offset + 3].mean(axis=(0, 1))))
        for offset in (0, 3, 6)
    ]
    second_channel_means = [
        int(np.argmax(second.frame[:, :, offset:offset + 3].mean(axis=(0, 1))))
        for offset in (0, 3, 6)
    ]
    assert first_channel_means == [0, 0, 0]
    assert second_channel_means == [0, 0, 1]
