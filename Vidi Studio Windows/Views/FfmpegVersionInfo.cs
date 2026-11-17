using System;
using System.Text.RegularExpressions;

namespace Vidi_Studio_Windows.Views;

internal readonly record struct FfmpegVersionInfo(string Version, string License)
{
    internal static FfmpegVersionInfo Parse(string output)
    {
        var match = Regex.Match(output, @"^ffmpeg version\s+(\S+)", RegexOptions.Multiline);
        var release = match.Success ? match.Groups[1].Value : "Unknown";
        // Vendor suffixes belong in diagnostics; About shows the upstream release.
        var numeric = Regex.Match(release, @"^\d+(?:\.\d+)+(?!\d)");
        if (numeric.Success) release = numeric.Value;
        var gpl = output.Contains("--enable-gpl", StringComparison.Ordinal);
        var v3 = output.Contains("--enable-version3", StringComparison.Ordinal);
        var license = output.Contains("--enable-nonfree", StringComparison.Ordinal) ? "Nonredistributable build"
            : gpl ? (v3 ? "GNU GPL version 3 or later" : "GNU GPL version 2 or later")
            : v3 ? "GNU LGPL version 3 or later" : "GNU LGPL version 2.1 or later";
        return new(release, license);
    }
}
