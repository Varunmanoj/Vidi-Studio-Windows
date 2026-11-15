using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Vidi_Studio_Windows.Services;

public sealed record SarvamVoice(string Id, string Name, string Style, string Model, string[] Languages, string? DemoUrl)
{
    public string StyleLabel {
        get {
            var normalized = string.Join(" ",Style.Replace('_',' ').Replace('-',' ').Split(' ',StringSplitOptions.RemoveEmptyEntries)).ToLowerInvariant();
            return normalized switch {
                "ads" => "Advertising", "ads formal" => "Advertising — Formal", "ads informal" => "Advertising — Informal",
                "edtech" => "Education", "edtech bot" => "Education assistant", "kyc" => "Identity verification", "ecomm" => "Shopping",
                "customer" => "Customer support", "customer expressive" => "Customer support — Expressive", "customer utility" => "Customer support — Everyday", "customer warm" => "Customer support — Warm",
                "latenight" => "Late night", "latenight warm" => "Late night — Warm", "news breaking" => "Breaking news",
                _ => System.Globalization.CultureInfo.CurrentCulture.TextInfo.ToTitleCase(normalized)
            };
        }
    }
    public string Label => string.IsNullOrEmpty(Style) ? Name : Name + " — " + StyleLabel;
}
public sealed record SarvamCatalog(Dictionary<string,string> Languages, List<SarvamVoice> Voices);

public static class SarvamSpeech
{
    public static string[] Models => Catalog.Voices.Select(v => v.Model).Distinct().ToArray();
    public static string ModelLabel(string id) => id switch { "bulbul:v3" => "Bulbul v3", "bulbul:v2" => "Bulbul v2", "bulbul:v4-flash" => "Bulbul v4 Flash", _ => System.Globalization.CultureInfo.CurrentCulture.TextInfo.ToTitleCase(id.Replace(':',' ').Replace('-',' ')) };
    public static SarvamCatalog Catalog { get; } = LoadCatalog();
    private static SarvamCatalog LoadCatalog()
    {
        var file = Path.Combine(AppContext.BaseDirectory, "Assets", "sarvam-voices.json");
        try { return JsonSerializer.Deserialize<SarvamCatalog>(File.ReadAllText(file), new JsonSerializerOptions { PropertyNameCaseInsensitive = true })!; }
        catch (Exception ex) when (ex is IOException or JsonException) { return new(new(), new()); }
    }
    public static string Model(JsonObject data) => SettingsStore.GetString(data["sarvamSpeech"]?["model"], Models[0]);
    public static JsonObject Profile(JsonObject data)
    {
        var speech = data["sarvamSpeech"] as JsonObject;
        if (speech == null) { speech = new(); data["sarvamSpeech"] = speech; }
        var model = Model(data); speech["model"] = model;
        var profiles = speech["profiles"] as JsonObject;
        if (profiles == null) { profiles = new(); speech["profiles"] = profiles; }
        if (profiles[model] is not JsonObject profile)
        {
            profile = new JsonObject { ["language"] = "en-IN", ["voice"] = model == "bulbul:v2" ? "anushka" : model == "bulbul:v3" ? "shubh" : model == "bulbul:v4-flash" ? "shubh_enhi_ads" : "", ["pace"] = 1, ["temperature"] = .6, ["pitch"] = 0, ["loudness"] = 1, ["sampleRate"] = 22050, ["codec"] = "wav", ["preprocessing"] = false };
            profiles[model] = profile;
        }
        return profile;
    }
    public static string Voice(JsonObject data) => SettingsStore.GetString(Profile(data)["voice"]) is { Length: > 0 } voice ? voice : "Default voice";
}

public sealed record SummarySpeechProfile(string Engine, string Voice, double Rate, double Pitch, double Volume)
{
    public static JsonObject Independent(JsonObject data)
    {
        if (data["summarySpeech"] is JsonObject saved) return saved;
        var profile = new JsonObject {
            ["engine"] = "windows-core", ["windowsCoreVoice"] = SettingsStore.GetString(data["windowsCoreVoice"], SettingsStore.GetString(data["systemVoice"])),
            ["sapi5Voice"] = SettingsStore.GetString(data["sapi5Voice"]), ["piperVoice"] = SettingsStore.GetString(data["piperVoice"]),
            ["rate"] = SettingsStore.GetDouble(data["systemSpeechRate"],1), ["pitch"] = SettingsStore.GetDouble(data["systemSpeechPitch"],1), ["volume"] = SettingsStore.GetDouble(data["systemSpeechVolume"],1) };
        data["summarySpeech"] = profile; return profile;
    }
    public static SummarySpeechProfile Resolve(SettingsStore settings)
    {
        var mode = settings.Get("ttsMode", "windows-core");
        if (mode is "gemini" or "sarvam")
        {
            var profile = Independent(settings.Data);
            mode = SettingsStore.GetString(profile["engine"], "windows-core");
            if (mode is not ("windows-core" or "sapi5" or "piper")) mode = "windows-core";
            var key = mode == "piper" ? "piperVoice" : mode == "sapi5" ? "sapi5Voice" : "windowsCoreVoice";
            return new(mode, SettingsStore.GetString(profile[key]), Math.Clamp(SettingsStore.GetDouble(profile["rate"],1),.5,2), Math.Clamp(SettingsStore.GetDouble(profile["pitch"],1),.5,2), Math.Clamp(SettingsStore.GetDouble(profile["volume"],1),0,1));
        }
        if (mode is not ("windows-core" or "sapi5" or "piper")) mode = "windows-core";
        return new(mode, settings.Get(mode == "piper" ? "piperVoice" : "systemVoice"), settings.Number("systemSpeechRate"), settings.Number("systemSpeechPitch"), settings.Number("systemSpeechVolume"));
    }
}
