"""Unit tests for the unified ONNX exporter (`training/export_onnx.py`).

Only the pure argparse / path-derivation layer is exercised here. The actual
torch.onnx.export call requires a trained SB3 checkpoint and the heavyweight
``training`` extra, so it is left to the manual export workflow.
"""

from __future__ import annotations

from pathlib import Path

import pytest

from training import export_onnx

# ---------------------------------------------------------------------------
# parse_args: required + defaults
# ---------------------------------------------------------------------------


def test_parse_args_requires_rev() -> None:
    with pytest.raises(SystemExit):
        export_onnx.parse_args([])


def test_parse_args_minimal_invocation_has_v9_defaults() -> None:
    args = export_onnx.parse_args(["--rev", "rev42"])
    assert args.rev == "rev42"
    assert args.model_family == "cardboard-corridor-ppo-v9"
    assert args.version == "1.0.0"
    assert args.frame_stack == 1
    assert args.opset == 11
    assert args.sanity_forward is False
    assert args.verify_signature is False
    assert args.artifacts_root == Path("python/training/artifacts")


def test_parse_args_frame_stack_for_rev38() -> None:
    args = export_onnx.parse_args(["--rev", "rev38", "--frame-stack", "4"])
    assert args.frame_stack == 4


def test_parse_args_diagnostic_flags() -> None:
    args = export_onnx.parse_args(
        ["--rev", "rev24", "--sanity-forward", "--verify-signature"]
    )
    assert args.sanity_forward is True
    assert args.verify_signature is True


def test_parse_args_custom_artifacts_root_and_version() -> None:
    args = export_onnx.parse_args(
        [
            "--rev",
            "rev42",
            "--artifacts-root",
            "/tmp/custom-artifacts",
            "--version",
            "2.0.0-beta",
        ]
    )
    assert args.artifacts_root == Path("/tmp/custom-artifacts")
    assert args.version == "2.0.0-beta"


def test_parse_args_supports_alternate_model_family() -> None:
    # Future PPO families can re-use the same exporter with --model-family.
    args = export_onnx.parse_args(
        ["--rev", "rev01", "--model-family", "maze-ppo-v1"]
    )
    assert args.model_family == "maze-ppo-v1"


# ---------------------------------------------------------------------------
# main: missing-checkpoint error path is reachable without torch installed
# ---------------------------------------------------------------------------


def test_main_raises_filenotfound_for_missing_checkpoint(tmp_path: Path) -> None:
    """Confirm the path-resolution layer surfaces a clear error before importing torch."""
    # We cannot exercise the torch+SB3 happy path without a real checkpoint,
    # but the FileNotFoundError raised before model-load is testable as long
    # as torch + SB3 are importable. Skip cleanly otherwise.
    pytest.importorskip("torch")
    pytest.importorskip("stable_baselines3")

    with pytest.raises(FileNotFoundError, match="SB3 checkpoint not found"):
        export_onnx.main(
            [
                "--rev",
                "rev_does_not_exist",
                "--artifacts-root",
                str(tmp_path),
            ]
        )
