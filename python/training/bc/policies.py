"""Stable-Baselines3 BaseFeaturesExtractor for our multi-modal observation
{image, ultrasonic, occupancy}.

Mirrors the architecture of training.bc.trainer._MultiModalHead so a BC
checkpoint can be lifted into an SB3 PPO with weight-for-weight
correspondence: same NatureCNN for the image, same _MapCnn for the
occupancy, same concatenated feature dim of 641 going into the policy
MLP head.

Registered via `policy_kwargs={"features_extractor_class": MultiModalOccupancyExtractor}`
in PPO(...) — SB3 wires it as the policy's features_extractor and the
default MlpExtractor takes over from there.
"""
from __future__ import annotations

import gymnasium as gym
import torch
import torch.nn as nn
from gymnasium import spaces
from stable_baselines3.common.torch_layers import BaseFeaturesExtractor


_CNN_OUTPUT_DIM = 512
_MAP_CNN_OUTPUT_DIM = 128


class MultiModalOccupancyExtractor(BaseFeaturesExtractor):
    """Multi-modal features extractor for image + ultrasonic + occupancy.

    Input observation space must be a Dict with the three keys.

      image       (3, H, W) uint8  — NatureCNN → 512
      ultrasonic  (1,)      float  — passthrough (Flatten)  →  1
      occupancy   (3, 21, 21) float — MapCNN → 128
                                                  total = 641

    Output features are concatenated in the order (image, ultrasonic,
    occupancy) — same ordering as _MultiModalHead.forward — so the SB3
    PPO's MlpExtractor sees them in a layout compatible with our trained
    BC weights.
    """

    def __init__(self, observation_space: spaces.Dict):
        if "image" not in observation_space.spaces:
            raise ValueError("MultiModalOccupancyExtractor requires 'image' key in observation")
        if "ultrasonic" not in observation_space.spaces:
            raise ValueError("MultiModalOccupancyExtractor requires 'ultrasonic' key in observation")
        if "occupancy" not in observation_space.spaces:
            raise ValueError("MultiModalOccupancyExtractor requires 'occupancy' key in observation")
        # distances_8 (8-d structured raycast) is plumbed offline into
        # the demo dataset (BcSample.distances_8, training.bc.occupancy.
        # directional_distances_8) but is intentionally NOT consumed here:
        # the production BC checkpoint is 641-d (image:512 + ultra:1 +
        # map:128) and the wrapper's obs_space matches. Re-enabling
        # distances_8 is a coordinated 5-touch change — wrapper obs_space
        # + extractor features_dim + trainer combined_dim + dataset/fit
        # batching + retrain BC to 649-d.
        self._has_distances_8 = False

        # We must compute the features_dim before super().__init__ in SB3's
        # BaseFeaturesExtractor.
        features_dim = _CNN_OUTPUT_DIM + 1 + _MAP_CNN_OUTPUT_DIM
        super().__init__(observation_space, features_dim=features_dim)

        # Image branch — same shape as the BC trainer's NatureCNN.
        img_space = observation_space.spaces["image"]
        # SB3 transposes (H, W, C) → (C, H, W) via VecTransposeImage before
        # the extractor sees it; assert we got the transposed (C, H, W) form.
        c, h, w = img_space.shape
        if c != 3:
            raise ValueError(
                f"Expected image with 3 channels (post-VecTransposeImage), got shape {img_space.shape}"
            )
        self.image_cnn = nn.Sequential(
            nn.Conv2d(3, 32, kernel_size=8, stride=4, padding=0),
            nn.ReLU(),
            nn.Conv2d(32, 64, kernel_size=4, stride=2, padding=0),
            nn.ReLU(),
            nn.Conv2d(64, 64, kernel_size=3, stride=1, padding=0),
            nn.ReLU(),
            nn.Flatten(),
        )
        with torch.no_grad():
            sample_img = torch.zeros(1, 3, h, w)
            n_flatten = self.image_cnn(sample_img).shape[1]
        self.image_linear = nn.Sequential(nn.Linear(n_flatten, _CNN_OUTPUT_DIM), nn.ReLU())

        # Occupancy branch — _MapCnn architecture from the BC trainer.
        occ_space = observation_space.spaces["occupancy"]
        oc, oh, ow = occ_space.shape
        self.map_cnn = nn.Sequential(
            nn.Conv2d(oc, 32, kernel_size=3, stride=1, padding=1),
            nn.ReLU(),
            nn.Conv2d(32, 64, kernel_size=3, stride=2, padding=1),
            nn.ReLU(),
            nn.Conv2d(64, 64, kernel_size=3, stride=2, padding=1),
            nn.ReLU(),
            nn.Flatten(),
        )
        with torch.no_grad():
            sample_occ = torch.zeros(1, oc, oh, ow)
            n_map_flatten = self.map_cnn(sample_occ).shape[1]
        self.map_linear = nn.Sequential(nn.Linear(n_map_flatten, _MAP_CNN_OUTPUT_DIM), nn.ReLU())

    def forward(self, observations: dict) -> torch.Tensor:
        img = observations["image"]
        # SB3 keeps image uint8 in obs and normalises to [0, 1] inside
        # NatureCNN. We follow the same convention for compatibility.
        img = img.float() / 255.0
        img_features = self.image_linear(self.image_cnn(img))

        ultra = observations["ultrasonic"]
        if ultra.dim() == 1:
            ultra = ultra.unsqueeze(0)

        occ = observations["occupancy"].float()
        map_features = self.map_linear(self.map_cnn(occ))

        # distances_8 not yet wired into this extractor (see __init__ note).
        return torch.cat([img_features, ultra, map_features], dim=1)
