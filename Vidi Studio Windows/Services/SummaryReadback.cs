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

/// <summary>One speakable summary sentence and the paragraph (markdown block) it belongs to.</summary>
public sealed record SummarySpeechSentence(string Text, int Paragraph);

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
    private IReadOnlyList<SummarySpeechSentence> source = Array.Empty<SummarySpeechSentence>();
    private IReadOnlyList<SummarySpeechSentence> active = Array.Empty<SummarySpeechSentence>();
    private int currentIndex = -1;
    public event Action<string>? SentenceChanged;
    public event Action<int, int>? WordChanged;
    public event Action<SummaryReadbackState>? StateChanged;
    public SummaryReadbackState State { get; private set; }
    public SummaryReadback(EngineClient engine, SettingsStore settings) { this.engine = engine; this.settings = settings; }

    /// <summary>Zero-based index of the sentence currently being read, or -1 when idle.</summary>
    public int CurrentIndex => currentIndex;
    public bool HasSentences => source.Count > 0;

    public bool CanGoPreviousSentence => active.Count > 0 && currentIndex != 0;
    public bool CanGoNextSentence => active.Count > 0 && (currentIndex < 0 || currentIndex + 1 < active.Count);
    public bool CanGoPreviousParagraph
    {
        get
        {
            if (active.Count == 0) return false;
            if (currentIndex < 0) return true;
            if (ParagraphRange(active, currentIndex) is not { } range) return false;
            return currentIndex > range.Start || range.Start > 0;
        }
    }
    public bool CanGoNextParagraph
    {
        get
        {
            if (active.Count == 0) return false;
            if (currentIndex < 0) return true;
            return ParagraphRange(active, currentIndex) is { } range && range.End + 1 < active.Count;
        }
    }

    /// <summary>
    /// Parses the rendered summary into sentences and paragraph blocks so menu
    /// and button navigation can jump precisely without re-reading the text.
    /// </summary>
    public void SetSource(string text)
    {
        if (State != SummaryReadbackState.Stopped) Stop();
        source = Parse(text);
        active = source;
        currentIndex = -1;
    }

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
    public void Stop()
    {
        cancellation?.Cancel(); player.Pause(); player.Source = null; currentSentence = "";
        currentIndex = -1; active = source; SetState(SummaryReadbackState.Stopped);
    }
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

    /// <summary>Reads an arbitrary block of text once, such as a selection.</summary>
    public Task SpeakAsync(string text) => PlayAsync(Parse(text), 0);

    /// <summary>Reads the complete summary from the first sentence.</summary>
    public Task PlayFromStartAsync() => PlayAsync(source, 0);

    public Task NextSentenceAsync()
    {
        var sentences = active;
        if (sentences.Count == 0) return Task.CompletedTask;
        var target = currentIndex < 0 ? 0 : Math.Min(currentIndex + 1, sentences.Count - 1);
        return PlayAsync(sentences, target);
    }

    public Task PreviousSentenceAsync()
    {
        var sentences = active;
        if (sentences.Count == 0) return Task.CompletedTask;
        var target = currentIndex <= 0 ? 0 : currentIndex - 1;
        return PlayAsync(sentences, target);
    }

    public Task NextParagraphAsync()
    {
        var sentences = active;
        if (sentences.Count == 0) return Task.CompletedTask;
        var target = 0;
        if (currentIndex >= 0 && ParagraphRange(sentences, currentIndex) is { } range)
        {
            if (range.End + 1 >= sentences.Count) return Task.CompletedTask;
            target = range.End + 1;
        }
        return PlayAsync(sentences, target);
    }

    public Task PreviousParagraphAsync()
    {
        var sentences = active;
        if (sentences.Count == 0) return Task.CompletedTask;
        var target = 0;
        if (currentIndex >= 0 && ParagraphRange(sentences, currentIndex) is { } range)
        {
            if (currentIndex == range.Start && range.Start == 0) return Task.CompletedTask;
            target = currentIndex > range.Start ? range.Start : range.Start - 1;
        }
        return PlayAsync(sentences, target);
    }

    private static (int Start, int End)? ParagraphRange(IReadOnlyList<SummarySpeechSentence> sentences, int index)
    {
        if (index < 0 || index >= sentences.Count) return null;
        var paragraph = sentences[index].Paragraph;
        var start = index;
        var end = index;
        while (start > 0 && sentences[start - 1].Paragraph == paragraph) start--;
        while (end + 1 < sentences.Count && sentences[end + 1].Paragraph == paragraph) end++;
        return (start, end);
    }

    private async Task PlayAsync(IReadOnlyList<SummarySpeechSentence> sentences, int startIndex)
    {
        Stop();
        if (sentences.Count == 0) return;
        active = sentences;
        startIndex = Math.Clamp(startIndex, 0, sentences.Count - 1);
        var run = new CancellationTokenSource(); cancellation = run;
        SetState(SummaryReadbackState.Preparing);
        var token = run.Token;
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
            for (var index = startIndex; index < sentences.Count; index++)
            {
                token.ThrowIfCancellationRequested();
                var sentence = sentences[index].Text;
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
                    currentSentence = sentence; preciseWordCues = false; lastWordStart = -1; currentIndex = index;
                    SentenceChanged?.Invoke(sentence);
                    player.Source = coreItem is not null ? coreItem : audioSource;
                    SetState(SummaryReadbackState.Playing);
                    player.Play();
                    await finished.Task;
                }
                finally { player.MediaEnded -= Ended; player.MediaFailed -= Failed; player.Source = null; audioSource.Dispose(); }
                if (ReferenceEquals(cancellation, run)) { currentSentence = ""; currentIndex = -1; SetState(SummaryReadbackState.Preparing); }
                File.Delete(path);
            }
        }
        finally
        {
            if (ownsPlayback) SentenceChanged?.Invoke("");
            try { Directory.Delete(folder, true); } catch (IOException) { }
            if (cancellation == run) { cancellation = null; currentIndex = -1; active = source; SetState(SummaryReadbackState.Stopped); }
            run.Dispose();
            if (ownsPlayback) playbackGate.Release();
        }
    }

    private static List<SummarySpeechSentence> Parse(string text)
    {
        var result = new List<SummarySpeechSentence>();
        var paragraph = -1;
        foreach (var line in text.Split('\n'))
        {
            if (string.IsNullOrWhiteSpace(line)) continue;
            paragraph++;
            foreach (var sentence in Chunks(line)) result.Add(new SummarySpeechSentence(sentence, paragraph));
        }
        return result;
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
