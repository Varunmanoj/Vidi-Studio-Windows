using System;
using System.IO;
using System.Text.Json.Nodes;

namespace Vidi_Studio_Windows.Services;

public sealed class SettingsStore
{
    public static string DirectoryPath => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "VidiStudio");
    public static string ConfigPath => Environment.GetEnvironmentVariable("VIDISTUDIO_CONFIG") ?? Path.Combine(DirectoryPath, "config.json");
    public JsonObject Data { get; private set; }
    public SettingsStore()
    {
        var defaultsPath = Path.Combine(AppContext.BaseDirectory, "engine", "ai-defaults.json");
        Data = File.Exists(defaultsPath) ? JsonNode.Parse(File.ReadAllText(defaultsPath))!.AsObject() : new();
        if (File.Exists(ConfigPath)) Merge(Data, JsonNode.Parse(File.ReadAllText(ConfigPath))!.AsObject());
        if (Data["ttsMode"] == null || Get("ttsMode") == "system") Set("ttsMode", "windows-core");
        if (Data["libraryPath"] == null) Set("libraryPath", Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "VidiStudio Projects"));
        if (NormalizeNarrationPlaybackEngine()) Save();
    }
    private static void Merge(JsonObject target, JsonObject source)
    {
        foreach (var item in source)
            if (item.Value is JsonObject child && target[item.Key] is JsonObject existing) Merge(existing, child);
            else target[item.Key] = item.Value?.DeepClone();
    }
    public bool HasApiKey => !string.IsNullOrWhiteSpace(Get("apiKey")) && Get("apiKeyValid", "true") != "false" && Get("apiPlan", "") != "invalid";
    public bool IsApiKeyInvalid => !string.IsNullOrWhiteSpace(Get("apiKey")) && (Get("apiKeyValid", "") == "false" || Get("apiPlan", "") == "invalid");
    public string ApiPlan => Get("apiPlan", "auto");
    public void Reload()
    {
        var defaultsPath = Path.Combine(AppContext.BaseDirectory, "engine", "ai-defaults.json");
        Data = File.Exists(defaultsPath) ? JsonNode.Parse(File.ReadAllText(defaultsPath))!.AsObject() : new();
        if (File.Exists(ConfigPath)) Merge(Data, JsonNode.Parse(File.ReadAllText(ConfigPath))!.AsObject());
        if (Data["ttsMode"] == null || Get("ttsMode") == "system") Set("ttsMode", "windows-core");
        if (Data["libraryPath"] == null) Set("libraryPath", Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "VidiStudio Projects"));
        if (NormalizeNarrationPlaybackEngine()) Save();
    }
    public string Get(string key, string fallback = "") => Data[key]?.GetValue<string>() ?? fallback;
    public void Set(string key, string value)
    {
        Data[key] = value;
        if (key is "narrationPlayback" or "ttsMode") NormalizeNarrationPlaybackEngine();
    }
    private bool NormalizeNarrationPlaybackEngine()
    {
        var changed = false;
        if (Get("narrationPlayback", "generated") == "device")
        {
            Data["narrationPlayback"] = "ondevice";
            changed = true;
        }
        if (Get("narrationPlayback", "generated") is "ondevice" or "device" && Get("ttsMode") == "gemini")
        {
            Data["ttsMode"] = "windows-core";
            Data["systemVoice"] = Get("windowsCoreVoice");
            changed = true;
        }
        return changed;
    }
    public double Number(string key, double fallback = 1) => Data[key]?.GetValue<double>() ?? fallback;
    public void Save()
    {
        NormalizeNarrationPlaybackEngine();
        // These jobs now share the base selection for local and YouTube projects.
        if (Data["models"] is JsonObject models)
            foreach (var stage in new[] { "summary", "audio_descriptions", "screen_text", "text_rewrite", "tts_audio" })
                models.Remove("youtube_" + stage);
        Directory.CreateDirectory(Path.GetDirectoryName(ConfigPath)!);
        var temporary = ConfigPath + "." + Guid.NewGuid() + ".tmp";
        try { File.WriteAllText(temporary, Data.ToJsonString(Models.Json.Options)); File.Move(temporary, ConfigPath, true); }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }
}
