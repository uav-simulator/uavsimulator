#!/usr/bin/env python3
from __future__ import annotations

import argparse
import hashlib
import json
import mimetypes
import os
import re
import sys
import urllib.error
import urllib.parse
import urllib.request
from datetime import datetime, timezone
from pathlib import Path
from typing import Any


MANIFEST_SCHEMA_VERSION = 1
DEFAULT_CHANNEL = "stable"
DEFAULT_MANIFEST_NAME = "rusim-release-manifest.json"


def build_parser() -> argparse.ArgumentParser:
    parser = argparse.ArgumentParser(description="Generate rusim release manifest JSON.")
    subparsers = parser.add_subparsers(dest="command", required=True)

    local = subparsers.add_parser("local", help="Generate manifest from local files.")
    local.add_argument("--version", required=True)
    local.add_argument("--tag", required=True)
    local.add_argument("--asset", action="append", required=True, help="Path to local release asset. Repeat for multiple assets.")
    local.add_argument("--release-url", default="")
    local.add_argument("--output", required=True)
    local.add_argument("--channel", default=DEFAULT_CHANNEL)
    local.add_argument("--base-download-url", default="", help="Optional base URL for generated browser download URLs.")

    github_release = subparsers.add_parser("github-release", help="Generate manifest from GitHub Release assets.")
    github_release.add_argument("--repo", required=True, help="owner/repo")
    github_release.add_argument("--tag", required=True)
    github_release.add_argument("--github-token", default=os.environ.get("GITHUB_TOKEN", ""))
    github_release.add_argument("--output", required=True)
    github_release.add_argument("--channel", default=DEFAULT_CHANNEL)

    return parser


def main(argv: list[str] | None = None) -> int:
    parser = build_parser()
    args = parser.parse_args(argv)

    if args.command == "local":
        manifest = generate_local_manifest(
            version=args.version,
            tag=args.tag,
            asset_paths=[Path(item).expanduser().resolve() for item in args.asset],
            release_url=args.release_url,
            output_path=Path(args.output).expanduser().resolve(),
            channel=args.channel,
            base_download_url=args.base_download_url,
        )
    elif args.command == "github-release":
        manifest = generate_github_release_manifest(
            repo=args.repo,
            tag=args.tag,
            github_token=args.github_token,
            output_path=Path(args.output).expanduser().resolve(),
            channel=args.channel,
        )
    else:
        parser.error(f"Unknown command: {args.command}")
        return 2

    print(json.dumps(manifest, ensure_ascii=False, indent=2))
    return 0


def generate_local_manifest(
    *,
    version: str,
    tag: str,
    asset_paths: list[Path],
    release_url: str,
    output_path: Path,
    channel: str,
    base_download_url: str,
) -> dict[str, Any]:
    assets = [_build_local_asset_entry(path, base_download_url) for path in asset_paths]
    manifest = _build_manifest(
        repo=os.environ.get("GITHUB_REPOSITORY", ""),
        version=version,
        tag=tag,
        channel=channel,
        release_url=release_url,
        assets=assets,
        published_at=_utc_now_iso(),
    )
    _write_manifest(output_path, manifest)
    return manifest


def generate_github_release_manifest(
    *,
    repo: str,
    tag: str,
    github_token: str,
    output_path: Path,
    channel: str,
) -> dict[str, Any]:
    release = _fetch_release_by_tag(repo=repo, tag=tag, github_token=github_token)
    assets = _build_release_assets(release.get("assets") or [], github_token=github_token)
    manifest = _build_manifest(
        repo=repo,
        version=_normalize_version_from_tag(str(release.get("tag_name") or tag)),
        tag=str(release.get("tag_name") or tag),
        channel=channel,
        release_url=str(release.get("html_url") or ""),
        assets=assets,
        published_at=str(release.get("published_at") or _utc_now_iso()),
    )
    _write_manifest(output_path, manifest)
    return manifest


def _build_manifest(
    *,
    repo: str,
    version: str,
    tag: str,
    channel: str,
    release_url: str,
    assets: list[dict[str, Any]],
    published_at: str,
) -> dict[str, Any]:
    return {
        "schemaVersion": MANIFEST_SCHEMA_VERSION,
        "channel": channel,
        "generatedAt": _utc_now_iso(),
        "repo": repo,
        "latestVersion": version,
        "latestTag": tag,
        "releases": [
            {
                "version": version,
                "tag": tag,
                "publishedAt": published_at,
                "releaseUrl": release_url,
                "manifestAssetName": DEFAULT_MANIFEST_NAME,
                "assets": assets,
            }
        ],
    }


def _build_local_asset_entry(path: Path, base_download_url: str) -> dict[str, Any]:
    if not path.exists():
        raise FileNotFoundError(f"Asset not found: {path}")

    name = path.name
    sha256 = _sha256_file(path)
    browser_download_url = ""
    if base_download_url:
        browser_download_url = urllib.parse.urljoin(base_download_url.rstrip("/") + "/", urllib.parse.quote(name))

    return {
        "name": name,
        "kind": _infer_asset_kind(name),
        "platform": _infer_platform(name),
        "contentType": mimetypes.guess_type(name)[0] or "application/octet-stream",
        "sizeBytes": path.stat().st_size,
        "apiUrl": "",
        "browserDownloadUrl": browser_download_url,
        "sha256": sha256,
        "sha256Source": "inline",
    }


def _build_release_assets(release_assets: list[dict[str, Any]], *, github_token: str) -> list[dict[str, Any]]:
    checksum_by_target: dict[str, str] = {}
    for asset in release_assets:
        name = str(asset.get("name") or "")
        if name.endswith(".sha256"):
            target_name = name[: -len(".sha256")]
            checksum = _try_extract_checksum_from_asset(asset, github_token=github_token)
            if checksum:
                checksum_by_target[target_name] = checksum

    assets: list[dict[str, Any]] = []
    for asset in release_assets:
        name = str(asset.get("name") or "")
        if not name or name.endswith(".sha256") or name == DEFAULT_MANIFEST_NAME:
            continue

        assets.append(
            {
                "name": name,
                "kind": _infer_asset_kind(name),
                "platform": _infer_platform(name),
                "contentType": str(asset.get("content_type") or "application/octet-stream"),
                "sizeBytes": int(asset.get("size") or 0),
                "apiUrl": str(asset.get("url") or ""),
                "browserDownloadUrl": str(asset.get("browser_download_url") or ""),
                "sha256": checksum_by_target.get(name),
                "sha256Source": "release-asset" if checksum_by_target.get(name) else None,
            }
        )
    return assets


def _infer_asset_kind(name: str) -> str:
    lowered = name.lower()
    if "manifest" in lowered:
        return "manifest"
    if lowered.endswith(".sha256"):
        return "checksum"
    if lowered.endswith(".zip") or lowered.endswith(".tar.gz") or lowered.endswith(".tgz"):
        return "runtime"
    return "unknown"


def _infer_platform(name: str) -> str:
    lowered = name.lower()
    if "macos" in lowered or "darwin" in lowered or lowered.endswith(".app.zip"):
        return "macos"
    if "linux" in lowered:
        return "linux"
    if "windows" in lowered or lowered.endswith(".zip") and "win" in lowered:
        return "windows"
    return "unknown"


def _fetch_release_by_tag(*, repo: str, tag: str, github_token: str) -> dict[str, Any]:
    encoded_tag = urllib.parse.quote(tag, safe="")
    url = f"https://api.github.com/repos/{repo}/releases/tags/{encoded_tag}"
    request = urllib.request.Request(url)
    request.add_header("Accept", "application/vnd.github+json")
    if github_token:
        request.add_header("Authorization", f"Bearer {github_token}")

    try:
        with urllib.request.urlopen(request, timeout=30) as response:
            return json.loads(response.read().decode("utf-8"))
    except urllib.error.HTTPError as exc:
        body = exc.read().decode("utf-8", errors="replace")
        raise RuntimeError(f"GitHub API error {exc.code}: {body}") from exc


def _try_extract_checksum_from_asset(asset: dict[str, Any], *, github_token: str) -> str | None:
    urls_to_try: list[tuple[str, bool]] = []
    api_url = str(asset.get("url") or "")
    browser_download_url = str(asset.get("browser_download_url") or "")
    if api_url:
        urls_to_try.append((api_url, True))
    if browser_download_url:
        urls_to_try.append((browser_download_url, False))

    content: str | None = None
    for url, use_api_headers in urls_to_try:
        request = urllib.request.Request(url)
        request.add_header("Accept", "application/octet-stream")
        if use_api_headers:
            request.add_header("X-GitHub-Api-Version", "2022-11-28")
        if github_token:
            request.add_header("Authorization", f"Bearer {github_token}")
        try:
            with urllib.request.urlopen(request, timeout=30) as response:
                content = response.read().decode("utf-8", errors="replace").strip()
                break
        except Exception:
            continue

    if not content:
        return None

    match = re.search(r"\b([a-fA-F0-9]{64})\b", content)
    return match.group(1).lower() if match else None


def _sha256_file(path: Path) -> str:
    digest = hashlib.sha256()
    with path.open("rb") as handle:
        while True:
            chunk = handle.read(1024 * 1024)
            if not chunk:
                break
            digest.update(chunk)
    return digest.hexdigest()


def _normalize_version_from_tag(tag: str) -> str:
    return tag[1:] if tag.startswith("v") else tag


def _write_manifest(output_path: Path, manifest: dict[str, Any]) -> None:
    output_path.parent.mkdir(parents=True, exist_ok=True)
    output_path.write_text(json.dumps(manifest, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")


def _utc_now_iso() -> str:
    return datetime.now(timezone.utc).replace(microsecond=0).isoformat().replace("+00:00", "Z")


if __name__ == "__main__":
    raise SystemExit(main())
