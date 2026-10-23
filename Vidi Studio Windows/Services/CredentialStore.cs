using System;
using System.Runtime.InteropServices;
using System.Text;

namespace Vidi_Studio_Windows.Services;

/// <summary>
/// Stores API keys in Windows Credential Manager as generic credentials owned by the current
/// user. They are therefore protected by Windows, are not written to config.json, and survive
/// app updates and reinstalls because they are not tied to the installed app package.
/// </summary>
internal static class CredentialStore
{
    private static readonly CredentialSlot Gemini = new("VidiStudio/GeminiApiKey", "Gemini API key");
    /// <summary>The Azure AI Speech subscription key used by Azure online voices.</summary>
    public static readonly CredentialSlot AzureSpeech = new("VidiStudio/AzureSpeechKey", "Azure Speech key");

    /// <summary>Returns the stored Gemini API key, or <c>null</c> when none is present.</summary>
    public static string? Read() => Gemini.Read();

    /// <summary>True when a non-empty Gemini key is available.</summary>
    public static bool ContainsValue() => Gemini.ContainsValue();

    /// <summary>Stores (or clears, for an empty value) the Gemini API key. Returns true on success.</summary>
    public static bool Write(string value) => Gemini.Write(value);

    /// <summary>Removes the stored Gemini API key.</summary>
    public static void Delete() => Gemini.Delete();
}

internal sealed class CredentialSlot
{
    private const int CredTypeGeneric = 1;
    private const int CredPersistLocalMachine = 2;
    private const int ErrorNotFound = 1168;

    // Stable target names so every version addresses the same credential across updates.
    private readonly string targetName;
    private readonly string userName;
    private readonly object gate = new();
    private bool loaded;
    private string? cached;

    public CredentialSlot(string targetName, string userName) { this.targetName = targetName; this.userName = userName; }

    public string? Read()
    {
        lock (gate)
        {
            if (!loaded)
            {
                cached = ReadFromStore();
                loaded = true;
            }
            return cached;
        }
    }

    public bool ContainsValue() => !string.IsNullOrWhiteSpace(Read());

    public bool Write(string value)
    {
        var normalized = (value ?? string.Empty).Trim();
        var desired = normalized.Length == 0 ? null : normalized;
        lock (gate)
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

    public void Delete()
    {
        lock (gate)
        {
            DeleteFromStore();
            cached = null;
            loaded = true;
        }
    }

    private string? ReadFromStore()
    {
        if (!CredRead(targetName, CredTypeGeneric, 0, out var pointer))
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

    private bool WriteToStore(string value)
    {
        var bytes = Encoding.Unicode.GetBytes(value);
        var blob = Marshal.AllocHGlobal(Math.Max(bytes.Length, 1));
        try
        {
            if (bytes.Length > 0) Marshal.Copy(bytes, 0, blob, bytes.Length);
            var credential = new CREDENTIAL
            {
                Type = CredTypeGeneric,
                TargetName = targetName,
                CredentialBlob = blob,
                CredentialBlobSize = bytes.Length,
                Persist = CredPersistLocalMachine,
                UserName = userName,
            };
            return CredWrite(ref credential, 0);
        }
        finally
        {
            Marshal.FreeHGlobal(blob);
        }
    }

    private bool DeleteFromStore()
    {
        if (CredDelete(targetName, CredTypeGeneric, 0)) return true;
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
