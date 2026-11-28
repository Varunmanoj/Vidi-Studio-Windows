using System;
using System.IO;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Web;

namespace Vidi_Studio_Windows.Services;

internal static class GoogleSignInReturn
{
    internal static async Task<string?> ReceiveCodeAsync(TcpListener listener, string state, bool cloud, CancellationToken cancellationToken)
    {
        while (true)
        {
            using var client = await listener.AcceptTcpClientAsync(cancellationToken);
            using var stream = client.GetStream();
            using var requestTimeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            requestTimeout.CancelAfter(TimeSpan.FromSeconds(5));
            string? target;
            try { target = await ReadRequestTargetAsync(stream, requestTimeout.Token); }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested) { continue; }
            catch (IOException) { continue; }
            if (target == null || !Uri.TryCreate("http://127.0.0.1" + target, UriKind.Absolute, out var uri)) continue;
            var query = HttpUtility.ParseQueryString(uri.Query);
            if (query["state"] != state) continue;

            var code = query["code"];
            var receivedCode = !string.IsNullOrEmpty(code);
            try
            {
                await stream.WriteAsync(Response(cloud, receivedCode), cancellationToken);
                client.Client.Shutdown(SocketShutdown.Send);
            }
            // Closing the tab after consent must not discard an already received callback.
            catch (IOException) { }
            catch (SocketException) { }
            return receivedCode ? code : null;
        }
    }

    private static async Task<string?> ReadRequestTargetAsync(NetworkStream stream, CancellationToken cancellationToken)
    {
        // Closing with unread request headers can reset the connection before Edge renders the response.
        var headers = new byte[16 * 1024];
        var used = 0;
        while (used < headers.Length)
        {
            var count = await stream.ReadAsync(headers.AsMemory(used), cancellationToken);
            if (count == 0) return null;
            var searchFrom = Math.Max(0, used - 3);
            used += count;
            if (headers.AsSpan(searchFrom, used - searchFrom).IndexOf("\r\n\r\n"u8) < 0) continue;

            var lineEnd = headers.AsSpan(0, used).IndexOf("\r\n"u8);
            if (lineEnd > 8192) return null;
            var line = Encoding.ASCII.GetString(headers, 0, lineEnd).Split(' ');
            return line.Length == 3 && line[0] == "GET" && line[1].StartsWith("/?", StringComparison.Ordinal)
                ? line[1] : null;
        }
        return null;
    }

    internal static byte[] Response(bool cloud, bool receivedCode)
    {
        var service = cloud ? "Google Cloud" : "YouTube";
        var heading = receivedCode ? "Return to VidiStudio" : "Sign-in didn’t finish";
        var message = receivedCode
            ? "You’re done in the browser. VidiStudio will finish connecting your account."
            : "To try again, start a new sign-in in VidiStudio.";
        var assets = Path.Combine(AppContext.BaseDirectory, "Assets");
        const string fallback = "<!doctype html><html lang=\"en\"><meta charset=\"utf-8\"><title>{{HEADING}} · VidiStudio</title><main><h1>{{HEADING}}</h1><p>{{MESSAGE}}</p><p>Close this tab and switch back to the app.</p></main></html>";
        var template = ReadAsset("GoogleSignInReturn.html", path => File.ReadAllText(path)) ?? fallback;
        var icon = ReadAsset("Square44x44Logo.scale-200.png",
            path => "data:image/png;base64," + Convert.ToBase64String(File.ReadAllBytes(path)));
        var page = template.Replace("{{SERVICE}}", service)
            .Replace("{{HEADING}}", heading)
            .Replace("{{MESSAGE}}", message)
            .Replace("{{APP_ICON}}", icon is null ? "" : $"<img src=\"{icon}\" alt=\"\">")
            .Replace("{{FAVICON}}", icon is null ? "" : $"<link rel=\"icon\" href=\"{icon}\">");
        var header = string.Join("\r\n",
            "HTTP/1.1 200 OK",
            "Content-Type: text/html; charset=utf-8",
            $"Content-Length: {Encoding.UTF8.GetByteCount(page)}",
            "Cache-Control: no-store",
            "Referrer-Policy: no-referrer",
            "Content-Security-Policy: default-src 'none'; img-src data:; style-src 'unsafe-inline'; frame-ancestors 'none'; base-uri 'none'; form-action 'none'",
            "Connection: close",
            "", "");
        return Encoding.UTF8.GetBytes(header + page);

        string? ReadAsset(string name, Func<string, string> read)
        {
            try { return read(Path.Combine(assets, name)); }
            catch (IOException) { return null; }
            catch (UnauthorizedAccessException) { return null; }
        }
    }
}
