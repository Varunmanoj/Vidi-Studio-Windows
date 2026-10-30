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
    private readonly DispatcherTimer integrityTimer = new() { Interval = TimeSpan.FromSeconds(4) };
    private bool updatingPosition;
    private string? loadedProject;
    private string? activeCue;
    private bool applyingProjectPlaybackPreferences;
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
    private bool fullScreenVideo;
    private Microsoft.UI.Windowing.OverlappedPresenter? windowedPresenter;
    private bool windowedPresenterMaximized;
    private Grid? previewFullScreenParent;
    private int previewFullScreenIndex;
    private int previewFullScreenRow;
    private int previewFullScreenRowSpan;
    private int previewFullScreenColumn;
    private int previewFullScreenColumnSpan;
    private double previewFullScreenHeight;
    private HorizontalAlignment previewFullScreenHorizontalAlignment;
    private VerticalAlignment previewFullScreenVerticalAlignment;
    private Visibility previewFullScreenVisibility;
    private readonly Dictionary<UIElement, Visibility> fullScreenHidden = new();
    private Button? fullScreenExitButton;
    private Views.SettingsDialog? openSettings;
    private double Position => Model.Project?.IsYouTube == true ? youtubePosition : video.PlaybackSession.Position.TotalSeconds;
    public MainWindow(string? folder = null)
    {
        readback = new SummaryReadback(Model.Engine, Model.Settings);
        onDeviceNarrator = new OnDeviceNarrator(Model.Settings, Model.Engine);
        onDeviceNarrator.Failed += message => Model.ReportStatus(message);
        InitializeComponent();
        InitializeSummaryOriginOptions();
        ExtendsContentIntoTitleBar = true;
        SetTitleBar(AppTitleBar);
        Views.WindowAppearance.ApplyMaterial(this);
        ActiveExports.Observe(Model);
        Model.PropertyChanged += UpdateStatusFooter;
        Views.WindowAppearance.ApplyIcon(this);
        App.ThemeChanged += ApplyThemeFromApp;
        BuildMenus();
        ProjectPlaybackPreferences.Changed += ReceiveProjectPlaybackPreference;
        UpdateEditorProjectButtons();
        var fullScreenEscape = new KeyboardAccelerator { Key = VirtualKey.Escape };
        fullScreenEscape.Invoked += (_, args) =>
        {
            if (!fullScreenVideo) return;
            ExitFullScreenVideo();
            args.Handled = true;
        };
        Root.KeyboardAccelerators.Add(fullScreenEscape);
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
        integrityTimer.Tick += (_, _) => RefreshProjectIntegrity();
        integrityTimer.Start();
        Activated += (_, _) => { if (ready) RefreshProjectIntegrity(); };
        Closed += (_, _) =>
        {
            if (fullScreenVideo) { try { ExitFullScreenVideo(); } catch (Exception ex) { Model.Engine.Log(ex.ToString()); } }
            App.ThemeChanged -= ApplyThemeFromApp;
            ProjectPlaybackPreferences.Changed -= ReceiveProjectPlaybackPreference;
            ActiveExports.Detach();
            Model.PropertyChanged -= UpdateStatusFooter;
            Model.Cancel(); timer.Stop(); integrityTimer.Stop();
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
            if (folder != null) { Model.Open(folder); ShowEditor(); }
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
    private void AddShortcut(VirtualKey key, VirtualKeyModifiers modifiers, RoutedEventHandler handler, Func<bool>? canExecute = null)
    {
        var shortcut = new KeyboardAccelerator { Key = key, Modifiers = modifiers };
        shortcut.Invoked += (_, e) =>
        {
            if (!Model.Idle || canExecute?.Invoke() == false) { e.Handled = false; return; }
            handler(this, new RoutedEventArgs()); e.Handled = true;
        };
        Root.KeyboardAccelerators.Add(shortcut);
    }
    private void UpdateStatusFooter(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(Model.Busy) or nameof(Model.Idle) or nameof(Model.IsAnalyzing)
            or nameof(Model.IsSynthesizingNarration) or nameof(Model.NarratingSegmentId)
            or nameof(Model.NarratingSegmentIndex) or nameof(Model.ActiveNarrationStatusText)
            or nameof(Model.ProcessingSection) or nameof(Model.VideoPreparation) or nameof(Model.Status))
            UpdateProcessingChrome();
        if (e.PropertyName is nameof(Model.Busy) or nameof(Model.HasApiKey) or nameof(Model.Idle) or nameof(Model.IsSynthesizingNarration)
            or nameof(Model.AiIssueMessage) or nameof(Model.AiProcessingBlocked) or nameof(Model.CanUseAI))
            UpdateApiKeyUi();
        if (e.PropertyName is nameof(Model.Busy) or nameof(Model.Idle) or nameof(Model.Project))
            UpdateProjectIntegrityUi();
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
        Views.WindowAppearance.ApplyWindowSurface(this, Root, AppSurface, theme);
        Views.WindowAppearance.UpdateTitleBar(this, theme);
    }
    private ContentDialog Dialog(string title, object content, string action = "Save") => new()
    { Title = title, Content = content, PrimaryButtonText = action, CloseButtonText = "Cancel", DefaultButton = ContentDialogButton.Primary };
    private static StackPanel DialogMessage(string text)
    {
        var panel = new StackPanel { Width = 400 };
        foreach (var line in WrapDialogLines(text))
            panel.Children.Add(new TextBlock { Text = line, TextWrapping = TextWrapping.NoWrap });
        return panel;
    }

    private static List<string> WrapDialogLines(string text, int lineLength = 52)
    {
        var words = text.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var lines = new List<string>();
        var current = new System.Text.StringBuilder();
        foreach (var word in words)
        {
            if (current.Length > 0 && current.Length + 1 + word.Length > lineLength)
            {
                lines.Add(current.ToString());
                current.Clear();
            }
            if (current.Length > 0) current.Append(' ');
            current.Append(word);
        }
        if (current.Length > 0) lines.Add(current.ToString());
        return lines;
    }
    private Task<ContentDialogResult> ShowDialogAsync(ContentDialog dialog, int width = 680, int height = 460)
        => Views.WindowedDialog.ShowAsync(dialog, this, width, height);
    private async Task<bool> Confirm(string title, string text, string action = "Continue") => await ShowDialogAsync(Dialog(title, DialogMessage(text), action)) == ContentDialogResult.Primary;
    private async Task ShowError(Exception ex)
    {
        Model.Engine.Log(ex.ToString());
        // Processing and speech messages are already written for people; other failures stay generic.
        var message = ex is ProcessingException or SpeechFailedException
            ? ex.Message
            : "Check your settings and the diagnostic log, then retry. Your saved project remains available.";
        await ShowDialogAsync(new ContentDialog { Title = "Action could not finish", Content = DialogMessage(message), CloseButtonText = "Close" });
    }
    private async Task ShowErrorMessage(string title, string message)
    {
        await ShowDialogAsync(new ContentDialog
        {
            Title = title,
            Content = DialogMessage(message),
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
                ShowEditor();
                return;
            }

            await Model.RunAsync("import-video", new[] { "--video", videoPath, "--library", Model.Settings.Get("libraryPath") }, false);
            if (Model.Project != null) ShowEditor();
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
                ShowEditor();
                return;
            }

            await Model.RunAsync("import-youtube", new[] { "--url", url, "--library", Model.Settings.Get("libraryPath") }, false);
            if (Model.Project != null) ShowEditor();
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
    { if (Model.Busy) return; try { if (await PickFolder() is { } folder) { Model.Open(folder); ShowEditor(); } } catch (Exception ex) { await ShowError(ex); } }
    private async void YouTube_Click(object sender, RoutedEventArgs e) => await ShowNewProjectDialogAsync(true);
    private async Task ShowNewProjectDialogAsync(bool youtube)
    {
        if (Model.Busy) return;
        if (summaryEditMode)
        {
            if (!await SaveSummaryChangesAsync()) return;
            SetSummaryEditMode(false);
        }
        try
        {
            var dialog = new Views.NewProjectDialog(this, Root.ActualTheme, Model.Settings, youtube);
            var result = await ShowDialogAsync(dialog, 760, 900);
            if (result is not (ContentDialogResult.Primary or ContentDialogResult.Secondary)) return;
            var imports = dialog.AssetSelection;
            List<EngineEvent> created;
            if (dialog.IsYouTube)
                created = await Model.RunAsync("import-youtube", new[] { "--url", dialog.YouTubeUrl, "--library", dialog.LibraryPath }, false);
            else if (dialog.VideoPath is { } path)
                created = await Model.RunAsync("import-video", new[] { "--video", path, "--library", dialog.LibraryPath }, false);
            else return;
            // Cancelled processing returns no events and leaves the previous project selected.
            // Never apply the new files to that previous project.
            if (!created.Any(item => item.Data.TryGetProperty("project", out _)) || Model.Project == null)
            {
                if (!Model.Status.StartsWith("Cancelled", StringComparison.Ordinal)) await ShowError(new IOException(Model.Status));
                return;
            }
            ShowEditor();
            if (imports.HasAny) await ImportSelectedAssetsAsync(imports, dialog.CombineRequested);
        }
        catch (Exception ex) { await ShowError(ex); }
    }
    private void Search_Changed(object sender, TextChangedEventArgs e) { if (ready) Filter(); }
    private void Filter_Changed(object sender, SelectionChangedEventArgs e) { if (ready) Filter(); }
    private void Filter() => Model.Reload(Search.Text, SourceFilter.SelectedIndex switch { 1 => "local", 2 => "youtube", _ => "all" });
    private bool changingProject;
    private async void Project_Changed(object sender, SelectionChangedEventArgs e)
    {
        try
        {
            if ((sender as ListViewBase)?.SelectedItem is StudioProject selected) await SwitchProjectAsync(selected);
        }
        catch (Exception ex) { await ShowError(ex); }
    }
    private async Task SwitchProjectAsync(StudioProject selected)
    {
        if (changingProject || Model.Busy) return;
        if (Model.Project?.Folder == selected.Folder) return;
        changingProject = true;
        var previous = Model.Projects.FirstOrDefault(project => project.Folder == Model.Project?.Folder);
        try
        {
            if (summaryEditMode)
            {
                if (!await SaveSummaryChangesAsync())
                {
                    ProjectList.SelectedItem = previous;
                    ProjectGrid.SelectedItem = previous;
                    return;
                }
                SetSummaryEditMode(false);
            }
            StopProjectPlayback();
            Model.Open(selected.Folder); ShowEditor();
            var current = Model.Projects.FirstOrDefault(project => project.Folder == Model.Project?.Folder);
            ProjectList.SelectedItem = current;
            ProjectGrid.SelectedItem = current;
            UpdateEditorProjectButtons();
        }
        finally { changingProject = false; }
    }
    private async void RefreshProject()
    {
        if (Model.Project is not { } p) return;
        ApplyProjectPlaybackPreferences(p);
        p.Integrity = ProjectIntegrityAuditor.Audit(p.ToIntegrityManifest());
        UpdateInspector(p);
        UpdateApiKeyUi();
        UpdateProjectIntegrityUi();
        if (EditorWorkspace.Visibility == Visibility.Visible)
        {
            ProjectTitle.Text = p.Name;
            Title = p.Name + " — Vidi Studio";
        }
        ProjectInformation.Text = $"{(p.IsYouTube ? "YouTube" : "Local video")}\n{p.Width} × {p.Height}\n{TimeSpan.FromSeconds(p.Duration):hh\\:mm\\:ss}\n{p.Folder}";
        Playhead.Maximum = Math.Max(1, p.Duration);
        UpdateTimelineCursor();
        UpdatePlaybackControls();
        RenderSection();
        RenderTimelineThumbnails(p);
        if (p.IntegrityPlaceholder || p.Integrity?.ManifestUnreadable == true || p.Integrity?.FolderMissing == true)
        {
            loadedProject = p.Folder;
            StopProjectPlayback();
            Player.Visibility = Visibility.Collapsed;
            YouTubePlayer.Visibility = Visibility.Collapsed;
            video.Source = null;
            return;
        }
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
        if (summaryEditMode && Section.SelectedIndex != 1 && Model.Project != null)
        {
            if (!await SaveSummaryChangesAsync()) { Section.SelectedIndex = 1; return; }
            SetSummaryEditMode(false);
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
        ExportList.Visibility = Visibility.Collapsed;
        IEnumerable<Segment> rows = Section.SelectedIndex switch
        {
            0 => p.Transcript,
            2 => p.Cues,
            _ => p.Transcript.Concat(p.Cues).OrderBy(c => c.Start)
        };
        rows = ApplySegmentSort(rows);
        Segments.ItemsSource = segmentFilter.IsActive && Section.SelectedIndex is 0 or 2 or 4
            ? rows.Where(segment => segmentFilter.Matches(segment, HasSegmentAudio(p, segment))).ToList()
            : rows;
        RefreshEditingRows();
        RefreshSummarySourceBadge();
        SummaryText.Text = p.Summary;
        RenderMarkdown(p.Summary);
        if (!summaryEditMode || summaryDraftProjectId != p.Id)
        {
            if (summaryEditMode) SetSummaryEditMode(false);
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
    /// <summary>
    /// Restores the project's saved playback toggles whenever the editor loads or refreshes
    /// that project, so the choice survives closing and reopening the app.
    /// </summary>
    private void ApplyProjectPlaybackPreferences(StudioProject project)
    {
        if (string.IsNullOrWhiteSpace(project.Id)) return;
        var narration = ProjectPlaybackPreferences.NarrationEnabled(Model.Settings, project);
        var follow = ProjectPlaybackPreferences.FollowEnabled(Model.Settings, project);
        applyingProjectPlaybackPreferences = true;
        try { Narration.IsOn = narration; Follow.IsOn = follow; }
        finally { applyingProjectPlaybackPreferences = false; }
        if (!follow) ClearFollowedSegments();
        UpdateDesktopMenuStates();
    }
    private void ReceiveProjectPlaybackPreference(ProjectPlaybackPreferenceChange change)
    {
        if (Model.Project is not { } project || project.Id != change.ProjectId) return;
        DispatcherQueue.TryEnqueue(() =>
        {
            if (Model.Project is not { } current || current.Id != change.ProjectId) return;
            applyingProjectPlaybackPreferences = true;
            try
            {
                if (change.Preference == ProjectPlaybackPreference.Narration) Narration.IsOn = change.Enabled;
                else Follow.IsOn = change.Enabled;
            }
            finally { applyingProjectPlaybackPreferences = false; }
            if (change.Preference == ProjectPlaybackPreference.Narration && !change.Enabled) StopLiveNarration();
            else if (change.Preference == ProjectPlaybackPreference.Follow)
            {
                if (change.Enabled) UpdateActiveSegment(true);
                else ClearFollowedSegments();
            }
            UpdateDesktopMenuStates();
        });
    }
    private void Narration_Toggled(object sender, RoutedEventArgs e)
    {
        if (!ready || applyingProjectPlaybackPreferences) return;
        if (Model.Project is not { } project || string.IsNullOrWhiteSpace(project.Id)) return;
        ProjectPlaybackPreferences.SetNarrationEnabled(Model.Settings, project, Narration.IsOn);
        if (!Narration.IsOn) StopLiveNarration();
        UpdateDesktopMenuStates();
    }
    private void StopLiveNarration()
    {
        narrationPlayer.Pause();
        narrationPlayer.Source = null;
        if (activeCue != null) onDeviceNarrator.Stop();
        activeCue = null;
    }
    private void ClearFollowedSegments()
    {
        foreach (var active in activeSegmentRows) active.IsActive = false;
        activeSegmentRows.Clear();
    }
    private void Follow_Toggled(object sender, RoutedEventArgs e)
    {
        if (!ready || applyingProjectPlaybackPreferences) return;
        if (Model.Project is { } project && !string.IsNullOrWhiteSpace(project.Id))
            ProjectPlaybackPreferences.SetFollowEnabled(Model.Settings, project, Follow.IsOn);
        if (!Follow.IsOn)
        {
            ClearFollowedSegments();
        }
        else UpdateActiveSegment(true);
        UpdateDesktopMenuStates();
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
        if (Model.Project is not { } activeProject
            || activeProject.IntegrityPlaceholder
            || activeProject.Integrity?.ManifestUnreadable == true
            || activeProject.Integrity?.FolderMissing == true) return;
        if (command == "synthesize-tts")
        {
            await SynthesizeNarrationWithConfirmationAsync();
            return;
        }
        if (!Model.CanUseAI && command != "prepare-video")
        {
            await ShowSettingsSectionAsync(3);
            return;
        }
        if (command is "analyze" or "transcribe" or "generate-summary" or "generate-ad-cues")
            if (!await Confirm("Generate content", "This replaces existing content for the selected stages and may remove superseded narration or exports. Manual edits in those stages will be replaced. Continue?")) return;
        await Model.RunAsync(command);
    }
    private void Cancel_Click(object sender, RoutedEventArgs e) => Model.Cancel();
    private async void RenameProjectTitle_Click(object sender, RoutedEventArgs e)
    {
        try { await RunDesktopCommandAsync("rename-project"); }
        catch (Exception ex) { await ShowError(ex); }
    }
    private async void Rename_Click(object sender, RoutedEventArgs e)
    {
        if (Model.Project is { } project) await RenameProjectAsync(project);
    }

    private async Task RenameProjectAsync(StudioProject project)
    {
        if (project.IntegrityPlaceholder || project.Integrity?.ManifestUnreadable == true) return;
        var name = new TextBox { Header = "Project name", Text = project.Name };
        if (await ShowDialogAsync(Dialog("Rename project", name)) != ContentDialogResult.Primary) return;
        if (Model.Project is not { } current || !string.Equals(current.Folder, project.Folder, StringComparison.OrdinalIgnoreCase))
            Model.Open(project.Folder);
        await Model.RunAsync("rename-project", new[] { "--name", name.Text });
        if (!desktopEditorVisible)
        {
            var renamed = Model.Projects.FirstOrDefault(item => string.Equals(item.Folder, project.Folder, StringComparison.OrdinalIgnoreCase));
            if (GalleryGrid.Visibility == Visibility.Visible) GalleryGrid.SelectedItem = renamed;
            else GalleryList.SelectedItem = renamed;
        }
        UpdateDesktopMenuStates();
    }
    private async void DeleteProject_Click(object sender, RoutedEventArgs e)
    {
        if (Model.Project is { } p) await DeleteProjectAsync(p);
    }

    private async Task DeleteProjectAsync(StudioProject p)
    {
        var folderExists = Directory.Exists(p.Folder);
        var damaged = p.Integrity?.IsDamaged == true || p.IntegrityPlaceholder;
        var title = folderExists
            ? (damaged ? "Delete damaged project" : "Move project to Recycle Bin")
            : "Remove missing project";
        var message = folderExists
            ? (damaged
                ? $"This project is marked as damaged. Move {p.Name} and all remaining files to the Recycle Bin?"
                : $"Move {p.Name} and all its media to the Recycle Bin?")
            : $"The project folder for {p.Name} no longer exists. Remove its entry from VidiStudio?";
        var action = folderExists ? "Move to Recycle Bin" : "Remove project";
        if (!await Confirm(title, message, action)) return;
        try
        {
            var deletingLoadedProject = Model.Project is { } current
                && string.Equals(current.Folder, p.Folder, StringComparison.OrdinalIgnoreCase);
            if (deletingLoadedProject)
            {
                StopProjectPlayback(); video.Source = null; speech.Source = null; narrationPlayer.Source = null;
            }
            if (folderExists)
                Microsoft.VisualBasic.FileIO.FileSystem.DeleteDirectory(p.Folder, Microsoft.VisualBasic.FileIO.UIOption.OnlyErrorDialogs, Microsoft.VisualBasic.FileIO.RecycleOption.SendToRecycleBin);
            Model.ForgetProject(p);
            if (deletingLoadedProject) ClearProject();
            else { RefreshGalleryCount(); UpdateProjectIntegrityUi(); }
        }
        catch (Exception ex) { await ShowError(ex); }
    }

    private void RefreshProjectIntegrity()
    {
        if (!ready) return;
        if (Model.Project is { IntegrityPlaceholder: true } placeholder && StudioProject.IsManifestReadable(placeholder.Folder))
        {
            var wasEditorVisible = EditorWorkspace.Visibility == Visibility.Visible;
            Model.Open(placeholder.Folder);
            if (wasEditorVisible) ShowEditor();
            return;
        }
        if (EditorWorkspace.Visibility == Visibility.Visible)
            Model.RefreshProjectIntegrity();
        if (GalleryPanel.Visibility == Visibility.Visible &&
            Model.ReloadIfProjectsChanged(GallerySearch.Text, GalleryFilter.SelectedIndex switch { 1 => "local", 2 => "youtube", _ => "all" }))
            RefreshGalleryCount();
        UpdateProjectIntegrityUi();
    }

    private void UpdateProjectIntegrityUi()
    {
        if (ProjectIntegrityBar == null) return;
        if (EditorWorkspace.Visibility != Visibility.Visible || Model.Project is not { } p)
        {
            ProjectIntegrityBar.IsOpen = false;
            UpdateInspectorIntegrityButton(null);
            return;
        }
        var report = p.Integrity ?? ProjectIntegrityAuditor.Audit(p.ToIntegrityManifest());
        p.Integrity = report;
        if (report.IsHealthy)
        {
            ProjectIntegrityBar.IsOpen = false;
            UpdateInspectorIntegrityButton(null);
            return;
        }
        UpdateInspectorIntegrityButton(report);
        ProjectIntegrityBar.IsOpen = true;
        ProjectIntegrityBar.Severity = report.IsDamaged ? InfoBarSeverity.Error : InfoBarSeverity.Warning;
        ProjectIntegrityBar.Title = report.IsDamaged
            ? $"“{p.Name}” needs attention"
            : $"“{p.Name}” has missing files";
        ProjectIntegrityBar.Message = ProjectIntegrityBannerMessage(report);

        var folderExists = Directory.Exists(p.Folder);
        ProjectIntegrityPrimaryAction.Content = report.IsDamaged
            ? (folderExists ? "Delete project…" : "Remove project…")
            : "Review issues…";
        ProjectIntegrityPrimaryAction.Foreground = report.IsDamaged
            ? new SolidColorBrush(Windows.UI.Color.FromArgb(255, 229, 43, 50))
            : null;
        ProjectIntegrityPrimaryAction.IsEnabled = Model.Idle;
        ProjectIntegrityDetailsButton.Content = $"Review {report.Problems.Count} issue{(report.Problems.Count == 1 ? "" : "s")}…";
        ProjectIntegrityDetailsButton.Visibility = report.IsDamaged ? Visibility.Visible : Visibility.Collapsed;
        ProjectIntegrityDetailsButton.IsEnabled = Model.Idle;
        ProjectIntegrityFinderButton.Visibility = report.CanShowInFinder ? Visibility.Visible : Visibility.Collapsed;

        var narration = report.CanRegenerateNarration
            && (Model.CurrentNarrationEngine() != "gemini" || Model.CanUseAI);
        var videoArtifacts = report.CanRegenerateVideoArtifacts;
        ProjectIntegrityRegenerateButton.Visibility = narration || videoArtifacts ? Visibility.Visible : Visibility.Collapsed;
        ProjectIntegrityRegenerateButton.Content = narration ? "Regenerate missing narration" : "Regenerate video details & pictures";
        ProjectIntegrityRegenerateButton.Tag = narration ? "narration" : "video";
        ProjectIntegrityRegenerateButton.IsEnabled = Model.Idle;
    }

    private static string ProjectIntegrityBannerMessage(ProjectIntegrityReport report)
    {
        if (report.FolderMissing)
            return "The project folder is missing. Restore it, or remove this project entry.";
        if (report.ManifestUnreadable)
            return "project.json is missing or damaged. Restore it from a backup, or delete the project.";
        if (report.Problems.Any(problem => problem.Category == ProjectFileCategory.Source))
            return "The original video is missing. Playback, analysis, and video exports are unavailable.";
        if (report.IsDamaged)
            return "Most project files are missing. Review the remaining files or delete the project.";
        if (report.Problems.Count == 1)
        {
            var problem = report.Problems[0];
            return problem.Category switch
            {
                ProjectFileCategory.Narration => $"Narration audio is missing for {problem.MissingCount} AD segment{(problem.MissingCount == 1 ? "" : "s")}. It can be regenerated.",
                ProjectFileCategory.Thumbnails => "Timeline pictures are missing. They can be regenerated from the original video.",
                ProjectFileCategory.Metadata => "Video details are missing. They can be regenerated from the original video.",
                ProjectFileCategory.Exports => $"{problem.MissingCount} exported file{(problem.MissingCount == 1 ? "" : "s")} missing. Export again to recreate {(problem.MissingCount == 1 ? "it" : "them")}.",
                _ => "Some project files are missing."
            };
        }
        return $"{report.MissingTrackedFiles} of {report.TotalTrackedFiles} tracked files are missing. Generated files can usually be regenerated.";
    }

    private void UpdateInspectorIntegrityButton(ProjectIntegrityReport? report)
    {
        if (InspectorIntegrityButton == null || InspectorIntegrityIcon == null) return;
        var show = report != null && report.Problems.Count > 1;
        InspectorIntegrityButton.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
        if (!show || report == null) return;
        var tint = report.IsDamaged
            ? Windows.UI.Color.FromArgb(255, 229, 43, 50)
            : Windows.UI.Color.FromArgb(255, 199, 122, 0);
        InspectorIntegrityIcon.Foreground = new SolidColorBrush(tint);
        var label = $"Show all {report.Problems.Count} project file issues";
        ToolTipService.SetToolTip(InspectorIntegrityButton, label);
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(InspectorIntegrityButton, label);
    }

    private async void OpenInspectorIntegrity_Click(object sender, RoutedEventArgs e)
    {
        if (Model.Project is not { } p) return;
        await ShowProjectIntegrityDetailsAsync(p.Integrity ?? ProjectIntegrityAuditor.Audit(p.ToIntegrityManifest()));
    }

    private async void ProjectIntegrityPrimaryAction_Click(object sender, RoutedEventArgs e)
    {
        if (Model.Project is not { } p) return;
        var report = p.Integrity ?? ProjectIntegrityAuditor.Audit(p.ToIntegrityManifest());
        if (report.IsDamaged) await DeleteProjectAsync(p);
        else await ShowProjectIntegrityDetailsAsync(report);
    }

    private async void ProjectIntegrityDetails_Click(object sender, RoutedEventArgs e)
    {
        if (Model.Project is not { } p) return;
        await ShowProjectIntegrityDetailsAsync(p.Integrity ?? ProjectIntegrityAuditor.Audit(p.ToIntegrityManifest()));
    }

    private async void ProjectIntegrityRegenerate_Click(object sender, RoutedEventArgs e)
    {
        if (Model.Busy || Model.Project == null) return;
        if (((FrameworkElement)sender).Tag as string == "narration")
            await SynthesizeNarrationWithConfirmationAsync(new[] { "--changed" });
        else
            await Model.RunAsync("prepare-video");
        RefreshProject();
    }

    private async Task ShowProjectIntegrityDetailsAsync(ProjectIntegrityReport report)
    {
        var tint = report.IsDamaged
            ? Windows.UI.Color.FromArgb(255, 229, 43, 50)
            : Windows.UI.Color.FromArgb(255, 199, 122, 0);
        var content = new StackPanel { Spacing = 14, MaxWidth = 700 };

        var heading = new StackPanel { Spacing = 5 };
        heading.Children.Add(new TextBlock
        {
            Text = report.ProjectName,
            FontSize = 22,
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
            TextTrimming = TextTrimming.CharacterEllipsis
        });
        var statusRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10 };
        statusRow.Children.Add(new Border
        {
            Background = new SolidColorBrush(Windows.UI.Color.FromArgb(32, tint.R, tint.G, tint.B)),
            CornerRadius = new CornerRadius(10),
            Padding = new Thickness(8, 2, 8, 2),
            VerticalAlignment = VerticalAlignment.Center,
            Child = new TextBlock
            {
                Text = report.IsDamaged ? "Damaged" : "Needs Attention",
                Foreground = new SolidColorBrush(tint),
                FontSize = 12,
                FontWeight = Microsoft.UI.Text.FontWeights.SemiBold
            }
        });
        statusRow.Children.Add(new TextBlock
        {
            Text = $"{report.Problems.Count} issue{(report.Problems.Count == 1 ? "" : "s")} · {report.MissingTrackedFiles} of {report.TotalTrackedFiles} tracked files missing or unreadable",
            FontSize = 12,
            Opacity = .6,
            VerticalAlignment = VerticalAlignment.Center
        });
        heading.Children.Add(statusRow);
        heading.Children.Add(new TextBlock
        {
            Text = report.Message,
            FontSize = 14,
            TextWrapping = TextWrapping.Wrap,
            Opacity = .78
        });
        content.Children.Add(heading);

        var issues = new StackPanel { Spacing = 10 };
        foreach (var problem in report.Problems)
            issues.Children.Add(ProjectIntegrityProblemCard(problem, tint));
        content.Children.Add(new ScrollViewer
        {
            Content = issues,
            MaxHeight = 430,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled
        });

        var dialog = new ContentDialog
        {
            Title = "Project File Issues",
            Content = content,
            CloseButtonText = "Done"
        };
        await ShowDialogAsync(dialog, 780, 660);
    }

    private static UIElement ProjectIntegrityProblemCard(ProjectFileProblem problem, Windows.UI.Color tint)
    {
        var problemTint = problem.IsCritical
            ? Windows.UI.Color.FromArgb(255, 229, 43, 50)
            : tint;
        var stack = new StackPanel { Spacing = 8 };

        var titleRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10 };
        titleRow.Children.Add(new Border
        {
            Width = 28,
            Height = 28,
            CornerRadius = new CornerRadius(14),
            Background = new SolidColorBrush(Windows.UI.Color.FromArgb(32, problemTint.R, problemTint.G, problemTint.B)),
            Child = new FontIcon
            {
                Glyph = problem.IconGlyph,
                FontSize = 13,
                Foreground = new SolidColorBrush(problemTint),
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            }
        });
        var titleText = new StackPanel { Spacing = 2, VerticalAlignment = VerticalAlignment.Center };
        titleText.Children.Add(new TextBlock
        {
            Text = problem.Title,
            FontSize = 15,
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold
        });
        titleText.Children.Add(new TextBlock
        {
            Text = problem.Detail,
            FontSize = 13,
            TextWrapping = TextWrapping.Wrap,
            Opacity = .7
        });
        titleRow.Children.Add(titleText);
        stack.Children.Add(titleRow);

        stack.Children.Add(new TextBlock
        {
            Text = problem.Impact,
            FontSize = 13,
            TextWrapping = TextWrapping.Wrap,
            Opacity = .72
        });

        if (!string.IsNullOrWhiteSpace(problem.Recovery))
        {
            var recovery = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 7 };
            recovery.Children.Add(new FontIcon
            {
                Glyph = "\uE82F",
                FontSize = 12,
                Foreground = new SolidColorBrush(problemTint),
                VerticalAlignment = VerticalAlignment.Top,
                Margin = new Thickness(0, 2, 0, 0)
            });
            recovery.Children.Add(new TextBlock
            {
                Text = problem.Recovery,
                FontSize = 13,
                TextWrapping = TextWrapping.Wrap,
                Opacity = .72
            });
            stack.Children.Add(recovery);
        }

        if (problem.MissingFiles.Count > 0)
        {
            var files = new StackPanel { Spacing = 3 };
            files.Children.Add(new TextBlock
            {
                Text = "Missing files",
                FontSize = 12,
                FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                Opacity = .65
            });
            var fileList = new StackPanel { Spacing = 3 };
            foreach (var file in problem.MissingFiles)
            {
                fileList.Children.Add(new TextBlock
                {
                    Text = file.DisplayName,
                    FontFamily = new FontFamily("Cascadia Mono"),
                    FontSize = 11,
                    TextWrapping = TextWrapping.Wrap,
                    IsTextSelectionEnabled = true
                });
            }
            var fileScroll = new ScrollViewer
            {
                Content = fileList,
                MaxHeight = 132,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled
            };
            files.Children.Add(fileScroll);
            stack.Children.Add(new Border
            {
                Background = new SolidColorBrush(Windows.UI.Color.FromArgb(18, 128, 128, 128)),
                CornerRadius = new CornerRadius(8),
                Padding = new Thickness(10),
                Child = files
            });
        }

        return new Border
        {
            Background = new SolidColorBrush(Windows.UI.Color.FromArgb(18, tint.R, tint.G, tint.B)),
            BorderBrush = new SolidColorBrush(Windows.UI.Color.FromArgb(45, tint.R, tint.G, tint.B)),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(12),
            Padding = new Thickness(14),
            Child = stack
        };
    }
    private async void Reveal_Click(object sender, RoutedEventArgs e)
    {
        if (Model.Project is { } project) await RevealProjectAsync(project);
    }
    private async Task RevealProjectAsync(StudioProject project)
    {
        try { await Launcher.LaunchFolderPathAsync(project.Folder); }
        catch (Exception ex) { await ShowError(ex); }
    }
    /// <summary>Opens the project's project.json in the default app for JSON files.</summary>
    private async void OpenConfiguration_Click(object sender, RoutedEventArgs e)
    {
        if (Model.Project is { } project) await OpenProjectConfigurationAsync(project);
    }
    private async Task OpenProjectConfigurationAsync(StudioProject project)
    {
        if (string.IsNullOrWhiteSpace(project.Folder)) return;
        var manifest = Path.Combine(project.Folder, "project.json");
        if (!File.Exists(manifest)) return;
        try { await Launcher.LaunchFileAsync(await Windows.Storage.StorageFile.GetFileFromPathAsync(manifest)); }
        catch (Exception ex) { await ShowError(ex); }
    }
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
        if (isDescription && p.Transcript.Count == 0)
        {
            await ShowDialogAsync(new ContentDialog
            {
                Title = "Transcript Required",
                Content = new TextBlock
                {
                    Text = "Import or generate a transcript before adding or editing descriptions. It is required for overlap validation. Use Project > Import Existing Assets… to bring a transcript now.",
                    TextWrapping = TextWrapping.Wrap
                },
                CloseButtonText = "Close"
            });
            return;
        }
        var maxDuration = p.Duration;
        var initialStart = existing?.Start ?? Position;
        var initialEnd = existing?.End ?? Math.Min(maxDuration, initialStart + (isDescription ? 3.0 : 4.0));
        if (initialEnd <= initialStart) initialEnd = Math.Min(maxDuration, initialStart + 1.0);

        var title = existing == null
            ? (isDescription ? "Add Audio Description Segment" : "Add Transcript Segment")
            : $"Edit {(isDescription ? "Audio Description" : "Transcript")} Segment";

        var timingEditor = new Views.SegmentTimingEditor(initialStart, initialEnd, maxDuration, () => Position);
        var startPicker = timingEditor.StartPicker;
        var endPicker = timingEditor.EndPicker;

        var timingTitle = new TextBlock
        {
            Text = "Timing",
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
            FontSize = 15
        };

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

        var originPicker = new ComboBox
        {
            Header = "Provenance",
            HorizontalAlignment = HorizontalAlignment.Stretch,
            ItemsSource = new[] { "AI-generated", "AI-generated · modified", "Imported", "Imported · modified", "Manually added", "Origin unknown" }
        };
        var originIds = new[] { "ai", "ai_modified", "imported", "imported_modified", "manual", "unknown" };
        originPicker.SelectedIndex = Math.Max(0, Array.IndexOf(originIds, existing?.Origin ?? "manual"));
        ToolTipService.SetToolTip(originPicker, "Choose a provenance label. The manual choice persists and is not overwritten by automatic inference.");
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(originPicker, "Provenance");

        var ocrPanel = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Spacing = 10 };
        var ocrStatus = new TextBlock { FontSize = 12, Foreground = Application.Current.Resources["TextFillColorSecondaryBrush"] as Brush, VerticalAlignment = VerticalAlignment.Center, Visibility = Visibility.Collapsed };
        var ocrButton = new Button
        {
            Content = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, Children = { new FontIcon { Glyph = "\uE7BA", FontSize = 13 }, new TextBlock { Text = "Read Frame Text at Start" } } },
            IsEnabled = Model.CanUseAI
        };
        ToolTipService.SetToolTip(ocrButton, Model.HasApiKey ? "Read text from the video frame at the segment start time and use it as the description" : "Configure your Gemini API key in Settings (Gemini API tab) to enable frame OCR");
        ocrPanel.Children.Add(ocrStatus);
        ocrPanel.Children.Add(ocrButton);
        ocrButton.Click += async (_, _) =>
        {
            if (!Model.HasApiKey)
            {
                await ShowSettingsSectionAsync(3);
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
        contentStack.Children.Add(timingTitle);
        contentStack.Children.Add(timingEditor);
        contentStack.Children.Add(validationBorder);

        contentStack.Children.Add(new TextBlock { Text = isDescription ? "Audio Description" : "Transcript", FontWeight = Microsoft.UI.Text.FontWeights.SemiBold, FontSize = 15 });
        contentStack.Children.Add(textBox);
        contentStack.Children.Add(originPicker);
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
            ? new Segment { Origin = "manual" }
            : JsonSerializer.Deserialize<Segment>(JsonSerializer.Serialize(existing, Json.Options), Json.Options)!;
        draft.Start = startPicker.Value;
        draft.End = endPicker.Value;
        draft.Text = textBox.Text.Trim();
        draft.IsDescription = isDescription;
        var selectedOrigin = originIds[Math.Clamp(originPicker.SelectedIndex, 0, originIds.Length - 1)];
        draft.Origin = selectedOrigin;
        draft.OriginOverride = true;

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
        if (Model.Project is not { } narrationProject
            || narrationProject.IntegrityPlaceholder
            || narrationProject.Integrity?.ManifestUnreadable == true
            || narrationProject.Integrity?.FolderMissing == true) return;
        var flags = extra?.ToList() ?? new List<string>();
        if (!flags.Contains("--segment") && !flags.Contains("--changed") && !flags.Contains("--regenerate-all")) flags.Add("--regenerate-all");
        if (Model.CurrentNarrationEngine() == "gemini" && !Model.CanUseAI)
        {
            await ShowSettingsSectionAsync(3);
            return;
        }
        if (Model.CurrentNarrationEngine() == "piper" && !PiperService.IsVoiceDownloaded(Model.CurrentNarrationVoice()))
        {
            await ShowSettingsSectionAsync(2);
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
        if (!Model.HasApiKey) { await ShowSettingsSectionAsync(3); return; }
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
        if (!Model.HasApiKey) { await ShowSettingsSectionAsync(3); return; }
        video.Pause();
        var result = await Model.RunAsync("capture-frame", new[] { "--timestamp", Position.ToString(CultureInfo.InvariantCulture) });
        var evt = result.FirstOrDefault(x => x.Data.TryGetProperty("screenText", out _));
        if (evt != null) { Section.SelectedIndex = 2; await EditSegment(null, evt.Data.GetProperty("screenText").GetString()); }
    }
    private async void DeleteGenerated_Click(object sender, RoutedEventArgs e)
    {
        if (Model.Project is not { IntegrityPlaceholder: false } || Model.Project.Integrity?.ManifestUnreadable == true) return;
        var (dialog, boxes) = CreateDeleteGeneratedDialog();
        if (await ShowDialogAsync(dialog, 620, 820) == ContentDialogResult.Primary)
        {
            var selected = boxes.Where(b => b.IsChecked == true).Select(b => (string)b.Tag).ToList();
            if (selected.Count == 0) return;
            StopSegmentPreview();
            await Model.RunAsync("delete-generated", selected.SelectMany(component => new[] { "--component", component }));
        }
    }
    private static (ContentDialog Dialog, List<CheckBox> Boxes) CreateDeleteGeneratedDialog()
    {
        var content = new StackPanel { MinWidth = 360 };
        var options = new StackPanel { Spacing = 4 };
        var boxes = new[] { "transcript", "summary", "descriptions", "narration", "thumbnails", "exports", "history" }
            .Select(component => new CheckBox { Content = component, Tag = component }).ToList();
        foreach (var box in boxes) options.Children.Add(box);
        content.Children.Add(options);
        boxes[2].Checked += (_, _) => { boxes[3].IsChecked = true; boxes[3].IsEnabled = false; };
        boxes[2].Unchecked += (_, _) => boxes[3].IsEnabled = true;
        var dialog = new ContentDialog
        {
            Title = "Permanently delete generated content",
            Content = content,
            PrimaryButtonText = "Delete selected",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Primary
        };
        void SyncPrimaryButton() => dialog.IsPrimaryButtonEnabled = boxes.Any(box => box.IsChecked == true);
        foreach (var box in boxes)
        {
            box.Checked += (_, _) => SyncPrimaryButton();
            box.Unchecked += (_, _) => SyncPrimaryButton();
        }
        SyncPrimaryButton();
        return (dialog, boxes);
    }
    private async void Export_Click(object sender, RoutedEventArgs e)
    {
        if (Model.Project is { IntegrityPlaceholder: false } project
            && project.Integrity?.ManifestUnreadable != true
            && project.Integrity?.FolderMissing != true
            && Model.Idle)
            await ShowDialogAsync(new Views.ExportDialog(Model), 1040, 760);
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
            "Projects" => 1, "Narration" => 2, "Gemini API" => 3, "Developer prompts" => 4,
            "Storage" => 5, "Help" => 6, "About" => 7, _ => 0
        };
        await ShowSettingsSectionAsync(section);
    }
    private void HelpSettings_Click(object sender, RoutedEventArgs e) => App.OpenHelpWindow(Root.ActualTheme);
    private async void AboutSettings_Click(object sender, RoutedEventArgs e) => await ShowSettingsSectionAsync(7);
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
    private string? summaryDraftProjectId;

    private void SetSummaryEditMode(bool edit)
    {
        // Initializing the heading picker must never apply Paragraph to a saved heading.
        summaryEditMode = false;
        SummaryEditorPanel.Visibility = edit ? Visibility.Visible : Visibility.Collapsed;
        SummaryPanel.Visibility = edit ? Visibility.Collapsed : Visibility.Visible;
        if (Section.SelectedIndex == 1)
        {
            SetEditModeButton(edit);
        }
        if (edit)
        {
            readback.Stop();
            SummaryEditorBox.Text = NormalizeSummaryText(Model.Project?.Summary);
            summaryDraftProjectId = Model.Project?.Id;
            SummaryHeadingPicker.SelectedIndex = 0;
            SummaryProvenance.SelectedIndex = Model.Project?.SummaryOrigin switch
            {
                "ai" => 0, "imported" => 1, "manual" => 2, _ => -1
            };
            UpdateSummaryWordCount();
            SummaryEditNotice.Text = "";
            SummaryEditorBox.Focus(FocusState.Programmatic);
        }
        summaryEditMode = edit;
        UpdateEditorActionVisibility();
        UpdateDesktopMenuStates();
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
        var original = NormalizeSummaryText(Model.Project?.Summary);
        var hasChanges = NormalizeSummaryText(SummaryEditorBox.Text) != original;
        SummaryEditNotice.Text = hasChanges ? "• Unsaved changes" : "";
    }

    private void UpdateSummaryWordCount()
    {
        var text = SummaryEditorBox.Text ?? "";
        var chars = text.Length;
        var words = string.IsNullOrWhiteSpace(text) ? 0 : text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).Length;
        SummaryWordCount.Text = $"{words} {(words == 1 ? "word" : "words")} · {chars} {(chars == 1 ? "character" : "characters")}";
    }

    private async Task<bool> SaveSummaryChangesAsync()
    {
        if (Model.Project is not { } p || Model.Busy) return false;
        try
        {
            var text = NormalizeSummaryText(SummaryEditorBox.Text);
            var origin = SummaryProvenance.SelectedIndex switch
            {
                0 => "ai", 1 => "imported", 2 => "manual", _ => null
            };
            var originChanged = origin != null && origin != p.SummaryOrigin;
            if (text != p.Summary || originChanged)
            {
                await Model.SaveSummaryAsync(text, originChanged ? origin : null);
                SummaryEditNotice.Text = "";
            }
            // Refresh from the confirmed save, independently of model reload notifications.
            SummaryText.Text = text;
            RenderMarkdown(text);
            return true;
        }
        catch (Exception ex)
        {
            SummaryEditNotice.Text = "Summary could not be saved. Your edits are still here.";
            await ShowError(ex);
            return false;
        }
    }

    /// <summary>WinUI TextBoxes expose line breaks as '\r'; store and render one newline form.</summary>
    private static string NormalizeSummaryText(string? text) =>
        (text ?? "").TrimStart('\uFEFF').Replace("\r\n", "\n").Replace('\r', '\n');

    private async void OpenGeminiSettings_Click(object sender, RoutedEventArgs e) => await ShowSettingsSectionAsync(3);

    private async void AiRetry_Click(object sender, RoutedEventArgs e)
    {
        if (Model.Busy) return;
        try
        {
            var events = await Model.RunAsync("check-api-key", null, false);
            if (events.Any(item => item.Data.TryGetProperty("models", out _))) Model.ClearAiIssue();
            UpdateApiKeyUi();
        }
        catch (Exception ex) { await ShowError(ex); }
    }

    private void UpdateApiKeyUi()
    {
        UpdateEditorProjectButtons();
        var hasKey = Model.HasApiKey;
        var isInvalid = Model.IsApiKeyInvalid;
        var canUseAi = hasKey && !isInvalid && !Model.AiProcessingBlocked && Model.Idle;
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

        if (AiWarningBar != null)
        {
            AiWarningBar.IsOpen = Model.AiProcessingBlocked;
            AiWarningBar.Title = Model.AiIssueTitle ?? "Gemini AI Processing Paused";
            AiWarningBar.Message = (Model.AiIssueMessage ?? "Gemini AI processing is paused.") +
                " Existing transcripts, descriptions, narration, and exports are unchanged.";
        }

        if (GalleryImport != null)
        {
            GalleryImport.IsEnabled = Model.Idle;
            ToolTipService.SetToolTip(GalleryImport, "Create a new local video or YouTube project");
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
