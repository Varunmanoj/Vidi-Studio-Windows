using System.Collections.Generic;

namespace Vidi_Studio_Windows.Services;

/// <summary>Announces each visible generation stage once per operation.</summary>
internal sealed class GenerationProcessingAnnouncement
{
    private readonly HashSet<string> announcedStages = new();

    internal string? Take(string? stage)
    {
        var title = stage switch {
            "transcript" => "Generating transcript",
            "summary" => "Generating summary",
            "descriptions" => "Generating descriptions",
            _ => null
        };
        if (stage == null || title == null || !announcedStages.Add(stage)) return null;
        return $"Processing. {title}.";
    }

    internal void Reset() => announcedStages.Clear();
}
