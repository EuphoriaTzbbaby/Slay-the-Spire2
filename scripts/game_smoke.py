"""macOS isolated offline Mod-load smoke test. Does not open a real run or use Steam saves."""
from pathlib import Path
import json
import secrets
import shutil
import subprocess
import sys
from datetime import datetime

ROOT = Path(__file__).resolve().parents[1]
APP = Path.home() / 'Library/Application Support/Steam/steamapps/common/Slay the Spire 2/SlayTheSpire2.app'

def main():
    if sys.platform != 'darwin':
        raise SystemExit('This isolated launcher currently supports macOS only.')
    output = ROOT / 'game/legacy/SpireCompanion/bin/Release/net9.0'
    names = ['SpireCompanion.dll', 'SpireCompanion.json']
    if not APP.is_dir() or any(not (output / name).is_file() for name in names):
        raise SystemExit('Install the game and build SpireCompanion first.')
    stage = ROOT / '.local/smoke' / datetime.now().strftime('%Y%m%d-%H%M%S-%f')
    stage.mkdir(parents=True)
    app = stage / 'SlayTheSpire2.app'
    profile = Path.home() / 'Library/Application Support/SlayTheSpire2/default' / str(1_000_000_000 + secrets.randbelow(900_000_000))
    profile_created = False
    try:
        subprocess.run(['cp', '-cR', str(APP), str(app)], check=True, capture_output=True)
        mods = app / 'Contents/MacOS/mods'
        if mods.exists():
            shutil.rmtree(mods)
        target = mods / 'SpireCompanion'
        target.mkdir(parents=True)
        for name in names:
            shutil.copy2(output / name, target / name)
        shutil.copy2(ROOT / 'LICENSE', target / 'LICENSE')
        profile.mkdir(exist_ok=False)
        profile_created = True
        (profile / 'settings.save').write_text(json.dumps({
            'schema_version': 8,
            'mod_settings': {'mods_enabled': True, 'mod_list': [
                {'id': 'SpireCompanion', 'is_enabled': True, 'source': 'mods_directory'}]},
            'seen_ea_disclaimer': True, 'skip_intro_logo': True, 'volume_master': 0,
        }), encoding='utf-8')
        executable = app / 'Contents/MacOS/Slay the Spire 2'
        with (stage / 'stdout.log').open('w', encoding='utf-8') as log:
            result = subprocess.run([
                str(executable), '--headless', '--max-fps', '30', '--quit-after', '300',
                '--log-file', str(stage / 'godot.log'), '--force-steam', 'off', '--clientId', profile.name,
            ], cwd=executable.parent, stdout=log, stderr=subprocess.STDOUT, timeout=45)
        text = (stage / 'godot.log').read_text(encoding='utf-8', errors='replace')
        failures = ['[SpireCompanion] read_state:', '[SpireCompanion] solver_start:',
                    'SpireCompanion.CompanionPanel..ctor', 'FreeType: Error loading font',
                    'Failed to load mod', 'System.TypeLoadException', 'System.IO.FileNotFoundException',
                    'not supported for conversion to/from Variant']
        if result.returncode != 0 or '[SpireCompanion] PANEL_READY' not in text or 'font_ready=True' not in text or any(item in text for item in failures):
            raise RuntimeError(f'Mod load smoke test failed; inspect {stage}')
        print('PASS: isolated Mod initialization, panel creation and main-menu state read.')
        print('NOT VERIFIED: visible layout, fonts on screen, combat actions or actual CombatSolver handoff.')
        print(f'Evidence: {stage}')
        return 0
    finally:
        if profile_created:
            shutil.rmtree(profile)
        if app.exists():
            shutil.rmtree(app)

if __name__ == '__main__':
    raise SystemExit(main())
