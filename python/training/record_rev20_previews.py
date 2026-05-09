"""rev20: record N preview episodes through randomized cardboard corridor.

Drives the rev16 policy (the only one that knows how to navigate) through the
DR-enabled scene with different seeds — each seed produces a different visual
sample. Encodes each as separate mp4 (336x336, 7fps, nearest upscale from
84x84) for human inspection.

Run on Win after the new Unity build is up on port 8000.
"""
from __future__ import annotations

import argparse
import subprocess
import sys
from pathlib import Path

import numpy as np
import onnxruntime as ort
from PIL import Image

REPO = Path(__file__).resolve().parents[2]
sys.path.insert(0, str(REPO / "python"))

from training.ab_corridor_vision_env import ABCorridorVisionEnv
from training.discrete_action_wrapper import DiscreteActionWrapper


def record_one(
    seed: int,
    base_url: str,
    scenario_path: Path,
    onnx_path: Path,
    out_mp4: Path,
    work_dir: Path,
    max_steps: int = 300,
    img_size: int = 84,
) -> dict:
    work_dir.mkdir(parents=True, exist_ok=True)
    for f in work_dir.glob("*.png"):
        f.unlink()

    sess = ort.InferenceSession(str(onnx_path), providers=["CPUExecutionProvider"])
    input_names = [i.name for i in sess.get_inputs()]

    base = ABCorridorVisionEnv(
        base_url=base_url,
        scenario_path=str(scenario_path),
        max_steps=max_steps,
        oob_margin_m=0.10,
        time_scale=1.0,
        img_size=img_size,
    )
    env = DiscreteActionWrapper(base)

    obs, info = env.reset(seed=seed)
    print(f"  seed={seed}: reset done")

    step = 0
    done = False
    term_reason = "unknown"
    while not done and step < max_steps:
        img = obs["image"].astype(np.float32)[np.newaxis, ...]
        ult = obs["ultrasonic"].astype(np.float32)[np.newaxis, ...]
        logits = sess.run(None, {input_names[0]: img, input_names[1]: ult})[0][0]
        action = int(np.argmax(logits))

        Image.fromarray(obs["image"]).save(work_dir / f"frame_{step:04d}.png")

        obs, reward, term, trunc, info = env.step(action)
        step += 1
        done = term or trunc
        if done:
            term_reason = info.get("termination_reason", "truncated")

    print(f"  seed={seed}: ended after {step} steps, reason={term_reason}")

    out_mp4.parent.mkdir(parents=True, exist_ok=True)
    try:
        import imageio_ffmpeg
        ffmpeg_bin = imageio_ffmpeg.get_ffmpeg_exe()
    except Exception:
        ffmpeg_bin = "ffmpeg"
    cmd = [
        ffmpeg_bin, "-y", "-loglevel", "warning",
        "-framerate", "7",
        "-i", str(work_dir / "frame_%04d.png"),
        "-c:v", "libx264", "-pix_fmt", "yuv420p",
        "-vf", "scale=336:336:flags=neighbor",
        str(out_mp4),
    ]
    result = subprocess.run(cmd, capture_output=True, text=True)
    if result.returncode != 0:
        print(f"  ffmpeg FAILED: {result.stderr}")
        return {"seed": seed, "ok": False, "steps": step, "reason": term_reason}

    sz_kb = out_mp4.stat().st_size / 1024
    print(f"  saved {out_mp4.name} ({sz_kb:.1f} KB)")
    return {"seed": seed, "ok": True, "steps": step, "reason": term_reason, "kb": sz_kb}


def main() -> int:
    p = argparse.ArgumentParser()
    p.add_argument("--base-url", default="http://127.0.0.1:8000")
    p.add_argument("--scenario", default=str(REPO / "configs/scenarios/cardboard-corridor-v1.yaml"))
    p.add_argument("--onnx",
                   default=str(REPO / "python/training/artifacts/cardboard-corridor-ppo-v9-rev16/1.0.0/cardboard-corridor-ppo-v9-rev16.onnx"))
    p.add_argument("--out-dir", default=str(REPO / "rev20_preview_videos"))
    p.add_argument("--seeds", type=int, nargs="+",
                   default=[101, 202, 303, 404, 505, 606, 707, 808])
    p.add_argument("--work-dir", default=r"C:\Users\<user>\rev20_preview_frames")
    args = p.parse_args()

    out_dir = Path(args.out_dir)
    out_dir.mkdir(parents=True, exist_ok=True)
    work_dir = Path(args.work_dir)

    print(f"Recording {len(args.seeds)} preview episodes to {out_dir}")
    print(f"  ONNX: {args.onnx}")
    print(f"  base_url: {args.base_url}")
    print()

    summary = []
    for seed in args.seeds:
        out_mp4 = out_dir / f"rev20_preview_{seed}.mp4"
        info = record_one(
            seed=seed,
            base_url=args.base_url,
            scenario_path=Path(args.scenario),
            onnx_path=Path(args.onnx),
            out_mp4=out_mp4,
            work_dir=work_dir,
        )
        summary.append(info)

    print()
    print("=== Summary ===")
    for s in summary:
        print(f"  seed={s['seed']:>3}  ok={s['ok']}  steps={s['steps']:>3}  "
              f"reason={s.get('reason', '?'):<16}")

    return 0


if __name__ == "__main__":
    raise SystemExit(main())
