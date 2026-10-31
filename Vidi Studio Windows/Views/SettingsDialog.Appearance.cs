using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Documents;
using Microsoft.UI.Xaml.Media;
using System;
using Vidi_Studio_Windows.Services;
using Windows.UI;

namespace Vidi_Studio_Windows.Views;

public sealed partial class SettingsDialog
{
    private void BuildAppearance(StackPanel panel)
    {
        panel.Children.Add(new TextBlock { Text = "Appearance and playback", FontSize = 20, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 4) });
        var card = new StackPanel { Spacing = 16 };
        panel.Children.Add(new Border { CornerRadius = new CornerRadius(8), Padding = new Thickness(16), Background = new SolidColorBrush(Color.FromArgb(15, 128, 128, 128)), Child = card });
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
                var contrast = color.R * .299 + color.G * .587 + color.B * .114 < 135 ? Colors.White : Colors.Black;
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
            var button = new Button { Content = swatch, Padding = new Thickness(5), MinWidth = 54 };
            var picker = new ColorPicker { Color = ReadColor(key, fallback), IsAlphaEnabled = false, IsMoreButtonVisible = false, IsColorSliderVisible = true };
            picker.ColorChanged += (_, args) => { draft[key] = $"#{args.NewColor.R:X2}{args.NewColor.G:X2}{args.NewColor.B:X2}"; swatch.Background = new SolidColorBrush(args.NewColor); Preview(); };
            button.Flyout = new Flyout { Content = picker }; Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(button, label); Row(label, button);
        }
        card.Children.Add(new TextBlock { Text = "Caption appearance", FontWeight = Microsoft.UI.Text.FontWeights.SemiBold, Margin = new Thickness(0, 8, 0, 0) });
        Slider("Text size", "captionFontSize", 24, 12, 48, 1, false);
        ColorRow("Dialogue text color", "captionTextColor", "#FFFFFF");
        ColorRow("Dialogue background color", "captionBackgroundColor", "#000000");
        ColorRow("Audio description text color", "adCaptionTextColor", "#FFD60A");
        ColorRow("Audio description background color", "adCaptionBackgroundColor", "#3A2F00");
        Slider("Background opacity", "captionBackgroundOpacity", .75, 0, 1, .05, true);
        Choice("Position", "captionPosition", new[] { "bottom", "top" }, new[] { "Bottom", "Top" });
        var sample = new StackPanel { Spacing = 12, VerticalAlignment = VerticalAlignment.Center }; sample.Children.Add(dialogueBox); sample.Children.Add(descriptionBox);
        card.Children.Add(new Border { Background = new SolidColorBrush(Color.FromArgb(255, 34, 71, 125)), CornerRadius = new CornerRadius(8), Padding = new Thickness(16), Child = sample });
        card.Children.Add(new TextBlock { Text = "Summary readback highlighting", FontWeight = Microsoft.UI.Text.FontWeights.SemiBold, Margin = new Thickness(0, 8, 0, 0) });
        void HighlightChoice(string label, string key)
        {
            var toggle = new ToggleSwitch { IsOn = SettingsStore.GetString(draft[key], "true") != "false", OnContent = "On", OffContent = "Off" };
            toggle.Toggled += (_, _) => { draft[key] = toggle.IsOn ? "true" : "false"; Preview(); };
            Row(label, toggle);
        }
        HighlightChoice("Highlight the spoken sentence", "summarySentenceHighlight");
        ColorRow("Sentence highlight color", "summarySentenceHighlightColor", "#A9D6FF");
        HighlightChoice("Highlight the spoken word", "summaryWordHighlight");
        ColorRow("Word highlight color", "summaryWordHighlightColor", "#FFD54A");
        card.Children.Add(new Border { Background = new SolidColorBrush(Color.FromArgb(24, 128, 128, 128)), CornerRadius = new CornerRadius(8), Padding = new Thickness(12), Child = summaryPreview });
        Preview();
        var advanced = new StackPanel { Spacing = 12 };
        advanced.Children.Add(new TextBlock { Text = "Advanced", FontWeight = Microsoft.UI.Text.FontWeights.SemiBold });
        var developerMode = new ToggleSwitch
        {
            IsOn = SettingsStore.GetBool(draft["developerMode"]),
            OnContent = "On",
            OffContent = "Off",
            MinWidth = 0
        };
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(developerMode, "Developer mode");
        AddSettingsRow(advanced, "Developer mode", developerMode);
        advanced.Children.Add(new TextBlock { Text = "Shows project configuration controls, Developer prompts, and advanced executable-path overrides in Narration and Storage.", TextWrapping = TextWrapping.Wrap, FontSize = 12, Opacity = .65 });
        developerMode.Toggled += (_, _) =>
        {
            draft["developerMode"] = developerMode.IsOn;
            UpdateDeveloperPromptsVisibility();
        };
        panel.Children.Add(new Border { CornerRadius = new CornerRadius(8), Padding = new Thickness(16), Background = new SolidColorBrush(Color.FromArgb(15, 128, 128, 128)), Child = advanced });
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
