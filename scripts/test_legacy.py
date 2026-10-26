"""Run the legacy suite with paths independent of the caller's directory."""
import os
from pathlib import Path
import subprocess
import sys

root = Path(__file__).resolve().parents[1] / "game" / "legacy"
env = dict(os.environ, PYTHONPATH=str(root / "src"))
raise SystemExit(subprocess.call(
    [sys.executable, "-m", "unittest", "discover", "-s", "tests", "-v"],
    cwd=root, env=env,
))
