using Microsoft.UI.Xaml.Controls;
using System;
using System.Threading.Tasks;
using System.Collections.Generic;
using System.Linq;

namespace Vidi_Studio_Windows;

public sealed partial class MainWindow
{
    private readonly Dictionary<string, string> segmentSorts = new();
    private string segmentSort
    {
        get => segmentSorts.TryGetValue((Model.Project?.Id ?? "") + SegmentFilterKey, out var value) ? value : "start";
        set => segmentSorts[(Model.Project?.Id ?? "") + SegmentFilterKey] = value;
    }
    private async void ClearSegmentView_Click(object sender, Microsoft.UI.Xaml.RoutedEventArgs e)
    {
        if (sender is Microsoft.UI.Xaml.FrameworkElement { Tag: string id }) await RunDesktopCommandAsync(id);
    }

    private void ClearSegmentFilter()
    {
        if (!CanRunDesktopCommand("clear-segment-filter")) return;
        segmentFilter.Clear(); ClearSegmentSelection(); RenderSection(); UpdateBulkActionState(); UpdateDesktopMenuStates();
    }

    private IEnumerable<Models.Segment> ApplySegmentSort(IEnumerable<Models.Segment> rows) => segmentSort switch
    {
        "durationAscending" => rows.OrderBy(segment => segment.End - segment.Start).ThenBy(segment => segment.Start),
        "durationDescending" => rows.OrderByDescending(segment => segment.End - segment.Start).ThenBy(segment => segment.Start),
        "origin" => rows.OrderBy(segment => OriginOrder(segment.Origin)).ThenBy(segment => segment.Start),
        _ => rows.OrderBy(segment => segment.Start).ThenBy(segment => segment.Id)
    };

    private async Task ShowSegmentSortAsync()
    {
        while (!Model.Busy && Model.Project is { } project && Section.SelectedIndex is 0 or 2 or 4)
        {
            ContentDialog? dialog = null;
            string? requestedResult = null;
            var panel = new StackPanel { Spacing = 16 };
            panel.Children.Add(BuildSegmentSavedResultPickers(project, id => { requestedResult = id; dialog?.Hide(); }));
            var values = new[] { "start", "durationAscending", "durationDescending", "origin" };
            var sort = new ComboBox { Header = "Sort by", ItemsSource = new[] { "Start Time", "Duration — Shortest First", "Duration — Longest First", "Origin" },
                SelectedIndex = Math.Max(0, Array.IndexOf(values, segmentSort)), HorizontalAlignment = Microsoft.UI.Xaml.HorizontalAlignment.Stretch };
            panel.Children.Add(sort);
            dialog = new ContentDialog { Title = Section.SelectedIndex switch { 0 => "Sort Transcripts", 2 => "Sort Descriptions", _ => "Sort AD Transcript" },
                Content = panel, PrimaryButtonText = "Apply Sort", CloseButtonText = "Cancel", DefaultButton = ContentDialogButton.Primary };
            var result = await ShowDialogAsync(dialog, 640, 500);
            if (requestedResult != null) { await RunLanguageActionAsync("select-result", new[] { "--result-id", requestedResult }); continue; }
            if (result == ContentDialogResult.Primary) SetSegmentSort(values[Math.Max(0, sort.SelectedIndex)]);
            return;
        }
    }

    private void SetSegmentSort(string sort)
    {
        segmentSort = sort;
        ClearSegmentSelection();
        RenderSection();
        UpdateBulkActionState();
        UpdateDesktopMenuStates();
    }

    private void SortStart_Click(object sender, Microsoft.UI.Xaml.RoutedEventArgs e) => SetSegmentSort("start");
    private void SortDurationAsc_Click(object sender, Microsoft.UI.Xaml.RoutedEventArgs e) => SetSegmentSort("durationAscending");
    private void SortDurationDesc_Click(object sender, Microsoft.UI.Xaml.RoutedEventArgs e) => SetSegmentSort("durationDescending");
    private void SortOrigin_Click(object sender, Microsoft.UI.Xaml.RoutedEventArgs e) => SetSegmentSort("origin");
}
