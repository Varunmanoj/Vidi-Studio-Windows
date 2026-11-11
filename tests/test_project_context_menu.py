from pathlib import Path
import unittest


ROOT = Path(__file__).resolve().parents[1]
COMMANDS = (ROOT / 'Vidi Studio Windows' / 'MainWindow.Commands.cs').read_text(encoding='utf-8')


class ProjectContextMenu(unittest.TestCase):
    def test_context_actions_do_not_open_the_editor_unconditionally(self):
        self.assertNotIn('item.Click += (s, args) => { Model.Open(project.Folder); handler(s, args); };', COMMANDS)
        self.assertIn('RunProjectContextCommandAsync(string id, StudioProject project, bool editorBrowser)', COMMANDS)
        self.assertIn('if (!CanRunProjectContextCommand(id, project, editorBrowser)) return;', COMMANDS)
        self.assertIn('if (Model.Project is not { } current || !string.Equals(current.Folder, project.Folder, StringComparison.OrdinalIgnoreCase))', COMMANDS)

    def test_folder_and_configuration_act_on_the_clicked_project_only(self):
        self.assertIn('case "show-in-finder": await RevealProjectAsync(project); return;', COMMANDS)
        self.assertIn('if (Model.Settings.GetBool("developerMode")) Item("open-configuration", "Open Project Configuration");', COMMANDS)
        self.assertIn('case "open-configuration": await OpenProjectConfigurationAsync(project); return;', COMMANDS)
        self.assertIn('if (id == "open-configuration") return Model.Settings.GetBool("developerMode")', COMMANDS)


    def test_new_window_action_and_in_place_editor_switch_are_explicit(self):
        self.assertIn('Item("open-editor-window", "Open in New Window");', COMMANDS)
        self.assertIn('Item("open-project-here", "Open in This Window");', COMMANDS)
        self.assertIn('case "open-project-here": await SwitchProjectAsync(project); return;', COMMANDS)
        self.assertIn('case "open-editor-window": App.OpenWindow(project.Folder); return;', COMMANDS)



if __name__ == '__main__':
    unittest.main()
