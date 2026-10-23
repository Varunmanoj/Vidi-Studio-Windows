using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Vidi_Studio_Windows.Models;
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
    private const string DefaultAzureVoice = SpeechService.DefaultAzureVoice;
    private const string DefaultAzureRegion = SpeechService.DefaultAzureRegion;
    private readonly StackPanel azureControls = new() { Spacing = 10 };
    private readonly StackPanel naturalControls = new() { Spacing = 8 };
    private readonly StackPanel summaryControls = new() { Spacing = 8 };
    private readonly PasswordBox azureKey = new() { Header = "Azure Speech key", PlaceholderText = "Stored in Windows Credential Manager" };
    private readonly TextBox azureRegion = new() { Header = "Azure region", PlaceholderText = DefaultAzureRegion };
    private readonly ComboBox summaryVoice = new() { Header = "Summary read-aloud voice", HorizontalAlignment = HorizontalAlignment.Stretch };
    private readonly TextBlock summaryVoiceNote = new() { TextWrapping = TextWrapping.Wrap, Opacity = .75 };
    private bool voiceControlsBuilt;
    private string loadedAzureAccount = "";

    private string DraftAzureKey => SettingsStore.GetString(draft[SettingsStore.AzureSpeechKey]).Trim();
    private string AzureAccount => DraftAzureKey + "|" + SettingsStore.GetString(draft["azureSpeechRegion"], DefaultAzureRegion);

    private void BuildVoiceEngineControls()
    {
        foreach (var element in new FrameworkElement[] { azureControls, naturalControls, summaryControls }) if (element.Parent is Panel parent) parent.Children.Remove(element);
        azureControls.Children.Clear(); naturalControls.Children.Clear(); summaryControls.Children.Clear();
        azureKey.Password = DraftAzureKey;
        azureRegion.Text = SettingsStore.GetString(draft["azureSpeechRegion"], DefaultAzureRegion);
        AutomationProperties.SetName(azureKey, "Azure Speech key");
        AutomationProperties.SetName(azureRegion, "Azure region");
        if (!voiceControlsBuilt)
        {
            voiceControlsBuilt = true;
            azureKey.PasswordChanged += (_, _) => { draft[SettingsStore.AzureSpeechKey] = azureKey.Password.Trim(); UpdateSampleControls(); UpdateSummaryVoiceOptions(); };
            azureRegion.TextChanged += (_, _) => draft["azureSpeechRegion"] = azureRegion.Text.Trim().ToLowerInvariant();
            // Reload Azure voices once the key or region is finished.
            azureKey.LostFocus += async (_, _) => { if (AzureAccount != loadedAzureAccount) await RefreshVoices(); };
            azureRegion.LostFocus += async (_, _) => { if (AzureAccount != loadedAzureAccount) await RefreshVoices(); };
            summaryVoice.SelectionChanged += (_, _) =>
            {
                if (summaryVoice.SelectedItem is PlaybackOption option) draft["summaryReadbackVoice"] = option.Id;
                UpdateSummaryVoiceNote();
            };
        }

        naturalControls.Children.Add(new TextBlock { TextWrapping = TextWrapping.Wrap, Opacity = .75,
            Text = "Natural voices sound more like a real person. This list shows the voices downloaded to this PC in Windows Settings for Narrator or Magnifier. They work without the internet, but Windows only shares them with other apps through a free voice bridge add-on, such as NaturalVoiceSAPIAdapter. Voices that need the add-on are shown but can't be picked yet. For online voices in every language, choose Azure Voices." });
        var links = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4 };
        links.Children.Add(new HyperlinkButton { Content = "Download natural voices in Windows Settings", NavigateUri = new Uri("ms-settings:easeofaccess-narrator") });
        links.Children.Add(new HyperlinkButton { Content = "Get the voice bridge add-on", NavigateUri = new Uri("https://github.com/gexgd0419/NaturalVoiceSAPIAdapter") });
        naturalControls.Children.Add(links);
        naturalControls.Children.Add(new TextBlock { TextWrapping = TextWrapping.Wrap, Opacity = .6, FontSize = 12,
            Text = "The voice bridge add-on is made by an independent developer, not by Microsoft or Vidi Studio." });

        azureControls.Children.Add(new TextBlock { TextWrapping = TextWrapping.Wrap, Opacity = .75,
            Text = "Azure Voices are Microsoft's natural online voices, such as Ava, Andrew, Jenny and Ryan, in every language. Paste your Azure Speech key and its region to use them. They need the internet, send the narration text to Microsoft Azure, and Azure may charge for use." });
        azureControls.Children.Add(azureKey);
        azureControls.Children.Add(azureRegion);
        azureControls.Children.Add(new HyperlinkButton { Content = "Get an Azure Speech key", NavigateUri = new Uri("https://portal.azure.com/#create/Microsoft.CognitiveServicesSpeechServices") });

        var heading = new TextBlock { Text = "Summary read-aloud", FontSize = 17, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 12, 0, 0) };
        AutomationProperties.SetHeadingLevel(heading, Microsoft.UI.Xaml.Automation.Peers.AutomationHeadingLevel.Level2);
        summaryControls.Children.Add(heading);
        summaryControls.Children.Add(summaryVoice);
        summaryControls.Children.Add(summaryVoiceNote);
        UpdateSummaryVoiceOptions();
    }

    private void UpdateVoiceEngineVisibility(string provider)
    {
        naturalControls.Visibility = provider == "windows-natural" ? Visibility.Visible : Visibility.Collapsed;
        azureControls.Visibility = provider == "azure" ? Visibility.Visible : Visibility.Collapsed;
        UpdateSummaryVoiceNote();
    }

    /// <summary>The Azure summary voice is offered only while an Azure Speech key is entered.</summary>
    private void UpdateSummaryVoiceOptions()
    {
        var options = new List<PlaybackOption> { new("narration", "Same as narration voice") };
        if (DraftAzureKey.Length > 0) options.Add(new("azure", "Azure online voice"));
        var saved = SettingsStore.GetString(draft["summaryReadbackVoice"], "narration");
        if (summaryVoice.ItemsSource is List<PlaybackOption> current && current.Select(o => o.Id).SequenceEqual(options.Select(o => o.Id))
            && (summaryVoice.SelectedItem as PlaybackOption)?.Id == saved) return;
        summaryVoice.ItemsSource = options;
        summaryVoice.SelectedItem = options.FirstOrDefault(o => o.Id == saved) ?? options[0];
        UpdateSummaryVoiceNote();
    }

    private void UpdateSummaryVoiceNote()
    {
        var useAzure = (summaryVoice.SelectedItem as PlaybackOption)?.Id == "azure";
        var azureVoice = NarrationVoiceFormatting.VoiceDisplayName(SettingsStore.GetString(draft["azureVoice"], DefaultAzureVoice));
        summaryVoiceNote.Text = useAzure
            ? $"The summary is read with {azureVoice}, the voice last chosen under Azure Voices. Each sentence is created online, so reading needs the internet and may start after a short pause."
            : DraftAzureKey.Length == 0
                ? "The summary is read with the narration voice. To read it with an online voice, choose Azure Voices as the speech engine and add your Azure Speech key."
                : "The summary is read with the voice chosen for narration above.";
    }

    private static List<Voice> FilterWithGroupHeadings(IEnumerable<Voice> source, string text)
    {
        var result = new List<Voice>();
        Voice? heading = null;
        foreach (var item in source)
        {
            if (item.Group == VoiceGroups.Heading) { heading = item; continue; }
            if (!item.ToString().Contains(text, StringComparison.CurrentCultureIgnoreCase)) continue;
            if (heading != null) { result.Add(heading); heading = null; }
            result.Add(item);
        }
        return result;
    }

    private static Voice Heading(string id, string text) => new("heading:" + id, text, "", "", false, text, VoiceGroups.Heading);

    private async Task<(List<Voice> Voices, string Status)> NaturalVoicesAsync(CancellationToken cancellation)
    {
        var host = !string.IsNullOrWhiteSpace(sapiHost.Text) ? sapiHost.Text : model.Engine.SpeechHost;
        var downloaded = (await SpeechService.VoicesAsync(host, "windows-natural", cancellation)).Where(v => v.Group != VoiceGroups.BridgeOnline).ToList();
        var ready = downloaded.Count(v => v.Available);
        var status = downloaded.Count == 0
            ? "No downloaded natural voices were found. You can download them in Windows Settings > Accessibility > Narrator, then select Refresh voices."
            : ready == 0
                ? $"{downloaded.Count} downloaded voices found. Install the voice bridge add-on to use them, then select Refresh voices."
                : $"{ready} of {downloaded.Count} downloaded voices ready.";
        var voices = new List<Voice>();
        if (downloaded.Count > 0)
        {
            voices.Add(Heading("downloaded", "Downloaded to this PC"));
            voices.AddRange(downloaded.Select(v => v with { Group = VoiceGroups.Downloaded }));
        }
        return (voices, status);
    }

    private async Task<(List<Voice> Voices, string Status)> AzureVoicesAsync(CancellationToken cancellation)
    {
        loadedAzureAccount = AzureAccount;
        var saved = SettingsStore.GetString(draft["azureVoice"], DefaultAzureVoice).Trim();
        var savedVoice = new Voice(saved, NarrationVoiceFormatting.VoiceDisplayName(saved), "", "");
        if (DraftAzureKey.Length == 0)
            return (new() { savedVoice }, "Enter your Azure Speech key and region to load Azure Voices.");
        Directory.CreateDirectory(SettingsStore.DirectoryPath);
        var config = Path.Combine(SettingsStore.DirectoryPath, Guid.NewGuid() + ".json");
        try
        {
            await File.WriteAllTextAsync(config, PreviewConfig().ToJsonString(Models.Json.Options), cancellation);
            var events = await model.Engine.RunAsync(new[] { "list-azure-voices" }, cancellation: cancellation, configPath: config, azureSpeechKey: DraftAzureKey);
            var payload = events.LastOrDefault(e => e.Data.TryGetProperty("voices", out _))
                ?? throw new InvalidOperationException("Azure returned no voices.");
            var voices = payload.Data.GetProperty("voices").EnumerateArray()
                .Select(item => new Voice(Text(item, "id"), "Microsoft " + Text(item, "name"), Text(item, "language"), Text(item, "gender"),
                    true, "Online voice. Needs the internet and uses your Azure key."))
                .Where(v => v.Id.Length > 0).ToList();
            // Keep the saved choice even if this region does not list it, so it is not silently replaced.
            if (saved.Length > 0 && !voices.Any(v => string.Equals(v.Id, saved, StringComparison.OrdinalIgnoreCase))) voices.Insert(0, savedVoice);
            return (voices, $"{voices.Count} Azure Voices available.");
        }
        finally { try { File.Delete(config); } catch (IOException) { } }
        static string Text(JsonElement item, string name) => item.TryGetProperty(name, out var value) ? value.GetString() ?? "" : "";
    }

    /// <summary>A copy of the draft for transient engine configs; the Azure key travels only in the environment.</summary>
    private System.Text.Json.Nodes.JsonObject PreviewConfig()
    {
        var config = draft.DeepClone().AsObject();
        config.Remove(SettingsStore.AzureSpeechKey);
        return config;
    }
}
