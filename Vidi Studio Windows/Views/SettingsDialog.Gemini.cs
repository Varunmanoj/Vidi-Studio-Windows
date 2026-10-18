using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
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
using Windows.UI;

namespace Vidi_Studio_Windows.Views;

internal sealed record GeminiModelOption(string Id, string Label)
{
    public override string ToString() => Label;
}

internal static class GeminiVoices
{
    public static readonly string[] Names =
    {
        "Aoede", "Callirrhoe", "Charon", "Fenrir", "Kore", "Leda", "Orus", "Puck", "Zephyr"
    };

    public static IReadOnlyList<Voice> Featured { get; } =
        Names.Select(name => new Voice(name, name, "", "")).ToArray();

    public static IReadOnlyList<Voice> Voices => Featured;
}

public sealed partial class SettingsDialog
{
    private readonly List<GeminiModelOption> availableModels = new();
    private readonly Dictionary<string, ComboBox> modelSelectors = new();

    private readonly Border apiStatusCard = new();
    private readonly FontIcon apiStatusIcon = new();
    private readonly TextBlock apiStatusTitle = new();
    private readonly TextBlock apiStatusDescription = new() { TextWrapping = TextWrapping.Wrap };

    private readonly Border planPillBadge = new();
    private readonly FontIcon planDot = new();
    private readonly TextBlock planPillText = new();
    private readonly TextBlock planDetailsSubtitle = new();

    private Button? checkPlanButton;
    private string detectedPlan = "free";
    private DispatcherTimer? checkDebounceTimer;

    private static string CleanModelName(string id, string rawName)
    {
        if (id == "gemini-3.5-transcribe") return "Gemini 3.5 Transcribe";
        if (id == "gemini-3.8-flash") return "Gemini Flash — Latest";
        if (id == "gemini-3.8-flash-lite") return "Gemini Flash-Lite — Latest";
        if (id == "gemini-3.8-flash-lite-tts") return "Gemini 3.8 Flash-Lite TTS";
        if (id == "gemini-3.8-pro") return "Gemini Pro — Latest";
        if (id == "gemini-2.5-flash") return "Gemini 2.5 Flash";
        if (id == "gemini-2.5-pro") return "Gemini 2.5 Pro";
        if (id == "gemini-2.5-flash-lite") return "Gemini 2.5 Flash-Lite";
        if (!string.IsNullOrWhiteSpace(rawName) && rawName != id) return rawName;
        return id;
    }

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
        panel.Children.Add(new TextBlock { Text = "Gemini API", FontSize = 22, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 6) });

        var topStack = new StackPanel { Spacing = 10 };

        var key = new PasswordBox
        {
            Header = "Personal Gemini API key",
            Password = draft["apiKey"]?.GetValue<string>() ?? "",
            HorizontalAlignment = HorizontalAlignment.Stretch
        };
        topStack.Children.Add(key);

        var openKeysLink = new HyperlinkButton
        {
            Content = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 6,
                Children =
                {
                    new FontIcon { Glyph = "\uE8A7", FontSize = 12 },
                    new TextBlock { Text = "Open API Keys in Google AI Studio" }
                }
            },
            NavigateUri = new Uri("https://aistudio.google.com/api-keys"),
            Padding = new Thickness(0, 2, 0, 2)
        };
        topStack.Children.Add(openKeysLink);

        checkPlanButton = new Button
        {
            Content = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 8,
                Children =
                {
                    new FontIcon { Glyph = "\uE72C", FontSize = 13 },
                    new TextBlock { Text = "Check API Key & Reload Models" }
                }
            },
            Padding = new Thickness(14, 8, 14, 8),
            IsEnabled = !string.IsNullOrWhiteSpace(key.Password)
        };
        checkPlanButton.Click += async (_, _) => await CheckApiKeyAndPlanAsync();
        topStack.Children.Add(checkPlanButton);

        // Verification Card matching Mac
        apiStatusCard.CornerRadius = new CornerRadius(8);
        apiStatusCard.Padding = new Thickness(14, 12, 14, 12);
        apiStatusCard.Margin = new Thickness(0, 2, 0, 0);

        var verifyGrid = new Grid { ColumnSpacing = 12 };
        verifyGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        verifyGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        apiStatusIcon.FontSize = 18;
        apiStatusIcon.VerticalAlignment = VerticalAlignment.Center;
        Grid.SetColumn(apiStatusIcon, 0);
        verifyGrid.Children.Add(apiStatusIcon);

        var verifyTextStack = new StackPanel { Spacing = 2, VerticalAlignment = VerticalAlignment.Center };
        apiStatusTitle.FontWeight = Microsoft.UI.Text.FontWeights.SemiBold;
        apiStatusTitle.FontSize = 13;
        apiStatusDescription.FontSize = 12;
        apiStatusDescription.Opacity = 0.85;
        verifyTextStack.Children.Add(apiStatusTitle);
        verifyTextStack.Children.Add(apiStatusDescription);
        Grid.SetColumn(verifyTextStack, 1);
        verifyGrid.Children.Add(verifyTextStack);

        apiStatusCard.Child = verifyGrid;
        topStack.Children.Add(apiStatusCard);

        panel.Children.Add(topStack);

        // Account Plan group matching Mac
        var accountPlanGroup = new StackPanel { Spacing = 8, Margin = new Thickness(0, 16, 0, 0) };
        accountPlanGroup.Children.Add(new TextBlock { Text = "Account Plan", FontWeight = Microsoft.UI.Text.FontWeights.SemiBold, FontSize = 14 });

        var planCard = new Border
        {
            CornerRadius = new CornerRadius(10),
            Padding = new Thickness(16, 14, 16, 14),
            Background = new SolidColorBrush(Color.FromArgb(18, 128, 128, 128))
        };

        var planCardStack = new StackPanel { Spacing = 8 };
        var planRow = new Grid();
        planRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        planRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var planTitle = new TextBlock { Text = "Plan Tier", FontSize = 14, VerticalAlignment = VerticalAlignment.Center };
        Grid.SetColumn(planTitle, 0);
        planRow.Children.Add(planTitle);

        planPillBadge.CornerRadius = new CornerRadius(14);
        planPillBadge.Padding = new Thickness(12, 4, 12, 4);
        var pillStack = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, VerticalAlignment = VerticalAlignment.Center };
        planDot.FontSize = 9;
        planDot.VerticalAlignment = VerticalAlignment.Center;
        planPillText.FontSize = 12;
        planPillText.FontWeight = Microsoft.UI.Text.FontWeights.SemiBold;
        planPillText.VerticalAlignment = VerticalAlignment.Center;
        pillStack.Children.Add(planDot);
        pillStack.Children.Add(planPillText);
        planPillBadge.Child = pillStack;
        Grid.SetColumn(planPillBadge, 1);
        planRow.Children.Add(planPillBadge);
        planCardStack.Children.Add(planRow);

        planDetailsSubtitle.FontSize = 12;
        planDetailsSubtitle.Opacity = 0.75;
        planDetailsSubtitle.TextWrapping = TextWrapping.Wrap;
        planCardStack.Children.Add(planDetailsSubtitle);

        planCard.Child = planCardStack;
        accountPlanGroup.Children.Add(planCard);
        panel.Children.Add(accountPlanGroup);

        // Debounce timer for automatic checking when key is entered / pasted
        checkDebounceTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(700) };
        checkDebounceTimer.Tick += async (_, _) =>
        {
            checkDebounceTimer.Stop();
            await CheckApiKeyAndPlanAsync();
        };

        key.PasswordChanged += (_, _) =>
        {
            draft["apiKey"] = key.Password;
            var hasKey = !string.IsNullOrWhiteSpace(key.Password);
            if (checkPlanButton != null) checkPlanButton.IsEnabled = hasKey;

            if (!hasKey)
            {
                checkDebounceTimer.Stop();
                draft["apiKeyValid"] = false;
                draft["apiPlan"] = "none";
                availableModels.Clear();
                ApplyGeminiModelChoices();
                UpdatePlanDisplay(null, "", 0);
            }
            else
            {
                checkDebounceTimer.Stop();
                checkDebounceTimer.Start();
            }
        };

        // Local video group matching Mac
        modelSelectors.Clear();
        var local = AddGeminiGroup(panel, "Local video");
        AddModelSelector(local, "transcript", "Speech transcription");
        AddModelSelector(local, "summary", "Video summaries");
        AddModelSelector(local, "audio_descriptions", "Audio descriptions");
        AddModelSelector(local, "screen_text", "On-screen text");
        AddModelSelector(local, "text_rewrite", "Description rewrites");
        AddModelSelector(local, "tts_audio", "Gemini narration");

        // YouTube video group matching Mac
        var youtube = AddGeminiGroup(panel, "YouTube video");
        AddModelSelector(youtube, "youtube_transcript", "YouTube transcription");
        AddGeminiNote(panel, "YouTube transcription uses a video model such as Gemini Flash.");

        // Advanced group
        var advanced = new StackPanel { Spacing = 12 };
        AddText(advanced, "API version", "apiVersion", "v1beta");
        AddChoice(advanced, "Video processing", "videoProcessing", new[] { "agentic", "standard" });
        var latest = new Button { Content = "Use latest available models" };
        latest.Click += (_, _) => SelectLatestModels();
        advanced.Children.Add(latest);
        panel.Children.Add(new Expander { Header = "Advanced", Content = advanced, HorizontalAlignment = HorizontalAlignment.Stretch });

        // Initialize status card based on initial key
        var initialHasKey = !string.IsNullOrWhiteSpace(key.Password);
        if (initialHasKey)
        {
            var isInvalid = draft["apiKeyValid"]?.GetValue<bool>() == false || draft["apiPlan"]?.GetValue<string>() == "invalid";
            if (isInvalid)
            {
                UpdatePlanDisplay(false, "invalid", 0, "The API key is invalid or was rejected by Google.");
            }
            else
            {
                var savedPlan = draft["apiPlan"]?.GetValue<string>()?.ToLowerInvariant() ?? "free";
                UpdatePlanDisplay(true, savedPlan, availableModels.Count);
            }
            _ = CheckApiKeyAndPlanAsync();
        }
        else
        {
            UpdatePlanDisplay(null, "", 0);
        }
    }

    private void UpdatePlanDisplay(bool? valid, string plan, int modelCount, string errorMsg = "")
    {
        var isPaid = plan.Equals("paid", StringComparison.OrdinalIgnoreCase);

        if (valid == true)
        {
            var planName = isPaid ? "Paid Plan" : "Free Plan";
            apiStatusCard.Visibility = Visibility.Visible;
            apiStatusCard.Background = new SolidColorBrush(Color.FromArgb(28, 16, 124, 65));
            apiStatusCard.BorderBrush = new SolidColorBrush(Color.FromArgb(120, 16, 124, 65));
            apiStatusCard.BorderThickness = new Thickness(1);
            apiStatusIcon.Glyph = "\uE73E"; // Checkmark icon
            apiStatusIcon.Foreground = new SolidColorBrush(Color.FromArgb(255, 16, 124, 65));
            apiStatusTitle.Text = $"API Key Verified ({planName})";
            apiStatusTitle.Foreground = new SolidColorBrush(Color.FromArgb(255, 16, 124, 65));
            var countLabel = modelCount > 0 ? $"{modelCount} models available." : "Models loaded.";
            apiStatusDescription.Text = $"Key verified ({planName}). {countLabel}";

            // Account Plan pill:
            var pillColor = isPaid ? Color.FromArgb(255, 0, 120, 212) : Color.FromArgb(255, 16, 124, 65);
            planPillBadge.Background = new SolidColorBrush(Color.FromArgb(35, pillColor.R, pillColor.G, pillColor.B));
            planPillBadge.BorderBrush = new SolidColorBrush(Color.FromArgb(120, pillColor.R, pillColor.G, pillColor.B));
            planPillBadge.BorderThickness = new Thickness(1);
            planDot.Glyph = "\uE915"; // small solid circle
            planDot.Foreground = new SolidColorBrush(pillColor);
            planPillText.Text = planName;
            planPillText.Foreground = new SolidColorBrush(pillColor);

            planDetailsSubtitle.Text = isPaid
                ? "Paid plan: higher limits and enterprise data privacy."
                : "Free plan: standard limits and quota.";
        }
        else if (valid == false)
        {
            apiStatusCard.Visibility = Visibility.Visible;
            apiStatusCard.Background = new SolidColorBrush(Color.FromArgb(32, 230, 80, 40));
            apiStatusCard.BorderBrush = new SolidColorBrush(Color.FromArgb(120, 230, 80, 40));
            apiStatusCard.BorderThickness = new Thickness(1);
            apiStatusIcon.Glyph = "\uE783";
            apiStatusIcon.Foreground = new SolidColorBrush(Color.FromArgb(255, 230, 80, 40));
            apiStatusTitle.Text = "API Key Verification Failed";
            apiStatusTitle.Foreground = new SolidColorBrush(Color.FromArgb(255, 230, 80, 40));
            apiStatusDescription.Text = string.IsNullOrEmpty(errorMsg) ? "The API key is invalid or was rejected by Google." : errorMsg;

            // Account Plan pill:
            planPillBadge.Background = new SolidColorBrush(Color.FromArgb(30, 230, 80, 40));
            planPillBadge.BorderBrush = new SolidColorBrush(Color.FromArgb(80, 230, 80, 40));
            planPillBadge.BorderThickness = new Thickness(1);
            planDot.Glyph = "\uE915";
            planDot.Foreground = new SolidColorBrush(Color.FromArgb(255, 230, 80, 40));
            planPillText.Text = "Invalid key";
            planPillText.Foreground = new SolidColorBrush(Color.FromArgb(255, 230, 80, 40));
            planDetailsSubtitle.Text = "Enter a valid API key to enable AI features.";
        }
        else
        {
            // Empty / checking
            var hasKey = !string.IsNullOrEmpty(draft["apiKey"]?.GetValue<string>());
            if (hasKey)
            {
                apiStatusCard.Visibility = Visibility.Visible;
                apiStatusCard.Background = new SolidColorBrush(Color.FromArgb(22, 128, 128, 128));
                apiStatusCard.BorderBrush = new SolidColorBrush(Color.FromArgb(80, 128, 128, 128));
                apiStatusCard.BorderThickness = new Thickness(1);
                apiStatusIcon.Glyph = "\uE72C";
                apiStatusIcon.Foreground = (Brush)Application.Current.Resources["AccentTextFillColorPrimaryBrush"];
                apiStatusTitle.Text = "Checking API key…";
                apiStatusTitle.Foreground = (Brush)Application.Current.Resources["TextFillColorPrimaryBrush"];
                apiStatusDescription.Text = "Verifying key and plan tier…";
            }
            else
            {
                apiStatusCard.Visibility = Visibility.Collapsed;
            }

            planPillBadge.Background = new SolidColorBrush(Color.FromArgb(30, 128, 128, 128));
            planPillBadge.BorderBrush = new SolidColorBrush(Color.FromArgb(80, 128, 128, 128));
            planPillBadge.BorderThickness = new Thickness(1);
            planDot.Glyph = "\uE915";
            planDot.Foreground = new SolidColorBrush(Color.FromArgb(255, 160, 160, 160));
            planPillText.Text = "No API key";
            planPillText.Foreground = new SolidColorBrush(Color.FromArgb(255, 180, 180, 180));
            planDetailsSubtitle.Text = "Enter a Gemini API key to detect your plan.";
        }
    }

    private async Task CheckApiKeyAndPlanAsync()
    {
        if (closed) return;
        var enteredKey = draft["apiKey"]?.GetValue<string>() ?? "";
        if (string.IsNullOrWhiteSpace(enteredKey))
        {
            status.Text = "Enter a Gemini API key first.";
            draft["apiKeyValid"] = false;
            draft["apiPlan"] = "none";
            UpdatePlanDisplay(null, "", 0);
            return;
        }

        try
        {
            if (checkPlanButton != null) checkPlanButton.IsEnabled = false;
            status.Text = "Checking Gemini API key and verifying plan with Google…";

            List<Services.EngineEvent> events;
            try
            {
                events = await QueryGeminiCatalogAsync("check-api-key", lifetime.Token);
            }
            catch
            {
                events = await QueryGeminiCatalogAsync("list-models", lifetime.Token);
            }

            if (closed) return;
            var found = events.LastOrDefault(item => item.Data.TryGetProperty("models", out _) || item.Data.TryGetProperty("keyValid", out _));
            if (found == null) throw new InvalidDataException("Gemini returned no response for this key.");

            if (found.Data.TryGetProperty("keyValid", out var kvElem) && kvElem.GetBoolean() == false)
            {
                var errMsg = found.Data.TryGetProperty("error", out var eElem) ? eElem.GetString() ?? "" : "";
                if (string.IsNullOrEmpty(errMsg)) errMsg = "The API key is invalid or was rejected by Google.";
                draft["apiKeyValid"] = false;
                draft["apiPlan"] = "invalid";
                availableModels.Clear();
                ApplyGeminiModelChoices();
                UpdatePlanDisplay(false, "invalid", 0, errMsg);
                status.Text = "API key check failed: " + errMsg;
                return;
            }

            availableModels.Clear();
            var hasPro = false;
            if (found.Data.TryGetProperty("models", out var modelsElem) && modelsElem.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in modelsElem.EnumerateArray())
                {
                    var id = item.GetProperty("name").GetString();
                    if (!string.IsNullOrWhiteSpace(id))
                    {
                        var rawName = item.GetProperty("displayName").GetString() ?? id;
                        availableModels.Add(new GeminiModelOption(id, CleanModelName(id, rawName)));
                        if (id.Contains("pro", StringComparison.OrdinalIgnoreCase)) hasPro = true;
                    }
                }
            }

            if (found.Data.TryGetProperty("detectedPlan", out var dpElem))
                detectedPlan = dpElem.GetString() ?? (hasPro ? "paid" : "free");
            else if (found.Data.TryGetProperty("plan", out var pElem))
                detectedPlan = pElem.GetString() ?? (hasPro ? "paid" : "free");
            else
                detectedPlan = hasPro ? "paid" : "free";

            draft["apiKeyValid"] = true;
            draft["apiPlan"] = detectedPlan;

            // Automatically select values based on the detected plan without requiring user to choose them
            if (found.Data.TryGetProperty("selectedModels", out var smElem) && smElem.ValueKind == JsonValueKind.Object)
            {
                var draftModels = draft["models"] as JsonObject ?? new JsonObject();
                foreach (var prop in smElem.EnumerateObject())
                {
                    draftModels[prop.Name] = prop.Value.GetString();
                }
                draft["models"] = draftModels;
            }
            if (found.Data.TryGetProperty("selectedVideoProcessing", out var vpElem))
            {
                draft["videoProcessing"] = vpElem.GetString();
            }

            ApplyGeminiModelChoices();

            var isPaid = detectedPlan.Equals("paid", StringComparison.OrdinalIgnoreCase);
            var planLabel = isPaid ? "Paid Plan" : "Free Plan";

            UpdatePlanDisplay(true, detectedPlan, availableModels.Count);
            status.Text = $"Gemini API key verified. Plan: {planLabel}. {availableModels.Count} models available.";
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            model.Engine.Log(ex.ToString());
            draft["apiKeyValid"] = false;
            draft["apiPlan"] = "invalid";
            availableModels.Clear();
            ApplyGeminiModelChoices();
            var exMsg = ex.ToString();
            var isInvalid = exMsg.Contains("API_KEY_INVALID", StringComparison.OrdinalIgnoreCase)
                || exMsg.Contains("API key not valid", StringComparison.OrdinalIgnoreCase)
                || exMsg.Contains("INVALID_ARGUMENT", StringComparison.OrdinalIgnoreCase)
                || exMsg.Contains("PERMISSION_DENIED", StringComparison.OrdinalIgnoreCase)
                || exMsg.Contains("UNAUTHENTICATED", StringComparison.OrdinalIgnoreCase)
                || exMsg.Contains("invalid", StringComparison.OrdinalIgnoreCase)
                || exMsg.Contains("API key", StringComparison.OrdinalIgnoreCase);
            var msg = isInvalid ? "The API key is invalid or was rejected by Google." : "Could not reach Gemini. Check the key and connection.";
            UpdatePlanDisplay(false, "invalid", 0, msg);
            status.Text = "API key check failed: " + msg;
        }
        finally
        {
            if (checkPlanButton != null) checkPlanButton.IsEnabled = !string.IsNullOrWhiteSpace(draft["apiKey"]?.GetValue<string>());
        }
    }

    private static void AddGeminiNote(StackPanel panel, string text) =>
        panel.Children.Add(new TextBlock { Text = text, TextWrapping = TextWrapping.Wrap, FontSize = 12, Opacity = 0.75, Margin = new Thickness(0, 4, 0, 0) });

    private static StackPanel AddGeminiGroup(StackPanel panel, string title)
    {
        panel.Children.Add(new TextBlock { Text = title, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold, FontSize = 14, Margin = new Thickness(0, 16, 0, 8) });
        var rows = new StackPanel { Spacing = 10 };
        panel.Children.Add(new Border
        {
            CornerRadius = new CornerRadius(10), Padding = new Thickness(16, 12, 16, 12), Child = rows,
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

    private void AddModelSelector(StackPanel panel, string stage, string label)
    {
        var row = new Grid { ColumnSpacing = 16, MinHeight = 38 };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.3, GridUnitType.Star) });
        row.Children.Add(new TextBlock { Text = label, VerticalAlignment = VerticalAlignment.Center, FontSize = 14 });
        var selector = new ComboBox { HorizontalAlignment = HorizontalAlignment.Stretch };
        modelSelectors[stage] = selector;
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
            var hasPro = false;
            foreach (var item in found.Data.GetProperty("models").EnumerateArray())
            {
                var id = item.GetProperty("name").GetString();
                if (!string.IsNullOrWhiteSpace(id))
                {
                    var rawName = item.GetProperty("displayName").GetString() ?? id;
                    availableModels.Add(new GeminiModelOption(id, CleanModelName(id, rawName)));
                    if (id.Contains("pro", StringComparison.OrdinalIgnoreCase)) hasPro = true;
                }
            }
            if (found.Data.TryGetProperty("detectedPlan", out var dpElem))
                detectedPlan = dpElem.GetString() ?? (hasPro ? "paid" : "free");
            else
                detectedPlan = hasPro ? "paid" : "free";

            draft["apiPlan"] = detectedPlan;
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
                availableModels.Add(new GeminiModelOption(id, CleanModelName(id, id)));
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
                throw new InvalidOperationException("Missing transcription option did not fall back.");
        }
        finally
        {
            availableModels.Clear();
            availableModels.AddRange(originalCatalog);
            draft["models"] = originalModels;
            ApplyGeminiModelChoices();
            status.Text = originalStatus;
        }
    }
#endif
}
