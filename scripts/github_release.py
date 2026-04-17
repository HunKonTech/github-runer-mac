#!/usr/bin/env python3
"""Create/update GitHub releases and upload release assets."""

from __future__ import annotations

import argparse
import glob
import json
import mimetypes
import os
import sys
import urllib.error
import urllib.parse
import urllib.request
from pathlib import Path
from typing import Any

API_BASE = "https://api.github.com"


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    subparsers = parser.add_subparsers(dest="command", required=True)

    upload_parser = subparsers.add_parser("upload", help="Upload assets to a GitHub release.")
    add_shared_release_args(upload_parser)
    upload_parser.add_argument("patterns", nargs="+", help="Glob patterns for files to upload.")

    notes_parser = subparsers.add_parser("update-notes", help="Update GitHub release metadata.")
    add_shared_release_args(notes_parser)
    notes_parser.add_argument("--notes", required=True, help="Release notes body.")

    args = parser.parse_args()
    token = os.environ.get("GITHUB_TOKEN") or os.environ.get("GH_TOKEN")
    if not token:
        print("GITHUB_TOKEN or GH_TOKEN must be set.", file=sys.stderr)
        return 1

    client = GitHubReleaseClient(token=token, repo=args.repo)

    if args.command == "upload":
        release = client.ensure_release(
            tag=args.tag,
            target=args.target,
            name=args.name,
        )
        files = resolve_patterns(args.patterns)
        if not files:
            print("No release assets matched the provided patterns.", file=sys.stderr)
            return 1
        for file_path in files:
            client.upload_asset(release=release, file_path=file_path)
        return 0

    if args.command == "update-notes":
        release = client.ensure_release(
            tag=args.tag,
            target=args.target,
            name=args.name,
        )
        client.update_release(
            release_id=release["id"],
            name=args.name,
            notes=args.notes,
            prerelease=False,
        )
        return 0

    parser.error(f"Unsupported command: {args.command}")
    return 2


def add_shared_release_args(parser: argparse.ArgumentParser) -> None:
    parser.add_argument("--repo", required=True, help="Repository in OWNER/REPO format.")
    parser.add_argument("--tag", required=True, help="Release tag.")
    parser.add_argument("--target", required=True, help="Target commit SHA.")
    parser.add_argument("--name", required=True, help="Release name.")


def resolve_patterns(patterns: list[str]) -> list[Path]:
    files: list[Path] = []
    for pattern in patterns:
        for match in sorted(glob.glob(pattern)):
            path = Path(match)
            if path.is_file():
                files.append(path)
    deduped: list[Path] = []
    seen: set[Path] = set()
    for file_path in files:
        resolved = file_path.resolve()
        if resolved not in seen:
            deduped.append(file_path)
            seen.add(resolved)
    return deduped


class GitHubReleaseClient:
    def __init__(self, token: str, repo: str) -> None:
        self._repo = repo
        self._headers = {
            "Accept": "application/vnd.github+json",
            "Authorization": f"Bearer {token}",
            "X-GitHub-Api-Version": "2022-11-28",
            "User-Agent": "face-local-release-script",
        }

    def ensure_release(self, tag: str, target: str, name: str) -> dict[str, Any]:
        existing = self.get_release_by_tag(tag)
        if existing is not None:
            return existing

        payload = {
            "tag_name": tag,
            "target_commitish": target,
            "name": name,
            "body": "",
            "draft": False,
            "prerelease": False,
        }
        try:
            return self._request_json(
                "POST",
                f"/repos/{self._repo}/releases",
                payload=payload,
            )
        except urllib.error.HTTPError as exc:
            if exc.code == 422:
                existing = self.get_release_by_tag(tag)
                if existing is not None:
                    return existing
            raise

    def get_release_by_tag(self, tag: str) -> dict[str, Any] | None:
        encoded_tag = urllib.parse.quote(tag, safe="")
        try:
            return self._request_json(
                "GET",
                f"/repos/{self._repo}/releases/tags/{encoded_tag}",
            )
        except urllib.error.HTTPError as exc:
            if exc.code == 404:
                return None
            raise

    def update_release(self, release_id: int, name: str, notes: str, prerelease: bool) -> dict[str, Any]:
        payload = {
            "name": name,
            "body": notes,
            "prerelease": prerelease,
            "draft": False,
        }
        return self._request_json(
            "PATCH",
            f"/repos/{self._repo}/releases/{release_id}",
            payload=payload,
        )

    def upload_asset(self, release: dict[str, Any], file_path: Path) -> None:
        asset_name = file_path.name
        for asset in release.get("assets", []):
            if asset.get("name") == asset_name:
                self._request_json(
                    "DELETE",
                    f"/repos/{self._repo}/releases/assets/{asset['id']}",
                    allow_empty=True,
                )
                break

        upload_url = release["upload_url"].split("{", 1)[0]
        content_type = mimetypes.guess_type(asset_name)[0] or "application/octet-stream"
        with file_path.open("rb") as handle:
            data = handle.read()

        request = urllib.request.Request(
            url=f"{upload_url}?name={urllib.parse.quote(asset_name)}",
            data=data,
            method="POST",
            headers={
                **self._headers,
                "Content-Type": content_type,
                "Content-Length": str(len(data)),
            },
        )
        with urllib.request.urlopen(request) as response:
            response.read()

    def _request_json(
        self,
        method: str,
        path: str,
        payload: dict[str, Any] | None = None,
        allow_empty: bool = False,
    ) -> dict[str, Any]:
        data = None
        headers = dict(self._headers)
        if payload is not None:
            data = json.dumps(payload).encode("utf-8")
            headers["Content-Type"] = "application/json"

        request = urllib.request.Request(
            url=f"{API_BASE}{path}",
            data=data,
            method=method,
            headers=headers,
        )
        with urllib.request.urlopen(request) as response:
            raw = response.read()
        if not raw:
            if allow_empty:
                return {}
            return {}
        return json.loads(raw.decode("utf-8"))


if __name__ == "__main__":
    raise SystemExit(main())
