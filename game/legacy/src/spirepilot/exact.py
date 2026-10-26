"""Exact (within the forward model) search for the current player turn.

Unlike the beam solver, this enumerates every legal card sequence up to a small
cap.  It is exact for a *fixed draw order*: hidden draw order is handled by
sampling and aggregating root actions, not by claiming true game-wide
optimality.

Objective (lexicographic):
    1. win / survive
    2. less damage taken
    3. less enemy HP
    4. fewer actions
    5. heuristic score
"""

from __future__ import annotations

import random
import time
from dataclasses import dataclass

from .evaluate import Evaluation, evaluate_after_enemy
from .model import Action, CombatState, EndTurn, PlayCard, action_key
from .rules import legal_actions, play_card
from .search import (
    Alternative,
    SearchConfig,
    SearchResult,
    action_label,
    sample_state,
)


@dataclass
class _Line:
    root_action: Action
    actions: tuple[Action, ...]
    labels: tuple[str, ...]
    evaluation: Evaluation


def _quality(line: _Line) -> tuple:
    ev = line.evaluation
    return (
        1 if ev.lethal else 0,
        ev.player_hp,
        -ev.enemy_hp,
        -len(line.actions),
        ev.score,
    )


def _better(candidate: _Line, current: _Line | None) -> bool:
    return current is None or _quality(candidate) > _quality(current)


def _one_sample(
    root: CombatState,
    rng: random.Random,
    config: SearchConfig,
    budget_s: float,
) -> tuple[_Line | None, dict[tuple, _Line], int, bool]:
    started = time.perf_counter()
    nodes = 0
    complete = True
    best: _Line | None = None
    root_best: dict[tuple, _Line] = {}

    def remember(line: _Line) -> None:
        nonlocal best
        key = action_key(line.root_action)
        if key not in root_best or _better(line, root_best[key]):
            root_best[key] = line
        if _better(line, best):
            best = line

    def timed_out() -> bool:
        nonlocal complete
        if time.perf_counter() - started >= budget_s:
            complete = False
            return True
        return False

    def visit(
        state: CombatState,
        actions: tuple[Action, ...],
        labels: tuple[str, ...],
        root_action: Action,
    ) -> None:
        nonlocal nodes
        if timed_out():
            return
        nodes += 1
        # Ending the turn here is always a legal option.
        ev = evaluate_after_enemy(state, rng)
        remember(_Line(root_action, actions + (EndTurn(),), labels + ("结束回合",), ev))
        if len(actions) >= max(1, config.max_depth):
            return
        for action in legal_actions(state):
            if isinstance(action, EndTurn):
                continue
            if timed_out():
                return
            try:
                next_state, _ = play_card(state, action, rng)
            except (ValueError, IndexError):
                continue
            label = action_label(state, action)
            visit(next_state, actions + (action,), labels + (label,), root_action)

    for action in legal_actions(root):
        if timed_out():
            break
        if isinstance(action, EndTurn):
            ev = evaluate_after_enemy(root, rng)
            remember(_Line(action, (action,), ("结束回合",), ev))
            continue
        try:
            next_state, _ = play_card(root, action, rng)
        except (ValueError, IndexError):
            continue
        label = action_label(root, action)
        visit(next_state, (action,), (label,), action)

    return best, root_best, nodes, complete


def exact_search(state: CombatState, config: SearchConfig | None = None) -> SearchResult:
    config = config or SearchConfig(max_depth=8, time_budget_ms=900, samples=1)
    if state.combat_over:
        return SearchResult(
            action=EndTurn(),
            score=-1_000_000_000.0 if not state.player.alive else 1_000_000.0,
            sequence=(),
            labels=(),
            warnings=state.warnings,
            stats={"solver": "exact", "nodes": 0, "elapsed_ms": 0.0, "complete": True},
            solver="exact",
            complete=True,
        )

    started = time.perf_counter()
    samples = max(1, int(config.samples))
    per_sample_budget = max(0.02, (config.time_budget_ms / 1000.0) / samples)
    results: list[tuple[_Line | None, dict[tuple, _Line], int, bool, int]] = []
    all_root_scores: dict[tuple, list[float]] = {}
    all_nodes = 0
    all_complete = True

    for sample in range(samples):
        root, rng = sample_state(state, sample)
        best, root_best, nodes, complete = _one_sample(root, rng, config, per_sample_budget)
        results.append((best, root_best, nodes, complete, sample))
        all_nodes += nodes
        all_complete = all_complete and complete
        for key, line in root_best.items():
            all_root_scores.setdefault(key, []).append(line.evaluation.score)

    mean_scores = {k: sum(v) / len(v) for k, v in all_root_scores.items() if v}
    if not mean_scores:
        return SearchResult(
            action=EndTurn(),
            score=0.0,
            sequence=(EndTurn(),),
            labels=("结束回合",),
            warnings=tuple(dict.fromkeys((*state.warnings, "exact 搜索未产生有效线路，降级为结束回合"))),
            stats={"solver": "exact", "nodes": all_nodes, "complete": False},
            solver="exact",
            complete=False,
        )

    # Select by the declared objective, not by the heuristic score alone.
    # The chosen action/labels/sequence all come from this one line.
    all_lines = [line for item in results for line in item[1].values()]
    chosen = max(all_lines, key=_quality)
    best_key = action_key(chosen.root_action)
    action = chosen.root_action

    alternatives: list[Alternative] = []
    for key, mean_score in sorted(mean_scores.items(), key=lambda kv: kv[1], reverse=True)[:6]:
        sample_line: _Line | None = None
        for item in results:
            line = item[1].get(key)
            if line is not None and (sample_line is None or _better(line, sample_line)):
                sample_line = line
        if sample_line is None:
            continue
        alternatives.append(
            Alternative(
                action=sample_line.root_action,
                label=sample_line.labels[0],
                score=round(mean_score, 3),
                player_hp=sample_line.evaluation.player_hp,
                enemy_hp=sample_line.evaluation.enemy_hp,
                damage_taken=sample_line.evaluation.damage_taken,
                hp_lost=sample_line.evaluation.damage_taken - sample_line.evaluation.block_absorbed,
                lethal=sample_line.evaluation.lethal,
            )
        )

    warnings = list(state.warnings)
    if not all_complete:
        warnings.append("exact 搜索超时：当前结果是最佳已发现线路，未证明模型内最优")
    if samples > 1:
        warnings.append(f"exact 使用 {samples} 个抽牌样本；结论是样本内最优")
    if samples == 1:
        warnings.append("exact 只使用 1 个抽牌样本；换抽牌顺序可能改变结论")
    warnings.append("前向模型未覆盖全部卡牌/遗物/药水；exact 是模型内最优，不是游戏全局最优")

    elapsed = (time.perf_counter() - started) * 1000.0
    return SearchResult(
        action=action,
        score=round(mean_scores.get(best_key, chosen.evaluation.score), 3),
        sequence=chosen.actions,
        labels=chosen.labels,
        alternatives=tuple(alternatives),
        warnings=tuple(dict.fromkeys(warnings)),
        stats={
            "solver": "exact",
            "nodes": all_nodes,
            "samples": samples,
            "complete": all_complete,
            "depth": len(chosen.actions),
            "elapsed_ms": round(elapsed, 1),
        },
        sample=chosen_line_sample(results, chosen),
        solver="exact",
        complete=all_complete,
    )


def chosen_line_sample(results, chosen: _Line) -> int:
    for _best, _root_best, _nodes, _complete, sample in results:
        if _best is chosen:
            return sample
    return 0
