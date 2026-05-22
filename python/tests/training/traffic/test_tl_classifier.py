import numpy as np

from training.traffic.tl_classifier import TlClassifier, TlClassifierConfig


def test_tl_classifier_overfits_synthetic_3_classes():
    """If trainer is correctly implemented, it overfits 12 strongly-tinted samples."""
    cfg = TlClassifierConfig(epochs=50, batch_size=4, lr=1e-3, device="cpu", seed=0)
    clf = TlClassifier(cfg)
    rng = np.random.default_rng(0)
    samples = []
    for label in range(3):
        for _ in range(4):
            f = rng.integers(0, 50, (84, 84, 3), dtype=np.uint8)
            f[:, :, label] = 220
            samples.append((f, label))
    history = clf.fit(samples)
    assert history["train_accuracy"][-1] >= 0.95
