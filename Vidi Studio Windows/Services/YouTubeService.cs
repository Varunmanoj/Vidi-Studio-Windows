using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using System.Xml;

namespace Vidi_Studio_Windows.Services;

public sealed record YouTubeAccount(string Id, string Subject, string Name, string Email, string Photo, string ChannelId, string ChannelName, string ChannelPhoto, string Uploads)
{
    public override string ToString() => Name + " · " + ChannelName;
}
public sealed record YouTubeVideo(JsonObject Data)
{
    public string Id => Data["videoID"]?.GetValue<string>() ?? "";
    public string Title => Data["title"]?.GetValue<string>() ?? "Unavailable video";
    public string Visibility => Data["visibility"]?.GetValue<string>() ?? "unknown";
    public double Duration => Data["duration"]?.GetValue<double>() ?? 0;
    public string Url => "https://www.youtube.com/watch?v=" + Id;
    public string Published => Data["publishedAt"]?.GetValue<string>() ?? "";
    public string Channel => Data["channelTitle"]?.ToString() ?? "";
    public string DurationLabel => Models.NarrationTimeFormat.Timestamp(Duration);
    public string PublishedLabel => DateTimeOffset.TryParse(Published, out var date) ? date.LocalDateTime.ToString("d") : Published;
    public string ViewsLabel => Data["viewCount"]?.ToString() ?? "Unavailable";
    public string LikesLabel => Data["likeCount"]?.ToString() ?? "Unavailable";
    public string CommentsLabel => Data["commentCount"]?.ToString() ?? "Unavailable";
    public string CaptionsLabel => Data["captionsAvailable"]?.GetValue<bool>() == true ? "Available" : "Not reported";
    public string ListSubtitle => Visibility + " · " + DurationLabel + " · " + Channel;
    public string StatisticsLabel => "Published: " + PublishedLabel + " · Views: " + ViewsLabel + " · Likes: " + LikesLabel + " · Comments: " + CommentsLabel;
    public string TableAccessibleLabel => "Title: " + Title + ", Channel: " + Channel + ", Visibility: " + Visibility + ", Duration: " + DurationLabel + ", Published: " + PublishedLabel + ", Views: " + ViewsLabel + ", Likes: " + LikesLabel + ", Comments: " + CommentsLabel + ", Captions: " + CaptionsLabel;
    public string Thumbnail => Data["thumbnailURL"]?.GetValue<string>() ?? "";
    public override string ToString() => Title + " · " + Visibility + " · " + DurationLabel + " · " + Data["channelTitle"];
    public void RequireAvailable()
    {
        if (Visibility == "private") throw new InvalidOperationException("Private Video Cannot Be Opened. Signing in to Google does not give Gemini access to this private video. Choose a public or unlisted video, or import your original local video.");
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
public sealed record YouTubePlaylist(string Id, string Title, string Description, string Visibility, string Count, string Published)
{
    public override string ToString() => Title + " · " + Visibility + " · " + Count + " videos";
}
public sealed record YouTubeCaption(string Id, string Language, string Name, string Kind, string Status)
{
    public override string ToString() => Language + " · " + (Name.Length > 0 ? Name : Kind == "ASR" ? "Automatic" : "Standard") + " · " + Status;
}

internal sealed class YouTubeNotFoundException : InvalidOperationException { }
internal sealed class YouTubeAccessExpiredException : InvalidOperationException { public YouTubeAccessExpiredException() : base("Google access was revoked or has expired. Sign in again from YouTube Accounts.") { } }

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
    private readonly JsonObject config;
    private readonly string accountFile;
    private readonly Dictionary<string, (string Token, DateTimeOffset Expires)> tokens = new();
    private readonly SemaphoreSlim refreshGate = new(1, 1);
    public List<YouTubeAccount> Accounts { get; private set; } = new();
    public string SelectedId { get; set; } = "";
    public YouTubeAccount? Selected => Accounts.FirstOrDefault(a => a.Id == SelectedId);
    private CredentialSlot Credential(string id) => new("VidiStudio/YouTube/" + id, "YouTube refresh token");
    public YouTubeService()
    {
        var configFile = Path.Combine(AppContext.BaseDirectory, "youtube-config.json");
        config = File.Exists(configFile) ? JsonNode.Parse(File.ReadAllText(configFile))!.AsObject() : new();
        accountFile = Path.Combine(SettingsStore.DirectoryPath, "youtube-accounts.json");
        if (File.Exists(accountFile)) { try { Accounts = JsonSerializer.Deserialize<List<YouTubeAccount>>(File.ReadAllText(accountFile)) ?? new(); } catch (JsonException) { } }
        SelectedId = Accounts.FirstOrDefault()?.Id ?? "";
    }
    private string ClientId => config["clientID"]?.ToString() ?? "";
    private string ClientSecret => config["clientSecret"]?.ToString() ?? "";
    private void Save() { Directory.CreateDirectory(SettingsStore.DirectoryPath); File.WriteAllText(accountFile, JsonSerializer.Serialize(Accounts)); }
    private static string Random() => Convert.ToHexString(RandomNumberGenerator.GetBytes(32)).ToLowerInvariant();
    private async Task<JsonObject> TokenAsync(Dictionary<string, string> fields, CancellationToken ct)
    {
        using var response = await http.PostAsync("https://oauth2.googleapis.com/token", new FormUrlEncodedContent(fields), ct);
        if (!response.IsSuccessStatusCode) {
            var error = JsonNode.Parse(await response.Content.ReadAsStringAsync(ct));
            if (error?["error"]?.ToString() == "invalid_grant") throw new YouTubeAccessExpiredException();
            throw new InvalidOperationException("Google access has expired or was denied. Sign in again from YouTube Accounts.");
        }
        return JsonNode.Parse(await response.Content.ReadAsStringAsync(ct))!.AsObject();
    }
    public async Task SignInAsync(CancellationToken ct)
    {
        if (ClientId.Length == 0) throw new InvalidOperationException("YouTube sign-in has not been configured for this build. Contact the VidiStudio publisher.");
        var verifier = Random(); var state = Random();
        var challenge = Convert.ToBase64String(SHA256.HashData(Encoding.ASCII.GetBytes(verifier))).TrimEnd('=').Replace('+', '-').Replace('/', '_');
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct); timeout.CancelAfter(TimeSpan.FromMinutes(3));
        var listener = new TcpListener(IPAddress.Loopback, 0); listener.Start();
        string code; var redirect = "http://127.0.0.1:" + ((IPEndPoint)listener.LocalEndpoint).Port;
        try {
            var query = new Dictionary<string, string> { ["client_id"] = ClientId, ["redirect_uri"] = redirect, ["response_type"] = "code", ["scope"] = "openid email profile https://www.googleapis.com/auth/youtube.readonly https://www.googleapis.com/auth/youtube.force-ssl", ["state"] = state, ["code_challenge"] = challenge, ["code_challenge_method"] = "S256", ["access_type"] = "offline", ["prompt"] = "consent select_account" };
            Process.Start(new ProcessStartInfo("https://accounts.google.com/o/oauth2/v2/auth?" + string.Join("&", query.Select(v => Uri.EscapeDataString(v.Key) + "=" + Uri.EscapeDataString(v.Value)))) { UseShellExecute = true });
            while (true) {
                using var socket = await listener.AcceptTcpClientAsync(timeout.Token); using var stream = socket.GetStream();
                using var reader = new StreamReader(stream, Encoding.ASCII, false, 1024, true);
                var line = await reader.ReadLineAsync(timeout.Token) ?? "";
                if (line.Length > 8192 || !line.StartsWith("GET /")) continue;
                var path = line.Split(' ').ElementAtOrDefault(1) ?? "/";
                var values = new Uri("http://127.0.0.1" + path).Query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries).Select(v => v.Split('=', 2)).GroupBy(v => Uri.UnescapeDataString(v[0])).ToDictionary(g => g.Key, g => Uri.UnescapeDataString(g.First().ElementAtOrDefault(1) ?? ""));
                if (values.GetValueOrDefault("state") != state) continue;
                var response = Encoding.UTF8.GetBytes("HTTP/1.1 200 OK\r\nContent-Type: text/html; charset=utf-8\r\nConnection: close\r\n\r\n<!doctype html><title>VidiStudio</title><p>You may close this tab and return to VidiStudio.</p>");
                await stream.WriteAsync(response, timeout.Token);
                if (!values.TryGetValue("code", out var returnedCode)) throw new InvalidOperationException("Google sign-in was cancelled or permission was not granted.");
                code = returnedCode; break;
            }
        } finally { listener.Stop(); }
        var result = await TokenAsync(new() { ["client_id"] = ClientId, ["client_secret"] = ClientSecret, ["code"] = code, ["redirect_uri"] = redirect, ["code_verifier"] = verifier, ["grant_type"] = "authorization_code" }, ct);
        var token = result["access_token"]?.ToString(); var refresh = result["refresh_token"]?.ToString();
        if (token == null || refresh == null) throw new InvalidOperationException("Google did not grant saved access. Sign in again and allow access.");
        var profile = await GetAsync("https://openidconnect.googleapis.com/v1/userinfo", token, ct);
        var channels = await ApiAsync("channels", new() { ["part"] = "snippet,contentDetails", ["mine"] = "true" }, token, ct);
        var subject = profile["sub"]?.ToString() ?? throw new InvalidOperationException("Google profile details were unavailable.");
        if (channels["items"] is not JsonArray items || items.Count == 0) throw new InvalidOperationException("No YouTube channel is available to this account through the API. Sign in with the channel owner.");
        foreach (var channel in items.Where(i => i != null)) {
            var id = channel!["id"]!.ToString(); var s = channel["snippet"]; var connectionId = subject + ":" + id;
            var account = new YouTubeAccount(connectionId, subject, profile["name"]?.ToString() ?? "Google account", profile["email"]?.ToString() ?? "", profile["picture"]?.ToString() ?? "", id, s?["title"]?.ToString() ?? "YouTube channel", s?["thumbnails"]?["default"]?["url"]?.ToString() ?? "", channel["contentDetails"]?["relatedPlaylists"]?["uploads"]?.ToString() ?? "");
            if (!Credential(connectionId).Write(refresh)) throw new InvalidOperationException("YouTube access could not be saved securely. Try again.");
            Accounts.RemoveAll(a => a.Id == connectionId); Accounts.Add(account); SelectedId = connectionId; tokens[connectionId] = (token, DateTimeOffset.UtcNow.AddMinutes(55));
        }
        Save();
    }
    private async Task<string> BearerAsync(string id, CancellationToken ct)
    {
        await refreshGate.WaitAsync(ct);
        try {
            if (tokens.TryGetValue(id, out var saved) && saved.Expires > DateTimeOffset.UtcNow) return saved.Token;
            var refresh = Credential(id).Read() ?? throw new InvalidOperationException("Sign in to this YouTube account again.");
            var value = await TokenAsync(new() { ["client_id"] = ClientId, ["client_secret"] = ClientSecret, ["refresh_token"] = refresh, ["grant_type"] = "refresh_token" }, ct);
            var token = value["access_token"]?.ToString() ?? throw new InvalidOperationException("Sign in to this YouTube account again.");
            if (!Accounts.Any(a => a.Id == id)) throw new OperationCanceledException();
            tokens[id] = (token, DateTimeOffset.UtcNow.AddSeconds(Math.Max(0, (value["expires_in"]?.GetValue<double>() ?? 3600) - 60))); return token;
        } catch (YouTubeAccessExpiredException) {
            var subject = Accounts.FirstOrDefault(a => a.Id == id)?.Subject;
            foreach (var account in Accounts.Where(a => a.Subject == subject).ToArray()) SignOut(account.Id);
            throw;
        } finally { refreshGate.Release(); }
    }
    public void SignOut(string id) { Credential(id).Delete(); tokens.Remove(id); Accounts.RemoveAll(a => a.Id == id); if (SelectedId == id) SelectedId = Accounts.FirstOrDefault()?.Id ?? ""; Save(); }
    public async Task DisconnectAsync(YouTubeAccount account, CancellationToken ct)
    {
        var refresh = Credential(account.Id).Read();
        if (refresh != null) {
            using var result = await http.PostAsync("https://oauth2.googleapis.com/revoke", new FormUrlEncodedContent(new Dictionary<string, string> { ["token"] = refresh }), ct);
            if (!result.IsSuccessStatusCode && result.StatusCode != HttpStatusCode.BadRequest) throw new InvalidOperationException("Google access could not be disconnected. Try again online.");
        }
        foreach (var value in Accounts.Where(a => a.Subject == account.Subject).ToArray()) SignOut(value.Id);
    }
    private async Task<JsonObject> GetAsync(string url, string? token, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        if (token != null) request.Headers.Authorization = new("Bearer", token);
        using var response = await http.SendAsync(request, ct);
        if (!response.IsSuccessStatusCode) {
            if (response.StatusCode == HttpStatusCode.NotFound) throw new YouTubeNotFoundException();
            var body = JsonNode.Parse(await response.Content.ReadAsStringAsync(ct)); var reason = body?["error"]?["errors"]?[0]?["reason"]?.ToString();
            if (reason is "quotaExceeded" or "dailyLimitExceeded") throw new InvalidOperationException("VidiStudio’s YouTube allowance has been reached. Try again later.");
            if (response.StatusCode == HttpStatusCode.Unauthorized) throw new InvalidOperationException("Google access has expired. Sign in again from YouTube Accounts.");
            throw new InvalidOperationException("YouTube did not allow this request. Check the selected account’s video permissions and your connection.");
        }
        return JsonNode.Parse(await response.Content.ReadAsStringAsync(ct))!.AsObject();
    }
    private async Task<JsonObject> ApiAsync(string path, Dictionary<string, string> query, string? token, CancellationToken ct)
    {
        if (token == null) {
            var key = config["apiKey"]?.ToString() ?? "";
            if (key.Length == 0) throw new InvalidOperationException("YouTube video details have not been configured for this build. Contact the VidiStudio publisher.");
            query["key"] = key;
        }
        return await GetAsync("https://www.googleapis.com/youtube/v3/" + path + "?" + string.Join("&", query.Select(v => Uri.EscapeDataString(v.Key) + "=" + Uri.EscapeDataString(v.Value))), token, ct);
    }
    public async Task<YouTubeVideo> VideoAsync(string id, CancellationToken ct = default, string? accountId = null)
    {
        accountId ??= Selected?.Id;
        var result = await ApiAsync("videos", new() { ["part"] = "snippet,contentDetails,status,statistics", ["id"] = id }, accountId == null ? null : await BearerAsync(accountId, ct), ct);
        if (result["items"] is not JsonArray items || items.Count == 0) throw new InvalidOperationException("This video is unavailable. It may be private, deleted, or inaccessible to this account.");
        return YouTubeVideo.Parse(items[0]!);
    }
    public async Task<(List<YouTubeVideo>, string)> VideosAsync(string playlist, string page, string accountId, CancellationToken ct)
    {
        var token = await BearerAsync(accountId, ct);
        var result = await ApiAsync("playlistItems", new() { ["part"] = "contentDetails", ["playlistId"] = playlist, ["maxResults"] = "50", ["pageToken"] = page }, token, ct);
        var ids = (result["items"]?.AsArray() ?? new()).Select(i => i?["contentDetails"]?["videoId"]?.ToString()).Where(i => i != null).ToArray();
        if (ids.Length == 0) return (new(), result["nextPageToken"]?.ToString() ?? "");
        var details = await ApiAsync("videos", new() { ["part"] = "snippet,contentDetails,status,statistics", ["id"] = string.Join(",", ids) }, token, ct);
        var videos = (details["items"]?.AsArray() ?? new()).Where(i => i != null).Select(i => YouTubeVideo.Parse(i!)).ToList();
        return (ids.Select(id => videos.FirstOrDefault(v => v.Id == id)).Where(v => v != null).Select(v => v!).ToList(), result["nextPageToken"]?.ToString() ?? "");
    }
    public async Task<(List<YouTubePlaylist>, string)> PlaylistsAsync(string page, string accountId, CancellationToken ct)
    {
        var result = await ApiAsync("playlists", new() { ["part"] = "snippet,contentDetails,status", ["mine"] = "true", ["maxResults"] = "50", ["pageToken"] = page }, await BearerAsync(accountId, ct), ct);
        return ((result["items"]?.AsArray() ?? new()).Where(i => i != null).Select(i => new YouTubePlaylist(i!["id"]!.ToString(), i["snippet"]?["title"]?.ToString() ?? "Playlist", i["snippet"]?["description"]?.ToString() ?? "", i["status"]?["privacyStatus"]?.ToString() ?? "unknown", i["contentDetails"]?["itemCount"]?.ToString() ?? "Unknown", i["snippet"]?["publishedAt"]?.ToString() ?? "")).ToList(), result["nextPageToken"]?.ToString() ?? "");
    }
    public static JsonObject PlaylistData(YouTubePlaylist list) => new() {
        ["id"] = list.Id, ["title"] = list.Title, ["description"] = list.Description,
        ["visibility"] = list.Visibility, ["count"] = int.TryParse(list.Count, out var count) ? JsonValue.Create(count) : null, ["publishedAt"] = list.Published
    };
    public async Task<YouTubeVideo> VideoWithPlaylistsAsync(string id, CancellationToken ct = default) {
        var account = Selected;
        var video = await VideoAsync(id, ct, account?.Id); video.RequireAvailable();
        var matches = new JsonArray(); video.Data["playlists"] = matches;
        if (account == null) { video.Data["playlistLookupStatus"] = "Sign in and refresh to load playlists for your channel."; return video; }
        video.Data["playlistChannelTitle"] = account.ChannelName;
        var page = "";
        try {
            do {
                ct.ThrowIfCancellationRequested();
                if (SelectedId != account.Id) throw new OperationCanceledException();
                var (lists, next) = await PlaylistsAsync(page, account.Id, ct);
                foreach (var list in lists) {
                    try {
                        var found = await ApiAsync("playlistItems", new() { ["part"] = "id", ["playlistId"] = list.Id, ["videoId"] = id, ["maxResults"] = "1" }, await BearerAsync(account.Id, ct), ct);
                        if (found["items"] is JsonArray { Count: > 0 }) matches.Add(PlaylistData(list));
                    } catch (YouTubeNotFoundException) { }
                }
                page = next;
            } while (page.Length > 0);
            video.Data["playlistLookupStatus"] = "Playlists returned for the connected channel.";
        } catch (OperationCanceledException) { throw; }
        catch (Exception) { video.Data["playlistLookupStatus"] = "Some playlist information could not be loaded. Use Refresh YouTube Details to try again."; }
        if (SelectedId != account.Id) throw new OperationCanceledException();
        return video;
    }
    public async Task<List<YouTubeCaption>> CaptionsAsync(string id, string accountId, CancellationToken ct)
    {
        var result = await ApiAsync("captions", new() { ["part"] = "snippet", ["videoId"] = id }, await BearerAsync(accountId, ct), ct);
        return (result["items"]?.AsArray() ?? new()).Where(i => i != null).Select(i => new YouTubeCaption(i!["id"]!.ToString(), i["snippet"]?["language"]?.ToString() ?? "Unknown", i["snippet"]?["name"]?.ToString() ?? "", i["snippet"]?["trackKind"]?.ToString() ?? "standard", i["snippet"]?["status"]?.ToString() ?? "unknown")).ToList();
    }
    public async Task<byte[]> DownloadAsync(YouTubeCaption caption, string accountId, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "https://www.googleapis.com/youtube/v3/captions/" + Uri.EscapeDataString(caption.Id) + "?tfmt=srt");
        request.Headers.Authorization = new("Bearer", await BearerAsync(accountId, ct));
        using var result = await http.SendAsync(request, ct);
        if (!result.IsSuccessStatusCode) throw new InvalidOperationException("This caption track could not be downloaded. The selected account must have permission to edit the video.");
        var data = await result.Content.ReadAsByteArrayAsync(ct);
        if (data.Length > 20 * 1024 * 1024) throw new InvalidOperationException("The caption file is too large to import.");
        return data;
    }
}
