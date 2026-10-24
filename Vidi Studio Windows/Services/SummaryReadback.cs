using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Windows.Media.Core;
using Windows.Media.Playback;
using Windows.Media.SpeechSynthesis;

namespace Vidi_Studio_Windows.Services;

public enum SummaryReadbackState { Stopped, Preparing, Playing, Paused }

/// <summary>Uses the selected provider for summary playback; temporary audio never enters projects.</summary>
public sealed class SummaryReadback : IDisposable
{
    private readonly MediaPlayer player = new();
    private readonly EngineClient engine;
    private readonly SettingsStore settings;
    private CancellationTokenSource? cancellation;
    private readonly SemaphoreSlim playbackGate = new(1, 1);
    private string currentSentence = "";
    private bool preciseWordCues;
    private int lastWordStart = -1;
    public event Action<string>? SentenceChanged;
    public event Action<int, int>? WordChanged;
    public event Action<SummaryReadbackState>? StateChanged;
    public SummaryReadbackState State { get; private set; }
    public SummaryReadback(EngineClient engine, SettingsStore settings) { this.engine = engine; this.settings = settings; }
    private void SetState(SummaryReadbackState state)
    {
        if (State == state) return;
        State = state;
        StateChanged?.Invoke(state);
    }
    public void Pause()
    {
        if (State != SummaryReadbackState.Playing) return;
        player.Pause(); SetState(SummaryReadbackState.Paused);
    }
    public void Resume()
    {
        if (State != SummaryReadbackState.Paused) return;
        player.Play(); SetState(SummaryReadbackState.Playing);
    }
    public void Stop() { cancellation?.Cancel(); player.Pause(); player.Source = null; currentSentence = ""; SetState(SummaryReadbackState.Stopped); }
    public void TickWordProgress()
    {
        if (State != SummaryReadbackState.Playing || preciseWordCues || currentSentence.Length == 0) return;
        var words = Regex.Matches(currentSentence, @"\S+");
        if (words.Count == 0) return;
        var duration = player.PlaybackSession.NaturalDuration.TotalSeconds;
        if (duration <= 0 || !double.IsFinite(duration)) duration = Math.Max(1, words.Count / 2.5);
        var fraction = Math.Clamp(player.PlaybackSession.Position.TotalSeconds / duration, 0, .999);
        var word = words[Math.Min(words.Count - 1, (int)(fraction * words.Count))];
        if (word.Index == lastWordStart) return;
        lastWordStart = word.Index; WordChanged?.Invoke(word.Index, word.Length);
    }
    public async Task SpeakAsync(string text)
    {
        Stop();
        var source = new CancellationTokenSource(); cancellation = source;
        SetState(SummaryReadbackState.Preparing);
        var token = source.Token;
        var ownsPlayback = false;
        Directory.CreateDirectory(SettingsStore.DirectoryPath);
        var folder = Path.Combine(SettingsStore.DirectoryPath, "readback-" + Guid.NewGuid());
        Directory.CreateDirectory(folder);
        try
        {
            await playbackGate.WaitAsync(token);
            ownsPlayback = true;
            token.ThrowIfCancellationRequested();
            var config = Path.Combine(folder, "config.json");
            // The summary is read with the same engine and voice as narration.
            var mode = settings.Get("ttsMode", "windows-core");
            var configData = settings.Data.DeepClone().AsObject();
            configData["ttsMode"] = mode;
            await File.WriteAllTextAsync(config, configData.ToJsonString(Models.Json.Options), token);
            foreach (var sentence in Chunks(text))
            {
                token.ThrowIfCancellationRequested();
                var path = Path.Combine(folder, Guid.NewGuid() + ".wav");
                MediaSource audioSource;
                MediaPlaybackItem? coreItem = null;
                if (mode is "windows-core" or "system")
                {
                    using var synthesizer = new SpeechSynthesizer();
                    var voiceId = settings.Get("systemVoice");
                    if (!string.IsNullOrEmpty(voiceId)) synthesizer.Voice = SpeechSynthesizer.AllVoices.FirstOrDefault(v => v.Id == voiceId)
                        ?? throw new InvalidOperationException("The selected Windows voice is not installed.");
                    synthesizer.Options.SpeakingRate = Math.Clamp(settings.Number("systemSpeechRate"), .5, 2);
                    synthesizer.Options.AudioPitch = Math.Clamp(settings.Number("systemSpeechPitch"), .5, 2);
                    synthesizer.Options.AudioVolume = Math.Clamp(settings.Number("systemSpeechVolume"), 0, 1);
                    synthesizer.Options.IncludeWordBoundaryMetadata = true;
                    var stream = await synthesizer.SynthesizeTextToStreamAsync(sentence).AsTask(token);
                    audioSource = MediaSource.CreateFromStream(stream, stream.ContentType);
                    coreItem = new MediaPlaybackItem(audioSource);
                    var registered = new HashSet<TimedMetadataTrack>();
                    void RegisterWords(MediaPlaybackItem item)
                    {
                        for (var i = 0; i < item.TimedMetadataTracks.Count; i++)
                        {
                            var track = item.TimedMetadataTracks[i];
                            if (track.Label != "SpeechWord" || !registered.Add(track)) continue;
                            track.CueEntered += (_, args) =>
                            {
                                if (!token.IsCancellationRequested && args.Cue is SpeechCue word && word.StartPositionInInput is { } start && word.EndPositionInInput is { } end)
                                {
                                    preciseWordCues = true;
                                    var offset = (int)start;
                                    if (offset >= 0 && offset < currentSentence.Length)
                                    {
                                        var spoken = Regex.Match(currentSentence[offset..], @"^\S+");
                                        WordChanged?.Invoke(offset, spoken.Success ? spoken.Length : Math.Clamp((int)(end - start + 1), 1, currentSentence.Length - offset));
                                    }
                                }
                            };
                            item.TimedMetadataTracks.SetPresentationMode((uint)i, TimedMetadataTrackPresentationMode.ApplicationPresented);
                        }
                    }
                    coreItem.TimedMetadataTracksChanged += (item, _) => RegisterWords(item);
                    RegisterWords(coreItem);
                }
                else
                {
                    var host = settings.Get("sapiHostPath"); if (string.IsNullOrWhiteSpace(host)) host = engine.SpeechHost;
                    if (mode is "piper" or "sapi5")
                    {
                        // Piper and SAPI 5 speak through the same speech host as live narration,
                        // so summary playback does not depend on an extra engine process.
                        var voice = mode == "piper" ? settings.Get("piperVoice", settings.Get("systemVoice")) : settings.Get("systemVoice");
                        await SpeechService.SynthesizeAsync(host, mode, voice, sentence, path, settings.Number("systemSpeechRate"), token);
                        audioSource = MediaSource.CreateFromUri(new Uri(path));
                    }
                    else
                    {
                        await engine.RunAsync(new[] { "preview-tts", "--text", sentence, "--output", path }, cancellation: token, configPath: config);
                        audioSource = MediaSource.CreateFromUri(new Uri(path));
                    }
                }
                token.ThrowIfCancellationRequested();
                var finished = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
                void Ended(MediaPlayer _, object args) => finished.TrySetResult(true);
                void Failed(MediaPlayer _, MediaPlayerFailedEventArgs args) => finished.TrySetException(new IOException("Summary playback failed."));
                player.MediaEnded += Ended; player.MediaFailed += Failed;
                using var registration = token.Register(() => finished.TrySetCanceled(token));
                try
                {
                    currentSentence = sentence; preciseWordCues = false; lastWordStart = -1;
                    SentenceChanged?.Invoke(sentence);
                    player.Source = coreItem is not null ? coreItem : audioSource;
                    SetState(SummaryReadbackState.Playing);
                    player.Play();
                    await finished.Task;
                }
                finally { player.MediaEnded -= Ended; player.MediaFailed -= Failed; player.Source = null; audioSource.Dispose(); }
                if (ReferenceEquals(cancellation, source)) { currentSentence = ""; SetState(SummaryReadbackState.Preparing); }
                File.Delete(path);
            }
        }
        finally
        {
            if (ownsPlayback) SentenceChanged?.Invoke("");
            try { Directory.Delete(folder, true); } catch (IOException) { }
            if (cancellation == source) { cancellation = null; SetState(SummaryReadbackState.Stopped); }
            source.Dispose();
            if (ownsPlayback) playbackGate.Release();
        }
    }
    private static IEnumerable<string> Chunks(string text)
    {
        text = Regex.Replace(text, @"(?m)^[ \t]*#{1,6}[ \t]*|^[ \t]*[-*][ \t]+|[*_`]", "");
        foreach (var sentence in Regex.Split(text, @"(?<=[.!?।])\s+|\n+"))
        {
            var remaining = sentence.Trim();
            while (remaining.Length > 500)
            {
                var split = remaining.LastIndexOf(' ', 499, 499); if (split <= 0) split = 499;
                yield return remaining[..split]; remaining = remaining[split..].Trim();
            }
            if (remaining.Length > 0 && remaining.Any(char.IsLetterOrDigit)) yield return remaining;
        }
    }
    public void Dispose() { Stop(); player.Dispose(); }
}
