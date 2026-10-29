using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using System;
using System.IO;
using System.Threading.Tasks;
using Windows.Storage;
using Windows.Storage.Pickers;
using Vidi_Studio_Windows.Models;

namespace Vidi_Studio_Windows.Views;

/// <summary>Collects existing transcript, summary, AD transcript, and AD audio imports.</summary>
public sealed class ImportAssetsDialog : ContentDialog
{
    private readonly Window owner;
    private readonly ImportScope scope;
    private readonly TextBlock transcriptName = new() { Text = "None", Opacity = .7, TextTrimming = TextTrimming.CharacterEllipsis };
    private readonly TextBlock summaryName = new() { Text = "None", Opacity = .7, TextTrimming = TextTrimming.CharacterEllipsis };
    private readonly TextBlock adTranscriptName = new() { Text = "None", Opacity = .7, TextTrimming = TextTrimming.CharacterEllipsis };
    private readonly TextBlock adAudioName = new() { Text = "None", Opacity = .7, TextTrimming = TextTrimming.CharacterEllipsis };
    private readonly ComboBox adTranscriptMode = new() { SelectedIndex = 0, MinWidth = 240 };
    private readonly ComboBox adAudioMode = new() { SelectedIndex = 0, MinWidth = 240 };
    private readonly CheckBox replace = new() { Content = "Replace existing assets after validation" };

    public string? TranscriptPath { get; private set; }
    public string? SummaryPath { get; private set; }
    public string? ADTranscriptPath { get; private set; }
    public string? ADAudioPath { get; private set; }
    public bool ReplaceExisting => replace.IsChecked == true;
    public string ADTranscriptMode => adTranscriptMode.SelectedIndex == 0 ? "narration" : "combined";
    public string ADAudioMode => adAudioMode.SelectedIndex == 0 ? "narration" : "fullMix";

    public ImportAssetsDialog(Window owner, ImportScope scope = ImportScope.All)
    {
        this.owner = owner;
        this.scope = scope;
        Title = scope switch
        {
            ImportScope.Transcript => "Import Transcript",
            ImportScope.Summary => "Import Summary",
            ImportScope.Descriptions => "Import Descriptions",
            _ => "Import Existing Assets"
        };
        PrimaryButtonText = "Import";
        CloseButtonText = "Cancel";
        DefaultButton = ContentDialogButton.Primary;
        adTranscriptMode.Items.Add("AD narration only — editable description segments");
        adTranscriptMode.Items.Add("Dialogue + AD combined — one caption/export track");
        adAudioMode.Items.Add("AD narration audio only — mixed with original audio");
        adAudioMode.Items.Add("Full AD mix — used directly as the described track");
        ToolTipService.SetToolTip(adTranscriptMode, "Choose whether this AD transcript creates editable descriptions or one combined caption track");
        ToolTipService.SetToolTip(adAudioMode, "Choose narration-only audio or a full AD mix");
        ToolTipService.SetToolTip(replace, "Replace existing assets only after the import passes validation");
        Content = BuildContent();
    }

    private UIElement BuildContent()
    {
        var panel = new StackPanel { Spacing = 10, Width = 640 };
        panel.Children.Add(new TextBlock
        {
            Text = scope switch
            {
                ImportScope.Transcript => "Choose an SRT, VTT, or TXT transcript. TXT opens the timing editor.",
                ImportScope.Summary => "Choose a Markdown or TXT summary. A transcript is not required.",
                ImportScope.Descriptions => "Choose an AD transcript. AD-only text becomes editable descriptions and requires an existing transcript.",
                _ => "Files are copied into the project folder. Transcripts: SRT, VTT, TXT. Summaries: Markdown, TXT. Audio: WAV, MP3, M4A, AAC, FLAC. TXT import opens the timing editor."
            },
            TextWrapping = TextWrapping.Wrap, Opacity = .75
        });
        if (scope is ImportScope.All or ImportScope.Transcript)
        panel.Children.Add(Row("Transcript", transcriptName, async () =>
        {
            TranscriptPath = await Pick("Transcript", new[] { ".srt", ".vtt", ".txt" });
            transcriptName.Text = TranscriptPath == null ? "None" : Path.GetFileName(TranscriptPath);
        }));
        if (scope is ImportScope.All or ImportScope.Summary)
        panel.Children.Add(Row("Summary", summaryName, async () =>
        {
            SummaryPath = await Pick("Summary", new[] { ".md", ".markdown", ".txt" });
            summaryName.Text = SummaryPath == null ? "None" : Path.GetFileName(SummaryPath);
        }));
        if (scope is ImportScope.All or ImportScope.Descriptions)
        panel.Children.Add(Row("AD transcript", adTranscriptName, async () =>
        {
            ADTranscriptPath = await Pick("AD transcript", new[] { ".srt", ".vtt", ".txt" });
            adTranscriptName.Text = ADTranscriptPath == null ? "None" : Path.GetFileName(ADTranscriptPath);
            adTranscriptMode.IsEnabled = ADTranscriptPath != null;
        }));
        adTranscriptMode.IsEnabled = false;
        if (scope is ImportScope.All or ImportScope.Descriptions) panel.Children.Add(adTranscriptMode);
        if (scope is ImportScope.All)
        panel.Children.Add(Row("AD audio", adAudioName, async () =>
        {
            ADAudioPath = await Pick("AD audio", new[] { ".wav", ".mp3", ".m4a", ".aac", ".flac" });
            adAudioName.Text = ADAudioPath == null ? "None" : Path.GetFileName(ADAudioPath);
            adAudioMode.IsEnabled = ADAudioPath != null;
        }));
        adAudioMode.IsEnabled = false;
        if (scope is ImportScope.All)
        {
            panel.Children.Add(adAudioMode);
            panel.Children.Add(new TextBlock
            {
                Text = "Combining creates Original Audio and English - Audio Description tracks. Viewers can switch between them. Project > Import Existing Assets can combine again later.",
                TextWrapping = TextWrapping.Wrap, Opacity = .75
            });
        }
        panel.Children.Add(replace);
        return new ScrollViewer { Content = panel, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
    }

    private UIElement Row(string label, TextBlock fileName, Func<Task> browse)
    {
        var grid = new Grid { ColumnSpacing = 8 };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(120) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var title = new TextBlock { Text = label, VerticalAlignment = VerticalAlignment.Center };
        Grid.SetColumn(title, 0);
        Grid.SetColumn(fileName, 1);
        var button = new Button { Content = "Choose…", Padding = new Thickness(10, 4, 10, 4) };
        ToolTipService.SetToolTip(button, "Choose a " + label.ToLowerInvariant() + " file");
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(button, "Choose " + label);
        button.Click += async (_, _) => await browse();
        Grid.SetColumn(button, 2);
        grid.Children.Add(title);
        grid.Children.Add(fileName);
        grid.Children.Add(button);
        return grid;
    }

    private async Task<string?> Pick(string title, string[] extensions)
    {
        var picker = new FileOpenPicker();
        foreach (var extension in extensions) picker.FileTypeFilter.Add(extension);
        WinRT.Interop.InitializeWithWindow.Initialize(picker, WinRT.Interop.WindowNative.GetWindowHandle(owner));
        var file = await picker.PickSingleFileAsync();
        return file?.Path;
    }
}
