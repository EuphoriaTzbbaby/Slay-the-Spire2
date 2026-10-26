"""Card database loading and catalog fallback.

The committed database is a mechanical snapshot for local development.  Live
state from the mod wins over the static snapshot where fields overlap (cost,
damage, block, draw, energy, target), so an upgraded card or a game patch does
not silently use the wrong numbers.
"""

from __future__ import annotations

import json
import os
import re
from dataclasses import replace
from pathlib import Path
from typing import Any, Mapping

from .model import CardInstance, CardSpec, Power

_DATA_DIR = Path(__file__).resolve().parents[2] / "data" / "cards"
_DEFAULT_FILES = ("ironclad_cards.json", "ironclad_core.json")

_ALIASES = {
    "STRIKE": ("STRIKE_IRONCLAD",),
    "DEFEND": ("DEFEND_IRONCLAD",),
    "STRIKE_RED": ("STRIKE_IRONCLAD",),
    "DEFEND_RED": ("DEFEND_IRONCLAD",),
}

# Statuses/curses are not playable.  The live CardCatalog does not expose the
# card type, so keep a small explicit set for the common cases.
_UNPLAYABLE_IDS = {
    "WOUND",
    "BURN",
    "DAZED",
    "SLIMED",
    "VOID",
    "ASCENDERS_BANE",
    "CURSE_OF_THE_BELL",
    "DOUBT",
    "SHAME",
    "REGRET",
    "DECAY",
    "PAIN",
    "NORMALITY",
}

_TARGETS = {
    "anyenemy": "AnyEnemy",
    "allenemies": "AllEnemies",
    "randomenemy": "RandomEnemy",
    "self": "Self",
    "anyally": "AnyAlly",
    "none": "Self",
    "unknown": "Self",
}


def humanize_id(card_id: str) -> str:
    return str(card_id).replace("_", " ").title()


def normalize_card_id(value: str) -> str:
    text = str(value or "").strip().upper()
    text = re.sub(r"\s+", "_", text)
    return text


def normalize_target(value: str | None) -> str:
    key = str(value or "Self").strip().lower().replace("_", "")
    return _TARGETS.get(key, "AnyEnemy" if "enemy" in key else "Self")


def parse_cost(value: Any) -> tuple[int, bool]:
    """Return ``(cost, is_x_cost)`` from a catalog value."""

    if isinstance(value, bool):
        return int(value), False
    if isinstance(value, (int, float)):
        return max(0, int(value)), False
    text = str(value or "0").strip().upper()
    if text in {"X", "X_COST"}:
        return 0, True
    try:
        return max(0, int(float(text))), False
    except ValueError:
        return 0, False


def _upgrade_tuple(value: Any) -> tuple[tuple[str, object], ...]:
    if not isinstance(value, dict):
        return ()
    out: list[tuple[str, object]] = []
    for key, val in value.items():
        if isinstance(val, (str, int, float, bool)) or val is None:
            out.append((str(key), val))
    return tuple(out)


def _power_tuple(value: Any) -> tuple[Power, ...]:
    if not isinstance(value, list):
        return ()
    out: list[Power] = []
    for item in value:
        if not isinstance(item, dict):
            continue
        pid = item.get("id") or item.get("power") or item.get("power_key")
        if not pid:
            continue
        try:
            amount = int(item.get("amount", 0) or 0)
        except (TypeError, ValueError):
            amount = 0
        if amount:
            out.append(Power(str(pid), amount))
    return tuple(out)


def spec_from_dict(raw: Mapping[str, Any]) -> CardSpec:
    card_id = normalize_card_id(raw.get("card_id") or raw.get("id") or "")
    name = str(raw.get("name") or humanize_id(card_id))
    cost_raw = raw.get("cost", 0)
    cost, x_cost = parse_cost(cost_raw)
    if bool(raw.get("x_cost")):
        x_cost = True
    return CardSpec(
        card_id=card_id,
        name=name,
        cost=cost,
        type=str(raw.get("type") or "Skill"),
        target=normalize_target(raw.get("target")),
        damage=max(0, int(raw.get("damage", 0) or 0)),
        block=max(0, int(raw.get("block", 0) or 0)),
        hits=max(1, int(raw.get("hits", 1) or 1)),
        draw=max(0, int(raw.get("draw", 0) or 0)),
        energy=int(raw.get("energy", 0) or 0),
        hp_loss=max(0, int(raw.get("hp_loss", 0) or 0)),
        powers=_power_tuple(raw.get("powers")),
        keywords=tuple(str(k) for k in (raw.get("keywords") or [])),
        text=str(raw.get("text") or ""),
        x_cost=x_cost,
        upgrade=_upgrade_tuple(raw.get("upgrade")),
        source=str(raw.get("source") or "static"),
    )


def spec_from_catalog(entry: Mapping[str, Any]) -> CardSpec:
    card_id = normalize_card_id(entry.get("CardId") or entry.get("card_id") or "")
    cost, x_cost = parse_cost(entry.get("Cost", 0))
    damage = max(0, int(entry.get("Damage", 0) or 0))
    block = max(0, int(entry.get("Block", 0) or 0))
    draw = max(0, int(entry.get("DrawsCards", 0) or 0))
    energy = int(entry.get("GivesEnergy", 0) or 0)
    vulnerable = max(0, int(entry.get("AppliesVulnerable", 0) or 0))
    weak = max(0, int(entry.get("AppliesWeak", 0) or 0))
    powers: list[Power] = []
    if vulnerable:
        powers.append(Power("Vulnerable", vulnerable))
    if weak:
        powers.append(Power("Weak", weak))
    return CardSpec(
        card_id=card_id,
        name=humanize_id(card_id),
        cost=cost,
        type="Attack" if damage else "Skill",
        target=normalize_target(entry.get("TargetType")),
        damage=damage,
        block=block,
        draw=draw,
        energy=energy,
        powers=tuple(powers),
        x_cost=x_cost,
        source="live_catalog",
    )


def merge_catalog(spec: CardSpec, entry: Mapping[str, Any] | None) -> CardSpec:
    """Override static data with live catalog values when they are present."""

    if not entry:
        return spec
    updates: dict[str, Any] = {}
    if "Cost" in entry:
        cost, x_cost = parse_cost(entry.get("Cost"))
        updates["cost"] = cost
        updates["x_cost"] = x_cost
    if "TargetType" in entry:
        updates["target"] = normalize_target(entry.get("TargetType"))
    if "Damage" in entry:
        updates["damage"] = max(0, int(entry.get("Damage", 0) or 0))
    if "Block" in entry:
        updates["block"] = max(0, int(entry.get("Block", 0) or 0))
    if "DrawsCards" in entry:
        updates["draw"] = max(0, int(entry.get("DrawsCards", 0) or 0))
    if "GivesEnergy" in entry:
        updates["energy"] = int(entry.get("GivesEnergy", 0) or 0)
    has_vulnerable = "AppliesVulnerable" in entry
    has_weak = "AppliesWeak" in entry
    vulnerable = max(0, int(entry.get("AppliesVulnerable", 0) or 0)) if has_vulnerable else 0
    weak = max(0, int(entry.get("AppliesWeak", 0) or 0)) if has_weak else 0
    if has_vulnerable or has_weak:
        powers = [
            p for p in spec.powers
            if not ((p.key == "vulnerable" and has_vulnerable) or (p.key == "weak" and has_weak))
        ]
        if has_vulnerable and vulnerable:
            powers.append(Power("Vulnerable", vulnerable))
        if has_weak and weak:
            powers.append(Power("Weak", weak))
        if powers != list(spec.powers):
            updates["powers"] = tuple(powers)
    if not updates:
        return spec
    return replace(spec, **updates)


def unplayable(card_id: str, spec: CardSpec | None = None) -> bool:
    if normalize_card_id(card_id) in _UNPLAYABLE_IDS:
        return True
    if spec is not None and spec.type.lower() in {"status", "curse"}:
        return True
    return False


class CardDatabase:
    def __init__(self, specs: list[CardSpec], path: Path | None = None):
        self._specs: dict[str, CardSpec] = {s.card_id: s for s in specs}
        self.path = path

    def __len__(self) -> int:
        return len(self._specs)

    def get(self, card_id: str) -> CardSpec | None:
        key = normalize_card_id(card_id)
        if key in self._specs:
            return self._specs[key]
        for alias in _ALIASES.get(key, ()):
            if alias in self._specs:
                return self._specs[alias]
        # Common fallback: "BASH" -> "BASH", "STRIKE" -> "STRIKE_IRONCLAD".
        for suffix in ("_IRONCLAD", "_RED"):
            if f"{key}{suffix}" in self._specs:
                return self._specs[f"{key}{suffix}"]
        return None

    def resolve(
        self,
        card_id: str,
        catalog_entry: Mapping[str, Any] | None = None,
        upgraded: bool = False,
    ) -> CardSpec:
        key = normalize_card_id(card_id)
        spec = self.get(key)
        if spec is not None:
            return merge_catalog(spec, catalog_entry)
        if catalog_entry:
            return spec_from_catalog(catalog_entry)
        return CardSpec(
            card_id=key,
            name=humanize_id(key),
            cost=1,
            type="Skill",
            target="Self",
            source="unknown",
            text="",
        )

    def make_instance(
        self,
        card_id: str,
        uid: str,
        catalog_entry: Mapping[str, Any] | None = None,
        upgraded: bool = False,
        cost_override: int | None = None,
    ) -> CardInstance:
        spec = self.resolve(card_id, catalog_entry, upgraded)
        return CardInstance(
            uid=uid,
            card_id=normalize_card_id(card_id),
            name=spec.name,
            spec=spec,
            upgraded=upgraded,
            cost_override=cost_override,
            playable_override=False if unplayable(card_id, spec) else None,
        )

    @classmethod
    def load(cls, path: str | Path | None = None) -> "CardDatabase":
        candidate: Path | None = None
        if path:
            candidate = Path(path)
        elif os.environ.get("SPIREPILOT_CARD_DB"):
            candidate = Path(os.environ["SPIREPILOT_CARD_DB"])
        else:
            for name in _DEFAULT_FILES:
                p = _DATA_DIR / name
                if p.exists():
                    candidate = p
                    break
        if candidate is None or not candidate.exists():
            return cls([], None)
        payload = json.loads(candidate.read_text(encoding="utf-8"))
        raw_cards = payload.get("cards") if isinstance(payload, dict) else payload
        specs = [spec_from_dict(item) for item in (raw_cards or []) if isinstance(item, dict)]
        return cls(specs, candidate)

    def all(self) -> list[CardSpec]:
        return list(self._specs.values())
