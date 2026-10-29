"""Build the mod and produce a one-click Windows zip (mod + installer + readme)."""
from __future__ import annotations

import shutil
import subprocess
import sys
import zipfile
from pathlib import Path

ROOT = Path(__file__).resolve().parent.parent
DIST = ROOT / "dist"
STAGING = DIST / "staging"
MOD_FILES = ("SpireCompanion.dll", "SpireCompanion.json", "LICENSE")


def main() -> int:
    subprocess.run([sys.executable, str(ROOT / "scripts/game_dev.py"), "build"], check=True)
    built = ROOT / "game/legacy/SpireCompanion/bin/Release/net9.0"
    if STAGING.exists():
        shutil.rmtree(STAGING)
    mod_dir = STAGING / "SpireCompanion"
    mod_dir.mkdir(parents=True)
    for name in MOD_FILES:
        source = built / name
        if not source.exists():
            source = ROOT / "game/legacy/SpireCompanion" / name if name != "LICENSE" else ROOT / "LICENSE"
        if not source.exists():
            print(f"缺少文件: {name}")
            return 1
        shutil.copy2(source, mod_dir / name)
    shutil.copy2(ROOT / "scripts/install_windows.bat", STAGING / "install.bat")
    shutil.copy2(ROOT / "scripts/install_windows.ps1", STAGING / "install.ps1")
    shutil.copy2(ROOT / "scripts/update_windows.bat", STAGING / "update.bat")
    (STAGING / "README.txt").write_text(
        "尖塔 AI 陪玩（开发版）\n\n"
        "1. 解压整个压缩包；\n"
        "2. 双击 install.bat；\n"
        "3. 按提示确认游戏目录；\n"
        "4. 自动战斗必须另外安装 CombatSolver 与 RitsuLib（创意工坊订阅）；\n"
        "5. 启动游戏，确认 Mod 已启用，进游戏按 F9。\n\n"
        "以后更新：重新下载同一个 zip，退出游戏后再次双击 install.bat。\n",
        encoding="utf-8",
    )
    archive = DIST / "SpireCompanion-win64.zip"
    if archive.exists():
        archive.unlink()
    with zipfile.ZipFile(archive, "w", zipfile.ZIP_DEFLATED) as zf:
        for path in sorted(STAGING.rglob("*")):
            if path.is_file():
                zf.write(path, path.relative_to(STAGING).as_posix())
    print(f"安装包: {archive}")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
