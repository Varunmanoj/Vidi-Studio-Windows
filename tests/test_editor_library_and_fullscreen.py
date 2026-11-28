import unittest
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
APP = ROOT / 'Vidi Studio Windows'


class EditorProjectControls(unittest.TestCase):
    def test_editor_has_persistent_bottom_left_project_buttons(self):
        xaml = (APP / 'MainWindow.xaml').read_text(encoding='utf-8')
        commands = (APP / 'MainWindow.Commands.cs').read_text(encoding='utf-8')
        self.assertIn('x:Name="EditorProjectLayoutButton"', xaml)
        self.assertIn('x:Name="EditorProjectListButton"', xaml)
        self.assertIn('Click="ToggleGrid_Click"', xaml)
        toggle = xaml.split('x:Name="EditorProjectListButton"', 1)[1].split('>', 1)[0]
        self.assertIn('Checked="PanelVisibilityToggle_Changed"', toggle)
        self.assertIn('Unchecked="PanelVisibilityToggle_Changed"', toggle)
        self.assertIn('private void UpdateEditorProjectButtons()', commands)
        self.assertIn('UpdateEditorProjectButtons();', commands)

    def test_fullscreen_uses_the_appwindow_presenter_not_the_uwp_only_property(self):
        commands = (APP / 'MainWindow.Commands.cs').read_text(encoding='utf-8')
        self.assertNotIn('Player.IsFullWindow = !Player.IsFullWindow', commands)
        self.assertIn('AppWindowPresenterKind.FullScreen', commands)
        self.assertIn('private void EnterFullScreenVideo()', commands)
        self.assertIn('private void ExitFullScreenVideo()', commands)
        self.assertIn('fullScreenExitButton', commands)
        self.assertIn('Root.Children.Remove(PreviewPanel)', commands)

    def test_escape_exits_fullscreen(self):
        main = (APP / 'MainWindow.xaml.cs').read_text(encoding='utf-8')
        self.assertIn('new KeyboardAccelerator { Key = VirtualKey.Escape }', main)
        self.assertIn('ExitFullScreenVideo();', main)

    def test_fullscreen_is_in_view_and_playback_with_shortcuts(self):
        menus = (APP / 'MainWindow.Menus.cs').read_text(encoding='utf-8')
        view = menus.split('Menu("View",', 1)[1].split('Menu("Playback",', 1)[0]
        playback = menus.split('Menu("Playback",', 1)[1].split('Menu("Project",', 1)[0]
        for menu in (view, playback):
            self.assertLess(menu.index('Command("preview")'), menu.index('Command("fullscreen")'))
        self.assertLess(view.index('Command("fullscreen")'), view.index('Command("toggle-timeline")'))
        self.assertLess(playback.index('Command("fullscreen")'), playback.index('Command("play")'))
        self.assertIn('["fullscreen"] = (VirtualKey.F, VirtualKeyModifiers.Control | VirtualKeyModifiers.Menu, "Ctrl+Alt+F")', menus)
        self.assertIn('AddShortcut(VirtualKey.F11, VirtualKeyModifiers.None', menus)


if __name__ == '__main__':
    unittest.main()
