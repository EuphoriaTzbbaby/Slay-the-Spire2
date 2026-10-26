"""Command-line interface for SpirePilot."""

from __future__ import annotations

import argparse
import json
import sys
from pathlib import Path

from . import __version__
from .advisor import advice_to_dict, advise
from .adapter import parse_file
from .bridge import format_advice_text, serve
from .capture import load_capture_raw, serve_capture
from .cards import CardDatabase
from .inspect import audit_state
from .overlay import run_overlay
from .rollout import rollout_best, rollout_rank
from .search import SearchConfig
from .sts2mcp import DEFAULT_URL, UnsupportedStateError, fetch_state, parse_state
from .trace import load_trace

_ROOT = Path(__file__).resolve().parents[2]
_DEFAULT_SAMPLE = _ROOT / "data" / "samples" / "combat_basic.json"


def _search_config(args: argparse.Namespace) -> SearchConfig:
    return SearchConfig(
        max_depth=args.depth,
        beam_width=args.width,
        time_budget_ms=args.budget_ms,
        samples=args.samples,
    )


def _add_solver_arg(parser: argparse.ArgumentParser, default: str = "beam", choices=("beam", "exact", "compare")) -> None:
    parser.add_argument(
        "--solver",
        choices=list(choices),
        default=default,
        help="求解器：beam / exact / compare",
    )


def _add_search_args(parser: argparse.ArgumentParser) -> None:
    parser.add_argument("--db", default=None, help="卡牌库 JSON 路径（默认 data/cards/ironclad_cards.json）")
    parser.add_argument("--depth", type=int, default=5, help="搜索最多打出的牌数（默认 5）")
    parser.add_argument("--width", type=int, default=18, help="beam 宽度（默认 18）")
    parser.add_argument("--budget-ms", type=int, default=650, help="每步搜索时间预算（毫秒）")
    parser.add_argument("--samples", type=int, default=2, help="抽牌顺序采样数（默认 2）")


def _print_state_summary(state) -> None:
    enemies = "、".join(
        f"{e.name} {e.hp}/{e.max_hp}" for e in state.alive_enemies
    ) or "无"
    print(
        f"回合 {state.turn} | HP {state.player.hp}/{state.player.max_hp} | "
        f"能量 {state.player.energy}/{state.player.max_energy} | 敌人：{enemies}"
    )


def cmd_advise(args: argparse.Namespace) -> int:
    db = CardDatabase.load(args.db)
    if len(db) == 0:
        print("错误：卡牌库为空。请检查 data/cards/ironclad_cards.json 或 --db 路径。", file=sys.stderr)
        return 2
    try:
        state = parse_file(args.state, db)
    except Exception as exc:  # noqa: BLE001 - CLI should surface a clean error
        print(f"状态解析失败：{exc}", file=sys.stderr)
        return 2

    advice = advise(state, _search_config(args), solver=getattr(args, "solver", "beam"))
    if args.json:
        payload = advice_to_dict(advice)
        payload["state"] = {
            "turn": state.turn,
            "hp": state.player.hp,
            "max_hp": state.player.max_hp,
            "energy": state.player.energy,
            "enemies": [
                {"name": e.name, "hp": e.hp, "max_hp": e.max_hp}
                for e in state.alive_enemies
            ],
        }
        print(json.dumps(payload, ensure_ascii=False, indent=2))
    else:
        _print_state_summary(state)
        print(format_advice_text(advice, state))
    return 0


def cmd_demo(args: argparse.Namespace) -> int:
    args.state = str(_DEFAULT_SAMPLE)
    return cmd_advise(args)


def cmd_inspect(args: argparse.Namespace) -> int:
    try:
        if args.capture:
            raw = load_capture_raw(args.capture, args.index)
        else:
            path = Path(args.state)
            if not path.exists():
                print(f"状态文件不存在：{path}", file=sys.stderr)
                return 2
            raw = json.loads(path.read_text(encoding="utf-8"))
    except (OSError, json.JSONDecodeError, ValueError) as exc:
        print(f"读取/解析失败：{exc}", file=sys.stderr)
        return 2
    report = audit_state(raw)
    if args.json:
        print(json.dumps(report.to_dict(), ensure_ascii=False, indent=2))
    else:
        print(report.render())
    return 0 if report.verdict != "blocked" else 2


def cmd_doctor(args: argparse.Namespace) -> int:
    print(f"SpirePilot {__version__}")
    print(f"Python: {sys.version.split()[0]}")
    db = CardDatabase.load(args.db)
    print(f"卡牌库：{len(db)} 张")
    print(f"路径：{db.path or '未找到'}")
    if len(db) == 0:
        print("FAIL: 卡牌库为空")
        return 2
    if not _DEFAULT_SAMPLE.exists():
        print(f"FAIL: 示例状态不存在：{_DEFAULT_SAMPLE}")
        return 2
    try:
        state = parse_file(str(_DEFAULT_SAMPLE), db)
    except Exception as exc:  # noqa: BLE001
        print(f"FAIL: 示例状态解析失败：{exc}")
        return 2
    print(
        f"示例状态：回合 {state.turn}，手牌 {len(state.hand)}，"
        f"敌人 {len(state.alive_enemies)}，警告 {len(state.warnings)}"
    )
    advice = advise(state, SearchConfig(max_depth=3, beam_width=8, time_budget_ms=200, samples=1))
    print(f"示例建议：{advice.label}（评分 {advice.score:.2f}）")
    print("OK")
    return 0


def cmd_capture(args: argparse.Namespace) -> int:
    try:
        serve_capture(args.host, args.port, args.out, args.session)
    except OSError as exc:
        print(f"无法启动 capture：{exc}", file=sys.stderr)
        return 2
    return 0


def cmd_live(args: argparse.Namespace) -> int:
    db = CardDatabase.load(args.db)
    if len(db) == 0:
        print("错误：卡牌库为空。", file=sys.stderr)
        return 2
    if args.state:
        try:
            raw = json.loads(Path(args.state).read_text(encoding="utf-8"))
        except (OSError, json.JSONDecodeError) as exc:
            print(f"读取 STS2MCP 状态失败：{exc}", file=sys.stderr)
            return 2
    else:
        try:
            raw = fetch_state(args.url)
        except Exception as exc:  # noqa: BLE001 - CLI should show a clean network error
            print(f"无法连接 STS2MCP：{exc}", file=sys.stderr)
            print("确认游戏已启动、STS2 MCP 已启用，并且 http://127.0.0.1:15526/ 可访问。", file=sys.stderr)
            return 2
    state_type = raw.get("state_type")
    if state_type not in {"monster", "elite", "boss"}:
        print(f"当前不是战斗状态：state_type={state_type!r}")
        print(raw.get("message") or "")
        return 0
    try:
        state = parse_state(raw, db)
    except (UnsupportedStateError, ValueError) as exc:
        print(f"状态解析失败：{exc}", file=sys.stderr)
        return 2
    config = _search_config(args)
    if args.solver == "rollout":
        lines = rollout_rank(state, turns=2, samples=1, policy_ms=25)
        if args.json:
            print(json.dumps([
                {"label": l.label, "hp": l.hp, "enemy_hp": l.enemy_hp, "lethal": l.lethal}
                for l in lines
            ], ensure_ascii=False, indent=2))
            return 0
        _print_state_summary(state)
        print("2T rollout 对比：")
        for l in lines[:8]:
            print(f"  {l.label:30} HP {l.hp}  敌 {l.enemy_hp}  击杀 {l.lethal}")
        return 0

    if args.solver == "compare":
        exact_advice = advise(state, config, solver="exact")
        beam_advice = advise(state, config, solver="beam")
        rollout = rollout_best(state, turns=2, samples=1, policy_ms=25)
        if args.json:
            payload = {
                "state": {
                    "state_type": state_type,
                    "turn": state.turn,
                    "hp": state.player.hp,
                    "max_hp": state.player.max_hp,
                    "energy": state.player.energy,
                    "hand": [c.name for c in state.hand],
                },
                "exact": advice_to_dict(exact_advice),
                "beam": advice_to_dict(beam_advice),
            }
            print(json.dumps(payload, ensure_ascii=False, indent=2))
            return 0
        _print_state_summary(state)
        print(format_advice_text(exact_advice, state, title="精确搜索（模型内最优）"))
        print()
        print("对比：")
        for name, adv in (("exact", exact_advice), ("beam", beam_advice)):
            est = adv.estimated
            print(
                f"  {name:5} | {adv.label} | score {adv.score:.2f} | "
                f"HP {est.get('player_hp_after_enemy', '?')} | "
                f"敌 {est.get('enemy_hp_after_enemy', '?')} | "
                f"掉血 {est.get('hp_lost', '?')}"
            )
        if rollout is not None:
            print(f"  rollout | {rollout.label} | 2T HP {rollout.hp} | 敌 {rollout.enemy_hp}")
        return 0

    advice = advise(state, config, solver=args.solver)
    if args.json:
        payload = advice_to_dict(advice)
        payload["state"] = {
            "state_type": state_type,
            "turn": state.turn,
            "hp": state.player.hp,
            "max_hp": state.player.max_hp,
            "energy": state.player.energy,
            "hand": [c.name for c in state.hand],
            "enemies": [{"name": e.name, "hp": e.hp, "max_hp": e.max_hp} for e in state.enemies],
        }
        print(json.dumps(payload, ensure_ascii=False, indent=2))
    else:
        _print_state_summary(state)
        title = "精确搜索（模型内最优）" if args.solver == "exact" else "Beam 搜索（近似）"
        print(format_advice_text(advice, state, title=title))
    return 0


def cmd_rollout(args: argparse.Namespace) -> int:
    db = CardDatabase.load(args.db)
    if len(db) == 0:
        print("错误：卡牌库为空。", file=sys.stderr)
        return 2
    if args.state:
        try:
            raw = json.loads(Path(args.state).read_text(encoding="utf-8"))
        except (OSError, json.JSONDecodeError) as exc:
            print(f"读取状态失败：{exc}", file=sys.stderr)
            return 2
    else:
        try:
            raw = fetch_state(args.url)
        except Exception as exc:  # noqa: BLE001
            print(f"无法连接 STS2MCP：{exc}", file=sys.stderr)
            return 2
    try:
        state = parse_state(raw, db)
    except (UnsupportedStateError, ValueError) as exc:
        print(f"状态解析失败：{exc}", file=sys.stderr)
        return 2
    lines = rollout_rank(state, turns=args.turns, samples=args.samples, policy_ms=args.policy_ms)
    if args.json:
        print(json.dumps([
            {
                "label": l.label,
                "hp": l.hp,
                "enemy_hp": l.enemy_hp,
                "lethal": l.lethal,
                "score": round(l.score, 2),
                "actions": [a.__class__.__name__ for a in l.actions],
                "labels": list(l.labels),
            }
            for l in lines
        ], ensure_ascii=False, indent=2))
        return 0
    _print_state_summary(state)
    print(f"Rollout：{args.turns} 回合，{args.samples} 样本，策略预算 {args.policy_ms}ms/回合")
    print(f"{'动作':30} {'HP':>4} {'敌HP':>5} {'击杀':>5}")
    for line in lines[:8]:
        print(f"{line.label[:29]:30} {line.hp:>4} {line.enemy_hp:>5} {str(line.lethal):>5}")
    return 0


def cmd_overlay(args: argparse.Namespace) -> int:
    try:
        run_overlay(args.url, _search_config(args), args.db)
    except Exception as exc:  # noqa: BLE001 - GUI errors should be readable
        print(f"无法启动 overlay：{exc}", file=sys.stderr)
        return 2
    return 0


def cmd_trace(args: argparse.Namespace) -> int:
    db = CardDatabase.load(args.db)
    if len(db) == 0:
        print("错误：卡牌库为空。", file=sys.stderr)
        return 2
    try:
        result = load_trace(args.trace, db, seed=args.seed)
    except (OSError, json.JSONDecodeError) as exc:
        print(f"trace 读取/解析失败：{exc}", file=sys.stderr)
        return 2
    if args.json:
        print(json.dumps(result.to_dict(), ensure_ascii=False, indent=2))
    else:
        print(result.render())
    return 0 if result.passed else 1


def cmd_serve(args: argparse.Namespace) -> int:
    db = CardDatabase.load(args.db)
    if len(db) == 0:
        print("错误：卡牌库为空。请检查 data/cards/ironclad_cards.json 或 --db 路径。", file=sys.stderr)
        return 2
    config = _search_config(args)
    try:
        serve(
            host=args.host,
            port=args.port,
            db=db,
            config=config,
            auto=args.auto,
            quiet=args.quiet,
            log_path=args.log,
        )
    except OSError as exc:
        print(f"无法启动 bridge：{exc}", file=sys.stderr)
        return 2
    return 0


def build_parser() -> argparse.ArgumentParser:
    parser = argparse.ArgumentParser(
        prog="spirepilot",
        description="Slay the Spire 2 战斗内建议（离线状态 / 本地 bridge）",
    )
    parser.add_argument("--version", action="version", version=f"%(prog)s {__version__}")
    sub = parser.add_subparsers(dest="command", required=True)

    advise_p = sub.add_parser("advise", help="对一份战斗状态 JSON 给出建议")
    advise_p.add_argument("--state", required=True, help="Communication_Mod_STS2 状态 JSON")
    advise_p.add_argument("--json", action="store_true", help="输出机器可读 JSON")
    _add_solver_arg(advise_p, default="beam", choices=("beam", "exact"))
    _add_search_args(advise_p)
    advise_p.set_defaults(func=cmd_advise)

    demo_p = sub.add_parser("demo", help="用内置示例状态跑一遍建议")
    demo_p.add_argument("--json", action="store_true", help="输出机器可读 JSON")
    _add_search_args(demo_p)
    demo_p.set_defaults(func=cmd_demo)

    doctor_p = sub.add_parser("doctor", help="检查卡牌库、示例状态与建议链路")
    doctor_p.add_argument("--db", default=None, help="卡牌库 JSON 路径")
    doctor_p.set_defaults(func=cmd_doctor)

    inspect_p = sub.add_parser("inspect", help="审计一份原始状态 JSON 的字段完整度")
    inspect_src = inspect_p.add_mutually_exclusive_group(required=True)
    inspect_src.add_argument("--state", help="Communication_Mod_STS2 状态 JSON")
    inspect_src.add_argument("--capture", help="capture 生成的 states.jsonl")
    inspect_p.add_argument("--index", type=int, default=-1, help="capture 记录索引；-1 表示最后一条")
    inspect_p.add_argument("--json", action="store_true", help="输出机器可读 JSON")
    inspect_p.set_defaults(func=cmd_inspect)

    capture_p = sub.add_parser("capture", help="只抓取原始状态，不执行动作（D1-D3 用）")
    capture_p.add_argument("--host", default="127.0.0.1")
    capture_p.add_argument("--port", type=int, default=5000)
    capture_p.add_argument("--out", default="data/replays", help="输出目录")
    capture_p.add_argument("--session", default=None, help="会话名；默认时间戳")
    capture_p.set_defaults(func=cmd_capture)

    live_p = sub.add_parser("live", help="从 STS2MCP (15526) 读取当前战斗并给建议")
    live_p.add_argument("--url", default=DEFAULT_URL, help="STS2MCP 地址")
    live_p.add_argument("--state", default=None, help="可选：从保存的 STS2MCP JSON 读取，而不是访问游戏")
    live_p.add_argument("--json", action="store_true", help="输出机器可读 JSON")
    _add_solver_arg(live_p, default="exact", choices=("beam", "exact", "rollout", "compare"))
    _add_search_args(live_p)
    live_p.set_defaults(func=cmd_live)

    overlay_p = sub.add_parser("overlay", help="打开置顶的建议对比窗口（Phase A）")
    overlay_p.add_argument("--url", default=DEFAULT_URL, help="STS2MCP 地址")
    _add_search_args(overlay_p)
    overlay_p.set_defaults(func=cmd_overlay)

    rollout_p = sub.add_parser("rollout", help="对当前动作做 2–3 回合 rollout 对比")
    rollout_p.add_argument("--url", default=DEFAULT_URL)
    rollout_p.add_argument("--state", default=None, help="可选：保存的 STS2MCP JSON")
    rollout_p.add_argument("--turns", type=int, default=2)
    rollout_p.add_argument("--samples", type=int, default=1)
    rollout_p.add_argument("--policy-ms", type=int, default=35)
    rollout_p.add_argument("--json", action="store_true")
    rollout_p.add_argument("--db", default=None)
    rollout_p.set_defaults(func=cmd_rollout)

    trace_p = sub.add_parser("trace", help="回放 golden trace 并与期望状态对比")
    trace_p.add_argument("--trace", required=True, help="trace JSON 路径")
    trace_p.add_argument("--json", action="store_true", help="输出机器可读 JSON")
    trace_p.add_argument("--seed", type=int, default=0, help="抽牌/随机种子")
    trace_p.add_argument("--db", default=None, help="卡牌库 JSON 路径")
    trace_p.set_defaults(func=cmd_trace)

    serve_p = sub.add_parser("serve", help="启动本地 bridge，接收 mod 推送")
    serve_p.add_argument("--host", default="127.0.0.1")
    serve_p.add_argument("--port", type=int, default=5000)
    serve_p.add_argument("--auto", action="store_true", help="自动回传动作；默认只建议并回 OK")
    serve_p.add_argument("--quiet", action="store_true", help="不打印每条建议")
    serve_p.add_argument("--log", default="logs/bridge.jsonl", help="回放/决策日志路径；空字符串关闭")
    _add_search_args(serve_p)
    serve_p.set_defaults(func=cmd_serve)
    return parser


def main(argv: list[str] | None = None) -> int:
    parser = build_parser()
    args = parser.parse_args(argv)
    if args.command == "serve" and args.log == "":
        args.log = None
    return int(args.func(args))


if __name__ == "__main__":
    raise SystemExit(main())
