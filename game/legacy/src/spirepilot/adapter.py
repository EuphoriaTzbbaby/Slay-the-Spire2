"""Adapter for the JSON pushed by Communication_Mod_STS2.

The upstream mod uses PascalCase anonymous objects and a few fields that are
not stable across patches.  This module is intentionally tolerant: it accepts
missing optional fields, records warnings, and falls back to the live
``CardCatalog`` for card stats.
"""

from __future__ import annotations

from dataclasses import dataclass, field
from typing import Any, Iterable, Mapping

from .cards import CardDatabase, normalize_card_id
from .model import CardInstance, CombatState, EnemyState, Intent, PlayerState, normalize_power_id


class UnsupportedStateError(ValueError):
    """Raised when a payload is not a combat state (e.g. a reward screen)."""


class StateParseError(ValueError):
    """Raised when a payload looks like combat but lacks required fields."""


@dataclass
class CatalogIndex:
    entries: dict[str, list[dict[str, Any]]] = field(default_factory=dict)

    @classmethod
    def from_raw(cls, raw: Mapping[str, Any]) -> "CatalogIndex":
        index = cls()
        items = raw.get("CardCatalog") or []
        if not isinstance(items, list):
            return index
        for item in items:
            if not isinstance(item, dict):
                continue
            card_id = normalize_card_id(item.get("CardId") or item.get("card_id") or "")
            if not card_id:
                continue
            index.entries.setdefault(card_id, []).append(dict(item))
        return index

    def choose(self, card_id: str, upgraded: bool = False) -> dict[str, Any] | None:
        items = self.entries.get(normalize_card_id(card_id)) or []
        if not items:
            return None
        # Prefer the matching upgrade state; otherwise use the first entry.
        for item in items:
            if bool(item.get("IsUpgraded")) == bool(upgraded):
                return item
        return items[0]

    def has_card(self, card_id: str) -> bool:
        return normalize_card_id(card_id) in self.entries


def is_combat_state(raw: Any) -> bool:
    return isinstance(raw, dict) and isinstance(raw.get("Player"), dict) and isinstance(raw.get("Enemies"), list)


def _as_int(value: Any, default: int = 0) -> int:
    try:
        if value is None:
            return default
        return int(value)
    except (TypeError, ValueError):
        return default


def _as_bool(value: Any, default: bool = False) -> bool:
    if value is None:
        return default
    if isinstance(value, bool):
        return value
    return str(value).strip().lower() in {"1", "true", "yes", "y"}


def _powers(raw: Any) -> dict[str, int]:
    out: dict[str, int] = {}
    if not isinstance(raw, list):
        return out
    for item in raw:
        if not isinstance(item, dict):
            continue
        pid = item.get("PowerId") or item.get("power_id") or item.get("Id") or item.get("id")
        if not pid:
            continue
        key = normalize_power_id(str(pid))
        amount = _as_int(item.get("Amount", item.get("amount", 0)))
        if amount:
            out[key] = out.get(key, 0) + amount
    return out


def _intents(raw: Any) -> tuple[Intent, ...]:
    out: list[Intent] = []
    if not isinstance(raw, list):
        return ()
    for item in raw:
        if not isinstance(item, dict):
            continue
        kind = str(item.get("Type") or item.get("IntentType") or "Unknown")
        damage = _as_int(item.get("Damage", item.get("damage", 0)))
        hits = max(1, _as_int(item.get("Hits", item.get("hits", 1)), 1))
        out.append(Intent(kind=kind, damage=max(0, damage), hits=hits))
    return tuple(out)


def _pile(raw: Any, prefix: str) -> list[dict[str, Any]]:
    if not isinstance(raw, list):
        return []
    return [item for item in raw if isinstance(item, dict)]


def _card_name(item: Mapping[str, Any]) -> str:
    return normalize_card_id(item.get("Card_Name") or item.get("CardName") or item.get("CardId") or item.get("card_id") or "")


def _upgraded(item: Mapping[str, Any], catalog_entry: Mapping[str, Any] | None) -> bool:
    if "IsUpgraded" in item:
        return _as_bool(item.get("IsUpgraded"))
    if "Upgraded" in item:
        return _as_bool(item.get("Upgraded"))
    if catalog_entry is not None and "IsUpgraded" in catalog_entry:
        return _as_bool(catalog_entry.get("IsUpgraded"))
    return False


def _card_instances(
    items: Iterable[Mapping[str, Any]],
    prefix: str,
    db: CardDatabase,
    catalog: CatalogIndex,
    warnings: list[str],
) -> list[CardInstance]:
    out: list[CardInstance] = []
    for i, item in enumerate(items):
        card_id = _card_name(item)
        if not card_id:
            warnings.append(f"{prefix} 中存在缺少 Card_Name 的卡牌，已跳过")
            continue
        catalog_entry = catalog.choose(card_id, upgraded=_upgraded(item, None))
        upgraded = _upgraded(item, catalog_entry)
        if catalog_entry is None and db.get(card_id) is None:
            warnings.append(f"未知卡牌 {card_id}：使用保守默认值（1 费/无效果）")
        instance = db.make_instance(
            card_id=card_id,
            uid=f"{prefix}{i}",
            catalog_entry=catalog_entry,
            upgraded=upgraded,
        )
        # If the live payload explicitly says a card is playable/not playable,
        # trust it over our type heuristic.
        if "CanPlay" in item or "Playable" in item:
            instance.playable_override = _as_bool(item.get("CanPlay", item.get("Playable")))
        out.append(instance)
    return out


def parse_combat_state(raw: Mapping[str, Any], db: CardDatabase) -> CombatState:
    if not is_combat_state(raw):
        raise UnsupportedStateError("payload is not a combat state")

    warnings: list[str] = []
    player_raw = raw.get("Player")
    if not isinstance(player_raw, dict):
        raise StateParseError("missing Player object")
    piles_raw = raw.get("Piles")
    if not isinstance(piles_raw, dict):
        raise StateParseError("missing Piles object")

    hp = _as_int(player_raw.get("CurrentHp", player_raw.get("Hp", player_raw.get("HP"))), -1)
    if hp < 0:
        raise StateParseError("missing Player.CurrentHp")
    max_hp = _as_int(player_raw.get("MaxHp", hp), hp)
    if "Block" not in player_raw:
        warnings.append("玩家格挡字段缺失，按 0 处理（可能低估防御收益）")
    player = PlayerState(
        hp=hp,
        max_hp=max_hp,
        block=max(0, _as_int(player_raw.get("Block", 0))),
        energy=max(0, _as_int(player_raw.get("Energy", 0))),
        max_energy=max(0, _as_int(player_raw.get("MaxEnergy", 3), 3)),
        powers=_powers(player_raw.get("Powers")),
        relics=tuple(
            str(r.get("RelicId") or r.get("id") or "")
            for r in (player_raw.get("Relics") or [])
            if isinstance(r, dict) and (r.get("RelicId") or r.get("id"))
        ),
        potions=tuple(str(p) for p in (player_raw.get("Potions") or []) if p),
    )

    catalog = CatalogIndex.from_raw(raw)
    if not catalog.entries:
        warnings.append("CardCatalog 缺失：卡牌数值使用静态数据库，升级/补丁差异可能不准")

    hand = _card_instances(_pile(piles_raw.get("HandPile"), "hand"), "h", db, catalog, warnings)
    draw = _card_instances(_pile(piles_raw.get("DrawPile"), "draw"), "d", db, catalog, warnings)
    discard = _card_instances(_pile(piles_raw.get("DiscardPile"), "discard"), "x", db, catalog, warnings)
    exhaust = _card_instances(_pile(piles_raw.get("ExhaustPile"), "exhaust"), "e", db, catalog, warnings)

    enemies: list[EnemyState] = []
    for i, item in enumerate(raw.get("Enemies") or []):
        if not isinstance(item, dict):
            continue
        ehp = _as_int(item.get("CurrentHp", item.get("Hp")), 0)
        if "Block" not in item:
            # Only warn once to keep the UI readable.
            if not any("敌人格挡" in w for w in warnings):
                warnings.append("敌人格挡字段缺失，按 0 处理（可能高估斩杀线）")
        enemies.append(
            EnemyState(
                index=_as_int(item.get("Index"), i),
                monster_id=str(item.get("MonsterId") or item.get("Monster") or f"ENEMY_{i}"),
                hp=max(0, ehp),
                max_hp=max(ehp, _as_int(item.get("MaxHp"), ehp)),
                block=max(0, _as_int(item.get("Block", 0))),
                powers=_powers(item.get("Powers")),
                intents=_intents(item.get("Intents")),
            )
        )

    first = raw.get("FirstInfo") if isinstance(raw.get("FirstInfo"), dict) else {}
    run_status = first.get("RunStatus") if isinstance(first.get("RunStatus"), dict) else {}
    room = first.get("RoomInfo") if isinstance(first.get("RoomInfo"), dict) else {}

    return CombatState(
        player=player,
        hand=hand,
        draw_pile=draw,
        discard_pile=discard,
        exhaust_pile=exhaust,
        enemies=enemies,
        turn=max(1, _as_int(raw.get("TurnNumber"), 1)),
        action_count=max(0, _as_int(raw.get("ActionCount"), 0)),
        character=str(run_status.get("Character") or player_raw.get("Character") or "IRONCLAD"),
        act=max(1, _as_int(run_status.get("Act"), 1)),
        floor=max(0, _as_int(run_status.get("Floor"), 0)),
        trigger=str(raw.get("Trigger") or ""),
        warnings=tuple(dict.fromkeys(warnings)),
    )


def parse_file(path: str, db: CardDatabase) -> CombatState:
    import json

    with open(path, "r", encoding="utf-8") as handle:
        raw = json.load(handle)
    return parse_combat_state(raw, db)
