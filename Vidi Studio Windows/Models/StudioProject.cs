using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Globalization;
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
    [JsonIgnore] public string DurationLabel => Math.Max(0, End - Start).ToString("0.00", CultureInfo.InvariantCulture) + "s";
    public string? Audio { get; set; }
    public string? AudioText { get; set; }
    public string? AudioSettings { get; set; }
    public string? AudioEngine { get; set; }
    public string? AudioVoice { get; set; }
    public double? AudioDuration { get; set; }
    public double? AudioStart { get; set; }
    public double? AudioEnd { get; set; }
    public string? AudioError { get; set; }
    public string? AudioOrigin { get; set; }
    public string? AudioSourceName { get; set; }
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
    public string? OriginSource { get; set; }
    public bool? OriginOverride { get; set; }
    [JsonIgnore] public string OriginGlyph => Origin switch { "ai" => "\uE734", "ai_modified" => "\uE70F", "imported" => "\uE896", "imported_modified" => "\uE8B5", "manual" => "\uE77B", _ => "\uE9CE" };
    [JsonIgnore] public string OriginLabel => Origin switch { "ai" => "AI-generated", "ai_modified" => "AI-generated · modified", "imported" => "Imported", "imported_modified" => "Imported · modified", "manual" => "Manually added", _ => "Origin unknown" };
    [JsonIgnore] public string OriginColor => Origin switch { "ai" => "#C832DE", "ai_modified" => "#D87919", "imported" => "#008B8B", "imported_modified" => "#B7791F", "manual" => "#0078D4", _ => "#777777" };
    [JsonIgnore] public bool ShowOrigin => true;
    [JsonIgnore] public bool IsTimingFlagged { get; set; }
    [JsonIgnore] public string TimingFlagHelp => IsTimingFlagged
        ? "This description overlaps dialogue or another description. Described exports are blocked until it is retimed."
        : "";
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
            if (AudioDuration is { } duration && duration > End - Start + (AudioOrigin == "imported" ? 0.05 : 0.001))
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

/// <summary>
/// Project-specific playback choices persisted in the project's project.json.
/// Both desktop apps read and write the same playback object, so the choices
/// travel with the project folder instead of staying on one computer.
/// </summary>
public sealed class ProjectPlaybackSettings
{
    public bool? NarrationEnabled { get; set; }
    public bool? FollowTranscript { get; set; }
}

public sealed class StudioProject : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;
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
    public ImportedAudioDescription? ImportedAudioDescription { get; set; }
    public ImportedADTranscript? ImportedADTranscript { get; set; }
    public string? SummaryOrigin { get; set; }
    public string? SummaryOriginSource { get; set; }
    public ImportReport? LastImportReport { get; set; }
    public List<ImportReport>? ImportReports { get; set; }
    public DescriptionTimingAudit? DescriptionTimingAudit { get; set; }
    public ProjectPlaybackSettings? Playback { get; set; }
    public List<string> Thumbnails { get; set; } = new();
    public List<Segment> Transcript { get; set; } = new();
    public List<Segment> Cues { get; set; } = new();
    public List<string> Exports { get; set; } = new();
    [JsonExtensionData] public Dictionary<string, JsonElement>? Extra { get; set; }
    [JsonIgnore] public string Folder { get; set; } = "";
    private ProjectIntegrityReport? integrity;
    [JsonIgnore]
    public ProjectIntegrityReport? Integrity
    {
        get => integrity;
        set
        {
            if (ReferenceEquals(integrity, value)) return;
            integrity = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Integrity)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(HasIntegrityIssue)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IntegrityIsDamaged)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IntegrityHasWarning)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IntegrityStatus)));
        }
    }
    [JsonIgnore] public bool IntegrityPlaceholder { get; set; }
    [JsonIgnore] public bool HasIntegrityIssue => Integrity is { IsHealthy: false };
    [JsonIgnore] public bool IntegrityIsDamaged => Integrity?.IsDamaged == true;
    [JsonIgnore] public bool IntegrityHasWarning => Integrity?.HasWarnings == true;
    [JsonIgnore] public string IntegrityStatus => Integrity?.BadgeText ?? "";
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
    [JsonIgnore] public int DescriptionTimingIssueCount => DescriptionTimingAudit?.Count ?? 0;
    [JsonIgnore] public bool HasDescriptionTimingIssues => DescriptionTimingIssueCount > 0;
    [JsonIgnore] public string SummaryOriginLabel => (SummaryOrigin ?? "unknown") switch
    {
        "ai" => "AI-generated", "imported" => "Imported", "manual" => "Manually added", _ => "Origin unknown"
    };
    [JsonIgnore] public bool CanCombineImportedAudio => ImportedAudioDescription != null && !IsYouTube && HasLocalSource;
    [JsonIgnore] public int GeneratedNarrationCount => Cues.Count(c => c.Audio != null && c.AudioOrigin == "generated");
    [JsonIgnore] public int ImportedNarrationCount => Cues.Count(c => c.Audio != null && c.AudioOrigin == "imported");
    [JsonIgnore] public int UnknownNarrationCount => Cues.Count(c => c.Audio != null && c.AudioOrigin != "generated" && c.AudioOrigin != "imported");
    [JsonIgnore] public string NarrationSourcesText
    {
        get
        {
            var parts = new List<string>();
            if (GeneratedNarrationCount > 0) parts.Add($"{GeneratedNarrationCount} AI-generated");
            if (ImportedNarrationCount > 0) parts.Add($"{ImportedNarrationCount} imported");
            if (UnknownNarrationCount > 0) parts.Add($"{UnknownNarrationCount} unknown");
            return parts.Count == 0 ? "No narration audio" : string.Join(" · ", parts);
        }
    }
    [JsonIgnore] public List<string> ImportedNarrationFiles => Cues
        .Where(c => c.Audio != null && c.AudioOrigin == "imported" && !string.IsNullOrWhiteSpace(c.AudioSourceName))
        .Select(c => c.AudioSourceName!).Distinct().OrderBy(name => name).ToList();
    [JsonIgnore] public bool HasNarration => Cues.Any(c => c.AudioReady);
    [JsonIgnore] public bool HasNarrationAudio => Cues.Any(c => c.Audio != null);
    [JsonIgnore] public string AllADAudioTitle => HasNarrationAudio ? "Regenerate All AD Audio…" : "Generate All AD Audio";
    [JsonIgnore] public bool CanGenerateMissingOrChangedAudio => HasNarrationAudio && HasMissingOrChangedNarration;
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
        var manifest = Path.Combine(folder, "project.json");
        if (!File.Exists(manifest)) throw new FileNotFoundException("project.json is missing.", manifest);
        var project = JsonSerializer.Deserialize<StudioProject>(ReadManifestText(manifest), Json.Options)
            ?? throw new InvalidDataException("Invalid project.");
        if (string.IsNullOrWhiteSpace(project.Id) || string.IsNullOrWhiteSpace(project.Name))
            throw new InvalidDataException("project.json is missing required project fields.");
        project.Folder = Path.GetFullPath(folder);
        project.IntegrityPlaceholder = false;
        var flagged = project.DescriptionTimingAudit?.Segments.Select(s => s.Id).Where(id => !string.IsNullOrEmpty(id)).ToHashSet() ?? new HashSet<string>();
        for (var i = 0; i < project.Cues.Count; i++)
        {
            var cue = project.Cues[i];
            cue.IsDescription = true; cue.AdNumber = i + 1; cue.ProjectFolder = project.Folder;
            cue.IsTimingFlagged = flagged.Contains(cue.Id);
        }
        foreach (var item in project.Transcript) { item.IsDescription = false; item.ProjectFolder = project.Folder; }
        return project;
    }

    /// <summary>
    /// Reads project.json without blocking the engine's atomic replace. Windows
    /// refuses a rename while a reader omits FileShare.Delete, which is why a
    /// background gallery/integrity read could surface as "Access is denied".
    /// </summary>
    internal static string ReadManifestText(string path)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        using var reader = new StreamReader(stream, System.Text.Encoding.UTF8);
        return reader.ReadToEnd();
    }

    public static StudioProject? TryLoad(string folder)
    {
        try { return Load(folder); }
        catch (Exception) { return null; }
    }

    public static bool IsManifestReadable(string folder)
    {
        try { Load(folder); return true; }
        catch (Exception) { return false; }
    }

    /// <summary>Builds a displayable in-memory project for a folder whose
    /// project.json is missing or cannot be decoded.</summary>
    public static StudioProject DamagedPlaceholder(string folder)
    {
        var fullFolder = Path.GetFullPath(folder);
        string? name = null;
        string? id = null;
        var source = "";
        string? sourceType = null;
        string? youtubeUrl = null;
        var summary = "";
        var thumbnails = new List<string>();
        var exports = new List<string>();
        try
        {
            using var document = JsonDocument.Parse(ReadManifestText(Path.Combine(fullFolder, "project.json")));
            var root = document.RootElement;
            if (root.TryGetProperty("name", out var nameValue) && nameValue.ValueKind == JsonValueKind.String) name = nameValue.GetString();
            if (root.TryGetProperty("id", out var idValue) && idValue.ValueKind == JsonValueKind.String) id = idValue.GetString();
            if (root.TryGetProperty("source", out var sourceValue) && sourceValue.ValueKind == JsonValueKind.String) source = sourceValue.GetString() ?? "";
            if (root.TryGetProperty("sourceType", out var typeValue) && typeValue.ValueKind == JsonValueKind.String) sourceType = typeValue.GetString();
            if (root.TryGetProperty("youtubeURL", out var youtubeValue) && youtubeValue.ValueKind == JsonValueKind.String) youtubeUrl = youtubeValue.GetString();
            if (root.TryGetProperty("summary", out var summaryValue) && summaryValue.ValueKind == JsonValueKind.String) summary = summaryValue.GetString() ?? "";
            if (root.TryGetProperty("thumbnails", out var thumbsValue) && thumbsValue.ValueKind == JsonValueKind.Array)
                thumbnails = thumbsValue.EnumerateArray().Where(v => v.ValueKind == JsonValueKind.String).Select(v => v.GetString() ?? "").Where(v => v.Length > 0).ToList();
            if (root.TryGetProperty("exports", out var exportsValue) && exportsValue.ValueKind == JsonValueKind.Array)
                exports = exportsValue.EnumerateArray().Where(v => v.ValueKind == JsonValueKind.String).Select(v => v.GetString() ?? "").Where(v => v.Length > 0).ToList();
        }
        catch (Exception) { }
        return new StudioProject
        {
            Id = string.IsNullOrWhiteSpace(id) ? "damaged-" + StableId(fullFolder) : id!,
            Name = string.IsNullOrWhiteSpace(name) ? DisplayName(fullFolder) : name!,
            Source = source,
            SourceType = sourceType,
            YoutubeURL = youtubeUrl,
            Summary = summary,
            Thumbnails = thumbnails,
            Exports = exports,
            Status = "Damaged",
            Folder = fullFolder,
            IntegrityPlaceholder = true
        };
    }

    public ProjectIntegrityManifest ToIntegrityManifest()
    {
        var narration = new List<ProjectNarrationReference>();
        for (var i = 0; i < Cues.Count; i++)
        {
            var cue = Cues[i];
            if (string.IsNullOrWhiteSpace(cue.Audio)) continue;
            narration.Add(new ProjectNarrationReference(cue.Audio.Trim(), i + 1, $"{cue.StartLabel}–{cue.EndLabel}"));
        }
        var imported = new List<string>();
        if (!string.IsNullOrWhiteSpace(ImportedAudioDescription?.Path)) imported.Add(ImportedAudioDescription.Path);
        if (!string.IsNullOrWhiteSpace(ImportedADTranscript?.Path)) imported.Add(ImportedADTranscript.Path);
        var manifestReadable = IsManifestReadable(Folder);
        return new ProjectIntegrityManifest
        {
            Folder = string.IsNullOrEmpty(Folder) ? null : Folder,
            ProjectName = Name,
            IsYouTubeProject = IsYouTube,
            SourceRelativePath = string.IsNullOrWhiteSpace(Source) ? null : Source,
            ThumbnailPaths = Thumbnails,
            NarrationReferences = narration,
            ImportedPaths = imported,
            ExportPaths = Exports,
            KnownManifestUnreadable = IntegrityPlaceholder && !manifestReadable
        };
    }

    private static string DisplayName(string folder)
    {
        var name = Path.GetFileName(folder.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
        if (name.Length <= 9) return name;
        var suffix = name[^9..];
        return suffix[0] == '-' && suffix[1..].All(IsHexDigit) ? name[..^9] : name;
    }

    private static bool IsHexDigit(char value) =>
        value is >= '0' and <= '9' or >= 'a' and <= 'f' or >= 'A' and <= 'F';

    private static string StableId(string folder)
    {
        ulong hash = 0xcbf29ce484222325;
        foreach (var value in System.Text.Encoding.UTF8.GetBytes(Path.GetFullPath(folder)))
        {
            hash ^= value;
            hash = unchecked(hash * 0x100000001b3UL);
        }
        return hash.ToString("x");
    }

    public override string ToString() => Name;
}

public sealed class ImportedAudioDescription
{
    public string Path { get; set; } = "";
    public string Mode { get; set; } = "narration";
    public string SourceName { get; set; } = "";
    public double? Duration { get; set; }
    [JsonIgnore] public string ModeLabel => Mode == "fullMix" ? "Full AD mix" : "AD narration audio";
}

public sealed class ImportedADTranscript
{
    public List<Segment> Segments { get; set; } = new();
    public string Mode { get; set; } = "narration";
    public string? Format { get; set; }
    public string? SourceName { get; set; }
    public string? Path { get; set; }
}

public sealed class ImportConflict
{
    public string Kind { get; set; } = "";
    public string? Id { get; set; }
    public int? Row { get; set; }
    public double? Start { get; set; }
    public double? End { get; set; }
    public string? Text { get; set; }
    [JsonIgnore] public string Description
    {
        get
        {
            var kind = Kind == "transcript" ? "Transcript" : Kind == "description" ? "AD segment" : "Timing";
            var range = Start is { } start && End is { } end
                ? $"{TimeSpan.FromSeconds(start):hh\\:mm\\:ss\\.fff} to {TimeSpan.FromSeconds(end):hh\\:mm\\:ss\\.fff}"
                : "";
            return $"{kind} {Row ?? 0} overlaps {range}: {Text}";
        }
    }
}

public sealed class ImportFlaggedSegment
{
    public string? Id { get; set; }
    public int? Row { get; set; }
    public int? SourceLine { get; set; }
    public double? Start { get; set; }
    public double? End { get; set; }
    public string? Text { get; set; }
    public List<ImportConflict>? Conflicts { get; set; }
}

public sealed class ImportOmittedRow
{
    public int? SourceLine { get; set; }
    public string Text { get; set; } = "";
    public string Reason { get; set; } = "";
    public string Code { get; set; } = "";
}

public sealed class ImportReport
{
    public string Id { get; set; } = "";
    public string Kind { get; set; } = "";
    public string SourceName { get; set; } = "";
    public int RowsImported { get; set; }
    public int RowsFlagged { get; set; }
    public int RowsOmittedAfterVideo { get; set; }
    public int RowsOmittedInvalid { get; set; }
    public int? RowsOmittedTranscriptOverlap { get; set; }
    public double? VideoDuration { get; set; }
    public List<ImportOmittedRow>? Omitted { get; set; }
    public List<ImportFlaggedSegment>? Flagged { get; set; }
    public List<string>? Warnings { get; set; }
    public string? Guidance { get; set; }
    [JsonIgnore] public string KindTitle => Kind switch
    {
        "transcript" => "Transcript", "narration" => "Descriptions", "combined" => "AD Transcript",
        "summary" => "Summary", "adAudio" => "AD audio", "segmentNarration" => "Narration audio", _ => "Import"
    };
    [JsonIgnore] public string DurationText => VideoDuration is { } duration
        ? $"Video duration used for validation: {TimeSpan.FromSeconds(duration):hh\\:mm\\:ss\\.fff}"
        : "Video duration is unknown. Timing could not be verified against the video.";
}

public sealed class DescriptionTimingAudit
{
    public int Count { get; set; }
    public List<ImportFlaggedSegment> Segments { get; set; } = new();
    public double? VideoDuration { get; set; }
    public string? Guidance { get; set; }
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


/// <summary>Persistent Gemini quota/service failure that pauses every AI action.</summary>
public static class AIServiceIssueDetector
{
    public static (string Title, string Message, string Kind)? Detect(string? rawDetail)
    {
        if (string.IsNullOrWhiteSpace(rawDetail)) return null;
        var detail = rawDetail.ToLowerInvariant();
        if (detail.Contains("api key not valid") || detail.Contains("api_key_invalid") || detail.Contains("invalid api key")) return null;
        if (detail.Contains("quota") || detail.Contains("resource_exhausted") || detail.Contains("429") ||
            detail.Contains("billing") || detail.Contains("credit") || detail.Contains("spend") || detail.Contains("rate limit"))
            return ("Gemini Usage Limit Reached",
                "Your Gemini key has no remaining quota or credits for AI processing. AI actions are paused. Add billing or credits in Google AI Studio, or wait for the limit to reset, then choose Retry AI.",
                "quota");
        if (detail.Contains("503") || detail.Contains("service unavailable") || detail.Contains("overloaded") ||
            detail.Contains("deadline exceeded") || detail.Contains("try again later"))
            return ("Gemini AI Processing Unavailable",
                "Gemini could not process the request because the service is temporarily unavailable. AI actions are paused until you retry successfully or the service recovers.",
                "service");
        if (detail.Contains("permission_denied") || detail.Contains("permission denied"))
            return ("Gemini AI Access Needs Attention",
                "Gemini refused an AI processing request. Check the key's project permissions and AI Studio access, then retry. AI actions are paused until then.",
                "access");
        return null;
    }
}

/// <summary>The summary shown for narration status, both after a run and on demand.</summary>
public sealed class NarrationFailureReport
{
    public string Title { get; set; } = "Narration status";
    public string Detail { get; set; } = "";
    public int Generated { get; set; }
    public List<NarrationFailure> Failures { get; set; } = new();
    public List<string> Warnings { get; set; } = new();
}

public static class NarrationVoiceFormatting
{
    public static string EngineDisplayName(string? engine) => (engine ?? "").Trim().ToLowerInvariant() switch
    {
        "gemini" => "Gemini Voices",
        "system" => "Apple Voices",
        "windows-core" => "Windows OneCore",
        "sapi5" => "Windows SAPI 5",
        "windows-natural" => "Windows SAPI 5",
        "piper" => "Piper Voices",
        "unknown" or "" => "Unknown engine",
        _ => "Unknown engine"
    };

    private static readonly System.Text.RegularExpressions.Regex NaturalPackage =
        new(@"MicrosoftWindows\.Voice\.(?<locale>[A-Za-z]{2,3}-[A-Za-z0-9]{2,8})\.(?<speaker>[A-Za-z]+)", System.Text.RegularExpressions.RegexOptions.IgnoreCase);

    // Downloaded Piper voices use ids such as en_US-amy-low or en_GB-alan-medium.
    private static readonly System.Text.RegularExpressions.Regex PiperVoice =
        new(@"^(?<locale>[a-z]{2,3}_[A-Za-z]{2})-(?<name>.+)-(?<quality>x_low|low|medium|high)$", System.Text.RegularExpressions.RegexOptions.IgnoreCase);

    public static string VoiceDisplayName(string? voice)
    {
        var trimmed = (voice ?? "").Trim();
        if (string.IsNullOrEmpty(trimmed) || trimmed.StartsWith("default:", StringComparison.OrdinalIgnoreCase))
            return "System default";
        // Bridged Narrator voices are identified by their voice package name.
        if (NaturalPackage.Match(trimmed) is { Success: true } natural)
        {
            var speaker = natural.Groups["speaker"].Value;
            var hd = speaker.Length > 2 && speaker.EndsWith("HD", StringComparison.Ordinal);
            return $"Microsoft {(hd ? speaker[..^2] : speaker)} (Natural{(hd ? " HD" : "")}) - {natural.Groups["locale"].Value}";
        }
        if (PiperVoice.Match(trimmed) is { Success: true } piper)
        {
            var name = System.Text.RegularExpressions.Regex.Replace(piper.Groups["name"].Value, "[_-]+", " ");
            name = name.Length == 0 ? name : char.ToUpperInvariant(name[0]) + name[1..];
            var quality = piper.Groups["quality"].Value.Replace('_', ' ');
            return $"{name} ({quality}) — {piper.Groups["locale"].Value.Replace('_', '-')}";
        }
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
    public int ImportedCount { get; set; }
    public bool HasMixedSources { get; set; }
    public List<string> ImportedFiles { get; set; } = new();
}
