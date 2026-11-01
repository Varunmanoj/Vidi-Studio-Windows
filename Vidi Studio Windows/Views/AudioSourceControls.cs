using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using System;
using System.IO;
using System.Threading.Tasks;
using Vidi_Studio_Windows.Models;
using Vidi_Studio_Windows.ViewModels;

namespace Vidi_Studio_Windows.Views;

public sealed class AudioSourceControls : UserControl
{
    private readonly MainWindow owner;
    private readonly StudioViewModel model;
    private readonly ContentDialog? host;
    private readonly bool exportHost;
    private readonly StackPanel panel = new() { Spacing = 8 };

    public AudioSourceControls(MainWindow owner, StudioViewModel model, ContentDialog? host = null, bool exportHost = true)
    {
        this.owner = owner; this.model = model; this.host = host;
        this.exportHost = exportHost;
        Content = panel;
        Refresh();
    }

    private void Text(string text) => panel.Children.Add(new TextBlock { Text = text, TextWrapping = TextWrapping.Wrap });

    private void Refresh()
    {
        panel.Children.Clear();
        if (model.Project is not { IsYouTube: true } project) return;
        Text("Audio sources");
        Text(project.OriginalAudio is { } asset ? "Imported audio: " + asset.SourceName : "No original audio file imported");
        var sources = project.AudioSources ?? new();
        Picker("Export source", "--soundtrack-source", sources.Export, "Extract from YouTube when exporting", project);
        Picker("Playback audio", "--playback-audio-source", sources.Playback, "YouTube audio", project);
        Picker("Transcription source", "--transcription-audio-source", sources.Transcription, "YouTube video", project);
        Text("Playing: " + (sources.Playback == "imported" ? "Imported audio: " + project.OriginalAudio?.SourceName : "YouTube audio"));
        Text("AD audio available: " + (project.ImportedAudioDescription is { } ad ? "Imported AD audio: " + ad.SourceName : "Generated narration"));
        if (project.Transcript.Count > 0 && project.TranscriptAudioSource is { } actual) Text("Existing transcript audio: " + (actual.Origin == "youtube" ? "YouTube video" : actual.Label));
        if (project.OriginalAudio is { } original)
        {
            if (!File.Exists(project.Resolve(original.Path))) Text("Imported original audio is missing. Choose Audio File to repair it.");
            else if (original.NeedsConfirmation(project.Duration))
            {
                Text("Audio timing needs confirmation");
                Action("Review Audio Timing…", async () => { await owner.ImportOriginalAudioAsync(null); });
            }
            else if (project.Duration <= 0) Text("Duration could not be verified; imported with your confirmation.");
            else if (Math.Abs(original.Duration - project.Duration) > .100000001) Text("Different audio duration accepted for this video.");
            else Text("Audio duration matches the video.");
        }
        Action("Choose Audio File…", owner.ChooseOriginalAudioAsync);
        if (project.OriginalAudio != null) Action("Remove Selection", async () => { await model.RunAsync("remove-original-audio"); });
    }

    private void Picker(string title, string argument, string current, string youtube, StudioProject project)
    {
        var box = new ComboBox { Header = title, HorizontalAlignment = HorizontalAlignment.Stretch };
        box.Items.Add(youtube);
        if (project.OriginalAudio is { } asset) box.Items.Add("Imported audio: " + asset.SourceName);
        box.SelectedIndex = current == "imported" && box.Items.Count > 1 ? 1 : 0;
        AutomationProperties.SetName(box, title);
        box.SelectionChanged += async (_, _) =>
        {
            if (model.Busy) return;
            await model.RunAsync("set-audio-sources", new[] { argument, box.SelectedIndex == 1 ? "imported" : "youtube" });
            Refresh();
        };
        panel.Children.Add(box);
    }

    private void Action(string title, Func<Task> action)
    {
        var button = new Button { Content = title };
        AutomationProperties.SetName(button, title);
        button.Click += async (_, _) =>
        {
            if (model.Busy) return;
            if (host != null)
            {
                host.Closed += async (_, _) => { await action(); if (exportHost) await owner.ShowAudioExportAsync(); else await owner.ShowAudioSourcesAsync(); };
                host.Hide();
            }
            else { await action(); Refresh(); }
        };
        panel.Children.Add(button);
    }
}
