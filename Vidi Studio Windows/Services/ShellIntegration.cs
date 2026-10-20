using Microsoft.Win32;
using System;
using System.IO;

namespace Vidi_Studio_Windows.Services;

public static class ShellIntegration
{
    public const string VerbKey = "OpenInVidiStudio";
    public const string MenuText = "Open in VidiStudio";
    public static readonly string[] VideoExtensions = { ".mp4", ".mov", ".m4v", ".mkv", ".avi", ".webm", ".wmv" };

    public static bool IsContextMenuRegistered()
    {
        if (!OperatingSystem.IsWindows()) return false;
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(@"Software\Classes\SystemFileAssociations\video\shell\" + VerbKey);
            return key != null;
        }
        catch { return false; }
    }

    public static void UpdateContextMenuState(bool hasActiveKey = true, string? exePath = null)
    {
        if (!OperatingSystem.IsWindows()) return;
        try
        {
            exePath ??= Environment.ProcessPath;
            if (string.IsNullOrEmpty(exePath) || !File.Exists(exePath)) return;
            RegisterContextMenu(exePath);
        }
        catch { }
    }

    public static void RegisterContextMenu(string? exePath = null)
    {
        if (!OperatingSystem.IsWindows()) return;
        try
        {
            exePath ??= Environment.ProcessPath;
            if (string.IsNullOrEmpty(exePath) || !File.Exists(exePath)) return;

            var command = $"\"{exePath}\" --video \"%1\"";
            var icon = $"\"{exePath}\",0";

            // Register under SystemFileAssociations\video (covers all video formats in Explorer)
            using (var verb = Registry.CurrentUser.CreateSubKey(@"Software\Classes\SystemFileAssociations\video\shell\" + VerbKey))
            {
                if (verb != null)
                {
                    verb.SetValue("", MenuText);
                    verb.SetValue("Icon", icon);
                    try { verb.DeleteValue("LegacyDisable", false); } catch { }
                    using var cmdKey = verb.CreateSubKey("command");
                    cmdKey?.SetValue("", command);
                }
            }

            // Register per specific video extension to guarantee context menu visibility
            foreach (var ext in VideoExtensions)
            {
                using var verb = Registry.CurrentUser.CreateSubKey($@"Software\Classes\SystemFileAssociations\{ext}\shell\" + VerbKey);
                if (verb != null)
                {
                    verb.SetValue("", MenuText);
                    verb.SetValue("Icon", icon);
                    try { verb.DeleteValue("LegacyDisable", false); } catch { }
                    using var cmdKey = verb.CreateSubKey("command");
                    cmdKey?.SetValue("", command);
                }
            }

            // Register under Applications for "Open with" compatibility
            var exeName = Path.GetFileName(exePath);
            if (!string.IsNullOrEmpty(exeName))
            {
                using var appKey = Registry.CurrentUser.CreateSubKey($@"Software\Classes\Applications\{exeName}");
                if (appKey != null)
                {
                    using var supportedTypes = appKey.CreateSubKey("SupportedTypes");
                    if (supportedTypes != null)
                    {
                        foreach (var ext in VideoExtensions)
                        {
                            supportedTypes.SetValue(ext, string.Empty);
                        }
                    }
                    using var cmdKey = appKey.CreateSubKey(@"shell\open\command");
                    cmdKey?.SetValue("", command);
                }
            }
        }
        catch { }
    }

    public static void UnregisterContextMenu()
    {
        if (!OperatingSystem.IsWindows()) return;
        try
        {
            Registry.CurrentUser.DeleteSubKeyTree(@"Software\Classes\SystemFileAssociations\video\shell\" + VerbKey, false);
            foreach (var ext in VideoExtensions)
            {
                Registry.CurrentUser.DeleteSubKeyTree($@"Software\Classes\SystemFileAssociations\{ext}\shell\" + VerbKey, false);
            }
            var exePath = Environment.ProcessPath;
            if (!string.IsNullOrEmpty(exePath))
            {
                var exeName = Path.GetFileName(exePath);
                if (!string.IsNullOrEmpty(exeName))
                {
                    Registry.CurrentUser.DeleteSubKeyTree($@"Software\Classes\Applications\{exeName}", false);
                }
            }
        }
        catch { }
    }
}
