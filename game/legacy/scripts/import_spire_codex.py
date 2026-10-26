#!/usr/bin/env python3
"""Fetch the Ironclad card list from the public Spire Codex API.

Usage:
    python scripts/import_spire_codex.py
    python scripts/import_spire_codex.py --out data/cards/ironclad_cards.json

This rewrites the local mechanical snapshot.  It is intentionally a separate
script so the runtime advisor stays offline and dependency-free.
"""

from __future__ import annotations

import argparse
import json
import re
import shutil
import subprocess
import urllib.error
import urllib.request
from pathlib import Path

API = "https://spire-codex.com/api/cards?color=ironclad"


def clean_text(text: str) -> str:
    text = re.sub(r"\[/?gold\]", "", text or "")
    text = re.sub(r"\[energy:(\d+)\]", r"\1 energy", text)
    text = re.sub(r"\[[^\]]+\]", "", text)
    return text.strip()


def convert(card: dict) -> dict:
    powers = []
    for power in card.get("powers_applied") or []:
        powers.append(
            {
                "id": power.get("power_key") or power.get("power"),
                "amount": power.get("amount", 0),
            }
        )
    hp_loss = card.get("hp_loss")
    if hp_loss is None:
        hp_loss = (card.get("vars") or {}).get("HpLoss", 0) or 0
    return {
        "card_id": card["id"],
        "name": card["name"],
        "cost": 0 if card.get("is_x_cost") else (card.get("cost") or 0),
        "x_cost": bool(card.get("is_x_cost")),
        "type": card.get("type") or "Unknown",
        "target": card.get("target") or "Self",
        "damage": card.get("damage") or 0,
        "block": card.get("block") or 0,
        "hits": card.get("hit_count") or 1,
        "draw": card.get("cards_draw") or 0,
        "energy": card.get("energy_gain") or 0,
        "hp_loss": hp_loss or 0,
        "powers": powers,
        "keywords": card.get("keywords") or [],
        "text": clean_text(card.get("description") or ""),
        "upgrade": card.get("upgrade") or {},
        "source": "spire_codex_api",
    }


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("--out", default="data/cards/ironclad_cards.json")
    parser.add_argument("--api", default=API)
    parser.add_argument("--game-version", default="0.107.1")
    args = parser.parse_args()

    request = urllib.request.Request(
        args.api,
        headers={"User-Agent": "SpirePilot/0.1 (local research; card data import)"},
    )
    try:
        with urllib.request.urlopen(request, timeout=30) as response:
            body = response.read().decode("utf-8")
    except (urllib.error.URLError, OSError) as exc:
        # macOS python.org builds sometimes lack a CA bundle.  Fall back to
        # curl, which uses the system trust store, instead of disabling TLS
        # verification silently.
        if shutil.which("curl") is None:
            raise SystemExit(f"download failed: {exc}; install a CA bundle or use curl")
        print(f"urllib failed ({exc}); retrying with curl")
        result = subprocess.run(
            ["curl", "-sS", "-L", "-A", "SpirePilot/0.1", args.api],
            check=False,
            capture_output=True,
            text=True,
        )
        if result.returncode != 0:
            raise SystemExit(f"curl failed: {result.stderr.strip()}")
        body = result.stdout
    cards = json.loads(body)

    payload = {
        "schema_version": "0.1",
        "game_version": args.game_version,
        "source": (
            f"Spire Codex public API {args.api} (accessed via scripts/import_spire_codex.py); "
            "mechanical fields only, local research use"
        ),
        "cards": [convert(card) for card in cards],
    }
    out = Path(args.out)
    out.parent.mkdir(parents=True, exist_ok=True)
    out.write_text(json.dumps(payload, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")
    print(f"wrote {out} ({len(payload['cards'])} cards)")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
