from __future__ import annotations

import time
from pathlib import Path
from typing import Any

import requests
from requests.adapters import HTTPAdapter
from urllib3.util.retry import Retry

# Major version of the runtime API contract this client is built against.
# Bump when a breaking change in `/contract`, `/reset`, or `/step` lands on
# the Unity side. The client will refuse to talk to a server whose
# `contractVersion` is on a different major track.
SUPPORTED_CONTRACT_VERSION_MAJOR = 0


class ContractMismatchError(RuntimeError):
    """Raised when the connected simulator advertises an API contract this
    client does not know how to talk to (e.g. major version drift)."""


class SimClient:
    """HTTP client for Unity simulator runtime API.

    Uses a persistent ``requests.Session()`` for keep-alive connection pooling.
    Critical on Windows: per-request connections quickly exhaust the TCP
    ephemeral port pool (TIME_WAIT) under high training fps, triggering
    ``WinError 10055`` / ``EOFError`` in subprocess workers.

    Retry semantics:
      * GET/HEAD requests retry up to ``retries`` times on transient
        connection errors and HTTP 502/503/504 with exponential backoff.
        Used to bridge short Unity GC pauses without killing a training run.
      * POST requests (``/reset``, ``/step``, control commands) are
        intentionally NOT retried after the request leaves the wire — the
        simulator is not idempotent on side-effecting calls.
    """

    def __init__(
        self,
        base_url: str,
        timeout_s: float = 10.0,
        *,
        retries: int = 3,
        backoff_factor: float = 0.3,
    ):
        self.base_url = base_url
        self.timeout_s = timeout_s
        self.session = requests.Session()
        # 1 host (the Unity instance) — keep up to 16 idle connections,
        # match expected env worker concurrency.
        retry_policy = Retry(
            total=retries,
            connect=retries,
            read=retries,
            status=retries,
            backoff_factor=backoff_factor,
            status_forcelist=(502, 503, 504),
            # urllib3's default already excludes POST/PUT/DELETE, but we list
            # the safe methods explicitly to make the intent unmistakable.
            allowed_methods=frozenset({"GET", "HEAD", "OPTIONS"}),
            raise_on_status=False,
        )
        adapter = HTTPAdapter(
            pool_connections=4,
            pool_maxsize=16,
            max_retries=retry_policy,
        )
        self.session.mount("http://", adapter)
        self.session.mount("https://", adapter)

    def health(self) -> dict[str, Any]:
        return self._get("/health")

    def get_contract(self) -> dict[str, Any]:
        return self._get("/contract")

    def wait_for_ready(self, deadline_s: float = 30.0, poll_interval_s: float = 0.5) -> dict[str, Any]:
        """Poll ``/health`` until the simulator answers 200, or raise.

        Useful at the start of a training run where Unity may still be
        booting (scene load, JIT, plugin registry merge). Independent of
        the GET-retry policy attached to the adapter — that one fires per
        request, this one wraps the boot itself.

        Returns the final ``/health`` payload on success. Raises
        :class:`TimeoutError` if the deadline expires.
        """
        start = time.monotonic()
        last_exc: Exception | None = None
        while True:
            try:
                return self.health()
            except (requests.ConnectionError, requests.Timeout, requests.HTTPError) as exc:
                last_exc = exc
            if time.monotonic() - start > deadline_s:
                raise TimeoutError(
                    f"Simulator at {self.base_url} not ready within {deadline_s:.1f}s "
                    f"(last error: {last_exc!r})"
                ) from last_exc
            time.sleep(poll_interval_s)

    def check_contract_version(self, expected_major: int = SUPPORTED_CONTRACT_VERSION_MAJOR) -> tuple[bool, str]:
        """Fetch ``/contract`` and verify the server's major version matches ours.

        Returns ``(True, server_version_string)`` on match, otherwise
        ``(False, human_readable_message)``. Does NOT raise — callers
        decide whether a mismatch is fatal (training pipelines should
        usually fail fast; CLI tools may want to warn and proceed).
        Use :meth:`assert_contract_compatible` for the raise-on-mismatch
        variant.
        """
        contract = self.get_contract()
        raw_version = contract.get("contractVersion")
        if not isinstance(raw_version, str) or not raw_version.strip():
            return False, f"server did not advertise contractVersion (got {raw_version!r})"

        # Accept both "MAJOR.MINOR.PATCH" and bare "MAJOR".
        major_str = raw_version.split(".", 1)[0].strip()
        try:
            server_major = int(major_str)
        except ValueError:
            return False, f"unparseable contractVersion: {raw_version!r}"

        if server_major != expected_major:
            return (
                False,
                f"contractVersion major mismatch: client expects {expected_major}.x, "
                f"server reports {raw_version!r}. Update the client (`pip install -U uav-sim-client`) "
                f"or roll the simulator back.",
            )
        return True, raw_version

    def assert_contract_compatible(self, expected_major: int = SUPPORTED_CONTRACT_VERSION_MAJOR) -> str:
        """Raise :class:`ContractMismatchError` if :meth:`check_contract_version` fails."""
        ok, info = self.check_contract_version(expected_major=expected_major)
        if not ok:
            raise ContractMismatchError(info)
        return info

    def reset(self, config: dict[str, Any]) -> dict[str, Any]:
        return self._post("/reset", config)

    def step(self, command: dict[str, Any]) -> dict[str, Any]:
        return self._post("/step", command)

    def list_models(self) -> dict[str, Any] | list[dict[str, Any]]:
        return self._get_any("/api/models")

    def get_active_model(self) -> dict[str, Any] | None:
        url = f"{self.base_url}/api/models/active"
        r = self.session.get(url, timeout=self.timeout_s)
        if r.status_code == 404:
            return None
        self._raise_for_status(r)
        return r.json()

    def get_model_catalog(self) -> list[dict[str, Any]]:
        return self._get_any("/api/model-catalog")  # type: ignore[return-value]

    def activate_model(self, model_id: str) -> dict[str, Any]:
        return self._post("/api/models/activate", {"modelId": model_id})

    def get_model_binding(
        self,
        client_id: str,
        runtime_mode: str,
        agent_id: str = "",
    ) -> dict[str, Any] | None:
        params = {
            "clientId": client_id,
            "runtimeMode": runtime_mode,
        }
        if agent_id.strip():
            params["agentId"] = agent_id.strip()

        url = f"{self.base_url}/api/model-bindings/current"
        r = self.session.get(url, params=params, timeout=self.timeout_s)
        if r.status_code == 404:
            return None
        self._raise_for_status(r)
        return r.json()

    def set_model_binding(
        self,
        client_id: str,
        runtime_mode: str,
        model_id: str,
        agent_id: str = "",
    ) -> dict[str, Any]:
        payload: dict[str, Any] = {
            "clientId": client_id,
            "runtimeMode": runtime_mode,
            "modelId": model_id,
        }
        if agent_id.strip():
            payload["agentId"] = agent_id.strip()
        return self._post("/api/model-bindings", payload)

    def upload_model(
        self,
        artifact_path: Path,
        *,
        name: str = "",
        version: str = "",
        source: str = "",
        metadata_json: str = "",
        metrics_json: str = "",
    ) -> dict[str, Any]:
        url = f"{self.base_url}/api/models/upload"
        data: dict[str, str] = {}
        if name:
            data["name"] = name
        if version:
            data["version"] = version
        if source:
            data["source"] = source
        if metadata_json:
            data["metadata"] = metadata_json
        if metrics_json:
            data["metrics"] = metrics_json
        with artifact_path.open("rb") as handle:
            files = {
                "file": (
                    artifact_path.name,
                    handle,
                    "application/octet-stream",
                )
            }
            r = self.session.post(url, data=data, files=files, timeout=self.timeout_s)
        self._raise_for_status(r)
        return r.json()

    def _get(self, path: str) -> dict[str, Any]:
        url = f"{self.base_url}{path}"
        r = self.session.get(url, timeout=self.timeout_s)
        self._raise_for_status(r)
        return r.json()

    def _get_any(self, path: str) -> dict[str, Any] | list[dict[str, Any]]:
        url = f"{self.base_url}{path}"
        r = self.session.get(url, timeout=self.timeout_s)
        self._raise_for_status(r)
        return r.json()

    def _post(self, path: str, payload: dict[str, Any]) -> dict[str, Any]:
        url = f"{self.base_url}{path}"
        r = self.session.post(url, json=payload, timeout=self.timeout_s)
        self._raise_for_status(r)
        return r.json()

    def _raise_for_status(self, response: requests.Response) -> None:
        try:
            response.raise_for_status()
        except requests.HTTPError as exc:
            try:
                payload = response.json()
            except ValueError:
                payload = None
            if isinstance(payload, dict) and payload.get("error"):
                raise requests.HTTPError(str(payload["error"]), response=response) from exc
            raise
