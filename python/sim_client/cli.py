from __future__ import annotations

import argparse
import json
import os
import signal
import subprocess
import sys
import time
from pathlib import Path
from typing import Any, Dict

from .contract import validate_contract
from .http_client import SimClient
from .scenario import load_scenario_file, scenario_to_reset_config, validate_scenario


DEFAULT_UNITY_VERSION = "6000.1.8f1"
DEFAULT_UNITY_BIN = f"/Applications/Unity/Hub/Editor/{DEFAULT_UNITY_VERSION}/Unity.app/Contents/MacOS/Unity"
DEFAULT_PROJECT_PATH = "src/UnityProject/uav-simulator"
DEFAULT_SCENE_PATH = "Assets/Scenes/TrackScence.unity"
DEFAULT_RUNTIME_APP = "build/runtime/macos/uav-simulator.app"


def build_parser() -> argparse.ArgumentParser:
    parser = argparse.ArgumentParser(description="CLI for uav-simulator operator/runtime flows.")
    subparsers = parser.add_subparsers(dest="command", required=True)

    doctor = subparsers.add_parser("doctor", help="Check simulator health and contract.")
    doctor.add_argument("--base-url", default="http://127.0.0.1:8000")

    contract = subparsers.add_parser("contract", help="Print simulator contract.")
    contract.add_argument("--base-url", default="http://127.0.0.1:8000")

    runtime = subparsers.add_parser("runtime", help="Build and inspect standalone runtime.")
    runtime_sub = runtime.add_subparsers(dest="runtime_command", required=True)

    build = runtime_sub.add_parser("build", help="Build standalone macOS runtime app.")
    build.add_argument("--unity-bin", default=os.environ.get("UNITY_BIN", DEFAULT_UNITY_BIN))
    build.add_argument("--project-path", default=DEFAULT_PROJECT_PATH)
    build.add_argument("--scene", default=DEFAULT_SCENE_PATH)
    build.add_argument("--output", default=DEFAULT_RUNTIME_APP)
    build.add_argument("--wait-seconds", type=float, default=900.0)

    server = subparsers.add_parser("server", help="Manage Unity runtime process.")
    server_sub = server.add_subparsers(dest="server_command", required=True)

    start = server_sub.add_parser("start", help="Start Unity runtime server.")
    start.add_argument("--mode", choices=["windowed", "headless"], default="windowed")
    start.add_argument("--unity-bin", default=os.environ.get("UNITY_BIN", DEFAULT_UNITY_BIN))
    start.add_argument("--project-path", default=DEFAULT_PROJECT_PATH)
    start.add_argument("--scene", default=DEFAULT_SCENE_PATH)
    start.add_argument("--host", default="127.0.0.1")
    start.add_argument("--port", type=int, default=8000)
    start.add_argument("--scenario")
    start.add_argument("--wait-seconds", type=float, default=45.0)
    start.add_argument("--runtime-app", default="")

    status = server_sub.add_parser("status", help="Show Unity runtime server status.")
    status.add_argument("--host", default="127.0.0.1")
    status.add_argument("--port", type=int, default=8000)

    stop = server_sub.add_parser("stop", help="Stop Unity runtime server.")
    stop.add_argument("--grace-seconds", type=float, default=8.0)

    scenario = subparsers.add_parser("scenario", help="Scenario file operations.")
    scenario_sub = scenario.add_subparsers(dest="scenario_command", required=True)

    validate_cmd = scenario_sub.add_parser("validate", help="Validate scenario file.")
    validate_cmd.add_argument("file")

    reset_cmd = scenario_sub.add_parser("reset", help="Reset simulator from scenario file.")
    reset_cmd.add_argument("file")
    reset_cmd.add_argument("--base-url", default="http://127.0.0.1:8000")

    print_reset_cmd = scenario_sub.add_parser("print-reset", help="Print reset payload derived from scenario.")
    print_reset_cmd.add_argument("file")

    step = subparsers.add_parser("step", help="Send a single control step.")
    step.add_argument("--base-url", default="http://127.0.0.1:8000")
    step.add_argument("--throttle", type=float, default=0.0)
    step.add_argument("--steer", type=float, default=0.0)
    step.add_argument("--brake", type=float, default=0.0)

    return parser


def main(argv: list[str] | None = None) -> int:
    parser = build_parser()
    args = parser.parse_args(argv)

    try:
        if args.command == "doctor":
            return _doctor(args.base_url)
        if args.command == "contract":
            return _contract(args.base_url)
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

    parser.error("Unknown command")
    return 2


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


def _contract(base_url: str) -> int:
    client = SimClient(base_url=base_url)
    print(json.dumps(client.get_contract(), ensure_ascii=False, indent=2))
    return 0


def _runtime(args: argparse.Namespace) -> int:
    if args.runtime_command == "build":
        return _runtime_build(args)
    raise ValueError(f"Unknown runtime command: {args.runtime_command}")


def _server(args: argparse.Namespace) -> int:
    if args.server_command == "start":
        return _server_start(args)
    if args.server_command == "status":
        return _server_status(args.host, args.port)
    if args.server_command == "stop":
        return _server_stop(args.grace_seconds)
    raise ValueError(f"Unknown server command: {args.server_command}")


def _scenario(args: argparse.Namespace) -> int:
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
    return Path("tmp/rusim-runtime").resolve()


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
    output = Path(args.output).expanduser().resolve()

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
    result = {
        "output": str(output),
        "executable": str(executable),
        "logFile": str(log_file),
    }
    print(json.dumps(result, ensure_ascii=False, indent=2))
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
    if mode == "headless":
        cmd.extend(["-batchmode", "-nographics"])
    return cmd


def _runtime_launch_command(executable: Path, mode: str, log_file: Path) -> list[str]:
    cmd = [str(executable), "-logFile", str(log_file)]
    if mode == "headless":
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


if __name__ == "__main__":
    raise SystemExit(main())
