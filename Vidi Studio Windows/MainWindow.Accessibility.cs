using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Controls;
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Linq;
using System.Threading.Tasks;
using Vidi_Studio_Windows.Models;

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
                    throw new InvalidOperationException("Submenu access keys must be present and unique within their scope: " +
                        string.Join(", ", items.Select(item => (item is MenuFlyoutItem action ? action.Text : ((MenuFlyoutSubItem)item).Text) + "=" + item.AccessKey)));
                foreach (var item in items) {
                    var (title, icon) = item switch {
                        MenuFlyoutItem command => (command.Text, command.Icon),
                        MenuFlyoutSubItem submenu => (submenu.Text, submenu.Icon),
                        _ => ("", null)
                    };
                    if (icon == null || icon is FontIcon font && (string.IsNullOrWhiteSpace(font.Glyph) || font.Glyph == "\uE712" && title != "More Actions"))
                        throw new InvalidOperationException("Menu command needs a visible icon: " + title);
                }
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
            var cloudRecord = project.CloudVideo;
            try {
                project.CloudVideo = new() { ["bucket"] = "fixture", ["object"] = "preview.mp4" };
                foreach (var (id, _) in CloudActions(project)) {
                    var command = ProjectMenu(project).Items.OfType<MenuFlyoutItem>().Single(item => Equals(item.Tag, id));
                    if (command.Icon is not FontIcon { Glyph: var glyph } || glyph.Length == 0 || glyph == "\uE712")
                        throw new InvalidOperationException("Cloud context command needs a specific icon: " + id);
                }
                if (DesktopCommandIcon("cloud-upload") is not FontIcon { Glyph: "\uE898" } ||
                    DesktopCommandIcon("new-cloud-project") is not FontIcon { Glyph: "\uE753" })
                    throw new InvalidOperationException("Cloud upload or New Project command icon is missing.");
            }
            finally { project.CloudVideo = cloudRecord; }
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
        VerifyYouTubeInspectorCopyForSmokeTest();
        var project = Model.Project ?? throw new InvalidOperationException("Inspector fixture missing.");
        foreach (var editing in new[] { false, true })
        {
            var settingsPanel = BuildLanguageResultsPanel(project, settingsContext: true, editingSettings: editing);
            if (settingsPanel.Children.OfType<Button>().Any(button => button.Content as string == "Keep All Sections in Sync") ||
                settingsPanel.Children.OfType<ComboBox>().Any(picker => picker.Header?.ToString()?.EndsWith(" Generation Language") == true))
                throw new InvalidOperationException("Project Settings must share the same single language.");
        }
        ShowGallery_Click(this, new RoutedEventArgs());
    }

    private void VerifyYouTubeInspectorCopyForSmokeTest()
    {
        const string videoID = "AbcDef432_1";
        const string url = "https://www.youtube.com/watch?v=AbcDef432_1&t=6";
        var panel = new StackPanel();
        AddVideoSourceInformation(panel, new StudioProject { SourceType = "youtube", YoutubeURL = url });
        var expected = new[] {
            (Value: videoID, Name: "Copy YouTube Video ID", Id: "copy-youtube-video-id"),
            (Value: url, Name: "Copy YouTube URL", Id: "copy-youtube-url")
        };
        foreach (var (row, item) in panel.Children.OfType<Grid>().Zip(expected))
        {
            if (row.Children[1] is not TextBlock text || text.Text != item.Value ||
                row.Children.OfType<Button>().SingleOrDefault() is not { } button ||
                AutomationProperties.GetName(button) != item.Name || !Equals(ToolTipService.GetToolTip(button), item.Name) ||
                AutomationProperties.GetAutomationId(button) != item.Id || row.ContextFlyout is not MenuFlyout menu ||
                menu.Items.OfType<MenuFlyoutItem>().Single() is not { } command ||
                command.Text != item.Name || !Equals(command.Tag, item.Id) || command.Icon == null)
                throw new InvalidOperationException("YouTube inspector source rows must expose named, matching copy actions.");
        }
        if (panel.Children.Count != expected.Length) throw new InvalidOperationException("YouTube copy rows are incomplete.");
        panel.Children.Clear();
        AddVideoSourceInformation(panel, new StudioProject
        {
            SourceType = "youtube",
            YouTubeMetadata = System.Text.Json.JsonSerializer.SerializeToElement(new { videoID })
        });
        var fallbackRow = panel.Children.OfType<Grid>().First();
        if (fallbackRow.Children[1] is not TextBlock fallback || fallback.Text != videoID ||
            fallbackRow.Children.OfType<Button>().SingleOrDefault() is not { } fallbackCopy ||
            AutomationProperties.GetName(fallbackCopy) != "Copy YouTube Video ID")
            throw new InvalidOperationException("A saved YouTube video ID must stay copyable when its URL is unavailable.");
        panel.Children.Clear();
        AddVideoSourceInformation(panel, new StudioProject { Source = "sample.mp4" });
        AddVideoSourceInformation(panel, new StudioProject { SourceType = "youtube", YoutubeURL = "" });
        if (panel.Children.OfType<Grid>().Any(row => row.Children.OfType<Button>().Any() || row.ContextFlyout != null))
            throw new InvalidOperationException("Local and unavailable video sources must not expose copy actions.");
    }

    private async Task VerifyPreviewToolbarForSmokeTestAsync()
    {
        var originalPreview = PreviewPanel.Visibility == Visibility.Visible;
        var originalTimeline = timelineVisible;
        var originalSetting = Model.Settings.Get("showPreview", "true");
        try
        {
            timelineVisible = true;
            ApplyPreviewVisibility(true);
            Root.UpdateLayout();
            var parent = (Grid)PreviewPanel.Parent;
            double ToolbarTop() => EditorSectionToolbar.TransformToVisual(parent).TransformPoint(new Windows.Foundation.Point()).Y;
            var shownTop = ToolbarTop();
            var previewPeer = FrameworkElementAutomationPeer.CreatePeerForElement(PreviewToggleButton)!;
            var fullScreenPeer = FrameworkElementAutomationPeer.CreatePeerForElement(FullScreenVideoButton)!;
            if (previewPeer.GetName() != "Hide Preview" ||
                ToolTipService.GetToolTip(PreviewToggleButton) as string != "Hide video preview (Ctrl+Shift+V)" ||
                fullScreenPeer.GetName() != "Full-screen video" ||
                ToolTipService.GetToolTip(FullScreenVideoButton) is not string fullScreenTip || !fullScreenTip.Contains("Press Esc to return"))
                throw new InvalidOperationException("The editor preview buttons must have accessible names and tooltips.");

            ((Microsoft.UI.Xaml.Automation.Provider.IInvokeProvider)previewPeer.GetPattern(PatternInterface.Invoke)).Invoke();
            for (var attempt = 0; attempt < 50 && PreviewPanel.Visibility == Visibility.Visible; attempt++) await Task.Delay(20);
            Root.UpdateLayout();
            if (PreviewPanel.Visibility != Visibility.Collapsed || TimelinePanel.Visibility != Visibility.Collapsed ||
                ToolbarTop() >= shownTop || previewPeer.GetName() != "Show Preview" ||
                ToolTipService.GetToolTip(PreviewToggleButton) as string != "Show video preview (Ctrl+Shift+V)" ||
                Model.Settings.Get("showPreview", "true") != "false")
                throw new InvalidOperationException("Hide Preview must collapse the video and timeline and reclaim editor space.");

            await RunDesktopCommandAsync("preview");
            if (PreviewPanel.Visibility != Visibility.Visible || TimelinePanel.Visibility != Visibility.Visible)
                throw new InvalidOperationException("Show Preview must restore both panels when the timeline is enabled.");

            await RunDesktopCommandAsync("toggle-timeline");
            await RunDesktopCommandAsync("preview");
            await RunDesktopCommandAsync("preview");
            if (PreviewPanel.Visibility != Visibility.Visible || TimelinePanel.Visibility != Visibility.Collapsed ||
                DesktopCommandTitle("toggle-timeline") != "Show Timeline")
                throw new InvalidOperationException("Showing the preview must preserve the separate Hide Timeline choice.");

            await RunDesktopCommandAsync("preview");
            await RunDesktopCommandAsync("toggle-timeline");
            if (TimelinePanel.Visibility != Visibility.Collapsed)
                throw new InvalidOperationException("The timeline must stay collapsed while the preview is hidden.");
            await RunDesktopCommandAsync("preview");
            if (TimelinePanel.Visibility != Visibility.Visible)
                throw new InvalidOperationException("The restored preview must use the latest timeline choice.");
        }
        finally
        {
            timelineVisible = originalTimeline;
            ApplyPreviewVisibility(originalPreview);
            Model.Settings.Set("showPreview", originalSetting);
            Model.Settings.Save();
        }
    }

    private void VerifyEditorButtonTooltipsForSmokeTest()
    {
        IEnumerable<FrameworkElement> EditorElements(FrameworkElement element)
        {
            if (element.Visibility != Visibility.Visible) yield break;
            yield return element;
            IEnumerable<FrameworkElement> children = element switch
            {
                Panel panel => panel.Children.OfType<FrameworkElement>(),
                Border { Child: FrameworkElement content } => new[] { content },
                SplitView split => new[] { split.Pane, split.Content }.OfType<FrameworkElement>(),
                Expander { IsExpanded: true, Content: FrameworkElement content } => new[] { content },
                Expander => Array.Empty<FrameworkElement>(),
                ScrollViewer { Content: FrameworkElement content } => new[] { content },
                InfoBar { IsOpen: true, ActionButton: FrameworkElement action } => new[] { action },
                ListViewBase list => Enumerable.Range(0, list.Items.Count)
                    .Select(index => (list.ContainerFromIndex(index) as ContentControl)?.ContentTemplateRoot).OfType<FrameworkElement>(),
                ContentControl { Content: FrameworkElement content } => new[] { content },
                _ => Array.Empty<FrameworkElement>()
            };
            foreach (var child in children.SelectMany(EditorElements)) yield return child;
        }

        var buttons = EditorElements(EditorWorkspace).OfType<Microsoft.UI.Xaml.Controls.Primitives.ButtonBase>().ToArray();
        if (buttons.Length < 5) throw new InvalidOperationException("Tooltip audit did not reach the rendered editor buttons.");
        foreach (var button in buttons)
        {
            var tooltip = ToolTipService.GetToolTip(button);
            var label = (tooltip as ToolTip)?.Content as string ?? tooltip as string;
            if (string.IsNullOrWhiteSpace(label))
            {
                var name = FrameworkElementAutomationPeer.CreatePeerForElement(button)?.GetName();
                throw new InvalidOperationException($"Missing tooltip in {EditorSectionTitle}: {button.Name} {name}.");
            }
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct TooltipTestPoint { public int X; public int Y; }

    [DllImport("user32.dll", EntryPoint = "GetCursorPos")]
    private static extern bool GetTooltipTestCursor(out TooltipTestPoint point);

    [DllImport("user32.dll", EntryPoint = "SetCursorPos")]
    private static extern bool MoveTooltipTestCursor(int x, int y);

    [DllImport("user32.dll", EntryPoint = "ClientToScreen")]
    private static extern bool MapTooltipClientPoint(IntPtr window, ref TooltipTestPoint point);

    [DllImport("user32.dll", EntryPoint = "mouse_event")]
    private static extern void SendTooltipMouseMove(uint flags, int x, int y, uint data, UIntPtr extra);

    [DllImport("user32.dll", EntryPoint = "WindowFromPoint")]
    private static extern IntPtr TooltipWindowAt(TooltipTestPoint point);

    [DllImport("user32.dll", EntryPoint = "GetAncestor")]
    private static extern IntPtr TooltipRootWindow(IntPtr window, uint flag);

    [DllImport("user32.dll", EntryPoint = "SetForegroundWindow")]
    private static extern bool FocusTooltipTestWindow(IntPtr window);

    private async Task VerifyEditorActionHoverForSmokeTestAsync()
    {
        if (!GetTooltipTestCursor(out var original)) throw new InvalidOperationException("Cannot read the Windows test pointer.");
        var window = WinRT.Interop.WindowNative.GetWindowHandle(this);
        var originalPosition = AppWindow.Position;
        var originalPreview = PreviewPanel.Visibility == Visibility.Visible;
        var workArea = Microsoft.UI.Windowing.DisplayArea.GetFromWindowId(AppWindow.Id, Microsoft.UI.Windowing.DisplayAreaFallback.Primary).WorkArea;
        TooltipTestPoint Hover(FrameworkElement element)
        {
            var center = element.TransformToVisual(Root).TransformPoint(
                new Windows.Foundation.Point(element.ActualWidth / 2, element.ActualHeight / 2));
            var scale = Root.XamlRoot.RasterizationScale;
            var point = new TooltipTestPoint { X = (int)(center.X * scale), Y = (int)(center.Y * scale) };
            if (!MapTooltipClientPoint(window, ref point) || !MoveTooltipTestCursor(point.X, point.Y))
                throw new InvalidOperationException("Cannot hover over " + element.Name);
            SendTooltipMouseMove(1, 1, 0, 0, UIntPtr.Zero); // Send a native move after positioning the cursor.
            return point;
        }

        try
        {
            AppWindow.Move(new Windows.Graphics.PointInt32(workArea.X + 16, workArea.Y + 16));
            ApplyPreviewVisibility(false);
            Activate();
            FocusTooltipTestWindow(window);
            await Task.Delay(300);
            Root.UpdateLayout();
            var controls = new[]
            {
                (EditorAddButton, EditorAddToolTip),
                (EditModeButton, EditModeToolTip),
                (GenerateCurrent, GenerateCurrent.IsEnabled ? GenerateCurrentToolTip : GenerateUnavailableToolTip)
            };
            foreach (var (button, tooltip) in controls)
            {
                var command = ReferenceEquals(button, GenerateCurrent) ? "transcribe"
                    : ReferenceEquals(button, EditModeButton) ? "segment-edit-mode" : "add";
                if (tooltip.Content is not string text || !text.Contains(DesktopShortcuts[command].Text, StringComparison.Ordinal))
                    throw new InvalidOperationException("The rendered editor tooltip lost its shortcut for " + command);
                Hover(EditorSectionHeading);
                await Task.Delay(200);
                var target = ReferenceEquals(tooltip, GenerateUnavailableToolTip) ? (FrameworkElement)GenerateUnavailableHoverTarget : button;
                var entered = false;
                void PointerEntered(object sender, Microsoft.UI.Xaml.Input.PointerRoutedEventArgs args) => entered = true;
                target.PointerEntered += PointerEntered;
                try
                {
                    var point = Hover(button);
                    for (var attempt = 0; attempt < 32 && !tooltip.IsOpen; attempt++) await Task.Delay(75);
                    if (!tooltip.IsOpen)
                    {
                        GetTooltipTestCursor(out var cursor);
                        var inEditor = TooltipRootWindow(TooltipWindowAt(point), 2) == window;
                        throw new InvalidOperationException(
                            $"Hover did not open the tooltip for {button.Name} (pointer entered: {entered}, editor under pointer: {inEditor}, cursor {cursor.X},{cursor.Y}, target {point.X},{point.Y}).");
                    }
                }
                finally { target.PointerEntered -= PointerEntered; }
            }
        }
        finally
        {
            ApplyPreviewVisibility(originalPreview);
            AppWindow.Move(originalPosition);
            MoveTooltipTestCursor(original.X, original.Y);
        }
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
            var cloudGroups = desktopMenuGroups.Where(entry => entry.Name == "Cloud Storage").Select(entry => entry.Item).ToArray();
            var project = Model.Project!;
            var savedCloud = project.CloudVideo;
            var connected = Model.Settings.GetBool("vertexConnected");
            try
            {
                Model.Settings.Set("vertexConnected", false);
                foreach (var cloud in new Dictionary<string, string>?[] {
                    null, new() { ["bucket"] = "ui-smoke-fixture", ["object"] = "test.mp4", ["ownership"] = "referenced" }
                })
                {
                    project.CloudVideo = cloud;
                    UpdateDesktopMenuStates();
                    if (cloudGroups.Length == 0 || cloudGroups.Any(group => group.Visibility != Visibility.Collapsed) ||
                        CloudActions(project).Length != 0)
                        throw new InvalidOperationException("The editor must hide Cloud Storage actions for signed-out projects, including bucket videos.");
                }
            }
            finally
            {
                project.CloudVideo = savedCloud;
                Model.Settings.Set("vertexConnected", connected);
                UpdateDesktopMenuStates();
            }
            var scale = Root.XamlRoot.RasterizationScale;
            foreach (var theme in new[] { "light", "dark" })
            {
                PreviewTheme(theme);
                foreach (var width in new[] { 1120, 880 })
                {
                    AppWindow.Resize(new Windows.Graphics.SizeInt32((int)(width * scale), (int)(760 * scale)));
                    await Task.Delay(100);
                    await VerifyPreviewToolbarForSmokeTestAsync();
                    for (var index = 0; index < 5; index++)
                    {
                        Section.SelectedIndex = index; UpdateSectionPresentation(); await Task.Delay(100); Root.UpdateLayout();
                        VerifyEditorButtonTooltipsForSmokeTest();
                        if (CurrentGenerationCommand is { } command)
                        {
                            var shortcut = DesktopShortcuts[command].Text;
                            if (GenerateCurrentToolTip.Content is not string tooltip || !tooltip.Contains(shortcut, StringComparison.Ordinal) ||
                                AutomationProperties.GetAcceleratorKey(InspectorGenerate) != shortcut ||
                                desktopMenuItems[command].Last().KeyboardAcceleratorTextOverride != shortcut ||
                                GenerateCurrent.IsEnabled != CanRunDesktopCommand(command))
                                throw new InvalidOperationException("Generation menu, button, and tooltip must share " + shortcut);
                        }
                        if (index is 2 or 3 or 4)
                        {
                            var button = index == 4 ? OverviewGenerateAllAudioButton : InspectorSynthesizeNarrationButton;
                            const string shortcut = "Ctrl+Alt+Shift+N";
                            if (ToolTipService.GetToolTip(button) is not string tooltip || !tooltip.Contains(shortcut, StringComparison.Ordinal) ||
                                AutomationProperties.GetAcceleratorKey(button) != shortcut ||
                                desktopMenuItems["synthesize-tts"].Last().KeyboardAcceleratorTextOverride != shortcut)
                                throw new InvalidOperationException("Narration generation must show the same shortcut in the menu and tooltip.");
                        }
                        if (index == 4)
                        {
                            var panels = new[] { OverviewProjectDetails, OverviewTranscript, OverviewSummary, OverviewStatistics, OriginStatistics };
                            var groups = new[] { OverviewProjectGroup, OverviewTranscriptGroup, OverviewSummaryGroup, OverviewDescriptionGroup, OverviewOriginGroup };
                            var names = new[] { "Project details", "Transcript", "Summary", "Audio description details", "Description origins" };
                            for (int i = 0; i < panels.Length; i++)
                            {
                                if (FrameworkElementAutomationPeer.CreatePeerForElement(groups[i])?.GetName() != names[i] || panels[i].ActualWidth <= 0 ||
                                    panels[i].Children.Count == 0 || OverviewPanel.ScrollableHeight <= 0)
                                    throw new InvalidOperationException("Overview group is missing, empty or inaccessible: " + names[i]);
                            }
                            var summaryLabels = OverviewSummary.Children.OfType<Grid>()
                                .Select(row => ((TextBlock)row.Children[0]).Text);
                            if (OverviewSummary.Children.Count != 2 || !summaryLabels.SequenceEqual(new[] { "Summary status", "Summary characters" }))
                                throw new InvalidOperationException("Overview Summary must contain only status and character count.");
                            var transcript = OverviewTranscript.TransformToVisual(OverviewPanel).TransformPoint(new Windows.Foundation.Point());
                            var summary = OverviewSummary.TransformToVisual(OverviewPanel).TransformPoint(new Windows.Foundation.Point());
                            if (OverviewTextPair.ActualWidth >= 656 ? summary.X <= transcript.X || Math.Abs(summary.Y - transcript.Y) > 1
                                : summary.Y < transcript.Y + OverviewTranscript.ActualHeight || Math.Abs(summary.X - transcript.X) > 1)
                                throw new InvalidOperationException("Transcript and Summary groups did not reflow at this editor width.");
                        }
                        var languagePanel = LanguageResultsPanel.Children.OfType<StackPanel>().Single();
                        var language = languagePanel.Children.OfType<Control>().FirstOrDefault() as ComboBox
                            ?? throw new InvalidOperationException("Inspector language must be the first control.");
                        var languagePeer = FrameworkElementAutomationPeer.CreatePeerForElement(language);
                        var firstAction = languagePanel.Children.OfType<Button>().FirstOrDefault();
                        var languageTop = language.TransformToVisual(languagePanel).TransformPoint(new Windows.Foundation.Point());
                        if (languagePeer?.GetName() != "View Project Language" || language.ActualWidth <= 0 ||
                            languageTop.X < -1 || languageTop.X + language.ActualWidth > languagePanel.ActualWidth + 1 ||
                            firstAction != null && firstAction.TransformToVisual(languagePanel).TransformPoint(new Windows.Foundation.Point()).Y < languageTop.Y + language.ActualHeight - 1)
                            throw new InvalidOperationException("Shared inspector language must be accessible, bounded and above every action.");
                        if (languagePanel.Children.OfType<ComboBox>().Any(picker => picker.Header?.ToString()?.EndsWith(" Generation Language") == true || picker.Header as string == "Review language filter"))
                            throw new InvalidOperationException("Rendered inspector contains repeated language choices.");
                        if (InspectorExports.Children.OfType<ComboBox>().Any(picker => picker.Header as string == "Export language filter"))
                            throw new InvalidOperationException("Created Exports must follow the shared inspector language.");
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
            foreach (var index in new[] { 0, 1, 2, 3 })
            {
                Section.SelectedIndex = index;
                if (index == 1) SetSummaryEditMode(true);
                else EditMode_Click(this, new RoutedEventArgs());
                try
                {
                    await Task.Delay(100);
                    Root.UpdateLayout();
                    VerifyEditorButtonTooltipsForSmokeTest();
                    if (EditModeToolTip.Content is not string tip || !tip.StartsWith("Done editing", StringComparison.Ordinal))
                        throw new InvalidOperationException("The editor tooltip must change to Done when editing.");
                }
                finally
                {
                    if (index == 1) SetSummaryEditMode(false);
                    else EditMode_Click(this, new RoutedEventArgs());
                }
            }
            Section.SelectedIndex = 0;
            UpdateSectionPresentation();
            await VerifyEditorActionHoverForSmokeTestAsync();
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
        await VerifyProjectPaneTransitionsForSmokeTestAsync();
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
        var ids = new[] { "transcript", "summary", "descriptions", "ad-transcript", "ad-overview" };
        var titles = new[] { "Transcript", "Summary", "Descriptions", "AD Transcript", "Overview" };
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
        // The timeline is populated by the first dispatcher timer tick. Wait for
        // that production update instead of assuming it ran during window layout.
        for (var attempt=0; attempt<20 && (!AutomationProperties.GetName(TimeLabel).Contains(duration) || !AutomationProperties.GetItemStatus(Playhead).Contains(duration)); attempt++)
            await Task.Delay(100);
        if (!AutomationProperties.GetName(TimeLabel).Contains(duration) || !AutomationProperties.GetItemStatus(Playhead).Contains(duration))
            throw new InvalidOperationException($"Timeline has no spoken media time/duration: name={AutomationProperties.GetName(TimeLabel)}, status={AutomationProperties.GetItemStatus(Playhead)}, duration={duration}.");
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
            foreach (var section in new[] { 0, 2, 3 })
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
