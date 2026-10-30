namespace Vidi_Studio_Windows.Models;

/// <summary>Files selected before import starts; contains no project credentials.</summary>
public sealed record ImportAssetSelection(
    string? TranscriptPath = null, string? SummaryPath = null,
    string? ADTranscriptPath = null, string? ADAudioPath = null,
    string ADTranscriptMode = "narration", string ADAudioMode = "narration",
    bool ReplaceExisting = false)
{
    public bool HasAny => TranscriptPath != null || SummaryPath != null || ADTranscriptPath != null || ADAudioPath != null;
}
