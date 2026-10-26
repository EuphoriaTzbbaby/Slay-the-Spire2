"""High-level combat advice: search + explanation + alternatives."""

from __future__ import annotations

from dataclasses import dataclass, field

from .model import Action, CombatState, EndTurn, PlayCard
from .exact import exact_search
from .rules import enemy_turn, play_card
from .search import (
    Alternative,
    SearchConfig,
    action_label,
    sample_state,
    search,
)


@dataclass
class Advice:
    action: Action
    label: str
    score: float
    sequence: tuple[Action, ...]
    sequence_labels: tuple[str, ...]
    reasons: tuple[str, ...]
    alternatives: tuple[Alternative, ...] = ()
    warnings: tuple[str, ...] = ()
    stats: dict = field(default_factory=dict)
    estimated: dict = field(default_factory=dict)

    @property
    def approximate(self) -> bool:
        return bool(self.warnings)


def _dedupe(items: list[str], limit: int = 8) -> tuple[str, ...]:
    out: list[str] = []
    for item in items:
        item = item.strip()
        if item and item not in out:
            out.append(item)
        if len(out) >= limit:
            break
    return tuple(out)


def _replay_sequence(
    state: CombatState,
    sequence: tuple[Action, ...],
    sample: int,
) -> tuple[list[object], CombatState, object]:
    """Replay the recommended line with the same draw-order sample as search."""

    current, rng = sample_state(state, sample)
    reports: list[object] = []
    for action in sequence:
        if isinstance(action, EndTurn):
            break
        current, report = play_card(current, action, rng)
        reports.append(report)
    after_enemy, enemy_report = enemy_turn(current, rng)
    return reports, after_enemy, enemy_report


def _reasons_for(
    state: CombatState,
    advice_action: Action,
    sequence_labels: tuple[str, ...],
    after_enemy: CombatState,
    reports: list[object],
    enemy_report: object,
) -> tuple[str, ...]:
    reasons: list[str] = []
    first = reports[0] if reports else None

    if isinstance(advice_action, EndTurn) and first is None:
        reasons.append("当前没有明显更高收益的可打出牌，结束回合进入敌方行动")
    elif first is not None:
        killed = getattr(first, "killed", [])
        dmg = getattr(first, "damage_dealt", 0)
        block = getattr(first, "block_gained", 0)
        draw = getattr(first, "cards_drawn", 0)
        energy = getattr(first, "energy_gained", 0)
        hp_lost = getattr(first, "hp_lost", 0)
        vuln = getattr(first, "vulnerable_applied", 0)
        weak = getattr(first, "weak_applied", 0)
        if killed:
            reasons.append(f"可击杀：{'、'.join(killed)}")
        if dmg:
            reasons.append(f"第一步预计造成约 {dmg} 点伤害（含被格挡部分）")
        if vuln:
            reasons.append(f"施加 {vuln} 层易伤（多目标时为合计）：后续攻击伤害 +50%")
        if weak:
            reasons.append(f"施加 {weak} 层虚弱（多目标时为合计）：敌方攻击伤害 -25%")
        if block:
            reasons.append(f"获得 {block} 点格挡，优先抵消本回合敌方伤害")
        if draw:
            reasons.append(f"抽 {draw} 张牌，增加本回合的选择")
        if energy:
            reasons.append(f"获得 {energy} 点能量")
        if hp_lost:
            reasons.append(f"以 {hp_lost} 点生命为代价换取节奏")
        for note in getattr(first, "notes", [])[:2]:
            reasons.append(note)

    if len(reports) > 1:
        total_damage = sum(getattr(r, "damage_dealt", 0) for r in reports)
        total_block = sum(getattr(r, "block_gained", 0) for r in reports)
        total_draw = sum(getattr(r, "cards_drawn", 0) for r in reports)
        total_kills: list[str] = []
        for report in reports:
            for name in getattr(report, "killed", []):
                if name not in total_kills:
                    total_kills.append(name)
        if total_damage:
            reasons.append(f"整条连招合计约 {total_damage} 点伤害、{total_block} 点格挡、抽 {total_draw} 张")
        if total_kills:
            reasons.append(f"连招内击杀：{'、'.join(total_kills)}")

    damage_taken = getattr(enemy_report, "damage_taken", 0)
    block_absorbed = getattr(enemy_report, "block_absorbed", 0)
    hp_lost = max(0, damage_taken - block_absorbed)
    if getattr(enemy_report, "player_died", False):
        reasons.append("警告：按当前估算会死亡，建议重新评估格挡/击杀线")
    elif damage_taken:
        reasons.append(
            f"预计敌方回合攻击 {damage_taken}，格挡 {block_absorbed}，掉血 {hp_lost}，"
            f"结算后 HP {after_enemy.player.hp}/{state.player.max_hp}"
        )
    else:
        reasons.append(f"预计敌方回合无伤害，结算后 HP {after_enemy.player.hp}/{state.player.max_hp}")

    if not after_enemy.alive_enemies:
        reasons.append("按推荐连招预计清场，战斗结束")
    elif after_enemy.alive_enemies:
        total_hp = sum(e.hp for e in after_enemy.alive_enemies)
        reasons.append(f"连招结算后敌方剩余总血量约 {total_hp}")

    if len(sequence_labels) > 2:
        shown = " → ".join(sequence_labels[:5])
        reasons.append(f"推荐连招：{shown}")
    return _dedupe(reasons)


def advise(
    state: CombatState,
    config: SearchConfig | None = None,
    *,
    solver: str = "beam",
    result=None,
) -> Advice:
    if state.combat_over:
        alive = bool(state.alive_enemies)
        return Advice(
            action=EndTurn(),
            label="战斗已结束" if not alive else "无法行动",
            score=1_000_000.0 if not alive else -1_000_000_000.0,
            sequence=(),
            sequence_labels=(),
            reasons=("当前没有可执行的战斗动作",),
            warnings=state.warnings,
            stats={},
            estimated={},
        )

    if result is None:
        result = exact_search(state, config) if solver == "exact" else search(state, config)
    action = result.action
    label = result.labels[0] if result.labels else action_label(state, action)

    try:
        reports, after_enemy, enemy_report = _replay_sequence(state, result.sequence, result.sample)
    except (ValueError, IndexError) as exc:
        # The search should only return legal actions; this is a safety net.
        return Advice(
            action=EndTurn(),
            label="结束回合（回退）",
            score=result.score,
            sequence=(EndTurn(),),
            sequence_labels=("结束回合",),
            reasons=(f"推荐动作无法在前向模型中复现（{exc}），已降级为结束回合",),
            alternatives=result.alternatives,
            warnings=tuple(dict.fromkeys((*result.warnings, "建议动作回退，请人工确认"))),
            stats=result.stats,
            estimated={},
        )

    reasons = _reasons_for(state, action, result.labels, after_enemy, reports, enemy_report)
    estimated = {
        "player_hp_after_enemy": after_enemy.player.hp,
        "enemy_hp_after_enemy": sum(e.hp for e in after_enemy.alive_enemies),
        "enemy_count_after": len(after_enemy.alive_enemies),
        "damage_taken": getattr(enemy_report, "damage_taken", 0),
        "block_absorbed": getattr(enemy_report, "block_absorbed", 0),
        "hp_lost": max(0, getattr(enemy_report, "damage_taken", 0) - getattr(enemy_report, "block_absorbed", 0)),
        "sequence_damage": sum(getattr(r, "damage_dealt", 0) for r in reports),
        "score": result.score,
    }
    warnings = list(result.warnings)
    if any(c.spec.source == "unknown" for c in state.hand):
        warnings.append("存在未知卡牌：按保守默认值处理")
    if isinstance(action, PlayCard):
        card = state.hand[action.hand_index] if action.hand_index < len(state.hand) else None
        if card and card.spec.source == "unknown":
            warnings.append(f"{card.card_id} 不在卡牌库中，建议仅作参考")

    return Advice(
        action=action,
        label=label,
        score=result.score,
        sequence=result.sequence,
        sequence_labels=result.labels,
        reasons=reasons,
        alternatives=result.alternatives,
        warnings=tuple(dict.fromkeys(warnings)),
        stats=result.stats,
        estimated=estimated,
    )


def advice_to_dict(advice: Advice) -> dict:
    def action_dict(action: Action) -> dict:
        if isinstance(action, EndTurn):
            return {"type": "end_turn"}
        return {
            "type": "play_card",
            "hand_index": action.hand_index,
            "target_index": action.target_index,
        }

    return {
        "action": action_dict(advice.action),
        "label": advice.label,
        "score": advice.score,
        "sequence": [action_dict(a) for a in advice.sequence],
        "sequence_labels": list(advice.sequence_labels),
        "reasons": list(advice.reasons),
        "alternatives": [
            {
                "label": alt.label,
                "score": alt.score,
                "action": action_dict(alt.action),
                "player_hp": alt.player_hp,
                "enemy_hp": alt.enemy_hp,
                "damage_taken": alt.damage_taken,
                "hp_lost": alt.hp_lost,
                "lethal": alt.lethal,
            }
            for alt in advice.alternatives
        ],
        "warnings": list(advice.warnings),
        "stats": advice.stats,
        "solver": advice.stats.get("solver", "beam"),
        "complete": advice.stats.get("complete", True),
        "estimated": advice.estimated,
    }
