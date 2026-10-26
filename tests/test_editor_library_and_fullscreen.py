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
        self.assertIn('Click="ToggleLibrary_Click"', xaml)
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
        self.assertIn('Menu("View",\n                Command("preview"), Command("fullscreen"), Command("toggle-timeline")', menus)
        self.assertIn('Menu("Playback",\n                Command("preview"), Command("fullscreen"), Command("play")', menus)
        self.assertIn('["fullscreen"] = (VirtualKey.F, VirtualKeyModifiers.Control | VirtualKeyModifiers.Menu, "Ctrl+Alt+F")', menus)
        self.assertIn('AddShortcut(VirtualKey.F11, VirtualKeyModifiers.None', menus)


if __name__ == '__main__':
    unittest.main()
