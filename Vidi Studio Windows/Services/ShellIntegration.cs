using Microsoft.Win32;
using System;
using System.IO;
using System.Runtime.InteropServices;

namespace Vidi_Studio_Windows.Services;

public static class ShellIntegration
{
    public const string VerbKey = "DescribeWithVidiStudio";
    public const string LegacyVerbKey = "OpenInVidiStudio";
    public const string MenuText = "Describe with Vidi Studio";
    public const string AppDisplayName = "Vidi Studio";
    public static readonly string[] VideoExtensions = { ".mp4", ".mov", ".m4v", ".mkv", ".avi", ".webm", ".wmv" };

    [DllImport("shell32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    private static extern void SHChangeNotify(uint wEventId, uint uFlags, IntPtr dwItem1, IntPtr dwItem2);
    private const uint SHCNE_ASSOCCHANGED = 0x08000000;
    private const uint SHCNF_IDLIST = 0x0000;

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
            var exeName = Path.GetFileName(exePath);

            // Clean up any legacy verb entries
            CleanupLegacyKeys(exeName);

            // 1. Register under SystemFileAssociations\video (covers all video formats in Explorer)
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

            // 2. Register per specific video extension under SystemFileAssociations
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

            // 3. Register under Software\Classes\*\shell with AppliesTo for Explorer modern / classic menu
            var appliesTo = string.Join(" OR ", Array.ConvertAll(VideoExtensions, ext => $"System.FileExtension:={ext}"));
            using (var anyVerb = Registry.CurrentUser.CreateSubKey(@"Software\Classes\*\shell\" + VerbKey))
            {
                if (anyVerb != null)
                {
                    anyVerb.SetValue("", MenuText);
                    anyVerb.SetValue("Icon", icon);
                    anyVerb.SetValue("AppliesTo", appliesTo);
                    try { anyVerb.DeleteValue("LegacyDisable", false); } catch { }
                    using var cmdKey = anyVerb.CreateSubKey("command");
                    cmdKey?.SetValue("", command);
                }
            }

            // 4. Register ProgID for Open With & Shell registration
            using (var progIdKey = Registry.CurrentUser.CreateSubKey(@"Software\Classes\VidiStudio.Video"))
            {
                if (progIdKey != null)
                {
                    progIdKey.SetValue("", "Video (Vidi Studio)");
                    progIdKey.SetValue("FriendlyTypeName", "Video (Vidi Studio)");
                    using (var defIcon = progIdKey.CreateSubKey("DefaultIcon"))
                    {
                        defIcon?.SetValue("", icon);
                    }
                    using (var verb = progIdKey.CreateSubKey(@"shell\" + VerbKey))
                    {
                        if (verb != null)
                        {
                            verb.SetValue("", MenuText);
                            verb.SetValue("Icon", icon);
                            using var cmdKey = verb.CreateSubKey("command");
                            cmdKey?.SetValue("", command);
                        }
                    }
                    using var openVerb = progIdKey.CreateSubKey(@"shell\open\command");
                    openVerb?.SetValue("", command);
                }
            }

            // 5. Register under Applications for "Open with" compatibility
            if (!string.IsNullOrEmpty(exeName))
            {
                using var appKey = Registry.CurrentUser.CreateSubKey($@"Software\Classes\Applications\{exeName}");
                if (appKey != null)
                {
                    appKey.SetValue("FriendlyAppName", AppDisplayName);
                    appKey.SetValue("Icon", icon);
                    using var supportedTypes = appKey.CreateSubKey("SupportedTypes");
                    if (supportedTypes != null)
                    {
                        foreach (var ext in VideoExtensions)
                        {
                            supportedTypes.SetValue(ext, string.Empty);
                        }
                    }
                    using var openCmdKey = appKey.CreateSubKey(@"shell\open\command");
                    openCmdKey?.SetValue("", command);

                    using var verb = appKey.CreateSubKey(@"shell\" + VerbKey);
                    if (verb != null)
                    {
                        verb.SetValue("", MenuText);
                        verb.SetValue("Icon", icon);
                        using var cmdKey = verb.CreateSubKey("command");
                        cmdKey?.SetValue("", command);
                    }
                }

                // Register per-extension OpenWithList & OpenWithProgids
                foreach (var ext in VideoExtensions)
                {
                    using var owlKey = Registry.CurrentUser.CreateSubKey($@"Software\Classes\{ext}\OpenWithList\{exeName}");
                    owlKey?.SetValue("", string.Empty);

                    using var owpKey = Registry.CurrentUser.CreateSubKey($@"Software\Classes\{ext}\OpenWithProgids");
                    owpKey?.SetValue("VidiStudio.Video", string.Empty);
                }
            }

            // Notify Explorer of the shell associations change
            try
            {
                SHChangeNotify(SHCNE_ASSOCCHANGED, SHCNF_IDLIST, IntPtr.Zero, IntPtr.Zero);
            }
            catch { }
        }
        catch { }
    }

    private static void CleanupLegacyKeys(string? exeName)
    {
        try
        {
            Registry.CurrentUser.DeleteSubKeyTree(@"Software\Classes\SystemFileAssociations\video\shell\" + LegacyVerbKey, false);
            Registry.CurrentUser.DeleteSubKeyTree(@"Software\Classes\*\shell\" + LegacyVerbKey, false);
            foreach (var ext in VideoExtensions)
            {
                Registry.CurrentUser.DeleteSubKeyTree($@"Software\Classes\SystemFileAssociations\{ext}\shell\" + LegacyVerbKey, false);
            }
            if (!string.IsNullOrEmpty(exeName))
            {
                Registry.CurrentUser.DeleteSubKeyTree($@"Software\Classes\Applications\{exeName}\shell\" + LegacyVerbKey, false);
            }
            Registry.CurrentUser.DeleteSubKeyTree(@"Software\Classes\VidiStudio.Video\shell\" + LegacyVerbKey, false);
        }
        catch { }
    }

    public static void UnregisterContextMenu()
    {
        if (!OperatingSystem.IsWindows()) return;
        try
        {
            Registry.CurrentUser.DeleteSubKeyTree(@"Software\Classes\SystemFileAssociations\video\shell\" + VerbKey, false);
            Registry.CurrentUser.DeleteSubKeyTree(@"Software\Classes\*\shell\" + VerbKey, false);
            foreach (var ext in VideoExtensions)
            {
                Registry.CurrentUser.DeleteSubKeyTree($@"Software\Classes\SystemFileAssociations\{ext}\shell\" + VerbKey, false);
            }
            Registry.CurrentUser.DeleteSubKeyTree(@"Software\Classes\VidiStudio.Video", false);

            var exePath = Environment.ProcessPath;
            if (!string.IsNullOrEmpty(exePath))
            {
                var exeName = Path.GetFileName(exePath);
                if (!string.IsNullOrEmpty(exeName))
                {
                    Registry.CurrentUser.DeleteSubKeyTree($@"Software\Classes\Applications\{exeName}", false);
                    foreach (var ext in VideoExtensions)
                    {
                        try { Registry.CurrentUser.DeleteSubKey($@"Software\Classes\{ext}\OpenWithList\{exeName}", false); } catch { }
                        try
                        {
                            using var owp = Registry.CurrentUser.OpenSubKey($@"Software\Classes\{ext}\OpenWithProgids", true);
                            owp?.DeleteValue("VidiStudio.Video", false);
                        }
                        catch { }
                    }
                }
            }

            CleanupLegacyKeys(exePath != null ? Path.GetFileName(exePath) : null);

            try
            {
                SHChangeNotify(SHCNE_ASSOCCHANGED, SHCNF_IDLIST, IntPtr.Zero, IntPtr.Zero);
            }
            catch { }
        }
        catch { }
    }
}
