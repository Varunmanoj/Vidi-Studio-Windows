#if DEBUG
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Vidi_Studio_Windows.Models;

namespace Vidi_Studio_Windows;
public sealed partial class MainWindow
{
    private async Task VerifyProjectSearchForSmokeTestAsync()
    {
        static void Check(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
        static void Invoke(Button button)
        {
            var peer = new Microsoft.UI.Xaml.Automation.Peers.ButtonAutomationPeer(button);
            ((Microsoft.UI.Xaml.Automation.Provider.IInvokeProvider)peer.GetPattern(Microsoft.UI.Xaml.Automation.Peers.PatternInterface.Invoke)).Invoke();
        }
        var project = Model.Project!;
        var path = Path.Combine(project.Folder, "project.json");
        var before = File.ReadAllText(path);
        Section.SelectedIndex = 0;
        await RunDesktopCommandAsync("focus-search");
        await Task.Delay(100);
        Check(ReferenceEquals(FocusManager.GetFocusedElement(Root.XamlRoot), EditorSearch), "Editor menu did not focus search.");
        Check(DesktopShortcuts["focus-search"].Key == Windows.System.VirtualKey.F, "Search shortcut is missing.");
        EditorSearch.Text = "Beacon"; await Task.Delay(100);
        Check(Section.SelectedIndex == 0 && IsProjectSearching && ProjectSearchResults.Visibility == Visibility.Visible, $"Ordinary search: section={Section.SelectedIndex}, searching={IsProjectSearching}, view={ProjectSearchResults.Visibility}, text={EditorSearch.Text}, ready={ready}, project={searchProjectID}.");
        Check(searchGroups.SelectMany(g => g.Hits).Count() == 2 && searchGroups.Any(g => g.Result.State == "draft"), "Older and draft transcript matches are missing.");
        Check(Segments.Visibility == Visibility.Collapsed && EditToolbar.Visibility == Visibility.Collapsed && !CanRunDesktopCommand("segment-edit-mode"), "Search must be read-only.");
        foreach (var (prefix, index) in new[] { ("SuM", 1), ("des", 2), ("AdT", 4), ("Tr", 0) })
        {
            EditorSearch.Text = "";
            Section.SelectedIndex = index == 0 ? 1 : 0;
            for (var length = 1; length <= prefix.Length; length++)
            {
                EditorSearch.Text = prefix[..length]; await Task.Delay(100);
                if (length == prefix.Length)
                    Check(Section.SelectedIndex == index && !IsProjectSearching && ProjectSearchResults.Visibility == Visibility.Collapsed,
                        "Prefix letters must navigate before a colon or search term: " + prefix);
            }
            EditorSearch.Text += ":"; await Task.Delay(100);
            Check(Section.SelectedIndex == index && !IsProjectSearching, "Prefix colon must not require a search term.");
        }
        foreach (var (prefix, index, count) in new[] { ("SUM : Beacon", 1, 2), ("des:beacon", 2, 2), ("AdT: Beacon", 4, 4), ("Tr: Beacon", 0, 2) })
        {
            EditorSearch.Text = prefix; await Task.Delay(100);
            Check(Section.SelectedIndex == index && searchGroups.Sum(g => g.Hits.Count) == count, "Prefix navigation/scope mismatch: " + prefix);
            foreach (var text in searchHighlightBlocks)
            {
                Check(text.TextHighlighters.Count == 1, "Search match highlighting missing in " + SearchSection);
                var ranges = text.TextHighlighters[0].Ranges;
                Check(ranges.Count == ProjectSearchQuery.Parse(prefix).MatchingRanges(text.Text).Count && ranges.Count > 0, "Not all occurrences were highlighted.");
                foreach (var range in ranges) Check(text.Text.Substring(range.StartIndex, range.Length).Equals("Beacon", StringComparison.OrdinalIgnoreCase), "Prefix or nonmatching text was highlighted.");
            }
            if (index == 1) Check(searchGroups.SelectMany(g => g.Hits).All(h => !h.Text.Contains("hidden", StringComparison.OrdinalIgnoreCase)), "Summary exposed nonmatching sentences.");
        }
        var settings = new Views.SettingsDialog(Model, this, 0);
        var settingsDisplay = settings.ShowAsync();
        try
        {
            await Task.Delay(150);
            await settings.VerifySearchHighlightAppearanceForSmokeTestAsync((enabled, color) =>
            {
                Check(EditorSearch.Text == "Tr: Beacon" && searchGroups.Sum(g => g.Hits.Count) == 2, "Appearance edits changed the query or results.");
                foreach (var text in searchHighlightBlocks)
                {
                    Check(text.TextHighlighters.Count == (enabled ? 1 : 0), "Appearance switch did not update open results.");
                    if (enabled) Check(((Microsoft.UI.Xaml.Media.SolidColorBrush)text.TextHighlighters[0].Background).Color == SummaryColor(color, DefaultSearchHighlightColor), "Appearance color did not update open results.");
                    if (enabled && color == "#102040") Check(((Microsoft.UI.Xaml.Media.SolidColorBrush)text.TextHighlighters[0].Foreground).Color == Microsoft.UI.Colors.White, "Dark highlight has unreadable text.");
                }
            });
        }
        finally { settings.Close(); await settingsDisplay; }
        Activate(); await Task.Delay(100);
        var announcements = searchAnnouncementCount;
        EditorSearch.Text = "abs"; EditorSearch.Text = "absent search phrase";
        await Task.Delay(550);
        Check(searchAnnouncementCount == announcements + 1 && lastSpokenSearchStatus == "No search results in Transcript.", "Search count announcements must coalesce and include zero results.");
        EditorSearch.Text = "Tr: Beacon"; await Task.Delay(550);
        Check(lastSpokenSearchStatus == "2 search results in Transcript.", "Editor count announcement is incorrect.");
        Check(File.ReadAllText(path) == before && Model.Project!.ActiveResultID("transcript") == "selected", "Typing search changed saved selections or project data.");
        EditorSearch.Text = "Tr:"; await Task.Delay(100);
        Check(!IsProjectSearching && Section.SelectedIndex == 0, "Prefix alone must navigate without filtering.");
        EditorSearch.Text = "sum: Beacon"; await Task.Delay(100);
        Section.SelectedIndex = 0; await Task.Delay(100);
        Check(!IsProjectSearching && Segments.Visibility == Visibility.Visible, "Other tabs must display normally outside a prefixed scope.");
        EditorSearch.Text = "Beacon"; await Task.Delay(100);
        EditorSearch.Text = ""; await Task.Delay(100);
        Check(ProjectSearchResults.Visibility == Visibility.Collapsed && Segments.Visibility == Visibility.Visible, "Deleting the query did not restore segments immediately.");
        EditorSearch.Text = "Beacon"; await Task.Delay(100);
        Invoke(EditorClearSearch); await Task.Delay(100);
        Check(EditorSearch.Text == "" && ReferenceEquals(FocusManager.GetFocusedElement(Root.XamlRoot), EditorSearch) && ProjectSearchResults.Visibility == Visibility.Collapsed && Segments.Visibility == Visibility.Visible, "Editor clear icon did not restore segments immediately.");
        EditorSearch.Text = "Tr:Beacon"; await Task.Delay(100);
        var old = searchGroups.Single(g => g.Result.Id == "old").Hits.Single();
        await OpenSearchHitAsync(old);
        Check(EditorSearch.Text == "" && Model.Project!.ActiveResultID("transcript") == "old" && Segments.Visibility == Visibility.Visible, "Opening an older match failed.");
        EditorSearch.Text = "Tr:Beacon"; await Task.Delay(100);
        var draft = searchGroups.Single(g => g.Result.Id == "draft").Hits.Single();
        ContentDialog? opened = null;
        void Capture(ContentDialog dialog, Window window) { opened = dialog; }
        Views.WindowedDialog.Preparing += Capture;
        try
        {
            var review = OpenSearchHitAsync(draft);
            for (var attempt = 0; attempt < 100 && opened == null; attempt++) await Task.Delay(20);
            Check(opened != null, "Draft review did not open.");
            await dialogPresentationVerifier!.WaitUntilReadyAsync(opened!);
            opened!.Hide(); await review;
            Check(EditorSearch.Text.Length > 0 && Model.Project!.SavedResults.Single(r => r.Id == "draft").State == "draft", "Review Later accepted or lost the draft/query.");
            opened = null;
            review = OpenSearchHitAsync(draft);
            for (var attempt = 0; attempt < 100 && opened == null; attempt++) await Task.Delay(20);
            Check(opened != null, "Draft review could not reopen.");
            await dialogPresentationVerifier.WaitUntilReadyAsync(opened!);
            Invoke(FindButton(opened!, "Apply Choice") ?? throw new InvalidOperationException("Draft choice button missing."));
            await review;
            Check(EditorSearch.Text == "" && Model.Project!.ActiveResultID("transcript") == "draft" && Model.Project.SavedResults.Single(r => r.Id == "draft").State == "saved", "Keep Both did not use the existing review flow.");
        }
        finally { Views.WindowedDialog.Preparing -= Capture; }
        ShowGallery_Click(this, new RoutedEventArgs()); await Task.Delay(150);
        await RunDesktopCommandAsync("focus-search"); await Task.Delay(100);
        Check(ReferenceEquals(FocusManager.GetFocusedElement(Root.XamlRoot), GallerySearch), "All Projects menu did not focus search.");
        GallerySearch.Text = "absent search text"; await Task.Delay(550);
        Check(lastSpokenSearchStatus == "No projects found.", "All Projects did not announce its search count.");
        Check(CanRunDesktopCommand("clear-search") && GalleryClearSearch.Visibility == Visibility.Visible, "Gallery clear state missing.");
        Invoke(GalleryClearSearch); await Task.Delay(550);
        Check(lastSpokenSearchStatus == SearchResultStatus.Gallery("", Model.Projects.Count), "Clearing gallery search did not announce the restored count.");
        Check(GallerySearch.Text == "" && ReferenceEquals(FocusManager.GetFocusedElement(Root.XamlRoot), GallerySearch), "Gallery clear icon failed.");
        ShowEditor(); await Task.Delay(150);
        AppWindow.Resize(new Windows.Graphics.SizeInt32(900, 740)); await Task.Delay(150);
        Check(EditorSearch.ActualWidth > 100 && EditorSearch.ActualWidth <= 521, "Search field does not fit the narrow editor.");
        EditorSearch.Text = "absent"; await Task.Delay(100); await RunDesktopCommandAsync("clear-search");
        Check(EditorSearch.Text == "" && ProjectSearchResults.Visibility == Visibility.Collapsed, "Clear Search menu did not restore normal content.");
    }
}
#endif
