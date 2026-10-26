"""Golden-trace replay and comparison.

A trace is a small JSON file with an initial state and a sequence of actions,
each optionally followed by the state observed in the real game.  Replaying it
through the forward model and diffing the snapshots is the fastest way to find
rules bugs without branching the live game.
"""

from __future__ import annotations

import json
import random
from dataclasses import dataclass, field
from pathlib import Path
from typing import Any, Mapping

from .adapter import parse_combat_state
from .cards import CardDatabase, normalize_card_id
from .model import Action, CombatState, EndTurn, PlayCard
from .rules import end_turn, play_card
from .search import action_label


@dataclass
class TraceDiff:
    field: str
    expected: Any
    actual: Any
    severity: str = "error"

    def to_dict(self) -> dict:
        return {
            "field": self.field,
            "expected": self.expected,
            "actual": self.actual,
            "severity": self.severity,
        }


@dataclass
class StepResult:
    index: int
    action_label: str
    diffs: list[TraceDiff] = field(default_factory=list)
    warnings: list[str] = field(default_factory=list)
    error: str | None = None

    @property
    def errors(self) -> int:
        return sum(1 for d in self.diffs if d.severity == "error")

    @property
    def ok(self) -> bool:
        return self.error is None and self.errors == 0

    def to_dict(self) -> dict:
        return {
            "index": self.index,
            "action": self.action_label,
            "ok": self.ok,
            "errors": self.errors,
            "diffs": [d.to_dict() for d in self.diffs],
            "warnings": self.warnings,
            "error": self.error,
        }


@dataclass
class TraceResult:
    name: str
    steps: list[StepResult] = field(default_factory=list)
    warnings: list[str] = field(default_factory=list)
    error: str | None = None

    @property
    def errors(self) -> int:
        return sum(s.errors for s in self.steps)

    @property
    def passed(self) -> bool:
        return self.error is None and all(s.ok for s in self.steps)

    def to_dict(self) -> dict:
        return {
            "name": self.name,
            "passed": self.passed,
            "errors": self.errors,
            "warnings": self.warnings,
            "steps": [s.to_dict() for s in self.steps],
        }

    def render(self) -> str:
        lines = [
            f"Golden trace: {self.name}",
            "=" * 48,
        ]
        if self.error:
            lines.append(f"FATAL: {self.error}")
        for step in self.steps:
            status = "PASS" if step.ok else "FAIL"
            lines.append(f"[{status}] step {step.index + 1}: {step.action_label}")
            if step.error:
                lines.append(f"  error: {step.error}")
            for diff in step.diffs[:12]:
                lines.append(
                    f"  {diff.severity.upper():5} {diff.field}: expected={diff.expected!r} actual={diff.actual!r}"
                )
            for warning in step.warnings[:4]:
                lines.append(f"  warn: {warning}")
        lines.append("")
        lines.append(
            f"结果：{'PASS' if self.passed else 'FAIL'}；"
            f"步骤 {len(self.steps)}，error diff {self.errors}"
        )
        return "\n".join(lines)


def parse_action(raw: Any) -> Action:
    if not isinstance(raw, dict):
        raise ValueError("action must be an object")
    kind = str(raw.get("Type") or raw.get("type") or "").strip().lower()
    if kind == "endturn" or kind == "end_turn":
        return EndTurn()
    if kind in {"playcard", "play_card"}:
        if "HandIndex" not in raw and "hand_index" not in raw:
            raise ValueError("PlayCard action missing HandIndex")
        hand_index = int(raw.get("HandIndex", raw.get("hand_index")))
        target_raw = raw.get("TargetIndex", raw.get("target_index"))
        target_index = int(target_raw) if target_raw is not None and int(target_raw) >= 0 else None
        return PlayCard(hand_index, target_index)
    raise ValueError(f"unknown action type: {raw.get('Type')!r}")


def _card_ids(cards) -> list[str]:
    return sorted(c.card_id for c in cards)


def _powers(cards) -> dict[str, int]:
    return {k: v for k, v in sorted(cards.items()) if v}


def _expected_has(raw: Mapping[str, Any], section: str, key: str) -> bool:
    part = raw.get(section)
    return isinstance(part, dict) and key in part


def _compare(
    actual: CombatState,
    expected: CombatState,
    expected_raw: Mapping[str, Any],
) -> tuple[list[TraceDiff], list[str]]:
    diffs: list[TraceDiff] = []
    warnings: list[str] = []

    def add(field: str, exp: Any, act: Any, severity: str = "error") -> None:
        if exp != act:
            diffs.append(TraceDiff(field, exp, act, severity))

    add("Player.hp", expected.player.hp, actual.player.hp)
    add("Player.energy", expected.player.energy, actual.player.energy)
    if _expected_has(expected_raw, "Player", "Block"):
        add("Player.block", expected.player.block, actual.player.block)
    else:
        warnings.append("期望状态未导出 Player.Block，跳过格挡对比")
    if _expected_has(expected_raw, "Player", "Powers"):
        add("Player.powers", _powers(expected.player.powers), _powers(actual.player.powers), "warn")

    if _expected_has(expected_raw, "Piles", "HandPile"):
        add("Piles.hand", _card_ids(expected.hand), _card_ids(actual.hand))
    if _expected_has(expected_raw, "Piles", "DrawPile"):
        add("Piles.draw", _card_ids(expected.draw_pile), _card_ids(actual.draw_pile), "warn")
    if _expected_has(expected_raw, "Piles", "DiscardPile"):
        add("Piles.discard", _card_ids(expected.discard_pile), _card_ids(actual.discard_pile), "warn")
    if _expected_has(expected_raw, "Piles", "ExhaustPile"):
        add("Piles.exhaust", _card_ids(expected.exhaust_pile), _card_ids(actual.exhaust_pile), "warn")

    expected_enemies = {e.index: e for e in expected.enemies}
    actual_enemies = {e.index: e for e in actual.enemies}
    if set(expected_enemies) != set(actual_enemies):
        add("Enemies.count", sorted(expected_enemies), sorted(actual_enemies))
    for idx in sorted(set(expected_enemies) & set(actual_enemies)):
        exp = expected_enemies[idx]
        act = actual_enemies[idx]
        add(f"Enemy[{idx}].hp", exp.hp, act.hp)
        # Find whether the raw enemy had a Block key.
        raw_enemy = None
        raw_enemies = expected_raw.get("Enemies")
        if isinstance(raw_enemies, list) and idx < len(raw_enemies) and isinstance(raw_enemies[idx], dict):
            raw_enemy = raw_enemies[idx]
        if raw_enemy is not None and "Block" in raw_enemy:
            add(f"Enemy[{idx}].block", exp.block, act.block)
        if raw_enemy is not None and "Powers" in raw_enemy:
            add(f"Enemy[{idx}].powers", _powers(exp.powers), _powers(act.powers), "warn")

    if "TurnNumber" in expected_raw:
        add("TurnNumber", expected.turn, actual.turn, "warn")
    return diffs, warnings


def replay_trace(trace: Mapping[str, Any], db: CardDatabase, *, seed: int = 0) -> TraceResult:
    name = str(trace.get("name") or "unnamed")
    result = TraceResult(name=name)
    initial = trace.get("initial_state") or trace.get("state")
    if not isinstance(initial, dict):
        result.error = "missing initial_state"
        return result
    try:
        current = parse_combat_state(initial, db)
    except Exception as exc:  # noqa: BLE001 - trace runner should report, not crash
        result.error = f"initial_state parse failed: {exc}"
        return result

    steps = trace.get("steps") or []
    if not isinstance(steps, list):
        result.error = "steps must be a list"
        return result

    for i, step in enumerate(steps):
        if not isinstance(step, dict):
            result.steps.append(StepResult(i, "?", error="step must be an object"))
            continue
        try:
            action = parse_action(step.get("action"))
        except (TypeError, ValueError) as exc:
            result.steps.append(StepResult(i, "?", error=str(exc)))
            continue
        label = action_label(current, action)
        step_result = StepResult(i, label)
        rng = random.Random(seed + i)
        try:
            if isinstance(action, EndTurn):
                actual = end_turn(current, rng)
            else:
                actual, _report = play_card(current, action, rng)
        except (ValueError, IndexError) as exc:
            step_result.error = f"action failed: {exc}"
            result.steps.append(step_result)
            continue

        expected_raw = step.get("expected_state") or step.get("state_after")
        if isinstance(expected_raw, dict):
            try:
                expected = parse_combat_state(expected_raw, db)
            except Exception as exc:  # noqa: BLE001
                step_result.error = f"expected_state parse failed: {exc}"
                result.steps.append(step_result)
                continue
            diffs, warnings = _compare(actual, expected, expected_raw)
            step_result.diffs.extend(diffs)
            step_result.warnings.extend(warnings)
        else:
            step_result.warnings.append("没有 expected_state；只检查动作可执行")

        result.steps.append(step_result)
        current = actual
    return result


def load_trace(path: str | Path, db: CardDatabase, *, seed: int = 0) -> TraceResult:
    payload = json.loads(Path(path).read_text(encoding="utf-8"))
    return replay_trace(payload, db, seed=seed)
