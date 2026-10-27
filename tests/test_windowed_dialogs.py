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
        self.assertIn('new DialogWindowLayout(dialog, window, root, owner)', self.host)

    def test_windowed_dialogs_are_fitted_to_their_content(self):
        self.assertNotIn('fitToContent', self.host)
        self.assertNotIn('fitToContent', self.main)
        self.assertNotIn('fitToContent', self.settings)
        self.assertIn('WindowedDialog.ShowAsync(dialog, this, width, height)', self.main)
        self.assertIn('WindowedDialog.ShowAsync(confirmDialog, this, width: 500, height: 240)', self.settings)

    def test_escape_cancels_without_drawing_a_key_tip(self):
        self.assertIn('scope.KeyboardAcceleratorPlacementMode = Microsoft.UI.Xaml.Input.KeyboardAcceleratorPlacementMode.Hidden;', self.host)
        self.assertIn('new Microsoft.UI.Xaml.Input.KeyboardAccelerator { Key = Windows.System.VirtualKey.Escape }', self.host)

    def test_dialog_is_hosted_in_place_without_a_separate_smoke_popup(self):
        self.assertIn('root.Children.Add(dialog);', self.host)
        self.assertIn('var showing = dialog.ShowAsync(ContentDialogPlacement.InPlace);', self.host)
        self.assertNotIn('return await dialog.ShowAsync();', self.host)
        self.assertIn('root.Children.Remove(dialog);', self.host)
        self.assertIn('AutomationProperties.SetIsDialog(dialog, true);', self.host)
        self.assertNotIn('ClearSmokeLayers', self.host)

    def test_native_card_chrome_is_flattened_without_recoloring_content(self):
        self.assertIn('dialog.Background = transparent;', self.host)
        self.assertIn('dialog.BorderThickness = new Thickness(0);', self.host)
        self.assertIn('dialog.CornerRadius = new CornerRadius(0);', self.host)
        self.assertIn('card.Background = transparent;', self.host)
        self.assertIn('if (card.Shadow is not null) card.Shadow = null;', self.host)
        self.assertIn('card.Translation = System.Numerics.Vector3.Zero;', self.host)
        self.assertIn('card.CornerRadius = new CornerRadius(0);', self.host)
        self.assertIn('dialogSpace.CornerRadius = new CornerRadius(0);', self.host)
        self.assertIn('contentGrid.Background = transparent;', self.host)
        self.assertIn('contentGrid.BorderThickness = new Thickness(0);', self.host)
        self.assertIn('dialog.Loaded += OnDialogLoaded;', self.host)

    def test_elevation_is_removed_after_winui_prepares_the_dialog(self):
        preparation = self.host.index('var showing = dialog.ShowAsync(ContentDialogPlacement.InPlace);')
        flatten = self.host.index('FlattenDialogSurface(dialog);', preparation)
        awaiting = self.host.index('return await showing;', preparation)
        self.assertLess(preparation, flatten)
        self.assertLess(flatten, awaiting)
        self.assertIn('element.RegisterPropertyChangedCallback(UIElement.ShadowProperty,', self.host)
        self.assertIn('static (sender, _) => ClearDialogElevation((UIElement)sender)', self.host)
        self.assertIn('FlatCards.GetValue(card, static element =>', self.host)

    def test_one_hidden_window_is_measured_before_its_only_activation(self):
        host = self.host.split('internal static class WindowedDialog', 1)[1]
        self.assertEqual(host.count('new Window {'), 1)
        self.assertEqual(host.count('window.Activate();'), 1)
        self.assertLess(host.index('await loaded.Task'), host.index('await layout.PrepareAsync();'))
        self.assertLess(host.index('await layout.PrepareAsync();'), host.index('window.Activate();'))
        self.assertIn('window.AppWindow.ResizeClient(', host)
        self.assertIn('root.Measure(size);', host)
        self.assertIn('root.Arrange(', host)
        self.assertNotIn('Task.Delay', host)
        self.assertNotIn('FitWindowToCard', host)
        self.assertIn('group.Transitions.Clear();', host)
        self.assertIn('content.SizeChanged -= ContentSizeChanged;', host)
        self.assertIn('owner.Closed -= OnOwnerClosed;', host)
        self.assertIn('dialog.Loaded -= OnDialogLoaded;', host)
        self.assertIn('dialog.Opened -= OnDialogOpened;', host)
        self.assertIn('dialog.Closed -= OnDialogClosed;', host)
        self.assertIn('dialog.KeyboardAccelerators.Remove(dialogEscape);', host)

    def test_dialog_window_uses_the_same_fluent_material_and_theme_as_settings(self):
        dialog_host = self.host.split('internal static class WindowedDialog', 1)[1]
        self.assertIn('WindowAppearance.ApplyMaterial(window);', dialog_host)
        self.assertIn('WindowAppearance.ApplyTheme(window, root, requested);', dialog_host)
        self.assertIn('WindowAppearance.Refresh(window, root);', dialog_host)
        self.assertIn('App.ThemeChanged -= ApplyTheme;', dialog_host)
        self.assertNotIn('window.SystemBackdrop = null;', dialog_host)
        self.assertNotIn('ApplyFlatSurface', dialog_host)
        self.assertNotIn('DialogSurfaceColor', self.host)

    def test_native_smoke_test_checks_the_window_not_just_the_dialog_subtree(self):
        smoke = (PROJECT / 'MainWindow.SmokeTest.cs').read_text(encoding='utf-8')
        self.assertIn('GetOpenPopupsForXamlRoot(dialog.XamlRoot).Count != 0', smoke)
        self.assertIn('ReferenceEquals(dialog.Parent, root)', smoke)
        self.assertIn('Views.WindowAppearance.SurfaceColor(this, expected)', smoke)
        self.assertIn('foreach (var theme in new[] { "dark", "light", "system" })', smoke)
        self.assertIn('VIDISTUDIO_UI_SMOKE_DIALOGS_ONLY', smoke)
        self.assertIn('Views.WindowedDialog.Preparing += OnPreparing;', smoke)
        self.assertIn('Views.WindowedDialog.ReadyToShow += OnReadyToShow;', smoke)
        self.assertIn('dialogPresentationVerifier?.Verify(dialog);', smoke)
        self.assertIn('The dialog changed size after becoming visible.', smoke)
        self.assertIn('foreach (var initialTheme in new[] { "light", "dark" })', smoke)
        self.assertIn('VerifyDialogContentGrowthForSmokeTestAsync(report)', smoke)
        self.assertIn('VerifyDialogCanceledBeforePresentationForSmokeTestAsync()', smoke)
        self.assertIn('VerifySingleDialogSurfaceForSmokeTest(dialog);', smoke)
        self.assertIn('card.Shadow is not null || Math.Abs(card.Translation.Z) > .001', smoke)
        for name in ('new-project-window', 'export-window', 'delete-generated-window',
                     'error-dialog-window', 'confirmation-window'):
            self.assertIn(f'"{name}"', smoke)
        self.assertIn('new Views.PiperVoicesDialog()', smoke)
        self.assertIn('new Views.PiperDeleteVoicesDialog()', smoke)

    def test_dialog_minimum_size_is_scaled_to_the_display(self):
        self.assertIn('var minPixelWidth = (int)Math.Ceiling(480 * scale);', self.host)
        self.assertIn('var minPixelHeight = (int)Math.Ceiling(240 * scale);', self.host)

    def test_card_lookup_prefers_the_named_template_card(self):
        self.assertIn('if (border.Name == "BackgroundElement") return border;', self.host)
        self.assertIn('var chromeHeight = Math.Max(0, card.ActualHeight - content.ActualHeight);', self.host)


if __name__ == '__main__':
    unittest.main()
