using System;
using System.Runtime.InteropServices;
using System.Text;

namespace Vidi_Studio_Windows.Services;

/// <summary>
/// Stores the Gemini API key in Windows Credential Manager as a generic credential owned by
/// the current user. It is therefore protected by Windows, is not written to config.json, and
/// survives app updates and reinstalls because it is not tied to the installed app package.
/// </summary>
internal static class CredentialStore
{
    // A stable target name so every version addresses the same credential across updates.
    private const string TargetName = "VidiStudio/GeminiApiKey";
    private const string UserName = "Gemini API key";
    private const int CredTypeGeneric = 1;
    private const int CredPersistLocalMachine = 2;
    private const int ErrorNotFound = 1168;

    private static readonly object Gate = new();
    private static bool loaded;
    private static string? cached;

    /// <summary>Returns the stored API key, or <c>null</c> when none is present.</summary>
    public static string? Read()
    {
        lock (Gate)
        {
            if (!loaded)
            {
                cached = ReadFromStore();
                loaded = true;
            }
            return cached;
        }
    }

    /// <summary>True when a non-empty key is available.</summary>
    public static bool ContainsValue() => !string.IsNullOrWhiteSpace(Read());

    /// <summary>Stores (or clears, for an empty value) the API key. Returns true on success.</summary>
    public static bool Write(string value)
    {
        var normalized = (value ?? string.Empty).Trim();
        var desired = normalized.Length == 0 ? null : normalized;
        lock (Gate)
        {
            if (loaded && cached == desired) return true;
            if (normalized.Length == 0)
            {
                if (!DeleteFromStore()) return false;
                cached = null;
                loaded = true;
                return true;
            }
            if (!WriteToStore(normalized)) return false;
            cached = normalized;
            loaded = true;
            return true;
        }
    }

    /// <summary>Removes the stored API key.</summary>
    public static void Delete()
    {
        lock (Gate)
        {
            DeleteFromStore();
            cached = null;
            loaded = true;
        }
    }

    private static string? ReadFromStore()
    {
        if (!CredRead(TargetName, CredTypeGeneric, 0, out var pointer))
            return null;
        try
        {
            var credential = Marshal.PtrToStructure<CREDENTIAL>(pointer);
            if (credential.CredentialBlobSize <= 0 || credential.CredentialBlob == IntPtr.Zero) return string.Empty;
            return Marshal.PtrToStringUni(credential.CredentialBlob, credential.CredentialBlobSize / 2) ?? string.Empty;
        }
        finally
        {
            CredFree(pointer);
        }
    }

    private static bool WriteToStore(string value)
    {
        var bytes = Encoding.Unicode.GetBytes(value);
        var blob = Marshal.AllocHGlobal(Math.Max(bytes.Length, 1));
        try
        {
            if (bytes.Length > 0) Marshal.Copy(bytes, 0, blob, bytes.Length);
            var credential = new CREDENTIAL
            {
                Type = CredTypeGeneric,
                TargetName = TargetName,
                CredentialBlob = blob,
                CredentialBlobSize = bytes.Length,
                Persist = CredPersistLocalMachine,
                UserName = UserName,
            };
            return CredWrite(ref credential, 0);
        }
        finally
        {
            Marshal.FreeHGlobal(blob);
        }
    }

    private static bool DeleteFromStore()
    {
        if (CredDelete(TargetName, CredTypeGeneric, 0)) return true;
        return Marshal.GetLastWin32Error() == ErrorNotFound;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct CREDENTIAL
    {
        public int Flags;
        public int Type;
        [MarshalAs(UnmanagedType.LPWStr)] public string TargetName;
        [MarshalAs(UnmanagedType.LPWStr)] public string? Comment;
        public long LastWritten;
        public int CredentialBlobSize;
        public IntPtr CredentialBlob;
        public int Persist;
        public int AttributeCount;
        public IntPtr Attributes;
        [MarshalAs(UnmanagedType.LPWStr)] public string? TargetAlias;
        [MarshalAs(UnmanagedType.LPWStr)] public string UserName;
    }

    [DllImport("advapi32.dll", EntryPoint = "CredReadW", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool CredRead(string target, int type, int reservedFlag, out IntPtr credentialPtr);

    [DllImport("advapi32.dll", EntryPoint = "CredWriteW", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool CredWrite(ref CREDENTIAL userCredential, int flags);

    [DllImport("advapi32.dll", EntryPoint = "CredDeleteW", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool CredDelete(string target, int type, int flags);

    [DllImport("advapi32.dll", EntryPoint = "CredFree", SetLastError = true)]
    private static extern void CredFree(IntPtr credential);
}
