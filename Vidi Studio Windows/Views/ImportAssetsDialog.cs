using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using System;
using System.Collections.Generic;
using System.IO;
using Windows.Storage.Pickers;
using Vidi_Studio_Windows.Models;

namespace Vidi_Studio_Windows.Views;

/// <summary>Shared file selection for New Project and existing-project imports.</summary>
public sealed class ImportAssetsPicker : UserControl
{
    private readonly Window owner;
    private readonly StackPanel panel = new() { Spacing = 10 };
    private readonly StackPanel audioPanel = new() { Spacing = 10 };
    private readonly ComboBox adTranscriptMode = new() { HorizontalAlignment = HorizontalAlignment.Stretch };
    private readonly ComboBox adAudioMode = new() { HorizontalAlignment = HorizontalAlignment.Stretch };
    private readonly TextBlock validation = new() { TextWrapping = TextWrapping.Wrap };
    private string? transcript, summary, adTranscript, adAudio;
    private readonly List<Action> refreshRows = new();
    private bool youtube, hasTranscript;
    public event EventHandler? SelectionChanged;
    public ImportAssetSelection Selection => new(transcript, summary, adTranscript, youtube ? null : adAudio,
        adTranscriptMode.SelectedIndex == 0 ? "narration" : "combined",
        adAudioMode.SelectedIndex == 0 ? "narration" : "fullMix");
    public bool CanImport => Selection.HasAny && (adTranscript == null || adTranscriptMode.SelectedIndex != 0 || hasTranscript || transcript != null);

    public ImportAssetsPicker(Window owner, ImportScope scope = ImportScope.All, double width = 640)
    {
        this.owner = owner;
        Width = width;
        Content = panel;
        panel.Children.Add(new TextBlock
        {
            Text = "Choose existing files to copy into the project. SRT, VTT, or TXT for text; Markdown or TXT for summaries. Review text timing before importing.",
            TextWrapping = TextWrapping.Wrap, Opacity = .75
        });
        if (scope is ImportScope.All or ImportScope.Transcript)
            panel.Children.Add(Row("Transcript", new[] { ".srt", ".vtt", ".txt" }, () => transcript, value => transcript = value));
        if (scope is ImportScope.All or ImportScope.Summary)
            panel.Children.Add(Row("Summary", new[] { ".md", ".markdown", ".txt" }, () => summary, value => summary = value));
        if (scope is ImportScope.All or ImportScope.Descriptions)
        {
            panel.Children.Add(Row("AD transcript", new[] { ".srt", ".vtt", ".txt" }, () => adTranscript, value => adTranscript = value));
            AddMode(adTranscriptMode, "AD narration only — editable description segments", "Dialogue + AD combined — one caption/export track");
            ToolTipService.SetToolTip(adTranscriptMode, "Choose whether this AD transcript creates editable descriptions or one combined caption track");
            AutomationProperties.SetName(adTranscriptMode, "AD transcript type");
            panel.Children.Add(adTranscriptMode);
        }
        if (scope == ImportScope.All)
        {
            audioPanel.Children.Add(Row("AD audio", new[] { ".wav", ".mp3", ".m4a", ".aac", ".flac" }, () => adAudio, value => adAudio = value));
            AddMode(adAudioMode, "AD narration audio only — mixed with original audio", "Full AD mix — used directly as the described track");
            ToolTipService.SetToolTip(adAudioMode, "Choose narration-only audio or a full AD mix");
            AutomationProperties.SetName(adAudioMode, "AD audio type");
            audioPanel.Children.Add(adAudioMode);
            audioPanel.Children.Add(new TextBlock
            {
                Text = "Optional Combine creates Original Audio and English - Audio Description tracks that viewers can switch between. Import alone opens the editor without exporting a video.",
                TextWrapping = TextWrapping.Wrap, Opacity = .75
            });
            panel.Children.Add(audioPanel);
        }
        panel.Children.Add(validation);
        adTranscriptMode.SelectionChanged += (_, _) => UpdateState();
        adAudioMode.SelectionChanged += (_, _) => UpdateState();
        UpdateState();
    }

    private static void AddMode(ComboBox box, params string[] options)
    {
        foreach (var option in options) box.Items.Add(new TextBlock { Text = option, TextWrapping = TextWrapping.Wrap, MaxWidth = 420 });
        box.SelectedIndex = 0;
    }

    public void SetContext(bool isYouTube, bool transcriptExists)
    {
        youtube = isYouTube; hasTranscript = transcriptExists;
        audioPanel.Visibility = youtube ? Visibility.Collapsed : Visibility.Visible;
        UpdateState();
    }

    private void UpdateState()
    {
        adTranscriptMode.IsEnabled = adTranscript != null;
        adAudioMode.IsEnabled = adAudio != null && !youtube;
        validation.Text = adTranscript != null && adTranscriptMode.SelectedIndex == 0 && !hasTranscript && transcript == null
            ? "Choose a transcript before importing AD-only descriptions, or choose Dialogue + AD combined."
            : youtube ? "YouTube projects support text assets. Audio combination requires a local video." : "";
        SelectionChanged?.Invoke(this, EventArgs.Empty);
    }

    private Grid Row(string label, string[] extensions, Func<string?> get, Action<string?> set)
    {
        var grid = new Grid { ColumnSpacing = 8 };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(90) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var name = new TextBlock { Text = "None", TextTrimming = TextTrimming.CharacterEllipsis, VerticalAlignment = VerticalAlignment.Center };
        var choose = new Button { Content = "Choose…", Padding = new Thickness(8, 4, 8, 4) };
        var remove = new Button { Content = new FontIcon { Glyph = "\uE711", FontSize = 12 }, Visibility = Visibility.Collapsed, Padding = new Thickness(7) };
        ToolTipService.SetToolTip(choose, "Choose a " + label.ToLowerInvariant() + " file");
        AutomationProperties.SetName(choose, "Choose " + label);
        ToolTipService.SetToolTip(remove, "Remove selected " + label.ToLowerInvariant());
        AutomationProperties.SetName(remove, "Remove selected " + label.ToLowerInvariant());
        void Refresh()
        {
            var path = get();
            name.Text = path == null ? "None" : Path.GetFileName(path);
            ToolTipService.SetToolTip(name, path ?? "No file selected");
            remove.Visibility = path == null ? Visibility.Collapsed : Visibility.Visible;
            UpdateState();
        }
        choose.Click += async (_, _) =>
        {
            try
            {
                var picker = new FileOpenPicker();
                foreach (var extension in extensions) picker.FileTypeFilter.Add(extension);
                WinRT.Interop.InitializeWithWindow.Initialize(picker, WinRT.Interop.WindowNative.GetWindowHandle(owner));
                var file = await picker.PickSingleFileAsync();
                if (file == null) return; // Keep the old selection when the picker is cancelled.
                set(file.Path); Refresh();
            }
            catch (Exception ex) { validation.Text = "The file chooser could not open: " + ex.Message; }
        };
        remove.Click += (_, _) => { set(null); Refresh(); };
        refreshRows.Add(Refresh);
        grid.Children.Add(new TextBlock { Text = label, VerticalAlignment = VerticalAlignment.Center });
        Grid.SetColumn(name, 1); grid.Children.Add(name);
        Grid.SetColumn(choose, 2); grid.Children.Add(choose);
        Grid.SetColumn(remove, 3); grid.Children.Add(remove);
        return grid;
    }

#if DEBUG
    internal void SetFilesForSmokeTest(ImportAssetSelection selection)
    {
        transcript = selection.TranscriptPath; summary = selection.SummaryPath;
        adTranscript = selection.ADTranscriptPath; adAudio = selection.ADAudioPath;
        adTranscriptMode.SelectedIndex = selection.ADTranscriptMode == "narration" ? 0 : 1;
        adAudioMode.SelectedIndex = selection.ADAudioMode == "narration" ? 0 : 1;
        foreach (var refresh in refreshRows) refresh();
        UpdateState();
    }
#endif
}

public sealed class ImportAssetsDialog : ContentDialog
{
    private readonly ImportAssetsPicker picker;
    private readonly CheckBox replace = new() { Content = "Replace existing assets after validation" };
    public ImportAssetSelection Selection => picker.Selection with { ReplaceExisting = replace.IsChecked == true };
    public bool CombineRequested { get; private set; }

    public ImportAssetsDialog(Window owner, StudioProject project, ImportScope scope = ImportScope.All)
    {
        Title = scope switch { ImportScope.Transcript => "Import Transcript", ImportScope.Summary => "Import Summary", ImportScope.Descriptions => "Import Descriptions", _ => "Import Existing Assets" };
        PrimaryButtonText = "Import"; CloseButtonText = "Cancel";
        DefaultButton = ContentDialogButton.Primary;
        Width = MaxWidth = 740;
        Resources["ContentDialogMaxWidth"] = 740d;
        picker = new ImportAssetsPicker(owner, scope);
        picker.SetContext(project.IsYouTube, project.Transcript.Count > 0);
        picker.SelectionChanged += (_, _) => UpdateState();
        var panel = new StackPanel { Spacing = 14, Width = 640 };
        panel.Children.Add(picker);
        ToolTipService.SetToolTip(replace, "Replace existing assets only after the import passes validation");
        panel.Children.Add(replace);
        Content = new ScrollViewer { Content = panel, MaxHeight = 600, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };
        PrimaryButtonClick += (_, args) => args.Cancel = !picker.CanImport;
        SecondaryButtonClick += (_, args) => { args.Cancel = !picker.CanImport; CombineRequested = !args.Cancel; };
        UpdateState();
    }

    private void UpdateState()
    {
        IsPrimaryButtonEnabled = picker.CanImport;
        SecondaryButtonText = picker.Selection.ADAudioPath == null ? "" : "Import & Combine";
        IsSecondaryButtonEnabled = picker.CanImport;
    }
}
