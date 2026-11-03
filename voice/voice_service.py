"""Run from a checkout, without requiring an editable package install."""
import sys
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parent / "src"))
from spire_voice.__main__ import main

if __name__ == "__main__":
    main()
