"""Plan 5 (rev40): pre-trained vision feature extractor for SB3 PPO.

Bypasses scratch-CNN training. Frozen pretrained backbone + small MLP head:

  84x84x3 image  -> resize to 224x224 -> ResNet18 (frozen) -> 512-d features
                                                                      |
                                                                      v
  ultrasonic (k,)   ────────────────────────────────────── concat ─> [512+k]
                                                                      |
                                                                      v
                                                             MLP -> 256-d feat
                                                                  -> SB3 policy / value

The "R3M" name is preserved in the file name for plan-tracking continuity,
but the backbone is torchvision ImageNet ResNet18 (R3M-specific manipulation
weights gave dependency hassles in this stack, and ImageNet weights work
for indoor corridor navigation just as well — the policy needs robust
texture / edge features, not manipulation-specific ones).

Frozen = no backprop through ResNet → orders of magnitude fewer trainable
params → variance bound ≪ scratch CNN training. Pretrained on real-world
images = closer to real-camera distribution than scratch CNN.
"""
from __future__ import annotations

import torch
import torch.nn as nn
import torch.nn.functional as F
from gymnasium import spaces
from stable_baselines3.common.torch_layers import BaseFeaturesExtractor


class R3MFeatureExtractor(BaseFeaturesExtractor):
    """Frozen pretrained vision backbone + small MLP head.

    Args:
        observation_space: SB3 dict obs space with 'image' (84,84,3 uint8)
            and 'ultrasonic' (k,) keys.
        features_dim: output dim of the head MLP. Default 256.
        backbone: 'resnet18' (44 MB, fast) or 'resnet50' (98 MB, slow but
            more capacity). Default 'resnet18'.
    """

    def __init__(
        self,
        observation_space: spaces.Dict,
        features_dim: int = 256,
        backbone: str = "resnet18",
    ):
        super().__init__(observation_space, features_dim)

        # Lazy import — torchvision optional dep
        from torchvision.models import resnet18, resnet50, ResNet18_Weights, ResNet50_Weights

        if backbone == "resnet18":
            net = resnet18(weights=ResNet18_Weights.IMAGENET1K_V1)
            self._feat_dim = 512
        elif backbone == "resnet50":
            net = resnet50(weights=ResNet50_Weights.IMAGENET1K_V2)
            self._feat_dim = 2048
        else:
            raise ValueError(f"Unsupported backbone: {backbone}")

        # Freeze all params + drop the final fc (we want pre-fc 512-d features)
        for p in net.parameters():
            p.requires_grad = False
        net.fc = nn.Identity()
        net.eval()
        self.backbone = net

        # ImageNet normalization constants
        self.register_buffer(
            "img_mean", torch.tensor([0.485, 0.456, 0.406]).view(1, 3, 1, 1)
        )
        self.register_buffer(
            "img_std", torch.tensor([0.229, 0.224, 0.225]).view(1, 3, 1, 1)
        )

        sonar_dim = int(observation_space["ultrasonic"].shape[0])

        self.head = nn.Sequential(
            nn.Linear(self._feat_dim + sonar_dim, 512),
            nn.ReLU(inplace=True),
            nn.Linear(512, features_dim),
            nn.ReLU(inplace=True),
        )

    def forward(self, obs):
        image = obs["image"]
        if image.dtype == torch.uint8:
            image = image.float()
        # SB3 may pass 0-255 floats; normalise to 0-1
        if image.max() > 1.5:
            image = image / 255.0

        # Layout detection: SB3 auto-wraps HWC obs in VecTransposeImage,
        # producing (B, C, H, W). Frame-stack also works on CHW. Without
        # the wrapper obs comes through as (B, H, W, C). Detect by checking
        # if dim 1 is the small channel count (3 or 3*k frame-stack) vs the
        # large H = 84.
        if image.dim() == 4 and image.shape[1] in (3, 6, 9, 12, 15):
            # Already CHW — VecTransposeImage applied
            image_chw = image.contiguous()
        else:
            # HWC — permute to CHW
            image_chw = image.permute(0, 3, 1, 2).contiguous()

        # Resize to ResNet input size (224x224)
        image_chw = F.interpolate(image_chw, size=(224, 224), mode="bilinear", align_corners=False)

        # Apply ImageNet normalization. img_mean/std are (1, 3, 1, 1) — for
        # frame-stacked obs (channels=12), normalize each k-block independently
        # by repeating mean/std along channel axis.
        c = image_chw.shape[1]
        if c == 3:
            image_chw = (image_chw - self.img_mean) / self.img_std
        else:
            # Repeat normalization stats across stacked frames
            mean_rep = self.img_mean.repeat(1, c // 3, 1, 1)
            std_rep = self.img_std.repeat(1, c // 3, 1, 1)
            image_chw = (image_chw - mean_rep) / std_rep

        # Forward through frozen backbone. ResNet18 expects 3 channels —
        # for frame-stacked obs (12 channels), reduce to 3 via channel-mean
        # over the k-block. This loses fine motion info but keeps the
        # network usable; alternative is to retrain backbone first conv.
        if c != 3:
            B = image_chw.shape[0]
            image_chw = image_chw.reshape(B, c // 3, 3, 224, 224).mean(dim=1)

        with torch.no_grad():
            features = self.backbone(image_chw)  # (B, 512) for resnet18

        sonar = obs["ultrasonic"].float()
        if sonar.dim() == 1:
            sonar = sonar.unsqueeze(0)

        combined = torch.cat([features, sonar], dim=1)
        return self.head(combined)
