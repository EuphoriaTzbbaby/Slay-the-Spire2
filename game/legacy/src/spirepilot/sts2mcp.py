"""Adapter for the STS2MCP REST API (localhost:15526).

The current STS2MCP snapshot is richer than the old Communication_Mod payload:
hand cards carry IDs and playability, enemies carry entity IDs, and combat
state is a single JSON tree.  This module maps that tree to SpirePilot's
internal CombatState.
"""

from __future__ import annotations

import json
import re
import urllib.request
from pathlib import Path
from typing import Any, Mapping

from .cards import CardDatabase, normalize_card_id
from .model import CardInstance, CardSpec, CombatState, EnemyState, Intent, PlayerState, Power, normalize_power_id

DEFAULT_URL = "http://127.0.0.1:15526"
_NAMES_ZH = Path(__file__).resolve().parents[2] / "data" / "cards" / "ironclad_names_zh.json"

_COMBAT_STATES = {"monster", "elite", "boss"}

_STATUS_ALIASES = {
    "易伤": "vulnerable",
    "虚弱": "weak",
    "脆弱": "frail",
    "力量": "strength",
    "敏捷": "dexterity",
    "中毒": "poison",
    "金属化": "metallicize",
    "荆棘": "thorns",
    "格挡": "block",
    "人工制品": "artifact",
    "再生": "regen",
    "缴械": "disarm",
    "无实体": "intangible",
    "堡垒": "barricade",
    "腐化": "corruption",
    "黑暗之拥": "dark_embrace",
    "无痛": "feel_no_pain",
    "恶魔形态": "demon_form",
    "势不可挡": "juggernaut",
    "破裂": "rupture",
    "火焰屏障": "flame_barrier",
    "暴怒": "rage",
    " plating": "plating",
}

_CHARACTER_ALIASES = {
    "铁甲战士": "IRONCLAD",
    "静默猎手": "SILENT",
    "故障机器人": "DEFECT",
    "观者": "WATCHER",
    "亡灵法师": "NECROBINDER",
    "摄政王": "REGENT",
}


class UnsupportedStateError(ValueError):
    pass


def load_name_map(path: Path = _NAMES_ZH) -> dict[str, str]:
    if not path.exists():
        return {}
    try:
        payload = json.loads(path.read_text(encoding="utf-8"))
    except (OSError, json.JSONDecodeError):
        return {}
    rows = payload.get("cards", payload) if isinstance(payload, dict) else payload
    out: dict[str, str] = {}
    for row in rows or []:
        if not isinstance(row, dict):
            continue
        zh = str(row.get("name_zh") or "").strip()
        cid = normalize_card_id(row.get("card_id") or "")
        if zh and cid:
            out[zh] = cid
    return out


def _int(value: Any, default: int = 0) -> int:
    try:
        return int(value)
    except (TypeError, ValueError):
        return default


def _parse_cost(value: Any) -> tuple[int, bool]:
    text = str(value or "").strip().upper()
    if text in {"X", "X_COST"}:
        return 0, True
    try:
        return max(0, int(float(text))), False
    except ValueError:
        return 0, False


def _status_powers(value: Any) -> dict[str, int]:
    out: dict[str, int] = {}
    if not isinstance(value, list):
        return out
    for item in value:
        if isinstance(item, str):
            name, amount = item, 1
        elif isinstance(item, dict):
            name = item.get("id") or item.get("name") or item.get("power_id") or ""
            amount = _int(item.get("amount", item.get("stacks", 1)), 1)
        else:
            continue
        key = _STATUS_ALIASES.get(str(name), normalize_power_id(str(name)))
        if key and amount:
            out[key] = out.get(key, 0) + amount
    return out


def _parse_intent(item: Any) -> Intent:
    if not isinstance(item, dict):
        return Intent("Unknown", 0, 1)
    kind = str(item.get("type") or item.get("intent_type") or "Unknown")
    label = str(item.get("label") or "")
    damage = _int(item.get("damage", item.get("value", 0)))
    hits = max(1, _int(item.get("hits", 1), 1))
    if damage <= 0:
        numbers = [int(n) for n in re.findall(r"\d+", label)]
        if numbers:
            damage = numbers[0]
        else:
            numbers = [int(n) for n in re.findall(r"(\d+)\s*点伤害", str(item.get("description") or ""))]
            if numbers:
                damage = numbers[0]
    # "7x2", "7×2", "7 点伤害 2 次"
    match = re.search(r"(\d+)\s*[x×*]\s*(\d+)", label)
    if match:
        damage = int(match.group(1))
        hits = max(1, int(match.group(2)))
    elif "attack" in kind.lower() and damage <= 0:
        damage = 0
    if "attack" not in kind.lower():
        damage = 0
    return Intent(kind=kind, damage=max(0, damage), hits=hits)


def _hand_card(item: Mapping[str, Any], index: int, db: CardDatabase, warnings: list[str]) -> CardInstance:
    card_id = normalize_card_id(item.get("id") or item.get("card_id") or "")
    name = str(item.get("name") or card_id)
    upgraded = bool(item.get("is_upgraded"))
    cost, x_cost = _parse_cost(item.get("cost"))
    target = str(item.get("target_type") or "Self")
    type_ = str(item.get("type") or "Skill")

    spec = db.get(card_id)
    if spec is None:
        warnings.append(f"未知手牌 {card_id or name}：按状态中的描述保守解析")
        spec = CardSpec(
            card_id=card_id or name,
            name=name,
            cost=cost,
            type=type_,
            target=target,
            text=str(item.get("description") or ""),
            x_cost=x_cost,
            source="sts2mcp",
        )
    else:
        # Live cost/target/upgrade state wins over the static snapshot.
        from dataclasses import replace

        spec = replace(
            spec,
            cost=cost,
            target=target,
            x_cost=x_cost,
            text=str(item.get("description") or spec.text),
        )
    return CardInstance(
        uid=f"h{index}",
        card_id=card_id or name,
        name=name,
        spec=spec,
        upgraded=upgraded,
        cost_override=cost,
        playable_override=bool(item.get("can_play")) if "can_play" in item else None,
    )


def _pile_card(item: Mapping[str, Any], index: int, prefix: str, db: CardDatabase, names: Mapping[str, str], warnings: list[str]) -> CardInstance:
    name = str(item.get("name") or "").strip()
    card_id = names.get(name, "")
    if not card_id:
        if name:
            warnings.append(f"牌堆中的「{name}」无法映射到卡牌 ID，按占位卡处理")
        card_id = normalize_card_id(name) or f"UNKNOWN_{prefix}_{index}"
    cost, x_cost = _parse_cost(item.get("cost"))
    spec = db.get(card_id)
    if spec is None:
        spec = CardSpec(
            card_id=card_id,
            name=name or card_id,
            cost=cost,
            target="Self",
            type="Skill",
            text=str(item.get("description") or ""),
            x_cost=x_cost,
            source="sts2mcp_name_map",
        )
    return CardInstance(
        uid=f"{prefix}{index}",
        card_id=card_id,
        name=name or spec.name,
        spec=spec,
        cost_override=cost,
    )


def _character(raw: str) -> str:
    text = str(raw or "")
    for zh, en in _CHARACTER_ALIASES.items():
        if zh in text:
            return en
    return normalize_card_id(text) or "IRONCLAD"


def parse_state(raw: Mapping[str, Any], db: CardDatabase | None = None) -> CombatState:
    db = db or CardDatabase.load()
    state_type = str(raw.get("state_type") or "")
    if state_type not in _COMBAT_STATES:
        raise UnsupportedStateError(f"not a combat state: state_type={state_type!r}")

    player_raw = raw.get("player")
    battle = raw.get("battle")
    if not isinstance(player_raw, dict) or not isinstance(battle, dict):
        raise UnsupportedStateError("missing player/battle object")

    warnings: list[str] = []
    names = load_name_map()

    hand_raw = player_raw.get("hand") or []
    hand: list[CardInstance] = []
    for i, item in enumerate(hand_raw):
        if isinstance(item, dict):
            hand.append(_hand_card(item, i, db, warnings))

    def pile(key: str, prefix: str) -> list[CardInstance]:
        items = player_raw.get(key) or []
        if items:
            warnings.append(f"{key} 只有名称/描述，没有稳定 CardId；按中文名映射，可能有个别遗漏")
        return [
            _pile_card(item, i, prefix, db, names, warnings)
            for i, item in enumerate(items)
            if isinstance(item, dict)
        ]

    enemies: list[EnemyState] = []
    for i, item in enumerate(battle.get("enemies") or []):
        if not isinstance(item, dict):
            continue
        enemy_id = str(item.get("entity_id") or item.get("id") or f"ENEMY_{i}")
        enemies.append(
            EnemyState(
                index=i,
                monster_id=enemy_id,
                hp=max(0, _int(item.get("hp"))),
                max_hp=max(0, _int(item.get("max_hp"), _int(item.get("hp")))),
                block=max(0, _int(item.get("block"))),
                powers=_status_powers(item.get("status")),
                intents=tuple(_parse_intent(x) for x in (item.get("intents") or [])),
            )
        )

    player = PlayerState(
        hp=max(0, _int(player_raw.get("hp"))),
        max_hp=max(0, _int(player_raw.get("max_hp"), _int(player_raw.get("hp")))),
        block=max(0, _int(player_raw.get("block"))),
        energy=max(0, _int(player_raw.get("energy"))),
        max_energy=max(0, _int(player_raw.get("max_energy"), 3)),
        powers=_status_powers(player_raw.get("status")),
        relics=tuple(
            str(r.get("id") or r.get("name") or "")
            for r in (player_raw.get("relics") or [])
            if isinstance(r, dict)
        ),
        potions=tuple(
            str(p.get("id") or p.get("name") or p) if isinstance(p, dict) else str(p)
            for p in (player_raw.get("potions") or [])
        ),
    )

    if not battle.get("is_play_phase", True):
        warnings.append("当前不是玩家出牌阶段；建议可能不适用")
    if not any(e.intents for e in enemies):
        warnings.append("敌人意图缺失；敌方回合为近似估计")

    return CombatState(
        player=player,
        hand=hand,
        draw_pile=pile("draw_pile", "d"),
        discard_pile=pile("discard_pile", "x"),
        exhaust_pile=pile("exhaust_pile", "e"),
        enemies=enemies,
        turn=max(1, _int(battle.get("round"), 1)),
        action_count=0,
        character=_character(player_raw.get("character")),
        act=max(1, _int((raw.get("run") or {}).get("act"), 1)),
        floor=max(0, _int((raw.get("run") or {}).get("floor"), 0)),
        trigger=f"sts2mcp:{state_type}",
        warnings=tuple(dict.fromkeys(warnings)),
    )


def fetch_state(url: str = DEFAULT_URL, timeout: float = 5.0) -> dict[str, Any]:
    endpoint = url.rstrip("/") + "/api/v1/singleplayer"
    with urllib.request.urlopen(endpoint, timeout=timeout) as response:
        payload = json.loads(response.read().decode("utf-8"))
    if not isinstance(payload, dict):
        raise ValueError("STS2MCP state is not a JSON object")
    return payload


def load_state_file(path: str | Path, db: CardDatabase | None = None) -> CombatState:
    payload = json.loads(Path(path).read_text(encoding="utf-8"))
    return parse_state(payload, db)
