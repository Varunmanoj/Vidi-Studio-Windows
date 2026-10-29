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
        var dialog = new Views.ImportAssetsDialog(this, scope);
        if (await ShowDialogAsync(dialog, 760, 700) != ContentDialogResult.Primary) return;
        try
        {
            var replace = dialog.ReplaceExisting ? new[] { "--replace-existing" } : Array.Empty<string>();
            if (dialog.TranscriptPath is { } transcript)
            {
                var (proceed, timing) = await ReviewTimingBeforeImportAsync(transcript, false, "narration", project);
                if (!proceed) return;
                var arguments = new List<string> { "--input", transcript };
                if (timing != null) arguments.AddRange(new[] { "--timing-input", timing });
                arguments.AddRange(replace);
                await Model.RunAsync("import-transcript", arguments);
            }
            if (dialog.ADTranscriptPath is { } adTranscript)
            {
                var (proceed, timing) = await ReviewTimingBeforeImportAsync(adTranscript, true, dialog.ADTranscriptMode, project);
                if (!proceed) return;
                var arguments = new List<string> { "--input", adTranscript, "--ad-transcript-mode", dialog.ADTranscriptMode };
                if (timing != null) arguments.AddRange(new[] { "--timing-input", timing });
                arguments.AddRange(replace);
                await Model.RunAsync("import-ad-transcript", arguments);
            }
            if (dialog.SummaryPath is { } summary)
                await Model.RunAsync("import-summary", new[] { "--input", summary }.Concat(replace));
            if (dialog.ADAudioPath is { } audio)
                await Model.RunAsync("import-ad-audio", new[] { "--input", audio, "--audio-mode", dialog.ADAudioMode }.Concat(replace));
            if (dialog.ADAudioPath != null && !project.IsYouTube)
                await Model.RunAsync("combine-imported-audio", new[] { "--audio-presentation", "selectable", "--captions", "none", "--media-format", "mov" }.Concat(replace));
            await ShowImportReportAsync();
        }
        catch (Exception ex) { await ShowError(ex); }
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
        if (!Path.GetExtension(path).Equals(".txt", StringComparison.OrdinalIgnoreCase)) return null;
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
        var canEditTimings = Model.Project is { Transcript.Count: > 0 } && report.Kind is "transcript" or "narration";
        if (canEditTimings) text.AppendLine("\nChoose Edit Transcript Timings to adjust row timing, or Close to finish.");
        var result = await ShowDialogAsync(new ContentDialog
        {
            Title = "Import Report",
            Content = new ScrollViewer
            {
                Content = new TextBlock { Text = text.ToString(), TextWrapping = TextWrapping.Wrap, IsTextSelectionEnabled = true, FontFamily = new FontFamily("Cascadia Mono") },
                MaxHeight = 520
            },
            PrimaryButtonText = canEditTimings ? "Edit Transcript Timings…" : "",
            CloseButtonText = "Close",
            DefaultButton = canEditTimings ? ContentDialogButton.Primary : ContentDialogButton.Close
        }, 820, 620);
        if (result == ContentDialogResult.Primary && canEditTimings)
        {
            Section.SelectedIndex = 0;
            if (!editMode) EditMode_Click(this, new RoutedEventArgs());
            Model.ReportStatus("Import complete. Edit a transcript row to adjust its timing.");
        }
    }

    private static TextBlock Message(string value) => new() { Text = value, TextWrapping = TextWrapping.Wrap };
}
