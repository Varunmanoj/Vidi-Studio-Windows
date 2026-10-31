using System;
using Vidi_Studio_Windows.Views;

class Program
{
    static void Main()
    {
        var normal = SettingsWindowPlacement.Calculate(new(200, 100, 1100, 800), new(0, 0, 1920, 1040), 1);
        if (normal != new SettingsWindowBounds(230, 110, 1040, 780)) throw new Exception("Settings must center over a restored parent.");
        var areas = new[] { new SettingsWindowBounds(0, 0, 1920, 1040), new(-1920, 40, 1920, 1040), new(1920, -1080, 1280, 1000), new(0, 0, 1024, 728), new(0, 0, 640, 440) };
        var scales = new[] { 1d, 1.25, 1.5, 2, 2.5 };
        var cases = 0;
        foreach (var area in areas)
            foreach (var scale in scales)
                foreach (var owner in new[] { area, new SettingsWindowBounds(area.X + 200, area.Y + 100, 900, 600), new SettingsWindowBounds(area.X - 500, area.Y - 200, 2000, 1400) })
                {
                    var window = SettingsWindowPlacement.Calculate(owner, area, scale);
                    if (window.Width <= 0 || window.Height <= 0 || window.X < area.X || window.Y < area.Y || window.X + window.Width > area.X + area.Width || window.Y + window.Height > area.Y + area.Height)
                        throw new Exception($"Settings clipped at {scale} scale: {window} in {area}");
                    cases++;
                }
        foreach (var scale in new[] { 0, double.NaN, double.PositiveInfinity })
            if (SettingsWindowPlacement.Calculate(new(200, 100, 1100, 800), areas[0], scale) != normal)
                throw new Exception("Invalid DPI must use the default scale.");
        Console.WriteLine($"PASS: restored-parent centering and {cases} DPI, taskbar, small-display, off-screen-parent, and multi-monitor placement cases.");
    }
}
