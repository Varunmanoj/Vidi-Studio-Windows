using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json.Nodes;
using System.Threading;
using Vidi_Studio_Windows.Services;
using Windows.Storage.Pickers;
using Vidi_Studio_Windows.Models;

namespace Vidi_Studio_Windows.Views;

/// <summary>Shared file selection for New Project and existing-project imports.</summary>
public sealed class ImportAssetsPicker : UserControl
{
    private readonly ComboBox languagePicker = new() { Header = "Language", DisplayMemberPath = "Title", ItemsSource = OutputLanguage.Options };
    private readonly Window owner;
    private readonly StackPanel panel = new() { Spacing = 10 };
    private readonly StackPanel audioPanel = new() { Spacing = 10 };
    private readonly StackPanel originalPanel = new() { Spacing = 10 };
    private readonly ComboBox originalPlayback = new() { Header = "Use original audio", HorizontalAlignment = HorizontalAlignment.Stretch };
    private readonly ComboBox originalTranscription = new() { Header = "Transcription source", HorizontalAlignment = HorizontalAlignment.Stretch };
    private string? originalAudio;
    private readonly ComboBox adTranscriptMode = new() { HorizontalAlignment = HorizontalAlignment.Stretch };
    private readonly ComboBox adAudioMode = new() { HorizontalAlignment = HorizontalAlignment.Stretch };
    private readonly TextBlock validation = new() { TextWrapping = TextWrapping.Wrap };
    private readonly TextBlock combineGuidance = new() { TextWrapping = TextWrapping.Wrap, Opacity = .75 };
    private string? transcript, summary, adTranscript, adAudio;
    private readonly List<Action> refreshRows = new();
    private bool youtube, hasTranscript;
    private readonly ImportScope scope;
    private string? captionProvenance;
    private readonly List<string> captionFiles = new();
    private readonly CancellationTokenSource captionCancellation = new();

    public event EventHandler? SelectionChanged;
    public ImportAssetSelection Selection => new(transcript, summary, adTranscript, adAudio,
        adTranscriptMode.SelectedIndex == 0 ? "narration" : "combined",
        adAudioMode.SelectedIndex == 0 ? "narration" : "fullMix",
        OriginalAudioPath: youtube ? originalAudio : null,
        OriginalAudioPlayback: originalPlayback.SelectedIndex == 1,
        OriginalAudioTranscription: originalTranscription.SelectedIndex == 1, CaptionProvenancePath: captionProvenance, Language: (languagePicker.SelectedItem as OutputLanguage)?.Id ?? "auto");
    public bool CanImport => Selection.HasAny && (adTranscript == null || adTranscriptMode.SelectedIndex != 0 || hasTranscript || transcript != null);

    public ImportAssetsPicker(Window owner, ImportScope scope = ImportScope.All, double width = 640, bool showOriginal = true)
    {
        this.owner = owner;
        this.scope = scope;
        Width = width;
        Content = panel;
        languagePicker.SelectedItem = OutputLanguage.Options.FirstOrDefault(v => v.Id == (owner as MainWindow)?.SettingsProject?.SelectedLanguage) ?? OutputLanguage.Options[0];
        panel.Children.Add(languagePicker);
        panel.Children.Add(new TextBlock { Text = "Assigns a language without translating. Existing results are retained for review.", TextWrapping = TextWrapping.Wrap });
        panel.Children.Add(new TextBlock
        {
            Text = "Choose existing files to copy into the project. SRT, VTT, or TXT for text; Markdown or TXT for summaries. Review text timing before importing.",
            TextWrapping = TextWrapping.Wrap, Opacity = .75
        });
        if (scope is ImportScope.All or ImportScope.Transcript)
            panel.Children.Add(Row("Transcript", new[] { ".srt", ".vtt", ".txt" }, () => transcript, value => { transcript = value; captionProvenance = null; }));
        if (scope is ImportScope.All or ImportScope.Summary)
            panel.Children.Add(Row("Summary", new[] { ".md", ".markdown", ".txt" }, () => summary, value => summary = value));
        if (scope is ImportScope.All or ImportScope.Descriptions)
        {
            panel.Children.Add(Row("AD transcript", new[] { ".srt", ".vtt", ".txt" }, () => adTranscript, value => adTranscript = value));
            AddMode(adTranscriptMode, "AD narration only — editable description segments", "Dialogue + AD combined — one caption/export track");
            ToolTipService.SetToolTip(adTranscriptMode, "Choose whether this AD transcript creates editable descriptions or one combined caption track");
            AutomationProperties.SetName(adTranscriptMode, "AD transcript type");
            panel.Children.Add(adTranscriptMode);
        }
        if (showOriginal && (scope is ImportScope.All or ImportScope.OriginalAudio))
        {
            originalPanel.Children.Add(Row("Original audio", new[] { ".wav", ".mp3", ".m4a", ".aac", ".flac" }, () => originalAudio, value => originalAudio = value));
            originalPlayback.ItemsSource = new[] { "Use for exports only", "Use for playback and exports" };
            originalPlayback.SelectedIndex = 0;
            originalTranscription.ItemsSource = new[] { "YouTube video", "Chosen original audio file" };
            originalTranscription.SelectedIndex = 0;
            originalPanel.Children.Add(originalPlayback);
            originalPanel.Children.Add(originalTranscription);
            originalPanel.Children.Add(new TextBlock { Text = "Timing is checked before import. Differences over 0.1 seconds, or an unknown video duration, require your confirmation.", TextWrapping = TextWrapping.Wrap });
            panel.Children.Add(originalPanel);
            originalPlayback.SelectionChanged += (_, _) => UpdateState();
            originalTranscription.SelectionChanged += (_, _) => UpdateState();
        }
        if (scope is ImportScope.All or ImportScope.ADAudio)
        {
            audioPanel.Children.Add(Row("AD audio", new[] { ".wav", ".mp3", ".m4a", ".aac", ".flac" }, () => adAudio, value => adAudio = value));
            AddMode(adAudioMode, "AD narration audio only — mixed with original audio", "Full AD mix — used directly as the described track");
            ToolTipService.SetToolTip(adAudioMode, "Choose narration-only audio or a full AD mix");
            AutomationProperties.SetName(adAudioMode, "AD audio type");
            audioPanel.Children.Add(adAudioMode);
            audioPanel.Children.Add(combineGuidance);
            panel.Children.Add(audioPanel);
        }
        panel.Children.Add(validation);
        adTranscriptMode.SelectionChanged += (_, _) => UpdateState();
        adAudioMode.SelectionChanged += (_, _) => UpdateState();
        UpdateState();
    }

    private static void AddMode(ComboBox box, params string[] options)
    {
        foreach (var option in options) box.Items.Add(new TextBlock { Text = option, TextWrapping = TextWrapping.Wrap, MaxWidth = 420 });
        box.SelectedIndex = 0;
    }

    public void SetContext(bool isYouTube, bool transcriptExists)
    {
        youtube = isYouTube; hasTranscript = transcriptExists;
        combineGuidance.Text = youtube ? "Import & Combine creates described audio using the selected original soundtrack. A full AD mix is used directly."
            : "Optional Combine creates Original Audio and English - Audio Description tracks that viewers can switch between. Import alone opens the editor without exporting a video.";
        originalPanel.Visibility = youtube ? Visibility.Visible : Visibility.Collapsed;
        audioPanel.Visibility = Visibility.Visible;
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
        panel.Children.Insert(2, new Expander { Header = "Download YouTube Captions", Content = body, HorizontalAlignment = HorizontalAlignment.Stretch });
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
        adTranscriptMode.IsEnabled = adTranscript != null;
        adAudioMode.IsEnabled = adAudio != null;
        originalPlayback.IsEnabled = originalTranscription.IsEnabled = originalAudio != null;
        validation.Text = adTranscript != null && adTranscriptMode.SelectedIndex == 0 && !hasTranscript && transcript == null
            ? "Choose a transcript before importing AD-only descriptions, or choose Dialogue + AD combined."
            : youtube ? "Import & Combine creates described audio. A full AD mix is used directly." : "";
        SelectionChanged?.Invoke(this, EventArgs.Empty);
    }

    private Grid Row(string label, string[] extensions, Func<string?> get, Action<string?> set)
    {
        var grid = new Grid { ColumnSpacing = 8 };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(90) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var name = new TextBlock { Text = "None", TextTrimming = TextTrimming.CharacterEllipsis, VerticalAlignment = VerticalAlignment.Center };
        var choose = new Button { Content = "Choose…", Padding = new Thickness(8, 4, 8, 4) };
        var remove = new Button { Content = new FontIcon { Glyph = "\uE711", FontSize = 12 }, Visibility = Visibility.Collapsed, Padding = new Thickness(7) };
        ToolTipService.SetToolTip(choose, "Choose a " + label.ToLowerInvariant() + " file");
        AutomationProperties.SetName(choose, "Choose " + label);
        ToolTipService.SetToolTip(remove, "Remove selected " + label.ToLowerInvariant());
        AutomationProperties.SetName(remove, "Remove selected " + label.ToLowerInvariant());
        void Refresh()
        {
            var path = get();
            name.Text = path == null ? "None" : Path.GetFileName(path);
            ToolTipService.SetToolTip(name, path ?? "No file selected");
            remove.Visibility = path == null ? Visibility.Collapsed : Visibility.Visible;
            UpdateState();
        }
        choose.Click += async (_, _) =>
        {
            try
            {
                var picker = new FileOpenPicker();
                foreach (var extension in extensions) picker.FileTypeFilter.Add(extension);
                WinRT.Interop.InitializeWithWindow.Initialize(picker, WinRT.Interop.WindowNative.GetWindowHandle(owner));
                var file = await picker.PickSingleFileAsync();
                if (file == null) return; // Keep the old selection when the picker is cancelled.
                set(file.Path); Refresh();
            }
            catch (Exception ex) { validation.Text = "The file chooser could not open: " + ex.Message; }
        };
        remove.Click += (_, _) => { set(null); Refresh(); };
        refreshRows.Add(Refresh);
        grid.Children.Add(new TextBlock { Text = label, VerticalAlignment = VerticalAlignment.Center });
        Grid.SetColumn(name, 1); grid.Children.Add(name);
        Grid.SetColumn(choose, 2); grid.Children.Add(choose);
        Grid.SetColumn(remove, 3); grid.Children.Add(remove);
        return grid;
    }

#if DEBUG
    internal void SetFilesForSmokeTest(ImportAssetSelection selection)
    {
        transcript = selection.TranscriptPath; summary = selection.SummaryPath;
        adTranscript = selection.ADTranscriptPath; adAudio = selection.ADAudioPath;
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
    private readonly CheckBox replace = new() { Content = "Replace existing assets after validation" };
    public ImportAssetSelection Selection => picker.Selection with { ReplaceExisting = replace.IsChecked == true };
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
        picker.EnableYouTubeCaptions(project);
        picker.SelectionChanged += (_, _) => UpdateState();
        var panel = new StackPanel { Spacing = 14, Width = 640 };
        panel.Children.Add(picker);
        ToolTipService.SetToolTip(replace, "Replace existing assets only after the import passes validation");
        panel.Children.Add(replace);
        Content = new ScrollViewer { Content = panel, MaxHeight = 600, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };
        PrimaryButtonClick += (_, args) => args.Cancel = !picker.CanImport;
        SecondaryButtonClick += (_, args) => { args.Cancel = !picker.CanImport; CombineRequested = !args.Cancel; };
        UpdateState();
    }

    public void CleanupYouTubeCaptions() => picker.CleanupYouTubeCaptions();

    private void UpdateState()
    {
        IsPrimaryButtonEnabled = picker.CanImport;
        SecondaryButtonText = picker.Selection.ADAudioPath == null ? "" : "Import & Combine";
        IsSecondaryButtonEnabled = picker.CanImport;
    }
}
