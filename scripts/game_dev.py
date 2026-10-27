"""Build/check/install the first-party game mod. Never starts a game or edits saves."""
from pathlib import Path
import argparse
import os
import shutil
import subprocess
import sys
import tempfile
from datetime import datetime

ROOT = Path(__file__).resolve().parents[1]
GAME = ROOT / 'game' / 'legacy'
DEFAULT_DATA = Path.home() / 'Library/Application Support/Steam/steamapps/common/Slay the Spire 2/SlayTheSpire2.app/Contents/Resources/data_sts2_macos_arm64'

def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('action', choices=['build', 'check', 'install'])
    parser.add_argument('--game-data', type=Path, help='Directory containing sts2.dll and GodotSharp.dll')
    parser.add_argument('--mods-dir', type=Path, help='Game mods directory; Windows requires this option for install')
    args = parser.parse_args()
    if shutil.which('dotnet') is None:
        parser.error('Install .NET 9 SDK first.')
    if args.action == 'check':
        return subprocess.call(['dotnet', 'run', '--project', str(GAME / 'SpireCompanion.Checks'), '-c', 'Release'])
    data = args.game_data
    if data is None and sys.platform == 'darwin':
        data = DEFAULT_DATA
    if args.action == 'build':
        command = ['dotnet', 'build', str(GAME / 'SpireCompanion/SpireCompanion.csproj'), '-c', 'Release', '--nologo']
        if data is not None:
            command.append(f'-p:STS2GameDataDir={data.resolve()}')
        result = subprocess.call(command)
        if result:
            return result
        return subprocess.call(['dotnet', 'run', '--project', str(GAME / 'SpireCompanion.Checks'),
            '-c', 'Release', '--', '--inspect-mod', str(GAME / 'SpireCompanion/bin/Release/net9.0/SpireCompanion.dll')])
    mods = args.mods_dir
    if mods is None and sys.platform == 'darwin' and data == DEFAULT_DATA:
        mods = DEFAULT_DATA.parent.parent / 'MacOS/mods'
    if mods is None or not mods.is_dir():
        parser.error('Provide an existing game mods directory with --mods-dir.')
    output = GAME / 'SpireCompanion/bin/Release/net9.0'
    sources = [output / 'SpireCompanion.dll', output / 'SpireCompanion.json', ROOT / 'LICENSE']
    if any(not source.is_file() for source in sources):
        parser.error('Build successfully before install.')
    destination = mods / 'SpireCompanion'
    if destination.exists():
        if not destination.is_dir() or destination.is_symlink():
            parser.error('Existing target is not a regular directory.')
        backup_root = ROOT / '.local/mod-backups'
        backup_root.mkdir(parents=True, exist_ok=True)
        backup = backup_root / ('SpireCompanion-' + datetime.now().strftime('%Y%m%d-%H%M%S-%f'))
        shutil.copytree(destination, backup)
        print(f'Previous mod backed up to {backup}')
    destination.mkdir(exist_ok=True)
    # Validate all inputs before mutation; replace each file atomically. No other mod is altered.
    for source in sources:
        fd, temporary = tempfile.mkstemp(prefix='.install-', dir=destination)
        os.close(fd)
        try:
            shutil.copy2(source, temporary)
            os.replace(temporary, destination / source.name)
        finally:
            if os.path.exists(temporary):
                os.unlink(temporary)
    # Only retire the obsolete dependency owned by this Mod, after backing up the old install.
    obsolete = destination / 'SpireCompanion.Core.dll'
    if obsolete.is_file():
        obsolete.unlink()
    print(f'Installed development mod in {destination}')
    print('Restart the game, enable SpireCompanion, and press F9. Runtime behavior still requires in-game verification.')
    return 0

if __name__ == '__main__':
    raise SystemExit(main())
