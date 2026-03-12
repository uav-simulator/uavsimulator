from __future__ import annotations

import argparse
import hashlib
import json
import os
import shutil
import signal
import subprocess
import sys
import time
import urllib.error
import urllib.parse
import urllib.request
import zipfile
from datetime import datetime
from importlib.metadata import PackageNotFoundError, version as package_version
from pathlib import Path
from typing import Any, Dict, List

from .contract import validate_contract
from .http_client import SimClient
from .scenario import load_scenario_file, scenario_to_reset_config, validate_scenario


CLI_PACKAGE_NAME = "uav-sim-client"
DEFAULT_UNITY_VERSION = "6000.1.8f1"
DEFAULT_UNITY_BIN = f"/Applications/Unity/Hub/Editor/{DEFAULT_UNITY_VERSION}/Unity.app/Contents/MacOS/Unity"
DEFAULT_PROJECT_PATH = "src/UnityProject/uav-simulator"
DEFAULT_SCENE_PATH = "Assets/Scenes/TrackScence.unity"
REPO_ROOT = Path(__file__).resolve().parents[2]
DEFAULT_RUNTIME_OUTPUT_DIR = REPO_ROOT / "build" / "runtime" / "macos"
DEFAULT_BIN_DIR = Path.home() / ".local" / "bin"
DEFAULT_ZSHRC = Path.home() / ".zshrc"
DEFAULT_RUSIM_HOME = REPO_ROOT / ".rusim"
DEFAULT_GITHUB_REPO = os.environ.get("GITHUB_REPOSITORY", "NMGorovenko/uav-simulator")
DEFAULT_RELEASE_CHANNEL = "stable"
RELEASE_MANIFEST_ASSET_NAME = "rusim-release-manifest.json"


def build_parser() -> argparse.ArgumentParser:
    parser = argparse.ArgumentParser(description="CLI for uav-simulator operator/runtime flows.")
    parser.set_defaults(_parser=parser)
    subparsers = parser.add_subparsers(dest="command")

    doctor = subparsers.add_parser("doctor", help="Check simulator health and contract.")
    doctor.set_defaults(_parser=doctor)
    doctor.add_argument("--base-url", default="http://127.0.0.1:8000")

    version_cmd = subparsers.add_parser("version", help="Show rusim CLI and runtime metadata.")
    version_cmd.set_defaults(_parser=version_cmd)

    contract = subparsers.add_parser("contract", help="Print simulator contract.")
    contract.set_defaults(_parser=contract)
    contract.add_argument("--base-url", default="http://127.0.0.1:8000")

    install = subparsers.add_parser("install", help="Install rusim wrapper into user PATH.")
    install.set_defaults(_parser=install)
    install.add_argument("--bin-dir", default=str(DEFAULT_BIN_DIR))
    install.add_argument("--rc-file", default=str(DEFAULT_ZSHRC))
    install.add_argument("--write-shell-config", action="store_true")

    upgrade = subparsers.add_parser("upgrade", help="Download and install runtime from GitHub Release manifest.")
    upgrade.set_defaults(_parser=upgrade)
    upgrade.add_argument("--repo", default=DEFAULT_GITHUB_REPO)
    upgrade.add_argument("--tag", default="latest", help="Release tag or 'latest'.")
    upgrade.add_argument("--manifest-url", default="", help="Optional direct URL to release manifest JSON.")
    upgrade.add_argument("--platform", default=_detect_runtime_platform())
    upgrade.add_argument("--channel", default=DEFAULT_RELEASE_CHANNEL)
    upgrade.add_argument("--check-only", action="store_true")
    upgrade.add_argument("--force", action="store_true", help="Reinstall even if same release build is already present.")
    upgrade.add_argument("--no-set-favorite", action="store_true")
    upgrade.add_argument("--github-token", default=os.environ.get("GITHUB_TOKEN", ""))

    list_cmd = subparsers.add_parser("list", help="List available runtime entities from simulator contract.")
    list_cmd.set_defaults(_parser=list_cmd)
    list_sub = list_cmd.add_subparsers(dest="list_command")
    for name in ("tracks", "scenes", "vehicles"):
        parser_item = list_sub.add_parser(name, help=f"List available {name}.")
        parser_item.set_defaults(_parser=parser_item)
        parser_item.add_argument("--base-url", default="http://127.0.0.1:8000")

    inspect = subparsers.add_parser("inspect", help="Inspect a vehicle or track from simulator contract.")
    inspect.set_defaults(_parser=inspect)
    inspect_sub = inspect.add_subparsers(dest="inspect_command")
    for name in ("track", "scene", "vehicle"):
        parser_item = inspect_sub.add_parser(name, help=f"Inspect {name} by id.")
        parser_item.set_defaults(_parser=parser_item)
        parser_item.add_argument("id")
        parser_item.add_argument("--base-url", default="http://127.0.0.1:8000")

    reset_cmd = subparsers.add_parser("reset", help="Reset simulator by selecting track and vehicle directly.")
    reset_cmd.set_defaults(_parser=reset_cmd)
    reset_cmd.add_argument("--base-url", default="http://127.0.0.1:8000")
    reset_cmd.add_argument("--track-id", default="")
    reset_cmd.add_argument("--vehicle-id", default="")
    reset_cmd.add_argument("--seed", type=int, default=0)
    reset_cmd.add_argument("--time-scale", type=float, default=1.0)

    runtime = subparsers.add_parser("runtime", help="Build and inspect standalone runtime.")
    runtime.set_defaults(_parser=runtime)
    runtime_sub = runtime.add_subparsers(dest="runtime_command")

    build = runtime_sub.add_parser("build", help="Build standalone macOS runtime app.")
    build.set_defaults(_parser=build)
    build.add_argument("--unity-bin", default=os.environ.get("UNITY_BIN", DEFAULT_UNITY_BIN))
    build.add_argument("--project-path", default=DEFAULT_PROJECT_PATH)
    build.add_argument("--scene", default=DEFAULT_SCENE_PATH)
    build.add_argument("--output", default="")
    build.add_argument("--wait-seconds", type=float, default=900.0)
    build.add_argument("--label", default="")

    list_builds = runtime_sub.add_parser("list", help="List registered standalone runtime builds.")
    list_builds.set_defaults(_parser=list_builds)
    list_builds.add_argument("--json", action="store_true")

    inspect_build = runtime_sub.add_parser("inspect", help="Inspect runtime build metadata.")
    inspect_build.set_defaults(_parser=inspect_build)
    inspect_build.add_argument("build")

    run_build = runtime_sub.add_parser("run", help="Run standalone runtime by build id, latest or favorite.")
    run_build.set_defaults(_parser=run_build)
    run_build.add_argument("--build", default="latest")
    run_build.add_argument("--mode", choices=["windowed", "background", "headless"], default="windowed")
    run_build.add_argument("--host", default="127.0.0.1")
    run_build.add_argument("--port", type=int, default=8000)
    run_build.add_argument("--scenario")
    run_build.add_argument("--wait-seconds", type=float, default=45.0)

    favorite = runtime_sub.add_parser("favorite", help="Manage favorite standalone runtime build.")
    favorite.set_defaults(_parser=favorite)
    favorite_sub = favorite.add_subparsers(dest="runtime_favorite_command")
    favorite_set = favorite_sub.add_parser("set", help="Mark build as favorite.")
    favorite_set.set_defaults(_parser=favorite_set)
    favorite_set.add_argument("build")
    favorite_show = favorite_sub.add_parser("show", help="Show favorite build.")
    favorite_show.set_defaults(_parser=favorite_show)

    remove_build = runtime_sub.add_parser("remove", help="Remove runtime build from registry and disk.")
    remove_build.set_defaults(_parser=remove_build)
    remove_build.add_argument("build")
    remove_build.add_argument("--keep-files", action="store_true")
    remove_build.add_argument("--grace-seconds", type=float, default=8.0)

    server = subparsers.add_parser("server", help="Manage Unity runtime process.")
    server.set_defaults(_parser=server)
    server_sub = server.add_subparsers(dest="server_command")

    start = server_sub.add_parser("start", help="Start Unity runtime server.")
    start.set_defaults(_parser=start)
    start.add_argument("--mode", choices=["windowed", "background", "headless"], default="windowed")
    start.add_argument("--unity-bin", default=os.environ.get("UNITY_BIN", DEFAULT_UNITY_BIN))
    start.add_argument("--project-path", default=DEFAULT_PROJECT_PATH)
    start.add_argument("--scene", default=DEFAULT_SCENE_PATH)
    start.add_argument("--host", default="127.0.0.1")
    start.add_argument("--port", type=int, default=8000)
    start.add_argument("--scenario")
    start.add_argument("--wait-seconds", type=float, default=45.0)
    start.add_argument("--runtime-app", default="")

    status = server_sub.add_parser("status", help="Show Unity runtime server status.")
    status.set_defaults(_parser=status)
    status.add_argument("--host", default="127.0.0.1")
    status.add_argument("--port", type=int, default=8000)

    stop = server_sub.add_parser("stop", help="Stop Unity runtime server.")
    stop.set_defaults(_parser=stop)
    stop.add_argument("--grace-seconds", type=float, default=8.0)

    scenario = subparsers.add_parser("scenario", help="Scenario file operations.")
    scenario.set_defaults(_parser=scenario)
    scenario_sub = scenario.add_subparsers(dest="scenario_command")

    validate_cmd = scenario_sub.add_parser("validate", help="Validate scenario file.")
    validate_cmd.set_defaults(_parser=validate_cmd)
    validate_cmd.add_argument("file")

    reset_cmd = scenario_sub.add_parser("reset", help="Reset simulator from scenario file.")
    reset_cmd.set_defaults(_parser=reset_cmd)
    reset_cmd.add_argument("file")
    reset_cmd.add_argument("--base-url", default="http://127.0.0.1:8000")

    print_reset_cmd = scenario_sub.add_parser("print-reset", help="Print reset payload derived from scenario.")
    print_reset_cmd.set_defaults(_parser=print_reset_cmd)
    print_reset_cmd.add_argument("file")

    step = subparsers.add_parser("step", help="Send a single control step.")
    step.set_defaults(_parser=step)
    step.add_argument("--base-url", default="http://127.0.0.1:8000")
    step.add_argument("--throttle", type=float, default=0.0)
    step.add_argument("--steer", type=float, default=0.0)
    step.add_argument("--brake", type=float, default=0.0)

    return parser


def main(argv: list[str] | None = None) -> int:
    parser = build_parser()
    raw_args = list(sys.argv[1:] if argv is None else argv)

    if not raw_args:
        parser.print_help()
        return 0

    if raw_args[0] == "help":
        return _handle_help(parser, raw_args[1:])

    args = parser.parse_args(raw_args)

    if getattr(args, "command", None) is None:
        parser.print_help()
        return 0

    try:
        if args.command == "doctor":
            return _doctor(args.base_url)
        if args.command == "version":
            return _version()
        if args.command == "contract":
            return _contract(args.base_url)
        if args.command == "install":
            return _install(args)
        if args.command == "upgrade":
            return _upgrade(args)
        if args.command == "list":
            return _list_entities(args)
        if args.command == "inspect":
            return _inspect_entity(args)
        if args.command == "reset":
            return _reset_runtime(args)
        if args.command == "runtime":
            return _runtime(args)
        if args.command == "server":
            return _server(args)
        if args.command == "scenario":
            return _scenario(args)
        if args.command == "step":
            return _step(args.base_url, args.throttle, args.steer, args.brake)
    except Exception as exc:  # pragma: no cover - CLI boundary
        print(f"error: {exc}", file=sys.stderr)
        return 1

    parser.print_help()
    return 2


def _handle_help(parser: argparse.ArgumentParser, topics: list[str]) -> int:
    if not topics:
        parser.print_help()
        return 0

    help_args = topics + ["--help"]
    try:
        parser.parse_args(help_args)
    except SystemExit as exc:  # argparse exits with 0 after printing help
        return int(exc.code)
    return 0


def _doctor(base_url: str) -> int:
    client = SimClient(base_url=base_url)
    health = client.health()
    contract = client.get_contract()
    ok, errors = validate_contract(contract)

    result = {
        "baseUrl": base_url,
        "healthStatus": health.get("status"),
        "contractValid": ok,
        "contractErrors": errors,
        "simulatorId": contract.get("simulatorId"),
        "simulatorName": contract.get("simulatorName"),
        "vehicles": len(contract.get("availableVehicles") or []),
        "tracks": len(contract.get("availableTracks") or []),
    }
    print(json.dumps(result, ensure_ascii=False, indent=2))
    return 0 if ok else 1


def _version() -> int:
    result = {
        "cliVersion": _cli_version(),
        "gitSha": _git_short_sha(),
        "repoRoot": str(REPO_ROOT),
        "rusimHome": str(_rusim_home()),
    }
    registry = _load_runtime_registry()
    builds = registry.get("builds") or []
    if builds:
        latest = _latest_build_entry(builds)
        result["latestBuildId"] = latest.get("buildId")
    favorite_id = registry.get("favoriteBuildId")
    if favorite_id:
        result["favoriteBuildId"] = favorite_id
    print(json.dumps(result, ensure_ascii=False, indent=2))
    return 0


def _contract(base_url: str) -> int:
    client = SimClient(base_url=base_url)
    print(json.dumps(client.get_contract(), ensure_ascii=False, indent=2))
    return 0


def _install(args: argparse.Namespace) -> int:
    bin_dir = Path(args.bin_dir).expanduser().resolve()
    rc_file = Path(args.rc_file).expanduser().resolve()
    wrapper_path = REPO_ROOT / "rusim"

    if not wrapper_path.exists():
        raise FileNotFoundError(f"rusim wrapper not found: {wrapper_path}")

    wrapper_path.chmod(wrapper_path.stat().st_mode | 0o111)
    bin_dir.mkdir(parents=True, exist_ok=True)

    target = bin_dir / "rusim"
    if target.exists() or target.is_symlink():
        target.unlink()
    target.symlink_to(wrapper_path)

    path_entry = str(bin_dir)
    path_ok = _path_contains(path_entry, os.environ.get("PATH", ""))
    rc_updated = False
    export_line = f'export PATH="{path_entry}:$PATH"'

    if args.write_shell_config and not _rc_contains_line(rc_file, export_line):
        if not rc_file.exists():
            rc_file.parent.mkdir(parents=True, exist_ok=True)
            rc_file.write_text("", encoding="utf-8")
        with rc_file.open("a", encoding="utf-8") as handle:
            if rc_file.stat().st_size > 0:
                handle.write("\n")
            handle.write(f"{export_line}\n")
        rc_updated = True

    result = {
        "wrapper": str(wrapper_path),
        "installedSymlink": str(target),
        "pathEntry": path_entry,
        "pathConfiguredInCurrentShell": path_ok,
        "rcFile": str(rc_file),
        "rcUpdated": rc_updated,
        "nextStep": "source ~/.zshrc" if args.write_shell_config and not path_ok else "rusim --help",
    }
    print(json.dumps(result, ensure_ascii=False, indent=2))
    return 0


def _upgrade(args: argparse.Namespace) -> int:
    platform = _normalize_platform(args.platform)
    manifest_data, manifest_url, release_meta = _resolve_release_manifest(
        repo=args.repo,
        tag=args.tag,
        manifest_url=args.manifest_url,
        github_token=args.github_token,
    )
    release_entry = _select_manifest_release(manifest_data, args.channel, release_meta.get("tag"))
    runtime_asset = _select_runtime_asset(release_entry, platform)

    version = str(release_entry.get("version") or "unknown")
    tag = str(release_entry.get("tag") or release_meta.get("tag") or "unknown")
    build_id = f"release-{tag}-{platform}"
    registry = _load_runtime_registry()
    existing = next((item for item in registry.get("builds") or [] if str(item.get("buildId") or "") == build_id), None)
    already_installed = existing is not None and Path(str(existing.get("appPath") or "")).expanduser().exists()

    result: Dict[str, Any] = {
        "repo": args.repo,
        "channel": args.channel,
        "manifestUrl": manifest_url,
        "releaseTag": tag,
        "releaseVersion": version,
        "platform": platform,
        "assetName": runtime_asset.get("name"),
        "assetUrl": runtime_asset.get("browserDownloadUrl"),
        "buildId": build_id,
        "alreadyInstalled": already_installed,
        "checkOnly": bool(args.check_only),
    }

    if args.check_only:
        result["upgradeAvailable"] = not already_installed
        print(json.dumps(result, ensure_ascii=False, indent=2))
        return 0

    if already_installed and not args.force:
        result["skipped"] = True
        result["reason"] = "build already installed (use --force to reinstall)"
        print(json.dumps(result, ensure_ascii=False, indent=2))
        return 0

    asset_url = str(runtime_asset.get("browserDownloadUrl") or "").strip()
    if not asset_url:
        raise RuntimeError("Runtime asset has empty browserDownloadUrl in release manifest.")

    downloads_dir = _rusim_home() / "downloads" / tag
    downloads_dir.mkdir(parents=True, exist_ok=True)
    archive_path = downloads_dir / str(runtime_asset.get("name") or "runtime.zip")
    _download_file(asset_url, archive_path, github_token=args.github_token)

    expected_sha = str(runtime_asset.get("sha256") or "").strip().lower()
    actual_sha = _sha256_file(archive_path)
    if expected_sha and expected_sha != actual_sha:
        raise RuntimeError(
            f"SHA256 mismatch for {archive_path.name}: expected={expected_sha} actual={actual_sha}"
        )

    install_root = _rusim_home() / "releases" / tag
    install_root.mkdir(parents=True, exist_ok=True)
    app_path = _extract_runtime_archive(archive_path, install_root)
    executable_path = _resolve_runtime_executable(app_path)

    entry = _register_external_runtime_build(
        build_id=build_id,
        version_label=f"{version} ({tag})",
        app_path=app_path,
        executable_path=executable_path,
        unity_version=str(release_entry.get("unityVersion") or "unknown"),
        source=f"github-release://{args.repo}/{tag}",
        source_project_path=Path(f"github-release://{args.repo}"),
        metadata={
            "releaseTag": tag,
            "releaseVersion": version,
            "manifestUrl": manifest_url,
            "assetName": str(runtime_asset.get("name") or ""),
            "assetUrl": asset_url,
            "channel": args.channel,
        },
    )

    if not args.no_set_favorite:
        registry = _load_runtime_registry()
        registry["favoriteBuildId"] = entry["buildId"]
        _save_runtime_registry(registry)

    result.update(
        {
            "installed": True,
            "downloadedArchive": str(archive_path),
            "archiveSha256": actual_sha,
            "appPath": str(app_path),
            "executablePath": str(executable_path),
            "favoriteBuildId": _load_runtime_registry().get("favoriteBuildId"),
        }
    )
    print(json.dumps(result, ensure_ascii=False, indent=2))
    return 0


def _list_entities(args: argparse.Namespace) -> int:
    if not args.list_command:
        args._parser.print_help()
        return 0
    contract = SimClient(base_url=args.base_url).get_contract()
    if args.list_command in ("tracks", "scenes"):
        items = [_normalize_track_descriptor(item) for item in contract.get("availableTracks") or []]
        result = {
            "baseUrl": args.base_url,
            "kind": "tracks",
            "count": len(items),
            "items": items,
        }
    else:
        items = [_normalize_vehicle_descriptor(item) for item in contract.get("availableVehicles") or []]
        result = {
            "baseUrl": args.base_url,
            "kind": "vehicles",
            "count": len(items),
            "items": items,
        }
    print(json.dumps(result, ensure_ascii=False, indent=2))
    return 0


def _inspect_entity(args: argparse.Namespace) -> int:
    if not args.inspect_command:
        args._parser.print_help()
        return 0
    contract = SimClient(base_url=args.base_url).get_contract()
    if args.inspect_command in ("track", "scene"):
        item = _find_track(contract, args.id)
        result = {
            "baseUrl": args.base_url,
            "kind": "track",
            "track": _normalize_track_descriptor(item),
            "resetExample": {
                "command": f"rusim reset --base-url {args.base_url} --track-id {item.get('trackId')}",
            },
        }
    else:
        item = _find_vehicle(contract, args.id)
        result = {
            "baseUrl": args.base_url,
            "kind": "vehicle",
            "vehicle": _normalize_vehicle_descriptor(item, include_contract=True),
            "connectionMode": {
                "type": "shared-runtime",
                "description": "К машинке в Unity не подключаются отдельным портом. Нужно подключаться к runtime и выбирать vehicle через reset.",
            },
            "resetExample": {
                "command": f"rusim reset --base-url {args.base_url} --vehicle-id {item.get('deviceId')}",
            },
        }
    print(json.dumps(result, ensure_ascii=False, indent=2))
    return 0


def _reset_runtime(args: argparse.Namespace) -> int:
    client = SimClient(base_url=args.base_url)
    contract = client.get_contract()

    selected_track_id = args.track_id or _first_track_id(contract)
    selected_vehicle_id = args.vehicle_id or _first_vehicle_id(contract)

    if selected_track_id:
        _find_track(contract, selected_track_id)
    if selected_vehicle_id:
        _find_vehicle(contract, selected_vehicle_id)

    payload = {
        "seed": int(args.seed),
        "timeScale": float(args.time_scale),
        "selectedTrackId": selected_track_id,
        "selectedVehicleId": selected_vehicle_id,
        "trackParams": [],
        "vehicleParams": [],
        "flags": [],
    }
    response = client.reset(payload)
    result = {
        "baseUrl": args.base_url,
        "selectedTrackId": selected_track_id,
        "selectedVehicleId": selected_vehicle_id,
        "done": response.get("done"),
        "hasFrame": bool(response.get("frame")),
        "resetPayload": payload,
    }
    print(json.dumps(result, ensure_ascii=False, indent=2))
    return 0


def _runtime(args: argparse.Namespace) -> int:
    if not args.runtime_command:
        args._parser.print_help()
        return 0
    if args.runtime_command == "build":
        return _runtime_build(args)
    if args.runtime_command == "list":
        return _runtime_list(args)
    if args.runtime_command == "inspect":
        return _runtime_inspect(args)
    if args.runtime_command == "run":
        return _runtime_run(args)
    if args.runtime_command == "favorite":
        return _runtime_favorite(args)
    if args.runtime_command == "remove":
        return _runtime_remove(args)
    raise ValueError(f"Unknown runtime command: {args.runtime_command}")


def _server(args: argparse.Namespace) -> int:
    if not args.server_command:
        args._parser.print_help()
        return 0
    if args.server_command == "start":
        return _server_start(args)
    if args.server_command == "status":
        return _server_status(args.host, args.port)
    if args.server_command == "stop":
        return _server_stop(args.grace_seconds)
    raise ValueError(f"Unknown server command: {args.server_command}")


def _scenario(args: argparse.Namespace) -> int:
    if not args.scenario_command:
        args._parser.print_help()
        return 0
    payload = load_scenario_file(args.file)
    ok, errors = validate_scenario(payload)
    if args.scenario_command == "validate":
        print(
            json.dumps(
                {
                    "file": args.file,
                    "valid": ok,
                    "errors": errors,
                    "scenarioId": payload.get("scenarioId"),
                },
                ensure_ascii=False,
                indent=2,
            )
        )
        return 0 if ok else 1

    reset_payload = scenario_to_reset_config(payload)

    if args.scenario_command == "print-reset":
        print(json.dumps(reset_payload, ensure_ascii=False, indent=2))
        return 0

    if not ok:
        print(json.dumps({"file": args.file, "valid": False, "errors": errors}, ensure_ascii=False, indent=2))
        return 1

    client = SimClient(base_url=args.base_url)
    response = client.reset(reset_payload)
    print(
        json.dumps(
            {
                "baseUrl": args.base_url,
                "scenarioId": payload.get("scenarioId"),
                "selectedTrackId": reset_payload.get("selectedTrackId"),
                "selectedVehicleId": reset_payload.get("selectedVehicleId"),
                "done": response.get("done"),
                "hasFrame": bool(response.get("frame")),
            },
            ensure_ascii=False,
            indent=2,
        )
    )
    return 0


def _step(base_url: str, throttle: float, steer: float, brake: float) -> int:
    client = SimClient(base_url=base_url)
    payload: Dict[str, Any] = {
        "throttle": throttle,
        "steer": steer,
        "brake": brake,
        "timestamp": 0,
        "timeBase": "unix_ms",
        "extensions": [],
    }
    response = client.step(payload)
    print(
        json.dumps(
            {
                "speed": (response.get("state") or {}).get("speed"),
                "reward": response.get("reward"),
                "done": response.get("done"),
                "hasFrame": bool(response.get("frame")),
            },
            ensure_ascii=False,
            indent=2,
        )
    )
    return 0


def _server_start(args: argparse.Namespace) -> int:
    state_path = _state_file()

    existing = _load_state()
    if existing and _pid_alive(int(existing.get("pid", 0))):
        raise RuntimeError(f"Runtime server is already running with pid={existing.get('pid')}")

    runtime_dir = _runtime_dir()
    runtime_dir.mkdir(parents=True, exist_ok=True)
    log_file = runtime_dir / f"unity-{args.mode}.log"
    env = os.environ.copy()
    env["UAVSIM_API_HOST"] = args.host
    env["UAVSIM_API_PORT"] = str(args.port)
    env["RUSIM_START_SCENE"] = args.scene

    if args.runtime_app:
        runtime_app = Path(args.runtime_app).expanduser().resolve()
        if not runtime_app.exists():
            raise FileNotFoundError(f"Runtime app not found: {runtime_app}")
        executable = _resolve_runtime_executable(runtime_app)
        process = _spawn_process(
            cmd=_runtime_launch_command(executable, mode=args.mode, log_file=log_file),
            cwd=runtime_app.parent,
            env=env,
            log_file=log_file,
        )
        launch_kind = "standalone-runtime"
        launch_meta: Dict[str, Any] = {
            "runtimeApp": str(runtime_app),
            "runtimeExecutable": str(executable),
        }
    else:
        unity_bin = Path(args.unity_bin).expanduser()
        project_path = Path(args.project_path).expanduser().resolve()

        if not unity_bin.exists():
            raise FileNotFoundError(f"Unity binary not found: {unity_bin}")
        if not project_path.exists():
            raise FileNotFoundError(f"Unity project path not found: {project_path}")

        process = _spawn_process(
            cmd=_editor_launch_command(unity_bin, project_path, args.scene, log_file, mode=args.mode),
            cwd=project_path,
            env=env,
            log_file=log_file,
        )
        launch_kind = "unity-editor"
        launch_meta = {
            "unityBin": str(unity_bin),
            "projectPath": str(project_path),
        }

    base_url = f"http://{args.host}:{args.port}"
    if not _wait_for_health_or_exit(process, base_url=base_url, timeout_s=args.wait_seconds):
        _terminate_pid(process.pid, grace_seconds=3.0)
        log_tail = _read_log_tail(log_file)
        if "another Unity instance is running with this project open" in log_tail:
            raise RuntimeError(
                "Unity project is already open in another instance. "
                "Close the existing Unity Editor for this project or use that instance for windowed work. "
                f"Log: {log_file}"
            )
        raise RuntimeError(
            f"Unity runtime did not become healthy within {args.wait_seconds:.1f}s. "
            f"Check log: {log_file}\n{log_tail}"
        )

    scenario_id = None
    if args.scenario:
        payload = load_scenario_file(args.scenario)
        ok, errors = validate_scenario(payload)
        if not ok:
            _terminate_pid(process.pid, grace_seconds=3.0)
            raise RuntimeError(f"Scenario validation failed: {errors}")
        SimClient(base_url=base_url).reset(scenario_to_reset_config(payload))
        scenario_id = payload.get("scenarioId")

    state = {
        "pid": process.pid,
        "mode": args.mode,
        "launchKind": launch_kind,
        "host": args.host,
        "port": args.port,
        "baseUrl": base_url,
        "scene": args.scene,
        "logFile": str(log_file),
        "scenarioId": scenario_id,
        "startedAt": int(time.time()),
    }
    state.update(launch_meta)
    state_path.write_text(json.dumps(state, ensure_ascii=False, indent=2), encoding="utf-8")
    print(json.dumps(state, ensure_ascii=False, indent=2))
    return 0


def _server_status(host: str, port: int) -> int:
    state = _load_state() or {}
    pid = int(state.get("pid", 0) or 0)
    base_url = state.get("baseUrl") or f"http://{host}:{port}"
    healthy = False
    error = None
    try:
        SimClient(base_url=base_url, timeout_s=2.0).health()
        healthy = True
    except Exception as exc:  # pragma: no cover - network boundary
        error = str(exc)

    result = {
        "pid": pid or None,
        "processAlive": _pid_alive(pid) if pid else False,
        "baseUrl": base_url,
        "healthy": healthy,
        "mode": state.get("mode"),
        "launchKind": state.get("launchKind"),
        "scene": state.get("scene"),
        "logFile": state.get("logFile"),
        "runtimeApp": state.get("runtimeApp"),
        "error": error,
    }
    print(json.dumps(result, ensure_ascii=False, indent=2))
    return 0 if healthy else 1


def _server_stop(grace_seconds: float) -> int:
    state = _load_state()
    if not state:
        print(json.dumps({"stopped": False, "reason": "state file not found"}, ensure_ascii=False, indent=2))
        return 1

    pid = int(state.get("pid", 0) or 0)
    if pid <= 0:
        _state_file().unlink(missing_ok=True)
        print(json.dumps({"stopped": False, "reason": "invalid pid in state"}, ensure_ascii=False, indent=2))
        return 1

    stopped = _terminate_pid(pid, grace_seconds=grace_seconds)
    _state_file().unlink(missing_ok=True)
    print(json.dumps({"stopped": stopped, "pid": pid}, ensure_ascii=False, indent=2))
    return 0 if stopped else 1


def _runtime_dir() -> Path:
    return (_rusim_home() / "runtime").resolve()


def _state_file() -> Path:
    return _runtime_dir() / "unity-server.json"


def _load_state() -> Dict[str, Any] | None:
    state_path = _state_file()
    if not state_path.exists():
        return None
    return json.loads(state_path.read_text(encoding="utf-8"))


def _pid_alive(pid: int) -> bool:
    if pid <= 0:
        return False
    try:
        os.kill(pid, 0)
        return True
    except OSError:
        return False


def _terminate_pid(pid: int, grace_seconds: float) -> bool:
    if not _pid_alive(pid):
        return True

    os.kill(pid, signal.SIGTERM)
    deadline = time.time() + grace_seconds
    while time.time() < deadline:
        if not _pid_alive(pid):
            return True
        time.sleep(0.25)

    os.kill(pid, signal.SIGKILL)
    deadline = time.time() + 2.0
    while time.time() < deadline:
        if not _pid_alive(pid):
            return True
        time.sleep(0.1)
    return not _pid_alive(pid)


def _wait_for_health_or_exit(process: subprocess.Popen[Any], base_url: str, timeout_s: float) -> bool:
    deadline = time.time() + timeout_s
    while time.time() < deadline:
        if process.poll() is not None:
            return False
        try:
            health = SimClient(base_url=base_url, timeout_s=2.0).health()
            if health.get("status") == "ok":
                return True
        except Exception:
            pass
        time.sleep(1.0)
    return False


def _read_log_tail(path: Path, max_lines: int = 20) -> str:
    try:
        lines = path.read_text(encoding="utf-8", errors="replace").splitlines()
    except OSError:
        return ""
    return "\n".join(lines[-max_lines:])


def _runtime_build(args: argparse.Namespace) -> int:
    unity_bin = Path(args.unity_bin).expanduser()
    project_path = Path(args.project_path).expanduser().resolve()
    build_id = _build_id(args.label)
    output = _resolve_runtime_output(args.output, build_id)

    if not unity_bin.exists():
        raise FileNotFoundError(f"Unity binary not found: {unity_bin}")
    if not project_path.exists():
        raise FileNotFoundError(f"Unity project path not found: {project_path}")

    runtime_dir = _runtime_dir()
    runtime_dir.mkdir(parents=True, exist_ok=True)
    log_file = runtime_dir / "runtime-build.log"

    env = os.environ.copy()
    env["RUSIM_BUILD_OUTPUT"] = str(output)
    env["RUSIM_BUILD_SCENE"] = args.scene

    process = _spawn_process(
        cmd=[
            str(unity_bin),
            "-projectPath",
            str(project_path),
            "-batchmode",
            "-nographics",
            "-executeMethod",
            "UavSimulator.EditorTools.RuntimeBuildPipeline.BuildMacOsRuntime",
            "-quit",
            "-logFile",
            str(log_file),
        ],
        cwd=project_path,
        env=env,
        log_file=log_file,
    )
    try:
        return_code = process.wait(timeout=args.wait_seconds)
    except subprocess.TimeoutExpired:
        _terminate_pid(process.pid, grace_seconds=3.0)
        raise RuntimeError(f"Runtime build timed out after {args.wait_seconds:.1f}s. Log: {log_file}")

    if return_code != 0:
        log_tail = _read_log_tail(log_file, max_lines=40)
        if "another Unity instance is running with this project open" in log_tail:
            raise RuntimeError(
                "Cannot build runtime while the Unity project is already open in another instance. "
                "Close the current Unity Editor and rerun `rusim runtime build`."
            )
        raise RuntimeError(f"Runtime build failed with exit code {return_code}. Log: {log_file}\n{log_tail}")

    executable = _resolve_runtime_executable(output)
    entry = _register_runtime_build(
        build_id=build_id,
        output=output,
        executable=executable,
        scene=args.scene,
        unity_version=_unity_version_from_path(unity_bin),
        source_project_path=project_path,
    )
    result = {
        "buildId": entry["buildId"],
        "versionLabel": entry["versionLabel"],
        "output": str(output),
        "executable": str(executable),
        "logFile": str(log_file),
    }
    print(json.dumps(result, ensure_ascii=False, indent=2))
    return 0


def _runtime_list(args: argparse.Namespace) -> int:
    registry = _load_runtime_registry()
    builds = registry.get("builds") or []
    favorite_id = registry.get("favoriteBuildId")
    latest_id = _latest_build_entry(builds).get("buildId") if builds else None
    items = []
    for entry in _sorted_builds(builds):
        item = {
            "buildId": entry.get("buildId"),
            "versionLabel": entry.get("versionLabel"),
            "createdAt": entry.get("createdAt"),
            "gitSha": entry.get("gitSha"),
            "unityVersion": entry.get("unityVersion"),
            "appPath": entry.get("appPath"),
            "favorite": entry.get("buildId") == favorite_id,
            "latest": entry.get("buildId") == latest_id,
        }
        items.append(item)

    if args.json:
        print(json.dumps({"favoriteBuildId": favorite_id, "items": items}, ensure_ascii=False, indent=2))
        return 0

    lines = []
    if not items:
        lines.append("No runtime builds registered.")
    else:
        for item in items:
            badges = []
            if item["latest"]:
                badges.append("latest")
            if item["favorite"]:
                badges.append("favorite")
            badge_text = f" [{', '.join(badges)}]" if badges else ""
            lines.append(f"{item['buildId']}{badge_text}")
            lines.append(f"  version: {item['versionLabel']}")
            lines.append(f"  app: {item['appPath']}")
            lines.append(f"  unity: {item['unityVersion']}")
    print("\n".join(lines))
    return 0


def _runtime_inspect(args: argparse.Namespace) -> int:
    entry = _resolve_build_selector(args.build)
    print(json.dumps(entry, ensure_ascii=False, indent=2))
    return 0


def _runtime_run(args: argparse.Namespace) -> int:
    entry = _resolve_build_selector(args.build)
    run_args = argparse.Namespace(
        mode=args.mode,
        unity_bin=DEFAULT_UNITY_BIN,
        project_path=DEFAULT_PROJECT_PATH,
        scene=entry.get("scene") or DEFAULT_SCENE_PATH,
        host=args.host,
        port=args.port,
        scenario=args.scenario,
        wait_seconds=args.wait_seconds,
        runtime_app=entry["appPath"],
    )
    return _server_start(run_args)


def _runtime_favorite(args: argparse.Namespace) -> int:
    if not args.runtime_favorite_command:
        args._parser.print_help()
        return 0
    if args.runtime_favorite_command == "set":
        return _runtime_favorite_set(args.build)
    if args.runtime_favorite_command == "show":
        return _runtime_favorite_show()
    raise ValueError(f"Unknown runtime favorite command: {args.runtime_favorite_command}")


def _runtime_remove(args: argparse.Namespace) -> int:
    registry = _load_runtime_registry()
    entry = _resolve_build_selector(args.build, registry=registry)
    build_id = str(entry["buildId"])
    app_path = Path(str(entry.get("appPath") or "")).expanduser()

    state = _load_state()
    stopped_running_build = False
    if state and str(state.get("runtimeApp") or "") == str(app_path):
        _server_stop(args.grace_seconds)
        stopped_running_build = True

    builds = [item for item in registry.get("builds") or [] if item.get("buildId") != build_id]
    registry["builds"] = builds

    favorite_id = registry.get("favoriteBuildId")
    if favorite_id == build_id:
        registry["favoriteBuildId"] = _latest_build_entry(builds).get("buildId") if builds else None

    deleted_files = False
    if not args.keep_files and app_path.exists():
        if app_path.is_dir():
            shutil.rmtree(app_path)
        else:
            app_path.unlink()
        deleted_files = True

    _save_runtime_registry(registry)
    print(
        json.dumps(
            {
                "removedBuildId": build_id,
                "deletedFiles": deleted_files,
                "appPath": str(app_path),
                "stoppedRunningBuild": stopped_running_build,
                "favoriteBuildId": registry.get("favoriteBuildId"),
                "remainingBuilds": len(builds),
            },
            ensure_ascii=False,
            indent=2,
        )
    )
    return 0


def _runtime_favorite_set(build_selector: str) -> int:
    registry = _load_runtime_registry()
    entry = _resolve_build_selector(build_selector, registry=registry)
    registry["favoriteBuildId"] = entry["buildId"]
    _save_runtime_registry(registry)
    print(json.dumps({"favoriteBuildId": entry["buildId"]}, ensure_ascii=False, indent=2))
    return 0


def _runtime_favorite_show() -> int:
    registry = _load_runtime_registry()
    favorite_id = registry.get("favoriteBuildId")
    if not favorite_id:
        print(json.dumps({"favoriteBuildId": None}, ensure_ascii=False, indent=2))
        return 0
    entry = _resolve_build_selector("favorite", registry=registry)
    print(json.dumps(entry, ensure_ascii=False, indent=2))
    return 0


def _editor_launch_command(unity_bin: Path, project_path: Path, scene: str, log_file: Path, mode: str) -> list[str]:
    cmd = [
        str(unity_bin),
        "-projectPath",
        str(project_path),
        "-executeMethod",
        "UavSimulator.EditorTools.RuntimeServerLauncher.StartRuntimeServer",
        "-logFile",
        str(log_file),
    ]
    if mode == "background":
        cmd.append("-batchmode")
    elif mode == "headless":
        cmd.extend(["-batchmode", "-nographics"])
    return cmd


def _runtime_launch_command(executable: Path, mode: str, log_file: Path) -> list[str]:
    cmd = [str(executable), "-logFile", str(log_file)]
    if mode == "background":
        cmd.append("-batchmode")
    elif mode == "headless":
        cmd.extend(["-batchmode", "-nographics"])
    return cmd


def _spawn_process(cmd: list[str], cwd: Path, env: Dict[str, str], log_file: Path) -> subprocess.Popen[Any]:
    with open(log_file, "ab") as log_handle:
        return subprocess.Popen(
            cmd,
            cwd=str(cwd),
            env=env,
            stdout=log_handle,
            stderr=subprocess.STDOUT,
            start_new_session=True,
        )


def _resolve_runtime_executable(runtime_app: Path) -> Path:
    if runtime_app.suffix != ".app":
        raise ValueError(f"Expected macOS app bundle (.app), got: {runtime_app}")
    binary_name = runtime_app.stem
    executable = runtime_app / "Contents" / "MacOS" / binary_name
    if executable.exists():
        return executable
    candidates = list((runtime_app / "Contents" / "MacOS").glob("*"))
    if len(candidates) == 1:
        return candidates[0]
    raise FileNotFoundError(f"Runtime executable not found in app bundle: {runtime_app}")


def _path_contains(path_entry: str, path_env: str) -> bool:
    parts = [part for part in path_env.split(os.pathsep) if part]
    return path_entry in parts


def _rc_contains_line(rc_file: Path, line: str) -> bool:
    if not rc_file.exists():
        return False
    return line in rc_file.read_text(encoding="utf-8")


def _cli_version() -> str:
    try:
        return package_version(CLI_PACKAGE_NAME)
    except PackageNotFoundError:
        return "0.1.0-dev"


def _git_short_sha() -> str:
    try:
        output = subprocess.check_output(
            ["git", "rev-parse", "--short", "HEAD"],
            cwd=str(REPO_ROOT),
            stderr=subprocess.DEVNULL,
            text=True,
        ).strip()
        return output or "nogit"
    except Exception:
        return "nogit"


def _unity_version_from_path(unity_bin: Path) -> str:
    try:
        return unity_bin.parents[3].name
    except Exception:
        return DEFAULT_UNITY_VERSION


def _build_id(label: str) -> str:
    if label.strip():
        return label.strip()
    timestamp = datetime.now().strftime("%Y.%m.%d-%H%M%S")
    return f"{timestamp}+{_git_short_sha()}"


def _resolve_runtime_output(output_arg: str, build_id: str) -> Path:
    if output_arg:
        return Path(output_arg).expanduser().resolve()
    app_name = f"uav-simulator-{build_id}.app"
    return (DEFAULT_RUNTIME_OUTPUT_DIR / app_name).resolve()


def _rusim_home() -> Path:
    raw = os.environ.get("RUSIM_HOME")
    if raw:
        return Path(raw).expanduser().resolve()
    return DEFAULT_RUSIM_HOME


def _runtime_registry_path() -> Path:
    return _rusim_home() / "runtime-builds.json"


def _load_runtime_registry() -> Dict[str, Any]:
    path = _runtime_registry_path()
    if not path.exists():
        return {"schemaVersion": 1, "favoriteBuildId": None, "builds": []}
    return json.loads(path.read_text(encoding="utf-8"))


def _save_runtime_registry(registry: Dict[str, Any]) -> None:
    path = _runtime_registry_path()
    path.parent.mkdir(parents=True, exist_ok=True)
    path.write_text(json.dumps(registry, ensure_ascii=False, indent=2), encoding="utf-8")


def _register_runtime_build(
    *,
    build_id: str,
    output: Path,
    executable: Path,
    scene: str,
    unity_version: str,
    source_project_path: Path,
) -> Dict[str, Any]:
    registry = _load_runtime_registry()
    builds = [entry for entry in registry.get("builds") or [] if entry.get("buildId") != build_id]
    entry = {
        "buildId": build_id,
        "versionLabel": build_id,
        "createdAt": datetime.now().isoformat(timespec="seconds"),
        "gitSha": _git_short_sha(),
        "unityVersion": unity_version,
        "scene": scene,
        "appPath": str(output),
        "executablePath": str(executable),
        "sourceProjectPath": str(source_project_path),
    }
    builds.append(entry)
    registry["builds"] = builds
    if not registry.get("favoriteBuildId"):
        registry["favoriteBuildId"] = build_id
    _save_runtime_registry(registry)
    return entry


def _register_external_runtime_build(
    *,
    build_id: str,
    version_label: str,
    app_path: Path,
    executable_path: Path,
    unity_version: str,
    source: str,
    source_project_path: Path,
    metadata: Dict[str, Any],
) -> Dict[str, Any]:
    registry = _load_runtime_registry()
    builds = [entry for entry in registry.get("builds") or [] if entry.get("buildId") != build_id]
    entry: Dict[str, Any] = {
        "buildId": build_id,
        "versionLabel": version_label,
        "createdAt": datetime.now().isoformat(timespec="seconds"),
        "gitSha": _git_short_sha(),
        "unityVersion": unity_version,
        "scene": DEFAULT_SCENE_PATH,
        "appPath": str(app_path),
        "executablePath": str(executable_path),
        "sourceProjectPath": str(source_project_path),
        "source": source,
    }
    entry.update(metadata)
    builds.append(entry)
    registry["builds"] = builds
    _save_runtime_registry(registry)
    return entry


def _sorted_builds(builds: List[Dict[str, Any]]) -> List[Dict[str, Any]]:
    return sorted(builds, key=lambda item: str(item.get("createdAt") or ""), reverse=True)


def _latest_build_entry(builds: List[Dict[str, Any]]) -> Dict[str, Any]:
    sorted_builds = _sorted_builds(builds)
    if not sorted_builds:
        raise RuntimeError("No runtime builds are registered.")
    return sorted_builds[0]


def _resolve_build_selector(build_selector: str, registry: Dict[str, Any] | None = None) -> Dict[str, Any]:
    active_registry = registry or _load_runtime_registry()
    builds = active_registry.get("builds") or []
    if not builds:
        raise RuntimeError("No runtime builds are registered.")

    if build_selector == "latest":
        entry = _latest_build_entry(builds)
    elif build_selector == "favorite":
        favorite_id = active_registry.get("favoriteBuildId")
        if not favorite_id:
            raise RuntimeError("Favorite runtime build is not set.")
        entry = next((item for item in builds if item.get("buildId") == favorite_id), None)
        if entry is None:
            raise RuntimeError(f"Favorite runtime build '{favorite_id}' is missing from registry.")
    else:
        entry = next((item for item in builds if item.get("buildId") == build_selector), None)
        if entry is None:
            raise RuntimeError(f"Unknown runtime build: '{build_selector}'.")

    app_path = Path(str(entry.get("appPath") or "")).expanduser()
    if not app_path.exists():
        raise RuntimeError(f"Registered runtime app does not exist: {app_path}")
    return entry


def _resolve_release_manifest(*, repo: str, tag: str, manifest_url: str, github_token: str) -> tuple[Dict[str, Any], str, Dict[str, str]]:
    if manifest_url.strip():
        data = _http_get_json(manifest_url.strip(), github_token=github_token)
        return data, manifest_url.strip(), {"tag": tag if tag != "latest" else str(data.get("latestTag") or "")}

    release = _fetch_github_release(repo=repo, tag=tag, github_token=github_token)
    release_tag = str(release.get("tag_name") or "")
    assets = release.get("assets") or []
    manifest_asset = next((item for item in assets if str(item.get("name") or "") == RELEASE_MANIFEST_ASSET_NAME), None)
    if not manifest_asset:
        raise RuntimeError(
            f"Release '{release_tag or tag}' does not contain '{RELEASE_MANIFEST_ASSET_NAME}'. "
            "Run Release Manifest workflow or attach manifest asset manually."
        )

    url = str(manifest_asset.get("browser_download_url") or "").strip()
    if not url:
        raise RuntimeError(f"Manifest asset URL is empty in release '{release_tag or tag}'.")

    data = _http_get_json(url, github_token=github_token)
    return data, url, {"tag": release_tag}


def _fetch_github_release(*, repo: str, tag: str, github_token: str) -> Dict[str, Any]:
    if "/" not in repo:
        raise RuntimeError(f"Invalid repo format '{repo}'. Expected owner/repo.")

    if tag == "latest":
        url = f"https://api.github.com/repos/{repo}/releases/latest"
    else:
        encoded = urllib.parse.quote(tag, safe="")
        url = f"https://api.github.com/repos/{repo}/releases/tags/{encoded}"
    return _http_get_json(url, github_token=github_token)


def _http_get_json(url: str, *, github_token: str) -> Dict[str, Any]:
    data = _http_get_bytes(url, github_token=github_token)
    return json.loads(data.decode("utf-8"))


def _http_get_bytes(url: str, *, github_token: str) -> bytes:
    request = urllib.request.Request(url)
    if "api.github.com" in url:
        request.add_header("Accept", "application/vnd.github+json")
    else:
        request.add_header("Accept", "application/octet-stream")
    if github_token:
        request.add_header("Authorization", f"Bearer {github_token}")

    try:
        with urllib.request.urlopen(request, timeout=60) as response:
            return response.read()
    except urllib.error.HTTPError as exc:
        body = exc.read().decode("utf-8", errors="replace")
        raise RuntimeError(f"HTTP {exc.code} for {url}: {body}") from exc


def _download_file(url: str, output_path: Path, *, github_token: str) -> None:
    output_path.parent.mkdir(parents=True, exist_ok=True)
    payload = _http_get_bytes(url, github_token=github_token)
    output_path.write_bytes(payload)


def _extract_runtime_archive(archive_path: Path, target_dir: Path) -> Path:
    if archive_path.suffix.lower() != ".zip":
        raise RuntimeError(f"Unsupported runtime archive format: {archive_path.name}. Expected .zip")

    temp_extract = target_dir / "_extract_tmp"
    if temp_extract.exists():
        shutil.rmtree(temp_extract)
    temp_extract.mkdir(parents=True, exist_ok=True)

    with zipfile.ZipFile(archive_path, "r") as archive:
        archive.extractall(temp_extract)

    app_candidates = sorted(temp_extract.rglob("*.app"))
    if not app_candidates:
        raise RuntimeError(f"Archive does not contain .app bundle: {archive_path}")

    source_app = app_candidates[0]
    final_app = target_dir / source_app.name
    if final_app.exists():
        shutil.rmtree(final_app)
    shutil.move(str(source_app), str(final_app))
    shutil.rmtree(temp_extract, ignore_errors=True)
    return final_app


def _sha256_file(path: Path) -> str:
    digest = hashlib.sha256()
    with path.open("rb") as handle:
        while True:
            chunk = handle.read(1024 * 1024)
            if not chunk:
                break
            digest.update(chunk)
    return digest.hexdigest()


def _select_manifest_release(manifest: Dict[str, Any], channel: str, preferred_tag: str) -> Dict[str, Any]:
    if int(manifest.get("schemaVersion") or 0) != 1:
        raise RuntimeError(f"Unsupported release manifest schemaVersion: {manifest.get('schemaVersion')}")

    manifest_channel = str(manifest.get("channel") or "")
    if channel and manifest_channel and manifest_channel != channel:
        raise RuntimeError(f"Manifest channel mismatch: expected '{channel}', got '{manifest_channel}'")

    releases = manifest.get("releases") or []
    if not releases:
        raise RuntimeError("Release manifest does not contain releases.")

    if preferred_tag:
        matched = next((item for item in releases if str(item.get("tag") or "") == preferred_tag), None)
        if matched:
            return matched

    latest_tag = str(manifest.get("latestTag") or "")
    if latest_tag:
        matched = next((item for item in releases if str(item.get("tag") or "") == latest_tag), None)
        if matched:
            return matched

    return releases[0]


def _select_runtime_asset(release_entry: Dict[str, Any], platform: str) -> Dict[str, Any]:
    assets = release_entry.get("assets") or []
    if not assets:
        raise RuntimeError("Release entry does not contain assets.")

    exact = next(
        (
            item
            for item in assets
            if str(item.get("kind") or "") == "runtime"
            and str(item.get("platform") or "") == platform
        ),
        None,
    )
    if exact:
        return exact

    fallback = next((item for item in assets if str(item.get("kind") or "") == "runtime"), None)
    if fallback:
        return fallback
    raise RuntimeError("Release entry does not contain runtime assets.")


def _normalize_platform(value: str) -> str:
    lowered = value.strip().lower()
    if lowered in {"mac", "macos", "darwin", "osx"}:
        return "macos"
    if lowered in {"linux", "ubuntu"}:
        return "linux"
    if lowered in {"windows", "win", "win32"}:
        return "windows"
    if not lowered:
        return _detect_runtime_platform()
    return lowered


def _detect_runtime_platform() -> str:
    if sys.platform == "darwin":
        return "macos"
    if sys.platform.startswith("linux"):
        return "linux"
    if sys.platform in {"win32", "cygwin"}:
        return "windows"
    return "unknown"


def _first_track_id(contract: Dict[str, Any]) -> str:
    for item in contract.get("availableTracks") or []:
        if isinstance(item, dict):
            candidate = str(item.get("trackId") or "")
            if candidate:
                return candidate
    return ""


def _first_vehicle_id(contract: Dict[str, Any]) -> str:
    for item in contract.get("availableVehicles") or []:
        if isinstance(item, dict):
            candidate = str(item.get("deviceId") or "")
            if candidate:
                return candidate
    return ""


def _find_track(contract: Dict[str, Any], track_id: str) -> Dict[str, Any]:
    for item in contract.get("availableTracks") or []:
        if isinstance(item, dict) and str(item.get("trackId") or "") == track_id:
            return item
    raise KeyError(f"Unknown track id: '{track_id}'.")


def _find_vehicle(contract: Dict[str, Any], vehicle_id: str) -> Dict[str, Any]:
    for item in contract.get("availableVehicles") or []:
        if isinstance(item, dict) and str(item.get("deviceId") or "") == vehicle_id:
            return item
    raise KeyError(f"Unknown vehicle id: '{vehicle_id}'.")


def _normalize_track_descriptor(item: Any) -> Dict[str, Any]:
    descriptor = dict(item) if isinstance(item, dict) else {}
    track_id = str(descriptor.get("trackId") or "")
    display_name = str(descriptor.get("displayName") or track_id)
    return {
        "id": track_id,
        "displayName": display_name,
        "parametersSchemaJson": descriptor.get("parametersSchemaJson"),
    }


def _normalize_vehicle_descriptor(item: Any, include_contract: bool = False) -> Dict[str, Any]:
    descriptor = dict(item) if isinstance(item, dict) else {}
    sensors = descriptor.get("sensors") or []
    actuators = descriptor.get("actuators") or []
    result = {
        "id": str(descriptor.get("deviceId") or ""),
        "deviceType": str(descriptor.get("deviceType") or ""),
        "sensorCount": len(sensors),
        "actuatorCount": len(actuators),
        "sensorIds": [str(sensor.get("id") or "") for sensor in sensors if isinstance(sensor, dict)],
        "actuatorIds": [str(actuator.get("id") or "") for actuator in actuators if isinstance(actuator, dict)],
    }
    if include_contract:
        result["sensors"] = sensors
        result["actuators"] = actuators
        result["observationSchemaJson"] = descriptor.get("observationSchemaJson")
        result["actionSchemaJson"] = descriptor.get("actionSchemaJson")
    return result


if __name__ == "__main__":
    raise SystemExit(main())
