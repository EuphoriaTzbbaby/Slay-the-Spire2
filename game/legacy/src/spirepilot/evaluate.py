"""Heuristic evaluation for one-turn combat lines."""

from __future__ import annotations

import random
from dataclasses import dataclass

from .model import CombatState
from .rules import enemy_turn

# Values are intentionally provisional.  They are exposed in one place so the
# project can tune them with CEM/offline evaluation later.
POWER_VALUE = {
    "strength": 2.5,
    "dexterity": 2.0,
    "vulnerable": 1.5,
    "weak": 1.2,
    "frail": -1.0,
    "plating": 2.0,
    "metallicize": 1.5,
    "juggernaut": 2.0,
    "feel_no_pain": 1.5,
    "dark_embrace": 2.0,
    "corruption": 6.0,
    "barricade": 6.0,
    "demon_form": 5.0,
    "rupture": 1.5,
    "rage": 1.0,
    "flame_barrier": 1.0,
    "thorns": 1.0,
    "crimson_mantle": 1.0,
    "inferno": 1.0,
    "cruelty": 1.0,
}

PLAYER_WEIGHT = 10.0
ENEMY_HP_WEIGHT = 4.0
ENEMY_BLOCK_WEIGHT = 2.0
ENERGY_PENALTY = 0.10


@dataclass
class Evaluation:
    score: float
    player_hp: int
    enemy_hp: int
    damage_taken: int
    block_absorbed: int
    lethal: bool
    player_died: bool
    notes: tuple[str, ...]


def _power_score(target) -> float:
    score = 0.0
    for key, amount in target.powers.items():
        if not amount:
            continue
        score += POWER_VALUE.get(key, 0.5) * amount
    return score


def evaluate_after_enemy(state: CombatState, rng: random.Random | None = None) -> Evaluation:
    """Score a player-turn state by resolving the visible enemy turn."""

    rng = rng or random.Random(0)
    after, enemy_report = enemy_turn(state, rng)

    if not after.player.alive:
        return Evaluation(
            score=-1_000_000_000.0,
            player_hp=0,
            enemy_hp=sum(e.hp for e in after.enemies if e.alive),
            damage_taken=enemy_report.damage_taken,
            block_absorbed=enemy_report.block_absorbed,
            lethal=False,
            player_died=True,
            notes=("预计玩家死亡",),
        )

    alive = after.alive_enemies
    enemy_hp = sum(e.hp for e in alive)
    enemy_block = sum(e.block for e in alive)
    lethal = not alive

    if lethal:
        score = 1_000_000.0 + after.player.hp * PLAYER_WEIGHT
    else:
        score = after.player.hp * PLAYER_WEIGHT
        score -= enemy_hp * ENEMY_HP_WEIGHT
        score -= enemy_block * ENEMY_BLOCK_WEIGHT
        score += _power_score(after.player)
        for enemy in alive:
            # Enemy buffs/debuffs are scored from the player's perspective.
            for key, amount in enemy.powers.items():
                if not amount:
                    continue
                if key in {"vulnerable", "weak", "frail"}:
                    score += POWER_VALUE.get(key, 0.5) * amount
                elif key in {"strength", "dexterity", "plating", "metallicize"}:
                    score -= POWER_VALUE.get(key, 0.5) * amount
                else:
                    score -= 0.2 * amount
        score += after.player.block * 0.5
        score -= max(0, state.player.energy) * ENERGY_PENALTY

    notes: list[str] = []
    if enemy_report.damage_taken:
        notes.append(
            f"敌方回合：受到 {enemy_report.damage_taken} 伤害，格挡吸收 {enemy_report.block_absorbed}"
        )
    if lethal:
        notes.append("预计本回合击杀全部敌人")
    if state.player.energy > 0:
        notes.append(f"剩余 {state.player.energy} 点能量未使用")

    return Evaluation(
        score=score,
        player_hp=after.player.hp,
        enemy_hp=enemy_hp,
        damage_taken=enemy_report.damage_taken,
        block_absorbed=enemy_report.block_absorbed,
        lethal=lethal,
        player_died=False,
        notes=tuple(notes),
    )
