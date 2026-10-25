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

    def test_languages_use_plain_english_names_and_dialects(self):
        for voice_id, voice in self.voices.items():
            with self.subTest(voice=voice_id):
                language = voice['language']
                self.assertTrue(language.get('family'))
                self.assertTrue(language.get('name_english'))
                self.assertTrue(language.get('country_english'))

    def test_every_catalog_voice_has_a_published_sample_beside_its_model(self):
        for voice_id, voice in self.voices.items():
            with self.subTest(voice=voice_id):
                model = next(name for name in voice['files'] if name.endswith('.onnx'))
                sample = model.rsplit('/', 1)[0] + '/samples/speaker_0.mp3'
                self.assertTrue(sample.endswith('/samples/speaker_0.mp3'))
                self.assertEqual(sample.split('/')[-2], 'samples')


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


class PiperVoiceBrowser(unittest.TestCase):
    def test_catalog_service_reads_plain_english_language_names(self):
        service = (ROOT / 'Vidi Studio Windows' / 'Services' / 'PiperService.cs').read_text(encoding='utf-8')
        self.assertIn('"family"', service)
        self.assertIn('"name_english"', service)
        self.assertIn('"country_english"', service)
        self.assertIn('public string LanguageLabel', service)
        self.assertIn('public string QualityLabel', service)
        self.assertIn('public static string QualityLabelFor', service)
        self.assertIn('public static int QualityRank', service)
        # The visible label is "English (United States)", never just "en-US".
        self.assertIn('? LanguageName', service)
        self.assertIn(': $"{LanguageName} ({Country})"', service)
        self.assertIn('public string DisplayName => $"{Name} ({QualityLabel}) — {LanguageLabel}"', service)
        self.assertIn('public string SamplePath', service)
        self.assertIn('samples/speaker_0.mp3', service)

    def test_dialog_filters_sorts_and_previews_hosted_samples(self):
        dialog = (ROOT / 'Vidi Studio Windows' / 'Views' / 'PiperVoicesDialog.cs').read_text(encoding='utf-8')
        self.assertIn('Header = "Language"', dialog)
        self.assertIn('Header = "Quality"', dialog)
        self.assertIn('Header = "Sort by"', dialog)
        self.assertIn('new("", "All languages")', dialog)
        self.assertIn('new("", "All qualities")', dialog)
        self.assertIn('new("language", "Language (A–Z)")', dialog)
        self.assertIn('new("name", "Voice name (A–Z)")', dialog)
        self.assertIn('new("quality", "Quality (high to low)")', dialog)
        self.assertIn('new("size", "Size (smallest first)")', dialog)
        self.assertIn('voice.LanguageFamily.Equals(language', dialog)
        self.assertIn('voice.Quality.Equals(quality', dialog)
        self.assertIn('ToggleSampleAsync', dialog)
        self.assertIn('PiperService.GetSampleAsync', dialog)
        self.assertIn('"Play sample"', dialog)
        self.assertIn('"Stop sample"', dialog)
        self.assertIn('MediaPlayerAudioCategory.Speech', dialog)
        self.assertIn('The preview needs the internet', dialog)

    def test_dialog_shows_download_and_removal_progress(self):
        dialog = (ROOT / 'Vidi Studio Windows' / 'Views' / 'PiperVoicesDialog.cs').read_text(encoding='utf-8')
        settings = (ROOT / 'Vidi Studio Windows' / 'Views' / 'SettingsDialog.Voices.cs').read_text(encoding='utf-8')
        # Each row owns an inline progress bar and spinner, updated in place during a transfer.
        self.assertIn('private sealed class RowState', dialog)
        self.assertIn('public ProgressBar Bar', dialog)
        self.assertIn('public ProgressRing Ring', dialog)
        self.assertIn('private readonly Dictionary<string, RowState> rows', dialog)
        self.assertIn('rows[voice.Id] = state', dialog)
        self.assertIn('private void ReportDownload(', dialog)
        self.assertIn('state.Bar.Value = downloadFraction * 100', dialog)
        self.assertIn('state.ProgressText.Text = ProgressLabel(', dialog)
        self.assertIn('private static string ProgressLabel(', dialog)
        self.assertIn('of {SizeText(total)}', dialog)
        # Downloading and deleting both report through the shared indicator and disable the row.
        self.assertIn('downloadingVoiceId = voice.Id', dialog)
        self.assertIn('downloadingVoiceId = null', dialog)
        self.assertIn('deletingVoiceId = voice.Id', dialog)
        self.assertIn('deletingVoiceId = null', dialog)
        self.assertIn('"Deleting…"', dialog)
        self.assertIn('"Downloading…"', dialog)
        # Each row's download or delete action carries a Fluent icon.
        self.assertIn('IconButtonContent.Set(state.Action,', dialog)
        self.assertIn('downloaded || deleting ? Symbol.Delete : Symbol.Download);', dialog)
        # Delete is red in the download catalog too, not only in the delete dialog.
        self.assertIn('IconButtonContent.SetDestructive(state.Action, downloaded || deleting);', dialog)
        self.assertIn('internal static void SetDestructive(Button button, bool destructive)', (ROOT / 'Vidi Studio Windows' / 'Views' / 'IconButtonContent.cs').read_text(encoding='utf-8'))
        # Each playing row shows a fixed-width audio icon beside its sample button.
        self.assertIn('public FontIcon SamplePlaying', dialog)
        self.assertIn('Glyph = "\\uE9D9"', dialog)
        self.assertIn('public Grid SampleStatus', dialog)
        self.assertIn('public ProgressRing SampleLoading', dialog)
        self.assertIn('Grid.SetColumn(state.SampleStatus, 2)', dialog)
        # Fixed button widths keep the sample and action columns aligned in every row.
        self.assertIn('public Button Sample { get; } = new() { Width = 120', dialog)
        self.assertIn('public Button Action { get; } = new() { Width = 150', dialog)
        delete_dialog = (ROOT / 'Vidi Studio Windows' / 'Views' / 'PiperDeleteVoicesDialog.cs').read_text(encoding='utf-8')
        self.assertIn('public FontIcon SamplePlaying', delete_dialog)
        self.assertIn('Glyph = "\\uE9D9"', delete_dialog)
        self.assertIn('public Grid SampleStatus', delete_dialog)
        self.assertIn('public ProgressRing SampleLoading', delete_dialog)
        self.assertIn('Grid.SetColumn(state.SampleStatus, 2)', delete_dialog)
        self.assertIn('public Button Sample { get; } = new() { Width = 120', delete_dialog)
        self.assertIn('public Button Action { get; } = new() { Width = 110', delete_dialog)
        self.assertIn('IconButtonContent.Set(manage, "Download or remove voices…", Symbol.Download)', settings)
        self.assertIn('progress.IsIndeterminate = true', dialog)
        self.assertIn('progress.Visibility = Visibility.Visible', dialog)
        self.assertIn('await Task.Run(() => PiperService.DeleteVoice(voice.Id))', dialog)
        self.assertIn('state.Ring.IsActive = downloading || deleting', dialog)
        self.assertIn('state.Action.IsEnabled = !locked && !downloading && !deleting', dialog)
        self.assertIn('Voice download or removal progress', dialog)

    def test_sample_download_is_cached_and_removed_with_the_voice(self):
        service = (ROOT / 'Vidi Studio Windows' / 'Services' / 'PiperService.cs').read_text(encoding='utf-8')
        self.assertIn('public static async Task<string> GetSampleAsync', service)
        self.assertIn('private static string SampleCachePath', service)
        self.assertIn('TryDelete(SampleCachePath(id))', service)
        self.assertIn('"samples"', service)


    def test_delete_dialog_lists_only_downloaded_voices_with_samples_and_filters(self):
        dialog = (ROOT / 'Vidi Studio Windows' / 'Views' / 'PiperDeleteVoicesDialog.cs').read_text(encoding='utf-8')
        self.assertIn('public sealed class PiperDeleteVoicesDialog : ContentDialog', dialog)
        self.assertIn('Title = "Delete Piper Voices"', dialog)
        self.assertIn('PiperService.Catalog.Where(voice => PiperService.IsVoiceDownloaded(voice.Id))', dialog)
        self.assertIn('Header = "Language"', dialog)
        self.assertIn('Header = "Quality"', dialog)
        self.assertIn('Header = "Sort by"', dialog)
        self.assertIn('new("language", "Language (A–Z)")', dialog)
        self.assertIn('new("name", "Voice name (A–Z)")', dialog)
        self.assertIn('new("quality", "Quality (high to low)")', dialog)
        self.assertIn('new("size", "Size (smallest first)")', dialog)
        self.assertIn('ToggleSampleAsync', dialog)
        self.assertIn('PiperService.GetSampleAsync', dialog)
        self.assertIn('"Play sample"', dialog)
        self.assertIn('PiperService.DeleteVoice(voice.Id)', dialog)
        self.assertIn('VirtualKey.Escape', dialog)
        # Delete and download actions carry Fluent icons.
        self.assertIn('IconButtonContent.Set(state.Action, removing ? "Deleting…" : "Delete", Symbol.Delete)', dialog)
        # The settings page shows the two dialog entry points without long descriptions.
        settings = (ROOT / 'Vidi Studio Windows' / 'Views' / 'SettingsDialog.Voices.cs').read_text(encoding='utf-8')
        self.assertIn('"Download or remove voices…"', settings)
        self.assertIn('"Delete voices…"', settings)
        self.assertIn('new PiperDeleteVoicesDialog()', settings)
        self.assertNotIn('Piper Voices are free neural voices', settings)
        self.assertNotIn('open-source project by Rhasspy', settings)
        # Both Piper dialogs close with Escape.
        catalog = (ROOT / 'Vidi Studio Windows' / 'Views' / 'PiperVoicesDialog.cs').read_text(encoding='utf-8')
        self.assertIn('VirtualKey.Escape', catalog)
        self.assertNotIn('Choose the voices you want to use with Piper.', catalog)


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
