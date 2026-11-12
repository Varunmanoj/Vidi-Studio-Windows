using System;
using System.Globalization;

namespace Vidi_Studio_Windows.Models;

/// <summary>Explicit, millisecond-precision labels for the narration issues table.</summary>
public static class NarrationTimeFormat
{
    private static long Milliseconds(double seconds) => double.IsFinite(seconds)
        ? (long)Math.Round(Math.Max(0, seconds) * 1000, MidpointRounding.AwayFromZero) : 0;

    public static string Timestamp(double seconds)
    {
        var total = Milliseconds(seconds);
        var clock = string.Create(CultureInfo.InvariantCulture,
            $"{total / 3600000 % 24:00}:{total / 60000 % 60:00}:{total / 1000 % 60:00}.{total % 1000:000}");
        return total >= 86400000 ? $"{total / 86400000}d {clock}" : clock;
    }

    public static string Duration(double seconds)
    {
        var total = Milliseconds(seconds);
        return string.Create(CultureInfo.InvariantCulture, $"{total / 1000}.{total % 1000:000} s");
    }

    private static string Unit(long value, string name) => $"{value} {name}{(value == 1 ? "" : "s")}";

    public static string SpokenTime(double seconds)
    {
        var total = Milliseconds(seconds);
        var parts = new System.Collections.Generic.List<string>();
        foreach (var (value, name) in new[] { (total / 86400000, "day"), (total / 3600000 % 24, "hour"), (total / 60000 % 60, "minute"),
                     (total / 1000 % 60, "second"), (total % 1000, "millisecond") })
            if (value > 0) parts.Add(Unit(value, name));
        return parts.Count == 0 ? "0 seconds" : string.Join(", ", parts);
    }

    public static string SpokenDuration(double seconds) => SpokenTime(seconds);

    public static string MediaDuration(double seconds)
    {
        var total = Milliseconds(seconds);
        var parts = new System.Collections.Generic.List<string>();
        foreach (var (value, name) in new[] { (total / 86400000, "d"), (total / 3600000 % 24, "h"),
                     (total / 60000 % 60, "m"), (total / 1000 % 60, "s"), (total % 1000, "ms") })
            if (value > 0) parts.Add(value.ToString(CultureInfo.InvariantCulture) + name);
        return parts.Count == 0 ? "0s" : string.Join(" ", parts);
    }

    public static string ProjectDuration(double? seconds) => seconds is { } value && double.IsFinite(value) && value > 0
        ? MediaDuration(value) : "Unavailable";

    public static string SpokenProjectDuration(double? seconds) => seconds is { } value && double.IsFinite(value) && value > 0
        ? SpokenDuration(value) : "unavailable";
}
