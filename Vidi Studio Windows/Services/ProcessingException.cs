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
            "No edit history in this direction.", "No summary available to export.", "No text available to export."
        }) if (diagnostic == message) return message;
        if (diagnostic.Contains("API key", StringComparison.OrdinalIgnoreCase)) return "Check your Gemini API key in Settings, save it, and retry. Existing results remain saved.";
        if (diagnostic.Contains("already", StringComparison.OrdinalIgnoreCase) && diagnostic.Contains("export", StringComparison.OrdinalIgnoreCase))
            return "An export of this type already exists. Select Replace existing in the Export dialog to replace it.";
        return "Processing could not finish. Completed results are saved. Check your settings and the diagnostic log, then retry.";
    }
}
