"""Core data model for SpirePilot.

The model is deliberately small and dependency-free so it can run next to the
game with the standard library only.  It mirrors the subset of the StS2 combat
state that is visible through Communication_Mod_STS2, plus the card metadata we
need for a first-pass combat advisor.
"""

from __future__ import annotations

import copy
from dataclasses import dataclass, field
from typing import Iterable


# ---------------------------------------------------------------------------
# Card metadata
# ---------------------------------------------------------------------------


@dataclass(frozen=True)
class Power:
    """A power/buff/debuff with an amount (e.g. Strength 3)."""

    power_id: str
    amount: int

    @property
    def key(self) -> str:
        return normalize_power_id(self.power_id)


@dataclass(frozen=True)
class CardSpec:
    """Static card data.

    ``cost`` is the printed cost.  For X-cost cards we store 0 and set
    ``x_cost=True``; the rules layer decides how much energy is spent.
    """

    card_id: str
    name: str
    cost: int = 0
    type: str = "Skill"
    target: str = "Self"
    damage: int = 0
    block: int = 0
    hits: int = 1
    draw: int = 0
    energy: int = 0
    hp_loss: int = 0
    powers: tuple[Power, ...] = ()
    keywords: tuple[str, ...] = ()
    text: str = ""
    x_cost: bool = False
    upgrade: tuple[tuple[str, object], ...] = ()
    source: str = "unknown"

    @property
    def is_attack(self) -> bool:
        return self.type.lower() == "attack"

    @property
    def is_skill(self) -> bool:
        return self.type.lower() == "skill"

    @property
    def is_power(self) -> bool:
        return self.type.lower() == "power"

    @property
    def is_playable_type(self) -> bool:
        return self.type.lower() in {"attack", "skill", "power"}

    @property
    def exhausts(self) -> bool:
        return any(k.lower() == "exhaust" for k in self.keywords)

    @property
    def ethereal(self) -> bool:
        return any(k.lower() == "ethereal" for k in self.keywords)

    def power_amount(self, *names: str) -> int:
        wanted = {normalize_power_id(n) for n in names}
        for power in self.powers:
            if power.key in wanted:
                return power.amount
        return 0


@dataclass
class CardInstance:
    """A concrete card in a pile.

    ``uid`` is local to a parsed combat state.  It is not the game's runtime
    hash (``Card_ID``), which is not stable across runs.
    """

    uid: str
    card_id: str
    name: str
    spec: CardSpec
    upgraded: bool = False
    cost_override: int | None = None
    playable_override: bool | None = None

    @property
    def cost(self) -> int:
        if self.cost_override is not None:
            return max(0, self.cost_override)
        return max(0, self.spec.cost)

    @property
    def display_name(self) -> str:
        return f"{self.name}+" if self.upgraded else self.name


# ---------------------------------------------------------------------------
# Combat state
# ---------------------------------------------------------------------------


@dataclass(frozen=True)
class Intent:
    kind: str
    damage: int = 0
    hits: int = 1

    @property
    def is_attack(self) -> bool:
        return "attack" in self.kind.lower() and self.damage > 0


@dataclass
class EnemyState:
    index: int
    monster_id: str
    hp: int
    max_hp: int
    block: int = 0
    powers: dict[str, int] = field(default_factory=dict)
    intents: tuple[Intent, ...] = ()

    @property
    def alive(self) -> bool:
        return self.hp > 0

    @property
    def name(self) -> str:
        return self.monster_id.replace("_", " ").title()

    def power(self, *names: str) -> int:
        wanted = {normalize_power_id(n) for n in names}
        return sum(v for k, v in self.powers.items() if k in wanted)


@dataclass
class PlayerState:
    hp: int
    max_hp: int
    block: int = 0
    energy: int = 0
    max_energy: int = 3
    powers: dict[str, int] = field(default_factory=dict)
    relics: tuple[str, ...] = ()
    potions: tuple[str, ...] = ()

    @property
    def alive(self) -> bool:
        return self.hp > 0

    def power(self, *names: str) -> int:
        wanted = {normalize_power_id(n) for n in names}
        return sum(v for k, v in self.powers.items() if k in wanted)


@dataclass
class CombatState:
    player: PlayerState
    hand: list[CardInstance] = field(default_factory=list)
    draw_pile: list[CardInstance] = field(default_factory=list)
    discard_pile: list[CardInstance] = field(default_factory=list)
    exhaust_pile: list[CardInstance] = field(default_factory=list)
    enemies: list[EnemyState] = field(default_factory=list)
    turn: int = 1
    action_count: int = 0
    character: str = "IRONCLAD"
    act: int = 1
    floor: int = 0
    trigger: str = ""
    warnings: tuple[str, ...] = ()

    @property
    def alive_enemies(self) -> list[EnemyState]:
        return [e for e in self.enemies if e.alive]

    @property
    def combat_over(self) -> bool:
        return (not self.player.alive) or (not self.alive_enemies)

    def enemy(self, index: int) -> EnemyState | None:
        for enemy in self.enemies:
            if enemy.index == index:
                return enemy
        if 0 <= index < len(self.enemies):
            return self.enemies[index]
        return None

    def clone(self) -> "CombatState":
        return copy.deepcopy(self)

    def all_cards(self) -> Iterable[CardInstance]:
        yield from self.hand
        yield from self.draw_pile
        yield from self.discard_pile
        yield from self.exhaust_pile


# ---------------------------------------------------------------------------
# Actions
# ---------------------------------------------------------------------------


@dataclass(frozen=True)
class PlayCard:
    hand_index: int
    target_index: int | None = None

    @property
    def key(self) -> tuple:
        return ("play", self.hand_index, self.target_index)


@dataclass(frozen=True)
class EndTurn:
    @property
    def key(self) -> tuple:
        return ("end_turn",)


Action = PlayCard | EndTurn


def action_key(action: Action) -> tuple:
    return action.key


# ---------------------------------------------------------------------------
# Small shared helpers
# ---------------------------------------------------------------------------


def normalize_power_id(value: str) -> str:
    """Normalize ``FeelNoPain``/``feel-no-pain`` to ``feel_no_pain``."""

    text = str(value).strip()
    if not text:
        return ""
    # Insert an underscore between lower/digit and upper, then lower-case.
    out: list[str] = []
    for i, ch in enumerate(text):
        if ch in "- ":
            out.append("_")
            continue
        if ch.isupper() and i > 0 and (text[i - 1].islower() or text[i - 1].isdigit()):
            out.append("_")
        out.append(ch.lower())
    return "".join(out).strip("_")


def target_label(state: CombatState, action: Action) -> str:
    if isinstance(action, EndTurn):
        return "结束回合"
    if action.hand_index < 0 or action.hand_index >= len(state.hand):
        return "?"
    card = state.hand[action.hand_index]
    if action.target_index is None:
        return card.display_name
    enemy = state.enemy(action.target_index)
    return f"{card.display_name} -> {enemy.name if enemy else '?'}"
