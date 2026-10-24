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
        return Available ? label : label + " — unavailable";
    }
}

/// <summary>Where a downloaded voice comes from, shown as headings in the voice picker.</summary>
public static class VoiceGroups
{
    public const string Heading = "heading";
    public const string Downloaded = "downloaded";
}

/// <summary>A speech host failure; Message is safe to show, Detail is for the diagnostic log.</summary>
public sealed class SpeechFailedException : Exception
{
    public string Detail { get; }
    public SpeechFailedException(string detail) : base(SpeechService.FriendlyMessage(detail)) => Detail = detail;
    public override string ToString() => base.ToString() + System.Environment.NewLine + Detail;
}

public sealed class SpeechService
{
    private const string GenericVoiceFailure = "The selected voice could not speak. Try again or choose another voice in Settings.";

    /// <summary>
    /// Turns speech host or engine output into a message safe to show: the speech host's own
    /// first-line explanations pass through, anything else becomes a general voice message.
    /// </summary>
    public static string FriendlyMessage(string detail)
    {
        var text = (detail ?? "").Trim();
        const string enginePrefix = "Windows speech failed:";
        if (text.StartsWith(enginePrefix, StringComparison.Ordinal)) text = text[enginePrefix.Length..].Trim();
        var first = text.Split('\n', 2)[0].Trim();
        foreach (var known in new[] { "The selected voice", "The selected Windows voice", "Selected SAPI voice", "SAPI 5 is unavailable",
                     "Windows speech host is missing", "The speech helper stopped unexpectedly", "The bundled Piper speech engine is missing",
                     "The selected Piper voice", "The Piper voice folder", "Choose a Piper voice", "Piper could not create audio", "Piper did not finish" })
            if (first.StartsWith(known, StringComparison.Ordinal)) return first;
        return GenericVoiceFailure;
    }

    public static Task SynthesizeAsync(string host, string engine, string voice, string text, string output, double rate, CancellationToken cancellation)
    {
        if (engine == "piper")
        {
            var voiceId = (voice ?? "").Trim();
            if (voiceId.Length == 0 || !PiperService.IsVoiceDownloaded(voiceId))
                throw new SpeechFailedException("The selected Piper voice is not downloaded. Download it in Settings, then try again.");
            return RequestAsync(host, new
            {
                command = "synthesize", engine, voice = voiceId, text, output, rate,
                piperPath = PiperService.RuntimePath,
                piperModel = PiperService.ModelPath(voiceId),
                piperConfig = PiperService.ConfigPath(voiceId),
                piperSpeaker = 0
            }, cancellation);
        }
        return RequestAsync(host, new { command = "synthesize", engine, voice, text, output, rate }, cancellation);
    }

    public static Task<List<Voice>> VoicesAsync(string host, string engine, CancellationToken cancellation = default)
    {
        if (engine == "piper") return Task.FromResult(PiperService.InstalledVoices());
        return VoicesCoreAsync(host, engine, cancellation);
    }

    private static async Task<List<Voice>> VoicesCoreAsync(string host, string engine, CancellationToken cancellation)
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
        if (process.ExitCode != 0)
        {
            var detail = (await error).Trim();
            // An empty error usually means a voice crashed the helper process.
            if (detail.Length == 0) detail = $"The speech helper stopped unexpectedly (exit code {process.ExitCode}). The selected voice may have stopped it; try again or choose another voice.";
            throw new SpeechFailedException(detail);
        }
        return await output;
    }
}
