using System;
using System.IO;
using Windows.Media.Core;
using Windows.Media.Playback;

namespace Vidi_Studio_Windows.Services;

/// <summary>Retains the player until the completion cue has finished.</summary>
public static class ExportCompletionSound
{
    private static MediaPlayer? player;
#if DEBUG
    internal static int PlayCountForSmokeTest { get; private set; }
#endif

    public static bool ShouldPlay(string command, bool enabled, bool succeeded, bool cancelled, bool hasOutput) =>
        enabled && succeeded && !cancelled && hasOutput &&
        (command.StartsWith("export-", StringComparison.Ordinal) || command == "combine-imported-audio");

    public static void Complete(string command, bool enabled, bool succeeded, bool cancelled, bool hasOutput)
    {
        if (!ShouldPlay(command, enabled, succeeded, cancelled, hasOutput)) return;
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
        catch { /* An unavailable audio device must not turn a successful export into a failure. */ }
    }
}
