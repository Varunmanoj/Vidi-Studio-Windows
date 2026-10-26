from pathlib import Path
import unittest

ROOT = Path(__file__).resolve().parents[1]
PROJECT = ROOT / 'Vidi Studio Windows'


class WindowedDialogs(unittest.TestCase):
    def setUp(self):
        self.host = (PROJECT / 'Views' / 'WindowAppearance.cs').read_text(encoding='utf-8')
        self.main = (PROJECT / 'MainWindow.xaml.cs').read_text(encoding='utf-8')
        self.settings = (PROJECT / 'Views' / 'SettingsDialog.Gemini.cs').read_text(encoding='utf-8')

    def test_dialog_windows_use_the_app_surface_without_card_chrome(self):
        self.assertIn('window.ExtendsContentIntoTitleBar = true;', self.host)
        self.assertIn('dialog.Resources["ContentDialogPadding"] = new Thickness(24, ContentTopInset, 24, 24);', self.host)
        for resource in (
            'ContentDialogSmokeFill',
            'ContentDialogTopOverlay',
            'ContentDialogBackground',
            'ContentDialogBorderBrush',
            'ContentDialogBorderWidth',
            'ContentDialogSeparatorThickness',
        ):
            with self.subTest(resource=resource):
                self.assertIn(f'dialog.Resources["{resource}"] =', self.host)
        self.assertIn('presenter.IsMinimizable = false;', self.host)
        self.assertIn('presenter.IsMaximizable = false;', self.host)
        self.assertIn('presenter.IsResizable = false;', self.host)
        self.assertIn('FitWindowToCard(dialog, window, root, owner);', self.host)

    def test_windowed_dialogs_are_fitted_to_their_content(self):
        self.assertNotIn('fitToContent', self.host)
        self.assertNotIn('fitToContent', self.main)
        self.assertNotIn('fitToContent', self.settings)
        self.assertIn('WindowedDialog.ShowAsync(dialog, this, width, height)', self.main)
        self.assertIn('WindowedDialog.ShowAsync(confirmDialog, this, width: 500, height: 240)', self.settings)

    def test_escape_cancels_without_drawing_a_key_tip(self):
        self.assertIn('scope.KeyboardAcceleratorPlacementMode = Microsoft.UI.Xaml.Input.KeyboardAcceleratorPlacementMode.Hidden;', self.host)
        self.assertIn('new Microsoft.UI.Xaml.Input.KeyboardAccelerator { Key = Windows.System.VirtualKey.Escape }', self.host)

    def test_light_surface_removes_the_smoke_and_separator(self):
        self.assertIn('FindTemplateElement<Microsoft.UI.Xaml.Shapes.Rectangle>(dialog, "SmokeLayerBackground")', self.host)
        self.assertIn('smoke.Fill = transparent;', self.host)
        self.assertIn('smokeHost.Children.Remove(smoke);', self.host)
        self.assertIn('ClearSmokeLayers(dialog, dialog, transparent);', self.host)
        self.assertIn('card.Background = transparent;', self.host)
        self.assertIn('contentGrid.Background = transparent;', self.host)
        self.assertIn('contentGrid.BorderThickness = new Thickness(0);', self.host)
        self.assertIn('dialog.Loaded += (_, _) => FlattenDialogSurface(dialog);', self.host)

    def test_dialog_window_uses_an_opaque_flat_surface(self):
        self.assertIn('root.Background = new SolidColorBrush(surface);', self.host)
        self.assertIn('internal static Windows.UI.Color DialogSurfaceColor(ElementTheme theme) =>', self.host)
        self.assertIn('theme == ElementTheme.Dark ? Microsoft.UI.Colors.Black : Microsoft.UI.Colors.White;', self.host)
        self.assertIn('window.SystemBackdrop = null;', self.host)

    def test_dialog_minimum_size_is_scaled_to_the_display(self):
        self.assertIn('var minPixelWidth = (int)Math.Ceiling(480 * scale);', self.host)
        self.assertIn('var minPixelHeight = (int)Math.Ceiling(240 * scale);', self.host)

    def test_card_lookup_prefers_the_named_template_card(self):
        self.assertIn('if (border.Name == "BackgroundElement") return border;', self.host)
        self.assertIn('var chromeHeight = Math.Max(0, card.ActualHeight - content.ActualHeight);', self.host)


if __name__ == '__main__':
    unittest.main()
