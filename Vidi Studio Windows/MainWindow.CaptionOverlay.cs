using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using System;

namespace Vidi_Studio_Windows;

public sealed partial class MainWindow
{
    private void CaptionOverlayItem_Click(object sender, RoutedEventArgs e)
    {
        if (sender is MenuFlyoutItem { Tag: string mode }) SetCaptionMode(mode);
    }

    private void UpdateCaptionOverlayButton()
    {
        if (CaptionOverlayButton == null || CaptionOverlayIcon == null) return;
        var mode = Model.Settings.Get("captionDisplayMode", "off");
        var label = mode switch
        {
            "transcript" => "Transcript",
            "combined" => "AD Transcript",
            "descriptions" => "Descriptions",
            _ => "Off"
        };
        CaptionOverlayButton.Opacity = mode == "off" ? .55 : 1;
        var tooltip = $"Transcript overlay: {label}. Open the menu to choose Off, Transcript, or AD Transcript.";
        ToolTipService.SetToolTip(CaptionOverlayButton, tooltip);
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(CaptionOverlayButton, $"Transcript overlay: {label}");
        SetItem(CaptionOffItem, mode == "off");
        SetItem(CaptionTranscriptItem, mode == "transcript");
        SetItem(CaptionADTranscriptItem, mode == "combined");
    }

    private static void SetItem(MenuFlyoutItem item, bool selected)
    {
        if (item == null) return;
        item.Icon = new FontIcon { Glyph = selected ? "\uE73E" : "\uE7F4" };
    }
}
