using System;

namespace Vidi_Studio_Windows.Views;

internal readonly record struct SettingsWindowBounds(int X, int Y, int Width, int Height);

internal static class SettingsWindowPlacement
{
    internal static SettingsWindowBounds Calculate(SettingsWindowBounds owner, SettingsWindowBounds workArea, double scale)
    {
        if (!double.IsFinite(scale) || scale <= 0) scale = 1;
        var inset = Math.Min((int)Math.Round(16 * scale), Math.Max(0, Math.Min(workArea.Width, workArea.Height) / 10));
        var width = Math.Min((int)Math.Round(1040 * scale), Math.Max(1, workArea.Width - 2 * inset));
        var height = Math.Min((int)Math.Round(780 * scale), Math.Max(1, workArea.Height - 2 * inset));
        var left = workArea.X + inset;
        var top = workArea.Y + inset;
        var x = Math.Clamp(owner.X + (owner.Width - width) / 2, left, left + Math.Max(0, workArea.Width - 2 * inset - width));
        var y = Math.Clamp(owner.Y + (owner.Height - height) / 2, top, top + Math.Max(0, workArea.Height - 2 * inset - height));
        return new(x, y, width, height);
    }
}
