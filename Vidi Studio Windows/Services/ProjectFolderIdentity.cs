using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32.SafeHandles;

namespace Vidi_Studio_Windows.Services;

/// <summary>One identity for a project folder reached through different paths.</summary>
public static class ProjectFolderIdentity
{
    public static string Normalize(string folder)
    {
        var fullPath = Path.TrimEndingDirectorySeparator(Path.GetFullPath(RemoveExtendedPrefix(folder)));
        try
        {
            if (OperatingSystem.IsWindows())
                fullPath = ResolveWindowsDirectory(fullPath) ?? fullPath;
            else
                fullPath = ResolveDirectoryLinks(fullPath);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException)
        {
            // Missing/offline folders keep their lexical identity for damaged-project recovery.
        }
        return Path.TrimEndingDirectorySeparator(RemoveExtendedPrefix(fullPath));
    }

    public static List<string> DistinctFolders(IEnumerable<string> folders) => folders
        .Select(Normalize).Distinct(StringComparer.OrdinalIgnoreCase).ToList();

    private static string RemoveExtendedPrefix(string path)
    {
        if (!OperatingSystem.IsWindows()) return path;
        if (path.StartsWith(@"\\?\UNC\", StringComparison.OrdinalIgnoreCase)) return @"\\" + path[8..];
        if (path.StartsWith(@"\\?\", StringComparison.Ordinal) && path.Length >= 6 && path[5] == ':') return path[4..];
        return path;
    }

    private static string ResolveDirectoryLinks(string path)
    {
        var directory = new DirectoryInfo(path);
        if (!directory.Exists) return path;
        if (directory.ResolveLinkTarget(true) is { } target) return ResolveDirectoryLinks(target.FullName);
        return directory.Parent is { } parent
            ? Path.Combine(ResolveDirectoryLinks(parent.FullName), directory.Name) : directory.FullName;
    }

    private static string? ResolveWindowsDirectory(string path)
    {
        // Metadata-only, OPEN_EXISTING, shared read/write/delete. Follow directory links.
        // https://learn.microsoft.com/windows/win32/api/fileapi/nf-fileapi-getfinalpathnamebyhandlew
        const uint openExisting = 3;
        const uint backupSemantics = 0x02000000;
        using var handle = CreateFileW(path, 0, FileShare.Read | FileShare.Write | FileShare.Delete,
            IntPtr.Zero, openExisting, backupSemantics, IntPtr.Zero);
        if (handle.IsInvalid) return null;
        var buffer = new StringBuilder(512);
        var length = GetFinalPathNameByHandleW(handle, buffer, (uint)buffer.Capacity, 0);
        if (length >= buffer.Capacity)
        {
            buffer = new StringBuilder(checked((int)length + 1));
            length = GetFinalPathNameByHandleW(handle, buffer, (uint)buffer.Capacity, 0);
        }
        return length > 0 && length < buffer.Capacity ? buffer.ToString() : null;
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true, ExactSpelling = true)]
    private static extern SafeFileHandle CreateFileW(string path, uint access, FileShare share, IntPtr security,
        uint disposition, uint flags, IntPtr template);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true, ExactSpelling = true)]
    private static extern uint GetFinalPathNameByHandleW(SafeFileHandle handle, StringBuilder path, uint size, uint flags);
}
