from __future__ import annotations

import zipfile

import pytest

from sim_client.cli import _extract_runtime_archive, _resolve_runtime_executable, _select_runtime_asset


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


def test_extract_runtime_archive_supports_windows_build_directory(tmp_path) -> None:
    archive_path = tmp_path / "uav-simulator-windows-v0.1.3.zip"
    with zipfile.ZipFile(archive_path, "w") as archive:
        archive.writestr("uav-simulator-windows/uav-simulator.exe", b"exe")
        archive.writestr("uav-simulator-windows/UnityPlayer.dll", b"dll")
        archive.writestr("uav-simulator-windows/uav-simulator_Data/boot.config", b"config")

    extracted = _extract_runtime_archive(archive_path, tmp_path / "install")

    assert extracted == tmp_path / "install" / "uav-simulator-windows"
    assert (extracted / "uav-simulator.exe").read_bytes() == b"exe"
    assert _resolve_runtime_executable(extracted) == extracted / "uav-simulator.exe"


def test_extract_runtime_archive_supports_root_level_windows_build(tmp_path) -> None:
    archive_path = tmp_path / "uav-simulator-windows-v0.1.3.zip"
    with zipfile.ZipFile(archive_path, "w") as archive:
        archive.writestr("uav-simulator.exe", b"exe")
        archive.writestr("UnityPlayer.dll", b"dll")

    extracted = _extract_runtime_archive(archive_path, tmp_path / "install")

    assert extracted == tmp_path / "install" / "uav-simulator-windows-v0.1.3"
    assert (extracted / "uav-simulator.exe").read_bytes() == b"exe"
    assert _resolve_runtime_executable(extracted) == extracted / "uav-simulator.exe"


def test_extract_runtime_archive_rejects_archives_without_runtime(tmp_path) -> None:
    archive_path = tmp_path / "notes.zip"
    with zipfile.ZipFile(archive_path, "w") as archive:
        archive.writestr("README.txt", b"not a runtime")

    with pytest.raises(RuntimeError, match="supported Unity runtime"):
        _extract_runtime_archive(archive_path, tmp_path / "install")
