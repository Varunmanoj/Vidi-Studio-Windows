using System;
using System.IO;
using Windows.Media.Core;
using Windows.Media.Playback;

namespace Vidi_Studio_Windows.Services;

/// <summary>Retains the player until the completion cue has finished.</summary>
public static class CompletionSound
{
    private static MediaPlayer? player;
#if DEBUG
    internal static int PlayCountForSmokeTest { get; private set; }
#endif

    public static bool ShouldPlay(string command, bool exportEnabled, bool generationEnabled, bool succeeded,
        bool cancelled, bool hasExportOutput, bool hasGeneratedProject)
    {
        if (!succeeded || cancelled) return false;
        if (command.StartsWith("export-", StringComparison.Ordinal) || command == "combine-imported-audio")
            return exportEnabled && hasExportOutput;
        return generationEnabled && hasGeneratedProject && command is
            ("transcribe" or "generate-summary" or "generate-ad-cues" or "analyze" or "regenerate-all" or
             "reanalyze-transcript-segment" or "reanalyze-segment");
    }

    public static void Complete(string command, bool exportEnabled, bool generationEnabled, bool succeeded,
        bool cancelled, bool hasExportOutput, bool hasGeneratedProject)
    {
        if (!ShouldPlay(command, exportEnabled, generationEnabled, succeeded, cancelled, hasExportOutput, hasGeneratedProject)) return;
        try
        {
            var path = Path.Combine(AppContext.BaseDirectory, "Assets", "Sounds", "descend.wav");
            if (!File.Exists(path)) return;
            player?.Dispose();
            player = new MediaPlayer { AudioCategory = MediaPlayerAudioCategory.SoundEffects };
            player.Source = MediaSource.CreateFromUri(new Uri(path));
            player.Play();
#if DEBUG
            PlayCountForSmokeTest++;
#endif
        }
        catch { /* An unavailable audio device must not turn a successful operation into a failure. */ }
    }
}
