import unittest
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]


def windows_sources():
    for candidate in (ROOT, ROOT / 'Vidi-Studio-Windows'):
        app = candidate / 'Vidi Studio Windows'
        if (app / 'Services' / 'SettingsStore.cs').is_file():
            return app
    return None


class ProjectPlaybackSettings(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        cls.app = windows_sources()
        if cls.app is None:
            raise unittest.SkipTest('Windows checkout is required for project playback settings checks.')

    def read(self, relative):
        return (self.app / relative).read_text(encoding='utf-8')

    def test_preferences_are_keyed_by_project_id_and_saved_outside_the_project(self):
        source = self.read('Services/ProjectPlaybackPreferences.cs')
        # The keys match the macOS ProjectPlaybackPreferences user-defaults names.
        self.assertIn('"projectNarrationEnabled." + projectId', source)
        self.assertIn('"projectFollowTranscript." + projectId', source)
        self.assertIn('settings.GetBool(NarrationKey(projectId), false)', source)
        self.assertIn('settings.GetBool(FollowKey(projectId), true)', source)
        self.assertIn('settings.Set(key, enabled);', source)
        self.assertIn('settings.Save();', source)
        self.assertIn('public static event Action<ProjectPlaybackPreferenceChange>? Changed;', source)
        # Only a real project id can own a preference.
        self.assertIn('if (string.IsNullOrWhiteSpace(projectId)) return;', source)

    def test_settings_lists_projects_after_appearance_like_macos(self):
        dialog = self.read('Views/SettingsDialog.cs')
        self.assertIn('"Appearance", "Projects", "Narration", "Gemini API", "Developer prompts", "Storage", "Help", "About"', dialog)
        self.assertIn('case 1: BuildProjects(panel); break;', dialog)
        self.assertIn('case 2: BuildNarration(panel); break;', dialog)
        self.assertIn('case 3: BuildGemini(panel); break;', dialog)
        self.assertIn('ProjectPlaybackPreferences.Changed += ReceiveProjectPlaybackPreference;', dialog)
        self.assertIn('ProjectPlaybackPreferences.Changed -= ReceiveProjectPlaybackPreference;', dialog)
        self.assertIn('model.PropertyChanged += ReceiveModelProjectChange;', dialog)

    def test_projects_page_hides_toggles_without_an_open_project(self):
        source = self.read('Views/SettingsDialog.Projects.cs')
        self.assertIn('if (model.Project is not { } project || string.IsNullOrWhiteSpace(project.Id))', source)
        self.assertIn('Text = "No Open Projects"', source)
        self.assertIn('Text = "Open a project to manage its project-specific playback settings."', source)
        self.assertIn('"Narration during playback",', source)
        self.assertIn('"Follow synchronized transcript",', source)
        self.assertIn('ProjectPlaybackPreferences.NarrationEnabled(model.Settings, project.Id)', source)
        self.assertIn('ProjectPlaybackPreferences.FollowEnabled(model.Settings, project.Id)', source)
        self.assertIn('ProjectPlaybackPreferences.SetNarrationEnabled(', source)
        self.assertIn('ProjectPlaybackPreferences.SetFollowEnabled(', source)

    def test_editor_toggles_restore_and_save_the_same_project_choices(self):
        window = self.read('MainWindow.xaml.cs')
        xaml = self.read('MainWindow.xaml')
        self.assertIn('ApplyProjectPlaybackPreferences(p);', window)
        self.assertIn('private void ApplyProjectPlaybackPreferences(StudioProject project)', window)
        self.assertIn('ProjectPlaybackPreferences.NarrationEnabled(Model.Settings, project.Id)', window)
        self.assertIn('ProjectPlaybackPreferences.FollowEnabled(Model.Settings, project.Id)', window)
        self.assertIn('private void Narration_Toggled(object sender, RoutedEventArgs e)', window)
        self.assertIn('ProjectPlaybackPreferences.SetNarrationEnabled(Model.Settings, project.Id, Narration.IsOn);', window)
        self.assertIn('ProjectPlaybackPreferences.SetFollowEnabled(Model.Settings, project.Id, Follow.IsOn);', window)
        self.assertIn('private void ReceiveProjectPlaybackPreference(ProjectPlaybackPreferenceChange change)', window)
        self.assertIn('Toggled="Narration_Toggled"', xaml)
        self.assertIn('Toggled="Follow_Toggled"', xaml)

    def test_settings_shortcuts_open_the_renumbered_sections(self):
        window = self.read('MainWindow.xaml.cs')
        self.assertIn('"Projects" => 1, "Narration" => 2, "Gemini API" => 3, "Developer prompts" => 4,', window)
        self.assertIn('"Storage" => 5, "Help" => 6, "About" => 7, _ => 0', window)
        self.assertIn('await ShowSettingsSectionAsync(3);', window)
        self.assertIn('await ShowSettingsSectionAsync(6);', window)
        self.assertIn('await ShowSettingsSectionAsync(7);', window)


if __name__ == '__main__':
    unittest.main()
