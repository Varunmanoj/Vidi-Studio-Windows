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
    public static string KindTitle(string kind) => kind switch { "transcript" => "Transcript", "summary" => "Summary", "descriptions" => "Descriptions and Narration", _ => "Imported AD Audio" };
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
