from pathlib import Path
import unittest


ROOT = Path(__file__).resolve().parents[1]
COMMANDS = (ROOT / 'Vidi Studio Windows' / 'MainWindow.Commands.cs').read_text(encoding='utf-8')


class ProjectContextMenu(unittest.TestCase):
    def test_context_actions_do_not_open_the_editor_unconditionally(self):
        self.assertNotIn('item.Click += (s, args) => { Model.Open(project.Folder); handler(s, args); };', COMMANDS)
        self.assertIn('void LoadForEditorAction()', COMMANDS)
        self.assertIn('if (Model.Project is not { } current || !string.Equals(current.Folder, project.Folder, StringComparison.OrdinalIgnoreCase))', COMMANDS)

    def test_folder_and_configuration_act_on_the_clicked_project_only(self):
        self.assertIn('Item("Open project folder", (_, _) => _ = RevealProjectAsync(project));', COMMANDS)
        self.assertIn('Item("Open project configuration", (_, _) => _ = OpenProjectConfigurationAsync(project));', COMMANDS)

    def test_open_in_new_window_is_the_only_editor_window_action(self):
        self.assertIn('Item("Open editor in new window", (_, _) => App.OpenWindow(project.Folder));', COMMANDS)


if __name__ == '__main__':
    unittest.main()
