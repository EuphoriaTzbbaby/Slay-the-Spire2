"""Audit a raw Communication_Mod_STS2 payload before trusting advice.

The mod's exported schema is not a formal API.  This module reports which
fields are present, which are missing, and whether the advisor can run in a
normal or degraded mode.  It is designed to be run on a single captured state
during D1-D3 integration.
"""

from __future__ import annotations

from dataclasses import dataclass, field
from typing import Any, Mapping

from .cards import normalize_card_id


@dataclass
class AuditCheck:
    name: str
    status: str  # ok | warn | missing | na
    detail: str = ""


@dataclass
class AuditReport:
    kind: str
    trigger: str
    verdict: str
    checks: list[AuditCheck] = field(default_factory=list)
    stats: dict[str, Any] = field(default_factory=dict)
    warnings: list[str] = field(default_factory=list)

    def add(self, name: str, status: str, detail: str = "") -> None:
        self.checks.append(AuditCheck(name, status, detail))

    def warn(self, text: str) -> None:
        if text not in self.warnings:
            self.warnings.append(text)

    def to_dict(self) -> dict[str, Any]:
        return {
            "kind": self.kind,
            "trigger": self.trigger,
            "verdict": self.verdict,
            "checks": [
                {"name": c.name, "status": c.status, "detail": c.detail}
                for c in self.checks
            ],
            "stats": self.stats,
            "warnings": self.warnings,
        }

    def render(self) -> str:
        icon = {"ok": "OK", "warn": "WARN", "missing": "MISS", "na": "N/A"}
        lines = [
            "SpirePilot 状态审计",
            "=" * 48,
            f"类型：{self.kind}    触发：{self.trigger or '?'}    结论：{self.verdict}",
            "",
        ]
        for check in self.checks:
            suffix = f"  —— {check.detail}" if check.detail else ""
            lines.append(f"[{icon.get(check.status, '?'):4}] {check.name}{suffix}")
        if self.stats:
            lines.append("")
            lines.append("统计：")
            for key, value in self.stats.items():
                lines.append(f"  - {key}: {value}")
        if self.warnings:
            lines.append("")
            lines.append("警告：")
            for warning in self.warnings:
                lines.append(f"  - {warning}")
        return "\n".join(lines)


def _has(mapping: Any, key: str) -> bool:
    return isinstance(mapping, dict) and key in mapping


def _list(value: Any) -> list:
    return value if isinstance(value, list) else []


def _card_id(item: Any) -> str:
    if not isinstance(item, dict):
        return ""
    return normalize_card_id(
        item.get("Card_Name") or item.get("CardName") or item.get("CardId") or ""
    )


def audit_state(raw: Any) -> AuditReport:
    if not isinstance(raw, dict):
        report = AuditReport(kind="unknown", trigger="", verdict="blocked")
        report.add("payload", "missing", "顶层不是 JSON object")
        return report

    trigger = str(raw.get("Trigger") or "")
    if isinstance(raw.get("Player"), dict) and isinstance(raw.get("Piles"), dict) and isinstance(raw.get("Enemies"), list):
        return _audit_combat(raw, trigger)
    if isinstance(raw.get("Rewards"), list):
        return _audit_rewards(raw, trigger)
    report = AuditReport(kind="non_combat", trigger=trigger, verdict="non_combat")
    report.add("payload", "na", "不是战斗状态；bridge 会记录但不决策")
    report.stats["top_level_keys"] = ", ".join(sorted(str(k) for k in raw.keys()))
    return report


def _audit_combat(raw: Mapping[str, Any], trigger: str) -> AuditReport:
    report = AuditReport(kind="combat", trigger=trigger, verdict="ready")
    player = raw.get("Player") if isinstance(raw.get("Player"), dict) else {}
    piles = raw.get("Piles") if isinstance(raw.get("Piles"), dict) else {}
    enemies = _list(raw.get("Enemies"))
    catalog = _list(raw.get("CardCatalog"))

    # Player core fields.
    if player:
        report.add("Player", "ok")
    else:
        report.add("Player", "missing")
        report.warn("缺少 Player，无法计算战斗")

    for key in ("CurrentHp", "MaxHp", "Energy"):
        if _has(player, key):
            report.add(f"Player.{key}", "ok", str(player.get(key)))
        else:
            report.add(f"Player.{key}", "missing")
            report.warn(f"缺少 Player.{key}，建议不可用")
    if _has(player, "Block"):
        report.add("Player.Block", "ok", str(player.get("Block")))
    else:
        report.add("Player.Block", "warn", "按 0 估算，攻防估值会偏差")
        report.warn("Player.Block 缺失：当前版本会按 0 估算，建议本地扩展 scanner")
    for key in ("Powers", "Relics"):
        report.add(f"Player.{key}", "ok" if _has(player, key) else "warn")

    # Piles.
    for key in ("HandPile", "DrawPile", "DiscardPile", "ExhaustPile"):
        if _has(piles, key) and isinstance(piles.get(key), list):
            report.add(f"Piles.{key}", "ok", f"{len(piles.get(key))} 张")
        else:
            report.add(f"Piles.{key}", "missing")
            report.warn(f"缺少 Piles.{key}，手牌/抽牌/弃牌状态不完整")

    hand = _list(piles.get("HandPile"))
    draw = _list(piles.get("DrawPile"))
    discard = _list(piles.get("DiscardPile"))
    exhaust = _list(piles.get("ExhaustPile"))
    hand_ids = [_card_id(c) for c in hand]
    hand_valid = [cid for cid in hand_ids if cid]
    report.stats.update(
        {
            "hand": len(hand),
            "draw": len(draw),
            "discard": len(discard),
            "exhaust": len(exhaust),
            "enemies": len(enemies),
            "catalog": len(catalog),
        }
    )
    if len(hand_valid) != len(hand):
        report.add("HandPile 卡牌字段", "warn", f"{len(hand) - len(hand_valid)} 张缺少 Card_Name")
        report.warn("部分手牌缺少 Card_Name，已跳过")

    # Hand-level fields.  The current upstream schema usually only has names.
    cost_missing = sum(1 for c in hand if isinstance(c, dict) and "Cost" not in c)
    playable_missing = sum(1 for c in hand if isinstance(c, dict) and "CanPlay" not in c)
    upgrade_missing = sum(1 for c in hand if isinstance(c, dict) and "IsUpgraded" not in c)
    if cost_missing:
        report.add("手牌 Cost", "warn", f"{cost_missing}/{len(hand)} 张缺字段；用 CardCatalog/静态库补")
        report.warn("手牌缺少 Cost/CanPlay 字段：合法动作按费用/目标推断，条件卡可能误判")
    else:
        report.add("手牌 Cost", "ok")
    if playable_missing:
        report.add("手牌 CanPlay", "warn", f"{playable_missing}/{len(hand)} 张缺字段；合法动作按费用/目标推断")
    else:
        report.add("手牌 CanPlay", "ok")
    if upgrade_missing:
        report.add("手牌 IsUpgraded", "warn", f"{upgrade_missing}/{len(hand)} 张缺字段；升级牌可能按基础值")
    else:
        report.add("手牌 IsUpgraded", "ok")

    # Catalog coverage.
    if catalog:
        catalog_ids = {
            normalize_card_id(item.get("CardId") or "")
            for item in catalog
            if isinstance(item, dict)
        }
        covered = sum(1 for cid in hand_valid if cid in catalog_ids)
        report.add("CardCatalog", "ok", f"{len(catalog)} 条，覆盖手牌 {covered}/{len(hand_valid)}")
        report.stats["catalog_hand_coverage"] = f"{covered}/{len(hand_valid)}"
        if covered < len(hand_valid):
            report.warn("CardCatalog 未覆盖全部手牌，部分卡将使用静态库/默认值")
    else:
        report.add("CardCatalog", "warn", "缺失；完全依赖静态库")
        report.warn("CardCatalog 缺失：费用/伤害/格挡可能不是当前版本数值")

    # Enemies.
    if enemies:
        report.add("Enemies", "ok", f"{len(enemies)} 个")
    else:
        report.add("Enemies", "missing")
        report.warn("Enemies 为空，无法给出战斗建议")
    block_missing = 0
    no_intent = 0
    non_attack_intent = 0
    for enemy in enemies:
        if not isinstance(enemy, dict):
            continue
        if "Block" not in enemy:
            block_missing += 1
        intents = _list(enemy.get("Intents"))
        if not intents:
            no_intent += 1
        for intent in intents:
            if isinstance(intent, dict):
                kind = str(intent.get("Type") or "Unknown").lower()
                if "attack" not in kind or int(intent.get("Damage") or 0) <= 0:
                    non_attack_intent += 1
    if block_missing:
        report.add("敌人 Block", "warn", f"{block_missing}/{len(enemies)} 个缺字段；按 0 估算")
        report.warn("敌人 Block 缺失：可能高估斩杀线")
    else:
        report.add("敌人 Block", "ok")
    if no_intent:
        report.add("敌人意图", "warn", f"{no_intent}/{len(enemies)} 个无意图")
    else:
        report.add("敌人意图", "ok", f"非攻击意图 {non_attack_intent} 条")
    if non_attack_intent:
        report.warn("存在非攻击意图，当前前向模型主要处理攻击/格挡")

    # Index consistency: the mod exports Index, but commands use list position.
    indices = []
    for enemy in enemies:
        if isinstance(enemy, dict) and "Index" in enemy:
            try:
                indices.append(int(enemy["Index"]))
            except (TypeError, ValueError):
                pass
    if len(indices) == len(enemies):
        if sorted(indices) != list(range(len(enemies))):
            report.add("敌人索引", "warn", f"Index={indices}，与列表位置可能不一致")
            report.warn("敌人 Index 与列表位置不一致，TargetIndex 可能打错目标")
        else:
            report.add("敌人索引", "ok")

    # Run context.
    first = raw.get("FirstInfo") if isinstance(raw.get("FirstInfo"), dict) else {}
    run = first.get("RunStatus") if isinstance(first.get("RunStatus"), dict) else {}
    if run:
        report.add(
            "RunStatus",
            "ok",
            f"{run.get('Character', '?')} Act {run.get('Act', '?')} Floor {run.get('Floor', '?')}",
        )
    else:
        report.add("RunStatus", "warn", "缺 FirstInfo.RunStatus；角色/幕数/楼层未知")

    # Command capability.
    report.add("命令通道", "warn", "仅确认 PlayCard / EndTurn；药水/奖励选择未开放")
    report.add("游戏版本", "warn", "状态本身不含版本号；需外部记录（如 v0.107.1）")

    # Verdict.
    if not player or not piles or not enemies:
        report.verdict = "blocked"
    elif block_missing or cost_missing or not catalog:
        report.verdict = "degraded"
    else:
        report.verdict = "ready"
    return report


def _audit_rewards(raw: Mapping[str, Any], trigger: str) -> AuditReport:
    report = AuditReport(kind="reward", trigger=trigger, verdict="non_combat")
    rewards = _list(raw.get("Rewards"))
    card_options = []
    for reward in rewards:
        if not isinstance(reward, dict):
            continue
        if str(reward.get("Type") or "").lower() == "card":
            card_options.extend(reward.get("Options") or [])
    report.add("Rewards", "ok", f"{len(rewards)} 个奖励项")
    report.add("卡牌选项", "ok" if card_options else "warn", f"{len(card_options)} 张")
    report.stats["rewards"] = len(rewards)
    report.stats["card_options"] = len(card_options)
    report.add("选择命令", "missing", "当前上游命令通道无 SelectReward；只能建议/人工点击")
    report.warn("奖励界面可读但不可自动选择；选牌推荐先做 copilot")
    return report
