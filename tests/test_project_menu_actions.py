from pathlib import Path
import unittest

ROOT = Path(__file__).resolve().parents[1]
PROJECT = ROOT / 'Vidi Studio Windows'


class ProjectMenuActions(unittest.TestCase):
    def test_menu_entries_use_the_common_guarded_command_path(self):
        source = (PROJECT / 'MainWindow.Menus.cs').read_text(encoding='utf-8')
        self.assertIn('Separator(), Command("rename-project"), Command("trash-project")),', source)
        self.assertIn('if (!desktopEditorVisible || Model.Project is not { } p) return false;', source)
        self.assertIn('if (!Model.Idle) return false;', source)
        self.assertIn('case "rename-project": return !p.IntegrityPlaceholder && p.Integrity?.ManifestUnreadable != true && p.Integrity?.FolderMissing != true;', source)
        self.assertIn('case "trash-project": return true;', source)
        self.assertIn('await RenameProjectAsync(renameProject)', source)
        self.assertIn('await DeleteProjectAsync(trashProject)', source)
        self.assertIn('"rename-project" => "Rename", "trash-project" => "Move to Recycle Bin"', source)

    def test_whole_project_recycling_is_confirmed_and_recoverable(self):
        source = (PROJECT / 'MainWindow.xaml.cs').read_text(encoding='utf-8')
        action = source.split('private async Task DeleteProjectAsync(', 1)[1].split('private void RefreshProjectIntegrity()', 1)[0]
        self.assertIn('if (!await Confirm(title, message, action)) return;', action)
        self.assertIn('RecycleOption.SendToRecycleBin', action)
        self.assertIn('if (deletingLoadedProject) ClearProject();', action)

    def test_native_smoke_checks_offline_actions_and_cancels_recycling(self):
        source = (PROJECT / 'MainWindow.SmokeTest.cs').read_text(encoding='utf-8')
        self.assertIn('VerifyProjectMenuEntriesForSmokeTest();', source)
        self.assertIn('await VerifyProjectMenuActionsForSmokeTestAsync();', source)
        self.assertIn('("trash-project", null)', source)
        self.assertIn('FindButton(dialog, name is null ? "Cancel" : "Save")', source)


if __name__ == '__main__':
    unittest.main()
