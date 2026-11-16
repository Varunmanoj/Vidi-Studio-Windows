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
    private readonly List<(string Stage, ComboBox Selector)> modelSelectorRows = new();
    private bool synchronizingModelSelection;

    private TextBlock apiKeyStatusText = new();
    private FontIcon? apiStatusIcon;

    private Border planPillBadge = new();
    private FontIcon planDot = new();
    private TextBlock planPillText = new();
    private TextBlock planDetailsSubtitle = new();

    private Button? checkPlanButton;
    private string detectedPlan = "free";
    private DispatcherTimer? checkDebounceTimer;
    private long apiKeyCheckRevision;
    private bool checkingApiKey;

#if DEBUG
    internal void BringGeminiIntoViewForSmokeTest(bool models)
    {
        if (models) modelSelectorRows.First().Selector.StartBringIntoView(new BringIntoViewOptions { VerticalAlignmentRatio = 0 });
        else sectionHost.ChangeView(null,0,null,true);
    }
    internal void VerifyGeminiGroupsForSmokeTest()
    {
        layout.UpdateLayout();
        var cards = SpeechItems<Border>(currentSectionPanel!).Where(b => Microsoft.UI.Xaml.Automation.AutomationProperties.GetAutomationId(b).StartsWith("gemini-section-",StringComparison.Ordinal)).ToArray();
        if (cards.Length != 6) throw new InvalidOperationException("Gemini sections must use six compact grouped cards.");
        foreach (var card in cards) {
            if (card.Background is not SolidColorBrush brush) throw new InvalidOperationException("Gemini cards must use their theme fill.");
            var c = brush.Color;
            if (((FrameworkElement)Content).ActualTheme == ElementTheme.Dark && (c.R+c.G+c.B)/3.0*c.A/255.0 + 32*(1-c.A/255.0) > 120)
                throw new InvalidOperationException("Gemini cards must follow dark mode.");
        }
        var language = SpeechItems<ComboBox>(currentSectionPanel!).Single(c => Microsoft.UI.Xaml.Automation.AutomationProperties.GetAutomationId(c) == "gemini-output-language");
        foreach (var field in modelSelectorRows.Select(r => r.Selector).Append(language)) {
            if (field.Header != null || field.Parent is not Grid row) throw new InvalidOperationException("Gemini labels must share aligned rows with their fields.");
            var label = row.Children.OfType<TextBlock>().First();
            var l = label.TransformToVisual(row).TransformPoint(new Windows.Foundation.Point());
            var f = field.TransformToVisual(row).TransformPoint(new Windows.Foundation.Point());
            if (row.ActualWidth >= 480 && (f.X <= l.X || System.Math.Abs((l.Y+label.ActualHeight/2)-(f.Y+field.ActualHeight/2)) > 2))
                throw new InvalidOperationException("Gemini labels and selectors must appear beside one another.");
        }
        var actions = SpeechItems<CompactActionsPanel>(currentSectionPanel!).Single();
        if (actions.Children.OfType<Button>().Count() != 2) throw new InvalidOperationException("Gemini key actions must use matching native buttons.");
        var sizes = actions.Children.Cast<FrameworkElement>().Select(b => b.ActualHeight).ToArray();
        if (System.Math.Abs(sizes[0]-sizes[1]) > 2) throw new InvalidOperationException("Gemini key buttons must have matching heights.");
    }
    internal async Task VerifyGeminiKeyLayoutForSmokeTestAsync(Func<Task> capture)
    {
        NavigateForSmokeTest(3); await Task.Delay(150); layout.UpdateLayout();
        var key = SpeechItems<PasswordBox>(currentSectionPanel!).Single();
        var row = key.Parent as Grid ?? throw new InvalidOperationException("Gemini key must use an inline grid.");
        var icon = row.Children.OfType<FontIcon>().Single();
        var trash = row.Children.OfType<Button>().Single();
        if (Grid.GetColumn(icon) != 1 || Grid.GetColumn(trash) != 2 ||
            Microsoft.UI.Xaml.Automation.AutomationProperties.GetAutomationId(icon) != "gemini-key-status" ||
            string.IsNullOrWhiteSpace(Microsoft.UI.Xaml.Automation.AutomationProperties.GetName(icon)))
            throw new InvalidOperationException("Gemini key status must be accessible between the key and trash.");
        var actions = SpeechItems<Panel>(currentSectionPanel!).Single(p => Microsoft.UI.Xaml.Automation.AutomationProperties.GetAutomationId(p) == "gemini-key-actions");
        VerifyKeyActionsForSmokeTest(actions);
        var probe = new TextBlock(); var probeIcon = ApiKeyStatusIcon(probe,"gemini-key-status");
        foreach (var (text,glyph) in new[] { ("API key verified. Paid Plan. 37 models available.","\uE73E"), ("API key rejected. Google rejected this key.","\uE711"), ("Checking API key…","\uE823"), ("API key check incomplete. Connection unavailable.","\uE7BA"), ("No API key entered","\uE7BA") }) {
            probe.Text = text;
            if (probeIcon.Glyph != glyph || Microsoft.UI.Xaml.Automation.AutomationProperties.GetName(probeIcon) != text || ToolTipService.GetToolTip(probeIcon) as string != text)
                throw new InvalidOperationException("Gemini status must retain its full accessible state.");
            if (glyph == "\uE73E" && (probeIcon.Foreground is not SolidColorBrush green || green.Color.G <= green.Color.R))
                throw new InvalidOperationException("Verified Gemini keys must show a green checkmark.");
        }
        sectionHost.ChangeView(null,0,null,true); await Task.Delay(150); await capture();
    }
#endif

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
        ConnectivityService.Shared.Require(OnlineService.Gemini);
        Directory.CreateDirectory(SettingsStore.DirectoryPath);
        var config = Path.Combine(SettingsStore.DirectoryPath, Guid.NewGuid() + ".json");
        try
        {
            var configData = draft.DeepClone().AsObject();
            var enteredKey = SettingsStore.GetString(draft["apiKey"]).Trim();
            if (!string.IsNullOrEmpty(enteredKey)) configData["apiKey"] = enteredKey;
            await File.WriteAllTextAsync(config, configData.ToJsonString(Models.Json.Options), cancellation);
            return await model.Engine.RunAsync(new[] { command }, configPath: config, cancellation: cancellation,
                apiKey: enteredKey);
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

    private static void DetachFromVisualParent(FrameworkElement? element)
    {
        if (element == null) return;
        if (element.Parent is Panel panel)
        {
            panel.Children.Remove(element);
        }
        else if (element.Parent is ContentControl cc)
        {
            cc.Content = null;
        }
        else if (element.Parent is Border b)
        {
            b.Child = null;
        }
    }

    private void BuildOutputLanguages(StackPanel panel)
    {
        var group = AddGeminiGroup(panel, "Language");
        var legacy = draft["outputLanguages"] as JsonObject;
        var existing = new[] { "transcript", "summary", "audio_descriptions" }.Select(k => SettingsStore.GetString(legacy?[k], "auto")).Distinct().ToList();
        var selected = SettingsStore.GetString(draft["defaultOutputLanguage"], existing.Count == 1 ? existing[0] : "auto");
        var field = new ComboBox { Header = "Default Language for New Projects", DisplayMemberPath = "Title", ItemsSource = Vidi_Studio_Windows.Models.OutputLanguage.Options };
        field.SelectedItem = Vidi_Studio_Windows.Models.OutputLanguage.Options.FirstOrDefault(v => v.Id == selected);
        field.SelectionChanged += (_, _) => {
            if (field.SelectedItem is Vidi_Studio_Windows.Models.OutputLanguage choice) {
                draft["defaultOutputLanguage"] = choice.Id;
            }
        };
        field.Header = null; field.Width = 280;
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetAutomationId(field,"gemini-output-language");
        AddGeminiRow(group,"Default Language for New Projects",field);
    }

    private void BuildGemini(StackPanel panel)
    {
        // These display-only controls belong to the newly constructed page.
        // Unloaded WinUI controls can no longer expose Parent for detachment.
        planPillBadge = new Border(); planDot = new FontIcon();
        planPillText = new TextBlock(); planDetailsSubtitle = new TextBlock();

        var titleRows = new StackPanel();
        titleRows.Children.Add(ScreenReaderHeading.Mark(new TextBlock { Text = "Gemini", FontSize = 20, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold }));
        var titleCard = GeminiSettingsCard(titleRows);
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetAutomationId(titleCard,"gemini-title-card");
        panel.Children.Add(titleCard);

        BuildOutputLanguages(panel);

        var topStack = AddGeminiGroup(panel,"Connect to Gemini");

        var keyRow = new Grid { ColumnSpacing = 8 };
        keyRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        keyRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        keyRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var key = new PasswordBox
        {
            Header = "Personal Gemini API key",
            Password = SettingsStore.GetString(draft["apiKey"]),
            HorizontalAlignment = HorizontalAlignment.Stretch
        };
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetAutomationId(key,"gemini-api-key");
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(key,"Personal Gemini API key");
        key.Header = null;
        Grid.SetColumn(key, 0);
        keyRow.Children.Add(key);

        var deleteKeyButton = new Button
        {
            Content = new FontIcon { Glyph = "\uE74D", FontSize = 14 },
            VerticalAlignment = VerticalAlignment.Bottom,
            MinWidth = 36,
            Padding = new Thickness(8, 6, 8, 6),
            IsEnabled = !string.IsNullOrWhiteSpace(key.Password),
            Visibility = !string.IsNullOrWhiteSpace(key.Password) ? Visibility.Visible : Visibility.Collapsed
        };
        ToolTipService.SetToolTip(deleteKeyButton, "Clear and delete API key from Windows Credential Manager");
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(deleteKeyButton, "Delete Gemini API key");
        deleteKeyButton.Click += async (_, _) =>
        {
            if (string.IsNullOrWhiteSpace(key.Password)) return;
            var confirmDialog = new ContentDialog
            {
                Title = "Delete Gemini API Key?",
                Content = new TextBlock
                {
                    Text = "Are you sure you want to delete your Gemini API key from Windows Credential Manager? You will need to enter a new key to use AI features.",
                    TextWrapping = TextWrapping.Wrap,
                    MaxWidth = 480
                },
                PrimaryButtonText = "Delete API key",
                CloseButtonText = "Cancel",
                DefaultButton = ContentDialogButton.Close
            };

            var confirmResult = await WindowedDialog.ShowAsync(confirmDialog, this, width: 500, height: 240);
            if (confirmResult != ContentDialogResult.Primary || string.IsNullOrWhiteSpace(key.Password)) return;

            checkDebounceTimer?.Stop();
            key.Password = "";
            draft["apiKey"] = "";
            draft.Remove("apiKeyValid");
            draft["apiPlan"] = "none";
            if (!ApplyPreferences()) return;
            model.ClearAiIssue();
            availableModels.Clear();
            ApplyGeminiModelChoices();
            UpdatePlanDisplay(null, "", 0);
            deleteKeyButton.IsEnabled = false;
            deleteKeyButton.Visibility = Visibility.Collapsed;
            checkingApiKey = false;
            ConnectivityChanged(null, EventArgs.Empty);
            status.Text = "Gemini API key deleted from Windows Credential Manager.";
        };
        apiKeyStatusText = new TextBlock();
        apiStatusIcon = ApiKeyStatusIcon(apiKeyStatusText,"gemini-key-status");
        Grid.SetColumn(apiStatusIcon,1); keyRow.Children.Add(apiStatusIcon);
        Grid.SetColumn(deleteKeyButton, 2);
        keyRow.Children.Add(deleteKeyButton);

        Microsoft.UI.Xaml.Automation.AutomationProperties.SetAutomationId(deleteKeyButton,"gemini-key-delete-icon");
        AddGeminiRow(topStack,"API key",keyRow,true);
        var openKeysLink = new Button
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
            Padding = new Thickness(12, 6, 12, 6)
        };
        openKeysLink.Click += async (_,_) => await Windows.System.Launcher.LaunchUriAsync(new Uri("https://aistudio.google.com/api-keys"));
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(openKeysLink,"Open API Keys in Google AI Studio");
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetAutomationId(openKeysLink,"gemini-key-dashboard");
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
            Padding = new Thickness(12, 6, 12, 6),
            IsEnabled = true
        };
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(checkPlanButton,"Check API Key & Reload Models");
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetAutomationId(checkPlanButton,"gemini-key-check");
        checkPlanButton.Click += async (_, _) => await CheckApiKeyAndPlanAsync();
        topStack.Children.Add(checkPlanButton);

        SpeechActionsRow(topStack,"gemini-key-actions",checkPlanButton,openKeysLink);
        topStack.Children.Insert(1,GeminiRowDivider());
        IconButtonContent.SetDestructive(deleteKeyButton,true);

        var accountPlanGroup = new StackPanel();
        var planCardStack = AddGeminiGroup(accountPlanGroup,"Account Plan");
        accountPlanGroup.Visibility = string.IsNullOrWhiteSpace(key.Password) ? Visibility.Collapsed : Visibility.Visible;
        panel.Children.Add(accountPlanGroup);

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
        AddGeminiRow(planCardStack,"Plan Tier",planPillBadge);
        planDetailsSubtitle.FontSize = 12;
        planDetailsSubtitle.Opacity = 0.75;
        planDetailsSubtitle.TextWrapping = TextWrapping.Wrap;
        planCardStack.Children.Add(GeminiRowDivider());
        planCardStack.Children.Add(planDetailsSubtitle);

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
            accountPlanGroup.Visibility = hasKey ? Visibility.Visible : Visibility.Collapsed;
            apiKeyCheckRevision++; // Ignore results for the previously entered key.
            checkingApiKey = false;
            draft.Remove("apiKeyValid");
            draft["apiPlan"] = "auto";
            availableModels.Clear();
            ApplyGeminiModelChoices();
            UpdatePlanDisplay(null, "", 0);
            ConnectivityChanged(null, EventArgs.Empty);
            deleteKeyButton.IsEnabled = hasKey;
            deleteKeyButton.Visibility = hasKey ? Visibility.Visible : Visibility.Collapsed;

            if (!hasKey)
            {
                status.Text = "No API key entered. Enter a Gemini API key, then choose Check API Key & Plan.";
                checkDebounceTimer.Stop();
                draft.Remove("apiKeyValid");
                draft["apiPlan"] = "none";
                availableModels.Clear();
                ApplyGeminiModelChoices();
                UpdatePlanDisplay(null, "", 0);
            }
            else
            {
                status.Text = "API key not checked. Choose Check API Key & Plan to verify the entered key.";
                checkDebounceTimer.Stop();
                checkDebounceTimer.Start();
            }
        };

        // Local video group matching Mac
        modelSelectors.Clear();
        modelSelectorRows.Clear();
        var local = AddGeminiGroup(panel, "Local video");
        AddModelSelector(local, "transcript", "Speech transcription");
        AddModelSelector(local, "summary", "Video summaries");
        AddModelSelector(local, "audio_descriptions", "Audio descriptions");
        AddModelSelector(local, "screen_text", "On-screen text");
        AddModelSelector(local, "text_rewrite", "Description rewrites");
        AddModelSelector(local, "tts_audio", "Gemini narration");

        // YouTube video group matching Mac
        var youtube = AddGeminiGroup(panel, "YouTube video");
        AddModelSelector(youtube, "youtube_transcript", "YouTube video model");
        AddModelSelector(youtube, "transcript", "Imported audio transcription");
        AddModelSelector(youtube, "audio_descriptions", "Audio descriptions with imported audio");

        // Advanced group
        var advanced = new StackPanel { Spacing = 0 };
        var version = new TextBox { Text = SettingsStore.GetString(draft["apiVersion"],"v1beta"), Width = 280 };
        version.TextChanged += (_,_) => draft["apiVersion"] = version.Text;
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetAutomationId(version,"gemini-api-version");
        AddGeminiRow(advanced,"API version",version,true);
        var processing = new ComboBox
        {
            Header = "Video processing",
            ItemsSource = new[] { "Agentic", "Static" },
            SelectedIndex = SettingsStore.GetString(draft["videoProcessing"], "agentic") == "static" ? 1 : 0
        };
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(processing, "Video processing");
        processing.SelectionChanged += (_, _) =>
        {
            if (processing.SelectedIndex >= 0) draft["videoProcessing"] = processing.SelectedIndex == 0 ? "agentic" : "static";
        };
        processing.Header = null; processing.Width = 280;
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetAutomationId(processing,"gemini-video-processing");
        AddGeminiRow(advanced,"Video processing",processing);
        var latest = new Button { Content = "Use latest available models" };
        latest.Click += (_, _) => SelectLatestModels();
        latest.Padding = new Thickness(12,6,12,6);
        advanced.Children.Add(GeminiRowDivider()); advanced.Children.Add(latest);
        var advancedRows = new StackPanel();
        advancedRows.Children.Add(new Expander { Header = "Advanced", Content = advanced, HorizontalAlignment = HorizontalAlignment.Stretch });
        var advancedCard = GeminiSettingsCard(advancedRows);
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetAutomationId(advancedCard,"gemini-section-advanced");
        panel.Children.Add(advancedCard);

        // Initialize status card based on initial key
        var initialHasKey = !string.IsNullOrWhiteSpace(key.Password);
        if (initialHasKey)
        {
            var isInvalid = !SettingsStore.GetBool(draft["apiKeyValid"], true) || SettingsStore.GetString(draft["apiPlan"]) == "invalid";
            if (isInvalid)
            {
                UpdatePlanDisplay(false, "invalid", 0, "The API key is invalid or was rejected by Google.");
            }
            else
            {
                var savedPlan = SettingsStore.GetString(draft["apiPlan"], "free").ToLowerInvariant();
                UpdatePlanDisplay(draft["apiKeyValid"] == null ? null : true, savedPlan, availableModels.Count);
            }
            _ = CheckApiKeyAndPlanAsync();
        }
        else
        {
            UpdatePlanDisplay(null, "", 0);
        }
    }

    private void UpdatePlanDisplay(bool? valid, string plan, int modelCount, string errorMsg = "", bool checking = false)
    {
        var isPaid = plan.Equals("paid", StringComparison.OrdinalIgnoreCase);

        if (valid == true)
        {
            var planName = isPaid ? "Paid Plan" : "Free Plan";
            apiKeyStatusText.Text = $"API key verified. {planName}. " + (modelCount > 0 ? $"{modelCount} models available." : "Models loaded.");

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
            apiKeyStatusText.Text = "API key rejected. " + (errorMsg.Length > 0 ? errorMsg : "Google rejected this key.");

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
            var hasKey = !string.IsNullOrWhiteSpace(SettingsStore.GetString(draft["apiKey"]));
            apiKeyStatusText.Text = !hasKey ? "No API key entered" : checking ? "Checking API key…" : errorMsg.Length > 0 ? "API key check incomplete. " + errorMsg : "API key saved — unverified";

            planPillBadge.Background = new SolidColorBrush(Color.FromArgb(30, 128, 128, 128));
            planPillBadge.BorderBrush = new SolidColorBrush(Color.FromArgb(80, 128, 128, 128));
            planPillBadge.BorderThickness = new Thickness(1);
            planDot.Glyph = "\uE915";
            planDot.Foreground = new SolidColorBrush(Color.FromArgb(255, 160, 160, 160));
            planPillText.Text = !hasKey ? "No API key" : checking ? "Checking…" : errorMsg.Length > 0 ? "Check incomplete" : "Not Checked";
            planPillText.Foreground = new SolidColorBrush(Color.FromArgb(255, 180, 180, 180));
            planDetailsSubtitle.Text = !hasKey ? "Enter a Gemini API key to detect your plan." : "Check the entered API key before showing account information.";
        }
    }

    private async Task CheckApiKeyAndPlanAsync()
    {
        if (closed) return;
        checkDebounceTimer?.Stop();
        var revision = ++apiKeyCheckRevision;
        var enteredKey = SettingsStore.GetString(draft["apiKey"]).Trim();
        if (string.IsNullOrWhiteSpace(enteredKey))
        {
            status.Text = "No API key entered. Enter a Gemini API key, then choose Check API Key & Plan.";
            draft.Remove("apiKeyValid");
            draft["apiPlan"] = "none";
            UpdatePlanDisplay(null, "", 0);
            ConnectivityChanged(null, EventArgs.Empty);
            return;
        }
        if (!ConnectivityService.Shared.Available(OnlineService.Gemini))
        {
            var reason = "The key could not be checked because Gemini is unavailable. Reconnect and try again. Previous verification and models have been kept.";
            status.Text = reason;
            UpdatePlanDisplay(null, "", 0, reason);
            return;
        }

        try
        {
            checkingApiKey = true;
            if (checkPlanButton != null) checkPlanButton.IsEnabled = false;
            UpdatePlanDisplay(null, "", 0, checking: true);
            status.Text = "Checking Gemini API key and verifying plan with Google…";

            var events = await QueryGeminiCatalogAsync("check-api-key", lifetime.Token);
            if (closed || revision != apiKeyCheckRevision || enteredKey != SettingsStore.GetString(draft["apiKey"]).Trim()) return;
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
                status.Text = "The entered Gemini API key is invalid. Google rejected it. Replace it with a valid key from Google AI Studio.";
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
            model.ClearAiIssue();

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
            if (closed || revision != apiKeyCheckRevision || enteredKey != SettingsStore.GetString(draft["apiKey"]).Trim()) return;
            model.Engine.Log(ex.ToString());
            var detail = ex.ToString();
            var invalid = detail.Contains("API_KEY_INVALID", StringComparison.OrdinalIgnoreCase)
                || detail.Contains("API key not valid", StringComparison.OrdinalIgnoreCase)
                || detail.Contains("invalid API key", StringComparison.OrdinalIgnoreCase);
            if (invalid)
            {
                draft["apiKeyValid"] = false; draft["apiPlan"] = "invalid";
                availableModels.Clear(); ApplyGeminiModelChoices();
                UpdatePlanDisplay(false, "invalid", 0, "The API key is invalid or was rejected by Google.");
            }
            if (invalid)
                status.Text = "The entered Gemini API key is invalid. Google rejected it. Replace it with a valid key from Google AI Studio.";
            else
            {
                var reason = detail.Contains("429") || detail.Contains("quota", StringComparison.OrdinalIgnoreCase)
                    ? "Gemini could not complete the key check because of a usage limit. Try again after the limit resets."
                    : detail.Contains("403") || detail.Contains("permission", StringComparison.OrdinalIgnoreCase)
                    ? "Gemini refused access. Check the key's project permissions and restrictions in Google AI Studio."
                    : "The API key check could not finish. Check your connection or try again when Gemini is available.";
                status.Text = reason + " This check did not determine whether the key is valid. Previous verification and models have been kept.";
                UpdatePlanDisplay(null, "", 0, status.Text);
            }
        }
        finally
        {
            if (revision == apiKeyCheckRevision)
            {
                checkingApiKey = false;
                ConnectivityChanged(null, EventArgs.Empty);
            }
        }
    }

    private static Border GeminiRowDivider() => (Border)Microsoft.UI.Xaml.Markup.XamlReader.Load("<Border xmlns=\"http://schemas.microsoft.com/winfx/2006/xaml/presentation\" Height=\"1\" Background=\"{ThemeResource DividerStrokeColorDefaultBrush}\" Margin=\"0,6,0,6\"/>");

    private static Border GeminiSettingsCard(StackPanel rows)
    {
        var card = SpeechSettingsCard(rows);
        card.Padding = new Thickness(12,10,12,10); card.Margin = new Thickness(0);
        return card;
    }
    private static Grid AddGeminiRow(StackPanel rows, string label, FrameworkElement control, bool stretch = false)
    {
        if (rows.Children.Count > 0) rows.Children.Add(GeminiRowDivider());
        return AddSettingsRow(rows,label,control,stretch);
    }
    private static StackPanel AddGeminiGroup(StackPanel panel, string title)
    {
        panel.Children.Add(ScreenReaderHeading.Mark(new TextBlock { Text = title, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold, FontSize = 14, Margin = new Thickness(0,12,0,4) }));
        var rows = new StackPanel { Spacing = 0 };
        var card = GeminiSettingsCard(rows);
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetAutomationId(card,"gemini-section-"+title.Replace(" ","-").ToLowerInvariant());
        panel.Children.Add(card);
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
        foreach (var (stage, selector) in modelSelectorRows)
        {
            var current = SettingsStore.GetString(draft["models"]?[stage]);
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
        var selector = new ComboBox { HorizontalAlignment = HorizontalAlignment.Stretch, MinWidth = 0 };
        modelSelectors.TryAdd(stage, selector);
        modelSelectorRows.Add((stage, selector));
        selector.SelectionChanged += (_, _) =>
        {
            if (synchronizingModelSelection) return;
            if (selector.SelectedItem is GeminiModelOption chosen && chosen.Id.Length > 0 && SupportsModelStage(stage, chosen.Id))
            {
                draft["models"]!.AsObject()[stage] = chosen.Id;
                synchronizingModelSelection = true;
                try
                {
                    foreach (var (peerStage, peer) in modelSelectorRows)
                        if (peerStage == stage && peer != selector)
                            peer.SelectedItem = (peer.ItemsSource as IEnumerable<GeminiModelOption>)?.FirstOrDefault(option => option.Id == chosen.Id);
                }
                finally { synchronizingModelSelection = false; }
            }
        };
        AddGeminiRow(panel,label,selector,true);
    }

    private Task RefreshGeminiModelsAsync() => CheckApiKeyAndPlanAsync();

    private void SelectLatestModels()
    {
        if (availableModels.Count == 0) { status.Text = "Load the Gemini catalog first."; return; }
        static (int Major, int Minor, int Patch, int Alias) Rank(string id)
        {
            var version = Regex.Match(id, @"gemini-(\d+)\.(\d+)(?:\.(\d+))?");
            int Parse(Group group) => int.TryParse(group.Value, out var number) ? number : 0;
            return (Parse(version.Groups[1]), Parse(version.Groups[2]), Parse(version.Groups[3]), id.Contains("latest", StringComparison.OrdinalIgnoreCase) ? 1 : 0);
        }
        foreach (var (stage, selector) in modelSelectorRows)
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
            NavigateForSmokeTest(3);
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
            if (SettingsStore.GetString(draft["models"]?["transcript"]) != "gemini-3.5-transcribe")
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
