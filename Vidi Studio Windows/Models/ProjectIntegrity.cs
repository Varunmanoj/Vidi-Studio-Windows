using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace Vidi_Studio_Windows.Models;

/// <summary>Overall health of the files that make up a VidiStudio project.</summary>
public enum ProjectIntegrityLevel
{
    Healthy,
    Warning,
    Damaged
}

public enum ProjectFileCategory
{
    Manifest,
    Source,
    Metadata,
    Thumbnails,
    Narration,
    Exports,
    SignificantLoss
}

public sealed record ProjectMissingFile(string Path, string DisplayName);

public sealed class ProjectFileProblem
{
    public required ProjectFileCategory Category { get; init; }
    public required ProjectIntegrityLevel Severity { get; init; }
    public required string Title { get; init; }
    public required string Detail { get; init; }
    public required string Impact { get; init; }
    public string? Recovery { get; init; }
    public IReadOnlyList<ProjectMissingFile> MissingFiles { get; init; } = Array.Empty<ProjectMissingFile>();
    public int ExpectedCount { get; init; }

    public int MissingCount => MissingFiles.Count;
    public bool IsCritical => Severity == ProjectIntegrityLevel.Damaged;

    public string IconGlyph => Category switch
    {
        ProjectFileCategory.Manifest => "\uE8A5",
        ProjectFileCategory.Source => "\uE714",
        ProjectFileCategory.Metadata => "\uE946",
        ProjectFileCategory.Thumbnails => "\uEB9F",
        ProjectFileCategory.Narration => "\uE8D6",
        ProjectFileCategory.Exports => "\uE898",
        ProjectFileCategory.SignificantLoss => "\uE7BA",
        _ => "\uE7BA"
    };
}

public sealed record ProjectNarrationReference(string Path, int SegmentNumber, string TimeRange)
{
    public string DisplayName => $"{Path} (AD {SegmentNumber}{(string.IsNullOrEmpty(TimeRange) ? "" : $" · {TimeRange}")})";
}

public sealed class ProjectIntegrityManifest
{
    public string? Folder { get; init; }
    public required string ProjectName { get; init; }
    public bool IsYouTubeProject { get; init; }
    public string? SourceRelativePath { get; init; }
    public IReadOnlyList<string> ThumbnailPaths { get; init; } = Array.Empty<string>();
    public IReadOnlyList<ProjectNarrationReference> NarrationReferences { get; init; } = Array.Empty<ProjectNarrationReference>();
    public IReadOnlyList<string> ExportPaths { get; init; } = Array.Empty<string>();
    public bool KnownManifestUnreadable { get; init; }
}

public sealed class ProjectIntegrityReport
{
    public ProjectIntegrityLevel Level { get; init; }
    public IReadOnlyList<ProjectFileProblem> Problems { get; init; } = Array.Empty<ProjectFileProblem>();
    public int TotalTrackedFiles { get; init; }
    public int MissingTrackedFiles { get; init; }
    public bool ManifestUnreadable { get; init; }
    public bool FolderMissing { get; init; }
    public string ProjectName { get; init; } = "";
    public string? FolderPath { get; init; }

    public bool IsHealthy => Level == ProjectIntegrityLevel.Healthy;
    public bool IsDamaged => Level == ProjectIntegrityLevel.Damaged;
    public bool HasWarnings => Level == ProjectIntegrityLevel.Warning;
    public bool CanShowInFinder => !FolderMissing && !string.IsNullOrEmpty(FolderPath);
    public bool CanRegenerateNarration => !FolderMissing && Problems.Any(p => p.Category == ProjectFileCategory.Narration);
    public bool CanRegenerateVideoArtifacts =>
        !FolderMissing
        && Problems.All(p => p.Category != ProjectFileCategory.Source)
        && Problems.Any(p => p.Category is ProjectFileCategory.Metadata or ProjectFileCategory.Thumbnails);
    public bool CanReExport => !FolderMissing && Problems.Any(p => p.Category == ProjectFileCategory.Exports);
    public string BadgeText => IsDamaged ? "Damaged" : "Files Missing";

    public string Headline
    {
        get
        {
            if (IsHealthy) return "Project Files Are Healthy";
            if (FolderMissing) return "Project Folder Is Missing";
            if (ManifestUnreadable) return "Project Is Marked as Damaged";
            if (Problems.Any(p => p.Category == ProjectFileCategory.Source)) return "Original Video Is Missing";
            if (IsDamaged) return "Project Is Marked as Damaged";
            if (Problems.Count == 1)
            {
                return Problems[0].Category switch
                {
                    ProjectFileCategory.Narration => "Narration Audio Is Missing",
                    ProjectFileCategory.Thumbnails => "Timeline Pictures Are Missing",
                    ProjectFileCategory.Metadata => "Video Details Are Missing",
                    ProjectFileCategory.Exports => "Exports Are Missing",
                    ProjectFileCategory.Manifest => "Project File Is Missing",
                    ProjectFileCategory.Source => "Original Video Is Missing",
                    _ => "Project Is Marked as Damaged"
                };
            }
            return "Project Files Are Missing";
        }
    }

    public string Message
    {
        get
        {
            if (IsHealthy)
                return "All tracked project files are available.";
            if (FolderMissing)
                return "The project folder could not be found, so none of its files are available. Remove the project from VidiStudio or restore the folder from the Recycle Bin.";
            if (ManifestUnreadable)
                return "The project file (project.json) is missing, empty, or damaged. VidiStudio cannot load or save this project's transcript, summary, descriptions, or settings, so the project is marked as damaged. Restore it from a backup, or delete the project if no backup exists.";
            if (Problems.Any(p => p.Category == ProjectFileCategory.Source))
                return "The original video stored in this project is missing. Playback, video analysis, and video exports are unavailable, and the original video cannot be regenerated. Delete the project if the source is no longer available.";
            if (IsDamaged)
                return "Most of this project's files are missing. VidiStudio marked the project as damaged because too little of the original project remains. Regenerate what is available or delete the project if it cannot be recovered.";
            if (Problems.Count == 1)
            {
                var problem = Problems[0];
                switch (problem.Category)
                {
                    case ProjectFileCategory.Narration:
                        return $"Narration audio is missing for {problem.MissingCount} AD segment{(problem.MissingCount == 1 ? "" : "s")}. The description text and timing are safe, and the missing audio can be regenerated.";
                    case ProjectFileCategory.Thumbnails:
                        return "Timeline pictures are missing. The project can still be edited, and the pictures can be regenerated from the original video.";
                    case ProjectFileCategory.Metadata:
                        return "Video details are missing. Playback and editing still work, and the details can be regenerated from the original video.";
                    case ProjectFileCategory.Exports:
                        return $"{problem.MissingCount} exported file{(problem.MissingCount == 1 ? "" : "s")} listed in this project are missing. Export them again to recreate the files.";
                }
            }
            return "Some generated project files are missing. Your transcript, summary, descriptions, and project settings are stored in project.json and are still available. The missing files can be regenerated or re-exported.";
        }
    }

    public string AccessibilityLabel =>
        $"{Headline}. {Message} {MissingTrackedFiles} of {TotalTrackedFiles} tracked files are missing.";

    public string ShortDescription => $"{Headline}. {MissingTrackedFiles} of {TotalTrackedFiles} tracked files are missing.";

    public static ProjectIntegrityReport Healthy(string projectName, string? folderPath) => new()
    {
        Level = ProjectIntegrityLevel.Healthy,
        ProjectName = projectName,
        FolderPath = folderPath
    };
}

public static class ProjectIntegrityAuditor
{
    private enum FileState
    {
        Ok,
        Missing,
        Empty,
        Unreadable,
        NotRegularFile,
        InvalidJson
    }

    public static ProjectIntegrityReport Audit(ProjectIntegrityManifest manifest)
    {
        if (string.IsNullOrWhiteSpace(manifest.Folder))
            return FolderMissingReport(manifest, null);

        var folder = manifest.Folder!;
        if (!Directory.Exists(folder))
            return FolderMissingReport(manifest, folder);

        var problems = new List<ProjectFileProblem>();
        var trackedFiles = 0;
        var missingFiles = 0;
        var manifestUnreadable = false;

        trackedFiles++;
        var manifestState = manifest.KnownManifestUnreadable ? FileState.InvalidJson : ManifestState(folder);
        if (manifestState != FileState.Ok)
        {
            manifestUnreadable = true;
            missingFiles++;
            problems.Add(new ProjectFileProblem
            {
                Category = ProjectFileCategory.Manifest,
                Severity = ProjectIntegrityLevel.Damaged,
                Title = "Project file is missing or damaged",
                Detail = $"project.json is {Describe(manifestState)}.",
                Impact = "VidiStudio cannot open, save, or recover the project's transcript, summary, descriptions, and settings.",
                Recovery = "Restore project.json from a backup, or delete the project if no backup exists.",
                MissingFiles = new[] { new ProjectMissingFile("project.json", "project.json") },
                ExpectedCount = 1
            });
        }

        if (!manifest.IsYouTubeProject)
        {
            trackedFiles++;
            var source = manifest.SourceRelativePath?.Trim() ?? "";
            if (string.IsNullOrEmpty(source))
            {
                missingFiles++;
                problems.Add(SourceProblem(
                    "Original video reference is missing",
                    "The project does not contain a valid relative path to its original video.",
                    new ProjectMissingFile("source", "source/ (unknown video file)")));
            }
            else if (Resolve(source, folder) is not { } sourcePath)
            {
                missingFiles++;
                problems.Add(SourceProblem(
                    "Original video path is invalid",
                    "The saved video path is outside the project folder.",
                    new ProjectMissingFile(source, source)));
            }
            else
            {
                var state = FileStateOf(sourcePath, allowDirectory: false);
                if (state != FileState.Ok)
                {
                    missingFiles++;
                    problems.Add(SourceProblem(
                        "Original video is missing",
                        $"{source} is {Describe(state)}.",
                        new ProjectMissingFile(source, source)));
                }
            }
        }

        trackedFiles++;
        const string probePath = "metadata/probe.json";
        var probeState = FileStateOf(Path.Combine(folder, "metadata", "probe.json"), allowDirectory: false);
        if (probeState != FileState.Ok)
        {
            missingFiles++;
            problems.Add(new ProjectFileProblem
            {
                Category = ProjectFileCategory.Metadata,
                Severity = ProjectIntegrityLevel.Warning,
                Title = "Video details are missing",
                Detail = $"{probePath} is {Describe(probeState)}.",
                Impact = "Project information and the timeline may be incomplete until the video details are regenerated.",
                Recovery = "Use Analysis → Generate Video Details & Timeline Pictures to recreate this file.",
                MissingFiles = new[] { new ProjectMissingFile(probePath, probePath) },
                ExpectedCount = 1
            });
        }

        var thumbnailPaths = UniquePaths(manifest.ThumbnailPaths);
        var missingThumbnails = thumbnailPaths
            .Where(path => Resolve(path, folder) is not { } url || FileStateOf(url, allowDirectory: false) != FileState.Ok)
            .Select(path => new ProjectMissingFile(path, path))
            .ToList();
        trackedFiles += thumbnailPaths.Count;
        if (missingThumbnails.Count > 0)
        {
            missingFiles += missingThumbnails.Count;
            problems.Add(new ProjectFileProblem
            {
                Category = ProjectFileCategory.Thumbnails,
                Severity = ProjectIntegrityLevel.Warning,
                Title = "Timeline pictures are missing",
                Detail = $"{missingThumbnails.Count} of {thumbnailPaths.Count} timeline picture{(thumbnailPaths.Count == 1 ? "" : "s")} {(missingThumbnails.Count == 1 ? "is" : "are")} missing or empty.",
                Impact = "The project browser preview and timeline pictures may be incomplete.",
                Recovery = "Use Analysis → Generate Video Details & Timeline Pictures to recreate them.",
                MissingFiles = missingThumbnails,
                ExpectedCount = thumbnailPaths.Count
            });
        }

        var narrationReferences = UniqueNarration(manifest.NarrationReferences);
        var missingNarration = narrationReferences
            .Where(reference => Resolve(reference.Path, folder) is not { } url || FileStateOf(url, allowDirectory: false) != FileState.Ok)
            .Select(reference => new ProjectMissingFile(reference.Path, reference.DisplayName))
            .ToList();
        trackedFiles += narrationReferences.Count;
        if (missingNarration.Count > 0)
        {
            missingFiles += missingNarration.Count;
            problems.Add(new ProjectFileProblem
            {
                Category = ProjectFileCategory.Narration,
                Severity = ProjectIntegrityLevel.Warning,
                Title = "Narration audio is missing",
                Detail = $"{missingNarration.Count} of {narrationReferences.Count} narration audio file{(narrationReferences.Count == 1 ? "" : "s")} {(missingNarration.Count == 1 ? "is" : "are")} missing or empty.",
                Impact = "Affected AD segments will play without narration, and exports that need narration may fail until the audio is regenerated.",
                Recovery = "Use Generate Missing or Changed AD Audio to recreate the missing narration.",
                MissingFiles = missingNarration,
                ExpectedCount = narrationReferences.Count
            });
        }

        var exportPaths = UniquePaths(manifest.ExportPaths);
        var missingExports = exportPaths
            .Where(path => Resolve(path, folder) is not { } url || FileStateOf(url, allowDirectory: true) != FileState.Ok)
            .Select(path => new ProjectMissingFile(path, path))
            .ToList();
        trackedFiles += exportPaths.Count;
        if (missingExports.Count > 0)
        {
            missingFiles += missingExports.Count;
            problems.Add(new ProjectFileProblem
            {
                Category = ProjectFileCategory.Exports,
                Severity = ProjectIntegrityLevel.Warning,
                Title = "Exports are missing",
                Detail = $"{missingExports.Count} of {exportPaths.Count} exported file{(exportPaths.Count == 1 ? "" : "s")} {(missingExports.Count == 1 ? "is" : "are")} missing or empty.",
                Impact = "The project still lists these exports, but they cannot be opened or shared until they are recreated.",
                Recovery = "Use Export to recreate the missing files.",
                MissingFiles = missingExports,
                ExpectedCount = exportPaths.Count
            });
        }

        var affectedGeneratedCategories = problems
            .Where(p => p.Severity == ProjectIntegrityLevel.Warning && p.Category != ProjectFileCategory.Metadata)
            .Select(p => p.Category)
            .Distinct()
            .Count();
        var significantLoss = trackedFiles >= 4 && missingFiles >= 3
            && affectedGeneratedCategories >= 2
            && (double)missingFiles / trackedFiles >= 0.75
            && problems.All(p => p.Severity != ProjectIntegrityLevel.Damaged);
        if (significantLoss)
        {
            problems.Insert(0, new ProjectFileProblem
            {
                Category = ProjectFileCategory.SignificantLoss,
                Severity = ProjectIntegrityLevel.Damaged,
                Title = "Most project files are missing",
                Detail = $"{missingFiles} of {trackedFiles} tracked files are missing or empty.",
                Impact = "The project is marked as damaged because a significant portion of its files are gone. Regenerate the missing files or delete the project.",
                Recovery = "Regenerate available content where possible, or delete the project if the remaining files are not useful.",
                ExpectedCount = trackedFiles
            });
        }

        var level = problems.Any(p => p.Severity == ProjectIntegrityLevel.Damaged)
            ? ProjectIntegrityLevel.Damaged
            : problems.Count == 0 ? ProjectIntegrityLevel.Healthy : ProjectIntegrityLevel.Warning;

        return new ProjectIntegrityReport
        {
            Level = level,
            Problems = problems,
            TotalTrackedFiles = trackedFiles,
            MissingTrackedFiles = missingFiles,
            ManifestUnreadable = manifestUnreadable,
            FolderMissing = false,
            ProjectName = manifest.ProjectName,
            FolderPath = folder
        };
    }

    public static bool FolderLooksLikeProject(string folder)
    {
        var markers = new[] { "project.json", "source", "metadata", "thumbnails", "audio-description", "exports", ".engine.lock" };
        if (markers.Any(marker => File.Exists(Path.Combine(folder, marker)) || Directory.Exists(Path.Combine(folder, marker))))
            return true;
        var name = Path.GetFileName(folder.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
        if (name.Length <= 9) return false;
        var suffix = name[^9..];
        return suffix[0] == '-' && suffix[1..].All(IsHexDigit);
    }

    private static bool IsHexDigit(char value) =>
        value is >= '0' and <= '9' or >= 'a' and <= 'f' or >= 'A' and <= 'F';

    public static string? Resolve(string relative, string folder)
    {
        if (string.IsNullOrWhiteSpace(relative) || Path.IsPathRooted(relative)) return null;
        var root = Path.GetFullPath(folder).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
        var path = Path.GetFullPath(Path.Combine(root, relative));
        return path.StartsWith(root, StringComparison.OrdinalIgnoreCase) ? path : null;
    }

    private static ProjectFileProblem SourceProblem(string title, string detail, ProjectMissingFile missing) => new()
    {
        Category = ProjectFileCategory.Source,
        Severity = ProjectIntegrityLevel.Damaged,
        Title = title,
        Detail = detail,
        Impact = "Playback, video analysis, and video exports are unavailable, and the original video cannot be regenerated.",
        Recovery = "Restore the source video from the Recycle Bin or a backup. If it is gone, delete the project and import the video again.",
        MissingFiles = new[] { missing },
        ExpectedCount = 1
    };

    private static ProjectIntegrityReport FolderMissingReport(ProjectIntegrityManifest manifest, string? folderPath) => new()
    {
        Level = ProjectIntegrityLevel.Damaged,
        Problems = new[]
        {
            new ProjectFileProblem
            {
                Category = ProjectFileCategory.Manifest,
                Severity = ProjectIntegrityLevel.Damaged,
                Title = "Project folder is missing",
                Detail = folderPath == null ? "The project folder could not be found." : $"The folder {folderPath} could not be found.",
                Impact = "No project files are available, so the project cannot be opened, edited, or exported.",
                Recovery = "Restore the folder from the Recycle Bin, or remove the project from VidiStudio.",
                MissingFiles = folderPath == null ? Array.Empty<ProjectMissingFile>() : new[] { new ProjectMissingFile(folderPath, folderPath) },
                ExpectedCount = 1
            }
        },
        TotalTrackedFiles = 1,
        MissingTrackedFiles = 1,
        ManifestUnreadable = true,
        FolderMissing = true,
        ProjectName = manifest.ProjectName,
        FolderPath = folderPath
    };

    private static FileState ManifestState(string folder)
    {
        var path = Path.Combine(folder, "project.json");
        var state = FileStateOf(path, allowDirectory: false);
        if (state != FileState.Ok) return state;
        try
        {
            using var document = JsonDocument.Parse(File.ReadAllText(path));
            return FileState.Ok;
        }
        catch (JsonException)
        {
            return FileState.InvalidJson;
        }
        catch (IOException)
        {
            return FileState.Unreadable;
        }
        catch (UnauthorizedAccessException)
        {
            return FileState.Unreadable;
        }
    }

    private static FileState FileStateOf(string path, bool allowDirectory)
    {
        var isDirectory = Directory.Exists(path);
        if (!isDirectory && !File.Exists(path)) return FileState.Missing;
        if (isDirectory)
        {
            if (!allowDirectory) return FileState.NotRegularFile;
            try
            {
                return Directory.EnumerateFileSystemEntries(path).Any() ? FileState.Ok : FileState.Empty;
            }
            catch (Exception)
            {
                return FileState.Unreadable;
            }
        }
        try
        {
            var info = new FileInfo(path);
            if (info.Length == 0) return FileState.Empty;
            using var stream = File.Open(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            return FileState.Ok;
        }
        catch (UnauthorizedAccessException)
        {
            return FileState.Unreadable;
        }
        catch (IOException)
        {
            return FileState.Unreadable;
        }
    }

    private static string Describe(FileState state) => state switch
    {
        FileState.Ok => "available",
        FileState.Missing => "missing",
        FileState.Empty => "empty",
        FileState.Unreadable => "unreadable",
        FileState.NotRegularFile => "not a regular file",
        FileState.InvalidJson => "damaged or not valid JSON",
        _ => "unavailable"
    };

    private static List<string> UniquePaths(IEnumerable<string> paths)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var result = new List<string>();
        foreach (var path in paths)
        {
            var trimmed = path?.Trim() ?? "";
            if (trimmed.Length == 0 || !seen.Add(trimmed)) continue;
            result.Add(trimmed);
        }
        return result;
    }

    private static List<ProjectNarrationReference> UniqueNarration(IEnumerable<ProjectNarrationReference> references)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var result = new List<ProjectNarrationReference>();
        foreach (var reference in references)
        {
            var path = reference.Path.Trim();
            if (path.Length == 0 || !seen.Add(path)) continue;
            result.Add(new ProjectNarrationReference(path, reference.SegmentNumber, reference.TimeRange));
        }
        return result;
    }
}
