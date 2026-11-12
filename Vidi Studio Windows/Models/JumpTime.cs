using System;
using System.Globalization;
using System.Text.RegularExpressions;

namespace Vidi_Studio_Windows.Models;

internal static class JumpTime
{
    public static double? Parse(string text, double duration)
    {
        if (!double.IsFinite(duration) || duration <= 0) return null;
        var parts = text.Trim().Split(':');
        if (parts.Length is < 1 or > 3) return null;
        double total = 0;
        for (var index = 0; index < parts.Length; index++)
        {
            var pattern = index == parts.Length - 1 ? @"\A[0-9]+(?:\.[0-9]+)?\z" : @"\A[0-9]+\z";
            if (!Regex.IsMatch(parts[index], pattern) ||
                !double.TryParse(parts[index], NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var value) ||
                !double.IsFinite(value) || (index > 0 && value >= 60)) return null;
            total = total * 60 + value;
        }
        return double.IsFinite(total) && total <= duration ? total : null;
    }

    public static string Format(double seconds)
    {
        var ms = (long)Math.Floor(Math.Max(0, seconds) * 1000);
        return string.Create(CultureInfo.InvariantCulture, $"{ms / 3600000:00}:{ms / 60000 % 60:00}:{ms / 1000 % 60:00}.{ms % 1000:000}");
    }
}
