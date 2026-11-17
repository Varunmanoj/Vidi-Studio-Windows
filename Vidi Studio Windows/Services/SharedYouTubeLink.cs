using System;
using System.Linq;
using System.Net;

namespace Vidi_Studio_Windows.Services;

internal static class SharedYouTubeLink
{
    internal const string InvalidMessage = "This link does not point to a YouTube video.";

    internal static string? VideoId(string? value)
    {
        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri)
            || uri.Scheme is not ("http" or "https") || uri.UserInfo.Length != 0) return null;

        var path = uri.AbsolutePath;
        var parts = (path.EndsWith('/') ? path[..^1] : path).Split('/');
        string? id = null;
        switch (uri.Host.ToLowerInvariant())
        {
            case "youtu.be" or "www.youtu.be":
                if (parts.Length == 2) id = parts[1];
                break;
            case "youtube.com" or "www.youtube.com" or "m.youtube.com"
                or "youtube-nocookie.com" or "www.youtube-nocookie.com":
                if (path == "/watch")
                {
                    foreach (var item in uri.Query.TrimStart('?').Split('&'))
                    {
                        var pair = item.Split('=', 2);
                        if (WebUtility.UrlDecode(pair[0]) != "v") continue;
                        if (pair.Length == 2) id = WebUtility.UrlDecode(pair[1]);
                        break;
                    }
                }
                else if (parts.Length == 3 && parts[1] is "shorts" or "embed" or "live")
                    id = parts[2];
                break;
        }
        return id is { Length: 11 } && id.All(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_') ? id : null;
    }
}
