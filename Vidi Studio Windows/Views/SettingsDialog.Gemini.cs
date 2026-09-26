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
        var key = new PasswordBox { Header = "Personal Gemini API key", Password = draft["apiKey"]?.GetValue<string>() ?? "" };
        key.PasswordChanged += (_, _) => draft["apiKey"] = key.Password;
        panel.Children.Add(key);
        AddText(panel, "API version", "apiVersion", "v1beta");
        AddChoice(panel, "Video processing", "videoProcessing", new[] { "agentic", "standard" });
        var refresh = new Button { Content = "Reload models from Gemini" };
        refresh.Click += async (_, _) => await RefreshGeminiModelsAsync();
        key.LostFocus += async (_, _) => { if (!string.IsNullOrWhiteSpace(key.Password)) await RefreshGeminiModelsAsync(); };
        panel.Children.Add(refresh);
        panel.Children.Add(new TextBlock { Text = "Model choices come from Google for this API key. Choose a model for each job, or select the latest available models after loading the catalog.", TextWrapping = TextWrapping.Wrap, Opacity = .7 });
        var latest = new Button { Content = "Use latest available models" };
        latest.Click += (_, _) => SelectLatestModels();
        panel.Children.Add(latest);
        modelSelectors.Clear();
        panel.Children.Add(new TextBlock { Text = "Local videos", FontSize = 17, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold, Margin = new Thickness(0, 12, 0, 0) });
        AddModelSelector(panel, "transcript", "Speech transcription");
        AddModelSelector(panel, "summary", "Video summaries");
        AddModelSelector(panel, "audio_descriptions", "Audio descriptions");
        AddModelSelector(panel, "screen_text", "On-screen text");
        AddModelSelector(panel, "text_rewrite", "Description rewrites");
        AddModelSelector(panel, "tts_audio", "Gemini narration");
        panel.Children.Add(new TextBlock { Text = "YouTube videos", FontSize = 17, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold, Margin = new Thickness(0, 12, 0, 0) });
        AddModelSelector(panel, "youtube_transcript", "YouTube transcription");
        AddModelSelector(panel, "youtube_summary", "YouTube summaries");
        AddModelSelector(panel, "youtube_audio_descriptions", "YouTube audio descriptions");
        AddModelSelector(panel, "youtube_screen_text", "YouTube on-screen text");
        AddModelSelector(panel, "youtube_text_rewrite", "YouTube description rewrites");
        AddModelSelector(panel, "youtube_tts_audio", "YouTube Gemini narration");
        if (!string.IsNullOrWhiteSpace(key.Password)) _ = RefreshGeminiModelsAsync();
    }

    private void AddModelSelector(StackPanel panel, string stage, string title)
    {
        var selector = new ComboBox { Header = title, HorizontalAlignment = HorizontalAlignment.Stretch };
        modelSelectors[stage] = selector;
        var models = draft["models"]!.AsObject();
        var current = models[stage]?.GetValue<string>() ?? (stage.StartsWith("youtube_", StringComparison.Ordinal) ? models[stage[8..]]?.GetValue<string>() ?? "" : "");
        if (models[stage] == null && current.Length > 0) models[stage] = current;
        selector.Items.Add(new GeminiModelOption(current, current.Length == 0 ? "Load available models" : current));
        selector.SelectedIndex = 0;
        selector.SelectionChanged += (_, _) =>
        {
            if (selector.SelectedItem is GeminiModelOption chosen && chosen.Id.Length > 0)
                draft["models"]!.AsObject()[stage] = chosen.Id;
        };
        panel.Children.Add(selector);
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
            foreach (var (stage, selector) in modelSelectors)
            {
                var current = draft["models"]?[stage]?.GetValue<string>() ?? "";
                var choices = availableModels.Where(option => stage.EndsWith("tts_audio", StringComparison.Ordinal) == option.Id.Contains("tts", StringComparison.OrdinalIgnoreCase)).ToList();
                if (current.Length > 0 && choices.All(option => option.Id != current)) choices.Insert(0, new GeminiModelOption(current, current + " (currently saved)"));
                selector.Items.Clear();
                selector.ItemsSource = choices;
                selector.SelectedItem = choices.FirstOrDefault(option => option.Id == current);
            }
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
