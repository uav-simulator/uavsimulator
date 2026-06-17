from __future__ import annotations

import pytest

from sim_client.cli import _select_runtime_asset


def test_select_runtime_asset_requires_exact_platform_match() -> None:
    release = {
        "assets": [
            {
                "name": "uav-simulator-macos-v0.1.2.zip",
                "kind": "runtime",
                "platform": "macos",
            },
            {
                "name": "uav_sim_client-0.1.2.tar.gz",
                "kind": "package",
                "platform": "unknown",
            },
        ]
    }

    with pytest.raises(RuntimeError) as excinfo:
        _select_runtime_asset(release, "windows")

    message = str(excinfo.value)
    assert "windows" in message
    assert "macos" in message
    assert "uav-simulator-macos-v0.1.2.zip" in message
    assert "uav_sim_client-0.1.2.tar.gz" not in message


def test_select_runtime_asset_returns_exact_requested_platform() -> None:
    release = {
        "assets": [
            {
                "name": "uav-simulator-macos-v0.1.2.zip",
                "kind": "runtime",
                "platform": "macos",
            },
            {
                "name": "uav-simulator-windows-v0.1.2.zip",
                "kind": "runtime",
                "platform": "windows",
            },
        ]
    }

    selected = _select_runtime_asset(release, "windows")

    assert selected["name"] == "uav-simulator-windows-v0.1.2.zip"
