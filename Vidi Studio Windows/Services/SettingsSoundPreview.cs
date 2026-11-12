using System;
using System.IO;
using Windows.Media.Core;
using Windows.Media.Playback;

namespace Vidi_Studio_Windows.Services;

public sealed class SettingsSoundPreview : IDisposable
{
    private readonly Action<Action> dispatch;
    private MediaPlayer? player;
    public string? Resource { get; private set; }
    public bool Playing { get; private set; }
    public event Action? Changed;
    public SettingsSoundPreview(Action<Action> dispatch) { this.dispatch = dispatch; }
    public bool IsPlaying(string sound) => Resource == sound && Playing;
    public void Toggle(string sound)
    {
        if (Resource == sound && player != null)
        {
            if (Playing) { player.Pause(); Playing = false; }
            else { player.Play(); Playing = true; }
            Changed?.Invoke();
            return;
        }
        Stop();
        var path = Path.Combine(AppContext.BaseDirectory, "Assets", "Sounds", sound + ".wav");
        if (!File.Exists(path)) return;
        try
        {
            var next = new MediaPlayer { AudioCategory = MediaPlayerAudioCategory.SoundEffects };
            player = next; Resource = sound;
            next.MediaEnded += (_, _) => dispatch(() => { if (ReferenceEquals(player, next)) Stop(); });
            next.MediaFailed += (_, _) => dispatch(() => { if (ReferenceEquals(player, next)) Stop(); });
            next.Source = MediaSource.CreateFromUri(new Uri(path));
            Playing = true; next.Play(); Changed?.Invoke();
        }
        catch { Stop(); }
    }
    public void Stop()
    {
        var old = player; player = null; Resource = null; Playing = false;
        old?.Dispose(); Changed?.Invoke();
    }
    public void Dispose() => Stop();
}
