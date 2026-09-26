using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using System;
using System.Collections.Generic;
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
    private int videoAudio = 1, videoCaptions, audioChoice = 1, webAudio = 1, webCaptions, textContent, textFormat;

    public ExportDialog(StudioViewModel model)
    {
        this.model = model;
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
            var (command, arguments) = BuildRequest();
            Closed += async (_, _) => await model.RunAsync(command, arguments);
            Hide();
        };
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
        sidebar.SelectedIndex = model.Project?.IsYouTube == true ? 2 : 0;
    }

#if DEBUG
    internal StackPanel FooterForSmokeTest => footer;
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
        UpdatePreview();
    }

    private static Border Card(UIElement child) => new()
    {
        Child = child, CornerRadius = new CornerRadius(10), Padding = new Thickness(16, 12, 16, 12),
        Background = new SolidColorBrush(Color.FromArgb(18, 128, 128, 128))
    };

    private void AddChoices(string title, string prompt, string[] labels, int selected, Action<int> changed)
    {
        var group = new StackPanel { Spacing = 8 };
        group.Children.Add(new TextBlock { Text = title, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold, FontSize = 16 });
        var buttons = new RadioButtons { Header = prompt };
        foreach (var label in labels) buttons.Items.Add(label);
        buttons.SelectedIndex = selected;
        buttons.SelectionChanged += (_, _) =>
        { if (buttons.SelectedIndex >= 0) { changed(buttons.SelectedIndex); UpdatePreview(); } };
        group.Children.Add(Card(buttons)); page.Children.Add(group);
    }

    private void BuildVideo()
    {
        AddChoices("Audio", "Audio included in the video",
            new[] { "Original audio only", "Closed Audio Description", "Open Audio Description" },
            videoAudio, index => videoAudio = index);
        AddChoices("Transcript and captions", "Transcript included in the video", CaptionLabels,
            videoCaptions, index => videoCaptions = index);
    }

    private void BuildAudio()
    {
        var youtube = model.Project?.IsYouTube == true;
        var labels = youtube ? new[] { "Narration audio only — M4A" } :
            new[] { "Original video audio — MP3", "Final audio with Audio Description — MP3", "Narration audio only — M4A" };
        AddChoices("Audio", "Audio to save", labels, youtube ? 0 : audioChoice,
            index => audioChoice = youtube ? 2 : index);
    }

    private void BuildText()
    {
        var form = new StackPanel { Spacing = 12 };
        var content = new ComboBox { Header = "Content", ItemsSource = TextLabels,
            SelectedIndex = textContent, HorizontalAlignment = HorizontalAlignment.Stretch };
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
            webAudio, index => webAudio = index);
        AddChoices("Transcript and captions", "Transcript included in Web Playback", CaptionLabels,
            webCaptions, index => webCaptions = index);
    }

    private void UpdatePreview()
    {
        if (sidebar.SelectedIndex < 0) return;
        var project = model.Project;
        var section = sidebar.SelectedIndex;
        explanation.Text = section switch
        {
            0 when videoAudio == 0 => videoCaptions == 0 ? "Save an unchanged copy of the source video." : "Save the original soundtrack with the selected captions.",
            0 when videoAudio == 1 => "Save the original soundtrack and a separate described soundtrack viewers can choose.",
            0 => "Save the video with synchronized Audio Description mixed into its soundtrack.",
            1 when project?.IsYouTube == true || audioChoice == 2 => "Save synchronized narration without the original soundtrack as M4A.",
            1 when audioChoice == 0 => "Save the original video soundtrack as MP3.",
            1 => "Save the original soundtrack mixed with synchronized Audio Description as MP3.",
            2 when textContent == 1 => "Save the project summary as Markdown.",
            2 when textContent == 3 => "Save transcript and Audio Description segments together, including their timing.",
            2 => "Save the selected text and segment timing in the chosen format.",
            _ => "Save an adaptive HLS Web Playback folder with the selected audio and captions."
        };
        saveButton.IsEnabled = project != null && (section != 2 || textContent switch
        {
            0 => project.Transcript.Count > 0,
            1 => !string.IsNullOrWhiteSpace(project.Summary),
            2 => project.Cues.Count > 0,
            _ => project.Transcript.Count + project.Cues.Count > 0
        });
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
                break;
            case 1:
                command = (model.Project?.IsYouTube == true ? 2 : audioChoice) switch
                { 0 => "export-original-audio", 1 => "export-described-audio", _ => "export-audio" };
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
}
