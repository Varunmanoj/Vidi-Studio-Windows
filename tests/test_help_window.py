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
            html = (HELP / f"{page['slug']}.html").read_text(encoding='utf-8')
            self.assertIn('<main class="topic"', html)
            self.assertNotIn('platform-note', html)
            self.assertNotIn('Windows edition.', html)
            self.assertTrue(page['sections'], page['slug'])
            for section in page['sections']:
                self.assertIn(f'id="{section["id"]}"', html, f"{page['slug']}#{section['id']}")

    def test_windows_help_has_no_macos_only_content(self):
        for page in json.loads((HELP / 'manifest.json').read_text(encoding='utf-8'))['pages']:
            html = (HELP / f"{page['slug']}.html").read_text(encoding='utf-8')
            for mac_only in ('Command-', 'Command +', 'VoiceOver', 'Control-Option',
                             'Mac shortcut', 'Mac Help book', 'System Settings',
                             'Login Items', 'Trash', 'macOS', 'Finder'):
                self.assertNotIn(mac_only, html, f"{page['slug']}: {mac_only}")
            self.assertNotIn('both desktops', html, page['slug'])
            self.assertNotIn('both platforms', html, page['slug'])

    def test_help_window_has_native_book_navigation_and_html_reader(self):
        xaml = (APP / 'Views' / 'HelpWindow.xaml').read_text(encoding='utf-8')
        code = (APP / 'Views' / 'HelpWindow.xaml.cs').read_text(encoding='utf-8')
        self.assertIn('x:Name="TocTree"', xaml)
        self.assertIn('x:Name="ContentView"', xaml)
        self.assertIn('Search chapters and topics', xaml)
        self.assertIn('SetVirtualHostNameToFolderMapping', code)
        self.assertIn('manifest.json', code)
        self.assertIn('scrollIntoView', code)
        self.assertIn('PreviousButton', xaml)
        self.assertIn('NextButton', xaml)
        self.assertIn('<TreeView.ItemTemplate>', xaml)
        self.assertIn('AutomationProperties.Name="{Binding Title}"', xaml)
        self.assertIn('Content = new HelpNode', code)
        self.assertNotIn('NodeView', code)
        self.assertIn('ApplyWebTheme', code)
        self.assertNotIn('BrowserHelpButton', xaml)
        self.assertNotIn('HomeButton', xaml)

    def test_help_menu_and_settings_open_the_new_window(self):
        app = (APP / 'App.xaml.cs').read_text(encoding='utf-8')
        main = (APP / 'MainWindow.xaml.cs').read_text(encoding='utf-8')
        menus = (APP / 'MainWindow.Menus.cs').read_text(encoding='utf-8')
        settings = (APP / 'Views' / 'SettingsDialog.Information.cs').read_text(encoding='utf-8')
        self.assertIn('OpenHelpWindow(ElementTheme theme', app)
        self.assertIn('App.OpenHelpWindow(Root.ActualTheme)', main)
        self.assertIn('App.OpenHelpWindow(menuOwner?.CurrentTheme', settings)
        self.assertIn('internal ElementTheme CurrentTheme', menus)
        self.assertIn('Content Include="Help\\**\\*"', (APP / 'Vidi Studio Windows.csproj').read_text(encoding='utf-8'))


if __name__ == '__main__':
    unittest.main()
