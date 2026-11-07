using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Documents;
using System;

namespace Vidi_Studio_Windows;

public sealed partial class MainWindow
{
    private bool updatingSummaryZoom;

    private void UpdateSummaryZoomControls()
    {
        if (SummaryPanel == null || SummaryZoomSlider == null || SummaryZoomFooter == null) return;
        var available = desktopEditorVisible && Section.SelectedIndex == 1 && !summaryEditMode && Model.Project?.HasSummary == true && Model.Idle;
        SummaryZoomFooter.Visibility = available && PreviewPanel.Visibility != Visibility.Visible ? Visibility.Visible : Visibility.Collapsed;
        SummaryZoomInButton.IsEnabled = available && SummaryPanel.ZoomFactor < 4;
        SummaryZoomOutButton.IsEnabled = available && SummaryPanel.ZoomFactor > .5;
        SummaryZoomResetButton.IsEnabled = SummaryZoomSlider.IsEnabled = available;
        updatingSummaryZoom = true;
        SummaryZoomSlider.Value = SummaryPanel.ZoomFactor * 100;
        SummaryZoomPercent.Text = $"{SummaryPanel.ZoomFactor * 100:0}%";
        AutomationProperties.SetName(SummaryZoomPercent, $"Summary zoom: {SummaryPanel.ZoomFactor * 100:0} percent");
        updatingSummaryZoom = false;
    }

    private void ReflowSummaryZoom()
    {
        if (SummaryPanel == null || SummaryZoomContent == null || SummaryPanel.ViewportWidth <= 0) return;
        // Keep the scaled content exactly as wide as the viewport. Native
        // touchpad/touch zoom retains its focal point while text wraps afresh.
        var width = Math.Max(1, SummaryPanel.ViewportWidth / SummaryPanel.ZoomFactor);
        if (double.IsNaN(SummaryZoomContent.Width) || Math.Abs(SummaryZoomContent.Width - width) > .01)
            SummaryZoomContent.Width = width;
    }

    private static TextPointer? SummaryTextPosition(InlineCollection inlines, ref int offset)
    {
        foreach (var inline in inlines)
        {
            if (inline is Run run)
            {
                if (offset < run.Text.Length) return run.ContentStart.GetPositionAtOffset(offset, LogicalDirection.Forward);
                offset -= run.Text.Length;
            }
            else if (inline is Span span)
            {
                var found = SummaryTextPosition(span.Inlines, ref offset);
                if (found != null) return found;
            }
            else if (inline is LineBreak) offset--;
        }
        return null;
    }

    private void FollowSummaryCharacter(int offset)
    {
        if (activeSummaryBlock is not { } block || SummaryView.Visibility != Visibility.Visible || !Follow.IsOn) return;
        var pointer = SummaryTextPosition(block.Inlines, ref offset);
        if (pointer == null) return;
        var rect = pointer.GetCharacterRect(LogicalDirection.Forward);
        var transform = block.TransformToVisual(SummaryPanel);
        var top = transform.TransformPoint(new Windows.Foundation.Point(rect.X, rect.Y)).Y;
        var bottom = transform.TransformPoint(new Windows.Foundation.Point(rect.X, rect.Y + rect.Height)).Y;
        if (top < 24 || bottom > SummaryPanel.ViewportHeight - 24)
            SummaryPanel.ChangeView(null, Math.Max(0, SummaryPanel.VerticalOffset + top - 24), null, true);
    }

    private void SetSummaryZoom(double zoom)
    {
        if (!double.IsFinite(zoom)) return;
        SummaryPanel.ChangeView(0, null, (float)Math.Clamp(zoom, .5, 4), true);
    }

    private void SummaryZoom_ViewChanged(object? sender, ScrollViewerViewChangedEventArgs e)
    {
        ReflowSummaryZoom();
        if (desktopMenuItems.Count > 0 && SummaryZoomFooter != null) UpdateDesktopMenuStates();
    }

    private void SummaryZoom_SizeChanged(object sender, SizeChangedEventArgs e) => ReflowSummaryZoom();

    private void SummaryZoomSlider_Changed(object sender, RangeBaseValueChangedEventArgs e)
    {
        if (!updatingSummaryZoom && SummaryPanel != null && CanRunDesktopCommand("summary-zoom-reset")) SetSummaryZoom(e.NewValue / 100);
    }

    private async void SummaryZoom_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: string command }) await RunDesktopCommandAsync(command);
    }
}
