using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Vidi_Studio_Windows.Models;
using Vidi_Studio_Windows.Services;
using Windows.Storage.Pickers;

namespace Vidi_Studio_Windows;

public sealed partial class MainWindow
{
    private async void ImportAssets_Click(object sender, RoutedEventArgs e)
    {
        try { await RunDesktopCommandAsync("import-assets"); }
        catch (OperationCanceledException) { }
        catch (Exception ex) { await ShowError(ex); }
    }

    internal async Task ImportExistingAssetsAsync(Models.ImportScope scope = Models.ImportScope.All)
    {
        if (Model.Busy || Model.Project is not { } project ||
            scope == Models.ImportScope.OriginalAudio && !project.AllowsOriginalAudioImport) return;
        var dialog = new Views.ImportAssetsDialog(this, project, scope);
        try {
            var result = await ShowDialogAsync(dialog, 800, 760);
            if (result is not (ContentDialogResult.Primary or ContentDialogResult.Secondary)) return;
            if (!ReferenceEquals(Model.Project, project)) return;
            await ImportSelectedAssetsAsync(dialog.Selection, dialog.CombineRequested);
        } finally { dialog.CleanupYouTubeCaptions(); }
    }

    private async Task ImportSelectedAssetsAsync(ImportAssetSelection selection, bool combine)
    {
        if (Model.Busy || Model.Project is not { } project || !selection.HasAny ||
            selection.OriginalAudioPath != null && !project.AllowsOriginalAudioImport) return;
        var timingFiles = new List<string>();
        try
        {
            if (selection.LocalPlaybackVideoPath == null && selection.OriginalAudioPath is { } original)
            {
                if (!await ImportOriginalAudioAsync(original, selection.OriginalAudioPlayback, selection.OriginalAudioTranscription)) return;
                project = Model.Project!;
            }
            if (selection.LocalPlaybackVideoPath is { } video)
            {
                if (!await AttachYouTubePlaybackVideoAsync(project, video)) return;
                project = Model.Project!;
            }
            async Task RunImport(string command, IEnumerable<string> arguments, Func<Task<string?>>? review = null)
            {
                if ((await RunImportWithWarningsAsync(command, arguments.Concat(new[] { "--language", project.SelectedLanguage }),
                    reviewTiming: review == null ? null : async () =>
                    {
                        var timing = await review();
                        if (timing != null) timingFiles.Add(timing);
                        return timing;
                    })).Count == 0)
                    throw new OperationCanceledException();
            }
            if (selection.TranscriptPath is { } transcript)
            {
                var arguments = new List<string> { "--input", transcript };
                if (selection.CaptionProvenancePath is { } provenance) arguments.AddRange(new[] { "--caption-provenance", provenance });
                await RunImport("import-transcript", arguments, () => ReviewTextTimingAsync(transcript, "narration", project));
            }
            if (selection.ADTranscriptPath is { } adTranscript)
                await RunImport("import-ad-transcript", new[] { "--input", adTranscript, "--ad-transcript-mode", selection.ADTranscriptMode },
                    () => ReviewTextTimingAsync(adTranscript, selection.ADTranscriptMode, project));
            if (selection.SummaryPath is { } summary)
                await RunImport("import-summary", new[] { "--input", summary });
            if (selection.ADAudioPath is { } audio)
                await RunImport("import-ad-audio", new[] { "--input", audio, "--audio-mode", selection.ADAudioMode });
            if (combine && selection.ADAudioPath != null)
                await Model.RunAsync("combine-imported-audio", (project.UsesStandaloneYouTubeAudio ? new[] { "--audio-only", "--media-format", "wav" } : new[] { "--audio-presentation", "selectable", "--captions", "none", "--media-format", "mov" }));
            if ((selection with { LocalPlaybackVideoPath = null }).HasAny) await ShowImportReportAsync();
        }
        catch (OperationCanceledException) { Model.ReportStatus("Import cancelled. Completed imports remain in the project."); }
        catch (Exception ex) { await ShowError(ex); }
        finally { foreach (var file in timingFiles) if (File.Exists(file)) File.Delete(file); }
    }

    private async Task<string?> ReviewTextTimingAsync(string path, string mode, StudioProject project)
    {
        try
        {
            var parsed = ImportTimingInspector.ReadRows(path);
            var dialog = new Views.TextTimingDialog(Path.GetFileName(path), project.Duration, parsed, mode, Model, project);
            if (await ShowDialogAsync(dialog, 940, 720) != ContentDialogResult.Primary) return null;
            return dialog.TimingPath;
        }
        catch (Exception ex)
        {
            await ShowError(ex);
            return null;
        }
    }

    private async Task ImportSegmentNarrationAsync(Segment segment)
    {
        if (Model.Busy || Model.Project is null) return;
        var picker = new FileOpenPicker();
        foreach (var extension in new[] { ".wav", ".mp3", ".m4a", ".aac", ".flac" }) picker.FileTypeFilter.Add(extension);
        WinRT.Interop.InitializeWithWindow.Initialize(picker, WinRT.Interop.WindowNative.GetWindowHandle(this));
        var file = await picker.PickSingleFileAsync();
        if (file == null) return;
        var replace = segment.Audio != null;
        if (replace && !await Confirm("Replace narration audio?", "The imported audio becomes an alternative after validation. Review it afterward to keep both, replace the selected result, or discard the new result.", "Replace")) return;
        try
        {
            var args = new List<string> { "--input", file.Path, "--segment", segment.Id };
            if (replace) args.Add("--replace-existing");
            await RunImportWithWarningsAsync("import-segment-narration", args);
        }
        catch (Exception ex) { await ShowError(ex); }
    }

    private async Task ShowImportReportAsync()
    {
        if (Model.Project?.LastImportReport is not { } report)
        {
            if (Model.Project is { HasDescriptionTimingIssues: true })
            {
                await ShowDialogAsync(new ContentDialog
                {
                    Title = "Description timing needs attention",
                    Content = Message("Descriptions overlap dialogue or each other. Fix the affected descriptions in Descriptions; described exports are blocked until then."),
                    CloseButtonText = "Close"
                });
            }
            return;
        }
        var text = new StringBuilder();
        foreach (var warning in report.Warnings ?? new List<string>()) text.AppendLine("Warning: " + warning);
        foreach (var row in report.Omitted ?? new List<ImportOmittedRow>())
            text.AppendLine($"Source line {row.SourceLine ?? 0}: {row.Reason} — {row.Text}");
        foreach (var flagged in report.Flagged ?? new List<ImportFlaggedSegment>())
        {
            text.AppendLine($"AD segment {flagged.Row ?? 0}: {flagged.Text}");
            foreach (var conflict in flagged.Conflicts ?? new List<ImportConflict>())
                text.AppendLine("  " + conflict.Description);
        }
        if (report.RowsFlagged > 0 || report.RowsOmittedAfterVideo > 0 || report.RowsOmittedInvalid > 0 || report.RowsOmittedTranscriptOverlap > 0) { if (!string.IsNullOrWhiteSpace(report.Guidance)) text.AppendLine(report.Guidance); }
        else text.AppendLine("No timing issues were reported.");
        var descriptionTiming = report.Kind == "narration";
        var canEditTimings = descriptionTiming ? Model.Project is { Cues.Count: > 0 }
            : report.Kind == "transcript" && Model.Project is { Transcript.Count: > 0 };
        var editTimingTitle = descriptionTiming ? "Edit Description Timings…" : "Edit Transcript Timings…";
        if (canEditTimings) text.AppendLine("\nChoose " + editTimingTitle + " to adjust row timing, or Close to finish.");
        var result = await ShowDialogAsync(new ContentDialog
        {
            Title = "Import Report",
            Content = new ScrollViewer
            {
                Content = new Views.ImportReportSummary(report, text.ToString()),
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                MaxHeight = 520
            },
            PrimaryButtonText = canEditTimings ? editTimingTitle : "",
            CloseButtonText = "Close",
            DefaultButton = canEditTimings ? ContentDialogButton.Primary : ContentDialogButton.Close
        }, 820, 620);
        if (result == ContentDialogResult.Primary && canEditTimings)
        {
            Section.SelectedIndex = descriptionTiming ? 2 : 0;
            if (!editMode) EditMode_Click(this, new RoutedEventArgs());
            Model.ReportStatus(descriptionTiming ? "Import complete. Edit a description row to adjust its timing." : "Import complete. Edit a transcript row to adjust its timing.");
        }
    }

    private static TextBlock Message(string value) => new() { Text = value, TextWrapping = TextWrapping.Wrap };
}
