"""Render a 2x2 grid of 4 consecutive frames from a demo MP4, illustrating
what the policy 'sees' under temporal frame stacking with k=4.

Saves a PNG with the 4 frames laid out left-to-right top-to-bottom, with
timestamps t-3, t-2, t-1, t labels. Used as a visual in the article /
МД-chapter alongside the variance-table to make the frame-stacking
mechanism concrete for a defense audience.
"""
from __future__ import annotations

import argparse
from pathlib import Path

import cv2
import matplotlib

matplotlib.use("Agg")

import matplotlib.pyplot as plt
import numpy as np


def extract_consecutive_frames(
    video_path: Path, start_frame: int, k: int = 4, step: int = 1,
) -> list[np.ndarray]:
    """Read k frames spaced by `step` video frames, starting at start_frame.
    Returns list of RGB ndarrays. step=1 means consecutive; step=4 means
    every 4th frame (useful when the source video has small per-frame motion
    and you want a wider time window for visualisation)."""
    cap = cv2.VideoCapture(str(video_path))
    if not cap.isOpened():
        raise FileNotFoundError(f"Cannot open video: {video_path}")
    frames: list[np.ndarray] = []
    for i in range(k):
        cap.set(cv2.CAP_PROP_POS_FRAMES, start_frame + i * step)
        ok, bgr = cap.read()
        if not ok or bgr is None:
            break
        frames.append(cv2.cvtColor(bgr, cv2.COLOR_BGR2RGB))
    cap.release()
    return frames


def render(
    video_path: Path, output_path: Path, start_frame: int = 90, k: int = 4, step: int = 1,
) -> None:
    frames = extract_consecutive_frames(video_path, start_frame, k, step)
    if len(frames) < k:
        raise RuntimeError(f"got only {len(frames)} frames, need {k}")

    fig, axes = plt.subplots(2, 2, figsize=(8, 8))
    axes = axes.flatten()
    for i, (ax, frame) in enumerate(zip(axes, frames)):
        ax.imshow(frame)
        # Convention: t is the last (most recent) frame; older frames are
        # to its left in the stack.
        lag = k - 1 - i
        if lag == 0:
            label = "t (current)"
        else:
            label = f"t − {lag}"
        ax.set_title(label, fontsize=14, fontweight="bold")
        ax.set_xticks([])
        ax.set_yticks([])
        for s in ax.spines.values():
            s.set_visible(False)

    fig.suptitle(
        f"Temporal frame stack, k = {k}: what the policy sees as one observation",
        fontsize=15,
    )
    fig.text(
        0.5, 0.02,
        f"Source: {video_path.name}, frames [{start_frame}, {start_frame + k - 1}]",
        ha="center", fontsize=9, color="gray",
    )
    fig.tight_layout(rect=[0, 0.04, 1, 0.96])
    output_path.parent.mkdir(parents=True, exist_ok=True)
    fig.savefig(output_path, dpi=160, bbox_inches="tight")
    plt.close(fig)
    print(f"Wrote {output_path}")


def main():
    p = argparse.ArgumentParser(description=__doc__)
    p.add_argument("--video", type=Path, required=True)
    p.add_argument("--output", type=Path, required=True)
    p.add_argument("--start-frame", type=int, default=90,
                   help="Frame index of the EARLIEST frame in the stack")
    p.add_argument("--k", type=int, default=4)
    p.add_argument("--step", type=int, default=1,
                   help="Spacing between stack frames (video-frame units). step=4 "
                        "samples every 4th video frame, useful when consecutive "
                        "frames have too little motion to be visible.")
    args = p.parse_args()
    render(args.video, args.output, args.start_frame, args.k, args.step)


if __name__ == "__main__":
    main()
