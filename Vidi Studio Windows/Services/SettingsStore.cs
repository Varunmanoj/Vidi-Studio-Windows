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
        if (NormalizeNarrationPlayback() | NormalizeSpeechEngine() | NormalizeSummaryVoice()) Save();
        if (MigrateApiKeyToCredentialStore()) Save();
    }
    /// <summary>
    /// One-time migration: move any plaintext key left in config.json into Windows Credential
    /// Manager and drop it from the file. Returns true when the file data changed.
    /// </summary>
    private bool MigrateApiKeyToCredentialStore()
    {
        if (Data["apiKey"] == null) return false;
        var legacy = GetString(Data["apiKey"]);
        // Keep the plaintext value in place rather than lose the key if the store is unavailable.
        if (!string.IsNullOrWhiteSpace(legacy) && !CredentialStore.Write(legacy)) return false;
        Data.Remove("apiKey");
        return true;
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
        if (NormalizeNarrationPlayback() | NormalizeSpeechEngine() | NormalizeSummaryVoice()) Save();
        if (MigrateApiKeyToCredentialStore()) Save();
    }
    public static string GetString(JsonNode? node, string fallback = "")
    {
        if (node == null) return fallback;
        if (node is JsonValue val)
        {
            if (val.TryGetValue<string>(out var str)) return str;
            if (val.TryGetValue<bool>(out var b)) return b ? "true" : "false";
            if (val.TryGetValue<double>(out var d)) return d.ToString(System.Globalization.CultureInfo.InvariantCulture);
            if (val.TryGetValue<long>(out var l)) return l.ToString(System.Globalization.CultureInfo.InvariantCulture);
            if (val.TryGetValue<int>(out var i)) return i.ToString(System.Globalization.CultureInfo.InvariantCulture);
            return val.ToString();
        }
        return node.ToJsonString();
    }
    public static double GetDouble(JsonNode? node, double fallback = 1)
    {
        if (node == null) return fallback;
        if (node is JsonValue val)
        {
            if (val.TryGetValue<double>(out var d)) return d;
            if (val.TryGetValue<long>(out var l)) return l;
            if (val.TryGetValue<int>(out var i)) return i;
            if (val.TryGetValue<string>(out var str) && double.TryParse(str, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var parsed)) return parsed;
        }
        return fallback;
    }
    public static bool GetBool(JsonNode? node, bool fallback = false)
    {
        if (node == null) return fallback;
        if (node is JsonValue val)
        {
            if (val.TryGetValue<bool>(out var b)) return b;
            if (val.TryGetValue<string>(out var str))
            {
                if (bool.TryParse(str, out var parsed)) return parsed;
                if (string.Equals(str, "true", StringComparison.OrdinalIgnoreCase)) return true;
                if (string.Equals(str, "false", StringComparison.OrdinalIgnoreCase)) return false;
            }
            if (val.TryGetValue<long>(out var l)) return l != 0;
            if (val.TryGetValue<int>(out var i)) return i != 0;
        }
        return fallback;
    }
    /// <summary>
    /// The Gemini API key is owned by Windows Credential Manager, never config.json, so reads
    /// and writes for "apiKey" are routed to the OS credential store.
    /// </summary>
    public string Get(string key, string fallback = "") => key switch
    {
        "apiKey" => CredentialStore.Read() ?? fallback,
        _ => GetString(Data[key], fallback)
    };
    public bool GetBool(string key, bool fallback = false) => GetBool(Data[key], fallback);
    public void Set(string key, string value)
    {
        if (key == "apiKey")
        {
            CredentialStore.Write(value);
            Data.Remove("apiKey");
            return;
        }
        Data[key] = value;
        if (key == "narrationPlayback") NormalizeNarrationPlayback();
    }
    public void Set(string key, bool value) => Data[key] = value;
    public void Set(string key, double value) => Data[key] = value;
    /// <summary>
    /// Moves legacy engine choices to a supported local engine. Natural voices used to be a
    /// separate "windows-natural" engine but are SAPI 5 voices, and Azure Voices was removed.
    /// </summary>
    private bool NormalizeSpeechEngine()
    {
        var changed = false;
        if (Get("ttsMode") == "windows-natural")
        {
            Data["ttsMode"] = "sapi5";
            var natural = Get("windowsNaturalVoice", Get("systemVoice"));
            // Unbridged Narrator packages were listed as "natural-package:" and can never speak.
            if (natural.StartsWith("natural-package:", StringComparison.Ordinal)) natural = "";
            Data["sapi5Voice"] = natural; Data["systemVoice"] = natural;
            Data.Remove("windowsNaturalVoice");
            changed = true;
        }
        if (Get("ttsMode") == "azure")
        {
            Data["ttsMode"] = "windows-core";
            Data.Remove("azureVoice");
            Data.Remove("azureSpeechRegion");
            Data.Remove("azureSpeechKey");
            changed = true;
        }
        return changed;
    }
    /// <summary>The summary now always uses the narration voice; drop the removed Azure option.</summary>
    private bool NormalizeSummaryVoice()
    {
        if (Get("summaryReadbackVoice", "narration") != "azure") return false;
        Data["summaryReadbackVoice"] = "narration";
        return true;
    }
    private bool NormalizeNarrationPlayback()
    {
        if (Get("narrationPlayback", "generated") != "device") return false;
        Data["narrationPlayback"] = "ondevice";
        return true;
    }
    public double Number(string key, double fallback = 1) => GetDouble(Data[key], fallback);
    public void Save()
    {
        NormalizeNarrationPlayback();
        // The Gemini key lives only in Windows Credential Manager; never persist it here.
        Data.Remove("apiKey");
        Data.Remove("azureSpeechKey");
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
