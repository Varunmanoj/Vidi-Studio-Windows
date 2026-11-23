using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Vidi_Studio_Windows.Services;

namespace Vidi_Studio_Windows;

public sealed partial class MainWindow
{
    private static void OpenProjectInYouTube(Models.StudioProject project) {
        if (YouTubeService.ProjectWatchURL(project.YoutubeURL) is { } url) System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(url) { UseShellExecute = true });
    }
    private readonly HashSet<string> youtubeInspectorLoaded = new();
    private bool youtubeInspectorRefreshing;
    private void PopulateYouTubeInspector(Models.StudioProject project) {
        if (project.YouTubeMetadata is JsonElement { ValueKind: JsonValueKind.Object } value) {
            string Get(string key) => value.TryGetProperty(key, out var found) && found.ValueKind is not (JsonValueKind.Null or JsonValueKind.Undefined) && found.ToString().Length > 0 ? found.ToString() : "Unavailable";
            void Row(string label, string key) => AddInfo(YouTubeInspectorDetails, label, Get(key));
            AddInfo(YouTubeInspectorDetails, "Information source", "YouTube Data API · saved in this project");
            Row("YouTube title", "title"); Row("Channel", "channelTitle"); Row("Visibility", "visibility");
            Row("Published", "publishedAt"); Row("Views", "viewCount"); Row("Likes", "likeCount"); Row("Comments", "commentCount");
            AddInfo(YouTubeInspectorDetails, "Captions", Get("captionsAvailable") == "True" ? "Available" : "Not reported");
            Row("Language", "language"); Row("Audio language", "audioLanguage"); Row("Category", "categoryID");
            Row("Embedding allowed", "embeddable"); Row("Processing", "uploadStatus"); Row("Live status", "liveStatus");
            Row("Video ID", "videoID"); Row("Channel ID", "channelID");
            if (value.TryGetProperty("fetchedAt", out var at) && at.TryGetDouble(out var seconds)) AddInfo(YouTubeInspectorDetails, "Fetched", DateTimeOffset.FromUnixTimeSeconds((long)seconds).LocalDateTime.ToString("g"));
            foreach (var field in new[] { ("Description", "description"), ("Tags", "tags") }) {
                if (Get(field.Item2) == "Unavailable") continue;
                var text = value.GetProperty(field.Item2);
                var display = text.ValueKind == JsonValueKind.Array ? string.Join(", ", text.EnumerateArray().Select(item => item.ToString())) : text.ToString();
                YouTubeInspectorDetails.Children.Add(new Expander { Header = field.Item1, Content = new TextBlock { Text = display, TextWrapping = TextWrapping.Wrap, IsTextSelectionEnabled = true }, HorizontalAlignment = HorizontalAlignment.Stretch });
            }
        }
        var openYouTube = new Button { Content = "Open in YouTube" }; ToolTipService.SetToolTip(openYouTube, openYouTube.Content); openYouTube.Click += (_, _) => OpenProjectInYouTube(project); YouTubeInspectorDetails.Children.Add(openYouTube);
        var refresh = new Button { Content = "Refresh YouTube Details", IsEnabled = !youtubeInspectorRefreshing && Model.Idle };
        ToolTipService.SetToolTip(refresh, refresh.Content);
        refresh.Click += async (_, _) => await RefreshYouTubeInspectorAsync(); YouTubeInspectorDetails.Children.Add(refresh);
        if (youtubeInspectorRefreshing) {
            var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
            row.Children.Add(new ProgressRing { IsActive = true, Width = 18, Height = 18 }); row.Children.Add(new TextBlock { Text = "Fetching YouTube details…", TextWrapping = TextWrapping.Wrap }); YouTubeInspectorDetails.Children.Add(row);
        }
        var missing = project.YouTubeMetadata is not JsonElement { ValueKind: JsonValueKind.Object };
        if (missing && !youtubeInspectorRefreshing && youtubeInspectorLoaded.Add(project.Folder)) _ = RefreshYouTubeInspectorAsync();
    }
    private async Task RefreshYouTubeInspectorAsync() {
        if (youtubeInspectorRefreshing || Model.Project is not { IsYouTube: true } project) return;
        youtubeInspectorRefreshing = true; UpdateInspector(project);
        var file = Path.Combine(Path.GetTempPath(), "vidistudio-youtube-" + Guid.NewGuid() + ".json");
        try {
            var url = new Uri(project.YoutubeURL ?? "");
            var id = url.Query.TrimStart('?').Split('&').FirstOrDefault(v => v.StartsWith("v="))?[2..] ?? "";
            var video = await YouTubeService.Shared.VideoAsync(id);
            video.RequireAvailable();
            while (Model.Busy && Model.Project?.Folder == project.Folder) await Task.Delay(100);
            if (Model.Project?.Folder != project.Folder) return;
            File.WriteAllText(file, video.Data.ToJsonString());
            await Model.RunAsync("update-youtube-metadata", new[] { "--youtube-metadata", file });
        } catch (Exception ex) { if (Model.Project?.Folder == project.Folder) await ShowErrorMessage("YouTube Details Could Not Be Refreshed", ex.Message); }
        finally {
            File.Delete(file); youtubeInspectorRefreshing = false;
            if (Model.Project is { } currentProject) UpdateInspector(currentProject);
        }
    }
    private async Task ShowYouTubeDetailsAsync()
    {
        if (Model.Project is not { IsYouTube: true } project) return;
        var service = YouTubeService.Shared;
        var panel = new StackPanel { Spacing = 12, MinWidth = 540 };
        var details = new TextBlock { TextWrapping = TextWrapping.Wrap, IsTextSelectionEnabled = true }; panel.Children.Add(details);
        var status = new TextBlock { TextWrapping = TextWrapping.Wrap }; panel.Children.Add(status);
        var refresh = new Button { Content = "Refresh Details" }; panel.Children.Add(refresh);
        var dialog = new ContentDialog { Title = "YouTube Details", Content = new ScrollViewer { Content = panel }, CloseButtonText = "Close", SecondaryButtonText = "Delete Stored YouTube Data…" };
        using var cancellation = new CancellationTokenSource();
        var id = new Uri(project.YoutubeURL ?? "").Query.TrimStart('?').Split('&').FirstOrDefault(v => v.StartsWith("v="))?[2..] ?? "";
        async Task Refresh() {
            try {
                var video = await service.VideoAsync(id, cancellation.Token); details.Text = video.ToString() + "\n" + video.Data["description"] + "\nPublished: " + video.Published + "\nViews: " + (video.Data["viewCount"]?.ToString() ?? "Unavailable") + " · Likes: " + (video.Data["likeCount"]?.ToString() ?? "Unavailable") + " · Comments: " + (video.Data["commentCount"]?.ToString() ?? "Unavailable") + "\nLanguage: " + (video.Data["language"]?.ToString() ?? "Unavailable") + " · Audio language: " + (video.Data["audioLanguage"]?.ToString() ?? "Unavailable") + "\nCategory: " + (video.Data["categoryID"]?.ToString() ?? "Unavailable") + " · Embedding allowed: " + (video.Data["embeddable"]?.ToString() ?? "Unavailable") + "\nProcessing: " + (video.Data["uploadStatus"]?.ToString() ?? "Unavailable") + " · Live status: " + (video.Data["liveStatus"]?.ToString() ?? "Unavailable") + "\nTags: " + string.Join(", ", video.Data["tags"]?.AsArray().Select(t => t?.ToString()) ?? Array.Empty<string>());
                video.RequireAvailable(); var file = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".json");
                try { File.WriteAllText(file, video.Data.ToJsonString()); await Model.RunAsync("update-youtube-metadata", new[] { "--youtube-metadata", file }); }
                finally { File.Delete(file); }
            } catch (Exception ex) { status.Text = ex.Message; }
        }
        refresh.Click += async (_, _) => await Refresh();
        dialog.Opened += async (_, _) => await Refresh();
        var outcome = await ShowDialogAsync(dialog, 820, 720);
        if (outcome == ContentDialogResult.Secondary) {
            var confirmDelete = new ContentDialog { Title = "Delete stored YouTube data?", Content = "Removes fetched details, downloaded captions, imported YouTube caption rows (including edits), and transcript history. Other manually created rows remain. Nothing is deleted on YouTube.", PrimaryButtonText = "Delete Stored YouTube Data", CloseButtonText = "Cancel", DefaultButton = ContentDialogButton.Close };
            if (await ShowDialogAsync(confirmDelete, 650, 400) == ContentDialogResult.Primary) await Model.RunAsync("clear-youtube-data");
            return;
        }
    }
}
