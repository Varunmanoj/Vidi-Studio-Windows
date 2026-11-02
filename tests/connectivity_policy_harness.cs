using System;
using Vidi_Studio_Windows.Services;
using Vidi_Studio_Windows.Models;
namespace Vidi_Studio_Windows.Services {
    public enum OnlineService { Gemini, YouTube, Voices }
    public sealed class SettingsStore { public string Mode = "windows-core"; public string Get(string key, string fallback) => Mode; }
}
namespace Vidi_Studio_Windows.Models {
    public sealed class AudioSourceSettings { public string Export = "youtube"; }
    public sealed class ImportedAudioDescription { public string Mode = "narration"; }
    public sealed class StudioProject { public bool IsYouTube; public double Duration = 10; public AudioSourceSettings? AudioSources; public ImportedAudioDescription? ImportedAudioDescription; }
}
class Program {
 static void Main() {
    if (!OnlineRequirements.AcceptsHost("m.youtube.com",OnlineService.YouTube) || !OnlineRequirements.AcceptsHost("www.youtube.com",OnlineService.YouTube)
        || OnlineRequirements.AcceptsHost("youtube.com.portal.example",OnlineService.YouTube) || OnlineRequirements.AcceptsHost("login.example",OnlineService.YouTube)) throw new Exception("Service redirects");
    var settings = new SettingsStore();var project = new StudioProject();
    void Check(string command, OnlineService? service, params string[] args) {
        var actual = OnlineRequirements.Services(command, project, settings, args);
        if (actual.Length != (service == null ? 0 : 1) || (service != null && actual[0] != service)) throw new Exception(command);
    }
    foreach (var command in new[] {"import-video", "prepare-video", "save-summary", "export-audio", "export-subtitles", "doctor", "synthesize-tts"}) Check(command,null);
    foreach (var command in OnlineRequirements.GeminiCommands) Check(command,OnlineService.Gemini);
    settings.Mode="gemini";Check("synthesize-tts",OnlineService.Gemini);
    settings.Mode="piper";Check("synthesize-tts",null);
    project.IsYouTube=true;Check("prepare-video",OnlineService.YouTube);Check("export-youtube-original-audio",OnlineService.YouTube);
    project.AudioSources=new AudioSourceSettings { Export="imported" };Check("export-youtube-original-audio",null);
    Check("export-youtube-original-audio",OnlineService.YouTube,"--soundtrack-source","youtube");
    project.AudioSources=null;project.ImportedAudioDescription=new ImportedAudioDescription { Mode="fullMix" };
    Check("combine-imported-audio",null);Check("export-youtube-described-audio",null,"--ad-source","imported");
    Check("export-youtube-described-audio",OnlineService.YouTube,"--ad-source","generated");Check("export-audio",null);
    project.Duration=0;Check("inspect-original-audio",null);
    if (OnlineRequirements.Services("preview-tts",project,settings,null,"gemini").Length!=1) throw new Exception("Draft voice");
    Console.WriteLine("PASS: Windows online requirements and local source/provider overrides");
 }
}
