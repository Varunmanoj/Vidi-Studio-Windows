using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace Vidi_Studio_Windows.Models;

public sealed class LanguageResult
{
    public string Id { get; set; } = "";
    public string Kind { get; set; } = "";
    public string Language { get; set; } = "auto";
    public int Version { get; set; } = 1;
    public double CreatedAt { get; set; }
    public double UpdatedAt { get; set; }
    public string Origin { get; set; } = "unknown";
    public string State { get; set; } = "saved";
    public JsonElement Payload { get; set; }
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingDefault)]
    public JsonElement SourceAsset { get; set; }
    public string? ReplacesResultID { get; set; }
    public Dictionary<string, string>? SourceResultIDs { get; set; }
    public Dictionary<string, SavedAIProcessing>? AIProcessing { get; set; }
    [System.Text.Json.Serialization.JsonIgnore]
    public bool ShowsNarrationProvider
    {
        get
        {
            if (Kind != "descriptions") return false;
            if (AIProcessing?.ContainsKey("narration") == true) return true;
            if (Payload.ValueKind != JsonValueKind.Object || !Payload.TryGetProperty("cues", out var cues) ||
                cues.ValueKind != JsonValueKind.Array) return false;
            var narrationEngine = ReadString(Payload, "narrationEngine");
            return cues.EnumerateArray().Any(cue => !string.IsNullOrEmpty(ReadString(cue, "audio")) &&
                (ReadString(cue, "audioEngine") ?? ReadString(cue, "audioSettings")?.Split('|')[0] ?? narrationEngine) == "gemini");
        }
    }
    private static string? ReadString(JsonElement element, string name) =>
        element.ValueKind == JsonValueKind.Object && element.TryGetProperty(name, out var field) &&
        field.ValueKind == JsonValueKind.String ? field.GetString() : null;

    private JsonElement Field(string name) =>
        Payload.ValueKind == JsonValueKind.Object && Payload.TryGetProperty(name, out var field) ? field : default;

    private IEnumerable<JsonElement> Rows(string name) =>
        Field(name) is { ValueKind: JsonValueKind.Array } rows ? rows.EnumerateArray().ToArray() : Array.Empty<JsonElement>();

    [System.Text.Json.Serialization.JsonIgnore]
    internal string? ImportedSourceName
    {
        get
        {
            var source = ReadString(SourceAsset, "sourceName");
            var filename = Kind switch {
                "adAudio" => ReadString(Field("importedAudioDescription"), "sourceName"),
                "descriptions" => source ?? ReadString(Field("importedADTranscript"), "sourceName"),
                "summary" => source ?? (Origin is "imported" or "imported_modified" ? ReadString(Payload, "summaryOriginSource") : null),
                _ => Rows("transcript").Where(row => ReadString(row, "origin") == "imported")
                    .Select(row => ReadString(row, "originSource")).FirstOrDefault(name => name != null) ?? source
            };
            if (string.IsNullOrEmpty(filename)) return null;
            return filename.StartsWith("vidistudio-youtube-", StringComparison.Ordinal) ? "YouTube captions (SRT)" : filename;
        }
    }

    [System.Text.Json.Serialization.JsonIgnore]
    internal IEnumerable<string> ImportedNarrationSourceNames => Kind == "descriptions"
        ? Rows("cues").Where(row => ReadString(row, "audio") != null && ReadString(row, "audioOrigin") == "imported")
            .Select(row => ReadString(row, "audioSourceName")).OfType<string>()
        : Array.Empty<string>();

    [System.Text.Json.Serialization.JsonIgnore]
    internal string? AnalysisProvider
    {
        get
        {
            if (State != "saved" || Kind is not ("transcript" or "summary" or "descriptions")) return null;
            if (AIProcessing?.GetValueOrDefault("text") is { } processing)
                return SavedAIProcessing.ProviderChoices.Contains(processing.Provider) ? processing.Provider : "unknown";
            static bool IsNonAI(string? origin) => origin is "manual" or "imported" or "imported_modified";
            if (IsNonAI(Origin)) return null;
            if (Origin is "ai" or "ai_modified") return "unknown";
            if (Kind == "summary") return HasContent ? "unknown" : null;
            var key = Kind == "transcript" ? "transcript" : "cues";
            return Payload.ValueKind == JsonValueKind.Object && Payload.TryGetProperty(key, out var rows) &&
                rows.ValueKind == JsonValueKind.Array && rows.EnumerateArray().Any(row => !IsNonAI(ReadString(row, "origin")))
                ? "unknown" : null;
        }
    }
    [System.Text.Json.Serialization.JsonIgnore] public bool HasContent {
        get {
            if (Payload.ValueKind != JsonValueKind.Object) return false;
            var key = Kind switch { "transcript" => "transcript", "summary" => "summary", "descriptions" => "cues", _ => "importedAudioDescription" };
            if (Payload.TryGetProperty(key, out var value)) {
                if (Kind == "summary" && value.ValueKind == JsonValueKind.String) return !string.IsNullOrWhiteSpace(value.GetString());
                if (value.ValueKind == JsonValueKind.Array && value.GetArrayLength() > 0) return true;
                if (Kind == "adAudio" && value.ValueKind == JsonValueKind.Object && value.TryGetProperty("path", out var path)) return !string.IsNullOrWhiteSpace(path.GetString());
            }
            return Kind == "descriptions" && Payload.TryGetProperty("importedADTranscript", out var imported) && imported.ValueKind == JsonValueKind.Object && imported.TryGetProperty("segments", out var rows) && rows.ValueKind == JsonValueKind.Array && rows.GetArrayLength() > 0;
        }
    }
    [System.Text.Json.Serialization.JsonIgnore] public bool HasExportableText {
        get {
            if (Payload.ValueKind != JsonValueKind.Object) return false;
            if (Kind == "summary") return Payload.TryGetProperty("summary", out var summary) && summary.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(summary.GetString());
            if (Kind is not ("transcript" or "descriptions")) return false;
            bool HasText(JsonElement rows) => rows.ValueKind == JsonValueKind.Array && rows.EnumerateArray().Any(row => row.ValueKind == JsonValueKind.Object && row.TryGetProperty("text", out var text) && text.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(text.GetString()));
            if (Payload.TryGetProperty(Kind == "transcript" ? "transcript" : "cues", out var rows) && HasText(rows)) return true;
            return Kind == "descriptions" && Payload.TryGetProperty("importedADTranscript", out var imported) && imported.ValueKind == JsonValueKind.Object && imported.TryGetProperty("segments", out var importedRows) && HasText(importedRows);
        }
    }
    public string Title => $"Version {Version} · {Origin} · {DateTimeOffset.FromUnixTimeSeconds((long)CreatedAt).ToLocalTime():g}";
    public string Preview {
        get {
            if (Payload.ValueKind != JsonValueKind.Object) return "";
            if (Payload.TryGetProperty("summary", out var summary)) return summary.GetString() ?? "";
            if (Payload.TryGetProperty("importedAudioDescription", out var audio) && audio.ValueKind == JsonValueKind.Object && audio.TryGetProperty("sourceName", out var name)) return name.GetString() ?? "Imported narration";
            foreach (var key in new[] { "transcript", "cues" })
                if (Payload.TryGetProperty(key, out var rows) && rows.ValueKind == JsonValueKind.Array)
                    return string.Join("\n", rows.EnumerateArray().Select(row => row.TryGetProperty("text", out var text) ? text.GetString() : ""));
            return "No text in this result";
        }
    }
    public static string SavedResultsTitle(string kind) => kind switch {
        "transcript" => "Transcript Saved Results", "summary" => "Summary Saved Results",
        "descriptions" => "Description Saved Results", _ => "Imported AD Audio Saved Results"
    };
    public static string KindTitle(string kind) => kind switch { "transcript" => "Transcript", "summary" => "Summary", "descriptions" => "Descriptions and Narration", _ => "Imported AD Audio" };
}

public sealed class SavedAIProcessing
{
    public string Provider { get; set; } = "";
    public string Source { get; set; } = "";
    internal string Title => ProviderTitle(Provider) + (Source == "assigned" ? " (assigned)" : "");
    internal static IReadOnlyList<string> ProviderChoices { get; } =
        new[] { "gemini-developer", "vertex-ai", "vertex-express", "none", "unknown" };
    internal static IReadOnlyList<string> ProjectFilterOptions(IEnumerable<StudioProject> projects)
    {
        var providers = projects.SelectMany(project => project.ProjectFilterAIProviders).ToHashSet();
        return new[] { "all" }.Concat(ProviderChoices.Where(providers.Contains)).ToArray();
    }
    internal static string ProviderTitle(string provider) => provider switch
    {
        "all" => "All AI Providers",
        "gemini-developer" => "Gemini Developer API",
        "vertex-ai" => "Vertex AI",
        "vertex-express" => "Vertex AI Express",
        "none" => "No AI processing",
        _ => "Not recorded",
    };
}

public sealed class ExportResultProvenance
{
    public bool? Described { get; set; }
    public List<string>? Languages { get; set; }
    public bool Matches(string code) => code == "all" || (Languages ?? new List<string> { Language }).Contains(code);
    public string Language { get; set; } = "auto";
    public Dictionary<string, string> ResultIDs { get; set; } = new();
    public double CreatedAt { get; set; }
}

public sealed record OutputLanguage(string Id, string Title)
{
    public static IReadOnlyList<OutputLanguage> Options { get; } = Load();
    public static string Label(string code) => Options.FirstOrDefault(v => v.Id == code)?.Title ?? code;
    public static IReadOnlyList<OutputLanguage> ProjectFilterOptions(IEnumerable<StudioProject> projects) => projects
        .SelectMany(project => project.ProjectFilterLanguageCodes).Distinct()
        .Select(code => new OutputLanguage(code, Label(code)))
        .OrderBy(language => language.Title, StringComparer.CurrentCultureIgnoreCase).ToList();
    private static IReadOnlyList<OutputLanguage> Load()
    {
        var result = new List<OutputLanguage> { new("auto", "Auto") };
        var path = Path.Combine(AppContext.BaseDirectory, "engine", "ai-defaults.json");
        if (!File.Exists(path)) return result;
        using var doc = JsonDocument.Parse(File.ReadAllText(path));
        if (doc.RootElement.TryGetProperty("outputLanguageOptions", out var values))
            result.AddRange(values.EnumerateObject().Select(v => new OutputLanguage(v.Name, v.Value.GetString() ?? v.Name)).OrderBy(v => v.Title, StringComparer.CurrentCultureIgnoreCase));
        return result;
    }
}
