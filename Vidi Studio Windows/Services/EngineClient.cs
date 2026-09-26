using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Vidi_Studio_Windows.Services;

public sealed record EngineEvent(string Message, double? Progress, JsonElement Data);

public sealed class EngineClient
{
    private readonly SettingsStore settings;
    private readonly SemaphoreSlim gate = new(1, 1);
    public EngineClient(SettingsStore settings) => this.settings = settings;
    public string SpeechHost => Path.Combine(AppContext.BaseDirectory, "speech", "VidiStudio.SpeechHost.exe");

    public async Task<List<EngineEvent>> RunAsync(IEnumerable<string> arguments, IProgress<EngineEvent>? progress = null,
        CancellationToken cancellation = default, string? configPath = null)
    {
        await gate.WaitAsync(cancellation);
        try
        {
            var frozen = Path.Combine(AppContext.BaseDirectory, "engine-bin", "vidistudio-engine.exe");
            var python = settings.Get("pythonPath", "python.exe");
            var start = new ProcessStartInfo(File.Exists(frozen) ? frozen : python)
            {
                UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true,
                RedirectStandardError = true, StandardOutputEncoding = Encoding.UTF8, StandardErrorEncoding = Encoding.UTF8,
                WorkingDirectory = AppContext.BaseDirectory
            };
            if (!File.Exists(frozen)) start.ArgumentList.Add(Path.Combine(AppContext.BaseDirectory, "engine", "vidistudio_engine.py"));
            foreach (var argument in arguments) start.ArgumentList.Add(argument);
            start.Environment["VIDISTUDIO_CONFIG"] = configPath ?? SettingsStore.ConfigPath;
            start.Environment["VIDISTUDIO_SPEECH_HOST"] = SpeechHost;
            start.Environment["PYTHONUTF8"] = "1";
            using var process = Process.Start(start) ?? throw new InvalidOperationException("Cannot start processing engine.");
            using var registration = cancellation.Register(() => { try { process.Kill(true); } catch (InvalidOperationException) { } });
            var errors = process.StandardError.ReadToEndAsync();
            var events = new List<EngineEvent>();
            while (await process.StandardOutput.ReadLineAsync() is { } line)
            {
                try
                {
                    using var doc = JsonDocument.Parse(line);
                    var root = doc.RootElement.Clone();
                    var message = root.TryGetProperty("message", out var m) ? m.GetString() ?? "Processing" : "Processing";
                    double? value = root.TryGetProperty("progress", out var p) && p.TryGetDouble(out var number) ? number : null;
                    var evt = new EngineEvent(message, value, root);
                    events.Add(evt);
                    // Error payloads contain diagnostics; do not display them as status text.
                    if (!root.TryGetProperty("error", out _)) progress?.Report(evt);
                }
                catch (JsonException) { /* Third-party stdout is not UI content. */ }
            }
            await process.WaitForExitAsync();
            cancellation.ThrowIfCancellationRequested();
            if (process.ExitCode != 0)
            {
                Log(string.Join(Environment.NewLine, events.ConvertAll(e => e.Message)) + Environment.NewLine + await errors);
                throw new ProcessingException(events.Count > 0 ? events[^1].Message : "");
            }
            return events;
        }
        finally { gate.Release(); }
    }

    public void Log(string detail)
    {
        var key = settings.Get("apiKey");
        if (!string.IsNullOrEmpty(key)) detail = detail.Replace(key, "[redacted]");
        Directory.CreateDirectory(SettingsStore.DirectoryPath);
        File.AppendAllText(Path.Combine(SettingsStore.DirectoryPath, "diagnostics.log"), $"{DateTimeOffset.Now:u} {detail}{Environment.NewLine}");
    }
}
