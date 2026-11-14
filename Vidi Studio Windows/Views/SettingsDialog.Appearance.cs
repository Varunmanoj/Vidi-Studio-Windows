using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Documents;
using Microsoft.UI.Xaml.Media;
using System;
using System.Linq;
using Vidi_Studio_Windows.Services;
using Windows.UI;

namespace Vidi_Studio_Windows.Views;

public sealed partial class SettingsDialog
{
    private readonly System.Collections.Generic.Dictionary<string, ToggleSwitch> summaryHighlightToggles = new();
    private readonly System.Collections.Generic.Dictionary<string, Button> appearanceColorButtons = new();
    private ToggleSwitch? searchHighlightToggle;
    private Button? restoreHighlightDefaultsButton;
#if DEBUG
    internal async System.Threading.Tasks.Task VerifySearchHighlightAppearanceForSmokeTestAsync(Action<bool, string> verifyResults)
    {
        var before = draft.DeepClone().AsObject();
        try
        {
            SelectSection(0); await System.Threading.Tasks.Task.Delay(150);
            var toggle = searchHighlightToggle ?? throw new InvalidOperationException("Search highlighting is missing from Appearance.");
            var color = appearanceColorButtons["searchHighlightColor"];
            if (Microsoft.UI.Xaml.Automation.AutomationProperties.GetName(toggle) != "Highlight search matches" ||
                Microsoft.UI.Xaml.Automation.AutomationProperties.GetName(color) != "Search highlight color")
                throw new InvalidOperationException("Search highlighting controls lack accessible names.");
            toggle.IsOn = false;
            await System.Threading.Tasks.Task.Delay(650);
            verifyResults(false, model.Settings.Get("searchHighlightColor", MainWindow.DefaultSearchHighlightColor));
            toggle.StartBringIntoView(); await System.Threading.Tasks.Task.Delay(100);
            var menu = (MenuFlyout)toggle.ContextFlyout;
            menu.ShowAt(toggle); await System.Threading.Tasks.Task.Delay(100);
            var choice = (ToggleMenuFlyoutItem)menu.Items[0];
            if (choice.IsChecked) throw new InvalidOperationException("Search highlight menu check is stale.");
            menu.Hide();
            var peer = new Microsoft.UI.Xaml.Automation.Peers.ToggleMenuFlyoutItemAutomationPeer(choice);
            ((Microsoft.UI.Xaml.Automation.Provider.IToggleProvider)peer.GetPattern(Microsoft.UI.Xaml.Automation.Peers.PatternInterface.Toggle)).Toggle();
            await System.Threading.Tasks.Task.Delay(650);
            verifyResults(true, model.Settings.Get("searchHighlightColor", MainWindow.DefaultSearchHighlightColor));
            color.StartBringIntoView(); await System.Threading.Tasks.Task.Delay(100);
            var colorMenu = (MenuFlyout)color.ContextFlyout;
            var colorChoice = (MenuFlyoutItem)colorMenu.Items[0];
            var colorPeer = new Microsoft.UI.Xaml.Automation.Peers.MenuFlyoutItemAutomationPeer(colorChoice);
            ((Microsoft.UI.Xaml.Automation.Provider.IInvokeProvider)colorPeer.GetPattern(Microsoft.UI.Xaml.Automation.Peers.PatternInterface.Invoke)).Invoke();
            await System.Threading.Tasks.Task.Delay(100);
            var picker = (ColorPicker)((Flyout)color.Flyout).Content;
            if (picker.XamlRoot == null) throw new InvalidOperationException("Search color menu did not open its picker.");
            picker.Color = Color.FromArgb(255, 16, 32, 64);
            await System.Threading.Tasks.Task.Delay(650);
            verifyResults(true, "#102040");
            toggle.IsOn = false; await System.Threading.Tasks.Task.Delay(650);
            verifyResults(false, "#102040");
            toggle.IsOn = true; await System.Threading.Tasks.Task.Delay(650);
            verifyResults(true, "#102040");
            color.Flyout.Hide();
            void DirtyHighlights()
            {
                foreach (var control in summaryHighlightToggles.Values) control.IsOn = false;
                searchHighlightToggle!.IsOn = false;
                foreach (var key in new[] { "summarySentenceHighlightColor", "summaryWordHighlightColor", "searchHighlightColor" })
                    ((ColorPicker)((Flyout)appearanceColorButtons[key].Flyout).Content).Color = Color.FromArgb(255, 16, 32, 64);
            }
            void CheckDefaults()
            {
                foreach (var key in new[] { "summarySentenceHighlight", "summaryWordHighlight", "searchHighlightEnabled" })
                    if (!model.Settings.GetBool(key)) throw new InvalidOperationException("Restore omitted " + key);
                foreach (var (key, expected) in new[] { ("summarySentenceHighlightColor", "#A9D6FF"), ("summaryWordHighlightColor", "#FFD54A"), ("searchHighlightColor", MainWindow.DefaultSearchHighlightColor) })
                    if (model.Settings.Get(key) != expected) throw new InvalidOperationException("Restore omitted " + key);
                verifyResults(true, MainWindow.DefaultSearchHighlightColor);
            }
            DirtyHighlights(); await System.Threading.Tasks.Task.Delay(650);
            var restore = restoreHighlightDefaultsButton!;
            restore.StartBringIntoView(); await System.Threading.Tasks.Task.Delay(100);
            if (restore.TransformToVisual(currentSectionPanel).TransformPoint(new Windows.Foundation.Point()).Y <=
                appearanceColorButtons["searchHighlightColor"].TransformToVisual(currentSectionPanel).TransformPoint(new Windows.Foundation.Point()).Y)
                throw new InvalidOperationException("Restore must be below Search highlight color.");
            var restorePeer = new Microsoft.UI.Xaml.Automation.Peers.ButtonAutomationPeer(restore);
            ((Microsoft.UI.Xaml.Automation.Provider.IInvokeProvider)restorePeer.GetPattern(Microsoft.UI.Xaml.Automation.Peers.PatternInterface.Invoke)).Invoke();
            await System.Threading.Tasks.Task.Delay(650); CheckDefaults();
            DirtyHighlights(); await System.Threading.Tasks.Task.Delay(650);
            var restoreMenu = (MenuFlyout)restoreHighlightDefaultsButton!.ContextFlyout;
            var restoreItem = (MenuFlyoutItem)restoreMenu.Items[0];
            var restoreMenuPeer = new Microsoft.UI.Xaml.Automation.Peers.MenuFlyoutItemAutomationPeer(restoreItem);
            ((Microsoft.UI.Xaml.Automation.Provider.IInvokeProvider)restoreMenuPeer.GetPattern(Microsoft.UI.Xaml.Automation.Peers.PatternInterface.Invoke)).Invoke();
            await System.Threading.Tasks.Task.Delay(650); CheckDefaults();
        }
        finally
        {
            draft.Clear(); foreach (var item in before) draft[item.Key] = item.Value?.DeepClone();
            if (!ApplyPreferences()) throw new InvalidOperationException("Could not restore search highlight settings.");
        }
    }

    internal async System.Threading.Tasks.Task VerifySummaryHighlightAppearanceForSmokeTestAsync()
    {
        SelectSection(0); await System.Threading.Tasks.Task.Delay(150);
        foreach (var key in new[] { "summarySentenceHighlight", "summaryWordHighlight" })
        {
            if (!summaryHighlightToggles.TryGetValue(key, out var toggle)) throw new InvalidOperationException("Appearance is missing " + key);
            if (string.IsNullOrWhiteSpace(Microsoft.UI.Xaml.Automation.AutomationProperties.GetName(toggle)))
                throw new InvalidOperationException("Highlight switch has no accessible name.");
            var colorKey = key + "Color";
            var color = appearanceColorButtons[colorKey];
            var track = SettingsSwitchTrack(toggle) ?? throw new InvalidOperationException("Native switch track missing.");
            var switchCenter = track.TransformToVisual(currentSectionPanel).TransformPoint(new Windows.Foundation.Point(track.ActualWidth / 2, 0)).X;
            var colorCenter = color.TransformToVisual(currentSectionPanel).TransformPoint(new Windows.Foundation.Point(color.ActualWidth / 2, 0)).X;
            if (Math.Abs(switchCenter - colorCenter) > 1 || toggle.ActualWidth > 54)
                throw new InvalidOperationException("Highlight switch and color swatch are not aligned.");
            var original = toggle.IsOn;
            toggle.IsOn = !original;
            if (SettingsStore.GetBool(draft[key], true) == original) throw new InvalidOperationException("Highlight toggle did not update its setting.");
            toggle.StartBringIntoView(); await System.Threading.Tasks.Task.Delay(100);
            var menu = (MenuFlyout)toggle.ContextFlyout;
            menu.ShowAt(toggle); await System.Threading.Tasks.Task.Delay(100);
            var item = (ToggleMenuFlyoutItem)menu.Items[0];
            if (item.IsChecked != toggle.IsOn) throw new InvalidOperationException("Highlight menu check differs.");
            menu.Hide();
            var peer = new Microsoft.UI.Xaml.Automation.Peers.ToggleMenuFlyoutItemAutomationPeer(item);
            ((Microsoft.UI.Xaml.Automation.Provider.IToggleProvider)peer.GetPattern(Microsoft.UI.Xaml.Automation.Peers.PatternInterface.Toggle)).Toggle();
            if (toggle.IsOn != original || SettingsStore.GetBool(draft[key], true) != original) throw new InvalidOperationException("Highlight menu did not restore the same shared value.");
        }
        SelectSection(2); await System.Threading.Tasks.Task.Delay(150);
        if (currentSectionPanel is not null && ContainsSummaryControls(currentSectionPanel)) throw new InvalidOperationException("Narration still contains Summary highlighting.");
        if (narrationPlaybackChoice is null || exportSoundControl is null) throw new InvalidOperationException("Narration controls missing.");
        var playbackChoice = narrationPlaybackChoice;
        var selected = playbackChoice.SelectedItem;
        var engineSelection = mode.SelectedItem;
        var controls = new FrameworkElement[] { mode, playbackChoice, exportSoundControl, localSpeechControls };
        Windows.Foundation.Rect[] Bounds() => controls.Select(control =>
        {
            var point = control.TransformToVisual(currentSectionPanel).TransformPoint(new Windows.Foundation.Point());
            return new Windows.Foundation.Rect(point.X, point.Y, control.ActualWidth, control.ActualHeight);
        }).ToArray();
        layout.UpdateLayout();
        var before = Bounds();
        try
        {
            foreach (var option in (System.Collections.Generic.IEnumerable<PlaybackOption>)playbackChoice.ItemsSource)
            {
                playbackChoice.SelectedItem = option;
                await System.Threading.Tasks.Task.Delay(100); layout.UpdateLayout();
                var after = Bounds();
                if (!ReferenceEquals(engineSelection, mode.SelectedItem) || before.Where((rect, index) =>
                    Math.Abs(rect.X - after[index].X) > 1 || Math.Abs(rect.Y - after[index].Y) > 1 ||
                    Math.Abs(rect.Width - after[index].Width) > 1 || Math.Abs(rect.Height - after[index].Height) > 1).Any())
                    throw new InvalidOperationException("Narration playback selection shifts the Settings layout.");
            }
        }
        finally { playbackChoice.SelectedItem = selected; }
        static bool ContainsSummaryControls(DependencyObject node)
        {
            if (node is FrameworkElement element && Microsoft.UI.Xaml.Automation.AutomationProperties.GetAutomationId(element).StartsWith("summary-", StringComparison.Ordinal)) return true;
            for (var i=0;i<VisualTreeHelper.GetChildrenCount(node);i++) if (ContainsSummaryControls(VisualTreeHelper.GetChild(node,i))) return true;
            return false;
        }
    }
#endif
    private void BuildAppearance(StackPanel panel)
    {
        summaryHighlightToggles.Clear();
        appearanceColorButtons.Clear();
        panel.Children.Add(ScreenReaderHeading.Mark(new TextBlock { Text = "Appearance and playback", FontSize = 20, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 4) }));
        var card = new StackPanel { Spacing = 16 };
        panel.Children.Add(new AccessibilityGroup("Appearance and playback settings") { CornerRadius = new CornerRadius(8), Padding = new Thickness(16), Background = new SolidColorBrush(Color.FromArgb(15, 128, 128, 128)), Child = card });
        Grid Row(string title, FrameworkElement control) => AddSettingsRow(card, title, control);
        void Choice(string label, string key, string[] ids, string[] labels)
        {
            var choice = new ComboBox { ItemsSource = labels, SelectedIndex = Math.Max(0, Array.IndexOf(ids, SettingsStore.GetString(draft[key], ids[0]))), Width = 180, MinWidth = 0 };
            choice.SelectionChanged += (_, _) =>
            {
                if (choice.SelectedIndex < 0) return;
                draft[key] = ids[choice.SelectedIndex];
                if (key == "theme") ApplyWindowTheme(ids[choice.SelectedIndex]);
            };
            Row(label, choice);
        }
        Choice("Appearance", "theme", new[] { "system", "light", "dark" }, new[] { "System", "Light", "Dark" });
        Choice("Transcript overlay", "captionDisplayMode", new[] { "off", "transcript", "combined", "descriptions" }, new[] { "Off", "Transcript", "AD Transcript", "Descriptions" });
        card.Children.Add(new TextBlock { Text = "Uses transcript and description segments saved in the project.", TextWrapping = TextWrapping.Wrap, FontSize = 12, Opacity = .65 });
        var dialogue = new TextBlock { Text = "Dialogue caption preview", FontWeight = Microsoft.UI.Text.FontWeights.SemiBold, TextAlignment = TextAlignment.Center, TextWrapping = TextWrapping.Wrap };
        var description = new TextBlock { Text = "Audio description: A person enters the room.", FontWeight = Microsoft.UI.Text.FontWeights.SemiBold, TextAlignment = TextAlignment.Center, TextWrapping = TextWrapping.Wrap };
        var dialogueBox = new Border { Child = dialogue, CornerRadius = new CornerRadius(6), Padding = new Thickness(10, 6, 10, 6), HorizontalAlignment = HorizontalAlignment.Center };
        var descriptionBox = new Border { Child = description, CornerRadius = new CornerRadius(6), Padding = new Thickness(10, 6, 10, 6), HorizontalAlignment = HorizontalAlignment.Center };
        var summaryPreview = new TextBlock { Text = "A person enters the room and opens the door.", TextWrapping = TextWrapping.Wrap, FontSize = 16 };
        Color ReadColor(string key, string fallback)
        {
            var value = SettingsStore.GetString(draft[key], fallback).TrimStart('#');
            if (value.Length != 6 || !uint.TryParse(value, System.Globalization.NumberStyles.HexNumber, null, out var rgb)) rgb = Convert.ToUInt32(fallback.TrimStart('#'), 16);
            return Color.FromArgb(255, (byte)(rgb >> 16), (byte)(rgb >> 8), (byte)rgb);
        }
        void Preview()
        {
            dialogue.FontSize = description.FontSize = SettingsStore.GetDouble(draft["captionFontSize"], 24);
            dialogue.Foreground = new SolidColorBrush(ReadColor("captionTextColor", "#FFFFFF"));
            description.Foreground = new SolidColorBrush(ReadColor("adCaptionTextColor", "#FFD60A"));
            var alpha = (byte)(255 * SettingsStore.GetDouble(draft["captionBackgroundOpacity"], .75));
            var normal = ReadColor("captionBackgroundColor", "#000000"); normal.A = alpha;
            var ad = ReadColor("adCaptionBackgroundColor", "#3A2F00"); ad.A = alpha;
            dialogueBox.Background = new SolidColorBrush(normal); descriptionBox.Background = new SolidColorBrush(ad);
            summaryPreview.TextHighlighters.Clear();
            void Highlight(int start, int length, string key, string fallback)
            {
                var color = ReadColor(key, fallback);
                var contrast = color.R * .299 + color.G * .587 + color.B * .114 < 135 ? Microsoft.UI.Colors.White : Microsoft.UI.Colors.Black;
                var textHighlight = new TextHighlighter { Background = new SolidColorBrush(color), Foreground = new SolidColorBrush(contrast) };
                textHighlight.Ranges.Add(new TextRange { StartIndex = start, Length = length });
                summaryPreview.TextHighlighters.Add(textHighlight);
            }
            if (SettingsStore.GetString(draft["summarySentenceHighlight"], "true") != "false") Highlight(0, summaryPreview.Text.Length, "summarySentenceHighlightColor", "#A9D6FF");
            if (SettingsStore.GetString(draft["summaryWordHighlight"], "true") != "false") Highlight(2, 6, "summaryWordHighlightColor", "#FFD54A");
        }
        void Slider(string label, string key, double fallback, double low, double high, double step, bool percent)
        {
            var group = new Grid { Width = 280, ColumnSpacing = 12 };
            group.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            group.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var slider = new Slider { MinWidth = 0, HorizontalAlignment = HorizontalAlignment.Stretch, Minimum = low, Maximum = high, StepFrequency = step, Value = SettingsStore.GetDouble(draft[key], fallback) };
            var value = new TextBlock { Width = 45, TextAlignment = TextAlignment.Right, VerticalAlignment = VerticalAlignment.Center, Text = percent ? $"{slider.Value:P0}" : $"{slider.Value:F0} pt" };
            slider.ValueChanged += (_, _) => { draft[key] = slider.Value; value.Text = percent ? $"{slider.Value:P0}" : $"{slider.Value:F0} pt"; Preview(); };
            Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(slider, label);
            Grid.SetColumn(value, 1);
            group.Children.Add(slider); group.Children.Add(value); Row(label, group);
        }
        void ColorRow(string label, string key, string fallback)
        {
            var swatch = new Border { Width = 40, Height = 16, CornerRadius = new CornerRadius(8), Background = new SolidColorBrush(ReadColor(key, fallback)) };
            var button = new Button { Content = swatch, Padding = new Thickness(5), Width = 54 };
            appearanceColorButtons[key] = button;
            var picker = new ColorPicker { Color = ReadColor(key, fallback), IsAlphaEnabled = false, IsMoreButtonVisible = false, IsColorSliderVisible = true };
            picker.ColorChanged += (_, args) => { draft[key] = $"#{args.NewColor.R:X2}{args.NewColor.G:X2}{args.NewColor.B:X2}"; swatch.Background = new SolidColorBrush(args.NewColor); Preview(); };
            button.Flyout = new Flyout { Content = picker }; Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(button, label); Row(label, button);
        }
        card.Children.Add(ScreenReaderHeading.Mark(new TextBlock { Text = "Caption appearance", FontWeight = Microsoft.UI.Text.FontWeights.SemiBold, Margin = new Thickness(0, 8, 0, 0) }));
        Slider("Text size", "captionFontSize", 24, 12, 48, 1, false);
        ColorRow("Dialogue text color", "captionTextColor", "#FFFFFF");
        ColorRow("Dialogue background color", "captionBackgroundColor", "#000000");
        ColorRow("Audio description text color", "adCaptionTextColor", "#FFD60A");
        ColorRow("Audio description background color", "adCaptionBackgroundColor", "#3A2F00");
        Slider("Background opacity", "captionBackgroundOpacity", .75, 0, 1, .05, true);
        Choice("Position", "captionPosition", new[] { "bottom", "top" }, new[] { "Bottom", "Top" });
        var sample = new StackPanel { Spacing = 12, VerticalAlignment = VerticalAlignment.Center }; sample.Children.Add(dialogueBox); sample.Children.Add(descriptionBox);
        card.Children.Add(new Border { Background = new SolidColorBrush(Color.FromArgb(255, 34, 71, 125)), CornerRadius = new CornerRadius(8), Padding = new Thickness(16), Child = sample });
        card.Children.Add(ScreenReaderHeading.Mark(new TextBlock { Text = "Summary readback highlighting", FontWeight = Microsoft.UI.Text.FontWeights.SemiBold, Margin = new Thickness(0, 8, 0, 0) }));
        void HighlightChoice(string label, string key)
        {
            var toggle = new ToggleSwitch { IsOn = SettingsStore.GetString(draft[key], "true") != "false", OnContent = "", OffContent = "", MinWidth = 0 };
            toggle.Toggled += (_, _) => { draft[key] = toggle.IsOn ? "true" : "false"; Preview(); };
            var id = key == "summarySentenceHighlight" ? "summary-sentence-highlight" : "summary-word-highlight";
            summaryHighlightToggles[key] = toggle;
            Microsoft.UI.Xaml.Automation.AutomationProperties.SetAutomationId(toggle, id);
            Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(toggle, label);
            var menu = Vidi_Studio_Windows.Views.MenuAccessKeys.Create();
            var choice = new ToggleMenuFlyoutItem { Text = label, Tag = id + "-menu" };
            menu.Opening += (_, _) => choice.IsChecked = toggle.IsOn;
            choice.Click += (_, _) => toggle.IsOn = choice.IsChecked;
            menu.Items.Add(choice); toggle.ContextFlyout = menu;
            Row(label, CompactSettingsControl(toggle));
        }
        HighlightChoice("Highlight the spoken sentence", "summarySentenceHighlight");
        ColorRow("Sentence highlight color", "summarySentenceHighlightColor", "#A9D6FF");
        HighlightChoice("Highlight the spoken word", "summaryWordHighlight");
        ColorRow("Word highlight color", "summaryWordHighlightColor", "#FFD54A");
        card.Children.Add(new Border { Background = new SolidColorBrush(Color.FromArgb(24, 128, 128, 128)), CornerRadius = new CornerRadius(8), Padding = new Thickness(12), Child = summaryPreview });
        card.Children.Add(ScreenReaderHeading.Mark(new TextBlock { Text = "Search highlighting", FontWeight = Microsoft.UI.Text.FontWeights.SemiBold, Margin = new Thickness(0, 8, 0, 0) }));
        var searchHighlight = new ToggleSwitch { IsOn = SettingsStore.GetBool(draft["searchHighlightEnabled"], true), OnContent = "", OffContent = "", MinWidth = 0 };
        searchHighlightToggle = searchHighlight;
        searchHighlight.Toggled += (_, _) => draft["searchHighlightEnabled"] = searchHighlight.IsOn;
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetAutomationId(searchHighlight, "search-highlight");
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(searchHighlight, "Highlight search matches");
        var searchHighlightMenu = MenuAccessKeys.Create();
        var searchHighlightChoice = new ToggleMenuFlyoutItem { Text = "Highlight search matches", Tag = "search-highlight-menu" };
        searchHighlightMenu.Opening += (_, _) => searchHighlightChoice.IsChecked = searchHighlight.IsOn;
        searchHighlightChoice.Click += (_, _) => searchHighlight.IsOn = searchHighlightChoice.IsChecked;
        searchHighlightMenu.Items.Add(searchHighlightChoice); searchHighlight.ContextFlyout = searchHighlightMenu;
        Row("Highlight search matches", CompactSettingsControl(searchHighlight));
        ColorRow("Search highlight color", "searchHighlightColor", MainWindow.DefaultSearchHighlightColor);
        var searchColor = appearanceColorButtons["searchHighlightColor"];
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetAutomationId(searchColor, "search-highlight-color");
        var searchColorMenu = MenuAccessKeys.Create();
        var searchColorChoice = new MenuFlyoutItem { Text = "Search Highlight Color…", Tag = "search-highlight-color-menu" };
        searchColorChoice.Click += (_, _) => searchColor.Flyout.ShowAt(searchColor);
        searchColorMenu.Items.Add(searchColorChoice); searchColor.ContextFlyout = searchColorMenu;
        var restoreHighlights = new Button { Content = "Restore Highlight Defaults" };
        restoreHighlightDefaultsButton = restoreHighlights;
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetAutomationId(restoreHighlights, "restore-highlight-defaults");
        void RestoreHighlights()
        {
            draft["summarySentenceHighlightColor"] = "#A9D6FF"; draft["summaryWordHighlightColor"] = "#FFD54A";
            draft["summarySentenceHighlight"] = true; draft["summaryWordHighlight"] = true;
            draft["searchHighlightEnabled"] = true; draft["searchHighlightColor"] = MainWindow.DefaultSearchHighlightColor;
            panel.Children.Clear(); BuildAppearance(panel);
        }
        restoreHighlights.Click += (_, _) => RestoreHighlights();
        var restoreHighlightMenu = Vidi_Studio_Windows.Views.MenuAccessKeys.Create();
        var restoreHighlightItem = new MenuFlyoutItem { Text = "Restore Highlight Defaults", Tag = "restore-highlight-defaults-menu" };
        restoreHighlightItem.Click += (_, _) => RestoreHighlights();
        restoreHighlightMenu.Items.Add(restoreHighlightItem); restoreHighlights.ContextFlyout = restoreHighlightMenu;
        card.Children.Add(restoreHighlights);
        Preview();
        var advanced = new StackPanel { Spacing = 12 };
        advanced.Children.Add(ScreenReaderHeading.Mark(new TextBlock { Text = "Advanced", FontWeight = Microsoft.UI.Text.FontWeights.SemiBold }));
        var developerMode = new ToggleSwitch
        {
            IsOn = SettingsStore.GetBool(draft["developerMode"]),
            OnContent = "",
            OffContent = "",
            MinWidth = 0
        };
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(developerMode, "Developer mode");
        AddSettingsRow(advanced, "Developer mode", CompactSettingsControl(developerMode));
        developerMode.Toggled += (_, _) =>
        {
            draft["developerMode"] = developerMode.IsOn;
            UpdateDeveloperPromptsVisibility();
        };
        panel.Children.Add(new AccessibilityGroup("Advanced appearance settings") { CornerRadius = new CornerRadius(8), Padding = new Thickness(16), Background = new SolidColorBrush(Color.FromArgb(15, 128, 128, 128)), Child = advanced });
        var restore = new Button { Content = "Restore caption defaults" };
        restore.Click += (_, _) =>
        {
            draft["captionFontSize"] = 24; draft["captionTextColor"] = "#FFFFFF"; draft["captionBackgroundColor"] = "#000000";
            draft["adCaptionTextColor"] = "#FFD60A"; draft["adCaptionBackgroundColor"] = "#3A2F00"; draft["captionBackgroundOpacity"] = .75; draft["captionPosition"] = "bottom";
            panel.Children.Clear(); BuildAppearance(panel);
        };
        panel.Children.Add(restore);
    }
}
