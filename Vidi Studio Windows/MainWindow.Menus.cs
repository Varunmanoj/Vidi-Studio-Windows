using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Vidi_Studio_Windows.Services;
using Windows.System;

namespace Vidi_Studio_Windows;

public sealed partial class MainWindow
{
    // Stable IDs mirror StudioCommands / StudioShortcut on Mac. The parity
    // checker compares these menu trees with the actual Swift declarations.
    private readonly Dictionary<string, List<MenuFlyoutItem>> desktopMenuItems = new();
    private readonly List<(string Name, MenuFlyoutSubItem Item)> desktopMenuGroups = new();
    private bool desktopEditorVisible;
    private bool? desktopPlaybackWasPlaying;
    internal IEnumerable<(string Label, string Shortcut)> DesktopShortcutReference =>
        DesktopShortcuts.Select(entry => (DesktopCommandTitle(entry.Key), entry.Value.Text));
    /// <summary>Current window appearance, shared with companion windows such as Help.</summary>
    internal ElementTheme CurrentTheme => Root.ActualTheme;

    private void BuildMenus()
    {
        MenuFlyoutItem Command(string id)
        {
            var item = new MenuFlyoutItem { Tag = id, Text = DesktopCommandTitle(id), Icon = DesktopCommandIcon(id) };
            if (DesktopShortcuts.TryGetValue(id, out var shortcut)) item.KeyboardAcceleratorTextOverride = shortcut.Text;
            item.Click += async (_, _) =>
            {
                if (!CanRunDesktopCommand(id)) return;
                try { await RunDesktopCommandAsync(id); }
                catch (OperationCanceledException) { }
                catch (Exception ex) { await ShowError(ex); }
            };
            if (!desktopMenuItems.TryGetValue(id, out var items)) desktopMenuItems[id] = items = new();
            items.Add(item);
            return item;
        }
        static MenuFlyoutSeparator Separator() => new();
        MenuFlyoutSubItem Submenu(string title, params MenuFlyoutItemBase[] entries)
        {
            var group = new MenuFlyoutSubItem { Text = title, Icon = DesktopCommandIcon(title) };
            foreach (var entry in entries) group.Items.Add(entry);
            desktopMenuGroups.Add((title, group));
            return group;
        }
        MenuBarItem Menu(string title, params MenuFlyoutItemBase[] entries)
        {
            var menu = new MenuBarItem { Title = title };
            foreach (var entry in entries) menu.Items.Add(entry);
            menu.PointerEntered += (_, _) => UpdateDesktopMenuStates();
            menu.GettingFocus += (_, _) => UpdateDesktopMenuStates();
            return menu;
        }
        MenuBarItem[] Layout() => new[]
        {
            Menu("File",
                Command("new-project"), Command("open-project"), Separator(), Command("export"), Separator(),
                Command("remove-from-list"), Separator(), Command("open-editor-window"), Command("settings"), Command("close-window")),
            Menu("View",
                Command("preview"), Command("fullscreen"), Command("toggle-timeline"), Command("filter-segments"),
                Submenu("Sort Segments", Command("sort-start"), Command("sort-duration-asc"), Command("sort-duration-desc"), Command("sort-origin")), Separator(),
                Command("show-projects"), Command("show-inspector"), Separator(),
                Command("project-list"), Command("project-grid")),
            Menu("Playback",
                Command("preview"), Command("fullscreen"), Command("play"), Command("stop"),
                Command("back"), Command("forward"), Command("narration"), Separator(),
                Submenu("Transcript Overlay", Command("caption-cycle"), Separator(),
                    Command("caption-off"), Command("caption-transcript"), Command("caption-ad-transcript")), Separator(),
                Submenu("Summary Reading", Command("summary-play"), Command("summary-pause"), Command("summary-stop"), Separator(),
                    Command("summary-previous-sentence"), Command("summary-next-sentence"),
                    Command("summary-previous-paragraph"), Command("summary-next-paragraph"), Separator(),
                    Command("summary-from-start"), Command("summary-from-selection"), Command("summary-read-selection"))),
            Menu("Project",
                Submenu("Editor Sections", Command("transcript"), Command("summary"), Command("descriptions"), Command("ad-overview"), Command("ad-transcript")),
                Submenu("Segments", Command("segment-edit-mode"), Command("add"), Separator(), Command("reanalyze-selection"),
                    Command("segment-select-all"), Command("segment-deselect-all"), Command("segment-edit-selected"), Command("segment-delete-selected"), Separator(),
                    Command("follow"), Command("undo"), Command("redo")),
                Submenu("Analysis", Command("analyze"), Separator(), Command("prepare-video"), Command("transcribe"), Command("generate-summary")),
                Submenu("Narration",
                    Submenu("AD Segment Generation", Command("generate-ad-cues"), Separator(), Command("delete-descriptions")),
                    Submenu("Narration Audio", Command("synthesize-tts"), Command("synthesize-changed"), Command("review-narration-issues"), Command("review-narration-voices"), Separator(), Command("delete-narration"))),
                Command("delete-current"), Separator(),
                Submenu("Import", Command("import-transcript"), Command("import-summary"), Command("import-descriptions"), Separator(), Command("import-assets")),
                Command("show-in-finder"), Command("open-configuration"),
                Separator(), Command("rename-project"), Command("trash-project")),
            Menu("Help", Command("help"), Command("about"))
        };
        galleryMenus.AddRange(Layout());
        editorMenus.AddRange(Layout());
        videoPlayMenuItem = desktopMenuItems["play"].Last();
        foreach (var (id, shortcut) in DesktopShortcuts)
            AddShortcut(shortcut.Key, shortcut.Modifiers, async (_, _) =>
            {
                if (!CanRunDesktopCommand(id)) return;
                try { await RunDesktopCommandAsync(id); }
                catch (OperationCanceledException) { }
                catch (Exception ex) { await ShowError(ex); }
            }, () => CanRunDesktopCommand(id));
        AddShortcut(VirtualKey.F11, VirtualKeyModifiers.None, (_, _) => FullScreen_Click(this, new RoutedEventArgs()));
        GalleryGrid.SelectionChanged += (_, _) => UpdateDesktopMenuStates();
        GalleryList.SelectionChanged += (_, _) => UpdateDesktopMenuStates();
        ProjectList.SelectionChanged += (_, _) => { UpdateDesktopMenuStates(); UpdateEditorProjectButtons(); };
        ProjectGrid.SelectionChanged += (_, _) => { UpdateDesktopMenuStates(); UpdateEditorProjectButtons(); };
        Segments.SelectionChanged += (_, _) => { UpdateDesktopMenuStates(); UpdateBulkActionState(); };
        Narration.Toggled += (_, _) => UpdateDesktopMenuStates();
        Follow.Toggled += (_, _) => UpdateDesktopMenuStates();
        UpdateMenuBar(false);
    }

    // Command -> Ctrl; Option -> Alt. Mac's extra Control modifier and the
    // Windows-reserved Ctrl+Alt+Delete need the documented native substitutions.
    private static readonly Dictionary<string, (VirtualKey Key, VirtualKeyModifiers Modifiers, string Text)> DesktopShortcuts = new()
    {
        ["new-project"] = (VirtualKey.N, VirtualKeyModifiers.Control, "Ctrl+N"),
        ["open-project"] = (VirtualKey.O, VirtualKeyModifiers.Control | VirtualKeyModifiers.Shift, "Ctrl+Shift+O"),
        ["export"] = (VirtualKey.E, VirtualKeyModifiers.Control | VirtualKeyModifiers.Shift, "Ctrl+Shift+E"),
        ["settings"] = ((VirtualKey)188, VirtualKeyModifiers.Control, "Ctrl+,"),
        ["close-window"] = (VirtualKey.W, VirtualKeyModifiers.Control, "Ctrl+W"),
        ["help"] = (VirtualKey.F1, VirtualKeyModifiers.None, "F1"),
        ["preview"] = (VirtualKey.V, VirtualKeyModifiers.Control | VirtualKeyModifiers.Shift, "Ctrl+Shift+V"),
        ["show-projects"] = (VirtualKey.B, VirtualKeyModifiers.Control, "Ctrl+B"),
        ["show-inspector"] = (VirtualKey.B, VirtualKeyModifiers.Control | VirtualKeyModifiers.Shift, "Ctrl+Shift+B"),
        ["project-list"] = (VirtualKey.Number1, VirtualKeyModifiers.Control | VirtualKeyModifiers.Menu, "Ctrl+Alt+1"),
        ["project-grid"] = (VirtualKey.Number2, VirtualKeyModifiers.Control | VirtualKeyModifiers.Menu, "Ctrl+Alt+2"),
        ["fullscreen"] = (VirtualKey.F, VirtualKeyModifiers.Control | VirtualKeyModifiers.Menu, "Ctrl+Alt+F"),
        ["play"] = (VirtualKey.Space, VirtualKeyModifiers.Control | VirtualKeyModifiers.Shift, "Ctrl+Shift+Space"),
        ["stop"] = ((VirtualKey)190, VirtualKeyModifiers.Control | VirtualKeyModifiers.Shift, "Ctrl+Shift+Period"),
        ["back"] = (VirtualKey.Left, VirtualKeyModifiers.Control | VirtualKeyModifiers.Menu, "Ctrl+Alt+Left"),
        ["forward"] = (VirtualKey.Right, VirtualKeyModifiers.Control | VirtualKeyModifiers.Menu, "Ctrl+Alt+Right"),
        ["narration"] = (VirtualKey.D, VirtualKeyModifiers.Control | VirtualKeyModifiers.Shift, "Ctrl+Shift+D"),
        ["caption-cycle"] = (VirtualKey.C, VirtualKeyModifiers.Control | VirtualKeyModifiers.Shift, "Ctrl+Shift+C"),
        ["caption-off"] = (VirtualKey.Number0, VirtualKeyModifiers.Control | VirtualKeyModifiers.Menu | VirtualKeyModifiers.Shift, "Ctrl+Alt+Shift+0"),
        ["caption-transcript"] = (VirtualKey.Number1, VirtualKeyModifiers.Control | VirtualKeyModifiers.Menu | VirtualKeyModifiers.Shift, "Ctrl+Alt+Shift+1"),
        ["caption-ad-transcript"] = (VirtualKey.Number2, VirtualKeyModifiers.Control | VirtualKeyModifiers.Menu | VirtualKeyModifiers.Shift, "Ctrl+Alt+Shift+2"),
        ["transcript"] = (VirtualKey.Number1, VirtualKeyModifiers.Control, "Ctrl+1"),
        ["summary"] = (VirtualKey.Number2, VirtualKeyModifiers.Control, "Ctrl+2"),
        ["descriptions"] = (VirtualKey.Number3, VirtualKeyModifiers.Control, "Ctrl+3"),
        ["ad-overview"] = (VirtualKey.Number4, VirtualKeyModifiers.Control, "Ctrl+4"),
        ["ad-transcript"] = (VirtualKey.Number5, VirtualKeyModifiers.Control, "Ctrl+5"),
        ["segment-edit-mode"] = (VirtualKey.E, VirtualKeyModifiers.Control | VirtualKeyModifiers.Menu, "Ctrl+Alt+E"),
        ["reanalyze-selection"] = (VirtualKey.R, VirtualKeyModifiers.Control | VirtualKeyModifiers.Menu, "Ctrl+Alt+R"),
        ["add"] = (VirtualKey.N, VirtualKeyModifiers.Control | VirtualKeyModifiers.Shift, "Ctrl+Shift+N"),
        ["segment-select-all"] = (VirtualKey.A, VirtualKeyModifiers.Control | VirtualKeyModifiers.Menu, "Ctrl+Alt+A"),
        ["segment-deselect-all"] = (VirtualKey.U, VirtualKeyModifiers.Control | VirtualKeyModifiers.Menu | VirtualKeyModifiers.Shift, "Ctrl+Alt+Shift+U"),
        ["segment-edit-selected"] = (VirtualKey.Enter, VirtualKeyModifiers.Control | VirtualKeyModifiers.Menu, "Ctrl+Alt+Enter"),
        ["segment-delete-selected"] = (VirtualKey.Delete, VirtualKeyModifiers.Control | VirtualKeyModifiers.Shift, "Ctrl+Shift+Delete"),
        ["follow"] = (VirtualKey.F, VirtualKeyModifiers.Control | VirtualKeyModifiers.Shift, "Ctrl+Shift+F"),
        ["delete-narration"] = (VirtualKey.Back, VirtualKeyModifiers.Control | VirtualKeyModifiers.Menu, "Ctrl+Alt+Backspace"),
        ["summary-play"] = (VirtualKey.P, VirtualKeyModifiers.Control | VirtualKeyModifiers.Shift, "Ctrl+Shift+P"),
        ["summary-pause"] = (VirtualKey.P, VirtualKeyModifiers.Control | VirtualKeyModifiers.Menu, "Ctrl+Alt+P"),
        ["summary-stop"] = (VirtualKey.S, VirtualKeyModifiers.Control | VirtualKeyModifiers.Shift, "Ctrl+Shift+S"),
        ["summary-previous-sentence"] = ((VirtualKey)219, VirtualKeyModifiers.Control | VirtualKeyModifiers.Shift, "Ctrl+Shift+["),
        ["summary-next-sentence"] = ((VirtualKey)221, VirtualKeyModifiers.Control | VirtualKeyModifiers.Shift, "Ctrl+Shift+]"),
        ["summary-previous-paragraph"] = ((VirtualKey)219, VirtualKeyModifiers.Control | VirtualKeyModifiers.Menu, "Ctrl+Alt+["),
        ["summary-next-paragraph"] = ((VirtualKey)221, VirtualKeyModifiers.Control | VirtualKeyModifiers.Menu, "Ctrl+Alt+]"),
        ["undo"] = (VirtualKey.Z, VirtualKeyModifiers.Control | VirtualKeyModifiers.Menu, "Ctrl+Alt+Z"),
        ["redo"] = (VirtualKey.Z, VirtualKeyModifiers.Control | VirtualKeyModifiers.Menu | VirtualKeyModifiers.Shift, "Ctrl+Alt+Shift+Z"),
        ["analyze"] = (VirtualKey.A, VirtualKeyModifiers.Control | VirtualKeyModifiers.Menu | VirtualKeyModifiers.Shift, "Ctrl+Alt+Shift+A"),
        ["transcribe"] = (VirtualKey.T, VirtualKeyModifiers.Control | VirtualKeyModifiers.Menu | VirtualKeyModifiers.Shift, "Ctrl+Alt+Shift+T"),
        ["generate-summary"] = (VirtualKey.S, VirtualKeyModifiers.Control | VirtualKeyModifiers.Menu | VirtualKeyModifiers.Shift, "Ctrl+Alt+Shift+S"),
        ["generate-ad-cues"] = (VirtualKey.D, VirtualKeyModifiers.Control | VirtualKeyModifiers.Menu | VirtualKeyModifiers.Shift, "Ctrl+Alt+Shift+D"),
        ["import-transcript"] = (VirtualKey.I, VirtualKeyModifiers.Control | VirtualKeyModifiers.Menu, "Ctrl+Alt+I"),
        ["import-summary"] = (VirtualKey.M, VirtualKeyModifiers.Control | VirtualKeyModifiers.Menu, "Ctrl+Alt+M"),
        ["import-descriptions"] = (VirtualKey.D, VirtualKeyModifiers.Control | VirtualKeyModifiers.Menu, "Ctrl+Alt+D"),
        ["import-assets"] = (VirtualKey.I, VirtualKeyModifiers.Control | VirtualKeyModifiers.Menu | VirtualKeyModifiers.Shift, "Ctrl+Alt+Shift+I")
    };

    private void UpdateMenuBar(bool editor)
    {
        desktopEditorVisible = editor;
        MainMenu.Items.Clear();
        foreach (var menu in editor ? editorMenus : galleryMenus) MainMenu.Items.Add(menu);
        UpdateDesktopMenuStates();
    }

    private void UpdateMenuKeyStates(bool hasKey) => UpdateDesktopMenuStates();

    private void UpdateDesktopPlaybackState()
    {
        var playing = IsVideoPlaying();
        if (desktopPlaybackWasPlaying == playing) return;
        desktopPlaybackWasPlaying = playing;
        if (desktopMenuItems.TryGetValue("play", out var items))
            foreach (var item in items) item.Icon = DesktopCommandIcon("play");
    }

    private void UpdateDesktopMenuStates()
    {
        foreach (var (id, items) in desktopMenuItems)
            foreach (var item in items)
            {
                item.Text = DesktopCommandTitle(id);
                item.Icon = DesktopCommandIcon(id);
                item.IsEnabled = CanRunDesktopCommand(id);
            }
        if (RenameProjectTitleButton != null)
        {
            RenameProjectTitleButton.Visibility = desktopEditorVisible ? Visibility.Visible : Visibility.Collapsed;
            RenameProjectTitleButton.IsEnabled = CanRunDesktopCommand("rename-project");
        }
        var timedSection = Section.SelectedIndex is 0 or 2 or 4;
        foreach (var item in desktopMenuItems.TryGetValue("filter-segments", out var filterItems) ? filterItems : new List<MenuFlyoutItem>())
            item.Visibility = timedSection ? Visibility.Visible : Visibility.Collapsed;
        foreach (var (name, group) in desktopMenuGroups)
        {
            group.IsEnabled = desktopEditorVisible && Model.Project != null && (Model.Idle || ((name is "Narration" or "Narration Audio") && Model.IsSynthesizingNarration));
            if (name == "Summary Reading") group.IsEnabled &= Section.SelectedIndex == 1 && !summaryEditMode;
            if (name == "Sort Segments")
            {
                group.Text = Section.SelectedIndex switch
                {
                    0 => "Sort Transcripts…", 2 => "Sort Descriptions…", 4 => "Sort AD Transcript…", _ => "Sort Segments"
                };
                group.Visibility = timedSection && desktopEditorVisible ? Visibility.Visible : Visibility.Collapsed;
            }
            if (name == "Transcript Overlay")
            {
                var mode = Model.Settings.Get("captionDisplayMode", "off");
                group.Text = "Transcript Overlay: " + (mode == "combined" ? "AD Transcript" : mode == "transcript" ? "Transcript" : mode == "descriptions" ? "Descriptions" : "Off");
                group.Visibility = desktopEditorVisible && (Section.SelectedIndex is 0 or 2 or 4) ? Visibility.Visible : Visibility.Collapsed;
            }
        }
        // Mac inserts the overlay separator only on timed sections. Hide its
        // Windows counterpart with the submenu rather than leaving two dividers.
        foreach (var menu in galleryMenus.Concat(editorMenus).Where(menu => menu.Title == "Playback"))
        {
            var overlay = menu.Items.OfType<MenuFlyoutSubItem>().First(item => item.Text.StartsWith("Transcript Overlay:", StringComparison.Ordinal));
            var index = menu.Items.IndexOf(overlay);
            if (index > 0 && menu.Items[index - 1] is MenuFlyoutSeparator separator)
                separator.Visibility = overlay.Visibility;
        }
    }

    private static bool ExtraFlag(Models.StudioProject project, string key) =>
        project.Extra is { } extra && extra.TryGetValue(key, out var value) && value.ValueKind == JsonValueKind.True;
    private bool HasHistory(string key) => Model.Project?.Extra is { } extra && extra.TryGetValue(key, out var value)
        && value.ValueKind == JsonValueKind.Array && value.GetArrayLength() > 0;

    private Models.StudioProject? SelectedGalleryProject()
    {
        if (desktopEditorVisible || GalleryPanel.Visibility != Visibility.Visible) return null;
        var selected = (GalleryGrid.Visibility == Visibility.Visible ? GalleryGrid.SelectedItem : GalleryList.SelectedItem) as Models.StudioProject;
        return selected != null && Model.Projects.Contains(selected) ? selected : null;
    }

    private Models.StudioProject? SelectedBrowserProject()
    {
        if (!desktopEditorVisible) return SelectedGalleryProject();
        if (!ProjectSplitView.IsPaneOpen) return null;
        var selected = (ProjectGrid.Visibility == Visibility.Visible ? ProjectGrid.SelectedItem : ProjectList.SelectedItem) as Models.StudioProject;
        return selected != null && Model.Projects.Contains(selected) ? selected : null;
    }

    private Models.StudioProject? ProjectManagementTarget() => desktopEditorVisible ? Model.Project : SelectedGalleryProject();

    private bool CanManageProject(string id)
    {
        if (!Model.Idle || ProjectManagementTarget() is not { } project) return false;
        return id switch
        {
            "rename-project" => !project.IntegrityPlaceholder && project.Integrity?.ManifestUnreadable != true && project.Integrity?.FolderMissing != true,
            "trash-project" => true,
            "show-in-finder" => Directory.Exists(project.Folder),
            "open-configuration" => File.Exists(Path.Combine(project.Folder, "project.json")),
            _ => false
        };
    }

    private bool CanRunDesktopCommand(string id)
    {
        if (!Model.Idle) return false;
        if (id == "new-project") return true;
        if (id is "rename-project" or "trash-project" or "show-in-finder" or "open-configuration") return CanManageProject(id);
        if (id == "remove-from-list") return SelectedBrowserProject() != null;
        if (id is "open-project" or "settings" or "help" or "about" or "close-window") return true;
        if (id is "project-list" or "project-grid") return !(Section.SelectedIndex == 1 && summaryEditMode);
        if (!desktopEditorVisible || Model.Project is not { } p) return false;
        var timed = Section.SelectedIndex is 0 or 2 or 4;
        var hasTranscript = p.HasTranscript || ExtraFlag(p, "transcriptGenerated");
        var canDescribe = hasTranscript && p.HasSummary;
        var ai = Model.CanUseAI;
        var tts = Model.Settings.Get("ttsMode", "windows-core") != "gemini" || ai;
        switch (id)
        {
            case "rename-project": return !p.IntegrityPlaceholder && p.Integrity?.ManifestUnreadable != true && p.Integrity?.FolderMissing != true;
            case "import-assets":
            case "import-transcript":
            case "import-summary":
            case "import-descriptions":
                return !p.IntegrityPlaceholder && p.Integrity?.ManifestUnreadable != true && p.Integrity?.FolderMissing != true;
            case "trash-project": return true;
            case "segment-edit-mode": return timed && Segments.Items.Count > 0 || Section.SelectedIndex == 1;
            case "add": return Section.SelectedIndex is 0 or 2;
            case "reanalyze-selection": return ai && (Section.SelectedIndex == 0 || Section.SelectedIndex == 2 && canDescribe);
            case "segment-select-all": return timed && editMode && Segments.Items.Count > Segments.SelectedItems.Count;
            case "segment-deselect-all": case "segment-edit-selected": case "segment-delete-selected": return timed && editMode && Segments.SelectedItems.Count > 0;
            case "undo": return HasHistory(Section.SelectedIndex == 0 ? "transcriptUndoStack" : "undoStack");
            case "redo": return HasHistory(Section.SelectedIndex == 0 ? "transcriptRedoStack" : "redoStack");
            case "narration": return (Section.SelectedIndex is 2 or 4) && p.Cues.Count > 0;
            case "follow": return timed && Segments.Items.Count > 0;
            case "caption-cycle": case "caption-ad-transcript": return timed && (p.HasTranscript || p.Cues.Count > 0);
            case "caption-transcript": return timed && p.HasTranscript;
            case "filter-segments":
            case "sort-start":
            case "sort-duration-asc":
            case "sort-duration-desc":
            case "sort-origin": return timed;
            case "caption-off": return timed;
            case "analyze": case "transcribe": case "generate-summary": return ai;
            case "generate-ad-cues": return ai && canDescribe;
            case "synthesize-tts": return tts && p.Cues.Count > 0;
            case "synthesize-changed": return tts && p.CanGenerateMissingOrChangedAudio;
            case "delete-narration": return p.Cues.Any(c => c.Audio != null);
            case "delete-descriptions": return p.Cues.Count > 0 || ExtraFlag(p, "descriptionsGenerated");
            case "delete-current": return Section.SelectedIndex switch
                { 0 => hasTranscript, 1 => p.HasSummary || ExtraFlag(p, "summaryGenerated"), 2 => p.Cues.Count > 0 || ExtraFlag(p, "descriptionsGenerated"), _ => false };
            case "fullscreen": return !p.IsYouTube;
        }
        if (id.StartsWith("summary-", StringComparison.Ordinal))
        {
            if (Section.SelectedIndex != 1 || summaryEditMode || !p.HasSummary) return false;
            return id switch
            {
                "summary-play" => readback.State is SummaryReadbackState.Stopped or SummaryReadbackState.Paused,
                "summary-pause" => readback.State == SummaryReadbackState.Playing,
                "summary-stop" => readback.State != SummaryReadbackState.Stopped,
                "summary-from-selection" or "summary-read-selection" => SummarySelection().Selected.Length > 0,
                "summary-previous-sentence" => readback.State != SummaryReadbackState.Preparing && readback.CanGoPreviousSentence,
                "summary-next-sentence" => readback.State != SummaryReadbackState.Preparing && readback.CanGoNextSentence,
                "summary-previous-paragraph" => readback.State != SummaryReadbackState.Preparing && readback.CanGoPreviousParagraph,
                "summary-next-paragraph" => readback.State != SummaryReadbackState.Preparing && readback.CanGoNextParagraph,
                _ => true
            };
        }
        return true;
    }

    private string DesktopCommandTitle(string id)
    {
        var p = Model.Project;
        return id switch
        {
            "remove-from-list" => "Remove from List",
            "new-project" => "New Project…", "open-project" => "Open Project Folder…", "export" => "Export…",
            "open-editor-window" => "Open Editor in New Window", "settings" => "Settings…", "close-window" => "Close Window",
            "help" => "VidiStudio Help", "about" => "About VidiStudio",
            "preview" => PreviewPanel.Visibility == Visibility.Visible ? "Hide Preview" : "Show Preview",
            "toggle-timeline" => TimelinePanel.Visibility == Visibility.Visible ? "Hide Timeline" : "Show Timeline",
            "show-projects" => ProjectSplitView.IsPaneOpen ? "Hide Project List" : "Show Project List",
            "show-inspector" => InspectorSplitView.IsPaneOpen ? "Hide Inspector" : "Show Inspector",
            "project-list" => "Project List View", "project-grid" => "Project Grid View",
            "fullscreen" => fullScreenVideo ? "Exit Full-Screen Video" : "Full-Screen Video", "play" => "Play or Pause", "stop" => "Stop Video",
            "back" => "Rewind 5 Seconds", "forward" => "Fast Forward 5 Seconds", "narration" => "Toggle Narration",
            "caption-cycle" => "Cycle Transcript Overlay", "caption-off" => "Turn Transcript Overlay Off",
            "caption-transcript" => "Show Transcript Overlay", "caption-ad-transcript" => "Show AD Transcript Overlay",
            "summary-play" => "Play or Resume Summary Reading", "summary-pause" => "Pause Summary Reading", "summary-stop" => "Stop Summary Reading",
            "summary-previous-sentence" => "Previous Summary Sentence", "summary-next-sentence" => "Next Summary Sentence",
            "summary-previous-paragraph" => "Previous Summary Paragraph", "summary-next-paragraph" => "Next Summary Paragraph",
            "summary-from-start" => "Read Summary from Start", "summary-from-selection" => "Read Summary from Selection", "summary-read-selection" => "Read Selected Summary Text",
            "transcript" => "Show Transcript", "summary" => "Show Summary", "descriptions" => "Show Descriptions",
            "ad-overview" => "Show AD Overview", "ad-transcript" => "Show AD Transcript",
            "segment-edit-mode" => Section.SelectedIndex == 1 ? (summaryEditMode ? "Done Editing Summary" : "Edit Summary")
                : editMode ? "Done Editing Segments" : Section.SelectedIndex switch
                    { 0 => "Edit Transcript Segments", 2 => "Edit Audio Description Segments", 4 => "Edit AD Transcript Segments", _ => "Edit Segments" },
            "add" => Section.SelectedIndex switch { 0 => "Add Transcript Segment", 2 => "Add Audio Description Segment", _ => "Add Segment" },
            "reanalyze-selection" => "Re-Analyze a Video Section", "segment-select-all" => "Select All Segments", "segment-deselect-all" => "Deselect All Segments",
            "segment-edit-selected" => "Edit Selected Segments…", "segment-delete-selected" => "Delete Selected Segments…",
            "follow" => "Toggle Transcript Follow", "undo" => "Undo Segment Edit", "redo" => "Redo Segment Edit", "analyze" => "Analyze Video…",
            "prepare-video" => p?.Thumbnails.Count > 0 ? "Regenerate Video Details & Timeline Pictures…" : "Generate Video Details & Timeline Pictures",
            "transcribe" => p != null && (p.HasTranscript || ExtraFlag(p, "transcriptGenerated")) ? "Regenerate Transcript…" : "Generate Transcript",
            "generate-summary" => p != null && (p.HasSummary || ExtraFlag(p, "summaryGenerated")) ? "Regenerate Summary…" : "Generate Summary",
            "generate-ad-cues" => p != null && (p.Cues.Count > 0 || ExtraFlag(p, "descriptionsGenerated")) ? "Regenerate Audio Descriptions…" : "Generate Audio Descriptions",
            "synthesize-tts" => Model.IsSynthesizingNarration
                ? (Model.NarratingSegmentIndex is { } idx && p != null ? $"Synthesizing AD Segment {idx} of {p.Cues.Count}…" : "Synthesizing Narration…")
                : p?.AllADAudioTitle ?? "Generate All AD Audio",
            "synthesize-changed" => "Generate Missing or Changed AD Audio",
            "review-narration-issues" => "Review Narration Issues…", "delete-narration" => "Delete All Narration Audio…",
            "review-narration-voices" => "Review Narration Voices…",
            "delete-descriptions" => "Delete Descriptions…",
            "delete-current" => Section.SelectedIndex switch { 0 => "Delete Transcript…", 1 => "Delete Summary…", 2 => "Delete Descriptions…", _ => "Delete Current Page…" },
            "import-transcript" => "Import Transcript…", "import-summary" => "Import Summary…",
            "import-descriptions" => "Import Descriptions…", "import-assets" => "Import Existing Assets…",
            "filter-segments" => Section.SelectedIndex switch
            {
                0 => "Filter Transcript…", 2 => "Filter Descriptions…", 4 => "Filter AD Transcript…", _ => "Filter Segments…"
            },
            "sort-start" => "Start Time",
            "sort-duration-asc" => "Duration — Shortest First",
            "sort-duration-desc" => "Duration — Longest First",
            "sort-origin" => "Origin",
            "show-in-finder" => "Show Project in File Explorer", "open-configuration" => "Open Project Configuration",
            "rename-project" => "Rename Project…", "trash-project" => "Move Project to Recycle Bin…", _ => id
        };
    }

    private IconElement DesktopCommandIcon(string id)
    {
        if (id == "play") return new FontIcon { Glyph = IsVideoPlaying() ? "\uE769" : "\uE768" };
        if (id == "fullscreen") return new FontIcon { Glyph = fullScreenVideo ? "\uE73F" : "\uE740" };
        if (id is "synthesize-tts" or "Narration" && Model.IsSynthesizingNarration)
            return Views.MenuIcons.Create("Preparing summary…");
        var checkedItem = id switch
        {
            "narration" => Narration.IsOn, "follow" => Follow.IsOn,
            "caption-off" => Model.Settings.Get("captionDisplayMode", "off") == "off",
            "caption-transcript" => Model.Settings.Get("captionDisplayMode") == "transcript",
            "caption-ad-transcript" => Model.Settings.Get("captionDisplayMode") == "combined",
            "sort-start" => segmentSort == "start",
            "sort-duration-asc" => segmentSort == "durationAscending",
            "sort-duration-desc" => segmentSort == "durationDescending",
            "sort-origin" => segmentSort == "origin", _ => false
        };
        return checkedItem ? new FontIcon { Glyph = "\uE73E" } : Views.MenuIcons.Create(id switch
        {
            "stop" or "summary-stop" => "Stop summary",
            "preview" or "fullscreen" => "Show / hide preview", "toggle-timeline" => "Show / hide timeline",
            "show-projects" => "Show / hide projects", "show-inspector" => "Show / hide inspector",
            "project-list" or "project-grid" or "Editor Sections" => "List / Grid projects",
            "new-project" or "add" => "New project", "open-project" or "show-in-finder" => "Open project folder",
            "Import" => "Import", "import-assets" => "Import existing assets", "import-transcript" => "Import transcript",
            "import-summary" => "Import summary", "import-descriptions" => "Import descriptions", "filter-segments" => "Filter segments",
            "Sort Segments" => "Sort segments",
            "sort-start" => "Sort by start time", "sort-duration-asc" => "Shortest duration first",
            "sort-duration-desc" => "Longest duration first", "sort-origin" => "Sort by origin",
            "open-configuration" => "Open project configuration",
            "remove-from-list" => "Remove from List",
            "rename-project" => "Rename", "trash-project" => "Move to Recycle Bin",
            "open-editor-window" => "Open editor in new window", "export" => "Export", "settings" => "Settings",
            "close-window" => "Close window", "help" => "Help and keyboard shortcuts", "about" => "About Vidi Studio",
            "back" => "Rewind five seconds", "forward" => "Forward five seconds", "follow" => "Follow active segment",
            "summary-play" or "summary-from-start" or "summary-from-selection" or "summary-read-selection" => "Play summary",
            "summary-pause" => "Pause summary",
            "summary-previous-sentence" => "Previous summary sentence", "summary-next-sentence" => "Next summary sentence",
            "summary-previous-paragraph" => "Previous summary paragraph", "summary-next-paragraph" => "Next summary paragraph",
            "Summary Reading" or "Narration" or "Narration Audio" or "narration" or "synthesize-tts" or "synthesize-changed" => "Narration",
            "AD Segment Generation" => "Generate descriptions", "delete-descriptions" => "Delete selected",
            "review-narration-issues" => "Review Narration Issues…", "delete-narration" or "delete-current" or "segment-delete-selected" => "Delete narration",
            "review-narration-voices" => "Review Narration Voices…",
            "segment-edit-mode" or "segment-edit-selected" => "Edit selected", "segment-select-all" => "Select all", "segment-deselect-all" => "Deselect all",
            "undo" => "Undo", "redo" => "Redo", "analyze" or "Analysis" or "reanalyze-selection" => "Analyze video",
            "prepare-video" => "Refresh video details", "transcribe" or "transcript" => "Transcript", "generate-summary" or "summary" => "Summary",
            "generate-ad-cues" or "descriptions" => "Descriptions", "ad-overview" => "AD Overview", "ad-transcript" => "AD Transcript",
            _ => "AD Transcript"
        });
    }

    private (string Text, int Start, string Selected) SummarySelection()
    {
        if (summaryEditMode) return (SummaryEditorBox.Text, SummaryEditorBox.SelectionStart, SummaryEditorBox.SelectedText);
        var all = string.Join("\n", summaryLines.Select(line => line.Text));
        var offset = 0;
        foreach (var (block, text) in summaryLines)
        {
            if (!string.IsNullOrEmpty(block.SelectedText))
            {
                var start = Math.Clamp(block.SelectionStart.Offset, 0, text.Length);
                return (all, offset + start, block.SelectedText);
            }
            offset += text.Length + 1;
        }
        return (all, 0, "");
    }

    private async Task DeleteMenuContentAsync(string component)
    {
        if (!await Confirm("Delete " + component, "Permanently delete this generated content and its associated audio or exports?", "Delete")) return;
        StopSegmentPreview(); narrationPlayer.Pause(); onDeviceNarrator.Stop();
        await Model.RunAsync("delete-generated", new[] { "--component", component });
    }

    private async Task RunDesktopCommandAsync(string id)
    {
        if (!CanRunDesktopCommand(id)) return;
        var e = new RoutedEventArgs();
        switch (id)
        {
            case "new-project": await ShowNewProjectDialogAsync(false); break;
            case "open-project": OpenProject_Click(this, e); break;
            case "remove-from-list":
                if (SelectedBrowserProject() is { } listedProject) RemoveProjectFromList(listedProject);
                break;
            case "export": Export_Click(this, e); break;
            case "settings": Settings_Click(this, e); break;
            case "help": HelpSettings_Click(this, e); break;
            case "filter-segments": await ShowSegmentFilterAsync(); break;
            case "sort-start": SortStart_Click(this, e); break;
            case "sort-duration-asc": SortDurationAsc_Click(this, e); break;
            case "sort-duration-desc": SortDurationDesc_Click(this, e); break;
            case "sort-origin": SortOrigin_Click(this, e); break;
            case "about": AboutSettings_Click(this, e); break;
            case "close-window": CloseWindowDeferred(); return;
            case "open-editor-window": OpenEditor_Click(this, e); break;
            case "preview": TogglePreview_Click(this, e); break;
            case "toggle-timeline": ToggleTimeline_Click(this, e); break;
            case "show-projects": ToggleLibrary_Click(this, e); break;
            case "show-inspector": ToggleInspector_Click(this, e); break;
            case "project-list": SetProjectLayout(false); break;
            case "project-grid": SetProjectLayout(true); break;
            case "fullscreen": FullScreen_Click(this, e); break;
            case "play": PlayPause_Click(this, e); break;
            case "stop": StopProjectPlayback(); Seek(0); break;
            case "back": Rewind_Click(this, e); break;
            case "forward": Forward_Click(this, e); break;
            case "narration": Narration.IsOn = !Narration.IsOn; break;
            case "caption-cycle":
                var modes = new[] { "off", "transcript", "combined" };
                SetCaptionMode(modes[(Array.IndexOf(modes, Model.Settings.Get("captionDisplayMode", "off")) + 1) % modes.Length]); break;
            case "caption-off": SetCaptionMode("off"); break;
            case "caption-transcript": SetCaptionMode("transcript"); break;
            case "caption-ad-transcript": SetCaptionMode("combined"); break;
            case "transcript": Section.SelectedIndex = 0; break;
            case "summary": Section.SelectedIndex = 1; break;
            case "descriptions": Section.SelectedIndex = 2; break;
            case "ad-overview": Section.SelectedIndex = 3; break;
            case "ad-transcript": Section.SelectedIndex = 4; break;
            case "segment-edit-mode": EditMode_Click(this, e); break;
            case "add": case "reanalyze-selection": await EditSegment(null); break;
            case "segment-select-all": SelectAll_Click(this, e); break;
            case "segment-deselect-all": Deselect_Click(this, e); break;
            case "segment-edit-selected": Edit_Click(this, e); break;
            case "segment-delete-selected": DeleteSegments_Click(this, e); break;
            case "follow": Follow.IsOn = !Follow.IsOn; break;
            case "undo": Undo_Click(this, e); break;
            case "redo": Redo_Click(this, e); break;
            case "analyze": case "transcribe": case "generate-summary": case "generate-ad-cues": case "prepare-video":
                Command_Click(new Button { Tag = id }, e); break;
            case "synthesize-tts": await SynthesizeNarrationWithConfirmationAsync(); break;
            case "synthesize-changed": await SynthesizeNarrationWithConfirmationAsync(new[] { "--changed" }); break;
            case "review-narration-issues": ReviewNarrationIssues_Click(this, e); break;
            case "review-narration-voices":
                if (Model.NarrationVoiceUsageReport() is { } voiceReport) await ShowNarrationVoicesAsync(voiceReport);
                break;
            case "delete-narration": await DeleteMenuContentAsync("narration"); break;
            case "delete-descriptions": await DeleteMenuContentAsync("descriptions"); break;
            case "delete-current": await DeleteMenuContentAsync(Section.SelectedIndex switch { 0 => "transcript", 1 => "summary", _ => "descriptions" }); break;
            case "import-assets": await ImportExistingAssetsAsync(Models.ImportScope.All); break;
            case "import-transcript": await ImportExistingAssetsAsync(Models.ImportScope.Transcript); break;
            case "import-summary": await ImportExistingAssetsAsync(Models.ImportScope.Summary); break;
            case "import-descriptions": await ImportExistingAssetsAsync(Models.ImportScope.Descriptions); break;
            case "show-in-finder": if (ProjectManagementTarget() is { } revealProject) await RevealProjectAsync(revealProject); break;
            case "open-configuration": if (ProjectManagementTarget() is { } configurationProject) await OpenProjectConfigurationAsync(configurationProject); break;
            case "rename-project": if (ProjectManagementTarget() is { } renameProject) await RenameProjectAsync(renameProject); break;
            case "trash-project": if (ProjectManagementTarget() is { } trashProject) await DeleteProjectAsync(trashProject); break;
            case "summary-play": if (readback.State == SummaryReadbackState.Paused) readback.Resume(); else await readback.PlayFromStartAsync(); break;
            case "summary-pause": readback.Pause(); break;
            case "summary-stop": readback.Stop(); break;
            case "summary-previous-sentence": ClearSummaryHighlight(); await readback.PreviousSentenceAsync(); break;
            case "summary-next-sentence": ClearSummaryHighlight(); await readback.NextSentenceAsync(); break;
            case "summary-previous-paragraph": ClearSummaryHighlight(); await readback.PreviousParagraphAsync(); break;
            case "summary-next-paragraph": ClearSummaryHighlight(); await readback.NextParagraphAsync(); break;
            case "summary-from-start": await readback.PlayFromStartAsync(); break;
            case "summary-from-selection": var selection = SummarySelection(); await readback.SpeakAsync(selection.Text[selection.Start..]); break;
            case "summary-read-selection": await readback.SpeakAsync(SummarySelection().Selected); break;
            default: throw new InvalidOperationException("Unknown desktop menu command: " + id);
        }
        UpdateDesktopMenuStates();
    }

    /// <summary>
    /// Closes after the current menu or keyboard handler returns. Calling Window.Close()
    /// synchronously from a menu click can abort with E_ABORT while the flyout is still
    /// dismissing, which previously surfaced as an "Action could not finish" dialog.
    /// </summary>
    private void CloseWindowDeferred()
    {
        DispatcherQueue.TryEnqueue(() =>
        {
            try { Close(); }
            catch (Exception ex)
            {
                Model.Engine.Log(ex.ToString());
                try { AppWindow.Destroy(); }
                catch (Exception fallback) { Model.Engine.Log(fallback.ToString()); }
            }
        });
    }

    private void SetCaptionMode(string mode)
    {
        Model.Settings.Set("captionDisplayMode", mode); Model.Settings.Save(); UpdateCaptions();
    }
    private void SetProjectLayout(bool grid)
    {
        if (desktopEditorVisible)
        {
            ProjectGrid.Visibility = grid ? Visibility.Visible : Visibility.Collapsed;
            ProjectList.Visibility = grid ? Visibility.Collapsed : Visibility.Visible;
            ProjectSplitView.IsPaneOpen = true;
        }
        else
        {
            GalleryGrid.Visibility = grid ? Visibility.Visible : Visibility.Collapsed;
            GalleryTable.Visibility = grid ? Visibility.Collapsed : Visibility.Visible;
        }
        Model.Settings.Set("projectPresentation", grid ? "grid" : "list"); Model.Settings.Save();
        UpdateEditorProjectButtons();
    }
}
