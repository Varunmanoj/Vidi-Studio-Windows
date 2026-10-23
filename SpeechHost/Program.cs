using System.Globalization;
using System.Text.RegularExpressions;
using Microsoft.Win32;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using Windows.Management.Deployment;
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
            if (request.Engine is not ("windows-core" or "sapi5" or "windows-natural")) throw new ArgumentException("Unknown speech engine.");
            if (request.Command == "voices")
            {
                var voices = request.Engine switch { "windows-core" => CoreVoices(), "windows-natural" => NaturalVoices(), _ => SapiVoices() };
                Console.WriteLine(JsonSerializer.Serialize(voices));
            }
            else if (request.Command == "synthesize")
            {
                if (string.IsNullOrWhiteSpace(request.Text) || string.IsNullOrWhiteSpace(request.Output))
                    throw new ArgumentException("Text and output are required.");
                Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(request.Output))!);
                if (request.Engine == "windows-core") CoreSpeech(request).GetAwaiter().GetResult();
                else if (request.Engine == "windows-natural") SapiSpeech(request with { Voice = ResolveNaturalVoice(request.Voice) });
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

    // Narrator and Magnifier natural voices are Microsoft Store packages named
    // MicrosoftWindows.Voice.<locale>.<speaker>.<n>. Windows exposes them only to Narrator
    // and Magnifier, so Vidi Studio lists every installed package and synthesizes through a
    // SAPI 5 bridge the user has installed (for example NaturalVoiceSAPIAdapter).
    private const string NaturalPackagePrefix = "MicrosoftWindows.Voice.";
    private const string UnbridgedPrefix = "natural-package:";
    private static readonly Regex NaturalPackageName = new(@"^MicrosoftWindows\.Voice\.(?<locale>[A-Za-z]{2,3}-[A-Za-z0-9]{2,8})\.(?<speaker>[A-Za-z]+)", RegexOptions.IgnoreCase);
    private sealed record NaturalPackage(string PackageName, string Locale, string Speaker, string DisplayName);

    private static List<NaturalPackage> NaturalPackages()
    {
        var found = new Dictionary<string, NaturalPackage>(StringComparer.OrdinalIgnoreCase);
        void Add(string packageName, string? displayName)
        {
            var match = NaturalPackageName.Match(packageName);
            if (!match.Success || found.ContainsKey(packageName)) return;
            var locale = match.Groups["locale"].Value;
            var speaker = match.Groups["speaker"].Value;
            var display = (displayName ?? "").Trim();
            if (display.StartsWith('@') || display.StartsWith("ms-resource", StringComparison.OrdinalIgnoreCase) || !display.Contains("Natural", StringComparison.OrdinalIgnoreCase))
            {
                var hd = speaker.Length > 2 && speaker.EndsWith("HD", StringComparison.Ordinal);
                display = $"Microsoft {(hd ? speaker[..^2] : speaker)} (Natural{(hd ? " HD" : "")})";
            }
            else if (display.IndexOf(" - ", StringComparison.Ordinal) is var dash and > 0) display = display[..dash].Trim();
            found[packageName] = new NaturalPackage(packageName, locale, speaker, display);
        }
        try
        {
            foreach (var package in new PackageManager().FindPackagesForUser(string.Empty))
            {
                var name = package.Id.Name;
                if (!name.StartsWith(NaturalPackagePrefix, StringComparison.OrdinalIgnoreCase)) continue;
                string? display = null;
                try { display = package.DisplayName; } catch (Exception) { }
                Add(name, display);
            }
        }
        catch (Exception) { }
        try
        {
            // Package repository fallback when package queries are unavailable to this process.
            using var repository = Registry.CurrentUser.OpenSubKey(@"Software\Classes\Local Settings\Software\Microsoft\Windows\CurrentVersion\AppModel\Repository\Packages");
            foreach (var fullName in repository?.GetSubKeyNames() ?? Array.Empty<string>())
            {
                if (!fullName.StartsWith(NaturalPackagePrefix, StringComparison.OrdinalIgnoreCase)) continue;
                using var entry = repository!.OpenSubKey(fullName);
                Add(fullName.Split('_')[0], entry?.GetValue("DisplayName") as string);
            }
        }
        catch (Exception) { }
        return found.Values.ToList();
    }

    private static bool IsBridgedNaturalVoice(Voice voice) =>
        voice.Id.Contains("NaturalVoice", StringComparison.OrdinalIgnoreCase) || voice.Name.Contains("(Natural", StringComparison.OrdinalIgnoreCase);

    private static List<Voice> NaturalVoices()
    {
        List<Voice> bridged;
        try { bridged = SapiVoices().Where(IsBridgedNaturalVoice).ToList(); }
        catch (Exception) { bridged = new(); }
        var voices = new List<Voice>();
        var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var package in NaturalPackages())
        {
            var token = bridged.FirstOrDefault(v => !used.Contains(v.Id) && v.Id.Contains(package.PackageName, StringComparison.OrdinalIgnoreCase))
                ?? bridged.FirstOrDefault(v => !used.Contains(v.Id) && !v.Name.Contains("Online", StringComparison.OrdinalIgnoreCase)
                    && v.Name.Contains(package.Speaker, StringComparison.OrdinalIgnoreCase)
                    && string.Equals(v.Language, package.Locale, StringComparison.OrdinalIgnoreCase));
            if (token != null)
            {
                used.Add(token.Id);
                voices.Add(new Voice(token.Id, package.DisplayName, package.Locale, token.Gender, true, "Downloaded to this PC. Works without the internet.", "downloaded"));
            }
            else voices.Add(new Voice(UnbridgedPrefix + package.PackageName, package.DisplayName, package.Locale, "", false,
                "Downloaded for Narrator and Magnifier. Install the voice bridge add-on to use it in Vidi Studio.", "downloaded"));
        }
        foreach (var token in bridged.Where(v => !used.Contains(v.Id)))
            voices.Add(token.Name.Contains("Online", StringComparison.OrdinalIgnoreCase)
                ? token with { Note = "Online voice from the voice bridge add-on. Needs the internet.", Group = "bridge-online" }
                : token with { Note = "Downloaded to this PC. Works without the internet.", Group = "downloaded" });
        return voices.OrderBy(v => v.Available ? 0 : 1).ThenBy(v => v.Language, StringComparer.OrdinalIgnoreCase)
            .ThenBy(v => v.Name, StringComparer.CurrentCultureIgnoreCase).ToList();
    }

    private static string ResolveNaturalVoice(string voice)
    {
        if (voice.StartsWith(UnbridgedPrefix, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Windows only lets Narrator and Magnifier use this downloaded voice. Install the voice bridge add-on, then select Refresh voices in Settings.");
        var available = NaturalVoices().Where(v => v.Available).ToList();
        if (voice.Length > 0)
            return available.Any(v => v.Id == voice) ? voice
                : throw new InvalidOperationException("The selected natural voice can't be found. Select Refresh voices in Settings, or choose another SAPI host there.");
        return available.FirstOrDefault()?.Id
            ?? throw new InvalidOperationException("No downloaded natural voice is ready. Download voices in Windows Settings > Accessibility > Narrator, install the voice bridge add-on, then select Refresh voices.");
    }

    private sealed record Voice(string Id, string Name, string Language, string Gender, bool Available = true, string Note = "", string Group = "");
    private sealed record Request(string Command, string Engine, string Voice = "", string Text = "", string Output = "", double Rate = 1);
}
