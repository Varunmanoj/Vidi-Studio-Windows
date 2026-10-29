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
    private readonly Models.SegmentFilter segmentFilter = new();

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
        if (Model.Busy || Model.Project is not { } project || Section.SelectedIndex is not (0 or 2 or 4)) return;
        var rows = UnfilteredSectionRows().ToList();
        var origins = rows.Select(segment => segment.Origin).Distinct().ToList();
        var boxes = new List<CheckBox>();
        var panel = new StackPanel { Spacing = 10, MinWidth = 360 };
        panel.Children.Add(new TextBlock
        {
            Text = "Choose which rows are visible. Leave every origin unchecked to show all origins. Filters do not change project content.",
            TextWrapping = TextWrapping.Wrap, Opacity = .75
        });
        panel.Children.Add(new TextBlock { Text = "Origin", FontWeight = Microsoft.UI.Text.FontWeights.SemiBold });
        if (origins.Count == 0)
            panel.Children.Add(new TextBlock { Text = "No origin labels are present in this section.", Opacity = .65 });
        foreach (var origin in origins.OrderBy(OriginOrder))
        {
            var box = new CheckBox { Content = OriginLabel(origin), Tag = origin, IsChecked = segmentFilter.Origins.Contains(origin) };
            boxes.Add(box); panel.Children.Add(box);
        }
        ComboBox? audio = null;
        if (Section.SelectedIndex is 2 or 4)
        {
            panel.Children.Add(new TextBlock { Text = "Audio", FontWeight = Microsoft.UI.Text.FontWeights.SemiBold, Margin = new Thickness(0, 8, 0, 0) });
            audio = new ComboBox { MinWidth = 220 };
            audio.Items.Add("All descriptions"); audio.Items.Add("Has audio"); audio.Items.Add("Missing audio");
            audio.SelectedIndex = segmentFilter.Audio switch { "withAudio" => 1, "withoutAudio" => 2, _ => 0 };
            panel.Children.Add(audio);
            panel.Children.Add(new TextBlock
            {
                Text = "Audio filters apply to description rows only; transcript rows remain visible when their origin matches.",
                TextWrapping = TextWrapping.Wrap, Opacity = .65, FontSize = 12
            });
        }
        var dialog = new ContentDialog
        {
            Title = Section.SelectedIndex switch
            {
                0 => "Filter Transcript",
                2 => "Filter Descriptions",
                _ => "Filter AD Transcript"
            },
            Content = new ScrollViewer { Content = panel, MaxHeight = 480 },
            PrimaryButtonText = "Apply Filter",
            SecondaryButtonText = "Clear Filter",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Primary
        };
        var result = await ShowDialogAsync(dialog, 540, 520);
        if (result == ContentDialogResult.None) return;
        if (result == ContentDialogResult.Secondary) segmentFilter.Clear();
        else
        {
            segmentFilter.Origins.Clear();
            foreach (var box in boxes.Where(box => box.IsChecked == true))
                segmentFilter.Origins.Add((string)box.Tag);
            segmentFilter.Audio = audio?.SelectedIndex switch { 1 => "withAudio", 2 => "withoutAudio", _ => "all" };
        }
        Segments.SelectedItems.Clear();
        RenderSection();
        UpdateBulkActionState();
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
