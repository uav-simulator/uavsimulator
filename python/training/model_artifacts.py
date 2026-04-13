from __future__ import annotations

import json
import re
from pathlib import Path
from typing import Any, Iterable


def slugify_model_name(name: str) -> str:
    slug = re.sub(r"[^a-zA-Z0-9._-]+", "-", name.strip().lower()).strip("-")
    return slug or "model"


def normalize_csv(raw: str) -> list[str]:
    return [item.strip() for item in raw.split(",") if item.strip()]


def normalize_values(values: Iterable[str]) -> list[str]:
    seen: set[str] = set()
    normalized: list[str] = []
    for value in values:
        item = value.strip()
        if not item:
            continue
        key = item.lower()
        if key in seen:
            continue
        seen.add(key)
        normalized.append(item)
    return normalized


def default_artifact_dir(root: Path, model_name: str, model_version: str) -> Path:
    return root / "python" / "training" / "artifacts" / slugify_model_name(model_name) / model_version.strip()


def resolve_artifact_dir(root: Path, output_dir: str, model_name: str, model_version: str) -> Path:
    if output_dir.strip():
        return Path(output_dir).expanduser().resolve()
    return default_artifact_dir(root, model_name, model_version)


def default_onnx_file_name(model_name: str) -> str:
    return f"{slugify_model_name(model_name)}.onnx"


def default_sb3_stem(model_name: str) -> str:
    return f"{slugify_model_name(model_name)}_sb3"


def build_compatibility(
    runtime_modes: Iterable[str] = (),
    vehicle_ids: Iterable[str] = (),
    robot_kinds: Iterable[str] = (),
) -> dict[str, list[str]]:
    return {
        "runtimeModes": normalize_values(runtime_modes),
        "vehicleIds": normalize_values(vehicle_ids),
        "robotKinds": normalize_values(robot_kinds),
    }


def build_model_metadata(
    *,
    model_name: str,
    model_version: str,
    model_source: str,
    policy_format: str,
    observation_schema: dict[str, Any],
    action_schema: dict[str, Any],
    compatibility: dict[str, list[str]],
    artifact_file_name: str | None = None,
    extra: dict[str, Any] | None = None,
) -> dict[str, Any]:
    metadata: dict[str, Any] = {
        "name": model_name,
        "version": model_version,
        "source": model_source,
        "policyId": f"{slugify_model_name(model_name)}:{model_version}",
        "format": policy_format,
        "observationSchema": observation_schema,
        "actionSchema": action_schema,
        "compatibility": compatibility,
    }
    if artifact_file_name:
        metadata["artifactFileName"] = artifact_file_name
    if extra:
        metadata.update(extra)
    return metadata


def write_json(path: Path, payload: dict[str, Any]) -> None:
    path.parent.mkdir(parents=True, exist_ok=True)
    path.write_text(json.dumps(payload, indent=2, ensure_ascii=False) + "\n", encoding="utf-8")
