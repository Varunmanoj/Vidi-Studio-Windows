using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using System;
using System.Globalization;

namespace Vidi_Studio_Windows.Views;

/// <summary>Start and end times share the full row width, matching the Mac segment editor.</summary>
internal sealed class SegmentTimingEditor : StackPanel
{
    internal TimecodePicker StartPicker { get; }
    internal TimecodePicker EndPicker { get; }
    internal TextBlock DurationLabel { get; } = new()
    {
        HorizontalAlignment = HorizontalAlignment.Right,
        FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
        FontSize = 16,
        Opacity = 0.8,
        TextWrapping = TextWrapping.Wrap
    };

    internal SegmentTimingEditor(double start, double end, double maximum, Func<double> currentPosition)
    {
        Spacing = 22;
        HorizontalAlignment = HorizontalAlignment.Stretch;
        StartPicker = new TimecodePicker { Header = "Start time", Maximum = maximum, Value = start };
        EndPicker = new TimecodePicker { Header = "End time", Maximum = maximum, Value = end };
        StartPicker.SetCurrentPositionProvider(currentPosition, "Set start time to current playhead position");
        EndPicker.SetCurrentPositionProvider(currentPosition, "Set end time to current playhead position");
        Children.Add(StartPicker);
        Children.Add(DurationLabel);
        Children.Add(EndPicker);
        StartPicker.ValueChanged += (_, _) => UpdateDuration();
        EndPicker.ValueChanged += (_, _) => UpdateDuration();
        UpdateDuration();
    }

    private void UpdateDuration()
    {
        var seconds = Math.Max(0, EndPicker.Value - StartPicker.Value);
        var duration = seconds.ToString("F3", CultureInfo.CurrentCulture) + " seconds";
        DurationLabel.Text = "Duration " + duration;
        AutomationProperties.SetName(DurationLabel, "Segment duration: " + duration);
    }
}
