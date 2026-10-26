"""Beam search over one player turn.

The advisor does not claim to be an exact solver.  It enumerates legal actions,
plays short sequences on a forward model, resolves the visible enemy turn, and
keeps the best lines under a small time budget.  The default is intentionally
small enough to run inside a local bridge without blocking the game.
"""

from __future__ import annotations

import hashlib
import random
import time
from dataclasses import dataclass, field

from .evaluate import evaluate_after_enemy
from .model import (
    Action,
    CombatState,
    EndTurn,
    action_key,
)
from .rules import (
    legal_actions,
    play_card,
    state_fingerprint,
    target_mode,
)


@dataclass(frozen=True)
class SearchConfig:
    max_depth: int = 5
    beam_width: int = 18
    time_budget_ms: int = 650
    samples: int = 2
    include_end_turn: bool = True


@dataclass(frozen=True)
class Alternative:
    action: Action
    label: str
    score: float
    player_hp: int | None = None
    enemy_hp: int | None = None
    damage_taken: int | None = None
    hp_lost: int | None = None
    lethal: bool = False


@dataclass
class SearchResult:
    action: Action
    score: float
    sequence: tuple[Action, ...]
    labels: tuple[str, ...]
    alternatives: tuple[Alternative, ...] = ()
    warnings: tuple[str, ...] = ()
    stats: dict = field(default_factory=dict)
    sample: int = 0
    solver: str = "beam"
    complete: bool = True


@dataclass
class _Node:
    state: CombatState
    actions: tuple[Action, ...]
    labels: tuple[str, ...]
    first_key: tuple | None
    score: float


def action_label(state: CombatState, action: Action) -> str:
    if isinstance(action, EndTurn):
        return "结束回合"
    if action.hand_index < 0 or action.hand_index >= len(state.hand):
        return "?"
    card = state.hand[action.hand_index]
    mode = target_mode(card)
    if mode == "all":
        return f"{card.display_name}（全体）"
    if mode == "random":
        return f"{card.display_name}（随机目标）"
    if mode == "enemy" and action.target_index is not None:
        enemy = state.enemy(action.target_index)
        return f"{card.display_name} -> {enemy.name if enemy else '?'}"
    return card.display_name


def _seed_for(state: CombatState, sample: int) -> int:
    digest = hashlib.md5((state_fingerprint(state) + f"#{sample}").encode("utf-8")).hexdigest()
    return int(digest[:12], 16)


def _shuffle_for_sample(state: CombatState, sample: int) -> tuple[CombatState, random.Random]:
    rng = random.Random(_seed_for(state, sample))
    clone = state.clone()
    if clone.draw_pile:
        rng.shuffle(clone.draw_pile)
    return clone, rng


def _is_timeout(started: float, budget_s: float) -> bool:
    return (time.perf_counter() - started) >= budget_s


def _beam(
    root: CombatState,
    rng: random.Random,
    config: SearchConfig,
    budget_s: float,
) -> tuple[SearchResult, dict[tuple, float], dict[tuple, str], dict[tuple, Action], int]:
    started = time.perf_counter()
    frontier: list[_Node] = [
        _Node(state=root, actions=(), labels=(), first_key=None, score=0.0)
    ]
    best: _Node | None = None
    root_scores: dict[tuple, float] = {}
    root_labels: dict[tuple, str] = {}
    root_actions: dict[tuple, Action] = {}
    nodes = 0

    def record_root(action: Action, label: str, score: float) -> None:
        key = action_key(action)
        if key not in root_scores or score > root_scores[key]:
            root_scores[key] = score
            root_labels[key] = label
            root_actions[key] = action

    def improve_root(key: tuple, score: float) -> None:
        if key not in root_scores or score > root_scores[key]:
            root_scores[key] = score

    for depth in range(max(1, config.max_depth)):
        next_frontier: list[_Node] = []
        for node in frontier:
            if node.state.combat_over:
                continue
            actions = legal_actions(node.state)
            for action in actions:
                if isinstance(action, EndTurn) and not config.include_end_turn:
                    continue
                nodes += 1
                if depth > 0 and _is_timeout(started, budget_s):
                    break
                if isinstance(action, EndTurn):
                    evaluation = evaluate_after_enemy(node.state, rng)
                    sequence = node.actions + (action,)
                    labels = node.labels + ("结束回合",)
                    first = node.first_key or action.key
                    candidate = _Node(node.state, sequence, labels, first, evaluation.score)
                    if depth == 0:
                        record_root(action, "结束回合", evaluation.score)
                    else:
                        improve_root(first, evaluation.score)
                else:
                    try:
                        next_state, _report = play_card(node.state, action, rng)
                    except ValueError:
                        continue
                    evaluation = evaluate_after_enemy(next_state, rng)
                    label = action_label(node.state, action)
                    sequence = node.actions + (action,)
                    labels = node.labels + (label,)
                    first = node.first_key or action.key
                    candidate = _Node(next_state, sequence, labels, first, evaluation.score)
                    if depth == 0:
                        record_root(action, label, evaluation.score)
                    else:
                        improve_root(first, evaluation.score)
                    if not next_state.combat_over and depth + 1 < config.max_depth:
                        next_frontier.append(candidate)
                if best is None or candidate.score > best.score:
                    best = candidate
                if depth == 0 and _is_timeout(started, budget_s):
                    break
            if depth == 0 and _is_timeout(started, budget_s):
                break

        if not next_frontier:
            break

        # Deduplicate equivalent states, keeping the best-scoring line.
        dedup: dict[str, _Node] = {}
        for node in next_frontier:
            key = state_fingerprint(node.state)
            previous = dedup.get(key)
            if previous is None or node.score > previous.score:
                dedup[key] = node
        frontier = sorted(dedup.values(), key=lambda n: n.score, reverse=True)[
            : max(1, config.beam_width)
        ]
        if _is_timeout(started, budget_s):
            break

    if best is None:
        evaluation = evaluate_after_enemy(root, rng)
        best = _Node(root, (EndTurn(),), ("结束回合",), ("end_turn",), evaluation.score)
        record_root(EndTurn(), "结束回合", evaluation.score)

    elapsed_ms = (time.perf_counter() - started) * 1000.0
    result = SearchResult(
        action=best.actions[0] if best.actions else EndTurn(),
        score=best.score,
        sequence=best.actions,
        labels=best.labels,
        stats={"nodes": nodes, "elapsed_ms": round(elapsed_ms, 1), "depth": len(best.actions)},
    )
    return result, root_scores, root_labels, root_actions, nodes


def sample_state(state: CombatState, sample: int) -> tuple[CombatState, random.Random]:
    """Return a clone with a deterministic draw-order sample and its RNG."""

    return _shuffle_for_sample(state, sample)


def search(state: CombatState, config: SearchConfig | None = None) -> SearchResult:
    config = config or SearchConfig()
    if state.combat_over:
        return SearchResult(
            action=EndTurn(),
            score=-1_000_000_000.0 if not state.player.alive else 1_000_000.0,
            sequence=(),
            labels=(),
            warnings=state.warnings,
            stats={"nodes": 0, "elapsed_ms": 0.0, "depth": 0},
        )

    samples = max(1, int(config.samples))
    per_sample_budget = max(0.02, (config.time_budget_ms / 1000.0) / samples)
    results: list[SearchResult] = []
    all_root_scores: dict[tuple, list[float]] = {}
    root_labels: dict[tuple, str] = {}
    root_actions: dict[tuple, Action] = {}
    total_nodes = 0

    for sample in range(samples):
        root, rng = _shuffle_for_sample(state, sample)
        result, root_scores, labels, actions, nodes = _beam(
            root, rng, config, per_sample_budget
        )
        result.sample = sample
        results.append(result)
        total_nodes += nodes
        for key, score in root_scores.items():
            all_root_scores.setdefault(key, []).append(score)
        root_labels.update(labels)
        root_actions.update(actions)

    # Aggregate root-action scores across draw-order samples.
    mean_scores = {key: sum(vals) / len(vals) for key, vals in all_root_scores.items() if vals}
    if not mean_scores:
        return SearchResult(
            action=EndTurn(),
            score=results[0].score if results else 0.0,
            sequence=(EndTurn(),),
            labels=("结束回合",),
            warnings=state.warnings,
            stats={"nodes": total_nodes, "samples": samples},
            sample=results[0].sample if results else 0,
        )

    best_key = max(mean_scores, key=lambda k: (mean_scores[k], max(all_root_scores.get(k, [0]))))
    # Prefer the sample that actually found the best line for this root action.
    candidates = [r for r in results if r.sequence and action_key(r.sequence[0]) == best_key]
    chosen = max(candidates, key=lambda r: r.score) if candidates else max(results, key=lambda r: r.score)
    # The chosen sequence must define the action; using root_actions here can
    # mismatch when the best line for the highest-mean root comes from another
    # sample/line (a real bug seen in live STS2MCP play).
    best_action = chosen.sequence[0] if chosen.sequence else chosen.action
    chosen_key = action_key(best_action)
    if chosen_key != best_key:
        best_key = chosen_key

    ranked = sorted(mean_scores.items(), key=lambda item: item[1], reverse=True)
    alternatives = tuple(
        Alternative(
            action=root_actions.get(key, EndTurn()),
            label=root_labels.get(key, "?"),
            score=round(score, 3),
        )
        for key, score in ranked[:5]
    )

    warnings = list(state.warnings)
    if any(c.spec.source == "unknown" for c in state.hand):
        warnings.append("手牌中存在未知卡牌，建议结果可能不准")
    if any(not e.intents for e in state.alive_enemies):
        warnings.append("部分敌人意图缺失，敌方回合为近似估计")
    warnings.append("前向模型未覆盖全部卡牌/遗物/药水；结果为近似建议，请以游戏内实际效果为准")

    elapsed = sum(float(r.stats.get("elapsed_ms", 0.0)) for r in results)
    return SearchResult(
        action=best_action,
        score=round(mean_scores.get(best_key, chosen.score), 3),
        sequence=chosen.sequence,
        labels=chosen.labels,
        alternatives=alternatives,
        warnings=tuple(dict.fromkeys(warnings)),
        stats={
            "nodes": total_nodes,
            "samples": samples,
            "elapsed_ms": round(elapsed, 1),
            "depth": chosen.stats.get("depth", len(chosen.sequence)),
        },
        sample=chosen.sample,
    )
