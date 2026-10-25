using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Vidi_Studio_Windows.Models;
using Vidi_Studio_Windows.Services;
using Vidi_Studio_Windows.ViewModels;
using Windows.Media.Core;
using Windows.Media.Playback;
using Windows.Storage.Pickers;
using Windows.System;

namespace Vidi_Studio_Windows;

public sealed partial class MainWindow : Window
{
    public StudioViewModel Model { get; } = new();
    private readonly MediaPlayer video = new();
    private readonly MediaPlayer speech = new();
    private readonly MediaPlayer narrationPlayer = new();
    private readonly SummaryReadback readback;
    private readonly OnDeviceNarrator onDeviceNarrator;
    private readonly DispatcherTimer timer = new() { Interval = TimeSpan.FromMilliseconds(150) };
    private bool updatingPosition;
    private string? loadedProject;
    private string? activeCue;
    private double youtubePosition;
    private bool youtubePlaying;
    private bool wasPlaying;
    private bool youtubePolling;
    private bool youtubeMetadataUpdating;
    private bool ready;
    private string? pendingVideoPath;
    private string? pendingYouTubeUrl;
    private Segment? highlightedSegment;
    private Segment? previewingSegment;
    private readonly HashSet<Segment> activeSegmentRows = new();
    private bool timelineScrubbing;
    private bool playheadDragging;
    private Views.SettingsDialog? openSettings;
    private double Position => Model.Project?.IsYouTube == true ? youtubePosition : video.PlaybackSession.Position.TotalSeconds;
    public MainWindow(string? folder = null)
    {
        readback = new SummaryReadback(Model.Engine, Model.Settings);
        onDeviceNarrator = new OnDeviceNarrator(Model.Settings, Model.Engine);
        onDeviceNarrator.Failed += message => Model.ReportStatus(message);
        InitializeComponent();
        ActiveExports.Observe(Model);
        Model.PropertyChanged += UpdateStatusFooter;
        Views.WindowAppearance.ApplyIcon(this);
        App.ThemeChanged += ApplyThemeFromApp;
        BuildMenus();
        video.PlaybackSession.PlaybackStateChanged += (_, _) => DispatcherQueue.TryEnqueue(UpdatePlaybackControls);
        speech.MediaEnded += (_, _) => DispatcherQueue.TryEnqueue(ClearSegmentPreview);
        InitializePresentation();
        UpdateApiKeyUi();
        Root.ActualThemeChanged += (_, _) => UpdateTitleBarTheme(Root.ActualTheme);
        readback.SentenceChanged += sentence => DispatcherQueue.TryEnqueue(() => { ReadbackSentence.Text = sentence; HighlightReadbackSentence(sentence); });
        readback.WordChanged += (start, length) => DispatcherQueue.TryEnqueue(() => HighlightReadbackWord(start, length));
        readback.StateChanged += _ => DispatcherQueue.TryEnqueue(UpdateSummaryControls);
        if (AppWindow.Presenter is Microsoft.UI.Windowing.OverlappedPresenter presenter) presenter.Maximize();
        Player.SetMediaPlayer(video);
        // Any saved project change or a newly started video ends summary playback.
        Model.ProjectReloaded += (_, _) => { readback.Stop(); RefreshProject(); };
        timer.Tick += Timer_Tick;
        timer.Start();
        Closed += (_, _) =>
        {
            App.ThemeChanged -= ApplyThemeFromApp;
            ActiveExports.Detach();
            Model.PropertyChanged -= UpdateStatusFooter;
            Model.Cancel(); timer.Stop();
            foreach (var cleanup in new Action[] { video.Dispose, speech.Dispose, narrationPlayer.Dispose, onDeviceNarrator.Dispose, readback.Dispose,
                () => { if (YouTubePlayer.CoreWebView2 != null) YouTubePlayer.Close(); } })
            {
                try { cleanup(); } catch (Exception ex) { Model.Engine.Log(ex.ToString()); }
            }
        };
        Root.Loaded += async (_, _) =>
        {
            ready = true;
            Model.Reload();
            ApplyTheme();
            if (Model.Settings.Get("showPreview", "true") == "false") PreviewPanel.Visibility = Visibility.Collapsed;
            if (folder != null) Model.Open(folder);
            if (Model.Settings.Get("projectPresentation") == "grid") ToggleGrid_Click(this, new RoutedEventArgs());
            UpdateApiKeyUi();
            UpdateSectionOverlay(Model.IsAnalyzing);
            if (pendingVideoPath is { } vidPath)
            {
                pendingVideoPath = null;
                await OpenVideoFileAsync(vidPath);
            }
            else if (pendingYouTubeUrl is { } ytUrl)
            {
                pendingYouTubeUrl = null;
                await OpenYouTubeUrlAsync(ytUrl);
            }
        };
#if DEBUG
        Root.Loaded += async (_, _) => { if (Environment.GetEnvironmentVariable("VIDISTUDIO_UI_SMOKE_REPORT") is { } report && System.Threading.Interlocked.CompareExchange(ref smokeTestClaimed, 1, 0) == 0) await RunSmokeTestAsync(report); };
#endif
    }
    private void AddShortcut(VirtualKey key, VirtualKeyModifiers modifiers, RoutedEventHandler handler)
    {
        var shortcut = new KeyboardAccelerator { Key = key, Modifiers = modifiers };
        shortcut.Invoked += (_, e) => { if (Model.Idle) handler(this, new RoutedEventArgs()); e.Handled = true; };
        Root.KeyboardAccelerators.Add(shortcut);
    }
    private void UpdateStatusFooter(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(Model.Busy) or nameof(Model.Idle) or nameof(Model.IsAnalyzing)
            or nameof(Model.IsSynthesizingNarration) or nameof(Model.NarratingSegmentId)
            or nameof(Model.NarratingSegmentIndex) or nameof(Model.ActiveNarrationStatusText)
            or nameof(Model.ProcessingSection) or nameof(Model.VideoPreparation) or nameof(Model.Status))
            UpdateProcessingChrome();
        if (e.PropertyName is nameof(Model.Busy) or nameof(Model.HasApiKey) or nameof(Model.Idle) or nameof(Model.IsSynthesizingNarration))
            UpdateApiKeyUi();
    }
    private void UpdateProcessingChrome()
    {
        var analyzing = Model.IsAnalyzing;
        var synthesizing = Model.IsSynthesizingNarration;
        var activeStage = analyzing || synthesizing;
        if (StatusRing != null)
        {
            StatusRing.IsActive = activeStage;
            StatusRing.Visibility = activeStage ? Visibility.Visible : Visibility.Collapsed;
        }
        if (StatusStageTitle != null)
        {
            StatusStageTitle.Text = activeStage ? Model.ProcessingStageTitle : "";
            StatusStageTitle.Visibility = activeStage ? Visibility.Visible : Visibility.Collapsed;
        }
        if (CancelProcessingButton != null)
        {
            CancelProcessingButton.Visibility = activeStage ? Visibility.Visible : Visibility.Collapsed;
        }
        if (InspectorNarrationProgressPanel != null)
        {
            InspectorNarrationProgressPanel.Visibility = synthesizing ? Visibility.Visible : Visibility.Collapsed;
            InspectorNarrationRing.IsActive = synthesizing;
            InspectorNarrationStatusText.Text = synthesizing ? Model.ActiveNarrationStatusText : "";
        }
        if (InspectorSynthesizeNarrationLabel != null)
        {
            InspectorSynthesizeNarrationLabel.Text = synthesizing
                ? (Model.NarratingSegmentIndex is { } idx ? $"Synthesizing AD {idx}…" : "Synthesizing…")
                : Model.Project?.AllADAudioTitle ?? "Generate All AD Audio";
        }
        if (OverviewNarrationProgressPanel != null)
        {
            OverviewNarrationProgressPanel.Visibility = synthesizing ? Visibility.Visible : Visibility.Collapsed;
            OverviewNarrationRing.IsActive = synthesizing;
            OverviewNarrationStatusText.Text = synthesizing ? Model.ActiveNarrationStatusText : "";
        }
        // Keep the editor navigable while an analysis or narration stage runs;
        // other long operations keep the previous workspace lock.
        if (EditorWorkspace != null)
        {
            EditorWorkspace.IsEnabled = Model.Idle || analyzing || synthesizing;
        }
        UpdateSectionOverlay(analyzing);
        UpdateDesktopMenuStates();
    }
    private void UpdateSectionOverlay(bool analyzing)
    {
        if (!ready || SectionProcessingRing == null || SectionProcessingOverlay == null) return;
        var state = analyzing ? CurrentSectionState() : SectionProcessingState.Idle;
        if (state == SectionProcessingState.Idle)
        {
            SectionProcessingRing.IsActive = false;
            SectionProcessingOverlay.Visibility = Visibility.Collapsed;
            return;
        }
        SectionProcessingRing.IsActive = true;
        SectionProcessingTitle.Text = state == SectionProcessingState.Queued ? "Waiting to generate…" : Model.ProcessingStageTitle;
        SectionProcessingStatus.Text = state == SectionProcessingState.Queued
            ? "This section updates automatically when its turn arrives. You can keep working in other sections."
            : Model.Status;
        SectionProcessingOverlay.Visibility = Visibility.Visible;
    }
    private SectionProcessingState CurrentSectionState()
    {
        IReadOnlyList<string> keys = Section.SelectedIndex switch
        {
            0 => new[] { "transcript" },
            1 => new[] { "summary" },
            2 => new[] { "descriptions" },
            3 => new[] { "descriptions" },
            4 => new[] { "transcript", "descriptions" },
            _ => Array.Empty<string>()
        };
        return Model.StateFor(keys);
    }
    private void ApplyTheme()
    {
        if (App.ActiveTheme is { } active) ApplyThemeFromApp(active);
        else PreviewTheme(Model.Settings.Get("theme", "system"));
    }

    internal void PreviewTheme(string theme) => App.PreviewTheme(theme);

    private void ApplyThemeFromApp(string theme)
    {
        var requested = theme switch { "light" => ElementTheme.Light, "dark" => ElementTheme.Dark, _ => ElementTheme.Default };
        Root.RequestedTheme = requested;
        UpdateTitleBarTheme(requested == ElementTheme.Default ? Root.ActualTheme : requested);
    }

    private void UpdateTitleBarTheme(ElementTheme theme)
    {
        if (AppWindow?.TitleBar is not { } titleBar) return;
        var dark = theme == ElementTheme.Dark;
        var background = dark ? Windows.UI.Color.FromArgb(255, 32, 32, 32) : Windows.UI.Color.FromArgb(255, 243, 243, 243);
        var foreground = dark ? Microsoft.UI.Colors.White : Microsoft.UI.Colors.Black;
        var inactive = dark ? Windows.UI.Color.FromArgb(255, 170, 170, 170) : Windows.UI.Color.FromArgb(255, 90, 90, 90);
        var hover = dark ? Windows.UI.Color.FromArgb(255, 62, 62, 62) : Windows.UI.Color.FromArgb(255, 224, 224, 224);
        var pressed = dark ? Windows.UI.Color.FromArgb(255, 82, 82, 82) : Windows.UI.Color.FromArgb(255, 205, 205, 205);
        titleBar.BackgroundColor = background;
        titleBar.ForegroundColor = foreground;
        titleBar.InactiveBackgroundColor = background;
        titleBar.InactiveForegroundColor = inactive;
        titleBar.ButtonBackgroundColor = background;
        titleBar.ButtonForegroundColor = foreground;
        titleBar.ButtonInactiveBackgroundColor = background;
        titleBar.ButtonInactiveForegroundColor = inactive;
        titleBar.ButtonHoverBackgroundColor = hover;
        titleBar.ButtonHoverForegroundColor = foreground;
        titleBar.ButtonPressedBackgroundColor = pressed;
        titleBar.ButtonPressedForegroundColor = foreground;
    }
    private ContentDialog Dialog(string title, object content, string action = "Save") => new()
    { Title = title, Content = content, PrimaryButtonText = action, CloseButtonText = "Cancel", DefaultButton = ContentDialogButton.Primary };
    private Task<ContentDialogResult> ShowDialogAsync(ContentDialog dialog, int width = 680, int height = 460, bool fitToContent = false)
        => Views.WindowedDialog.ShowAsync(dialog, this, width, height, fitToContent);
    private async Task<bool> Confirm(string title, string text, string action = "Continue") => await ShowDialogAsync(Dialog(title, new TextBlock { Text = text, TextWrapping = TextWrapping.Wrap, MaxWidth = 520 }, action)) == ContentDialogResult.Primary;
    private async Task ShowError(Exception ex)
    {
        Model.Engine.Log(ex.ToString());
        // Processing and speech messages are already written for people; other failures stay generic.
        var message = ex is ProcessingException or SpeechFailedException
            ? ex.Message
            : "Check your settings and the diagnostic log, then retry. Your saved project remains available.";
        await ShowDialogAsync(new ContentDialog { Title = "Action could not finish", Content = new TextBlock { Text = message, TextWrapping = TextWrapping.Wrap, MaxWidth = 520 }, CloseButtonText = "Close" });
    }
    private async Task ShowErrorMessage(string title, string message)
    {
        await ShowDialogAsync(new ContentDialog
        {
            Title = title,
            Content = new TextBlock { Text = message, TextWrapping = TextWrapping.Wrap, MaxWidth = 520 },
            CloseButtonText = "OK"
        });
    }
    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(IntPtr hWnd);
    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);
    public void BringToForeground()
    {
        try
        {
            var hWnd = WinRT.Interop.WindowNative.GetWindowHandle(this);
            if (hWnd != IntPtr.Zero)
            {
                const int SW_RESTORE = 9;
                ShowWindow(hWnd, SW_RESTORE);
                SetForegroundWindow(hWnd);
            }
        }
        catch { }
    }
    public async Task OpenVideoFileAsync(string videoPath)
    {
        if (string.IsNullOrWhiteSpace(videoPath)) return;
        if (!ready)
        {
            pendingVideoPath = videoPath;
            return;
        }

        Activate();
        BringToForeground();

        if (!File.Exists(videoPath))
        {
            await ShowErrorMessage("Video Not Found", $"The selected video file could not be found:\n{videoPath}");
            return;
        }

        if (Model.Busy)
        {
            await ShowErrorMessage("Vidi Studio Is Busy", "Wait for the current operation to finish, then open the video again.");
            return;
        }

        try
        {
            Model.Reload();
            var existing = Model.ExistingProjectForVideo(videoPath);
            if (existing != null)
            {
                Model.Open(existing.Folder);
                return;
            }

            await Model.RunAsync("import-video", new[] { "--video", videoPath, "--library", Model.Settings.Get("libraryPath") }, false);
        }
        catch (Exception ex)
        {
            await ShowError(ex);
        }
    }
    public async Task OpenYouTubeUrlAsync(string url)
    {
        if (string.IsNullOrWhiteSpace(url)) return;
        if (!ready)
        {
            pendingYouTubeUrl = url;
            return;
        }

        Activate();
        BringToForeground();

        if (Model.Busy)
        {
            await ShowErrorMessage("Vidi Studio Is Busy", "Wait for the current operation to finish, then open the video again.");
            return;
        }

        try
        {
            Model.Reload();
            var existing = Model.ExistingProjectForYouTube(url);
            if (existing != null)
            {
                Model.Open(existing.Folder);
                return;
            }

            await Model.RunAsync("import-youtube", new[] { "--url", url, "--library", Model.Settings.Get("libraryPath") }, false);
        }
        catch (Exception ex)
        {
            await ShowError(ex);
        }
    }
    private async Task<string?> PickFolder()
    {
        var picker = new FolderPicker(); picker.FileTypeFilter.Add("*");
        WinRT.Interop.InitializeWithWindow.Initialize(picker, WinRT.Interop.WindowNative.GetWindowHandle(this));
        return (await picker.PickSingleFolderAsync())?.Path;
    }
    private async void ImportVideo_Click(object sender, RoutedEventArgs e) => await ShowNewProjectDialogAsync(false);
    private async void OpenProject_Click(object sender, RoutedEventArgs e)
    { if (Model.Busy) return; try { if (await PickFolder() is { } folder) Model.Open(folder); } catch (Exception ex) { await ShowError(ex); } }
    private async void TopOpenFolder_Click(object sender, RoutedEventArgs e)
    {
        if (Model.Project is not { } project) return;
        try { await Launcher.LaunchFolderPathAsync(project.Folder); }
        catch (Exception ex) { await ShowError(ex); }
    }
    private async void YouTube_Click(object sender, RoutedEventArgs e) => await ShowNewProjectDialogAsync(true);
    private async Task ShowNewProjectDialogAsync(bool youtube)
    {
        if (Model.Busy) return;
        try
        {
            var dialog = new Views.NewProjectDialog(this, Root.ActualTheme, Model.Settings, youtube);
            if (await ShowDialogAsync(dialog, 700, 820, fitToContent: true) != ContentDialogResult.Primary) return;
            if (dialog.IsYouTube)
                await Model.RunAsync("import-youtube", new[] { "--url", dialog.YouTubeUrl, "--library", Model.Settings.Get("libraryPath") }, false);
            else if (dialog.VideoPath is { } path)
                await Model.RunAsync("import-video", new[] { "--video", path, "--library", Model.Settings.Get("libraryPath") }, false);
        }
        catch (Exception ex) { await ShowError(ex); }
    }
    private void Search_Changed(object sender, TextChangedEventArgs e) { if (ready) Filter(); }
    private void Filter_Changed(object sender, SelectionChangedEventArgs e) { if (ready) Filter(); }
    private void Filter() => Model.Reload(Search.Text, SourceFilter.SelectedIndex switch { 1 => "local", 2 => "youtube", _ => "all" });
    private void Project_Changed(object sender, SelectionChangedEventArgs e) { if (Model.Idle && ProjectList.SelectedItem is StudioProject p) Model.Open(p.Folder); }
    private async void RefreshProject()
    {
        if (Model.Project is not { } p) return;
        ShowEditor();
        UpdateInspector(p);
        UpdateApiKeyUi();
        ProjectTitle.Text = p.Name;
        ProjectInformation.Text = $"{(p.IsYouTube ? "YouTube" : "Local video")}\n{p.Width} × {p.Height}\n{TimeSpan.FromSeconds(p.Duration):hh\\:mm\\:ss}\n{p.Folder}";
        Playhead.Maximum = Math.Max(1, p.Duration);
        UpdateTimelineCursor();
        UpdatePlaybackControls();
        RenderSection();
        RenderTimelineThumbnails(p);
        if (loadedProject == p.Folder) return;
        loadedProject = p.Folder; activeCue = null; previewingSegment = null; speech.Pause(); narrationPlayer.Pause(); video.Pause(); onDeviceNarrator.Stop(); readback.Stop();
        Player.Visibility = p.IsYouTube ? Visibility.Collapsed : Visibility.Visible;
        YouTubePlayer.Visibility = p.IsYouTube ? Visibility.Visible : Visibility.Collapsed;
        try
        {
            if (p.IsYouTube)
            {
                video.Source = null;
                var url = new Uri(p.YoutubeURL!);
                var id = System.Text.RegularExpressions.Regex.Match(url.Query, @"(?:\?|&)v=([A-Za-z0-9_-]{11})(?:&|$)").Groups[1].Value;
                if (id.Length != 11) throw new InvalidDataException("Invalid YouTube URL.");
                await YouTubePlayer.EnsureCoreWebView2Async();
                YouTubePlayer.CoreWebView2.Settings.AreDevToolsEnabled = false;
                YouTubePlayer.CoreWebView2.SetVirtualHostNameToFolderMapping("vidistudio.local", AppContext.BaseDirectory, Microsoft.Web.WebView2.Core.CoreWebView2HostResourceAccessKind.DenyCors);
                YouTubePlayer.Source = new Uri("https://vidistudio.local/Assets/youtube.html?id=" + id);
            }
            else
            {
                if (YouTubePlayer.CoreWebView2 != null) YouTubePlayer.Source = new Uri("about:blank");
                video.Source = MediaSource.CreateFromUri(new Uri(p.Resolve(p.Source)));
            }
        }
        catch (Exception ex) { await ShowError(ex); }
    }
    private async void Section_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (!ready) return;
        if (summaryEditMode && Model.Project != null)
        {
            await SaveSummaryChangesAsync();
        }
        RenderSection();
        UpdateSectionOverlay(Model.IsAnalyzing);
    }
    private void RenderTimelineThumbnails(StudioProject project)
    {
        Thumbnails.Children.Clear(); Thumbnails.ColumnDefinitions.Clear();
        foreach (var thumbnail in project.Thumbnails)
        {
            try
            {
                var path = project.Resolve(thumbnail);
                if (!File.Exists(path)) continue;
                var tile = new Grid { Height = 42, Background = new Microsoft.UI.Xaml.Media.ImageBrush
                {
                    ImageSource = new BitmapImage(new Uri(path)), Stretch = Microsoft.UI.Xaml.Media.Stretch.UniformToFill,
                    AlignmentX = Microsoft.UI.Xaml.Media.AlignmentX.Center, AlignmentY = Microsoft.UI.Xaml.Media.AlignmentY.Center
                } };
                Grid.SetColumn(tile, Thumbnails.Children.Count);
                Thumbnails.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                Thumbnails.Children.Add(tile);
            }
            catch (Exception ex) { Model.Engine.Log(ex.ToString()); }
        }
        ThumbnailEmpty.Visibility = Thumbnails.Children.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        UpdateTimelineCursor();
    }
    private void RenderSection()
    {
        if (Model.Project is not { } p) return;
        UpdateSectionPresentation();
        Segments.Visibility = Section.SelectedIndex is 0 or 2 or 4 ? Visibility.Visible : Visibility.Collapsed;
        SummaryView.Visibility = Section.SelectedIndex == 1 ? Visibility.Visible : Visibility.Collapsed;
        Overview.Visibility = Visibility.Collapsed;
        ExportList.Visibility = Section.SelectedIndex == 5 ? Visibility.Visible : Visibility.Collapsed;
        Segments.ItemsSource = Section.SelectedIndex switch { 0 => p.Transcript, 2 => p.Cues, _ => p.Transcript.Concat(p.Cues).OrderBy(c => c.Start).ToList() };
        RefreshEditingRows();
        SummaryText.Text = p.Summary;
        RenderMarkdown(p.Summary);
        if (!summaryEditMode)
        {
            SummaryEditorBox.Text = p.Summary ?? "";
            UpdateSummaryWordCount();
        }
        if (Section.SelectedIndex == 1 && readback.State is SummaryReadbackState.Playing or SummaryReadbackState.Paused)
            HighlightReadbackSentence(ReadbackSentence.Text);
        RenderTracks();
        var usable = p.Cues.Count(c => c.AudioReady);
        Overview.Text = $"Audio Description overview\n\n{p.Cues.Count} description segments\n{usable} narration files ready\n{p.Cues.Count - usable} missing or changed\n{p.Cues.Sum(c => c.End - c.Start):F1} seconds of description time";
        PopulateExportGroups(ExportGroups, p);
        foreach (var active in activeSegmentRows) active.IsActive = false;
        activeSegmentRows.Clear();
        highlightedSegment = null;
        UpdateActiveSegment(true);
    }
    private async void Timer_Tick(object? sender, object e)
    {
        var polledProject = Model.Project;
        if (polledProject == null) return;
        double? youtubeDuration = null;
        string? youtubeTitle = null;
        if (polledProject.IsYouTube && YouTubePlayer.CoreWebView2 != null && !youtubePolling)
        {
            youtubePolling = true;
            try
            {
                var state = await YouTubePlayer.ExecuteScriptAsync("window.vidiState ? window.vidiState() : null");
                using var doc = JsonDocument.Parse(state);
                if (doc.RootElement.ValueKind == JsonValueKind.Object)
                {
                    if (!ReferenceEquals(Model.Project, polledProject)) return;
                    youtubePosition = doc.RootElement.GetProperty("time").GetDouble(); youtubePlaying = doc.RootElement.GetProperty("playing").GetBoolean();
                    UpdatePlaybackControls();
                    if (doc.RootElement.TryGetProperty("duration", out var duration) && duration.TryGetDouble(out var seconds) && double.IsFinite(seconds) && seconds > 0) youtubeDuration = seconds;
                    if (doc.RootElement.TryGetProperty("title", out var title)) youtubeTitle = title.GetString();
                }
            }
            catch { /* Player may be navigating. */ }
            finally { youtubePolling = false; }
        }
        if (!ReferenceEquals(Model.Project, polledProject)) return;
        if (!playheadDragging) { updatingPosition = true; Playhead.Value = Position; updatingPosition = false; }
        UpdateTimelineCursor();
        readback.TickWordProgress();
        TimeLabel.Text = $"{TimeSpan.FromSeconds(Position):hh\\:mm\\:ss\\.fff}";
        var playing = Model.Project.IsYouTube ? youtubePlaying : video.PlaybackSession.PlaybackState == MediaPlaybackState.Playing;
        if (playing && !wasPlaying) readback.Stop();
        wasPlaying = playing;
        var targetInterval = playing ? TimeSpan.FromMilliseconds(40) : TimeSpan.FromMilliseconds(150);
        if (timer.Interval != targetInterval) timer.Interval = targetInterval;
        UpdatePlaybackControls();
        var cue = Model.Project.Cues.FirstOrDefault(c => c.Start <= Position && Position < c.End);
        UpdateCaptions();
        UpdateAvailability();
        if (!playing || !Narration.IsOn || cue == null) { narrationPlayer.Pause(); if (activeCue != null) onDeviceNarrator.Stop(); activeCue = null; }
        else if (activeCue != cue.Id)
        {
            narrationPlayer.Pause(); narrationPlayer.Source = null; onDeviceNarrator.Stop();
            activeCue = cue.Id;
            if (Model.Settings.Get("narrationPlayback") == "ondevice") await onDeviceNarrator.SpeakAsync(cue.Text);
            else if (cue.AudioReady && cue.Audio is { } audio)
            { narrationPlayer.Source = MediaSource.CreateFromUri(new Uri(Model.Project.Resolve(audio))); narrationPlayer.PlaybackSession.Position = TimeSpan.FromSeconds(Math.Max(0, Position - cue.Start)); narrationPlayer.Play(); }
        }
        if (playing && Model.Settings.Get("narrationPlayback") == "ondevice" && Model.Settings.Get("ttsMode") == "piper")
        {
            // Render the next cue while the current one plays so Piper starts on time.
            var nextCue = Model.Project.Cues.FirstOrDefault(c => c.Start > Position && c.Start < Position + 20);
            if (nextCue != null) onDeviceNarrator.Prepare(nextCue.Text);
        }        UpdateActiveSegment();
        if (!youtubeMetadataUpdating && Model.Idle && ReferenceEquals(Model.Project, polledProject) && youtubeDuration is { } durationSeconds && Math.Abs(durationSeconds - polledProject.Duration) > .5)
        {
            youtubeMetadataUpdating = true;
            try
            {
                var arguments = new List<string> { "--duration", durationSeconds.ToString(CultureInfo.InvariantCulture) };
                if (!string.IsNullOrWhiteSpace(youtubeTitle)) arguments.AddRange(new[] { "--name", youtubeTitle });
                await Model.RunAsync("update-metadata", arguments);
            }
            finally { youtubeMetadataUpdating = false; }
        }
    }
    private async void Seek(double value)
    {
        if (Model.Project == null) return;
        value = Math.Clamp(value, 0, Model.Project.Duration); activeCue = null; narrationPlayer.Pause(); onDeviceNarrator.Stop();
        if (Model.Project?.IsYouTube == true && YouTubePlayer.CoreWebView2 != null)
        { youtubePosition = value; await YouTubePlayer.ExecuteScriptAsync($"window.vidiSeek?.({value.ToString(CultureInfo.InvariantCulture)})"); }
        else video.PlaybackSession.Position = TimeSpan.FromSeconds(value);
        updatingPosition = true; Playhead.Value = value; updatingPosition = false;
        UpdateTimelineCursor(); UpdateCaptions(value); UpdateActiveSegment(true);
    }
    private void Playhead_Changed(object sender, Microsoft.UI.Xaml.Controls.Primitives.RangeBaseValueChangedEventArgs e) { if (ready && !updatingPosition) Seek(e.NewValue); }
    private void Playhead_PointerPressed(object sender, PointerRoutedEventArgs e) => playheadDragging = true;
    private void Playhead_PointerReleased(object sender, PointerRoutedEventArgs e) { playheadDragging = false; StartVideoPlayback(); }
    private void Playhead_PointerCaptureLost(object sender, PointerRoutedEventArgs e) => playheadDragging = false;
    private void TimelineScrubSurface_SizeChanged(object sender, SizeChangedEventArgs e) => UpdateTimelineCursor();
    private void UpdateTimelineCursor()
    {
        var duration = Model.Project?.Duration ?? 0;
        var hasDuration = double.IsFinite(duration) && duration > 0;
        ThumbnailCursor.Visibility = hasDuration ? Visibility.Visible : Visibility.Collapsed;
        TimelineCursor.Visibility = hasDuration ? Visibility.Visible : Visibility.Collapsed;
        if (hasDuration && ThumbnailScrubSurface.ActualWidth > 0)
            Canvas.SetLeft(ThumbnailCursor, Math.Clamp(Position / duration * ThumbnailScrubSurface.ActualWidth - ThumbnailCursor.Width / 2,
                0, Math.Max(0, ThumbnailScrubSurface.ActualWidth - ThumbnailCursor.Width)));
        var width = TimelineScrubSurface.ActualWidth;
        if (!hasDuration || width <= 0) return;
        Microsoft.UI.Xaml.Controls.Canvas.SetLeft(TimelineCursor, Math.Clamp(Math.Clamp(Position / duration, 0, 1) * width - TimelineCursor.Width / 2, 0, Math.Max(0, width - TimelineCursor.Width)));
    }
    private void ScrubTimeline(FrameworkElement surface, PointerRoutedEventArgs e)
    {
        if (Model.Project is not { Duration: > 0 } project || surface.ActualWidth <= 0) return;
        var fraction = Math.Clamp(e.GetCurrentPoint(surface).Position.X / surface.ActualWidth, 0, 1);
        Seek(fraction * project.Duration);
    }
    private void TimelineScrubSurface_PointerPressed(object sender, PointerRoutedEventArgs e)
    {
        if (Model.Project == null) return;
        var surface = (FrameworkElement)sender;
        timelineScrubbing = surface.CapturePointer(e.Pointer);
        ScrubTimeline(surface, e);
        StartVideoPlayback();
        e.Handled = true;
    }
    private void TimelineScrubSurface_PointerMoved(object sender, PointerRoutedEventArgs e)
    { if (timelineScrubbing) { ScrubTimeline((FrameworkElement)sender, e); e.Handled = true; } }
    private void TimelineScrubSurface_PointerReleased(object sender, PointerRoutedEventArgs e)
    { if (timelineScrubbing) ScrubTimeline((FrameworkElement)sender, e); timelineScrubbing = false; ((FrameworkElement)sender).ReleasePointerCapture(e.Pointer); e.Handled = true; }
    private void TimelineScrubSurface_PointerCaptureLost(object sender, PointerRoutedEventArgs e) => timelineScrubbing = false;
    private async void StartVideoPlayback()
    {
        if (Model.Project?.IsYouTube == true)
        {
            if (YouTubePlayer.CoreWebView2 != null)
            {
                await YouTubePlayer.ExecuteScriptAsync($"window.vidiSeek?.({youtubePosition.ToString(CultureInfo.InvariantCulture)}); window.vidiPlay?.()");
                youtubePlaying = true;
                UpdatePlaybackControls();
            }
        }
        else if (Model.Project != null) { video.Play(); UpdatePlaybackControls(); }
    }
    private void Follow_Toggled(object sender, RoutedEventArgs e)
    {
        if (!ready) return;
        if (!Follow.IsOn)
        {
            foreach (var active in activeSegmentRows) active.IsActive = false;
            activeSegmentRows.Clear();
        }
        else UpdateActiveSegment(true);
    }
    private void UpdateActiveSegment(bool forceScroll = false)
    {
        if (Section.SelectedIndex is not (0 or 2 or 4) || Segments.ItemsSource is not IEnumerable<Segment> rows) return;
        var currentRows = rows.Where(c => c.Start <= Position && Position < c.End).ToArray();
        var current = currentRows.FirstOrDefault();
        var nextActive = Follow.IsOn ? currentRows.ToHashSet() : new HashSet<Segment>();
        foreach (var previous in activeSegmentRows)
            if (!nextActive.Contains(previous)) previous.IsActive = false;
        foreach (var active in nextActive) active.IsActive = true;
        activeSegmentRows.Clear();
        activeSegmentRows.UnionWith(nextActive);
        if (!ReferenceEquals(current, highlightedSegment))
        {
            highlightedSegment = current;
            forceScroll = true;
        }
        if (current == null) return;
        if (Follow.IsOn && forceScroll) Segments.ScrollIntoView(current);
    }
    private async void PlayPause_Click(object sender, RoutedEventArgs e)
    {
        if (Model.Project == null) return;
        if (Model.Project.IsYouTube)
        {
            if (YouTubePlayer.CoreWebView2 == null) return;
            await YouTubePlayer.ExecuteScriptAsync("window.vidiToggle?.()");
            youtubePlaying = !youtubePlaying;
        }
        else if (video.PlaybackSession.PlaybackState == MediaPlaybackState.Playing) video.Pause(); else video.Play();
        UpdatePlaybackControls();
    }
    private bool IsVideoPlaying() => Model.Project?.IsYouTube == true ? youtubePlaying
        : Model.Project != null && video.PlaybackSession.PlaybackState == MediaPlaybackState.Playing;
    private void UpdatePlaybackControls()
    {
        var playing = IsVideoPlaying();
        VideoPlayPauseButton.Content = playing ? "\uE769" : "\uE768";
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(VideoPlayPauseButton, playing ? "Pause video" : "Play video");
        ToolTipService.SetToolTip(VideoPlayPauseButton, playing ? "Pause video" : "Play video");
        UpdateDesktopPlaybackState();
    }
    private void Rewind_Click(object sender, RoutedEventArgs e) => Seek(Position - 5);
    private void Forward_Click(object sender, RoutedEventArgs e) => Seek(Position + 5);
    private async void Segment_DoubleTapped(object sender, DoubleTappedRoutedEventArgs e)
    {
        if (Segments.SelectedItem is Segment segment)
        {
            if (editMode)
                await EditSegment(segment);
            else
                Seek(segment.Start);
        }
    }
    private async void Command_Click(object sender, RoutedEventArgs e)
    {
        var command = (string)((FrameworkElement)sender).Tag;
        if (Model.Project == null) return;
        if (command == "synthesize-tts")
        {
            await SynthesizeNarrationWithConfirmationAsync();
            return;
        }
        if (!Model.HasApiKey && command != "prepare-video")
        {
            await ShowSettingsSectionAsync(2);
            return;
        }
        if (command is "analyze" or "transcribe" or "generate-summary" or "generate-ad-cues")
            if (!await Confirm("Generate content", "This replaces existing content for the selected stages and may remove superseded narration or exports. Manual edits in those stages will be replaced. Continue?")) return;
        await Model.RunAsync(command);
    }
    private void Cancel_Click(object sender, RoutedEventArgs e) => Model.Cancel();
    private async void Rename_Click(object sender, RoutedEventArgs e)
    {
        if (Model.Project == null) return;
        var name = new TextBox { Header = "Project name", Text = Model.Project.Name };
        if (await ShowDialogAsync(Dialog("Rename project", name)) == ContentDialogResult.Primary) await Model.RunAsync("rename-project", new[] { "--name", name.Text });
    }
    private async void DeleteProject_Click(object sender, RoutedEventArgs e)
    {
        if (Model.Project is not { } p || !await Confirm("Move project to Recycle Bin", $"Move {p.Name} and all its media to the Recycle Bin?", "Move to Recycle Bin")) return;
        try
        {
            StopProjectPlayback(); video.Source = null; speech.Source = null; narrationPlayer.Source = null;
            Microsoft.VisualBasic.FileIO.FileSystem.DeleteDirectory(p.Folder, Microsoft.VisualBasic.FileIO.UIOption.OnlyErrorDialogs, Microsoft.VisualBasic.FileIO.RecycleOption.SendToRecycleBin);
            ClearProject();
        }
        catch (Exception ex) { await ShowError(ex); }
    }
    private async void Reveal_Click(object sender, RoutedEventArgs e) { if (Model.Project != null) await Launcher.LaunchFolderPathAsync(Model.Project.Folder); }
    private void SelectAll_Click(object sender, RoutedEventArgs e) { if (!editMode) EditMode_Click(sender, e); Segments.SelectAll(); }
    private void Deselect_Click(object sender, RoutedEventArgs e) => Segments.SelectedItems.Clear();
    private async void Add_Click(object sender, RoutedEventArgs e) => await EditSegment(null);
    private async void SegmentEditRow_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is Segment segment)
            await EditSegment(segment);
    }
    private async void Edit_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is Segment tagged)
        {
            await EditSegment(tagged);
            return;
        }
        if (Segments.SelectedItems.Count > 0)
        {
            foreach (var segment in Segments.SelectedItems.Cast<Segment>().ToList())
                await EditSegment(segment);
            return;
        }
        if (Segments.SelectedItem is Segment single)
        {
            await EditSegment(single);
        }
    }
    private async Task EditSegment(Segment? existing, string? initialText = null)
    {
        if (Model.Project is not { } p) return;
        var isDescription = existing?.IsDescription ?? (Section.SelectedIndex != 0);
        var maxDuration = p.Duration;
        var initialStart = existing?.Start ?? Position;
        var initialEnd = existing?.End ?? Math.Min(maxDuration, initialStart + (isDescription ? 3.0 : 4.0));
        if (initialEnd <= initialStart) initialEnd = Math.Min(maxDuration, initialStart + 1.0);

        var title = existing == null
            ? (isDescription ? "Add Audio Description Segment" : "Add Transcript Segment")
            : $"Edit {(isDescription ? "Audio Description" : "Transcript")} Segment";

        var startPicker = new Views.TimecodePicker
        {
            Header = "Start time",
            Value = initialStart,
            Minimum = 0,
            Maximum = maxDuration
        };
        startPicker.SetCurrentPositionProvider(() => Position, "Set start time to current playhead position");

        var durationText = new TextBlock
        {
            Text = $"Duration {TimeSpan.FromSeconds(Math.Max(0, initialEnd - initialStart)):hh\\:mm\\:ss\\.fff} ({(Math.Max(0, initialEnd - initialStart)):F3}s)",
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Center,
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
            FontSize = 13,
            Foreground = Application.Current.Resources["TextFillColorSecondaryBrush"] as Brush,
            FontFamily = new FontFamily("Consolas, Segoe UI")
        };

        var endPicker = new Views.TimecodePicker
        {
            Header = "End time",
            Value = initialEnd,
            Minimum = 0,
            Maximum = maxDuration
        };
        endPicker.SetCurrentPositionProvider(() => Position, "Set end time to current playhead position");

        var timingHeader = new Grid();
        timingHeader.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        timingHeader.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var timingTitle = new TextBlock
        {
            Text = "Timing",
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
            FontSize = 15,
            VerticalAlignment = VerticalAlignment.Center
        };
        Grid.SetColumn(timingTitle, 0);
        Grid.SetColumn(durationText, 1);
        timingHeader.Children.Add(timingTitle);
        timingHeader.Children.Add(durationText);

        var timingGrid = new Grid { ColumnSpacing = 16 };
        timingGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        timingGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        Grid.SetColumn(startPicker, 0);
        Grid.SetColumn(endPicker, 1);
        timingGrid.Children.Add(startPicker);
        timingGrid.Children.Add(endPicker);

        var errorIcon = new FontIcon
        {
            Glyph = "\uE783",
            FontSize = 14,
            Foreground = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 239, 68, 68))
        };
        var errorTitle = new TextBlock
        {
            Text = "Timing error",
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
            FontSize = 13,
            Foreground = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 239, 68, 68))
        };
        var errorHeader = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 8,
            Children = { errorIcon, errorTitle }
        };
        var errorList = new StackPanel { Spacing = 4 };
        var errorFooter = new TextBlock
        {
            Text = isDescription
                ? "Adjust the start or end time so the audio description plays only in a clear gap."
                : "Adjust the start or end time so transcript and Audio Description segments do not overlap.",
            FontSize = 12,
            Foreground = Application.Current.Resources["TextFillColorSecondaryBrush"] as Brush,
            TextWrapping = TextWrapping.Wrap
        };
        var errorStack = new StackPanel { Spacing = 6, Children = { errorHeader, errorList, errorFooter } };
        var validationBorder = new Border
        {
            Background = new SolidColorBrush(Windows.UI.Color.FromArgb(30, 239, 68, 68)),
            BorderBrush = new SolidColorBrush(Windows.UI.Color.FromArgb(160, 239, 68, 68)),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(6),
            Padding = new Thickness(12, 10, 12, 10),
            Visibility = Visibility.Collapsed,
            Child = errorStack
        };

        var textBox = new TextBox
        {
            Header = isDescription ? "Audio description text" : "Transcript text",
            Text = initialText ?? existing?.Text ?? "",
            AcceptsReturn = true,
            TextWrapping = TextWrapping.Wrap,
            MinHeight = isDescription ? 130 : 95,
            PlaceholderText = isDescription ? "Describe visual events or details in this scene..." : "Spoken words or dialogue..."
        };

        var ocrPanel = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Spacing = 10 };
        var ocrStatus = new TextBlock { FontSize = 12, Foreground = Application.Current.Resources["TextFillColorSecondaryBrush"] as Brush, VerticalAlignment = VerticalAlignment.Center, Visibility = Visibility.Collapsed };
        var ocrButton = new Button
        {
            Content = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, Children = { new FontIcon { Glyph = "\uE7BA", FontSize = 13 }, new TextBlock { Text = "Read Frame Text at Start" } } },
            IsEnabled = Model.HasApiKey
        };
        ToolTipService.SetToolTip(ocrButton, Model.HasApiKey ? "Read text from the video frame at the segment start time and use it as the description" : "Configure your Gemini API key in Settings (Gemini API tab) to enable frame OCR");
        ocrPanel.Children.Add(ocrStatus);
        ocrPanel.Children.Add(ocrButton);
        ocrButton.Click += async (_, _) =>
        {
            if (!Model.HasApiKey)
            {
                await ShowSettingsSectionAsync(2);
                return;
            }
            ocrButton.IsEnabled = false;
            ocrStatus.Visibility = Visibility.Visible;
            ocrStatus.Text = "Reading frame text…";
            try
            {
                var targetTime = double.IsFinite(startPicker.Value) ? startPicker.Value : Position;
                var captureResult = await Model.RunAsync("capture-frame", new[] { "--timestamp", targetTime.ToString(CultureInfo.InvariantCulture) });
                var evt = captureResult.FirstOrDefault(x => x.Data.TryGetProperty("screenText", out _));
                var screenText = evt?.Data.GetProperty("screenText").GetString();
                if (!string.IsNullOrWhiteSpace(screenText))
                {
                    textBox.Text = screenText;
                    ocrStatus.Text = "Screen text added to the description.";
                }
                else
                {
                    ocrStatus.Text = "No readable text was found at the segment start time.";
                }
            }
            catch (Exception ex)
            {
                ocrStatus.Text = "Could not read frame text: " + ex.Message;
            }
            finally
            {
                ocrButton.IsEnabled = true;
            }
        };

        var hintDesc = new TextBlock
        {
            Text = isDescription
                ? "Optionally tell Gemini what to examine more closely. It may adjust descriptions in this range and its immediate neighbors to fit around dialogue. You can undo the result."
                : "Generate one or more transcript segments for this range and reconcile its immediate neighbors. All spoken languages and original scripts are preserved. Existing Audio Descriptions and narration are kept; conflicting speech timing is rejected. You can undo the result.",
            TextWrapping = TextWrapping.Wrap,
            Foreground = Application.Current.Resources["TextFillColorSecondaryBrush"] as Brush,
            FontSize = 12
        };
        var hintBox = new TextBox
        {
            Header = "Optional hint for Gemini",
            AcceptsReturn = true,
            TextWrapping = TextWrapping.Wrap,
            MinHeight = 65,
            PlaceholderText = isDescription
                ? "e.g. Focus on the character's facial expression and the sign in the background..."
                : "e.g. Dialogue is in French with English subtitles..."
        };

        var contentStack = new StackPanel { Width = 580, Spacing = 14 };
        contentStack.Children.Add(timingHeader);
        contentStack.Children.Add(timingGrid);
        contentStack.Children.Add(validationBorder);

        contentStack.Children.Add(new TextBlock { Text = isDescription ? "Audio Description" : "Transcript", FontWeight = Microsoft.UI.Text.FontWeights.SemiBold, FontSize = 15 });
        contentStack.Children.Add(textBox);
        if (isDescription) contentStack.Children.Add(ocrPanel);

        contentStack.Children.Add(new TextBlock { Text = "Hint for Gemini (Optional)", FontWeight = Microsoft.UI.Text.FontWeights.SemiBold, FontSize = 15 });
        contentStack.Children.Add(hintDesc);
        contentStack.Children.Add(hintBox);

        var scrollViewer = new ScrollViewer
        {
            Content = contentStack,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            MaxHeight = 600
        };

        var dialog = Dialog(title, scrollViewer, "Save Segment");
        dialog.SecondaryButtonText = "Re-Analyze This Segment";

        string PreciseTimecode(double sec) => TimeSpan.FromSeconds(Math.Max(0, sec)).ToString(@"hh\:mm\:ss\.fff");
        string SegmentExcerpt(string s)
        {
            var collapsed = System.Text.RegularExpressions.Regex.Replace(s?.Trim() ?? "", @"\s+", " ");
            return collapsed.Length > 80 ? collapsed[..77] + "…" : collapsed;
        }

        List<string> ValidateTiming(double s, double e)
        {
            var errors = new List<string>();
            if (!double.IsFinite(s) || !double.IsFinite(e))
            {
                errors.Add("Start and end times must be valid numbers.");
                return errors;
            }
            if (s < 0 || e > maxDuration)
            {
                errors.Add("The segment must remain inside the video duration.");
            }
            if (e <= s)
            {
                errors.Add("End time must be after start time.");
            }
            if (s >= 0 && e > s && e <= maxDuration)
            {
                var allRows = p.Transcript.Concat(p.Cues);
                var currentId = existing?.Id ?? "";
                var overlaps = allRows
                    .Where(other => other.Id != currentId && s < other.End && e > other.Start)
                    .OrderBy(other => other.Start)
                    .ToList();
                foreach (var other in overlaps)
                {
                    var kind = other.IsDescription ? "audio description" : "transcript";
                    errors.Add($"Overlaps {kind} {PreciseTimecode(other.Start)} to {PreciseTimecode(other.End)}: {SegmentExcerpt(other.Text)}");
                }
            }
            return errors;
        }

        void UpdateDialogState()
        {
            var s = startPicker.Value;
            var e = endPicker.Value;
            var dur = (double.IsFinite(s) && double.IsFinite(e)) ? Math.Max(0, e - s) : 0;
            var durTs = TimeSpan.FromSeconds(dur);
            durationText.Text = $"Duration {durTs:hh\\:mm\\:ss\\.fff} ({dur:F3}s)";

            var errors = ValidateTiming(s, e);
            if (errors.Count > 0)
            {
                errorTitle.Text = errors.Count == 1 ? "Timing error" : $"{errors.Count} timing errors";
                errorList.Children.Clear();
                foreach (var err in errors)
                    errorList.Children.Add(new TextBlock { Text = err, FontSize = 12, TextWrapping = TextWrapping.Wrap });
                validationBorder.Visibility = Visibility.Visible;
                dialog.IsPrimaryButtonEnabled = false;
            }
            else
            {
                validationBorder.Visibility = Visibility.Collapsed;
                dialog.IsPrimaryButtonEnabled = !string.IsNullOrWhiteSpace(textBox.Text);
            }

            dialog.IsSecondaryButtonEnabled = Model.Idle && double.IsFinite(s) && double.IsFinite(e) && s >= 0 && e > s && e <= maxDuration;
        }

        startPicker.ValueChanged += (_, _) => UpdateDialogState();
        endPicker.ValueChanged += (_, _) => UpdateDialogState();
        textBox.TextChanged += (_, _) => UpdateDialogState();
        UpdateDialogState();

        var result = await ShowDialogAsync(dialog, 680, 720);
        if (result == ContentDialogResult.None) return;

        if (result == ContentDialogResult.Secondary)
        {
            Directory.CreateDirectory(SettingsStore.DirectoryPath);
            var path = Path.Combine(SettingsStore.DirectoryPath, Guid.NewGuid() + ".json");
            try
            {
                await File.WriteAllTextAsync(path, JsonSerializer.Serialize(new
                {
                    id = existing?.Id ?? Guid.NewGuid().ToString(),
                    start = startPicker.Value,
                    end = endPicker.Value,
                    text = textBox.Text.Trim(),
                    hint = hintBox.Text.Trim()
                }));
                await Model.RunAsync(isDescription ? "reanalyze-segment" : "reanalyze-transcript-segment", new[] { "--input", path });
            }
            finally { if (File.Exists(path)) File.Delete(path); }
            return;
        }

        var draft = existing == null
            ? new Segment { Origin = isDescription ? "manual" : "transcript" }
            : JsonSerializer.Deserialize<Segment>(JsonSerializer.Serialize(existing, Json.Options), Json.Options)!;
        draft.Start = startPicker.Value;
        draft.End = endPicker.Value;
        draft.Text = textBox.Text.Trim();
        draft.IsDescription = isDescription;
        if (isDescription && existing != null && draft.Origin == "ai" && draft.Text != existing.Text)
            draft.Origin = "ai_modified";

        var transcript = p.Transcript.ToList();
        var cues = p.Cues.ToList();
        var target = isDescription ? cues : transcript;
        var index = target.FindIndex(c => c.Id == draft.Id);
        if (index >= 0) target[index] = draft; else target.Add(draft);
        await Model.SaveSegmentsAsync(transcript.OrderBy(c => c.Start), cues.OrderBy(c => c.Start));
    }
    private async void DeleteSegments_Click(object sender, RoutedEventArgs e)
    {
        if (Model.Project is not { } p || Segments.SelectedItems.Count == 0) return;
        if (!await Confirm("Delete selected segments", "Selected text and associated narration are permanently removed. Existing exports are invalidated.", "Delete")) return;
        var selected = Segments.SelectedItems.Cast<Segment>().Select(c => c.Id).ToHashSet();
        StopSegmentPreview();
        await Model.SaveSegmentsAsync(p.Transcript.Where(c => !selected.Contains(c.Id)), p.Cues.Where(c => !selected.Contains(c.Id)));
    }
    private async void Undo_Click(object sender, RoutedEventArgs e) => await Model.RunAsync(Section.SelectedIndex == 0 ? "undo-transcript" : "undo-cues");
    private async void Redo_Click(object sender, RoutedEventArgs e) => await Model.RunAsync(Section.SelectedIndex == 0 ? "redo-transcript" : "redo-cues");
    private async Task<bool> ConfirmNarrationVoiceChangeAsync()
    {
        if (Model.Project is not { } p) return false;
        var narratedCues = p.Cues.Where(c => c.HasAudioFile || !string.IsNullOrEmpty(c.Audio) || !string.IsNullOrEmpty(c.ResolvedAudioVoice)).ToList();
        if (narratedCues.Count == 0) return true;
        var selectedEngine = Model.CurrentNarrationEngine();
        var selectedVoice = Model.CurrentNarrationVoice();
        var existingLabels = new List<string>();
        var hasDifferentVoice = false;
        foreach (var cue in narratedCues)
        {
            var cueEngine = cue.ResolvedAudioEngine ?? p.NarrationEngine ?? "windows-core";
            var cueVoice = cue.ResolvedAudioVoice ?? p.NarrationVoice ?? "System default";
            var label = NarrationVoiceFormatting.CombinedLabel(cueVoice, cueEngine);
            if (!existingLabels.Contains(label)) existingLabels.Add(label);
            if (!NarrationVoiceFormatting.VoicesMatch(cueEngine, cueVoice, selectedEngine, selectedVoice))
                hasDifferentVoice = true;
        }
        if (!string.IsNullOrWhiteSpace(p.NarrationEngine) && !string.IsNullOrWhiteSpace(p.NarrationVoice) &&
            !NarrationVoiceFormatting.VoicesMatch(p.NarrationEngine, p.NarrationVoice, selectedEngine, selectedVoice))
        {
            hasDifferentVoice = true;
            var label = NarrationVoiceFormatting.CombinedLabel(p.NarrationVoice, p.NarrationEngine);
            if (!existingLabels.Contains(label)) existingLabels.Insert(0, label);
        }
        if (!hasDifferentVoice) return true;
        return await Confirm(
            "Generate narration with a different voice?",
            $"This project already contains narration generated using {string.Join(", ", existingLabels)}. You have selected {Model.CurrentNarrationSummary()}. Proceeding will result in different narration voices across the project, which will be detailed in AD Overview. Do you want to proceed with generating narration?",
            "Proceed");
    }

    private async Task SynthesizeNarrationWithConfirmationAsync(IEnumerable<string>? extra = null)
    {
        if (Model.Project == null) return;
        var flags = extra?.ToList() ?? new List<string>();
        if (!flags.Contains("--segment") && !flags.Contains("--changed") && !flags.Contains("--regenerate-all")) flags.Add("--regenerate-all");
        if (Model.CurrentNarrationEngine() == "gemini" && !Model.HasApiKey)
        {
            await ShowSettingsSectionAsync(2);
            return;
        }
        if (Model.CurrentNarrationEngine() == "piper" && !PiperService.IsVoiceDownloaded(Model.CurrentNarrationVoice()))
        {
            await ShowSettingsSectionAsync(1);
            return;
        }
        var hadPriorNarration = Model.Project.Cues.Any(c => c.HasAudioFile);
        if (flags.Contains("--regenerate-all"))
        {
            if (Model.Project.HasNarrationAudio && !await Confirm("Regenerate all AD audio?",
                $"All generated narration audio will be replaced by newly generated audio for every Audio Description segment using the currently selected voice: {Model.CurrentNarrationSummary()}. This becomes the project's primary narration voice as soon as generation starts. Description text and timing will be kept. Existing audio is replaced only after each segment is generated successfully; if generation fails or is canceled, some old audio may remain. Do you want to continue?",
                "Regenerate All AD Audio")) return;
        }
        else if (!await ConfirmNarrationVoiceChangeAsync()) return;
        await Model.RunAsync("synthesize-tts", flags);
        if (!flags.Contains("--regenerate-all") && hadPriorNarration && Model.NarrationVoiceUsageReport() is { HasDifferentVoices: true } voiceReport)
        {
            try { await ShowNarrationVoicesAsync(voiceReport); }
            catch (Exception ex) { Model.Engine.Log(ex.ToString()); }
        }
    }

    private async void ChangedNarration_Click(object sender, RoutedEventArgs e)
    {
        await SynthesizeNarrationWithConfirmationAsync(new[] { "--changed" });
    }
    private async void NarrateSelected_Click(object sender, RoutedEventArgs e)
    {
        if (Segments.SelectedItem is Segment { IsDescription: true } c)
            await SynthesizeNarrationWithConfirmationAsync(new[] { "--segment", c.Id });
    }
    private async void Rewrite_Click(object sender, RoutedEventArgs e)
    {
        if (!Model.HasApiKey) { await ShowSettingsSectionAsync(2); return; }
        if (Segments.SelectedItem is Segment { IsDescription: true } c) await Model.RunAsync("rewrite-cue", new[] { "--segment", c.Id });
    }
    private void PreviewNarration_Click(object sender, RoutedEventArgs e)
    {
        if (Segments.SelectedItem is Segment { Audio: { } audio } segment && Model.Project is { } p)
        {
            StopSegmentPreview();
            previewingSegment = segment;
            segment.IsPreviewing = true;
            speech.Source = MediaSource.CreateFromUri(new Uri(p.Resolve(audio)));
            speech.Play();
        }
    }
    private async void DeleteNarration_Click(object sender, RoutedEventArgs e)
    { if (Segments.SelectedItem is Segment { IsDescription: true } c && await Confirm("Delete narration", "Remove this segment's saved audio?", "Delete")) { StopSegmentPreview(); await Model.RunAsync("delete-segment-audio", new[] { "--segment", c.Id }); } }
    private async void Ocr_Click(object sender, RoutedEventArgs e)
    {
        if (!Model.HasApiKey) { await ShowSettingsSectionAsync(2); return; }
        video.Pause();
        var result = await Model.RunAsync("capture-frame", new[] { "--timestamp", Position.ToString(CultureInfo.InvariantCulture) });
        var evt = result.FirstOrDefault(x => x.Data.TryGetProperty("screenText", out _));
        if (evt != null) { Section.SelectedIndex = 2; await EditSegment(null, evt.Data.GetProperty("screenText").GetString()); }
    }
    private async void DeleteGenerated_Click(object sender, RoutedEventArgs e)
    {
        var panel = new StackPanel { Spacing = 8 };
        var boxes = new[] { "transcript", "summary", "descriptions", "narration", "thumbnails", "exports", "history" }.Select(c => new CheckBox { Content = c, Tag = c }).ToList();
        foreach (var box in boxes) panel.Children.Add(box);
        boxes[2].Checked += (_, _) => { boxes[3].IsChecked = true; boxes[3].IsEnabled = false; };
        boxes[2].Unchecked += (_, _) => boxes[3].IsEnabled = true;
        if (await ShowDialogAsync(Dialog("Permanently delete generated content", panel, "Delete selected")) == ContentDialogResult.Primary)
        { StopSegmentPreview(); await Model.RunAsync("delete-generated", boxes.Where(b => b.IsChecked == true).SelectMany(b => new[] { "--component", (string)b.Tag })); }
    }
    private async void Export_Click(object sender, RoutedEventArgs e)
    {
        if (Model.Project != null && Model.Idle) await ShowDialogAsync(new Views.ExportDialog(Model), 1040, 760);
    }
    private async void ExportOpen_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is not string path || Model.Project is not { } p || !p.Exports.Contains(path)) return;
        try
        {
            var full = p.Resolve(path);
            if (Directory.Exists(full)) await Launcher.LaunchFolderPathAsync(full);
            else if (File.Exists(full)) await Launcher.LaunchFileAsync(await Windows.Storage.StorageFile.GetFileFromPathAsync(full));
        }
        catch (Exception ex) { await ShowError(ex); }
    }
    private async void ExportInfo_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is not string relative) return;
        try { await ShowExportInfoAsync(relative); }
        catch (Exception ex) { await ShowError(ex); }
    }
    private async Task ShowExportInfoAsync(string relative)
    {
        if (Model.Project is not { } project || !project.Exports.Contains(relative)) return;
        var full = project.Resolve(relative);
        var isFile = File.Exists(full);
        var isFolder = Directory.Exists(full);
        var actions = new StackPanel { Spacing = 12, Width = 380 };
        actions.Children.Add(new TextBlock { Text = Path.GetFileName(relative), FontWeight = Microsoft.UI.Text.FontWeights.SemiBold, TextWrapping = TextWrapping.Wrap });
        actions.Children.Add(new TextBlock { Text = full, Opacity = .7, TextWrapping = TextWrapping.Wrap });
        var dialog = new ContentDialog { Title = "Export information", Content = actions, CloseButtonText = "Cancel" };
        string? selected = null;
        void AddAction(string label, string choice, bool enabled = true)
        {
            var button = new Button { Content = label, IsEnabled = enabled, HorizontalAlignment = HorizontalAlignment.Stretch,
                HorizontalContentAlignment = HorizontalAlignment.Center };
            button.Click += (_, _) => { selected = choice; dialog.Hide(); };
            actions.Children.Add(button);
        }
        AddAction("Open file", "file", isFile);
        AddAction("Open folder", "folder", isFile || isFolder);
        AddAction("Delete file", "delete", isFile || isFolder);
        await ShowDialogAsync(dialog, 580, 480);
        if (!ReferenceEquals(Model.Project, project) || !project.Exports.Contains(relative)) return;
        if (selected == "file")
            await Launcher.LaunchFileAsync(await Windows.Storage.StorageFile.GetFileFromPathAsync(full));
        else if (selected == "folder")
            await Launcher.LaunchFolderPathAsync(isFolder ? full : Path.GetDirectoryName(full)!);
        else if (selected == "delete" && await Confirm("Delete export", $"Permanently remove {Path.GetFileName(relative)}?", "Delete"))
            await Model.RunAsync("delete-export", new[] { "--name", relative });
    }
    private void TogglePreview_Click(object sender, RoutedEventArgs e)
    {
        PreviewPanel.Visibility = PreviewPanel.Visibility == Visibility.Visible ? Visibility.Collapsed : Visibility.Visible;
        Model.Settings.Set("showPreview", PreviewPanel.Visibility == Visibility.Visible ? "true" : "false"); Model.Settings.Save();
    }
    private void ToggleTimeline_Click(object sender, RoutedEventArgs e) => TimelinePanel.Visibility = TimelinePanel.Visibility == Visibility.Visible ? Visibility.Collapsed : Visibility.Visible;
    private async Task ShowSettingsSectionAsync(int section)
    {
        if (openSettings != null)
        {
            openSettings.SelectSection(section);
            openSettings.Activate();
            return;
        }
        var settings = new Views.SettingsDialog(Model, this, section);
        openSettings = settings;
        try { await settings.ShowAsync(); }
        finally { openSettings = null; ApplyTheme(); Model.Reload(); UpdateApiKeyUi(); UpdateSectionPresentation(); }
    }
    private async void Settings_Click(object sender, RoutedEventArgs e)
    {
        var section = ((sender as FrameworkElement)?.Tag as string) switch
        {
            "Narration" => 1, "Gemini API" => 2, "Developer prompts" => 3,
            "Storage and tools" => 4, "Help" => 5, "About" => 6, _ => 0
        };
        await ShowSettingsSectionAsync(section);
    }
    private async void HelpSettings_Click(object sender, RoutedEventArgs e) => await ShowSettingsSectionAsync(5);
    private async void AboutSettings_Click(object sender, RoutedEventArgs e) => await ShowSettingsSectionAsync(6);
    private async void SpeakSummary_Click(object sender, RoutedEventArgs e)
    {
        if (readback.State == SummaryReadbackState.Playing) { readback.Pause(); return; }
        if (readback.State == SummaryReadbackState.Paused) { readback.Resume(); return; }
        if (readback.State == SummaryReadbackState.Preparing) return;
        if (Model.Project is not { } p || string.IsNullOrWhiteSpace(p.Summary)) return;
        try
        {
            await readback.PlayFromStartAsync();
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) { await ShowError(ex); }
    }
    private async void PreviousSummarySentence_Click(object sender, RoutedEventArgs e) => await NavigateSummaryAsync(readback.PreviousSentenceAsync);
    private async void NextSummarySentence_Click(object sender, RoutedEventArgs e) => await NavigateSummaryAsync(readback.NextSentenceAsync);
    private async void PreviousSummaryParagraph_Click(object sender, RoutedEventArgs e) => await NavigateSummaryAsync(readback.PreviousParagraphAsync);
    private async void NextSummaryParagraph_Click(object sender, RoutedEventArgs e) => await NavigateSummaryAsync(readback.NextParagraphAsync);
    private async Task NavigateSummaryAsync(Func<Task> navigation)
    {
        try
        {
            ClearSummaryHighlight();
            await navigation();
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) { await ShowError(ex); }
    }
    private void StopSpeech_Click(object sender, RoutedEventArgs e) { readback.Stop(); StopSegmentPreview(); }

    private bool summaryEditMode;

    private void SetSummaryEditMode(bool edit)
    {
        summaryEditMode = edit;
        SummaryEditorPanel.Visibility = edit ? Visibility.Visible : Visibility.Collapsed;
        SummaryPanel.Visibility = edit ? Visibility.Collapsed : Visibility.Visible;
        if (Section.SelectedIndex == 1)
        {
            EditModeButton.Content = edit ? "Done" : "Edit";
        }
        if (edit)
        {
            readback.Stop();
            SummaryEditorBox.Text = Model.Project?.Summary ?? "";
            UpdateSummaryWordCount();
            SummaryEditNotice.Text = "";
            SummaryEditorBox.Focus(FocusState.Programmatic);
        }
    }

    private async void SummarySave_Click(object sender, RoutedEventArgs e)
    {
        await SaveSummaryChangesAsync();
        SetSummaryEditMode(false);
    }

    private void SummaryCancel_Click(object sender, RoutedEventArgs e)
    {
        SummaryEditorBox.Text = Model.Project?.Summary ?? "";
        UpdateSummaryWordCount();
        SetSummaryEditMode(false);
    }

    private void SummaryPanel_DoubleTapped(object sender, DoubleTappedRoutedEventArgs e)
    {
        SetSummaryEditMode(true);
    }

    private void SummaryEditorBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        UpdateSummaryWordCount();
        var original = Model.Project?.Summary ?? "";
        var hasChanges = (SummaryEditorBox.Text ?? "") != original;
        SummaryEditNotice.Text = hasChanges ? "• Unsaved changes" : "";
    }

    private void UpdateSummaryWordCount()
    {
        var text = SummaryEditorBox.Text ?? "";
        var chars = text.Length;
        var words = string.IsNullOrWhiteSpace(text) ? 0 : text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).Length;
        SummaryWordCount.Text = $"{words} {(words == 1 ? "word" : "words")} · {chars} {(chars == 1 ? "character" : "characters")}";
    }

    private async Task SaveSummaryChangesAsync()
    {
        if (Model.Project is not { } p) return;
        var text = SummaryEditorBox.Text ?? "";
        if (text != p.Summary)
        {
            await Model.SaveSummaryAsync(text);
            SummaryEditNotice.Text = "";
            SummaryText.Text = text;
            RenderMarkdown(text);
        }
    }

    private async void OpenGeminiSettings_Click(object sender, RoutedEventArgs e) => await ShowSettingsSectionAsync(2);

    private void UpdateApiKeyUi()
    {
        var hasKey = Model.HasApiKey;
        var isInvalid = Model.IsApiKeyInvalid;
        var canUseAi = hasKey && !isInvalid && Model.Idle;
        try { Services.ShellIntegration.UpdateContextMenuState(); } catch { }

        if (ApiKeyWarningBar != null)
        {
            ApiKeyWarningBar.IsOpen = !hasKey;
            if (isInvalid)
            {
                ApiKeyWarningBar.Severity = InfoBarSeverity.Error;
                ApiKeyWarningBar.Title = "Gemini API Key Invalid";
                ApiKeyWarningBar.Message = "Your Gemini API key was rejected by Google as invalid. Enter a valid API key in Settings to enable AI features.";
            }
            else
            {
                ApiKeyWarningBar.Severity = InfoBarSeverity.Warning;
                ApiKeyWarningBar.Title = "Gemini API Key Required";
                ApiKeyWarningBar.Message = "Enter your personal Gemini API key in Settings to generate transcripts, summaries, audio descriptions, and narration.";
            }
        }

        if (GalleryImport != null)
        {
            GalleryImport.IsEnabled = Model.Idle;
            ToolTipService.SetToolTip(GalleryImport, "Create a new local video or YouTube project");
        }

        if (GalleryEmptyButton != null)
        {
            GalleryEmptyButton.IsEnabled = Model.Idle;
            ToolTipService.SetToolTip(GalleryEmptyButton, "Create a new project");
        }

        if (GalleryEmptySubtitle != null)
        {
            GalleryEmptySubtitle.Text = "Import a video or add a public YouTube link to begin.";
        }

        var missingKeyTooltip = isInvalid
            ? "Your Gemini API key is invalid. Enter a valid API key in Settings (Gemini API tab) to enable this feature."
            : "Configure your Gemini API key in Settings (Gemini API tab) to enable this feature.";

        var project = Model.Project;
        if (OverviewReviewNarrationButton != null)
        {
            OverviewReviewNarrationButton.IsEnabled = Model.Idle && project != null;
            ToolTipService.SetToolTip(OverviewReviewNarrationButton, "Review missing audio, generation failures, and text or timing changes without generating narration");
        }
        if (OverviewReviewNarrationVoicesButton != null)
        {
            OverviewReviewNarrationVoicesButton.IsEnabled = Model.Idle && project != null;
            ToolTipService.SetToolTip(OverviewReviewNarrationVoicesButton, "Review how many segments use each voice, which segments use a different voice, and segment timing details");
        }

        void SetAiButton(Button? button, string normalTooltip = "", bool available = true, string? unavailableTooltip = null)
        {
            if (button == null) return;
            button.IsEnabled = canUseAi && available;
            var tooltip = !hasKey ? missingKeyTooltip
                : !available ? (unavailableTooltip ?? normalTooltip)
                : normalTooltip;
            ToolTipService.SetToolTip(button, string.IsNullOrEmpty(tooltip) ? null : tooltip);
        }

        SetAiButton(GenerateCurrent);
        SetAiButton(InspectorGenerate);
        OverviewGenerateAllAudioButton.Content = project?.AllADAudioTitle ?? "Generate All AD Audio";
        OverviewGenerateChangedAudioButton.Content = "Generate Missing or Changed AD Audio";
        InspectorSynthesizeNarrationLabel.Text = Model.IsSynthesizingNarration ? Model.ActiveNarrationStatusText : project?.AllADAudioTitle ?? "Generate All AD Audio";
        foreach (var button in new[] { OverviewGenerateAllAudioButton, InspectorSynthesizeNarrationButton })
        {
            button.IsEnabled = CanRunDesktopCommand("synthesize-tts");
            ToolTipService.SetToolTip(button, "Generate audio for every AD segment using the selected voice and set it as the primary voice");
        }
        foreach (var button in new[] { OverviewGenerateChangedAudioButton, InspectorSynthesizeChangedButton })
        {
            button.IsEnabled = CanRunDesktopCommand("synthesize-changed");
            ToolTipService.SetToolTip(button, "After audio has been generated, repair missing or changed audio while keeping current audio");
        }
        SetAiButton(InspectorRewriteButton, "Rewrite description text to fit available gap duration");
        SetAiButton(PrepareAnalysisButton, "Upload and prepare video on Gemini File API for full multimodal analysis");
        SetAiButton(RefreshAnalysisButton, "Refresh Gemini video analysis availability status");
        SetAiButton(OcrFrameButton, "Read on-screen text from current video frame using Gemini vision");
        InspectorExportButton.IsEnabled = Model.Idle && Model.Project != null;
        ToolTipService.SetToolTip(InspectorExportButton, "Open export dialog to produce video, audio, or caption outputs");

        UpdateMenuKeyStates(hasKey);
    }
}
