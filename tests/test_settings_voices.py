import unittest
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
VIEWS = ROOT / 'Vidi Studio Windows' / 'Views'


class NarrationVoiceSettings(unittest.TestCase):
    def test_narration_tab_has_no_voice_filter_box(self):
        dialog = (VIEWS / 'SettingsDialog.cs').read_text(encoding='utf-8')
        voices = (VIEWS / 'SettingsDialog.Voices.cs').read_text(encoding='utf-8')
        self.assertNotIn('Header = "Filter voices"', dialog)
        self.assertNotIn('FilterWithGroupHeadings', dialog)
        self.assertNotIn('FilterWithGroupHeadings', voices)
        self.assertIn('panel.Children.Add(voice); panel.Children.Add(sapiHost);', dialog)
        # The picker still filters by engine and keeps group headings for Piper.
        self.assertIn('voice.ItemsSource = filtered;', dialog)


if __name__ == '__main__':
    unittest.main()
