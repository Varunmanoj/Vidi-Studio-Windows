using Vidi_Studio_Windows.Services;
using Vidi_Studio_Windows.Models;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Text.RegularExpressions;
using Vidi_Studio_Windows.ViewModels;
using Windows.UI;

namespace Vidi_Studio_Windows.Views;

public sealed partial class ExportDialog : ContentDialog
{
    private static readonly string[] Sections = { "Video", "Audio", "Text", "Web Playback" };
    private static readonly string[] TextLabels = { "Transcript", "Summary", "Descriptions", "AD Transcript" };
    private static readonly string[] TextIds = { "transcript", "summary", "descriptions", "combined" };
    private static readonly string[] CaptionLabels = {
        "No transcript", "Transcript — closed captions", "Transcript — open captions",
        "AD Transcript — closed captions", "AD Transcript — open captions",
        "Transcript and AD Transcript — two closed-caption tracks"
    };
    private readonly StudioViewModel model;
    private readonly MainWindow? owner;
    private readonly ListView sidebar = new() { SelectionMode = ListViewSelectionMode.Single };
    private readonly StackPanel page = new() { Spacing = 18, Padding = new Thickness(26, 22, 26, 20) };
    private readonly StackPanel footer = new() { Orientation = Orientation.Horizontal,
        HorizontalAlignment = HorizontalAlignment.Center, Spacing = 12, Padding = new Thickness(0, 12, 0, 8) };
    private readonly Button saveButton = new() { Content = "Save", Width = 120 };
    private string videoFormat = "mkv", audioFormat = "wav";
    private int videoAudio = 1, videoCaptions, audioChoice = 1, webAudio = 1, webCaptions, textContent, textFormat;
    private int adSource;
    private readonly HashSet<string> exportLanguages = new();
    private bool awaitingReplacementConfirmation;
    private readonly Dictionary<string, Dictionary<string, string>> resultSelections = new();


    private StudioProject? ExportProject {
        get {
            if (model.Project is not { } project) return null;
            var code = exportLanguages.OrderBy(v => v).FirstOrDefault() ?? project.SelectedLanguage;
            return project.WithLanguageResults(code, resultSelections.TryGetValue(code, out var chosen) ? chosen : null);
        }
    }

    public ExportDialog(StudioViewModel model, MainWindow? owner = null)
    {
        this.model = model;
        exportLanguages.Add(ExportProject?.SelectedLanguage ?? "auto");
        this.owner = owner;
        var ready = ExportProject?.HasCompleteExportNarration == true;
        var imported = ExportProject?.ImportedAudioDescription != null;
        adSource = imported && !ready ? 1 : 0;
        videoAudio = webAudio = ready || imported ? 1 : 0;
        videoCaptions = !ready && !imported && ExportProject?.CanExportText("transcript") == true ? 1 : 0;
        audioChoice = ExportProject?.IsYouTube == true ? 0 : ready || imported ? 1 : 0;
        textContent = Array.FindIndex(TextIds, id => ExportProject?.CanExportText(id) == true);
        if (textContent < 0) textContent = 0;
        Title = "Export";
        Width = 950; MinWidth = 620; MaxWidth = 950;
        Resources["ContentDialogMinWidth"] = 620.0;
        Resources["ContentDialogMaxWidth"] = 950.0;
        var shell = new Grid { Width = 900, Height = 570 };
        shell.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(210) });
        shell.ColumnDefinitions.Add(new ColumnDefinition());
        shell.RowDefinitions.Add(new RowDefinition());
        shell.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        shell.Children.Add(new Border { Background = new SolidColorBrush(Color.FromArgb(18, 128, 128, 128)),
            Padding = new Thickness(10, 16, 10, 10), Child = sidebar });
        var glyphs = new[] { "\uE714", "\uE767", "\uE8D2", "\uE774" };
        for (var i = 0; i < Sections.Length; i++)
        {
            var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 12 };
            row.Children.Add(new FontIcon { Glyph = glyphs[i], FontSize = 18,
                Foreground = new SolidColorBrush(Color.FromArgb(255, 0, 120, 212)) });
            row.Children.Add(new TextBlock { Text = Sections[i], VerticalAlignment = VerticalAlignment.Center });
            sidebar.Items.Add(new ListViewItem { Content = row, MinHeight = 46,
                HorizontalContentAlignment = HorizontalAlignment.Stretch,
                IsEnabled = ExportProject?.IsYouTube != true || i is 1 or 2 });
        }
        var scroll = new ScrollViewer { Content = page, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        Grid.SetColumn(scroll, 1); shell.Children.Add(scroll);
        saveButton.Style = (Style)Application.Current.Resources["AccentButtonStyle"];
        var cancelButton = new Button { Content = "Cancel", Width = 120 };
        cancelButton.Click += (_, _) => Hide();
        saveButton.Click += async (_, _) => await SaveAsync();
        footer.Children.Add(cancelButton);
        footer.Children.Add(saveButton);
        Grid.SetRow(footer, 1);
        Grid.SetColumnSpan(footer, 2);
        shell.Children.Add(footer);
        Content = shell;
        Loaded += (_, _) => ConnectivityService.Shared.Changed += ConnectivityChanged;
        Unloaded += (_, _) => ConnectivityService.Shared.Changed -= ConnectivityChanged;
        Loaded += (_, _) =>
        {
            var width = Math.Clamp((XamlRoot?.Size.Width ?? 1000) - 48, 620, 950);
            Width = width;
            shell.Width = width - 50;
            shell.Height = Math.Clamp((XamlRoot?.Size.Height ?? 720) - 210, 320, 570);
            shell.ColumnDefinitions[0].Width = new GridLength(width < 780 ? 170 : 210);
        };
        sidebar.SelectionChanged += (_, _) => BuildPage();
        sidebar.SelectedIndex = ExportProject?.IsYouTube == true ? 1 : 0;
    }

#if DEBUG
    internal StackPanel FooterForSmokeTest => footer;
    internal bool CanSaveForSmokeTest => saveButton.IsEnabled;
    internal bool AudioChoiceForSmokeTest(int index) => CanUseAudio(index);
    internal bool CaptionChoiceForSmokeTest(int index) => CanUseCaptions(index);
    internal void FormatsForSmokeTest(string video, string audio) { videoFormat = video; audioFormat = audio; BuildPage(); }
    internal void NavigateForSmokeTest(int section) => sidebar.SelectedIndex = section;
    internal (string Command, List<string> Arguments) RequestForSmokeTest() => BuildRequest();
    internal (string Command, List<string> Arguments) RequestForSmokeTest(int section, int primary, int secondary)
    {
        sidebar.SelectedIndex = section;
        switch (section)
        {
            case 0: videoAudio = primary; videoCaptions = secondary; break;
            case 1: audioChoice = primary; break;
            case 2: textContent = primary; textFormat = secondary; break;
            case 3: webAudio = primary; webCaptions = secondary; break;
        }
        BuildPage();
        return BuildRequest();
    }
#endif

    private async Task SaveAsync()
    {
        if (SelectingTracks) { if (CanSave()) await SaveSelectedTracksAsync(); return; }
        if (!CanSave() || exportLanguages.Count == 0) return;
        var (command, arguments) = BuildRequest();
        if (owner == null || model.Project is not { } project) {
            Closed += async (_, _) => await model.RunAsync(command, arguments);
            Hide(); return;
        }
        var options = new Dictionary<string, object>();
        for (var index = 0; index < arguments.Count; index++) {
            var key = arguments[index][2..].Replace('-', '_');
            if (index + 1 < arguments.Count && !arguments[index + 1].StartsWith("--", StringComparison.Ordinal)) options[key] = arguments[++index];
            else options[key] = true;
        }
        var collisions = new List<(int Index, string Path)>();
        var jobs = new List<Dictionary<string, object>>();
        foreach (var code in exportLanguages.OrderBy(v => v)) {
            var ids = new Dictionary<string, string>();
            foreach (var kind in new[] { "transcript", "summary", "descriptions", "adAudio" }) {
                var rid = resultSelections.TryGetValue(code, out var selected) && selected.TryGetValue(kind, out var choice) ? choice : project.ActiveResultID(kind, code);
                if (!string.IsNullOrEmpty(rid)) ids[kind] = rid;
            }
            var target = project.WithLanguageResults(code, ids);
            var suffix = string.Join("-", ids.Values.Select(v => v[..Math.Min(8, v.Length)]).OrderBy(v => v, StringComparer.Ordinal)).Replace('-', '.');
            target.Name = string.Concat(project.Name.EnumerateRunes().Take(40).Select(r => r.ToString())) + " [" + OutputLanguage.Label(code) + (suffix.Length == 0 ? "" : " " + suffix) + "]";
            var path = TargetRelativePath(target);
            var legacyExists = project.Exports.Any(old => {
                var oldCode = project.ExportResults.TryGetValue(old, out var provenance) ? provenance.Language : old.Split('/').Length > 2 ? old.Split('/')[1] : "auto";
                return oldCode == code && Path.GetFileName(old) == Path.GetFileName(path) && (File.Exists(project.Resolve(old)) || Directory.Exists(project.Resolve(old)));
            });
            if (File.Exists(project.Resolve(path)) || Directory.Exists(project.Resolve(path)) || legacyExists) collisions.Add((jobs.Count, path));
            jobs.Add(new() { ["language"] = code, ["resultIDs"] = ids, ["projectName"] = project.Name, ["command"] = command, ["options"] = options, ["replaceExisting"] = false });
        }
        if (collisions.Count > 0) {
            awaitingReplacementConfirmation = true; UpdatePreview();
            bool? choice;
            try { choice = await owner.ConfirmExportReplacementAsync(collisions.Select(c => c.Path).ToArray()); }
            finally { awaitingReplacementConfirmation = false; UpdatePreview(); }
            if (choice == null) return;
            if (choice == true) foreach (var collision in collisions) jobs[collision.Index]["replaceExisting"] = true;
        }
        Closed += async (_, _) => await owner.RunResultRequestAsync("export-batch", new { jobs });
        Hide();
    }

    private void AddLanguageSelections()
    {
        if (ExportProject is not { } project) return;
        page.Children.Add(new TextBlock { Text = "Languages and Saved Results", FontWeight = Microsoft.UI.Text.FontWeights.SemiBold });
        foreach (var code in project.SavedResults.Select(r => r.Language).Append(project.SelectedLanguage).Distinct().OrderBy(v => v)) {
            var toggle = new CheckBox { Content = OutputLanguage.Label(code), IsChecked = exportLanguages.Contains(code) };
            var fields = new StackPanel { Spacing = 8, Visibility = exportLanguages.Contains(code) ? Visibility.Visible : Visibility.Collapsed };
            toggle.Checked += (_, _) => { exportLanguages.Add(code); fields.Visibility = Visibility.Visible; UpdatePreview(); };
            toggle.Unchecked += (_, _) => { exportLanguages.Remove(code); fields.Visibility = Visibility.Collapsed; UpdatePreview(); };
            page.Children.Add(toggle); page.Children.Add(fields);
            foreach (var kind in new[] { "transcript", "summary", "descriptions", "adAudio" }) {
                var rows = project.SavedResults.Where(r => r.Kind == kind && r.Language == code && r.State == "saved").ToList();
                if (rows.Count == 0) continue;
                if (!resultSelections.TryGetValue(code, out var selected)) resultSelections[code] = selected = new();
                var rid = selected.TryGetValue(kind, out var chosen) ? chosen : project.ActiveResultID(kind, code);
                var picker = new ComboBox { Header = LanguageResult.KindTitle(kind), ItemsSource = rows, DisplayMemberPath = "Title" };
                SavedResultOptions.Populate(picker, rows, project, rows.FirstOrDefault(r => r.Id == rid)?.Id ?? rows[0].Id, selected);
                picker.SelectionChanged += (_, _) => { if (SavedResultOptions.Selected(picker) is LanguageResult value) { resultSelections[code][kind] = value.Id; BuildPage(); } };
                fields.Children.Add(picker);
            }

        }
    }

    private void BuildPage()
    {
        page.Children.Clear();
        if (sidebar.SelectedIndex < 0) return;
        var section = sidebar.SelectedIndex;
        var heading = new StackPanel { Spacing = 2 };
        heading.Children.Add(new TextBlock { Text = Sections[section], FontSize = 24,
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold });
        heading.Children.Add(new TextBlock { Text = ExportProject?.Name ?? "No project selected", Opacity = .6,
            TextTrimming = TextTrimming.CharacterEllipsis });
        page.Children.Add(heading);
        if (section is 0 or 1) {
            var output = new CheckBox { Content = "One File with Selected Tracks", IsChecked = combineTracks };
            output.Checked += (_, _) => { combineTracks = true; BuildPage(); };
            output.Unchecked += (_, _) => { combineTracks = false; BuildPage(); };
            page.Children.Add(output);
        }
        if (SelectingTracks) { BuildTrackChoices(); UpdatePreview(); return; }
        AddLanguageSelections();
        switch (section)
        {
            case 0: BuildVideo(); break;
            case 1: BuildAudio(); break;
            case 2: BuildText(); break;
            case 3: BuildWeb(); break;
        }
        UpdatePreview();
    }

    private static Border Card(UIElement child) => new()
    {
        Child = child, CornerRadius = new CornerRadius(10), Padding = new Thickness(16, 12, 16, 12),
        Background = new SolidColorBrush(Color.FromArgb(18, 128, 128, 128))
    };

    private void AddChoices(string title, string prompt, string[] labels, int selected, Action<int> changed, Func<int, bool>? available = null)
    {
        var group = new StackPanel { Spacing = 8 };
        group.Children.Add(new TextBlock { Text = title, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold, FontSize = 16 });
        var buttons = new RadioButtons { Header = prompt };
        for (var index = 0; index < labels.Length; index++)
        {
            var enabled = available?.Invoke(index) ?? true;
            var option = new RadioButton { Content = labels[index], IsEnabled = enabled };
            Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(option, labels[index]);
            if (!enabled) ToolTipService.SetToolTip(option, "Create the required text or generate current narration for every description first.");
            buttons.Items.Add(option);
        }
        buttons.SelectedIndex = selected;
        buttons.SelectionChanged += (_, _) =>
        { if (buttons.SelectedIndex >= 0) { changed(buttons.SelectedIndex); UpdatePreview(); } };
        group.Children.Add(Card(buttons)); page.Children.Add(group);
    }

    private void BuildVideo()
    {
        AddFormat("Video file format", new[] { "mkv", "mp4", "mov", "m4v" }, videoFormat, value => videoFormat = value);
        AddChoices("Audio", "Audio included in the video",
            new[] { "Original audio only", "Closed Audio Description", "Open Audio Description" },
            videoAudio, index => videoAudio = index, CanUseVideoAudio);
        AddAdSource();
        AddChoices("Transcript and captions", "Transcript included in the video", CaptionLabels,
            videoCaptions, index => videoCaptions = index, CanUseCaptions);
    }

    private void BuildAudio()
    {
        var youtube = ExportProject?.IsYouTube == true;
        AddFormat("Audio file format", new[] { "wav", "mp3", "m4a" }, audioFormat, value => audioFormat = value);
        var labels = youtube ? new[] { "YouTube audio only", "Narration audio only", "YouTube audio with narration" } :
            new[] { "Original video audio", "Final audio with Audio Description", "Narration audio only" };
        AddChoices("Audio", "Audio to save", labels, audioChoice, index => audioChoice = index, CanUseAudio);
        AddAdSource();
        if (youtube && owner != null) page.Children.Add(new AudioSourceControls(owner, model, this));
    }

    private void BuildText()
    {
        var form = new StackPanel { Spacing = 12 };
        var content = new ComboBox { Header = "Content", HorizontalAlignment = HorizontalAlignment.Stretch };
        for (var index = 0; index < TextLabels.Length; index++) content.Items.Add(new ComboBoxItem
        { Content = TextLabels[index], IsEnabled = ExportProject?.CanExportText(TextIds[index]) == true });
        content.SelectedIndex = textContent;
        var format = new ComboBox { Header = "Format", HorizontalAlignment = HorizontalAlignment.Stretch };
        void RefreshFormats()
        {
            format.ItemsSource = textContent == 1 ? new[] { "Markdown (.md)" } :
                new[] { "SubRip (.srt)", "WebVTT (.vtt)", "Plain text (.txt)", "JSON (.json)" };
            format.SelectedIndex = textContent == 1 ? 0 : Math.Clamp(textFormat, 0, 3);
        }
        content.SelectionChanged += (_, _) =>
        { textContent = Math.Max(0, content.SelectedIndex); RefreshFormats(); UpdatePreview(); };
        format.SelectionChanged += (_, _) =>
        { if (textContent != 1 && format.SelectedIndex >= 0) textFormat = format.SelectedIndex; UpdatePreview(); };
        RefreshFormats(); form.Children.Add(content); form.Children.Add(format);
        page.Children.Add(new TextBlock { Text = "Text content", FontSize = 16,
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold });
        page.Children.Add(Card(form));
    }

    private void BuildWeb()
    {
        AddChoices("Audio", "Audio included in Web Playback",
            new[] { "Original audio only", "Closed Audio Description", "Open Audio Description" },
            webAudio, index => webAudio = index, CanUseVideoAudio);
        AddChoices("Transcript and captions", "Transcript included in Web Playback", CaptionLabels,
            webCaptions, index => webCaptions = index, CanUseCaptions);
    }

    private void AddFormat(string title, string[] formats, string selected, Action<string> changed)
    {
        var picker = new ComboBox { Header = title, ItemsSource = formats, SelectedItem = selected,
            HorizontalAlignment = HorizontalAlignment.Stretch };
        picker.SelectionChanged += (_, _) => { if (picker.SelectedItem is string value) { changed(value); UpdatePreview(); } };
        page.Children.Add(Card(picker));
    }

    private bool CanUseVideoAudio(int choice) => ExportProject is { HasLocalSource: true } project &&
        (choice == 0 || (adSource == 1 ? project.ImportedAudioDescription != null : project.HasCompleteExportNarration));
    private bool CanUseAudio(int choice) => ExportProject is { } project && (project.IsYouTube
        ? choice == 0 || (choice == 2 && project.Duration > 0 && project.ImportedAudioDescription != null) || project.HasCompleteExportNarration
        : choice == 0 ? project.HasLocalSource && project.HasAudio
        : choice == 1 ? project.HasLocalSource && (adSource == 1 ? project.ImportedAudioDescription != null : project.HasCompleteExportNarration)
        : project.HasCompleteExportNarration);
    private void AddAdSource()
    {
        if (ExportProject is not { } project || project.ImportedAudioDescription == null) return;
        if (!project.HasCompleteExportNarration)
        {
            page.Children.Add(new TextBlock
            {
                Text = "Audio Description source: Imported AD audio (" + project.ImportedAudioDescription.ModeLabel + ")",
                FontWeight = Microsoft.UI.Text.FontWeights.SemiBold, TextWrapping = TextWrapping.Wrap
            });
            return;
        }
        var picker = new ComboBox
        {
            Header = "Audio Description source",
            HorizontalAlignment = HorizontalAlignment.Stretch,
            ItemsSource = new[] { "Generated narration", "Imported AD audio" },
            SelectedIndex = adSource
        };
        ToolTipService.SetToolTip(picker, "Choose whether the described track uses generated narration or the imported AD audio.");
        picker.SelectionChanged += (_, _) => { adSource = Math.Max(0, picker.SelectedIndex); UpdatePreview(); };
        page.Children.Add(Card(picker));
    }

    private bool CanUseCaptions(int choice) => ExportProject is { } project && (choice switch
    {
        0 => true,
        1 or 2 => project.CanExportText("transcript"),
        3 or 4 => project.CanExportText("combined"),
        5 => project.CanExportText("transcript") && project.CanExportText("combined"),
        _ => false
    });
    private bool ConnectionReady()
    {
        if (sidebar.SelectedIndex < 0) return false;
        var request = BuildRequest();
        return model.OnlineReason(request.Command, request.Arguments) == null;
    }
    private void ConnectivityChanged(object? sender, EventArgs e) => UpdatePreview();
    private bool CanSave() => !awaitingReplacementConfirmation && ConnectionReady() && model.Idle && ExportProject != null && (SelectingTracks ? TrackSelectionValid : (sidebar.SelectedIndex switch
    {
        0 => CanUseVideoAudio(videoAudio) && CanUseCaptions(videoCaptions) && (videoAudio != 0 || videoCaptions != 0),
        1 => CanUseAudio(audioChoice),
        2 => ExportProject.CanExportText(TextIds[textContent]),
        3 => CanUseVideoAudio(webAudio) && CanUseCaptions(webCaptions),
        _ => false
    }));

    private void UpdatePreview()
    {
        if (sidebar.SelectedIndex < 0) return;
        var project = ExportProject;
        var section = sidebar.SelectedIndex;
        saveButton.Content = "Save";
        saveButton.IsEnabled = CanSave();
        var request = BuildRequest();
        ToolTipService.SetToolTip(saveButton, model.OnlineReason(request.Command, request.Arguments) ?? "Save export");
    }

    private (string Command, List<string> Arguments) BuildRequest()
    {
        var args = new List<string>();
        string command;
        switch (sidebar.SelectedIndex)
        {
            case 0:
            case 3:
                var audio = sidebar.SelectedIndex == 0 ? videoAudio : webAudio;
                var captions = sidebar.SelectedIndex == 0 ? videoCaptions : webCaptions;
                var captionMode = captions switch { 1 or 3 => "closed", 2 or 4 => "open", 5 => "dual-closed", _ => "none" };
                var content = captions is 3 or 4 ? "combined" : "transcript";
                if (sidebar.SelectedIndex == 3)
                {
                    command = "export-hls";
                    args.AddRange(new[] { "--web-audio", audio switch { 0 => "original", 1 => "closed-description", _ => "open-description" } });
                }
                else if (audio == 0)
                    command = captions switch { 0 => "export-original-video", 1 or 3 => "export-closed-caption-video", 2 or 4 => "export-open-caption-video", _ => "export-dual-closed-caption-video" };
                else if (audio != 0 && adSource == 1)
                {
                    command = "combine-imported-audio";
                    args.AddRange(new[] { "--audio-presentation", audio == 1 ? "selectable" : "open" });
                }
                else command = audio == 1 ? "export-closed-audio-description-video" : "export-open-audio-description-video";
                args.AddRange(new[] { "--captions", captionMode, "--content", content });
                if (sidebar.SelectedIndex == 0 && command != "export-original-video") args.AddRange(new[] { "--media-format", videoFormat });
                break;
            case 1:
                command = ExportProject?.IsYouTube == true ? audioChoice switch
                { 0 => "export-youtube-original-audio", 1 => "export-audio", _ => "export-youtube-described-audio" }
                : audioChoice == 1 && adSource == 1 ? "combine-imported-audio"
                : audioChoice switch { 0 => "export-original-audio", 1 => "export-described-audio", _ => "export-audio" };
                if (audioChoice == 1 && adSource == 1) args.Add("--audio-only");
                if (ExportProject?.IsYouTube == true && audioChoice == 2) args.AddRange(new[] { "--ad-source", adSource == 1 ? "imported" : "generated" });
                args.AddRange(new[] { "--media-format", audioFormat });
                break;
            case 2:
                command = "export-subtitles";
                args.AddRange(new[] { "--content", TextIds[textContent], "--format",
                    textContent == 1 ? "md" : new[] { "srt", "vtt", "txt", "json" }[textFormat] });
                break;
            default: throw new InvalidOperationException("Choose an export category.");
        }
        return (command, args);
    }

    private string TargetRelativePath(StudioProject? targetProject = null)
    {
        if ((targetProject ?? ExportProject) is not { } project || sidebar.SelectedIndex < 0) return "";
        var name = Regex.Replace(project.Name.Trim(), "[<>:\"/\\\\|?*\\x00-\\x1F\\x7F]+", "-");
        name = Regex.Replace(Regex.Replace(name, @"\s*-\s*", " - "), @"\s+", " ").Trim(' ', '.', '-');
        if (name.Length > 100) name = string.Concat(name.EnumerateRunes().Take(100).Select(r => r.ToString())).Trim(' ', '.', '-');
        if (name.Length == 0) name = "Project";
        var section = sidebar.SelectedIndex;
        var audio = section == 0 ? videoAudio : webAudio;
        var captions = section == 0 ? videoCaptions : webCaptions;
        var captionSuffix = captions switch
        {
            1 => " with Transcript Closed Captions", 2 => " with Transcript Open Captions",
            3 => " with AD Transcript Closed Captions", 4 => " with AD Transcript Open Captions",
            5 => " with Transcript and Descriptive Transcript Closed Captions", _ => ""
        };
        var type = section switch
        {
            0 when audio == 0 => captions switch
            { 1 => "Transcript Closed Caption Video", 2 => "Transcript Open Caption Video", 3 => "AD Transcript Closed Caption Video", 4 => "AD Transcript Open Caption Video", 5 => "Transcript and Descriptive Transcript Closed Caption Video", _ => "Original Video" },
            0 => (audio == 1 ? "Closed Audio Description" : "Open Audio Description") + captionSuffix,
            1 when project.IsYouTube => audioChoice switch { 0 => "YouTube Audio Only", 1 => "Narration Audio", _ => "YouTube Studio Audio Description" },
            1 => audioChoice switch { 0 => "Original Video Audio", 1 => "Final Audio with Audio Description", _ => "Narration Audio" },
            2 => TextLabels[textContent],
            _ => "Web Playback - " + (audio switch { 0 => "Original Audio", 1 => "Closed Audio Description", _ => "Open Audio Description" }) + captionSuffix
        };
        var extension = section switch { 0 => videoFormat, 1 => audioFormat, 2 => textContent == 1 ? "md" : new[] { "srt", "vtt", "txt", "json" }[textFormat], _ => "" };
        return $"exports/{project.SelectedLanguage}/{name} - {type}" + (extension.Length == 0 ? "" : "." + extension);
    }
}
