from contextlib import redirect_stderr
import importlib.util
import io
import json
from pathlib import Path
import sys
import tempfile
import unittest
from unittest.mock import patch
import zipfile

ROOT = Path(__file__).resolve().parents[2]


def script(name):
    spec = importlib.util.spec_from_file_location(name, ROOT / 'scripts' / (name + '.py'))
    module = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(module)
    return module


class PackagingTests(unittest.TestCase):
    def setUp(self):
        self.temporary = tempfile.TemporaryDirectory(prefix='voice 安装 ')
        self.addCleanup(self.temporary.cleanup)
        self.root = Path(self.temporary.name)
        self.game = self.root / 'game/legacy'
        built = self.game / 'SpireCompanion/bin/Release/net9.0'
        built.mkdir(parents=True)
        for name in ['SpireCompanion.dll', 'SpireCompanion.json']:
            (built / name).write_text('fixture')
        (self.root / 'LICENSE').write_text('fixture license')
        scripts = self.root / 'scripts'
        scripts.mkdir()
        for name in ['install_windows.bat', 'install_windows.ps1', 'update_windows.bat']:
            (scripts / name).write_text('fixture installer')
        self.packager = script('package_release')

    def package(self, runtime=None):
        dist = self.root / 'dist'
        argv = ['package_release.py'] + (['--voice-runtime', str(runtime)] if runtime else [])
        with patch.multiple(self.packager, ROOT=self.root, DIST=dist, STAGING=dist / 'staging'), \
                patch.object(self.packager.subprocess, 'run') as build, patch.object(sys, 'argv', argv):
            self.assertEqual(self.packager.main(), 0)
            build.assert_called_once()
        return zipfile.ZipFile(dist / 'SpireCompanion-win64.zip')

    def test_source_install_wires_local_environment_without_copying_it(self):
        developer = script('game_dev')
        python = self.root / 'voice/.venv' / ('Scripts/python.exe' if sys.platform == 'win32' else 'bin/python')
        python.parent.mkdir(parents=True)
        python.write_text('fixture interpreter')
        entry = self.root / 'voice/voice_service.py'
        entry.write_text('fixture entry')
        model = self.root / 'voice/models/vosk-model-small-cn-0.22'
        (model / 'am').mkdir(parents=True)
        (model / 'am/final.mdl').write_text('fixture model')
        mods = self.root / 'mods'
        mods.mkdir()
        with patch.multiple(developer, ROOT=self.root, GAME=self.game):
            target = developer.install(mods)
        config = json.loads((target / 'voice-launch.json').read_text())
        self.assertEqual(config, {'Python': str(python), 'Script': str(entry), 'Model': str(model)})
        self.assertFalse((target / 'voice/.venv').exists())

    def test_release_without_runtime_explicitly_describes_text_only(self):
        with self.package() as archive:
            self.assertIn('本安装包仅提供文字聊天', archive.read('README.txt').decode())
            self.assertFalse(any('voice-launch.json' in name or '.venv' in name for name in archive.namelist()))

    def test_voice_release_contains_runtime_dependencies_and_model(self):
        runtime = self.root / 'runtime'
        for name in ['spire-voice.exe', '_internal/library.dll', 'models/vosk-model-small-cn-0.22/am/final.mdl']:
            path = runtime / name
            path.parent.mkdir(parents=True, exist_ok=True)
            path.write_text('fixture')
        with self.package(runtime) as archive:
            for name in ['spire-voice.exe', '_internal/library.dll', 'models/vosk-model-small-cn-0.22/am/final.mdl']:
                self.assertIn('SpireCompanion/voice/' + name, archive.namelist())
            self.assertIn('无需安装 Python 或打开终端', archive.read('README.txt').decode())

    def test_incomplete_voice_runtime_is_rejected_before_build(self):
        with patch.object(sys, 'argv', ['package_release.py', '--voice-runtime', str(self.root)]), \
                patch.object(self.packager.subprocess, 'run') as build, redirect_stderr(io.StringIO()):
            with self.assertRaises(SystemExit):
                self.packager.main()
            build.assert_not_called()

    def test_publisher_preserves_voice_runtime_option_before_authentication(self):
        publisher = script('publish_release')
        runtime = self.root / 'runtime'
        with patch.object(sys, 'argv', ['publish_release.py', '--voice-runtime', str(runtime)]), \
                patch.object(publisher.subprocess, 'run', side_effect=RuntimeError('stop before publishing')) as package, \
                patch.object(publisher, 'token') as auth:
            with self.assertRaisesRegex(RuntimeError, 'stop before publishing'):
                publisher.main()
            self.assertEqual(package.call_args.args[0][-2:], ['--voice-runtime', str(runtime)])
            auth.assert_not_called()


if __name__ == '__main__':
    unittest.main()
