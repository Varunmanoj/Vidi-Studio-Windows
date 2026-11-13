using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Controls;
using System;
using System.Linq;
using System.Threading.Tasks;

namespace Vidi_Studio_Windows;

public sealed partial class MainWindow
{
#if DEBUG
    private async Task VerifyInspectorDisclosureForSmokeTestAsync()
    {
        ShowEditor(); await Task.Delay(200); Root.UpdateLayout();
        if (InspectorScrollViewer.Content is not StackPanel sections) throw new InvalidOperationException("Inspector sections missing.");
        var groups = sections.Children.OfType<Expander>().ToArray();
        foreach (var group in groups.Where(group => group.Visibility == Visibility.Visible))
        {
            var peer = FrameworkElementAutomationPeer.CreatePeerForElement(group) ?? throw new InvalidOperationException("Expander peer missing.");
            var name = peer.GetName();
            if (string.IsNullOrWhiteSpace(name) || name == "Inspector section") throw new InvalidOperationException("Inspector disclosure has no section name.");
            if (peer.GetPattern(PatternInterface.ExpandCollapse) is not Microsoft.UI.Xaml.Automation.Provider.IExpandCollapseProvider provider)
                throw new InvalidOperationException("Inspector disclosure has no native expand/collapse pattern: " + name);
            var expanded = group.IsExpanded;
            try
            {
                provider.Collapse(); await Task.Delay(75);
                if (group.IsExpanded || provider.ExpandCollapseState != ExpandCollapseState.Collapsed || peer.GetName() != name)
                    throw new InvalidOperationException("Collapsed disclosure state/name mismatch: " + name);
                provider.Expand(); await Task.Delay(75);
                if (!group.IsExpanded || provider.ExpandCollapseState != ExpandCollapseState.Expanded || peer.GetName() != name)
                    throw new InvalidOperationException("Expanded disclosure state/name mismatch: " + name);
            }
            finally { group.IsExpanded = expanded; }
        }
        await VerifyPanelVisibilityTogglesForSmokeTestAsync();
        await VerifyEditorSectionMenusAndTitlesForSmokeTestAsync();
        VerifyLanguageInspectorForSmokeTest();
        var project = Model.Project ?? throw new InvalidOperationException("Inspector fixture missing.");
        foreach (var editing in new[] { false, true })
        {
            var settingsPanel = BuildLanguageResultsPanel(project, settingsContext: true, editingSettings: editing);
            if (settingsPanel.Children.OfType<Button>().Any(button => button.Content as string == "Keep All Sections in Sync") != editing)
                throw new InvalidOperationException("Project Settings sync action must appear only while editing.");
        }
        ShowGallery_Click(this, new RoutedEventArgs());
    }

    private async Task VerifyPanelVisibilityTogglesForSmokeTestAsync()
    {
        foreach (var projects in new[] { true, false })
        {
            var pane = projects ? ProjectSplitView : InspectorSplitView;
            var button = projects ? EditorProjectListButton : TopInspectorToggle;
            var command = projects ? "show-projects" : "show-inspector";
            var original = pane.IsPaneOpen;
            var peer = FrameworkElementAutomationPeer.CreatePeerForElement(button) ?? throw new InvalidOperationException("Panel toggle peer missing.");
            if (peer.GetPattern(PatternInterface.Toggle) is not Microsoft.UI.Xaml.Automation.Provider.IToggleProvider provider)
                throw new InvalidOperationException("Panel visibility control has no native Toggle pattern: " + command);
            void Verify(bool shown)
            {
                var title = projects ? "Project list" : "Inspector";
                if (pane.IsPaneOpen != shown || button.IsChecked != shown || provider.ToggleState != (shown ? ToggleState.On : ToggleState.Off) ||
                    !peer.GetName().Equals((shown ? "Hide " : "Show ") + title, StringComparison.OrdinalIgnoreCase) ||
                    peer.GetItemStatus() != title + (shown ? " shown" : " hidden"))
                    throw new InvalidOperationException("Panel toggle name/state is stale: " + command);
            }
            try
            {
                pane.IsPaneOpen = false; await Task.Delay(200); Verify(false);
                button.Focus(FocusState.Programmatic);
                provider.Toggle(); await Task.Delay(250); Verify(true);
                provider.Toggle(); await Task.Delay(250); Verify(false);
                await RunDesktopCommandAsync(command); await Task.Delay(250); Verify(true);
                await RunDesktopCommandAsync(command); await Task.Delay(250); Verify(false);
                pane.IsPaneOpen = true; await Task.Delay(200); Verify(true);
            }
            finally { pane.IsPaneOpen = original; }
        }
    }

    private async Task VerifyEditorSectionMenusAndTitlesForSmokeTestAsync()
    {
        var ids = new[] { "transcript", "summary", "descriptions", "ad-overview", "ad-transcript" };
        var titles = new[] { "Transcript", "Summary", "Descriptions", "AD Overview", "AD Transcript" };
        foreach (var menuName in new[] { "View", "Project" })
        {
            var menu = editorMenus.Single(menu => menu.Title == menuName).Items.OfType<MenuFlyoutSubItem>().Single(group => group.Text == "Editor Sections");
            if (!menu.Items.OfType<MenuFlyoutItem>().Select(item => item.Tag as string).SequenceEqual(ids))
                throw new InvalidOperationException("Editor Sections menu copy drifted: " + menuName);
        }
        var previous = Section.SelectedIndex;
        var project = Model.Project ?? throw new InvalidOperationException("Window title fixture missing.");
        try
        {
            for (var index = 0; index < ids.Length; index++)
            {
                await RunDesktopCommandAsync(ids[index]); await Task.Delay(100); Root.UpdateLayout();
                var expected = project.Name + " — " + titles[index];
                if (Section.SelectedIndex != index || ProjectTitle.Text != expected || Title != expected + " — Vidi Studio")
                    throw new InvalidOperationException("Editor window title did not follow the selected section: " + ids[index]);
                if (index <= 2 && InspectorScrollViewer.Content is StackPanel sections)
                {
                    var group = sections.Children.OfType<Expander>().Single(group => ReferenceEquals(group.Header, InspectorSectionTitle));
                    if (FrameworkElementAutomationPeer.CreatePeerForElement(group)?.GetName() != InspectorSectionTitle.Text)
                        throw new InvalidOperationException("Dynamic inspector disclosure name is stale.");
                }
            }
        }
        finally { Section.SelectedIndex = previous; RenderSection(); RefreshLanguagePanel(); }
    }

    private async Task VerifyAccessibleMediaForSmokeTestAsync()
    {
        var project = Model.Project ?? throw new InvalidOperationException("Accessibility fixture missing.");
        ShowGallery_Click(this, new RoutedEventArgs());
        foreach (var grid in new[] { true, false })
        {
            if ((GalleryGrid.Visibility == Visibility.Visible) != grid) GalleryLayout_Click(this, new RoutedEventArgs());
            ListViewBase list = grid ? GalleryGrid : GalleryList;
            var listed = Model.Projects.First(item => string.Equals(item.Folder, project.Folder, StringComparison.OrdinalIgnoreCase));
            list.ScrollIntoView(listed); await Task.Delay(150); Root.UpdateLayout();
            if (list.ContainerFromItem(listed) is not FrameworkElement container ||
                FrameworkElementAutomationPeer.CreatePeerForElement(container)?.GetName() != listed.ProjectAnnouncement ||
                !project.ProjectAnnouncement.Contains("Duration: " + Models.NarrationTimeFormat.SpokenProjectDuration(project.Duration)))
                throw new InvalidOperationException("Gallery item has no spoken media duration.");
        }
        ShowEditor(); await Task.Delay(250);
        if (AutomationProperties.GetHeadingLevel(ProjectTitle) != AutomationHeadingLevel.Level1 ||
            AutomationProperties.GetHeadingLevel(EditorSectionHeading) != AutomationHeadingLevel.Level2 ||
            AutomationProperties.GetHeadingLevel(InspectorSectionTitle) != AutomationHeadingLevel.Level2)
            throw new InvalidOperationException("Editor/inspector heading levels are missing.");
        var duration = Models.NarrationTimeFormat.SpokenProjectDuration(project.Duration);
        if (!AutomationProperties.GetName(TimeLabel).Contains(duration) || !AutomationProperties.GetItemStatus(Playhead).Contains(duration))
            throw new InvalidOperationException("Timeline has no spoken media time/duration.");
        bool Visible(FrameworkElement element)
        {
            for (DependencyObject? node = element; node != null; node = Microsoft.UI.Xaml.Media.VisualTreeHelper.GetParent(node))
                if (node is UIElement { Visibility: Visibility.Collapsed }) return false;
            return element.XamlRoot != null;
        }
        string PeerName(FrameworkElement element) => FrameworkElementAutomationPeer.CreatePeerForElement(element)?.GetName() ?? "";
        if (PeerName(InspectorProjectInformationHeading) != "Project Information" ||
            AutomationProperties.GetHeadingLevel(InspectorProjectInformationHeading) != AutomationHeadingLevel.Level2 ||
            PeerName(InspectorDeleteContentButton) != "Choose content to delete" ||
            PeerName(Follow) != "Follow active segment")
            throw new InvalidOperationException($"Inspector names: heading=[{PeerName(InspectorProjectInformationHeading)}] level={AutomationProperties.GetHeadingLevel(InspectorProjectInformationHeading)}, delete=[{PeerName(InspectorDeleteContentButton)}], narration=[{PeerName(Narration)}], follow=[{PeerName(Follow)}].");
        var previousSection = Section.SelectedIndex;
        try
        {
            foreach (var section in new[] { 0, 2, 4 })
            {
                Section.SelectedIndex = section; RenderSection(); await Task.Delay(200); Root.UpdateLayout();
                if (Narration.Visibility == Visibility.Visible && PeerName(Narration) != "Narration")
                    throw new InvalidOperationException("Visible narration toggle has no accessible name.");
                var rows = ((System.Collections.Generic.IEnumerable<Models.Segment>)Segments.ItemsSource).ToArray();
                if (rows.Length == 0) throw new InvalidOperationException("Timing accessibility fixture has no rows.");
                foreach (var row in rows.Take(2))
                {
                    Segments.ScrollIntoView(row); await Task.Delay(100); Root.UpdateLayout();
                    if (Segments.ContainerFromItem(row) is not FrameworkElement container || PeerName(container) != row.SegmentAnnouncement ||
                        !PeerName(container).Contains("Start time: ") || !PeerName(container).Contains("End time: ") ||
                        !PeerName(container).Contains(row.DurationAnnouncement) ||
                        PeerName(container).IndexOf(row.DurationAnnouncement, StringComparison.Ordinal) >= PeerName(container).LastIndexOf(row.Text, StringComparison.Ordinal) ||
                        !row.PlayLabel.Contains(row.TimingAnnouncement) || !row.PreviewLabel.Contains(row.TimingAnnouncement))
                        throw new InvalidOperationException("Segment row does not announce both named time boundaries.");
                }
                foreach (var button in new Button[] { InspectorGenerate, InspectorRewriteButton, InspectorSynthesizeNarrationButton,
                    InspectorSynthesizeChangedButton, InspectorDeleteNarrationButton, InspectorDeleteContentButton, InspectorExportButton })
                    if (Visible(button) && string.IsNullOrWhiteSpace(PeerName(button))) throw new InvalidOperationException("Inspector action has no accessible name: " + button.Name);
                if (Visible(InspectorSynthesizeNarrationButton) && PeerName(InspectorSynthesizeNarrationButton) != InspectorSynthesizeNarrationLabel.Text)
                    throw new InvalidOperationException("Narration action's accessible name is stale.");
            }
        }
        finally { Section.SelectedIndex = previousSection; RenderSection(); }
        var iconButton = new Button();
        Views.IconButtonContent.Set(iconButton, "Preview sound", Symbol.Play);
        Root.Children.Add(iconButton);
        try { Root.UpdateLayout(); if (PeerName(iconButton) != "Preview sound") throw new InvalidOperationException("Composite icon button has no accessible name."); }
        finally { Root.Children.Remove(iconButton); }
        ShowGallery_Click(this, new RoutedEventArgs());
    }
#endif
}
