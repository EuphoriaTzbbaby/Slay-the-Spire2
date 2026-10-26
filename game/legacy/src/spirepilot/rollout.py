"""Short-horizon combat rollout for the current first action.

This is a practical bridge between exact-turn search and full cross-turn search:
for each legal first action, finish the current turn with a small beam policy,
resolve the enemy turn, then play a few more turns with the same policy.  It is
still sampled/approx, but it catches decisions that look good for one turn and
bad for the next.
"""

from __future__ import annotations

import random
from dataclasses import dataclass

from .evaluate import Evaluation, evaluate_after_enemy
from .model import Action, CombatState, EndTurn, PlayCard, action_key
from .rules import end_turn, legal_actions, play_card
from .search import SearchConfig, action_label, sample_state, search


@dataclass
class RolloutLine:
    action: Action
    label: str
    hp: int
    enemy_hp: int
    lethal: bool
    score: float
    actions: tuple[Action, ...]
    labels: tuple[str, ...]
    turns: int


def _quality(line: RolloutLine) -> tuple:
    return (1 if line.lethal else 0, line.hp, -line.enemy_hp, -line.turns)


def _run_policy_turn(state: CombatState, rng: random.Random, policy_ms: int) -> tuple[CombatState, tuple[Action, ...], tuple[str, ...]]:
    """Finish one player turn with a small beam policy, then end the turn."""

    current = state
    actions: list[Action] = []
    labels: list[str] = []
    config = SearchConfig(max_depth=5, beam_width=8, time_budget_ms=policy_ms, samples=1)
    result = search(current, config)
    for action in result.sequence:
        if isinstance(action, EndTurn):
            break
        try:
            current, _ = play_card(current, action, rng)
        except (ValueError, IndexError):
            break
        actions.append(action)
        labels.append(action_label(state, action))
        state = current
    if not current.combat_over:
        current = end_turn(current, rng)
    return current, tuple(actions), tuple(labels)


def _one_line(root: CombatState, first: Action, turns: int, rng: random.Random, policy_ms: int) -> RolloutLine | None:
    current = root
    actions: list[Action] = []
    labels: list[str] = []
    if isinstance(first, PlayCard):
        try:
            current, _ = play_card(current, first, rng)
        except (ValueError, IndexError):
            return None
        actions.append(first)
        labels.append(action_label(root, first))
    elif isinstance(first, EndTurn):
        # Respect the first action: ending the turn immediately means the
        # policy does not get to play the current hand.
        current = end_turn(current, rng)
        actions.append(EndTurn())
        labels.append("结束回合")
    if current.combat_over:
        ev = evaluate_after_enemy(current, rng)
        return RolloutLine(first, action_label(root, first), ev.player_hp, ev.enemy_hp, ev.lethal, ev.score, tuple(actions), tuple(labels), 0)

    remaining = max(1, turns)
    future_turns = remaining - 1 if isinstance(first, EndTurn) else remaining
    for turn_index in range(max(0, future_turns)):
        current, turn_actions, turn_labels = _run_policy_turn(current, rng, policy_ms)
        actions.extend(turn_actions)
        labels.extend(turn_labels)
        labels.append("结束回合")
        actions.append(EndTurn())
    ev: Evaluation = evaluate_after_enemy(current, rng)
    return RolloutLine(
        action=first,
        label=action_label(root, first),
        hp=ev.player_hp,
        enemy_hp=ev.enemy_hp,
        lethal=ev.lethal,
        score=ev.score,
        actions=tuple(actions),
        labels=tuple(labels),
        turns=remaining,
    )


def rollout_rank(state: CombatState, turns: int = 2, samples: int = 1, policy_ms: int = 35) -> list[RolloutLine]:
    if state.combat_over:
        return []
    samples = max(1, samples)
    roots = legal_actions(state)
    scores: dict[tuple, list[float]] = {}
    examples: dict[tuple, RolloutLine] = {}
    for sample in range(samples):
        root, rng = sample_state(state, sample)
        for first in roots:
            line = _one_line(root, first, turns, rng, policy_ms)
            if line is None:
                continue
            key = action_key(first)
            scores.setdefault(key, []).append(line.score)
            if key not in examples or _quality(line) > _quality(examples[key]):
                examples[key] = line
    out: list[RolloutLine] = []
    for key, vals in scores.items():
        line = examples[key]
        # Rebuild with the mean score for display.
        out.append(
            RolloutLine(
                action=line.action,
                label=line.label,
                hp=line.hp,
                enemy_hp=line.enemy_hp,
                lethal=line.lethal,
                score=sum(vals) / len(vals),
                actions=line.actions,
                labels=line.labels,
                turns=line.turns,
            )
        )
    return sorted(out, key=_quality, reverse=True)


def rollout_best(state: CombatState, turns: int = 2, samples: int = 1, policy_ms: int = 35) -> RolloutLine | None:
    lines = rollout_rank(state, turns=turns, samples=samples, policy_ms=policy_ms)
    return lines[0] if lines else None
