using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Vidi_Studio_Windows.Models;
using Windows.System;

namespace Vidi_Studio_Windows;

public sealed partial class MainWindow
{
    private bool editMode;
    private bool galleryReady;
    private void InitializePresentation()
    {
        Model.Projects.CollectionChanged += (_, _) => RefreshGalleryCount();
        Model.NarrationFailures += OnNarrationFailures;
        Root.Loaded += (_, _) => { galleryReady = true; RefreshGalleryCount(); };
    }
    private async void OnNarrationFailures(object? sender, NarrationFailureReport report)
    {
        try { await ShowNarrationFailuresAsync(report); }
        catch (Exception ex) { Model.Engine.Log(ex.ToString()); }
    }
    private async void ReviewNarrationIssues_Click(object sender, RoutedEventArgs e)
    {
        if (Model.NarrationIssueReport() is not { } report) return;
        try { await ShowNarrationFailuresAsync(report); }
        catch (Exception ex) { Model.Engine.Log(ex.ToString()); }
    }
    private async void ReviewNarrationVoices_Click(object sender, RoutedEventArgs e)
    {
        if (Model.NarrationVoiceUsageReport() is not { } report) return;
        try { await ShowNarrationVoicesAsync(report); }
        catch (Exception ex) { Model.Engine.Log(ex.ToString()); }
    }
    private Task<ContentDialogResult> ShowNarrationVoicesAsync(NarrationVoiceReport report)
    {
        var hasSegments = report.AllSegments.Count > 0;
        var heading = new Grid { ColumnSpacing = 10, Margin = new Thickness(0, 0, 0, 8) };
        heading.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        heading.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        heading.Children.Add(new FontIcon { Glyph = report.HasDifferentVoices ? "\uE7BA" : "\uE73E", VerticalAlignment = VerticalAlignment.Top });
        var detail = new TextBlock { Text = report.Detail, TextWrapping = TextWrapping.Wrap };
        Grid.SetColumn(detail, 1); heading.Children.Add(detail);
        var panel = new StackPanel { Spacing = 8, MinWidth = hasSegments ? 820 : 0 };
        panel.Children.Add(heading);
        if (report.VoiceSummaries.Count > 0)
        {
            panel.Children.Add(new TextBlock { Text = "Voice usage by segment count", FontWeight = Microsoft.UI.Text.FontWeights.SemiBold, FontSize = 13 });
            foreach (var summary in report.VoiceSummaries)
            {
                var segmentText = summary.SegmentNumbers.Count == 0
                    ? "0 segments"
                    : $"{summary.Count} segment{(summary.Count == 1 ? "" : "s")} (AD {string.Join(", ", summary.SegmentNumbers)})";
                var summaryRow = new TextBlock
                {
                    Text = $"• {summary.CombinedLabel}{(summary.IsDifferentVoice ? " · Different voice" : " · Primary voice")}: {segmentText}",
                    TextWrapping = TextWrapping.Wrap,
                    FontSize = 12,
                    FontWeight = summary.IsDifferentVoice ? Microsoft.UI.Text.FontWeights.SemiBold : Microsoft.UI.Text.FontWeights.Normal
                };
                panel.Children.Add(summaryRow);
            }
        }
        if (report.DifferentSegments.Count > 0)
        {
            panel.Children.Add(new TextBlock
            {
                Text = $"Segments using a different voice: AD {string.Join(", ", report.DifferentSegments.Select(s => s.Index))}",
                FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                FontSize = 13
            });
        }
        if (hasSegments)
        {
            panel.Children.Add(new TextBlock
            {
                Text = "Start and end: HH:MM:SS.mmm (hours:minutes:seconds.milliseconds). Duration is in seconds.",
                FontSize = 12, TextWrapping = TextWrapping.Wrap, Opacity = 0.7
            });
        }
        Grid MakeVoiceRow(string index, string start, string end, string duration, string voiceEngine, string reason, bool header, NarrationVoiceSegmentEntry? entry = null)
        {
            var row = new Grid { ColumnSpacing = 12 };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(36) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(120) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(120) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(86) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(180) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star), MinWidth = 240 });
            void Add(int column, string text, string? accessibleName = null)
            {
                var block = new TextBlock
                {
                    Text = text,
                    TextWrapping = column >= 4 ? TextWrapping.Wrap : TextWrapping.NoWrap,
                    VerticalAlignment = VerticalAlignment.Top,
                    IsTextSelectionEnabled = !header && column >= 4,
                    FontWeight = header || (entry?.UsesDifferentVoice == true && column >= 4) ? Microsoft.UI.Text.FontWeights.SemiBold : Microsoft.UI.Text.FontWeights.Normal,
                    Opacity = header ? 0.7 : 1
                };
                if (column < 4) block.FontFamily = new FontFamily("Consolas");
                Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(block, accessibleName ?? text);
                Grid.SetColumn(block, column);
                row.Children.Add(block);
            }
            Add(0, index);
            Add(1, start, entry == null ? null : $"Start: {NarrationTimeFormat.SpokenTime(entry.Start)}");
            Add(2, end, entry == null ? null : $"End: {NarrationTimeFormat.SpokenTime(entry.End)}");
            Add(3, duration, entry == null ? null : $"Duration: {NarrationTimeFormat.SpokenDuration(entry.Duration)}");
            Add(4, voiceEngine);
            Add(5, reason);
            return row;
        }
        if (hasSegments) panel.Children.Add(MakeVoiceRow("AD", "Start", "End", "Duration", "Voice & Engine", "Details", true));
        foreach (var entry in report.AllSegments)
            panel.Children.Add(MakeVoiceRow(entry.Index.ToString(), NarrationTimeFormat.Timestamp(entry.Start), NarrationTimeFormat.Timestamp(entry.End),
                NarrationTimeFormat.Duration(entry.Duration), entry.CombinedLabel, entry.Reason, false, entry));
        var scroll = new ScrollViewer
        {
            Content = panel, MaxHeight = 420,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollMode = ScrollMode.Auto,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto
        };
        if (hasSegments)
        {
            panel.Width = 820;
            scroll.SizeChanged += (_, args) => panel.Width = Math.Max(820, args.NewSize.Width - 16);
        }
        var dialog = new ContentDialog
        {
            Title = report.Title,
            Content = scroll,
            CloseButtonText = "Close",
            DefaultButton = ContentDialogButton.Close
        };
        if (hasSegments)
        {
            dialog.Width = Math.Min(980, Math.Max(0, (Root.XamlRoot?.Size.Width ?? 1080) - 80));
            dialog.MinWidth = 0;
            dialog.MaxWidth = 980;
            dialog.HorizontalContentAlignment = HorizontalAlignment.Stretch;
            dialog.Resources["ContentDialogMinWidth"] = 0.0;
            dialog.Resources["ContentDialogMaxWidth"] = 980.0;
        }
        return ShowDialogAsync(dialog, hasSegments ? 1060 : 680, hasSegments ? 600 : 420);
    }
    private Task<ContentDialogResult> ShowNarrationFailuresAsync(NarrationFailureReport report)
    {
        var healthy = report.Failures.Count == 0;
        var heading = new Grid { ColumnSpacing = 10, Margin = new Thickness(0, 0, 0, 8) };
        heading.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        heading.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        heading.Children.Add(new FontIcon { Glyph = healthy ? "\uE73E" : "\uE7BA", VerticalAlignment = VerticalAlignment.Top });
        var detail = new TextBlock { Text = report.Detail, TextWrapping = TextWrapping.Wrap };
        Grid.SetColumn(detail, 1); heading.Children.Add(detail);
        var panel = new StackPanel { Spacing = 6, MinWidth = healthy ? 0 : 760 };
        panel.Children.Add(heading);
        if (!healthy)
        {
            panel.Children.Add(new TextBlock
            {
                Text = "Start and end: HH:MM:SS.mmm (hours:minutes:seconds.milliseconds). Duration is in seconds.",
                FontSize = 12, TextWrapping = TextWrapping.Wrap, Opacity = 0.7
            });
        }
        Grid MakeRow(string index, string start, string end, string duration, string reason, bool header, NarrationFailure? failure = null)
        {
            var row = new Grid { ColumnSpacing = 12 };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(36) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(124) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(124) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(88) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star), MinWidth = 300 });
            void Add(int column, string text, string? accessibleName = null)
            {
                var block = new TextBlock
                {
                    Text = text,
                    TextWrapping = column == 4 ? TextWrapping.Wrap : TextWrapping.NoWrap,
                    VerticalAlignment = VerticalAlignment.Top,
                    IsTextSelectionEnabled = !header && column == 4,
                    FontWeight = header ? Microsoft.UI.Text.FontWeights.SemiBold : Microsoft.UI.Text.FontWeights.Normal,
                    Opacity = header ? 0.7 : 1
                };
                if (column < 4) block.FontFamily = new FontFamily("Consolas");
                Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(block, accessibleName ?? text);
                Grid.SetColumn(block, column);
                row.Children.Add(block);
            }
            Add(0, index);
            Add(1, start, failure == null ? null : $"Start: {NarrationTimeFormat.SpokenTime(failure.Start)}");
            Add(2, end, failure == null ? null : $"End: {NarrationTimeFormat.SpokenTime(failure.End)}");
            Add(3, duration, failure == null ? null : $"Duration: {NarrationTimeFormat.SpokenDuration(failure.Duration)}");
            Add(4, reason);
            return row;
        }
        if (!healthy) panel.Children.Add(MakeRow("AD", "Start", "End", "Duration", "Reason", true));
        foreach (var failure in report.Failures)
            panel.Children.Add(MakeRow(failure.Index.ToString(), NarrationTimeFormat.Timestamp(failure.Start), NarrationTimeFormat.Timestamp(failure.End),
                NarrationTimeFormat.Duration(failure.Duration), string.IsNullOrEmpty(failure.Reason) ? "Unknown error" : failure.Reason, false, failure));
        var scroll = new ScrollViewer
        {
            Content = panel, MaxHeight = 420,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollMode = ScrollMode.Auto,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto
        };
        if (!healthy)
        {
            // A finite table width makes the star-sized Reason column wrap at
            // the viewport width. Preserve a readable table on smaller windows
            // via horizontal scrolling instead of squeezing reasons into a sliver.
            panel.Width = 760;
            scroll.SizeChanged += (_, args) => panel.Width = Math.Max(760, args.NewSize.Width - 16);
        }
        var dialog = new ContentDialog
        {
            Title = report.Title,
            Content = scroll,
            CloseButtonText = "Close",
            DefaultButton = ContentDialogButton.Close
        };
        if (!healthy)
        {
            // The default ContentDialog cap applies even inside a wider host
            // window. Override the inner dialog too, as the Export dialog does.
            dialog.Width = Math.Min(960, Math.Max(0, (Root.XamlRoot?.Size.Width ?? 1060) - 100));
            dialog.MinWidth = 0;
            dialog.MaxWidth = 960;
            dialog.HorizontalContentAlignment = HorizontalAlignment.Stretch;
            dialog.Resources["ContentDialogMinWidth"] = 0.0;
            dialog.Resources["ContentDialogMaxWidth"] = 960.0;
        }
        return ShowDialogAsync(dialog, healthy ? 680 : 1040, healthy ? 420 : 580);
    }
    private void RefreshGalleryCount()
    {
        if (ProjectCount == null) return;
        ProjectCount.Text = $"{Model.Projects.Count} projects";
        GalleryEmpty.Visibility = Model.Projects.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        UpdateApiKeyUi();
    }
    private void ShowGallery_Click(object sender, RoutedEventArgs e)
    {
        StopProjectPlayback();
        GalleryPanel.Visibility = Visibility.Visible; EditorWorkspace.Visibility = Visibility.Collapsed;
        UpdateMenuBar(false);
        ProjectTitle.Text = "All Projects"; Title = "All Projects — Vidi Studio";
        TopOpenFolderButton.Visibility = TopInspectorToggle.Visibility = Visibility.Collapsed;
        GallerySearchHost.Visibility = Visibility.Visible;
        GalleryImport.Visibility = Visibility.Visible;
        Model.Reload(); RefreshGalleryCount();
    }
    private void StopProjectPlayback()
    {
        video.Pause(); speech.Pause(); narrationPlayer.Pause(); onDeviceNarrator.Stop(); readback.Stop();
        ClearSegmentPreview();
        activeCue = null;
        _ = PauseYouTubeAsync();
        UpdatePlaybackControls();
    }
    private async Task PauseYouTubeAsync()
    {
        youtubePlaying = false;
        UpdatePlaybackControls();
        if (YouTubePlayer.CoreWebView2 == null) return;
        try { await YouTubePlayer.ExecuteScriptAsync("window.vidiPause?.()"); }
        catch (Exception ex) { Model.Engine.Log(ex.ToString()); }
    }
    private void ClearProject()
    {
        StopProjectPlayback();
        SetSummaryEditMode(false);
        video.Source = speech.Source = narrationPlayer.Source = null;
        foreach (var active in activeSegmentRows) active.IsActive = false;
        activeSegmentRows.Clear();
        Model.Project = null; loadedProject = null; highlightedSegment = null;
        Segments.ItemsSource = null; ExportGroups.Children.Clear();
        ClearSummaryHighlight(); SummaryRichText.Children.Clear(); summaryLines.Clear(); ReadbackSentence.Text = "";
        ProjectDetails.Children.Clear(); VideoDetails.Children.Clear(); InspectorExports.Children.Clear();
        Thumbnails.Children.Clear(); Thumbnails.ColumnDefinitions.Clear(); ThumbnailEmpty.Visibility = Visibility.Visible;
        TranscriptTrack.Children.Clear(); DescriptionTrack.Children.Clear(); UpdateTimelineCursor();
        CaptionText.Text = DescriptionCaptionText.Text = ""; CaptionOverlay.Visibility = Visibility.Collapsed;
        ShowGallery_Click(this, new RoutedEventArgs());
    }
    private void ShowEditor()
    {
        GalleryPanel.Visibility = Visibility.Collapsed; EditorWorkspace.Visibility = Visibility.Visible;
        UpdateMenuBar(true);
        TopOpenFolderButton.Visibility = TopInspectorToggle.Visibility = Visibility.Visible;
        GallerySearchHost.Visibility = Visibility.Collapsed;
        GalleryImport.Visibility = Visibility.Collapsed;
        Title = (Model.Project?.Name ?? "Project") + " — Vidi Studio";
    }
    private void GalleryFilter_Changed(object sender, SelectionChangedEventArgs e) { if (galleryReady) FilterGallery(); }
    private void GallerySearch_Changed(object sender, TextChangedEventArgs e) { if (galleryReady) FilterGallery(); }
    private void FilterGallery() => Model.Reload(GallerySearch.Text, GalleryFilter.SelectedIndex switch { 1 => "local", 2 => "youtube", _ => "all" });
    private void GalleryLayout_Click(object sender, RoutedEventArgs e)
    {
        var list = GalleryGrid.Visibility == Visibility.Visible;
        GalleryGrid.Visibility = list ? Visibility.Collapsed : Visibility.Visible;
        GalleryTable.Visibility = list ? Visibility.Visible : Visibility.Collapsed;
    }
    private void GalleryProject_Click(object sender, ItemClickEventArgs e) { if (e.ClickedItem is StudioProject p) App.OpenWindow(p.Folder); }
    private async void NewProject_Click(object sender, RoutedEventArgs e) => await ShowNewProjectDialogAsync(false);
    private void GenerateCurrent_Click(object sender, RoutedEventArgs e)
    {
        var command = Section.SelectedIndex switch { 0 => "transcribe", 1 => "generate-summary", 2 => "generate-ad-cues", _ => "synthesize-tts" };
        Command_Click(new Button { Tag = command }, e);
    }
    private async void EditMode_Click(object sender, RoutedEventArgs e)
    {
        if (Section.SelectedIndex == 1)
        {
            if (summaryEditMode)
            {
                await SaveSummaryChangesAsync();
                SetSummaryEditMode(false);
            }
            else
            {
                SetSummaryEditMode(true);
            }
            return;
        }
        editMode = !editMode; EditModeButton.Content = editMode ? "Done" : "Edit";
        EditToolbar.Visibility = editMode ? Visibility.Visible : Visibility.Collapsed;
        Segments.SelectionMode = editMode ? ListViewSelectionMode.Multiple : ListViewSelectionMode.None;
        RefreshEditingRows();
    }
    private void RefreshEditingRows()
    {
        UpdateSegmentMoreVisibility();
        if (Segments.ItemsSource is IEnumerable<Segment> rows)
            foreach (var row in rows) row.IsEditing = editMode;
    }
    private void UpdateSegmentMoreVisibility()
    {
        var timedSection = Section.SelectedIndex is 0 or 2 or 4;
        EditorMoreButton.Visibility = !timedSection || editMode ? Visibility.Visible : Visibility.Collapsed;
    }
    // Older generated XAML can still reference the pre-rename handler during
    // incremental builds. Route it through the current preserve-play-state action.
    private void SegmentSeek_Click(object sender, RoutedEventArgs e) => SegmentPlay_Click(sender, e);

    private void SegmentPlay_Click(object sender, RoutedEventArgs e)
    {
        if (((FrameworkElement)sender).Tag is not Segment segment) return;
        // Preserve the play state: keep playing when the video was already
        // playing, otherwise just move the playhead to the segment start.
        var resume = IsVideoPlaying();
        Seek(segment.Start);
        if (resume) StartVideoPlayback();
    }
    private void SegmentPreview_Click(object sender, RoutedEventArgs e)
    {
        if (((FrameworkElement)sender).Tag is not Segment segment || Model.Project is not { } p) return;
        if (segment.IsPreviewing) { StopSegmentPreview(); return; }
        if (!segment.AudioReady || segment.Audio == null) return;
        StopSegmentPreview();
        previewingSegment = segment;
        segment.IsPreviewing = true;
        speech.Source = Windows.Media.Core.MediaSource.CreateFromUri(new Uri(p.Resolve(segment.Audio)));
        speech.Play();
    }
    private void StopSegmentPreview()
    {
        speech.Pause();
        speech.Source = null;
        ClearSegmentPreview();
    }
    private void ClearSegmentPreview()
    {
        if (previewingSegment is not { } previous) return;
        previous.IsPreviewing = false;
        previewingSegment = null;
    }
    private void UpdateSectionPresentation()
    {
        if (Model.Project is not { } p) return;
        var index = Section.SelectedIndex;
        foreach (var segment in p.Transcript.Concat(p.Cues)) segment.ShowKind = index == 4;
        // Reset the source so row templates see the combined-view labels.
        var name = index switch { 0 => "Transcript", 1 => "Summary", 2 => "Audio Descriptions", 3 => "Narration", 4 => "AD Transcript", _ => "Created Exports" };
        var existing = index switch { 0 => p.Transcript.Count > 0, 1 => p.Summary.Length > 0, 2 => p.Cues.Count > 0, _ => false };
        GenerateCurrent.Content = InspectorGenerate.Content = (existing ? "Regenerate " : "Generate ") + name + "…";
        GenerateCurrent.Visibility = InspectorGenerate.Visibility = index <= 2 ? Visibility.Visible : Visibility.Collapsed;
        InspectorSectionTitle.Text = index == 2 ? "AD Segment Generation" : name;
        UpdateSegmentMoreVisibility();
        ContextActions.Visibility = index <= 2 ? Visibility.Visible : Visibility.Collapsed;
        InspectorRewriteButton.Visibility = index == 2 ? Visibility.Visible : Visibility.Collapsed;
        NarrationActions.Visibility = index is 2 or 3 or 4 ? Visibility.Visible : Visibility.Collapsed;
        Narration.Visibility = index is 2 or 3 or 4 ? Visibility.Visible : Visibility.Collapsed;
        Follow.Visibility = index is 0 or 2 or 4 ? Visibility.Visible : Visibility.Collapsed;
        EditModeButton.Visibility = index is 0 or 1 or 2 or 4 ? Visibility.Visible : Visibility.Collapsed;
        EditModeButton.Content = (index == 1 ? summaryEditMode : editMode) ? "Done" : "Edit";
        EditToolbar.Visibility = editMode && index is 0 or 2 or 4 ? Visibility.Visible : Visibility.Collapsed;
        OverviewPanel.Visibility = index == 3 ? Visibility.Visible : Visibility.Collapsed;
        OverviewStatistics.Children.Clear(); OriginStatistics.Children.Clear();
        AddInfo(OverviewStatistics, "Description segments", p.Cues.Count.ToString());
        AddInfo(OverviewStatistics, "Narration audio ready", p.Cues.Count(c => c.AudioReady).ToString());
        AddInfo(OverviewStatistics, "Audio needs updating", p.Cues.Count(c => c.AudioNeedsRegeneration).ToString());
        AddInfo(OverviewStatistics, "Audio missing", p.Cues.Count(c => !c.AudioReady && !c.AudioNeedsRegeneration).ToString());
        AddInfo(OverviewStatistics, "Description time", TimeSpan.FromSeconds(p.Cues.Sum(c => c.End - c.Start)).ToString(@"mm\:ss"));
        AddInfo(OverviewStatistics, "Spoken transcript segments", p.Transcript.Count.ToString());
        var voiceReport = Model.NarrationVoiceUsageReport();
        var voiceStat = voiceReport == null || voiceReport.AllSegments.Count == 0
            ? "None"
            : voiceReport.HasDifferentVoices
                ? $"{voiceReport.VoiceSummaries.Count(s => s.Count > 0)} voices ({voiceReport.DifferentSegments.Count} different)"
                : voiceReport.PrimaryVoiceLabel ?? Model.CurrentNarrationSummary();
        AddInfo(OverviewStatistics, "Narration voices", voiceStat);
        foreach (var origin in new[] { ("ai", "AI-generated"), ("ai_modified", "AI-generated · modified"), ("manual", "Manually added"), ("unknown", "Origin unknown") })
            AddInfo(OriginStatistics, origin.Item2, p.Cues.Count(c => c.Origin == origin.Item1).ToString());
        OriginStatistics.Children.Add(new TextBlock { Text = "AI-generated and modified segments are counted separately. Manual descriptions stay manual when edited.", TextWrapping = TextWrapping.Wrap, FontSize = 12, Opacity = .65 });
        OverviewVoice.Text = "Speech engine: " + EngineTitle(Model.Settings.Get("ttsMode", "windows-core")) + "\nVoice: " + NarrationVoiceFormatting.VoiceDisplayName(Model.CurrentNarrationVoice());
        if (OverviewVoiceWarningBorder != null && OverviewVoiceWarningText != null)
        {
            if (voiceReport is { HasDifferentVoices: true })
            {
                var lines = new List<string> { "Different narration voices are used in this project", voiceReport.Detail };
                foreach (var summary in voiceReport.VoiceSummaries)
                {
                    var segs = summary.SegmentNumbers.Count == 0 ? "0 segments" : $"{summary.Count} segment{(summary.Count == 1 ? "" : "s")} (AD {string.Join(", ", summary.SegmentNumbers)})";
                    lines.Add($"• {summary.CombinedLabel}{(summary.IsDifferentVoice ? " — Different voice" : " — Primary voice")}: {segs}");
                }
                OverviewVoiceWarningText.Text = string.Join("\n", lines);
                OverviewVoiceWarningBorder.Visibility = Visibility.Visible;
            }
            else
            {
                OverviewVoiceWarningText.Text = "";
                OverviewVoiceWarningBorder.Visibility = Visibility.Collapsed;
            }
        }
        UpdateApiKeyUi();
    }
    private static string EngineTitle(string mode) => mode switch
    {
        "gemini" => "Gemini Voices",
        "sapi5" => "Windows SAPI 5",
        "windows-natural" => "Windows SAPI 5",
        "azure" => "Azure Voices",
        _ => "Windows OneCore",
    };
    private static void AddInfo(StackPanel panel, string label, string value)
    {
        var row = new Grid { ColumnSpacing = 10 };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto }); row.ColumnDefinitions.Add(new ColumnDefinition());
        row.Children.Add(new TextBlock { Text = label, FontSize = 12 });
        var text = new TextBlock { Text = value, TextTrimming = TextTrimming.CharacterEllipsis, HorizontalAlignment = HorizontalAlignment.Right, FontSize = 12, Opacity = .65 };
        ToolTipService.SetToolTip(text, value); Grid.SetColumn(text, 1); row.Children.Add(text); panel.Children.Add(row);
    }
    private async void UpdateInspector(StudioProject p)
    {
        ProjectDetails.Children.Clear(); VideoDetails.Children.Clear(); InspectorExports.Children.Clear();
        AddInfo(ProjectDetails, "Name", p.Name); AddInfo(ProjectDetails, "Type", p.IsYouTube ? "YouTube" : "Local Video");
        AddInfo(ProjectDetails, "Folder", p.Folder);
        AddInfo(VideoDetails, "Source", p.IsYouTube ? p.YoutubeURL ?? "" : Path.GetFileName(p.Source));
        AddInfo(VideoDetails, "Duration", TimeSpan.FromSeconds(p.Duration).ToString(@"hh\:mm\:ss"));
        AddInfo(VideoDetails, "Resolution", $"{p.Width} × {p.Height}");
        try
        {
            var probe = p.Resolve("metadata/probe.json");
            if (File.Exists(probe))
            {
                using var doc = JsonDocument.Parse(File.ReadAllText(probe));
                string Value(JsonElement node, string key) => node.TryGetProperty(key, out var v) ? v.ToString() : "Not reported";
                if (doc.RootElement.TryGetProperty("streams", out var streams))
                    foreach (var stream in streams.EnumerateArray())
                    {
                        if (Value(stream, "codec_type") == "video")
                        {
                            AddInfo(VideoDetails, "Video", Value(stream, "codec_long_name")); AddInfo(VideoDetails, "Frame rate", Value(stream, "avg_frame_rate"));
                            AddInfo(VideoDetails, "Color / HDR", Value(stream, "color_transfer") + ", " + Value(stream, "pix_fmt"));
                        }
                        else if (Value(stream, "codec_type") == "audio") AddInfo(VideoDetails, "Audio", Value(stream, "codec_name") + ", " + Value(stream, "channels") + " channels");
                    }
                if (doc.RootElement.TryGetProperty("format", out var format)) { AddInfo(VideoDetails, "Container", Value(format, "format_long_name")); if (long.TryParse(Value(format, "size"), out var size)) AddInfo(VideoDetails, "Source size", $"{size / 1048576d:F1} MB"); }
            }
            PopulateExportGroups(InspectorExports, p);
            UpdateAvailability();
            var bytes = await Task.Run(() => Directory.EnumerateFiles(p.Folder, "*", SearchOption.AllDirectories).Sum(file => new FileInfo(file).Length));
            if (ReferenceEquals(Model.Project, p)) AddInfo(ProjectDetails, "Project size", $"{bytes / 1048576d:F1} MB");
        }
        catch (Exception ex) { Model.Engine.Log(ex.ToString()); }
    }
    private static (string Name, string Glyph, int Order) ExportCategoryFor(StudioProject project, string relative)
    {
        var extension = Path.GetExtension(relative).ToLowerInvariant();
        var name = ExportDisplayName(project, relative);
        if (extension is ".mp4" or ".mov" or ".m4v" or ".mkv" or ".webm") return ("Video", "\uE714", 0);
        if (extension is ".mp3" or ".m4a" or ".wav" or ".aac" or ".flac") return ("Audio", "\uE767", 1);
        if (extension == ".md") return ("Summary", "\uE8A5", 3);
        if (extension is ".srt" or ".vtt" or ".txt" or ".json")
        {
            if (name.Contains("AD Transcript", StringComparison.OrdinalIgnoreCase)) return ("AD Transcript", "\uE8A5", 5);
            if (name.Contains("Descriptions", StringComparison.OrdinalIgnoreCase)) return ("Descriptions", "\uE8A5", 4);
            if (name.Contains("Transcript", StringComparison.OrdinalIgnoreCase)) return ("Transcript", "\uE8A5", 2);
            return ("Text", "\uE8A5", 6);
        }
        if (extension.Length == 0 || name.Contains("Web Playback", StringComparison.OrdinalIgnoreCase)) return ("Web Playback", "\uE774", 7);
        return ("Other exports", "\uE8A5", 8);
    }
    private static string ExportDisplayName(StudioProject project, string relative)
    {
        var name = Path.GetFileName(relative);
        var prefix = project.Name + " - ";
        return name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) ? name[prefix.Length..] : name;
    }
    private void PopulateExportGroups(StackPanel target, StudioProject project)
    {
        target.Children.Clear();
        if (project.Exports.Count == 0)
        {
            target.Children.Add(new TextBlock { Text = "No exports yet", Opacity = .65 });
            return;
        }
        var groups = project.Exports.Select(relative => (Relative: relative, Category: ExportCategoryFor(project, relative)))
            .GroupBy(item => item.Category.Name).OrderBy(group => group.First().Category.Order);
        foreach (var group in groups)
        {
            var category = group.First().Category;
            var section = new StackPanel { Spacing = 6, Margin = new Thickness(0, target.Children.Count == 0 ? 0 : 12, 0, 0) };
            var heading = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
            heading.Children.Add(new FontIcon { Glyph = category.Glyph, FontSize = 16,
                Foreground = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 0, 120, 212)) });
            heading.Children.Add(new TextBlock { Text = category.Name, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                VerticalAlignment = VerticalAlignment.Center });
            section.Children.Add(heading);
            foreach (var item in group)
            {
                var row = new Grid { ColumnSpacing = 6 };
                row.ColumnDefinitions.Add(new ColumnDefinition());
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                var button = new Button { Content = new TextBlock { Text = ExportDisplayName(project, item.Relative),
                    TextTrimming = TextTrimming.CharacterEllipsis }, Tag = item.Relative,
                    HorizontalAlignment = HorizontalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Stretch };
                ToolTipService.SetToolTip(button, item.Relative);
                button.Click += ExportOpen_Click;
                row.Children.Add(button);
                var info = new Button { Content = "\uE946", FontFamily = new FontFamily("Segoe Fluent Icons"), Tag = item.Relative };
                Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(info, "Export information for " + Path.GetFileName(item.Relative));
                ToolTipService.SetToolTip(info, "Export information");
                info.Click += ExportInfo_Click;
                Grid.SetColumn(info, 1);
                row.Children.Add(info);
                section.Children.Add(row);
            }
            target.Children.Add(section);
        }
    }
    private void UpdateAvailability()
    {
        if (Model.Project is not { } p) return;
        if (p.IsYouTube) { AnalysisAvailability.Text = "YouTube is accessed through its public link."; return; }
        if (p.Extra?.TryGetValue("geminiVideo", out var state) == true && state.ValueKind == JsonValueKind.Object)
        {
            var label = state.TryGetProperty("state", out var name) ? name.GetString() : "Not checked";
            if (state.TryGetProperty("expiresAt", out var expiry) && expiry.TryGetDouble(out var seconds))
            {
                var remaining = TimeSpan.FromSeconds(Math.Max(0, seconds - DateTimeOffset.UtcNow.ToUnixTimeSeconds()));
                label += $" · {remaining.Days}d {remaining.Hours}h {remaining.Minutes}m {remaining.Seconds}s remaining";
            }
            AnalysisAvailability.Text = label;
        }
        else AnalysisAvailability.Text = "Not prepared for video analysis.";
    }
}
