using System;
using System.IO;
using System.Text;

namespace Vidi_Studio_Windows.Services;

internal static class GoogleSignInReturn
{
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
