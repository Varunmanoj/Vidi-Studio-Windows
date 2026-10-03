using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
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
    private bool youtubePolling;
    private bool youtubeMetadataUpdating;
    private bool ready;
    private Segment? highlightedSegment;
    private readonly HashSet<Segment> activeSegmentRows = new();
    private bool timelineScrubbing;
    private bool playheadDragging;
    private Views.SettingsDialog? openSettings;
    private double Position => Model.Project?.IsYouTube == true ? youtubePosition : video.PlaybackSession.Position.TotalSeconds;
    public MainWindow(string? folder = null)
    {
        readback = new SummaryReadback(Model.Engine, Model.Settings);
        onDeviceNarrator = new OnDeviceNarrator(Model.Settings, Model.Engine);
        InitializeComponent();
        ActiveExports.Observe(Model);
        Model.PropertyChanged += UpdateStatusFooter;
        Views.WindowAppearance.ApplyIcon(this);
        App.ThemeChanged += ApplyThemeFromApp;
        BuildMenus();
        video.PlaybackSession.PlaybackStateChanged += (_, _) => DispatcherQueue.TryEnqueue(UpdatePlaybackControls);
        InitializePresentation();
        Root.ActualThemeChanged += (_, _) => UpdateTitleBarTheme(Root.ActualTheme);
        readback.SentenceChanged += sentence => DispatcherQueue.TryEnqueue(() => { ReadbackSentence.Text = sentence; HighlightReadbackSentence(sentence); });
        readback.WordChanged += (start, length) => DispatcherQueue.TryEnqueue(() => HighlightReadbackWord(start, length));
        readback.StateChanged += _ => DispatcherQueue.TryEnqueue(UpdateSummaryControls);
        if (AppWindow.Presenter is Microsoft.UI.Windowing.OverlappedPresenter presenter) presenter.Maximize();
        Player.SetMediaPlayer(video);
        Model.ProjectReloaded += (_, _) => RefreshProject();
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
        Root.Loaded += (_, _) => { ready = true; Model.Reload(); ApplyTheme(); if (Model.Settings.Get("showPreview", "true") == "false") PreviewPanel.Visibility = TimelinePanel.Visibility = Visibility.Collapsed; if (folder != null) Model.Open(folder); if (Model.Settings.Get("projectPresentation") == "grid") ToggleGrid_Click(this, new RoutedEventArgs()); };
#if DEBUG
        Root.Loaded += async (_, _) => { if (Environment.GetEnvironmentVariable("VIDISTUDIO_UI_SMOKE_REPORT") is { } report && System.Threading.Interlocked.CompareExchange(ref smokeTestClaimed, 1, 0) == 0) await RunSmokeTestAsync(report); };
#endif
        AddShortcut(VirtualKey.N, VirtualKeyModifiers.Control, ImportVideo_Click);
        AddShortcut(VirtualKey.O, VirtualKeyModifiers.Control | VirtualKeyModifiers.Shift, OpenProject_Click);
        AddShortcut(VirtualKey.E, VirtualKeyModifiers.Control | VirtualKeyModifiers.Shift, Export_Click);
        AddShortcut(VirtualKey.Space, VirtualKeyModifiers.Control | VirtualKeyModifiers.Shift, PlayPause_Click);
        for (var index = 0; index < 5; index++)
        {
            var section = index;
            var shortcut = new KeyboardAccelerator { Key = (VirtualKey)((int)VirtualKey.Number1 + index), Modifiers = VirtualKeyModifiers.Control };
            shortcut.Invoked += (_, e) => { Section.SelectedIndex = section; e.Handled = true; };
            Root.KeyboardAccelerators.Add(shortcut);
        }
    }
    private void AddShortcut(VirtualKey key, VirtualKeyModifiers modifiers, RoutedEventHandler handler)
    {
        var shortcut = new KeyboardAccelerator { Key = key, Modifiers = modifiers };
        shortcut.Invoked += (_, e) => { if (Model.Idle) handler(this, new RoutedEventArgs()); e.Handled = true; };
        Root.KeyboardAccelerators.Add(shortcut);
    }
    private void UpdateStatusFooter(object? sender, PropertyChangedEventArgs e)
    { if (e.PropertyName == nameof(Model.Busy)) StatusFooter.Visibility = Model.Idle ? Visibility.Visible : Visibility.Collapsed; }
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
    private Task<ContentDialogResult> ShowDialogAsync(ContentDialog dialog, int width = 680, int height = 460)
        => Views.WindowedDialog.ShowAsync(dialog, this, width, height);
    private async Task<bool> Confirm(string title, string text, string action = "Continue") => await ShowDialogAsync(Dialog(title, new TextBlock { Text = text, TextWrapping = TextWrapping.Wrap, MaxWidth = 520 }, action)) == ContentDialogResult.Primary;
    private async Task ShowError(Exception ex)
    {
        Model.Engine.Log(ex.ToString());
        await ShowDialogAsync(new ContentDialog { Title = "Action could not finish", Content = "Check your settings and the diagnostic log, then retry. Your saved project remains available.", CloseButtonText = "Close" });
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
            if (await ShowDialogAsync(dialog, 680, 780) != ContentDialogResult.Primary) return;
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
        ProjectTitle.Text = p.Name;
        ProjectInformation.Text = $"{(p.IsYouTube ? "YouTube" : "Local video")}\n{p.Width} × {p.Height}\n{TimeSpan.FromSeconds(p.Duration):hh\\:mm\\:ss}\n{p.Folder}";
        Playhead.Maximum = Math.Max(1, p.Duration);
        UpdateTimelineCursor();
        UpdatePlaybackControls();
        RenderSection();
        Thumbnails.Children.Clear();
        foreach (var thumbnail in p.Thumbnails)
        {
            try
            {
                var index = Thumbnails.Children.Count;
                var button = new Button { Content = new Image { Source = new BitmapImage(new Uri(p.Resolve(thumbnail))), Width = 88, Height = 50 }, Padding = new Thickness(0) };
                Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(button, $"Seek to timeline picture {index + 1}");
                button.Click += (_, _) => Seek(index * p.Duration / Math.Max(1, p.Thumbnails.Count));
                Thumbnails.Children.Add(button);
            }
            catch (Exception ex) { Model.Engine.Log(ex.ToString()); }
        }
        if (loadedProject == p.Folder) return;
        loadedProject = p.Folder; activeCue = null; speech.Pause(); narrationPlayer.Pause(); video.Pause(); onDeviceNarrator.Stop(); readback.Stop();
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
    private void Section_Changed(object sender, SelectionChangedEventArgs e) { if (ready) RenderSection(); }
    private void RenderSection()
    {
        if (Model.Project is not { } p) return;
        UpdateSectionPresentation();
        Segments.Visibility = Section.SelectedIndex is 0 or 2 or 4 ? Visibility.Visible : Visibility.Collapsed;
        SummaryView.Visibility = Section.SelectedIndex == 1 ? Visibility.Visible : Visibility.Collapsed;
        Overview.Visibility = Visibility.Collapsed;
        ExportList.Visibility = Section.SelectedIndex == 5 ? Visibility.Visible : Visibility.Collapsed;
        Segments.ItemsSource = Section.SelectedIndex switch { 0 => p.Transcript, 2 => p.Cues, _ => p.Transcript.Concat(p.Cues).OrderBy(c => c.Start).ToList() };
        SummaryText.Text = p.Summary;
        RenderMarkdown(p.Summary);
        if (Section.SelectedIndex == 1 && readback.State is SummaryReadbackState.Playing or SummaryReadbackState.Paused)
            HighlightReadbackSentence(ReadbackSentence.Text);
        RenderTracks();
        var usable = p.Cues.Count(c => c.Audio != null && c.AudioText == c.Text && File.Exists(p.Resolve(c.Audio)));
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
            else if (cue.Audio is { } audio && cue.AudioText == cue.Text && File.Exists(Model.Project.Resolve(audio)))
            { narrationPlayer.Source = MediaSource.CreateFromUri(new Uri(Model.Project.Resolve(audio))); narrationPlayer.PlaybackSession.Position = TimeSpan.FromSeconds(Math.Max(0, Position - cue.Start)); narrationPlayer.Play(); }
        }
        UpdateActiveSegment();
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
        var width = TimelineScrubSurface.ActualWidth;
        if (duration <= 0 || width <= 0) return;
        Microsoft.UI.Xaml.Controls.Canvas.SetLeft(TimelineCursor, Math.Clamp(Math.Clamp(Position / duration, 0, 1) * width - TimelineCursor.Width / 2, 0, Math.Max(0, width - TimelineCursor.Width)));
    }
    private void ScrubTimeline(PointerRoutedEventArgs e)
    {
        if (Model.Project is not { Duration: > 0 } project || TimelineScrubSurface.ActualWidth <= 0) return;
        var fraction = Math.Clamp(e.GetCurrentPoint(TimelineScrubSurface).Position.X / TimelineScrubSurface.ActualWidth, 0, 1);
        Seek(fraction * project.Duration);
    }
    private void TimelineScrubSurface_PointerPressed(object sender, PointerRoutedEventArgs e)
    {
        if (Model.Project == null) return;
        timelineScrubbing = TimelineScrubSurface.CapturePointer(e.Pointer);
        ScrubTimeline(e);
        StartVideoPlayback();
        e.Handled = true;
    }
    private void TimelineScrubSurface_PointerMoved(object sender, PointerRoutedEventArgs e)
    { if (timelineScrubbing) { ScrubTimeline(e); e.Handled = true; } }
    private void TimelineScrubSurface_PointerReleased(object sender, PointerRoutedEventArgs e)
    { if (timelineScrubbing) ScrubTimeline(e); timelineScrubbing = false; TimelineScrubSurface.ReleasePointerCapture(e.Pointer); e.Handled = true; }
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
    private void UpdatePlaybackControls()
    {
        var playing = Model.Project?.IsYouTube == true ? youtubePlaying :
            Model.Project != null && video.PlaybackSession.PlaybackState == MediaPlaybackState.Playing;
        VideoPlayPauseButton.Content = playing ? "\uE769" : "\uE768";
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(VideoPlayPauseButton, playing ? "Pause video" : "Play video");
        ToolTipService.SetToolTip(VideoPlayPauseButton, playing ? "Pause video" : "Play video");
        if (videoPlayMenuItem != null) { videoPlayMenuItem.Text = playing ? "Pause video" : "Play video"; videoPlayMenuItem.Icon = Views.MenuIcons.Create(videoPlayMenuItem.Text); }
    }
    private void Rewind_Click(object sender, RoutedEventArgs e) => Seek(Position - 5);
    private void Forward_Click(object sender, RoutedEventArgs e) => Seek(Position + 5);
    private void Segment_DoubleTapped(object sender, DoubleTappedRoutedEventArgs e) { if (Segments.SelectedItem is Segment segment) Seek(segment.Start); }
    private async void Command_Click(object sender, RoutedEventArgs e)
    {
        var command = (string)((FrameworkElement)sender).Tag;
        if (Model.Project == null) return;
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
    private async void Edit_Click(object sender, RoutedEventArgs e)
    { foreach (var segment in Segments.SelectedItems.Cast<Segment>().ToList()) await EditSegment(segment); }
    private async Task EditSegment(Segment? existing, string? initialText = null)
    {
        if (Model.Project is not { } p) return;
        var isDescription = existing?.IsDescription ?? Section.SelectedIndex != 0;
        var start = new NumberBox { Header = "Start (seconds)", Value = existing?.Start ?? Position, Minimum = 0, Maximum = p.Duration };
        var end = new NumberBox { Header = "End (seconds)", Value = existing?.End ?? Math.Min(p.Duration, Position + 3), Minimum = 0, Maximum = p.Duration };
        var text = new TextBox { Header = "Segment text", Text = initialText ?? existing?.Text ?? "", AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, MinHeight = 100 };
        var hint = new TextBox { Header = "Optional hint for Gemini", AcceptsReturn = true, TextWrapping = TextWrapping.Wrap };
        var panel = new StackPanel { Width = 560, Spacing = 10 }; panel.Children.Add(start); panel.Children.Add(end); panel.Children.Add(text); panel.Children.Add(hint);
        var dialog = Dialog(isDescription ? "Audio Description segment" : "Transcript segment", panel, "Save segment");
        dialog.SecondaryButtonText = "Re-analyze range";
        var result = await ShowDialogAsync(dialog, 700, 570);
        if (result == ContentDialogResult.None) return;
        if (!double.IsFinite(start.Value) || !double.IsFinite(end.Value) || start.Value < 0 || end.Value <= start.Value || end.Value > p.Duration)
        { await Confirm("Invalid timing", "Choose a non-empty time range within the video.", "OK"); return; }
        var draft = existing == null ? new Segment { Origin = "manual" } : JsonSerializer.Deserialize<Segment>(JsonSerializer.Serialize(existing, Json.Options), Json.Options)!;
        draft.Start = start.Value; draft.End = end.Value; draft.Text = text.Text; draft.IsDescription = isDescription;
        if (result == ContentDialogResult.Secondary)
        {
            Directory.CreateDirectory(SettingsStore.DirectoryPath);
            var path = Path.Combine(SettingsStore.DirectoryPath, Guid.NewGuid() + ".json");
            try
            {
                await File.WriteAllTextAsync(path, JsonSerializer.Serialize(new { id = draft.Id, start = draft.Start, end = draft.End, text = draft.Text, hint = hint.Text }));
                await Model.RunAsync(isDescription ? "reanalyze-segment" : "reanalyze-transcript-segment", new[] { "--input", path });
            }
            finally { File.Delete(path); }
            return;
        }
        var transcript = p.Transcript.ToList(); var cues = p.Cues.ToList();
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
        speech.Source = null;
        await Model.SaveSegmentsAsync(p.Transcript.Where(c => !selected.Contains(c.Id)), p.Cues.Where(c => !selected.Contains(c.Id)));
    }
    private async void Undo_Click(object sender, RoutedEventArgs e) => await Model.RunAsync(Section.SelectedIndex == 0 ? "undo-transcript" : "undo-cues");
    private async void Redo_Click(object sender, RoutedEventArgs e) => await Model.RunAsync(Section.SelectedIndex == 0 ? "redo-transcript" : "redo-cues");
    private async void ChangedNarration_Click(object sender, RoutedEventArgs e) => await Model.RunAsync("synthesize-tts", new[] { "--changed" });
    private async void NarrateSelected_Click(object sender, RoutedEventArgs e) { if (Segments.SelectedItem is Segment { IsDescription: true } c) await Model.RunAsync("synthesize-tts", new[] { "--segment", c.Id }); }
    private async void Rewrite_Click(object sender, RoutedEventArgs e) { if (Segments.SelectedItem is Segment { IsDescription: true } c) await Model.RunAsync("rewrite-cue", new[] { "--segment", c.Id }); }
    private void PreviewNarration_Click(object sender, RoutedEventArgs e)
    { if (Segments.SelectedItem is Segment { Audio: { } audio } && Model.Project is { } p) { speech.Source = MediaSource.CreateFromUri(new Uri(p.Resolve(audio))); speech.Play(); } }
    private async void DeleteNarration_Click(object sender, RoutedEventArgs e)
    { if (Segments.SelectedItem is Segment { IsDescription: true } c && await Confirm("Delete narration", "Remove this segment's saved audio?", "Delete")) { speech.Source = null; await Model.RunAsync("delete-segment-audio", new[] { "--segment", c.Id }); } }
    private async void Ocr_Click(object sender, RoutedEventArgs e)
    {
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
        { speech.Source = null; await Model.RunAsync("delete-generated", boxes.Where(b => b.IsChecked == true).SelectMany(b => new[] { "--component", (string)b.Tag })); }
    }
    private async void Export_Click(object sender, RoutedEventArgs e)
    { if (Model.Project != null && Model.Idle) await ShowDialogAsync(new Views.ExportDialog(Model), 1040, 760); }
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
        TimelinePanel.Visibility = PreviewPanel.Visibility;
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
        finally { openSettings = null; ApplyTheme(); Model.Reload(); }
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
            await readback.SpeakAsync(p.Summary);
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) { await ShowError(ex); }
    }
    private void StopSpeech_Click(object sender, RoutedEventArgs e) { readback.Stop(); speech.Pause(); speech.Source = null; }
}
