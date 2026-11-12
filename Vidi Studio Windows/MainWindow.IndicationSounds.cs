using System;
using System.IO;
using Vidi_Studio_Windows.Models;
using Windows.Media.Core;
using Windows.Media.Playback;

namespace Vidi_Studio_Windows;

public sealed partial class MainWindow
{
    private readonly MediaPlayer indicationPlayer = new() { AudioCategory = MediaPlayerAudioCategory.SoundEffects };
    private string? indicatedCue;
    private void UpdateIndicationSound(Segment? cue, bool playing)
    {
        if (cue == null) { StopIndicationSound(true); return; }
        if (!playing) { indicationPlayer.Pause(); return; }
        var entering = indicatedCue != cue.Id;
        indicatedCue = cue.Id;
        var sound = !Narration.IsOn
            ? (Model.Settings.GetBool("soundWhenNarrationOff", true) ? "Boink3" : null)
            : Model.Settings.Get("narrationPlayback") != "ondevice" && !cue.AudioReady && Model.Settings.GetBool("soundWhenNarrationMissing", true) ? "ascend" : null;
        if (sound == null) { StopIndicationSound(false); return; }
        if (!entering) return;
        var path = Path.Combine(AppContext.BaseDirectory, "Assets", "Sounds", sound + ".wav");
        if (!File.Exists(path)) return;
        try { indicationPlayer.Source = MediaSource.CreateFromUri(new Uri(path)); indicationPlayer.Play(); }
        catch { /* Audio output availability must not interrupt video playback. */ }
    }
    private void StopIndicationSound(bool reset)
    {
        indicationPlayer.Pause(); indicationPlayer.Source = null;
        if (reset) indicatedCue = null;
    }
}
