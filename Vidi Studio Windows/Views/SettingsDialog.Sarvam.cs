using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using Vidi_Studio_Windows.Services;

namespace Vidi_Studio_Windows.Views;

public sealed partial class SettingsDialog
{
    private readonly StackPanel sarvamControls = new() { Spacing = 12 };
    private readonly StackPanel summaryVoiceControls = new() { Spacing = 12 };
    private FrameworkElement? nativeVoiceGroup, nativeSample, nativeSampleButtons;
    private Windows.Media.Playback.MediaPlayer? sarvamDemo;
    private bool sarvamDemoActive, sarvamSamplePreparing, extraSpeechHooked;
    private int demoGeneration, summaryVoiceRequest;
    private Button? sarvamPlayControl, sarvamStopControl;
    private List<(string Id,string Label)> sarvamModelOptions = SarvamSpeech.Models.Select(m => (m,SarvamSpeech.ModelLabel(m))).ToList();
    private bool sarvamModelsRequested, sarvamModelsRefreshing;
    private string sarvamModelStatus = "";
    private string summaryVoiceSearch = "", summaryVoiceLanguage = "", summaryVoiceSort = "language";
    private Windows.Media.Playback.MediaPlayer? summarySamplePlayer;
    private System.Threading.CancellationTokenSource? summarySampleCancellation;
    private string? summarySamplePath;
    private Button? summarySamplePlayButton, summarySampleStopButton;
    private TextBlock? summarySampleStatus;
    private Border? sarvamSettingsCard, summarySettingsCard;
    private bool sarvamKeyChecking;
    private Microsoft.UI.Dispatching.DispatcherQueueTimer? sarvamKeyTimer;
    private TextBlock? sarvamKeyStatusText;
    private Button? sarvamCheckKeyButton;
    private string sarvamKeyMessage = "";
    private TextBlock? activeSarvamSampleStatus;
    private string? sarvamSampleId;
    private readonly Dictionary<string,ProgressRing> sarvamSampleRings = new();
    private readonly object sarvamSampleOwner = new(), summarySampleOwner = new();
    private ProgressRing sarvamPreparingRing = new() { Width = 18, Height = 18, Visibility = Visibility.Collapsed };
    private ProgressRing summaryPreparingRing = new() { Width = 18, Height = 18, Visibility = Visibility.Collapsed };

    private void BuildAdditionalSpeechControls(StackPanel panel)
    {
        PrepareElevenLabs(panel);
        sarvamSettingsCard ??= SpeechSettingsCard(sarvamControls);
        summarySettingsCard ??= SpeechSettingsCard(summaryVoiceControls);
        foreach (var card in new[] { sarvamSettingsCard, summarySettingsCard }) {
            if (card.Parent is Panel parent) parent.Children.Remove(card);
            panel.Children.Add(card);
        }
        if (!extraSpeechHooked)
        {
            extraSpeechHooked = true;
            sarvamKeyTimer = DispatcherQueue.CreateTimer(); sarvamKeyTimer.Interval = TimeSpan.FromMilliseconds(700);
            sarvamKeyTimer.Tick += async (_,_) => { sarvamKeyTimer.Stop(); if (sarvamKeyChecking) { sarvamKeyTimer.Start(); return; } await CheckSarvamKeyAsync(); };
            Closed += (_, _) => { sarvamKeyTimer?.Stop(); StopSummaryVoiceSample(); summarySamplePlayer?.Dispose(); StopSarvamDemo(); sarvamDemo?.Dispose(); };
        }
    }
    private void UpdateAdditionalSpeechVisibility(string provider)
    {
        if (elevenCard != null) elevenCard.Visibility = provider == "elevenlabs" ? Visibility.Visible : Visibility.Collapsed;
        if (provider == "elevenlabs") { if (elevenFields.Children.Count == 0) BuildElevenFields(); if (elevenModels.Count == 0 && !elevenChecking) _ = RefreshElevenLabs(); } else StopElevenDemo();
        sarvamControls.Visibility = provider == "sarvam" ? Visibility.Visible : Visibility.Collapsed;
        summaryVoiceControls.Visibility = provider is "sarvam" or "gemini" or "elevenlabs" ? Visibility.Visible : Visibility.Collapsed;
        if (sarvamSettingsCard != null) sarvamSettingsCard.Visibility = sarvamControls.Visibility;
        if (summarySettingsCard != null) summarySettingsCard.Visibility = summaryVoiceControls.Visibility;
        foreach (var control in new[] { nativeVoiceGroup, nativeSample, nativeSampleButtons })
            if (control != null) control.Visibility = provider is "sarvam" or "elevenlabs" ? Visibility.Collapsed : Visibility.Visible;
        if (provider == "sarvam") { BuildSarvamSettings(); if (!sarvamModelsRequested) { sarvamModelsRequested = true; _ = RefreshSarvamModelsAsync(); if (!string.IsNullOrWhiteSpace(SettingsStore.GetString(draft["sarvamApiKey"])) && SettingsStore.GetString(draft["sarvamKeyStatus"]) != "verified") sarvamKeyTimer?.Start(); } } else StopSarvamDemo();
        if (provider is "sarvam" or "gemini" or "elevenlabs") BuildSummarySpeechSettings();
    }

    private ComboBox SpeechChoiceControl(StackPanel panel, string title, string id, string value,
        IEnumerable<(string Id, string Label)> choices, Action<string> change)
    {
        var options = choices.ToArray();
        var picker = new ComboBox { Header = title, HorizontalAlignment = HorizontalAlignment.Stretch };
        foreach (var option in options) picker.Items.Add(new ComboBoxItem { Content = option.Label, Tag = option.Id });
        picker.SelectedItem = picker.Items.Cast<ComboBoxItem>().FirstOrDefault(i => (string)i.Tag == value);
        AutomationProperties.SetAutomationId(picker, id);
        var selectedValue = value;
        picker.SelectionChanged += (_, _) => {
            if (picker.SelectedItem is ComboBoxItem item && (string)item.Tag != selectedValue) {
                selectedValue = (string)item.Tag;
                change(selectedValue);
            }
        };
        var menu = MenuAccessKeys.Create();
        foreach (var option in options)
        {
            var item = new ToggleMenuFlyoutItem { Text = option.Label, Tag = id + "-" + option.Id };
            AutomationProperties.SetAutomationId(item, id + "-" + option.Id);
            item.Click += (_, _) => picker.SelectedItem = picker.Items.Cast<ComboBoxItem>().First(i => (string)i.Tag == option.Id);
            menu.Opening += (_, _) => item.IsChecked = picker.SelectedItem is ComboBoxItem selected && (string)selected.Tag == option.Id;
            menu.Items.Add(item);
        }
        picker.ContextFlyout = menu; picker.Header = null; picker.Width = 280; AddSettingsRow(panel,title,picker); return picker;
    }
    private void SpeechAdjustmentControl(StackPanel panel, string title, string id, JsonObject profile, string key, double initial, double low, double high, double step = .05, bool percentage = false)
    {
        var slider = new Slider { Header = title, Minimum = low, Maximum = high, StepFrequency = step, Value = SettingsStore.GetDouble(profile[key], initial) };
        string FormatValue() => slider.Value.ToString(percentage ? "0%" : id.StartsWith("summary-", StringComparison.Ordinal) ? "P0" : "0.00", System.Globalization.CultureInfo.CurrentCulture);
        var number = new TextBlock { Text = FormatValue() };
        AutomationProperties.SetAutomationId(number, id + "-value");
        AutomationProperties.SetName(slider, title); AutomationProperties.SetAutomationId(slider, id);
        slider.ValueChanged += (_, _) => { profile[key] = slider.Value; number.Text = FormatValue(); };
        var menu = MenuAccessKeys.Create();
        foreach (var (suffix, label, action) in new (string,string,Action)[] {
            ("increase", "Increase " + title, () => slider.Value = Math.Min(high, slider.Value + step)),
            ("decrease", "Decrease " + title, () => slider.Value = Math.Max(low, slider.Value - step)),
            ("reset", "Restore Default", () => slider.Value = initial) })
        {
            var item = new MenuFlyoutItem { Text = label, Tag = id + "-" + suffix };
            AutomationProperties.SetAutomationId(item, id + "-" + suffix); item.Click += (_, _) => action();
            menu.Opening += (_, _) => item.IsEnabled = suffix == "reset" || (suffix == "increase" ? slider.Value < high : slider.Value > low);
            menu.Items.Add(item);
        }
        slider.ContextFlyout = menu; slider.Header = null;
        var controls = new Grid { ColumnSpacing = 10, Width = 320 };
        controls.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1,GridUnitType.Star) });
        controls.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        controls.Children.Add(slider); Grid.SetColumn(number,1); number.VerticalAlignment = VerticalAlignment.Center; controls.Children.Add(number);
        AddSettingsRow(panel,title,controls,true);
    }
    private Button SpeechAction(StackPanel panel, string label, string id, Action action, bool enabled = true)
    {
        var button = new Button { Content = label, IsEnabled = enabled };
        AutomationProperties.SetAutomationId(button, id);
        button.Click += (_, _) => { if (button.IsEnabled) action(); };
        var menu = MenuAccessKeys.Create();
        var item = new MenuFlyoutItem { Text = label, Tag = id };
        AutomationProperties.SetAutomationId(item,id);
        item.Click += (_, _) => { if (button.IsEnabled) action(); };
        menu.Opening += (_, _) => item.IsEnabled = button.IsEnabled;
        menu.Items.Add(item); button.ContextFlyout = menu; panel.Children.Add(button); return button;
    }
    private void BuildSarvamSettings()
    {
        StopSarvamDemo(); sarvamControls.Children.Clear();
        sarvamPreparingRing = new ProgressRing { Width = 18, Height = 18, Visibility = Visibility.Collapsed };
        sarvamControls.Children.Add(ScreenReaderHeading.Mark(new TextBlock { Text = "Sarvam Voices", FontSize = 18 }));
        var profile = SarvamSpeech.Profile(draft); var modelId = SarvamSpeech.Model(draft);
        Button? deleteKeyButton = null;
        var password = new PasswordBox { Header = "Sarvam API key", Password = SettingsStore.GetString(draft["sarvamApiKey"]) };
        AutomationProperties.SetAutomationId(password,"sarvam-api-key");
        var keyStatus = new TextBlock { Text = password.Password.Length == 0 ? "No API key" : SettingsStore.GetString(draft["sarvamKeyStatus"]) switch { "verified" => "API key verified", "invalid" => "API key rejected", _ => "API key saved — unverified" } };
        keyStatus.MaxWidth = 220; keyStatus.TextWrapping = TextWrapping.Wrap;
        sarvamKeyStatusText = keyStatus;
        keyStatus.Visibility = Visibility.Collapsed;
        AutomationProperties.SetLiveSetting(keyStatus,Microsoft.UI.Xaml.Automation.Peers.AutomationLiveSetting.Polite);
        if (sarvamKeyChecking) keyStatus.Text = "Checking API key…"; else if (sarvamKeyMessage.Length > 0) keyStatus.Text = sarvamKeyMessage;
        password.PasswordChanged += (_, _) => { sarvamKeyMessage = ""; sarvamKeyTimer?.Stop(); if (password.Password.Trim().Length > 0) sarvamKeyTimer?.Start(); StopSarvamDemo(); draft["sarvamApiKey"] = password.Password.Trim(); UpdateSarvamSampleControls(); draft["sarvamKeyStatus"] = "unverified"; keyStatus.Text = password.Password.Length == 0 ? "No API key" : "API key saved — unverified"; if (deleteKeyButton != null) deleteKeyButton.IsEnabled = password.Password.Length > 0; if (sarvamCheckKeyButton != null) sarvamCheckKeyButton.IsEnabled = password.Password.Trim().Length > 0 && !sarvamKeyChecking; };
        var keyMenu = MenuAccessKeys.Create();
        var editKey = new MenuFlyoutItem { Text = "Edit Sarvam API Key", Tag = "sarvam-key-edit" };
        editKey.Click += (_, _) => password.Focus(FocusState.Programmatic); keyMenu.Items.Add(editKey);
        password.ContextFlyout = keyMenu; sarvamControls.Children.Add(password); sarvamControls.Children.Add(keyStatus);
        deleteKeyButton = SpeechAction(sarvamControls,"Delete Sarvam API Key…","sarvam-key-delete", async () => {
            var dialog = new ContentDialog { Title = "Delete Sarvam API Key?", PrimaryButtonText = "Delete API Key", CloseButtonText = "Cancel" };
            if (await WindowedDialog.ShowAsync(dialog,this,460,250) == ContentDialogResult.Primary) password.Password = "";
        }, password.Password.Length > 0);
        sarvamControls.Children.Remove(password); sarvamControls.Children.Remove(deleteKeyButton);
        var keyRow = new Grid { ColumnSpacing = 8 };
        keyRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1,GridUnitType.Star) });
        keyRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        keyRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var statusIcon = ApiKeyStatusIcon(keyStatus);
        keyRow.Children.Add(password); Grid.SetColumn(statusIcon,1); keyRow.Children.Add(statusIcon);
        Grid.SetColumn(deleteKeyButton,2); keyRow.Children.Add(deleteKeyButton);
        deleteKeyButton.Content = new FontIcon { Glyph = "\uE74D", FontSize = 14 };
        deleteKeyButton.MinWidth = 36; deleteKeyButton.Padding = new Thickness(8,6,8,6); deleteKeyButton.VerticalAlignment = VerticalAlignment.Bottom;
        IconButtonContent.SetDestructive(deleteKeyButton,true);
        AutomationProperties.SetName(deleteKeyButton,"Delete Sarvam API key");
        AutomationProperties.SetAutomationId(deleteKeyButton,"sarvam-key-delete-icon");
        ToolTipService.SetToolTip(deleteKeyButton,"Delete Sarvam API key");
        password.Header = null; AutomationProperties.SetName(password,"Sarvam API key"); var passwordRow = AddSettingsRow(sarvamControls,"Sarvam API key",keyRow,true);
        sarvamControls.Children.Remove(passwordRow); sarvamControls.Children.Insert(1,passwordRow);
        sarvamCheckKeyButton = SpeechAction(sarvamControls,sarvamKeyChecking ? "Checking API Key…" : "Check API Key","sarvam-key-check",async () => { sarvamKeyTimer?.Stop(); await CheckSarvamKeyAsync(); },password.Password.Trim().Length > 0 && !sarvamKeyChecking);
        SpeechAction(sarvamControls,"Open Sarvam API Keys","sarvam-key-dashboard", async () => await Windows.System.Launcher.LaunchUriAsync(new Uri("https://indus.sarvam.ai/key-management")));
        var modelChoices = sarvamModelOptions.ToList();
        if (!modelChoices.Any(m => m.Id == modelId)) modelChoices.Add((modelId,SarvamSpeech.ModelLabel(modelId)));
        SpeechChoiceControl(sarvamControls,"Model","sarvam-model",modelId,modelChoices, value => { draft["sarvamSpeech"]!["model"] = value; sarvamVoiceSearch = ""; sarvamVoiceStyle = "all"; sarvamVoiceSort = "name"; BuildSarvamSettings(); });
        SpeechAction(sarvamControls,sarvamModelsRefreshing ? "Refreshing Models…" : "Refresh Models","sarvam-models-refresh",async () => await RefreshSarvamModelsAsync(),!sarvamModelsRefreshing);
        if (sarvamModelStatus.Length > 0) sarvamControls.Children.Add(new TextBlock { Text = sarvamModelStatus });
        var knownModel = SarvamSpeech.Models.Contains(modelId);
        var language = SettingsStore.GetString(profile["language"],"en-IN");
        var languages = (knownModel ? SarvamSpeech.Catalog.Voices.Where(v => v.Model == modelId).SelectMany(v => v.Languages).Distinct() : SarvamSpeech.Catalog.Languages.Keys).OrderBy(c => SarvamSpeech.Catalog.Languages.GetValueOrDefault(c,c));
        SpeechChoiceControl(sarvamControls,"Narration language","sarvam-language",language,languages.Select(c => (c,SarvamSpeech.Catalog.Languages.GetValueOrDefault(c,c))), value => {
            profile["language"] = value;
            sarvamVoiceSearch = ""; sarvamVoiceStyle = "all"; sarvamVoiceSort = "name";
            if (knownModel && !SarvamSpeech.Catalog.Voices.Any(v => v.Model == modelId && v.Languages.Contains(value) && v.Id == SettingsStore.GetString(profile["voice"])))
                profile["voice"] = SarvamSpeech.Catalog.Voices.First(v => v.Model == modelId && v.Languages.Contains(value)).Id;
            BuildSarvamSettings();
        });
        var voices = SarvamSpeech.Catalog.Voices.Where(v => v.Model == modelId && v.Languages.Contains(language)).ToArray();
        if (knownModel) BuildSarvamVoiceBrowser(profile,modelId);
        else sarvamControls.Children.Add(new TextBlock { Text = "Default voice" });
        if (knownModel) {
        SpeechAdjustmentControl(sarvamControls,"Speaking speed","sarvam-pace",profile,"pace",1,modelId == "bulbul:v2" ? .3 : .5,modelId == "bulbul:v2" ? 3 : 2);
        if (modelId == "bulbul:v2")
        {
            SpeechAdjustmentControl(sarvamControls,"Pitch","sarvam-pitch",profile,"pitch",0,-.75,.75);
            SpeechAdjustmentControl(sarvamControls,"Loudness","sarvam-loudness",profile,"loudness",1,.3,3);
            var toggle = new ToggleSwitch { Header = "Normalize text", IsOn = SettingsStore.GetBool(profile["preprocessing"]) };
            toggle.Toggled += (_, _) => profile["preprocessing"] = toggle.IsOn;
            var menu = MenuAccessKeys.Create(); var item = new ToggleMenuFlyoutItem { Text = "Normalize text", Tag = "sarvam-preprocessing" };
            item.Click += (_, _) => toggle.IsOn = item.IsChecked; menu.Opening += (_, _) => item.IsChecked = toggle.IsOn;
            menu.Items.Add(item); toggle.ContextFlyout = menu; sarvamControls.Children.Add(toggle);
        }
        else SpeechAdjustmentControl(sarvamControls,"Expressiveness","sarvam-temperature",profile,"temperature",.6,.01,1,.01);
        var rates = modelId == "bulbul:v2" ? new[] {8000,16000,22050,24000} : new[] {8000,16000,22050,24000,32000,44100,48000};
        SpeechChoiceControl(sarvamControls,"Sample rate","sarvam-sample-rate",SettingsStore.GetDouble(profile["sampleRate"],22050).ToString(System.Globalization.CultureInfo.InvariantCulture),rates.Select(r => (r.ToString(),r + " Hz")), value => profile["sampleRate"] = int.Parse(value));
        SpeechChoiceControl(sarvamControls,"Audio format","sarvam-codec",SettingsStore.GetString(profile["codec"],"wav"),new[] {"wav","mp3","aac","flac"}.Select(c => (c,c.ToUpperInvariant())), value => profile["codec"] = value);
        }
        var sampleVoice = voices.FirstOrDefault(v => v.Id == SettingsStore.GetString(profile["voice"]));
        var demoStatus = new TextBlock();
        sarvamPlayControl = SpeechAction(sarvamControls,"Play Sample","sarvam-sample-play", () => { if (sarvamDemoActive) StopSarvamDemo(); else if (sampleVoice != null) _ = PlaySarvamDemoAsync(sampleVoice,language,demoStatus); },sampleVoice != null);
        SetVoiceSampleMenu(sarvamPlayControl,"sarvam-sample",() => CanPlaySarvamSample,() => { if (sampleVoice != null) _ = PlaySarvamDemoAsync(sampleVoice,language,demoStatus); },() => sarvamDemoActive,StopSarvamDemo);
        sarvamStopControl = SpeechAction(sarvamControls,"Stop Sample","sarvam-sample-stop",StopSarvamDemo,false);
        sarvamStopControl.Visibility = Visibility.Collapsed;
        sarvamControls.Children.Add(demoStatus);
        SpeechActionsRow(sarvamControls,"sarvam-key-actions", sarvamCheckKeyButton!, SpeechItems<Button>(sarvamControls).Single(b => AutomationProperties.GetAutomationId(b) == "sarvam-key-dashboard"));
        SpeechActionsRow(sarvamControls,"sarvam-sample-actions",sarvamPlayControl!,sarvamStopControl!,sarvamPreparingRing,demoStatus);
        AutomationProperties.SetName(sarvamPreparingRing,"Preparing Sarvam sample");
        UpdateSarvamSampleControls();
    }
    private async Task CheckSarvamKeyAsync()
    {
        if (closed || sarvamKeyChecking) return;
        var key = SettingsStore.GetString(draft["sarvamApiKey"]).Trim();
        if (key.Length == 0) { if (sarvamKeyStatusText != null) sarvamKeyStatusText.Text = "No API key entered"; return; }
        if (!ApplyPreferences()) return;
        sarvamKeyChecking = true; draft["sarvamKeyStatus"] = "checking"; sarvamKeyMessage = "Checking API key…";
        if (sarvamKeyStatusText != null) sarvamKeyStatusText.Text = sarvamKeyMessage;
        if (sarvamCheckKeyButton != null) { sarvamCheckKeyButton.IsEnabled = false; sarvamCheckKeyButton.Content = "Checking API Key…"; }
        try {
            var events = await model.Engine.RunAsync(new[] { "check-sarvam-key" },cancellation:lifetime.Token);
            if (key != SettingsStore.GetString(draft["sarvamApiKey"]).Trim()) return;
            foreach (var item in events) if (item.Data.TryGetProperty("sarvamKeyStatus",out var state)) {
                draft["sarvamKeyStatus"] = state.GetString();
                sarvamKeyMessage = item.Data.TryGetProperty("sarvamKeyMessage",out var message) ? message.GetString() ?? "" : "";
            }
        } catch (OperationCanceledException) { }
        catch (Exception) { if (key == SettingsStore.GetString(draft["sarvamApiKey"]).Trim()) { draft["sarvamKeyStatus"] = "unavailable"; sarvamKeyMessage = "Could not check the key. Check your connection."; } }
        finally {
            sarvamKeyChecking = false;
            if (!closed) {
                if (sarvamKeyStatusText != null) sarvamKeyStatusText.Text = sarvamKeyMessage;
                if (sarvamCheckKeyButton != null) { sarvamCheckKeyButton.IsEnabled = !string.IsNullOrWhiteSpace(SettingsStore.GetString(draft["sarvamApiKey"])); sarvamCheckKeyButton.Content = "Check API Key"; }
            }
        }
    }
    private async Task RefreshSarvamModelsAsync()
    {
        if (sarvamModelsRefreshing || closed) return;
        sarvamModelsRefreshing = true; BuildSarvamSettings();
        try {
            var events = await model.Engine.RunAsync(new[] { "list-sarvam-models" },cancellation:lifetime.Token);
            foreach (var item in events) if (item.Data.TryGetProperty("sarvamModels",out var rows) && rows.ValueKind == System.Text.Json.JsonValueKind.Array) {
                var models = rows.EnumerateArray().Select(row => (Id:row.GetProperty("id").GetString()!,Label:row.GetProperty("label").GetString()!)).ToList();
                if (models.Count > 0) sarvamModelOptions = models;
                sarvamModelStatus = item.Data.TryGetProperty("sarvamModelsCached",out var cached) && cached.GetBoolean() ? "Using saved models" : "Models updated";
            }
        } catch (OperationCanceledException) { }
        catch (Exception) { sarvamModelStatus = "Using saved models"; }
        finally { sarvamModelsRefreshing = false; if (!closed && SettingsStore.GetString(draft["ttsMode"]) == "sarvam") BuildSarvamSettings(); }
    }
    private bool CanPlaySarvamSample => !closed && !sarvamDemoActive && !sarvamSamplePreparing && !VoiceSampleSession.Busy && !string.IsNullOrWhiteSpace(SettingsStore.GetString(draft["sarvamApiKey"]));
    private void UpdateSarvamSampleControls() {
        foreach (var button in SpeechItems<Button>(sarvamControls)) {
            var id = AutomationProperties.GetAutomationId(button);
            if (id.StartsWith("sarvam-row-sample-")) {
                var current = sarvamDemoActive && id == "sarvam-row-sample-" + sarvamSampleId;
                button.IsEnabled = current || CanPlaySarvamSample;
                IconButtonContent.Set(button,current ? "Stop Sample" : "Play Sample",current ? Symbol.Stop : Symbol.Play);
            }
        }
        if (sarvamPlayControl != null) {
            sarvamPlayControl.IsEnabled = sarvamDemoActive || (CanPlaySarvamSample && SarvamSpeech.Catalog.Voices.Any(v => v.Model == SarvamSpeech.Model(draft) && v.Id == SarvamSpeech.Voice(draft)));
            IconButtonContent.Set(sarvamPlayControl,sarvamDemoActive ? "Stop Sample" : "Play Sample",sarvamDemoActive ? Symbol.Stop : Symbol.Play);
        }
        if (sarvamStopControl != null) { sarvamStopControl.IsEnabled = sarvamDemoActive; IconButtonContent.Set(sarvamStopControl,"Stop Sample",Symbol.Stop); }
        foreach (var pair in sarvamSampleRings) {
            pair.Value.IsActive = sarvamSamplePreparing && sarvamSampleId == pair.Key;
            pair.Value.Visibility = pair.Value.IsActive ? Visibility.Visible : Visibility.Collapsed;
        }
        sarvamPreparingRing.IsActive = sarvamSamplePreparing;
        sarvamPreparingRing.Visibility = sarvamSamplePreparing ? Visibility.Visible : Visibility.Collapsed;
    }

    private void StopSarvamDemo() {
        demoGeneration++;
        sarvamDemo?.Pause(); if (sarvamDemo != null) sarvamDemo.Source = null; sarvamDemoActive = false; sarvamSampleId = null;
        if (activeSarvamSampleStatus != null) activeSarvamSampleStatus.Text = "";
        activeSarvamSampleStatus = null;
        if (!sarvamSamplePreparing) VoiceSampleSession.End(sarvamSampleOwner);
        UpdateSarvamSampleControls();
    }
    private async Task PlaySarvamDemoAsync(SarvamVoice voice, string language, TextBlock status)
    {
        if (!CanPlaySarvamSample) return;
        StopSarvamDemo();
        if (!ApplyPreferences()) return;
        if (!VoiceSampleSession.Begin(sarvamSampleOwner)) return;
        var generation = demoGeneration; sarvamSamplePreparing = true; sarvamDemoActive = true; sarvamSampleId = voice.Id; activeSarvamSampleStatus = status;
        status.Text = "Preparing sample…";
        UpdateSarvamSampleControls();
        try
        {
            // Do not cancel a submitted audition on Stop/close: let it finish
            // caching, then suppress playback using the generation guard.
            var events = await model.Engine.RunAsync(new[] { "preview-sarvam-sample", "--sarvam-model", voice.Model, "--sarvam-voice", voice.Id, "--sarvam-language", language });
            var ready = events.LastOrDefault(e => e.Data.TryGetProperty("sarvamSamplePath", out _));
            var path = ready?.Data.GetProperty("sarvamSamplePath").GetString();
            if (string.IsNullOrEmpty(path)) throw new IOException("Sample could not be prepared.");
            if (closed || generation != demoGeneration) return;
            if (sarvamDemo == null) {
                sarvamDemo = new Windows.Media.Playback.MediaPlayer();
                sarvamDemo.MediaEnded += (_,_) => DispatcherQueue.TryEnqueue(StopSarvamDemo);
                sarvamDemo.MediaFailed += (_,_) => DispatcherQueue.TryEnqueue(() => { var failed = activeSarvamSampleStatus; StopSarvamDemo(); if (failed != null) failed.Text = "Sample could not be played."; });
            }
            sarvamDemo.Source = Windows.Media.Core.MediaSource.CreateFromUri(new Uri(path)); sarvamDemo.Play(); status.Text = "Playing sample";
        }
        catch (Exception ex) {
            if (!closed && generation == demoGeneration) { StopSarvamDemo(); status.Text = ex.Message; }
        }
        finally { sarvamSamplePreparing = false; if (!sarvamDemoActive) VoiceSampleSession.End(sarvamSampleOwner); if (!closed) UpdateSarvamSampleControls(); }
    }
    private void BuildSummarySpeechSettings()
    {
        StopSummaryVoiceSample();
        summaryVoiceControls.Children.Clear(); var profile = SummarySpeechProfile.Independent(draft);
        var summaryHeading = ScreenReaderHeading.Mark(new TextBlock { Text = "Summary voice", FontSize = 18 });
        AutomationProperties.SetAutomationId(summaryHeading,"summary-heading");
        AutomationProperties.SetHeadingLevel(summaryHeading,Microsoft.UI.Xaml.Automation.Peers.AutomationHeadingLevel.Level2);
        summaryVoiceControls.Children.Add(summaryHeading);
        var engine = SettingsStore.GetString(profile["engine"],"windows-core");
        SpeechChoiceControl(summaryVoiceControls,"Summary engine","summary-engine",engine,new[] {("windows-core","Windows OneCore"),("sapi5","Windows SAPI 5"),("piper","Piper Voices")}, value => { profile["engine"] = value; BuildSummarySpeechSettings(); });
        SpeechAdjustmentControl(summaryVoiceControls,"Speech rate","summary-rate",profile,"rate",1,.5,2);
        SpeechAdjustmentControl(summaryVoiceControls,"Pitch","summary-pitch",profile,"pitch",1,.5,2);
        SpeechAdjustmentControl(summaryVoiceControls,"Speech volume","summary-volume",profile,"volume",1,0,1);
        SpeechAction(summaryVoiceControls,"Restore Defaults","summary-controls-reset",() => { profile["rate"] = 1; profile["pitch"] = 1; profile["volume"] = 1; BuildSummarySpeechSettings(); });
        summaryVoiceControls.Children.Add(ScreenReaderHeading.Mark(new TextBlock { Text = "Voices", FontSize = 16 }));
        var holder = new StackPanel { Spacing = 8 }; summaryVoiceControls.Children.Add(holder);
        _ = PopulateSummaryVoicesAsync(holder,profile,engine);
        if (engine == "piper") SpeechAction(summaryVoiceControls,"Download or Remove Voices…","summary-manage-voices",async () => { await WindowedDialog.ShowAsync(new PiperVoicesDialog(),this,780,740); BuildSummarySpeechSettings(); });
    }
    private static string SpeechLanguageLabel(string code)
    {
        try { return System.Globalization.CultureInfo.GetCultureInfo(code).DisplayName; }
        catch (System.Globalization.CultureNotFoundException) { return code; }
    }
    private async Task PopulateSummaryVoicesAsync(StackPanel holder, JsonObject profile, string engine)
    {
        var request = ++summaryVoiceRequest;
        holder.Children.Clear(); summaryPreparingRing = new ProgressRing { Width = 18, Height = 18, Visibility = Visibility.Collapsed };
        try
        {
            var available = engine == "windows-core" ? Windows.Media.SpeechSynthesis.SpeechSynthesizer.AllVoices.Select(v => new Voice(v.Id,v.DisplayName,v.Language,v.Gender.ToString())).ToList()
                : await SpeechService.VoicesAsync(model.Engine.SpeechHost,engine,lifetime.Token);
            if (closed || request != summaryVoiceRequest) return;
            var key = engine == "piper" ? "piperVoice" : engine == "sapi5" ? "sapi5Voice" : "windowsCoreVoice";
            var selectedText = new TextBlock();
            var choices = new StackPanel { Spacing = 8 }; AutomationProperties.SetAutomationId(choices,"summary-voice-choices");
            void Render()
            {
                var selected = SettingsStore.GetString(profile[key]);
                selectedText.Text = "Selected: " + (selected.Length == 0 ? "System default" : available.FirstOrDefault(v => v.Id == selected)?.Name ?? "Selected voice unavailable");
                var filtered = available.Where(v => v.Available && (summaryVoiceLanguage.Length == 0 || v.Language == summaryVoiceLanguage) &&
                    (summaryVoiceSearch.Length == 0 || (v.Name + " " + v.Language + " " + SpeechLanguageLabel(v.Language)).Contains(summaryVoiceSearch,StringComparison.CurrentCultureIgnoreCase)));
                var ordered = summaryVoiceSort == "name" ? filtered.OrderBy(v => v.Name,StringComparer.CurrentCultureIgnoreCase) : filtered.OrderBy(v => SpeechLanguageLabel(v.Language),StringComparer.CurrentCultureIgnoreCase).ThenBy(v => v.Name,StringComparer.CurrentCultureIgnoreCase);
                var options = ordered.Select(v => (v.Id,v.Name + " — " + SpeechLanguageLabel(v.Language))).ToList();
                if (engine != "piper") options.Insert(0,("","System default"));
                choices.Children.Clear();
                SpeechChoiceControl(choices,"Summary voice","summary-voice",selected,options,value => { StopSummaryVoiceSample(); profile[key] = value; Render(); });
                UpdateSummarySampleControls();
            }
            var languageOptions = new[] {("","All languages")}.Concat(available.Select(v => v.Language).Where(code => code.Length > 0).Distinct().OrderBy(SpeechLanguageLabel).Select(code => (code,SpeechLanguageLabel(code))));
            SpeechChoiceControl(holder,"Language and region","summary-language-filter",summaryVoiceLanguage,languageOptions,value => { summaryVoiceLanguage = value; Render(); });
            var search = new TextBox { Header = "Search voices", PlaceholderText = "Search by voice or language", Text = summaryVoiceSearch };
            AutomationProperties.SetAutomationId(search,"summary-voice-search");
            FullWidthVoiceSearch(search);
            search.TextChanged += (_,_) => { summaryVoiceSearch = search.Text; Render(); }; holder.Children.Add(search);
            var clear = SpeechAction(holder,"Clear voice search","summary-search-clear",() => search.Text = "");
            if (clear.ContextFlyout is MenuFlyout searchMenu) {
                var focus = new MenuFlyoutItem { Text = "Search voices", Tag = "summary-search-focus" };
                focus.Click += (_,_) => search.Focus(FocusState.Programmatic); searchMenu.Items.Add(focus);
            }
            SpeechChoiceControl(holder,"Sort by","summary-voice-sort",summaryVoiceSort,new[] {("language","Language (A–Z)"),("name","Voice name (A–Z)")},value => { summaryVoiceSort = value; Render(); });
            holder.Children.Add(selectedText); holder.Children.Add(choices);
            summarySamplePlayButton = SpeechAction(holder,"Play Sample","summary-sample-play",async () => { if (summarySampleCancellation != null) StopSummaryVoiceSample(); else await PlaySummaryVoiceSampleAsync(profile,engine); });
            SetVoiceSampleMenu(summarySamplePlayButton,"summary-sample",() => !VoiceSampleSession.Busy && (engine != "piper" || PiperService.IsVoiceDownloaded(SettingsStore.GetString(profile["piperVoice"]))),() => _ = PlaySummaryVoiceSampleAsync(profile,engine),() => summarySampleCancellation != null,StopSummaryVoiceSample);
            summarySampleStopButton = SpeechAction(holder,"Stop Sample","summary-sample-stop",StopSummaryVoiceSample,false);
            summarySampleStopButton.Visibility = Visibility.Collapsed;
            summarySampleStatus = new TextBlock(); holder.Children.Add(summarySampleStatus);
            SpeechAction(holder,"Refresh Voices","summary-voices-refresh",async () => await PopulateSummaryVoicesAsync(holder,profile,engine));
            if (engine != "piper") {
                SpeechAction(holder,"Use system default voice","summary-voice-default",() => { StopSummaryVoiceSample(); profile[key] = ""; Render(); });
                SpeechAction(holder,"Open Windows Voice Settings","summary-system-voices",async () => await Windows.System.Launcher.LaunchUriAsync(new Uri("ms-settings:speech")));
            }
            Render();
            SpeechActionsRow(holder,"summary-sample-actions",summarySamplePlayButton!,summarySampleStopButton!,summaryPreparingRing,SpeechItems<Button>(holder).Single(b => AutomationProperties.GetAutomationId(b) == "summary-voices-refresh"));
            if (engine != "piper") SpeechActionsRow(holder,"summary-system-actions",SpeechItems<Button>(holder).Single(b => AutomationProperties.GetAutomationId(b) == "summary-voice-default"),SpeechItems<Button>(holder).Single(b => AutomationProperties.GetAutomationId(b) == "summary-system-voices"));
        }
        catch (OperationCanceledException) { }
        catch (Exception) { if (!closed && request == summaryVoiceRequest) holder.Children.Add(new TextBlock { Text = "Local voices could not be loaded." }); }
    }
    private void UpdateSummarySampleControls() {
        var active = summarySampleCancellation != null;
        var summary = SummarySpeechProfile.Independent(draft);
        var engine = SettingsStore.GetString(summary["engine"],"windows-core");
        if (summarySamplePlayButton != null) {
            summarySamplePlayButton.IsEnabled = active || (!VoiceSampleSession.Busy && (engine != "piper" || PiperService.IsVoiceDownloaded(SettingsStore.GetString(summary["piperVoice"]))));
            IconButtonContent.Set(summarySamplePlayButton,active ? "Stop Sample" : "Play Sample",active ? Symbol.Stop : Symbol.Play);
        }
        if (summarySampleStopButton != null) { summarySampleStopButton.IsEnabled = active; IconButtonContent.Set(summarySampleStopButton,"Stop Sample",Symbol.Stop); }
        summaryPreparingRing.IsActive = active && summarySamplePath == null;
        summaryPreparingRing.Visibility = summaryPreparingRing.IsActive ? Visibility.Visible : Visibility.Collapsed;
        AutomationProperties.SetName(summaryPreparingRing,"Preparing Summary voice sample");
    }

    private void StopSummaryVoiceSample()
    {
        summarySampleCancellation?.Cancel(); summarySampleCancellation?.Dispose(); summarySampleCancellation = null;
        summarySamplePlayer?.Pause(); if (summarySamplePlayer != null) summarySamplePlayer.Source = null;
        if (summarySamplePath != null) { try { File.Delete(summarySamplePath); } catch (IOException) { } summarySamplePath = null; }
        VoiceSampleSession.End(summarySampleOwner); UpdateSummarySampleControls();
        if (summarySampleStatus != null) summarySampleStatus.Text = "";
    }
    private async Task PlaySummaryVoiceSampleAsync(JsonObject profile, string engine)
    {
        if (engine is not ("windows-core" or "sapi5" or "piper") || closed || VoiceSampleSession.Busy) return;
        StopSummaryVoiceSample();
        if (!VoiceSampleSession.Begin(summarySampleOwner)) return;
        var cancellation = System.Threading.CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token); summarySampleCancellation = cancellation;
        var token = cancellation.Token;
        Directory.CreateDirectory(SettingsStore.DirectoryPath);
        var config = Path.Combine(SettingsStore.DirectoryPath,"summary-sample-" + Guid.NewGuid() + ".json");
        var output = Path.Combine(SettingsStore.DirectoryPath,"summary-sample-" + Guid.NewGuid() + ".wav");
        temporaryFiles.Add(config); temporaryFiles.Add(output);
        UpdateSummarySampleControls();
        if (summarySampleStatus != null) summarySampleStatus.Text = "Preparing sample…";
        try {
            var preferences = PreviewConfig(); preferences["ttsMode"] = engine;
            var key = engine == "piper" ? "piperVoice" : engine == "sapi5" ? "sapi5Voice" : "windowsCoreVoice";
            preferences["systemVoice"] = SettingsStore.GetString(profile[key]); preferences["piperVoice"] = SettingsStore.GetString(profile["piperVoice"]);
            preferences["systemSpeechRate"] = SettingsStore.GetDouble(profile["rate"],1); preferences["systemSpeechPitch"] = SettingsStore.GetDouble(profile["pitch"],1); preferences["systemSpeechVolume"] = SettingsStore.GetDouble(profile["volume"],1);
            await File.WriteAllTextAsync(config,preferences.ToJsonString(Models.Json.Options),token);
            await model.Engine.RunAsync(new[] {"preview-tts","--text","This is a sample of the selected Summary voice.","--output",output},cancellation:token,configPath:config);
            if (closed || token.IsCancellationRequested || !ReferenceEquals(cancellation,summarySampleCancellation)) { File.Delete(output); return; }
            if (summarySamplePlayer == null) {
                summarySamplePlayer = new Windows.Media.Playback.MediaPlayer();
                summarySamplePlayer.MediaEnded += (_,_) => DispatcherQueue.TryEnqueue(StopSummaryVoiceSample);
                summarySamplePlayer.MediaFailed += (_,_) => DispatcherQueue.TryEnqueue(() => { StopSummaryVoiceSample(); if (summarySampleStatus != null) summarySampleStatus.Text = "Sample could not be played."; });
            }
            summarySamplePath = output; UpdateSummarySampleControls(); summarySamplePlayer.Source = Windows.Media.Core.MediaSource.CreateFromUri(new Uri(output)); summarySamplePlayer.Play();
            if (summarySampleStatus != null) summarySampleStatus.Text = "Playing sample";
        } catch (OperationCanceledException) { try { File.Delete(output); } catch (IOException) { } }
        catch (Exception) { StopSummaryVoiceSample(); if (summarySampleStatus != null) summarySampleStatus.Text = "The selected local voice could not create a sample."; }
        finally { try { File.Delete(config); } catch (IOException) { } }
    }

#if DEBUG
    internal async Task VerifySarvamSettingsForSmokeTestAsync(Func<Task>? capture = null, Func<string,Task>? captureControls = null)
    {
        var originallyRequested = sarvamModelsRequested;
        sarvamModelsRequested = true;
        NavigateForSmokeTest(2);
        await Task.Delay(150);
        var original = draft.DeepClone().AsObject();
        var originalProvider = SettingsStore.GetString(draft["ttsMode"],"windows-core");
        try
        {
            mode.SelectedItem = ((IEnumerable<SpeechEngineOption>)mode.ItemsSource).First(o => o.Id == "sarvam");
            await RefreshVoices();
            await Task.Delay(150);
            if (localSpeechControls.Visibility != Visibility.Collapsed || sarvamControls.Visibility != Visibility.Visible || summaryVoiceControls.Visibility != Visibility.Visible)
                throw new InvalidOperationException("Sarvam controls and independent Summary controls must replace local narration controls.");
            var keyRow = (Grid)SpeechItems<PasswordBox>(sarvamControls).Single().Parent;
            if (keyRow.Children.OfType<PasswordBox>().Count() != 1 || keyRow.Children.OfType<Button>().Single().Content is not FontIcon)
                throw new InvalidOperationException("The key trash icon must be beside the secure field.");
            if (keyRow.Children.OfType<FontIcon>().Single().GetValue(AutomationProperties.AutomationIdProperty) as string != "sarvam-key-status") throw new InvalidOperationException("Key status must be inline before the trash icon.");
            layout.UpdateLayout();
            var actions = sarvamControls.Children.OfType<Panel>().Single(p => AutomationProperties.GetAutomationId(p) == "sarvam-key-actions");
            VerifyKeyActionsForSmokeTest(actions);
            var modelControl = SpeechItems<ComboBox>(sarvamControls).Single(c => AutomationProperties.GetAutomationId(c) == "sarvam-model");
            if (modelControl.Header != null || modelControl.Parent is not Grid) throw new InvalidOperationException("Speech choices must use aligned label/control rows.");
            if (capture != null) { keyRow.StartBringIntoView(new BringIntoViewOptions { VerticalAlignmentRatio = 0 }); await Task.Delay(150); await capture(); }
            foreach (var modelId in SarvamSpeech.Models)
            {
                var picker = SpeechItems<ComboBox>(sarvamControls).Single(c => AutomationProperties.GetAutomationId(c) == "sarvam-model");
                picker.SelectedItem = picker.Items.Cast<ComboBoxItem>().First(i => (string)i.Tag == modelId);
                await Task.Delay(50);
                if (SarvamSpeech.Model(draft) != modelId) throw new InvalidOperationException("Model menu and state differ.");
                VerifyKeyActionsForSmokeTest(sarvamControls.Children.OfType<Panel>().Single(p => AutomationProperties.GetAutomationId(p) == "sarvam-key-actions"));
                var expected = modelId == "bulbul:v2" ? "sarvam-pitch" : "sarvam-temperature";
                var slider = SpeechItems<Slider>(sarvamControls).Single(c => AutomationProperties.GetAutomationId(c) == expected);
                if (slider.ContextFlyout is not MenuFlyout menu || menu.Items.Count != 3) throw new InvalidOperationException("Voice sliders need matching adjustment menus.");
                foreach(var theme in new[]{"dark","light"}) {
                    ApplyWindowTheme(theme);await Task.Delay(50);
                    var unitCases = modelId == "bulbul:v2" ? new[]{("pitch",-.75),("pitch",0.0),("pitch",.75),("loudness",.3),("loudness",1.0),("loudness",3.0)} : new[]{("temperature",.01),("temperature",.6),("temperature",1.0)};
                    foreach(var (key,value) in unitCases) {
                        var control=SpeechItems<Slider>(sarvamControls).Single(c=>AutomationProperties.GetAutomationId(c)=="sarvam-"+key);
                        var saved=control.Value;control.Value=value;
                        var readout=SpeechItems<TextBlock>(sarvamControls).Single(c=>AutomationProperties.GetAutomationId(c)=="sarvam-"+key+"-value");
                        var stored=SettingsStore.GetDouble(SarvamSpeech.Profile(draft)[key],double.NaN);
                        if(readout.Text!=value.ToString("0.00",System.Globalization.CultureInfo.CurrentCulture)||!double.IsFinite(stored)||Math.Abs(stored-value)>.001)throw new InvalidOperationException("Sarvam numeric display must preserve saved values: "+key+"="+readout.Text);
                        control.Value=saved;
                    }
                    var pace=SpeechItems<Slider>(sarvamControls).Single(c=>AutomationProperties.GetAutomationId(c)=="sarvam-pace");
                    var savedPace=pace.Value;pace.Value=1;
                    if(SpeechItems<TextBlock>(sarvamControls).Single(c=>AutomationProperties.GetAutomationId(c)=="sarvam-pace-value").Text!=1.0.ToString("0.00",System.Globalization.CultureInfo.CurrentCulture))throw new InvalidOperationException("Sarvam speaking speed must retain its multiplier.");
                    pace.Value=savedPace;
                    if(captureControls!=null) { pace.StartBringIntoView(new BringIntoViewOptions{VerticalAlignmentRatio=.35,AnimationDesired=false});await Task.Delay(100);await captureControls(modelId.Replace(':','-')+"-"+theme); }
                }
                ApplyWindowTheme(model.Settings.Get("theme","system"));
                var play = SpeechItems<Button>(sarvamControls).Single(c => AutomationProperties.GetAutomationId(c) == "sarvam-sample-play");
                if (play.IsEnabled != CanPlaySarvamSample || play.ContextFlyout is not MenuFlyout playMenu || playMenu.Items.Count != 2)
                    throw new InvalidOperationException("Every known Sarvam model needs Play Sample and its matching menu.");
                var rowSamples = SpeechItems<Button>(sarvamControls).Where(c => AutomationProperties.GetAutomationId(c).StartsWith("sarvam-row-sample-"));
                if (!rowSamples.Any() || rowSamples.Any(b => b.IsEnabled != CanPlaySarvamSample || b.ContextFlyout is not MenuFlyout m || m.Items.Count != 2))
                    throw new InvalidOperationException("Every voice row needs enabled sample playback and Play/Stop menus.");
                var gateOwner = new object();
                if (!VoiceSampleSession.Begin(gateOwner)) throw new InvalidOperationException("Unexpected active sample before native sample test.");
                try {
                    if (rowSamples.Any(b => b.IsEnabled) || play.IsEnabled || summarySamplePlayButton?.IsEnabled == true) throw new InvalidOperationException("Another engine must disable Sarvam and Summary Play controls.");
                } finally { VoiceSampleSession.End(gateOwner); }
                var sampleOriginal = sampleState;
                try {
                    sampleState = SampleState.Generating; UpdateSampleControls();
                    if (!nativePreparingRing.IsActive || nativePreparingRing.Visibility != Visibility.Visible || AutomationProperties.GetName(samplePlayButton!) != "Stop Sample") throw new InvalidOperationException("Gemini/local preparation must immediately show Stop and an accessible spinner.");
                    sampleState = SampleState.Playing; UpdateSampleControls();
                    if (nativePreparingRing.IsActive || AutomationProperties.GetName(samplePlayButton!) != "Stop Sample") throw new InvalidOperationException("Playback must hide the spinner and retain Stop.");
                } finally { sampleState = sampleOriginal; UpdateSampleControls(); }
                var savedKey = SettingsStore.GetString(draft["sarvamApiKey"]);
                draft["sarvamApiKey"] = ""; UpdateSarvamSampleControls();
                if (play.IsEnabled || rowSamples.Any(b => b.IsEnabled)) throw new InvalidOperationException("Keyless Sarvam samples must be disabled.");
                draft["sarvamApiKey"] = "isolated-smoke-key";
                sarvamDemoActive = true; UpdateSarvamSampleControls();
                if (!play.IsEnabled || rowSamples.Any(b => b.IsEnabled) || sarvamStopControl?.IsEnabled != true) throw new InvalidOperationException("Active sample must disable every other play action.");
                var activeRow = rowSamples.First();
                sarvamSampleId = AutomationProperties.GetAutomationId(activeRow)["sarvam-row-sample-".Length..]; UpdateSarvamSampleControls();
                if (!activeRow.IsEnabled || AutomationProperties.GetName(activeRow) != "Stop Sample" || rowSamples.Where(b => b != activeRow).Any(b => b.IsEnabled)) throw new InvalidOperationException("Only the active row may remain enabled as Stop Sample.");
                sarvamDemoActive = false; sarvamSampleId = null; sarvamSamplePreparing = true; UpdateSarvamSampleControls();
                if (!sarvamPreparingRing.IsActive || sarvamPreparingRing.Visibility != Visibility.Visible) throw new InvalidOperationException("Sarvam preparation must show the spinner.");
                if (play.IsEnabled || rowSamples.Any(b => b.IsEnabled)) throw new InvalidOperationException("Pending sample must disable repeat requests.");
                sarvamSamplePreparing = false; UpdateSarvamSampleControls();
                if (!play.IsEnabled || rowSamples.Any(b => !b.IsEnabled)) throw new InvalidOperationException("Finishing a sample must re-enable all play actions.");
                draft["sarvamApiKey"] = savedKey; UpdateSarvamSampleControls();
                var browser = sarvamControls.Children.OfType<StackPanel>().Single(p => AutomationProperties.GetAutomationId(p) == "sarvam-voice-browser");
                var searchBox = browser.Children.OfType<Grid>().Single(g => AutomationProperties.GetAutomationId(g) == "sarvam-search-box");
                VerifyFullWidthVoiceSearchForSmokeTest(searchBox);
                var searchVoices = searchBox.Children.OfType<TextBox>().Single();
                var rows = (StackPanel)browser.Children.OfType<ScrollViewer>().Single().Content;
                var savedVoice = SettingsStore.GetString(SarvamSpeech.Profile(draft)["voice"]);
                var narrationLanguage = SettingsStore.GetString(SarvamSpeech.Profile(draft)["language"],"en-IN");
                if (rows.Children.OfType<StackPanel>().Count() != SarvamSpeech.Catalog.Voices.Count(v => v.Model == modelId && v.Languages.Contains(narrationLanguage)))
                    throw new InvalidOperationException("The inline voice browser must expose all model variants.");
                searchVoices.Text = "No voice should match this test phrase";
                for (var attempt = 0; attempt < 20 && rows.Children.OfType<StackPanel>().Any(); attempt++) await Task.Delay(25);
                if (rows.Children.OfType<StackPanel>().Any() || SettingsStore.GetString(SarvamSpeech.Profile(draft)["voice"]) != savedVoice)
                    throw new InvalidOperationException($"Sarvam search must hide nonmatches without changing the selected voice: rows={rows.Children.OfType<StackPanel>().Count()}, selectionChanged={SettingsStore.GetString(SarvamSpeech.Profile(draft)["voice"]) != savedVoice}, currentBrowser={ReferenceEquals(browser,sarvamControls.Children.OfType<StackPanel>().Single(p => AutomationProperties.GetAutomationId(p) == "sarvam-voice-browser"))}, loaded={searchVoices.IsLoaded}, textApplied={searchVoices.Text == "No voice should match this test phrase"}, queryApplied={sarvamVoiceSearch == "No voice should match this test phrase"}.");
                var clearSearch = searchBox.Children.OfType<StackPanel>().Single().Children.OfType<Button>().Single();
                if (!clearSearch.IsEnabled || clearSearch.ContextFlyout is not MenuFlyout) throw new InvalidOperationException("Search needs an enabled clear icon and matching menu.");
                var clearPeer = new Microsoft.UI.Xaml.Automation.Peers.ButtonAutomationPeer(clearSearch);
                ((Microsoft.UI.Xaml.Automation.Provider.IInvokeProvider)clearPeer.GetPattern(Microsoft.UI.Xaml.Automation.Peers.PatternInterface.Invoke)).Invoke();
                for (var attempt = 0; attempt < 20 && !rows.Children.OfType<StackPanel>().Any(); attempt++) await Task.Delay(25);
                if (searchVoices.Text.Length != 0 || !rows.Children.OfType<StackPanel>().Any()) throw new InvalidOperationException("The clear icon must restore the matching voice list.");
                if (SpeechItems<ComboBox>(browser).Any(c => AutomationProperties.GetAutomationId(c) == "sarvam-language-filter")) throw new InvalidOperationException("Narration language must also filter the voice browser without a duplicate language picker.");
                foreach (var filterId in new[] { "sarvam-style-filter", "sarvam-voice-sort" })
                {
                    var filter = SpeechItems<ComboBox>(browser).Single(c => AutomationProperties.GetAutomationId(c) == filterId);
                    if (filter.ContextFlyout is not MenuFlyout filterMenu || filterMenu.Items.Count != filter.Items.Count)
                        throw new InvalidOperationException("Voice browser choices must have matching menus.");
                }
            }
            var summaryProfile = SummarySpeechProfile.Independent(draft);
            summaryProfile["engine"] = "windows-core";
            BuildSummarySpeechSettings();
            var holder = summaryVoiceControls.Children.OfType<StackPanel>().Single();
            for (var attempt = 0; attempt < 50 && !holder.Children.OfType<TextBox>().Any(); attempt++) await Task.Delay(50);
            var search = holder.Children.OfType<TextBox>().Single();
            VerifyFullWidthVoiceSearchForSmokeTest(search);
            var heading = SpeechItems<TextBlock>(summaryVoiceControls).Single(t=>AutomationProperties.GetAutomationId(t)=="summary-heading");
            if (heading.Text != "Summary voice" || Microsoft.UI.Xaml.Automation.AutomationProperties.GetHeadingLevel(heading) != Microsoft.UI.Xaml.Automation.Peers.AutomationHeadingLevel.Level2)
                throw new InvalidOperationException("Summary voice heading must name the controls below it.");
            var summarySliders = SpeechItems<Slider>(summaryVoiceControls).ToArray();
            if (summarySliders.Length != 3 || summarySliders.Any(slider=>slider.TransformToVisual(summaryVoiceControls).TransformPoint(new Windows.Foundation.Point()).Y <= heading.TransformToVisual(summaryVoiceControls).TransformPoint(new Windows.Foundation.Point()).Y || slider.TransformToVisual(summaryVoiceControls).TransformPoint(new Windows.Foundation.Point()).Y >= holder.TransformToVisual(summaryVoiceControls).TransformPoint(new Windows.Foundation.Point()).Y))
                throw new InvalidOperationException("Summary tuning must follow its heading and precede voice browsing.");
            var selectedVoice = SettingsStore.GetString(summaryProfile["windowsCoreVoice"]);
            search.Text = "No voice should match this test phrase";
            var choicePanel = holder.Children.OfType<StackPanel>().Single(p => AutomationProperties.GetAutomationId(p) == "summary-voice-choices");
            for (var attempt = 0; attempt < 20 && SpeechItems<ComboBox>(choicePanel).Single().Items.Count != 1; attempt++) await Task.Delay(25);
            var voiceChoice = SpeechItems<ComboBox>(choicePanel).Single();
            if (voiceChoice.Items.Count != 1 || SettingsStore.GetString(summaryProfile["windowsCoreVoice"]) != selectedVoice)
                throw new InvalidOperationException("Summary filtering must retain the selected voice and hide nonmatches.");
            search.Text = "";
            await Task.Delay(50);
            var sort = SpeechItems<ComboBox>(holder).Single(c => AutomationProperties.GetAutomationId(c) == "summary-voice-sort");
            sort.SelectedItem = sort.Items.Cast<ComboBoxItem>().First(i => (string)i.Tag == "name");
            if (summaryVoiceSort != "name" || sort.ContextFlyout is not MenuFlyout sortMenu || sortMenu.Items.Count != 2)
                throw new InvalidOperationException("Summary sorting needs matching choices and menu access.");
            var language = SpeechItems<ComboBox>(holder).Single(c => AutomationProperties.GetAutomationId(c) == "summary-language-filter");
            if (language.Items.Count > 1) language.SelectedIndex = 1;
            if (SettingsStore.GetString(summaryProfile["windowsCoreVoice"]) != selectedVoice)
                throw new InvalidOperationException("Summary language filtering changed the selected voice.");
            summaryVoiceSearch = ""; summaryVoiceLanguage = ""; summaryVoiceSort = "language";
            var summary = SummarySpeechProfile.Independent(draft); summary["engine"] = "piper"; summary["rate"] = 1.75;
            if (SettingsStore.GetDouble(draft["systemSpeechRate"],1) == 1.75) throw new InvalidOperationException("Summary tuning changed narration.");
            var clean = PreviewConfig();
            if (clean.ContainsKey("sarvamApiKey") || clean.ContainsKey("apiKey")) throw new InvalidOperationException("Credentials entered a temporary configuration.");
        }
        finally
        {
            sarvamModelsRequested = originallyRequested;
            draft.Clear(); foreach (var item in original) draft[item.Key] = item.Value?.DeepClone();
            mode.SelectedItem = ((IEnumerable<SpeechEngineOption>)mode.ItemsSource).First(o => o.Id == originalProvider);
            await RefreshVoices();
        }
    }
#endif

}
