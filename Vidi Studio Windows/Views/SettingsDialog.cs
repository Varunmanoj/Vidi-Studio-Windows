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
using Vidi_Studio_Windows.ViewModels;
using Windows.Media.Core;
using Windows.Media.Playback;
using Windows.Media.SpeechSynthesis;
using Windows.Storage.Pickers;

namespace Vidi_Studio_Windows.Views;

public sealed partial class SettingsDialog : Window
{
    private readonly TaskCompletionSource<bool> completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly Grid layout;
    private bool saved;
    private ListView? sectionList;
    private int requestedSection;
#if DEBUG
    internal async Task VerifyWindowThemeAsync()
    {
        var original = model.Settings.Get("theme", "system");
        ApplyWindowTheme("dark");
        await Task.Delay(50);
        if (layout.ActualTheme != ElementTheme.Dark || ((SolidColorBrush)layout.Background).Color.R > 40)
            throw new InvalidOperationException("The Settings window did not switch to dark appearance.");
        ApplyWindowTheme("light");
        await Task.Delay(50);
        if (layout.ActualTheme != ElementTheme.Light || ((SolidColorBrush)layout.Background).Color.R < 200)
            throw new InvalidOperationException("The Settings window did not switch to light appearance.");
        ApplyWindowTheme(original);
    }
    internal async Task VerifyProviderSwitchingAsync()
    {
        NavigateForSmokeTest(1);
        mode.SelectedItem = ((IEnumerable<SpeechEngineOption>)mode.ItemsSource).First(option => option.Id == "sapi5");
        mode.SelectedItem = ((IEnumerable<SpeechEngineOption>)mode.ItemsSource).First(option => option.Id == "windows-core");
        await RefreshVoices();
        if (voices.Count < 2 || (mode.SelectedItem as SpeechEngineOption)?.Id != "windows-core") throw new InvalidOperationException("Core voice refresh did not finish.");
        mode.SelectedItem = ((IEnumerable<SpeechEngineOption>)mode.ItemsSource).First(option => option.Id == "sapi5");
        mode.SelectedItem = ((IEnumerable<SpeechEngineOption>)mode.ItemsSource).First(option => option.Id == "gemini");
        await RefreshVoices();
        await Task.Delay(100);
        if (voices.Count < 30 || voices.Any(v => v.Id == "")) throw new InvalidOperationException("A stale local voice list replaced Gemini settings.");
    }
    internal async Task VerifyLocalVoiceSamplesAsync()
    {
        NavigateForSmokeTest(1);
        foreach (var provider in new[] { "windows-core", "sapi5" })
        {
            mode.SelectedItem = ((IEnumerable<SpeechEngineOption>)mode.ItemsSource).First(option => option.Id == provider);
            await RefreshVoices();
            if (voices.Count < 2) throw new InvalidOperationException(provider + " voice list is empty.");
            await Preview("This is a Vidi Studio voice sample.");
            if (status.Text != "Playing sample.") throw new InvalidOperationException(provider + " sample failed: " + status.Text);
            if (samplePlayButton?.Content as string != "Pause sample") throw new InvalidOperationException("Play did not become Pause.");
            await ToggleSampleAsync("This is a Vidi Studio voice sample.");
            if (sampleState != SampleState.Paused || samplePlayButton.Content as string != "Resume sample")
                throw new InvalidOperationException("Sample did not pause.");
            await ToggleSampleAsync("This is a Vidi Studio voice sample.");
            if (sampleState != SampleState.Playing || samplePlayButton.Content as string != "Pause sample")
                throw new InvalidOperationException("Sample did not resume.");
            StopSample();
            if (sampleState != SampleState.Stopped || samplePlayButton.Content as string != "Play selected voice sample" || sampleStopButton?.IsEnabled != false)
                throw new InvalidOperationException("Stop did not reset the sample controls.");
        }
    }
    internal void NavigateForSmokeTest(int section) { if (sectionList != null) sectionList.SelectedIndex = section; }
    internal int SelectedSectionForSmokeTest => sectionList?.SelectedIndex ?? -1;
#endif
    private readonly StudioViewModel model;
    private readonly JsonObject draft;
    private readonly string originalTheme;
    private readonly MediaPlayer samplePlayer = new();
    private enum SampleState { Stopped, Generating, Playing, Paused }
    private SampleState sampleState;
    private Button? samplePlayButton;
    private Button? sampleStopButton;
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
    private readonly ComboBox voice = new() { Header = "Installed voice", HorizontalAlignment = HorizontalAlignment.Stretch };
    private readonly TextBox search = new() { Header = "Filter voices by name, language or region" };
    private readonly TextBox sapiHost = new() { Header = "Optional SAPI host executable (match your legacy voice architecture)" };
    private List<Voice> voices = new();
    public SettingsDialog(StudioViewModel model, Window window, int initialSection = 0)
    {
        if (initialSection < 0 || initialSection > 6) throw new ArgumentOutOfRangeException(nameof(initialSection));
        requestedSection = initialSection;
        this.model = model; draft = model.Settings.Data.DeepClone().AsObject();
        originalTheme = App.ActiveTheme ?? model.Settings.Get("theme", "system");
        Title = "Vidi Studio Settings";
        WindowAppearance.ApplyIcon(this);
        layout = new Grid { Padding = new Thickness(16), ColumnSpacing = 16 };
        layout.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        layout.RowDefinitions.Add(new RowDefinition());
        layout.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        layout.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(210) });
        layout.ColumnDefinitions.Add(new ColumnDefinition());
        layout.Children.Add(new TextBlock { Text = "Settings", FontSize = 26, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold, Margin = new Thickness(4, 4, 0, 18) });
        var sections = new ListView { SelectionMode = ListViewSelectionMode.Single };
        var labels = new[] { "Appearance", "Narration", "Gemini API", "Developer prompts", "Storage and tools", "Help", "About" };
        var glyphs = new[] { "\uE790", "\uE767", "\uE734", "\uE8F2", "\uE8B7", "\uE897", "\uE946" };
        for (var i = 0; i < labels.Length; i++)
        {
            var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 12 };
            row.Children.Add(new FontIcon { Glyph = glyphs[i], FontSize = 17 });
            row.Children.Add(new TextBlock { Text = labels[i], VerticalAlignment = VerticalAlignment.Center });
            sections.Items.Add(new ListViewItem { Content = row, MinHeight = 42, HorizontalContentAlignment = HorizontalAlignment.Stretch });
        }
        Grid.SetRow(sections, 1);
        sectionList = sections;
        var host = new ScrollViewer(); Grid.SetColumn(host, 1); Grid.SetRow(host, 1); layout.Children.Add(sections); layout.Children.Add(host);
        var footer = new Grid { ColumnSpacing = 12, Margin = new Thickness(0, 12, 0, 0) };
        footer.ColumnDefinitions.Add(new ColumnDefinition()); footer.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto }); footer.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        footer.Children.Add(status);
        var cancel = new Button { Content = "Cancel", MinWidth = 100 }; cancel.Click += (_, _) => Close(); Grid.SetColumn(cancel, 1); footer.Children.Add(cancel);
        var save = new Button { Content = "Save settings", MinWidth = 130, Style = (Style)Application.Current.Resources["AccentButtonStyle"] };
        save.Click += (_, _) => SaveAndClose(); Grid.SetColumn(save, 2); footer.Children.Add(save);
        Grid.SetRow(footer, 2); Grid.SetColumnSpan(footer, 2); layout.Children.Add(footer);
        Content = layout;
        App.ThemeChanged += ReceiveTheme;
        samplePlayer.MediaEnded += (_, _) => DispatcherQueue.TryEnqueue(() =>
        {
            if (closed || sampleState != SampleState.Playing) return;
            samplePlayer.Source = null;
            sampleState = SampleState.Stopped;
            UpdateSampleControls();
            status.Text = "Sample finished.";
        });
        samplePlayer.MediaFailed += (_, _) => DispatcherQueue.TryEnqueue(() =>
        {
            if (closed || sampleState is SampleState.Stopped or SampleState.Generating) return;
            StopSample(false);
            status.Text = "Sample playback failed. Try another voice.";
        });
        // AppWindow sizes are physical pixels; FrameworkElement.ActualWidth is in device-independent pixels.
        AppWindow.Resize(new Windows.Graphics.SizeInt32(Math.Max(940, window.AppWindow.Size.Width - 160), Math.Max(680, window.AppWindow.Size.Height - 120)));
        ReceiveTheme(App.ActiveTheme ?? model.Settings.Get("theme", "system"));
        layout.ActualThemeChanged += (_, _) => UpdateWindowTitleBar();
        sections.SelectionChanged += (_, _) =>
        {
            if (sampleState != SampleState.Stopped) StopSample(false);
            if (host.Content is Panel oldPanel) oldPanel.Children.Clear();
            host.Content = null;
            var panel = new StackPanel { Spacing = 12, Padding = new Thickness(0, 0, 16, 12) };
            host.Content = panel;
            switch (sections.SelectedIndex)
            {
                case 0:
                    BuildAppearance(panel);
                    break;
                case 1: BuildNarration(panel); break;
                case 2: BuildGemini(panel); break;
                case 3:
                    if (draft["prompts"] is JsonObject prompts)
                        foreach (var stage in prompts.ToList())
                        {
                            var fields = new StackPanel { Spacing = 10 };
                            if (stage.Value is JsonObject pair) foreach (var entry in pair.ToList())
                            {
                                var field = new TextBox { Header = entry.Key, Text = entry.Value?.GetValue<string>() ?? "", AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, MinHeight = 130 };
                                var name = entry.Key; field.TextChanged += (_, _) => pair[name] = field.Text; fields.Children.Add(field);
                            }
                            panel.Children.Add(new Expander { Header = stage.Key, Content = fields, HorizontalAlignment = HorizontalAlignment.Stretch });
                        }
                    break;
                case 4:
                    AddText(panel, "Project library", "libraryPath");
                    AddText(panel, "Python executable (with engine dependencies)", "pythonPath", "python.exe");
                    AddExecutablePicker(panel, "FFmpeg executable (blank uses PATH)", "ffmpegPath");
                    AddExecutablePicker(panel, "FFprobe executable (blank uses PATH)", "ffprobePath");
                    var doctor = new Button { Content = "Check saved engine and media tool settings" };
                    doctor.Click += async (_, _) => { await model.RunAsync("doctor", needsProject: false); status.Text = model.Status; }; panel.Children.Add(doctor);
                    var logs = new Button { Content = "Open settings and diagnostic folder" };
                    logs.Click += async (_, _) => { Directory.CreateDirectory(SettingsStore.DirectoryPath); await Windows.System.Launcher.LaunchFolderPathAsync(SettingsStore.DirectoryPath); }; panel.Children.Add(logs);
                    break;
                case 5: BuildHelp(panel); break;
                case 6: BuildAbout(panel); break;
            }
        };
        sections.SelectedIndex = requestedSection;
        // WinUI can restore a ListView's selection while activating a newly created window.
        // Reapply the requested destination after its visual tree is ready.
        layout.Loaded += (_, _) => sections.SelectedIndex = requestedSection;
        Closed += (_, _) =>
        {
            App.ThemeChanged -= ReceiveTheme;
            if (!saved) App.PreviewTheme(originalTheme);
            closed = true; voiceRequest++; lifetime.Cancel(); voiceCancellation?.Cancel(); sampleCancellation?.Cancel(); samplePlayer.Dispose();
            foreach (var file in temporaryFiles) { try { File.Delete(file); } catch (IOException) { } }
            completion.TrySetResult(saved);
        };
    }
    public Task<bool> ShowAsync() { Activate(); return completion.Task; }
    public void SelectSection(int section)
    {
        if (sectionList == null || section < 0 || section >= sectionList.Items.Count)
            throw new ArgumentOutOfRangeException(nameof(section));
        requestedSection = section;
        sectionList.SelectedIndex = section;
    }
    public void Hide() => Close();
    private void SaveAndClose()
    {
        foreach (var item in draft) model.Settings.Data[item.Key] = item.Value?.DeepClone();
        model.Settings.Save(); saved = true; App.CommitTheme(model.Settings.Get("theme", "system")); Close();
    }
    private void ApplyWindowTheme(string theme)
    {
        App.PreviewTheme(theme);
    }
    private void ReceiveTheme(string theme)
    {
        layout.RequestedTheme = theme switch { "light" => ElementTheme.Light, "dark" => ElementTheme.Dark, _ => ElementTheme.Default };
        UpdateWindowTitleBar();
    }
    private void UpdateWindowTitleBar()
    {
        if (AppWindow?.TitleBar is not { } titleBar) return;
        var dark = layout.ActualTheme == ElementTheme.Dark;
        layout.Background = new SolidColorBrush(dark ? Windows.UI.Color.FromArgb(255, 24, 24, 24) : Microsoft.UI.Colors.White);
        titleBar.BackgroundColor = dark ? Microsoft.UI.Colors.Black : Microsoft.UI.Colors.White;
        titleBar.ForegroundColor = dark ? Microsoft.UI.Colors.White : Microsoft.UI.Colors.Black;
        titleBar.ButtonBackgroundColor = titleBar.BackgroundColor;
        titleBar.ButtonForegroundColor = titleBar.ForegroundColor;
    }
    private void AddText(StackPanel panel, string label, string key, string fallback = "")
    {
        var field = new TextBox { Header = label, Text = draft[key]?.GetValue<string>() ?? fallback };
        field.TextChanged += (_, _) => draft[key] = field.Text; panel.Children.Add(field);
    }
    private void AddExecutablePicker(StackPanel panel, string label, string key)
    {
        var row = new Grid { ColumnSpacing = 10 };
        row.ColumnDefinitions.Add(new ColumnDefinition());
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var field = new TextBox { Header = label, Text = draft[key]?.GetValue<string>() ?? "", HorizontalAlignment = HorizontalAlignment.Stretch };
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
        Grid.SetColumn(browse, 1); row.Children.Add(browse); panel.Children.Add(row);
    }
    private void AddPlaybackChoice(StackPanel panel)
    {
        var options = new[] { new PlaybackOption("generated", "Play saved narration audio"),
            new PlaybackOption("ondevice", "Speak with an on-device voice") };
        var field = new ComboBox { Header = "Narration during video playback", ItemsSource = options,
            SelectedItem = options.FirstOrDefault(option => option.Id == (draft["narrationPlayback"]?.GetValue<string>() ?? "generated")) ?? options[0] };
        field.SelectionChanged += (_, _) => { if (field.SelectedItem is PlaybackOption option) draft["narrationPlayback"] = option.Id; };
        panel.Children.Add(field);
    }
    private void AddChoice(StackPanel panel, string label, string key, string[] values)
    {
        var field = new ComboBox { Header = label, ItemsSource = values, SelectedItem = draft[key]?.GetValue<string>() ?? values[0] };
        field.SelectionChanged += (_, _) => { if (field.SelectedItem is string selected) draft[key] = selected; };
        panel.Children.Add(field);
    }
    private void AddSpeechSlider(StackPanel panel, string label, string key, double fallback, double minimum, double maximum)
    {
        var row = new Grid { ColumnSpacing = 12 };
        row.ColumnDefinitions.Add(new ColumnDefinition());
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        row.Children.Add(new TextBlock { Text = label });
        var value = new TextBlock { Text = $"{(draft[key]?.GetValue<double>() ?? fallback):P0}", MinWidth = 52,
            HorizontalAlignment = HorizontalAlignment.Right };
        Grid.SetColumn(value, 1); row.Children.Add(value); panel.Children.Add(row);
        var slider = new Slider { Minimum = minimum, Maximum = maximum,
            Value = Math.Clamp(draft[key]?.GetValue<double>() ?? fallback, minimum, maximum),
            StepFrequency = .05, SmallChange = .05, LargeChange = .25,
            HorizontalAlignment = HorizontalAlignment.Stretch };
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(slider, label);
        slider.ValueChanged += (_, _) => { draft[key] = slider.Value; value.Text = $"{slider.Value:P0}"; };
        panel.Children.Add(slider);
    }
    private bool narrationBuilt;
    private void BuildNarration(StackPanel panel)
    {
        foreach (var element in new FrameworkElement[] { mode, voice, search, sapiHost }) if (element.Parent is Panel parent) parent.Children.Remove(element);
        var options = new[] { new SpeechEngineOption("windows-core", "Windows OneCore voices"), new SpeechEngineOption("sapi5", "SAPI 5 voices"), new SpeechEngineOption("gemini", "Gemini voices") };
        mode.ItemsSource = options;
        mode.SelectedItem = options.FirstOrDefault(option => option.Id == (draft["ttsMode"]?.GetValue<string>() ?? "windows-core")) ?? options[0];
        var initialProvider = (mode.SelectedItem as SpeechEngineOption)?.Id ?? "windows-core";
        if (initialProvider is "windows-core" or "sapi5" && draft[VoiceKey(initialProvider)] == null)
            draft[VoiceKey(initialProvider)] = draft["systemVoice"]?.GetValue<string>() ?? "";
        sapiHost.Text = draft["sapiHostPath"]?.GetValue<string>() ?? "";
        if (!narrationBuilt)
        {
            narrationBuilt = true;
            mode.SelectionChanged += async (_, _) =>
            {
                if (mode.SelectedItem is not SpeechEngineOption selected) return;
                draft["ttsMode"] = selected.Id;
                if (selected.Id != "gemini") draft["systemVoice"] = draft[VoiceKey(selected.Id)]?.GetValue<string>() ?? "";
                await RefreshVoices();
            };
            sapiHost.TextChanged += (_, _) => draft["sapiHostPath"] = sapiHost.Text;
            voice.SelectionChanged += (_, _) =>
            {
                if (voice.SelectedItem is not Voice selected) return;
                var provider = (mode.SelectedItem as SpeechEngineOption)?.Id ?? "windows-core";
                if (provider == "gemini") draft["geminiVoice"] = selected.Id;
                else { draft[VoiceKey(provider)] = selected.Id; draft["systemVoice"] = selected.Id; }
            };
            search.TextChanged += (_, _) => FilterVoices();
        }
        panel.Children.Add(mode); panel.Children.Add(search); panel.Children.Add(voice); panel.Children.Add(sapiHost);
        AddPlaybackChoice(panel);
        panel.Children.Add(new TextBlock { Text = "On-device playback uses local speech without saved project narration. With Gemini selected, on-device playback uses the default Windows OneCore voice. SAPI 5 samples and live playback use rate and volume; pitch is applied when generating saved audio.", TextWrapping = TextWrapping.Wrap });
        var refresh = new Button { Content = "Refresh installed voices" }; refresh.Click += async (_, _) => await RefreshVoices(); panel.Children.Add(refresh);
        AddSpeechSlider(panel, "Speech rate", "systemSpeechRate", 1, .5, 2);
        AddSpeechSlider(panel, "Speech pitch", "systemSpeechPitch", 1, .5, 2);
        AddSpeechSlider(panel, "Speech volume", "systemSpeechVolume", 1, 0, 1);
        var sample = new TextBox { Header = "Voice sample (up to 500 characters)", Text = "Welcome to Vidi Studio. Create accessible video with clear audio descriptions.", MaxLength = 500, TextWrapping = TextWrapping.Wrap };
        panel.Children.Add(sample);
        var controls = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        samplePlayButton = new Button();
        samplePlayButton.Click += async (_, _) => await ToggleSampleAsync(sample.Text);
        controls.Children.Add(samplePlayButton);
        sampleStopButton = new Button { Content = "Stop" };
        sampleStopButton.Click += (_, _) => StopSample();
        controls.Children.Add(sampleStopButton);
        UpdateSampleControls();
        panel.Children.Add(controls);
        _ = RefreshVoices();
    }
    private void UpdateSampleControls()
    {
        if (samplePlayButton != null)
        {
            samplePlayButton.Content = sampleState switch
            {
                SampleState.Generating => "Creating sample…",
                SampleState.Playing => "Pause sample",
                SampleState.Paused => "Resume sample",
                _ => "Play selected voice sample"
            };
            samplePlayButton.IsEnabled = sampleState != SampleState.Generating;
        }
        if (sampleStopButton != null) sampleStopButton.IsEnabled = sampleState != SampleState.Stopped;
    }
    private async Task ToggleSampleAsync(string text)
    {
        if (sampleState == SampleState.Playing)
        {
            samplePlayer.Pause(); sampleState = SampleState.Paused;
            status.Text = "Sample paused."; UpdateSampleControls();
        }
        else if (sampleState == SampleState.Paused)
        {
            samplePlayer.Play(); sampleState = SampleState.Playing;
            status.Text = "Playing sample."; UpdateSampleControls();
        }
        else if (sampleState == SampleState.Stopped) await Preview(text);
    }
    private void StopSample(bool report = true)
    {
        sampleCancellation?.Cancel(); sampleCancellation = null;
        samplePlayer.Pause(); samplePlayer.Source = null;
        sampleState = SampleState.Stopped;
        UpdateSampleControls();
        if (report) status.Text = "Sample stopped.";
    }
    private static string VoiceKey(string provider) => provider == "sapi5" ? "sapi5Voice" : "windowsCoreVoice";
    private void FilterVoices()
    {
        var provider = (mode.SelectedItem as SpeechEngineOption)?.Id ?? "windows-core";
        var selected = draft[provider == "gemini" ? "geminiVoice" : VoiceKey(provider)]?.GetValue<string>() ?? "";
        var filtered = voices.Where(v => v.ToString().Contains(search.Text, StringComparison.CurrentCultureIgnoreCase)).ToList();
        voice.ItemsSource = filtered;
        voice.SelectedItem = filtered.FirstOrDefault(v => v.Id == selected);
    }
    private async Task RefreshVoices()
    {
        var request = ++voiceRequest;
        voiceCancellation?.Cancel();
        if (closed) return;
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token);
        voiceCancellation = cancellation;
        var provider = (mode.SelectedItem as SpeechEngineOption)?.Id ?? "windows-core";
        voice.Header = provider == "gemini" ? "Gemini voice" : provider == "sapi5" ? "SAPI 5 voice" : "Windows OneCore voice";
        sapiHost.Visibility = provider == "sapi5" ? Visibility.Visible : Visibility.Collapsed;
        voice.ItemsSource = null;
        voices = new();
        try
        {
            if (provider == "gemini")
            {
                voices = GeminiVoices.Featured.ToList();
                FilterVoices();
                if (!string.IsNullOrWhiteSpace(draft["apiKey"]?.GetValue<string>()))
                {
                    var geminiVoices = await LoadGeminiVoicesAsync(cancellation.Token);
                    if (closed || request != voiceRequest) return;
                    if (geminiVoices.Count > 0) { voices = geminiVoices; FilterVoices(); }
                }
                status.Text = $"{voices.Count} Gemini voices available.";
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
            var saved = draft[VoiceKey(provider)]?.GetValue<string>() ?? "";
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
        StopSample(false);
        sampleCancellation = new();
        var cancellation = sampleCancellation;
        if (string.IsNullOrWhiteSpace(text)) { status.Text = "Enter a sample to speak."; return; }
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
                var voiceId = draft["systemVoice"]?.GetValue<string>() ?? "";
                if (voiceId.Length > 0) synthesizer.Voice = SpeechSynthesizer.AllVoices.FirstOrDefault(v => v.Id == voiceId)
                    ?? throw new InvalidOperationException("The selected Windows OneCore voice is unavailable. Refresh installed voices.");
                synthesizer.Options.SpeakingRate = Math.Clamp(draft["systemSpeechRate"]?.GetValue<double>() ?? 1, .5, 2);
                synthesizer.Options.AudioPitch = Math.Clamp(draft["systemSpeechPitch"]?.GetValue<double>() ?? 1, .5, 2);
                synthesizer.Options.AudioVolume = Math.Clamp(draft["systemSpeechVolume"]?.GetValue<double>() ?? 1, 0, 1);
                using var stream = await synthesizer.SynthesizeTextToStreamAsync(text).AsTask(cancellation.Token);
                using var input = stream.AsStreamForRead();
                using var output = File.Create(audio);
                await input.CopyToAsync(output, cancellation.Token);
                samplePlayer.Volume = 1;
            }
            else if (provider == "sapi5")
            {
                var host = !string.IsNullOrWhiteSpace(sapiHost.Text) ? sapiHost.Text : model.Engine.SpeechHost;
                await SpeechService.SynthesizeAsync(host, "sapi5", draft["systemVoice"]?.GetValue<string>() ?? "", text,
                    audio, draft["systemSpeechRate"]?.GetValue<double>() ?? 1, cancellation.Token);
                samplePlayer.Volume = Math.Clamp(draft["systemSpeechVolume"]?.GetValue<double>() ?? 1, 0, 1);
            }
            else
            {
                await File.WriteAllTextAsync(config, draft.ToJsonString(Models.Json.Options), cancellation.Token);
                await model.Engine.RunAsync(new[] { "preview-tts", "--text", text, "--output", audio }, cancellation: cancellation.Token, configPath: config);
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
            }
        }
        finally { File.Delete(config); }
    }
}

