from __future__ import annotations

from dataclasses import dataclass
from pathlib import Path
from typing import Any, Dict, Optional

import requests


@dataclass(frozen=True)
class SimClient:
    base_url: str
    timeout_s: float = 10.0

    def health(self) -> Dict[str, Any]:
        return self._get("/health")

    def get_contract(self) -> Dict[str, Any]:
        return self._get("/contract")

    def reset(self, config: Dict[str, Any]) -> Dict[str, Any]:
        return self._post("/reset", config)

    def step(self, command: Dict[str, Any]) -> Dict[str, Any]:
        return self._post("/step", command)

    def list_models(self) -> Dict[str, Any] | list[Dict[str, Any]]:
        return self._get_any("/api/models")

    def get_active_model(self) -> Optional[Dict[str, Any]]:
        url = f"{self.base_url}/api/models/active"
        r = requests.get(url, timeout=self.timeout_s)
        if r.status_code == 404:
            return None
        self._raise_for_status(r)
        return r.json()

    def activate_model(self, model_id: str) -> Dict[str, Any]:
        return self._post("/api/models/activate", {"modelId": model_id})

    def upload_model(
        self,
        artifact_path: Path,
        *,
        name: str = "",
        version: str = "",
        source: str = "",
        metadata_json: str = "",
        metrics_json: str = "",
    ) -> Dict[str, Any]:
        url = f"{self.base_url}/api/models/upload"
        data: Dict[str, str] = {}
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
            r = requests.post(url, data=data, files=files, timeout=self.timeout_s)
        self._raise_for_status(r)
        return r.json()

    def _get(self, path: str) -> Dict[str, Any]:
        url = f"{self.base_url}{path}"
        r = requests.get(url, timeout=self.timeout_s)
        self._raise_for_status(r)
        return r.json()

    def _get_any(self, path: str) -> Dict[str, Any] | list[Dict[str, Any]]:
        url = f"{self.base_url}{path}"
        r = requests.get(url, timeout=self.timeout_s)
        self._raise_for_status(r)
        return r.json()

    def _post(self, path: str, payload: Dict[str, Any]) -> Dict[str, Any]:
        url = f"{self.base_url}{path}"
        r = requests.post(url, json=payload, timeout=self.timeout_s)
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
