using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace Vidi_Studio_Windows;

public sealed partial class MainWindow
{
    private readonly Dictionary<string, Models.SegmentFilter> segmentFilters = new();
    private string? segmentFilterProject;
    private Models.SegmentFilter segmentFilter
    {
        get
        {
            if (segmentFilterProject != Model.Project?.Id) { segmentFilters.Clear(); segmentFilterProject = Model.Project?.Id; }
            if (!segmentFilters.TryGetValue(SegmentFilterKey, out var filter))
                segmentFilters[SegmentFilterKey] = filter = new Models.SegmentFilter();
            return filter;
        }
    }

    private string SegmentFilterKey => string.Join(":", new[] { Section.SelectedIndex.ToString(), Model.Project?.SelectedLanguage ?? "auto" }
        .Concat((Section.SelectedIndex == 0 ? new[] { "transcript" } : Section.SelectedIndex == 2 ? new[] { "descriptions" } : new[] { "transcript", "descriptions" })
            .Select(kind => Model.Project?.ActiveResultID(kind) ?? "")));

    private StackPanel BuildSegmentSavedResultPickers(Models.StudioProject project, Action<string> choose)
    {
        var panel = new StackPanel { Spacing = 8 };
        foreach (var kind in (Section.SelectedIndex == 0 ? new[] { "transcript" } : Section.SelectedIndex == 2 ? new[] { "descriptions" } : new[] { "transcript", "descriptions" }))
        {
            var rows = project.SavedResults.Where(result => result.Kind == kind && result.Language == project.SelectedLanguage)
                .OrderByDescending(result => result.CreatedAt).ToList();
            if (rows.Count == 0) continue;
            var picker = new ComboBox { Header = Models.LanguageResult.SavedResultsTitle(kind), HorizontalAlignment = HorizontalAlignment.Stretch };
            Services.SavedResultOptions.Populate(picker, rows, project, project.ActiveResultID(kind));
            Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(picker, Models.LanguageResult.SavedResultsTitle(kind));
            picker.SelectionChanged += (_, _) =>
            {
                if (Services.SavedResultOptions.Selected(picker) is Models.LanguageResult result && result.Id != project.ActiveResultID(kind) &&
                    result.Language == project.SelectedLanguage && rows.Any(row => row.Id == result.Id)) choose(result.Id);
            };
            panel.Children.Add(picker);
        }
        return panel;
    }

    private void ClearSegmentSelection()
    {
        // WinUI rejects mutations of SelectedItems while selection is disabled.
        if (Segments.SelectionMode != ListViewSelectionMode.None) Segments.SelectedItems.Clear();
    }

    private static bool HasSegmentAudio(Models.StudioProject project, Models.Segment segment) =>
        !string.IsNullOrWhiteSpace(segment.Audio) && File.Exists(project.Resolve(segment.Audio));

    private IEnumerable<Models.Segment> UnfilteredSectionRows()
    {
        if (Model.Project is not { } project) return Array.Empty<Models.Segment>();
        return Section.SelectedIndex switch
        {
            0 => project.Transcript,
            2 => project.Cues,
            4 => project.Transcript.Concat(project.Cues).OrderBy(segment => segment.Start),
            _ => Array.Empty<Models.Segment>()
        };
    }

    private async Task ShowSegmentFilterAsync()
    {
        while (!Model.Busy && Model.Project is { } project && Section.SelectedIndex is 0 or 2 or 4)
        {
            var rows = UnfilteredSectionRows().ToList();
            var origins = rows.Select(segment => segment.Origin).Distinct().ToList();
            var boxes = new List<CheckBox>();
            var panel = new StackPanel { Spacing = 10, MinWidth = 360 };
            var rangeDraft = segmentFilter.Ranges.Clone();
            var rangeError = new TextBlock { TextWrapping = TextWrapping.Wrap,
                Foreground = new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.Colors.Red), Visibility = Visibility.Collapsed };
            ContentDialog? dialog = null;
            string? requestedResult = null;
            var contextKey = SegmentFilterKey;
            panel.Children.Add(BuildSegmentSavedResultPickers(project, id => { requestedResult = id; dialog?.Hide(); }));
            void UpdateRangeState()
            {
                var error = rangeDraft.Error(rows.Count(row => !row.IsDescription), rows.Count(row => row.IsDescription), project.Duration,
                    rows.Select(ValidationKey).ToHashSet());
                rangeError.Text = error ?? "";
                rangeError.Visibility = error == null ? Visibility.Collapsed : Visibility.Visible;
                if (dialog != null) dialog.IsPrimaryButtonEnabled = error == null;
            }
            panel.Children.Add(BuildSegmentRangeFilterControls(rows, rangeDraft, project.Duration, UpdateRangeState));
            panel.Children.Add(rangeError);
            panel.Children.Add(new TextBlock { Text = "Origin", FontWeight = Microsoft.UI.Text.FontWeights.SemiBold });
            foreach (var origin in origins.OrderBy(OriginOrder))
            {
                var box = new CheckBox { Content = OriginLabel(origin), Tag = origin, IsChecked = segmentFilter.Origins.Contains(origin) };
                boxes.Add(box); panel.Children.Add(box);
            }
            ComboBox? audio = null;
            if (Section.SelectedIndex is 2 or 4)
            {
                audio = new ComboBox { Header = "Audio", MinWidth = 220, ItemsSource = new[] { "All descriptions", "Has audio", "Missing audio" },
                    SelectedIndex = segmentFilter.Audio switch { "withAudio" => 1, "withoutAudio" => 2, _ => 0 } };
                panel.Children.Add(audio);
            }
            dialog = new ContentDialog
            {
                Title = Section.SelectedIndex switch { 0 => "Filter Transcript", 2 => "Filter Descriptions", _ => "Filter AD Transcript" },
                Content = new ScrollViewer { Content = panel, MaxHeight = 560 },
                PrimaryButtonText = "Apply Filter", SecondaryButtonText = "Clear Filter", CloseButtonText = "Cancel",
                DefaultButton = ContentDialogButton.Primary
            };
            UpdateRangeState();
            var result = await ShowDialogAsync(dialog, 720, 800);
            if (requestedResult != null)
            {
                await RunLanguageActionAsync("select-result", new[] { "--result-id", requestedResult });
                continue;
            }
            if (result == ContentDialogResult.None || contextKey != SegmentFilterKey) return;
            if (result == ContentDialogResult.Secondary) segmentFilter.Clear();
            else
            {
                if (!dialog.IsPrimaryButtonEnabled) return;
                segmentFilter.Origins.Clear();
                foreach (var box in boxes.Where(box => box.IsChecked == true)) segmentFilter.Origins.Add((string)box.Tag);
                segmentFilter.Audio = audio?.SelectedIndex switch { 1 => "withAudio", 2 => "withoutAudio", _ => "all" };
                segmentFilter.Ranges = rangeDraft;
            }
            ClearSegmentSelection(); RenderSection(); UpdateBulkActionState(); return;
        }
    }

    private static int OriginOrder(string origin) => origin switch
    {
        "ai" => 0, "ai_modified" => 1, "imported" => 2, "imported_modified" => 3, "manual" => 4, _ => 5
    };

    private static string OriginLabel(string origin) => origin switch
    {
        "ai" => "AI-generated",
        "ai_modified" => "AI-generated · modified",
        "imported" => "Imported",
        "imported_modified" => "Imported · modified",
        "manual" => "Manually added",
        _ => "Origin unknown"
    };
}
