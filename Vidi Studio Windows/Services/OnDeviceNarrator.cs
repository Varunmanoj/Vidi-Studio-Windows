using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Windows.Media.Core;
using Windows.Media.Playback;
using Windows.Media.SpeechSynthesis;

namespace Vidi_Studio_Windows.Services;

public sealed class OnDeviceNarrator : IDisposable
{
    private readonly MediaPlayer player = new();
    private readonly SettingsStore settings;
    private readonly EngineClient engine;
    private CancellationTokenSource? current;
    private string? temporary;
    public OnDeviceNarrator(SettingsStore settings, EngineClient engine) { this.settings = settings; this.engine = engine; }
    /// <summary>Raised with a message safe to show when live narration cannot speak.</summary>
    public event Action<string>? Failed;
    public void Stop()
    {
        current?.Cancel(); current = null; player.Pause(); player.Source = null;
        if (temporary != null) { try { File.Delete(temporary); } catch (IOException) { } temporary = null; }
    }
    public async Task SpeakAsync(string text)
    {
        Stop();
        using var cancellation = new CancellationTokenSource(); current = cancellation;
        string? path = null;
        try
        {
            var mode = settings.Get("ttsMode", "windows-core");
            if (mode == "sapi5")
            {
                Directory.CreateDirectory(SettingsStore.DirectoryPath);
                path = Path.Combine(SettingsStore.DirectoryPath, "live-" + Guid.NewGuid() + ".wav");
                var host = settings.Get("sapiHostPath"); if (string.IsNullOrWhiteSpace(host)) host = engine.SpeechHost;
                await SpeechService.SynthesizeAsync(host, mode, settings.Get("systemVoice"), text, path, settings.Number("systemSpeechRate"), cancellation.Token);
                cancellation.Token.ThrowIfCancellationRequested();
                temporary = path;
                player.Source = MediaSource.CreateFromUri(new Uri(path));
                player.Volume = settings.Number("systemSpeechVolume");
            }
            else
            {
                using var synth = new SpeechSynthesizer();
                // Cloud generation (Gemini, Azure) still uses a local voice for on-device playback.
                var voiceId = mode is "windows-core" or "system" ? settings.Get("systemVoice") : "";
                if (!string.IsNullOrEmpty(voiceId))
                {
                    bool found = false;
                    foreach (var voice in SpeechSynthesizer.AllVoices) if (voice.Id == voiceId) { synth.Voice = voice; found = true; break; }
                    if (!found) throw new InvalidOperationException("The selected Windows Core voice is no longer installed.");
                }
                synth.Options.SpeakingRate = Math.Clamp(settings.Number("systemSpeechRate"), .5, 2);
                synth.Options.AudioPitch = Math.Clamp(settings.Number("systemSpeechPitch"), .5, 2);
                synth.Options.AudioVolume = Math.Clamp(settings.Number("systemSpeechVolume"), 0, 1);
                var stream = await synth.SynthesizeTextToStreamAsync(text).AsTask(cancellation.Token);
                cancellation.Token.ThrowIfCancellationRequested();
                player.Volume = 1; player.Source = MediaSource.CreateFromStream(stream, stream.ContentType);
            }
            player.Play();
        }
        catch (OperationCanceledException) { if (path != null) File.Delete(path); }
        catch (Exception ex)
        {
            engine.Log(ex.ToString());
            if (path != null) File.Delete(path);
            Failed?.Invoke(ex is SpeechFailedException ? "Live narration: " + ex.Message : "Live narration could not speak with the selected voice. Check the voice in Settings → Narration.");
        }
        finally { if (current == cancellation) current = null; }
    }
    public void Dispose() { Stop(); player.Dispose(); }
}
