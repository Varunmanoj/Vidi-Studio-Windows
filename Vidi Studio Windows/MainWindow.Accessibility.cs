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
    private async Task VerifyMenuAccessForSmokeTestAsync(string report)
    {
        var largeMenu = Enumerable.Range(0, 100).Select(index => index % 2 == 0
            ? (MenuFlyoutItemBase)new MenuFlyoutItem { Text = "Result " + index }
            : new ToggleMenuFlyoutItem { Text = "Language " + index }).ToArray();
        Views.MenuAccessKeys.Assign(largeMenu);
        if (largeMenu.Any(item => string.IsNullOrWhiteSpace(item.AccessKey)) || largeMenu.Select(item => item.AccessKey).Distinct().Count() != largeMenu.Length ||
            largeMenu.Any(item => largeMenu.Any(other => !ReferenceEquals(item, other) && other.AccessKey.StartsWith(item.AccessKey, StringComparison.Ordinal))))
            throw new InvalidOperationException("Large dynamic menus have duplicate access-key sequences.");
        foreach (var editor in new[] { false, true })
        {
            if (editor) ShowEditor(); else ShowGallery_Click(this, new RoutedEventArgs());
            await Task.Delay(150);
            var menus = MainMenu.Items.Where(item => item.Visibility == Visibility.Visible).ToArray();
            if (menus.Any(item => string.IsNullOrEmpty(item.AccessKey)) || menus.Select(item => item.AccessKey).Distinct().Count() != menus.Length)
                throw new InvalidOperationException("Menu bar access keys must be present and unique.");
            void VerifyKeys(System.Collections.Generic.IEnumerable<MenuFlyoutItemBase> entries)
            {
                var items = entries.Where(item => item is MenuFlyoutItem or MenuFlyoutSubItem).ToArray();
                if (items.Any(item => string.IsNullOrWhiteSpace(item.AccessKey)) || items.Select(item => item.AccessKey).Distinct().Count() != items.Length)
                    throw new InvalidOperationException("Submenu access keys must be present and unique within their scope.");
                foreach (var sub in items.OfType<MenuFlyoutSubItem>()) VerifyKeys(sub.Items);
            }
            foreach (var menu in menus) VerifyKeys(menu.Items);
            EnterMenuAccess(); await Task.Delay(100);
            if (!Microsoft.UI.Xaml.Input.AccessKeyManager.IsDisplayModeEnabled ||
                Microsoft.UI.Xaml.Input.FocusManager.GetFocusedElement(Root.XamlRoot) is not MenuBarItem)
                throw new InvalidOperationException("Menu access must move focus and display native key-tip boxes.");
            await CaptureSmokeImage(Root, System.IO.Path.Combine(System.IO.Path.GetDirectoryName(report)!, editor ? "menu-access-editor.png" : "menu-access-gallery.png"));
            LeaveMenuAccess();
            if (Microsoft.UI.Xaml.Input.AccessKeyManager.IsDisplayModeEnabled) throw new InvalidOperationException("Menu exit left key tips visible.");
            var project = Model.Project ?? throw new InvalidOperationException("Context menu fixture missing.");
            var contexts = editor
                ? new[] { SegmentMenu(project.Transcript.First()), (MenuFlyout)EditorWorkspace.ContextFlyout, SummaryReadingMenu() }
                : new[] { ProjectMenu(project) };
            foreach (var context in contexts)
            {
                context.ShowAt(Root); await Task.Delay(150);
                VerifyKeys(context.Items);
                if (!Microsoft.UI.Xaml.Input.AccessKeyManager.IsDisplayModeEnabled)
                    throw new InvalidOperationException("Context menu did not display native access-key tips.");
                foreach (var item in context.Items.OfType<MenuFlyoutItem>().Where(item => item.Visibility == Visibility.Visible))
                    if (string.IsNullOrWhiteSpace(FrameworkElementAutomationPeer.CreatePeerForElement(item)?.GetAccessKey()))
                        throw new InvalidOperationException("Context action does not expose its access key to UIA: " + item.Text);
                context.Hide(); Microsoft.UI.Xaml.Input.AccessKeyManager.ExitDisplayMode();
            }
        }
    }

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
            if (group.Content is not Views.AccessibilityGroup contentGroup ||
                FrameworkElementAutomationPeer.CreatePeerForElement(contentGroup) is not { } contentPeer ||
                contentPeer.GetAutomationControlType() != AutomationControlType.Group || contentPeer.GetName() != name + " controls")
                throw new InvalidOperationException("Inspector content must be a named UIA group: " + name);
            var expanded = group.IsExpanded;
            try
            {
                provider.Collapse(); await Task.Delay(75);
                if (group.IsExpanded || provider.ExpandCollapseState != ExpandCollapseState.Collapsed || peer.GetName() != name)
                    throw new InvalidOperationException("Collapsed disclosure state/name mismatch: " + name);
                provider.Expand(); await Task.Delay(75);
                if (!group.IsExpanded || provider.ExpandCollapseState != ExpandCollapseState.Expanded || peer.GetName() != name)
                    throw new InvalidOperationException("Expanded disclosure state/name mismatch: " + name);
                if (contentPeer.GetChildren() is not { Count: > 0 })
                    throw new InvalidOperationException("Expanded group lost its accessible children: " + name);
                provider.Collapse(); await Task.Delay(75);
                if (!contentPeer.IsOffscreen())
                    throw new InvalidOperationException("Collapsed inspector controls remain exposed on screen: " + name);
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

    private async Task VerifyEditorSectionToolbarForSmokeTestAsync(string report)
    {
        ShowEditor(); await Task.Delay(150);
        var originalSize = AppWindow.Size;
        var originalTheme = App.ActiveTheme ?? Model.Settings.Get("theme", "system");
        var originalSection = Section.SelectedIndex;
        var originalProjects = ProjectSplitView.IsPaneOpen;
        var originalInspector = InspectorSplitView.IsPaneOpen;
        try
        {
            ProjectSplitView.IsPaneOpen = false; InspectorSplitView.IsPaneOpen = true;
            var scale = Root.XamlRoot.RasterizationScale;
            foreach (var theme in new[] { "light", "dark" })
            {
                PreviewTheme(theme);
                foreach (var width in new[] { 1120, 880 })
                {
                    AppWindow.Resize(new Windows.Graphics.SizeInt32((int)(width * scale), (int)(760 * scale)));
                    for (var index = 0; index < 5; index++)
                    {
                        Section.SelectedIndex = index; UpdateSectionPresentation(); await Task.Delay(100); Root.UpdateLayout();
                        var controlsPeer = FrameworkElementAutomationPeer.CreatePeerForElement(EditorControlsGroup);
                        if (controlsPeer?.GetAutomationControlType() != AutomationControlType.Group || controlsPeer.GetName() != EditorSectionTitle + " controls" ||
                            controlsPeer.GetChildren() is not { Count: > 0 } || AutomationProperties.GetName(Segments) != EditorSectionTitle + " segments")
                            throw new InvalidOperationException("Editor controls and segment list lost their named accessibility groups.");
                        var heading = EditorSectionHeading.TransformToVisual(EditorSectionToolbar).TransformPoint(new Windows.Foundation.Point(EditorSectionHeading.ActualWidth / 2, EditorSectionHeading.ActualHeight));
                        var controls = EditorSectionControlsScroll.TransformToVisual(EditorSectionToolbar).TransformPoint(new Windows.Foundation.Point());
                        var actions = EditorSectionActions.TransformToVisual(EditorSectionToolbar).TransformPoint(new Windows.Foundation.Point());
                        var importPoint = EditorImportAssetsButton.TransformToVisual(EditorSectionControlsScroll).TransformPoint(new Windows.Foundation.Point());
                        var pickerPoint = Section.TransformToVisual(EditorSectionControlsScroll).TransformPoint(new Windows.Foundation.Point());
                        var peer = FrameworkElementAutomationPeer.CreatePeerForElement(EditorImportAssetsButton);
                        if (Math.Abs(heading.X - EditorSectionToolbar.ActualWidth / 2) > 1 || heading.Y > controls.Y || heading.Y > actions.Y ||
                            controls.X + EditorSectionControlsScroll.ActualWidth > actions.X + 1 ||
                            EditorSectionControlsScroll.ActualWidth <= 0 || importPoint.X + EditorImportAssetsButton.ActualWidth > pickerPoint.X ||
                            EditorSectionHeading.Text != EditorSectionTitle || AutomationProperties.GetHeadingLevel(EditorSectionHeading) != AutomationHeadingLevel.Level2 ||
                            peer?.GetName() != "Import Existing Assets" || EditorImportAssetsButton.IsEnabled != CanRunDesktopCommand("import-assets") ||
                            peer.GetPattern(PatternInterface.Invoke) is not Microsoft.UI.Xaml.Automation.Provider.IInvokeProvider)
                            throw new InvalidOperationException("Editor heading/import toolbar alignment or accessibility mismatch: " + theme + "/" + width + "/" + EditorSectionTitle);
                        if (index == 0) await CaptureSmokeImage(EditorSectionToolbar, System.IO.Path.Combine(System.IO.Path.GetDirectoryName(report)!, $"editor-section-toolbar-{theme}-{width}.png"));
                    }
                }
            }
            ContentDialog? opened = null;
            void Observe(ContentDialog dialog, Window window) { opened = dialog; }
            Views.WindowedDialog.Preparing += Observe;
            try
            {
                foreach (var fromButton in new[] { true, false })
                {
                    opened = null;
                    Task? command = null;
                    if (fromButton)
                    {
                        var peer = FrameworkElementAutomationPeer.CreatePeerForElement(EditorImportAssetsButton)!;
                        ((Microsoft.UI.Xaml.Automation.Provider.IInvokeProvider)peer.GetPattern(PatternInterface.Invoke)).Invoke();
                    }
                    else command = RunDesktopCommandAsync("import-assets");
                    for (var attempt = 0; attempt < 100 && opened == null; attempt++) await Task.Delay(20);
                    if (opened is not Views.ImportAssetsDialog || opened.Title as string != "Import Existing Assets")
                        throw new InvalidOperationException("Toolbar and menu must open the same all-assets import workflow.");
                    var dialog = opened;
                    if (dialog.Content is not ScrollViewer { Content: StackPanel importPanel } ||
                        importPanel.Children.OfType<CheckBox>().Any(box => box.Content as string == "Replace existing assets after validation"))
                        throw new InvalidOperationException("Import must use saved-result review without an upfront replacement checkbox.");
                    await dialogPresentationVerifier!.WaitUntilReadyAsync(dialog);
                    await Task.Delay(100);
                    // Verify startup before deliberately revealing the file-dependent controls.
                    if (fromButton) await VerifyDialogThemesForSmokeTestAsync(dialog, report, "import-assets-grouped-empty");
                    else dialogPresentationVerifier.Verify(dialog);
                    await ((Views.ImportAssetsDialog)dialog).VerifyGroupedLayoutForSmokeTestAsync();
                    var cancel = FindButton(dialog, "Cancel") ?? throw new InvalidOperationException("Import cancellation unavailable.");
                    var cancelPeer = FrameworkElementAutomationPeer.CreatePeerForElement(cancel)!;
                    ((Microsoft.UI.Xaml.Automation.Provider.IInvokeProvider)cancelPeer.GetPattern(PatternInterface.Invoke)).Invoke();
                    if (command != null) await command;
                    await Task.Delay(100);
                }
            }
            finally { Views.WindowedDialog.Preparing -= Observe; }
        }
        finally
        {
            Section.SelectedIndex = originalSection; UpdateSectionPresentation();
            ProjectSplitView.IsPaneOpen = originalProjects; InspectorSplitView.IsPaneOpen = originalInspector;
            AppWindow.Resize(originalSize); PreviewTheme(originalTheme);
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
