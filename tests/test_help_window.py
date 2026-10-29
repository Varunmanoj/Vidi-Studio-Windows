import json
import unittest
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
APP = ROOT / 'Vidi Studio Windows'
HELP = APP / 'Help'


class HelpWindowTests(unittest.TestCase):
    def test_manifest_covers_the_full_book(self):
        manifest = json.loads((HELP / 'manifest.json').read_text(encoding='utf-8'))
        pages = manifest['pages']
        self.assertGreaterEqual(len(pages), 12)
        slugs = {page['slug'] for page in pages}
        self.assertIn('welcome', slugs)
        self.assertIn('keyboard', slugs)
        self.assertIn('privacy', slugs)
        for page in pages:
            self.assertTrue((HELP / f"{page['slug']}.html").is_file(), page['slug'])
            self.assertTrue(page['sections'], page['slug'])

    def test_help_window_has_native_book_navigation_and_html_reader(self):
        xaml = (APP / 'Views' / 'HelpWindow.xaml').read_text(encoding='utf-8')
        code = (APP / 'Views' / 'HelpWindow.xaml.cs').read_text(encoding='utf-8')
        self.assertIn('x:Name="TocTree"', xaml)
        self.assertIn('x:Name="ContentView"', xaml)
        self.assertIn('Search chapters and topics', xaml)
        self.assertIn('SetVirtualHostNameToFolderMapping', code)
        self.assertIn('manifest.json', code)
        self.assertIn('scrollIntoView', code)

    def test_help_menu_and_settings_open_the_new_window(self):
        app = (APP / 'App.xaml.cs').read_text(encoding='utf-8')
        main = (APP / 'MainWindow.xaml.cs').read_text(encoding='utf-8')
        settings = (APP / 'Views' / 'SettingsDialog.Information.cs').read_text(encoding='utf-8')
        self.assertIn('OpenHelpWindow', app)
        self.assertIn('App.OpenHelpWindow()', main)
        self.assertIn('App.OpenHelpWindow()', settings)
        self.assertIn('Content Include="Help\\**\\*"', (APP / 'Vidi Studio Windows.csproj').read_text(encoding='utf-8'))


if __name__ == '__main__':
    unittest.main()
