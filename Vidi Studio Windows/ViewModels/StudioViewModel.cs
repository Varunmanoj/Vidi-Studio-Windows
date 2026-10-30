using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using Vidi_Studio_Windows.Models;
using Vidi_Studio_Windows.Services;

namespace Vidi_Studio_Windows.ViewModels;

/// <summary>Per-section processing state for the Transcript, Summary, and Audio Description sections.</summary>
public enum SectionProcessingState
{
    Idle,
    Queued,
    Processing
}

public sealed class StudioViewModel : INotifyPropertyChanged
{
    public SettingsStore Settings { get; } = new();
    public EngineClient Engine { get; }
    public ObservableCollection<StudioProject> Projects { get; } = new();
    public ObservableCollection<ExportSession> ExportSessions => ExportSession.Active;
    private readonly HashSet<string> externalFolders = new(StringComparer.OrdinalIgnoreCase);
    // Projects are cached by folder so refreshed views, filtering, and the
    // integrity timer reuse the same instances instead of rebuilding every
    // card. Rebuilding the collection made the gallery flicker while idle.
    private readonly Dictionary<string, StudioProject> knownProjects = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, ProjectStamp> projectStamps = new(StringComparer.OrdinalIgnoreCase);
    private string appliedSearch = "";
    private string appliedSource = "all";
    private readonly record struct ProjectStamp(bool FolderExists, long ManifestTicks);
    private StudioProject? project;
    public StudioProject? Project { get => project; set { project = value; Changed(); } }
    private bool busy;
    public bool Busy { get => busy; private set { busy = value; Changed(); Changed(nameof(Idle)); Changed(nameof(IsAnalyzing)); Changed(nameof(ProcessingStageTitle)); } }
    public bool Idle => !Busy;
    public bool IsExporting { get; private set; }
    private string status = "Import a video or open an existing project to begin.";
    public string Status { get => status; private set { status = value; Changed(); } }
    /// <summary>Shows a message in the status bar, for example when live narration cannot speak.</summary>
    public void ReportStatus(string message) => Status = message;
    private string? processingSection;
    public string? ProcessingSection
    {
        get => processingSection;
        private set { processingSection = value; Changed(); Changed(nameof(ProcessingStageTitle)); }
    }
    private string? videoPreparation;
    public string? VideoPreparation
    {
        get => videoPreparation;
        private set { videoPreparation = value; Changed(); Changed(nameof(ProcessingStageTitle)); }
    }
    private readonly List<string> processingPlan = new();
    public IReadOnlyList<string> ProcessingPlan => processingPlan;
    /// <summary>True while an analysis stage that belongs to a section is running.</summary>
    public bool IsAnalyzing => Busy && !IsExporting && processingPlan.Count > 0;
    public bool IsSynthesizingNarration { get; private set; }
    public string? NarratingSegmentId { get; private set; }
    public int? NarratingSegmentIndex { get; private set; }
    public int NarratingTotalCount { get; private set; }
    private readonly HashSet<string> narratingQueueIds = new(StringComparer.Ordinal);
    public IReadOnlyCollection<string> NarratingQueueIds => narratingQueueIds;
    public string ActiveNarrationStatusText => NarratingSegmentIndex is { } idx && NarratingTotalCount > 0
        ? $"Generating audio for AD segment {idx} of {NarratingTotalCount}…"
        : "Generating narration audio…";
    /// <summary>Short, user-facing stage label for the status bar; the Files API upload step takes precedence.</summary>
    public string ProcessingStageTitle
    {
        get
        {
            if (videoPreparation == "uploading") return "Uploading video";
            if (videoPreparation == "processing") return "Preparing video";
            if (IsSynthesizingNarration) return ActiveNarrationStatusText;
            return processingSection switch
            {
                "transcript" => "Generating transcript",
                "summary" => "Generating summary",
                "descriptions" => "Generating audio descriptions",
                _ => processingPlan.Count > 0 ? "Processing" : "Processing project"
            };
        }
    }
    private void ApplyNarrationStateToProject()
    {
        if (Project is not { } p) return;
        for (var i = 0; i < p.Cues.Count; i++)
        {
            var cue = p.Cues[i];
            cue.AdNumber = i + 1;
            cue.IsNarrating = IsSynthesizingNarration && NarratingSegmentId == cue.Id;
            cue.IsQueuedForNarration = IsSynthesizingNarration && NarratingSegmentId != cue.Id && narratingQueueIds.Contains(cue.Id);
        }
    }
    public bool HasApiKey => Settings.HasApiKey;
    public bool IsApiKeyInvalid => Settings.IsApiKeyInvalid;
    public string? AiIssueTitle { get; private set; }
    public string? AiIssueMessage { get; private set; }
    public string? AiIssueKind { get; private set; }
    public bool AiProcessingBlocked => AiIssueMessage != null;
    public bool CanUseAI => HasApiKey && !IsApiKeyInvalid && !AiProcessingBlocked;
    public void ClearAiIssue()
    {
        if (AiIssueMessage == null) return;
        AiIssueTitle = null; AiIssueMessage = null; AiIssueKind = null;
        Changed(nameof(AiIssueTitle)); Changed(nameof(AiIssueMessage)); Changed(nameof(AiIssueKind));
        Changed(nameof(AiProcessingBlocked)); Changed(nameof(CanUseAI));
    }
    private void DetectAiIssue(string? detail)
    {
        if (AIServiceIssueDetector.Detect(detail) is not { } issue) return;
        AiIssueTitle = issue.Title; AiIssueMessage = issue.Message; AiIssueKind = issue.Kind;
        Changed(nameof(AiIssueTitle)); Changed(nameof(AiIssueMessage)); Changed(nameof(AiIssueKind));
        Changed(nameof(AiProcessingBlocked)); Changed(nameof(CanUseAI));
    }
    public string ApiPlan => Settings.ApiPlan;
    private CancellationTokenSource? operation;
    private NarrationFailureReport? pendingNarrationFailures;
    public event PropertyChangedEventHandler? PropertyChanged;
    public event EventHandler? ProjectReloaded;
    /// <summary>Raised after a narration run finished with one or more skipped descriptions.</summary>
    public event EventHandler<NarrationFailureReport>? NarrationFailures;
    public StudioViewModel() { Engine = new(Settings); LoadExternalFolders(); }
    private void LoadExternalFolders()
    {
        if (Settings.Data["projectLocations"] is not System.Text.Json.Nodes.JsonArray saved) return;
        foreach (var entry in saved)
        {
            var path = entry is System.Text.Json.Nodes.JsonValue value && value.TryGetValue<string>(out var text) ? text : null;
            if (string.IsNullOrWhiteSpace(path)) continue;
            try { externalFolders.Add(Path.GetFullPath(path)); } catch (ArgumentException) { }
        }
    }
    private void SaveExternalFolders()
    {
        Settings.Data["projectLocations"] = new System.Text.Json.Nodes.JsonArray(
            externalFolders.OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
                .Select(path => (System.Text.Json.Nodes.JsonNode?)System.Text.Json.Nodes.JsonValue.Create(path)).ToArray());
        Settings.Save();
    }
    private void Changed([CallerMemberName] string? name = null) => PropertyChanged?.Invoke(this, new(name));

    public void Reload(string search = "", string source = "all")
    {
        Settings.Reload();
        LoadExternalFolders();
        Changed(nameof(HasApiKey));
        Changed(nameof(IsApiKeyInvalid));
        Changed(nameof(ApiPlan));
        var library = Settings.Get("libraryPath");
        // Browsing must not create directories or fail startup when a drive is unavailable.
        var libraryFolders = new List<string>();
        try { if (Directory.Exists(library)) libraryFolders.AddRange(Directory.EnumerateDirectories(library)); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        { Status = "The project library is unavailable. Choose a project location in Settings → Storage."; }
        var folders = libraryFolders.Concat(externalFolders)
            .Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        var currentFolders = new HashSet<string>(folders, StringComparer.OrdinalIgnoreCase);
        foreach (var stale in knownProjects.Keys.Where(folder => !currentFolders.Contains(folder)).ToList())
        {
            knownProjects.Remove(stale);
            projectStamps.Remove(stale);
        }
        var loaded = new List<StudioProject>();
        foreach (var folder in folders)
        {
            var knownFolder = externalFolders.Contains(folder);
            var stamp = StampFor(folder);
            if (knownProjects.TryGetValue(folder, out var cached) &&
                projectStamps.TryGetValue(folder, out var cachedStamp) && cachedStamp == stamp)
            {
                // Nothing changed on disk: keep the existing instance so the
                // grid and list rows (and their thumbnails) are not recreated.
                if (cached.Integrity == null)
                    cached.Integrity = ProjectIntegrityAuditor.Audit(cached.ToIntegrityManifest());
                loaded.Add(cached);
                continue;
            }
            var exists = stamp.FolderExists;
            var project = StudioProject.TryLoad(folder);
            if (project == null && exists && ProjectIntegrityAuditor.FolderLooksLikeProject(folder))
                project = StudioProject.DamagedPlaceholder(folder);
            else if (project == null && !exists && knownFolder)
                project = StudioProject.DamagedPlaceholder(folder);
            if (project == null)
            {
                knownProjects.Remove(folder);
                projectStamps.Remove(folder);
                continue;
            }
            project.Integrity = ProjectIntegrityAuditor.Audit(project.ToIntegrityManifest());
            knownProjects[folder] = project;
            projectStamps[folder] = stamp;
            loaded.Add(project);
        }
        var desired = loaded.Where(p =>
                p.Name.Contains(search, StringComparison.CurrentCultureIgnoreCase)
                && (source == "all" || p.IsYouTube == (source == "youtube")))
            .ToList();
        ApplyProjectList(desired);
        appliedSearch = search;
        appliedSource = source;
    }

    /// <summary>
    /// Reloads the gallery only when a project folder or manifest changed on
    /// disk. The periodic integrity check calls this so an idle gallery is
    /// never cleared and rebuilt.
    /// </summary>
    public bool ReloadIfProjectsChanged(string search = "", string source = "all")
    {
        if (!string.Equals(search, appliedSearch, StringComparison.Ordinal) ||
            !string.Equals(source, appliedSource, StringComparison.Ordinal))
        {
            Reload(search, source);
            return true;
        }
        var library = Settings.Get("libraryPath");
        var folders = (Directory.Exists(library)
                ? Directory.EnumerateDirectories(library).Concat(externalFolders)
                : externalFolders)
            .Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        var current = new HashSet<string>(folders, StringComparer.OrdinalIgnoreCase);
        if (knownProjects.Keys.Any(folder => !current.Contains(folder)))
        {
            Reload(search, source);
            return true;
        }
        foreach (var folder in folders)
        {
            if (knownProjects.ContainsKey(folder))
            {
                if (!projectStamps.TryGetValue(folder, out var cached) || cached != StampFor(folder))
                {
                    Reload(search, source);
                    return true;
                }
            }
            else if (externalFolders.Contains(folder) || File.Exists(Path.Combine(folder, "project.json")))
            {
                Reload(search, source);
                return true;
            }
        }
        return false;
    }

    private static ProjectStamp StampFor(string folder)
    {
        try
        {
            var exists = Directory.Exists(folder);
            var manifest = Path.Combine(folder, "project.json");
            var ticks = exists && File.Exists(manifest) ? File.GetLastWriteTimeUtc(manifest).Ticks : 0;
            return new ProjectStamp(exists, ticks);
        }
        catch (Exception)
        {
            return new ProjectStamp(false, 0);
        }
    }

    private void ApplyProjectList(IReadOnlyList<StudioProject> desired)
    {
        var unchanged = desired.Count == Projects.Count;
        for (var i = 0; unchanged && i < desired.Count; i++)
            unchanged = ReferenceEquals(Projects[i], desired[i]);
        if (unchanged) return;

        var wanted = new HashSet<StudioProject>(desired, ProjectReferenceComparer.Instance);
        for (var i = Projects.Count - 1; i >= 0; i--)
            if (!wanted.Contains(Projects[i])) Projects.RemoveAt(i);
        for (var i = 0; i < desired.Count; i++)
        {
            if (i < Projects.Count && ReferenceEquals(Projects[i], desired[i])) continue;
            var existing = -1;
            for (var j = i + 1; j < Projects.Count; j++)
                if (ReferenceEquals(Projects[j], desired[i])) { existing = j; break; }
            if (existing >= 0) Projects.Move(existing, i);
            else Projects.Insert(i, desired[i]);
        }
        while (Projects.Count > desired.Count) Projects.RemoveAt(Projects.Count - 1);
    }

    private sealed class ProjectReferenceComparer : IEqualityComparer<StudioProject>
    {
        public static readonly ProjectReferenceComparer Instance = new();
        public bool Equals(StudioProject? x, StudioProject? y) => ReferenceEquals(x, y);
        public int GetHashCode(StudioProject obj) => System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(obj);
    }
    public void Open(string folder)
    {
        Settings.Reload(); LoadExternalFolders();
        if (externalFolders.Add(Path.GetFullPath(folder))) SaveExternalFolders();
        var loaded = StudioProject.TryLoad(folder);
        Project = loaded ?? StudioProject.DamagedPlaceholder(folder);
        Project.Integrity = ProjectIntegrityAuditor.Audit(Project.ToIntegrityManifest());
        if (Idle) Status = Project.Integrity.IsHealthy ? "Ready" : Project.Integrity.Headline;
        Reload();
        ProjectReloaded?.Invoke(this, EventArgs.Empty);
    }
    /// <summary>Removes a project entry whose folder no longer exists.</summary>
    public void ForgetProject(StudioProject project)
    {
        externalFolders.Remove(project.Folder);
        SaveExternalFolders();
        knownProjects.Remove(project.Folder);
        projectStamps.Remove(project.Folder);
        Projects.Remove(project);
        if (Project != null && string.Equals(Project.Folder, project.Folder, StringComparison.OrdinalIgnoreCase))
            Project = null;
    }
    public void RefreshProjectIntegrity(bool includeGallery = false)
    {
        if (Project is { } current)
            current.Integrity = ProjectIntegrityAuditor.Audit(current.ToIntegrityManifest());
        if (!includeGallery) return;
        foreach (var project in Projects)
            project.Integrity = ProjectIntegrityAuditor.Audit(project.ToIntegrityManifest());
    }
    public StudioProject? ExistingProjectForVideo(string filePath)
    {
        if (string.IsNullOrWhiteSpace(filePath)) return null;
        var fileName = Path.GetFileName(filePath);
        if (string.IsNullOrEmpty(fileName)) return null;
        return Projects.FirstOrDefault(p =>
            !p.IsYouTube &&
            string.Equals(Path.GetFileName(p.Source), fileName, StringComparison.OrdinalIgnoreCase));
    }
    public StudioProject? ExistingProjectForYouTube(string rawUrl)
    {
        if (string.IsNullOrWhiteSpace(rawUrl)) return null;
        var candidateId = ExtractYouTubeId(rawUrl);
        return Projects.FirstOrDefault(p =>
        {
            if (!p.IsYouTube || string.IsNullOrWhiteSpace(p.YoutubeURL)) return false;
            if (candidateId != null)
            {
                var existingId = ExtractYouTubeId(p.YoutubeURL);
                if (existingId != null) return string.Equals(candidateId, existingId, StringComparison.OrdinalIgnoreCase);
            }
            return string.Equals(p.YoutubeURL.Trim(), rawUrl.Trim(), StringComparison.OrdinalIgnoreCase);
        });
    }
    private static string? ExtractYouTubeId(string url)
    {
        try
        {
            if (!Uri.TryCreate(url.Trim(), UriKind.Absolute, out var uri)) return null;
            var host = uri.Host.ToLowerInvariant();
            if (host is "youtu.be" or "www.youtu.be")
            {
                var id = uri.AbsolutePath.TrimStart('/');
                var slash = id.IndexOf('/');
                if (slash >= 0) id = id.Substring(0, slash);
                return id.Length == 11 ? id : null;
            }
            if (host.Contains("youtube.com"))
            {
                if (uri.AbsolutePath == "/watch")
                {
                    foreach (var pair in uri.Query.TrimStart('?').Split('&'))
                    {
                        var kv = pair.Split('=');
                        if (kv.Length == 2 && kv[0] == "v" && kv[1].Length == 11)
                            return kv[1];
                    }
                }
                var parts = uri.AbsolutePath.Split('/', StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length > 1 && (parts[0] == "shorts" || parts[0] == "embed" || parts[0] == "live"))
                {
                    return parts[1].Length == 11 ? parts[1] : null;
                }
            }
        }
        catch { }
        return null;
    }
    public void Cancel() => operation?.Cancel();
    public string CurrentNarrationEngine()
    {
        var mode = Settings.Get("ttsMode", "windows-core").Trim();
        if (string.IsNullOrEmpty(mode) || mode == "system") return "windows-core";
        return mode;
    }
    public string CurrentNarrationVoice()
    {
        var engine = CurrentNarrationEngine();
        if (engine == "gemini")
        {
            var gv = Settings.Get("geminiVoice", "Kore").Trim();
            return string.IsNullOrEmpty(gv) ? "Kore" : gv;
        }
        if (engine == "piper")
        {
            var pv = Settings.Get("piperVoice", Settings.Get("systemVoice")).Trim();
            return string.IsNullOrEmpty(pv) ? "Piper voice" : pv;
        }
        var sv = Settings.Get("systemVoice", "").Trim();
        return string.IsNullOrEmpty(sv) ? "System default" : sv;
    }
    public string CurrentNarrationSummary() =>
        NarrationVoiceFormatting.CombinedLabel(CurrentNarrationVoice(), CurrentNarrationEngine());

    /// <summary>Builds a detailed report of narration voice and engine usage across segments.</summary>
    public NarrationVoiceReport? NarrationVoiceUsageReport()
    {
        if (Project is not { } p) return null;
        var narratedEntries = new List<(int Index, Segment Cue, string Engine, string Voice, string Path)>();
        var importedEntries = new List<NarrationVoiceSegmentEntry>();
        for (var i = 0; i < p.Cues.Count; i++)
        {
            var cue = p.Cues[i];
            if (!cue.HasAudioFile || string.IsNullOrEmpty(cue.Audio)) continue;
            if (cue.AudioOrigin == "imported")
            {
                var source = string.IsNullOrWhiteSpace(cue.AudioSourceName) ? "Unknown source file" : cue.AudioSourceName;
                importedEntries.Add(new NarrationVoiceSegmentEntry
                {
                    Index = i + 1, Start = cue.Start, End = cue.End,
                    Duration = Math.Max(0, cue.End - cue.Start),
                    Engine = "imported", EngineDisplayName = "Imported audio",
                    Voice = source, VoiceDisplayName = source, AudioFile = cue.Audio,
                    UsesDifferentVoice = false,
                    Reason = $"Imported audio from {source}. It is not generated narration and does not use a selected voice."
                });
                continue;
            }
            var engine = cue.ResolvedAudioEngine ?? "unknown";
            var voice = cue.ResolvedAudioVoice ?? "Unknown voice";
            narratedEntries.Add((i + 1, cue, engine, voice, cue.Audio));
        }
        if (narratedEntries.Count == 0)
        {
            return new NarrationVoiceReport
            {
                Title = importedEntries.Count > 0 ? "Imported narration audio" : "Narration voice details",
                Detail = importedEntries.Count > 0
                    ? "All narration audio in this project is imported. Generated voice settings do not apply to these segments."
                    : "No narration audio has been generated for this project yet.",
                HasDifferentVoices = false,
                AllSegments = importedEntries,
                ImportedCount = importedEntries.Count,
                HasMixedSources = importedEntries.Count > 0,
                ImportedFiles = importedEntries.Select(e => e.VoiceDisplayName).Distinct().OrderBy(v => v).ToList()
            };
        }
        string primaryEngine;
        string primaryVoice;
        if (!string.IsNullOrWhiteSpace(p.NarrationEngine) && !string.IsNullOrWhiteSpace(p.NarrationVoice))
        {
            primaryEngine = p.NarrationEngine.Trim();
            primaryVoice = p.NarrationVoice.Trim();
        }
        else
        {
            primaryEngine = narratedEntries[0].Engine;
            primaryVoice = narratedEntries[0].Voice;
        }
        var primaryLabel = NarrationVoiceFormatting.CombinedLabel(primaryVoice, primaryEngine);
        var allSegments = new List<NarrationVoiceSegmentEntry>();
        var differentSegments = new List<NarrationVoiceSegmentEntry>();
        var grouped = new List<(string Engine, string Voice, bool IsDifferent, List<int> Numbers)>();

        foreach (var item in narratedEntries)
        {
            var matchesPrimary = NarrationVoiceFormatting.VoicesMatch(item.Engine, item.Voice, primaryEngine, primaryVoice);
            var engineDisplay = NarrationVoiceFormatting.EngineDisplayName(item.Engine);
            var voiceDisplay = NarrationVoiceFormatting.VoiceDisplayName(item.Voice);
            var reason = matchesPrimary
                ? $"Uses primary voice {voiceDisplay} ({engineDisplay})"
                : $"Different voice: uses {voiceDisplay} ({engineDisplay}) instead of {primaryLabel}";
            var entry = new NarrationVoiceSegmentEntry
            {
                Index = item.Index,
                Start = item.Cue.Start,
                End = item.Cue.End,
                Duration = Math.Max(0, item.Cue.End - item.Cue.Start),
                Engine = item.Engine,
                EngineDisplayName = engineDisplay,
                Voice = item.Voice,
                VoiceDisplayName = voiceDisplay,
                AudioFile = item.Path,
                UsesDifferentVoice = !matchesPrimary,
                Reason = reason
            };
            allSegments.Add(entry);
            if (!matchesPrimary) differentSegments.Add(entry);
            var existingIdx = grouped.FindIndex(g => NarrationVoiceFormatting.VoicesMatch(g.Engine, g.Voice, item.Engine, item.Voice));
            if (existingIdx >= 0) grouped[existingIdx].Numbers.Add(item.Index);
            else grouped.Add((item.Engine, item.Voice, !matchesPrimary, new List<int> { item.Index }));
        }
        if (!grouped.Any(g => NarrationVoiceFormatting.VoicesMatch(g.Engine, g.Voice, primaryEngine, primaryVoice)))
            grouped.Insert(0, (primaryEngine, primaryVoice, false, new List<int>()));

        var summaries = grouped.Select(g => new NarrationVoiceSummary
        {
            Id = $"{g.Engine}|{g.Voice}",
            Engine = g.Engine,
            EngineDisplayName = NarrationVoiceFormatting.EngineDisplayName(g.Engine),
            Voice = g.Voice,
            VoiceDisplayName = NarrationVoiceFormatting.VoiceDisplayName(g.Voice),
            Count = g.Numbers.Count,
            SegmentNumbers = g.Numbers,
            IsDifferentVoice = g.IsDifferent
        }).ToList();

        allSegments.AddRange(importedEntries);
        var hasDifferent = differentSegments.Count > 0 || summaries.Count(s => s.Count > 0) > 1;
        var hasMixedSources = importedEntries.Count > 0 && narratedEntries.Count > 0;
        var generatedCount = narratedEntries.Count;
        var detail = hasDifferent
            ? $"{differentSegments.Count} of {generatedCount} generated segment{(generatedCount == 1 ? "" : "s")} use a different voice from {primaryLabel}. ({string.Join("; ", summaries.Select(s => $"{s.CombinedLabel}: {s.Count} segment{(s.Count == 1 ? "" : "s")}"))})"
            : $"All {generatedCount} generated audio description segment{(generatedCount == 1 ? "" : "s")} use {primaryLabel}.";
        if (importedEntries.Count > 0)
            detail += $" {importedEntries.Count} segment{(importedEntries.Count == 1 ? "" : "s")} use imported audio.";

        return new NarrationVoiceReport
        {
            Title = hasDifferent || hasMixedSources ? "Narration sources need review" : "Narration voice details",
            Detail = detail,
            PrimaryVoiceLabel = primaryLabel,
            HasDifferentVoices = hasDifferent,
            VoiceSummaries = summaries,
            DifferentSegments = differentSegments,
            AllSegments = allSegments,
            ImportedCount = importedEntries.Count,
            HasMixedSources = hasMixedSources,
            ImportedFiles = importedEntries.Select(e => e.VoiceDisplayName).Distinct().OrderBy(v => v).ToList()
        };
    }
    /// <summary>Builds the narration status report from the open project for on-demand review.</summary>
    public NarrationFailureReport? NarrationIssueReport()
    {
        if (Project is not { } p) return null;
        var report = new NarrationFailureReport { Title = "Narration status" };
        for (var index = 0; index < p.Cues.Count; index++)
        {
            var cue = p.Cues[index];
            var reasons = cue.HasAudioFile ? cue.NarrationUpdateReasons : new List<string>
            {
                !string.IsNullOrEmpty(cue.AudioError) ? cue.AudioError
                    : string.IsNullOrEmpty(cue.Audio) ? "Narration audio has not been generated." : "The generated narration audio file is missing or unavailable."
            };
            if (reasons.Count == 0) report.Generated++;
            else report.Failures.Add(new NarrationFailure { Index = index + 1, Start = cue.Start, End = cue.End, Duration = cue.End - cue.Start, Reason = string.Join("\n", reasons) });
        }
        var total = p.Cues.Count;
        report.Detail = total == 0 ? "There are no audio description segments in this project yet." : report.Failures.Count == 0
            ? $"All {total} audio description segments have current narration audio."
            : $"{report.Failures.Count} of {total} description{(total == 1 ? "" : "s")} need attention.";
        if (p.GeneratedNarrationCount > 0 && p.ImportedNarrationCount > 0)
            report.Warnings.Add($"This project mixes AI-generated narration ({p.GeneratedNarrationCount} segments) and imported narration ({p.ImportedNarrationCount} segments).");
        if (p.ImportedNarrationFiles.Count > 1)
            report.Warnings.Add($"Imported narration comes from {p.ImportedNarrationFiles.Count} different audio files: {string.Join(", ", p.ImportedNarrationFiles)}.");
        if (p.ImportedNarrationCount > 0 && p.GeneratedNarrationCount == 0 && p.UnknownNarrationCount == 0)
            report.Warnings.Add("All narration audio in this project is imported. Generated voice settings do not apply to these segments.");
        if (p.GeneratedNarrationCount > 0 && p.ImportedNarrationCount == 0 && p.UnknownNarrationCount == 0)
            report.Warnings.Add("This project uses only AI-generated narration.");
        if (p.UnknownNarrationCount > 0)
            report.Warnings.Add($"{p.UnknownNarrationCount} narration segment(s) have no recorded source and are labelled Unknown voice.");
        return report;
    }
    public async Task<List<EngineEvent>> RunAsync(string command, IEnumerable<string>? extra = null, bool needsProject = true)
    {
        if (Busy) return new();
        if (needsProject && Project == null) { Status = "Open a project first."; return new(); }
        IsExporting = command.StartsWith("export-", StringComparison.Ordinal) && Project != null;
        processingPlan.Clear();
        processingPlan.AddRange(AnalysisPlan(command, extra));
        ProcessingSection = InitialProcessingSection(command);
        VideoPreparation = null;
        if (command == "synthesize-tts" && Project is { } synthProject)
        {
            IsSynthesizingNarration = true;
            NarratingTotalCount = synthProject.Cues.Count;
            narratingQueueIds.Clear();
            var extraList = extra?.ToList() ?? new List<string>();
            var segFlag = extraList.IndexOf("--segment");
            if (segFlag >= 0 && segFlag + 1 < extraList.Count)
            {
                var segId = extraList[segFlag + 1];
                narratingQueueIds.Add(segId);
                NarratingSegmentId = segId;
                var cueIdx = synthProject.Cues.FindIndex(c => c.Id == segId);
                NarratingSegmentIndex = cueIdx >= 0 ? cueIdx + 1 : null;
            }
            else
            {
                var changedOnly = extraList.Contains("--changed");
                var preserveUsable = changedOnly;
                for (var i = 0; i < synthProject.Cues.Count; i++)
                {
                    if (!preserveUsable || !synthProject.Cues[i].AudioReady)
                    {
                        narratingQueueIds.Add(synthProject.Cues[i].Id);
                        if (NarratingSegmentId == null)
                        {
                            NarratingSegmentId = synthProject.Cues[i].Id;
                            NarratingSegmentIndex = i + 1;
                        }
                    }
                }
            }
            ApplyNarrationStateToProject();
            Changed(nameof(IsSynthesizingNarration));
            Changed(nameof(NarratingSegmentId));
            Changed(nameof(NarratingSegmentIndex));
            Changed(nameof(ActiveNarrationStatusText));
        }
        Busy = true;
        operation = new();
        Status = "Starting…";
        ExportSession? exportSession = null;
        if (command.StartsWith("export-", StringComparison.Ordinal) && Project is { } exportProject)
        {
            exportSession = new(exportProject.Name, Cancel);
            ExportSessions.Add(exportSession);
        }
        try
        {
            Settings.Save();
            var args = new List<string> { command };
            if (needsProject) args.AddRange(new[] { "--project", Project!.Folder });
            if (extra != null) args.AddRange(extra);
            var result = await Engine.RunAsync(args, new Progress<EngineEvent>(e =>
            {
                Status = e.Message;
                if (e.Data.TryGetProperty("narrationPrimaryVoiceUpdated", out var primaryUpdated) && primaryUpdated.ValueKind == System.Text.Json.JsonValueKind.True && Project != null)
                {
                    try
                    {
                        Project = StudioProject.Load(Project.Folder);
                        ApplyNarrationStateToProject();
                        ProjectReloaded?.Invoke(this, EventArgs.Empty);
                    }
                    catch (Exception ex) { Engine.Log(ex.ToString()); }
                }
                if (e.Data.TryGetProperty("processingSection", out var section) && section.ValueKind == System.Text.Json.JsonValueKind.String)
                    ProcessingSection = section.GetString();
                if (e.Data.TryGetProperty("narratingTotal", out var nTotal) && nTotal.ValueKind == System.Text.Json.JsonValueKind.Number && nTotal.TryGetInt32(out var totalVal))
                    NarratingTotalCount = totalVal;
                if (e.Data.TryGetProperty("narratingQueue", out var nQueue) && nQueue.ValueKind == System.Text.Json.JsonValueKind.Array)
                {
                    narratingQueueIds.Clear();
                    foreach (var q in nQueue.EnumerateArray())
                        if (q.ValueKind == System.Text.Json.JsonValueKind.String && q.GetString() is { } qId)
                            narratingQueueIds.Add(qId);
                }
                if (e.Data.TryGetProperty("narratingSegment", out var nSeg) && nSeg.ValueKind == System.Text.Json.JsonValueKind.String)
                {
                    NarratingSegmentId = nSeg.GetString();
                    if (e.Data.TryGetProperty("narratingIndex", out var nIdx) && nIdx.ValueKind == System.Text.Json.JsonValueKind.Number && nIdx.TryGetInt32(out var idxVal))
                        NarratingSegmentIndex = idxVal;
                    ApplyNarrationStateToProject();
                    Changed(nameof(NarratingSegmentId));
                    Changed(nameof(NarratingSegmentIndex));
                    Changed(nameof(ActiveNarrationStatusText));
                    Changed(nameof(ProcessingStageTitle));
                }
                else if (e.Data.TryGetProperty("narratedSegment", out var doneSeg) && doneSeg.ValueKind == System.Text.Json.JsonValueKind.String)
                {
                    NarratingSegmentId = narratingQueueIds.FirstOrDefault();
                    if (Project != null && File.Exists(Path.Combine(Project.Folder, "project.json")))
                    {
                        try
                        {
                            Project = StudioProject.Load(Project.Folder);
                            if (NarratingSegmentId != null)
                            {
                                var nextIdx = Project.Cues.FindIndex(c => c.Id == NarratingSegmentId);
                                NarratingSegmentIndex = nextIdx >= 0 ? nextIdx + 1 : null;
                            }
                            else NarratingSegmentIndex = null;
                            ApplyNarrationStateToProject();
                            ProjectReloaded?.Invoke(this, EventArgs.Empty);
                        }
                        catch (Exception ex) { Engine.Log(ex.ToString()); }
                    }
                    Changed(nameof(NarratingSegmentId));
                    Changed(nameof(NarratingSegmentIndex));
                    Changed(nameof(ActiveNarrationStatusText));
                    Changed(nameof(ProcessingStageTitle));
                }
                if (e.Data.TryGetProperty("videoPreparation", out var preparation) && preparation.ValueKind == System.Text.Json.JsonValueKind.String)
                {
                    var state = preparation.GetString();
                    VideoPreparation = state == "ready" ? null : state;
                }
                var hasFraction = e.Data.TryGetProperty("exportProgress", out var progress);
                double? fraction = hasFraction && progress.ValueKind == System.Text.Json.JsonValueKind.Number && progress.TryGetDouble(out var number) ? number : null;
                exportSession?.Update(e.Message, fraction, hasFraction);
                if (e.Data.TryGetProperty("narrationFailures", out var failures) && failures.ValueKind == System.Text.Json.JsonValueKind.Array)
                {
                    var generatedCount = e.Data.TryGetProperty("narrationGenerated", out var generated) && generated.ValueKind == System.Text.Json.JsonValueKind.Number ? generated.GetInt32() : 0;
                    var report = new NarrationFailureReport
                    {
                        Title = "Some descriptions need attention",
                        Generated = generatedCount
                    };
                    foreach (var item in failures.EnumerateArray())
                    {
                        report.Failures.Add(new NarrationFailure
                        {
                            Index = item.TryGetProperty("index", out var index) && index.ValueKind == System.Text.Json.JsonValueKind.Number ? index.GetInt32() : 0,
                            Start = item.TryGetProperty("start", out var start) && start.ValueKind == System.Text.Json.JsonValueKind.Number ? start.GetDouble() : 0,
                            End = item.TryGetProperty("end", out var end) && end.ValueKind == System.Text.Json.JsonValueKind.Number ? end.GetDouble() : 0,
                            Duration = item.TryGetProperty("duration", out var duration) && duration.ValueKind == System.Text.Json.JsonValueKind.Number ? duration.GetDouble() : 0,
                            Reason = item.TryGetProperty("reason", out var reason) && reason.ValueKind == System.Text.Json.JsonValueKind.String ? reason.GetString() ?? "" : ""
                        });
                    }
                    if (report.Failures.Count > 0)
                    {
                        report.Detail = $"Narration finished for {generatedCount} description{(generatedCount == 1 ? "" : "s")}. {report.Failures.Count} could not be created.";
                        pendingNarrationFailures = report;
                    }
                }
            }), operation.Token);
            var imported = result.LastOrDefault(e => e.Data.TryGetProperty("project", out _));
            var folder = imported?.Data.GetProperty("project").GetString() ?? Project?.Folder;
            if (folder != null && File.Exists(Path.Combine(folder, "project.json"))) Open(folder);
            if (pendingNarrationFailures is { } narrationReport) { pendingNarrationFailures = null; NarrationFailures?.Invoke(this, narrationReport); }
            if (command is "check-api-key" or "list-models" || IsAICommand(command)) ClearAiIssue();
            Status = "Complete";
            return result;
        }
        catch (OperationCanceledException) { Status = "Cancelled. Completed results remain saved."; }
        catch (ProcessingException ex) { Status = ex.Message; DetectAiIssue(ex.Message); }
        catch (Exception ex) { Engine.Log(ex.ToString()); Status = "Could not finish. Check Settings and the diagnostic log, then retry."; DetectAiIssue(ex.Message); }
        finally
        {
            if (Project != null && File.Exists(Path.Combine(Project.Folder, "project.json")))
            {
                try { Project = StudioProject.Load(Project.Folder); ProjectReloaded?.Invoke(this, EventArgs.Empty); }
                catch (Exception ex) { Engine.Log(ex.ToString()); Status = "Could not reload the project. Reopen its folder to retry."; }
            }
            if (exportSession != null) ExportSessions.Remove(exportSession);
            IsSynthesizingNarration = false; NarratingSegmentId = null; NarratingSegmentIndex = null; NarratingTotalCount = 0; narratingQueueIds.Clear();
            ApplyNarrationStateToProject();
            Changed(nameof(IsSynthesizingNarration)); Changed(nameof(NarratingSegmentId)); Changed(nameof(NarratingSegmentIndex)); Changed(nameof(ActiveNarrationStatusText));
            ProcessingSection = null; VideoPreparation = null; processingPlan.Clear();
            operation.Dispose(); operation = null; IsExporting = false; Busy = false;
        }
        return new();
    }
    /// <summary>Combined processing state for a group of section keys (a tab can surface more than one stage).</summary>
    public SectionProcessingState StateFor(IReadOnlyList<string> sections)
    {
        if (!IsAnalyzing || sections.Count == 0) return SectionProcessingState.Idle;
        var current = processingSection ?? (processingPlan.Count > 0 ? processingPlan[0] : "");
        var currentIndex = processingPlan.IndexOf(current);
        if (currentIndex < 0) return SectionProcessingState.Idle;
        if (sections.Contains(current)) return SectionProcessingState.Processing;
        foreach (var section in sections)
        {
            var index = processingPlan.IndexOf(section);
            if (index > currentIndex) return SectionProcessingState.Queued;
        }
        return SectionProcessingState.Idle;
    }

    private static bool IsAICommand(string command) => command is
        "transcribe" or "generate-summary" or "generate-ad-cues" or "analyze" or "regenerate-all"
        or "reanalyze-segment" or "reanalyze-transcript-segment" or "rewrite-cue" or "capture-frame"
        or "import-youtube" or "align-imported-text" or "synthesize-tts" or "prepare-analysis-video";

    private static string? InitialProcessingSection(string command) => command switch
    {
        "transcribe" or "reanalyze-transcript-segment" => "transcript",
        "generate-summary" => "summary",
        "generate-ad-cues" or "reanalyze-segment" or "prepare-analysis-video" => "descriptions",
        "analyze" or "regenerate-all" => "transcript",
        _ => null
    };

    /// <summary>Ordered analysis stages for a command, respecting --start-stage.</summary>
    private static List<string> AnalysisPlan(string command, IEnumerable<string>? extra)
    {
        var stages = command switch
        {
            "analyze" or "regenerate-all" => new List<string> { "transcript", "summary", "descriptions" },
            "transcribe" or "reanalyze-transcript-segment" => new List<string> { "transcript" },
            "generate-summary" => new List<string> { "summary" },
            "generate-ad-cues" or "reanalyze-segment" or "prepare-analysis-video" => new List<string> { "descriptions" },
            _ => new List<string>()
        };
        if (stages.Count > 1 && extra != null)
        {
            var list = extra.ToList();
            var flag = list.IndexOf("--start-stage");
            if (flag >= 0 && flag + 1 < list.Count)
            {
                var startIndex = stages.IndexOf(list[flag + 1]);
                if (startIndex > 0) stages = stages.GetRange(startIndex, stages.Count - startIndex);
            }
        }
        return stages;
    }

    public async Task SaveSegmentsAsync(IEnumerable<Segment> transcript, IEnumerable<Segment> cues)
    {
        var temporary = Path.Combine(SettingsStore.DirectoryPath, Guid.NewGuid() + ".json");
        Directory.CreateDirectory(SettingsStore.DirectoryPath);
        try
        {
            await File.WriteAllTextAsync(temporary, System.Text.Json.JsonSerializer.Serialize(new { transcript, cues }, Json.Options));
            await RunAsync("save-segments", new[] { "--input", temporary });
        }
        finally { File.Delete(temporary); }
    }
    public async Task SaveSummaryAsync(string summary, string? origin = null)
    {
        if (Project == null) throw new InvalidOperationException("Open a project before saving the summary.");
        var temporary = Path.Combine(SettingsStore.DirectoryPath, Guid.NewGuid() + ".json");
        Directory.CreateDirectory(SettingsStore.DirectoryPath);
        try
        {
            // Only the engine writes project data, under its project lock with provenance updates.
            await File.WriteAllTextAsync(temporary, System.Text.Json.JsonSerializer.Serialize(
                new { summary, summaryOrigin = origin }, Json.Options), new System.Text.UTF8Encoding(false));
            var result = await RunAsync("save-summary", new[] { "--input", temporary });
            if (result.Count == 0) throw new IOException(Status);
        }
        finally
        {
            if (File.Exists(temporary)) File.Delete(temporary);
        }
    }
}
