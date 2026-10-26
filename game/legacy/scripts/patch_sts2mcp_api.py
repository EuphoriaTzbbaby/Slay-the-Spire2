#!/usr/bin/env python3
"""Patch STS2MCP 0.4.0 source for current StS2 API changes.

The 0.4.0 source uses StartRunLobby.MaxPlayers and LoadRunLobby.ConnectedPlayerIds,
which are absent from the current game assembly.  These fields only affect
multiplayer lobby metadata; for our single-player combat advisor we can fall
back to safe values.

Usage:
    python3 scripts/patch_sts2mcp_api.py
    python3 scripts/patch_sts2mcp_api.py --source /path/to/McpMod.StateBuilder.cs
"""

from __future__ import annotations

import argparse
import shutil
import time
from pathlib import Path

DEFAULT_SOURCE = (
    Path.home()
    / "Library/Application Support/Steam/steamapps/common/Slay the Spire 2"
    / "SlayTheSpire2.app/Contents/MacOS/STS2MCP/McpMod.StateBuilder.cs"
)

REPLACEMENTS = [
    (
        '            ["max_players"] = lobby.MaxPlayers,',
        '            ["max_players"] = 4, // patched: MaxPlayers removed from current game API',
    ),
    (
        '            info["connected_player_count"] = lobby.ConnectedPlayerIds?.Count ?? 0;',
        '            info["connected_player_count"] = lobby.Run?.Players?.Count ?? 0; // patched: ConnectedPlayerIds removed',
    ),
    (
        '''                var runPlayers = lobby.Run?.Players;
                var connectedPlayerIds = lobby.ConnectedPlayerIds;
                aboutToBegin = runPlayers != null
                    && connectedPlayerIds != null
                    && runPlayers.Count > 0
                    && runPlayers.All(player => connectedPlayerIds.Contains(player.NetId) && lobby.IsPlayerReady(player.NetId));''',
        '''                var runPlayers = lobby.Run?.Players;
                aboutToBegin = runPlayers != null
                    && runPlayers.Count > 0
                    && runPlayers.All(player => lobby.IsPlayerReady(player.NetId));''',
    ),
    (
        '                        bool isConnected = lobby.ConnectedPlayerIds?.Contains(sp.NetId) ?? false;',
        '                        bool isConnected = true; // patched: ConnectedPlayerIds removed from current API',
    ),
]


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("--source", default=str(DEFAULT_SOURCE))
    args = parser.parse_args()
    source = Path(args.source)
    if not source.exists():
        raise SystemExit(f"source not found: {source}")

    text = source.read_text(encoding="utf-8")
    backup = source.with_suffix(source.suffix + f".bak-{time.strftime('%Y%m%d-%H%M%S')}")
    shutil.copy2(source, backup)

    changed = 0
    for old, new in REPLACEMENTS:
        if old not in text:
            print(f"NOT FOUND (already patched?): {old.splitlines()[0]}")
            continue
        text = text.replace(old, new, 1)
        changed += 1
    source.write_text(text, encoding="utf-8")
    print(f"patched {changed}/{len(REPLACEMENTS)} blocks")
    print(f"backup: {backup}")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
