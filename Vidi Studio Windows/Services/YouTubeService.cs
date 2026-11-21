using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using System.Xml;

namespace Vidi_Studio_Windows.Services;

public sealed record YouTubeVideo(JsonObject Data)
{
    public string Id => Data["videoID"]?.GetValue<string>() ?? "";
    public string Title => Data["title"]?.GetValue<string>() ?? "Unavailable video";
    public string Visibility => Data["visibility"]?.GetValue<string>() ?? "unknown";
    public double Duration => Data["duration"]?.GetValue<double>() ?? 0;
    public string Url => "https://www.youtube.com/watch?v=" + Id;
    public string Published => Data["publishedAt"]?.GetValue<string>() ?? "";
    public string Channel => Data["channelTitle"]?.ToString() ?? "";
    public string DurationAnnouncement => "Duration: " + Models.NarrationTimeFormat.SpokenProjectDuration(Duration);
    public string ListSubtitleAnnouncement => Visibility + ", " + DurationAnnouncement + ", " + Channel;
    public string DurationLabel => Models.NarrationTimeFormat.ProjectDuration(Duration);
    public string PublishedLabel => DateTimeOffset.TryParse(Published, out var date) ? date.LocalDateTime.ToString("d") : Published;
    public string ViewsLabel => Data["viewCount"]?.ToString() ?? "Unavailable";
    public string LikesLabel => Data["likeCount"]?.ToString() ?? "Unavailable";
    public string CommentsLabel => Data["commentCount"]?.ToString() ?? "Unavailable";
    public string CaptionsLabel => Data["captionsAvailable"]?.GetValue<bool>() == true ? "Available" : "Not reported";
    public string ListSubtitle => Visibility + " · " + DurationLabel + " · " + Channel;
    public string StatisticsLabel => "Published: " + PublishedLabel + " · Views: " + ViewsLabel + " · Likes: " + LikesLabel + " · Comments: " + CommentsLabel;
    public string TableAccessibleLabel => "Title: " + Title + ", Channel: " + Channel + ", Visibility: " + Visibility + ", " + DurationAnnouncement + ", Published: " + PublishedLabel + ", Views: " + ViewsLabel + ", Likes: " + LikesLabel + ", Comments: " + CommentsLabel + ", Captions: " + CaptionsLabel;
    public string Thumbnail => Data["thumbnailURL"]?.GetValue<string>() ?? "";
    public override string ToString() => Title + " · " + Visibility + " · " + DurationLabel + " · " + Data["channelTitle"];
    public void RequireAvailable()
    {
        if (Visibility == "private") throw new InvalidOperationException("Private Video Cannot Be Opened. Choose a public or unlisted video, or import your original local video.");
        if (Duration <= 0 || Data["liveStatus"]?.ToString() is "live" or "upcoming" || Data["uploadStatus"]?.ToString() is "uploaded" or "failed" or "rejected" or "deleted")
            throw new InvalidOperationException("This video is live, scheduled, or has not finished processing on YouTube. Try again when a completed video is available.");
    }
    public static YouTubeVideo Parse(JsonNode item)
    {
        var s = item["snippet"]; var c = item["contentDetails"]; var status = item["status"]; var stats = item["statistics"];
        double duration = 0; try { duration = XmlConvert.ToTimeSpan(c?["duration"]?.ToString() ?? "PT0S").TotalSeconds; } catch (FormatException) { }
        return new(new JsonObject {
            ["videoID"] = item["id"]?.ToString(), ["title"] = s?["title"]?.ToString(), ["description"] = s?["description"]?.ToString() ?? "",
            ["channelID"] = s?["channelId"]?.ToString(), ["channelTitle"] = s?["channelTitle"]?.ToString(), ["duration"] = duration,
            ["visibility"] = status?["privacyStatus"]?.ToString() ?? "unknown", ["publishedAt"] = s?["publishedAt"]?.ToString() ?? "",
            ["thumbnailURL"] = (s?["thumbnails"]?["medium"] ?? s?["thumbnails"]?["default"])?["url"]?.ToString() ?? "",
            ["captionsAvailable"] = c?["caption"]?.ToString() == "true", ["tags"] = s?["tags"]?.DeepClone() ?? new JsonArray(),
            ["language"] = s?["defaultLanguage"]?.ToString(), ["audioLanguage"] = s?["defaultAudioLanguage"]?.ToString(),
            ["categoryID"] = s?["categoryId"]?.ToString(), ["embeddable"] = status?["embeddable"]?.DeepClone(),
            ["uploadStatus"] = status?["uploadStatus"]?.ToString(), ["liveStatus"] = s?["liveBroadcastContent"]?.ToString(),
            ["viewCount"] = stats?["viewCount"]?.ToString(), ["likeCount"] = stats?["likeCount"]?.ToString(), ["commentCount"] = stats?["commentCount"]?.ToString(),
            ["fetchedAt"] = DateTimeOffset.UtcNow.ToUnixTimeSeconds()
        });
    }
}
public sealed class YouTubeService
{
    public static string? ProjectWatchURL(string? source) {
        if (!Uri.TryCreate(source, UriKind.Absolute, out var uri)) return null;
        var host = uri.Host.ToLowerInvariant(); string? id = null;
        if (host == "youtu.be") id = uri.AbsolutePath.Trim('/').Split('/')[0];
        else if (host is "youtube.com" or "www.youtube.com" or "m.youtube.com" or "youtube-nocookie.com" or "www.youtube-nocookie.com") {
            id = uri.Query.TrimStart('?').Split('&').FirstOrDefault(item => item.StartsWith("v="))?[2..];
            if (id == null) { var parts = uri.AbsolutePath.Trim('/').Split('/'); if (parts.Length == 2 && parts[0] is "shorts" or "embed" or "live") id = parts[1]; }
        }
        return id is { Length: 11 } && id.All(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_') ? "https://www.youtube.com/watch?v=" + id : null;
    }
    public static YouTubeService Shared { get; } = new();
    public static void ExpireCaches(string library) {
        var executable = Path.Combine(AppContext.BaseDirectory, "engine-bin", "vidistudio-engine.exe");
        if (!File.Exists(executable)) return;
        var start = new ProcessStartInfo(executable) { UseShellExecute = false, CreateNoWindow = true };
        start.ArgumentList.Add("expire-youtube-cache"); start.ArgumentList.Add("--library"); start.ArgumentList.Add(library);
        try { Process.Start(start)?.Dispose(); } catch (System.ComponentModel.Win32Exception) { }
    }

    private readonly HttpClient http = new(new HttpClientHandler { AllowAutoRedirect = false }) { Timeout = TimeSpan.FromSeconds(45) };
    private readonly string apiKey;
    public YouTubeService()
    {
        var configFile = Path.Combine(AppContext.BaseDirectory, "youtube-config.json");
        apiKey = File.Exists(configFile) ? JsonNode.Parse(File.ReadAllText(configFile))?["apiKey"]?.ToString() ?? "" : "";
    }

    private async Task<JsonObject> GetAsync(string url, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        using var response = await http.SendAsync(request, ct);
        if (!response.IsSuccessStatusCode) {
            if (response.StatusCode == HttpStatusCode.NotFound) throw new InvalidOperationException("YouTube information was not found.");
            var body = JsonNode.Parse(await response.Content.ReadAsStringAsync(ct)); var reason = body?["error"]?["errors"]?[0]?["reason"]?.ToString();
            if (reason is "quotaExceeded" or "dailyLimitExceeded") throw new InvalidOperationException("VidiStudio’s YouTube allowance has been reached. Try again later.");
            throw new InvalidOperationException("YouTube did not allow this request. Check your connection and try an available public or unlisted video.");
        }
        return JsonNode.Parse(await response.Content.ReadAsStringAsync(ct))!.AsObject();
    }
    public async Task<YouTubeVideo> VideoAsync(string id, CancellationToken ct = default)
    {
        if (apiKey.Length == 0) throw new InvalidOperationException("YouTube video details have not been configured for this build. Contact the VidiStudio publisher.");
        var url = "https://www.googleapis.com/youtube/v3/videos?part=snippet,contentDetails,status,statistics"
            + "&id=" + Uri.EscapeDataString(id) + "&key=" + Uri.EscapeDataString(apiKey);
        var result = await GetAsync(url, ct);
        if (result["items"] is not JsonArray items || items.Count == 0)
            throw new InvalidOperationException("This video is unavailable. Use an available public or unlisted video.");
        return YouTubeVideo.Parse(items[0]!);
    }
}
