from __future__ import annotations

from dataclasses import dataclass
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

    def _get(self, path: str) -> Dict[str, Any]:
        url = f"{self.base_url}{path}"
        r = requests.get(url, timeout=self.timeout_s)
        r.raise_for_status()
        return r.json()

    def _post(self, path: str, payload: Dict[str, Any]) -> Dict[str, Any]:
        url = f"{self.base_url}{path}"
        r = requests.post(url, json=payload, timeout=self.timeout_s)
        r.raise_for_status()
        return r.json()

