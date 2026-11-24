using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Markup;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using Vidi_Studio_Windows.Services;
using Windows.Storage;
using Windows.Storage.Pickers;
using Vidi_Studio_Windows.Models;

namespace Vidi_Studio_Windows.Views;

/// <summary>Shared file selection for New Project and existing-project imports.</summary>
public sealed class ImportAssetsPicker : UserControl
{
    private readonly Window owner;
    private readonly StackPanel panel = new() { Spacing = 10 };
    private readonly StackPanel textPanel = new() { Spacing = 0 };
    private FrameworkElement? originalSection;
    private readonly StackPanel audioPanel = new() { Spacing = 10 };
    private readonly StackPanel originalPanel = new() { Spacing = 10 };
    private readonly ComboBox originalPlayback = new() { Header = "Use original audio", HorizontalAlignment = HorizontalAlignment.Stretch };
    private readonly ComboBox originalTranscription = new() { Header = "Transcription source", HorizontalAlignment = HorizontalAlignment.Stretch };
    private string? originalAudio, localPlaybackVideo;
    private readonly RadioButtons adTranscriptMode = new() { Header = "AD transcript type", HorizontalAlignment = HorizontalAlignment.Stretch, Margin = new Thickness(12) };
    private readonly RadioButtons adAudioMode = new() { Header = "Audio type", HorizontalAlignment = HorizontalAlignment.Stretch, Margin = new Thickness(12) };
    private readonly TextBlock validation = new() { TextWrapping = TextWrapping.Wrap };
    private readonly TextBlock combineGuidance = new() { TextWrapping = TextWrapping.Wrap, FontSize = 12, Opacity = .75, Margin = new Thickness(12, 0, 12, 12) };
    private string? transcript, summary, adTranscript, adAudio;
    private readonly List<Action> refreshRows = new();
    private readonly List<Action> refreshAvailability = new();
    private bool youtube, hasTranscript;
    private bool hasSavedPlaybackVideo, hasSavedOriginalAudio;
    private bool CanChooseOriginalAudio => !hasSavedPlaybackVideo && localPlaybackVideo == null;
    private bool CanChoosePlaybackVideo => !hasSavedOriginalAudio && originalAudio == null;
    private readonly ImportScope scope;
    private string? captionProvenance;
    private readonly List<string> captionFiles = new();
    private readonly CancellationTokenSource captionCancellation = new();

    public event EventHandler? SelectionChanged;
    public ImportAssetSelection Selection => new(transcript, summary, adTranscript, adAudio,
        adTranscriptMode.SelectedIndex == 0 ? "narration" : "combined",
        adAudioMode.SelectedIndex == 0 ? "narration" : "fullMix",
        OriginalAudioPath: youtube && CanChooseOriginalAudio ? originalAudio : null,
        OriginalAudioPlayback: originalPlayback.SelectedIndex == 1,
        OriginalAudioTranscription: originalTranscription.SelectedIndex == 1, CaptionProvenancePath: captionProvenance,
        Language: (owner as MainWindow)?.SettingsProject?.SelectedLanguage, LocalPlaybackVideoPath: localPlaybackVideo);
    public bool CanImport => Selection.HasAny && (localPlaybackVideo == null || CanChoosePlaybackVideo) &&
        (adTranscript == null || adTranscriptMode.SelectedIndex != 0 || hasTranscript || transcript != null);

    public ImportAssetsPicker(Window owner, ImportScope scope = ImportScope.All, double width = 640, bool showOriginal = true)
    {
        this.owner = owner;
        this.scope = scope;
        Width = width;
        Content = panel;
        if (showOriginal && (scope is ImportScope.All or ImportScope.OriginalAudio))
        {
            originalPanel.Children.Add(Row("Original audio", new[] { ".wav", ".mp3", ".m4a", ".aac", ".flac" }, () => originalAudio, value => originalAudio = value,
                canChoose: () => CanChooseOriginalAudio, blockedMessage: () => hasSavedPlaybackVideo
                    ? "Remove the local playback video to import original audio." : "Clear the local playback video selection to choose audio."));
            originalPlayback.ItemsSource = new[] { "Use for exports only", "Use for playback and exports" };
            originalPlayback.SelectedIndex = 0;
            originalTranscription.ItemsSource = new[] { "YouTube video", "Chosen original audio file" };
            originalTranscription.SelectedIndex = 0;
            originalPanel.Children.Add(originalPlayback);
            originalPanel.Children.Add(originalTranscription);
            originalSection = Section("Original audio", originalPanel);
            panel.Children.Add(originalSection);
            originalPlayback.SelectionChanged += (_, _) => UpdateState();
            originalTranscription.SelectionChanged += (_, _) => UpdateState();
        }
        if (scope is ImportScope.All or ImportScope.Transcript)
            textPanel.Children.Add(Row("Transcript", new[] { ".srt", ".vtt", ".txt" }, () => transcript, value => { transcript = value; captionProvenance = null; }));
        if (scope is ImportScope.All or ImportScope.Summary)
            textPanel.Children.Add(Row("Summary", new[] { ".md", ".markdown", ".txt" }, () => summary, value => summary = value));
        if (scope is ImportScope.All or ImportScope.Descriptions)
        {
            textPanel.Children.Add(Row("AD transcript", new[] { ".srt", ".vtt", ".txt" }, () => adTranscript, value => adTranscript = value));
            AddMode(adTranscriptMode, "AD narration only", "Dialogue + AD combined");
            ToolTipService.SetToolTip(adTranscriptMode, "Choose editable descriptions or one combined caption track");
            AutomationProperties.SetName(adTranscriptMode, "AD transcript type");
            textPanel.Children.Add(adTranscriptMode);
        }
        if (textPanel.Children.Count > 0) panel.Children.Add(Section("Text assets", textPanel));
        if (scope is ImportScope.All or ImportScope.ADAudio)
        {
            audioPanel.Children.Add(Row("AD audio", new[] { ".wav", ".mp3", ".m4a", ".aac", ".flac" }, () => adAudio, value => adAudio = value));
            AddMode(adAudioMode, "AD narration audio only", "Full AD mix");
            ToolTipService.SetToolTip(adAudioMode, "Choose narration-only audio or a full AD mix");
            AutomationProperties.SetName(adAudioMode, "Audio type");
            audioPanel.Children.Add(adAudioMode);
            audioPanel.Children.Add(combineGuidance);
            panel.Children.Add(Section("AD audio", audioPanel));
        }
        panel.Children.Add(validation);
        adTranscriptMode.SelectionChanged += (_, _) => UpdateState();
        adAudioMode.SelectionChanged += (_, _) => UpdateState();
        UpdateState();
    }

    private static FrameworkElement Section(string title, UIElement content, bool showHeading = true)
    {
        var heading = new TextBlock { Text = title, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold, Margin = new Thickness(2, 8, 0, 2) };
        AutomationProperties.SetHeadingLevel(heading, Microsoft.UI.Xaml.Automation.Peers.AutomationHeadingLevel.Level2);
        var section = new StackPanel { Spacing = 6 };
        if (showHeading) section.Children.Add(heading);
        var card = (Border)XamlReader.Load("<Border xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation' CornerRadius='8' BorderThickness='1' Background='{ThemeResource CardBackgroundFillColorDefaultBrush}' BorderBrush='{ThemeResource CardStrokeColorDefaultBrush}'/>");
        card.Padding = content is ComboBox ? new Thickness(12) : new Thickness(0);
        card.Child = content;
        section.Children.Add(card);
        return section;
    }

    private static void AddMode(RadioButtons box, params string[] options)
    {
        var group = Guid.NewGuid().ToString("N");
        foreach (var option in options) {
            var index = box.Items.Count;
            var choice = new RadioButton { GroupName = group, Content = new TextBlock { Text = option, TextWrapping = TextWrapping.Wrap, MaxWidth = 420 } };
            AutomationProperties.SetName(choice, option);
            choice.Checked += (_, _) => { if (box.SelectedIndex != index) box.SelectedIndex = index; };
            box.Items.Add(choice);
        }
        box.SelectedIndex = 0;
    }

    public void SetContext(bool isYouTube, bool transcriptExists)
    {
        youtube = isYouTube; hasTranscript = transcriptExists;
        audioPanel.Visibility = Visibility.Visible;
        UpdateState();
    }

    public void EnableLocalPlaybackVideo(StudioProject project, Func<Window> pickerOwner)
    {
        hasSavedPlaybackVideo = project.YouTubePlaybackVideo != null;
        hasSavedOriginalAudio = project.OriginalAudio != null;
        UpdateState();
        if (!project.IsYouTube || scope != ImportScope.All) return;
        var row = Row("", Array.Empty<string>(), () => localPlaybackVideo, value => localPlaybackVideo = value,
            chooseTitle: "Choose Local Playback Video…", clearTitle: "Clear Local Playback Video Selection",
            savedName: project.YouTubePlaybackVideo?.SourceName, pickFile: () => NewProjectDialog.PickVideoAsync(pickerOwner()),
            canChoose: () => CanChoosePlaybackVideo, blockedMessage: () => hasSavedOriginalAudio
                ? "Remove the original audio file to choose a video." : "Clear the original audio selection to choose a video.");
        panel.Children.Insert(0, Section("Local playback video", row));
        UpdateState();
    }

    public void EnableYouTubeCaptions(StudioProject project)
    {
        if (!project.IsYouTube || scope is not (ImportScope.All or ImportScope.Transcript) || YouTubeService.ProjectWatchURL(project.YoutubeURL) is not { } url) return;
        var videoID = new Uri(url).Query.TrimStart('?').Split('&').First(v => v.StartsWith("v="))[2..];
        var service = YouTubeService.Shared;
        var body = new StackPanel { Spacing = 8 };
        var account = new ComboBox { Header = "Caption account", ItemsSource = service.Accounts.ToArray(), SelectedItem = service.Selected, HorizontalAlignment = HorizontalAlignment.Stretch };
        var tracks = new ComboBox { Header = "Caption track", HorizontalAlignment = HorizontalAlignment.Stretch };
        var load = new Button { Content = "Load Caption Tracks" };
        var download = new Button { Content = "Use Selected YouTube Captions", IsEnabled = false };
        var status = new TextBlock { Text = "Caption downloads require video-edit permission. Confirm Import to save the selected transcript.", TextWrapping = TextWrapping.Wrap };
        AutomationProperties.SetLiveSetting(status, Microsoft.UI.Xaml.Automation.Peers.AutomationLiveSetting.Polite);
        var spinner = new ProgressRing { IsActive = false, Width = 24, Height = 24, Visibility = Visibility.Collapsed };
        bool busy = false;
        void State(bool loading) {
            busy = loading; account.IsEnabled = !loading;
            load.IsEnabled = !loading && account.SelectedItem is YouTubeAccount;
            download.IsEnabled = !loading && tracks.SelectedItem is YouTubeCaption { Status: "serving" };
            spinner.IsActive = loading; spinner.Visibility = loading ? Visibility.Visible : Visibility.Collapsed;
        }
        account.SelectionChanged += (_, _) => { tracks.ItemsSource = null; State(busy); };
        tracks.SelectionChanged += (_, _) => State(busy);
        load.Click += async (_, _) => {
            if (account.SelectedItem is not YouTubeAccount connection) return;
            State(true); status.Text = "Loading YouTube caption tracks…";
            try {
                var result = await service.CaptionsAsync(videoID, connection.Id, captionCancellation.Token);
                if (captionCancellation.IsCancellationRequested || account.SelectedItem is not YouTubeAccount current || current.Id != connection.Id) return;
                tracks.ItemsSource = result;
                status.Text = result.Count == 0 ? "No caption tracks were returned for this video." : "Choose a language and ready track, then use it as the transcript.";
            } catch (OperationCanceledException) { } catch (Exception ex) { status.Text = ex.Message; }
            finally { State(false); }
        };
        download.Click += async (_, _) => {
            if (account.SelectedItem is not YouTubeAccount connection || tracks.SelectedItem is not YouTubeCaption track) return;
            State(true); status.Text = "Downloading YouTube captions…";
            try {
                var data = await service.DownloadAsync(track, connection.Id, captionCancellation.Token);
                captionCancellation.Token.ThrowIfCancellationRequested();
                if (account.SelectedItem is not YouTubeAccount current || current.Id != connection.Id) return;
                var input = Path.Combine(Path.GetTempPath(), "vidistudio-caption-" + Guid.NewGuid() + ".srt");
                var provenance = Path.ChangeExtension(input, ".json");
                captionFiles.Add(input); captionFiles.Add(provenance);
                await File.WriteAllBytesAsync(input, data, captionCancellation.Token);
                await File.WriteAllTextAsync(provenance, new JsonObject { ["videoID"] = videoID, ["trackID"] = track.Id, ["language"] = track.Language, ["name"] = track.Name, ["trackKind"] = track.Kind }.ToJsonString(), captionCancellation.Token);
                captionCancellation.Token.ThrowIfCancellationRequested();
                transcript = input; captionProvenance = provenance;
                foreach (var refresh in refreshRows) refresh();
                status.Text = "Selected: YouTube captions · " + track.Language + " · " + (track.Kind == "ASR" ? "Automatic" : "Standard") + ". Confirm Import to save; existing content has not changed.";
            } catch (OperationCanceledException) { } catch (Exception ex) { status.Text = ex.Message; }
            finally { State(false); }
        };
        body.Children.Add(account); body.Children.Add(tracks); body.Children.Add(load); body.Children.Add(download); body.Children.Add(spinner); body.Children.Add(status);
        if (service.Accounts.Count == 0) status.Text = "Connect a Google account in Settings > YouTube Accounts, then reopen this import dialog. Video-edit permission is required.";
        textPanel.Children.Add(new Expander { Header = "Download YouTube Captions", Content = body, HorizontalAlignment = HorizontalAlignment.Stretch });
        Unloaded += (_, _) => captionCancellation.Cancel();
        State(false);
    }

    public void CleanupYouTubeCaptions()
    {
        captionCancellation.Cancel();
        foreach (var file in captionFiles) { try { File.Delete(file); } catch (IOException) { } catch (UnauthorizedAccessException) { } }
    }

    private void UpdateState()
    {
        if (originalSection != null)
            originalSection.Visibility = youtube ? Visibility.Visible : Visibility.Collapsed;
        foreach (var refresh in refreshAvailability) refresh();
        combineGuidance.Text = youtube && CanChooseOriginalAudio ? "Combine: described audio."
            : "Combine: selectable Original Audio and Audio Description tracks.";
        adTranscriptMode.Visibility = adTranscript != null ? Visibility.Visible : Visibility.Collapsed;
        adAudioMode.Visibility = combineGuidance.Visibility = adAudio != null ? Visibility.Visible : Visibility.Collapsed;
        originalPlayback.IsEnabled = originalTranscription.IsEnabled = CanChooseOriginalAudio && originalAudio != null;
        validation.Text = adTranscript != null && adTranscriptMode.SelectedIndex == 0 && !hasTranscript && transcript == null
            ? "Choose a transcript before importing AD-only descriptions, or choose Dialogue + AD combined."
            : "";
        validation.Visibility = string.IsNullOrEmpty(validation.Text) ? Visibility.Collapsed : Visibility.Visible;
        SelectionChanged?.Invoke(this, EventArgs.Empty);
    }

    private Border Row(string label, string[] extensions, Func<string?> get, Action<string?> set,
        string? chooseTitle = null, string? clearTitle = null, string? savedName = null, Func<Task<StorageFile?>>? pickFile = null,
        Func<bool>? canChoose = null, Func<string>? blockedMessage = null)
    {
        var grid = new Grid { ColumnSpacing = 8 };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var name = new TextBlock { MaxWidth = 220, Text = savedName ?? (chooseTitle == null ? "None" : "No file selected"), TextTrimming = TextTrimming.CharacterEllipsis, VerticalAlignment = VerticalAlignment.Center };
        var choose = new Button { Content = chooseTitle ?? "Choose…", Padding = new Thickness(8, 4, 8, 4) };
        var remove = new Button { Content = new FontIcon { Glyph = "\uE711", FontSize = 12 }, Visibility = Visibility.Collapsed, Padding = new Thickness(7) };
        ToolTipService.SetToolTip(choose, chooseTitle ?? "Choose a " + label.ToLowerInvariant() + " file");
        AutomationProperties.SetName(choose, chooseTitle ?? "Choose " + label);
        ToolTipService.SetToolTip(remove, clearTitle ?? "Remove selected " + label.ToLowerInvariant());
        AutomationProperties.SetName(remove, clearTitle ?? "Remove selected " + label.ToLowerInvariant());
        MenuFlyoutItem? chooseItem = null, clearItem = null;
        void RefreshAvailability()
        {
            var allowed = canChoose?.Invoke() != false;
            choose.IsEnabled = allowed;
            if (chooseItem != null) chooseItem.IsEnabled = allowed;
            var message = allowed ? null : blockedMessage?.Invoke();
            var path = get();
            name.Text = path == null ? message ?? savedName ?? (chooseTitle == null ? "None" : "No file selected") : Path.GetFileName(path);
            ToolTipService.SetToolTip(name, path ?? message ?? savedName ?? "No file selected");
        }
        void Refresh()
        {
            var path = get();
            RefreshAvailability();
            remove.Visibility = path == null ? Visibility.Collapsed : Visibility.Visible;
            if (clearItem != null) clearItem.Visibility = remove.Visibility;
            UpdateState();
        }
        async Task ChooseFileAsync()
        {
            if (canChoose?.Invoke() == false) return;
            try
            {
                StorageFile? file;
                if (pickFile != null) file = await pickFile();
                else
                {
                    var picker = new FileOpenPicker();
                    foreach (var extension in extensions) picker.FileTypeFilter.Add(extension);
                    WinRT.Interop.InitializeWithWindow.Initialize(picker, WinRT.Interop.WindowNative.GetWindowHandle(owner));
                    file = await picker.PickSingleFileAsync();
                }
                if (file == null || canChoose?.Invoke() == false) return;
                set(file.Path); Refresh();
            }
            catch (Exception ex) { validation.Text = "The file chooser could not open: " + ex.Message; validation.Visibility = Visibility.Visible; }
        }
        choose.Click += async (_, _) => await ChooseFileAsync();
        remove.Click += (_, _) => { set(null); Refresh(); };
        refreshRows.Add(Refresh);
        if (canChoose != null) refreshAvailability.Add(RefreshAvailability);
        if (label.Length > 0) grid.Children.Add(new TextBlock { Text = label, VerticalAlignment = VerticalAlignment.Center });
        Grid.SetColumn(name, label.Length == 0 ? 0 : 1); grid.Children.Add(name);
        Grid.SetColumn(choose, 2); grid.Children.Add(choose);
        Grid.SetColumn(remove, 3); grid.Children.Add(remove);
        var row = (Border)XamlReader.Load("<Border xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation' Padding='12,10' BorderThickness='0,0,0,1' BorderBrush='{ThemeResource DividerStrokeColorDefaultBrush}'/>");
        row.Child = grid;
        if (chooseTitle != null && clearTitle != null)
        {
            chooseItem = new MenuFlyoutItem { Text = chooseTitle, Icon = MenuIcons.Create("choose-playback-video") };
            chooseItem.Click += async (_, _) => await ChooseFileAsync();
            clearItem = new MenuFlyoutItem { Text = clearTitle, Icon = MenuIcons.Create(clearTitle), Visibility = Visibility.Collapsed };
            clearItem.Click += (_, _) => { set(null); Refresh(); };
            row.ContextFlyout = new MenuFlyout { Items = { chooseItem, clearItem } };
        }
        return row;
    }

#if DEBUG
    internal void VerifyPlaybackSelectionForSmokeTest()
    {
        var section = panel.Children.OfType<StackPanel>().Single(item => item.Children.OfType<TextBlock>()
            .Any(heading => heading.Text == "Local playback video"));
        var row = (Border)section.Children.OfType<Border>().Single().Child;
        var actions = (MenuFlyout)row.ContextFlyout;
        var videoChooser = ((Grid)row.Child).Children.OfType<Button>()
            .Single(button => AutomationProperties.GetName(button) == "Choose Local Playback Video…");
        var audioChooser = originalPanel.Children.OfType<Border>().Select(item => item.Child).OfType<Grid>()
            .SelectMany(item => item.Children.OfType<Button>()).Single(button => AutomationProperties.GetName(button) == "Choose Original audio");
        if (!ReferenceEquals(panel.Children[0], section) || CanImport ||
            !videoChooser.IsEnabled || !audioChooser.IsEnabled ||
            actions.Items.Count != 2 || actions.Items[1].Visibility != Visibility.Collapsed)
            throw new InvalidOperationException("Import Existing Assets is missing the scoped playback picker and actions.");
        try
        {
            SetFilesForSmokeTest(new ImportAssetSelection(LocalPlaybackVideoPath: "matching.mp4"));
            if (!CanImport || Selection.LocalPlaybackVideoPath != "matching.mp4" ||
                audioChooser.IsEnabled || actions.Items[1].Visibility != Visibility.Visible)
                throw new InvalidOperationException("The staged local video did not enable Import and disable original audio selection.");
            SetFilesForSmokeTest(new ImportAssetSelection(OriginalAudioPath: "original.wav"));
            if (!CanImport || !audioChooser.IsEnabled || videoChooser.IsEnabled || ((MenuFlyoutItem)actions.Items[0]).IsEnabled)
                throw new InvalidOperationException("Staging original audio left a playback file chooser enabled.");
        }
        finally { SetFilesForSmokeTest(new ImportAssetSelection()); }
        if (CanImport || !audioChooser.IsEnabled || !videoChooser.IsEnabled)
            throw new InvalidOperationException("Clearing the staged files did not restore both choosers.");
    }

    internal async Task VerifyGroupedLayoutForSmokeTestAsync()
    {
        var before = Selection;
        var sections = panel.Children.OfType<StackPanel>().Where(child => child.Visibility == Visibility.Visible).ToArray();
        var titles = sections.Select(section => section.Children.OfType<TextBlock>().SingleOrDefault() ??
            ((Grid)section.Children.OfType<Border>().Single().Child).Children.OfType<TextBlock>().Single()).ToArray();
        if (!titles.Select(title => title.Text).SequenceEqual(new[] { "Text assets", "AD audio" }) ||
            titles.Any(title => AutomationProperties.GetHeadingLevel(title) != Microsoft.UI.Xaml.Automation.Peers.AutomationHeadingLevel.Level2) ||
            adTranscriptMode.Visibility != Visibility.Collapsed || adAudioMode.Visibility != Visibility.Collapsed || combineGuidance.Visibility != Visibility.Collapsed)
            throw new InvalidOperationException("Empty import should expose grouped headings and no inactive type choices or combine text.");
        if (panel.Children.OfType<ComboBox>().Any(c => AutomationProperties.GetName(c) == "Language"))
            throw new InvalidOperationException("Imports must inherit the project language without another language picker.");
        try
        {
            SetFilesForSmokeTest(new ImportAssetSelection(TranscriptPath: "dialogue.srt", ADTranscriptPath: "description.srt", ADAudioPath: "narration.wav"));
            await Task.Delay(100); UpdateLayout();
            if (adTranscriptMode.Visibility != Visibility.Visible || adAudioMode.Visibility != Visibility.Visible || combineGuidance.Visibility != Visibility.Visible ||
                combineGuidance.Text != "Combine: selectable Original Audio and Audio Description tracks.")
                throw new InvalidOperationException("Type choices and concise language-neutral combine outcome must follow file selection.");
            foreach (var group in new[] { adTranscriptMode, adAudioMode })
            {
                if (group.ContainerFromIndex(1) is not RadioButton choice ||
                    Microsoft.UI.Xaml.Automation.Peers.FrameworkElementAutomationPeer.CreatePeerForElement(choice)?.GetPattern(Microsoft.UI.Xaml.Automation.Peers.PatternInterface.SelectionItem)
                    is not Microsoft.UI.Xaml.Automation.Provider.ISelectionItemProvider provider)
                    throw new InvalidOperationException("Import type choice must expose native radio selection semantics.");
                provider.Select();
            }
            if (Selection.ADTranscriptMode != "combined" || Selection.ADAudioMode != "fullMix")
                throw new InvalidOperationException("Native radio choices did not preserve import modes.");
        }
        finally { SetFilesForSmokeTest(before); }
    }

    internal void SetFilesForSmokeTest(ImportAssetSelection selection)
    {
        transcript = selection.TranscriptPath; summary = selection.SummaryPath;
        adTranscript = selection.ADTranscriptPath; adAudio = selection.ADAudioPath;
        originalAudio = selection.OriginalAudioPath;
        localPlaybackVideo = selection.LocalPlaybackVideoPath;
        adTranscriptMode.SelectedIndex = selection.ADTranscriptMode == "narration" ? 0 : 1;
        adAudioMode.SelectedIndex = selection.ADAudioMode == "narration" ? 0 : 1;
        foreach (var refresh in refreshRows) refresh();
        UpdateState();
    }
#endif
}

public sealed class ImportAssetsDialog : ContentDialog
{
    private readonly ImportAssetsPicker picker;
    internal Window? CompanionWindow { get; set; }
    public ImportAssetSelection Selection => picker.Selection;
    public bool CombineRequested { get; private set; }

    public ImportAssetsDialog(Window owner, StudioProject project, ImportScope scope = ImportScope.All)
    {
        Title = scope switch { ImportScope.Transcript => "Import Transcript", ImportScope.Summary => "Import Summary", ImportScope.Descriptions => "Import Descriptions", ImportScope.OriginalAudio => "Import Original Audio", ImportScope.ADAudio => "Import AD Audio", _ => "Import Existing Assets" };
        PrimaryButtonText = "Import"; CloseButtonText = "Cancel";
        DefaultButton = ContentDialogButton.Primary;
        Width = MaxWidth = 740;
        Resources["ContentDialogMaxWidth"] = 740d;
        picker = new ImportAssetsPicker(owner, scope);
        picker.SetContext(project.IsYouTube, project.Transcript.Count > 0);
        picker.EnableLocalPlaybackVideo(project, () => CompanionWindow ?? owner);
        picker.EnableYouTubeCaptions(project);
        picker.SelectionChanged += (_, _) => UpdateState();
        var panel = new StackPanel { Spacing = 14, Width = 640 };
        panel.Children.Add(new TextBlock { Text = project.Name, TextTrimming = TextTrimming.CharacterEllipsis, Opacity = .75 });
        panel.Children.Add(picker);
        Content = new ScrollViewer { Content = panel, MaxHeight = 600, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };
        PrimaryButtonClick += (_, args) => args.Cancel = !picker.CanImport;
        SecondaryButtonClick += (_, args) => { args.Cancel = !picker.CanImport; CombineRequested = !args.Cancel; };
        UpdateState();
    }

#if DEBUG
    internal Task VerifyGroupedLayoutForSmokeTestAsync() => picker.VerifyGroupedLayoutForSmokeTestAsync();
    internal void VerifyPlaybackSelectionForSmokeTest() => picker.VerifyPlaybackSelectionForSmokeTest();
#endif

    public void CleanupYouTubeCaptions() => picker.CleanupYouTubeCaptions();

    private void UpdateState()
    {
        IsPrimaryButtonEnabled = picker.CanImport;
        SecondaryButtonText = picker.Selection.ADAudioPath == null ? "" : "Import & Combine";
        IsSecondaryButtonEnabled = picker.CanImport;
    }
}
