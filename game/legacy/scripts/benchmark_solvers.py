#!/usr/bin/env python3
"""Compare exact-turn DFS vs beam search on a small fixed state set.

The goal is not a win-rate benchmark; it is a regression/diagnostic harness:
does exact find a line with less actual HP loss or a kill that beam misses?
"""

from __future__ import annotations

import copy
import json
from pathlib import Path

from spirepilot.advisor import advise
from spirepilot.cards import CardDatabase
from spirepilot.rollout import rollout_rank
from spirepilot.search import SearchConfig
from spirepilot.sts2mcp import load_state_file

ROOT = Path(__file__).resolve().parents[1]
FIXTURE = ROOT / "data" / "samples" / "sts2mcp_combat.json"


def variants(base):
    out = []
    for energy in (3, 2, 1, 0):
        s = copy.deepcopy(base)
        s.player.energy = energy
        out.append((f"energy={energy}", s))
    for hp in (5, 10, 20, 43):
        s = copy.deepcopy(base)
        s.enemies[0].hp = hp
        out.append((f"enemy_hp={hp}", s))
    for php in (10, 30, 60, 91):
        s = copy.deepcopy(base)
        s.player.hp = php
        out.append((f"player_hp={php}", s))
    # Add vulnerable / weak on the enemy to exercise debuff valuation.
    s = copy.deepcopy(base)
    s.enemies[0].powers["vulnerable"] = 2
    out.append(("enemy_vulnerable=2", s))
    s = copy.deepcopy(base)
    s.enemies[0].powers["weak"] = 1
    out.append(("enemy_weak=1", s))
    # Two enemies: add a low-HP second target.
    s = copy.deepcopy(base)
    from spirepilot.model import EnemyState
    s.enemies.append(EnemyState(index=1, monster_id="NIBBIT_1", hp=8, max_hp=8, intents=s.enemies[0].intents))
    out.append(("two_enemies", s))
    return out


def main() -> int:
    db = CardDatabase.load()
    base = load_state_file(FIXTURE, db)
    config = SearchConfig(max_depth=6, beam_width=24, time_budget_ms=500, samples=1)
    rows = []
    for name, state in variants(base):
        exact = advise(state, config, solver="exact")
        beam = advise(state, config, solver="beam")
        rollouts = rollout_rank(state, turns=2, samples=1, policy_ms=20)
        e = exact.estimated
        b = beam.estimated
        best_rollout = rollouts[0] if rollouts else None
        exact_rollout = next((r for r in rollouts if r.action.key == exact.action.key), None)
        beam_rollout = next((r for r in rollouts if r.action.key == beam.action.key), None)
        rows.append(
            {
                "case": name,
                "exact_action": exact.label,
                "exact_hp": e.get("player_hp_after_enemy"),
                "exact_enemy": e.get("enemy_hp_after_enemy"),
                "exact_lost": e.get("hp_lost"),
                "exact_nodes": exact.stats.get("nodes"),
                "exact_2t_hp": exact_rollout.hp if exact_rollout else None,
                "beam_action": beam.label,
                "beam_hp": b.get("player_hp_after_enemy"),
                "beam_enemy": b.get("enemy_hp_after_enemy"),
                "beam_lost": b.get("hp_lost"),
                "beam_nodes": beam.stats.get("nodes"),
                "beam_2t_hp": beam_rollout.hp if beam_rollout else None,
                "rollout_action": best_rollout.label if best_rollout else None,
                "rollout_2t_hp": best_rollout.hp if best_rollout else None,
                "same_action": exact.action.key == beam.action.key,
                "rollout_beats_exact_2t": bool(best_rollout and exact_rollout and best_rollout.hp > exact_rollout.hp),
            }
        )

    print(f"{'case':22} {'exact':26} {'exactHP':>7} {'beam':26} {'beamHP':>7} {'rollout':26} {'rollHP':>7} {'R>E':>4}")
    for r in rows:
        print(
            f"{r['case']:22} {r['exact_action'][:25]:26} {str(r['exact_2t_hp']):>7} "
            f"{r['beam_action'][:25]:26} {str(r['beam_2t_hp']):>7} "
            f"{str(r['rollout_action'])[:25]:26} {str(r['rollout_2t_hp']):>7} "
            f"{str(r['rollout_beats_exact_2t']):>4}"
        )
    print()
    print(
        f"cases={len(rows)} same_action={sum(r['same_action'] for r in rows)} "
        f"rollout_beats_exact_2t={sum(r['rollout_beats_exact_2t'] for r in rows)}"
    )
    out = ROOT / "experiments" / "20261022-exact-vs-beam.json"
    out.parent.mkdir(parents=True, exist_ok=True)
    out.write_text(json.dumps(rows, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")
    print(f"wrote {out}")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
