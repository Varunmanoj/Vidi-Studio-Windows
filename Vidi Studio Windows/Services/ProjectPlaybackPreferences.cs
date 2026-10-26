using System;

namespace Vidi_Studio_Windows.Services;

public enum ProjectPlaybackPreference
{
    Narration,
    Follow
}

public sealed class ProjectPlaybackPreferenceChange
{
    public ProjectPlaybackPreferenceChange(string projectId, ProjectPlaybackPreference preference, bool enabled)
    {
        ProjectId = projectId;
        Preference = preference;
        Enabled = enabled;
    }

    public string ProjectId { get; }
    public ProjectPlaybackPreference Preference { get; }
    public bool Enabled { get; }
}

/// <summary>
/// Project-specific playback choices shared by the editor toggles and Settings → Projects.
/// The keys mirror the macOS ProjectPlaybackPreferences user-defaults names, so both
/// desktops remember the same choice for the same project id. Values stay in the user
/// config outside the portable project folder and survive closing and reopening the app.
/// </summary>
public static class ProjectPlaybackPreferences
{
    public static string NarrationKey(string projectId) => "projectNarrationEnabled." + projectId;
    public static string FollowKey(string projectId) => "projectFollowTranscript." + projectId;

    public static bool NarrationEnabled(SettingsStore settings, string? projectId) =>
        !string.IsNullOrWhiteSpace(projectId) && settings.GetBool(NarrationKey(projectId), false);

    public static bool FollowEnabled(SettingsStore settings, string? projectId) =>
        string.IsNullOrWhiteSpace(projectId) || settings.GetBool(FollowKey(projectId), true);

    public static void SetNarrationEnabled(SettingsStore settings, string projectId, bool enabled) =>
        Set(settings, projectId, ProjectPlaybackPreference.Narration, enabled, false);

    public static void SetFollowEnabled(SettingsStore settings, string projectId, bool enabled) =>
        Set(settings, projectId, ProjectPlaybackPreference.Follow, enabled, true);

    /// <summary>Raised after a project's playback preference changes, for the other window or toggle.</summary>
    public static event Action<ProjectPlaybackPreferenceChange>? Changed;

    private static void Set(SettingsStore settings, string projectId, ProjectPlaybackPreference preference, bool enabled, bool fallback)
    {
        if (string.IsNullOrWhiteSpace(projectId)) return;
        var key = preference == ProjectPlaybackPreference.Narration ? NarrationKey(projectId) : FollowKey(projectId);
        if (settings.GetBool(key, fallback) == enabled) return;
        settings.Set(key, enabled);
        settings.Save();
        Changed?.Invoke(new ProjectPlaybackPreferenceChange(projectId, preference, enabled));
    }
}
