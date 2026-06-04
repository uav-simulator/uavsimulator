import numpy as np
from PIL import Image

from training.traffic.tl_classifier import TlClassifier, TlClassifierConfig, load_dataset_from_jsonl


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


def test_tl_classifier_overfits_synthetic_4_classes():
    """City gate smoke uses Red/Yellow/Green/None, not only the old 3-class layout."""
    cfg = TlClassifierConfig(epochs=60, batch_size=4, lr=1e-3, device="cpu", seed=1, n_classes=4)
    clf = TlClassifier(cfg)
    rng = np.random.default_rng(1)
    samples = []
    colors = [
        (220, 20, 20),
        (220, 220, 20),
        (20, 220, 20),
        (70, 70, 70),
    ]
    for label, color in enumerate(colors):
        for _ in range(4):
            f = rng.integers(0, 30, (84, 84, 3), dtype=np.uint8)
            f[20:64, 20:64, :] = color
            samples.append((f, label))
    history = clf.fit(samples)
    assert history["train_accuracy"][-1] >= 0.95


def test_load_dataset_from_jsonl_supports_city_schema(tmp_path):
    """Runtime city mini-datasets store modelInputPath/labelId instead of frame/label_idx."""
    frames = tmp_path / "model_84x84"
    frames.mkdir()
    Image.fromarray(np.zeros((84, 84, 3), dtype=np.uint8)).save(frames / "red.jpg")
    Image.fromarray(np.full((126, 224, 3), 127, dtype=np.uint8)).save(frames / "none.jpg")
    jsonl = tmp_path / "samples.jsonl"
    jsonl.write_text(
        "\n".join(
            [
                '{"modelInputPath":"model_84x84/red.jpg","labelId":0,"label":"Red"}',
                '{"framePath":"model_84x84/none.jpg","labelId":3,"label":"None"}',
            ]
        ),
        encoding="utf-8",
    )

    samples = load_dataset_from_jsonl(jsonl)

    assert [label for _, label in samples] == [0, 3]
    assert samples[0][0].shape == (84, 84, 3)
    assert samples[1][0].shape == (84, 84, 3)


def test_tl_classifier_exports_single_file_onnx(tmp_path):
    cfg = TlClassifierConfig(epochs=1, batch_size=2, lr=1e-3, device="cpu", seed=2, n_classes=4)
    clf = TlClassifier(cfg)
    output_path = tmp_path / "traffic-light.onnx"

    clf.export_onnx(output_path)

    assert output_path.exists()
    assert not output_path.with_suffix(".onnx.data").exists()
