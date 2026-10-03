using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using Vidi_Studio_Windows.ViewModels;
using Windows.UI;

namespace Vidi_Studio_Windows.Views;

public sealed class ExportDialog : ContentDialog
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
    private readonly ListView sidebar = new() { SelectionMode = ListViewSelectionMode.Single };
    private readonly StackPanel page = new() { Spacing = 18, Padding = new Thickness(26, 22, 26, 20) };
    private readonly TextBlock explanation = new() { TextWrapping = TextWrapping.Wrap, FontSize = 12, Opacity = .7 };
    private readonly StackPanel footer = new() { Orientation = Orientation.Horizontal,
        HorizontalAlignment = HorizontalAlignment.Center, Spacing = 12, Padding = new Thickness(0, 12, 0, 8) };
    private readonly Button saveButton = new() { Content = "Save", Width = 120 };
    private readonly CheckBox replaceExisting = new() { Content = "Replace the existing export", Visibility = Visibility.Collapsed };
    private readonly TextBlock replacementNotice = new() { TextWrapping = TextWrapping.Wrap, Visibility = Visibility.Collapsed };
    private readonly TextBlock captionNotice = new() { TextWrapping = TextWrapping.Wrap, Visibility = Visibility.Collapsed };
    private string videoFormat = "mkv", audioFormat = "wav", lastTarget = "";
    private int videoAudio = 1, videoCaptions, audioChoice = 1, webAudio = 1, webCaptions, textContent, textFormat;

    public ExportDialog(StudioViewModel model)
    {
        this.model = model;
        var ready = model.Project?.HasCompleteExportNarration == true;
        videoAudio = webAudio = ready ? 1 : 0;
        videoCaptions = !ready && model.Project?.CanExportText("transcript") == true ? 1 : 0;
        audioChoice = model.Project?.IsYouTube == true ? 0 : ready ? 1 : 0;
        textContent = Array.FindIndex(TextIds, id => model.Project?.CanExportText(id) == true);
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
        var glyphs = new[] { "\uE714", "\uE767", "\uE8A5", "\uE774" };
        for (var i = 0; i < Sections.Length; i++)
        {
            var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 12 };
            row.Children.Add(new FontIcon { Glyph = glyphs[i], FontSize = 18,
                Foreground = new SolidColorBrush(Color.FromArgb(255, 0, 120, 212)) });
            row.Children.Add(new TextBlock { Text = Sections[i], VerticalAlignment = VerticalAlignment.Center });
            sidebar.Items.Add(new ListViewItem { Content = row, MinHeight = 46,
                HorizontalContentAlignment = HorizontalAlignment.Stretch,
                IsEnabled = model.Project?.IsYouTube != true || i is 1 or 2 });
        }
        var scroll = new ScrollViewer { Content = page, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        Grid.SetColumn(scroll, 1); shell.Children.Add(scroll);
        saveButton.Style = (Style)Application.Current.Resources["AccentButtonStyle"];
        var cancelButton = new Button { Content = "Cancel", Width = 120 };
        cancelButton.Click += (_, _) => Hide();
        saveButton.Click += (_, _) =>
        {
            if (!CanSave()) return;
            var (command, arguments) = BuildRequest();
            if (replaceExisting.IsChecked == true) arguments.Add("--replace-existing");
            Closed += async (_, _) => await model.RunAsync(command, arguments);
            Hide();
        };
        replaceExisting.Checked += (_, _) => UpdatePreview();
        replaceExisting.Unchecked += (_, _) => UpdatePreview();
        footer.Children.Add(cancelButton);
        footer.Children.Add(saveButton);
        Grid.SetRow(footer, 1);
        Grid.SetColumnSpan(footer, 2);
        shell.Children.Add(footer);
        Content = shell;
        Loaded += (_, _) =>
        {
            var width = Math.Clamp((XamlRoot?.Size.Width ?? 1000) - 48, 620, 950);
            Width = width;
            shell.Width = width - 50;
            shell.Height = Math.Clamp((XamlRoot?.Size.Height ?? 720) - 210, 320, 570);
            shell.ColumnDefinitions[0].Width = new GridLength(width < 780 ? 170 : 210);
        };
        sidebar.SelectionChanged += (_, _) => BuildPage();
        sidebar.SelectedIndex = model.Project?.IsYouTube == true ? 1 : 0;
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

    private void BuildPage()
    {
        page.Children.Clear();
        if (sidebar.SelectedIndex < 0) return;
        var section = sidebar.SelectedIndex;
        var heading = new StackPanel { Spacing = 2 };
        heading.Children.Add(new TextBlock { Text = Sections[section], FontSize = 24,
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold });
        heading.Children.Add(new TextBlock { Text = model.Project?.Name ?? "No project selected", Opacity = .6,
            TextTrimming = TextTrimming.CharacterEllipsis });
        page.Children.Add(heading);
        switch (section)
        {
            case 0: BuildVideo(); break;
            case 1: BuildAudio(); break;
            case 2: BuildText(); break;
            case 3: BuildWeb(); break;
        }
        page.Children.Add(explanation);
        page.Children.Add(captionNotice);
        page.Children.Add(replacementNotice);
        page.Children.Add(replaceExisting);
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
        AddChoices("Transcript and captions", "Transcript included in the video", CaptionLabels,
            videoCaptions, index => videoCaptions = index, CanUseCaptions);
    }

    private void BuildAudio()
    {
        var youtube = model.Project?.IsYouTube == true;
        AddFormat("Audio file format", new[] { "wav", "mp3", "m4a" }, audioFormat, value => audioFormat = value);
        var labels = youtube ? new[] { "YouTube audio only", "Narration audio only", "YouTube audio with narration" } :
            new[] { "Original video audio", "Final audio with Audio Description", "Narration audio only" };
        AddChoices("Audio", "Audio to save", labels, audioChoice, index => audioChoice = index, CanUseAudio);
    }

    private void BuildText()
    {
        var form = new StackPanel { Spacing = 12 };
        var content = new ComboBox { Header = "Content", HorizontalAlignment = HorizontalAlignment.Stretch };
        for (var index = 0; index < TextLabels.Length; index++) content.Items.Add(new ComboBoxItem
        { Content = TextLabels[index], IsEnabled = model.Project?.CanExportText(TextIds[index]) == true });
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

    private bool CanUseVideoAudio(int choice) => model.Project is { HasLocalSource: true } project &&
        (choice == 0 || project.HasCompleteExportNarration);
    private bool CanUseAudio(int choice) => model.Project is { } project && (project.IsYouTube
        ? choice == 0 || project.HasCompleteExportNarration
        : choice == 0 ? project.HasLocalSource && project.HasAudio
        : choice == 1 ? project.HasLocalSource && project.HasCompleteExportNarration : project.HasCompleteExportNarration);
    private bool CanUseCaptions(int choice) => model.Project is { } project && (choice switch
    {
        0 => true,
        1 or 2 => project.CanExportText("transcript"),
        3 or 4 => project.CanExportText("combined"),
        5 => project.CanExportText("transcript") && project.CanExportText("combined"),
        _ => false
    });
    private bool CanSave() => model.Idle && model.Project != null && (sidebar.SelectedIndex switch
    {
        0 => CanUseVideoAudio(videoAudio) && CanUseCaptions(videoCaptions) && (videoAudio != 0 || videoCaptions != 0),
        1 => CanUseAudio(audioChoice),
        2 => model.Project.CanExportText(TextIds[textContent]),
        3 => CanUseVideoAudio(webAudio) && CanUseCaptions(webCaptions),
        _ => false
    }) && (replaceExisting.Visibility != Visibility.Visible || replaceExisting.IsChecked == true);

    private void UpdatePreview()
    {
        if (sidebar.SelectedIndex < 0) return;
        var project = model.Project;
        var section = sidebar.SelectedIndex;
        explanation.Text = section switch
        {
            0 when videoAudio == 0 => videoCaptions == 0 ? "Choose captions or Audio Description to create a video export." : "Save the original soundtrack with the selected captions.",
            0 when videoAudio == 1 => "Save the original soundtrack and a separate described soundtrack viewers can choose.",
            0 => "Save the video with synchronized Audio Description mixed into its soundtrack.",
            1 when project?.IsYouTube == true && audioChoice == 0 => "Download the original public YouTube soundtrack and save it in the selected format.",
            1 when project?.IsYouTube == true && audioChoice == 2 => "Download the original public YouTube soundtrack and mix it with synchronized Audio Description.",
            1 when project?.IsYouTube == true || audioChoice == 2 => "Save synchronized narration without the original soundtrack in the selected format.",
            1 when audioChoice == 0 => "Save the original video soundtrack in the selected format.",
            1 => "Save the original soundtrack mixed with synchronized Audio Description in the selected format.",
            2 when textContent == 1 => "Save the project summary as Markdown.",
            2 when textContent == 3 => "Save transcript and Audio Description segments together, including their timing.",
            2 => "Save the selected text and segment timing in the chosen format.",
            _ => "Save an adaptive HLS Web Playback folder with the selected audio and captions."
        };
        captionNotice.Visibility = section is 0 or 3 && (section == 0 ? videoCaptions : webCaptions) is 2 or 4 ? Visibility.Visible : Visibility.Collapsed;
        captionNotice.Text = "Open captions require a new video encode. Picture size is preserved and HDR is kept where possible, but quality or Dolby Vision information may change. Use closed captions for the closest match to the source.";
        var target = TargetRelativePath();
        if (lastTarget != target) { lastTarget = target; replaceExisting.IsChecked = false; }
        var exists = project != null && target.Length > 0 && (File.Exists(project.Resolve(target)) || Directory.Exists(project.Resolve(target)));
        replaceExisting.Visibility = replacementNotice.Visibility = exists ? Visibility.Visible : Visibility.Collapsed;
        replacementNotice.Text = exists ? $"{Path.GetFileName(target)} already exists. Replacing it saves the new export in its place; the old export is kept if generation fails." : "";
        saveButton.Content = exists ? "Replace export" : "Save";
        saveButton.IsEnabled = CanSave();
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
                else command = audio == 1 ? "export-closed-audio-description-video" : "export-open-audio-description-video";
                args.AddRange(new[] { "--captions", captionMode, "--content", content });
                if (sidebar.SelectedIndex == 0 && command != "export-original-video") args.AddRange(new[] { "--media-format", videoFormat });
                break;
            case 1:
                command = model.Project?.IsYouTube == true ? audioChoice switch
                { 0 => "export-youtube-original-audio", 1 => "export-audio", _ => "export-youtube-described-audio" }
                : audioChoice switch { 0 => "export-original-audio", 1 => "export-described-audio", _ => "export-audio" };
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

    private string TargetRelativePath()
    {
        if (model.Project is not { } project || sidebar.SelectedIndex < 0) return "";
        var name = Regex.Replace(project.Name.Trim(), "[<>:\"/\\\\|?*\\x00-\\x1F\\x7F]+", "-");
        name = Regex.Replace(Regex.Replace(name, @"\s*-\s*", " - "), @"\s+", " ").Trim(' ', '.', '-');
        if (name.Length > 100) name = name[..100].Trim(' ', '.', '-');
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
        return $"exports/{name} - {type}" + (extension.Length == 0 ? "" : "." + extension);
    }
}
