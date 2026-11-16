using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using Vidi_Studio_Windows.Services;
using Vidi_Studio_Windows.Models;
using Vidi_Studio_Windows.ViewModels;
using Windows.Media.Core;
using Windows.Media.Playback;
using Windows.Media.SpeechSynthesis;
using Windows.Storage.Pickers;

namespace Vidi_Studio_Windows.Views;

public sealed partial class SettingsDialog : Window
{
    private readonly TaskCompletionSource<bool> completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly MainWindow? menuOwner;
    private readonly Grid layout;
    private readonly Window owner;
    private readonly ScrollViewer sectionHost;
    private ListView? sectionList;
    private int SelectedSection => (sectionList?.SelectedItem as ListViewItem)?.Tag is int section ? section : -1;
    private ListViewItem? developerPromptsItem;
    private StackPanel? currentSectionPanel;
    private int requestedSection;
#if DEBUG
    internal async Task VerifyWindowThemeAsync()
    {
        var original = model.Settings.Get("theme", "system");
        ApplyWindowTheme("dark");
        await Task.Delay(50);
        var dark = ((SolidColorBrush)layout.Background).Color;
        if (layout.ActualTheme != ElementTheme.Dark || dark.R + dark.G + dark.B > 300)
            throw new InvalidOperationException("The Settings window did not switch to dark appearance.");
        ApplyWindowTheme("light");
        await Task.Delay(50);
        var light = ((SolidColorBrush)layout.Background).Color;
        if (layout.ActualTheme != ElementTheme.Light || light.R + light.G + light.B < 600)
            throw new InvalidOperationException("The Settings window did not switch to light appearance.");
        if (dark == light)
            throw new InvalidOperationException("The Settings window surface did not follow the selected appearance.");
        if (WindowAppearance.MaterialFor(this) != WindowMaterial.None && (dark.A == 255 || light.A == 255))
            throw new InvalidOperationException("The Settings window surface is opaque, so the system material cannot show through.");
        ApplyWindowTheme(original);
    }
    internal async Task VerifyExportSoundForSmokeTestAsync()
    {
        NavigateForSmokeTest(2);
        await Task.Delay(150);
        var sound = exportSoundControl ?? throw new InvalidOperationException("Missing export sound control.");
        sound.StartBringIntoView();
        await Task.Delay(150);
        var menu = sound.ContextFlyout as MenuFlyout ?? throw new InvalidOperationException("Missing export sound context menu.");
        var item = menu.Items.OfType<ToggleMenuFlyoutItem>().Single(choice => choice.Tag as string == "export-completion-sound-menu");
        var original = sound.IsOn;
        if (original != SettingsStore.GetBool(draft["soundAfterExport"], true)) throw new InvalidOperationException("Export sound default differs.");
        menu.ShowAt(sound);
        await Task.Delay(100);
        if (item.IsChecked != original) throw new InvalidOperationException("Export sound menu check differs.");
        menu.Hide();
        var peer = new Microsoft.UI.Xaml.Automation.Peers.ToggleMenuFlyoutItemAutomationPeer(item);
        var provider = (Microsoft.UI.Xaml.Automation.Provider.IToggleProvider)peer.GetPattern(Microsoft.UI.Xaml.Automation.Peers.PatternInterface.Toggle);
        provider.Toggle();
        // Toggle via automation invokes the same Click handler as menu activation.
        if (sound.IsOn != item.IsChecked || SettingsStore.GetBool(draft["soundAfterExport"], true) != item.IsChecked)
            throw new InvalidOperationException("Export sound menu did not update the shared setting.");
        sound.IsOn = !original;
        if (SettingsStore.GetBool(draft["soundAfterExport"], true) == original) throw new InvalidOperationException("Export sound control did not update setting.");
        sound.IsOn = original;
        if (!File.Exists(Path.Combine(AppContext.BaseDirectory, "Assets", "Sounds", "descend.wav")))
            throw new InvalidOperationException("Export completion WAV was not packaged.");
    }
    internal async Task VerifySoundPreviewsForSmokeTestAsync()
    {
        NavigateForSmokeTest(2);
        if (soundPreviewControls.Count != 4) throw new InvalidOperationException("Expected four sound preview buttons.");
        foreach (var (resource, controls) in soundPreviewControls)
        {
            void ClickButton()
            {
                var peer = new Microsoft.UI.Xaml.Automation.Peers.ButtonAutomationPeer(controls.Button);
                ((Microsoft.UI.Xaml.Automation.Provider.IInvokeProvider)peer.GetPattern(Microsoft.UI.Xaml.Automation.Peers.PatternInterface.Invoke)).Invoke();
            }
            var original = SettingsStore.GetBool(draft["soundAfterExport"], true);
            draft["soundAfterExport"] = false;
            ClickButton();
            if (!soundPreview.IsPlaying(resource) || controls.Button.Content is not SymbolIcon { Symbol: Symbol.Pause } || !controls.Menu.Text.StartsWith("Pause sound:"))
                throw new InvalidOperationException("Preview did not become Pause: " + resource);
            ClickButton();
            if (soundPreview.Playing || controls.Button.Content is not SymbolIcon { Symbol: Symbol.Play })
                throw new InvalidOperationException("Preview did not pause: " + resource);
            await Task.Delay(200);
            if (soundPreview.Resource != resource || soundPreview.Playing) throw new InvalidOperationException("Paused preview lost its position.");
            var menuPeer = new Microsoft.UI.Xaml.Automation.Peers.MenuFlyoutItemAutomationPeer(controls.Menu);
            ((Microsoft.UI.Xaml.Automation.Provider.IInvokeProvider)menuPeer.GetPattern(Microsoft.UI.Xaml.Automation.Peers.PatternInterface.Invoke)).Invoke();
            if (!soundPreview.IsPlaying(resource)) throw new InvalidOperationException("Menu did not resume preview.");
            for (var attempt = 0; attempt < 150 && soundPreview.Resource != null; attempt++) await Task.Delay(20);
            if (soundPreview.Resource != null || controls.Button.Content is not SymbolIcon { Symbol: Symbol.Play } || !controls.Menu.Text.StartsWith("Play sound:"))
                throw new InvalidOperationException("Finished preview did not reset to Play: " + resource);
            if (SettingsStore.GetBool(draft["soundAfterExport"], true)) throw new InvalidOperationException("Preview changed the toggle.");
            draft["soundAfterExport"] = original;
        }
        soundPreview.Toggle("descend");
        NavigateForSmokeTest(0);
        if (soundPreview.Resource != null) throw new InvalidOperationException("Leaving Narration did not stop its preview.");
    }
    internal async Task VerifyProviderSwitchingAsync()
    {
        NavigateForSmokeTest(2);
        narrationPlaybackChoice!.SelectedItem = ((IEnumerable<PlaybackOption>)narrationPlaybackChoice.ItemsSource).First(option => option.Id == "generated");
        mode.SelectedItem = ((IEnumerable<SpeechEngineOption>)mode.ItemsSource).First(option => option.Id == "sapi5");
        mode.SelectedItem = ((IEnumerable<SpeechEngineOption>)mode.ItemsSource).First(option => option.Id == "windows-core");
        await RefreshVoices();
        if (voices.Count < 2 || (mode.SelectedItem as SpeechEngineOption)?.Id != "windows-core" || localSpeechControls.Visibility != Visibility.Visible) throw new InvalidOperationException("Core voice refresh did not finish.");
        mode.SelectedItem = ((IEnumerable<SpeechEngineOption>)mode.ItemsSource).First(option => option.Id == "sapi5");
        await RefreshVoices();
        var expectedSapiHost = DeveloperModeEnabled ? Visibility.Visible : Visibility.Collapsed;
        if (localSpeechControls.Visibility != Visibility.Visible || sapiHost.Visibility != expectedSapiHost || piperControls.Visibility != Visibility.Collapsed
            || voices.FirstOrDefault()?.Id != "" || (voice.SelectedItem is Voice chosenSapi && !chosenSapi.Available))
            throw new InvalidOperationException("SAPI 5 must show local speech controls and a selectable voice.");
        mode.SelectedItem = ((IEnumerable<SpeechEngineOption>)mode.ItemsSource).First(option => option.Id == "piper");
        await RefreshVoices();
        if (piperControls.Visibility != Visibility.Visible || sapiHost.Visibility != Visibility.Collapsed
            || localSpeechControls.Visibility != Visibility.Visible)
            throw new InvalidOperationException("Piper Voices must show the voice download controls and local speech settings.");
        mode.SelectedItem = ((IEnumerable<SpeechEngineOption>)mode.ItemsSource).First(option => option.Id == "gemini");
        await RefreshVoices();
        await Task.Delay(100);
        if (voices.Count != GeminiVoices.Featured.Count || voices.Any(v => v.Id == "" || v.ToString() != v.Id) || localSpeechControls.Visibility != Visibility.Collapsed)
            throw new InvalidOperationException("A stale local voice list or local speech controls remained visible for Gemini settings.");
        foreach (var provider in new[] { "windows-core", "sapi5", "piper" })
        {
            mode.SelectedItem = ((IEnumerable<SpeechEngineOption>)mode.ItemsSource).First(option => option.Id == provider);
            if (!narrationPlaybackChoice.IsEnabled) throw new InvalidOperationException("Local speech must unlock playback choices.");
            narrationPlaybackChoice.SelectedItem = ((IEnumerable<PlaybackOption>)narrationPlaybackChoice.ItemsSource).First(option => option.Id == "ondevice");
            if (SettingsStore.GetString(draft["narrationPlayback"]) != "ondevice") throw new InvalidOperationException("Local text preview is unavailable.");
            mode.SelectedItem = ((IEnumerable<SpeechEngineOption>)mode.ItemsSource).First(option => option.Id == "gemini");
            VerifyGeneratedOnly();
            // Even a stale/programmatic selection cannot restore the invalid combination.
            narrationPlaybackChoice.SelectedItem = ((IEnumerable<PlaybackOption>)narrationPlaybackChoice.ItemsSource).First(option => option.Id == "ondevice");
            VerifyGeneratedOnly();
        }
        await RefreshVoices();
        NavigateForSmokeTest(0); NavigateForSmokeTest(2);
        VerifyGeneratedOnly();
        void VerifyGeneratedOnly()
        {
            if (narrationPlaybackChoice!.IsEnabled || (narrationPlaybackChoice.SelectedItem as PlaybackOption)?.Id != "generated" ||
                SettingsStore.GetString(draft["narrationPlayback"]) != "generated" || SettingsStore.GetString(draft["ttsMode"]) != "gemini")
                throw new InvalidOperationException("Gemini must lock playback to synchronized generated narration.");
        }
    }
    internal async Task VerifyLocalVoiceSamplesAsync()
    {
        NavigateForSmokeTest(2);
        foreach (var provider in new[] { "windows-core", "sapi5" })
        {
            mode.SelectedItem = ((IEnumerable<SpeechEngineOption>)mode.ItemsSource).First(option => option.Id == provider);
            await RefreshVoices();
            if (voices.Count < 2) throw new InvalidOperationException(provider + " voice list is empty.");
            await Preview("This is a Vidi Studio voice sample.");
            if (status.Text != "Playing sample.") throw new InvalidOperationException(provider + " sample failed: " + status.Text);
            if (Microsoft.UI.Xaml.Automation.AutomationProperties.GetName(samplePlayButton!) != "Stop Sample") throw new InvalidOperationException("Play did not immediately become Stop.");
            await ToggleSampleAsync("This is a Vidi Studio voice sample.");
            if (sampleState != SampleState.Stopped || Microsoft.UI.Xaml.Automation.AutomationProperties.GetName(samplePlayButton) != "Play Sample") throw new InvalidOperationException("Clicking the active sample did not stop and restore Play.");
            await ToggleSampleAsync("This is a Vidi Studio voice sample.");
            if (sampleState != SampleState.Playing) throw new InvalidOperationException("Stopped sample did not restart.");
            StopSample();
            if (sampleState != SampleState.Stopped || Microsoft.UI.Xaml.Automation.AutomationProperties.GetName(samplePlayButton) != "Play Sample" || sampleStopButton?.IsEnabled != false)
                throw new InvalidOperationException("Stop did not reset the sample controls.");
        }
    }
    internal void NavigateForSmokeTest(int section) => SelectSection(section);
    internal int SelectedSectionForSmokeTest => SelectedSection;
    internal void VerifyDeveloperModeGateForSmokeTest()
    {
        NavigateForSmokeTest(0);
        if (developerPromptsItem?.Visibility != Visibility.Collapsed)
            throw new InvalidOperationException("Developer prompts must be hidden until Developer mode is enabled.");
        SelectSection(4);
        if (SelectedSectionForSmokeTest == 4)
            throw new InvalidOperationException("Developer prompts opened while Developer mode was off.");
        draft["developerMode"] = true; UpdateDeveloperPromptsVisibility();
        if (developerPromptsItem.Visibility != Visibility.Visible)
            throw new InvalidOperationException("Developer mode did not reveal Developer prompts.");
        SelectSection(4);
        if (SelectedSectionForSmokeTest != 4)
            throw new InvalidOperationException("Developer prompts did not open after Developer mode was enabled.");
        draft["developerMode"] = false; UpdateDeveloperPromptsVisibility();
        if (developerPromptsItem.Visibility != Visibility.Collapsed || SelectedSectionForSmokeTest != 0)
            throw new InvalidOperationException("Turning Developer mode off did not hide Developer prompts.");
    }
    internal async Task VerifyAdvancedOptionsForSmokeTestAsync()
    {
        draft["developerMode"] = false; UpdateDeveloperPromptsVisibility();
        NavigateForSmokeTest(5);
        if (storageAdvancedControls.Visibility != Visibility.Collapsed || storageAdvancedControls.Children.Count == 0)
            throw new InvalidOperationException("Executable paths must be hidden until Developer mode is enabled.");
        draft["developerMode"] = true; UpdateDeveloperPromptsVisibility();
        if (storageAdvancedControls.Visibility != Visibility.Visible)
            throw new InvalidOperationException("Developer mode did not reveal the executable paths.");
        NavigateForSmokeTest(2);
        mode.SelectedItem = ((IEnumerable<SpeechEngineOption>)mode.ItemsSource).First(option => option.Id == "sapi5");
        await RefreshVoices();
        if (sapiHost.Visibility != Visibility.Visible)
            throw new InvalidOperationException("Developer mode did not reveal the optional SAPI host field.");
        draft["developerMode"] = false; UpdateDeveloperPromptsVisibility();
        if (sapiHost.Visibility != Visibility.Collapsed)
            throw new InvalidOperationException("Turning Developer mode off did not hide the optional SAPI host field.");
        NavigateForSmokeTest(0);
    }
#endif
    private readonly StudioViewModel model;
    private readonly JsonObject draft;
    private readonly MediaPlayer samplePlayer = new();
    private readonly SettingsSoundPreview soundPreview;
    private readonly Dictionary<string, (Button Button, MenuFlyoutItem Menu, string Label)> soundPreviewControls = new();
    private bool voiceWarningOpen;
    private readonly VoiceSelectionWarning voiceSelectionWarning = new();
    internal static ContentDialog VoiceConnectionDialog(OnlineService service, string reason) => new() {
        Title = "Internet Connection Required",
        Content = new TextBlock { Text = reason + "\n\nNew online narration and uncached voice samples need internet. Saved audio and installed local voices remain available.", TextWrapping = TextWrapping.Wrap },
        CloseButtonText = "Close"
    };
    private async Task ShowVoiceWarningAsync(OnlineService service, string? failure = null) {
        if(closed) return;
        var state=ConnectivityService.Shared.State(service);
        if(voiceWarningOpen) return;
        if(failure==null && !voiceSelectionWarning.ShouldWarn(service,state)) return;
        voiceWarningOpen=true;
        try {
            var dialog=VoiceConnectionDialog(service,ConnectivityService.Shared.Reason(service) ?? failure!);
            if(state==ConnectionState.Available) dialog.Title="Voice Sample Couldn’t Play";
            await WindowedDialog.ShowAsync(dialog,this,520,300);
            ConnectivityService.Shared.Check();
        } finally { voiceWarningOpen=false; }
    }
    private Task WarnSelectedVoiceAsync() => SettingsStore.GetString(draft["ttsMode"]) switch {
        "gemini" => ShowVoiceWarningAsync(OnlineService.Gemini),
        "sarvam" => ShowVoiceWarningAsync(OnlineService.Sarvam),
        "elevenlabs" => ShowVoiceWarningAsync(OnlineService.ElevenLabs),
        _ => ResetVoiceWarning()
    };
    private Task ResetVoiceWarning() { voiceSelectionWarning.ShouldWarn(null,ConnectionState.Available); return Task.CompletedTask; }
    private void ConnectivityChanged(object? sender, EventArgs e)
    {
        if(narrationBuilt && SelectedSection==2) _ = WarnSelectedVoiceAsync();
        if (checkPlanButton != null) {
            checkPlanButton.IsEnabled = !checkingApiKey && (string.IsNullOrWhiteSpace(SettingsStore.GetString(draft["apiKey"])) || ConnectivityService.Shared.Available(OnlineService.Gemini));
            ToolTipService.SetToolTip(checkPlanButton, string.IsNullOrWhiteSpace(SettingsStore.GetString(draft["apiKey"])) ? "Enter a Gemini API key to check it." : ConnectivityService.Shared.Reason(OnlineService.Gemini) ?? "Check the entered API key");
        }
        if (samplePlayButton != null && SettingsStore.GetString(draft["ttsMode"]) == "gemini")
            samplePlayButton.IsEnabled = sampleState != SampleState.Stopped || (ConnectivityService.Shared.Available(OnlineService.Gemini) && HasDraftApiKey && !VoiceSampleSession.Busy);
    }
    private readonly object nativeSampleOwner = new();
    private enum SampleState { Stopped, Generating, Playing }
    private SampleState sampleState;
    private Button? samplePlayButton;
    private Button? sampleStopButton;
    private ProgressRing nativePreparingRing = new() { Width = 18, Height = 18, Visibility = Visibility.Collapsed };
    private readonly List<string> temporaryFiles = new();
    private CancellationTokenSource? sampleCancellation;
    private CancellationTokenSource? voiceCancellation;
    private readonly CancellationTokenSource lifetime = new();
    private int voiceRequest;
    private bool closed;
    private readonly TextBlock status = new() { TextWrapping = TextWrapping.Wrap };
    private sealed record SpeechEngineOption(string Id, string Label) { public override string ToString() => Label; }
    private sealed record PlaybackOption(string Id, string Label) { public override string ToString() => Label; }
    private readonly ComboBox mode = new() { Header = "Speech engine", HorizontalAlignment = HorizontalAlignment.Stretch };
    private readonly VoiceComboBox voice = new() { Header = "Installed voice", HorizontalAlignment = HorizontalAlignment.Stretch };
    private readonly TextBox sapiHost = new() { Header = "Optional SAPI host executable" };
    private readonly StackPanel localSpeechControls = new() { Spacing = 14 };
    private readonly StackPanel storageAdvancedControls = new() { Spacing = 0 };
    private List<Voice> voices = new();
    public SettingsDialog(StudioViewModel model, Window window, int initialSection = 0, StudioProject? projectTarget = null)
    {
        if (initialSection < 0 || initialSection > 8) throw new ArgumentOutOfRangeException(nameof(initialSection));
        soundPreview = new(action => DispatcherQueue.TryEnqueue(() => action()));
        soundPreview.Changed += UpdateSoundPreviewControls;
        requestedSection = initialSection;
        owner = window;
        menuOwner = window as MainWindow;
        this.projectTarget = projectTarget;
        allowClosedProjectTarget = projectTarget != null && !App.OpenEditorProjects.Any(p => p.Folder == projectTarget.Folder);
        this.model = model; draft = model.Settings.Data.DeepClone().AsObject();
        draft["sarvamApiKey"] = model.Settings.Get("sarvamApiKey");
        // Developer prompts stay hidden until the user enables Developer mode in Appearance.
        if (requestedSection == 4 && !DeveloperModeEnabled) requestedSection = 0;
        // Seed the key box from Windows Credential Manager; it is never part of config.json.
        var savedApiKey = model.Settings.Get("apiKey");
        if (savedApiKey.Length > 0) draft["apiKey"] = savedApiKey;
        autoApply = new SettingsAutoApply(draft);
        Title = "Vidi Studio Settings";
        WindowAppearance.ApplyIcon(this);
        layout = new Grid { Padding = new Thickness(24, 0, 24, 20), ColumnSpacing = 24 };
        layout.RowDefinitions.Add(new RowDefinition { Height = new GridLength(32) });
        layout.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        layout.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        layout.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        layout.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(210) });
        layout.ColumnDefinitions.Add(new ColumnDefinition());
        layout.SizeChanged += (_, args) => layout.ColumnDefinitions[0].Width = new GridLength(Math.Clamp(args.NewSize.Width * .24, 160, 210));
        var heading = new TextBlock { FontSize = 24, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold, Margin = new Thickness(0, 12, 0, 20) };
        ScreenReaderHeading.Mark(heading, Microsoft.UI.Xaml.Automation.Peers.AutomationHeadingLevel.Level1);
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetAutomationId(heading, "settings-page-title");
        Grid.SetRow(heading, 1); Grid.SetColumn(heading, 1); layout.Children.Add(heading);
        var sections = new ListView { SelectionMode = ListViewSelectionMode.Single };
        var labels = new[] { "Appearance", "Projects", "Narration", "Gemini", "Developer prompts", "Storage", "Help", "About", "YouTube Accounts" };
        var glyphs = new[] { "\uE790", "\uE8F1", "\uE767", "\uE734", "\uE8F2", "\uE8B7", "\uE897", "\uE946", "\uE714" };
        // Keep section IDs stable for menu/navigation callers while matching the Mac sidebar order.
        foreach (var i in new[] { 0, 1, 2, 3, 8, 4, 5, 6, 7 })
        {
            var row = new Grid { ColumnSpacing = 12 };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(24) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            row.Children.Add(new FontIcon { Glyph = glyphs[i], FontSize = 18, Width = 24, Height = 24, VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center });
            var label = new TextBlock { Text = labels[i], VerticalAlignment = VerticalAlignment.Center, TextWrapping = TextWrapping.Wrap };
            Grid.SetColumn(label, 1); row.Children.Add(label);
            var item = new ListViewItem { Tag = i, Content = row, MinHeight = 44, Padding = new Thickness(12, 8, 12, 8), HorizontalContentAlignment = HorizontalAlignment.Stretch };
            Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(item, labels[i]);
            if (i == 4) developerPromptsItem = item;
            sections.Items.Add(item);
        }
        sections.Margin = new Thickness(0, 12, 0, 0);
        Grid.SetRow(sections, 1); Grid.SetRowSpan(sections, 2);
        sectionList = sections;
        UpdateDeveloperPromptsVisibility();
        var host = sectionHost = new ScrollViewer
        {
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            HorizontalScrollMode = ScrollMode.Disabled,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            VerticalScrollMode = ScrollMode.Auto,
            HorizontalContentAlignment = HorizontalAlignment.Stretch
        };
        Grid.SetColumn(host, 1); Grid.SetRow(host, 2); layout.Children.Add(sections); layout.Children.Add(host);
        var footer = new StackPanel { Spacing = 6, Margin = new Thickness(0, 12, 0, 0) };
        footer.Children.Add(status);
        footer.Children.Add(autoApplyError);
        Grid.SetRow(footer, 3); Grid.SetColumnSpan(footer, 2); layout.Children.Add(footer);
        var progress = new ProgressOverlay();
        Grid.SetRowSpan(progress, 4); Grid.SetColumnSpan(progress, 2); layout.Children.Add(progress);
        progress.Observe(model);
        App.OpenProjectsChanged += ReceiveOpenProjectsChanged;
        Closed += (_, _) => progress.Detach();
        Content = layout;
        WindowAppearance.ApplyMaterial(this);
        ExtendsContentIntoTitleBar = true;
        var titleBarDrag = new Border
        {
            Height = 32,
            VerticalAlignment = VerticalAlignment.Top,
            Background = new SolidColorBrush(Microsoft.UI.Colors.Transparent),
            Margin = new Thickness(-24, 0, -24, 0)
        };
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(titleBarDrag, "Vidi Studio Settings title bar");
        Grid.SetColumnSpan(titleBarDrag, 2);
        layout.Children.Add(titleBarDrag);
        SetTitleBar(titleBarDrag);
        App.ThemeChanged += ReceiveTheme;
        ProjectPlaybackPreferences.Changed += ReceiveProjectPlaybackPreference;
        model.PropertyChanged += ReceiveModelProjectChange;
        ConnectivityService.Shared.Changed += ConnectivityChanged;
        Closed += (_, _) => ConnectivityService.Shared.Changed -= ConnectivityChanged;
        VoiceSampleSession.Changed += VoiceSampleChanged;
        Closed += (_,_) => { VoiceSampleSession.Changed -= VoiceSampleChanged; VoiceSampleSession.End(nativeSampleOwner); };
        samplePlayer.MediaEnded += (_, _) => DispatcherQueue.TryEnqueue(() =>
        {
            if (closed || sampleState != SampleState.Playing) return;
            samplePlayer.Source = null;
            sampleState = SampleState.Stopped;
            VoiceSampleSession.End(nativeSampleOwner);
            UpdateSampleControls();
            status.Text = "Sample finished.";
        });
        samplePlayer.MediaFailed += (_, _) => DispatcherQueue.TryEnqueue(() =>
        {
            if (closed || sampleState is SampleState.Stopped or SampleState.Generating) return;
            StopSample(false);
            status.Text = "Sample playback failed. Try another voice.";
        });
        PositionOverOwner();
        ReceiveTheme(App.ActiveTheme ?? model.Settings.Get("theme", "system"));
        layout.ActualThemeChanged += (_, _) => UpdateWindowTitleBar();
        sections.SelectionChanged += (_, _) =>
        {
            // Developer prompts is hidden unless Developer mode is on in Appearance.
            var section = SelectedSection;
            if (section < 0) return;
            if (section == 4 && !DeveloperModeEnabled)
            {
                SelectSection(0);
                return;
            }
            soundPreview.Stop();
            if (sampleState != SampleState.Stopped) StopSample(false);
            // Release retained Narration controls while their nested rows are still
            // attached; after removing the page WinUI may no longer expose Parent.
            ReleaseRetainedVoiceFields();
            foreach (var control in new FrameworkElement[] { mode, voice, sapiHost, localSpeechControls, piperControls, storageAdvancedControls })
                DetachFromVisualParent(control);
            if (host.Content is Panel oldPanel) oldPanel.Children.Clear();
            host.Content = null;
            var panel = new StackPanel { Spacing = 12, Padding = new Thickness(0, 0, 16, 12) };
            panel.Children.Add(new ConnectivityNotice());
            host.Content = panel;
            ConnectivityChanged(this, EventArgs.Empty);
            currentSectionPanel = panel;
            heading.Text = labels[section];
            switch (section)
            {
                case 0:
                    BuildAppearance(panel);
                    break;
                case 1: BuildProjects(panel); break;
                case 2: BuildNarration(panel); break;
                case 3: BuildGemini(panel); break;
                case 4: BuildDeveloperPrompts(panel); break;
                case 5: BuildStorage(panel); break;
                case 6: BuildHelp(panel); break;
                case 7: BuildAbout(panel); break;
                case 8: panel.Children.Add(new YouTubeAccountsPanel(this)); break;
            }
        };
        SelectSection(requestedSection);
        // WinUI can restore a ListView's selection while activating a newly created window.
        // Reapply the requested destination after its visual tree is ready.
        layout.Loaded += (_, _) => SelectSection(requestedSection);
        Closed += (_, _) =>
        {
            App.OpenProjectsChanged -= ReceiveOpenProjectsChanged;
            App.ThemeChanged -= ReceiveTheme;
            ProjectPlaybackPreferences.Changed -= ReceiveProjectPlaybackPreference;
            model.PropertyChanged -= ReceiveModelProjectChange;
            autoApplyTimer.Stop();
            checkDebounceTimer?.Stop();
            closed = true; voiceRequest++; lifetime.Cancel(); voiceCancellation?.Cancel(); sampleCancellation?.Cancel(); samplePlayer.Dispose(); soundPreview.Dispose();
            foreach (var file in temporaryFiles) { try { File.Delete(file); } catch (IOException) { } }
            completion.TrySetResult(true);
        };
        StartAutoApply();
    }
    public Task<bool> ShowAsync()
    {
        if (!App.HasMainWindows) { Hide(); return completion.Task; }
        PositionOverOwner(); Activate(); return completion.Task;
    }
    private bool DeveloperModeEnabled => SettingsStore.GetBool(draft["developerMode"], model.Settings.GetBool("developerMode"));
    private void UpdateDeveloperPromptsVisibility()
    {
        if (developerPromptsItem != null)
            developerPromptsItem.Visibility = DeveloperModeEnabled ? Visibility.Visible : Visibility.Collapsed;
        if (!DeveloperModeEnabled && SelectedSection == 4) SelectSection(0);
        UpdateAdvancedControlsVisibility();
    }
    private void UpdateAdvancedControlsVisibility()
    {
        storageAdvancedControls.Visibility = DeveloperModeEnabled ? Visibility.Visible : Visibility.Collapsed;
        var provider = (mode.SelectedItem as SpeechEngineOption)?.Id ?? "windows-core";
        sapiHost.Visibility = provider == "sapi5" && DeveloperModeEnabled ? Visibility.Visible : Visibility.Collapsed;
    }
    public void SelectSection(int section)
    {
        if (section == 4 && !DeveloperModeEnabled) section = 0;
        var item = sectionList?.Items.OfType<ListViewItem>().FirstOrDefault(item => item.Tag is int id && id == section);
        if (item == null)
            throw new ArgumentOutOfRangeException(nameof(section));
        requestedSection = section;
        sectionList!.SelectedItem = item;
    }
    public void Hide() { if (ApplyPreferences()) Close(); }
    private void ApplyWindowTheme(string theme)
    {
        App.PreviewTheme(theme);
    }
    private void ReceiveTheme(string theme)
    {
        var requested = theme switch { "light" => ElementTheme.Light, "dark" => ElementTheme.Dark, _ => ElementTheme.Default };
        WindowAppearance.ApplyTheme(this, layout, requested);
    }
    private void UpdateWindowTitleBar()
    {
        WindowAppearance.ApplySurface(this, layout, layout.ActualTheme);
        WindowAppearance.UpdateTitleBar(this, layout.ActualTheme);
    }
    private void AddText(StackPanel panel, string label, string key, string fallback = "")
    {
        var field = new TextBox { Text = SettingsStore.GetString(draft[key], fallback), MinWidth = 0 };
        field.TextChanged += (_, _) => draft[key] = field.Text; CompactRow(panel,label,field,true);
    }
    private void AddExecutablePicker(StackPanel panel, string label, string key)
    {
        var row = new Grid { ColumnSpacing = 10 };
        row.ColumnDefinitions.Add(new ColumnDefinition());
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var field = new TextBox { Text = SettingsStore.GetString(draft[key]), HorizontalAlignment = HorizontalAlignment.Stretch, MinWidth = 0 };
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(field,label);
        field.TextChanged += (_, _) => draft[key] = field.Text;
        row.Children.Add(field);
        var browse = new Button { Content = "Browse…", VerticalAlignment = VerticalAlignment.Bottom, MinWidth = 100 };
        browse.Click += async (_, _) =>
        {
            try
            {
                var picker = new FileOpenPicker();
                picker.FileTypeFilter.Add(".exe");
                picker.SuggestedStartLocation = PickerLocationId.ComputerFolder;
                WinRT.Interop.InitializeWithWindow.Initialize(picker, WinRT.Interop.WindowNative.GetWindowHandle(this));
                var file = await picker.PickSingleFileAsync();
                if (file != null) field.Text = file.Path;
            }
            catch (Exception ex) { model.Engine.Log(ex.ToString()); status.Text = "Could not open the executable picker. Enter the path manually."; }
        };
        Grid.SetColumn(browse, 1); row.Children.Add(browse); CompactRow(panel,label,row,true);
    }
    private ComboBox? narrationPlaybackChoice;
    private void AddPlaybackChoice(StackPanel panel)
    {
        SettingsStore.NormalizeNarrationPlayback(draft);
        var options = new[] { new PlaybackOption("generated", "Play synthesized narration"),
            new PlaybackOption("ondevice", "Preview segment text with the selected voice") };
        var field = new ComboBox { MinWidth = 0, MaxWidth = 360, ItemsSource = options,
            SelectedItem = options.FirstOrDefault(option => option.Id == SettingsStore.GetString(draft["narrationPlayback"], "generated")) ?? options[0] };
        narrationPlaybackChoice = field;
        field.SelectionChanged += (_, _) =>
        {
            if (field.SelectedItem is not PlaybackOption option) return;
            StopSample();
            draft["narrationPlayback"] = option.Id;
            UpdateNarrationPlaybackChoice();
        };
        UpdateNarrationPlaybackChoice();
        AddSettingsRow(panel, "Narration during playback", field, stretchControl: true);
    }
    private void UpdateNarrationPlaybackChoice()
    {
        SettingsStore.NormalizeNarrationPlayback(draft);
        if (narrationPlaybackChoice is not { } field) return;
        field.IsEnabled = SettingsStore.GetString(draft["ttsMode"]) is not ("gemini" or "sarvam" or "elevenlabs");
        field.SelectedItem = ((IEnumerable<PlaybackOption>)field.ItemsSource)
            .First(option => option.Id == SettingsStore.GetString(draft["narrationPlayback"], "generated"));
    }
    private void AddChoice(StackPanel panel, string label, string key, string[] values)
    {
        var field = new ComboBox { Header = label, ItemsSource = values, SelectedItem = SettingsStore.GetString(draft[key], values[0]) };
        field.SelectionChanged += (_, _) => { if (field.SelectedItem is string selected) draft[key] = selected; };
        panel.Children.Add(field);
    }
    private readonly Dictionary<string, Slider> speechSliders = new();
    private void RestoreSpeechDefaults()
    {
        foreach (var key in new[] { "systemSpeechRate", "systemSpeechPitch", "systemSpeechVolume" })
        { draft[key] = 1.0; if (speechSliders.TryGetValue(key, out var slider)) slider.Value = 1.0; }
    }
    private static StackPanel AddNarrationGroup(StackPanel host, string title)
    {
        var heading = ScreenReaderHeading.Mark(new TextBlock { Text = title, FontSize = 17, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold, Margin = new Thickness(0, 12, 0, 4) });
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetHeadingLevel(heading, Microsoft.UI.Xaml.Automation.Peers.AutomationHeadingLevel.Level2);
        host.Children.Add(heading);
        var rows = new StackPanel { Spacing = 8 };
        var group = new AccessibilityGroup(title + " settings") { Tag = "narration-card", CornerRadius = new CornerRadius(8), Padding = new Thickness(12,10,12,10), Child = rows };
        group.Style = (Style)Microsoft.UI.Xaml.Markup.XamlReader.Load("<Style xmlns=\"http://schemas.microsoft.com/winfx/2006/xaml/presentation\" TargetType=\"ContentControl\"><Setter Property=\"Background\" Value=\"{ThemeResource CardBackgroundFillColorDefaultBrush}\"/></Style>");
        host.Children.Add(group);
        rows.RegisterPropertyChangedCallback(UIElement.VisibilityProperty,(_,_) => { group.Visibility = rows.Visibility; heading.Visibility = rows.Visibility; });
        return rows;
    }
    private void AddSpeechSlider(StackPanel panel, string label, string key, double fallback, double minimum, double maximum)
    {
        var group = new Grid { ColumnSpacing = 12, Width = 280 };
        group.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        group.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var value = new TextBlock { Text = $"{SettingsStore.GetDouble(draft[key], fallback):P0}", MinWidth = 52,
            VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Right };
        var slider = new Slider { Minimum = minimum, Maximum = maximum, MinWidth = 0,
            Value = Math.Clamp(SettingsStore.GetDouble(draft[key], fallback), minimum, maximum),
            StepFrequency = .05, SmallChange = .05, LargeChange = .25, HorizontalAlignment = HorizontalAlignment.Stretch };
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(slider, label);
        slider.ValueChanged += (_, _) => { draft[key] = slider.Value; value.Text = $"{slider.Value:P0}"; };
        speechSliders[key] = slider;
        group.Children.Add(slider); Grid.SetColumn(value, 1); group.Children.Add(value);
        AddSettingsRow(panel, label, group);
    }
    private bool narrationBuilt;
    private void UpdateNarrationEngineOptions()
    {
        var provider = SettingsStore.GetString(draft["ttsMode"], "windows-core");
        var options = new List<SpeechEngineOption> { new("windows-core", "Windows OneCore"), new("sapi5", "Windows SAPI 5"),
            new("piper", "Piper Voices"), new("gemini", "Gemini Voices"), new("sarvam", "Sarvam Voices"), new("elevenlabs", "ElevenLabs Voices") };
        if (mode.ItemsSource is not IEnumerable<SpeechEngineOption> existing)
        {
            mode.ItemsSource = options;
            mode.SelectedItem = options.FirstOrDefault(option => option.Id == provider) ?? options[0];
        }
        else
        {
            var selection = existing.FirstOrDefault(option => option.Id == provider);
            if (!ReferenceEquals(mode.SelectedItem, selection)) mode.SelectedItem = selection;
        }
    }
    private ToggleSwitch AddSoundPreference(StackPanel panel, string label, string key, string resource, string id, string menuId)
    {
        var toggle = new ToggleSwitch { IsOn = SettingsStore.GetBool(draft[key], true), OnContent = "", OffContent = "", MinWidth = 0, VerticalAlignment = VerticalAlignment.Center };
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(toggle, label);
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetAutomationId(toggle, id);
        ConfigureCompactToggle(toggle);
        toggle.Toggled += (_, _) => draft[key] = toggle.IsOn;
        var menu = Vidi_Studio_Windows.Views.MenuAccessKeys.Create();
        var choice = new ToggleMenuFlyoutItem { Text = label, Tag = menuId };
        var previewItem = new MenuFlyoutItem { Tag = id + "-preview-menu" };
        previewItem.Click += (_, _) => ToggleSoundPreview(resource);
        menu.Opening += (_, _) => { choice.IsChecked = toggle.IsOn; UpdateSoundPreviewControls(); };
        choice.Click += (_, _) => toggle.IsOn = choice.IsChecked;
        menu.Items.Add(choice); menu.Items.Add(previewItem);
        toggle.ContextFlyout = menu;
        var preview = new Button { Width = 36, Height = 36, VerticalAlignment = VerticalAlignment.Center };
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetAutomationId(preview, id + "-preview");
        preview.Click += (_, _) => ToggleSoundPreview(resource);
        var previewMenu = Vidi_Studio_Windows.Views.MenuAccessKeys.Create();
        var matchingPreviewItem = new MenuFlyoutItem { Tag = id + "-preview-button-menu" };
        matchingPreviewItem.Click += (_, _) => ToggleSoundPreview(resource);
        previewMenu.Items.Add(matchingPreviewItem);
        previewMenu.Opening += (_, _) => { matchingPreviewItem.Text = PreviewSoundTitle(resource, label); };
        preview.ContextFlyout = previewMenu;
        soundPreviewControls[resource] = (preview, previewItem, label);
        var row = new Grid { ColumnSpacing = 12 };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        row.Children.Add(new TextBlock { Text = label, TextWrapping = TextWrapping.Wrap, VerticalAlignment = VerticalAlignment.Center });
        Grid.SetColumn(toggle, 1); row.Children.Add(toggle); Grid.SetColumn(preview, 2); row.Children.Add(preview);
        panel.Children.Add(row); UpdateSoundPreviewControls();
        return toggle;
    }
    private string PreviewSoundTitle(string resource, string label) => (soundPreview.IsPlaying(resource) ? "Pause" : "Play") + " sound: " + label;
    private void ToggleSoundPreview(string resource) { StopSample(false); soundPreview.Toggle(resource); }
    private void UpdateSoundPreviewControls()
    {
        foreach (var (resource, controls) in soundPreviewControls)
        {
            var title = PreviewSoundTitle(resource, controls.Label);
            controls.Button.Content = new SymbolIcon(soundPreview.IsPlaying(resource) ? Symbol.Pause : Symbol.Play);
            Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(controls.Button, title);
            ToolTipService.SetToolTip(controls.Button, title);
            controls.Menu.Text = title;
        }
    }
    private void ReleaseRetainedVoiceFields()
    {
        // Collapsed WinUI groups can stop exposing Parent. Detach from the
        // retained logical owners before rebuilding or clearing their page.
        if (nativeVoiceGroup is StackPanel previousVoiceGroup) {
            if (previousVoiceGroup.Children.Contains(piperControls)) previousVoiceGroup.Children.Remove(piperControls);
            foreach (var row in previousVoiceGroup.Children.OfType<Grid>())
                foreach (var field in new FrameworkElement[] { voice, sapiHost })
                    if (row.Children.Contains(field)) row.Children.Remove(field);
        }
    }
    private ToggleSwitch? exportSoundControl;
    private void BuildNarration(StackPanel panel)
    {
        ReleaseRetainedVoiceFields();
        foreach (var element in new FrameworkElement[] { mode, voice, sapiHost, localSpeechControls }) if (element.Parent is Panel parent) parent.Children.Remove(element);
        localSpeechControls.Children.Clear();
        BuildVoiceEngineControls();
        UpdateNarrationEngineOptions();
        var initialProvider = (mode.SelectedItem as SpeechEngineOption)?.Id ?? "windows-core";
        if (initialProvider is "windows-core" or "sapi5" && draft[VoiceKey(initialProvider)] == null)
            draft[VoiceKey(initialProvider)] = SettingsStore.GetString(draft["systemVoice"]);
        sapiHost.Text = SettingsStore.GetString(draft["sapiHostPath"]);
        if (!narrationBuilt)
        {
            narrationBuilt = true;
            mode.SelectionChanged += async (_, _) =>
            {
                if (mode.SelectedItem is not SpeechEngineOption selected) return;
                StopSample(false);
                draft["ttsMode"] = selected.Id;
                UpdateNarrationPlaybackChoice();
                if (selected.Id is not ("gemini" or "sarvam" or "elevenlabs")) draft["systemVoice"] = SettingsStore.GetString(draft[VoiceKey(selected.Id)]);
                await WarnSelectedVoiceAsync();
                await RefreshVoices();
            };
            sapiHost.TextChanged += (_, _) => draft["sapiHostPath"] = sapiHost.Text;
            voice.SelectionChanged += (_, _) =>
            {
                if (voice.SelectedItem is not Voice selected) return;
                var provider = (mode.SelectedItem as SpeechEngineOption)?.Id ?? "windows-core";
                if (!selected.Available)
                {
                    // Keyboard selection on a closed picker can still land on a group heading or disabled voice.
                    if (selected.Group != VoiceGroups.Heading) status.Text = selected.Name + ": " + selected.Note;
                    var savedId = SettingsStore.GetString(draft[VoiceKey(provider)]);
                    voice.SelectedItem = (voice.ItemsSource as IEnumerable<Voice>)?.FirstOrDefault(v => v.Available && v.Id == savedId);
                    return;
                }
                StopSample(false);
                if (provider == "gemini") draft["geminiVoice"] = selected.Id;
                else { draft[VoiceKey(provider)] = selected.Id; draft["systemVoice"] = selected.Id; }
                UpdateSampleControls();
            };
        }
        var narrationGroup = AddNarrationGroup(panel, "Narration");
        mode.Header = null;
        mode.MinWidth = 0; mode.MaxWidth = 360;
        AddSettingsRow(narrationGroup, "Speech engine", mode, stretchControl: true);
        AddPlaybackChoice(narrationGroup);
        AddSoundPreference(narrationGroup, "Sound when narration is off", "soundWhenNarrationOff", "Boink3", "narration-off-sound", "narration-off-sound-menu");
        AddSoundPreference(narrationGroup, "Sound when synthesized audio is unavailable", "soundWhenNarrationMissing", "ascend", "narration-missing-sound", "narration-missing-sound-menu");
        exportSoundControl = AddSoundPreference(narrationGroup, "Play sound after export", "soundAfterExport", "descend", "export-completion-sound", "export-completion-sound-menu");
        AddSoundPreference(narrationGroup, "Sound when timing validation fails", "soundWhenTimingInvalid", "Buzzer1", "timing-validation-sound", "timing-validation-sound-menu");
        var speechGroup = AddNarrationGroup(localSpeechControls, "Voice controls");
        speechSliders.Clear();
        AddSpeechSlider(speechGroup, "Speech rate", "systemSpeechRate", 1, .5, 2);
        AddSpeechSlider(speechGroup, "Pitch", "systemSpeechPitch", 1, .5, 2);
        AddSpeechSlider(speechGroup, "Speech volume", "systemSpeechVolume", 1, 0, 1);
        var restoreSpeech = new Button();
        IconButtonContent.Set(restoreSpeech, "Restore Defaults", Symbol.Refresh);
        restoreSpeech.Click += (_, _) => RestoreSpeechDefaults();
        var restoreMenu = Vidi_Studio_Windows.Views.MenuAccessKeys.Create();
        var restoreItem = new MenuFlyoutItem { Text = "Restore Defaults", Tag = "restore-speech-defaults-menu" };
        restoreItem.Click += (_, _) => RestoreSpeechDefaults();
        restoreMenu.Items.Add(restoreItem); restoreSpeech.ContextFlyout = restoreMenu;
        speechGroup.Children.Add(restoreSpeech);
        panel.Children.Add(localSpeechControls);
        BuildAdditionalSpeechControls(panel);
        var voiceGroup = AddNarrationGroup(panel, "Voices");
        nativeVoiceGroup = voiceGroup;
        if (piperControls.Parent is Panel previousVoiceHost) previousVoiceHost.Children.Remove(piperControls);
        voiceGroup.Children.Add(piperControls);
        voice.Header=null; sapiHost.Header=null;
        CompactRow(voiceGroup,"Installed voice",voice,true); CompactRow(voiceGroup,"Optional SAPI host executable",sapiHost,true);
        var refresh = new Button { Content = "Refresh Voices" }; refresh.Click += async (_, _) => await RefreshVoices(); voiceGroup.Children.Add(refresh);
        var sample = new TextBox { Header = "Voice sample", Text = "Welcome to Vidi Studio. Create accessible video with clear audio descriptions.", MaxLength = 500, TextWrapping = TextWrapping.Wrap };
        sample.Header=null;
        var sampleSection = new StackPanel(); var sampleRows = CompactGroup(sampleSection,"Voice sample");
        CompactRow(sampleRows,"Sample text",sample,true); nativeSample=sampleSection; panel.Children.Add(sampleSection);
        var controls = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        samplePlayButton = new Button();
        samplePlayButton.Click += async (_, _) =>
        {
            if (SettingsStore.GetString(draft["ttsMode"]) == "piper") await TogglePiperSampleAsync(sample.Text);
            else await ToggleSampleAsync(sample.Text);
        };
        ConfigurePiperSampleMenu(sample);
        controls.Children.Add(samplePlayButton);
        sampleStopButton = new Button { Content = "Stop Sample", Visibility = Visibility.Collapsed };
        sampleStopButton.Click += (_, _) => StopSample();
        IconButtonContent.Set(sampleStopButton,"Stop Sample",Symbol.Stop);
        nativePreparingRing = new ProgressRing { Width = 18, Height = 18, Visibility = Visibility.Collapsed };
        controls.Children.Add(sampleStopButton); controls.Children.Add(nativePreparingRing);
        SetVoiceSampleMenu(samplePlayButton,"local-sample",() => sampleState == SampleState.Stopped && !VoiceSampleSession.Busy && ((mode.SelectedItem as SpeechEngineOption)?.Id != "gemini" || HasDraftApiKey && ConnectivityService.Shared.Available(OnlineService.Gemini)),() => _ = Preview(sample.Text),() => sampleState != SampleState.Stopped,() => StopSample());
        UpdateSampleControls();
        nativeSampleButtons = controls;
        sampleRows.Children.Add(GeminiRowDivider()); sampleRows.Children.Add(controls);
        _ = RefreshVoices();
    }
    private bool HasDraftApiKey => !string.IsNullOrWhiteSpace(SettingsStore.GetString(draft["apiKey"])) &&
        SettingsStore.GetString(draft["apiKeyValid"], "true") != "false" &&
        SettingsStore.GetString(draft["apiPlan"], "") != "invalid";
    private void VoiceSampleChanged() { if (!closed) { UpdateSampleControls(); UpdateSarvamSampleControls(); UpdateSummarySampleControls(); if(elevenCard?.Visibility == Visibility.Visible) RenderElevenRows(); } }
    private void UpdateSampleControls()
    {
        if (samplePlayButton != null)
        {
            var sampleTitle = sampleState switch
            {
                SampleState.Generating => "Stop Sample",
                SampleState.Playing => "Stop Sample",
                _ => "Play Sample"
            };
            IconButtonContent.Set(samplePlayButton,sampleTitle,sampleState != SampleState.Stopped ? Symbol.Stop : Symbol.Play);
            var provider = (mode.SelectedItem as SpeechEngineOption)?.Id ?? "windows-core";
            samplePlayButton.IsEnabled = sampleState != SampleState.Stopped || (!VoiceSampleSession.Busy && (provider != "gemini" || HasDraftApiKey && ConnectivityService.Shared.Available(OnlineService.Gemini)));
            if (provider == "piper") samplePlayButton.IsEnabled &= PiperService.IsVoiceDownloaded(SettingsStore.GetString(draft["piperVoice"]));
        }
        nativePreparingRing.IsActive = sampleState == SampleState.Generating;
        nativePreparingRing.Visibility = nativePreparingRing.IsActive ? Visibility.Visible : Visibility.Collapsed;
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(nativePreparingRing,"Preparing voice sample");
        if (sampleStopButton != null) sampleStopButton.IsEnabled = sampleState != SampleState.Stopped;
        UpdatePiperSampleControls();
    }
    private async Task ToggleSampleAsync(string text) {
        if (sampleState != SampleState.Stopped) StopSample();
        else await Preview(text);
    }
    private void StopSample(bool report = true)
    {
        sampleCancellation?.Cancel(); sampleCancellation = null;
        samplePlayer.Pause(); samplePlayer.Source = null;
        sampleState = SampleState.Stopped;
        VoiceSampleSession.End(nativeSampleOwner);
        UpdateSampleControls();
        if (report) status.Text = "Sample stopped.";
    }
    private static string VoiceKey(string provider) => provider switch
    {
        "sapi5" => "sapi5Voice",
        "piper" => "piperVoice",
        _ => "windowsCoreVoice"
    };
    private void FilterVoices()
    {
        var provider = (mode.SelectedItem as SpeechEngineOption)?.Id ?? "windows-core";
        var key = provider == "gemini" ? "geminiVoice" : VoiceKey(provider);
        var selected = SettingsStore.GetString(draft[key], provider == "gemini" ? "Kore" : "").Trim();
        var filtered = voices;
        voice.ItemsSource = filtered;
        var chosen = filtered.FirstOrDefault(v => v.Available && string.Equals(v.Id, selected, StringComparison.OrdinalIgnoreCase))
            ?? (provider == "gemini" ? filtered.FirstOrDefault(v => v.Id == "Kore") ?? filtered.FirstOrDefault() : filtered.FirstOrDefault(v => v.Available && v.Group != VoiceGroups.Heading));
        voice.SelectedItem = chosen;
        if (provider == "gemini" && chosen != null) draft["geminiVoice"] = chosen.Id;
    }
    private async Task RefreshVoices()
    {
        var request = ++voiceRequest;
        voiceCancellation?.Cancel();
        if (closed) return;
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token);
        voiceCancellation = cancellation;
        var provider = (mode.SelectedItem as SpeechEngineOption)?.Id ?? "windows-core";
        voice.Header = provider switch
        {
            "gemini" => "Gemini voice",
            "sapi5" => "Windows SAPI 5 voice",
            "piper" => "Downloaded Piper voice",
            _ => "Windows OneCore voice"
        };
        localSpeechControls.Visibility = provider is "gemini" or "sarvam" or "elevenlabs" ? Visibility.Collapsed : Visibility.Visible;
        sapiHost.Visibility = provider == "sapi5" && DeveloperModeEnabled ? Visibility.Visible : Visibility.Collapsed;
        UpdateVoiceEngineVisibility(provider);
        UpdateAdditionalSpeechVisibility(provider);
        UpdateSampleControls();
        voice.ItemsSource = null;
        voices = new();
        try
        {
            if (provider is "sarvam" or "elevenlabs") return;
            if (provider == "gemini")
            {
                voices = GeminiVoices.Featured.ToList();
                var savedGemini = SettingsStore.GetString(draft["geminiVoice"], "Kore").Trim();
                if (savedGemini.Length > 0 && !voices.Any(v => string.Equals(v.Id, savedGemini, StringComparison.OrdinalIgnoreCase)))
                    voices.Add(new Voice(savedGemini, savedGemini, "", ""));
                FilterVoices();
                status.Text = $"{voices.Count} Gemini voices available.";
                return;
            }
            if (provider == "piper")
            {
                var savedPiper = SettingsStore.GetString(draft["piperVoice"]).Trim();
                var removed = savedPiper.Length > 0 && !PiperService.IsVoiceDownloaded(savedPiper);
                if (removed)
                {
                    draft["piperVoice"] = "";
                    draft["systemVoice"] = "";
                }
                voices = PiperService.InstalledVoices();
                if (voices.Count > 0) voices.Insert(0, Heading("piper-downloaded", "Downloaded Piper voices"));
                FilterVoices();
                status.Text = removed
                    ? "The previously selected Piper voice was removed. Download it again or choose another voice."
                    : !PiperService.IsRuntimeAvailable
                        ? "The bundled Piper speech engine is missing. Reinstall Vidi Studio, or run script\\fetch-piper.ps1 and build the app again."
                        : voices.Count == 0
                            ? "No Piper voices are downloaded yet. Choose Download or remove voices to add one."
                            : $"{voices.Count - 1} Piper voice{(voices.Count == 2 ? "" : "s")} downloaded and ready to use.";
                return;
            }
            List<Voice> discovered;
            if (provider == "windows-core")
                discovered = SpeechSynthesizer.AllVoices.Select(v => new Voice(v.Id, v.DisplayName, v.Language, v.Gender.ToString())).ToList();
            else
            {
                var host = !string.IsNullOrWhiteSpace(sapiHost.Text) ? sapiHost.Text : model.Engine.SpeechHost;
                discovered = await SpeechService.VoicesAsync(host, provider, cancellation.Token);
            }
            if (closed || request != voiceRequest) return;
            voices = discovered;
            var saved = SettingsStore.GetString(draft[VoiceKey(provider)]);
            if (saved.Length > 0 && !voices.Any(v => v.Id == saved))
            {
                draft[VoiceKey(provider)] = "";
                draft["systemVoice"] = "";
                status.Text = "The previously selected voice is unavailable. Using the system default voice.";
            }
            voices.Insert(0, new Voice("", "System default", "", "")); FilterVoices();
            if (saved.Length == 0 || voices.Any(v => v.Id == saved)) status.Text = $"{voices.Count - 1} installed voices available.";
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            model.Engine.Log(ex.ToString());
            if (!closed && request == voiceRequest)
            {
                voices = new() { new Voice("", "System default", "", "") };
                FilterVoices();
                status.Text = "Voice discovery failed. You can still try the system default voice. " + ex.Message;
            }
        }
        finally { if (ReferenceEquals(voiceCancellation, cancellation)) voiceCancellation = null; }
    }
    private async Task Preview(string text)
    {
        if (VoiceSampleSession.Busy) return;
        soundPreview.Stop();
        if ((mode.SelectedItem as SpeechEngineOption)?.Id == "gemini" && !ConnectivityService.Shared.Available(OnlineService.Gemini)) {
            status.Text = ConnectivityService.Shared.Reason(OnlineService.Gemini); await ShowVoiceWarningAsync(OnlineService.Gemini,status.Text); return;
        }
        StopSample(false);
        sampleCancellation = new();
        var cancellation = sampleCancellation;
        if (string.IsNullOrWhiteSpace(text)) { status.Text = "Enter a sample to speak."; return; }
        if (!VoiceSampleSession.Begin(nativeSampleOwner)) return;
        sampleState = SampleState.Generating; UpdateSampleControls();
        Directory.CreateDirectory(SettingsStore.DirectoryPath);
        var config = Path.Combine(SettingsStore.DirectoryPath, Guid.NewGuid() + ".json");
        var audio = Path.Combine(SettingsStore.DirectoryPath, Guid.NewGuid() + ".wav");
        temporaryFiles.Add(config); temporaryFiles.Add(audio);
        try
        {
            status.Text = "Creating voice sample…";
            var provider = (mode.SelectedItem as SpeechEngineOption)?.Id ?? "windows-core";
            if (provider == "windows-core")
            {
                using var synthesizer = new SpeechSynthesizer();
                var voiceId = SettingsStore.GetString(draft["systemVoice"]);
                if (voiceId.Length > 0) synthesizer.Voice = SpeechSynthesizer.AllVoices.FirstOrDefault(v => v.Id == voiceId)
                    ?? throw new InvalidOperationException("The selected Windows OneCore voice is unavailable. Select Refresh voices.");
                synthesizer.Options.SpeakingRate = Math.Clamp(SettingsStore.GetDouble(draft["systemSpeechRate"], 1), .5, 2);
                synthesizer.Options.AudioPitch = Math.Clamp(SettingsStore.GetDouble(draft["systemSpeechPitch"], 1), .5, 2);
                synthesizer.Options.AudioVolume = Math.Clamp(SettingsStore.GetDouble(draft["systemSpeechVolume"], 1), 0, 1);
                using var stream = await synthesizer.SynthesizeTextToStreamAsync(text).AsTask(cancellation.Token);
                using var input = stream.AsStreamForRead();
                using var output = File.Create(audio);
                await input.CopyToAsync(output, cancellation.Token);
                samplePlayer.Volume = 1;
            }
            else if (provider is "sapi5" or "piper")
            {
                var host = !string.IsNullOrWhiteSpace(sapiHost.Text) ? sapiHost.Text : model.Engine.SpeechHost;
                var voice = SettingsStore.GetString(draft[VoiceKey(provider)], SettingsStore.GetString(draft["systemVoice"]));
                await SpeechService.SynthesizeAsync(host, provider, voice, text,
                    audio, SettingsStore.GetDouble(draft["systemSpeechRate"], 1), cancellation.Token);
                samplePlayer.Volume = Math.Clamp(SettingsStore.GetDouble(draft["systemSpeechVolume"], 1), 0, 1);
            }
            else
            {
                var previewConfig = PreviewConfig();
                var enteredKey = SettingsStore.GetString(draft["apiKey"]).Trim();
                await File.WriteAllTextAsync(config, previewConfig.ToJsonString(Models.Json.Options), cancellation.Token);
                await model.Engine.RunAsync(new[] { "preview-tts", "--text", text, "--output", audio }, cancellation: cancellation.Token, configPath: config,
                    apiKey: enteredKey);
                samplePlayer.Volume = 1;
            }
            if (closed || cancellation.IsCancellationRequested || !ReferenceEquals(sampleCancellation, cancellation)) return;
            if (!File.Exists(audio) || new FileInfo(audio).Length == 0) throw new IOException("The speech engine produced no audio.");
            samplePlayer.Source = MediaSource.CreateFromUri(new Uri(audio));
            sampleState = SampleState.Playing; UpdateSampleControls();
            samplePlayer.Play(); status.Text = "Playing sample.";
        }
        catch (OperationCanceledException) { if (ReferenceEquals(sampleCancellation, cancellation)) StopSample(); }
        catch (Exception ex)
        {
            model.Engine.Log(ex.ToString());
            if (ReferenceEquals(sampleCancellation, cancellation))
            {
                StopSample(false);
                status.Text = $"Sample could not be created: {ex.Message}";
                if ((mode.SelectedItem as SpeechEngineOption)?.Id == "gemini") await ShowVoiceWarningAsync(OnlineService.Gemini,status.Text);
            }
        }
        finally { File.Delete(config); }
    }
}
