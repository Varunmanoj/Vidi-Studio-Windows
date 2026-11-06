using Vidi_Studio_Windows.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Vidi_Studio_Windows.Models;

namespace Vidi_Studio_Windows.Views;

public sealed partial class ExportDialog
{
    private bool combineTracks;
    private bool originalTrack = true;
    private string trackAudioMode = "selectable", trackCaptionMode = "closed", trackAudioFormat = "m4a";
    private readonly Dictionary<string, string> audioTracks = new(), captionTracks = new(), descriptionTracks = new();
    private readonly HashSet<string> adCaptionTracks = new();
    private bool SelectingTracks => combineTracks && sidebar.SelectedIndex is 0 or 1;
    private bool TrackSelectionValid => owner != null && (sidebar.SelectedIndex == 0 || originalTrack || audioTracks.Count > 0)
        && (trackAudioMode != "mixed" || audioTracks.Count <= 1)
        && (sidebar.SelectedIndex != 0 || trackCaptionMode != "open" || captionTracks.Count <= 1)
        && (sidebar.SelectedIndex != 0 || captionTracks.Keys.All(code => !adCaptionTracks.Contains(code) || descriptionTracks.ContainsKey(code)));

    private void BuildTrackChoices()
    {
        var project = model.Project!;
        var video = sidebar.SelectedIndex == 0;
        AddFormat("Format", video ? new[] { "mkv", "mp4", "mov", "m4v" } : new[] { "m4a", "mka" }, video ? videoFormat : trackAudioFormat, value => { if (video) videoFormat = value; else trackAudioFormat = value; UpdatePreview(); });
        var audioMode = new ComboBox { Header = "Audio Description", ItemsSource = new[] { "Selectable tracks", "Mixed into audio · one language" }, SelectedIndex = trackAudioMode == "mixed" ? 1 : 0 };
        audioMode.SelectionChanged += (_, _) => {
            trackAudioMode = audioMode.SelectedIndex == 1 ? "mixed" : "selectable";
            if (trackAudioMode == "mixed" && audioTracks.Count > 1) { var first = audioTracks.OrderBy(p => p.Key, StringComparer.Ordinal).First(); audioTracks.Clear(); audioTracks[first.Key] = first.Value; }
            BuildPage();
        };
        page.Children.Add(audioMode);
        if (trackAudioMode == "selectable" || audioTracks.Count == 0) {
            var original = new CheckBox { Content = "Include Original Audio", IsChecked = originalTrack };
            original.Checked += (_, _) => { originalTrack = true; UpdatePreview(); };
            original.Unchecked += (_, _) => { originalTrack = false; UpdatePreview(); };
            page.Children.Add(original);
        }
        if (video) {
            var captions = new ComboBox { Header = "Captions", ItemsSource = new[] { "Selectable closed captions", "Burned into video · one language" }, SelectedIndex = trackCaptionMode == "open" ? 1 : 0 };
            captions.SelectionChanged += (_, _) => {
                trackCaptionMode = captions.SelectedIndex == 1 ? "open" : "closed";
                if (trackCaptionMode == "open" && captionTracks.Count > 1) { var first = captionTracks.OrderBy(p => p.Key, StringComparer.Ordinal).First(); captionTracks.Clear(); captionTracks[first.Key] = first.Value; }
                BuildPage();
            };
            page.Children.Add(captions);
        }
        foreach (var code in project.SavedResults.Where(r => r.State == "saved" && r.HasContent).Select(r => r.Language).Distinct().OrderBy(v => v, StringComparer.Ordinal)) {
            var generatedSelection = project.SavedResults.FirstOrDefault(r => audioTracks.TryGetValue(code, out var id) && r.Id == id && r.Kind == "descriptions");
            if (generatedSelection != null) descriptionTracks[code] = generatedSelection.Id;
            var panel = new StackPanel { Spacing = 8 };
            panel.Children.Add(new TextBlock { Text = OutputLanguage.Label(code), FontWeight = Microsoft.UI.Text.FontWeights.SemiBold });
            var audio = project.SavedResults.Where(r => r.State == "saved" && r.Language == code && r.HasContent && r.Kind is "descriptions" or "adAudio").ToList();
            if (audio.Count > 0) {
                var toggle = new CheckBox { Content = "AD Audio", IsChecked = audioTracks.ContainsKey(code) };
                toggle.Checked += (_, _) => { if (trackAudioMode == "mixed") audioTracks.Clear(); audioTracks[code] = audio[0].Id; BuildPage(); };
                toggle.Unchecked += (_, _) => { audioTracks.Remove(code); BuildPage(); };
                panel.Children.Add(toggle);
                if (audioTracks.ContainsKey(code)) AddTrackResult(panel, "Audio Saved Result", audio, audioTracks, code);
            }
            var transcripts = project.SavedResults.Where(r => r.State == "saved" && r.Language == code && r.Kind == "transcript" && r.HasContent).ToList();
            if (video && transcripts.Count > 0) {
                var toggle = new CheckBox { Content = "Captions", IsChecked = captionTracks.ContainsKey(code) };
                toggle.Checked += (_, _) => { if (trackCaptionMode == "open") captionTracks.Clear(); captionTracks[code] = transcripts[0].Id; BuildPage(); };
                toggle.Unchecked += (_, _) => { captionTracks.Remove(code); BuildPage(); };
                panel.Children.Add(toggle);
                if (captionTracks.ContainsKey(code)) {
                    AddTrackResult(panel, "Transcript Saved Result", transcripts, captionTracks, code);
                    var descriptions = project.SavedResults.Where(r => r.State == "saved" && r.Language == code && r.Kind == "descriptions" && r.HasContent).ToList();
                    var combined = new CheckBox { Content = "AD Transcript", IsChecked = adCaptionTracks.Contains(code), IsEnabled = descriptions.Count > 0 };
                    combined.Checked += (_, _) => { adCaptionTracks.Add(code); if (!descriptionTracks.ContainsKey(code)) descriptionTracks[code] = generatedSelection?.Id ?? descriptions[0].Id; BuildPage(); };
                    combined.Unchecked += (_, _) => { adCaptionTracks.Remove(code); BuildPage(); };
                    panel.Children.Add(combined);
                    if (adCaptionTracks.Contains(code) && generatedSelection == null) AddTrackResult(panel, "Description Saved Result", descriptions, descriptionTracks, code);
                }
            }
            page.Children.Add(Card(panel));
        }
    }
    private void AddTrackResult(StackPanel panel, string label, List<LanguageResult> rows, Dictionary<string,string> selected, string code)
    {
        var picker = new ComboBox { Header = label, HorizontalAlignment = HorizontalAlignment.Stretch };
        if (ExportProject is { } project) SavedResultOptions.Populate(picker, rows, project, selected.TryGetValue(code, out var chosen) ? chosen : project.ActiveResultID(rows[0].Kind, code), captionTracks.TryGetValue(code, out var transcript) ? new Dictionary<string,string> { ["transcript"] = transcript } : null);
        picker.SelectionChanged += (_, _) => { if (picker.SelectedIndex < 0) return; selected[code] = rows[picker.SelectedIndex].Id; BuildPage(); };
        panel.Children.Add(picker);
    }
    private Dictionary<string, object> TrackPlan()
    {
        var video = sidebar.SelectedIndex == 0;
        return new() { ["media"] = video ? "video" : "audio", ["format"] = video ? videoFormat : trackAudioFormat,
            ["original"] = originalTrack, ["audioMode"] = trackAudioMode, ["captionMode"] = trackCaptionMode,
            ["audio"] = audioTracks.OrderBy(p => p.Key, StringComparer.Ordinal).Select(p => new Dictionary<string,string> { ["language"] = p.Key, ["resultID"] = p.Value }).ToArray(),
            ["captions"] = video ? captionTracks.OrderBy(p => p.Key, StringComparer.Ordinal).Select(p => new Dictionary<string,string> { ["language"] = p.Key, ["transcriptID"] = p.Value, ["descriptionsID"] = adCaptionTracks.Contains(p.Key) && descriptionTracks.TryGetValue(p.Key, out var id) ? id : "" }).ToArray() : Array.Empty<Dictionary<string,string>>() };
    }
    private string TrackPath(StudioProject project)
    {
        var video = sidebar.SelectedIndex == 0;
        var media = video ? "video" : "audio";
        var format = video ? videoFormat : trackAudioFormat;
        var codes = audioTracks.Keys.Concat(video ? captionTracks.Keys : Array.Empty<string>()).Distinct().OrderBy(v => v, StringComparer.Ordinal).ToList();
        if (codes.Count == 0) codes.Add("auto");
        var items = new List<string> { media, format, trackAudioMode, trackCaptionMode, originalTrack ? "original" : "no-original" };
        items.AddRange(audioTracks.OrderBy(p => p.Key, StringComparer.Ordinal).Select(p => "a:" + p.Value));
        if (video) items.AddRange(captionTracks.OrderBy(p => p.Key, StringComparer.Ordinal).Select(p => "s:" + p.Value + ":" + (adCaptionTracks.Contains(p.Key) && descriptionTracks.TryGetValue(p.Key, out var id) ? id : "")));
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(string.Join("\n", items)))).ToLowerInvariant()[..12];
        var labels = string.Join(" + ", codes.Select(OutputLanguage.Label));
        labels = labels[..Math.Min(80, labels.Length)];
        var name = string.Concat(project.Name.EnumerateRunes().Take(40).Select(r => r.ToString())) + " [" + labels + "]";
        name = Regex.Replace(name.Trim(), "[<>:\"/\\\\|?*\\x00-\\x1f]+", "-");
        name = Regex.Replace(Regex.Replace(name, @"\s*-\s*", " - "), @"\s+", " ").Trim(' ', '.', '-');
        name = string.Concat(name.EnumerateRunes().Take(100).Select(r => r.ToString())).Trim(' ', '.', '-');
        if (name.Length == 0) name = "Project";
        return "exports/" + string.Join("+", codes) + "/" + name + " - Selected " + (video ? "Video" : "Audio") + " Tracks " + hash + "." + format;
    }
    private async Task SaveSelectedTracksAsync()
    {
        if (!TrackSelectionValid || model.Project is not { } project || owner == null) return;
        var path = TrackPath(project);
        var plan = TrackPlan();
        var replace = false;
        if (File.Exists(project.Resolve(path))) {
            var choice = await owner.ConfirmExportReplacementAsync(new[] { path });
            if (choice == null) return;
            replace = choice == true;
        }
        var jobs = new[] { new Dictionary<string, object> { ["command"] = "export-selected-tracks", ["trackPlan"] = plan, ["replaceExisting"] = replace } };
        Closed += async (_, _) => await owner.RunResultRequestAsync("export-batch", new { jobs });
        Hide();
    }
}
