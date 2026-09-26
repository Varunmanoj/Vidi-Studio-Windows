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
            if (request.Engine is not ("windows-core" or "sapi5")) throw new ArgumentException("Unknown speech engine.");
            if (request.Command == "voices")
            {
                var voices = request.Engine == "windows-core" ? CoreVoices() : SapiVoices();
                Console.WriteLine(JsonSerializer.Serialize(voices));
            }
            else if (request.Command == "synthesize")
            {
                if (string.IsNullOrWhiteSpace(request.Text) || string.IsNullOrWhiteSpace(request.Output))
                    throw new ArgumentException("Text and output are required.");
                Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(request.Output))!);
                if (request.Engine == "windows-core") CoreSpeech(request).GetAwaiter().GetResult();
                else SapiSpeech(request);
                Console.WriteLine("{\"complete\":true}");
            }
            else throw new ArgumentException("Unknown speech command.");
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine(ex.Message);
            return 1;
        }
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
            speech.Speak(r.Text, 16); // SVSFIsNotXML: user text must never be interpreted as SSML.
        }
        finally { if (opened) stream.Close(); Release(tokens); Release(stream); Release(speech); }
    }

    private sealed record Voice(string Id, string Name, string Language, string Gender);
    private sealed record Request(string Command, string Engine, string Voice = "", string Text = "", string Output = "", double Rate = 1);
}
