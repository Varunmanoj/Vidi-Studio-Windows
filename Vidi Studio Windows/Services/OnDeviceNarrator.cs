using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Windows.Media.Core;
using Windows.Media.Playback;
using Windows.Media.SpeechSynthesis;

namespace Vidi_Studio_Windows.Services;

public sealed class OnDeviceNarrator : IDisposable
{
    private const int MaxPrepared = 8;
    private readonly MediaPlayer player = new();
    private readonly SettingsStore settings;
    private readonly EngineClient engine;
    private CancellationTokenSource? current;
    private string? temporary;
    private readonly Dictionary<string, string> prepared = new(StringComparer.Ordinal);
    private readonly HashSet<string> preparing = new(StringComparer.Ordinal);
    private readonly Queue<string> preparedOrder = new();
    public OnDeviceNarrator(SettingsStore settings, EngineClient engine) { this.settings = settings; this.engine = engine; }
    /// <summary>Raised with a message safe to show when live narration cannot speak.</summary>
    public event Action<string>? Failed;
    public void Stop()
    {
        current?.Cancel(); current = null; player.Pause(); player.Source = null;
        if (temporary != null) { try { File.Delete(temporary); } catch (IOException) { } temporary = null; }
    }

    /// <summary>Renders an upcoming cue before it starts so Piper speech begins on time.</summary>
    public void Prepare(string text)
    {
        if (string.IsNullOrWhiteSpace(text) || !text.Any(char.IsLetterOrDigit)) return;
        var mode = settings.Get("ttsMode", "windows-core");
        if (mode != "piper") return;
        var voice = settings.Get("piperVoice", settings.Get("systemVoice"));
        var key = CacheKey(text, mode, voice);
        lock (prepared)
        {
            if (prepared.ContainsKey(key) || preparing.Contains(key)) return;
            preparing.Add(key);
        }
        var host = settings.Get("sapiHostPath"); if (string.IsNullOrWhiteSpace(host)) host = engine.SpeechHost;
        var rate = settings.Number("systemSpeechRate");
        _ = Task.Run(async () =>
        {
            string? path = null;
            try
            {
                Directory.CreateDirectory(SettingsStore.DirectoryPath);
                path = Path.Combine(SettingsStore.DirectoryPath, "live-ready-" + Guid.NewGuid() + ".wav");
                await SpeechService.SynthesizeAsync(host, mode, voice, text, path, rate, CancellationToken.None);
                lock (prepared)
                {
                    if (!prepared.ContainsKey(key))
                    {
                        prepared[key] = path; preparedOrder.Enqueue(key);
                        while (preparedOrder.Count > MaxPrepared)
                        {
                            var oldest = preparedOrder.Dequeue();
                            if (prepared.Remove(oldest, out var stale)) { try { File.Delete(stale); } catch (IOException) { } }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                engine.Log(ex.ToString());
                if (path != null) { try { File.Delete(path); } catch (IOException) { } }
            }
            finally { lock (prepared) preparing.Remove(key); }
        });
    }

    private string CacheKey(string text, string mode, string voice)
    {
        var material = $"{mode}|{voice}|{settings.Number("systemSpeechRate"):0.###}|{settings.Number("systemSpeechPitch"):0.###}|{text}";
        return Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(material)));
    }

    public async Task SpeakAsync(string text)
    {
        Stop();
        using var cancellation = new CancellationTokenSource(); current = cancellation;
        string? path = null;
        try
        {
            var mode = settings.Get("ttsMode", "windows-core");
            if (mode is "sapi5" or "piper")
            {
                var voice = mode == "piper" ? settings.Get("piperVoice", settings.Get("systemVoice")) : settings.Get("systemVoice");
                string? ready = null;
                if (mode == "piper") lock (prepared) prepared.TryGetValue(CacheKey(text, mode, voice), out ready);
                if (ready != null && File.Exists(ready))
                {
                    // Already rendered while the previous cue played; start immediately.
                    temporary = null;
                    player.Source = MediaSource.CreateFromUri(new Uri(ready));
                    player.Volume = settings.Number("systemSpeechVolume");
                }
                else
                {
                    Directory.CreateDirectory(SettingsStore.DirectoryPath);
                    path = Path.Combine(SettingsStore.DirectoryPath, "live-" + Guid.NewGuid() + ".wav");
                    var host = settings.Get("sapiHostPath"); if (string.IsNullOrWhiteSpace(host)) host = engine.SpeechHost;
                    await SpeechService.SynthesizeAsync(host, mode, voice, text, path, settings.Number("systemSpeechRate"), cancellation.Token);
                    cancellation.Token.ThrowIfCancellationRequested();
                    temporary = path;
                    player.Source = MediaSource.CreateFromUri(new Uri(path));
                    player.Volume = settings.Number("systemSpeechVolume");
                }
            }
            else
            {
                using var synth = new SpeechSynthesizer();
                // Gemini generation still uses a local voice for on-device playback.
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
    public void Dispose()
    {
        Stop();
        lock (prepared)
        {
            foreach (var path in prepared.Values) { try { File.Delete(path); } catch (IOException) { } }
            prepared.Clear(); preparedOrder.Clear(); preparing.Clear();
        }
        player.Dispose();
    }
}

