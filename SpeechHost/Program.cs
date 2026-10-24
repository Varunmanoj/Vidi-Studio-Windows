using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using Windows.Media.SpeechSynthesis;

namespace VidiStudio.SpeechHost;

internal static class Program
{
    // One JSON request on stdin; no text, credentials or voice IDs in command lines.
    [STAThread]
    private static int Main()
    {
        Console.InputEncoding = Encoding.UTF8;
        Console.OutputEncoding = new UTF8Encoding(false);
        try
        {
            var request = JsonSerializer.Deserialize<Request>(Console.In.ReadToEnd(),
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true }) ?? throw new ArgumentException("Missing request.");
            // "windows-natural" was an earlier name for natural voices offered through SAPI 5.
            if (request.Engine == "windows-natural") request = request with { Engine = "sapi5" };
            if (request.Engine is not ("windows-core" or "sapi5" or "piper")) throw new ArgumentException("Unknown speech engine.");
            if (request.Command == "voices")
            {
                if (request.Engine == "piper") throw new ArgumentException("Piper voices are listed by the app.");
                var voices = request.Engine == "windows-core" ? CoreVoices() : SapiVoices();
                Console.WriteLine(JsonSerializer.Serialize(voices));
            }
            else if (request.Command == "synthesize")
            {
                if (string.IsNullOrWhiteSpace(request.Text) || string.IsNullOrWhiteSpace(request.Output))
                    throw new ArgumentException("Text and output are required.");
                Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(request.Output))!);
                if (request.Engine == "windows-core")
                {
                    CoreSpeech(request).GetAwaiter().GetResult();
                    RequireAudio(request.Output);
                }
                else if (request.Engine == "piper")
                {
                    PiperSpeech(request);
                    RequireAudio(request.Output);
                }
                else
                {
                    // Some third-party SAPI voices fail or stay silent intermittently, so a failed
                    // attempt is retried a few times before reporting the error.
                    for (var attempt = 1; ; attempt++)
                    {
                        try { SapiSpeech(request); RequireAudio(request.Output); break; }
                        catch (Exception ex) when (attempt < SapiAttempts && ex is COMException or TimeoutException or SilentVoiceException)
                        {
                            RetryNotes.Add($"Attempt {attempt} of {SapiAttempts} failed; retried. {ex.GetType().Name}: {ex.Message}");
                            Thread.Sleep(800 * attempt);
                        }
                    }
                }
                Console.WriteLine("{\"complete\":true}");
            }
            else throw new ArgumentException("Unknown speech command.");
            return 0;
        }
        // The first stderr line is shown to people; technical details and retry notes follow it.
        catch (COMException ex)
        {
            Console.Error.WriteLine("The selected voice reported an error. Update the voice in Windows Settings, choose another voice, or check the diagnostics log.");
            Console.Error.WriteLine($"0x{ex.HResult:X8} {ex.Message}");
            foreach (var note in RetryNotes) Console.Error.WriteLine(note);
            return 1;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine(ex.Message);
            foreach (var note in RetryNotes) Console.Error.WriteLine(note);
            return 1;
        }
    }

    private const int SapiAttempts = 3;
    private static readonly List<string> RetryNotes = new();

    private sealed class SilentVoiceException : Exception
    {
        public SilentVoiceException() : base("The selected voice produced no audio. If it is an online voice, check the internet connection, then try again.") { }
    }

    /// <summary>A voice that fails silently must not be treated as narration.</summary>
    private static void RequireAudio(string output)
    {
        if (File.Exists(output) && new FileInfo(output).Length > 64) return;
        try { File.Delete(output); } catch (IOException) { }
        throw new SilentVoiceException();
    }

    private static List<Voice> CoreVoices() => SpeechSynthesizer.AllVoices
        .Select(v => new Voice(v.Id, v.DisplayName, v.Language, v.Gender.ToString())).ToList();

    private static async Task CoreSpeech(Request r)
    {
        using var synth = new SpeechSynthesizer();
        if (!string.IsNullOrEmpty(r.Voice)) synth.Voice = SpeechSynthesizer.AllVoices.FirstOrDefault(v => v.Id == r.Voice)
            ?? throw new InvalidOperationException("The selected Windows voice is no longer installed. Refresh voices in Settings.");
        synth.Options.SpeakingRate = Math.Clamp(r.Rate, .5, 2);
        using var audio = await synth.SynthesizeTextToStreamAsync(r.Text);
        using var input = audio.AsStreamForRead();
        using var output = File.Create(r.Output);
        await input.CopyToAsync(output);
    }

    private static dynamic CreateCom(string id) => Activator.CreateInstance(Type.GetTypeFromProgID(id)
        ?? throw new InvalidOperationException("SAPI 5 is unavailable for this host architecture."))!;
    private static void Release(object? value) { if (value != null && Marshal.IsComObject(value)) Marshal.FinalReleaseComObject(value); }

    private static List<Voice> SapiVoices()
    {
        dynamic speech = CreateCom("SAPI.SpVoice");
        dynamic? tokens = null;
        try
        {
            tokens = speech.GetVoices();
            var voices = new List<Voice>();
            for (int i = 0; i < tokens.Count; i++)
            {
                dynamic token = tokens.Item(i);
                try
                {
                    string language = token.GetAttribute("Language");
                    try { language = CultureInfo.GetCultureInfo(Convert.ToInt32(language.Split(';')[0], 16)).Name; } catch (ArgumentException) { }
                    voices.Add(new Voice(token.Id, token.GetDescription(), language, token.GetAttribute("Gender")));
                }
                finally { Release(token); }
            }
            return voices;
        }
        finally { Release(tokens); Release(speech); }
    }

    private const int SpeakTimeoutMs = 120_000;

    private static void SapiSpeech(Request r)
    {
        dynamic speech = CreateCom("SAPI.SpVoice");
        dynamic stream = CreateCom("SAPI.SpFileStream");
        dynamic? tokens = null;
        bool opened = false;
        try
        {
            if (!string.IsNullOrEmpty(r.Voice))
            {
                tokens = speech.GetVoices();
                bool found = false;
                for (int i = 0; i < tokens.Count; i++)
                {
                    dynamic token = tokens.Item(i);
                    try { if ((string)token.Id == r.Voice) { speech.Voice = token; found = true; break; } }
                    finally { Release(token); }
                }
                if (!found) throw new InvalidOperationException("Selected SAPI voice is not registered for this host architecture. Refresh voices or choose another SAPI host in Settings.");
            }
            speech.Rate = Math.Clamp((int)Math.Round((r.Rate - 1) * 10), -10, 10);
            speech.Volume = 100; // Engine applies volume and pitch once during normalization.
            stream.Open(r.Output, 3, false);
            opened = true;
            speech.AudioOutputStream = stream;
            // SVSFlagsAsync | SVSFIsNotXML: user text is never SSML, and a stalled voice (for example
            // an online voice that loses its connection) cannot hang the host indefinitely.
            speech.Speak(r.Text, 1 | 16);
            if (!(bool)speech.WaitUntilDone(SpeakTimeoutMs))
            {
                speech.Speak("", 2); // SVSFPurgeBeforeSpeak stops the stalled request.
                throw new TimeoutException("The selected voice did not finish speaking in time. If it is an online voice, check the internet connection, then try again.");
            }
        }
        finally { if (opened) stream.Close(); Release(tokens); Release(stream); Release(speech); }
    }

    private const int PiperTimeoutMs = 180_000;

    /// <summary>
    /// Runs the bundled Piper engine. Piper receives the text on stdin and writes a mono
    /// 16-bit WAV; the engine then applies pitch and volume while it normalizes the audio.
    /// </summary>
    private static void PiperSpeech(Request r)
    {
        if (string.IsNullOrWhiteSpace(r.PiperPath) || !File.Exists(r.PiperPath))
            throw new InvalidOperationException("The bundled Piper speech engine is missing. Reinstall Vidi Studio or run script\\fetch-piper.ps1, then build again.");
        if (string.IsNullOrWhiteSpace(r.PiperModel) || !File.Exists(r.PiperModel))
            throw new InvalidOperationException("The selected Piper voice is not downloaded. Download it in Settings, then try again.");
        if (string.IsNullOrWhiteSpace(r.PiperConfig) || !File.Exists(r.PiperConfig))
            throw new InvalidOperationException("The selected Piper voice is incomplete. Download it again in Settings.");
        var rate = Math.Clamp(r.Rate, .5, 2);
        var arguments = new List<string>
        {
            "--model", r.PiperModel,
            "--config", r.PiperConfig,
            "--output_file", r.Output,
            // Piper's length scale is the inverse of speaking rate.
            "--length_scale", (1 / rate).ToString("0.####", CultureInfo.InvariantCulture)
        };
        if (r.PiperSpeaker > 0)
        {
            arguments.Add("--speaker");
            arguments.Add(r.PiperSpeaker.ToString(CultureInfo.InvariantCulture));
        }
        var start = new ProcessStartInfo(r.PiperPath)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardInputEncoding = new UTF8Encoding(false),
            WorkingDirectory = Path.GetDirectoryName(Path.GetFullPath(r.PiperPath))!
        };
        foreach (var argument in arguments) start.ArgumentList.Add(argument);
        using var process = Process.Start(start) ?? throw new InvalidOperationException("Piper did not start.");
        var error = process.StandardError.ReadToEndAsync();
        var output = process.StandardOutput.ReadToEndAsync();
        process.StandardInput.Write(r.Text);
        process.StandardInput.Close();
        if (!process.WaitForExit(PiperTimeoutMs))
        {
            try { process.Kill(true); } catch (InvalidOperationException) { }
            throw new TimeoutException("Piper did not finish speaking in time. Try a shorter sample or choose another voice.");
        }
        if (process.ExitCode != 0)
        {
            var detail = error.GetAwaiter().GetResult().Trim();
            var first = detail.Split('\n', 2)[0].Trim();
            throw new InvalidOperationException(string.IsNullOrEmpty(first)
                ? "Piper could not create audio for the selected voice. Try again or choose another voice."
                : "Piper could not create audio: " + first);
        }
        _ = output.GetAwaiter().GetResult();
    }

    private sealed record Voice(string Id, string Name, string Language, string Gender);
    private sealed record Request(string Command, string Engine, string Voice = "", string Text = "", string Output = "", double Rate = 1,
        string PiperPath = "", string PiperModel = "", string PiperConfig = "", int PiperSpeaker = 0);
}
