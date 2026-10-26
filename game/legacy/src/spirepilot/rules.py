"""Combat rules used by the advisor.

This is a deliberately approximate forward model.  It models the parts that
matter for a one-turn recommendation: attack/block/draw/energy/status, common
Ironclad powers, and the enemy's visible intents.  Cards or powers that are not
modelled are still played with their direct stats and are marked as approximate
in the report.
"""

from __future__ import annotations

import random
from dataclasses import dataclass, field
from typing import Iterable

from .cards import unplayable
from .model import (
    Action,
    CardInstance,
    CombatState,
    EndTurn,
    EnemyState,
    PlayCard,
    normalize_power_id,
)

MAX_HAND = 10

_DEBUFF_POWERS = {"vulnerable", "weak", "frail"}
_SELF_POWERS = {
    "strength",
    "dexterity",
    "plating",
    "metallicize",
    "juggernaut",
    "feel_no_pain",
    "dark_embrace",
    "corruption",
    "barricade",
    "demon_form",
    "rupture",
    "rage",
    "flame_barrier",
    "thorns",
    "crimson_mantle",
    "inferno",
    "cruelty",
    "combust",
    "evolve",
}


@dataclass
class EffectReport:
    card_name: str = ""
    damage_dealt: int = 0
    block_gained: int = 0
    cards_drawn: int = 0
    energy_gained: int = 0
    hp_lost: int = 0
    killed: list[str] = field(default_factory=list)
    vulnerable_applied: int = 0
    weak_applied: int = 0
    strength_gained: int = 0
    notes: list[str] = field(default_factory=list)

    def add_note(self, text: str) -> None:
        if text and text not in self.notes:
            self.notes.append(text)


@dataclass
class EnemyTurnReport:
    damage_taken: int = 0
    block_absorbed: int = 0
    hp_lost: int = 0
    player_died: bool = False
    notes: list[str] = field(default_factory=list)

    def add_note(self, text: str) -> None:
        if text and text not in self.notes:
            self.notes.append(text)


def power_amount(target, *names: str) -> int:
    wanted = {normalize_power_id(n) for n in names}
    return sum(v for k, v in target.powers.items() if k in wanted)


def add_power(target, power_id: str, amount: int) -> None:
    key = normalize_power_id(power_id)
    if not key or not amount:
        return
    target.powers[key] = target.powers.get(key, 0) + int(amount)


def decay_power(target, power_id: str, amount: int = 1) -> None:
    key = normalize_power_id(power_id)
    value = target.powers.get(key, 0)
    if value <= 0:
        return
    left = value - amount
    if left > 0:
        target.powers[key] = left
    else:
        target.powers.pop(key, None)


def effective_cost(state: CombatState, card: CardInstance) -> int:
    if card.playable_override is False:
        return 999
    if card.cost_override is not None:
        return max(0, card.cost_override)
    if card.spec.x_cost:
        return 0
    if card.spec.is_skill and power_amount(state.player, "corruption") > 0:
        return 0
    return max(0, card.spec.cost)


def target_mode(card: CardInstance) -> str:
    target = (card.spec.target or "Self").strip().lower().replace("_", "")
    if target in {"allenemies", "allenemy"}:
        return "all"
    if target in {"randomenemy", "random"}:
        return "random"
    if target in {"anyenemy", "enemy"}:
        return "enemy"
    if target in {"anyally", "ally"}:
        return "ally"
    return "self"


def _alive_indices(state: CombatState) -> list[int]:
    return [e.index for e in state.alive_enemies]


def legal_actions(state: CombatState) -> list[Action]:
    if state.combat_over:
        return []
    actions: list[Action] = []
    targets = _alive_indices(state)
    for hand_index, card in enumerate(state.hand):
        if unplayable(card.card_id, card.spec):
            continue
        if card.playable_override is False:
            continue
        cost = effective_cost(state, card)
        if cost > state.player.energy:
            continue
        mode = target_mode(card)
        if mode == "enemy":
            for target_index in targets:
                actions.append(PlayCard(hand_index, target_index))
        elif mode in {"all", "random", "self", "ally"}:
            # All/random enemies and self/ally cards do not need a target.
            actions.append(PlayCard(hand_index, targets[0] if mode == "all" and targets else None))
    actions.append(EndTurn())
    # Preserve order but remove exact duplicates.
    seen: set[tuple] = set()
    unique: list[Action] = []
    for action in actions:
        if action.key in seen:
            continue
        seen.add(action.key)
        unique.append(action)
    return unique


def _attack_damage(base: int, strength: int = 0, weak: bool = False, vulnerable: bool = False) -> int:
    value = max(0, int(base) + int(strength))
    if weak:
        value = int(value * 0.75)
    if vulnerable:
        value = int(value * 1.5)
    return max(0, value)


def _deal_to_enemy(state: CombatState, enemy: EnemyState, amount: int, report: EffectReport) -> int:
    amount = max(0, int(amount))
    if amount <= 0 or not enemy.alive:
        return 0
    absorbed = min(enemy.block, amount)
    enemy.block -= absorbed
    hp_damage = amount - absorbed
    enemy.hp = max(0, enemy.hp - hp_damage)
    report.damage_dealt += amount
    if enemy.hp <= 0 and enemy.name not in report.killed:
        report.killed.append(enemy.name)
        report.add_note(f"击杀 {enemy.name}")
    return amount


def _juggernaut_trigger(state: CombatState, block_gained: int, report: EffectReport, rng: random.Random) -> None:
    stacks = power_amount(state.player, "juggernaut")
    if stacks <= 0 or block_gained <= 0:
        return
    alive = state.alive_enemies
    if not alive:
        return
    target = rng.choice(alive)
    _deal_to_enemy(state, target, stacks, report)
    report.add_note(f"Juggernaut 触发：对 {target.name} 造成 {stacks} 伤害")


def gain_block(state: CombatState, amount: int, report: EffectReport, rng: random.Random) -> int:
    if amount <= 0:
        return 0
    dex = power_amount(state.player, "dexterity")
    frail = power_amount(state.player, "frail") > 0
    value = max(0, int(amount) + dex)
    if frail:
        value = int(value * 0.75)
    state.player.block += value
    report.block_gained += value
    if power_amount(state.player, "juggernaut") > 0:
        _juggernaut_trigger(state, value, report, rng)
    return value


def _reshuffle_draw(state: CombatState, rng: random.Random, report: EffectReport) -> None:
    if state.draw_pile or not state.discard_pile:
        return
    state.draw_pile = list(state.discard_pile)
    state.discard_pile = []
    rng.shuffle(state.draw_pile)
    report.add_note("弃牌堆洗回抽牌堆")


def draw_cards(state: CombatState, count: int, report: EffectReport, rng: random.Random) -> int:
    drawn = 0
    for _ in range(max(0, int(count))):
        if len(state.hand) >= MAX_HAND:
            report.add_note("手牌已满，抽牌被跳过")
            break
        if not state.draw_pile:
            _reshuffle_draw(state, rng, report)
        if not state.draw_pile:
            break
        state.hand.append(state.draw_pile.pop(0))
        drawn += 1
    report.cards_drawn += drawn
    return drawn


def exhaust_card(state: CombatState, card: CardInstance, report: EffectReport, rng: random.Random) -> None:
    if card in state.hand:
        state.hand.remove(card)
    state.exhaust_pile.append(card)
    fnp = power_amount(state.player, "feel_no_pain")
    if fnp > 0:
        gain_block(state, fnp, report, rng)
        report.add_note(f"Feel No Pain：消耗触发 {fnp} 格挡")
    dark = power_amount(state.player, "dark_embrace")
    if dark > 0:
        draw_cards(state, dark, report, rng)
        report.add_note(f"Dark Embrace：消耗触发抽 {dark} 张")


def _apply_damage_to_targets(
    state: CombatState,
    card: CardInstance,
    action: PlayCard,
    base_damage: int,
    hits: int,
    report: EffectReport,
    rng: random.Random,
    strength: int,
    weak: bool,
    vulnerable: bool,
) -> None:
    mode = target_mode(card)
    if mode == "all":
        targets = list(state.alive_enemies)
    elif mode == "random":
        targets = [rng.choice(state.alive_enemies)] if state.alive_enemies else []
    elif mode == "enemy":
        enemy = state.enemy(action.target_index if action.target_index is not None else -1)
        targets = [enemy] if enemy and enemy.alive else []
    else:
        targets = []
    if not targets or base_damage <= 0 or hits <= 0:
        return
    for enemy in targets:
        for _ in range(max(1, hits)):
            if not enemy.alive:
                break
            dmg = _attack_damage(base_damage, strength, weak, vulnerable or enemy.power("vulnerable") > 0)
            _deal_to_enemy(state, enemy, dmg, report)


def _apply_debuff(
    state: CombatState,
    card: CardInstance,
    action: PlayCard,
    key: str,
    amount: int,
    report: EffectReport,
) -> None:
    if amount <= 0:
        return
    mode = target_mode(card)
    if mode == "all":
        targets = list(state.alive_enemies)
    elif mode == "enemy":
        enemy = state.enemy(action.target_index if action.target_index is not None else -1)
        targets = [enemy] if enemy and enemy.alive else []
    elif mode == "random":
        targets = [state.alive_enemies[0]] if state.alive_enemies else []
    else:
        targets = []
    for enemy in targets:
        add_power(enemy, key, amount)
        if key == "vulnerable":
            report.vulnerable_applied += amount
        elif key == "weak":
            report.weak_applied += amount


def _apply_self_powers(
    state: CombatState,
    card: CardInstance,
    report: EffectReport,
    skip: set[str] | None = None,
) -> None:
    skip = skip or set()
    for power in card.spec.powers:
        key = power.key
        if key in skip:
            continue
        if key in _DEBUFF_POWERS:
            # Debuffs are handled by _apply_debuff with target context.
            continue
        add_power(state.player, key, power.amount)
        if key == "strength":
            report.strength_gained += power.amount
        if key not in _SELF_POWERS:
            report.add_note(f"未建模能力 {power.power_id} {power.amount}（按估值计入）")


def _special_damage(state: CombatState, card: CardInstance, paid_energy: int) -> tuple[int, int, list[str]]:
    """Return (base_damage, hits, notes) for special attack cards."""

    spec = card.spec
    notes: list[str] = []
    damage = spec.damage
    hits = spec.hits
    cid = card.card_id.upper()

    if cid == "BODY_SLAM":
        damage = max(0, state.player.block)
        hits = 1
        notes.append(f"Body Slam 以当前格挡 {state.player.block} 作为伤害")
    elif cid == "PERFECTED_STRIKE":
        # Count other cards containing "Strike"; Perfected Strike itself also
        # contains the word, so add it back explicitly.
        strike_count = sum(1 for c in state.all_cards() if "STRIKE" in c.card_id.upper()) + 1
        damage = spec.damage + 2 * strike_count
        notes.append(f"Perfected Strike：牌组中 {strike_count} 张含 Strike 的牌，伤害 {damage}")
    elif cid == "WHIRLWIND":
        hits = max(0, paid_energy)
        notes.append(f"Whirlwind：消耗 {paid_energy} 点能量，攻击 {hits} 次")
    return damage, hits, notes


def play_card(state: CombatState, action: PlayCard, rng: random.Random | None = None) -> tuple[CombatState, EffectReport]:
    rng = rng or random.Random(0)
    if not isinstance(action, PlayCard):
        raise TypeError("play_card expects a PlayCard action")
    if action.hand_index < 0 or action.hand_index >= len(state.hand):
        raise ValueError("hand index out of range")

    next_state = state.clone()
    card = next_state.hand[action.hand_index]
    if unplayable(card.card_id, card.spec):
        raise ValueError(f"{card.card_id} is not playable")
    cost = effective_cost(next_state, card)
    if cost > next_state.player.energy:
        raise ValueError(f"not enough energy for {card.name}: {cost} > {next_state.player.energy}")

    report = EffectReport(card_name=card.display_name)
    paid_energy = next_state.player.energy if card.spec.x_cost else cost
    next_state.player.energy = max(0, next_state.player.energy - paid_energy)
    next_state.hand.pop(action.hand_index)

    # Some cards exhaust the hand before dealing damage.
    cid = card.card_id.upper()
    fiend_fire_exhausted = 0
    if cid == "FIEND_FIRE":
        others = list(next_state.hand)
        fiend_fire_exhausted = len(others)
        for other in others:
            exhaust_card(next_state, other, report, rng)
        report.add_note(f"Fiend Fire：消耗 {fiend_fire_exhausted} 张手牌")
        # The card itself is exhausted after resolving.
    elif cid == "SECOND_WIND":
        others = [c for c in list(next_state.hand) if not c.spec.is_attack]
        for other in others:
            exhaust_card(next_state, other, report, rng)
        block = max(0, card.spec.block * len(others))
        gain_block(next_state, block, report, rng)
        report.add_note(f"Second Wind：消耗 {len(others)} 张非攻击牌，获得 {block} 格挡")

    # HP loss is resolved before most effects in the game; doing it first also
    # makes Offering/Bloodletting valuation less optimistic.
    if card.spec.hp_loss > 0:
        loss = min(next_state.player.hp, card.spec.hp_loss)
        next_state.player.hp -= loss
        report.hp_lost += loss
        if loss:
            report.add_note(f"失去 {loss} 点生命")
        rupture = power_amount(next_state.player, "rupture")
        if rupture > 0 and loss:
            add_power(next_state.player, "strength", rupture)
            report.strength_gained += rupture
            report.add_note(f"Rupture：失去生命获得 {rupture} 力量")

    # Direct damage.
    if card.spec.damage > 0 or cid in {"BODY_SLAM", "PERFECTED_STRIKE", "WHIRLWIND", "FIEND_FIRE"}:
        if cid == "FIEND_FIRE":
            base_damage = card.spec.damage
            hits = max(1, fiend_fire_exhausted)
            report.add_note(f"Fiend Fire：{fiend_fire_exhausted} 张牌被消耗，攻击 {hits} 次")
        else:
            base_damage, hits, notes = _special_damage(next_state, card, paid_energy)
            for note in notes:
                report.add_note(note)
        _apply_damage_to_targets(
            next_state,
            card,
            action,
            base_damage=base_damage,
            hits=hits,
            report=report,
            rng=rng,
            strength=power_amount(next_state.player, "strength"),
            weak=power_amount(next_state.player, "weak") > 0,
            vulnerable=False,
        )

    # Block.
    if card.spec.block > 0 and cid != "SECOND_WIND":
        gain_block(next_state, card.spec.block, report, rng)

    # Card draw / energy.
    if card.spec.draw > 0:
        draw_cards(next_state, card.spec.draw, report, rng)
    if card.spec.energy:
        next_state.player.energy += card.spec.energy
        report.energy_gained += card.spec.energy

    # Powers and debuffs.
    skip_powers: set[str] = set()
    if cid == "DEMON_FORM":
        amount = card.spec.power_amount("Strength") or 2
        add_power(next_state.player, "demon_form", amount)
        skip_powers.add("strength")
        report.add_note(f"Demon Form：之后每回合 +{amount} 力量（按估值计入）")
    if cid == "CORRUPTION":
        add_power(next_state.player, "corruption", 1)
        skip_powers.add("corruption")
        report.add_note("Corruption：技能费用为 0，打出后消耗")
    if cid == "BARRICADE":
        add_power(next_state.player, "barricade", 1)
        skip_powers.add("barricade")
        report.add_note("Barricade：回合结束保留格挡")
    if cid == "FEEL_NO_PAIN":
        amount = card.spec.power_amount("Power") or _power_from_vars(card, "feel_no_pain") or 3
        add_power(next_state.player, "feel_no_pain", amount)
        skip_powers.add("feel_no_pain")
        report.add_note(f"Feel No Pain：消耗牌时获得 {amount} 格挡")
    if cid == "DARK_EMBRACE":
        add_power(next_state.player, "dark_embrace", 1)
        skip_powers.add("dark_embrace")
        report.add_note("Dark Embrace：消耗牌时抽 1 张")
    if cid == "JUGGERNAUT":
        amount = card.spec.power_amount("Juggernaut") or 6
        add_power(next_state.player, "juggernaut", amount)
        skip_powers.add("juggernaut")
        report.add_note(f"Juggernaut：获得格挡时造成 {amount} 伤害")
    if cid == "STONE_ARMOR":
        amount = card.spec.power_amount("Plating") or 4
        add_power(next_state.player, "plating", amount)
        skip_powers.add("plating")
        report.add_note(f"Stone Armor：回合结束获得 {amount} 格挡")
    if cid == "RUPTURE":
        amount = card.spec.power_amount("Strength") or 1
        add_power(next_state.player, "rupture", amount)
        skip_powers.add("rupture")
        skip_powers.add("strength")
        report.add_note(f"Rupture：失去生命时获得 {amount} 力量")
    if cid == "FLAME_BARRIER":
        amount = _power_from_vars(card, "flame_barrier") or 4
        add_power(next_state.player, "flame_barrier", amount)
        report.add_note(f"Flame Barrier：被攻击时反击 {amount}")
    if cid == "RAGE":
        add_power(next_state.player, "rage", 1)
        report.add_note("Rage：本回合打出攻击牌时获得格挡（近似）")

    _apply_self_powers(next_state, card, report, skip=skip_powers)
    for power in card.spec.powers:
        if power.key in _DEBUFF_POWERS:
            _apply_debuff(next_state, card, action, power.key, power.amount, report)

    # Exhaust the played card, if applicable.
    should_exhaust = (
        card.spec.exhausts
        or cid in {"FIEND_FIRE", "OFFERING"}
        or (card.spec.is_skill and power_amount(next_state.player, "corruption") > 0)
    )
    if should_exhaust:
        next_state.exhaust_pile.append(card)
        fnp = power_amount(next_state.player, "feel_no_pain")
        if fnp > 0:
            gain_block(next_state, fnp, report, rng)
            report.add_note(f"Feel No Pain：消耗触发 {fnp} 格挡")
        dark = power_amount(next_state.player, "dark_embrace")
        if dark > 0:
            draw_cards(next_state, dark, report, rng)
            report.add_note(f"Dark Embrace：消耗触发抽 {dark} 张")

    return next_state, report


def _power_from_vars(card: CardInstance, card_id: str) -> int:
    """Best-effort extraction of a power amount not exposed in ``powers_applied``."""

    key = card_id.upper()
    text = (card.spec.text or "").lower()
    if key == "FEEL_NO_PAIN" and "gain" in text:
        # Description: "gain 3 Block" -> pick the first number after gain.
        import re

        match = re.search(r"gain\s+(\d+)", text)
        if match:
            return int(match.group(1))
    if key == "FLAME_BARRIER":
        import re

        match = re.search(r"deal\s+(\d+)\s+damage\s+back", text)
        if match:
            return int(match.group(1))
    return 0


def enemy_turn(state: CombatState, rng: random.Random | None = None) -> tuple[CombatState, EnemyTurnReport]:
    """Resolve the enemy turn only (no draw/energy reset)."""

    rng = rng or random.Random(0)
    next_state = state.clone()
    report = EnemyTurnReport()

    # Discard the hand at the end of the player's turn. Ethereal cards exhaust.
    for card in list(next_state.hand):
        next_state.hand.remove(card)
        if card.spec.ethereal:
            next_state.exhaust_pile.append(card)
        else:
            next_state.discard_pile.append(card)

    # Plating / Metallicize trigger at the end of the player's turn.
    plating = power_amount(next_state.player, "plating") + power_amount(next_state.player, "metallicize")
    if plating > 0:
        gain_block(next_state, plating, EffectReport(), rng)

    # Enemy block expires at the start of the enemy's turn.
    for enemy in next_state.alive_enemies:
        enemy.block = 0

    for enemy in next_state.alive_enemies:
        if not enemy.alive:
            continue
        for intent in enemy.intents:
            if not enemy.alive:
                break
            if intent.is_attack:
                for _ in range(max(1, intent.hits)):
                    if not enemy.alive or not next_state.player.alive:
                        break
                    base = intent.damage + power_amount(enemy, "strength")
                    dmg = _attack_damage(
                        base,
                        weak=power_amount(enemy, "weak") > 0,
                        vulnerable=power_amount(next_state.player, "vulnerable") > 0,
                    )
                    absorbed = min(next_state.player.block, dmg)
                    next_state.player.block -= absorbed
                    hp_damage = max(0, dmg - absorbed)
                    next_state.player.hp = max(0, next_state.player.hp - hp_damage)
                    report.block_absorbed += absorbed
                    report.damage_taken += dmg
                    report.hp_lost += hp_damage
                    # Retaliation.
                    retaliation = power_amount(next_state.player, "flame_barrier") + power_amount(next_state.player, "thorns")
                    if retaliation > 0:
                        _deal_to_enemy(next_state, enemy, retaliation, EffectReport())
            elif "defend" in intent.kind.lower():
                enemy.block += max(0, intent.damage)
            elif intent.kind.lower() not in {"unknown", "none"}:
                report.add_note(f"{enemy.name} 的 {intent.kind} 意图未完整建模")

    if not next_state.player.alive:
        report.player_died = True
        return next_state, report

    # Decay one turn of debuffs.
    for target in [next_state.player, *next_state.alive_enemies]:
        for key in ("vulnerable", "weak", "frail"):
            decay_power(target, key, 1)

    # Block reset, unless Barricade.
    if power_amount(next_state.player, "barricade") <= 0:
        next_state.player.block = 0

    # Start-of-next-turn powers that are safe to model here.
    demon = power_amount(next_state.player, "demon_form")
    if demon > 0:
        add_power(next_state.player, "strength", demon)
        report.add_note(f"Demon Form：下回合 +{demon} 力量")
    crimson = power_amount(next_state.player, "crimson_mantle")
    if crimson > 0:
        next_state.player.block += crimson
        report.add_note(f"Crimson Mantle：下回合 +{crimson} 格挡")

    return next_state, report


def start_player_turn(state: CombatState, rng: random.Random | None = None) -> CombatState:
    rng = rng or random.Random(0)
    next_state = state.clone()
    next_state.turn += 1
    next_state.player.energy = next_state.player.max_energy
    report = EffectReport()
    draw_cards(next_state, 5, report, rng)
    return next_state


def end_turn(state: CombatState, rng: random.Random | None = None) -> CombatState:
    rng = rng or random.Random(0)
    after_enemy, _ = enemy_turn(state, rng)
    return start_player_turn(after_enemy, rng)


def state_fingerprint(state: CombatState) -> str:
    """Small deterministic key used for sampling and caching."""

    def card_key(c: CardInstance) -> tuple:
        return (c.card_id, c.upgraded, c.cost)

    parts = [
        state.player.hp,
        state.player.block,
        state.player.energy,
        tuple(sorted((k, v) for k, v in state.player.powers.items() if v)),
        tuple(card_key(c) for c in state.hand),
        tuple(card_key(c) for c in state.draw_pile),
        tuple(card_key(c) for c in state.discard_pile),
        tuple(card_key(c) for c in state.exhaust_pile),
        tuple(
            (
                e.index,
                e.hp,
                e.block,
                tuple(sorted((k, v) for k, v in e.powers.items() if v)),
                tuple((i.kind, i.damage, i.hits) for i in e.intents),
            )
            for e in state.enemies
        ),
    ]
    return repr(parts)


def state_after_sequence(
    state: CombatState,
    actions: Iterable[Action],
    rng: random.Random,
) -> tuple[CombatState, list[EffectReport]]:
    current = state.clone()
    reports: list[EffectReport] = []
    for action in actions:
        if isinstance(action, EndTurn):
            current = end_turn(current, rng)
        else:
            current, report = play_card(current, action, rng)
            reports.append(report)
    return current, reports
