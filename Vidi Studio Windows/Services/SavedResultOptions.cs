using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Vidi_Studio_Windows.Models;

namespace Vidi_Studio_Windows.Services;

public static class SavedResultOptions
{
    private static Dictionary<string,string> Choices(LanguageResult result, IReadOnlyDictionary<string,string>? choices)
    {
        var selected = choices?.ToDictionary(p => p.Key, p => p.Value) ?? new Dictionary<string,string>();
        selected[result.Kind] = result.Id;
        return selected;
    }
    public static List<string> Status(LanguageResult result, StudioProject project, bool selected, IReadOnlyDictionary<string,string>? choices = null)
    {
        var view = project.WithLanguageResults(result.Language, Choices(result, choices), includingDrafts: true);
        var allAudio = result.Kind == "descriptions" && view.HasCompleteExportNarration;
        var validTiming = !view.Cues.Any(c => view.Transcript.Any(t => c.Start < t.End && c.End > t.Start) ||
            view.Cues.Any(other => !ReferenceEquals(c, other) && c.Start < other.End && c.End > other.Start));
        var ready = result.Kind == "descriptions" ? allAudio && validTiming : result.HasContent;
        if (result.Kind == "adAudio") {
            try { ready = view.ImportedAudioDescription is { } audio && new FileInfo(view.Resolve(audio.Path)) is { Exists: true, Length: > 0 }; }
            catch (Exception) { ready = false; }
        }
        ready = result.State == "saved" && ready;
        var labels = new List<string>();
        if (selected) labels.Add("Selected");
        if (allAudio) labels.Add("All AD segments have audio");
        if (ready) labels.Add("Export ready");
        return labels;
    }
    public static string DisplayTitle(LanguageResult result) => result.Title + (result.State == "draft" ? " · Review draft" : "");
    public static string AccessibleName(LanguageResult result, StudioProject project, bool selected, IReadOnlyDictionary<string,string>? choices = null) =>
        string.Join(", ", new[] { DisplayTitle(result) }.Concat(Status(result, project, selected, choices)));
    public static StackPanel Content(LanguageResult result, StudioProject project, bool selected, IReadOnlyDictionary<string,string>? choices = null, string titlePrefix = "")
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
        foreach (var label in Status(result, project, selected, choices)) {
            var icon = new FontIcon { FontFamily = new FontFamily("Segoe Fluent Icons"), FontSize = 16,
                Glyph = label == "Selected" ? "\uE73E" : label == "Export ready" ? "\uE898" : "\uE767" };
            AutomationProperties.SetName(icon, label); ToolTipService.SetToolTip(icon, label); row.Children.Add(icon);
        }
        row.Children.Add(new TextBlock { Text = titlePrefix + DisplayTitle(result) });
        AutomationProperties.SetName(row, AccessibleName(result, project, selected, choices));
        return row;
    }
    public static IconElement? MenuIcon(LanguageResult result, StudioProject project, bool selected)
    {
        var labels = Status(result, project, selected);
        if (labels.Count == 0) return null;
        var icon = new FontIcon { FontFamily = new FontFamily("Segoe Fluent Icons"), FontSize = 14,
            Glyph = string.Join(" ", labels.Select(label => label == "Selected" ? "\uE73E" : label == "Export ready" ? "\uE898" : "\uE767")),
            Width = labels.Count * 20 };
        AutomationProperties.SetName(icon, string.Join(", ", labels));
        return icon;
    }
    public static void Populate(ComboBox picker, IEnumerable<LanguageResult> rows, StudioProject project, string selectedID, IReadOnlyDictionary<string,string>? choices = null)
    {
        picker.ItemsSource = null; picker.DisplayMemberPath = ""; picker.Items.Clear();
        foreach (var result in rows) {
            var item = new ComboBoxItem { Tag = result, Content = Content(result, project, result.Id == selectedID, choices) };
            AutomationProperties.SetName(item, AccessibleName(result, project, result.Id == selectedID, choices));
            picker.Items.Add(item);
            if (result.Id == selectedID) picker.SelectedItem = item;
        }
        picker.SelectionChanged += (_, _) => {
            foreach (ComboBoxItem item in picker.Items) {
                if (item.Tag is not LanguageResult result) continue;
                var selected = ReferenceEquals(item, picker.SelectedItem);
                item.Content = Content(result, project, selected, choices);
                AutomationProperties.SetName(item, AccessibleName(result, project, selected, choices));
            }
        };
    }
    public static LanguageResult? Selected(ComboBox picker) => (picker.SelectedItem as ComboBoxItem)?.Tag as LanguageResult;
}
