using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Vidi_Studio_Windows.Models;

namespace Vidi_Studio_Windows;

public sealed partial class MainWindow
{
    private void InitializeSummaryOriginOptions()
    {
        SummaryProvenance.Items.Clear();
        foreach (var origin in new[] { "ai", "imported", "manual" })
        {
            var source = new Segment { Origin = origin };
            var brush = new SolidColorBrush(SummaryColor(source.OriginColor, "#777777"));
            var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
            row.Children.Add(new FontIcon { Glyph = source.OriginGlyph, FontSize = 13, Foreground = brush });
            row.Children.Add(new TextBlock { Text = source.OriginLabel, Foreground = brush });
            var item = new ComboBoxItem { Content = row };
            Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(item, source.OriginLabel);
            SummaryProvenance.Items.Add(item);
        }
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(SummaryProvenance, "Summary source");
    }

    private void RefreshSummarySourceBadge()
    {
        var project = Model.Project;
        SummarySourceBadge.Visibility = project?.HasSummary == true ? Visibility.Visible : Visibility.Collapsed;
        var source = new Segment { Origin = project?.SummaryOrigin ?? "unknown" };
        var brush = new SolidColorBrush(SummaryColor(source.OriginColor, "#777777"));
        SummarySourceIcon.Glyph = source.OriginGlyph;
        SummarySourceIcon.Foreground = brush;
        SummarySourceText.Text = source.OriginLabel;
        SummarySourceText.Foreground = brush;
        var label = "Summary source: " + source.OriginLabel;
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(SummarySourceBadge, label);
        ToolTipService.SetToolTip(SummarySourceBadge, label);
    }
}
