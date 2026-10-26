import unittest
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
APP = ROOT / 'Vidi Studio Windows'


class GalleryRefresh(unittest.TestCase):
    def test_reload_reuses_projects_instead_of_rebuilding_the_collection(self):
        view_model = (APP / 'ViewModels' / 'StudioViewModel.cs').read_text(encoding='utf-8')
        reload_start = view_model.index('public void Reload(')
        reload_end = view_model.index('/// <summary>\n    /// Reloads the gallery only', reload_start)
        reload = view_model[reload_start:reload_end]
        self.assertNotIn('Projects.Clear()', reload)
        self.assertIn('knownProjects.TryGetValue(folder, out var cached)', reload)
        self.assertIn('ApplyProjectList(desired)', reload)
        self.assertIn('public bool ReloadIfProjectsChanged', view_model)
        self.assertIn('ProjectStamp', view_model)

    def test_integrity_timer_does_not_reload_an_unchanged_gallery(self):
        main = (APP / 'MainWindow.xaml.cs').read_text(encoding='utf-8')
        start = main.index('private void RefreshProjectIntegrity()')
        end = main.index('private void UpdateProjectIntegrityUi()', start)
        watchdog = main[start:end]
        self.assertIn('ReloadIfProjectsChanged', watchdog)
        self.assertIn('Model.RefreshProjectIntegrity();', watchdog)
        self.assertNotIn('Model.Reload(', watchdog)

    def test_project_integrity_updates_notify_existing_gallery_rows(self):
        project = (APP / 'Models' / 'StudioProject.cs').read_text(encoding='utf-8')
        self.assertIn('public sealed class StudioProject : INotifyPropertyChanged', project)
        self.assertIn('new PropertyChangedEventArgs(nameof(IntegrityStatus))', project)
        self.assertIn('new PropertyChangedEventArgs(nameof(IntegrityIsDamaged))', project)

    def test_manifest_reads_allow_the_engine_to_replace_project_json(self):
        project = (APP / 'Models' / 'StudioProject.cs').read_text(encoding='utf-8')
        integrity = (APP / 'Models' / 'ProjectIntegrity.cs').read_text(encoding='utf-8')
        self.assertIn('FileShare.ReadWrite | FileShare.Delete', project)
        self.assertIn('FileShare.ReadWrite | FileShare.Delete', integrity)
        self.assertIn('StudioProject.ReadManifestText(path)', integrity)


if __name__ == '__main__':
    unittest.main()
