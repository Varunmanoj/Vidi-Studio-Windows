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
    private const string trackAudioMode = "selectable";
    private string trackCaptionMode = "closed", trackAudioFormat = "m4a";
    private readonly Dictionary<string, string> audioTracks = new(), captionTracks = new(), descriptionTracks = new();
    private readonly HashSet<string> adCaptionTracks = new(), transcriptCaptionTracks = new();
    private bool SelectingTracks => combineTracks && sidebar.SelectedIndex is 0 or 1;
    private bool TrackSelectionValid => owner != null && (sidebar.SelectedIndex == 0 || originalTrack || audioTracks.Count > 0)
        && (sidebar.SelectedIndex != 0 || trackCaptionMode != "open" || transcriptCaptionTracks.Count + adCaptionTracks.Count <= 1)
        && (sidebar.SelectedIndex != 0 || captionTracks.Keys.All(code => !adCaptionTracks.Contains(code) || descriptionTracks.ContainsKey(code)));

    private void BuildTrackChoices()
    {
        var project = model.Project!;
        var video = sidebar.SelectedIndex == 0;
        AddFormat("Format", video ? new[] { "mkv", "mp4", "mov", "m4v" } : new[] { "m4a", "mka" }, video ? videoFormat : trackAudioFormat, value => { if (video) videoFormat = value; else trackAudioFormat = value; UpdatePreview(); });
        page.Children.Add(new TextBlock { Text = "Audio Description: Selectable tracks" });
        var original = new CheckBox { Content = "Include Original Audio", IsChecked = originalTrack };
        original.Checked += (_, _) => { originalTrack = true; UpdatePreview(); };
        original.Unchecked += (_, _) => { originalTrack = false; UpdatePreview(); };
        page.Children.Add(original);
        if (video) {
            var captions = new ComboBox { Header = "Captions", ItemsSource = new[] { "Selectable closed captions", "Burned into video · one language" }, SelectedIndex = trackCaptionMode == "open" ? 1 : 0 };
            captions.SelectionChanged += (_, _) => {
                trackCaptionMode = captions.SelectedIndex == 1 ? "open" : "closed";
                if (trackCaptionMode == "open" && transcriptCaptionTracks.Count + adCaptionTracks.Count > 1) { var first = captionTracks.OrderBy(p => p.Key, StringComparer.Ordinal).First(); captionTracks.Clear(); captionTracks[first.Key] = first.Value;
                    var plain = transcriptCaptionTracks.Contains(first.Key);
                    transcriptCaptionTracks.Clear(); adCaptionTracks.Clear();
                    if (plain) transcriptCaptionTracks.Add(first.Key); else adCaptionTracks.Add(first.Key); }
                BuildPage();
            };
            page.Children.Add(captions);
        }
        foreach (var code in project.SavedResults.Where(r => r.State == "saved" && r.HasContent).Select(r => r.Language).Distinct().OrderBy(v => v, StringComparer.Ordinal)) {
            var generatedSelection = project.SavedResults.FirstOrDefault(r => audioTracks.TryGetValue(code, out var id) && r.Id == id && r.Kind == "descriptions");
            if (generatedSelection != null) descriptionTracks[code] = generatedSelection.Id;
            var panel = new StackPanel { Spacing = 8 };
            panel.Children.Add(new TextBlock { Text = OutputLanguage.Label(code), FontWeight = Microsoft.UI.Text.FontWeights.SemiBold });
            var audio = project.SavedResults.Where(r => r.State == "saved" && r.Language == code && (r.Kind == "adAudio" ? r.HasContent : r.HasExportableText) && r.Kind is "descriptions" or "adAudio").ToList();
            if (audio.Count > 0) {
                var toggle = new CheckBox { Content = "AD Audio", IsChecked = audioTracks.ContainsKey(code) };
                toggle.Checked += (_, _) => { audioTracks[code] = audio[0].Id; BuildPage(); };
                toggle.Unchecked += (_, _) => { audioTracks.Remove(code); BuildPage(); };
                panel.Children.Add(toggle);
                if (audioTracks.ContainsKey(code)) AddTrackResult(panel, "Audio Saved Result", audio, audioTracks, code);
            }
            var transcripts = project.SavedResults.Where(r => r.State == "saved" && r.Language == code && r.Kind == "transcript" && r.HasExportableText).ToList();
            if (video && transcripts.Count > 0) {
                var descriptions = project.SavedResults.Where(r => r.State == "saved" && r.Language == code && r.Kind == "descriptions" && r.HasExportableText).ToList();
                foreach (var ad in new[] { false, true }) {
                    var selected = ad ? adCaptionTracks : transcriptCaptionTracks;
                    var toggle = new CheckBox { Content = ad ? "AD Transcript" : "Transcript", IsChecked = selected.Contains(code), IsEnabled = !ad || descriptions.Count > 0 };
                    toggle.Checked += (_, _) => {
                        if (trackCaptionMode == "open") { captionTracks.Clear(); adCaptionTracks.Clear(); transcriptCaptionTracks.Clear(); }
                        selected.Add(code);
                        if (!captionTracks.ContainsKey(code)) captionTracks[code] = transcripts[0].Id;
                        if (ad && !descriptionTracks.ContainsKey(code)) descriptionTracks[code] = generatedSelection?.Id ?? descriptions[0].Id;
                        BuildPage();
                    };
                    toggle.Unchecked += (_, _) => {
                        selected.Remove(code);
                        if (!adCaptionTracks.Contains(code) && !transcriptCaptionTracks.Contains(code)) captionTracks.Remove(code);
                        BuildPage();
                    };
                    panel.Children.Add(toggle);
                }
                if (captionTracks.ContainsKey(code)) {
                    AddTrackResult(panel, "Transcript Saved Result", transcripts, captionTracks, code);
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
            ["captions"] = video ? CaptionRows() : Array.Empty<Dictionary<string,string>>() };
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
        if (video) items.AddRange(CaptionRows().Select(row => "s:" + row["transcriptID"] + ":" + row["descriptionsID"]));
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
    private Dictionary<string, string>[] CaptionRows() => captionTracks.OrderBy(p => p.Key, StringComparer.Ordinal).SelectMany(p => {
        var rows = new List<Dictionary<string, string>>();
        if (transcriptCaptionTracks.Contains(p.Key)) rows.Add(new() { ["language"] = p.Key, ["transcriptID"] = p.Value, ["descriptionsID"] = "" });
        if (adCaptionTracks.Contains(p.Key)) rows.Add(new() { ["language"] = p.Key, ["transcriptID"] = p.Value, ["descriptionsID"] = descriptionTracks.GetValueOrDefault(p.Key, "") });
        return rows;
    }).ToArray();

    private async Task SaveSelectedTracksAsync()
    {
        if (!TrackSelectionValid || model.Project is not { } project || owner == null) return;
        var jobs = new List<Dictionary<string, object>> { new() { ["command"] = "export-selected-tracks", ["trackPlan"] = TrackPlan(), ["replaceExisting"] = false } };
        var paths = new List<string> { TrackPath(project) };
        await SubmitJobsAsync(project, jobs, paths);
    }

    private static void SetExportName(StudioProject target, StudioProject project, string code, Dictionary<string, string> ids)
    {
        var suffix = string.Join(".", ids.Values.Select(v => v[..Math.Min(8, v.Length)]).OrderBy(v => v, StringComparer.Ordinal));
        target.Name = string.Concat(project.Name.EnumerateRunes().Take(40).Select(r => r.ToString())) + " [" + OutputLanguage.Label(code) + (suffix.Length == 0 ? "" : " " + suffix) + "]";
    }
    private async Task SubmitJobsAsync(StudioProject project, List<Dictionary<string, object>> jobs, List<string> paths)
    {
        if (owner == null) return;
        var collisions = paths.Select((path, index) => (Path: path, Index: index)).Where(row => File.Exists(project.Resolve(row.Path)) || Directory.Exists(project.Resolve(row.Path)) || project.Exports.Any(old =>
            Path.GetFileName(old) == Path.GetFileName(row.Path) && (File.Exists(project.Resolve(old)) || Directory.Exists(project.Resolve(old))))).ToList();
        if (collisions.Count > 0) {
            awaitingReplacementConfirmation = true; UpdatePreview();
            bool? choice;
            try { choice = await owner.ConfirmExportReplacementAsync(collisions.Select(c => c.Path).ToArray()); }
            finally { awaitingReplacementConfirmation = false; UpdatePreview(); }
            if (choice == null) return;
            if (choice == true) foreach (var collision in collisions) jobs[collision.Index]["replaceExisting"] = true;
        }
        Closed += async (_, _) => await owner.RunResultRequestAsync("export-batch", new { jobs });
        Hide();
    }
    internal void VerifyExportCategoriesForSmokeTest()
    {
        foreach (var section in new[] { 0, 1 }) foreach (var combined in new[] { false, true }) {
            combineTracks = combined; sidebar.SelectedIndex = section; BuildPage();
            if (page.Children.OfType<TextBlock>().Any(t => t.Text == "Text Files") || page.Children.OfType<Expander>().Any())
                throw new InvalidOperationException("Standalone text options leaked into a media export category.");
            if (combined && (page.Children.OfType<ComboBox>().Any(c => c.Header?.ToString() == "Audio Description") ||
                TrackPlan()["audioMode"] as string != "selectable"))
                throw new InvalidOperationException("Combined audio must use selectable tracks without a presentation picker.");
            if (!page.Children.OfType<CheckBox>().Any(c => c.Content?.ToString() == "One File with Selected Tracks"))
                throw new InvalidOperationException("Media packaging control is missing.");
        }
        sidebar.SelectedIndex = 2; BuildPage();
        if (SelectingTracks || !page.Children.OfType<TextBlock>().Any(t => t.Text == "Text content"))
            throw new InvalidOperationException("Text export depends on the media packaging switch.");
        sidebar.SelectedIndex = 1; combineTracks = false; BuildPage();
    }
    private static void AttachExportMenus(FrameworkElement element)
    {
        if (element is CheckBox toggle) {
            var menu = new MenuFlyout();
            var item = new ToggleMenuFlyoutItem { Text = toggle.Content?.ToString() ?? "Select", IsChecked = toggle.IsChecked == true, IsEnabled = toggle.IsEnabled };
            item.Click += (_, _) => toggle.IsChecked = item.IsChecked;
            menu.Opening += (_, _) => item.IsChecked = toggle.IsChecked == true;
            menu.Items.Add(item); toggle.ContextFlyout = menu;
        } else if (element is ComboBox picker) {
            var menu = new MenuFlyout();
            for (var i = 0; i < picker.Items.Count; i++) {
                var index = i;
                var value = picker.Items[i];
                var item = new ToggleMenuFlyoutItem { Text = value is ComboBoxItem row ? Microsoft.UI.Xaml.Automation.AutomationProperties.GetName(row) is { Length: > 0 } accessible ? accessible : row.Content?.ToString() ?? "" : value?.ToString() ?? "", IsChecked = picker.SelectedIndex == i, IsEnabled = picker.IsEnabled && (value is not ComboBoxItem option || option.IsEnabled) };
                item.Click += (_, _) => picker.SelectedIndex = index;
                menu.Items.Add(item);
            }
            menu.Opening += (_, _) => { for (var i = 0; i < menu.Items.Count; i++) ((ToggleMenuFlyoutItem)menu.Items[i]).IsChecked = picker.SelectedIndex == i; };
            picker.ContextFlyout = menu;
        } else if (element is Panel panel) foreach (var child in panel.Children.OfType<FrameworkElement>()) AttachExportMenus(child);
        else if (element is Border border && border.Child is FrameworkElement child) AttachExportMenus(child);
        else if (element is Expander expander && expander.Content is FrameworkElement content) AttachExportMenus(content);
    }
}
