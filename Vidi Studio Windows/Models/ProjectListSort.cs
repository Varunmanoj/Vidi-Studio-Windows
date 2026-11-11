using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.Json.Nodes;

namespace Vidi_Studio_Windows.Models;

public static class ProjectListSort
{
    public static IEnumerable<StudioProject> Apply(IEnumerable<StudioProject> projects, string sort, JsonObject? history)
    {
        double LastOpened(StudioProject project)
        {
            if (history?[project.Folder] is JsonValue value && double.TryParse(value.ToJsonString(), NumberStyles.Float, CultureInfo.InvariantCulture, out var opened) && double.IsFinite(opened)) return opened;
            if (project.Extra?.TryGetValue("updatedAt", out var updated) == true && updated.TryGetDouble(out var date) && double.IsFinite(date)) return date;
            return 0;
        }
        IOrderedEnumerable<StudioProject> result = sort switch
        {
            "name" => projects.OrderBy(p => p.Name, StringComparer.CurrentCultureIgnoreCase),
            "duration" => projects.OrderByDescending(p => double.IsFinite(p.Duration) ? p.Duration : 0),
            "type" => projects.OrderBy(p => p.IsYouTube),
            _ => projects.OrderByDescending(LastOpened)
        };
        return result.ThenBy(p => p.Name, StringComparer.CurrentCultureIgnoreCase).ThenBy(p => p.Folder, StringComparer.OrdinalIgnoreCase);
    }
}
