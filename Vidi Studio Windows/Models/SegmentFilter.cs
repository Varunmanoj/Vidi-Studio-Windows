using System;
using System.Collections.Generic;
using System.Linq;

namespace Vidi_Studio_Windows.Models;

/// <summary>Filters timed rows by provenance and, for descriptions, saved audio.</summary>
public sealed class SegmentFilter
{
    public HashSet<string> Origins { get; } = new();
    public string Audio { get; set; } = "all";
    public SegmentRangeFilter Ranges { get; set; } = new();
    public bool IsActive => Origins.Count > 0 || Audio != "all" || Ranges.IsActive;

    public bool Matches(Segment segment, bool hasAudio)
    {
        if (!Ranges.Matches((segment.IsDescription ? "ad:" : "transcript:") + segment.Id, segment.SegmentNumber, segment.IsDescription, segment.Start, segment.End)) return false;
        if (Origins.Count > 0 && !Origins.Contains(segment.Origin)) return false;
        if (!segment.IsDescription || Audio == "all") return true;
        return Audio == "withAudio" ? hasAudio : !hasAudio;
    }

    public SegmentFilter Clone()
    {
        var copy = new SegmentFilter { Audio = Audio, Ranges = Ranges.Clone() };
        copy.Origins.UnionWith(Origins); return copy;
    }

    public void Clear()
    {
        Origins.Clear();
        Audio = "all";
        Ranges = new();
    }
}
