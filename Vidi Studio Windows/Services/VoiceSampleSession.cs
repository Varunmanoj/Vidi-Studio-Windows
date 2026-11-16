using System;

namespace Vidi_Studio_Windows.Services;

/// <summary>Serializes auditions across Settings and voice-management windows.</summary>
internal static class VoiceSampleSession
{
    private static object? owner;
    internal static bool Busy => owner != null;
    internal static event Action? Changed;
    internal static bool Begin(object candidate)
    {
        if (owner != null) return false;
        owner = candidate;
        Changed?.Invoke();
        return true;
    }
    internal static void End(object candidate)
    {
        if (!ReferenceEquals(owner, candidate)) return;
        owner = null;
        Changed?.Invoke();
    }
}
