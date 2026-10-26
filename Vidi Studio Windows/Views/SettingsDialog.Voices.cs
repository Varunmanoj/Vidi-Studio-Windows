using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Vidi_Studio_Windows.Services;

namespace Vidi_Studio_Windows.Views;

/// <summary>Voice picker that shows group headings and unavailable voices without letting them be chosen.</summary>
internal sealed partial class VoiceComboBox : ComboBox
{
    protected override void PrepareContainerForItemOverride(DependencyObject element, object item)
    {
        base.PrepareContainerForItemOverride(element, item);
        if (element is not ComboBoxItem container) return;
        var voice = item as Voice;
        var heading = voice?.Group == VoiceGroups.Heading;
        container.IsEnabled = voice?.Available != false;
        container.FontWeight = heading ? FontWeights.SemiBold : FontWeights.Normal;
        container.Margin = heading ? new Thickness(0, 6, 0, 0) : new Thickness(0);
        AutomationProperties.SetHeadingLevel(container, heading ? Microsoft.UI.Xaml.Automation.Peers.AutomationHeadingLevel.Level2 : Microsoft.UI.Xaml.Automation.Peers.AutomationHeadingLevel.None);
        AutomationProperties.SetHelpText(container, voice?.Note ?? "");
        ToolTipService.SetToolTip(container, heading || string.IsNullOrWhiteSpace(voice?.Note) ? null : voice!.Note);
    }
}

public sealed partial class SettingsDialog
{
    private readonly StackPanel piperControls = new() { Spacing = 10 };
    private readonly TextBlock piperStatus = new() { TextWrapping = TextWrapping.Wrap, Opacity = .75 };
    private Button? deletePiperVoices;
    private readonly StackPanel summaryControls = new() { Spacing = 8 };

    private void BuildVoiceEngineControls()
    {
        foreach (var element in new FrameworkElement[] { piperControls, summaryControls }) if (element.Parent is Panel parent) parent.Children.Remove(element);
        piperControls.Children.Clear(); summaryControls.Children.Clear();

        var buttons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        var manage = new Button { MinWidth = 200 };
        IconButtonContent.Set(manage, "Download or remove voices…", Symbol.Download);
        AutomationProperties.SetName(manage, "Download or remove Piper voices");
        manage.Click += async (_, _) =>
        {
            var dialog = new PiperVoicesDialog();
            await WindowedDialog.ShowAsync(dialog, this, 780, 740);
            await RefreshVoices();
        };
        buttons.Children.Add(manage);
        var delete = new Button { MinWidth = 140 };
        deletePiperVoices = delete;
        IconButtonContent.Set(delete, "Delete voices…", Symbol.Delete);
        AutomationProperties.SetName(delete, "Delete downloaded Piper voices");
        AutomationProperties.SetHelpText(delete, "Lists downloaded Piper voices so you can listen to a sample and remove the ones you no longer need");
        delete.Click += async (_, _) =>
        {
            var dialog = new PiperDeleteVoicesDialog();
            await WindowedDialog.ShowAsync(dialog, this, 720, 700);
            await RefreshVoices();
        };
        buttons.Children.Add(delete);
        piperControls.Children.Add(buttons);
        piperControls.Children.Add(piperStatus);

        var heading = new TextBlock { Text = "Summary read-aloud", FontSize = 17, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 12, 0, 0) };
        AutomationProperties.SetHeadingLevel(heading, Microsoft.UI.Xaml.Automation.Peers.AutomationHeadingLevel.Level2);
        summaryControls.Children.Add(heading);
        summaryControls.Children.Add(new TextBlock { TextWrapping = TextWrapping.Wrap, Opacity = .75,
            Text = "The summary is read with the same engine and voice chosen for narration. It never creates files in the project." });
        UpdatePiperStatus();
    }

    private void UpdateVoiceEngineVisibility(string provider)
    {
        piperControls.Visibility = provider == "piper" ? Visibility.Visible : Visibility.Collapsed;
        UpdatePiperStatus();
    }

    private void UpdatePiperStatus()
    {
        var installed = PiperService.InstalledVoices();
        if (deletePiperVoices is not null) deletePiperVoices.IsEnabled = installed.Count > 0;
        piperStatus.Text = !PiperService.IsRuntimeAvailable
            ? "The bundled Piper speech engine is missing. Reinstall Vidi Studio, or run script\\fetch-piper.ps1 and build the app again."
            : installed.Count == 0
                ? "No Piper voices are downloaded yet. Choose Download or remove voices to add one."
                : $"{installed.Count} Piper voice{(installed.Count == 1 ? "" : "s")} downloaded and ready to use.";
    }

    private static Voice Heading(string id, string text) => new("heading:" + id, text, "", "", false, text, VoiceGroups.Heading);

    /// <summary>A copy of the draft for transient engine configs.</summary>
    private System.Text.Json.Nodes.JsonObject PreviewConfig() => draft.DeepClone().AsObject();
}
