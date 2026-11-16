using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;

namespace Vidi_Studio_Windows.Services;

internal static class ElevenLabsSpeech
{
    internal static JsonObject Profile(JsonObject data)
    {
        if(data["elevenlabsSpeech"] is not JsonObject profile) {
            profile=new JsonObject { ["model"]="",["voice"]="",["voiceName"]="",["language"]="auto",["format"]="mp3_44100_128",["controls"]=new JsonArray(),["stability"]=.5,["stabilityMode"]=.5,["similarity"]=.75,["style"]=0,["speed"]=1,["cloneStrength"]=0,["speakerBoost"]=true };
            data["elevenlabsSpeech"]=profile;
        }
        return profile;
    }
    internal static string Signature(JsonObject profile)
    {
        var fields=new System.Collections.Generic.List<string> {"elevenlabs",S(profile["voice"]),S(profile["model"]),S(profile["language"],"auto"),S(profile["format"],"mp3_44100_128")};
        foreach(var key in (profile["controls"] as JsonArray ?? new()).Select(v=>S(v)).OrderBy(v=>v,StringComparer.Ordinal)) {
            if(key=="speakerBoost")fields.Add(key+"="+(SettingsStore.GetBool(profile[key],true)?"1":"0"));
            else if(key is "stability" or "stabilityMode" or "similarity" or "style" or "speed" or "cloneStrength") fields.Add(key+"="+SettingsStore.GetDouble(profile[key],key=="similarity"?.75:key=="speed"?1:key.StartsWith("stability")?.5:0).ToString("F3",CultureInfo.InvariantCulture));
        }
        return string.Join("|",fields);
    }
    internal static string S(JsonNode? node,string fallback="")=>SettingsStore.GetString(node,fallback);
    internal static async Task<JsonObject> Request(string action,string key,JsonObject? payload=null,CancellationToken cancellation=default)
    {
        var frozen=Path.Combine(AppContext.BaseDirectory,"engine-bin","vidistudio-engine.exe");
        var start=new ProcessStartInfo(File.Exists(frozen)?frozen:"python.exe") { UseShellExecute=false,CreateNoWindow=true,RedirectStandardOutput=true,RedirectStandardError=true };
        if(!File.Exists(frozen))start.ArgumentList.Add(Path.Combine(AppContext.BaseDirectory,"engine","vidistudio_engine.py"));
        foreach(var arg in new[]{"elevenlabs-settings","--elevenlabs-action",action,"--elevenlabs-request",(payload??new()).ToJsonString()})start.ArgumentList.Add(arg);
        foreach(var name in new[]{"VIDISTUDIO_API_KEY","VIDISTUDIO_SARVAM_API_KEY","VIDISTUDIO_ELEVENLABS_API_KEY"})start.Environment.Remove(name);
        start.Environment["VIDISTUDIO_ELEVENLABS_API_KEY"]=key;start.Environment["PYTHONUTF8"]="1";
        using var timeout=CancellationTokenSource.CreateLinkedTokenSource(cancellation);timeout.CancelAfter(TimeSpan.FromSeconds(150));
        using var process=Process.Start(start)??throw new IOException("Could not start the voice service.");
        using var registration=timeout.Token.Register(()=>{try {if(!process.HasExited)process.Kill(true);}catch(InvalidOperationException){}});
        var errors=process.StandardError.ReadToEndAsync();JsonObject? result=null;
        while(await process.StandardOutput.ReadLineAsync() is {} line) {
            try {if(JsonNode.Parse(line)?["elevenlabs"] is JsonObject value)result=value; }catch(System.Text.Json.JsonException){}
        }
        await process.WaitForExitAsync();await errors;timeout.Token.ThrowIfCancellationRequested();
        return result??throw new IOException("ElevenLabs could not complete the request.");
    }
}
