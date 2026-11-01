using System;

namespace Vidi_Studio_Windows.Models;

public sealed class OriginalAudioAsset
{
    public string Path { get; set; } = "";
    public string SourceName { get; set; } = "";
    public double Duration { get; set; }
    public string Sha256 { get; set; } = "";
    public string? ValidationStatus { get; set; }
    public double? ConfirmedVideoDuration { get; set; }
    public bool? TimingAccepted { get; set; }
    public bool NeedsConfirmation(double videoDuration)
    {
        var known = double.IsFinite(videoDuration) && videoDuration > 0;
        if (known && Math.Abs(Duration - videoDuration) <= .100000001) return false;
        return TimingAccepted != true || ConfirmedVideoDuration != (known ? videoDuration : (double?)null);
    }
}

public sealed class AudioSourceSettings
{
    public string Export { get; set; } = "youtube";
    public string Playback { get; set; } = "youtube";
    public string Transcription { get; set; } = "youtube";
}

public sealed class AudioSourceProvenance
{
    public string Origin { get; set; } = "";
    public string? SourceName { get; set; }
    public string? YoutubeURL { get; set; }
    public string? Sha256 { get; set; }
    public string? AdSource { get; set; }
    public string? AdSourceName { get; set; }
    [System.Text.Json.Serialization.JsonIgnore]
    public string Label => Origin switch { "mixed" => "Multiple audio sources", "text" => "Imported text: " + SourceName,
        "youtube" => "Extracted from YouTube", _ => "Imported audio: " + SourceName };
}

public sealed class OriginalAudioInspection
{
    public double AudioDuration { get; set; }
    public double? VideoDuration { get; set; }
    public double? Difference { get; set; }
    public string Status { get; set; } = "";
    public string SourceName { get; set; } = "";
    public string Sha256 { get; set; } = "";
    public string Warning => (VideoDuration is { } duration
        ? $"Audio: {AudioDuration:F3} seconds. Video: {duration:F3} seconds. Difference: {Difference:F3} seconds."
        : "Duration could not be verified against the YouTube video.")
        + " Audio starts at zero. Excess audio is omitted and shorter audio ends in silence. Audio is never stretched.";
}
