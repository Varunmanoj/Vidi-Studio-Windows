import json
import re
from urllib.parse import unquote, urlsplit
import unittest
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
APP = ROOT / 'Vidi Studio Windows'
HELP = APP / 'Help'


class HelpWindowTests(unittest.TestCase):
    def test_local_help_links_and_fragments_resolve(self):
        for page in HELP.glob('*.html'):
            html = page.read_text(encoding='utf-8')
            for reference in re.findall(r'(?:href|src)=[\"\']([^\"\']+)[\"\']', html):
                link = urlsplit(reference)
                if link.scheme or link.netloc:
                    continue
                target = page.parent / unquote(link.path) if link.path else page
                self.assertTrue(target.is_file(), f'{page.name}: {reference}')
                if link.fragment and target.suffix == '.html':
                    ids = re.findall(r'id=[\"\']([^\"\']+)[\"\']', target.read_text(encoding='utf-8'))
                    self.assertIn(unquote(link.fragment), ids, f'{page.name}: {reference}')

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
            if page['slug'] == 'playback':
                # This chapter intentionally documents players on both systems;
                # the generator explicitly preserves it rather than windowsizing.
                self.assertIn('QuickTime', html)
                self.assertIn('Movies &amp; TV', html)
                self.assertIn('VLC', html)
                self.assertIn('https://support.apple.com/', html)
                continue
            # The explicitly shared requirements matrix names both platforms.
            # Platform-specific instructions outside it must still be converted.
            html = re.sub(r'<!-- shared-platforms:start -->.*?<!-- shared-platforms:end -->', '', html, flags=re.S)
            for mac_only in ('Command-', 'Command +', 'VoiceOver', 'Control-Option',
                             'Mac shortcut', 'Mac Help book', 'System Settings',
                             'Login Items', 'Trash', 'macOS', 'Finder'):
                self.assertNotIn(mac_only, html, f"{page['slug']}: {mac_only}")
            self.assertNotIn('both desktops', html, page['slug'])
            self.assertNotIn('both platforms', html, page['slug'])

    def test_shared_requirements_keep_the_real_platform_names(self):
        html = (HELP / 'welcome.html').read_text(encoding='utf-8')
        sections = re.findall(r'<!-- shared-platforms:start -->(.*?)<!-- shared-platforms:end -->', html, flags=re.S)
        self.assertEqual(len(sections), 1)
        requirements = sections[0]
        self.assertIn('macOS 26.4', requirements)
        self.assertIn('Windows 10 version 1809', requirements)
        self.assertIn('Windows 11 version 21H2', requirements)
        self.assertNotIn('Windows 26.4', requirements)

    def test_welcome_puts_getting_started_before_requirements(self):
        html = (HELP / 'welcome.html').read_text(encoding='utf-8')
        self.assertRegex(html, r'<h1>Welcome to VidiStudio</h1>\s*<p>VidiStudio is a native Mac and Windows application')
        self.assertEqual(re.findall(r'<h2[^>]*>([^<]+)</h2>', html)[:4], [
            'Understand the three kinds of content',
            'How a project works',
            'Start with a short video',
            'Install VidiStudio',
        ])
        self.assertGreater(html.index('id="system-requirements"'), html.index('id="install-vidistudio"'))

        pages = json.loads((HELP / 'manifest.json').read_text(encoding='utf-8'))['pages']
        self.assertEqual(pages[0]['slug'], 'welcome')
        self.assertIn({'id': 'system-requirements', 'title': 'System requirements'}, pages[0]['sections'])

    def test_browser_installation_is_searchable_and_links_to_both_stores(self):
        pages = json.loads((HELP / 'manifest.json').read_text(encoding='utf-8'))['pages']
        topic = next(page for page in pages if page['slug'] == 'browser-extensions')
        self.assertIn('Browser extensions', topic['title'])
        self.assertIn(
            {'id': 'install-chrome-edge', 'title': 'Install for Chrome or Edge'},
            topic['sections'],
        )
        html = (HELP / 'browser-extensions.html').read_text(encoding='utf-8')
        self.assertRegex(html, r'<a href="https://chromewebstore\.google\.com/detail/[^"]+">[^<]+Chrome Web Store</a>')
        self.assertRegex(html, r'<a href="https://microsoftedge\.microsoft\.com/addons/detail/[^"]+">[^<]+Microsoft Edge Add-ons</a>')
        self.assertNotIn('Load unpacked', html)
        self.assertNotIn('Safari', html)

    def test_help_window_loads_live_guide_and_handles_offline_requests(self):
        code = (APP / 'Views' / 'HelpWindow.xaml.cs').read_text(encoding='utf-8')
        self.assertIn('https://vidistudio.varunmanojkumar.in/docs', code)
        self.assertIn('NavigateWithWebResourceRequest(request)', code)
        self.assertIn('Cache-Control: no-cache', code)
        self.assertIn('Network.setBypassServiceWorker', code)
        self.assertNotIn('docs-data/v1/', code)
        self.assertIn('Documentation requires internet.', code)
        self.assertNotIn('SetVirtualHostNameToFolderMapping', code)

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
