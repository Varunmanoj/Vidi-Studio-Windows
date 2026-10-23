using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Vidi_Studio_Windows.Services;

public sealed record Voice(string Id, string Name, string Language, string Gender, bool Available = true, string Note = "", string Group = "")
{
    public override string ToString()
    {
        if (Group == VoiceGroups.Heading) return Name;
        var label = string.IsNullOrWhiteSpace(Language) ? Name : string.IsNullOrWhiteSpace(Gender) ? $"{Name} — {Language}" : $"{Name} — {Language} ({Gender})";
        return Available ? label : label + " — needs the voice bridge add-on";
    }
}

/// <summary>Where a natural voice comes from, shown as headings in the voice picker.</summary>
public static class VoiceGroups
{
    public const string Heading = "heading";
    public const string Downloaded = "downloaded";
    public const string Online = "online";
    public const string BridgeOnline = "bridge-online";
}

public sealed class SpeechService
{
    public const string DefaultAzureVoice = "en-US-AvaMultilingualNeural";
    public const string DefaultAzureRegion = "eastus";
    public static async Task SynthesizeAsync(string host, string engine, string voice, string text, string output, double rate, CancellationToken cancellation)
    {
        await RequestAsync(host, new { command = "synthesize", engine, voice, text, output, rate }, cancellation);
    }
    public static async Task<List<Voice>> VoicesAsync(string host, string engine, CancellationToken cancellation = default)
    {
        var output = await RequestAsync(host, new { command = "voices", engine }, cancellation);
        return JsonSerializer.Deserialize<List<Voice>>(output, Models.Json.Options) ?? new();
    }
    private static async Task<string> RequestAsync(string host, object request, CancellationToken cancellation)
    {
        var info = new ProcessStartInfo(host) { UseShellExecute = false, CreateNoWindow = true,
            RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true,
            StandardInputEncoding = new UTF8Encoding(false), StandardOutputEncoding = Encoding.UTF8 };
        using var process = Process.Start(info) ?? throw new InvalidOperationException("Speech host did not start.");
        using var registration = cancellation.Register(() => { try { process.Kill(true); } catch (InvalidOperationException) { } });
        var output = process.StandardOutput.ReadToEndAsync();
        var error = process.StandardError.ReadToEndAsync();
        await process.StandardInput.WriteAsync(JsonSerializer.Serialize(request));
        process.StandardInput.Close();
        await process.WaitForExitAsync(cancellation);
        if (process.ExitCode != 0) throw new InvalidOperationException(await error);
        return await output;
    }
}
