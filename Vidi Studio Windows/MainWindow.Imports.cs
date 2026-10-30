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
using Windows.Storage.Pickers;

namespace Vidi_Studio_Windows;

public sealed partial class MainWindow
{
    private async void ImportAssets_Click(object sender, RoutedEventArgs e) => await ImportExistingAssetsAsync();

    internal async Task ImportExistingAssetsAsync(Models.ImportScope scope = Models.ImportScope.All)
    {
        if (Model.Busy || Model.Project is not { } project) return;
        var dialog = new Views.ImportAssetsDialog(this, project, scope);
        var result = await ShowDialogAsync(dialog, 800, 760);
        if (result is not (ContentDialogResult.Primary or ContentDialogResult.Secondary)) return;
        await ImportSelectedAssetsAsync(dialog.Selection, dialog.CombineRequested);
    }

    private async Task ImportSelectedAssetsAsync(ImportAssetSelection selection, bool combine)
    {
        if (Model.Busy || Model.Project is not { } project || !selection.HasAny) return;
        var timingFiles = new List<string>();
        try
        {
            // Validate/review every selected text file before writing any assets.
            string? transcriptTiming = null, descriptionTiming = null;
            if (selection.TranscriptPath is { } transcriptFile)
            {
                var (proceed, timing) = await ReviewTimingBeforeImportAsync(transcriptFile, false, "narration", project);
                if (!proceed) return;
                transcriptTiming = timing;
                if (timing != null) timingFiles.Add(timing);
            }
            if (selection.ADTranscriptPath is { } descriptionFile)
            {
                var (proceed, timing) = await ReviewTimingBeforeImportAsync(descriptionFile, selection.ADTranscriptMode == "narration", selection.ADTranscriptMode, project);
                if (!proceed) return;
                descriptionTiming = timing;
                if (timing != null) timingFiles.Add(timing);
            }
            var replace = selection.ReplaceExisting ? new[] { "--replace-existing" } : Array.Empty<string>();
            var replacements = new List<string>();
            if (selection.TranscriptPath != null && project.Transcript.Count > 0) replacements.Add("Transcript");
            if (selection.SummaryPath != null && !string.IsNullOrWhiteSpace(project.Summary)) replacements.Add("Summary");
            if (selection.ADTranscriptPath != null && (project.Cues.Count > 0 || project.ImportedADTranscript != null)) replacements.Add("AD transcript");
            if (selection.ADAudioPath != null && project.ImportedAudioDescription != null) replacements.Add("AD audio");
            if (replacements.Count > 0)
            {
                if (!await Confirm("Replace existing assets?", "This will replace: " + string.Join(", ", replacements) + ". Files are validated before replacement.", "Replace and Import")) return;
                replace = new[] { "--replace-existing" };
            }
            async Task RunImport(string command, IEnumerable<string> arguments)
            {
                if (await Model.RunAsync(command, arguments) is not { Count: > 0 })
                {
                    if (Model.Status.StartsWith("Cancelled", StringComparison.Ordinal)) throw new OperationCanceledException();
                    throw new IOException(Model.Status);
                }
            }
            if (selection.TranscriptPath is { } transcript)
            {
                var arguments = new List<string> { "--input", transcript };
                if (transcriptTiming != null) arguments.AddRange(new[] { "--timing-input", transcriptTiming });
                arguments.AddRange(replace);
                await RunImport("import-transcript", arguments);
            }
            if (selection.ADTranscriptPath is { } adTranscript)
            {
                var arguments = new List<string> { "--input", adTranscript, "--ad-transcript-mode", selection.ADTranscriptMode };
                if (descriptionTiming != null) arguments.AddRange(new[] { "--timing-input", descriptionTiming });
                arguments.AddRange(replace);
                await RunImport("import-ad-transcript", arguments);
            }
            if (selection.SummaryPath is { } summary)
                await RunImport("import-summary", new[] { "--input", summary }.Concat(replace));
            if (selection.ADAudioPath is { } audio)
                await RunImport("import-ad-audio", new[] { "--input", audio, "--audio-mode", selection.ADAudioMode }.Concat(replace));
            if (combine && selection.ADAudioPath != null && !project.IsYouTube)
                await RunImport("combine-imported-audio", new[] { "--audio-presentation", "selectable", "--captions", "none", "--media-format", "mov" }.Concat(replace));
            await ShowImportReportAsync();
        }
        catch (OperationCanceledException) { Model.ReportStatus("Import cancelled. Completed imports remain in the project."); }
        catch (Exception ex) { await ShowError(ex); }
        finally { foreach (var file in timingFiles) if (File.Exists(file)) File.Delete(file); }
    }

    private async Task<(bool Proceed, string? Timing)> ReviewTimingBeforeImportAsync(string path, bool description, string mode, StudioProject project)
    {
        if (Path.GetExtension(path).Equals(".txt", StringComparison.OrdinalIgnoreCase))
        {
            var timing = await ReviewTextTimingAsync(path, mode, project);
            return (timing != null, timing);
        }
        var warnings = ImportTimingInspector.Warnings(path, project.Duration, project, description);
        if (warnings.Count == 0) return (true, null);
        var warningDialog = new ContentDialog
        {
            Title = "Timing Warnings",
            Content = new ScrollViewer
            {
                Content = new TextBlock
                {
                    Text = string.Join("\n\n", warnings) + "\n\nYou can import anyway and edit transcript timings afterward.",
                    TextWrapping = TextWrapping.Wrap
                },
                MaxHeight = 420
            },
            PrimaryButtonText = "Import Anyway",
            SecondaryButtonText = "Review Timing…",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Primary
        };
        var result = await ShowDialogAsync(warningDialog, 720, 520);
        if (result == ContentDialogResult.Primary) return (true, null);
        if (result != ContentDialogResult.Secondary) return (false, null);
        var reviewed = await ReviewTextTimingAsync(path, mode, project);
        return (reviewed != null, reviewed);
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
        if (replace && !await Confirm("Replace narration audio?", "The existing narration file is replaced after the imported audio passes validation.", "Replace")) return;
        try
        {
            var args = new List<string> { "--input", file.Path, "--segment", segment.Id };
            if (replace) args.Add("--replace-existing");
            await Model.RunAsync("import-segment-narration", args);
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
        text.AppendLine($"{report.KindTitle}: {report.SourceName}");
        text.AppendLine(report.DurationText);
        text.AppendLine($"{report.RowsImported} imported · {report.RowsFlagged} flagged for retiming · {report.RowsOmittedAfterVideo} ended after the video · {report.RowsOmittedInvalid} invalid or empty");
        foreach (var warning in report.Warnings ?? new List<string>()) text.AppendLine("Warning: " + warning);
        foreach (var row in report.Omitted ?? new List<ImportOmittedRow>())
            text.AppendLine($"Source line {row.SourceLine ?? 0}: {row.Reason} — {row.Text}");
        foreach (var flagged in report.Flagged ?? new List<ImportFlaggedSegment>())
        {
            text.AppendLine($"AD segment {flagged.Row ?? 0}: {flagged.Text}");
            foreach (var conflict in flagged.Conflicts ?? new List<ImportConflict>())
                text.AppendLine("  " + conflict.Description);
        }
        if (!string.IsNullOrWhiteSpace(report.Guidance)) text.AppendLine(report.Guidance);
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
                Content = new TextBlock { Text = text.ToString(), TextWrapping = TextWrapping.Wrap, IsTextSelectionEnabled = true, FontFamily = new FontFamily("Cascadia Mono") },
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
