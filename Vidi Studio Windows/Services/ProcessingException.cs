using System;
using System.Text.RegularExpressions;

namespace Vidi_Studio_Windows.Services;

public sealed class ProcessingException : Exception
{
    private const string DuplicateProjectDiagnostic = "This project already exists. Please continue with the existing project or delete the existing project before continuing.";

    public string? RecoveryAction { get; }
    public string Diagnostic { get; }
    public bool IsDuplicateProject { get; }
    public ProcessingException(string diagnostic, string? recoveryAction = null, string? command = null)
        : base(recoveryAction == "choose-original-audio"
            ? "YouTube audio could not be retrieved. Choose a matching audio file to retry this export. Existing exports are unchanged."
            : UserMessage(diagnostic, command))
    {
        RecoveryAction = recoveryAction;
        Diagnostic = diagnostic;
        IsDuplicateProject = IsDuplicateProjectFailure(diagnostic, command);
    }

    public string DisplayMessage(bool developerMode, string apiKey)
    {
        if (!developerMode || string.IsNullOrEmpty(Diagnostic)) return Message;
        var detail = string.IsNullOrEmpty(apiKey) ? Diagnostic : Diagnostic.Replace(apiKey, "[redacted]");
        return Message + "\n\nTechnical details:\n" + detail;
    }
    private static bool IsDuplicateProjectFailure(string diagnostic, string? command) =>
        command is ("import-video" or "import-youtube") && diagnostic == DuplicateProjectDiagnostic;

    private static string UserMessage(string diagnostic, string? command)
    {
        if (diagnostic.Contains("storage.objects.get", StringComparison.OrdinalIgnoreCase) &&
            diagnostic.Contains("gcp-sa-aiplatform", StringComparison.OrdinalIgnoreCase))
            return "Ask your Google Cloud administrator to grant the Vertex AI service agent read access to this video, then retry.";
        if (command == "attach-youtube-playback" && Regex.IsMatch(diagnostic,
            @"^Playback video duration does not match\. YouTube is \d+\.\d{2} seconds and the local file is \d+\.\d{2} seconds\.$"))
            return diagnostic;
        if (command == "generate-summary")
            return "The summary could not be created. Your transcript is safe. Please try again.";

        if (IsDuplicateProjectFailure(diagnostic, command))
            return command == "import-video"
                ? "A project with this video filename already exists. Open it from All Projects, or rename the file if it’s a different video."
                : "A project for this YouTube video already exists. Open it from All Projects.";

        if (command is "cloud-import" or "cloud-preview")
        {
            return diagnostic switch
            {
                "Google Cloud denied permission to download this video. Check Google sign-in and your access to the object." or
                "VidiStudio could not verify download permission. Check your connection and Google Cloud access, then retry." or
                "The selected file has changed. Refresh the file list and choose it again." or
                "The selected video is no longer available in Cloud Storage. Refresh the file list." or
                "Downloaded video checksum did not match Cloud Storage. Retry the import." or
                "Choose a supported Cloud Storage video." or
                "Video File projects support files up to 2 GB. Choose a smaller video or use Cloud Storage." or
                "This video is too large for Cloud Storage analysis. Choose a smaller video." or
                "The selected video exceeds the supported import size." or
                "Choose a bucket in the selected Google Cloud project." or
                "Use Google sign-in in Settings > AI to connect your account." => diagnostic,
                _ => "Check Google sign-in, your access to the selected video, and your connection. Then try again."
            };
        }
        if (command == "cloud-delete-project-video")
        {
            if (diagnostic.Contains("changed", StringComparison.OrdinalIgnoreCase) ||
                diagnostic.Contains("newer cloud video", StringComparison.OrdinalIgnoreCase))
                return "The cloud video changed. Refresh the project and check its video in Cloud Storage before trying again.";
            return "Couldn’t delete the cloud video. Check Google sign-in, the project and bucket, and your permission to delete the video. Then try again.";
        }

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
            "Choose Vertex AI with Google sign-in in Settings > AI to analyze this Cloud Storage video.",
            "Connect Vertex AI with Google sign-in in Settings > AI to analyze this Cloud Storage video.",
            "Connect Gemini Developer API or sign in to Google for Vertex AI in Settings > AI to generate Gemini narration.",
            "Connect an AI provider in Settings > AI.",
            "Choose a readable video file with a video track.",
            "Refresh YouTube Details before choosing a local playback video.",
            "Local playback copies are available for YouTube projects.",
            "Remove the original audio file before importing a local playback video.",
            "Remove the local playback video before importing original audio.",
            "Choose a matching local playback video before exporting this project.",
            "The local playback video changed. Reopen Choose What to Delete and try again.",
            "The original audio changed. Reopen Choose What to Delete and try again.",
            "YouTube audio could not be downloaded. Check your connection and that the video is public, then try again. If YouTube blocks the download, import your original video as a local project and export its described audio instead.",
            "YouTube audio download timed out. Try again, or import your original video.",
            "YouTube audio duration does not match this project. The video may have been edited. Refresh its transcript and descriptions, or import the matching original video.",
            "No edit history in this direction.", "No summary available to export.", "No text available to export."
        }) if (diagnostic == message) return message;
        if (diagnostic.StartsWith("Windows speech failed:", StringComparison.Ordinal) || diagnostic.StartsWith("Windows speech host is missing", StringComparison.Ordinal))
            return SpeechService.FriendlyMessage(diagnostic);
        if (diagnostic.StartsWith("The bundled Piper speech engine is missing", StringComparison.Ordinal) ||
            diagnostic.StartsWith("The selected Piper voice", StringComparison.Ordinal) ||
            diagnostic.StartsWith("The Piper voice folder", StringComparison.Ordinal) ||
            diagnostic.StartsWith("Choose a Piper voice", StringComparison.Ordinal) ||
            diagnostic.StartsWith("Piper could not create audio", StringComparison.Ordinal) ||
            diagnostic.StartsWith("Piper did not finish", StringComparison.Ordinal) ||
            diagnostic.StartsWith("This text has no words to read aloud", StringComparison.Ordinal))
            return diagnostic;
        if (diagnostic.Contains("Sarvam", StringComparison.OrdinalIgnoreCase)) return diagnostic;
        if (diagnostic.Contains("API_KEY_INVALID", StringComparison.OrdinalIgnoreCase) ||
            (diagnostic.Contains("invalid", StringComparison.OrdinalIgnoreCase) && diagnostic.Contains("API key", StringComparison.OrdinalIgnoreCase)))
            return "The Gemini API key is invalid or rejected by Google. Please enter a valid API key in Settings.";
        if (diagnostic.Contains("API key", StringComparison.OrdinalIgnoreCase)) return "Check your Gemini API key in Settings, save it, and retry. Existing results remain saved.";
        if (diagnostic.Contains("already", StringComparison.OrdinalIgnoreCase) && diagnostic.Contains("export", StringComparison.OrdinalIgnoreCase))
            return "An export of this type already exists. Select Replace existing in the Export dialog to replace it.";
        return "Processing could not finish. Completed results are saved. Check your settings and the diagnostic log, then retry.";
    }
}
