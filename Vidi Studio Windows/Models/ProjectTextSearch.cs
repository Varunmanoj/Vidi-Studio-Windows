using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Vidi_Studio_Windows.Models;

public static class SearchResultStatus
{
    public static string SectionName(string section) => section switch { "transcript" => "Transcript", "summary" => "Summary", "descriptions" => "Descriptions", "ad-transcript" => "AD Transcript", "ad-overview" => "AD Overview", _ => section };
    public static string Editor(ProjectSearchQuery query, string section, int count)
    {
        var title = SectionName(section);
        if (query.Term.Length == 0) return query.Section == null ? $"Search cleared. All {title} content shown." : $"{SectionName(query.Section)}. All content shown.";
        if (query.Section is { } scope && scope != section) return $"{title}. Search is limited to {SectionName(scope)}.";
        if (section == "ad-overview") return "AD Overview. Search is available in the four text tabs.";
        return count == 0 ? $"No search results in {title}." : $"{count} search {(count == 1 ? "result" : "results")} in {title}.";
    }
    public static string Gallery(string query, int count)
    {
        var text = count == 0 ? "No projects found." : $"{count} {(count == 1 ? "project" : "projects")} shown.";
        return string.IsNullOrWhiteSpace(query) ? "Search cleared. " + text : text;
    }
}

public sealed record ProjectSearchQuery(string? Section, string Term)
{
    public static ProjectSearchQuery Parse(string text)
    {
        var trimmed = text.Trim();
        var prefix = Regex.Match(trimmed, @"^(tr|sum|des|adt)(?:\s*:\s*|$)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        if (!prefix.Success) return new(null, trimmed);
        var section = prefix.Groups[1].Value.ToLowerInvariant() switch { "tr" => "transcript", "sum" => "summary", "des" => "descriptions", _ => "ad-transcript" };
        return new(section, trimmed[prefix.Length..].Trim());
    }
    public bool Applies(string section) => Term.Length > 0 && section != "ad-overview" && (Section == null || Section == section);
    public bool Matches(string text) => text.Normalize(NormalizationForm.FormC).Contains(Term.Normalize(NormalizationForm.FormC), StringComparison.OrdinalIgnoreCase);

    public IReadOnlyList<(int Start, int Length)> MatchingRanges(string text)
    {
        var ranges = new List<(int, int)>();
        if (Term.Length == 0 || text.Length == 0) return ranges;
        // Match in NFC like filtering, then map back to the original UTF-16 text
        // so combining marks and supplementary characters retain correct offsets.
        var normalized = new StringBuilder();
        var starts = new List<int>(); var ends = new List<int>();
        var elements = StringInfo.GetTextElementEnumerator(text);
        while (elements.MoveNext())
        {
            var element = elements.GetTextElement();
            var value = element.Normalize(NormalizationForm.FormC);
            normalized.Append(value);
            for (var index = 0; index < value.Length; index++)
            {
                starts.Add(elements.ElementIndex);
                ends.Add(elements.ElementIndex + element.Length);
            }
        }
        var source = normalized.ToString(); var term = Term.Normalize(NormalizationForm.FormC);
        for (var offset = 0; offset < source.Length;)
        {
            var match = source.IndexOf(term, offset, StringComparison.OrdinalIgnoreCase);
            if (match < 0) break;
            var start = starts[match]; var end = ends[match + term.Length - 1];
            if (ranges.Count == 0 || start >= ranges[^1].Item1 + ranges[^1].Item2) ranges.Add((start, end - start));
            offset = match + term.Length;
        }
        return ranges;
    }
}

public sealed record ProjectSearchHit(string Id, string ResultID, string Kind, string Section, string Text,
    string? SegmentID, bool IsDescription, double? Start, double? End, bool ImportedCombined);

public sealed record ProjectSearchGroup(LanguageResult Result, string Section, bool Selected, IReadOnlyList<ProjectSearchHit> Hits)
{
    public string Id => Section + ":" + Result.Id;
    public string Title => (Result.Kind == "descriptions" ? "Descriptions" : Result.Kind == "transcript" ? "Transcript" : "Summary") + " · " + Result.Title +
        (Result.State == "draft" ? " · Draft" : " · Saved") + (Selected ? " · Selected" : "");
}

/// <summary>Revision-cached, local projections; querying never selects or accepts a result.</summary>
public sealed class ProjectTextSearch
{
    private string revision = "";
    private IReadOnlyList<ProjectSearchGroup> documents = Array.Empty<ProjectSearchGroup>();
    public string ProjectID { get; private set; } = "";
    public bool Update(StudioProject project)
    {
        var key = project.Id + ":" + project.SelectedLanguage + ":" + (project.Extra != null && project.Extra.TryGetValue("updatedAt", out var updated) ? updated.GetRawText() : string.Join(";", project.SavedResults.Select(r => r.Id + r.UpdatedAt)));
        if (key == revision) return false;
        revision = key; ProjectID = project.Id; documents = Index(project); return true;
    }
    public IReadOnlyList<ProjectSearchGroup> Search(ProjectSearchQuery query, string section) => !query.Applies(section)
        ? Array.Empty<ProjectSearchGroup>()
        : documents.Where(g => g.Section == section).Select(g => g with { Hits = g.Hits.Where(h => query.Matches(h.Text)).ToList() }).Where(g => g.Hits.Count > 0).ToList();

    public static IReadOnlyList<ProjectSearchGroup> Index(StudioProject project)
    {
        var groups = new List<ProjectSearchGroup>();
        foreach (var result in project.SavedResults.Where(r => r.Language == project.SelectedLanguage && r.State is "saved" or "draft"))
        {
            var data = result.Payload;
            if (data.ValueKind != JsonValueKind.Object) continue;
            List<Segment> Rows(string key) => data.TryGetProperty(key, out var rows) && rows.ValueKind == JsonValueKind.Array
                ? JsonSerializer.Deserialize<List<Segment>>(rows.GetRawText(), Json.Options) ?? new() : new();
            void Append(string section, IEnumerable<Segment> rows, bool description, bool combined = false)
            {
                var hits = rows.Select((row, index) => new ProjectSearchHit($"{section}:{result.Id}:{(combined ? "combined" : "segment")}:{row.Id}:{index}", result.Id, result.Kind, section, row.Text, row.Id, description, row.Start, row.End, combined))
                    .Where(h => !string.IsNullOrWhiteSpace(h.Text)).ToList();
                if (hits.Count > 0) groups.Add(new(result, section, project.ActiveResultID(result.Kind) == result.Id, hits));
            }
            if (result.Kind == "transcript") { var rows = Rows("transcript"); Append("transcript", rows, false); Append("ad-transcript", rows, false); }
            else if (result.Kind == "descriptions")
            {
                var cues = Rows("cues");
                var imported = data.TryGetProperty("importedADTranscript", out var value) && value.ValueKind == JsonValueKind.Object
                    ? JsonSerializer.Deserialize<ImportedADTranscript>(value.GetRawText(), Json.Options) : null;
                var descriptions = cues.Count == 0 && imported?.Mode != "combined" ? imported?.Segments ?? new() : cues;
                Append("descriptions", descriptions, true);
                var combined = imported?.Mode == "combined";
                Append("ad-transcript", combined ? imported!.Segments : descriptions, true, combined);
            }
            else if (result.Kind == "summary" && data.TryGetProperty("summary", out var summary) && summary.ValueKind == JsonValueKind.String)
            {
                var hits = Sentences(summary.GetString() ?? "").Select((sentence, index) => new ProjectSearchHit($"summary:{result.Id}:{index}", result.Id, result.Kind, "summary", sentence, null, false, null, null, false)).ToList();
                if (hits.Count > 0) groups.Add(new(result, "summary", project.ActiveResultID("summary") == result.Id, hits));
            }
        }
        return groups.OrderByDescending(g => g.Selected).ThenByDescending(g => g.Result.UpdatedAt).ThenBy(g => g.Id, StringComparer.Ordinal).ToList();
    }

    public static IReadOnlyList<string> Sentences(string markdown)
    {
        // Display sentences have no speech-provider chunk limit. Match rendered text,
        // preserving complete sentences across wrapped Markdown paragraph lines.
        var blocks = new List<string>(); var paragraph = new List<string>();
        void Flush() { if (paragraph.Count > 0) { blocks.Add(string.Join(" ", paragraph)); paragraph.Clear(); } }
        foreach (var raw in markdown.Replace("\r\n", "\n").Split('\n'))
        {
            var line = raw.Trim();
            if (line.Length == 0 || line.StartsWith("```", StringComparison.Ordinal) || Regex.IsMatch(line, @"^[\s|:\-*_]+$")) { Flush(); continue; }
            var blockStart = Regex.IsMatch(line, @"^(#{1,6}\s|>\s?|[-*+]\s|\d+[.)]\s|\|)");
            if (blockStart) Flush();
            line = Regex.Replace(line, @"^(?:#{1,6}\s+|>\s?|[-*+]\s+(?:\[[ xX]\]\s+)?|\d+[.)]\s+)", "");
            line = Regex.Replace(line, @"!?\[([^\]]*)\]\([^)]*\)", "$1");
            line = Regex.Replace(line, @"[*_`~]", "");
            line = line.Trim('|', ' ');
            paragraph.Add(line);
            if (blockStart) Flush();
        }
        Flush();
        return blocks.SelectMany(block => Regex.Split(block, @"(?<=[.!?।॥。！？])\s+|(?<=[。！？])(?=\S)"))
            .Select(s => s.Trim()).Where(s => s.Any(char.IsLetterOrDigit)).ToList();
    }
}
