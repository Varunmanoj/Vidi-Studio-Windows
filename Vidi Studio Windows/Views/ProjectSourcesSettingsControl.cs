using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using System;
using System.ComponentModel;
using System.Diagnostics;
using System.Linq;
using Vidi_Studio_Windows.Models;
using Vidi_Studio_Windows.ViewModels;

namespace Vidi_Studio_Windows.Views;

/// <summary>Project-scoped source commands; never retargets the Settings owner's editor.</summary>
public sealed class ProjectSourcesSettingsControl : UserControl
{
    private readonly Window settingsWindow;
    private readonly MainWindow owner;
    private readonly string folder;
    private readonly StudioViewModel scoped = new();
    private readonly StackPanel root = new() { Spacing = 12 };
    private readonly StackPanel body = new() { Spacing = 14 };
    private readonly ContentControl bodyHost = new() { HorizontalContentAlignment = HorizontalAlignment.Stretch };
    private readonly TextBlock status = new() { TextWrapping = TextWrapping.Wrap };
    private readonly ProgressRing progress = new() { Width = 22, Height = 22, Visibility = Visibility.Collapsed };
    private bool rebuilding, actionRunning, observing;
    private bool editingLanguages;
    private MainWindow? languageEditor;

    public ProjectSourcesSettingsControl(Window settingsWindow, MainWindow owner, StudioProject project)
    {
        this.settingsWindow = settingsWindow; this.owner = owner; folder = project.Folder;
        Content = root; bodyHost.Content = body; root.Children.Add(bodyHost); root.Children.Add(progress); root.Children.Add(status);
        AutomationProperties.SetLiveSetting(status, Microsoft.UI.Xaml.Automation.Peers.AutomationLiveSetting.Polite);
        scoped.Project = StudioProject.TryLoad(folder) ?? project;
        scoped.ReportStatus("");
        Loaded += (_, _) => { if (!observing) { observing = true; scoped.PropertyChanged += Changed; settingsWindow.Activated += Activated; } };
        Unloaded += (_, _) => { observing = false; scoped.PropertyChanged -= Changed; settingsWindow.Activated -= Activated; if (languageEditor != null) languageEditor.Model.PropertyChanged -= LanguageEditorChanged; };
        Rebuild();
    }
    private void Activated(object sender, WindowActivatedEventArgs args)
    {
        if (!scoped.Busy && !actionRunning) { scoped.Project = StudioProject.TryLoad(folder); Rebuild(); }
    }
    private void Changed(object? sender, PropertyChangedEventArgs args)
    {
        UpdateProgress();
        if (!rebuilding && !scoped.Busy && args.PropertyName == nameof(StudioViewModel.Project)) Rebuild();
    }
    private void LanguageEditorChanged(object? sender, PropertyChangedEventArgs args) {
        UpdateProgress();
        if (!rebuilding && languageEditor?.Model.Busy == false && args.PropertyName is nameof(StudioViewModel.Project) or nameof(StudioViewModel.Busy)) {
            scoped.Project = StudioProject.TryLoad(folder); Rebuild();
        }
    }
    private void UpdateProgress()
    {
        var busy = scoped.Busy || actionRunning || languageEditor?.Model.Busy == true;
        bodyHost.IsEnabled = !busy;
        progress.IsActive = busy; progress.Visibility = busy ? Visibility.Visible : Visibility.Collapsed;
        status.Text = actionRunning ? "Opening this project's editor…" : scoped.Status;
    }
    private static TextBlock Text(string value) => new() { Text = value, TextWrapping = TextWrapping.Wrap, IsTextSelectionEnabled = true };
    private static void Row(StackPanel panel, string label, string value, string? spokenValue = null)
    {
        var content=Text(string.IsNullOrWhiteSpace(value) ? "Not available" : value);
        if(spokenValue!=null) AutomationProperties.SetName(content,label+": "+spokenValue);
        SettingsDialog.CompactRow(panel,label,content,true);
    }
    private Button Action(string title, string command, bool imported = false)
    {
        var button = new Button { Content = new TextBlock { Text = title, TextWrapping = TextWrapping.Wrap }, HorizontalAlignment = HorizontalAlignment.Stretch };
        AutomationProperties.SetName(button, title);
        if (imported) MarkImported(button);
        button.Click += async (_, _) => await RunAction(command);
        return button;
    }
    internal static void MarkImported(Button button)
    {
        var status = (FontIcon)Microsoft.UI.Xaml.Markup.XamlReader.Load("<FontIcon xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation' Glyph='&#xE73E;' FontSize='14' Foreground='{ThemeResource SystemFillColorSuccessBrush}'/>");
        AutomationProperties.SetAccessibilityView(status, Microsoft.UI.Xaml.Automation.Peers.AccessibilityView.Raw);
        AutomationProperties.SetHelpText(button, "Imported");
        var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
        var label = (UIElement)button.Content;
        button.Content = null;
        row.Children.Add(label);
        row.Children.Add(status);
        button.Content = row;
    }
    private static void Actions(StackPanel panel, params Button[] actions)
    {
        var grid = new Grid { ColumnSpacing = 10, RowSpacing = 8 };
        grid.ColumnDefinitions.Add(new ColumnDefinition()); grid.ColumnDefinitions.Add(new ColumnDefinition());
        for (var i = 0; i < actions.Length; i++) {
            if (i % 2 == 0) grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            var button = actions[i]; Grid.SetColumn(button, i % 2); Grid.SetRow(button, i / 2); grid.Children.Add(button);
        }
        panel.Children.Add(grid);
    }
    private void Rebuild()
    {
        if (rebuilding) return; rebuilding = true;
        try {
            body.Children.Clear();
            if (scoped.Project is not { } project) { body.Children.Add(Text("This project is unavailable. Open its folder again from All Projects.")); return; }
            var editor = App.EditorForProject(project);
            if (languageEditor != editor) {
                if (languageEditor != null) languageEditor.Model.PropertyChanged -= LanguageEditorChanged;
                languageEditor = editor; languageEditor.Model.PropertyChanged += LanguageEditorChanged;
            }
            body.Children.Add(languageEditor.ProjectLanguageSettingsPanel(editingLanguages,
                enabled => { editingLanguages = enabled; Rebuild(); },
                () => { scoped.Project = StudioProject.TryLoad(folder); Rebuild(); }));
            var details = new StackPanel { Spacing = 8 };
            Row(details, "Name", project.Name); Row(details, "Type", project.IsYouTube ? "YouTube video" : "Local video");
            Row(details, "Duration", NarrationTimeFormat.ProjectDuration(project.Duration), NarrationTimeFormat.SpokenProjectDuration(project.Duration)); Row(details, "Folder", project.Folder);
            Row(details, "Source", project.IsYouTube ? project.YoutubeURL ?? "" : project.Source);
            Row(details, "Resolution", project.Width > 0 && project.Height > 0 ? project.Width + " × " + project.Height : "Not available");
            Row(details, "Audio", project.HasAudio ? "Present" : "Not available");
            body.Children.Add(ScreenReaderHeading.Mark(new Expander { Header = "Project and media details", Content = details, HorizontalAlignment = HorizontalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Stretch }));
            var sources = new StackPanel { Spacing = 8 };
            Row(sources, "Transcript", project.Transcript.Count == 0 ? "Not available" : project.TranscriptAudioSource?.Label ?? "Source not recorded");
            Row(sources, "Summary", string.IsNullOrWhiteSpace(project.Summary) ? "Not available" : project.SummaryOriginLabel + (project.SummaryOriginSource is { Length: > 0 } source ? " · " + source : ""));
            Row(sources, "AD transcript", project.ImportedADTranscript?.SourceName ?? (project.Cues.Count == 0 ? "Not available" : "Project descriptions"));
            Row(sources, "AD audio", project.ImportedAudioDescription?.SourceName ?? (project.HasNarrationAudio ? "Generated narration" : "Not available"));
            if (!project.UsesStandaloneYouTubeAudio) Row(sources, "Export audio", "Original audio in the project video");
            body.Children.Add(ScreenReaderHeading.Mark(new Expander { Header = "Content sources", Content = sources, IsExpanded = true, HorizontalAlignment = HorizontalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Stretch }));
            if (project.AllowsOriginalAudioImport)
                body.Children.Add(ScreenReaderHeading.Mark(new Expander { Header = "Audio sources", Content = new AudioSourceControls(owner, scoped, showHeading: false, chooseAudio: () => RunAction("import-original-audio"), reviewAudio: () => RunAction("review-audio-timing"), compact:true), IsExpanded = true, HorizontalAlignment = HorizontalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Stretch }));
            if (project.IsYouTube) {
                var youtube = new StackPanel { Spacing = 8 };
                var detailsButton = Action("YouTube Details & Captions…", "youtube-captions");
                detailsButton.HorizontalAlignment = HorizontalAlignment.Left;
                youtube.Children.Add(detailsButton);
                youtube.Children.Add(MainWindow.BuildYouTubeProjectLinks(project));
                body.Children.Add(ScreenReaderHeading.Mark(new Expander { Header = "YouTube video", Content = youtube, IsExpanded = true, HorizontalAlignment = HorizontalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Stretch }));
            }
            var imports = new StackPanel { Spacing = 8 };
            var importedKinds = project.SavedResults.Where(result => result.ImportedSourceName != null)
                .Select(result => result.Kind).ToHashSet();
            Actions(imports, Action("Import Transcript…", "import-transcript", importedKinds.Contains("transcript")),
                Action("Import Summary…", "import-summary", importedKinds.Contains("summary")),
                Action("Import Descriptions…", "import-descriptions", importedKinds.Contains("descriptions")),
                Action("Import AD Audio…", "import-ad-audio", importedKinds.Contains("adAudio")),
                Action("Import Existing Assets…", "import-assets"));
            if (project.IsYouTube) {
                var original = Action("Import Original Audio…", "import-original-audio", project.OriginalAudio != null);
                original.IsEnabled = project.AllowsOriginalAudioImport;
                imports.Children.Add(original);
                imports.Children.Add(editor.BuildYouTubePlaybackControls(project, RunAction, inProjectSettings: true));
            }
            body.Children.Add(ScreenReaderHeading.Mark(new Expander { Header = "Import into this project", Content = imports, IsExpanded = true, HorizontalAlignment = HorizontalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Stretch }));
            body.Children.Add(BuildImportedAssets(project, editor.CanDeleteProjectSettingsContent));
            Actions(body, Action("Export…", "export"), Action("Open in Editor", "open-editor"),
                Action("Open Project Folder", "show-folder"), Action("Rename…", "rename-project"));
            // Wrap the existing sections without changing their commands or expansion state.
            for(var i=0;i<body.Children.Count;i++) {
                var child=body.Children[i]; body.Children.RemoveAt(i); body.Children.Insert(i,SettingsDialog.CompactCard(child));
            }
        } finally { rebuilding = false; UpdateProgress(); }
    }
    private Expander BuildImportedAssets(StudioProject project, bool canDelete)
    {
        var panel = new StackPanel { Spacing = 8 };
        if (project.OriginalAudio is { } audio) Row(panel, "Original audio", audio.SourceName);
        if (project.YouTubePlaybackVideo is { } video) Row(panel, "Local playback video", video.SourceName);
        var results = project.SavedResults;
        foreach (var result in results)
            if (result.ImportedSourceName is { } filename)
            {
                var review = result.State == "draft" ? " · Review needed" : "";
                Row(panel, $"{LanguageResult.KindTitle(result.Kind)} · {OutputLanguage.Label(result.Language)} · Version {result.Version}{review}", filename);
            }
        foreach (var filename in results.SelectMany(result => result.ImportedNarrationSourceNames).Distinct().OrderBy(name => name))
            Row(panel, "Narration audio", filename);
        if (panel.Children.Count == 0) panel.Children.Add(Text("No imported assets"));

        const string title = "Choose What to Delete…";
        var button = Action(title, "delete-content");
        button.HorizontalAlignment = HorizontalAlignment.Left;
        button.IsEnabled = canDelete;
        var label = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        label.Children.Add(MenuIcons.Create("delete-content"));
        label.Children.Add(button.Content as TextBlock ?? new TextBlock { Text = title });
        button.Content = label;
        var item = new MenuFlyoutItem { Text = title, Icon = MenuIcons.Create("delete-content"), IsEnabled = canDelete };
        item.Click += async (_, _) => await RunAction("delete-content");
        var menu = new MenuFlyout();
        menu.Items.Add(item);
        button.ContextFlyout = menu;
        panel.Children.Add(button);
        return ScreenReaderHeading.Mark(new Expander { Header = "Imported assets", Content = panel, IsExpanded = true, HorizontalAlignment = HorizontalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Stretch });
    }
    private async System.Threading.Tasks.Task RunAction(string command)
    {
        if (scoped.Busy || actionRunning || scoped.Project is not { } project) return;
        if (command == "show-folder") { Process.Start(new ProcessStartInfo(project.Folder) { UseShellExecute = true }); return; }
        actionRunning = true; UpdateProgress();
        try {
            var editor = App.EditorForProject(project); editor.Activate();
            if (command == "open-editor") return;
            await editor.RunProjectSettingsActionAsync(command);
            scoped.Project = StudioProject.TryLoad(folder);
            settingsWindow.Activate(); Rebuild();
        } catch (Exception ex) { scoped.ReportStatus(ex.Message); }
        finally { actionRunning = false; UpdateProgress(); }
    }
}
