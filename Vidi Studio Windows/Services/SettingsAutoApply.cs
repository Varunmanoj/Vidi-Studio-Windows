using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Nodes;

namespace Vidi_Studio_Windows.Services;

/// <summary>Merges edits into the latest preferences without reverting changes from other windows.</summary>
internal sealed class SettingsAutoApply
{
    private JsonObject applied;

    public SettingsAutoApply(JsonObject initial) => applied = initial.DeepClone().AsObject();

    public bool HasChanges(JsonObject current) => !JsonNode.DeepEquals(applied, current);

    public bool Apply(JsonObject current, Func<JsonObject> load,
        Action<JsonObject, string?, IReadOnlyCollection<string>> save)
    {
        if (!HasChanges(current)) return false;
        var snapshot = current.DeepClone().AsObject();
        var changed = applied.Select(p => p.Key).Union(snapshot.Select(p => p.Key))
            .Where(key => applied.ContainsKey(key) != snapshot.ContainsKey(key)
                || !JsonNode.DeepEquals(applied[key], snapshot[key])).ToArray();
        var latest = load().DeepClone().AsObject();
        foreach (var key in changed.Where(key => key is not "apiKey" and not "azureSpeechKey"))
            MergeProperty(latest, key, applied, snapshot);
        // Credentials never enter the JSON payload passed to persistence.
        latest.Remove("apiKey");
        latest.Remove("azureSpeechKey");
        var keyEdit = changed.Contains("apiKey") ? snapshot["apiKey"]?.GetValue<string>() ?? "" : null;
        save(latest, keyEdit, changed);
        // A failed write must remain pending for retry, including when closing the window.
        applied = snapshot;
        return true;
    }

    private static void MergeProperty(JsonObject destination, string key, JsonObject previous, JsonObject current)
    {
        if (!current.ContainsKey(key)) { destination.Remove(key); return; }
        if (previous[key] is JsonObject before && current[key] is JsonObject after)
        {
            var target = destination[key] as JsonObject;
            if (target == null) { target = new JsonObject(); destination[key] = target; }
            foreach (var child in before.Select(p => p.Key).Union(after.Select(p => p.Key)))
                if (before.ContainsKey(child) != after.ContainsKey(child) || !JsonNode.DeepEquals(before[child], after[child]))
                    MergeProperty(target, child, before, after);
        }
        else destination[key] = current[key]?.DeepClone();
    }
}
