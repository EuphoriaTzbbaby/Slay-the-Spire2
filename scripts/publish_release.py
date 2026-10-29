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


def call(url: str, auth: str, method: str = "GET", body: dict | None = None) -> tuple[int, dict]:
    data = json.dumps(body).encode() if body is not None else None
    request = urllib.request.Request(url, data=data, method=method, headers={
        "Authorization": f"Bearer {auth}",
        "Accept": "application/vnd.github+json",
        "Content-Type": "application/json",
    })
    try:
        with urllib.request.urlopen(request) as response:
            return response.status, json.loads(response.read() or b"{}")
    except urllib.error.HTTPError as error:
        return error.code, json.loads(error.read() or b"{}")


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
    upload = urllib.request.Request(
        f"https://uploads.github.com/repos/{REPO}/releases/{release_id}/assets?name={ASSET}",
        data=ZIP.read_bytes(), method="POST",
        headers={"Authorization": f"Bearer {auth}", "Content-Type": "application/zip"},
    )
    with urllib.request.urlopen(upload) as response:
        asset = json.loads(response.read())
    print(f"Published: {asset['browser_download_url']}")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
