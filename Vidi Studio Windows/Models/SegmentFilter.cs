using System;
using System.Collections.Generic;
using System.Linq;

namespace Vidi_Studio_Windows.Models;

/// <summary>Filters timed rows by provenance and, for descriptions, saved audio.</summary>
public sealed class SegmentFilter
{
    public HashSet<string> Origins { get; } = new();
    public string Audio { get; set; } = "all";
    public bool IsActive => Origins.Count > 0 || Audio != "all";

    public bool Matches(Segment segment, bool hasAudio)
    {
        if (Origins.Count > 0 && !Origins.Contains(segment.Origin)) return false;
        if (!segment.IsDescription || Audio == "all") return true;
        return Audio == "withAudio" ? hasAudio : !hasAudio;
    }

    public void Clear()
    {
        Origins.Clear();
        Audio = "all";
    }
}
