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
    public bool Busy { get => busy; private set { busy = value; Changed(); Changed(nameof(Idle)); } }
    public bool Idle => !Busy;
    private string status = "Import a video or open an existing project to begin.";
    public string Status { get => status; private set { status = value; Changed(); } }
    private CancellationTokenSource? operation;
    public event PropertyChangedEventHandler? PropertyChanged;
    public event EventHandler? ProjectReloaded;
    public StudioViewModel() { Engine = new(Settings); }
    private void Changed([CallerMemberName] string? name = null) => PropertyChanged?.Invoke(this, new(name));

    public void Reload(string search = "", string source = "all")
    {
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
    public async Task<List<EngineEvent>> RunAsync(string command, IEnumerable<string>? extra = null, bool needsProject = true)
    {
        if (Busy) return new();
        if (needsProject && Project == null) { Status = "Open a project first."; return new(); }
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
                var hasFraction = e.Data.TryGetProperty("exportProgress", out var progress);
                double? fraction = hasFraction && progress.ValueKind == System.Text.Json.JsonValueKind.Number && progress.TryGetDouble(out var number) ? number : null;
                exportSession?.Update(e.Message, fraction, hasFraction);
            }), operation.Token);
            var imported = result.LastOrDefault(e => e.Data.TryGetProperty("project", out _));
            var folder = imported?.Data.GetProperty("project").GetString() ?? Project?.Folder;
            if (folder != null && File.Exists(Path.Combine(folder, "project.json"))) Open(folder);
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
            operation.Dispose(); operation = null; Busy = false;
        }
        return new();
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
}
