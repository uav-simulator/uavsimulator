"""Supervised trainer for behavior cloning on (frame, ultrasonic) -> action_logits.

Architecture mirrors SB3's NatureCNN + CombinedExtractor + ActorCriticPolicy so the
trained weights can be lifted verbatim into a `stable_baselines3.PPO` checkpoint:

    image (3, 84, 84) uint8  ─┐
                              ├─► CombinedExtractor ─► MlpExtractor(pi) ─► action_net
    ultrasonic (1,)          ─┘     │  (NatureCNN+Flatten)       │  (Tanh)
                                    │                            │
                                    └── 512 + 1 = 513 features ──┘

Image branch: Conv2d(3,32,8,4) → Conv2d(32,64,4,2) → Conv2d(64,64,3,1) → Flatten → Linear(64*7*7, 512) → ReLU.
Ultrasonic:   nn.Flatten() (identity for shape-(1,) input).
Policy MLP:   Linear(513, 64) → Tanh → Linear(64, 64) → Tanh.
Action head:  Linear(64, 5).

`export_sb3()` builds a stub PPO over a dummy env that matches the observation /
action spaces, copies our trained state dicts into the policy submodules, and
calls `model.save(...)` to produce a standard SB3 archive (`policy.pth` inside).
"""
from __future__ import annotations

import random
from collections import Counter
from dataclasses import dataclass
from pathlib import Path

import numpy as np
import torch
import torch.nn as nn
from torch.utils.data import DataLoader, Dataset, WeightedRandomSampler

from .dataset import BcSample

N_ACTIONS = 5
FRAME_SIZE = 84
CNN_OUTPUT_DIM = 512
MLP_HIDDEN = 64


@dataclass
class BcConfig:
    epochs: int = 30
    batch_size: int = 64
    lr: float = 3e-4
    device: str = "cpu"
    seed: int = 42
    log_every: int = 1
    # When True: draw each batch with WeightedRandomSampler so every action class
    # is sampled with equal probability per batch. Counteracts the mode-collapse
    # failure mode where the model just learns the action prior (e.g. always
    # predicting DirForward when ~38% of demo labels are forward) instead of
    # learning the visual cue → turn mapping. Cost: minority-class samples
    # (DirStop ~2% of the corpus) get repeated ~15× per epoch, so add a small
    # amount of overfitting risk; use modest epochs.
    class_balanced: bool = False
    # When True: train a multi-modal policy that consumes the ego-centric
    # occupancy map in addition to (image, ultrasonic). Requires every sample
    # in the corpus to carry an `occupancy` tensor (see training.bc.occupancy
    # for the offline reconstructor that adds these to existing demos).
    use_occupancy: bool = False


class _BcTorchDataset(Dataset):
    def __init__(self, samples: list[BcSample], use_occupancy: bool = False) -> None:
        self._samples = samples
        self._use_occupancy = use_occupancy
        if use_occupancy:
            missing = sum(1 for s in samples if s.occupancy is None)
            if missing:
                raise ValueError(
                    f"use_occupancy=True but {missing}/{len(samples)} samples have no "
                    f"occupancy tensor. Run training.bc.occupancy.reconstruct_for_demo "
                    f"to build occupancy_<tag>.npy next to each MP4 first."
                )

    def __len__(self) -> int:
        return len(self._samples)

    def __getitem__(self, idx: int):
        s = self._samples[idx]
        frame = torch.from_numpy(np.ascontiguousarray(s.frame.transpose(2, 0, 1))).float() / 255.0
        ultra = torch.tensor([s.ultrasonic], dtype=torch.float32)
        action = torch.tensor(s.action_idx, dtype=torch.long)
        if self._use_occupancy:
            occ = torch.from_numpy(np.ascontiguousarray(s.occupancy)).float()
            return frame, ultra, occ, action
        return frame, ultra, action


class _NatureCnnHead(nn.Module):
    """Mirrors SB3's NatureCNN + CombinedExtractor + Tanh MLP head."""

    def __init__(self) -> None:
        super().__init__()
        self.cnn = nn.Sequential(
            nn.Conv2d(3, 32, kernel_size=8, stride=4, padding=0),
            nn.ReLU(),
            nn.Conv2d(32, 64, kernel_size=4, stride=2, padding=0),
            nn.ReLU(),
            nn.Conv2d(64, 64, kernel_size=3, stride=1, padding=0),
            nn.ReLU(),
            nn.Flatten(),
        )
        with torch.no_grad():
            n_flatten = self.cnn(torch.zeros(1, 3, FRAME_SIZE, FRAME_SIZE)).shape[1]
        self.linear = nn.Sequential(nn.Linear(n_flatten, CNN_OUTPUT_DIM), nn.ReLU())
        # +1 for the concatenated ultrasonic scalar.
        self.policy_net = nn.Sequential(
            nn.Linear(CNN_OUTPUT_DIM + 1, MLP_HIDDEN),
            nn.Tanh(),
            nn.Linear(MLP_HIDDEN, MLP_HIDDEN),
            nn.Tanh(),
        )
        self.action_net = nn.Linear(MLP_HIDDEN, N_ACTIONS)

    def forward(self, frame: torch.Tensor, ultra: torch.Tensor) -> torch.Tensor:
        img_features = self.linear(self.cnn(frame))
        combined = torch.cat([img_features, ultra], dim=1)
        latent = self.policy_net(combined)
        return self.action_net(latent)


# Occupancy-aware multi-modal model: same NatureCNN over the image branch
# (so the BC checkpoint is structurally compatible with the existing PPO
# starting point), plus a small CNN branch over the 21×21×3 ego-centric
# map, plus the ultrasonic scalar. Features concatenate and feed the same
# Tanh-MLP head as _NatureCnnHead. Output dim and final action_net are
# identical so the SB3 lift in export_sb3 can reuse the same weights for
# the image/policy/action submodules.
MAP_CNN_OUTPUT_DIM = 128


class _MapCnn(nn.Module):
    """Compact CNN over a (3, 21, 21) ego-centric occupancy window."""

    def __init__(self) -> None:
        super().__init__()
        self.cnn = nn.Sequential(
            nn.Conv2d(3, 32, kernel_size=3, stride=1, padding=1),
            nn.ReLU(),
            nn.Conv2d(32, 64, kernel_size=3, stride=2, padding=1),
            nn.ReLU(),
            nn.Conv2d(64, 64, kernel_size=3, stride=2, padding=1),
            nn.ReLU(),
            nn.Flatten(),
        )
        with torch.no_grad():
            n_flatten = self.cnn(torch.zeros(1, 3, 21, 21)).shape[1]
        self.linear = nn.Sequential(nn.Linear(n_flatten, MAP_CNN_OUTPUT_DIM), nn.ReLU())

    def forward(self, occ: torch.Tensor) -> torch.Tensor:
        return self.linear(self.cnn(occ))


class _MultiModalHead(nn.Module):
    """NatureCNN(image) + ultrasonic + MapCNN(occupancy) → MLP → action logits."""

    def __init__(self) -> None:
        super().__init__()
        # Reuse NatureCNN architecture verbatim for the image branch.
        self.cnn = nn.Sequential(
            nn.Conv2d(3, 32, kernel_size=8, stride=4, padding=0),
            nn.ReLU(),
            nn.Conv2d(32, 64, kernel_size=4, stride=2, padding=0),
            nn.ReLU(),
            nn.Conv2d(64, 64, kernel_size=3, stride=1, padding=0),
            nn.ReLU(),
            nn.Flatten(),
        )
        with torch.no_grad():
            n_flatten = self.cnn(torch.zeros(1, 3, FRAME_SIZE, FRAME_SIZE)).shape[1]
        self.linear = nn.Sequential(nn.Linear(n_flatten, CNN_OUTPUT_DIM), nn.ReLU())
        self.map_cnn = _MapCnn()
        combined_dim = CNN_OUTPUT_DIM + 1 + MAP_CNN_OUTPUT_DIM
        self.policy_net = nn.Sequential(
            nn.Linear(combined_dim, MLP_HIDDEN),
            nn.Tanh(),
            nn.Linear(MLP_HIDDEN, MLP_HIDDEN),
            nn.Tanh(),
        )
        self.action_net = nn.Linear(MLP_HIDDEN, N_ACTIONS)

    def forward(self, frame: torch.Tensor, ultra: torch.Tensor, occ: torch.Tensor) -> torch.Tensor:
        img_features = self.linear(self.cnn(frame))
        map_features = self.map_cnn(occ)
        combined = torch.cat([img_features, ultra, map_features], dim=1)
        latent = self.policy_net(combined)
        return self.action_net(latent)


def _seed_everything(seed: int) -> None:
    random.seed(seed)
    np.random.seed(seed)
    torch.manual_seed(seed)


class BcTrainer:
    def __init__(self, cfg: BcConfig) -> None:
        self.cfg = cfg
        _seed_everything(cfg.seed)
        self.device = torch.device(cfg.device)
        if cfg.use_occupancy:
            self.model = _MultiModalHead().to(self.device)
        else:
            self.model = _NatureCnnHead().to(self.device)

    def fit(self, samples: list[BcSample]) -> dict[str, list[float]]:
        dataset = _BcTorchDataset(samples, use_occupancy=self.cfg.use_occupancy)
        if self.cfg.class_balanced:
            # WeightedRandomSampler with per-sample weight = 1/freq(class) makes
            # batches class-uniform in expectation. Action classes never observed
            # in the corpus (typically DirBack on KS0223) get sampler weight 0
            # — they remain unsampleable, which matches the runtime constraint
            # that the robot cannot execute them.
            class_counts = Counter(s.action_idx for s in samples)
            sample_weights = [
                1.0 / class_counts[s.action_idx] if class_counts[s.action_idx] > 0 else 0.0
                for s in samples
            ]
            gen = torch.Generator().manual_seed(self.cfg.seed)
            sampler = WeightedRandomSampler(
                sample_weights, num_samples=len(samples), replacement=True, generator=gen,
            )
            loader = DataLoader(
                dataset, batch_size=self.cfg.batch_size, sampler=sampler, drop_last=False,
            )
            print(
                f"[bc] class-balanced sampling enabled. Per-epoch class weights "
                f"(inverse freq): { {k: round(1.0 / v, 4) for k, v in class_counts.items()} }",
                flush=True,
            )
        else:
            loader = DataLoader(
                dataset,
                batch_size=self.cfg.batch_size,
                shuffle=True,
                drop_last=False,
                generator=torch.Generator().manual_seed(self.cfg.seed),
            )
        optimizer = torch.optim.Adam(self.model.parameters(), lr=self.cfg.lr)
        if self.cfg.class_balanced:
            # In addition to the WeightedRandomSampler that balances *batch
            # composition*, also class-weight the loss itself: each sample's
            # gradient gets scaled by sqrt(median_freq / freq(class)). Standard
            # `median-frequency balancing` (Eigen & Fergus 2015) — less
            # aggressive than full inverse-frequency, which over-shoots
            # toward minority classes (e.g. 47× weight on DirStop when it's
            # 5 % of the corpus, pushing the model to over-predict Stop).
            # Square root tempers the imbalance so the rare classes still
            # get up-weighted but no class dominates by more than ~3×.
            import math as _math
            class_counts = Counter(s.action_idx for s in samples)
            present_counts = [v for v in class_counts.values() if v > 0]
            median_freq = sorted(present_counts)[len(present_counts) // 2]
            weights = torch.zeros(N_ACTIONS, device=self.device)
            for k, v in class_counts.items():
                if v > 0:
                    weights[k] = _math.sqrt(median_freq / v)
            loss_fn = nn.CrossEntropyLoss(weight=weights)
            print(f"[bc] sqrt median-freq CE weights: {weights.tolist()}", flush=True)
        else:
            loss_fn = nn.CrossEntropyLoss()

        history: dict[str, list[float]] = {"train_loss": [], "train_accuracy": []}
        for epoch in range(self.cfg.epochs):
            self.model.train()
            running_loss = 0.0
            correct = 0
            total = 0
            for batch in loader:
                if self.cfg.use_occupancy:
                    frame, ultra, occ, action = batch
                    frame = frame.to(self.device)
                    ultra = ultra.to(self.device)
                    occ = occ.to(self.device)
                    action = action.to(self.device)
                    logits = self.model(frame, ultra, occ)
                else:
                    frame, ultra, action = batch
                    frame = frame.to(self.device)
                    ultra = ultra.to(self.device)
                    action = action.to(self.device)
                    logits = self.model(frame, ultra)
                loss = loss_fn(logits, action)
                optimizer.zero_grad()
                loss.backward()
                optimizer.step()
                running_loss += loss.item() * action.size(0)
                correct += (logits.argmax(dim=1) == action).sum().item()
                total += action.size(0)
            avg_loss = running_loss / max(total, 1)
            accuracy = correct / max(total, 1)
            history["train_loss"].append(avg_loss)
            history["train_accuracy"].append(accuracy)
            if self.cfg.log_every and (epoch % self.cfg.log_every == 0 or epoch == self.cfg.epochs - 1):
                print(f"[bc] epoch {epoch + 1}/{self.cfg.epochs}  loss={avg_loss:.4f}  acc={accuracy:.3f}")
        return history

    def export_sb3(self, output_zip: Path) -> None:
        """Lift trained weights into a real SB3 PPO and save the standard archive.

        Bakes the canonical BC->PPO hyperparameters (from BcToPpoConfig defaults)
        into the saved archive so that PPO.load() allocates a correctly sized
        rollout_buffer. Without this, SB3 defaults (n_steps=2048) get persisted
        and a later n_steps=256 override would NOT reallocate the buffer,
        causing an AssertionError on the first train() call.
        """
        if self.cfg.use_occupancy:
            # Multi-modal export: build an SB3 PPO with MultiModalOccupancyExtractor,
            # copy weights from our _MultiModalHead, save the SB3 archive. Also dump
            # the raw torch state dict alongside so non-SB3 consumers can load too.
            self._export_sb3_multimodal(output_zip)
            return
        import gymnasium as gym
        from gymnasium import spaces
        from stable_baselines3 import PPO
        from stable_baselines3.common.vec_env import DummyVecEnv

        from .bc_to_ppo import BcToPpoConfig

        class _StubEnv(gym.Env):
            metadata = {"render_modes": []}

            def __init__(self) -> None:
                super().__init__()
                self.observation_space = spaces.Dict(
                    {
                        "image": spaces.Box(low=0, high=255, shape=(3, FRAME_SIZE, FRAME_SIZE), dtype=np.uint8),
                        "ultrasonic": spaces.Box(low=0.0, high=1.0, shape=(1,), dtype=np.float32),
                    }
                )
                self.action_space = spaces.Discrete(N_ACTIONS)

            def _zero_obs(self):
                return {
                    "image": np.zeros((3, FRAME_SIZE, FRAME_SIZE), dtype=np.uint8),
                    "ultrasonic": np.zeros((1,), dtype=np.float32),
                }

            def reset(self, *, seed=None, options=None):
                super().reset(seed=seed)
                return self._zero_obs(), {}

            def step(self, action):
                return self._zero_obs(), 0.0, True, False, {}

        venv = DummyVecEnv([lambda: _StubEnv()])
        # `cnn_output_dim=CNN_OUTPUT_DIM` overrides SB3's default of 256 so the
        # NatureCNN linear layer shape matches our trained head (1×3136 → 512).
        ppo_cfg = BcToPpoConfig()
        model = PPO(
            "MultiInputPolicy",
            venv,
            learning_rate=ppo_cfg.learning_rate,
            n_steps=ppo_cfg.n_steps,
            batch_size=ppo_cfg.batch_size,
            n_epochs=ppo_cfg.n_epochs,
            gamma=ppo_cfg.gamma,
            clip_range=ppo_cfg.clip_range,
            ent_coef=ppo_cfg.ent_coef,
            target_kl=ppo_cfg.target_kl,
            device=self.cfg.device,
            seed=self.cfg.seed,
            policy_kwargs={"features_extractor_kwargs": {"cnn_output_dim": CNN_OUTPUT_DIM}},
        )

        # Copy our trained submodules into the SB3 policy. Names mirror SB3's
        # MultiInputActorCriticPolicy graph (verified against
        # stable_baselines3.common.{policies,torch_layers}). The ultrasonic
        # branch is a bare Flatten with no parameters, hence no copy.
        policy = model.policy
        src = self.model
        # Guard: if SB3 changes its layer layout in a future upgrade, fail
        # with an actionable message instead of a cryptic RuntimeError from load_state_dict.
        extractors = policy.features_extractor.extractors
        assert "image" in extractors and "ultrasonic" in extractors, (
            "SB3 CombinedExtractor layout changed: expected keys 'image' and 'ultrasonic', "
            f"got {list(extractors.keys())}"
        )
        assert hasattr(extractors["image"], "cnn") and hasattr(extractors["image"], "linear"), (
            "SB3 NatureCNN layout changed: expected attributes 'cnn' and 'linear' on image extractor"
        )
        assert extractors["image"].linear[0].out_features == CNN_OUTPUT_DIM, (
            f"cnn_output_dim mismatch: SB3 produced {extractors['image'].linear[0].out_features}, "
            f"trainer expects {CNN_OUTPUT_DIM}"
        )
        with torch.no_grad():
            policy.features_extractor.extractors["image"].cnn.load_state_dict(src.cnn.state_dict())
            policy.features_extractor.extractors["image"].linear.load_state_dict(src.linear.state_dict())
            policy.mlp_extractor.policy_net.load_state_dict(src.policy_net.state_dict())
            policy.action_net.load_state_dict(src.action_net.state_dict())

        output_zip = Path(output_zip)
        output_zip.parent.mkdir(parents=True, exist_ok=True)
        model.save(output_zip)
        venv.close()

    def _export_sb3_multimodal(self, output_zip: Path) -> None:
        """Lift the multi-modal BC model into an SB3 PPO checkpoint.

        Builds an SB3 PPO around a stub env that exposes
        Dict({image, ultrasonic, occupancy}) and registers our custom
        MultiModalOccupancyExtractor as the features_extractor. Then
        copies the trained weights submodule-by-submodule from our
        _MultiModalHead into the SB3 policy graph.

        Also dumps the raw state_dict to `<output>.pt` so non-SB3
        consumers (e.g. the WebUI live-inference path that bypasses
        the PPO wrapper) can load it directly.
        """
        import gymnasium as gym
        from gymnasium import spaces
        from stable_baselines3 import PPO
        from stable_baselines3.common.vec_env import DummyVecEnv

        from .bc_to_ppo import BcToPpoConfig
        from .policies import MultiModalOccupancyExtractor

        class _StubMultiModalEnv(gym.Env):
            metadata = {"render_modes": []}

            def __init__(self) -> None:
                super().__init__()
                self.observation_space = spaces.Dict(
                    {
                        "image": spaces.Box(low=0, high=255, shape=(3, FRAME_SIZE, FRAME_SIZE), dtype=np.uint8),
                        "ultrasonic": spaces.Box(low=0.0, high=1.0, shape=(1,), dtype=np.float32),
                        "occupancy": spaces.Box(low=0.0, high=1.0, shape=(3, 21, 21), dtype=np.float32),
                    }
                )
                self.action_space = spaces.Discrete(N_ACTIONS)

            def _zero_obs(self):
                return {
                    "image": np.zeros((3, FRAME_SIZE, FRAME_SIZE), dtype=np.uint8),
                    "ultrasonic": np.zeros((1,), dtype=np.float32),
                    "occupancy": np.zeros((3, 21, 21), dtype=np.float32),
                }

            def reset(self, *, seed=None, options=None):
                super().reset(seed=seed)
                return self._zero_obs(), {}

            def step(self, action):
                return self._zero_obs(), 0.0, True, False, {}

        venv = DummyVecEnv([lambda: _StubMultiModalEnv()])
        ppo_cfg = BcToPpoConfig()
        model = PPO(
            "MultiInputPolicy",
            venv,
            learning_rate=ppo_cfg.learning_rate,
            n_steps=ppo_cfg.n_steps,
            batch_size=ppo_cfg.batch_size,
            n_epochs=ppo_cfg.n_epochs,
            gamma=ppo_cfg.gamma,
            clip_range=ppo_cfg.clip_range,
            ent_coef=ppo_cfg.ent_coef,
            target_kl=ppo_cfg.target_kl,
            device=self.cfg.device,
            seed=self.cfg.seed,
            policy_kwargs={"features_extractor_class": MultiModalOccupancyExtractor},
        )

        policy = model.policy
        src = self.model
        extractor = policy.features_extractor
        assert hasattr(extractor, "image_cnn") and hasattr(extractor, "map_cnn"), (
            "MultiModalOccupancyExtractor layout changed: expected image_cnn / map_cnn"
        )
        with torch.no_grad():
            extractor.image_cnn.load_state_dict(src.cnn.state_dict())
            extractor.image_linear.load_state_dict(src.linear.state_dict())
            extractor.map_cnn.load_state_dict(src.map_cnn.cnn.state_dict())
            extractor.map_linear.load_state_dict(src.map_cnn.linear.state_dict())
            policy.mlp_extractor.policy_net.load_state_dict(src.policy_net.state_dict())
            policy.action_net.load_state_dict(src.action_net.state_dict())

        output_zip = Path(output_zip)
        output_zip.parent.mkdir(parents=True, exist_ok=True)
        model.save(output_zip)
        # Also save the raw torch state_dict — non-SB3 inference paths
        # (WebUI live, ablation forensics) read the .pt directly.
        torch.save({
            "model_state_dict": self.model.state_dict(),
            "modality": "image+ultrasonic+occupancy",
        }, output_zip.with_suffix(".pt"))
        venv.close()
        print(f"[bc] saved multi-modal SB3 checkpoint: {output_zip}", flush=True)
        print(f"[bc] saved raw torch state_dict:       {output_zip.with_suffix('.pt')}", flush=True)
