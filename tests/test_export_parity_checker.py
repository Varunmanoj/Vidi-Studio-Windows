import os
from pathlib import Path
import shutil
import subprocess
import sys
import tempfile
import unittest


ROOT = Path(__file__).resolve().parents[1]


class ExportParityChecker(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        workspace = Path(os.environ.get('VIDISTUDIO_DESKTOP_WORKSPACE', ROOT.parent))
        cls.mac = workspace / 'VidiStudio Mac'
        if not (cls.mac / 'engine/youtube_audio.py').is_file():
            raise unittest.SkipTest('Export parity requires the Mac checkout.')

    def check_fixture(self, mutate=False):
        with tempfile.TemporaryDirectory() as folder:
            fixture = Path(folder)
            (fixture / 'script').mkdir()
            (fixture / 'engine').mkdir()
            checker = fixture / 'script/check-mac-export-parity.py'
            shutil.copy2(ROOT / 'script/check-mac-export-parity.py', checker)
            for name in ('vidistudio_engine.py', 'youtube_audio.py', 'audio_sources.py'):
                shutil.copy2(ROOT / 'engine' / name, fixture / 'engine' / name)
            if mutate:
                downloader = fixture / 'engine/youtube_audio.py'
                source = downloader.read_text(encoding='utf-8')
                self.assertIn('DOWNLOAD_ATTEMPTS = 3', source)
                downloader.write_text(source.replace('DOWNLOAD_ATTEMPTS = 3', 'DOWNLOAD_ATTEMPTS = 2'), encoding='utf-8')
            return subprocess.run([sys.executable, str(checker), '--mac-root', str(self.mac)],
                                  capture_output=True, text=True)

    def test_reviewed_platform_downloaders_pass(self):
        result = self.check_fixture()
        self.assertEqual(result.returncode, 0, result.stdout + result.stderr)
        self.assertIn('53 shared export/progress functions', result.stdout)

    def test_changed_downloader_retry_behavior_is_rejected(self):
        result = self.check_fixture(mutate=True)
        self.assertNotEqual(result.returncode, 0)
        self.assertIn('Export parity needs review: youtube_audio.py', result.stdout + result.stderr)
