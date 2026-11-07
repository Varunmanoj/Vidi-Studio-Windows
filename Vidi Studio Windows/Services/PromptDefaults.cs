using System.Text.Json.Nodes;

namespace Vidi_Studio_Windows.Services;

public static class PromptDefaults
{
    /// <summary>Advance exact former built-in fields without overwriting custom fields.</summary>
    public static void Upgrade(JsonObject saved, JsonObject defaults)
    {
        if (saved["prompts"] is not JsonObject prompts || defaults["prompts"] is not JsonObject current
            || defaults["legacyPromptDefaults"] is not JsonObject legacy) return;
        foreach (var stage in legacy)
        {
            if (stage.Value is not JsonObject fields || prompts[stage.Key] is not JsonObject pair
                || current[stage.Key] is not JsonObject latest) continue;
            foreach (var field in fields)
            {
                if (field.Value is not JsonArray previous || pair[field.Key] is not JsonValue value
                    || !value.TryGetValue<string>(out var text) || latest[field.Key] is not JsonValue replacement) continue;
                foreach (var old in previous)
                    if (old is JsonValue oldValue && oldValue.TryGetValue<string>(out var oldText) && oldText == text)
                    {
                        pair[field.Key] = replacement.DeepClone();
                        break;
                    }
            }
        }
    }
}
