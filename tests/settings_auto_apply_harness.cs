using System;
using System.IO;
using System.Text.Json.Nodes;
using Vidi_Studio_Windows.Services;

static class SettingsAutoApplyHarness
{
    static void Check(bool condition, string message)
    { if (!condition) throw new Exception(message); }

    static void Main()
    {
        var initial = JsonNode.Parse("""{"theme":"system","models":{"summary":"one","transcript":"two"},"prompts":{"summary":"original"},"obsolete":true,"nullable":null,"apiKey":"original-key"}""")!.AsObject();
        var draft = initial.DeepClone().AsObject();
        var stored = initial.DeepClone().AsObject();
        stored.Remove("apiKey");
        var tracker = new SettingsAutoApply(initial);
        var writes = 0;
        string? credentialEdit = null;
        void Persist(JsonObject payload, string? key, System.Collections.Generic.IReadOnlyCollection<string> changed)
        {
            Check(!payload.ContainsKey("apiKey") && !payload.ContainsKey("azureSpeechKey"), "Credential leaked into JSON.");
            writes++;
            credentialEdit = key;
            stored = payload;
        }
        Check(!tracker.Apply(draft, () => throw new Exception("Unchanged settings reloaded."), Persist), "Opening settings saved unnecessarily.");

        // Simultaneous settings from other windows and nested engine model edits survive.
        stored["projectLocations"] = new JsonArray("other-project");
        stored["models"]!["transcript"] = "external-model";
        draft["models"]!["summary"] = "new-summary";
        draft["prompts"]!["summary"] = "new-prompt";
        draft["theme"] = "dark";
        draft.Remove("obsolete");
        draft.Remove("nullable");
        Check(tracker.Apply(draft, () => stored, Persist), "Edits did not apply.");
        Check(stored["models"]!["transcript"]!.GetValue<string>() == "external-model", "Unrelated nested model was overwritten.");
        Check(stored["models"]!["summary"]!.GetValue<string>() == "new-summary", "Model edit lost.");
        Check(stored["prompts"]!["summary"]!.GetValue<string>() == "new-prompt", "Prompt edit lost.");
        Check(stored["projectLocations"]![0]!.GetValue<string>() == "other-project", "Other window's setting lost.");
        Check(!stored.ContainsKey("obsolete") && !stored.ContainsKey("nullable"), "Removed settings returned.");
        Check(credentialEdit == null && !tracker.HasChanges(draft), "Unchanged credential was written or baseline stale.");

        draft["apiKey"] = "new-key";
        tracker.Apply(draft, () => stored, Persist);
        Check(credentialEdit == "new-key", "Key edit did not reach credential storage.");
        draft["apiKey"] = "";
        draft["apiPlan"] = "none";
        tracker.Apply(draft, () => stored, Persist);
        Check(credentialEdit == "" && stored["apiPlan"]!.GetValue<string>() == "none", "Key deletion was not persisted.");

        draft["captionFontSize"] = 32;
        var failed = false;
        try { tracker.Apply(draft, () => stored, (_, _, _) => throw new IOException("write failed")); }
        catch (IOException) { failed = true; }
        Check(failed && tracker.HasChanges(draft), "Failed settings were accepted instead of kept for retry.");
        tracker.Apply(draft, () => stored, Persist);
        Check(!tracker.HasChanges(draft) && stored["captionFontSize"]!.GetValue<int>() == 32, "Retry or immediate close flush lost the final edit.");
        var reopened = new SettingsAutoApply(stored);
        Check(!reopened.HasChanges(stored), "Reopening lost the saved baseline.");
        Check(writes == 4, "Duplicate settings writes.");
        Console.WriteLine("PASS: automatic settings merge, nested edits, removal, credentials, retry, close flush and reopen");
    }
}
