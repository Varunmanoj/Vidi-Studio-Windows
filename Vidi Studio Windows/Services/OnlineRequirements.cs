using System;
using System.Collections.Generic;
using System.Linq;
using Vidi_Studio_Windows.Models;
namespace Vidi_Studio_Windows.Services;

public static class OnlineRequirements
{
    public static bool AcceptsHost(string? host, OnlineService service)
    {
        var expected = service switch { OnlineService.Gemini => "generativelanguage.googleapis.com", OnlineService.YouTube => "youtube.com", _ => "huggingface.co" };
        return host != null && (host.Equals(expected, StringComparison.OrdinalIgnoreCase) || host.EndsWith("." + expected, StringComparison.OrdinalIgnoreCase));
    }
    public static readonly HashSet<string> GeminiCommands = new(StringComparer.Ordinal) {
        "transcribe", "generate-summary", "generate-ad-cues", "analyze", "regenerate-all",
        "reanalyze-segment", "reanalyze-transcript-segment", "rewrite-cue", "capture-frame",
        "check-api-key", "list-models", "list-voices", "validate-config", "align-imported-text",
        "prepare-analysis-video", "check-video-availability"
    };
    public static OnlineService[] Services(string command, StudioProject? project, SettingsStore settings, IEnumerable<string>? arguments = null, string? narrationProvider = null)
    {
        var extra = arguments?.ToArray() ?? Array.Empty<string>();
        if (GeminiCommands.Contains(command)) return new[] { OnlineService.Gemini };
        if (command is "synthesize-tts" or "synthesize-speech" or "preview-tts" or "synthesize-changed")
            return (narrationProvider ?? settings.Get("ttsMode", "windows-core")) == "gemini" ? new[] { OnlineService.Gemini } : Array.Empty<OnlineService>();
        if (command is "import-youtube" or "read-youtube-metadata" or "download-youtube-audio" or "youtube-playback") return new[] { OnlineService.YouTube };
        if (command == "download-voice") return new[] { OnlineService.Voices };
        if (project?.IsYouTube != true) return Array.Empty<OnlineService>();
        if (command == "prepare-video") return new[] { OnlineService.YouTube };
        if (command is "export-youtube-original-audio" or "export-youtube-described-audio" or "combine-imported-audio")
        {
            var adIndex = Array.IndexOf(extra, "--ad-source");
            var importedAD = adIndex >= 0 && adIndex + 1 < extra.Length && extra[adIndex + 1] == "imported";
            var fullMix = project.ImportedAudioDescription?.Mode == "fullMix" && (command == "combine-imported-audio" || importedAD);
            var index = Array.IndexOf(extra, "--soundtrack-source");
            var source = index >= 0 && index + 1 < extra.Length ? extra[index + 1] : project.AudioSources?.Export ?? "youtube";
            return fullMix || source == "imported" ? Array.Empty<OnlineService>() : new[] { OnlineService.YouTube };
        }
        return Array.Empty<OnlineService>();
    }
}

