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
    private readonly List<(VertexModelRow Row, ComboBox Selector)> modelSelectorRows = new();

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
    internal async Task VerifyVertexConnectionForSmokeTestAsync(Func<Task> capture)
    {
        var previousProvider = draft["aiProvider"]?.DeepClone();
        var previousAuth = draft["vertexAuth"]?.DeepClone();
        var previousCatalog = draft["vertexCatalog"]?.DeepClone();
        var previousModels = draft["vertexModels"]?.DeepClone();
        try {
            draft["aiProvider"] = "vertex";
            draft["vertexAuth"] = "signin";
            draft["vertexCatalog"] = new JsonArray("gemini-3.5-transcribe", "gemini-3.5-transcribe-preview", "gemini-3.5-transcribe-live",
                "gemini-3.8-flash", "gemini-3.8-flash-tts", "gemini-3.1-flash-image", "gemini-4-neon");
            draft["vertexModels"] = new JsonObject { ["transcript"] = "gemini-3.8-flash" };
            NavigateForSmokeTest(3);
            RebuildAiPage(currentSectionPanel!);
            await Task.Delay(150);
            layout.UpdateLayout();
            var panel = currentSectionPanel!;
            ComboBox Stage(string stage) => SpeechItems<ComboBox>(panel).Single(c =>
                Microsoft.UI.Xaml.Automation.AutomationProperties.GetAutomationId(c) == "vertex-model-" + stage);
            var transcript = Stage("transcript");
            var speechIds = new[] { "gemini-3.5-transcribe", "gemini-3.5-transcribe-preview" };
            if (!transcript.Items.OfType<ComboBoxItem>().Where(c => c.IsEnabled).Select(c => c.Tag as string).SequenceEqual(speechIds)
                || transcript.SelectedItem is not ComboBoxItem { Tag: "gemini-3.5-transcribe" }
                || transcript.ContextFlyout is not MenuFlyout choices || !choices.Items.OfType<ToggleMenuFlyoutItem>().Select(m => m.Text).SequenceEqual(speechIds))
                throw new InvalidOperationException("File transcription choices, menu or migrated selection differ.");
            transcript.SelectedIndex = 2;
            if (SettingsStore.GetString(draft["vertexModels"]?["transcript"]) != speechIds[1])
                throw new InvalidOperationException("Transcription preview selection was lost.");
            if (Stage("summary").Items.OfType<ComboBoxItem>().Count(c => c.IsEnabled) != 2
                || Stage("tts_audio").Items.OfType<ComboBoxItem>().Count(c => c.IsEnabled) != 1)
                throw new InvalidOperationException("Video or narration picker admitted a different capability.");
            var cloudVideo = Stage("cloud_video");
            var cloudModels = new[] { "gemini-3.8-flash", "gemini-4-neon" };
            if (!cloudVideo.Items.OfType<ComboBoxItem>().Where(c => c.IsEnabled).Select(c => c.Tag as string).SequenceEqual(cloudModels)
                || cloudVideo.ContextFlyout is not MenuFlyout cloudMenu
                || !cloudMenu.Items.OfType<ToggleMenuFlyoutItem>().Select(item => item.Text).SequenceEqual(cloudModels)
                || cloudMenu.Items.OfType<ToggleMenuFlyoutItem>().Any(item => item.Icon == null))
                throw new InvalidOperationException("Cloud Storage needs video-capable choices and an icon-bearing context menu.");
            cloudVideo.SelectedItem = cloudVideo.Items.OfType<ComboBoxItem>().Single(c => c.Tag as string == cloudModels[1]);
            if (SettingsStore.GetString(draft["vertexModels"]?["cloud_video"]) != cloudModels[1] ||
                draft["vertexModels"]?["youtube_transcript"] != null)
                throw new InvalidOperationException("Cloud Storage and YouTube must save independent selections.");
            foreach (var stage in new[] { "summary", "audio_descriptions", "screen_text", "text_rewrite", "tts_audio" })
            {
                var localStage = Stage(stage);
                foreach (var source in new[] { "youtube", "cloud" })
                {
                    var sourceStage = Stage(source + "_" + stage);
                    if (sourceStage.Items.OfType<ComboBoxItem>().Count(c => c.IsEnabled) !=
                        localStage.Items.OfType<ComboBoxItem>().Count(c => c.IsEnabled) ||
                        sourceStage.ContextFlyout is not MenuFlyout)
                        throw new InvalidOperationException("Each source needs capability-filtered task pickers and menus.");
                }
            }
            var account = SpeechItems<TextBlock>(panel).Single(t => Microsoft.UI.Xaml.Automation.AutomationProperties.GetAutomationId(t) == "vertex-signin-status");
            bool connected = CredentialStore.HasGoogleSignIn;
            if (account.Text != (connected ? "Google account connected" : "Signed out"))
                throw new InvalidOperationException("OAuth badge does not match the saved Windows connection.");
            if (connected && account.Parent is StackPanel badge && !badge.Children.OfType<FontIcon>().Any(f => f.Glyph == "\uE73E"))
                throw new InvalidOperationException("Connected OAuth account must show a check.");
            foreach (var label in new[] { "Google Cloud project ID", "Cloud Storage bucket" }) {
                var field = SpeechItems<TextBox>(panel).Single(f => Microsoft.UI.Xaml.Automation.AutomationProperties.GetName(f) == label);
                var parent = (Grid)field.Parent;
                var buttons = parent.Children.OfType<Button>().ToArray();
                var names = buttons.Select(b => Microsoft.UI.Xaml.Automation.AutomationProperties.GetName(b)).ToArray();
                if (!names.SequenceEqual(new[] {"Copy " + label, "Clear " + label}) || buttons.Any(b => b.IsEnabled != (field.Text.Length > 0)))
                    throw new InvalidOperationException("Cloud field actions or availability differ: " + label);
                if (Grid.GetColumn(buttons[0]) != 1 || Grid.GetColumn(buttons[1]) != 2 || parent.ContextFlyout is not MenuFlyout menu || menu.Items.Count != 2)
                    throw new InvalidOperationException("Cloud field actions need adjacent buttons and keyboard menus.");
            }
            FontIcon TestBadge() => SpeechItems<FontIcon>(panel).Single(icon =>
                Microsoft.UI.Xaml.Automation.AutomationProperties.GetAutomationId(icon) == "vertex-test-status");
            if (TestBadge().Visibility != Visibility.Collapsed) throw new InvalidOperationException("Untested Vertex connection shows a success icon");
            RebuildAiPage(panel, vertexVerified: true);
            await Task.Delay(100);
            var verified = TestBadge();
            if (verified.Visibility != Visibility.Visible || verified.Glyph != "\uE73E" ||
                Microsoft.UI.Xaml.Automation.AutomationProperties.GetName(verified) != "Vertex AI connection verified" ||
                verified.Parent is not StackPanel testRow || !testRow.Children.OfType<Button>().Any(button => button.Content as string == "Test Connection"))
                throw new InvalidOperationException("Vertex success needs a named checkmark next to Test Connection");
            await capture();
            draft["vertexAuth"] = "express";
            RebuildAiPage(panel);
            await Task.Delay(100);
            if (SpeechItems<ComboBox>(panel).Any(c => Microsoft.UI.Xaml.Automation.AutomationProperties.GetAutomationId(c) == "vertex-model-cloud_video"))
                throw new InvalidOperationException("Private Cloud Storage model choices belong to Google sign-in.");
            var expressIds = SpeechItems<ComboBox>(panel).Select(c => Microsoft.UI.Xaml.Automation.AutomationProperties.GetAutomationId(c)).ToArray();
            if (expressIds.Contains("vertex-model-transcript") || expressIds.Contains("vertex-model-tts_audio") ||
                !Stage("local_video").IsEnabled || !Stage("youtube_imported_audio").IsEnabled || !Stage("summary").IsEnabled)
                throw new InvalidOperationException("Express pickers must reflect its model capabilities.");
            if (TestBadge().Visibility != Visibility.Collapsed) throw new InvalidOperationException("Switching authentication retained the test result");
            if (SpeechItems<PasswordBox>(panel).Count() != 1 || !SpeechItems<Button>(panel).Any(b => b.Content as string == "Use Google sign-in"))
                throw new InvalidOperationException("Express key and explicit Google sign-in backup must be available.");
        }
        finally {
            draft["vertexCatalog"] = previousCatalog;
            draft["vertexModels"] = previousModels;
            draft["aiProvider"] = previousProvider;
            draft["vertexAuth"] = previousAuth;
            RebuildAiPage(currentSectionPanel!);
        }
    }

    internal void VerifyGeminiGroupsForSmokeTest()
    {
        layout.UpdateLayout();
        var cards = SpeechItems<Border>(currentSectionPanel!).Where(b => Microsoft.UI.Xaml.Automation.AutomationProperties.GetAutomationId(b).StartsWith("gemini-section-",StringComparison.Ordinal)).ToArray();
        if (cards.Length != 7) throw new InvalidOperationException("Gemini sections must use seven compact grouped cards.");
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
            foreach (var secret in new[] { "apiKey", "sarvamApiKey", "elevenlabsApiKey" }) configData.Remove(secret);
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

    private void BuildGemini(StackPanel panel, bool vertexVerified = false)
    {
        // These display-only controls belong to the newly constructed page.
        // Unloaded WinUI controls can no longer expose Parent for detachment.
        planPillBadge = new Border(); planDot = new FontIcon();
        planPillText = new TextBlock(); planDetailsSubtitle = new TextBlock();

        var titleRows = new StackPanel();
        titleRows.Children.Add(ScreenReaderHeading.Mark(new TextBlock { Text = "AI", FontSize = 20, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold }));
        var titleCard = GeminiSettingsCard(titleRows);
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetAutomationId(titleCard,"gemini-title-card");
        panel.Children.Add(titleCard);

        var connection = AddGeminiGroup(panel, "AI provider");
        var provider = new ComboBox { ItemsSource = new[] { "Gemini Developer API", "Vertex AI" },
            SelectedIndex = SettingsStore.GetString(draft["aiProvider"]) == "vertex" ? 1 : 0 };
        AddChoiceMenu(provider);
        AddGeminiRow(connection, "Provider", provider, true);
        provider.SelectionChanged += (_, _) => {
            draft["aiProvider"] = provider.SelectedIndex == 1 ? "vertex" : "developer";
            RebuildAiPage(panel);
        };
        BuildOutputLanguages(panel);
        if (provider.SelectedIndex == 1) { BuildVertex(panel, vertexVerified); return; }

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

        modelSelectors.Clear();
        modelSelectorRows.Clear();
        foreach (var group in VertexPolicy.Value.ProjectModelGroups.Where(group => !group.SignInOnly))
        {
            var projectRows = AddGeminiGroup(panel, group.Title);
            foreach (var row in group.Rows)
                AddModelSelector(projectRows, row);
        }

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

    private void RebuildAiPage(StackPanel panel, bool vertexVerified = false)
    {
        if (!ApplyPreferences()) return;
        checkDebounceTimer?.Stop();
        apiKeyCheckRevision++;
        status.Text = "";
        modelSelectors.Clear();
        modelSelectorRows.Clear();
        panel.Children.Clear();
        panel.Children.Add(new ConnectivityNotice());
        BuildGemini(panel, vertexVerified);
        panel.DispatcherQueue.TryEnqueue(() => sectionHost.ChangeView(null, 0, null, true));
    }

    private static void AddChoiceMenu(ComboBox selector)
    {
        var menu = MenuAccessKeys.Create();
        menu.Opening += (_, _) => {
            menu.Items.Clear();
            foreach (var choice in selector.Items.OfType<string>()) {
                var item = new ToggleMenuFlyoutItem { Text = choice, IsChecked = Equals(selector.SelectedItem, choice), Icon = new FontIcon { Glyph = "\uE713" } };
                item.Click += (_, _) => selector.SelectedItem = choice;
                menu.Items.Add(item);
            }
            MenuAccessKeys.Assign(menu.Items);
        };
        selector.ContextFlyout = menu;
    }

    private void AddCloudField(StackPanel rows, string label, string setting, Action changed)
    {
        var field = new TextBox { Text = SettingsStore.GetString(draft[setting]) };
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(field, label);
        var row = new Grid { ColumnSpacing = 6 };
        row.ColumnDefinitions.Add(new ColumnDefinition());
        row.Children.Add(field);
        var menu = MenuAccessKeys.Create();
        void AddAction(string name, Symbol symbol, System.Action execute)
        {
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var button = new Button { Content = new SymbolIcon(symbol), IsEnabled = field.Text.Length > 0 };
            Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(button, name);
            ToolTipService.SetToolTip(button, name);
            var item = new MenuFlyoutItem { Text = name, IsEnabled = button.IsEnabled, Icon = new SymbolIcon(symbol) };
            button.Click += (_, _) => execute();
            item.Click += (_, _) => execute();
            field.TextChanged += (_, _) => item.IsEnabled = button.IsEnabled = field.Text.Length > 0;
            Grid.SetColumn(button, row.ColumnDefinitions.Count - 1);
            row.Children.Add(button);
            menu.Items.Add(item);
        }
        field.TextChanged += (_, _) => { draft[setting] = field.Text; changed(); };
        AddAction("Copy " + label, Symbol.Copy, () => {
            var data = new Windows.ApplicationModel.DataTransfer.DataPackage();
            data.SetText(field.Text);
            Windows.ApplicationModel.DataTransfer.Clipboard.SetContent(data);
        });
        AddAction("Clear " + label, Symbol.Delete, () => field.Text = "");
        row.ContextFlyout = menu;
        AddGeminiRow(rows, label, row, true);
    }

    private void BuildVertex(StackPanel panel, bool verified)
    {
        var rows = AddGeminiGroup(panel, "Vertex AI");
        bool express = SettingsStore.GetString(draft["vertexAuth"]) == "express";
        var auth = new ComboBox { ItemsSource = new[] { "Google sign-in", "Express API key" }, SelectedIndex = express ? 1 : 0 };
        AddChoiceMenu(auth);
        AddGeminiRow(rows, "Authentication", auth, true);
        auth.SelectionChanged += (_, _) => { draft["vertexAuth"] = auth.SelectedIndex == 1 ? "express" : "signin"; RebuildAiPage(panel); };
        const string verifiedLabel = "Vertex AI connection verified";
        var check = (FontIcon)Microsoft.UI.Xaml.Markup.XamlReader.Load("<FontIcon xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation' Glyph='&#xE73E;' FontSize='18' Foreground='{ThemeResource SystemFillColorSuccessBrush}'/>");
        check.Visibility = verified ? Visibility.Visible : Visibility.Collapsed;
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetAutomationId(check, "vertex-test-status");
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(check, verifiedLabel);
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetAccessibilityView(check, Microsoft.UI.Xaml.Automation.Peers.AccessibilityView.Control);
        ToolTipService.SetToolTip(check, verifiedLabel);
        void ClearCheck() => check.Visibility = Visibility.Collapsed;
        var actions = new CompactActionsPanel();
        var result = new TextBlock { TextWrapping = TextWrapping.Wrap };
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetLiveSetting(result, Microsoft.UI.Xaml.Automation.Peers.AutomationLiveSetting.Polite);
        Button Action(string label, Action run) {
            var button = new Button { Content = label };
            button.Click += (_, _) => run();
            var menu = MenuAccessKeys.Create();
            var item = new MenuFlyoutItem { Text = label, Icon = MenuIcons.Create(label) };
            item.Click += (_, _) => { if (button.IsEnabled) run(); };
            menu.Items.Add(item);
            button.ContextFlyout = menu;
            actions.Children.Add(button);
            return button;
        }
        async Task Run(string command) {
            ClearCheck();
            result.Text = "";
            if (!ApplyPreferences()) return;
            var enabled = SpeechItems<Control>(panel).Where(control => control.IsEnabled).ToArray();
            foreach (var control in enabled) control.IsEnabled = false;
            try {
                if (command is "vertex-connect" or "vertex-disconnect")
                {
                    if (command == "vertex-connect") await YouTubeService.Shared.SignInToCloudAsync(CancellationToken.None);
                    else await YouTubeService.Shared.DisconnectCloudAccountAsync();
                    draft["vertexConnected"] = CredentialStore.HasGoogleSignIn;
                    RebuildAiPage(panel);
                    return;
                }
                var events = await model.Engine.RunAsync(new[] { command });
                bool passed = command == "vertex-check" && events.Any(e => e.Data.TryGetProperty("vertexModels", out _));
                foreach (var entry in events) {
                    result.Text = passed ? "" : entry.Message;
                    if (entry.Data.TryGetProperty("vertexConnected", out var connected)) draft["vertexConnected"] = connected.GetBoolean();
                    if (!express && entry.Data.TryGetProperty("vertexModels", out var catalog))
                        draft["vertexCatalog"] = JsonNode.Parse(catalog.GetRawText());
                }
                var peer = Microsoft.UI.Xaml.Automation.Peers.FrameworkElementAutomationPeer.CreatePeerForElement(result);
                peer?.RaiseNotificationEvent(Microsoft.UI.Xaml.Automation.Peers.AutomationNotificationKind.ActionCompleted,
                    Microsoft.UI.Xaml.Automation.Peers.AutomationNotificationProcessing.MostRecent, passed ? verifiedLabel : result.Text, "vertex-connection");
                if (ApplyPreferences() && events.Any(e => e.Data.TryGetProperty("vertexModels", out _) || e.Data.TryGetProperty("vertexConnected", out _))) {
                    RebuildAiPage(panel, passed);
                    status.Text = result.Text;
                }
            } catch (Exception ex) { result.Text = ex.Message; }
            finally { foreach (var control in enabled) control.IsEnabled = true; }
        }
        if (express) {
            var key = new PasswordBox();
            AddGeminiRow(rows, "Vertex AI express API key", key, true);
            var remove = new Button();
            var save = Action("Save Key", () => {
                if (string.IsNullOrWhiteSpace(key.Password)) return;
                if (CredentialStore.Vertex.Write(key.Password)) { ClearCheck(); key.Password = ""; result.Text = "Express key saved"; remove.IsEnabled = true; model.RefreshConnectivity(); }
                else result.Text = "Could not save the key";
            });
            save.IsEnabled = false;
            key.PasswordChanged += (_, _) => save.IsEnabled = !string.IsNullOrWhiteSpace(key.Password);
            remove = Action("Remove Key", () => {
                if (CredentialStore.Vertex.Write("")) { ClearCheck(); result.Text = "Key removed"; remove.IsEnabled = false; model.RefreshConnectivity(); }
                else result.Text = "Could not remove the key";
            });
            remove.IsEnabled = CredentialStore.Vertex.ContainsValue();
            if (remove.IsEnabled) result.Text = "Express key saved";
            Action("Use Google sign-in", () => auth.SelectedIndex = 0);
        } else {
            AddCloudField(rows, "Google Cloud project ID", "vertexProject", ClearCheck);
            AddCloudField(rows, "Cloud Storage bucket", "vertexBucket", ClearCheck);
            bool connected = CredentialStore.HasGoogleSignIn;
            draft["vertexConnected"] = connected;
            var account = new TextBlock { Text = connected ? "Google account connected" : "Signed out" };
            var badge = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
            if (connected) badge.Children.Add(new FontIcon { Glyph = "\uE73E", Foreground = new SolidColorBrush(Color.FromArgb(255, 16, 150, 75)) });
            badge.Children.Add(account);
            Microsoft.UI.Xaml.Automation.AutomationProperties.SetAutomationId(account, "vertex-signin-status");
            Microsoft.UI.Xaml.Automation.AutomationProperties.SetLiveSetting(account, Microsoft.UI.Xaml.Automation.Peers.AutomationLiveSetting.Polite);
            rows.Children.Add(badge);
            Action(connected ? "Reconnect Google Account" : "Sign in with Google", async () => await Run("vertex-connect"));
            Action("Remove Google sign-in", async () => {
                var confirm = new ContentDialog { Title = "Remove Google sign-in from this PC?", Content = "This removes Google Cloud access from this PC. Your YouTube accounts and projects stay available.", PrimaryButtonText = "Remove Google sign-in", CloseButtonText = "Cancel", DefaultButton = ContentDialogButton.Close };
                if (await WindowedDialog.ShowAsync(confirm, this, 460, 250) == ContentDialogResult.Primary) await Run("vertex-disconnect");
            }).IsEnabled = connected;
        }
        rows.Children.Add(actions);
        var test = new Button { Content = "Test Connection" };
        test.Click += async (_, _) => await Run("vertex-check");
        var testMenu = MenuAccessKeys.Create();
        var testItem = new MenuFlyoutItem { Text = "Test Connection", Icon = new FontIcon { Glyph = "\uE73E" } };
        testItem.Click += async (_, _) => await Run("vertex-check");
        testMenu.Items.Add(testItem);
        test.ContextFlyout = testMenu;
        var testRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        testRow.Children.Add(test);
        testRow.Children.Add(check);
        rows.Children.Add(testRow);
        rows.Children.Add(result);
        var policy = VertexPolicy.Value;
        var catalog = express ? policy.ExpressModels : (draft["vertexCatalog"] as JsonArray)?
            .Select(row => SettingsStore.GetString(row is JsonObject ? row["name"] : row)).ToArray() ?? Array.Empty<string>();
        string modelKey = express ? "vertexExpressModels" : "vertexModels";
        draft[modelKey] ??= new JsonObject();
        void AddVertexModelRow(StackPanel groupRows, VertexModelRow row)
        {
            var capability = policy.Stages[row.Stage];
            var selector = new ComboBox { PlaceholderText = express ? "Unavailable in express mode" : "No compatible models" };
            Microsoft.UI.Xaml.Automation.AutomationProperties.SetAutomationId(selector, "vertex-model-" + row.Id);
            var items = new SortedDictionary<string, ComboBoxItem>(StringComparer.Ordinal);
            var menu = MenuAccessKeys.Create();
            foreach (var group in policy.Groups.Where(g => g.Capability == capability)) {
                var names = catalog.Where(id => Regex.IsMatch(id, group.Pattern)).Distinct().OrderBy(id => id, StringComparer.Ordinal).ToArray();
                if (names.Length == 0) continue;
                AddModelGroupHeading(selector, menu, group.Title);
                foreach (var id in names) {
                    var choice = new ComboBoxItem { Content = id, Tag = id };
                    selector.Items.Add(choice);
                    items.Add(id, choice);
                    var entry = new ToggleMenuFlyoutItem { Text = id, Icon = new FontIcon { Glyph = "\uE945" } };
                    entry.Click += (_, _) => selector.SelectedItem = choice;
                    menu.Opening += (_, _) => entry.IsChecked = Equals(selector.SelectedItem, choice);
                    menu.Items.Add(entry);
                }
            }
            var saved = new[] { SettingsStore.GetString(draft[modelKey]?[row.Id]),
                                SettingsStore.GetString(draft[modelKey]?[row.Fallback ?? row.Stage]) };
            var selected = saved.Concat(policy.Preferred[capability]).FirstOrDefault(items.ContainsKey) ?? items.Keys.FirstOrDefault();
            if (selected != null) selector.SelectedItem = items[selected];
            selector.IsEnabled = items.Count > 0;
            selector.SelectionChanged += (_, change) => {
                if (selector.SelectedItem is not ComboBoxItem { Tag: string id }) return;
                var preferences = draft[modelKey]!;
                // Keep inherited choices stable when their previous default is edited.
                if (change.RemovedItems.FirstOrDefault() is ComboBoxItem { Tag: string previous })
                    foreach (var dependent in policy.ProjectModelGroups.SelectMany(group => group.Rows)
                        .Where(dependent => dependent.Id != row.Id && (dependent.Fallback ?? dependent.Stage) == row.Id && preferences[dependent.Id] == null))
                        preferences[dependent.Id] = previous;
                preferences[row.Id] = id;
            };
            selector.ContextFlyout = menu;
            AddGeminiRow(groupRows, row.Label, selector, true);
        }
        foreach (var group in policy.ProjectModelGroups.Where(group => !express || !group.SignInOnly))
        {
            var modelRows = AddGeminiGroup(panel, group.Title);
            foreach (var row in group.Rows.Where(row => !express || !row.SignInOnly))
                AddVertexModelRow(modelRows, row);
        }
    }

    private sealed record VertexModelGroup(string Title, string Capability, string Pattern);
    private sealed record VertexModelRow(string Id, string Stage, string Label, string? Fallback, bool SignInOnly);
    private sealed record VertexProjectGroup(string Title, bool SignInOnly, VertexModelRow[] Rows);
    private sealed record VertexModelPolicy(string[] ExpressModels, Dictionary<string, string> Stages,
        VertexModelGroup[] Groups, Dictionary<string, string[]> Preferred, VertexProjectGroup[] ProjectModelGroups);
    private static readonly Lazy<VertexModelPolicy> VertexPolicy = new(() => {
        using var defaults = JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "engine", "ai-defaults.json")));
        return defaults.RootElement.GetProperty("vertexModelPolicy").Deserialize<VertexModelPolicy>(
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true })!;
    });

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

    private static readonly string[] DeveloperGroupTitles =
        { "Latest", "Flash", "Flash-Lite", "Pro", "Gemini 4", "Omni", "Speech and transcription", "Other models" };

    private static string DeveloperModelGroup(string id)
    {
        if (Regex.IsMatch(id, @"^(?:models/)?gemini-4(?:[.-]|$)")) return "Gemini 4";
        if (id.Contains("latest")) return "Latest";
        if (id.Contains("tts") || id.Contains("transcribe")) return "Speech and transcription";
        if (id.Contains("flash-lite")) return "Flash-Lite";
        if (id.Contains("pro")) return "Pro";
        if (id.Contains("omni")) return "Omni";
        return id.Contains("flash") ? "Flash" : "Other models";
    }

    private static void AddModelGroupHeading(ComboBox selector, MenuFlyout menu, string title)
    {
        selector.Items.Add(new ComboBoxItem { Content = ScreenReaderHeading.Mark(new TextBlock { Text = title }), IsEnabled = false });
        // Menu heading: a category label has no action.
        menu.Items.Add(new MenuFlyoutItem { Text = title, IsEnabled = false });
    }

    private void ApplyGeminiModelChoices()
    {
        foreach (var (row, selector) in modelSelectorRows)
        {
            var current = SettingsStore.GetString(draft["models"]?[row.Id]);
            var inherited = SettingsStore.GetString(draft["models"]?[row.Fallback ?? row.Stage]);
            var choices = availableModels.Where(option => SupportsModelStage(row.Stage, option.Id)).ToList();
            selector.Items.Clear();
            var groups = choices.ToLookup(option => DeveloperModelGroup(option.Id));
            var menu = MenuAccessKeys.Create();
            foreach (var title in DeveloperGroupTitles.Where(groups.Contains)) {
                AddModelGroupHeading(selector, menu, title);
                foreach (var option in groups[title].OrderBy(m => m.Label, StringComparer.CurrentCultureIgnoreCase)) {
                    selector.Items.Add(option);
                    var entry = new ToggleMenuFlyoutItem { Text = option.Label, Icon = new FontIcon { Glyph = "\uE945" } };
                    entry.Click += (_, _) => selector.SelectedItem = option;
                    menu.Opening += (_, _) => entry.IsChecked = Equals(selector.SelectedItem, option);
                    menu.Items.Add(entry);
                }
            }
            selector.ContextFlyout = menu;
            var preferred = row.Stage switch
            {
                "transcript" => new[] { "gemini-3.5-transcribe" },
                "tts_audio" => new[] { "gemini-3.8-flash-lite-tts", "gemini-3.8-flash-tts", "gemini-3.1-flash-tts-preview", "gemini-2.5-flash-preview-tts" },
                _ => Array.Empty<string>()
            };
            selector.SelectedItem = choices.FirstOrDefault(option => option.Id == current)
                ?? choices.FirstOrDefault(option => option.Id == inherited)
                ?? preferred.Concat(new[] { "gemini-3.8-flash", "gemini-3.5-flash", "gemini-2.5-flash" })
                    .Select(id => choices.FirstOrDefault(option => option.Id == id)).FirstOrDefault(option => option != null)
                ?? choices.FirstOrDefault();
            selector.PlaceholderText = choices.Count == 0 ? "No suitable models available" : "Choose a model";
            selector.IsEnabled = choices.Count > 0;
        }
    }

    private void AddModelSelector(StackPanel panel, VertexModelRow row)
    {
        var selector = new ComboBox { HorizontalAlignment = HorizontalAlignment.Stretch, MinWidth = 0 };
        modelSelectors.Add(row.Id, selector);
        modelSelectorRows.Add((row, selector));
        selector.SelectionChanged += (_, _) =>
        {
            if (selector.SelectedItem is GeminiModelOption chosen && chosen.Id.Length > 0 && SupportsModelStage(row.Stage, chosen.Id))
                draft["models"]!.AsObject()[row.Id] = chosen.Id;
        };
        AddGeminiRow(panel, row.Label, selector, true);
    }

    private Task RefreshGeminiModelsAsync() => CheckApiKeyAndPlanAsync();

    private void SelectLatestModels()
    {
        if (availableModels.Count == 0) { status.Text = "Load the Gemini catalog first."; return; }
        static (int Major, int Minor, int Patch, int Alias) Rank(string id)
        {
            var version = Regex.Match(id, @"gemini-(\d+)(?:\.(\d+))?(?:\.(\d+))?");
            int Parse(Group group) => int.TryParse(group.Value, out var number) ? number : 0;
            return (Parse(version.Groups[1]), Parse(version.Groups[2]), Parse(version.Groups[3]), id.Contains("latest", StringComparison.OrdinalIgnoreCase) ? 1 : 0);
        }
        foreach (var (row, selector) in modelSelectorRows)
        {
            var options = selector.Items.OfType<GeminiModelOption>().Where(item => availableModels.Any(available => available.Id == item.Id)).ToList();
            if (options is not { Count: > 0 }) continue;
            var latest = options.OrderByDescending(option => Rank(option.Id)).First();
            selector.SelectedItem = latest;
            draft["models"]!.AsObject()[row.Id] = latest.Id;
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
                var capabilityStage = modelSelectorRows.Single(row => row.Selector == selector).Row.Stage;
                var expected = capabilityStage == "transcript" ? "gemini-3.5-transcribe" :
                    capabilityStage == "tts_audio" ? "gemini-3.8-flash-lite-tts" : "gemini-3.8-flash";
                var choices = selector.Items.OfType<GeminiModelOption>().ToList();
                if (choices.Count != 1 || choices[0].Id != expected || (selector.SelectedItem as GeminiModelOption)?.Id != expected)
                    throw new InvalidOperationException("Unsuitable Gemini model offered for " + stage);
            }
            SelectLatestModels();
            if (SettingsStore.GetString(draft["models"]?["transcript"]) != "gemini-3.5-transcribe")
                throw new InvalidOperationException("Latest model selection bypassed transcription filtering.");
            foreach (var id in new[] { "gemini-4-neon", "gemini-4-argon" })
                availableModels.Add(new GeminiModelOption(id, CleanModelName(id, id)));
            ApplyGeminiModelChoices();
            var summary = modelSelectors["summary"];
            var groupItems = summary.Items.Cast<object>().SkipWhile(item =>
                item is not ComboBoxItem { Content: TextBlock { Text: "Gemini 4" } }).Skip(1).TakeWhile(item => item is GeminiModelOption)
                .Cast<GeminiModelOption>().Select(option => option.Id);
            if (!groupItems.SequenceEqual(new[] { "gemini-4-argon", "gemini-4-neon" }) ||
                modelSelectors["transcript"].Items.OfType<GeminiModelOption>().Any(option => option.Id.StartsWith("gemini-4-")) ||
                summary.ContextFlyout is not MenuFlyout groupedMenu || !groupedMenu.Items.OfType<MenuFlyoutItem>().Any(item => item.Text == "Gemini 4"))
                throw new InvalidOperationException("Developer Gemini 4 grouping or task filtering differs.");
            var youtubeSummary = modelSelectors["youtube_summary"];
            youtubeSummary.SelectedItem = youtubeSummary.Items.OfType<GeminiModelOption>().Single(option => option.Id == "gemini-4-neon");
            if (SettingsStore.GetString(draft["models"]?["youtube_summary"]) != "gemini-4-neon" ||
                SettingsStore.GetString(draft["models"]?["summary"]) == "gemini-4-neon" ||
                modelSelectors.Keys.Any(key => key.StartsWith("cloud_", StringComparison.Ordinal)))
                throw new InvalidOperationException("Developer API needs independent Local and YouTube models.");
            SelectLatestModels();
            if (summary.SelectedItem is not GeminiModelOption newest || !newest.Id.StartsWith("gemini-4-"))
                throw new InvalidOperationException("Latest model selection ignored the major-only version.");
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
