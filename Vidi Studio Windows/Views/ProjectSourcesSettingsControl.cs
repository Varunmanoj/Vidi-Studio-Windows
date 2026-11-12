using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using System;
using System.ComponentModel;
using System.Diagnostics;
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
        var row = new Grid { ColumnSpacing = 14 };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(150) }); row.ColumnDefinitions.Add(new ColumnDefinition());
        var name = Text(label); name.Opacity = .7; row.Children.Add(name);
        var content = Text(string.IsNullOrWhiteSpace(value) ? "Not available" : value); if (spokenValue != null) AutomationProperties.SetName(content, label + ": " + spokenValue); Grid.SetColumn(content, 1); row.Children.Add(content); panel.Children.Add(row);
    }
    private Button Action(string title, string command)
    {
        var button = new Button { Content = new TextBlock { Text = title, TextWrapping = TextWrapping.Wrap }, HorizontalAlignment = HorizontalAlignment.Stretch };
        AutomationProperties.SetName(button, title);
        button.Click += async (_, _) => await RunAction(command);
        return button;
    }
    private void Actions(StackPanel panel, params (string Title, string Command)[] actions)
    {
        var grid = new Grid { ColumnSpacing = 10, RowSpacing = 8 };
        grid.ColumnDefinitions.Add(new ColumnDefinition()); grid.ColumnDefinitions.Add(new ColumnDefinition());
        for (var i = 0; i < actions.Length; i++) {
            if (i % 2 == 0) grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            var button = Action(actions[i].Title, actions[i].Command); Grid.SetColumn(button, i % 2); Grid.SetRow(button, i / 2); grid.Children.Add(button);
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
            if (!project.IsYouTube) Row(sources, "Playback and export audio", "Original audio in the project video");
            body.Children.Add(ScreenReaderHeading.Mark(new Expander { Header = "Content sources", Content = sources, IsExpanded = true, HorizontalAlignment = HorizontalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Stretch }));
            if (project.IsYouTube) {
                body.Children.Add(ScreenReaderHeading.Mark(new Expander { Header = "Audio sources", Content = new AudioSourceControls(owner, scoped, showHeading: false, chooseAudio: () => RunAction("import-original-audio"), reviewAudio: () => RunAction("review-audio-timing")), IsExpanded = true, HorizontalAlignment = HorizontalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Stretch }));
                Actions(body, ("YouTube Details & Captions…", "youtube-captions"), ("Open in YouTube", "youtube-open-browser"));
            }
            var imports = new StackPanel { Spacing = 8 };
            Actions(imports, ("Import Transcript…", "import-transcript"), ("Import Summary…", "import-summary"), ("Import Descriptions…", "import-descriptions"), ("Import AD Audio…", "import-ad-audio"), ("Import Existing Assets…", "import-assets"));
            if (project.IsYouTube) {
                imports.Children.Add(Action("Import Original Audio…", "import-original-audio"));
            }
            body.Children.Add(ScreenReaderHeading.Mark(new Expander { Header = "Import into this project", Content = imports, IsExpanded = true, HorizontalAlignment = HorizontalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Stretch }));
            Actions(body, ("Export…", "export"), ("Open in Editor", "open-editor"), ("Open Project Folder", "show-folder"), ("Rename…", "rename-project"));
        } finally { rebuilding = false; UpdateProgress(); }
    }
    private async System.Threading.Tasks.Task RunAction(string command)
    {
        if (scoped.Busy || actionRunning || scoped.Project is not { } project) return;
        if (command == "show-folder") { Process.Start(new ProcessStartInfo(project.Folder) { UseShellExecute = true }); return; }
        if (command == "youtube-open-browser" && Services.YouTubeService.ProjectWatchURL(project.YoutubeURL) is { } url) { Process.Start(new ProcessStartInfo(url) { UseShellExecute = true }); return; }
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
