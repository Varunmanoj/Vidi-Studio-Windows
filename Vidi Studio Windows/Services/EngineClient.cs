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
        CancellationToken cancellation = default, string? configPath = null, string? apiKey = null)
    {
        var requestArguments = new List<string>(arguments);
        var projectIndex = requestArguments.IndexOf("--project");
        var project = projectIndex >= 0 && projectIndex + 1 < requestArguments.Count ? Models.StudioProject.TryLoad(requestArguments[projectIndex + 1]) : null;
        string? narrationProvider = null;
        if (configPath != null && File.Exists(configPath)) {
            using var draft = JsonDocument.Parse(File.ReadAllText(configPath));
            if (draft.RootElement.TryGetProperty("ttsMode", out var provider)) narrationProvider = provider.GetString();
        }
        var required = OnlineRequirements.Services(requestArguments[0], project, settings, requestArguments, narrationProvider);
        foreach (var service in required) ConnectivityService.Shared.Require(service);
        await gate.WaitAsync(cancellation);
        string? cancellationFile = null;
        string? temporaryConfig = null;
        try
        {
            foreach (var service in required) ConnectivityService.Shared.Require(service);
            var frozen = Path.Combine(AppContext.BaseDirectory, "engine-bin", "vidistudio-engine.exe");
            var python = settings.Get("pythonPath", "python.exe");
            var start = new ProcessStartInfo(File.Exists(frozen) ? frozen : python)
            {
                UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true,
                RedirectStandardError = true, StandardOutputEncoding = Encoding.UTF8, StandardErrorEncoding = Encoding.UTF8,
                WorkingDirectory = AppContext.BaseDirectory
            };
            if (!File.Exists(frozen)) start.ArgumentList.Add(Path.Combine(AppContext.BaseDirectory, "engine", "vidistudio_engine.py"));
            foreach (var argument in requestArguments) start.ArgumentList.Add(argument);
            var geminiKey = (apiKey ?? settings.Get("apiKey")).Trim();
            if (configPath == null && !string.IsNullOrEmpty(geminiKey))
            {
                // Create a transient execution config so frozen engines that read config.json
                // can receive the securely stored key without persisting it to SettingsStore.ConfigPath.
                var runConfigData = settings.Data.DeepClone().AsObject();
                runConfigData["apiKey"] = geminiKey;
                Directory.CreateDirectory(SettingsStore.DirectoryPath);
                temporaryConfig = Path.Combine(SettingsStore.DirectoryPath, "run-" + Guid.NewGuid() + ".json");
                File.WriteAllText(temporaryConfig, runConfigData.ToJsonString(Models.Json.Options));
            }
            start.Environment["VIDISTUDIO_CONFIG"] = configPath ?? temporaryConfig ?? SettingsStore.ConfigPath;
            // The Gemini key is held in Windows Credential Manager and handed to the engine per
            // process in the environment; it is never written to the config file on disk.
            if (geminiKey.Length > 0) start.Environment["VIDISTUDIO_API_KEY"] = geminiKey;
            start.Environment["VIDISTUDIO_SPEECH_HOST"] = SpeechHost;
            start.Environment["VIDISTUDIO_PIPER_PATH"] = PiperService.RuntimePath;
            start.Environment["VIDISTUDIO_PIPER_VOICES"] = PiperService.VoicesPath;
            start.Environment["PYTHONUTF8"] = "1";
            start.Environment["VIDISTUDIO_NETWORK_EVENTS"] = "1";
            start.Environment["VIDISTUDIO_YOUTUBE_AVAILABLE"] = ConnectivityService.Shared.Available(OnlineService.YouTube) ? "1" : "0";
            Directory.CreateDirectory(SettingsStore.DirectoryPath);
            cancellationFile = Path.Combine(SettingsStore.DirectoryPath, Guid.NewGuid() + ".cancel");
            start.Environment["VIDISTUDIO_CANCEL_FILE"] = cancellationFile;
            using var process = Process.Start(start) ?? throw new InvalidOperationException("Cannot start processing engine.");
            using var connectionCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellation);
            using var connectionWatch = new ConnectionRecoveryWatch(connectionCancellation.Cancel);
            using var registration = connectionCancellation.Token.Register(() =>
            {
                try { File.WriteAllText(cancellationFile, "cancel"); }
                catch (Exception) { try { process.Kill(true); } catch (InvalidOperationException) { } }
                _ = StopUnresponsiveProcessAsync(process);
            });
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
                    connectionWatch.Consume(root);
                    if (root.TryGetProperty("networkActive", out _)) continue;
                    var evt = new EngineEvent(message, value, root);
                    events.Add(evt);
                    // Error payloads contain diagnostics; do not display them as status text.
                    if (!root.TryGetProperty("error", out _)) progress?.Report(evt);
                }
                catch (JsonException) { /* Third-party stdout is not UI content. */ }
            }
            await process.WaitForExitAsync();
            if (connectionWatch.Interrupted) throw new ConnectivityException("The connection did not recover within 30 seconds. Connect to the internet and try again. Completed results remain saved.");
            cancellation.ThrowIfCancellationRequested();
            if (process.ExitCode != 0)
            {
                var stderr = await errors;
                Log(string.Join(Environment.NewLine, events.ConvertAll(e => e.Message)) + Environment.NewLine + stderr);
                var errDiagnostic = events.Count > 0 && !string.IsNullOrWhiteSpace(events[^1].Message)
                    ? events[^1].Message
                    : (!string.IsNullOrWhiteSpace(stderr) ? stderr : "");
                var recovery = events.Exists(e => e.Data.TryGetProperty("recovery", out var action)
                    && action.ValueKind == JsonValueKind.String && action.GetString() == "choose-original-audio")
                    ? "choose-original-audio" : null;
                if (events.Exists(e => e.Data.TryGetProperty("failureKind", out var kind) && kind.GetString() == "connectivity"))
                    { ConnectivityService.Shared.Check(); throw new ConnectivityException("Connection interrupted. Check your internet connection and try again. Completed results remain saved."); }
                throw new ProcessingException(errDiagnostic, recovery, requestArguments[0]);
            }
            return events;
        }
        finally
        {
            try { if (temporaryConfig != null && File.Exists(temporaryConfig)) File.Delete(temporaryConfig); } catch { }
            try { if (cancellationFile != null) File.Delete(cancellationFile); }
            finally { gate.Release(); }
        }
    }

    private static async Task StopUnresponsiveProcessAsync(Process process)
    {
        await Task.Delay(3000);
        try { if (!process.HasExited) process.Kill(true); } catch (InvalidOperationException) { }
    }

    public void Log(string detail)
    {
        var key = settings.Get("apiKey");
        if (!string.IsNullOrEmpty(key)) detail = detail.Replace(key, "[redacted]");
        Directory.CreateDirectory(SettingsStore.DirectoryPath);
        File.AppendAllText(Path.Combine(SettingsStore.DirectoryPath, "diagnostics.log"), $"{DateTimeOffset.Now:u} {detail}{Environment.NewLine}");
    }
}
