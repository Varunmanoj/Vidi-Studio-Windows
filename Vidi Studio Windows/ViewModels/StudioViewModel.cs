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
    private StudioProject? project;
    public StudioProject? Project { get => project; set { project = value; Changed(); } }
    private bool busy;
    public bool Busy { get => busy; private set { busy = value; Changed(); Changed(nameof(Idle)); Changed(nameof(IsAnalyzing)); Changed(nameof(ProcessingStageTitle)); } }
    public bool Idle => !Busy;
    public bool IsExporting { get; private set; }
    private string status = "Import a video or open an existing project to begin.";
    public string Status { get => status; private set { status = value; Changed(); } }
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
    public string ApiPlan => Settings.ApiPlan;
    private CancellationTokenSource? operation;
    private NarrationFailureReport? pendingNarrationFailures;
    public event PropertyChangedEventHandler? PropertyChanged;
    public event EventHandler? ProjectReloaded;
    /// <summary>Raised after a narration run finished with one or more skipped descriptions.</summary>
    public event EventHandler<NarrationFailureReport>? NarrationFailures;
    public StudioViewModel() { Engine = new(Settings); }
    private void Changed([CallerMemberName] string? name = null) => PropertyChanged?.Invoke(this, new(name));

    public void Reload(string search = "", string source = "all")
    {
        Settings.Reload();
        Changed(nameof(HasApiKey));
        Changed(nameof(IsApiKeyInvalid));
        Changed(nameof(ApiPlan));
        var library = Settings.Get("libraryPath");
        Directory.CreateDirectory(library);
        var folders = Directory.EnumerateDirectories(library).Concat(externalFolders).Distinct(StringComparer.OrdinalIgnoreCase);
        Projects.Clear();
        foreach (var folder in folders)
        {
            if (!File.Exists(Path.Combine(folder, "project.json"))) continue;
            try
            {
                var p = StudioProject.Load(folder);
                if (p.Name.Contains(search, StringComparison.CurrentCultureIgnoreCase) && (source == "all" || p.IsYouTube == (source == "youtube"))) Projects.Add(p);
            }
            catch (Exception ex) { Engine.Log(ex.ToString()); }
        }
    }
    public void Open(string folder)
    {
        Project = StudioProject.Load(folder);
        if (Idle) Status = "Ready";
        externalFolders.Add(Project.Folder);
        Reload();
        ProjectReloaded?.Invoke(this, EventArgs.Empty);
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
        for (var i = 0; i < p.Cues.Count; i++)
        {
            var cue = p.Cues[i];
            if (!cue.HasAudioFile || string.IsNullOrEmpty(cue.Audio)) continue;
            var engine = cue.ResolvedAudioEngine ?? "windows-core";
            var voice = cue.ResolvedAudioVoice ?? "System default";
            narratedEntries.Add((i + 1, cue, engine, voice, cue.Audio));
        }
        if (narratedEntries.Count == 0)
        {
            return new NarrationVoiceReport
            {
                Title = "Narration voice details",
                Detail = "No narration audio has been generated for this project yet.",
                HasDifferentVoices = false
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

        var hasDifferent = differentSegments.Count > 0 || summaries.Count(s => s.Count > 0) > 1;
        var detail = hasDifferent
            ? $"{differentSegments.Count} of {allSegments.Count} narrated segment{(allSegments.Count == 1 ? "" : "s")} use a different voice from {primaryLabel}. ({string.Join("; ", summaries.Select(s => $"{s.CombinedLabel}: {s.Count} segment{(s.Count == 1 ? "" : "s")}"))})"
            : $"All {allSegments.Count} narrated audio description segment{(allSegments.Count == 1 ? "" : "s")} use {primaryLabel}.";

        return new NarrationVoiceReport
        {
            Title = hasDifferent ? "Different narration voices in project" : "Narration voice details",
            Detail = detail,
            PrimaryVoiceLabel = primaryLabel,
            HasDifferentVoices = hasDifferent,
            VoiceSummaries = summaries,
            DifferentSegments = differentSegments,
            AllSegments = allSegments
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
                var hasUsable = synthProject.Cues.Any(c => c.AudioReady);
                var hasUnusable = synthProject.Cues.Any(c => !c.AudioReady);
                var preserveUsable = changedOnly || (hasUsable && hasUnusable);
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
            Status = "Complete";
            return result;
        }
        catch (OperationCanceledException) { Status = "Cancelled. Completed results remain saved."; }
        catch (ProcessingException ex) { Status = ex.Message; }
        catch (Exception ex) { Engine.Log(ex.ToString()); Status = "Could not finish. Check Settings and the diagnostic log, then retry."; }
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
    public async Task SaveSummaryAsync(string summary)
    {
        if (Project is not { } p) return;
        p.Summary = summary;
        var temporary = Path.Combine(SettingsStore.DirectoryPath, Guid.NewGuid() + ".txt");
        Directory.CreateDirectory(SettingsStore.DirectoryPath);
        try
        {
            await File.WriteAllTextAsync(temporary, summary, System.Text.Encoding.UTF8);
            var result = await RunAsync("save-summary", new[] { "--input", temporary });
            if (result.Count == 0)
            {
                SaveSummaryDirectly(p.Folder, summary);
                Project = StudioProject.Load(p.Folder);
                ProjectReloaded?.Invoke(this, EventArgs.Empty);
            }
        }
        catch
        {
            SaveSummaryDirectly(p.Folder, summary);
            Project = StudioProject.Load(p.Folder);
            ProjectReloaded?.Invoke(this, EventArgs.Empty);
        }
        finally
        {
            if (File.Exists(temporary)) File.Delete(temporary);
        }
    }
    private static void SaveSummaryDirectly(string folder, string summary)
    {
        var projectFile = Path.Combine(folder, "project.json");
        if (!File.Exists(projectFile)) return;
        var json = File.ReadAllText(projectFile);
        using var doc = System.Text.Json.JsonDocument.Parse(json);
        var dict = new Dictionary<string, object?>();
        foreach (var prop in doc.RootElement.EnumerateObject())
        {
            if (prop.NameEquals("summary"))
                dict["summary"] = summary;
            else if (prop.NameEquals("summaryGenerated"))
                dict["summaryGenerated"] = !string.IsNullOrWhiteSpace(summary);
            else
                dict[prop.Name] = System.Text.Json.JsonSerializer.Deserialize<System.Text.Json.JsonElement>(prop.Value.GetRawText());
        }
        if (!dict.ContainsKey("summary")) dict["summary"] = summary;
        if (!dict.ContainsKey("summaryGenerated")) dict["summaryGenerated"] = !string.IsNullOrWhiteSpace(summary);
        var tempFile = Path.Combine(folder, "project." + Guid.NewGuid() + ".json");
        File.WriteAllText(tempFile, System.Text.Json.JsonSerializer.Serialize(dict, Json.Options));
        File.Move(tempFile, projectFile, overwrite: true);
    }
}
