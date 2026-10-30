using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json.Nodes;

namespace Vidi_Studio_Windows.Services;

/// <summary>Per-user list visibility; project folders and manifests are never changed.</summary>
public sealed class ProjectListVisibility
{
    private const string SettingsKey = "removedProjectFolders";
    private readonly SettingsStore settings;
    private HashSet<string> removedFolders = new(StringComparer.OrdinalIgnoreCase);

    public ProjectListVisibility(SettingsStore settings) { this.settings = settings; Reload(); }
    private static string Normalize(string folder) => ProjectFolderIdentity.Normalize(folder);
    public bool IsRemoved(string folder) => removedFolders.Contains(Normalize(folder));
    public void Reload()
    {
        var paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (settings.Data[SettingsKey] is JsonArray saved)
            foreach (var entry in saved)
                if (entry is JsonValue value && value.TryGetValue<string>(out var path) && !string.IsNullOrWhiteSpace(path))
                    try { paths.Add(Normalize(path)); } catch (ArgumentException) { }
        removedFolders = paths;
    }
    public void Remove(string folder) => SetRemoved(folder, true);
    public void Restore(string folder) => SetRemoved(folder, false);
    private void SetRemoved(string folder, bool removed)
    {
        var updated = new HashSet<string>(removedFolders, StringComparer.OrdinalIgnoreCase);
        var changed = removed ? updated.Add(Normalize(folder)) : updated.Remove(Normalize(folder));
        if (!changed) return;
        var previous = settings.Data[SettingsKey]?.DeepClone();
        settings.Data[SettingsKey] = new JsonArray(updated.OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
            .Select(path => (JsonNode?)JsonValue.Create(path)).ToArray());
        try { settings.Save(); }
        catch { settings.Data[SettingsKey] = previous; throw; }
        removedFolders = updated;
    }
}
