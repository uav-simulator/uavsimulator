from __future__ import annotations

import importlib.util
from pathlib import Path
from types import ModuleType

import pytest


def _load_manifest_module() -> ModuleType:
    module_path = Path(__file__).resolve().parents[2] / "scripts" / "generate_release_manifest.py"
    spec = importlib.util.spec_from_file_location("generate_release_manifest_for_tests", module_path)
    if spec is None or spec.loader is None:
        raise AssertionError(f"Unable to load {module_path}")
    module = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(module)
    return module


manifest = _load_manifest_module()


@pytest.mark.parametrize(
    "name",
    [
        "uav_sim_client-0.1.2-py3-none-any.whl",
        "uav_sim_client-0.1.2.tar.gz",
        "uav-sim-client-0.1.2.tar.gz",
        "uav_sim_client-0.1.2.tgz",
    ],
)
def test_python_package_artifacts_are_not_runtime_assets(name: str) -> None:
    assert manifest._infer_asset_kind(name) == "package"


@pytest.mark.parametrize(
    ("name", "platform"),
    [
        ("uav-simulator-macos-v0.1.2.zip", "macos"),
        ("uav-simulator-windows-v0.1.2.zip", "windows"),
        ("uav-simulator-linux-v0.1.2.tar.gz", "linux"),
        ("uav-simulator-linux-v0.1.2.tgz", "linux"),
    ],
)
def test_platform_runtime_archives_remain_runtime_assets(name: str, platform: str) -> None:
    assert manifest._infer_asset_kind(name) == "runtime"
    assert manifest._infer_platform(name) == platform
