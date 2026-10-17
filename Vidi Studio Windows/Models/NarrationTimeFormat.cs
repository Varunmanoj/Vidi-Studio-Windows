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
        return string.Create(CultureInfo.InvariantCulture,
            $"{total / 3600000:00}:{total / 60000 % 60:00}:{total / 1000 % 60:00}.{total % 1000:000}");
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
        return string.Join(", ", Unit(total / 3600000, "hour"), Unit(total / 60000 % 60, "minute"),
            Unit(total / 1000 % 60, "second"), Unit(total % 1000, "millisecond"));
    }

    public static string SpokenDuration(double seconds)
    {
        var total = Milliseconds(seconds);
        return $"{Unit(total / 1000, "second")}, {Unit(total % 1000, "millisecond")}";
    }
}
