using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;
using Vidi_Studio_Windows.Models;
using Windows.Storage.Pickers;
using Windows.Media.Core;
using Windows.Media.Playback;

namespace Vidi_Studio_Windows;

public sealed partial class MainWindow
{
    private readonly MediaPlayer originalAudioPlayer = new();
    private string? originalAudioPath;
    private bool originalAudioReady, originalAudioMode;

    private void InitializeOriginalAudio()
    {
        originalAudioPlayer.MediaOpened += (_, _) => DispatcherQueue.TryEnqueue(() => originalAudioReady = true);
        originalAudioPlayer.MediaFailed += (_, _) => DispatcherQueue.TryEnqueue(() =>
        {
            originalAudioReady = false;
            StopProjectPlayback();
            Model.ReportStatus("The imported original audio could not be played. Choose Audio File to repair it.");
        });
    }

    private async Task SyncOriginalAudioAsync(StudioProject project, JsonElement state)
    {
        var selected = project.AudioSources?.Playback == "imported";
        if (originalAudioMode != selected)
        {
            originalAudioMode = selected;
            await YouTubePlayer.ExecuteScriptAsync("window.vidiOriginalAudio?.(" + (selected ? "true" : "false") + ")");
        }
        var asset = project.OriginalAudio;
        var valid = selected && asset != null && File.Exists(project.Resolve(asset.Path)) && !asset.NeedsConfirmation(project.Duration);
        if (!valid)
        {
            originalAudioPlayer.Pause();
            originalAudioPlayer.Source = null;
            originalAudioPath = null;
            if (selected && youtubePlaying)
            {
                StopProjectPlayback();
                Model.ReportStatus("Imported original audio is missing or its timing needs confirmation. Review Audio sources.");
            }
            return;
        }
        var path = project.Resolve(asset!.Path);
        if (originalAudioPath != path)
        {
            originalAudioReady = false;
            originalAudioPath = path;
            originalAudioPlayer.Source = MediaSource.CreateFromUri(new Uri(path));
        }
        if (!originalAudioReady) return;
        var target = Math.Min(Math.Max(0, youtubePosition), asset.Duration);
        if (Math.Abs(originalAudioPlayer.PlaybackSession.Position.TotalSeconds - target) > .1)
            originalAudioPlayer.PlaybackSession.Position = TimeSpan.FromSeconds(target);
        if (state.TryGetProperty("rate", out var rate)) originalAudioPlayer.PlaybackSession.PlaybackRate = rate.GetDouble();
        var volume = state.TryGetProperty("volume", out var level) ? level.GetDouble() / 100 : 1;
        var duck = (Narration.IsOn && (narrationPlayer.PlaybackSession.PlaybackState == MediaPlaybackState.Playing || onDeviceNarrator.IsPlaying)) || indicationPlayer.PlaybackSession.PlaybackState == MediaPlaybackState.Playing;
        originalAudioPlayer.Volume = volume * (duck ? .2 : 1);
        if (youtubePlaying && youtubePosition < asset.Duration) originalAudioPlayer.Play();
        else originalAudioPlayer.Pause();
    }

    internal async Task<string?> PickOriginalAudioAsync()
    {
        var picker = new FileOpenPicker();
        foreach (var extension in new[] { ".wav", ".mp3", ".m4a", ".aac", ".flac" }) picker.FileTypeFilter.Add(extension);
        WinRT.Interop.InitializeWithWindow.Initialize(picker, WinRT.Interop.WindowNative.GetWindowHandle(this));
        return (await picker.PickSingleFileAsync())?.Path;
    }

    internal async Task<bool> ImportOriginalAudioAsync(string? file, bool? playback = null, bool? transcription = null)
    {
        if (Model.Busy || Model.Project is not { IsYouTube: true } project) return false;
        var output = Path.Combine(Path.GetTempPath(), "vidistudio-audio-" + Guid.NewGuid() + ".json");
        try
        {
            var inspectionArgs = new List<string> { "--output", output };
            if (file != null) inspectionArgs.AddRange(new[] { "--input", file });
            if ((await Model.RunAsync("inspect-original-audio", inspectionArgs)).Count == 0 || !File.Exists(output)) return false;
            var inspection = JsonSerializer.Deserialize<OriginalAudioInspection>(File.ReadAllText(output), Models.Json.Options)
                ?? throw new IOException("The original audio could not be inspected.");
            var accepted = false;
            if (inspection.Status != "matched")
            {
                var warning = new ContentDialog
                {
                    Title = inspection.Status == "unverified" ? "Duration could not be verified" : "Audio duration does not match",
                    Content = inspection.SourceName + "\n\n" + inspection.Warning,
                    CloseButtonText = "Cancel", PrimaryButtonText = "Import Anyway", SecondaryButtonText = "Choose Another File",
                    DefaultButton = ContentDialogButton.Close
                };
                var answer = await ShowDialogAsync(warning, 620, 480);
                if (answer == ContentDialogResult.Secondary)
                {
                    var chosen = await PickOriginalAudioAsync();
                    return chosen != null && await ImportOriginalAudioAsync(chosen, playback, transcription);
                }
                if (answer != ContentDialogResult.Primary) return false;
                accepted = true;
            }
            if (file != null && project.OriginalAudio != null &&
                !await Confirm("Replace imported original audio?", "Replace " + project.OriginalAudio.SourceName + " with " + inspection.SourceName + "?", "Replace")) return false;
            var args = new List<string>();
            args.AddRange(new[] { "--expected-audio-sha256", inspection.Sha256, "--expected-video-duration", (inspection.VideoDuration ?? 0).ToString(System.Globalization.CultureInfo.InvariantCulture) });
            if (file != null) args.AddRange(new[] { "--input", file, "--replace-existing" });
            if (accepted) args.Add("--allow-audio-mismatch");
            if (playback.HasValue) args.AddRange(new[] { "--playback-audio-source", playback.Value ? "imported" : "youtube" });
            if (transcription.HasValue) args.AddRange(new[] { "--transcription-audio-source", transcription.Value ? "imported" : "youtube" });
            return (await Model.RunAsync(file == null ? "confirm-original-audio" : "import-original-audio", args)).Count > 0;
        }
        catch (Exception ex) { await ShowError(ex); return false; }
        finally { if (File.Exists(output)) File.Delete(output); }
    }

    private async Task RecoverYouTubeAudioExportAsync(ViewModels.YouTubeAudioExportRecovery recovery)
    {
        try
        {
            if (Model.Busy || Model.Project?.Folder != recovery.ProjectFolder) return;
            var dialog = new ContentDialog
            {
                Title = "YouTube Audio Couldn’t Be Retrieved",
                Content = "Choose a matching audio file to retry this export with the same format and narration choices. The file will be checked against the video duration before use. Existing exports are unchanged.",
                PrimaryButtonText = "Choose Audio File…", CloseButtonText = "Cancel",
                DefaultButton = ContentDialogButton.Close
            };
            if (await ShowDialogAsync(dialog, 680, 460) != ContentDialogResult.Primary) return;
            var file = await PickOriginalAudioAsync();
            if (file == null || Model.Project?.Folder != recovery.ProjectFolder) return;
            if (!await ImportOriginalAudioAsync(file) || Model.Project?.Folder != recovery.ProjectFolder) return;
            var arguments = new List<string>(recovery.Arguments);
            while (arguments.IndexOf("--soundtrack-source") is var index && index >= 0)
            {
                arguments.RemoveAt(index);
                if (index < arguments.Count) arguments.RemoveAt(index);
            }
            arguments.AddRange(new[] { "--soundtrack-source", "imported" });
            await Model.RunAsync(recovery.Command, arguments);
        }
        catch (Exception ex) { await ShowError(ex); }
    }

    internal async Task ChooseOriginalAudioAsync()
    {
        var file = await PickOriginalAudioAsync();
        if (file == null) return;
        var choice = new ContentDialog
        {
            Title = "Use imported original audio", Content = Path.GetFileName(file), CloseButtonText = "Cancel",
            PrimaryButtonText = "Use for exports only", SecondaryButtonText = "Use for playback and exports",
            DefaultButton = ContentDialogButton.Primary
        };
        var result = await ShowDialogAsync(choice, 660, 400);
        if (result != ContentDialogResult.None) await ImportOriginalAudioAsync(file, result == ContentDialogResult.Secondary);
    }

    internal async Task ShowAudioExportAsync() => await ShowDialogAsync(new Views.ExportDialog(Model, this), 950, 780);
    internal async Task ShowAudioSourcesAsync()
    {
        var dialog = new ContentDialog { Title = "Audio sources", CloseButtonText = "Done" };
        dialog.Content = new ScrollViewer { Content = new Views.AudioSourceControls(this, Model, dialog, false), MaxHeight = 600 };
        await ShowDialogAsync(dialog, 740, 800);
    }
    private async void AudioSources_Click(object sender, RoutedEventArgs e) => await ShowAudioSourcesAsync();

    private void UpdateAudioSourcePanel()
    {
        AudioSourcePanel.Children.Clear();
        AudioSourcePanel.Visibility = Model.Project?.IsYouTube == true ? Visibility.Visible : Visibility.Collapsed;
        if (Model.Project?.IsYouTube == true) AudioSourcePanel.Children.Add(new Views.AudioSourceControls(this, Model, showHeading: false));
        AudioSourcesButton.Visibility = Model.Project?.IsYouTube == true ? Visibility.Visible : Visibility.Collapsed;
        AudioSourcesSummary.Visibility = AudioSourcesButton.Visibility;
        if (Model.Project is { } project) {
            AudioSourcesSummary.Glyph = project.AudioSources?.Playback == "imported" ? "\uE8A5" : "\uE714";
            var label = "Playing: " + (project.AudioSources?.Playback == "imported" ? "Imported audio: " + project.OriginalAudio?.SourceName : "YouTube audio");
            ToolTipService.SetToolTip(AudioSourcesSummary, label);
            Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(AudioSourcesSummary, label);
        }
    }
}
