using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

namespace Vidi_Studio_Windows.Models;

/// <summary>Shared timing inspection for TXT, SRT, and VTT imports.</summary>
public static class ImportTimingInspector
{
    public sealed record Row(int SourceLine, string Text, double? Start, double? End);

    public static List<Row> ReadRows(string path)
    {
        var text = File.ReadAllText(path).Replace("\r\n", "\n").Replace('\r', '\n');
        var extension = Path.GetExtension(path).ToLowerInvariant();
        if (extension == ".txt")
            return text.Split('\n').Select((line, index) => (line, index))
                .Where(item => !string.IsNullOrWhiteSpace(item.line))
                .Select(item => new Row(item.index + 1, item.line.Trim(), null, null)).ToList();
        var lines = text.Split('\n');
        var rows = new List<Row>();
        var index = 0;
        while (index < lines.Length)
        {
            while (index < lines.Length && string.IsNullOrWhiteSpace(lines[index])) index++;
            if (index >= lines.Length) break;
            var blockStart = index;
            var block = new List<string>();
            while (index < lines.Length && !string.IsNullOrWhiteSpace(lines[index])) block.Add(lines[index++]);
            if (block.Count == 0) continue;
            var first = block[0].Trim().ToUpperInvariant();
            if (first.StartsWith("WEBVTT") || first.StartsWith("NOTE") || first.StartsWith("STYLE") || first.StartsWith("REGION")) continue;
            var timingIndex = block.FindIndex(line => line.Contains("-->"));
            if (timingIndex < 0) continue;
            var pieces = block[timingIndex].Split("-->");
            if (pieces.Length != 2) continue;
            var endPart = pieces[1].Trim().Split(' ')[0];
            if (!TryParse(pieces[0], out var start) || !TryParse(endPart, out var end)) continue;
            var body = string.Join("\n", block.Skip(timingIndex + 1)).Trim();
            if (body.Length == 0) continue;
            rows.Add(new Row(blockStart + timingIndex + 2, body, start, end));
        }
        return rows;
    }

    public static List<string> Warnings(string path, double duration, StudioProject project, bool description)
    {
        var rows = ReadRows(path);
        var warnings = new List<string>();
        if (rows.Count == 0) return new List<string> { "No timed rows were found in this file." };
        double? previousEnd = null;
        for (var index = 0; index < rows.Count; index++)
        {
            var row = rows[index];
            var label = description ? $"AD row {index + 1}" : $"Transcript row {index + 1}";
            if (row.Start is not { } start || row.End is not { } end)
            {
                warnings.Add($"{label} has no start or end time.");
                continue;
            }
            if (end <= start) warnings.Add($"{label} ends before it starts.");
            else if (end > duration + .001) warnings.Add($"{label} ends after the video and will be omitted.");
            else if (start >= duration) warnings.Add($"{label} starts after the video and will be omitted.");
            if (previousEnd is { } prior && start < prior - .001) warnings.Add($"{label} overlaps another row in this file.");
            if (description)
            {
                var transcript = project.Transcript.FirstOrDefault(item => start < item.End && end > item.Start);
                if (transcript != null) warnings.Add($"{label} overlaps transcript and will be flagged for retiming.");
                var cue = project.Cues.FirstOrDefault(item => start < item.End && end > item.Start);
                if (cue != null) warnings.Add($"{label} overlaps an existing description and will be flagged for retiming.");
            }
            previousEnd = Math.Max(previousEnd ?? 0, end);
            if (warnings.Count >= 8) break;
        }
        return warnings.Distinct().Take(8).ToList();
    }

    public static bool TryParse(string raw, out double seconds)
    {
        seconds = 0;
        var value = raw.Trim().Replace(',', '.');
        var parts = value.Split(':');
        if (parts.Length is < 2 or > 3) return false;
        for (var index = 0; index < parts.Length; index++)
        {
            if (!double.TryParse(parts[index], NumberStyles.Float, CultureInfo.InvariantCulture, out var part) ||
                part < 0 || (index > 0 && part >= 60)) return false;
            seconds = seconds * 60 + part;
        }
        return double.IsFinite(seconds);
    }
}
