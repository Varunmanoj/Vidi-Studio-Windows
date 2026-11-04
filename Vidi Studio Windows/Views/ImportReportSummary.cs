using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Vidi_Studio_Windows.Models;

namespace Vidi_Studio_Windows.Views;

public sealed class ImportReportSummary : UserControl
{
    public ImportReportSummary(ImportReport report, string details)
    {
        var panel = new StackPanel { Spacing = 14, HorizontalAlignment = HorizontalAlignment.Stretch };
        var heading = new Grid { ColumnSpacing = 12 };
        heading.ColumnDefinitions.Add(new ColumnDefinition());
        heading.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        heading.Children.Add(new TextBlock { Text = report.KindTitle, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold, TextWrapping = TextWrapping.Wrap });
        var imported = new TextBlock { Text = report.RowsImported + " imported", FontWeight = Microsoft.UI.Text.FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Top };
        Grid.SetColumn(imported, 1); heading.Children.Add(imported); panel.Children.Add(heading);
        panel.Children.Add(new TextBlock { Text = report.SourceName.StartsWith("vidistudio-youtube-") || report.SourceName.StartsWith("vidistudio-caption-") ? "Source: YouTube captions (SRT)" : "Source: " + report.SourceName, TextWrapping = TextWrapping.Wrap, IsTextSelectionEnabled = true, Opacity = .75 });
        panel.Children.Add(new TextBlock { Text = report.DurationText, TextWrapping = TextWrapping.Wrap, Opacity = .75 });
        var counts = new Grid { ColumnSpacing = 16 };
        void Count(string label, int count)
        {
            int column = counts.ColumnDefinitions.Count;
            counts.ColumnDefinitions.Add(new ColumnDefinition());
            var cell = new StackPanel { Spacing = 4, VerticalAlignment = VerticalAlignment.Top };
            cell.Children.Add(new TextBlock { Text = count.ToString(), FontSize = 22, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold });
            cell.Children.Add(new TextBlock { Text = label, TextWrapping = TextWrapping.Wrap, FontSize = 12, Opacity = .75 });
            AutomationProperties.SetName(cell, label + ": " + count);
            Grid.SetColumn(cell, column); counts.Children.Add(cell);
        }
        Count("Flagged for retiming", report.RowsFlagged);
        Count("Ended after video", report.RowsOmittedAfterVideo);
        Count("Invalid or empty", report.RowsOmittedInvalid);
        if (report.RowsOmittedTranscriptOverlap is > 0) Count("Transcript overlaps", report.RowsOmittedTranscriptOverlap.Value);
        panel.Children.Add(counts);
        if (!string.IsNullOrWhiteSpace(details)) panel.Children.Add(new TextBlock { Text = details, TextWrapping = TextWrapping.Wrap, IsTextSelectionEnabled = true });
        Content = panel;
    }
}
