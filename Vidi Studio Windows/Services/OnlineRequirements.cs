using System;
using System.Collections.Generic;
using System.Linq;
using Vidi_Studio_Windows.Models;
namespace Vidi_Studio_Windows.Services;

internal enum AiConnection { Developer, GoogleSignIn, Express }

public static class OnlineRequirements
{
    public static bool AcceptsHost(string? host, OnlineService service)
    {
        var expected = service switch { OnlineService.ElevenLabs => "api.elevenlabs.io", OnlineService.Sarvam => "api.sarvam.ai", OnlineService.Gemini => "generativelanguage.googleapis.com", OnlineService.YouTube => "youtube.com", _ => "huggingface.co" };
        return host != null && (host.Equals(expected, StringComparison.OrdinalIgnoreCase) || host.EndsWith("." + expected, StringComparison.OrdinalIgnoreCase));
    }
    public static readonly HashSet<string> GeminiCommands = new(StringComparer.Ordinal) {
        "translate-results", "transcribe", "generate-summary", "generate-ad-cues", "analyze", "regenerate-all",
        "reanalyze-segment", "reanalyze-transcript-segment", "rewrite-cue", "capture-frame",
        "check-api-key", "list-models", "list-voices", "validate-config", "align-imported-text",
        "prepare-analysis-video", "check-video-availability"
    };
    internal static AiConnection Connection(string provider, string authentication) =>
        provider != "vertex" ? AiConnection.Developer : authentication == "express" ? AiConnection.Express : AiConnection.GoogleSignIn;
    internal static string Title(AiConnection connection) => connection switch {
        AiConnection.GoogleSignIn => "Vertex AI with Google sign-in",
        AiConnection.Express => "Vertex AI Express API key",
        _ => "Gemini Developer API"
    };
    internal static bool Supports(AiConnection connection, bool existingBucketVideo, bool narration) =>
        existingBucketVideo ? connection == AiConnection.GoogleSignIn : !narration || connection != AiConnection.Express;
    internal static bool VideoAICommand(string command) => command is "analyze" or "regenerate-all" or "analyze-video" or
        "transcribe" or "generate-summary" or "generate-ad-cues" or "reanalyze-segment" or "reanalyze-transcript-segment";
    internal static bool NarrationCommand(string command) => command is "synthesize-tts" or "synthesize-speech" or "preview-tts" or "synthesize-changed";
    internal static bool RequiresAIConnection(string command, bool usesGeminiNarration) =>
        NarrationCommand(command) ? usesGeminiNarration :
        GeminiCommands.Contains(command) && command is not ("check-api-key" or "list-models" or "list-voices" or "validate-config");

    public static OnlineService[] Services(string command, StudioProject? project, SettingsStore settings, IEnumerable<string>? arguments = null, string? narrationProvider = null)
    {
        var extra = arguments?.ToArray() ?? Array.Empty<string>();
        if (command == "elevenlabs-settings") return new[] { OnlineService.ElevenLabs };
        if (command == "check-sarvam-key") return new[] { OnlineService.Sarvam };
        if (GeminiCommands.Contains(command)) return new[] { OnlineService.Gemini };
        if (NarrationCommand(command))
            return (narrationProvider ?? settings.Get("ttsMode", "windows-core")) switch { "gemini" => new[] { OnlineService.Gemini }, "sarvam" => new[] { OnlineService.Sarvam }, "elevenlabs" => new[] { OnlineService.ElevenLabs }, _ => Array.Empty<OnlineService>() };
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


// Rechecks must not reopen a dismissed warning before the connection recovers.
internal sealed class VoiceSelectionWarning
{
    private OnlineService? warnedService;
    internal bool ShouldWarn(OnlineService? service, ConnectionState state)
    {
        if(service == null || state == ConnectionState.Available) { warnedService=null; return false; }
        if(state is not (ConnectionState.Offline or ConnectionState.Unreachable) || warnedService==service) return false;
        warnedService=service;
        return true;
    }
}
