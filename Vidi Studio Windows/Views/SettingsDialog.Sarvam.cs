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
    private bool sarvamDemoActive, extraSpeechHooked;
    private int demoGeneration, summaryVoiceRequest;
    private Button? sarvamPlayControl, sarvamStopControl;
    private System.Threading.CancellationTokenSource? sarvamDownload;
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
    private static readonly HttpClient demoClient = new() { Timeout = TimeSpan.FromSeconds(30) };

    private void BuildAdditionalSpeechControls(StackPanel panel)
    {
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
        sarvamControls.Visibility = provider == "sarvam" ? Visibility.Visible : Visibility.Collapsed;
        summaryVoiceControls.Visibility = provider is "sarvam" or "gemini" ? Visibility.Visible : Visibility.Collapsed;
        if (sarvamControls.Parent is Border sarvamCard) sarvamCard.Visibility = sarvamControls.Visibility;
        if (summaryVoiceControls.Parent is Border summaryCard) summaryCard.Visibility = summaryVoiceControls.Visibility;
        foreach (var control in new[] { nativeVoiceGroup, nativeSample, nativeSampleButtons })
            if (control != null) control.Visibility = provider == "sarvam" ? Visibility.Collapsed : Visibility.Visible;
        if (provider == "sarvam") { BuildSarvamSettings(); if (!sarvamModelsRequested) { sarvamModelsRequested = true; _ = RefreshSarvamModelsAsync(); if (!string.IsNullOrWhiteSpace(SettingsStore.GetString(draft["sarvamApiKey"])) && SettingsStore.GetString(draft["sarvamKeyStatus"]) != "verified") sarvamKeyTimer?.Start(); } } else StopSarvamDemo();
        if (provider is "sarvam" or "gemini") BuildSummarySpeechSettings();
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
    private void SpeechAdjustmentControl(StackPanel panel, string title, string id, JsonObject profile, string key, double initial, double low, double high, double step = .05)
    {
        var slider = new Slider { Header = title, Minimum = low, Maximum = high, StepFrequency = step, Value = SettingsStore.GetDouble(profile[key], initial) };
        string FormatValue() => id.StartsWith("summary-", StringComparison.Ordinal) ? slider.Value.ToString("P0", System.Globalization.CultureInfo.CurrentCulture) : slider.Value.ToString("0.00", System.Globalization.CultureInfo.CurrentCulture);
        var number = new TextBlock { Text = FormatValue() };
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
        password.PasswordChanged += (_, _) => { sarvamKeyMessage = ""; sarvamKeyTimer?.Stop(); if (password.Password.Trim().Length > 0) sarvamKeyTimer?.Start(); draft["sarvamApiKey"] = password.Password.Trim(); draft["sarvamKeyStatus"] = "unverified"; keyStatus.Text = password.Password.Length == 0 ? "No API key" : "API key saved — unverified"; if (deleteKeyButton != null) deleteKeyButton.IsEnabled = password.Password.Length > 0; if (sarvamCheckKeyButton != null) sarvamCheckKeyButton.IsEnabled = password.Password.Trim().Length > 0 && !sarvamKeyChecking; };
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
        var demo = voices.FirstOrDefault(v => v.Id == SettingsStore.GetString(profile["voice"]))?.DemoUrl;
        var demoStatus = new TextBlock { Text = demo == null ? "Sample unavailable" : "" };
        sarvamPlayControl = SpeechAction(sarvamControls,"Play Sample","sarvam-sample-play", () => { if (demo != null) _ = PlaySarvamDemoAsync(demo,demoStatus); },demo != null);
        sarvamStopControl = SpeechAction(sarvamControls,"Stop Sample","sarvam-sample-stop",StopSarvamDemo,false);
        sarvamControls.Children.Add(demoStatus);
        SpeechActionsRow(sarvamControls,"sarvam-key-actions", sarvamCheckKeyButton!, SpeechItems<Button>(sarvamControls).Single(b => AutomationProperties.GetAutomationId(b) == "sarvam-key-dashboard"));
        SpeechActionsRow(sarvamControls,"sarvam-sample-actions",sarvamPlayControl!,sarvamStopControl!,demoStatus);
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
    private void StopSarvamDemo() {
        demoGeneration++; sarvamDownload?.Cancel(); sarvamDownload?.Dispose(); sarvamDownload = null;
        sarvamDemo?.Pause(); if (sarvamDemo != null) sarvamDemo.Source = null; sarvamDemoActive = false;
        if (sarvamStopControl != null) sarvamStopControl.IsEnabled = false;
        if (sarvamPlayControl != null) sarvamPlayControl.IsEnabled = SarvamSpeech.Catalog.Voices.FirstOrDefault(v => v.Model == SarvamSpeech.Model(draft) && v.Id == SarvamSpeech.Voice(draft))?.DemoUrl != null;
    }
    private async Task PlaySarvamDemoAsync(string address, TextBlock status)
    {
        if (sarvamDemoActive) return;
        StopSarvamDemo(); var generation = demoGeneration; sarvamDemoActive = true;
        sarvamDownload = System.Threading.CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token);
        var token = sarvamDownload.Token;
        if (sarvamPlayControl != null) sarvamPlayControl.IsEnabled = false;
        if (sarvamStopControl != null) sarvamStopControl.IsEnabled = true;
        try
        {
            var directory = Path.Combine(SettingsStore.DirectoryPath,"sarvam-demo-cache"); Directory.CreateDirectory(directory);
            var path = Path.Combine(directory,Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(address))) + ".mp3");
            if (!File.Exists(path))
            {
                using var response = await demoClient.GetAsync(address,token); response.EnsureSuccessStatusCode();
                var bytes = await response.Content.ReadAsByteArrayAsync(token);
                if (bytes.Length < 64 || response.Content.Headers.ContentType?.MediaType?.StartsWith("audio/") != true) throw new IOException();
                var pending = path + "." + Guid.NewGuid() + ".tmp";
                try { await File.WriteAllBytesAsync(pending,bytes,token); File.Move(pending,path,true); } finally { if (File.Exists(pending)) File.Delete(pending); }
            }
            if (closed || generation != demoGeneration) return;
            if (sarvamDemo == null) {
                sarvamDemo = new Windows.Media.Playback.MediaPlayer();
                sarvamDemo.MediaEnded += (_,_) => DispatcherQueue.TryEnqueue(() => { StopSarvamDemo(); status.Text = ""; });
                sarvamDemo.MediaFailed += (_,_) => DispatcherQueue.TryEnqueue(() => { StopSarvamDemo(); status.Text = "Sample could not be played."; });
            }
            sarvamDemo.Source = Windows.Media.Core.MediaSource.CreateFromUri(new Uri(path)); sarvamDemo.Play(); status.Text = "Playing sample";
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) when (ex is HttpRequestException or IOException or UnauthorizedAccessException) { if (!closed && generation == demoGeneration) status.Text = "Sample unavailable. Check your connection and try again."; StopSarvamDemo(); }
    }
    private void BuildSummarySpeechSettings()
    {
        StopSummaryVoiceSample();
        summaryVoiceControls.Children.Clear(); var profile = SummarySpeechProfile.Independent(draft);
        summaryVoiceControls.Children.Add(ScreenReaderHeading.Mark(new TextBlock { Text = "Summary voice", FontSize = 18 }));
        var engine = SettingsStore.GetString(profile["engine"],"windows-core");
        SpeechChoiceControl(summaryVoiceControls,"Summary engine","summary-engine",engine,new[] {("windows-core","Windows OneCore"),("sapi5","Windows SAPI 5"),("piper","Piper Voices")}, value => { profile["engine"] = value; BuildSummarySpeechSettings(); });
        var holder = new StackPanel { Spacing = 8 }; summaryVoiceControls.Children.Add(holder);
        _ = PopulateSummaryVoicesAsync(holder,profile,engine);
        SpeechAdjustmentControl(summaryVoiceControls,"Speech rate","summary-rate",profile,"rate",1,.5,2);
        SpeechAdjustmentControl(summaryVoiceControls,"Pitch","summary-pitch",profile,"pitch",1,.5,2);
        SpeechAdjustmentControl(summaryVoiceControls,"Speech volume","summary-volume",profile,"volume",1,0,1);
        SpeechAction(summaryVoiceControls,"Restore Defaults","summary-controls-reset",() => { profile["rate"] = 1; profile["pitch"] = 1; profile["volume"] = 1; BuildSummarySpeechSettings(); });
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
        holder.Children.Clear();
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
                if (summarySamplePlayButton != null) summarySamplePlayButton.IsEnabled = engine != "piper" || PiperService.IsVoiceDownloaded(selected);
            }
            var languageOptions = new[] {("","All languages")}.Concat(available.Select(v => v.Language).Where(code => code.Length > 0).Distinct().OrderBy(SpeechLanguageLabel).Select(code => (code,SpeechLanguageLabel(code))));
            SpeechChoiceControl(holder,"Language and region","summary-language-filter",summaryVoiceLanguage,languageOptions,value => { summaryVoiceLanguage = value; Render(); });
            var search = new TextBox { Header = "Search voices", PlaceholderText = "Search by voice or language", Text = summaryVoiceSearch };
            AutomationProperties.SetAutomationId(search,"summary-voice-search");
            search.TextChanged += (_,_) => { summaryVoiceSearch = search.Text; Render(); }; holder.Children.Add(search);
            var clear = SpeechAction(holder,"Clear voice search","summary-search-clear",() => search.Text = "");
            if (clear.ContextFlyout is MenuFlyout searchMenu) {
                var focus = new MenuFlyoutItem { Text = "Search voices", Tag = "summary-search-focus" };
                focus.Click += (_,_) => search.Focus(FocusState.Programmatic); searchMenu.Items.Add(focus);
            }
            SpeechChoiceControl(holder,"Sort by","summary-voice-sort",summaryVoiceSort,new[] {("language","Language (A–Z)"),("name","Voice name (A–Z)")},value => { summaryVoiceSort = value; Render(); });
            holder.Children.Add(selectedText); holder.Children.Add(choices);
            summarySamplePlayButton = SpeechAction(holder,"Play Sample","summary-sample-play",async () => await PlaySummaryVoiceSampleAsync(profile,engine));
            summarySampleStopButton = SpeechAction(holder,"Stop Sample","summary-sample-stop",StopSummaryVoiceSample,false);
            summarySampleStatus = new TextBlock(); holder.Children.Add(summarySampleStatus);
            SpeechAction(holder,"Refresh Voices","summary-voices-refresh",async () => await PopulateSummaryVoicesAsync(holder,profile,engine));
            if (engine != "piper") {
                SpeechAction(holder,"Use system default voice","summary-voice-default",() => { StopSummaryVoiceSample(); profile[key] = ""; Render(); });
                SpeechAction(holder,"Open Windows Voice Settings","summary-system-voices",async () => await Windows.System.Launcher.LaunchUriAsync(new Uri("ms-settings:speech")));
            }
            Render();
            SpeechActionsRow(holder,"summary-sample-actions",summarySamplePlayButton!,summarySampleStopButton!,SpeechItems<Button>(holder).Single(b => AutomationProperties.GetAutomationId(b) == "summary-voices-refresh"));
            if (engine != "piper") SpeechActionsRow(holder,"summary-system-actions",SpeechItems<Button>(holder).Single(b => AutomationProperties.GetAutomationId(b) == "summary-voice-default"),SpeechItems<Button>(holder).Single(b => AutomationProperties.GetAutomationId(b) == "summary-system-voices"));
        }
        catch (OperationCanceledException) { }
        catch (Exception) { if (!closed && request == summaryVoiceRequest) holder.Children.Add(new TextBlock { Text = "Local voices could not be loaded." }); }
    }
    private void StopSummaryVoiceSample()
    {
        summarySampleCancellation?.Cancel(); summarySampleCancellation?.Dispose(); summarySampleCancellation = null;
        summarySamplePlayer?.Pause(); if (summarySamplePlayer != null) summarySamplePlayer.Source = null;
        if (summarySamplePath != null) { try { File.Delete(summarySamplePath); } catch (IOException) { } summarySamplePath = null; }
        if (summarySamplePlayButton != null) summarySamplePlayButton.IsEnabled = true;
        if (summarySampleStopButton != null) summarySampleStopButton.IsEnabled = false;
        if (summarySampleStatus != null) summarySampleStatus.Text = "";
    }
    private async Task PlaySummaryVoiceSampleAsync(JsonObject profile, string engine)
    {
        if (engine is not ("windows-core" or "sapi5" or "piper") || closed) return;
        StopSummaryVoiceSample(); StopSarvamDemo();
        var cancellation = System.Threading.CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token); summarySampleCancellation = cancellation;
        var token = cancellation.Token;
        Directory.CreateDirectory(SettingsStore.DirectoryPath);
        var config = Path.Combine(SettingsStore.DirectoryPath,"summary-sample-" + Guid.NewGuid() + ".json");
        var output = Path.Combine(SettingsStore.DirectoryPath,"summary-sample-" + Guid.NewGuid() + ".wav");
        temporaryFiles.Add(config); temporaryFiles.Add(output);
        if (summarySamplePlayButton != null) summarySamplePlayButton.IsEnabled = false;
        if (summarySampleStopButton != null) summarySampleStopButton.IsEnabled = true;
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
            summarySamplePath = output; summarySamplePlayer.Source = Windows.Media.Core.MediaSource.CreateFromUri(new Uri(output)); summarySamplePlayer.Play();
            if (summarySampleStatus != null) summarySampleStatus.Text = "Playing sample";
        } catch (OperationCanceledException) { try { File.Delete(output); } catch (IOException) { } }
        catch (Exception) { StopSummaryVoiceSample(); if (summarySampleStatus != null) summarySampleStatus.Text = "The selected local voice could not create a sample."; }
        finally { try { File.Delete(config); } catch (IOException) { } }
    }

#if DEBUG
    internal async Task VerifySarvamSettingsForSmokeTestAsync(Func<Task>? capture = null)
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
                var play = SpeechItems<Button>(sarvamControls).Single(c => AutomationProperties.GetAutomationId(c) == "sarvam-sample-play");
                if (play.IsEnabled != (modelId == "bulbul:v3")) throw new InvalidOperationException("Catalog demos must never generate a missing sample.");
                var browser = sarvamControls.Children.OfType<StackPanel>().Single(p => AutomationProperties.GetAutomationId(p) == "sarvam-voice-browser");
                var searchBox = browser.Children.OfType<Grid>().Single(g => AutomationProperties.GetAutomationId(g) == "sarvam-search-box");
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
