#!/usr/bin/env python3
"""Train v9 CNN-PPO with sim-to-real fixes from Run 1/2 lessons.

Stack of wrappers around ABCorridorVisionEnv:
1. DiscreteActionWrapper — Discrete(5) action space matching ks0223 commands
2. DelayedActionWrapper — 1-step latency to match real 7Hz inference loop
3. AntiSpinRewardWrapper — penalty for repeating same action 5+ times
4. ImageAugObservationWrapper — color/blur/jpeg augmentation for visual robustness

Output: PPO with MultiInputPolicy + Categorical(5) action distribution.
ONNX export produces (image, ultrasonic) → action_logits (1, 5).
Backend Phase 7.1 will detect this output dim and argmax it directly.

Usage:
    ./rusim server up --count 3
    caffeinate python python/training/train_cardboard_corridor_v9.py \\
        --total-timesteps 200000 --num-envs 3 \\
        --output-dir python/training/artifacts
"""

from __future__ import annotations

import argparse
import importlib.util
import json
import sys
import time
from pathlib import Path

import numpy as np
import torch

ROOT = Path(__file__).resolve().parents[2]
PYTHON_ROOT = ROOT / "python"
if str(PYTHON_ROOT) not in sys.path:
    sys.path.insert(0, str(PYTHON_ROOT))

from stable_baselines3 import PPO
from stable_baselines3.common.callbacks import (
    BaseCallback,
    CheckpointCallback,
    EvalCallback,
)
from stable_baselines3.common.monitor import Monitor
from stable_baselines3.common.vec_env import (
    DummyVecEnv,
    SubprocVecEnv,
    VecFrameStack,
    VecMonitor,
    VecNormalize,
)

from training.ab_corridor_vision_env import ABCorridorVisionEnv
from training.anti_spin_reward import AntiSpinRewardWrapper
from training.discrete_action_wrapper import ACTION_NAMES, DiscreteActionWrapper
from training.image_aug_wrapper import ImageAugObservationWrapper
from training.latency_wrapper import DelayedActionWrapper
from training.multi_agent_vision_env import MultiAgentVisionVecEnv
from training.model_artifacts import (
    build_compatibility,
    build_model_metadata,
    default_onnx_file_name,
    default_sb3_stem,
    resolve_artifact_dir,
    write_json,
)

DEFAULT_LOG_DIR = ROOT / "python/training/logs"
DEFAULT_SCENARIO = ROOT / "configs/scenarios/cardboard-corridor-v1.yaml"
DEFAULT_MODEL_NAME = "cardboard-corridor-ppo-v9"
DEFAULT_MODEL_VERSION = "1.0.0"


def parse_args() -> argparse.Namespace:
    p = argparse.ArgumentParser(description="Train v9 with sim-to-real wrappers")
    p.add_argument("--base-url", default="http://127.0.0.1:8000")
    p.add_argument("--total-timesteps", type=int, default=200_000)
    p.add_argument("--max-ep-steps", type=int, default=400)
    p.add_argument("--time-scale", type=float, default=3.0)
    p.add_argument("--scenario", default=str(DEFAULT_SCENARIO))
    p.add_argument("--output-dir", default="")
    p.add_argument("--log-dir", default=str(DEFAULT_LOG_DIR))
    p.add_argument("--model-name", default=DEFAULT_MODEL_NAME)
    p.add_argument("--model-version", default=DEFAULT_MODEL_VERSION)
    p.add_argument("--checkpoint-freq", type=int, default=10000)
    p.add_argument("--learning-rate", type=float, default=3e-4)
    # Plan 2 (rev38): n_steps 256->512 (4096 transitions/update for
    # multi-agent x 8) + n_epochs 4->10 (standard SB3 PPO defaults for
    # vision RL). Old defaults gave undersized batches per update.
    p.add_argument("--n-steps", type=int, default=512)
    p.add_argument("--batch-size", type=int, default=64)
    p.add_argument("--n-epochs", type=int, default=10)
    p.add_argument("--gamma", type=float, default=0.99)
    p.add_argument("--clip-range", type=float, default=0.2)
    # rev29 fix: default raised 0.02 -> 0.1 to match rev10-rev18 (working baselines).
    # rev24/26/27 silently inherited 0.02 (5x weaker entropy) on heavy-DR scene
    # and collapsed into degenerate basins. Always pass --ent-coef explicitly
    # for transfer runs; this default protects against future drift.
    p.add_argument("--ent-coef", type=float, default=0.1)
    # Plan 2 (rev38): linear schedule support. When 'linear', ent_coef
    # interpolates from --ent-coef (start, e.g. 0.1) to --ent-coef-end
    # (end, e.g. 0.01) over training. High exploration early avoids early
    # commitment to wrong basin; low exploitation late sharpens policy.
    p.add_argument("--ent-coef-schedule", choices=("constant", "linear"), default="constant",
                   help="Schedule for entropy bonus coefficient")
    p.add_argument("--ent-coef-end", type=float, default=0.01,
                   help="End value for linear schedule (default 0.01)")
    # rev30 (master-plan B6): catastrophic-update guard for transfer. PPO will
    # early-stop the policy update when approx_kl exceeds this. Pre-rev30 was
    # disabled (None); 0.02 is the standard anti-collapse value from the lit.
    p.add_argument("--target-kl", type=float, default=0.02)
    p.add_argument("--seed", type=int, default=42)
    p.add_argument("--img-size", type=int, default=84)
    p.add_argument("--num-envs", type=int, default=1)
    p.add_argument("--device", default="auto")
    p.add_argument("--latency-steps", type=int, default=1,
                   help="Action latency in env ticks (real loop ~140ms = ~1 tick at fast train speed)")
    # Plan 4 (rev39): randomize action latency between [latency-steps, latency-max]
    # if latency-max > latency-steps. Simulates real-robot timing jitter.
    p.add_argument("--latency-max", type=int, default=-1,
                   help="Max latency for randomization (-1 disables, equals --latency-steps)")
    # Plan 4 (rev39): spawn pose jitter passed to Unity via trackParams.
    # 0.0 disables. Recommended 0.10m / 30deg for maze (narrow cells).
    p.add_argument("--spawn-jitter-m", type=float, default=0.0,
                   help="±N metres XY spawn jitter (Unity-side via trackParams)")
    p.add_argument("--spawn-yaw-jitter-deg", type=float, default=0.0,
                   help="±N degrees spawn yaw jitter (Unity-side via trackParams)")
    p.add_argument("--disable-aug", action="store_true",
                   help="Disable image augmentation (for ablation / debugging)")
    p.add_argument("--disable-anti-spin", action="store_true")
    p.add_argument("--disable-latency", action="store_true")
    p.add_argument("--disable-discrete", action="store_true",
                   help="Keep continuous (throttle, steer) action space (v6-style). "
                        "Implies disable-anti-spin and disable-latency.")
    p.add_argument("--resume", default="",
                   help="Path to SB3 checkpoint .zip to resume from (transfer learning)")
    p.add_argument("--maze-randomize", action="store_true",
                   help="Randomize maze params each episode (requires track.cardboard_maze.v1)")
    p.add_argument("--maze-regen-every", type=int, default=1)
    p.add_argument("--curriculum", action="store_true",
                   help="Enable staged maze curriculum. Implies --maze-randomize.")
    # Plan 1 (rev37): override track from CLI for multi-agent maze training.
    # Default keeps cardboard_corridor for backward compat with rev10..rev36.
    p.add_argument("--track-id", default="track.cardboard_corridor.v1",
                   help="Unity track ID (track.cardboard_corridor.v1 | track.cardboard_maze.v1)")
    # Plan 2 (rev38): frame stacking for motion-aware features. k=4 stacks
    # 4 consecutive frames channel-wise (84x84x12). Helps policy distinguish
    # 'standing near wall' from 'approaching wall' — directly addresses the
    # multi-modal reward landscape problem (rev30-rev36 collapse to DirStop).
    # Default 1 = no stacking (back-compat with rev16-rev37 SB3 weights).
    p.add_argument("--frame-stack", type=int, default=1,
                   help="Number of frames to stack channel-wise (k=4 recommended)")
    # Plan 2 (rev38): VecNormalize for rewards. Keeps running mean/std of
    # returns; divides each reward to keep PPO value-function targets at
    # consistent scale across episodes. Helps when reward components vary
    # widely (lateral_penalty -23 vs goal_bonus +100).
    p.add_argument("--normalize-rewards", action="store_true",
                   help="Wrap train_env in VecNormalize for return normalization")
    p.add_argument("--strong-aug", action="store_true",
                   help="Aggressive image augmentations (rev13: enabled — wider brightness/contrast/blur/noise)")
    p.add_argument("--real-cam-postprocess", action="store_true",
                   help="rev18: dim+desaturate+JPEG-recompress 84x84 obs to mimic real USB camera characteristics")
    p.add_argument("--multi-agent", action="store_true",
                   help="rev18: 1 Unity process x N agents (avoids SubprocVecEnv pipe crashes on Win)")
    p.add_argument("--meta-multi-agent", type=int, default=1,
                   help="rev20: N Unity processes x agents_per_unity threaded parallelism. "
                        "When N>=2, --num-envs becomes agents_per_unity and total agents = N*num_envs. "
                        "Ports are base_port..base_port+N-1.")
    p.add_argument("--lateral-penalty-mult", type=float, default=1.0,
                   help="Multiplier for lateral wall-proximity penalty (rev13: 5.0)")
    # Plan 4 (rev39): default 0 keeps back-compat with rev37; recommended values
    # for rev39+ are 0.02/0.02 (master-plan: 0.05/0.05 from Sprint B was too
    # aggressive, broke training in rev30+).
    p.add_argument("--ultrasonic-noise-sigma", type=float, default=0.0,
                   help="Gaussian noise stddev (meters) added to front ultrasonic (rev39 rec: 0.02)")
    p.add_argument("--ultrasonic-dropout-prob", type=float, default=0.0,
                   help="Per-step probability ultrasonic returns 0 or 5m (rev39 rec: 0.02)")
    p.add_argument("--aruco-goal", action="store_true",
                   help="Enable ArUco bonus (+20 if detected)")
    p.add_argument("--aruco-distance-m", type=float, default=0.50)
    p.add_argument("--eval-base-url", default="",
                   help="If set, run periodic EvalCallback against a separate Unity instance "
                        "on this URL (clean-DR: aug + anti-spin disabled). Best model saved "
                        "to <output_dir>/best_model/.")
    p.add_argument("--eval-freq", type=int, default=20000,
                   help="EvalCallback frequency in policy steps (only used if --eval-base-url set)")
    p.add_argument("--eval-episodes", type=int, default=5)
    p.add_argument("--action-stats-freq", type=int, default=5000,
                   help="ActionStatsCallback flush frequency in policy steps")
    p.add_argument("--reward-log-freq", type=int, default=1000,
                   help="RewardBreakdownCallback flush frequency in policy steps")
    return p.parse_args()


class ProgressCallback(BaseCallback):
    def __init__(self, total_timesteps: int):
        super().__init__()
        self.total = total_timesteps
        self._last = 0
        self._t0 = time.time()

    def _on_step(self) -> bool:
        if self.num_timesteps - self._last >= 500:
            elapsed = time.time() - self._t0
            pct = self.num_timesteps / self.total * 100
            fps = self.num_timesteps / max(elapsed, 1)
            print(
                f"  [{pct:5.1f}%] steps={self.num_timesteps}/{self.total}  "
                f"elapsed={elapsed:.0f}s  fps={fps:.1f}",
                flush=True,
            )
            self._last = self.num_timesteps
        return True


class EntCoefScheduleCallback(BaseCallback):
    """Plan 2: linear ent_coef schedule for PPO.

    SB3 doesn't natively schedule ent_coef like learning_rate (it's used as a
    constant in the loss). This callback updates `model.ent_coef` at each step
    based on `num_timesteps / total_timesteps`, interpolating from
    `start_value` (typically 0.1) to `end_value` (typically 0.01).
    """

    def __init__(self, start_value: float, end_value: float, total_timesteps: int):
        super().__init__()
        self.start_value = float(start_value)
        self.end_value = float(end_value)
        self.total = max(1, int(total_timesteps))
        self._last_log = 0

    def _on_step(self) -> bool:
        progress_remaining = max(0.0, 1.0 - self.num_timesteps / self.total)
        new_ent = self.end_value + (self.start_value - self.end_value) * progress_remaining
        self.model.ent_coef = new_ent
        if self.num_timesteps - self._last_log >= 5000:
            self.logger.record("hyperparams/ent_coef", float(new_ent))
            self._last_log = self.num_timesteps
        return True


class ActionStatsCallback(BaseCallback):
    """rev29 monitoring: action distribution over rolling window.

    Catches degenerate collapse (DirForward-only / DirRight-only) within the
    first ~50k steps instead of after a 200k+ run finishes. Logs per-action
    fractions to TensorBoard as scalars; with 5 keys they form an implicit
    histogram view in TB.
    """

    def __init__(self, log_freq: int = 5000, n_actions: int = 5,
                 action_names: list[str] | None = None):
        super().__init__()
        self.log_freq = max(1, int(log_freq))
        self.n_actions = n_actions
        self.action_names = action_names or [f"a{i}" for i in range(n_actions)]
        self._buffer: list[int] = []
        self._last_log_step = 0

    def _on_step(self) -> bool:
        actions = self.locals.get("actions")
        if actions is not None:
            try:
                flat = np.asarray(actions).reshape(-1).astype(np.int64, copy=False)
                self._buffer.extend(int(a) for a in flat)
            except (TypeError, ValueError):
                pass

        if self.num_timesteps - self._last_log_step >= self.log_freq and self._buffer:
            arr = np.asarray(self._buffer, dtype=np.int64)
            counts = np.bincount(arr, minlength=self.n_actions)[: self.n_actions]
            total = max(int(counts.sum()), 1)
            fracs = counts.astype(np.float64) / total
            for i, name in enumerate(self.action_names):
                self.logger.record(f"actions/frac_{name}", float(fracs[i]))
            top_idx = int(np.argmax(counts))
            self.logger.record("actions/top_idx", float(top_idx))
            self.logger.record("actions/top_frac", float(fracs[top_idx]))
            self.logger.record("actions/window_samples", float(total))
            self._buffer.clear()
            self._last_log_step = self.num_timesteps
        return True


class RewardBreakdownCallback(BaseCallback):
    """rev29 monitoring: per-component reward means in TB.

    Reads `info["reward_breakdown"]` populated by ABCorridorVisionEnv and
    MultiAgentVisionVecEnv, accumulates running means over `log_freq`
    policy steps, then flushes to TB and clears.
    """

    def __init__(self, log_freq: int = 1000):
        super().__init__()
        self.log_freq = max(1, int(log_freq))
        self._sums: dict[str, float] = {}
        self._counts = 0
        self._last_log_step = 0

    def _on_step(self) -> bool:
        infos = self.locals.get("infos") or []
        for info in infos:
            if not isinstance(info, dict):
                continue
            br = info.get("reward_breakdown")
            if not isinstance(br, dict):
                continue
            for k, v in br.items():
                try:
                    self._sums[k] = self._sums.get(k, 0.0) + float(v)
                except (TypeError, ValueError):
                    continue
            self._counts += 1

        if self.num_timesteps - self._last_log_step >= self.log_freq and self._counts > 0:
            for k, total in self._sums.items():
                self.logger.record(f"reward/{k}_mean", total / self._counts)
            self._sums.clear()
            self._counts = 0
            self._last_log_step = self.num_timesteps
        return True


def _parse_base_port(base_url: str) -> tuple[str, int]:
    from urllib.parse import urlparse
    parsed = urlparse(base_url)
    port = parsed.port or 8000
    return f"{parsed.scheme}://{parsed.hostname}", port


def _wrap_env(
    base_env,
    *,
    enable_aug: bool,
    enable_anti_spin: bool,
    enable_latency: bool,
    latency_steps: int,
    seed: int,
    enable_discrete: bool = True,
    strong_aug: bool = False,
    latency_max: int = -1,
):
    """Apply v9 wrapper stack: Discrete -> Latency -> AntiSpin -> ImageAug."""
    env = base_env
    if enable_discrete:
        env = DiscreteActionWrapper(env)
        if enable_latency and latency_steps > 0:
            # Plan 4 (rev39): if latency_max > latency_steps, randomize per step.
            dmax = latency_max if latency_max > latency_steps else None
            env = DelayedActionWrapper(env, delay_steps=latency_steps, delay_max=dmax)
        if enable_anti_spin:
            env = AntiSpinRewardWrapper(env)
    if enable_aug:
        if strong_aug:
            env = ImageAugObservationWrapper(
                env,
                enable=True,
                seed=seed,
                noise_sigma=0.05,
                brightness_range=0.30,
                contrast_range=0.30,
                hue_shift_range=10.0,
                blur_prob=0.5,
                blur_radius_max=2.0,
                jpeg_recompress_prob=0.7,
                jpeg_quality_min=55,
                jpeg_quality_max=95,
            )
        else:
            env = ImageAugObservationWrapper(env, enable=True, seed=seed)
    return env


def _make_env(
    base_url: str,
    scenario_path: str,
    max_ep_steps: int,
    time_scale: float,
    img_size: int,
    rank: int,
    seed: int,
    enable_aug: bool,
    enable_anti_spin: bool,
    enable_latency: bool,
    latency_steps: int,
    maze_randomize: bool = False,
    maze_regen_every: int = 1,
    aruco_goal: bool = False,
    aruco_distance_m: float = 0.50,
    enable_discrete: bool = True,
    lateral_penalty_mult: float = 1.0,
    ultrasonic_noise_sigma: float = 0.0,
    ultrasonic_dropout_prob: float = 0.0,
    strong_aug: bool = False,
    real_cam_postprocess: bool = False,
):
    def _init():
        base_env = ABCorridorVisionEnv(
            base_url=base_url,
            scenario_path=scenario_path,
            max_steps=max_ep_steps,
            oob_margin_m=0.10,
            time_scale=time_scale,
            img_size=img_size,
            maze_randomize=maze_randomize,
            maze_regen_every=maze_regen_every,
            aruco_goal=aruco_goal,
            aruco_goal_distance_m=aruco_distance_m,
            lateral_penalty_mult=lateral_penalty_mult,
            ultrasonic_noise_sigma=ultrasonic_noise_sigma,
            ultrasonic_dropout_prob=ultrasonic_dropout_prob,
            real_cam_postprocess=real_cam_postprocess,
        )
        wrapped = _wrap_env(
            base_env,
            enable_aug=enable_aug,
            enable_anti_spin=enable_anti_spin,
            enable_latency=enable_latency,
            latency_steps=latency_steps,
            seed=seed + rank,
            enable_discrete=enable_discrete,
            strong_aug=strong_aug,
        )
        wrapped.reset(seed=seed + rank)
        return wrapped
    return _init


def _build_eval_env(args):
    """Build a clean-DR single-env eval env on a separate Unity URL.

    Used by EvalCallback: image augmentation OFF, anti-spin OFF, latency ON
    (so eval matches the latency the deployed policy will face). The Unity
    instance at `args.eval_base_url` should be launched separately by the
    caller (typically a 2nd `rusim server up` on a different port).
    """
    base_env = ABCorridorVisionEnv(
        base_url=args.eval_base_url,
        scenario_path=args.scenario,
        max_steps=args.max_ep_steps,
        oob_margin_m=0.10,
        time_scale=args.time_scale,
        img_size=args.img_size,
        maze_randomize=False,
        maze_regen_every=1,
        aruco_goal=args.aruco_goal,
        aruco_goal_distance_m=args.aruco_distance_m,
        lateral_penalty_mult=args.lateral_penalty_mult,
        ultrasonic_noise_sigma=0.0,
        ultrasonic_dropout_prob=0.0,
        real_cam_postprocess=False,
    )
    wrapped = _wrap_env(
        base_env,
        enable_aug=False,
        enable_anti_spin=False,
        enable_latency=not args.disable_latency,
        latency_steps=args.latency_steps,
        latency_max=args.latency_max,
        seed=args.seed + 9999,
        enable_discrete=not args.disable_discrete,
        strong_aug=False,
    )
    eval_seed = args.seed + 9999
    wrapped.reset(seed=eval_seed)
    monitored = Monitor(wrapped)
    eval_env = DummyVecEnv([lambda: monitored])
    # Plan 2 (rev38): match train-time frame stacking so policy sees the
    # same (84, 84, 3*k) obs in eval. Without this, eval-time obs shape
    # mismatches policy expectations and PPO crashes on first env.step().
    if args.frame_stack > 1:
        eval_env = VecFrameStack(eval_env, n_stack=args.frame_stack, channels_order='last')
    return eval_env


def export_to_onnx_discrete(model: PPO, output_path: Path, img_size: int = 84) -> None:
    """Export discrete-action PPO policy to ONNX with output (1, 5) logits."""
    policy = model.policy
    policy.eval()

    dummy_img = torch.zeros(1, img_size, img_size, 3, dtype=torch.float32)
    dummy_ultra = torch.zeros(1, 1, dtype=torch.float32)

    class DiscretePolicyWrapper(torch.nn.Module):
        def __init__(self, sb3_policy):
            super().__init__()
            self.features_extractor = sb3_policy.features_extractor
            self.mlp_extractor = sb3_policy.mlp_extractor
            self.action_net = sb3_policy.action_net

        def forward(self, image: torch.Tensor, ultrasonic: torch.Tensor):
            image_chw = image.permute(0, 3, 1, 2).contiguous()
            obs = {"image": image_chw, "ultrasonic": ultrasonic}
            features = self.features_extractor(obs)
            latent_pi, _ = self.mlp_extractor(features)
            # Categorical action net outputs raw logits — no tanh
            return self.action_net(latent_pi)

    wrapper = DiscretePolicyWrapper(policy)
    wrapper.eval()

    output_path.parent.mkdir(parents=True, exist_ok=True)
    torch.onnx.export(
        wrapper,
        (dummy_img, dummy_ultra),
        str(output_path),
        input_names=["image", "ultrasonic"],
        output_names=["action_logits"],
        external_data=False,
        dynamic_axes={
            "image": {0: "batch"},
            "ultrasonic": {0: "batch"},
            "action_logits": {0: "batch"},
        },
        opset_version=11,
    )
    print(f"Exported ONNX (discrete): {output_path}")


def main() -> int:
    args = parse_args()
    if args.curriculum:
        args.maze_randomize = True
    output_dir = resolve_artifact_dir(ROOT, args.output_dir, args.model_name, args.model_version)
    log_dir = Path(args.log_dir)
    output_dir.mkdir(parents=True, exist_ok=True)
    log_dir.mkdir(parents=True, exist_ok=True)

    num_envs = max(1, args.num_envs)
    enable_aug = not args.disable_aug
    enable_anti_spin = not args.disable_anti_spin
    enable_latency = not args.disable_latency

    tensorboard_log = None
    if importlib.util.find_spec("tensorboard") is not None:
        tensorboard_log = str(log_dir / "tb_v9")

    print("=" * 60)
    print("  Cardboard Corridor v9 CNN-PPO Training (sim-to-real fixes)")
    print("=" * 60)
    print(f"  base_url:         {args.base_url}")
    print(f"  total_timesteps:  {args.total_timesteps}")
    print(f"  num_envs:         {num_envs}")
    print(f"  scenario:         {args.scenario}")
    print(f"  output_dir:       {output_dir}")
    print(f"  device:           {args.device}")
    print()
    print("  Wrappers stack:")
    print(f"    DiscreteAction:    enabled (Discrete(5) -> ks0223 cmds)")
    print(f"    DelayedAction:     {'enabled' if enable_latency else 'disabled'} (delay={args.latency_steps})")
    print(f"    AntiSpinReward:    {'enabled' if enable_anti_spin else 'disabled'}")
    print(f"    ImageAug:          {'enabled' if enable_aug else 'disabled'}")
    print(f"  Discrete actions: {dict(enumerate(ACTION_NAMES))}")
    print()

    meta_n = max(1, int(args.meta_multi_agent))
    if meta_n >= 2:
        from sim_client.scenario import load_scenario_file
        from training.meta_multi_agent_vec_env import MetaMultiAgentVecEnv
        sc = load_scenario_file(args.scenario)
        route = (sc.get("route") or {})
        params = route.get("params") or {}
        ma_waypoints = [(float(w[0]), float(w[1])) for w in route.get("waypoints", [])]
        ma_corridor_w = float(params.get("corridor.width_m", 0.60))
        ma_goal_r = float(params.get("goal.radius_m", 0.25))
        scheme_host, base_port = _parse_base_port(args.base_url)
        url_template = f"{scheme_host}:{{port}}"
        total_agents = meta_n * num_envs
        print(f"  Meta-multi-agent: {meta_n} Unity x {num_envs} agents = {total_agents} total")
        print(f"  Ports: {base_port}..{base_port + meta_n - 1}")
        train_env = MetaMultiAgentVecEnv(
            n_unity=meta_n,
            agents_per_unity=num_envs,
            base_url_template=url_template,
            scenario_path=args.scenario,
            max_steps=args.max_ep_steps,
            time_scale=args.time_scale,
            img_size=args.img_size,
            corridor_width_m=ma_corridor_w,
            goal_radius_m=ma_goal_r,
            waypoints=ma_waypoints if ma_waypoints else None,
            real_cam_postprocess=args.real_cam_postprocess,
            base_port=base_port,
        )
        probe_track = "track.cardboard_corridor.v1"
        probe_corridor_w = ma_corridor_w
        probe_goal_r = ma_goal_r
    elif args.multi_agent:
        from sim_client.scenario import load_scenario_file
        sc = load_scenario_file(args.scenario)
        route = (sc.get("route") or {})
        params = route.get("params") or {}
        ma_waypoints = [(float(w[0]), float(w[1])) for w in route.get("waypoints", [])]
        ma_corridor_w = float(params.get("corridor.width_m", 0.60))
        ma_goal_r = float(params.get("goal.radius_m", 0.25))
        print(f"  Multi-agent mode: 1 Unity x {num_envs} agents (port {args.base_url})")
        print(f"  Track: {args.track_id}, maze_randomize={args.maze_randomize}, regen_every={args.maze_regen_every}")
        train_env = MultiAgentVisionVecEnv(
            n_agents=num_envs,
            base_url=args.base_url,
            scenario_path=args.scenario,
            max_steps=args.max_ep_steps,
            time_scale=args.time_scale,
            img_size=args.img_size,
            corridor_width_m=ma_corridor_w,
            goal_radius_m=ma_goal_r,
            waypoints=ma_waypoints if ma_waypoints else None,
            track_id=args.track_id,
            real_cam_postprocess=args.real_cam_postprocess,
            maze_randomize=args.maze_randomize,
            maze_regen_every=args.maze_regen_every,
            spawn_jitter_m=args.spawn_jitter_m,
            spawn_yaw_jitter_deg=args.spawn_yaw_jitter_deg,
        )
        probe_track = args.track_id
        probe_corridor_w = ma_corridor_w
        probe_goal_r = ma_goal_r
    elif num_envs == 1:
        base_env = ABCorridorVisionEnv(
            base_url=args.base_url,
            scenario_path=args.scenario,
            max_steps=args.max_ep_steps,
            oob_margin_m=0.10,
            time_scale=args.time_scale,
            img_size=args.img_size,
            maze_randomize=args.maze_randomize,
            maze_regen_every=args.maze_regen_every,
            aruco_goal=args.aruco_goal,
            aruco_goal_distance_m=args.aruco_distance_m,
            lateral_penalty_mult=args.lateral_penalty_mult,
            ultrasonic_noise_sigma=args.ultrasonic_noise_sigma,
            ultrasonic_dropout_prob=args.ultrasonic_dropout_prob,
            real_cam_postprocess=args.real_cam_postprocess,
        )
        wrapped = _wrap_env(
            base_env,
            enable_aug=enable_aug,
            enable_anti_spin=enable_anti_spin,
            enable_latency=enable_latency,
            latency_steps=args.latency_steps,
            latency_max=args.latency_max,
            seed=args.seed,
            enable_discrete=not args.disable_discrete,
            strong_aug=args.strong_aug,
        )
        train_env = Monitor(wrapped, filename=str(log_dir / "train_v9_monitor"))
        probe_track = base_env._reset_config["selectedTrackId"]
        probe_corridor_w = base_env.corridor_width_m
        probe_goal_r = base_env.goal_radius_m
    else:
        scheme_host, base_port = _parse_base_port(args.base_url)
        env_urls = [f"{scheme_host}:{base_port + i}" for i in range(num_envs)]
        print(f"  Vectorized envs ({num_envs}):")
        for i, url in enumerate(env_urls):
            print(f"    env[{i}]: {url}")
        print()
        vec_env = SubprocVecEnv([
            _make_env(
                base_url=env_urls[i],
                scenario_path=args.scenario,
                max_ep_steps=args.max_ep_steps,
                time_scale=args.time_scale,
                img_size=args.img_size,
                rank=i,
                seed=args.seed,
                enable_aug=enable_aug,
                enable_anti_spin=enable_anti_spin,
                enable_latency=enable_latency,
                latency_steps=args.latency_steps,
                maze_randomize=args.maze_randomize,
                maze_regen_every=args.maze_regen_every,
                aruco_goal=args.aruco_goal,
                aruco_distance_m=args.aruco_distance_m,
                enable_discrete=not args.disable_discrete,
                lateral_penalty_mult=args.lateral_penalty_mult,
                ultrasonic_noise_sigma=args.ultrasonic_noise_sigma,
                ultrasonic_dropout_prob=args.ultrasonic_dropout_prob,
                strong_aug=args.strong_aug,
                real_cam_postprocess=args.real_cam_postprocess,
            )
            for i in range(num_envs)
        ])
        train_env = VecMonitor(vec_env, filename=str(log_dir / "train_v9_monitor"))
        # quick probe on env 0 metadata via sync env
        probe_env = ABCorridorVisionEnv(
            base_url=env_urls[0],
            scenario_path=args.scenario,
            max_steps=args.max_ep_steps,
            oob_margin_m=0.10,
            time_scale=args.time_scale,
            img_size=args.img_size,
            maze_randomize=args.maze_randomize,
            maze_regen_every=args.maze_regen_every,
            aruco_goal=args.aruco_goal,
            aruco_goal_distance_m=args.aruco_distance_m,
        )
        probe_track = probe_env._reset_config["selectedTrackId"]
        probe_corridor_w = probe_env.corridor_width_m
        probe_goal_r = probe_env.goal_radius_m
        probe_env.close() if hasattr(probe_env, "close") else None

    print(f"  track:           {probe_track}")
    print(f"  corridor_width:  {probe_corridor_w:.2f}m")
    print(f"  goal_radius:     {probe_goal_r:.2f}m")
    print()

    # Plan 2 (rev38): frame stacking k>1 wraps train_env in VecFrameStack.
    # Image obs goes from (84, 84, 3) -> (84, 84, 3*k) channel-wise stacked.
    # Multi-agent / SubprocVec / DummyVec already are VecEnvs; single-env path
    # produces a Monitor (gym env) — VecFrameStack expects VecEnv so wrap
    # in DummyVecEnv first if needed.
    from stable_baselines3.common.vec_env import VecEnv as _VecEnvType
    if args.frame_stack > 1:
        if not isinstance(train_env, _VecEnvType):
            train_env = DummyVecEnv([lambda: train_env])
        train_env = VecFrameStack(train_env, n_stack=args.frame_stack, channels_order='last')
        print(f"  Frame stacking: k={args.frame_stack} -> image obs (84, 84, {3 * args.frame_stack})")
        print()

    # Plan 2 (rev38): reward normalization. VecNormalize keeps running mean/std
    # of returns and divides each reward by sqrt(var+eps), so PPO sees rewards
    # of comparable magnitude regardless of episode-to-episode swings (e.g.
    # rev37's lateral_penalty_mean -23 vs +100 goal_bonus). Stabilizes value
    # function learning. norm_obs=False since we already pass uint8 images.
    if args.normalize_rewards:
        if not isinstance(train_env, _VecEnvType):
            train_env = DummyVecEnv([lambda: train_env])
        train_env = VecNormalize(train_env, norm_obs=False, norm_reward=True,
                                 clip_reward=10.0, gamma=args.gamma)
        print(f"  VecNormalize: reward running mean/std + clip ±10 + gamma={args.gamma}")
        print()

    # rev30: target_kl=None disables the guard (back-compat); positive value
    # enables PPO early-stop on update when approx_kl exceeds it.
    target_kl = args.target_kl if args.target_kl and args.target_kl > 0 else None

    # Plan 2 (rev38): ent_coef constant or linear schedule. SB3 PPO uses
    # ent_coef as a constant in loss; for "linear" we install
    # EntCoefScheduleCallback below to update it per step.
    if args.ent_coef_schedule == "linear":
        # Start with the start value; callback overwrites each step.
        ent_coef_value = float(args.ent_coef)
        print(f"  ent_coef schedule: linear {args.ent_coef} -> {args.ent_coef_end} (via callback)")
    else:
        ent_coef_value = float(args.ent_coef)
        print(f"  ent_coef: constant {args.ent_coef}")

    if args.resume:
        print(f"Resuming PPO from checkpoint: {args.resume}")
        model = PPO.load(args.resume, env=train_env, device=args.device)
        # Refresh hyperparameters that may differ from training run
        from stable_baselines3.common.utils import get_schedule_fn
        model.learning_rate = args.learning_rate
        model.lr_schedule = get_schedule_fn(args.learning_rate)
        model.clip_range = get_schedule_fn(args.clip_range)
        # Plan 2: ent_coef updated each step by EntCoefScheduleCallback if
        # --ent-coef-schedule linear. Here just set start value.
        model.ent_coef = ent_coef_value
        model.target_kl = target_kl
        # PPO.load preserves num_timesteps automatically; total_timesteps relative
        print(f"  Resumed at num_timesteps={model.num_timesteps}, "
              f"will train to reach {args.total_timesteps}, target_kl={target_kl}")
    else:
        print(f"Creating new PPO model with MultiInputPolicy (device={args.device})...")
        model = PPO(
            "MultiInputPolicy",
            train_env,
            learning_rate=args.learning_rate,
            n_steps=args.n_steps,
            batch_size=args.batch_size,
            n_epochs=args.n_epochs,
            gamma=args.gamma,
            clip_range=args.clip_range,
            ent_coef=ent_coef_value,
            target_kl=target_kl,
            verbose=1,
            seed=args.seed,
            device=args.device,
            tensorboard_log=tensorboard_log,
            policy_kwargs=dict(
                net_arch=dict(pi=[128, 64], vf=[128, 64]),
            ),
        )
    print(f"  Action dist: {type(model.policy.action_dist).__name__}")
    assert "Categorical" in type(model.policy.action_dist).__name__, \
        f"Expected Categorical action dist for Discrete action_space, got {type(model.policy.action_dist)}"

    train_total_agents = int(getattr(model.env, "num_envs", 1) or 1)

    callbacks = [
        ProgressCallback(args.total_timesteps),
        CheckpointCallback(
            save_freq=args.checkpoint_freq,
            save_path=str(output_dir / "checkpoints"),
            name_prefix="cardboard_v9_ppo",
        ),
        ActionStatsCallback(
            log_freq=args.action_stats_freq,
            n_actions=len(ACTION_NAMES),
            action_names=ACTION_NAMES,
        ),
        RewardBreakdownCallback(log_freq=args.reward_log_freq),
    ]
    # Plan 2 (rev38): linear ent_coef schedule via callback (SB3 PPO doesn't
    # natively schedule ent_coef like learning_rate).
    if args.ent_coef_schedule == "linear":
        callbacks.append(EntCoefScheduleCallback(
            start_value=args.ent_coef,
            end_value=args.ent_coef_end,
            total_timesteps=args.total_timesteps,
        ))
    if args.curriculum:
        from training.maze_curriculum import MazeCurriculumCallback
        callbacks.append(MazeCurriculumCallback())
        print("  Curriculum: staged maze difficulty enabled (A-easy -> B -> C -> D-full)")

    if args.eval_base_url:
        eval_env = _build_eval_env(args)
        eval_freq_calls = max(1, args.eval_freq // train_total_agents)
        best_model_dir = output_dir / "best_model"
        best_model_dir.mkdir(parents=True, exist_ok=True)
        callbacks.append(EvalCallback(
            eval_env,
            best_model_save_path=str(best_model_dir),
            log_path=str(log_dir / f"eval_{args.model_name}"),
            eval_freq=eval_freq_calls,
            n_eval_episodes=args.eval_episodes,
            deterministic=True,
            render=False,
        ))
        print(f"  EvalCallback: every {args.eval_freq} steps "
              f"(={eval_freq_calls} calls), {args.eval_episodes} eps, "
              f"clean-DR @ {args.eval_base_url}")
        print(f"  best_model_save_path: {best_model_dir}")
    else:
        print("  EvalCallback: DISABLED (no --eval-base-url) — no best-model snapshot will be saved")

    print(f"\nStarting training for {args.total_timesteps} timesteps...")
    t0 = time.time()
    model.learn(total_timesteps=args.total_timesteps, callback=callbacks, progress_bar=False)
    train_time = time.time() - t0
    print(f"\nTraining completed in {train_time:.1f}s")

    # Save SB3
    model_path = output_dir / default_sb3_stem(args.model_name)
    model.save(str(model_path))
    print(f"Saved SB3 model: {model_path}")

    # ONNX export (discrete output)
    onnx_name = default_onnx_file_name(args.model_name)
    onnx_path = output_dir / onnx_name
    exported_onnx = False
    try:
        export_to_onnx_discrete(model, onnx_path, img_size=args.img_size)
        exported_onnx = True
    except Exception as e:
        print(f"ONNX export failed (non-fatal): {e}")

    # Metadata
    compatibility = build_compatibility(
        runtime_modes=["unity-sim", "real-robot"],
        vehicle_ids=["vehicle.prometeo.sport.v1", "vehicle.ks0223.arcade.blue.v1"],
        robot_kinds=["ks0223"],
    )
    metadata = build_model_metadata(
        model_name=args.model_name,
        model_version=args.model_version,
        model_source="python-rl-api",
        policy_format="onnx" if exported_onnx else "sb3",
        artifact_file_name=onnx_name if exported_onnx else f"{model_path.name}.zip",
        compatibility=compatibility,
        observation_schema={
            "image": {"shape": [args.img_size, args.img_size, 3], "dtype": "uint8"},
            "ultrasonic": {"shape": [1], "dtype": "float32"},
        },
        action_schema={
            "size": 5,
            "outputs": ACTION_NAMES,
            "type": "discrete-categorical",
            "mapping": {str(i): name for i, name in enumerate(ACTION_NAMES)},
        },
        extra={
            "scenario": str(Path(args.scenario).resolve()),
            "track": probe_track,
            "algorithm": "PPO",
            "framework": "stable-baselines3",
            "totalTimesteps": args.total_timesteps,
            "trainTimeSeconds": round(train_time, 1),
            "corridorWidthM": probe_corridor_w,
            "goalRadiusM": probe_goal_r,
            "wrappers": {
                "discreteAction": True,
                "delayedAction": enable_latency,
                "antiSpinReward": enable_anti_spin,
                "imageAug": enable_aug,
                "latencySteps": args.latency_steps,
            },
            "hyperparameters": {
                "learningRate": args.learning_rate,
                "nSteps": args.n_steps,
                "batchSize": args.batch_size,
                "nEpochs": args.n_epochs,
                "gamma": args.gamma,
                "clipRange": args.clip_range,
                "entCoef": args.ent_coef,
                "entCoefSchedule": args.ent_coef_schedule,
                "entCoefEnd": args.ent_coef_end if args.ent_coef_schedule == "linear" else None,
                "targetKl": target_kl,
                "frameStack": args.frame_stack,
                "normalizeRewards": bool(args.normalize_rewards),
                "spawnJitterM": args.spawn_jitter_m,
                "spawnYawJitterDeg": args.spawn_yaw_jitter_deg,
                "latencyMax": args.latency_max,
                "seed": args.seed,
                "lateralPenaltyMult": args.lateral_penalty_mult,
                "ultrasonicNoiseSigma": args.ultrasonic_noise_sigma,
                "ultrasonicDropoutProb": args.ultrasonic_dropout_prob,
                "strongAug": bool(args.strong_aug),
                "realCamPostprocess": bool(args.real_cam_postprocess),
                "resumeFrom": args.resume or None,
            },
            "monitoring": {
                "evalBaseUrl": args.eval_base_url or None,
                "evalFreq": args.eval_freq if args.eval_base_url else None,
                "evalEpisodes": args.eval_episodes if args.eval_base_url else None,
                "actionStatsFreq": args.action_stats_freq,
                "rewardLogFreq": args.reward_log_freq,
                "bestModelDir": str(output_dir / "best_model") if args.eval_base_url else None,
            },
        },
    )
    write_json(output_dir / "metadata.json", metadata)
    print(f"Saved metadata: {output_dir / 'metadata.json'}")

    return 0


if __name__ == "__main__":
    raise SystemExit(main())
