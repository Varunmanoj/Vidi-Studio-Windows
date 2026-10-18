using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Documents;
using Microsoft.UI.Xaml.Media;
using System;
using Windows.UI;

namespace Vidi_Studio_Windows.Views;

public sealed partial class SettingsDialog
{
    private void BuildAppearance(StackPanel panel)
    {
        panel.Children.Add(new TextBlock { Text = "Appearance and playback", FontSize = 20, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold, Margin = new Thickness(0, 2, 0, 10) });
        var card = new StackPanel { Spacing = 16 };
        panel.Children.Add(new Border { CornerRadius = new CornerRadius(8), Padding = new Thickness(16), Background = new SolidColorBrush(Color.FromArgb(15, 128, 128, 128)), Child = card });
        Grid Row(string title, FrameworkElement control)
        {
            var row = new Grid { ColumnSpacing = 14, MinHeight = 32 };
            row.ColumnDefinitions.Add(new ColumnDefinition()); row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            row.Children.Add(new TextBlock { Text = title, VerticalAlignment = VerticalAlignment.Center, TextWrapping = TextWrapping.Wrap });
            Grid.SetColumn(control, 1); row.Children.Add(control); card.Children.Add(row); return row;
        }
        void Choice(string label, string key, string[] ids, string[] labels)
        {
            var choice = new ComboBox { ItemsSource = labels, SelectedIndex = Math.Max(0, Array.IndexOf(ids, draft[key]?.GetValue<string>() ?? ids[0])), MinWidth = 165 };
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
            var value = (draft[key]?.GetValue<string>() ?? fallback).TrimStart('#');
            if (value.Length != 6 || !uint.TryParse(value, System.Globalization.NumberStyles.HexNumber, null, out var rgb)) rgb = Convert.ToUInt32(fallback.TrimStart('#'), 16);
            return Color.FromArgb(255, (byte)(rgb >> 16), (byte)(rgb >> 8), (byte)rgb);
        }
        void Preview()
        {
            dialogue.FontSize = description.FontSize = draft["captionFontSize"]?.GetValue<double>() ?? 24;
            dialogue.Foreground = new SolidColorBrush(ReadColor("captionTextColor", "#FFFFFF"));
            description.Foreground = new SolidColorBrush(ReadColor("adCaptionTextColor", "#FFD60A"));
            var alpha = (byte)(255 * (draft["captionBackgroundOpacity"]?.GetValue<double>() ?? .75));
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
            if (draft["summarySentenceHighlight"]?.GetValue<string>() != "false") Highlight(0, summaryPreview.Text.Length, "summarySentenceHighlightColor", "#A9D6FF");
            if (draft["summaryWordHighlight"]?.GetValue<string>() != "false") Highlight(2, 6, "summaryWordHighlightColor", "#FFD54A");
        }
        void Slider(string label, string key, double fallback, double low, double high, double step, bool percent)
        {
            var group = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 12 };
            var slider = new Slider { Width = 210, Minimum = low, Maximum = high, StepFrequency = step, Value = draft[key]?.GetValue<double>() ?? fallback };
            var value = new TextBlock { Width = 45, TextAlignment = TextAlignment.Right, VerticalAlignment = VerticalAlignment.Center, Text = percent ? $"{slider.Value:P0}" : $"{slider.Value:F0} pt" };
            slider.ValueChanged += (_, _) => { draft[key] = slider.Value; value.Text = percent ? $"{slider.Value:P0}" : $"{slider.Value:F0} pt"; Preview(); };
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
            var toggle = new ToggleSwitch { IsOn = draft[key]?.GetValue<string>() != "false", OnContent = "On", OffContent = "Off" };
            toggle.Toggled += (_, _) => { draft[key] = toggle.IsOn ? "true" : "false"; Preview(); };
            Row(label, toggle);
        }
        HighlightChoice("Highlight the spoken sentence", "summarySentenceHighlight");
        ColorRow("Sentence highlight color", "summarySentenceHighlightColor", "#A9D6FF");
        HighlightChoice("Highlight the spoken word", "summaryWordHighlight");
        ColorRow("Word highlight color", "summaryWordHighlightColor", "#FFD54A");
        card.Children.Add(new Border { Background = new SolidColorBrush(Color.FromArgb(24, 128, 128, 128)), CornerRadius = new CornerRadius(8), Padding = new Thickness(12), Child = summaryPreview });
        Preview();
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
