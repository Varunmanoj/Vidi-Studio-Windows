using System;
using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;
using Vidi_Studio_Windows.Models;

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
/// The values live in the project's project.json, so they follow the portable project
/// folder between Windows and macOS instead of staying on one computer. The legacy
/// per-user keys are still read once to migrate a choice made before that move.
/// </summary>
public static class ProjectPlaybackPreferences
{
    public const string NarrationField = "narrationEnabled";
    public const string FollowField = "followTranscript";

    /// <summary>Legacy per-user keys from before the choices moved into project.json.</summary>
    public static string NarrationKey(string projectId) => "projectNarrationEnabled." + projectId;
    public static string FollowKey(string projectId) => "projectFollowTranscript." + projectId;

    public static bool NarrationEnabled(SettingsStore settings, StudioProject project) =>
        Value(settings, project, ProjectPlaybackPreference.Narration) ?? false;

    public static bool FollowEnabled(SettingsStore settings, StudioProject project) =>
        Value(settings, project, ProjectPlaybackPreference.Follow) ?? true;

    public static void SetNarrationEnabled(SettingsStore settings, StudioProject project, bool enabled) =>
        Set(settings, project, ProjectPlaybackPreference.Narration, enabled);

    public static void SetFollowEnabled(SettingsStore settings, StudioProject project, bool enabled) =>
        Set(settings, project, ProjectPlaybackPreference.Follow, enabled);

    /// <summary>Raised after a project's playback preference changes, for the other window or toggle.</summary>
    public static event Action<ProjectPlaybackPreferenceChange>? Changed;

    private static void Set(
        SettingsStore settings,
        StudioProject project,
        ProjectPlaybackPreference preference,
        bool enabled)
    {
        if (project is null || string.IsNullOrWhiteSpace(project.Id)) return;
        if (Value(settings, project, preference) == enabled) return;
        Write(project, preference, enabled);
        Cache(project, preference, enabled);
        Changed?.Invoke(new ProjectPlaybackPreferenceChange(project.Id, preference, enabled));
    }

    /// <summary>
    /// Reads the choice from project.json, then falls back to the legacy per-user
    /// key and migrates that value into the project the first time it is seen.
    /// </summary>
    private static bool? Value(SettingsStore settings, StudioProject project, ProjectPlaybackPreference preference)
    {
        if (project is null || string.IsNullOrWhiteSpace(project.Id)) return null;
        if (project.Playback is { } local)
        {
            var cached = preference == ProjectPlaybackPreference.Narration
                ? local.NarrationEnabled
                : local.FollowTranscript;
            if (cached.HasValue) return cached;
        }
        if (ManifestValue(project, preference) is { } persisted)
        {
            Cache(project, preference, persisted);
            return persisted;
        }
        var legacyKey = preference == ProjectPlaybackPreference.Narration
            ? NarrationKey(project.Id)
            : FollowKey(project.Id);
        if (settings.Data[legacyKey] is null) return null;
        var legacy = SettingsStore.GetBool(
            settings.Data[legacyKey],
            preference == ProjectPlaybackPreference.Follow);
        Write(project, preference, legacy);
        Cache(project, preference, legacy);
        return legacy;
    }

    private static bool? ManifestValue(StudioProject project, ProjectPlaybackPreference preference)
    {
        if (string.IsNullOrWhiteSpace(project.Folder)) return null;
        var manifest = Path.Combine(project.Folder, "project.json");
        if (!File.Exists(manifest)) return null;
        try
        {
            using var document = JsonDocument.Parse(StudioProject.ReadManifestText(manifest));
            if (!document.RootElement.TryGetProperty("playback", out var playback) ||
                playback.ValueKind != JsonValueKind.Object ||
                !playback.TryGetProperty(Field(preference), out var value))
                return null;
            return value.ValueKind switch
            {
                JsonValueKind.True => true,
                JsonValueKind.False => false,
                _ => null
            };
        }
        catch (Exception)
        {
            return null;
        }
    }

    /// <summary>
    /// Writes one choice into project.json while preserving every other project
    /// field, including fields this app does not model.
    /// </summary>
    private static bool Write(StudioProject project, ProjectPlaybackPreference preference, bool enabled)
    {
        if (string.IsNullOrWhiteSpace(project.Folder)) return false;
        var manifest = Path.Combine(project.Folder, "project.json");
        if (!File.Exists(manifest)) return false;
        JsonObject root;
        try
        {
            root = JsonNode.Parse(StudioProject.ReadManifestText(manifest)) as JsonObject ?? new JsonObject();
        }
        catch (Exception)
        {
            return false;
        }
        if (root["playback"] is not JsonObject playback)
        {
            playback = new JsonObject();
            root["playback"] = playback;
        }
        playback[Field(preference)] = enabled;
        var temp = Path.Combine(project.Folder, "project." + Guid.NewGuid().ToString("N") + ".json");
        try
        {
            File.WriteAllText(temp, root.ToJsonString(Json.Options));
            File.Move(temp, manifest, true);
            return true;
        }
        catch (Exception)
        {
            try { File.Delete(temp); } catch (IOException) { }
            return false;
        }
    }

    private static void Cache(StudioProject project, ProjectPlaybackPreference preference, bool enabled)
    {
        project.Playback ??= new ProjectPlaybackSettings();
        if (preference == ProjectPlaybackPreference.Narration) project.Playback.NarrationEnabled = enabled;
        else project.Playback.FollowTranscript = enabled;
    }

    private static string Field(ProjectPlaybackPreference preference) =>
        preference == ProjectPlaybackPreference.Narration ? NarrationField : FollowField;
}
