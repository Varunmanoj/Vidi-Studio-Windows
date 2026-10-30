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

    def test_new_window_action_and_in_place_editor_switch_are_explicit(self):
        self.assertIn('Item("Open in New Window", (_, _) => App.OpenWindow(project.Folder), commandId: "open-editor-window");', COMMANDS)
        self.assertIn('Item("Open in This Window", async (_, _) =>', COMMANDS)
        self.assertIn('await SwitchProjectAsync(project);', COMMANDS)
        self.assertIn('commandId: "open-project-here"', COMMANDS)


if __name__ == '__main__':
    unittest.main()
