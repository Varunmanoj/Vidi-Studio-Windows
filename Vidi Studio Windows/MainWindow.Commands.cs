using Vidi_Studio_Windows.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Documents;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Vidi_Studio_Windows.Models;
using Windows.System;

namespace Vidi_Studio_Windows;

public sealed partial class MainWindow
{
    private readonly List<MenuBarItem> galleryMenus = new();
    private readonly List<MenuBarItem> editorMenus = new();
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
        FollowSummaryCharacter(activeSummaryStart + start);
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
                FollowSummaryCharacter(match);
                return;
            }
        }
    }
    private void UpdateSummaryControls()
    {
        var state = readback.State;
        SummaryPlayButton.Content = state == Services.SummaryReadbackState.Stopped ? "Play" : state == Services.SummaryReadbackState.Preparing ? "Preparing…" : state == Services.SummaryReadbackState.Playing ? "Pause" : "Resume";
        var online = Model.Settings.Get("ttsMode", "windows-core") != "gemini" || ConnectivityService.Shared.Available(OnlineService.Gemini);
        SummaryPlayButton.IsEnabled = state != Services.SummaryReadbackState.Preparing && (online || state is Services.SummaryReadbackState.Playing or Services.SummaryReadbackState.Paused);
        ToolTipService.SetToolTip(SummaryPlayButton, online ? "Read summary" : ConnectivityService.Shared.Reason(OnlineService.Gemini));
        SummaryStopButton.IsEnabled = state != Services.SummaryReadbackState.Stopped;
        var navigating = state != Services.SummaryReadbackState.Preparing && online;
        SummaryPreviousSentenceButton.IsEnabled = navigating && readback.CanGoPreviousSentence;
        SummaryNextSentenceButton.IsEnabled = navigating && readback.CanGoNextSentence;
        SummaryPreviousParagraphButton.IsEnabled = navigating && readback.CanGoPreviousParagraph;
        SummaryNextParagraphButton.IsEnabled = navigating && readback.CanGoNextParagraph;
        UpdateDesktopMenuStates();
        if (state == Services.SummaryReadbackState.Stopped) ClearSummaryHighlight();
    }
    private void OpenEditor_Click(object sender, RoutedEventArgs e) { if (Model.Project is { } p) App.OpenWindow(p.Folder); }
    private void ProjectGrid_Changed(object sender, SelectionChangedEventArgs e) => Project_Changed(sender, e);
    private void ToggleGrid_Click(object sender, RoutedEventArgs e)
    {
        var grid = ProjectGrid.Visibility != Visibility.Visible;
        ProjectGrid.Visibility = grid ? Visibility.Visible : Visibility.Collapsed;
        ProjectList.Visibility = grid ? Visibility.Collapsed : Visibility.Visible;
        Model.Settings.Set("projectPresentation", grid ? "grid" : "list"); Model.Settings.Save();
        UpdateEditorProjectButtons();
    }
    private void ToggleLibrary_Click(object sender, RoutedEventArgs e)
    {
        ProjectSplitView.IsPaneOpen = !ProjectSplitView.IsPaneOpen;
        UpdateEditorProjectButtons();
    }
    private void ProjectPane_Closing(SplitView sender, SplitViewPaneClosingEventArgs args)
    {
        // Keep keyboard focus reachable when the control containing it slides away.
        for (DependencyObject? focused = FocusManager.GetFocusedElement(Root.XamlRoot) as DependencyObject;
             focused != null; focused = Microsoft.UI.Xaml.Media.VisualTreeHelper.GetParent(focused))
        {
            if (!ReferenceEquals(focused, LibraryPanel)) continue;
            EditorProjectListButton.Focus(FocusState.Programmatic);
            break;
        }
    }

    private void UpdateEditorProjectButtons()
    {
        if (EditorProjectLayoutButton == null || EditorProjectListButton == null) return;
        var grid = ProjectGrid.Visibility == Visibility.Visible;
        EditorProjectLayoutIcon.Glyph = grid ? "\uE8FD" : "\uE80A";
        var layoutLabel = grid ? "Switch project list to list view" : "Switch project list to grid view";
        ToolTipService.SetToolTip(EditorProjectLayoutButton, layoutLabel);
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(EditorProjectLayoutButton, layoutLabel);
        var visibleLibrary = ProjectSplitView.IsPaneOpen;
        if (!changingProject && visibleLibrary && SelectedBrowserProject() == null && Model.Project is { } openProject)
        {
            var current = Model.Projects.FirstOrDefault(project => project.Folder == openProject.Folder);
            var wasChanging = changingProject;
            changingProject = true;
            ProjectList.SelectedItem = current;
            ProjectGrid.SelectedItem = current;
            changingProject = wasChanging;
        }
        ProjectList.IsEnabled = Model.Idle;
        ProjectGrid.IsEnabled = Model.Idle;
        var selected = SelectedBrowserProject();
        if (EditorRemoveProjectButton != null) EditorRemoveProjectButton.IsEnabled = Model.Idle && selected != null;
        if (EditorDeleteProjectButton != null) EditorDeleteProjectButton.IsEnabled = Model.Idle && selected != null;
        SyncPanelVisibilityToggles();
        EditorProjectListIcon.Glyph = "\uE8F1";
    }
    private void ToggleInspector_Click(object sender, RoutedEventArgs e)
    {
        InspectorSplitView.IsPaneOpen = !InspectorSplitView.IsPaneOpen;
        UpdateEditorActionVisibility();
        UpdateDesktopMenuStates();
    }
    private void SyncPanelVisibilityToggles()
    {
        if (EditorProjectListButton == null || TopInspectorToggle == null || ProjectSplitView == null || InspectorSplitView == null) return;
        EditorProjectListButton.IsChecked = ProjectSplitView.IsPaneOpen;
        TopInspectorToggle.IsChecked = InspectorSplitView.IsPaneOpen;
        var projectLabel = ProjectSplitView.IsPaneOpen ? "Hide project list" : "Show project list";
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(EditorProjectListButton, projectLabel);
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetItemStatus(EditorProjectListButton, ProjectSplitView.IsPaneOpen ? "Project list shown" : "Project list hidden");
        ToolTipService.SetToolTip(EditorProjectListButton, projectLabel);
        var inspectorLabel = DesktopCommandTitle("show-inspector");
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(TopInspectorToggle, inspectorLabel);
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetItemStatus(TopInspectorToggle, InspectorSplitView.IsPaneOpen ? "Inspector shown" : "Inspector hidden");
        ToolTipService.SetToolTip(TopInspectorToggle, inspectorLabel + " (" + DesktopShortcuts["show-inspector"].Text + ")");
    }
    private async void PanelVisibilityToggle_Changed(object sender, RoutedEventArgs e)
    {
        if (sender is not Microsoft.UI.Xaml.Controls.Primitives.ToggleButton button || ProjectSplitView == null || InspectorSplitView == null) return;
        var projects = ReferenceEquals(button, EditorProjectListButton);
        if ((button.IsChecked == true) == (projects ? ProjectSplitView.IsPaneOpen : InspectorSplitView.IsPaneOpen)) return;
        try { await RunDesktopCommandAsync(projects ? "show-projects" : "show-inspector"); }
        catch (OperationCanceledException) { }
        catch (Exception ex) { await ShowError(ex); }
        finally { SyncPanelVisibilityToggles(); }
    }
    private void RightInspectorPane_Closing(SplitView sender, SplitViewPaneClosingEventArgs args)
    {
        for (DependencyObject? focused = FocusManager.GetFocusedElement(Root.XamlRoot) as DependencyObject;
             focused != null; focused = VisualTreeHelper.GetParent(focused))
        {
            if (!ReferenceEquals(focused, InspectorPanel)) continue;
            TopInspectorToggle.Focus(FocusState.Programmatic);
            break;
        }
    }
    private void FullScreen_Click(object sender, RoutedEventArgs e)
    {
        if (Model.Project?.IsYouTube == true) return;
        if (fullScreenVideo) ExitFullScreenVideo();
        else EnterFullScreenVideo();
    }

    /// <summary>
    /// WinUI 3 does not implement MediaPlayerElement.IsFullWindow (it is a UWP-only
    /// feature), so full-screen video is implemented by moving the preview surface
    /// to the window root, hiding the editor chrome, and switching the AppWindow
    /// presenter to full screen.
    /// </summary>
    private void EnterFullScreenVideo()
    {
        if (fullScreenVideo || PreviewPanel.Parent is not Grid parent) return;
        previewFullScreenParent = parent;
        previewFullScreenIndex = parent.Children.IndexOf(PreviewPanel);
        previewFullScreenRow = Grid.GetRow(PreviewPanel);
        previewFullScreenRowSpan = Grid.GetRowSpan(PreviewPanel);
        previewFullScreenColumn = Grid.GetColumn(PreviewPanel);
        previewFullScreenColumnSpan = Grid.GetColumnSpan(PreviewPanel);
        previewFullScreenHeight = PreviewPanel.Height;
        previewFullScreenHorizontalAlignment = PreviewPanel.HorizontalAlignment;
        previewFullScreenVerticalAlignment = PreviewPanel.VerticalAlignment;
        previewFullScreenVisibility = PreviewPanel.Visibility;

        fullScreenHidden.Clear();
        foreach (var child in Root.Children.OfType<UIElement>().ToList())
        {
            if (ReferenceEquals(child, PreviewPanel)) continue;
            fullScreenHidden[child] = child.Visibility;
            child.Visibility = Visibility.Collapsed;
        }

        parent.Children.Remove(PreviewPanel);
        Grid.SetRow(PreviewPanel, 0);
        Grid.SetRowSpan(PreviewPanel, 6);
        Grid.SetColumn(PreviewPanel, 0);
        Grid.SetColumnSpan(PreviewPanel, 1);
        PreviewPanel.Height = double.NaN;
        PreviewPanel.HorizontalAlignment = HorizontalAlignment.Stretch;
        PreviewPanel.VerticalAlignment = VerticalAlignment.Stretch;
        PreviewPanel.Visibility = Visibility.Visible;
        Root.Children.Add(PreviewPanel);
        Canvas.SetZIndex(PreviewPanel, 1000);

        fullScreenExitButton = new Button
        {
            Content = new FontIcon { Glyph = "\uE73F", FontFamily = new FontFamily("Segoe Fluent Icons"), FontSize = 16 },
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Top,
            Margin = new Thickness(0, 20, 20, 0),
            Padding = new Thickness(10, 8, 10, 8),
            Background = new SolidColorBrush(Windows.UI.Color.FromArgb(170, 0, 0, 0)),
            Foreground = new SolidColorBrush(Microsoft.UI.Colors.White),
            BorderThickness = new Thickness(0),
            CornerRadius = new CornerRadius(6)
        };
        ToolTipService.SetToolTip(fullScreenExitButton, "Exit full screen (Esc)");
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(fullScreenExitButton, "Exit full-screen video");
        fullScreenExitButton.Click += (_, _) => ExitFullScreenVideo();
        PreviewPanel.Children.Add(fullScreenExitButton);

        fullScreenVideo = true;
        windowedPresenterMaximized = AppWindow.Presenter is Microsoft.UI.Windowing.OverlappedPresenter { State: Microsoft.UI.Windowing.OverlappedPresenterState.Maximized };
        windowedPresenter = AppWindow.Presenter as Microsoft.UI.Windowing.OverlappedPresenter;
        try { AppWindow.SetPresenter(Microsoft.UI.Windowing.AppWindowPresenterKind.FullScreen); }
        catch (Exception ex) { Model.Engine.Log(ex.ToString()); }
        Player.Focus(FocusState.Programmatic);
        UpdateMenuBar(true);
    }

    private void ExitFullScreenVideo()
    {
        if (!fullScreenVideo) return;
        fullScreenVideo = false;
        if (fullScreenExitButton != null)
        {
            PreviewPanel.Children.Remove(fullScreenExitButton);
            fullScreenExitButton = null;
        }
        Root.Children.Remove(PreviewPanel);
        if (previewFullScreenParent is { } parent)
        {
            Grid.SetRow(PreviewPanel, previewFullScreenRow);
            Grid.SetRowSpan(PreviewPanel, previewFullScreenRowSpan);
            Grid.SetColumn(PreviewPanel, previewFullScreenColumn);
            Grid.SetColumnSpan(PreviewPanel, previewFullScreenColumnSpan);
            PreviewPanel.Height = previewFullScreenHeight;
            PreviewPanel.HorizontalAlignment = previewFullScreenHorizontalAlignment;
            PreviewPanel.VerticalAlignment = previewFullScreenVerticalAlignment;
            PreviewPanel.Visibility = previewFullScreenVisibility;
            parent.Children.Insert(Math.Clamp(previewFullScreenIndex, 0, parent.Children.Count), PreviewPanel);
        }
        foreach (var pair in fullScreenHidden) pair.Key.Visibility = pair.Value;
        fullScreenHidden.Clear();
        if (windowedPresenter is { } presenter)
        {
            try
            {
                AppWindow.SetPresenter(presenter);
                if (windowedPresenterMaximized) presenter.Maximize();
            }
            catch (Exception ex)
            {
                Model.Engine.Log(ex.ToString());
                try { AppWindow.SetPresenter(Microsoft.UI.Windowing.AppWindowPresenterKind.Default); }
                catch (Exception fallback) { Model.Engine.Log(fallback.ToString()); }
            }
            windowedPresenter = null;
        }
        else
        {
            try { AppWindow.SetPresenter(Microsoft.UI.Windowing.AppWindowPresenterKind.Default); }
            catch (Exception ex) { Model.Engine.Log(ex.ToString()); }
        }
        Player.Focus(FocusState.Programmatic);
        UpdateMenuBar(true);
    }
    private void ProjectContext_Requested(UIElement sender, ContextRequestedEventArgs e)
    {
        var pointerRequest = e.TryGetPosition(sender, out var point);
        var element = (pointerRequest ? e.OriginalSource : FocusManager.GetFocusedElement(Root.XamlRoot)) as FrameworkElement;
        while (element != null && element.DataContext is not StudioProject) element = VisualTreeHelper.GetParent(element) as FrameworkElement;
        var project = element?.DataContext as StudioProject ?? (!pointerRequest ? (sender as ListViewBase)?.SelectedItem as StudioProject : null);
        if (project == null) return;
        var menu = ProjectMenu(project);
        if (pointerRequest) menu.ShowAt((FrameworkElement)sender, point);
        else menu.ShowAt(element ?? (FrameworkElement)sender);
        e.Handled = true;
    }
    private bool CanRunProjectContextCommand(string id, StudioProject project, bool editorBrowser)
    {
        if (!Model.Idle || windowClosed || validationShowcaseActive ||
            editorBrowser != desktopEditorVisible || !Model.Projects.Any(p => p.Folder == project.Folder) ||
            editorBrowser && HasUnsavedLanguageSummary) return false;
        var usable = !project.IntegrityPlaceholder && project.Integrity?.ManifestUnreadable != true && project.Integrity?.FolderMissing != true;
        if (id is "delete-project" or "trash-project" or "remove-from-list") return true;
        if (id == "open-configuration") return Model.Settings.GetBool("developerMode") && File.Exists(Path.Combine(project.Folder, "project.json"));
        if (id == "show-in-finder") return Directory.Exists(project.Folder);
        if (id is "open-gallery-window" or "open-editor-window" or "open-project-here") return true;
        if (!usable) return false;
        if (OnlineRequirements.Services(id, project, Model.Settings).Any(service => ConnectivityService.Shared.Reason(service) != null)) return false;
        return id switch {
            "youtube-open-browser" => YouTubeService.ProjectWatchURL(project.YoutubeURL) != null,
            "analyze" or "transcribe" or "generate-summary" => Model.CanUseAI,
            "generate-ad-cues" => Model.CanUseAI && project.SavedResults.Any(r => r.Kind == "transcript" && r.State == "saved") && project.SavedResults.Any(r => r.Kind == "summary" && r.State == "saved"),
            "delete-content" => project.SavedResults.Count > 0 || project.Exports.Count > 0 || project.Thumbnails.Count > 0 || project.Cues.Count > 0 || project.HasTranscript || project.HasSummary,
            "prepare-video" => !project.IsYouTube || ConnectivityService.Shared.Available(OnlineService.YouTube),
            _ => true
        };
    }

    private async System.Threading.Tasks.Task RunProjectContextCommandAsync(string id, StudioProject project, bool editorBrowser)
    {
        // Recheck the captured target and prerequisites when a flyout outlives its context.
        if (!CanRunProjectContextCommand(id, project, editorBrowser)) return;
        switch (id) {
        case "open-project-here": await SwitchProjectAsync(project); return;
        case "open-gallery-window":
        case "open-editor-window": App.OpenWindow(project.Folder); return;
        case "rename-project": await RenameProjectAsync(project); return;
        case "youtube-open-browser": OpenProjectInYouTube(project); return;
        case "project-settings": await ShowSettingsSectionAsync(1, project); return;
        case "show-in-finder": await RevealProjectAsync(project); return;
        case "open-configuration": await OpenProjectConfigurationAsync(project); return;
        case "remove-from-list": RemoveProjectFromList(project); return;
        case "delete-project":
        case "trash-project": await DeleteProjectAsync(project); return;
        }
        if (Model.Project is not { } current || !string.Equals(current.Folder, project.Folder, StringComparison.OrdinalIgnoreCase))
            Model.Open(project.Folder);
        if (!CanRunProjectContextCommand(id, project, editorBrowser)) return;
        if (id == "delete-content") await ShowLanguageDeletionAsync();
        else if (id == "export") Export_Click(this, new RoutedEventArgs());
        else Command_Click(new Button { Tag = id }, new RoutedEventArgs());
    }

    private MenuFlyout ProjectMenu(StudioProject project)
    {
        var menu = new MenuFlyout();
        var editorBrowser = desktopEditorVisible;
        MenuFlyoutItem Item(string id, string? title = null, IList<MenuFlyoutItemBase>? entries = null)
        {
            var label = title ?? DesktopCommandTitle(id, project);
            var item = new MenuFlyoutItem { Text = label, Icon = DesktopCommandIcon(id), Tag = id,
                IsEnabled = CanRunProjectContextCommand(id, project, editorBrowser) };
            Microsoft.UI.Xaml.Automation.AutomationProperties.SetAutomationId(item, id);
            if (id == "remove-from-list") ToolTipService.SetToolTip(item, "Remove this entry from the list and keep its files");
            item.Click += async (_, _) => {
                try { await RunProjectContextCommandAsync(id, project, editorBrowser); }
                catch (OperationCanceledException) { }
                catch (Exception ex) { await ShowError(ex); }
            };
            (entries ?? menu.Items).Add(item);
            return item;
        }
        if (editorBrowser)
        {
            Item("open-project-here", "Open in This Window");
            if (YouTubeService.ProjectWatchURL(project.YoutubeURL) != null) Item("youtube-open-browser");
            Item("open-editor-window", "Open in New Window");
            Item("project-settings");
            menu.Items.Add(new MenuFlyoutSeparator());
            Item("remove-from-list");
            Item("trash-project", "Delete Project…");
        }
        else
        {
            Item("open-gallery-window");
            menu.Items.Add(new MenuFlyoutSeparator());
            Item("rename-project", "Rename");
            if (YouTubeService.ProjectWatchURL(project.YoutubeURL) != null) Item("youtube-open-browser");
            Item("project-settings");
            Item("show-in-finder", "Open in Project Folder");
            if (Model.Settings.GetBool("developerMode")) Item("open-configuration", "Open Project Configuration");
            var analysis = new MenuFlyoutSubItem { Text = "Analysis", Tag = "Analysis", Icon = DesktopCommandIcon("Analysis"),
                IsEnabled = Model.Idle && !project.IntegrityPlaceholder };
            Item("prepare-video", entries: analysis.Items);
            analysis.Items.Add(new MenuFlyoutSeparator());
            Item("analyze", entries: analysis.Items);
            analysis.Items.Add(new MenuFlyoutSeparator());
            Item("transcribe", entries: analysis.Items);
            Item("generate-summary", entries: analysis.Items);
            Item("generate-ad-cues", entries: analysis.Items);
            menu.Items.Add(analysis);
            Item("export");
            Item("remove-from-list");
            menu.Items.Add(new MenuFlyoutSeparator());
            Item("delete-content", "Choose Content to Delete…");
            Item("delete-project");
        }
        menu.Opening += (_, _) => {
            void Refresh(IEnumerable<MenuFlyoutItemBase> entries) {
                foreach (var entry in entries) {
                    if (entry is MenuFlyoutItem item && item.Tag is string id) item.IsEnabled = CanRunProjectContextCommand(id, project, editorBrowser);
                    if (entry is MenuFlyoutSubItem group) { group.IsEnabled = Model.Idle && !project.IntegrityPlaceholder; Refresh(group.Items); }
                }
            }
            Refresh(menu.Items);
        };
        return menu;
    }
    private void RemoveListedProject_Click(object sender, RoutedEventArgs e)
    {
        if (SelectedBrowserProject() is { } project) RemoveProjectFromList(project);
    }
    private async void DeleteListedProject_Click(object sender, RoutedEventArgs e)
    {
        if (Model.Idle && SelectedBrowserProject() is { } project) await DeleteProjectAsync(project);
    }
    private void RemoveProjectFromList(StudioProject project)
    {
        if (!Model.Idle) return;
        try
        {
            Model.RemoveProjectFromList(project);
            RefreshGalleryCount();
        }
        catch (Exception ex) { _ = ShowError(ex); }
    }
    private async System.Threading.Tasks.Task RunSegmentRowCommandAsync(string id, Segment segment)
    {
        if (!CanRunRowCommand(id, segment)) return;
        switch (id) {
            case "segment-one-edit": await EditSegment(segment); break;
            case "segment-one-play":
                if (segment.IsDescription) Narration.IsOn = true;
                Seek(segment.Start); StartVideoPlayback(); break;
            case "segment-one-skip": StopProjectPlayback(); Seek(segment.Start); break;
            case "segment-one-preview": SegmentPreview_Click(new Button { Tag = segment }, new RoutedEventArgs()); break;
            case "segment-one-preview-stop": StopSegmentPreview(); break;
            case "segment-one-synthesize": await SynthesizeNarrationWithConfirmationAsync(new[] { "--segment", segment.Id }); break;
            case "segment-one-rewrite": await Model.RunAsync("rewrite-cue", new[] { "--segment", segment.Id }); break;
            case "segment-one-import-audio": await ImportSegmentNarrationAsync(segment); break;
            case "segment-one-delete-audio":
                if (await Confirm("Delete narration", "Remove this segment's saved audio?", "Delete")) {
                    if (!CanRunRowCommand(id, segment)) return;
                    StopSegmentPreview(); await Model.RunAsync("delete-segment-audio", new[] { "--segment", segment.Id });
                }
                break;
            case "segment-one-reanalyze":
                var path = Path.Combine(Path.GetTempPath(), "vidistudio-reanalyze-" + Guid.NewGuid() + ".json");
                try {
                    File.WriteAllText(path, System.Text.Json.JsonSerializer.Serialize(new { id = segment.Id, start = segment.Start, end = segment.End, text = segment.Text, hint = "" }));
                    await Model.RunAsync("reanalyze-transcript-segment", new[] { "--input", path });
                } finally { File.Delete(path); }
                break;
            case "segment-one-delete":
                if (Model.Project is not { } p || !await Confirm("Delete segment", "This text and associated narration are permanently removed. Existing exports are invalidated.", "Delete")) return;
                if (!CanRunRowCommand(id, segment) || Model.Project?.Id != p.Id) return;
                speech.Source = null;
                await Model.SaveSegmentsAsync(p.Transcript.Where(c => segment.IsDescription || c.Id != segment.Id), p.Cues.Where(c => !segment.IsDescription || c.Id != segment.Id));
                break;
        }
    }

    private MenuFlyout SegmentMenu(Segment? segment = null, bool moreActionsOnly = false)
    {
        var menu = new MenuFlyout();
        if (segment != null)
        {
            var actions = moreActionsOnly
                ? segment.IsDescription ? new[] { "segment-one-rewrite", "segment-one-synthesize", "segment-one-import-audio", "segment-one-delete-audio", "segment-one-delete" }
                    : new[] { "segment-one-edit", "segment-one-reanalyze", "segment-one-delete" }
                : new[] { "segment-one-edit", "segment-one-play", "segment-one-skip", "segment-one-preview", "segment-one-preview-stop", "segment-one-synthesize", "segment-one-rewrite", "segment-one-import-audio", "segment-one-delete-audio", "segment-one-reanalyze", "segment-one-delete" };
            foreach (var id in actions)
            {
                if (!RowCommandVisible(id, segment.IsDescription) || !SegmentBelongsToCurrentTab(segment) || id == "segment-one-preview-stop" && !ReferenceEquals(previewingSegment, segment)) continue;
                if (moreActionsOnly ? id == "segment-one-delete-audio" || id == "segment-one-delete" && !segment.IsDescription
                    : id is "segment-one-play" or "segment-one-preview" or "segment-one-synthesize" or "segment-one-delete-audio" or "segment-one-reanalyze" || id == "segment-one-delete" && !segment.IsDescription)
                    menu.Items.Add(new MenuFlyoutSeparator());
                var item = new MenuFlyoutItem { Text = SegmentCommandTitle(id, segment), Icon = DesktopCommandIcon(id, segment),
                    IsEnabled = CanRunRowCommand(id, segment), Tag = id };
                item.Click += async (_, _) => {
                    try { await RunSegmentRowCommandAsync(id, segment); }
                    catch (OperationCanceledException) { }
                    catch (Exception ex) { await ShowError(ex); }
                };
                menu.Items.Add(item);
            }
        }
        else
        {
            var sectionIndex = Section.SelectedIndex;
            var project = Model.Project;
            foreach (var id in new[] { "copy-to" }.Concat(EditorActionIds(sectionIndex, editMode, summaryEditMode)).Where(id => id == "-" || EditorCommandVisible(id)))
            {
                if (id == "-")
                {
                    menu.Items.Add(new MenuFlyoutSeparator());
                    continue;
                }
                var item = new MenuFlyoutItem
                {
                    Text = DesktopCommandTitle(id), Icon = DesktopCommandIcon(id),
                    IsEnabled = CanRunDesktopCommand(id), Tag = id
                };
                item.Click += async (_, _) =>
                {
                    // A flyout may outlive its tab, project, or editing mode.
                    if (Section.SelectedIndex != sectionIndex || Model.Project != project ||
                        !(id == "copy-to" || EditorActionIds(sectionIndex, editMode, summaryEditMode).Contains(id))) return;
                    try { await RunDesktopCommandAsync(id); }
                    catch (OperationCanceledException) { }
                    catch (Exception ex) { await ShowError(ex); }
                };
                menu.Items.Add(item);
            }
        }
        return menu;
    }
    private static string[] EditorActionIds(int sectionIndex, bool editing, bool editingSummary) => sectionIndex switch
    {
        0 when editing => new[] { "add", "segment-edit-selected", "segment-select-all", "segment-deselect-all", "segment-delete-selected", "undo", "redo" },
        1 when editingSummary => new[] { "segment-edit-mode" },
        1 => new[] { "segment-edit-mode", "generate-summary", "delete-current" },
        2 when editing => new[] { "add", "segment-edit-selected", "segment-select-all", "segment-deselect-all", "segment-delete-selected", "undo", "redo", "-", "synthesize-tts", "synthesize-changed" },
        3 => new[] { "synthesize-tts", "synthesize-changed" },
        4 when editing => new[] { "segment-edit-selected", "segment-select-all", "segment-deselect-all", "segment-delete-selected", "undo", "redo", "-", "synthesize-tts", "synthesize-changed" },
        _ => Array.Empty<string>()
    };
    private void SegmentMore_Click(object sender, RoutedEventArgs e)
    {
        if (Section.SelectedIndex is 0 or 2 or 4 && !editMode) return;
        if (sender is FrameworkElement element)
        {
            var menu = SegmentMenu(element.Tag as Segment, moreActionsOnly: true);
            if (menu.Items.Count > 0) menu.ShowAt(element);
        }
    }
    private void SegmentContext_Requested(UIElement sender, ContextRequestedEventArgs e)
    {
        if (!Model.Idle && !Model.IsSynthesizingNarration) return;
        var pointerRequest = e.TryGetPosition(sender, out var point);
        var element = (pointerRequest ? e.OriginalSource : FocusManager.GetFocusedElement(Root.XamlRoot)) as FrameworkElement;
        while (element != null && element.DataContext is not Segment) element = VisualTreeHelper.GetParent(element) as FrameworkElement;
        var segment = element?.DataContext as Segment ?? (!pointerRequest ? (sender as ListViewBase)?.SelectedItem as Segment : null);
        if (segment == null) return;
        var menu = SegmentMenu(segment);
        if (pointerRequest) menu.ShowAt((FrameworkElement)sender, point);
        else menu.ShowAt(element ?? (FrameworkElement)sender);
        e.Handled = true;
    }
    private void Track_SizeChanged(object sender, SizeChangedEventArgs e) => RenderTracks();
    private static Windows.UI.ViewManagement.UISettings? uiSettings;
    // Spoken transcript bars use the Windows accent color, matching the Mac
    // track's use of the macOS system accent. Description bars keep the
    // DarkGoldenrod color mirrored by the Mac description track.
    private static Windows.UI.Color SystemAccentColor()
    {
        try
        {
            uiSettings ??= new Windows.UI.ViewManagement.UISettings();
            return uiSettings.GetColorValue(Windows.UI.ViewManagement.UIColorType.Accent);
        }
        catch
        {
            return Microsoft.UI.Colors.SteelBlue;
        }
    }
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
        Draw(TranscriptTrack, project.Transcript, SystemAccentColor());
        Draw(DescriptionTrack, project.Cues, Microsoft.UI.Colors.DarkGoldenrod);
    }
    private void RenderMarkdown(string markdown)
    {
        ClearSummaryHighlight();
        SummaryRichText.Children.Clear();
        summaryLines.Clear();
        var view = BuildMarkdownView(markdown, trackReadback: true);
        foreach (var child in view.Children.ToList())
        {
            view.Children.Remove(child);
            SummaryRichText.Children.Add(child);
        }
        readback.SetSource(string.Join("\n", summaryLines.Select(line => line.Text)));
        UpdateSummaryControls();
    }
    private void UpdateCaptions(double? timestamp = null)
    {
        var project = Model.Project;
        var mode = Model.Settings.Get("captionDisplayMode", "off");
        UpdateCaptionOverlayButton();
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
