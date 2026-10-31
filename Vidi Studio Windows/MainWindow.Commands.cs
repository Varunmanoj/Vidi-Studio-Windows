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
        var navigating = state != Services.SummaryReadbackState.Preparing;
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
        var visible = ProjectSplitView.IsPaneOpen;
        var listLabel = visible ? "Hide project list" : "Show project list";
        ToolTipService.SetToolTip(EditorProjectListButton, listLabel);
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(EditorProjectListButton, listLabel);
        EditorProjectListIcon.Glyph = "\uE8F1";
    }
    private void ToggleInspector_Click(object sender, RoutedEventArgs e)
    {
        InspectorSplitView.IsPaneOpen = !InspectorSplitView.IsPaneOpen;
        UpdateEditorActionVisibility();
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
        if (!Model.Idle) return;
        var pointerRequest = e.TryGetPosition(sender, out var point);
        var element = (pointerRequest ? e.OriginalSource : FocusManager.GetFocusedElement(Root.XamlRoot)) as FrameworkElement;
        while (element != null && element.DataContext is not StudioProject) element = VisualTreeHelper.GetParent(element) as FrameworkElement;
        var project = element?.DataContext as StudioProject ?? (sender as ListViewBase)?.SelectedItem as StudioProject;
        if (project == null)
        {
            var emptyMenu = new MenuFlyout();
            var newVideoItem = new MenuFlyoutItem
            {
                Text = "New project from video…",
                Icon = Views.MenuIcons.Create("New project from video…"),
                IsEnabled = Model.Idle
            };
            newVideoItem.Click += ImportVideo_Click;
            emptyMenu.Items.Add(newVideoItem);

            var newYouTubeItem = new MenuFlyoutItem
            {
                Text = "New project from YouTube…",
                Icon = Views.MenuIcons.Create("New project from YouTube…"),
                IsEnabled = Model.Idle
            };
            newYouTubeItem.Click += YouTube_Click;
            emptyMenu.Items.Add(newYouTubeItem);

            if (pointerRequest) emptyMenu.ShowAt((FrameworkElement)sender, point);
            else emptyMenu.ShowAt((FrameworkElement)sender);
            e.Handled = true;
            return;
        }
        var menu = ProjectMenu(project);
        if (pointerRequest) menu.ShowAt((FrameworkElement)sender, point);
        else menu.ShowAt(element ?? (FrameworkElement)sender);
        e.Handled = true;
    }
    private MenuFlyout ProjectMenu(StudioProject project)
    {
        var menu = new MenuFlyout();
        void Item(string label, Action<object, RoutedEventArgs> handler, bool enabled = true, string? commandId = null)
        {
            var item = new MenuFlyoutItem { Text = label, Icon = Views.MenuIcons.Create(label), IsEnabled = Model.Idle && enabled };
            if (commandId != null)
            {
                item.Tag = commandId;
                Microsoft.UI.Xaml.Automation.AutomationProperties.SetAutomationId(item, commandId);
                if (commandId == "remove-from-list") ToolTipService.SetToolTip(item, "Remove this entry from the list. The project files stay in their folder. Use Open Project to add it again.");
            }
            item.Click += (s, args) => { if (Model.Idle) handler(s, args); };
            menu.Items.Add(item);
        }
        void LoadForEditorAction()
        {
            // Context actions must not navigate the window into the editor. Load the project only so
            // commands that need it can run, and leave the current page (gallery or editor) in place.
            if (Model.Project is not { } current || !string.Equals(current.Folder, project.Folder, StringComparison.OrdinalIgnoreCase))
                Model.Open(project.Folder);
        }
        var activeApiKey = Model.CanUseAI;
        var usable = !project.IntegrityPlaceholder && project.Integrity?.ManifestUnreadable != true && project.Integrity?.FolderMissing != true;
        if (desktopEditorVisible)
            Item("Open in This Window", async (_, _) =>
            {
                try { await SwitchProjectAsync(project); }
                catch (Exception ex) { await ShowError(ex); }
            }, commandId: "open-project-here");
        Item("Open in New Window", (_, _) => App.OpenWindow(project.Folder), commandId: "open-editor-window");
        Item("Export", (_, args) => { LoadForEditorAction(); Export_Click(this, args); }, activeApiKey && usable);
        Item("Open project folder", (_, _) => _ = RevealProjectAsync(project));
        if (Model.Settings.GetBool("developerMode"))
            Item("Open project configuration", (_, _) => _ = OpenProjectConfigurationAsync(project),
                File.Exists(Path.Combine(project.Folder, "project.json")), commandId: "open-configuration");
        foreach (var pair in new[] { ("Analyze video", "analyze"), ("Generate transcript", "transcribe"), ("Generate summary", "generate-summary"), ("Generate descriptions", "generate-ad-cues") })
            Item(pair.Item1, (_, args) => { LoadForEditorAction(); Command_Click(new Button { Tag = pair.Item2 }, args); }, activeApiKey && usable);
        Item("Refresh video details", (_, args) => { LoadForEditorAction(); Command_Click(new Button { Tag = "prepare-video" }, args); }, usable);
        Item("Rename", (_, _) => _ = RenameProjectAsync(project), usable);
        Item("Remove from List", (_, _) => RemoveProjectFromList(project), commandId: "remove-from-list");
        menu.Items.Add(new MenuFlyoutSeparator());
        Item(project.Integrity?.IsDamaged == true ? "Delete damaged project…" : "Move to Recycle Bin", (_, _) => _ = DeleteProjectAsync(project), commandId: "trash-project");
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
                var canSynthesize = Model.CurrentNarrationEngine() != "gemini" || Model.CanUseAI;
                var synthLabel = segment.IsNarrating
                    ? $"Generating audio for AD segment {segment.AdNumber}…"
                    : segment.IsQueuedForNarration
                        ? $"Queued for audio generation (AD {segment.AdNumber})…"
                        : (segment.Audio == null ? "Synthesize this segment" : "Regenerate segment narration");
                Item("Preview narration", PreviewNarration_Click, segment.AudioReady);
                Item(synthLabel, NarrateSelected_Click, canSynthesize);
                if (Model.IsSynthesizingNarration && !segment.IsNarrating && Model.NarratingSegmentIndex is { } activeIdx)
                    Item($"Currently generating AD segment {activeIdx}…", (_, _) => { }, false);
                Item("Import narration audio…", async (_, _) => await ImportSegmentNarrationAsync(segment), Model.Idle);
                Item("Rewrite description to fit", Rewrite_Click, Model.CanUseAI);
                Item("Delete narration", DeleteNarration_Click, segment.Audio != null);
            }
            else
            {
                Item("Re-Analyze this segment", async (_, _) => await EditSegment(segment), Model.CanUseAI);
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
            var sectionIndex = Section.SelectedIndex;
            var project = Model.Project;
            foreach (var id in EditorActionIds(sectionIndex, editMode, summaryEditMode))
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
                        !EditorActionIds(sectionIndex, editMode, summaryEditMode).Contains(id)) return;
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
            var menu = SegmentMenu(element.Tag as Segment);
            if (menu.Items.Count > 0) menu.ShowAt(element);
        }
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
