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
    public double? AudioDuration { get; set; }
    [JsonExtensionData] public Dictionary<string, JsonElement>? Extra { get; set; }
    [JsonIgnore] public string Timing => $"{TimeSpan.FromSeconds(Start):hh\\:mm\\:ss\\.fff} – {TimeSpan.FromSeconds(End):hh\\:mm\\:ss\\.fff}";
    [JsonIgnore] public bool IsDescription { get; set; }
    [JsonIgnore] public bool ShowKind { get; set; }
    [JsonIgnore] public string ProjectFolder { get; set; } = "";
    [JsonIgnore] public string StartLabel => TimeSpan.FromSeconds(Start).ToString(Start >= 3600 ? @"hh\:mm\:ss" : @"mm\:ss");
    [JsonIgnore] public string EndLabel => TimeSpan.FromSeconds(End).ToString(End >= 3600 ? @"hh\:mm\:ss" : @"mm\:ss");
    [JsonIgnore] public string SeekLabel => $"Seek to {StartLabel}: {Text}";
    [JsonIgnore] public string PreviewLabel => $"Preview narration at {StartLabel}: {Text}";
    [JsonIgnore] public string KindLabel => IsDescription ? "Audio description" : "Transcript";
    public string Origin { get; set; } = "unknown";
    [JsonIgnore] public string OriginLabel => Origin switch { "ai" => "AI-generated", "ai_modified" => "AI-generated · modified", "manual" => "Manually added", _ => "Origin unknown" };
    [JsonIgnore] public string OriginColor => Origin switch { "ai" => "#C832DE", "ai_modified" => "#D87919", "manual" => "#0078D4", _ => "#777777" };
    [JsonIgnore] public bool AudioReady
    {
        get
        {
            if (string.IsNullOrEmpty(Audio) || AudioText != Text || string.IsNullOrEmpty(ProjectFolder)) return false;
            try { return File.Exists(new StudioProject { Folder = ProjectFolder }.Resolve(Audio)); } catch (InvalidDataException) { return false; }
        }
    }
    [JsonIgnore] public string AudioStatus => AudioReady ? "Synchronized audio generated" : Audio != null && AudioText != Text ? "Narration needs updating" : "Narration not generated";
    [JsonIgnore] public string AudioStatusColor => AudioReady ? "#209447" : "#9B701B";
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
    [JsonIgnore] public bool HasLocalSource
    {
        get { try { return !IsYouTube && !string.IsNullOrEmpty(Source) && File.Exists(Resolve(Source)); } catch (Exception) { return false; } }
    }
    [JsonIgnore] public bool HasCompleteExportNarration => double.IsFinite(Duration) && Duration > 0 && Cues.Count > 0 && Cues.All(c =>
    {
        if (string.IsNullOrWhiteSpace(c.Text) || c.AudioText != c.Text || string.IsNullOrEmpty(c.Audio) ||
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
        foreach (var cue in project.Cues) { cue.IsDescription = true; cue.ProjectFolder = project.Folder; }
        foreach (var item in project.Transcript) { item.IsDescription = false; item.ProjectFolder = project.Folder; }
        return project;
    }
    public override string ToString() => Name;
}

public static class Json
{
    public static readonly JsonSerializerOptions Options = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase, PropertyNameCaseInsensitive = true, WriteIndented = true };
}
