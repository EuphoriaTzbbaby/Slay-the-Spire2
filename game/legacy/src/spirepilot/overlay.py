"""Phase A always-on-top advice panel.

This is deliberately an external window, not an in-game mod overlay.  It polls
STS2MCP (15526) and shows the exact/beam comparison over the game.  The GUI is
imported only when run, so the rest of the package has no Tk dependency.
"""

from __future__ import annotations

import queue
import threading
import time
from dataclasses import dataclass
from typing import Any, Callable

from .advisor import Advice, advise
from .cards import CardDatabase
from .search import SearchConfig
from .rollout import RolloutLine, rollout_best
from .sts2mcp import DEFAULT_URL, fetch_state, parse_state


@dataclass
class OverlayView:
    state_type: str = "unknown"
    message: str = ""
    turn: int = 0
    hp: int = 0
    max_hp: int = 0
    energy: int = 0
    enemy_text: str = ""
    hand: tuple[str, ...] = ()
    exact: Advice | None = None
    beam: Advice | None = None
    rollout: RolloutLine | None = None
    error: str = ""
    updated_at: float = 0.0


def build_view(raw: dict[str, Any], db: CardDatabase, config: SearchConfig) -> OverlayView:
    state_type = str(raw.get("state_type") or "unknown")
    if state_type not in {"monster", "elite", "boss"}:
        return OverlayView(
            state_type=state_type,
            message=str(raw.get("message") or ""),
            updated_at=time.time(),
        )
    try:
        state = parse_state(raw, db)
    except Exception as exc:  # noqa: BLE001 - overlay must never crash
        return OverlayView(state_type=state_type, error=str(exc), updated_at=time.time())
    exact = advise(state, config, solver="exact")
    beam = advise(state, config, solver="beam")
    rollout = rollout_best(state, turns=2, samples=1, policy_ms=25)
    return OverlayView(
        state_type=state_type,
        turn=state.turn,
        hp=state.player.hp,
        max_hp=state.player.max_hp,
        energy=state.player.energy,
        enemy_text="、".join(f"{e.name} {e.hp}/{e.max_hp}" for e in state.enemies if e.alive),
        hand=tuple(f"[{i}] {c.display_name} ({c.cost}费)" for i, c in enumerate(state.hand)),
        exact=exact,
        beam=beam,
        rollout=rollout,
        updated_at=time.time(),
    )


def format_lines(view: OverlayView) -> list[str]:
    if view.error:
        return ["SpirePilot", f"错误：{view.error}"]
    if view.state_type not in {"monster", "elite", "boss"}:
        return ["SpirePilot", f"当前不在战斗：{view.state_type}", view.message]
    if view.exact is None:
        return ["SpirePilot", "等待战斗状态..."]
    exact = view.exact
    beam = view.beam
    lines = [
        "SpirePilot  |  exact",
        f"回合 {view.turn}  HP {view.hp}/{view.max_hp}  能量 {view.energy}",
        f"敌人：{view.enemy_text}",
        "",
        f"推荐：{exact.label}",
    ]
    est = exact.estimated
    lines.append(
        f"HP {est.get('player_hp_after_enemy', '?')} ｜ "
        f"敌 {est.get('enemy_hp_after_enemy', '?')} ｜ "
        f"掉血 {est.get('hp_lost', '?')}"
    )
    if beam is not None:
        best = beam.estimated
        lines.append(
            f"beam: {beam.label} ｜ HP {best.get('player_hp_after_enemy', '?')} ｜ "
            f"敌 {best.get('enemy_hp_after_enemy', '?')}"
        )
    if view.rollout is not None:
        lines.append(
            f"2T rollout: {view.rollout.label} ｜ HP {view.rollout.hp} ｜ 敌 {view.rollout.enemy_hp}"
        )
    lines.append("")
    lines.append("手牌：")
    lines.extend(view.hand[:7])
    if exact.alternatives:
        lines.append("")
        lines.append("备选：")
        for alt in exact.alternatives[1:4]:
            lines.append(f"  {alt.label}  HP {alt.player_hp} / 敌 {alt.enemy_hp}")
    lines.append("")
    lines.append("Ctrl+Q / 关闭按钮退出")
    return lines


def _poll_loop(url: str, db: CardDatabase, config: SearchConfig, out: queue.Queue, stop: threading.Event) -> None:
    while not stop.is_set():
        try:
            raw = fetch_state(url)
            out.put(build_view(raw, db, config))
        except Exception as exc:  # noqa: BLE001
            out.put(OverlayView(error=str(exc), updated_at=time.time()))
        stop.wait(1.2)


def run_overlay(url: str = DEFAULT_URL, config: SearchConfig | None = None, db_path: str | None = None) -> None:
    import tkinter as tk

    db = CardDatabase.load(db_path)
    config = config or SearchConfig(max_depth=6, time_budget_ms=450, samples=1)
    root = tk.Tk()
    root.title("SpirePilot")
    root.configure(bg="#15171c")
    root.attributes("-topmost", True)
    try:
        root.attributes("-alpha", 0.94)
    except tk.TclError:
        pass
    root.geometry("420x430+40+80")
    text = tk.StringVar(value="SpirePilot\n连接 STS2MCP ...")
    label = tk.Label(
        root,
        textvariable=text,
        justify="left",
        anchor="nw",
        bg="#15171c",
        fg="#e8e8e8",
        font=("Menlo", 12),
        padx=14,
        pady=12,
    )
    label.pack(fill="both", expand=True)
    tk.Button(root, text="关闭", command=root.destroy, bg="#2a2d35", fg="#ffffff").pack(pady=(0, 8))
    root.bind("<Control-q>", lambda _event: root.destroy())
    root.bind("<Escape>", lambda _event: root.destroy())

    updates: queue.Queue = queue.Queue()
    stop = threading.Event()
    worker = threading.Thread(target=_poll_loop, args=(url, db, config, updates, stop), daemon=True)
    worker.start()

    def drain() -> None:
        view = None
        while True:
            try:
                view = updates.get_nowait()
            except queue.Empty:
                break
        if view is not None:
            text.set("\n".join(format_lines(view)))
        root.after(250, drain)

    def on_close() -> None:
        stop.set()
        root.destroy()

    root.protocol("WM_DELETE_WINDOW", on_close)
    drain()
    try:
        root.mainloop()
    finally:
        stop.set()
