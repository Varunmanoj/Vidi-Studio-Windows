using System;
using System.Collections.Generic;
using System.Linq;

namespace Vidi_Studio_Windows.Models;

public sealed class SegmentRangeFilter
{
    public string Mode { get; set; } = "all";
    public HashSet<string> SelectedIds { get; } = new();
    public List<NumberRange> NumberRanges { get; } = new();
    public List<TimeRange> TimeRanges { get; } = new();
    public bool Contained { get; set; }
    public bool IsActive => Mode != "all";
    public sealed class NumberRange { public bool IsDescription { get; set; } public double Start { get; set; } = 1; public double End { get; set; } = 1; }
    public sealed class TimeRange { public double Start { get; set; } public double End { get; set; } = 1; }
    public SegmentRangeFilter Clone()
    {
        var copy = new SegmentRangeFilter { Mode = Mode, Contained = Contained };
        copy.SelectedIds.UnionWith(SelectedIds);
        copy.NumberRanges.AddRange(NumberRanges.Select(r => new NumberRange { IsDescription = r.IsDescription, Start = r.Start, End = r.End }));
        copy.TimeRanges.AddRange(TimeRanges.Select(r => new TimeRange { Start = r.Start, End = r.End }));
        return copy;
    }
    public string? Error(int transcriptCount, int descriptionCount, double duration, HashSet<string> availableIds)
    {
        switch (Mode)
        {
            case "all": return null;
            case "selection": return SelectedIds.Count > 0 && SelectedIds.IsSubsetOf(availableIds) ? null : "Select at least one available segment.";
            case "numberRanges":
                if (NumberRanges.Count == 0) return "Add a segment number range.";
                foreach (var range in NumberRanges)
                {
                    var count = range.IsDescription ? descriptionCount : transcriptCount;
                    if (count == 0) return $"No {(range.IsDescription ? "description" : "transcript")} segments are available.";
                    if (!double.IsFinite(range.Start) || !double.IsFinite(range.End) || range.Start != Math.Truncate(range.Start) || range.End != Math.Truncate(range.End) ||
                        range.Start < 1 || range.End > count || range.Start > range.End)
                        return $"Enter whole segment numbers from 1 to {count}, with End at or after Start.";
                }
                return null;
            case "timeRanges":
                if (TimeRanges.Count == 0) return "Add a time range.";
                foreach (var range in TimeRanges)
                {
                    if (!double.IsFinite(range.Start) || !double.IsFinite(range.End) || range.Start < 0 || range.End <= range.Start)
                        return "Each End time must be after its Start time.";
                    if (double.IsFinite(duration) && duration > 0 && range.End > duration) return "Time ranges must stay within the video duration.";
                }
                return null;
            default: return "Choose a filter type.";
        }
    }
    public bool Matches(string id, int number, bool isDescription, double start, double end) => Mode switch
    {
        "all" => true,
        "selection" => SelectedIds.Contains(id),
        "numberRanges" => NumberRanges.Any(range => range.IsDescription == isDescription && number >= range.Start && number <= range.End),
        "timeRanges" => double.IsFinite(start) && double.IsFinite(end) && end > start && TimeRanges.Any(range =>
            Contained ? start >= range.Start && end <= range.End : start < range.End && end > range.Start),
        _ => false
    };
}
