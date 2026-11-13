namespace Vidi_Studio_Windows.Models;

/// <summary>Files selected before import starts; contains no project credentials.</summary>
public sealed record ImportAssetSelection(
    string? TranscriptPath = null, string? SummaryPath = null,
    string? ADTranscriptPath = null, string? ADAudioPath = null,
    string ADTranscriptMode = "narration", string ADAudioMode = "narration",
    string? OriginalAudioPath = null,
    bool OriginalAudioPlayback = false, bool OriginalAudioTranscription = false,
    string? CaptionProvenancePath = null, string? Language = null)
{
    public bool HasAny => OriginalAudioPath != null || TranscriptPath != null || SummaryPath != null || ADTranscriptPath != null || ADAudioPath != null;
}
