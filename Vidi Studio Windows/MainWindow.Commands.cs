using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Documents;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using Vidi_Studio_Windows.Models;
using Windows.System;

namespace Vidi_Studio_Windows;

public sealed partial class MainWindow
{
    private readonly List<MenuBarItem> galleryMenus = new();
    private readonly List<MenuBarItem> editorMenus = new();
    private MenuFlyoutItem? summaryPlayMenuItem;
    private MenuFlyoutItem? summaryStopMenuItem;
    private MenuFlyoutItem? videoPlayMenuItem;
    private readonly List<(TextBlock Block, string Text)> summaryLines = new();
    private TextBlock? activeSummaryBlock;
    private int activeSummaryStart;
    private int activeSummaryLength;
    private int summarySearchLine;
    private int summarySearchOffset;

    private void HighlightReadbackWord(int start, int length)
    {
        if (activeSummaryBlock == null || start < 0 || length <= 0 || start + length > activeSummaryLength) return;
        ApplySummaryHighlight(activeSummaryStart + start, length);
    }
    private static Windows.UI.Color SummaryColor(string value, string fallback)
    {
        value = value.TrimStart('#');
        if (value.Length != 6 || !uint.TryParse(value, System.Globalization.NumberStyles.HexNumber, null, out var rgb))
            rgb = Convert.ToUInt32(fallback.TrimStart('#'), 16);
        return Windows.UI.Color.FromArgb(255, (byte)(rgb >> 16), (byte)(rgb >> 8), (byte)rgb);
    }
    private void ApplySummaryHighlight(int wordStart = -1, int wordLength = 0)
    {
        if (activeSummaryBlock == null) return;
        activeSummaryBlock.TextHighlighters.Clear();
        void Add(int start, int length, string key, string fallback)
        {
            var color = SummaryColor(Model.Settings.Get(key, fallback), fallback);
            var contrast = color.R * .299 + color.G * .587 + color.B * .114 < 135 ? Microsoft.UI.Colors.White : Microsoft.UI.Colors.Black;
            var highlight = new TextHighlighter { Background = new SolidColorBrush(color), Foreground = new SolidColorBrush(contrast) };
            highlight.Ranges.Add(new TextRange { StartIndex = start, Length = length });
            activeSummaryBlock.TextHighlighters.Add(highlight);
        }
        if (Model.Settings.Get("summarySentenceHighlight", "true") != "false") Add(activeSummaryStart, activeSummaryLength, "summarySentenceHighlightColor", "#A9D6FF");
        if (wordStart >= 0 && Model.Settings.Get("summaryWordHighlight", "true") != "false") Add(wordStart, wordLength, "summaryWordHighlightColor", "#FFD54A");
    }
    private void ClearSummaryHighlight()
    {
        activeSummaryBlock?.TextHighlighters.Clear(); activeSummaryBlock = null;
        activeSummaryStart = activeSummaryLength = summarySearchLine = summarySearchOffset = 0;
    }
    private void HighlightReadbackSentence(string sentence)
    {
        activeSummaryBlock?.TextHighlighters.Clear(); activeSummaryBlock = null;
        if (sentence.Length == 0) { summarySearchLine = summarySearchOffset = 0; return; }
        for (var pass = 0; pass < 2; pass++)
        {
            var begin = pass == 0 ? summarySearchLine : 0;
            for (var i = begin; i < summaryLines.Count; i++)
            {
                var (block, line) = summaryLines[i];
                var offset = pass == 0 && i == begin ? summarySearchOffset : 0;
                var match = line.IndexOf(sentence, Math.Min(offset, line.Length), StringComparison.OrdinalIgnoreCase);
                if (match < 0) continue;
                activeSummaryBlock = block; activeSummaryStart = match; activeSummaryLength = sentence.Length;
                summarySearchLine = i; summarySearchOffset = match + sentence.Length;
                ApplySummaryHighlight();
                if (SummaryView.Visibility == Visibility.Visible)
                {
                    var point = block.TransformToVisual(SummaryPanel).TransformPoint(new Windows.Foundation.Point(0, 0));
                    SummaryPanel.ChangeView(null, Math.Max(0, point.Y + SummaryPanel.VerticalOffset - 24), null);
                }
                return;
            }
        }
    }
    private void UpdateSummaryControls()
    {
        var state = readback.State;
        SummaryPlayButton.Content = state == Services.SummaryReadbackState.Stopped ? "Play" : state == Services.SummaryReadbackState.Preparing ? "Preparing…" : state == Services.SummaryReadbackState.Playing ? "Pause" : "Resume";
        SummaryPlayButton.IsEnabled = state != Services.SummaryReadbackState.Preparing;
        SummaryStopButton.IsEnabled = state != Services.SummaryReadbackState.Stopped;
        UpdateDesktopMenuStates();
        if (state == Services.SummaryReadbackState.Stopped) ClearSummaryHighlight();
    }
    private void OpenEditor_Click(object sender, RoutedEventArgs e) { if (Model.Project is { } p) App.OpenWindow(p.Folder); }
    private void ProjectGrid_Changed(object sender, SelectionChangedEventArgs e) { if (ProjectGrid.SelectedItem is StudioProject p) Model.Open(p.Folder); }
    private void ToggleGrid_Click(object sender, RoutedEventArgs e)
    {
        var grid = ProjectGrid.Visibility != Visibility.Visible;
        ProjectGrid.Visibility = grid ? Visibility.Visible : Visibility.Collapsed;
        ProjectList.Visibility = grid ? Visibility.Collapsed : Visibility.Visible;
        Model.Settings.Set("projectPresentation", grid ? "grid" : "list"); Model.Settings.Save();
    }
    private void ToggleLibrary_Click(object sender, RoutedEventArgs e)
    { LibraryPanel.Visibility = LibraryPanel.Visibility == Visibility.Visible ? Visibility.Collapsed : Visibility.Visible; LibraryColumn.Width = new GridLength(LibraryPanel.Visibility == Visibility.Visible ? 240 : 0); }
    private void ToggleInspector_Click(object sender, RoutedEventArgs e)
    { InspectorPanel.Visibility = InspectorPanel.Visibility == Visibility.Visible ? Visibility.Collapsed : Visibility.Visible; InspectorColumn.Width = new GridLength(InspectorPanel.Visibility == Visibility.Visible ? 310 : 0); }
    private void FullScreen_Click(object sender, RoutedEventArgs e) { if (Model.Project?.IsYouTube != true) Player.IsFullWindow = !Player.IsFullWindow; }
    private void ProjectContext_Requested(UIElement sender, ContextRequestedEventArgs e)
    {
        if (!Model.Idle) return;
        var pointerRequest = e.TryGetPosition(sender, out var point);
        var element = (pointerRequest ? e.OriginalSource : FocusManager.GetFocusedElement(Root.XamlRoot)) as FrameworkElement;
        while (element != null && element.DataContext is not StudioProject) element = VisualTreeHelper.GetParent(element) as FrameworkElement;
        var project = element?.DataContext as StudioProject ?? (sender as ListViewBase)?.SelectedItem as StudioProject;
        if (project == null) return;
        var menu = new MenuFlyout();
        void Item(string label, RoutedEventHandler handler, bool enabled = true)
        {
            var item = new MenuFlyoutItem { Text = label, Icon = Views.MenuIcons.Create(label), IsEnabled = Model.Idle && enabled };
            item.Click += (s, args) => { Model.Open(project.Folder); handler(s, args); }; menu.Items.Add(item);
        }
        Item("Open editor in new window", OpenEditor_Click); Item("Export", Export_Click, Model.HasApiKey); Item("Open project folder", Reveal_Click);
        foreach (var pair in new[] { ("Analyze video", "analyze"), ("Generate transcript", "transcribe"), ("Generate summary", "generate-summary"), ("Generate descriptions", "generate-ad-cues") })
            Item(pair.Item1, (_, args) => Command_Click(new Button { Tag = pair.Item2 }, args), Model.HasApiKey);
        Item("Refresh video details", (_, args) => Command_Click(new Button { Tag = "prepare-video" }, args));
        Item("Rename", Rename_Click); Item("Move to Recycle Bin", DeleteProject_Click);
        if (pointerRequest) menu.ShowAt((FrameworkElement)sender, point);
        else menu.ShowAt(element ?? (FrameworkElement)sender);
        e.Handled = true;
    }
    private MenuFlyout SegmentMenu(Segment? segment = null)
    {
        var menu = new MenuFlyout();
        void Item(string label, RoutedEventHandler action, bool available = true)
        {
            var item = new MenuFlyoutItem { Text = label, Icon = Views.MenuIcons.Create(label), IsEnabled = Model.Idle && available, Tag = segment };
            item.Click += (_, args) =>
            {
                if (segment != null)
                {
                    if (!editMode) EditMode_Click(this, args);
                    Segments.SelectedItems.Clear(); Segments.SelectedItem = segment;
                }
                action(item, args);
            };
            menu.Items.Add(item);
        }
        if (segment != null)
        {
            Item("Edit segment", async (_, _) => await EditSegment(segment));
            Item("Play from segment", (_, _) =>
            {
                if (segment.IsDescription) Narration.IsOn = true;
                Seek(segment.Start);
                StartVideoPlayback();
            });
            Item("Skip to segment", (_, _) =>
            {
                StopProjectPlayback();
                Seek(segment.Start);
            });
            if (segment.IsDescription)
            {
                var canSynthesize = Model.CurrentNarrationEngine() != "gemini" || (Model.HasApiKey && !Model.IsApiKeyInvalid);
                var synthLabel = segment.IsNarrating
                    ? $"Generating audio for AD segment {segment.AdNumber}…"
                    : segment.IsQueuedForNarration
                        ? $"Queued for audio generation (AD {segment.AdNumber})…"
                        : (segment.Audio == null ? "Synthesize this segment" : "Regenerate segment narration");
                Item("Preview narration", PreviewNarration_Click, segment.AudioReady);
                Item(synthLabel, NarrateSelected_Click, canSynthesize);
                if (Model.IsSynthesizingNarration && !segment.IsNarrating && Model.NarratingSegmentIndex is { } activeIdx)
                    Item($"Currently generating AD segment {activeIdx}…", (_, _) => { }, false);
                Item("Rewrite description to fit", Rewrite_Click, Model.HasApiKey);
                Item("Delete narration", DeleteNarration_Click, segment.Audio != null);
            }
            else
            {
                Item("Re-Analyze this segment", async (_, _) => await EditSegment(segment), Model.HasApiKey);
            }
            menu.Items.Add(new MenuFlyoutSeparator());
            Item("Delete segment", async (_, _) =>
            {
                if (Model.Project is not { } p) return;
                if (!await Confirm("Delete segment", "This text and associated narration are permanently removed. Existing exports are invalidated.", "Delete")) return;
                speech.Source = null;
                await Model.SaveSegmentsAsync(p.Transcript.Where(c => c.Id != segment.Id), p.Cues.Where(c => c.Id != segment.Id));
            });
        }
        else
        {
            var timedSection = Section.SelectedIndex is 0 or 2 or 4;
            Item("Add segment", Add_Click, timedSection);
            Item("Edit selected", Edit_Click, timedSection && Segments.SelectedItems.Count > 0);
            Item("Select all", SelectAll_Click, timedSection);
            Item("Deselect all", Deselect_Click, timedSection && Segments.SelectedItems.Count > 0);
            Item("Delete selected", DeleteSegments_Click, timedSection && Segments.SelectedItems.Count > 0);
            Item("Undo", Undo_Click, timedSection);
            Item("Redo", Redo_Click, timedSection);
            Item(Model.Project?.AllADAudioTitle ?? "Generate All AD Audio", (_, args) => Command_Click(new Button { Tag = "synthesize-tts" }, args), CanRunDesktopCommand("synthesize-tts"));
            Item("Generate Missing or Changed AD Audio", ChangedNarration_Click, CanRunDesktopCommand("synthesize-changed"));
        }
        return menu;
    }
    private void SegmentMore_Click(object sender, RoutedEventArgs e)
    {
        if (Section.SelectedIndex is 0 or 2 or 4 && !editMode) return;
        if (sender is FrameworkElement element) SegmentMenu(element.Tag as Segment).ShowAt(element);
    }
    private void SegmentContext_Requested(UIElement sender, ContextRequestedEventArgs e)
    {
        if (!Model.Idle && !Model.IsSynthesizingNarration) return;
        var pointerRequest = e.TryGetPosition(sender, out var point);
        var element = (pointerRequest ? e.OriginalSource : FocusManager.GetFocusedElement(Root.XamlRoot)) as FrameworkElement;
        while (element != null && element.DataContext is not Segment) element = VisualTreeHelper.GetParent(element) as FrameworkElement;
        var segment = element?.DataContext as Segment ?? (sender as ListViewBase)?.SelectedItem as Segment;
        if (segment == null) return;
        var menu = SegmentMenu(segment);
        if (pointerRequest) menu.ShowAt((FrameworkElement)sender, point);
        else menu.ShowAt(element ?? (FrameworkElement)sender);
        e.Handled = true;
    }
    private void Track_SizeChanged(object sender, SizeChangedEventArgs e) => RenderTracks();
    private void RenderTracks()
    {
        if (Model.Project is not { } project || project.Duration <= 0) return;
        void Draw(Canvas canvas, IEnumerable<Segment> segments, Windows.UI.Color color)
        {
            canvas.Children.Clear();
            var width = Math.Max(1, TranscriptTrack.ActualWidth);
            foreach (var segment in segments)
            {
                var button = new Button { Width = Math.Max(3, (segment.End - segment.Start) / project.Duration * width), Height = 22, MinHeight = 0, MinWidth = 0,
                    Padding = new Thickness(0), Background = new SolidColorBrush(color), BorderThickness = new Thickness(0) };
                if (button.Width > 24) button.Content = new TextBlock { Text = segment.Text, TextTrimming = TextTrimming.CharacterEllipsis, FontSize = 10, Foreground = new SolidColorBrush(Microsoft.UI.Colors.White) };
                Canvas.SetLeft(button, segment.Start / project.Duration * width);
                ToolTipService.SetToolTip(button, $"{segment.Timing}: {segment.Text}");
                Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(button, $"{segment.Timing}: {segment.Text}");
                button.Click += (_, _) => Seek(segment.Start); canvas.Children.Add(button);
            }
        }
        Draw(TranscriptTrack, project.Transcript, Microsoft.UI.Colors.SteelBlue);
        Draw(DescriptionTrack, project.Cues, Microsoft.UI.Colors.DarkGoldenrod);
    }
    private void RenderMarkdown(string markdown)
    {
        ClearSummaryHighlight(); SummaryRichText.Children.Clear(); summaryLines.Clear();
        foreach (var line in markdown.Split('\n'))
        {
            var heading = Regex.Match(line, @"^(#{1,6})\s+(.*)");
            var paragraph = new TextBlock { FontSize = 16, TextWrapping = TextWrapping.Wrap, IsTextSelectionEnabled = true };
            var value = line;
            if (heading.Success) { paragraph.FontSize = 28 - heading.Groups[1].Length * 2; paragraph.FontWeight = Microsoft.UI.Text.FontWeights.SemiBold; value = heading.Groups[2].Value; }
            else if (Regex.IsMatch(line, @"^\s*[-*]\s")) value = "• " + Regex.Replace(line, @"^\s*[-*]\s+", "");
            var offset = 0;
            foreach (Match match in Regex.Matches(value, @"\*\*(.+?)\*\*|`([^`]+)`"))
            {
                paragraph.Inlines.Add(new Run { Text = value[offset..match.Index] });
                if (match.Groups[1].Success) { var bold = new Bold(); bold.Inlines.Add(new Run { Text = match.Groups[1].Value }); paragraph.Inlines.Add(bold); }
                else paragraph.Inlines.Add(new Run { Text = match.Groups[2].Value, FontFamily = new FontFamily("Cascadia Mono") });
                offset = match.Index + match.Length;
            }
            paragraph.Inlines.Add(new Run { Text = value[offset..] });
            SummaryRichText.Children.Add(paragraph);
            paragraph.SelectionChanged += (_, _) => UpdateDesktopMenuStates();
            summaryLines.Add((paragraph, Regex.Replace(value, @"\*\*(.+?)\*\*|`([^`]+)`", m => m.Groups[1].Success ? m.Groups[1].Value : m.Groups[2].Value)));
        }
    }
    private void UpdateCaptions(double? timestamp = null)
    {
        var project = Model.Project;
        var mode = Model.Settings.Get("captionDisplayMode", "off");
        var position = timestamp ?? Position;
        string Active(IEnumerable<Segment> rows) => string.Join("\n", rows.Where(c => c.Start <= position && position < c.End).Select(c => c.Text));
        CaptionText.Text = project != null && mode is "transcript" or "combined" ? Active(project.Transcript) : "";
        DescriptionCaptionText.Text = project != null && mode is "descriptions" or "combined" ? Active(project.Cues) : "";
        DialogueCaptionBox.Visibility = CaptionText.Text.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
        DescriptionCaptionBox.Visibility = DescriptionCaptionText.Text.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
        CaptionOverlay.Visibility = CaptionText.Text.Length + DescriptionCaptionText.Text.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
        CaptionText.FontSize = DescriptionCaptionText.FontSize = Math.Clamp(Model.Settings.Number("captionFontSize", 24), 12, 72);
        CaptionOverlay.VerticalAlignment = Model.Settings.Get("captionPosition") == "top" ? VerticalAlignment.Top : VerticalAlignment.Bottom;
        Windows.UI.Color Color(string key, string fallback)
        {
            var value = Model.Settings.Get(key, fallback).TrimStart('#');
            if (value.Length != 6 || !uint.TryParse(value, System.Globalization.NumberStyles.HexNumber, null, out var rgb)) rgb = Convert.ToUInt32(fallback.TrimStart('#'), 16);
            return Windows.UI.Color.FromArgb(255, (byte)(rgb >> 16), (byte)(rgb >> 8), (byte)rgb);
        }
        void Style(TextBlock text, Border box, string foregroundKey, string backgroundKey, string foreground, string background)
        {
            text.Foreground = new SolidColorBrush(Color(foregroundKey, foreground));
            var fill = Color(backgroundKey, background);
            fill.A = (byte)(Math.Clamp(Model.Settings.Number("captionBackgroundOpacity", .75), 0, 1) * 255);
            box.Background = new SolidColorBrush(fill);
        }
        Style(CaptionText, DialogueCaptionBox, "captionTextColor", "captionBackgroundColor", "#FFFFFF", "#000000");
        Style(DescriptionCaptionText, DescriptionCaptionBox, "adCaptionTextColor", "adCaptionBackgroundColor", "#FFD60A", "#3A2F00");
    }
}
