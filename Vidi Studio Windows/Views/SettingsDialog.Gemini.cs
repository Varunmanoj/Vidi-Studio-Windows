using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Vidi_Studio_Windows.Services;

namespace Vidi_Studio_Windows.Views;

internal sealed record GeminiModelOption(string Id, string Label)
{
    public override string ToString() => Label;
}

internal static class GeminiVoices
{
    public static IReadOnlyList<Voice> Featured { get; } =
        "Zephyr|Puck|Charon|Kore|Fenrir|Leda|Orus|Aoede|Callirrhoe|Autonoe|Enceladus|Iapetus|Umbriel|Algieba|Despina|Erinome|Algenib|Rasalgethi|Laomedeia|Achernar|Alnilam|Schedar|Gacrux|Pulcherrima|Achird|Zubenelgenubi|Vindemiatrix|Sadachbia|Sadaltager|Sulafat"
        .Split('|').Select(name => new Voice(name, name, "", "")).ToArray();
}

public sealed partial class SettingsDialog
{
    private readonly List<GeminiModelOption> availableModels = new();
    private readonly Dictionary<string, ComboBox> modelSelectors = new();

    private async Task<List<Services.EngineEvent>> QueryGeminiCatalogAsync(string command, CancellationToken cancellation)
    {
        Directory.CreateDirectory(SettingsStore.DirectoryPath);
        var config = Path.Combine(SettingsStore.DirectoryPath, Guid.NewGuid() + ".json");
        try
        {
            await File.WriteAllTextAsync(config, draft.ToJsonString(Models.Json.Options), cancellation);
            return await model.Engine.RunAsync(new[] { command }, configPath: config, cancellation: cancellation);
        }
        finally { if (File.Exists(config)) File.Delete(config); }
    }

    private async Task<List<Voice>> LoadGeminiVoicesAsync(CancellationToken cancellation)
    {
        try
        {
            var events = await QueryGeminiCatalogAsync("list-voices", cancellation);
            var found = events.LastOrDefault(item => item.Data.TryGetProperty("voices", out _));
            if (found == null) return new();
            return found.Data.GetProperty("voices").EnumerateArray()
                .Select(item => new Voice(item.GetProperty("id").GetString() ?? "", item.GetProperty("name").GetString() ?? "", item.GetProperty("language").GetString() ?? "", item.GetProperty("gender").GetString() ?? ""))
                .Where(item => item.Id.Length > 0).ToList();
        }
        catch (OperationCanceledException) { return new(); }
        catch (Exception ex) { model.Engine.Log(ex.ToString()); status.Text = "Gemini voice catalog unavailable. Featured voices remain selectable."; return new(); }
    }

    private void BuildGemini(StackPanel panel)
    {
        panel.Children.Add(new TextBlock { Text = "Gemini API", FontSize = 20, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold });
        var connection = AddGeminiGroup(panel, "Connect to Gemini");
        var key = new PasswordBox { Header = "Personal Gemini API key", Password = draft["apiKey"]?.GetValue<string>() ?? "" };
        key.PasswordChanged += (_, _) => draft["apiKey"] = key.Password;
        connection.Children.Add(key);
        var apiKeys = new HyperlinkButton { Content = "Open API Keys in Google AI Studio", NavigateUri = new Uri("https://aistudio.google.com/api-keys") };
        connection.Children.Add(apiKeys);
        AddGeminiNote(connection, "Your key lets VidiStudio create transcripts, summaries, descriptions, and online narration.");
        var refresh = new Button { Content = "Load Available Models", IsEnabled = !string.IsNullOrWhiteSpace(key.Password) };
        key.PasswordChanged += (_, _) => refresh.IsEnabled = !string.IsNullOrWhiteSpace(key.Password);
        refresh.Click += async (_, _) =>
        {
            refresh.IsEnabled = false;
            try { await RefreshGeminiModelsAsync(); }
            finally { refresh.IsEnabled = !string.IsNullOrWhiteSpace(key.Password); }
        };
        connection.Children.Add(refresh);
        AddGeminiNote(connection, "Enter your API key, then load the models available to your account.");
        modelSelectors.Clear();
        var local = AddGeminiGroup(panel, "Local video");
        AddModelSelector(local, "transcript", "Speech transcription");
        AddModelSelector(local, "summary", "Video summaries");
        AddModelSelector(local, "audio_descriptions", "Audio descriptions");
        AddModelSelector(local, "screen_text", "On-screen text");
        AddModelSelector(local, "text_rewrite", "Description rewrites");
        AddModelSelector(local, "tts_audio", "Gemini narration");
        AddGeminiNote(panel, "Summary, description, on-screen text, rewrite, and narration selections are also used for YouTube projects.");
        var youtube = AddGeminiGroup(panel, "YouTube video");
        AddModelSelector(youtube, "youtube_transcript", "YouTube transcription");
        AddGeminiNote(panel, "YouTube transcription uses a video model, such as Gemini Flash, to process the online video source. The dedicated speech transcription model is only for local videos.");
        var advanced = new StackPanel { Spacing = 12 };
        AddText(advanced, "API version", "apiVersion", "v1beta");
        AddChoice(advanced, "Video processing", "videoProcessing", new[] { "agentic", "standard" });
        var latest = new Button { Content = "Use latest available models" };
        latest.Click += (_, _) => SelectLatestModels();
        advanced.Children.Add(latest);
        panel.Children.Add(new Expander { Header = "Advanced", Content = advanced, HorizontalAlignment = HorizontalAlignment.Stretch });
        AddGeminiNote(panel, "Your Gemini key stays on this PC and is never saved inside a project.");
        if (!string.IsNullOrWhiteSpace(key.Password)) _ = RefreshGeminiModelsAsync();
    }

    private static void AddGeminiNote(StackPanel panel, string text) =>
        panel.Children.Add(new TextBlock { Text = text, TextWrapping = TextWrapping.Wrap, FontSize = 12, Opacity = .7 });

    private static StackPanel AddGeminiGroup(StackPanel panel, string title)
    {
        panel.Children.Add(new TextBlock { Text = title, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold, Margin = new Thickness(0, 12, 0, 0) });
        var rows = new StackPanel { Spacing = 8 };
        panel.Children.Add(new Border
        {
            CornerRadius = new CornerRadius(10), Padding = new Thickness(12), Child = rows,
            Background = new Microsoft.UI.Xaml.Media.SolidColorBrush(Windows.UI.Color.FromArgb(18, 128, 128, 128))
        });
        return rows;
    }

    private bool SupportsModelStage(string stage, string id)
    {
        bool Contains(string term) => id.Contains(term, StringComparison.OrdinalIgnoreCase);
        if (stage == "tts_audio") return Contains("tts");
        if (stage == "transcript" && availableModels.Any(option => option.Id.Contains("transcribe", StringComparison.OrdinalIgnoreCase)))
            return Contains("transcribe");
        return !new[] { "tts", "transcribe", "image", "embedding", "live", "computer-use", "robotics", "research" }.Any(Contains);
    }

    private void ApplyGeminiModelChoices()
    {
        foreach (var (stage, selector) in modelSelectors)
        {
            var current = draft["models"]?[stage]?.GetValue<string>() ?? "";
            var choices = availableModels.Where(option => SupportsModelStage(stage, option.Id)).ToList();
            selector.ItemsSource = null;
            selector.Items.Clear();
            selector.ItemsSource = choices;
            // Saved models must satisfy the same account availability and action rules.
            var preferred = stage switch
            {
                "transcript" => new[] { "gemini-3.5-transcribe" },
                "tts_audio" => new[] { "gemini-3.8-flash-lite-tts", "gemini-3.8-flash-tts", "gemini-3.1-flash-tts-preview", "gemini-2.5-flash-preview-tts" },
                _ => Array.Empty<string>()
            };
            selector.SelectedItem = choices.FirstOrDefault(option => option.Id == current)
                ?? preferred.Concat(new[] { "gemini-3.8-flash", "gemini-3.5-flash", "gemini-2.5-flash" })
                    .Select(id => choices.FirstOrDefault(option => option.Id == id)).FirstOrDefault(option => option != null)
                ?? choices.FirstOrDefault();
            selector.PlaceholderText = choices.Count == 0 ? "No suitable models available" : "Choose a model";
            selector.IsEnabled = choices.Count > 0;
        }
    }

#if DEBUG
    internal void VerifyGeminiModelFiltering()
    {
        var originalModels = draft["models"]!.DeepClone();
        var originalCatalog = availableModels.ToList();
        var originalStatus = status.Text;
        try
        {
            NavigateForSmokeTest(2);
            availableModels.Clear();
            foreach (var id in new[] { "gemini-3.8-flash", "gemini-3.5-transcribe", "gemini-3.8-flash-lite-tts", "gemini-image", "gemini-embedding", "gemini-live", "gemini-computer-use", "gemini-robotics", "gemini-research" })
                availableModels.Add(new GeminiModelOption(id, id));
            draft["models"]!["transcript"] = "gemini-3.8-flash";
            draft["models"]!["summary"] = "gemini-image";
            ApplyGeminiModelChoices();
            ApplyGeminiModelChoices(); // Repeated refresh must preserve filtering and valid selections.
            foreach (var (stage, selector) in modelSelectors)
            {
                var expected = stage == "transcript" ? "gemini-3.5-transcribe" : stage == "tts_audio" ? "gemini-3.8-flash-lite-tts" : "gemini-3.8-flash";
                var choices = ((IEnumerable<GeminiModelOption>)selector.ItemsSource).ToList();
                if (choices.Count != 1 || choices[0].Id != expected || (selector.SelectedItem as GeminiModelOption)?.Id != expected)
                    throw new InvalidOperationException("Unsuitable Gemini model offered for " + stage);
            }
            SelectLatestModels();
            if (draft["models"]!["transcript"]!.GetValue<string>() != "gemini-3.5-transcribe")
                throw new InvalidOperationException("Latest model selection bypassed transcription filtering.");
            availableModels.RemoveAll(option => option.Id.Contains("transcribe"));
            ApplyGeminiModelChoices();
            if ((modelSelectors["transcript"].SelectedItem as GeminiModelOption)?.Id != "gemini-3.8-flash")
                throw new InvalidOperationException("Transcription did not fall back to a general model.");
            availableModels.Clear();
            ApplyGeminiModelChoices();
            if (modelSelectors.Values.Any(selector => selector.IsEnabled || selector.Items.Count != 0))
                throw new InvalidOperationException("Unavailable saved models were reintroduced into the catalog.");
        }
        finally
        {
            draft["models"] = originalModels;
            availableModels.Clear();
            availableModels.AddRange(originalCatalog);
            status.Text = originalStatus;
            NavigateForSmokeTest(0);
            NavigateForSmokeTest(2);
        }
    }
#endif

    private void AddModelSelector(StackPanel panel, string stage, string title)
    {
        var row = new Grid { ColumnSpacing = 12, MinHeight = 38 };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.3, GridUnitType.Star) });
        row.Children.Add(new TextBlock { Text = title, VerticalAlignment = VerticalAlignment.Center, TextWrapping = TextWrapping.Wrap });
        var selector = new ComboBox { HorizontalAlignment = HorizontalAlignment.Stretch, IsEnabled = false };
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(selector, title);
        modelSelectors[stage] = selector;
        var models = draft["models"]!.AsObject();
        var current = models[stage]?.GetValue<string>() ?? "";
        selector.Items.Add(new GeminiModelOption(current, current.Length == 0 ? "Load available models" : current));
        selector.SelectedIndex = 0;
        selector.SelectionChanged += (_, _) =>
        {
            if (selector.SelectedItem is GeminiModelOption chosen && chosen.Id.Length > 0 && SupportsModelStage(stage, chosen.Id))
                draft["models"]!.AsObject()[stage] = chosen.Id;
        };
        Grid.SetColumn(selector, 1);
        row.Children.Add(selector);
        panel.Children.Add(row);
    }

    private async Task RefreshGeminiModelsAsync()
    {
        if (closed || modelSelectors.Count == 0) return;
        if (string.IsNullOrWhiteSpace(draft["apiKey"]?.GetValue<string>())) { status.Text = "Add a Gemini API key to load available models."; return; }
        try
        {
            status.Text = "Loading models from Gemini…";
            var events = await QueryGeminiCatalogAsync("list-models", lifetime.Token);
            if (closed) return;
            var found = events.LastOrDefault(item => item.Data.TryGetProperty("models", out _));
            if (found == null) throw new InvalidDataException("Gemini returned no model catalog.");
            availableModels.Clear();
            foreach (var item in found.Data.GetProperty("models").EnumerateArray())
            {
                var id = item.GetProperty("name").GetString();
                if (!string.IsNullOrWhiteSpace(id)) availableModels.Add(new GeminiModelOption(id, item.GetProperty("displayName").GetString() ?? id));
            }
            ApplyGeminiModelChoices();
            status.Text = $"{availableModels.Count} Gemini models available for this key.";
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) { model.Engine.Log(ex.ToString()); status.Text = "Could not load Gemini models. Check the API key and connection."; }
    }

    private void SelectLatestModels()
    {
        if (availableModels.Count == 0) { status.Text = "Load the Gemini catalog first."; return; }
        static (int Major, int Minor, int Patch, int Alias) Rank(string id)
        {
            var version = Regex.Match(id, @"gemini-(\d+)\.(\d+)(?:\.(\d+))?");
            int Parse(Group group) => int.TryParse(group.Value, out var number) ? number : 0;
            return (Parse(version.Groups[1]), Parse(version.Groups[2]), Parse(version.Groups[3]), id.Contains("latest", StringComparison.OrdinalIgnoreCase) ? 1 : 0);
        }
        foreach (var (stage, selector) in modelSelectors)
        {
            var options = ((IEnumerable<GeminiModelOption>?)selector.ItemsSource)?.Where(item => availableModels.Any(available => available.Id == item.Id)).ToList();
            if (options is not { Count: > 0 }) continue;
            var latest = options.OrderByDescending(option => Rank(option.Id)).First();
            selector.SelectedItem = latest;
            draft["models"]!.AsObject()[stage] = latest.Id;
        }
        status.Text = "Selected the newest available version in each model category. Review choices before saving.";
    }
}
