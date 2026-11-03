"""Set up and debug the local voice service without changing global Python packages."""
import argparse
from pathlib import Path
import subprocess
import sys
import venv

ROOT = Path(__file__).resolve().parents[1]
VOICE = ROOT / "voice"
PYTHON = VOICE / ".venv" / ("Scripts/python.exe" if sys.platform == "win32" else "bin/python")


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("action", choices=["setup", "run", "devices", "test-mic", "test-tts", "check"])
    args, remaining = parser.parse_known_args()
    if args.action == "setup":
        if sys.version_info < (3, 11):
            parser.error("需要 Python 3.11 或更高版本。")
        if not PYTHON.exists():
            venv.EnvBuilder(with_pip=True).create(VOICE / ".venv")
        result = subprocess.call([str(PYTHON), "-m", "pip", "install", "-e", str(VOICE)])
        if result:
            return result
        return subprocess.call([str(PYTHON), str(VOICE / "voice_service.py"), "--download-model"])
    if not PYTHON.exists():
        parser.error("先运行 python scripts/voice_dev.py setup。")
    if args.action == "check":
        return subprocess.call([str(PYTHON), "-m", "unittest", "discover", "-s", str(VOICE / "tests"), "-v", *remaining], cwd=ROOT)
    option = {"run": [], "devices": ["--list-devices"], "test-mic": ["--test-mic"], "test-tts": ["--test-tts"]}[args.action]
    return subprocess.call([str(PYTHON), str(VOICE / "voice_service.py"), *option, *remaining])


if __name__ == "__main__":
    try:
        raise SystemExit(main())
    except KeyboardInterrupt:
        raise SystemExit(130)
