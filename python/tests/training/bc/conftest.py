"""Shared fixtures for BC test suite."""
from __future__ import annotations

import numpy as np
import pytest

from training.bc.dataset import BcSample


def make_synthetic_samples(n: int, seed: int = 0) -> list[BcSample]:
    """Generate n random BcSamples for offline trainer/transfer tests."""
    rng = np.random.default_rng(seed)
    return [
        BcSample(
            frame=rng.integers(0, 256, (84, 84, 3), dtype=np.uint8),
            ultrasonic=float(rng.random()),
            action_idx=int(rng.integers(0, 5)),
        )
        for _ in range(n)
    ]


@pytest.fixture
def synthetic_samples():
    """Factory fixture: synthetic_samples(n, seed=0) -> list[BcSample]."""
    return make_synthetic_samples
