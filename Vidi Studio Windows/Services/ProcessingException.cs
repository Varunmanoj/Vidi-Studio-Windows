using System;
using System.Text.RegularExpressions;

namespace Vidi_Studio_Windows.Services;

public sealed class ProcessingException : Exception
{
    public ProcessingException(string diagnostic) : base(UserMessage(diagnostic)) { }
    private static string UserMessage(string diagnostic)
    {
        // Only known validation messages can cross from engine diagnostics into UI.
        if (Regex.IsMatch(diagnostic, @"^AD segment \d+ takes [\d.]+s but has [\d.]+s\. Shorten its text or extend its gap\.$")) return diagnostic;
        foreach (var message in new[] {
            "AD segments must have finite, non-overlapping times inside the video duration.",
            "Transcript segments must have finite, non-overlapping times inside the video duration.",
            "Every AD segment needs description text.", "Every transcript segment needs text.",
            "An audio description overlaps spoken dialogue. Adjust its timing.",
            "Create audio description segments first.", "Synthesize all changed AD segments before exporting.",
            "Create and synthesize AD segments before exporting.",
            "Some descriptions have missing or outdated narration. Synthesize narration or confirm an incomplete export.",
            "The source video has no audio track to export.",
            "YouTube audio could not be downloaded. Check your connection and that the video is public, then try again. If YouTube blocks the download, import your original video as a local project and export its described audio instead.",
            "YouTube audio download timed out. Try again, or import your original video.",
            "YouTube audio duration does not match this project. The video may have been edited. Refresh its transcript and descriptions, or import the matching original video.",
            "No edit history in this direction.", "No summary available to export.", "No text available to export."
        }) if (diagnostic == message) return message;
        if (diagnostic.StartsWith("Windows speech failed:", StringComparison.Ordinal) || diagnostic.StartsWith("Windows speech host is missing", StringComparison.Ordinal))
            return SpeechService.FriendlyMessage(diagnostic);
        if (diagnostic.Contains("API_KEY_INVALID", StringComparison.OrdinalIgnoreCase) ||
            (diagnostic.Contains("invalid", StringComparison.OrdinalIgnoreCase) && diagnostic.Contains("API key", StringComparison.OrdinalIgnoreCase)))
            return "The Gemini API key is invalid or rejected by Google. Please enter a valid API key in Settings.";
        if (diagnostic.Contains("API key", StringComparison.OrdinalIgnoreCase)) return "Check your Gemini API key in Settings, save it, and retry. Existing results remain saved.";
        if (diagnostic.Contains("already", StringComparison.OrdinalIgnoreCase) && diagnostic.Contains("export", StringComparison.OrdinalIgnoreCase))
            return "An export of this type already exists. Select Replace existing in the Export dialog to replace it.";
        return "Processing could not finish. Completed results are saved. Check your settings and the diagnostic log, then retry.";
    }
}
