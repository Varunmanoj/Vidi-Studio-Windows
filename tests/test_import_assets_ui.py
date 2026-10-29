import unittest
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
UI = (ROOT / 'Vidi Studio Windows' / 'MainWindow.xaml').read_text(encoding='utf-8')
MODEL = (ROOT / 'Vidi Studio Windows' / 'Models' / 'StudioProject.cs').read_text(encoding='utf-8')
IMPORTS = (ROOT / 'Vidi Studio Windows' / 'MainWindow.Imports.cs').read_text(encoding='utf-8')
DIALOG = (ROOT / 'Vidi Studio Windows' / 'Views' / 'ImportAssetsDialog.cs').read_text(encoding='utf-8')
MENUS = (ROOT / 'Vidi Studio Windows' / 'MainWindow.Menus.cs').read_text(encoding='utf-8')
MARKDOWN = (ROOT / 'Vidi Studio Windows' / 'Models' / 'MarkdownCommands.cs').read_text(encoding='utf-8')
MD_UI = (ROOT / 'Vidi Studio Windows' / 'MainWindow.SummaryMarkdown.cs').read_text(encoding='utf-8')


class ImportAssetsUI(unittest.TestCase):
    def test_menu_and_editor_entry_points_are_mirrored(self):
        self.assertIn('Command("import-assets")', MENUS)
        self.assertIn('"import-assets" => "Import Existing Assets…"', MENUS)
        self.assertIn('await ImportExistingAssetsAsync(Models.ImportScope.All)', MENUS)
        self.assertIn('Click="ImportAssets_Click"', UI)
        self.assertIn('ToolTipService.ToolTip="Import a transcript, summary, AD transcript, or AD audio into this project"', UI)
        self.assertIn('ToolTipService.ToolTip="Preview Markdown (Ctrl-Alt-Shift-P)"', UI)
        self.assertIn('AutomationProperties.Name="Undo summary edit"', UI)
        self.assertIn('Import narration audio…', (ROOT / 'Vidi Studio Windows' / 'MainWindow.Commands.cs').read_text(encoding='utf-8'))

    def test_all_text_formats_are_validated_before_import_and_after(self):
        inspector = (ROOT / 'Vidi Studio Windows' / 'Models' / 'ImportTimingInspector.cs').read_text(encoding='utf-8')
        imports = (ROOT / 'Vidi Studio Windows' / 'MainWindow.Imports.cs').read_text(encoding='utf-8')
        self.assertIn('ReadRows', inspector)
        self.assertIn('Warnings', inspector)
        self.assertIn('Import Anyway', imports)
        self.assertIn('Review Timing…', imports)
        self.assertIn('Edit Transcript Timings…', imports)
        self.assertIn('ImportTimingInspector.ReadRows', imports)

    def test_import_shortcuts_are_defined(self):
        self.assertIn('["import-transcript"] = (VirtualKey.I, VirtualKeyModifiers.Control | VirtualKeyModifiers.Menu, "Ctrl+Alt+I")', MENUS)
        self.assertIn('["import-summary"] = (VirtualKey.M, VirtualKeyModifiers.Control | VirtualKeyModifiers.Menu, "Ctrl+Alt+M")', MENUS)
        self.assertIn('["import-descriptions"] = (VirtualKey.D, VirtualKeyModifiers.Control | VirtualKeyModifiers.Menu, "Ctrl+Alt+D")', MENUS)
        self.assertIn('["import-assets"] = (VirtualKey.I, VirtualKeyModifiers.Control | VirtualKeyModifiers.Menu | VirtualKeyModifiers.Shift, "Ctrl+Alt+Shift+I")', MENUS)

    def test_section_add_menu_offers_manual_or_import(self):
        self.assertIn('Submenu("Import"', MENUS)
        self.assertIn('"import-transcript" => "Import Transcript…"', MENUS)
        self.assertIn('EditorAddButton', UI)
        self.assertIn('AddOrImport_Click', (ROOT / 'Vidi Studio Windows' / 'MainWindow.ImportMenus.cs').read_text(encoding='utf-8'))
        self.assertIn('Import Descriptions…', (ROOT / 'Vidi Studio Windows' / 'MainWindow.ImportMenus.cs').read_text(encoding='utf-8'))

    def test_import_dialog_and_engine_commands(self):
        for text in ('AD narration only', 'Dialogue + AD combined', 'AD narration audio only', 'Full AD mix'):
            self.assertIn(text, DIALOG)
        for command in ('import-transcript', 'import-ad-transcript', 'import-summary', 'import-ad-audio', 'combine-imported-audio', 'import-segment-narration'):
            self.assertIn(command, IMPORTS)
        self.assertIn('ShowImportReportAsync', IMPORTS)
        self.assertIn('Import Report', IMPORTS)

    def test_model_provenance_and_narration_sources(self):
        for label in ('Imported', 'Imported · modified'):
            self.assertIn(label, MODEL)
        self.assertIn('ImportedAudioDescription', MODEL)
        self.assertIn('DescriptionTimingAudit', MODEL)
        self.assertIn('NarrationSourcesText', MODEL)
        self.assertIn('IsTimingFlagged', MODEL)
        self.assertIn('OriginLabel', UI)

    def test_markdown_toolbar_and_shortcuts(self):
        self.assertIn('SummaryMarkdown_Click', UI)
        self.assertIn('SummaryProvenance', UI)
        self.assertIn('MarkdownCommands.Apply', MD_UI)
        self.assertIn('ListContinuation', MARKDOWN)
        self.assertIn('ClearFormatting', MARKDOWN)
        self.assertIn('SummaryEditorBox_KeyDown', UI)

    def test_txt_timing_markdown_renderer_and_warnings(self):
        timing = (ROOT / 'Vidi Studio Windows' / 'Views' / 'TextTimingDialog.cs').read_text(encoding='utf-8')
        renderer = (ROOT / 'Vidi Studio Windows' / 'MainWindow.Markdown.cs').read_text(encoding='utf-8')
        self.assertIn('Distribute Evenly', timing)
        self.assertIn('Auto-time with Gemini', timing)
        self.assertIn('TimingPath', timing)
        self.assertIn('BuildMarkdownView', renderer)
        self.assertIn('IsTableStart', renderer)
        self.assertIn('Task list', MD_UI)
        self.assertIn('originPicker', (ROOT / 'Vidi Studio Windows' / 'MainWindow.xaml.cs').read_text(encoding='utf-8'))
        self.assertIn('Warnings do not count as narration errors', (ROOT / 'Vidi Studio Windows' / 'MainWindow.Presentation.cs').read_text(encoding='utf-8'))

    def test_ai_quota_warning_and_new_project_without_key(self):
        viewmodel = (ROOT / 'Vidi Studio Windows' / 'ViewModels' / 'StudioViewModel.cs').read_text(encoding='utf-8')
        model = (ROOT / 'Vidi Studio Windows' / 'Models' / 'StudioProject.cs').read_text(encoding='utf-8')
        main = (ROOT / 'Vidi Studio Windows' / 'MainWindow.xaml.cs').read_text(encoding='utf-8')
        self.assertIn('CanUseAI => HasApiKey && !IsApiKeyInvalid && !AiProcessingBlocked', viewmodel)
        self.assertIn('AIServiceIssueDetector', model)
        self.assertIn('AiWarningBar', UI)
        self.assertIn('AiRetry_Click', main)
        self.assertIn('var ai = Model.CanUseAI;', MENUS)
        self.assertIn('if (id == "new-project") return true;', MENUS)

    def test_ad_overview_sources_and_voice_matching(self):
        presentation = (ROOT / 'Vidi Studio Windows' / 'MainWindow.Presentation.cs').read_text(encoding='utf-8')
        viewmodel = (ROOT / 'Vidi Studio Windows' / 'ViewModels' / 'StudioViewModel.cs').read_text(encoding='utf-8')
        self.assertIn('Narration sources', presentation)
        self.assertIn('Imported narration audio is in this project', presentation)
        self.assertIn('importedEntries', viewmodel)
        self.assertIn('Imported audio', viewmodel)


if __name__ == '__main__':
    unittest.main()
