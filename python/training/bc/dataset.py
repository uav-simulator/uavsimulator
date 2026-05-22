"""Load operator demonstration sessions into BC-friendly (frame, ultrasonic, action) tuples.

Each demo is a pair:
- `session_*.jsonl` — newline-delimited events (commands + telemetry meta + lifecycle).
- companion MP4 — camera frames recorded during the same session.

The two streams are independent and the JSONL does not contain a video PTS, so we align
by wall-clock timestamps:
  video_start_ts := timestamp of the first lifecycle event (`log.started` / `demo.started`).
                    Fallback: the first valid command timestamp.
  frame_idx      := round((command_ts - video_start_ts) * fps).

Telemetry handling: ideally each `command.outgoing` is paired with the most recent
ultrasonic reading. Sprint-3 sessions only log telemetry *metadata* (size / field count),
not the actual sensor values, so the loader gracefully falls back to a default normalised
distance of 1.0. Sprint-4 sessions (Task A.1) are expected to log full snapshots under
`telemetry.snapshot` events with `payload.sensors.ultrasonic_m`.

Action indexing matches `training.discrete_action_wrapper.ACTION_TABLE`:
    DirStop=0, DirForward=1, DirBack=2, DirLeft=3, DirRight=4.
"""
from __future__ import annotations

import json
from dataclasses import dataclass
from datetime import datetime
from pathlib import Path

import cv2
import numpy as np

ACTION_NAMES = ["DirStop", "DirForward", "DirBack", "DirLeft", "DirRight"]
ACTION_TO_INDEX = {name: idx for idx, name in enumerate(ACTION_NAMES)}

ULTRASONIC_MAX_M = 4.0  # cf. ABCorridorVisionEnv normalisation
FRAME_SIZE = 84
DEFAULT_FPS = 30.0
DEFAULT_ULTRASONIC_NORM = 1.0  # "far / unknown" — same default as the env on missing reads


# Event types that mark the start of the recording (= MP4 frame 0).
_LIFECYCLE_START_EVENTS = ("demo.started", "log.started")
# Event types that may carry ultrasonic readings under `payload.sensors.ultrasonic_m`.
_TELEMETRY_EVENTS = ("telemetry.snapshot", "sensor.telemetry.snapshot")


@dataclass(frozen=True)
class BcSample:
    """One supervised pair extracted from a demo session."""

    frame: np.ndarray  # (84, 84, 3) uint8 RGB
    ultrasonic: float  # normalised distance in [0, 1]
    action_idx: int    # 0..4 (cf. ACTION_NAMES)


def _parse_ts(ts: str) -> float:
    """ISO-8601 timestamp → unix seconds (float)."""
    return datetime.fromisoformat(ts.replace("Z", "+00:00")).timestamp()


def _resize_frame(bgr: np.ndarray) -> np.ndarray:
    """BGR HxWx3 → RGB 84x84x3 uint8."""
    rgb = cv2.cvtColor(bgr, cv2.COLOR_BGR2RGB)
    return cv2.resize(rgb, (FRAME_SIZE, FRAME_SIZE), interpolation=cv2.INTER_AREA)


def _iter_events(jsonl_path: Path):
    """Yield decoded events from a JSONL file. Skips blank lines and the BOM marker."""
    for raw in jsonl_path.read_text(encoding="utf-8").splitlines():
        line = raw.lstrip("﻿").strip()
        if not line:
            continue
        try:
            yield json.loads(line)
        except json.JSONDecodeError:
            # Tolerate corrupt single lines — common when a session is killed mid-flush.
            continue


def load_session(jsonl_path: Path, video_path: Path) -> list[BcSample]:
    """Load one demo session into a list of `BcSample`.

    Strategy:
      1. Stream JSONL line-by-line, tracking:
         - the first lifecycle-start timestamp (anchor for video alignment),
         - the last seen ultrasonic reading (carried forward to the next command),
         - every valid `command.outgoing` event with a recognised action name.
      2. Open the MP4 and, for each command, seek to
         `round((command_ts - video_start_ts) * fps)` and read one frame.
      3. Pack into `BcSample` and return.
    """
    events: list[tuple[float, str, float]] = []  # (ts, command, ultrasonic_norm)
    video_start_ts: float | None = None
    last_ultrasonic = DEFAULT_ULTRASONIC_NORM

    for ev in _iter_events(jsonl_path):
        ev_type = ev.get("type")
        ts_raw = ev.get("timestamp")
        ts = _parse_ts(ts_raw) if ts_raw else None

        if ev_type in _LIFECYCLE_START_EVENTS and ts is not None and video_start_ts is None:
            video_start_ts = ts
        elif ev_type in _TELEMETRY_EVENTS:
            ultra_m = ev.get("payload", {}).get("sensors", {}).get("ultrasonic_m")
            if ultra_m is not None:
                last_ultrasonic = float(np.clip(float(ultra_m) / ULTRASONIC_MAX_M, 0.0, 1.0))
        elif ev_type == "command.outgoing" and ts is not None:
            cmd = ev.get("payload", {}).get("command")
            if cmd in ACTION_TO_INDEX:
                events.append((ts, cmd, last_ultrasonic))

    if not events:
        return []

    if video_start_ts is None:
        video_start_ts = events[0][0]

    cap = cv2.VideoCapture(str(video_path))
    if not cap.isOpened():
        raise FileNotFoundError(f"Cannot open video: {video_path}")

    fps = cap.get(cv2.CAP_PROP_FPS)
    if not fps or fps <= 0:
        fps = DEFAULT_FPS  # rare: container lacked FPS metadata
    frame_count = int(cap.get(cv2.CAP_PROP_FRAME_COUNT) or 0)

    # Sequential read: events are sorted by timestamp (monotonic), so we advance the
    # decoder forward instead of `cap.set(CAP_PROP_POS_FRAMES, k)` per sample
    # (the latter forces seek-to-keyframe + decode-forward, 10-100× slower on H.264).
    samples: list[BcSample] = []
    next_frame_idx = 0
    current_bgr: np.ndarray | None = None
    ok = True
    try:
        for ts, cmd, ultra in events:
            offset_s = max(0.0, ts - video_start_ts)
            target = int(round(offset_s * fps))
            if frame_count > 0:
                target = min(target, frame_count - 1)
            # Advance sequentially until we reach the target frame.
            while next_frame_idx <= target:
                ok, bgr = cap.read()
                if not ok or bgr is None:
                    break
                current_bgr = bgr
                next_frame_idx += 1
            if not ok or current_bgr is None:
                continue
            samples.append(
                BcSample(
                    frame=_resize_frame(current_bgr),
                    ultrasonic=ultra,
                    action_idx=ACTION_TO_INDEX[cmd],
                )
            )
    finally:
        cap.release()

    return samples


def load_dataset(pairs: list[tuple[Path, Path]]) -> list[BcSample]:
    """Aggregate multiple `(jsonl, mp4)` pairs into one flat sample list."""
    out: list[BcSample] = []
    for jsonl_path, video_path in pairs:
        out.extend(load_session(Path(jsonl_path), Path(video_path)))
    return out


def discover_pairs(demos_dir: Path) -> list[tuple[Path, Path]]:
    """Find `(session_*.jsonl, MP4)` pairs in `demos_dir`.

    Handles two naming conventions:
      - Sprint-4+ (same stem):   `session_<ts>_<tag>.jsonl` + `session_<ts>_<tag>.mp4`
      - Sprint-3   (mp4 prefix): `session_<ts>_<tag>.jsonl` + `autopilot_<ts>_<tag>.mp4`
    JSONLs without any MP4 (e.g. `<ts>` shared with no recorded video) are skipped.
    """
    demos_dir = Path(demos_dir)
    pairs: list[tuple[Path, Path]] = []
    for jsonl in sorted(demos_dir.glob("session_*.jsonl")):
        stem = jsonl.stem  # e.g. session_20260428_004613_human-demo-...
        # 1) Same-stem MP4 (sprint-4 convention) — exact literal match, no prefix wildcard.
        same_stem = demos_dir / f"{stem}.mp4"
        if same_stem.exists():
            candidates = [same_stem]
        else:
            candidates = []
            # 2) MP4 sharing the YYYYMMDD_HHMMSS timestamp (sprint-3 convention).
            parts = stem.split("_")
            if len(parts) >= 3:
                ts = f"{parts[1]}_{parts[2]}"  # YYYYMMDD_HHMMSS
                candidates = list(demos_dir.glob(f"*{ts}*.mp4"))
        if candidates:
            pairs.append((jsonl, sorted(candidates)[0]))
    return pairs
