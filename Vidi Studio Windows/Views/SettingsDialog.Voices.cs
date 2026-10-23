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
    /// <summary>Picker IDs for online voices; the stored engine becomes "azure" with the bare voice name.</summary>
    private const string OnlineVoicePrefix = "online:";
    private readonly StackPanel azureControls = new() { Spacing = 10 };
    private readonly StackPanel naturalControls = new() { Spacing = 8 };
    private readonly PasswordBox azureKey = new() { Header = "Azure Speech key (optional)", PlaceholderText = "Paste your key to add online voices" };
    private readonly TextBox azureRegion = new() { Header = "Azure region", PlaceholderText = DefaultAzureRegion };
    private bool voiceControlsBuilt;
    private string loadedAzureAccount = "";

    private string DraftAzureKey => SettingsStore.GetString(draft[SettingsStore.AzureSpeechKey]).Trim();
    private string AzureAccount => DraftAzureKey + "|" + SettingsStore.GetString(draft["azureSpeechRegion"], DefaultAzureRegion);

    private void BuildVoiceEngineControls()
    {
        foreach (var element in new FrameworkElement[] { azureControls, naturalControls }) if (element.Parent is Panel parent) parent.Children.Remove(element);
        azureControls.Children.Clear(); naturalControls.Children.Clear();
        azureKey.Password = DraftAzureKey;
        azureRegion.Text = SettingsStore.GetString(draft["azureSpeechRegion"], DefaultAzureRegion);
        AutomationProperties.SetName(azureKey, "Azure Speech key, optional");
        AutomationProperties.SetName(azureRegion, "Azure region");
        if (!voiceControlsBuilt)
        {
            voiceControlsBuilt = true;
            azureKey.PasswordChanged += (_, _) => { draft[SettingsStore.AzureSpeechKey] = azureKey.Password.Trim(); UpdateSampleControls(); };
            azureRegion.TextChanged += (_, _) => draft["azureSpeechRegion"] = azureRegion.Text.Trim().ToLowerInvariant();
            // Reload the list once the key or region is finished, so online voices appear or disappear.
            azureKey.LostFocus += async (_, _) => { if (AzureAccount != loadedAzureAccount) await RefreshVoices(); };
            azureRegion.LostFocus += async (_, _) => { if (AzureAccount != loadedAzureAccount) await RefreshVoices(); };
        }

        naturalControls.Children.Add(new TextBlock { TextWrapping = TextWrapping.Wrap, Opacity = .75,
            Text = "Natural voices sound more like a real person. The list shows two kinds:\n" +
                   "• Downloaded to this PC: voices you added in Windows Settings for Narrator or Magnifier. They work without the internet, but Windows only shares them with other apps through a free voice bridge add-on, such as NaturalVoiceSAPIAdapter. Voices that need the add-on are shown but can't be picked yet.\n" +
                   "• Online with your Azure key: every Microsoft natural voice, in every language. These need the internet, send the narration text to Microsoft Azure, and Azure may charge for use." });
        var links = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4 };
        links.Children.Add(new HyperlinkButton { Content = "Download natural voices in Windows Settings", NavigateUri = new Uri("ms-settings:easeofaccess-narrator") });
        links.Children.Add(new HyperlinkButton { Content = "Get the voice bridge add-on", NavigateUri = new Uri("https://github.com/gexgd0419/NaturalVoiceSAPIAdapter") });
        naturalControls.Children.Add(links);
        naturalControls.Children.Add(new TextBlock { TextWrapping = TextWrapping.Wrap, Opacity = .6, FontSize = 12,
            Text = "The voice bridge add-on is made by an independent developer, not by Microsoft or Vidi Studio." });

        azureControls.Children.Add(azureKey);
        azureControls.Children.Add(azureRegion);
        azureControls.Children.Add(new HyperlinkButton { Content = "Get an Azure Speech key", NavigateUri = new Uri("https://portal.azure.com/#create/Microsoft.CognitiveServicesSpeechServices") });
    }

    private void UpdateVoiceEngineVisibility(string provider)
    {
        var natural = provider == "windows-natural" ? Visibility.Visible : Visibility.Collapsed;
        azureControls.Visibility = natural;
        naturalControls.Visibility = natural;
    }

    /// <summary>Stores a Natural Voices choice: downloaded voices speak locally, online voices use Azure.</summary>
    private void ApplyNaturalVoice(string id)
    {
        draft[VoiceKey("windows-natural")] = id;
        if (id.StartsWith(OnlineVoicePrefix, StringComparison.Ordinal))
        {
            draft["ttsMode"] = "azure";
            draft["azureVoice"] = id[OnlineVoicePrefix.Length..];
        }
        else
        {
            draft["ttsMode"] = "windows-natural";
            draft["systemVoice"] = id;
        }
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
        List<Voice> downloaded;
        string downloadedStatus;
        try
        {
            downloaded = await SpeechService.VoicesAsync(host, "windows-natural", cancellation);
            var ready = downloaded.Count(v => v.Available);
            downloadedStatus = downloaded.Count == 0
                ? "No downloaded natural voices were found. You can download them in Windows Settings > Accessibility > Narrator."
                : ready == 0
                    ? $"{downloaded.Count} downloaded voices found. Install the voice bridge add-on to use them, then select Refresh voices."
                    : $"{ready} of {downloaded.Count} downloaded voices ready.";
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            model.Engine.Log(ex.ToString());
            downloaded = new();
            downloadedStatus = "Downloaded voices could not be checked.";
        }

        List<Voice> online = new();
        string onlineStatus;
        loadedAzureAccount = AzureAccount;
        if (DraftAzureKey.Length == 0) onlineStatus = "Add an Azure Speech key to also see online voices.";
        else
        {
            try
            {
                online = await AzureVoicesAsync(cancellation);
                onlineStatus = $"{online.Count} online voices available.";
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex)
            {
                model.Engine.Log(ex.ToString());
                onlineStatus = "Online voices could not be loaded. " + ex.Message;
            }
        }

        // Keep a saved online choice while Azure is unreachable so it is not silently replaced.
        var saved = SettingsStore.GetString(draft[VoiceKey("windows-natural")]);
        if (DraftAzureKey.Length > 0 && saved.StartsWith(OnlineVoicePrefix, StringComparison.Ordinal) && !online.Any(v => v.Id == saved))
            online.Insert(0, new Voice(saved, NarrationVoiceFormatting.VoiceDisplayName(saved[OnlineVoicePrefix.Length..]), "", "", true,
                "Online voice. Needs the internet and uses your Azure key.", VoiceGroups.Online));
        var voices = new List<Voice>();
        var bridgeOnline = downloaded.Where(v => v.Group == VoiceGroups.BridgeOnline).ToList();
        var local = downloaded.Except(bridgeOnline).ToList();
        if (local.Count > 0)
        {
            voices.Add(Heading("downloaded", "Downloaded to this PC"));
            voices.AddRange(local.Select(v => v with { Group = VoiceGroups.Downloaded }));
        }
        if (online.Count > 0)
        {
            voices.Add(Heading("online", "Online with your Azure key"));
            voices.AddRange(online);
        }
        if (bridgeOnline.Count > 0)
        {
            voices.Add(Heading("bridge-online", "Online through the voice bridge add-on"));
            voices.AddRange(bridgeOnline);
        }
        return (voices, downloadedStatus + " " + onlineStatus);
    }

    private async Task<List<Voice>> AzureVoicesAsync(CancellationToken cancellation)
    {
        Directory.CreateDirectory(SettingsStore.DirectoryPath);
        var config = Path.Combine(SettingsStore.DirectoryPath, Guid.NewGuid() + ".json");
        try
        {
            await File.WriteAllTextAsync(config, PreviewConfig().ToJsonString(Models.Json.Options), cancellation);
            var events = await model.Engine.RunAsync(new[] { "list-azure-voices" }, cancellation: cancellation, configPath: config, azureSpeechKey: DraftAzureKey);
            var payload = events.LastOrDefault(e => e.Data.TryGetProperty("voices", out _))
                ?? throw new InvalidOperationException("Azure returned no voices.");
            return payload.Data.GetProperty("voices").EnumerateArray()
                .Select(item => new Voice(OnlineVoicePrefix + Text(item, "id"), "Microsoft " + Text(item, "name"), Text(item, "language"), Text(item, "gender"),
                    true, "Online voice. Needs the internet and uses your Azure key.", VoiceGroups.Online))
                .Where(v => v.Id.Length > OnlineVoicePrefix.Length).ToList();
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
