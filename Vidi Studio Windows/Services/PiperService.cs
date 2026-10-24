using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Vidi_Studio_Windows.Services;

/// <summary>A voice model offered by the Piper voice catalog.</summary>
public sealed record PiperCatalogVoice(
    string Id,
    string Name,
    string Language,
    string Quality,
    long ModelSize,
    string ModelPath,
    string ModelMd5,
    long ConfigSize,
    string ConfigPath,
    string ConfigMd5,
    int Speakers)
{
    public string DisplayName => string.IsNullOrWhiteSpace(Quality)
        ? $"{Name} — {Language}"
        : $"{Name} ({Quality}) — {Language}";
    public string SizeLabel => ModelSize >= 1_000_000
        ? $"{ModelSize / 1_000_000.0:0.#} MB"
        : $"{Math.Max(1, ModelSize / 1_000)} KB";
    public string Note => Speakers > 1
        ? $"{Quality} quality · {SizeLabel} · {Speakers} speakers"
        : $"{Quality} quality · {SizeLabel}";
    public override string ToString() => $"{DisplayName} · {SizeLabel}";
}

/// <summary>
/// Owns the bundled Piper runtime and the downloaded Piper voice models. The engine itself
/// ships inside the app; only the much larger voice models are downloaded on demand.
/// </summary>
public static class PiperService
{
    public const string VoiceDownloadBase = "https://huggingface.co/rhasspy/piper-voices/resolve/main/";
    public const string CatalogUrl = "https://huggingface.co/rhasspy/piper-voices";

    private static readonly HttpClient Http = new() { Timeout = Timeout.InfiniteTimeSpan };

    public static string RootPath => Path.Combine(SettingsStore.DirectoryPath, "piper");
    public static string VoicesPath => Path.Combine(RootPath, "voices");
    public static string RuntimePath => Path.Combine(AppContext.BaseDirectory, "piper", "piper.exe");
    public static bool IsRuntimeAvailable => File.Exists(RuntimePath);

    public static string ModelPath(string id) => Path.Combine(VoicesPath, id + ".onnx");
    public static string ConfigPath(string id) => Path.Combine(VoicesPath, id + ".onnx.json");
    public static bool IsVoiceDownloaded(string id) =>
        !string.IsNullOrWhiteSpace(id) && File.Exists(ModelPath(id)) && File.Exists(ConfigPath(id));

    public static IReadOnlyList<PiperCatalogVoice> Catalog { get; } = LoadCatalog();
    public static IReadOnlyDictionary<string, PiperCatalogVoice> CatalogById { get; } =
        Catalog.ToDictionary(voice => voice.Id, StringComparer.OrdinalIgnoreCase);

    /// <summary>Downloaded voices in the shape used by the voice picker.</summary>
    public static List<Voice> InstalledVoices()
    {
        Directory.CreateDirectory(VoicesPath);
        return Directory.EnumerateFiles(VoicesPath, "*.onnx")
            .Select(Path.GetFileNameWithoutExtension)
            .Where(id => !string.IsNullOrWhiteSpace(id) && IsVoiceDownloaded(id!))
            .Select(id => CatalogById.TryGetValue(id!, out var catalog)
                ? new Voice(id!, $"{catalog.Name} ({catalog.Quality})", catalog.Language, "", true,
                    catalog.Note, VoiceGroups.Downloaded)
                : new Voice(id!, id!, "", "", true, "Downloaded Piper voice.", VoiceGroups.Downloaded))
            .OrderBy(voice => voice.Language, StringComparer.CurrentCultureIgnoreCase)
            .ThenBy(voice => voice.Name, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
    }

    public static async Task DownloadVoiceAsync(PiperCatalogVoice voice, IProgress<double>? progress, CancellationToken cancellation)
    {
        Directory.CreateDirectory(VoicesPath);
        var total = Math.Max(1, voice.ModelSize + voice.ConfigSize);
        long received = 0;
        void Report(long bytes) => progress?.Report(Math.Clamp((double)(received += bytes) / total, 0, 1));

        await DownloadFileAsync(VoiceDownloadBase + voice.ModelPath, ModelPath(voice.Id), voice.ModelSize, voice.ModelMd5, Report, cancellation);
        await DownloadFileAsync(VoiceDownloadBase + voice.ConfigPath, ConfigPath(voice.Id), voice.ConfigSize, voice.ConfigMd5, Report, cancellation);
        progress?.Report(1);
    }

    public static void DeleteVoice(string id)
    {
        if (string.IsNullOrWhiteSpace(id)) return;
        TryDelete(ModelPath(id));
        TryDelete(ConfigPath(id));
    }

    private static void TryDelete(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    private static async Task DownloadFileAsync(string url, string destination, long expectedSize, string expectedMd5,
        Action<long> report, CancellationToken cancellation)
    {
        var temporary = destination + "." + Guid.NewGuid().ToString("N") + ".part";
        Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
        try
        {
            using var response = await Http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, cancellation);
            response.EnsureSuccessStatusCode();
            await using var input = await response.Content.ReadAsStreamAsync(cancellation);
            await using var output = File.Create(temporary);
            using var md5 = MD5.Create();
            var buffer = new byte[81920];
            long written = 0;
            while (true)
            {
                var read = await input.ReadAsync(buffer, cancellation);
                if (read == 0) break;
                await output.WriteAsync(buffer.AsMemory(0, read), cancellation);
                md5.TransformBlock(buffer, 0, read, null, 0);
                written += read;
                report(read);
            }
            md5.TransformFinalBlock(Array.Empty<byte>(), 0, 0);
            if (expectedSize > 0 && written != expectedSize)
                throw new IOException($"The download was incomplete ({written} of {expectedSize} bytes). Try again.");
            if (!string.IsNullOrWhiteSpace(expectedMd5))
            {
                var actual = Convert.ToHexString(md5.Hash ?? Array.Empty<byte>()).ToLowerInvariant();
                if (!string.Equals(actual, expectedMd5, StringComparison.OrdinalIgnoreCase))
                    throw new IOException("The downloaded voice did not match its published checksum. Try again.");
            }
            output.Close();
            File.Move(temporary, destination, true);
        }
        finally
        {
            TryDelete(temporary);
        }
    }

    private static IReadOnlyList<PiperCatalogVoice> LoadCatalog()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "Assets", "piper-voices.json");
        if (!File.Exists(path)) return Array.Empty<PiperCatalogVoice>();
        try
        {
            using var document = JsonDocument.Parse(File.ReadAllText(path));
            var voices = new List<PiperCatalogVoice>();
            foreach (var entry in document.RootElement.EnumerateObject())
            {
                var item = entry.Value;
                if (!item.TryGetProperty("files", out var files)) continue;
                string modelPath = "", configPath = "", modelMd5 = "", configMd5 = "";
                long modelSize = 0, configSize = 0;
                foreach (var file in files.EnumerateObject())
                {
                    if (file.Name.EndsWith(".onnx.json", StringComparison.OrdinalIgnoreCase))
                    {
                        configPath = file.Name;
                        configSize = Number(file.Value, "size_bytes");
                        configMd5 = Text(file.Value, "md5_digest");
                    }
                    else if (file.Name.EndsWith(".onnx", StringComparison.OrdinalIgnoreCase))
                    {
                        modelPath = file.Name;
                        modelSize = Number(file.Value, "size_bytes");
                        modelMd5 = Text(file.Value, "md5_digest");
                    }
                }
                if (modelPath.Length == 0 || configPath.Length == 0) continue;
                var language = item.TryGetProperty("language", out var languageNode) ? languageNode : default;
                var code = Text(language, "code").Replace('_', '-');
                var name = Text(item, "name");
                if (name.Length == 0) name = entry.Name;
                var quality = Text(item, "quality");
                var speakers = (int)Number(item, "num_speakers", 1);
                voices.Add(new PiperCatalogVoice(entry.Name, Capitalize(name), code, quality,
                    modelSize, modelPath, modelMd5, configSize, configPath, configMd5, Math.Max(1, speakers)));
            }
            return voices.OrderBy(voice => voice.Language, StringComparer.CurrentCultureIgnoreCase)
                .ThenBy(voice => voice.Name, StringComparer.CurrentCultureIgnoreCase)
                .ThenBy(voice => voice.Quality, StringComparer.CurrentCultureIgnoreCase)
                .ToArray();
        }
        catch (JsonException) { return Array.Empty<PiperCatalogVoice>(); }
        catch (IOException) { return Array.Empty<PiperCatalogVoice>(); }
    }

    private static string Text(JsonElement element, string name) =>
        element.ValueKind == JsonValueKind.Object && element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString() ?? ""
            : "";

    private static long Number(JsonElement element, string name, long fallback = 0) =>
        element.ValueKind == JsonValueKind.Object && element.TryGetProperty(name, out var value) && value.TryGetInt64(out var number)
            ? number
            : fallback;

    private static string Capitalize(string value) =>
        string.IsNullOrEmpty(value) ? value : char.ToUpper(value[0], CultureInfo.CurrentCulture) + value[1..];
}
