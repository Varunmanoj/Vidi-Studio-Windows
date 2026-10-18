from pathlib import Path
import unittest
import xml.etree.ElementTree as ET

ROOT = Path(__file__).resolve().parents[1]


class EditModeControls(unittest.TestCase):
    def test_row_more_button_uses_existing_edit_mode_binding(self):
        tree = ET.parse(ROOT/'Vidi Studio Windows/MainWindow.xaml')
        buttons = [e for e in tree.iter() if e.get('Click') == 'SegmentMore_Click']
        self.assertEqual(len(buttons), 2)
        row = next(e for e in buttons if e.get('Tag') == '{Binding}')
        self.assertEqual(row.get('Visibility'), '{Binding IsEditing, Converter={StaticResource VisibleWhenTrue}}')
        header = next(e for e in buttons if e is not row)
        self.assertEqual(header.get('{http://schemas.microsoft.com/winfx/2006/xaml}Name'), 'EditorMoreButton')
        self.assertEqual(header.get('Visibility'), 'Collapsed')

    def test_mode_and_section_changes_refresh_header_visibility(self):
        source = (ROOT/'Vidi Studio Windows/MainWindow.Presentation.cs').read_text()
        self.assertIn('var timedSection = Section.SelectedIndex is 0 or 2 or 4;', source)
        self.assertIn('EditorMoreButton.Visibility = !timedSection || editMode ? Visibility.Visible : Visibility.Collapsed;', source)
        refresh = source.split('private void RefreshEditingRows()', 1)[1].split('private void UpdateSegmentMoreVisibility()', 1)[0]
        self.assertIn('UpdateSegmentMoreVisibility();', refresh)
        self.assertIn('row.IsEditing = editMode;', refresh)
        presentation = source.split('private void UpdateSectionPresentation()', 1)[1]
        self.assertIn('UpdateSegmentMoreVisibility();', presentation)

    def test_hidden_more_command_is_guarded_without_disabling_context_menu(self):
        source = (ROOT/'Vidi Studio Windows/MainWindow.Commands.cs').read_text()
        more = source.split('private void SegmentMore_Click', 1)[1].split('private void SegmentContext_Requested', 1)[0]
        self.assertIn('if (Section.SelectedIndex is 0 or 2 or 4 && !editMode) return;', more)
        context = source.split('private void SegmentContext_Requested', 1)[1].split('private void Track_SizeChanged', 1)[0]
        self.assertNotIn('!editMode', context)


if __name__ == '__main__':
    unittest.main()
