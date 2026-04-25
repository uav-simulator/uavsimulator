"""Image augmentation observation wrapper for visual sim-to-real.

Real KS0223 camera frames differ from clean Unity renders along several axes:
- JPEG-compression artifacts (encoded q=95, decoded by ImageSharp in backend)
- Motion blur from robot vibration during movement
- Lighting variability (room ambient + window sunlight + table lamp)
- Color cast (warm room lighting vs neutral Unity)
- Sensor noise (low-light grain on USB cam)
- Slight focus softness

This wrapper applies stochastic augmentations to the "image" observation key
during training to widen the visual distribution PPO sees, which should help
the policy generalize to real frames at inference time.

Augmentations are applied independently per env step; toggle eval=True to
disable for deterministic evaluation.
"""

from __future__ import annotations

import io
from typing import Any

import gymnasium as gym
import numpy as np
from PIL import Image, ImageFilter


class ImageAugObservationWrapper(gym.ObservationWrapper):
    """Apply random image augmentations to obs["image"] each step.

    Expects obs to be a dict with key "image" of shape (H, W, 3) uint8.
    """

    def __init__(
        self,
        env: gym.Env,
        *,
        enable: bool = True,
        noise_sigma: float = 0.02,
        brightness_range: float = 0.15,
        contrast_range: float = 0.15,
        hue_shift_range: float = 5.0,  # degrees on H channel of HSV
        blur_prob: float = 0.3,
        blur_radius_max: float = 1.2,
        jpeg_recompress_prob: float = 0.5,
        jpeg_quality_min: int = 70,
        jpeg_quality_max: int = 95,
        seed: int | None = None,
    ):
        super().__init__(env)
        self.enable = enable
        self.noise_sigma = noise_sigma
        self.brightness_range = brightness_range
        self.contrast_range = contrast_range
        self.hue_shift_range = hue_shift_range
        self.blur_prob = blur_prob
        self.blur_radius_max = blur_radius_max
        self.jpeg_recompress_prob = jpeg_recompress_prob
        self.jpeg_quality_min = jpeg_quality_min
        self.jpeg_quality_max = jpeg_quality_max
        self.rng = np.random.default_rng(seed)

    def observation(self, obs: Any) -> Any:
        if not self.enable:
            return obs
        if not isinstance(obs, dict) or "image" not in obs:
            return obs

        image = obs["image"]
        if image.ndim != 3 or image.shape[2] != 3:
            return obs

        augmented = self._augment(image)
        out = dict(obs)
        out["image"] = augmented
        return out

    def _augment(self, image: np.ndarray) -> np.ndarray:
        # Work in float32 [0, 255] for math, return uint8 at the end
        x = image.astype(np.float32)

        # Brightness
        if self.brightness_range > 0:
            delta = self.rng.uniform(-self.brightness_range, self.brightness_range) * 255.0
            x = x + delta

        # Contrast (around 128)
        if self.contrast_range > 0:
            scale = 1.0 + self.rng.uniform(-self.contrast_range, self.contrast_range)
            x = (x - 128.0) * scale + 128.0

        # Hue shift via HSV (cheap approximation)
        if self.hue_shift_range > 0:
            x = self._hue_shift(x, self.rng.uniform(-self.hue_shift_range, self.hue_shift_range))

        # Gaussian noise
        if self.noise_sigma > 0:
            noise = self.rng.normal(0.0, self.noise_sigma * 255.0, x.shape).astype(np.float32)
            x = x + noise

        x = np.clip(x, 0.0, 255.0).astype(np.uint8)

        # Optional blur
        if self.blur_prob > 0 and self.rng.random() < self.blur_prob:
            radius = float(self.rng.uniform(0.3, self.blur_radius_max))
            pil_img = Image.fromarray(x).filter(ImageFilter.GaussianBlur(radius=radius))
            x = np.asarray(pil_img)

        # Optional JPEG recompress (matches real-cam UDP pipeline)
        if self.jpeg_recompress_prob > 0 and self.rng.random() < self.jpeg_recompress_prob:
            q = int(self.rng.integers(self.jpeg_quality_min, self.jpeg_quality_max + 1))
            buf = io.BytesIO()
            Image.fromarray(x).save(buf, format="JPEG", quality=q)
            buf.seek(0)
            x = np.asarray(Image.open(buf).convert("RGB"))

        return x

    @staticmethod
    def _hue_shift(rgb_f32: np.ndarray, hue_deg: float) -> np.ndarray:
        """Approximate hue shift on a float32 RGB image without full HSV conversion.

        Uses YIQ-like rotation in chroma plane, sufficient for ±5° training noise.
        """
        u = np.deg2rad(hue_deg)
        cos_u, sin_u = np.cos(u), np.sin(u)
        # YIQ rotation matrix for hue shift (well-known approximation)
        m = np.array(
            [
                [1.0, 0.0, 0.0],
                [0.0, cos_u, -sin_u],
                [0.0, sin_u, cos_u],
            ],
            dtype=np.float32,
        )
        # YIQ ↔ RGB matrices
        rgb_to_yiq = np.array(
            [
                [0.299, 0.587, 0.114],
                [0.595716, -0.274453, -0.321263],
                [0.211456, -0.522591, 0.311135],
            ],
            dtype=np.float32,
        )
        yiq_to_rgb = np.linalg.inv(rgb_to_yiq).astype(np.float32)
        flat = rgb_f32.reshape(-1, 3)
        flat = flat @ rgb_to_yiq.T
        flat = flat @ m.T
        flat = flat @ yiq_to_rgb.T
        return flat.reshape(rgb_f32.shape)
