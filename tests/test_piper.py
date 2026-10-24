import json
import re
import unittest
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
CATALOG = ROOT / 'Vidi Studio Windows' / 'Assets' / 'piper-voices.json'


class PiperCatalog(unittest.TestCase):
    def setUp(self):
        self.voices = json.loads(CATALOG.read_text(encoding='utf-8'))

    def test_catalog_is_the_public_piper_voice_collection(self):
        self.assertGreaterEqual(len(self.voices), 150)
        for voice_id, voice in self.voices.items():
            with self.subTest(voice=voice_id):
                self.assertIn('language', voice)
                self.assertIn('quality', voice)
                files = voice['files']
                models = [name for name in files if name.endswith('.onnx')]
                configs = [name for name in files if name.endswith('.onnx.json')]
                self.assertEqual(len(models), 1)
                self.assertEqual(len(configs), 1)
                for name in models + configs:
                    self.assertGreater(files[name]['size_bytes'], 0)
                    self.assertRegex(files[name]['md5_digest'], r'^[0-9a-f]{32}$')

    def test_every_catalog_voice_id_matches_its_model_file(self):
        for voice_id, voice in self.voices.items():
            with self.subTest(voice=voice_id):
                model = next(name for name in voice['files'] if name.endswith('.onnx'))
                self.assertTrue(model.endswith('/' + voice_id + '.onnx'))
                self.assertEqual(voice_id.split('-')[0], voice['language']['code'])


class PiperPackaging(unittest.TestCase):
    def test_build_fetches_the_verified_official_runtime(self):
        script = (ROOT / 'script' / 'fetch-piper.ps1').read_text(encoding='utf-8')
        self.assertIn('rhasspy/piper/releases/download', script)
        self.assertIn('2023.11.14-2', script)
        self.assertIn('F3C58906402B24F3A96D92145F58ACBA6D86C9B5DB896D207F78DC80811EFCEA', script)
        self.assertIn('Get-FileHash', script)
        self.assertIn('Expand-Archive', script)

    def test_build_scripts_prepare_piper(self):
        setup = (ROOT / 'script' / 'setup-windows.ps1').read_text(encoding='utf-8')
        build = (ROOT / 'script' / 'build-windows.ps1').read_text(encoding='utf-8')
        self.assertIn("fetch-piper.ps1", setup)
        self.assertIn("fetch-piper.ps1", build)

    def test_app_packages_the_runtime_and_catalog(self):
        project = (ROOT / 'Vidi Studio Windows' / 'Vidi Studio Windows.csproj').read_text(encoding='utf-8')
        self.assertIn(r'..\runtime\piper\**\*', project)
        self.assertIn(r'Link="piper\%(RecursiveDir)%(Filename)%(Extension)"', project)
        self.assertIn(r'Assets\piper-voices.json', project)
        self.assertIn('FetchPiperRuntime', project)

    def test_package_inspection_requires_piper(self):
        verifier = (ROOT / 'script' / 'verify-windows-package.py').read_text(encoding='utf-8')
        self.assertIn('"piper/piper.exe"', verifier)
        self.assertIn('"piper/onnxruntime.dll"', verifier)
        self.assertIn('"Assets/piper-voices.json"', verifier)
        self.assertIn('== 0x8664', verifier)
        self.assertNotIn('azure_speech.py', verifier)


class AzureRemoval(unittest.TestCase):
    def test_engine_and_app_no_longer_reference_azure_speech(self):
        engine = (ROOT / 'engine' / 'vidistudio_engine.py').read_text(encoding='utf-8')
        self.assertNotIn('azure_speech', engine)
        self.assertNotIn('list-azure-voices', engine)
        self.assertFalse((ROOT / 'engine' / 'azure_speech.py').exists())
        for path in (ROOT / 'Vidi Studio Windows').rglob('*.cs'):
            text = path.read_text(encoding='utf-8', errors='ignore')
            with self.subTest(file=path.name):
                self.assertNotIn('AzureSpeech', text)
                self.assertNotIn('DefaultAzure', text)


if __name__ == '__main__':
    unittest.main()
