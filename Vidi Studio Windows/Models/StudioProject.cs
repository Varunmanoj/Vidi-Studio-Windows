using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Vidi_Studio_Windows.Models;

public sealed class Segment : INotifyPropertyChanged
{
    private bool isActive;
    public event PropertyChangedEventHandler? PropertyChanged;
    [JsonIgnore] public bool IsActive
    {
        get => isActive;
        set
        {
            if (isActive == value) return;
            isActive = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsActive)));
        }
    }
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public double Start { get; set; }
    public double End { get; set; }
    public string Text { get; set; } = "";
    public string? Audio { get; set; }
    public string? AudioText { get; set; }
    public string? AudioSettings { get; set; }
    public string? AudioEngine { get; set; }
    public string? AudioVoice { get; set; }
    public double? AudioDuration { get; set; }
    public double? AudioStart { get; set; }
    public double? AudioEnd { get; set; }
    public string? AudioError { get; set; }
    [JsonIgnore] public string? ResolvedAudioEngine
    {
        get
        {
            if (!string.IsNullOrWhiteSpace(AudioEngine)) return AudioEngine.Trim();
            if (string.IsNullOrWhiteSpace(AudioSettings)) return null;
            var first = AudioSettings.Split('|')[0].Trim();
            return string.IsNullOrEmpty(first) ? null : first;
        }
    }
    [JsonIgnore] public string? ResolvedAudioVoice
    {
        get
        {
            if (!string.IsNullOrWhiteSpace(AudioVoice)) return AudioVoice.Trim();
            if (string.IsNullOrWhiteSpace(AudioSettings)) return null;
            var parts = AudioSettings.Split('|');
            if (parts.Length < 2) return null;
            var raw = parts[1].Trim();
            if (string.IsNullOrEmpty(raw)) return null;
            return raw.StartsWith("default:", StringComparison.OrdinalIgnoreCase) ? "System default" : raw;
        }
    }
    [JsonIgnore] public string? NarrationVoiceSummary => string.IsNullOrEmpty(Audio)
        ? null
        : NarrationVoiceFormatting.CombinedLabel(ResolvedAudioVoice ?? "System default", ResolvedAudioEngine ?? "windows-core");
    [JsonExtensionData] public Dictionary<string, JsonElement>? Extra { get; set; }
    [JsonIgnore] public string Timing => $"{TimeSpan.FromSeconds(Start):hh\\:mm\\:ss\\.fff} – {TimeSpan.FromSeconds(End):hh\\:mm\\:ss\\.fff}";
    [JsonIgnore] public bool IsDescription { get; set; }
    [JsonIgnore] public bool ShowKind { get; set; }
    [JsonIgnore] public int AdNumber { get; set; }
    private bool isNarrating;
    [JsonIgnore]
    public bool IsNarrating
    {
        get => isNarrating;
        set
        {
            if (isNarrating == value) return;
            isNarrating = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsNarrating)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(ShowAudioSpinner)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(AudioStatus)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(AudioStatusColor)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(AudioStatusGlyph)));
        }
    }
    private bool isQueuedForNarration;
    [JsonIgnore]
    public bool IsQueuedForNarration
    {
        get => isQueuedForNarration;
        set
        {
            if (isQueuedForNarration == value) return;
            isQueuedForNarration = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsQueuedForNarration)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(AudioStatus)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(AudioStatusColor)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(AudioStatusGlyph)));
        }
    }
    [JsonIgnore] public bool ShowAudioSpinner => isNarrating;
    private bool isEditing;
    [JsonIgnore]
    public bool IsEditing
    {
        get => isEditing;
        set { if (isEditing == value) return; isEditing = value; PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsEditing))); }
    }
    private bool isPreviewing;
    [JsonIgnore]
    public bool IsPreviewing
    {
        get => isPreviewing;
        set
        {
            if (isPreviewing == value) return;
            isPreviewing = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsPreviewing)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(PreviewGlyph)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(PreviewLabel)));
        }
    }
    [JsonIgnore] public string ProjectFolder { get; set; } = "";
    [JsonIgnore] public string StartLabel => TimeSpan.FromSeconds(Start).ToString(Start >= 3600 ? @"hh\:mm\:ss" : @"mm\:ss");
    [JsonIgnore] public string EndLabel => TimeSpan.FromSeconds(End).ToString(End >= 3600 ? @"hh\:mm\:ss" : @"mm\:ss");
    [JsonIgnore] public string PlayLabel => $"Play video from {StartLabel}: {Text}";
    [JsonIgnore] public string PreviewLabel => isPreviewing ? $"Stop narration at {StartLabel}: {Text}" : $"Preview narration at {StartLabel}: {Text}";
    [JsonIgnore] public string PreviewGlyph => isPreviewing ? "\uE769" : "\uE767";
    [JsonIgnore] public string KindLabel => IsDescription ? "Audio description" : "Transcript";
    public string Origin { get; set; } = "unknown";
    [JsonIgnore] public string OriginGlyph => Origin switch { "ai" => "\uE734", "ai_modified" => "\uE70F", "manual" => "\uE77B", _ => "\uE9CE" };
    [JsonIgnore] public string OriginLabel => Origin switch { "ai" => "AI-generated", "ai_modified" => "AI-generated · modified", "manual" => "Manually added", _ => "Origin unknown" };
    [JsonIgnore] public string OriginColor => Origin switch { "ai" => "#C832DE", "ai_modified" => "#D87919", "manual" => "#0078D4", _ => "#777777" };
    [JsonIgnore] public List<string> NarrationUpdateReasons
    {
        get
        {
            var reasons = new List<string>();
            if (!string.IsNullOrEmpty(AudioError)) reasons.Add(AudioError);
            if (AudioText != Text) reasons.Add("Description text changed since the narration was generated.");
            if ((AudioStart is { } start && Math.Abs(start - Start) > 0.001) ||
                (AudioEnd is { } end && Math.Abs(end - End) > 0.001))
                reasons.Add("Segment timing changed since the narration was generated.");
            if (AudioDuration is { } duration && duration > End - Start + 0.001)
                reasons.Add($"Narration is {duration:0.00}s but the segment timing only allows {End - Start:0.00}s.");
            return reasons;
        }
    }
    [JsonIgnore] public bool HasAudioFile
    {
        get
        {
            if (string.IsNullOrEmpty(Audio) || string.IsNullOrEmpty(ProjectFolder)) return false;
            try { return File.Exists(new StudioProject { Folder = ProjectFolder }.Resolve(Audio)); } catch (InvalidDataException) { return false; }
        }
    }
    [JsonIgnore] public bool AudioReady => HasAudioFile && NarrationUpdateReasons.Count == 0;
    [JsonIgnore] public string AdPrefix => AdNumber > 0 ? $"AD {AdNumber} · " : "";
    [JsonIgnore] public string AudioStatusGlyph => IsNarrating ? "\uE767" : IsQueuedForNarration ? "\uE823" : AudioReady ? "\uE73E" : AudioNeedsRegeneration ? "\uE72C" : "\uE767";
    [JsonIgnore] public string AudioStatus => IsNarrating
        ? $"{AdPrefix}Generating narration audio…" + (string.IsNullOrEmpty(NarrationVoiceSummary) ? "" : $" · {NarrationVoiceSummary}")
        : IsQueuedForNarration
            ? $"{AdPrefix}Queued for narration generation…"
            : AudioReady
                ? $"{AdPrefix}Synchronized audio generated" + (string.IsNullOrEmpty(NarrationVoiceSummary) ? "" : $" · {NarrationVoiceSummary}")
                : AudioNeedsRegeneration
                    ? $"{AdPrefix}Narration needs updating" + (string.IsNullOrEmpty(NarrationVoiceSummary) ? "" : $" · {NarrationVoiceSummary}")
                    : $"{AdPrefix}Narration not generated";
    [JsonIgnore] public string AudioStatusColor => IsNarrating ? "#0078D4" : AudioReady ? "#209447" : "#9B701B";
    [JsonIgnore] public bool AudioNeedsRegeneration => HasAudioFile && NarrationUpdateReasons.Count > 0;
    public override string ToString() => $"{Timing}  {(IsDescription ? "AD: " : "")}{Text}";
}

public sealed class StudioProject
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string Source { get; set; } = "";
    public string? SourceType { get; set; }
    public string? YoutubeURL { get; set; }
    public double Duration { get; set; }
    public int Width { get; set; }
    public int Height { get; set; }
    public bool HasAudio { get; set; }
    public string Summary { get; set; } = "";
    public string Status { get; set; } = "";
    public string? NarrationEngine { get; set; }
    public string? NarrationVoice { get; set; }
    public List<string> Thumbnails { get; set; } = new();
    public List<Segment> Transcript { get; set; } = new();
    public List<Segment> Cues { get; set; } = new();
    public List<string> Exports { get; set; } = new();
    [JsonExtensionData] public Dictionary<string, JsonElement>? Extra { get; set; }
    [JsonIgnore] public string Folder { get; set; } = "";
    [JsonIgnore] public bool IsYouTube => SourceType == "youtube";
    [JsonIgnore] public Uri? ThumbnailUri
    {
        get { try { var path = Thumbnails.FirstOrDefault(); return path != null && File.Exists(Resolve(path)) ? new Uri(Resolve(path)) : null; } catch { return null; } }
    }
    [JsonIgnore] public string ProjectSubtitle => $"{TimeSpan.FromSeconds(Duration):mm\\:ss} · {Cues.Count} descriptions";
    [JsonIgnore] public string DurationLabel => TimeSpan.FromSeconds(Duration).ToString(Duration >= 3600 ? @"hh\:mm\:ss" : @"mm\:ss");
    [JsonIgnore] public string AdSegmentLabel => Cues.Count.ToString();
    [JsonIgnore] public bool HasTranscript => Transcript.Count > 0;
    [JsonIgnore] public bool HasSummary => !string.IsNullOrWhiteSpace(Summary);
    [JsonIgnore] public bool HasNarration => Cues.Any(c => c.AudioReady);
    [JsonIgnore] public bool HasChangedNarration => Cues.Any(c => c.AudioNeedsRegeneration);
    [JsonIgnore] public bool HasMissingOrChangedNarration => Cues.Any(c => c.AudioNeedsRegeneration || !c.AudioReady);
    [JsonIgnore] public bool HasLocalSource
    {
        get { try { return !IsYouTube && !string.IsNullOrEmpty(Source) && File.Exists(Resolve(Source)); } catch (Exception) { return false; } }
    }
    [JsonIgnore] public bool HasCompleteExportNarration => double.IsFinite(Duration) && Duration > 0 && Cues.Count > 0 && Cues.All(c =>
    {
        if (string.IsNullOrWhiteSpace(c.Text) || !c.AudioReady || string.IsNullOrEmpty(c.Audio) ||
            !double.IsFinite(c.Start) || !double.IsFinite(c.End) || c.Start < 0 || c.End <= c.Start || c.End > Duration) return false;
        try { return new FileInfo(Resolve(c.Audio)) is { Exists: true, Length: > 0 }; } catch (Exception) { return false; }
    });
    public bool CanExportText(string content) => content switch
    {
        "transcript" => Transcript.Count > 0,
        "summary" => !string.IsNullOrWhiteSpace(Summary),
        "descriptions" => Cues.Count > 0,
        "combined" => Transcript.Count > 0 && Cues.Count > 0,
        _ => false
    };
    [JsonIgnore] public int VideoExportCount => Exports.Count(path =>
        Path.GetExtension(path).ToLowerInvariant() is ".mp4" or ".mov" or ".m4v" or ".mkv" or ".webm" || IsExportFolder(path));
    [JsonIgnore] public int AudioExportCount => Exports.Count(path =>
        Path.GetExtension(path).ToLowerInvariant() is ".mp3" or ".m4a" or ".wav" or ".aac" or ".flac");
    [JsonIgnore] public bool HasVideoExports => VideoExportCount > 0;
    [JsonIgnore] public bool HasAudioExports => AudioExportCount > 0;
    [JsonIgnore] public string VideoExportLabel => VideoExportCount == 0 ? "—" : VideoExportCount.ToString();
    [JsonIgnore] public string AudioExportLabel => AudioExportCount == 0 ? "—" : AudioExportCount.ToString();
    private bool IsExportFolder(string path)
    {
        try { return Directory.Exists(Resolve(path)); }
        catch (Exception) { return false; }
    }
    [JsonIgnore] public double TranscriptOpacity => Transcript.Count > 0 ? 1 : .25;
    [JsonIgnore] public double SummaryOpacity => Summary.Length > 0 ? 1 : .25;
    [JsonIgnore] public double NarrationOpacity => Cues.Any(c => c.AudioReady) ? 1 : .25;
    [JsonIgnore] public double ExportsOpacity => Exports.Count > 0 ? 1 : .25;
    [JsonIgnore] public string TranscriptStatus => $"{Transcript.Count} transcript segments";
    [JsonIgnore] public string SummaryStatus => Summary.Length > 0 ? "Summary ready" : "No summary";
    [JsonIgnore] public string NarrationStatus => $"{Cues.Count(c => c.AudioReady)} narration files ready";
    [JsonIgnore] public string ExportsStatus => $"{Exports.Count} created exports";
    public string Resolve(string relative)
    {
        if (Path.IsPathRooted(relative)) throw new InvalidDataException("Project paths must be relative.");
        var root = Path.GetFullPath(Folder).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        var path = Path.GetFullPath(Path.Combine(root, relative));
        if (!path.StartsWith(root, StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("Project path is outside its folder.");
        return path;
    }
    public static StudioProject Load(string folder)
    {
        var project = JsonSerializer.Deserialize<StudioProject>(File.ReadAllText(Path.Combine(folder, "project.json")), Json.Options)
            ?? throw new InvalidDataException("Invalid project.");
        project.Folder = Path.GetFullPath(folder);
        for (var i = 0; i < project.Cues.Count; i++) { var cue = project.Cues[i]; cue.IsDescription = true; cue.AdNumber = i + 1; cue.ProjectFolder = project.Folder; }
        foreach (var item in project.Transcript) { item.IsDescription = false; item.ProjectFolder = project.Folder; }
        return project;
    }
    public override string ToString() => Name;
}

public static class Json
{
    public static readonly JsonSerializerOptions Options = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase, PropertyNameCaseInsensitive = true, WriteIndented = true };
}

/// <summary>A description the engine could not narrate during a generation run.</summary>
public sealed class NarrationFailure
{
    public int Index { get; set; }
    public double Start { get; set; }
    public double End { get; set; }
    public double Duration { get; set; }
    public string Reason { get; set; } = "";
}

/// <summary>The summary shown for narration status, both after a run and on demand.</summary>
public sealed class NarrationFailureReport
{
    public string Title { get; set; } = "Narration status";
    public string Detail { get; set; } = "";
    public int Generated { get; set; }
    public List<NarrationFailure> Failures { get; set; } = new();
}

public static class NarrationVoiceFormatting
{
    public static string EngineDisplayName(string? engine) => (engine ?? "").Trim().ToLowerInvariant() switch
    {
        "gemini" => "Gemini TTS",
        "system" => "Apple on-device",
        "windows-core" => "Windows OneCore",
        "sapi5" => "SAPI 5",
        _ => "Windows OneCore"
    };

    public static string VoiceDisplayName(string? voice)
    {
        var trimmed = (voice ?? "").Trim();
        if (string.IsNullOrEmpty(trimmed) || trimmed.StartsWith("default:", StringComparison.OrdinalIgnoreCase))
            return "System default";
        if (trimmed.Contains('\\'))
            return trimmed.Split('\\').Last();
        return trimmed;
    }

    public static string CombinedLabel(string? voice, string? engine) =>
        $"{VoiceDisplayName(voice)} ({EngineDisplayName(engine)})";

    public static bool IsSystemDefaultVoice(string voice)
    {
        var lower = (voice ?? "").Trim().ToLowerInvariant();
        return lower == "system default" || lower.StartsWith("system default (") || lower.StartsWith("default:");
    }

    public static string? SystemDefaultInnerVoice(string voice)
    {
        var trimmed = (voice ?? "").Trim();
        if (!trimmed.StartsWith("System default (", StringComparison.OrdinalIgnoreCase) || !trimmed.EndsWith(")"))
            return null;
        var inner = trimmed.Substring("System default (".Length, trimmed.Length - "System default (".Length - 1).Trim();
        return string.IsNullOrEmpty(inner) ? null : inner;
    }

    public static string EffectiveVoiceName(string voice) =>
        SystemDefaultInnerVoice(voice) ?? VoiceDisplayName(voice);

    public static bool VoicesMatch(string? engineA, string? voiceA, string? engineB, string? voiceB)
    {
        var normEngineA = (engineA ?? "").Trim().ToLowerInvariant();
        var normEngineB = (engineB ?? "").Trim().ToLowerInvariant();
        if (normEngineA != normEngineB) return false;
        var trimA = (voiceA ?? "").Trim();
        var trimB = (voiceB ?? "").Trim();
        if (IsSystemDefaultVoice(trimA) && IsSystemDefaultVoice(trimB))
        {
            var innerA = SystemDefaultInnerVoice(trimA);
            var innerB = SystemDefaultInnerVoice(trimB);
            if (innerA != null && innerB != null)
                return string.Equals(innerA, innerB, StringComparison.OrdinalIgnoreCase);
            return true;
        }
        return string.Equals(EffectiveVoiceName(trimA), EffectiveVoiceName(trimB), StringComparison.OrdinalIgnoreCase);
    }
}

public sealed class NarrationVoiceSummary
{
    public string Id { get; set; } = "";
    public string Engine { get; set; } = "";
    public string EngineDisplayName { get; set; } = "";
    public string Voice { get; set; } = "";
    public string VoiceDisplayName { get; set; } = "";
    public int Count { get; set; }
    public List<int> SegmentNumbers { get; set; } = new();
    public bool IsDifferentVoice { get; set; }
    public string CombinedLabel => $"{VoiceDisplayName} ({EngineDisplayName})";
}

public sealed class NarrationVoiceSegmentEntry
{
    public int Index { get; set; }
    public double Start { get; set; }
    public double End { get; set; }
    public double Duration { get; set; }
    public string Engine { get; set; } = "";
    public string EngineDisplayName { get; set; } = "";
    public string Voice { get; set; } = "";
    public string VoiceDisplayName { get; set; } = "";
    public string AudioFile { get; set; } = "";
    public bool UsesDifferentVoice { get; set; }
    public string Reason { get; set; } = "";
    public string CombinedLabel => $"{VoiceDisplayName} ({EngineDisplayName})";
}

public sealed class NarrationVoiceReport
{
    public string Title { get; set; } = "Narration voice details";
    public string Detail { get; set; } = "";
    public string? PrimaryVoiceLabel { get; set; }
    public bool HasDifferentVoices { get; set; }
    public List<NarrationVoiceSummary> VoiceSummaries { get; set; } = new();
    public List<NarrationVoiceSegmentEntry> DifferentSegments { get; set; } = new();
    public List<NarrationVoiceSegmentEntry> AllSegments { get; set; } = new();
}
