using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Documents;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Vidi_Studio_Windows.Models;
using Windows.System;

namespace Vidi_Studio_Windows;

public sealed partial class MainWindow
{
    private readonly ProjectTextSearch projectTextSearch = new();
    private bool searchOpening;
    private string? searchRevealedKey;
    private bool updatingProjectSearch;
    private string searchProjectID = "";
    private IReadOnlyList<ProjectSearchGroup> searchGroups = Array.Empty<ProjectSearchGroup>();
    private readonly List<TextBlock> searchHighlightBlocks = new();
    internal const string DefaultSearchHighlightColor = "#FFD60A";
    private readonly DispatcherTimer searchAnnouncementTimer = new() { Interval = TimeSpan.FromMilliseconds(350) };
    private bool searchAnnouncementInitialized;
    private bool searchAnnouncementWindowActive;
    private bool pendingSearchIsEditor;
    private string? previousSearchQuery;
    private string previousSearchStatus = "";
    private string pendingSearchStatus = "";
#if DEBUG
    private string lastSpokenSearchStatus = "";
    private int searchAnnouncementCount;
#endif

    private void QueueSearchResultAnnouncement()
    {
        if (!ready || windowClosed) return;
        var query = desktopEditorVisible ? EditorSearch.Text : GallerySearch.Text;
        var status = desktopEditorVisible
            ? SearchResultStatus.Editor(ProjectSearchQuery.Parse(query), SearchSection, searchGroups.Sum(group => group.Hits.Count))
            : SearchResultStatus.Gallery(query, Model.Projects.Count);
        if (query == previousSearchQuery && status == previousSearchStatus) return;
        var previous = previousSearchQuery;
        previousSearchQuery = query; previousSearchStatus = status;
        searchAnnouncementTimer.Stop();
        if (query.Length == 0 && string.IsNullOrEmpty(previous)) return;
        pendingSearchStatus = status; pendingSearchIsEditor = desktopEditorVisible;
        if (!searchAnnouncementInitialized)
        {
            searchAnnouncementInitialized = true;
            Closed += (_, _) => searchAnnouncementTimer.Stop();
            searchAnnouncementTimer.Tick += (_, _) =>
            {
                searchAnnouncementTimer.Stop();
                if (windowClosed || !searchAnnouncementWindowActive || pendingSearchIsEditor != desktopEditorVisible) return;
                FrameworkElement source = desktopEditorVisible ? EditorSearch : GallerySearch;
                var peer = Microsoft.UI.Xaml.Automation.Peers.FrameworkElementAutomationPeer.FromElement(source)
                    ?? Microsoft.UI.Xaml.Automation.Peers.FrameworkElementAutomationPeer.CreatePeerForElement(source);
                if (peer == null) return;
                peer.RaiseNotificationEvent(Microsoft.UI.Xaml.Automation.Peers.AutomationNotificationKind.Other,
                    Microsoft.UI.Xaml.Automation.Peers.AutomationNotificationProcessing.MostRecent, pendingSearchStatus, "search-results");
#if DEBUG
                lastSpokenSearchStatus = pendingSearchStatus;
                searchAnnouncementCount++;
#endif
            };
        }
        searchAnnouncementTimer.Start();
    }
    private static readonly string[] SearchSections = { "transcript", "summary", "descriptions", "ad-overview", "ad-transcript" };
    private string SearchSection => SearchSections[Math.Clamp(Section?.SelectedIndex ?? 0, 0, 4)];
    private bool IsImportedCombinedTranscript => Section?.SelectedIndex == 4 && Model.Project?.ImportedADTranscript is { Mode: "combined", Segments.Count: > 0 };
    private bool IsProjectSearching => desktopEditorVisible && ProjectSearchQuery.Parse(EditorSearch?.Text ?? "").Applies(SearchSection);
    private ProjectSearchHit? SelectedSearchHit => (SearchMatches?.SelectedItem as ListViewItem)?.Tag as ProjectSearchHit;

    private void FocusSearch() => (desktopEditorVisible ? EditorSearch : GallerySearch).Focus(FocusState.Programmatic);
    private void ClearSearch()
    {
        if (desktopEditorVisible) EditorSearch.Text = ""; else GallerySearch.Text = "";
        FocusSearch();
    }
    private async void SearchCommand_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: string command }) await RunDesktopCommandAsync(command);
    }
    private void UpdateSearchControls()
    {
        if (GalleryClearSearch != null) GalleryClearSearch.Visibility = string.IsNullOrEmpty(GallerySearch.Text) ? Visibility.Collapsed : Visibility.Visible;
        if (EditorClearSearch != null) EditorClearSearch.Visibility = string.IsNullOrEmpty(EditorSearch.Text) ? Visibility.Collapsed : Visibility.Visible;
    }
    private void RefreshSearchIndex(StudioProject project)
    {
        if (searchProjectID != project.Id)
        {
            updatingProjectSearch = true;
            EditorSearch.Text = "";
            updatingProjectSearch = false;
            searchProjectID = project.Id;
        }
        projectTextSearch.Update(project);
        UpdateSearchControls();
    }
    private void UpdateEditorSearch()
    {
        if (!ready || updatingProjectSearch || Model.Project == null) return;
        if (readback.State != Services.SummaryReadbackState.Stopped) readback.Stop();
        if (!string.IsNullOrEmpty(EditorSearch.Text)) searchRevealedKey = null;
        if (Segments.SelectionMode != ListViewSelectionMode.None) Segments.SelectedItems.Clear();
        var query = ProjectSearchQuery.Parse(EditorSearch.Text);
        var destination = query.Section == null ? -1 : Array.IndexOf(SearchSections, query.Section);
        if (destination >= 0 && destination != Section.SelectedIndex && !HasUnsavedLanguageSummary) Section.SelectedIndex = destination;
        RenderSection();
        UpdateSearchControls();
        UpdateDesktopMenuStates();
        QueueSearchResultAnnouncement();
    }
    private void HideNormalSearchContent()
    {
        Segments.Visibility = SummaryView.Visibility = Overview.Visibility = ExportList.Visibility = EmptySectionActions.Visibility = Visibility.Collapsed;
        SectionProcessingOverlay.Visibility = EditToolbar.Visibility = Visibility.Collapsed;
        EditorSectionActions.Visibility = Visibility.Collapsed;
    }
    private void RenderProjectSearch()
    {
        HideNormalSearchContent();
        searchHighlightBlocks.Clear();
        var selected = SelectedSearchHit?.Id;
        SearchMatches.Items.Clear();
        searchGroups = projectTextSearch.Search(ProjectSearchQuery.Parse(EditorSearch.Text), SearchSection);
        var count = searchGroups.Sum(g => g.Hits.Count);
        ProjectSearchCount.Text = count > 0 ? $"{count} matches" : "No matches";
        foreach (var group in searchGroups)
        {
            var heading = new TextBlock { Text = group.Title, TextWrapping = TextWrapping.Wrap, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold, Margin = new Thickness(0, 12, 0, 4) };
            Microsoft.UI.Xaml.Automation.AutomationProperties.SetHeadingLevel(heading, Microsoft.UI.Xaml.Automation.Peers.AutomationHeadingLevel.Level3);
            SearchMatches.Items.Add(new ListViewItem { Content = heading, IsHitTestVisible = false, IsTabStop = false });
            foreach (var hit in group.Hits)
            {
                var panel = new StackPanel { Spacing = 6, Padding = new Thickness(4, 8, 4, 8), HorizontalAlignment = HorizontalAlignment.Stretch };
                if (hit.Start is { } start && hit.End is { } end)
                    panel.Children.Add(new TextBlock { Text = (hit.ImportedCombined ? "AD Transcript" : hit.IsDescription ? "AD" : "Transcript") + $" · {NarrationTimeFormat.Timestamp(start)}–{NarrationTimeFormat.Timestamp(end)}", Opacity = .7, FontSize = 12 });
                var text = new TextBlock { Text = hit.Text, TextWrapping = TextWrapping.Wrap, IsTextSelectionEnabled = true };
                searchHighlightBlocks.Add(text);
                panel.Children.Add(text);
                var title = group.Result.State == "draft" ? "Review Draft…" : "Open Result";
                var button = new Button { Content = title, IsEnabled = Model.Idle && !HasUnsavedLanguageSummary && !searchOpening, Tag = hit };
                button.Click += async (_, _) => await OpenSearchHitAsync(hit);
                panel.Children.Add(button);
                var item = new ListViewItem { Content = panel, Tag = hit, HorizontalContentAlignment = HorizontalAlignment.Stretch };
                var flyout = new MenuFlyout();
                var open = new MenuFlyoutItem { Text = title, IsEnabled = button.IsEnabled };
                open.Click += async (_, _) => await OpenSearchHitAsync(hit);
                flyout.Items.Add(open); item.ContextFlyout = flyout;
                SearchMatches.Items.Add(item);
                if (hit.Id == selected) SearchMatches.SelectedItem = item;
            }
        }
        ProjectSearchResults.Visibility = Visibility.Visible;
        RefreshSearchHighlights();
        QueueSearchResultAnnouncement();
    }

    private void RefreshSearchHighlights()
    {
        var query = ProjectSearchQuery.Parse(EditorSearch.Text);
        var enabled = IsProjectSearching && Model.Settings.GetBool("searchHighlightEnabled", true);
        var color = Model.Settings.Get("searchHighlightColor", DefaultSearchHighlightColor);
        foreach (var text in searchHighlightBlocks) ApplySearchHighlight(text, query, enabled, color);
    }

    internal static void ApplySearchHighlight(TextBlock text, ProjectSearchQuery query, bool enabled, string hex)
    {
        text.TextHighlighters.Clear();
        if (!enabled) return;
        var color = SummaryColor(hex, DefaultSearchHighlightColor);
        static double Linear(byte value) { var channel = value / 255d; return channel <= .04045 ? channel / 12.92 : Math.Pow((channel + .055) / 1.055, 2.4); }
        var luminance = .2126 * Linear(color.R) + .7152 * Linear(color.G) + .0722 * Linear(color.B);
        var highlight = new TextHighlighter { Background = new SolidColorBrush(color), Foreground = new SolidColorBrush(luminance > .179 ? Microsoft.UI.Colors.Black : Microsoft.UI.Colors.White) };
        foreach (var range in query.MatchingRanges(text.Text)) highlight.Ranges.Add(new TextRange { StartIndex = range.Start, Length = range.Length });
        if (highlight.Ranges.Count > 0) text.TextHighlighters.Add(highlight);
    }
    private void RenderImportedCombinedTranscript()
    {
        HideNormalSearchContent();
        SearchMatches.Items.Clear();
        searchGroups = Array.Empty<ProjectSearchGroup>();
        var rows = Model.Project!.ImportedADTranscript!.Segments;
        ProjectSearchCount.Text = "Imported combined AD Transcript";
        foreach (var row in rows) SearchMatches.Items.Add(new ListViewItem {
            Tag = row.Id, Content = new TextBlock { Text = $"{NarrationTimeFormat.Timestamp(row.Start)}–{NarrationTimeFormat.Timestamp(row.End)}\n{row.Text}", TextWrapping = TextWrapping.Wrap, IsTextSelectionEnabled = true },
            HorizontalContentAlignment = HorizontalAlignment.Stretch });
        ProjectSearchResults.Visibility = Visibility.Visible;
    }
    private void SearchMatches_SelectionChanged(object sender, SelectionChangedEventArgs e) { if (ready) UpdateDesktopMenuStates(); }
    private async void SearchMatches_DoubleTapped(object sender, DoubleTappedRoutedEventArgs e)
    {
        if (SelectedSearchHit is { } hit) { await OpenSearchHitAsync(hit); e.Handled = true; }
    }
    private async void SearchMatches_KeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key == VirtualKey.Enter && SelectedSearchHit is { } hit) { await OpenSearchHitAsync(hit); e.Handled = true; }
    }
    private async Task OpenSearchHitAsync(ProjectSearchHit hit)
    {
        if (searchOpening || Model.Busy || HasUnsavedLanguageSummary || !IsProjectSearching || Model.Project is not { } project || hit.Section != SearchSection) return;
        var result = project.SavedResults.FirstOrDefault(r => r.Id == hit.ResultID && r.Language == project.SelectedLanguage && r.State is "saved" or "draft");
        if (result == null || !projectTextSearch.Search(ProjectSearchQuery.Parse(EditorSearch.Text), SearchSection).SelectMany(g => g.Hits).Any(h => h.Id == hit.Id)) return;
        searchOpening = true;
        try
        {
            if (result.State == "draft") await ReviewLanguageResultAsync(project, result);
            else if (project.ActiveResultID(result.Kind) != result.Id) await RunLanguageActionAsync("select-result", new[] { "--result-id", result.Id });
            var current = Model.Project;
            if (current?.Id != project.Id || current.SelectedLanguage != result.Language || current.ActiveResultID(result.Kind) != result.Id || current.SavedResults.FirstOrDefault(r => r.Id == result.Id)?.State != "saved") return;
            searchRevealedKey = hit.SegmentID == null ? null : (hit.IsDescription ? "ad:" : "transcript:") + hit.SegmentID;
            EditorSearch.Text = "";
            RenderSection();
            if (hit.ImportedCombined && IsImportedCombinedTranscript)
            {
                var target = SearchMatches.Items.OfType<ListViewItem>().FirstOrDefault(item => item.Tag as string == hit.SegmentID);
                if (target != null) SearchMatches.ScrollIntoView(target);
            }
            else if (hit.SegmentID != null && Segments.ItemsSource is IEnumerable<Segment> rows)
            {
                var target = rows.FirstOrDefault(row => row.Id == hit.SegmentID && row.IsDescription == hit.IsDescription);
                if (target != null) { Segments.ScrollIntoView(target); Segments.Focus(FocusState.Programmatic); }
            }
            else if (hit.Kind == "summary")
            {
                var target = summaryLines.FirstOrDefault(line => line.Text.Contains(hit.Text, StringComparison.OrdinalIgnoreCase)).Block;
                target?.StartBringIntoView();
            }
        }
        catch (Exception ex) { await ShowError(ex); }
        finally { searchOpening = false; UpdateDesktopMenuStates(); }
    }
}
