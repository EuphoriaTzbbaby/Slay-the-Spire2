"""Build the Windows zip and publish it as the rolling `latest` GitHub release."""
from __future__ import annotations

import json
import os
import subprocess
import sys
import urllib.error
import urllib.request
from pathlib import Path

ROOT = Path(__file__).resolve().parent.parent
ZIP = ROOT / "dist/SpireCompanion-win64.zip"
REPO = "EuphoriaTzbbaby/Slay-the-Spire2"
TAG = "latest"
ASSET = ZIP.name


def token() -> str:
    value = os.environ.get("GITHUB_TOKEN", "").strip()
    if value:
        return value
    if sys.platform == "darwin":
        value = subprocess.check_output(["security", "find-internet-password", "-s", "github.com", "-w"], text=True).strip()
        if value:
            return value
    raise SystemExit("Set GITHUB_TOKEN or store a github.com token in the macOS keychain.")


def curl(args: list[str]) -> tuple[int, str]:
    result = subprocess.run(["curl", "-sS", "--max-time", "180", "-w", "\n%{http_code}", *args], capture_output=True, text=True)
    body, _, code = result.stdout.rpartition("\n")
    return int(code or 0), body


def call(url: str, auth: str, method: str = "GET", body: dict | None = None) -> tuple[int, dict]:
    args = ["-X", method, "-H", f"Authorization: Bearer {auth}", "-H", "Accept: application/vnd.github+json", url]
    if body is not None:
        args += ["-H", "Content-Type: application/json", "-d", json.dumps(body)]
    code, text = curl(args)
    try:
        return code, json.loads(text or "{}")
    except json.JSONDecodeError:
        return code, {"raw": text[:300]}


def main() -> int:
    subprocess.run([sys.executable, str(ROOT / "scripts/package_release.py")], check=True)
    if not ZIP.is_file():
        raise SystemExit(f"Missing {ZIP}")
    auth = token()
    status, release = call(f"https://api.github.com/repos/{REPO}/releases/tags/{TAG}", auth)
    if status == 404:
        status, release = call(f"https://api.github.com/repos/{REPO}/releases", auth, "POST", {
            "tag_name": TAG,
            "name": "Windows 最新构建",
            "body": "每次更新自动覆盖此 release 的 zip；队友下载同一个链接即可。",
            "prerelease": False,
        })
    if status not in (200, 201):
        raise SystemExit(f"GitHub release failed: {status} {release}")
    release_id = release["id"]
    for asset in release.get("assets", []):
        if asset["name"] == ASSET:
            call(f"https://api.github.com/repos/{REPO}/releases/assets/{asset['id']}", auth, "DELETE")
    status, asset = call(
        f"https://uploads.github.com/repos/{REPO}/releases/{release_id}/assets?name={ASSET}",
        auth, "POST")
    if status not in (200, 201):
        # curl uploads need the file appended after the JSON body is not used here.
        result = subprocess.run(
            ["curl", "-sS", "--max-time", "300", "-X", "POST",
             "-H", f"Authorization: Bearer {auth}", "-H", "Content-Type: application/zip",
             "--data-binary", f"@{ZIP}",
             f"https://uploads.github.com/repos/{REPO}/releases/{release_id}/assets?name={ASSET}"],
            capture_output=True, text=True)
        try:
            asset = json.loads(result.stdout)
        except json.JSONDecodeError:
            raise SystemExit(f"Upload failed: {result.stdout[:300]} {result.stderr[:300]}")
    print(f"Published: {asset.get('browser_download_url')}")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
