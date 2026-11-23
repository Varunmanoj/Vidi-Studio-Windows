using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Vidi_Studio_Windows.Services;

/// <summary>Deletion progress shared by gallery and editor windows; no estimated percentages.</summary>
internal sealed class DeletionActivity : IDisposable
{
    private static readonly Dictionary<Guid, string> activities = new();
    private static readonly object sync = new();
    private readonly Guid id = Guid.NewGuid();
    public static event EventHandler? Changed;
    public static string? Message { get { lock (sync) return activities.Values.FirstOrDefault(); } }
    private DeletionActivity(string message) { lock (sync) activities[id] = message; Changed?.Invoke(null, EventArgs.Empty); }
    public static DeletionActivity Begin(string title, string project) => new(title + " · " + project);
    public static DeletionActivity? BeginForCommand(string command, string project) => command switch
    {
        "cloud-delete-project-video" => Begin("Deleting cloud video…", project),
        "delete-results" or "delete-generated" or "delete-export" or "delete-segment-audio" => Begin("Deleting selected content…", project),
        _ => null
    };
    public void Dispose() { lock (sync) activities.Remove(id); Changed?.Invoke(null, EventArgs.Empty); }
    public static Task RecycleAsync(string folder)
    {
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var worker = new Thread(() => {
            try {
                Microsoft.VisualBasic.FileIO.FileSystem.DeleteDirectory(folder,
                    Microsoft.VisualBasic.FileIO.UIOption.OnlyErrorDialogs,
                    Microsoft.VisualBasic.FileIO.RecycleOption.SendToRecycleBin);
                completion.SetResult();
            } catch (Exception ex) { completion.SetException(ex); }
        }) { IsBackground = true };
        worker.SetApartmentState(ApartmentState.STA);
        worker.Start();
        return completion.Task;
    }
}

/// <summary>Cloud video import progress shared by native windows.</summary>
internal sealed class CloudImportActivity : IDisposable
{
    private static readonly Dictionary<Guid, (string Message, double? Fraction)> activities = new();
    private readonly Guid id = Guid.NewGuid();
    public static event EventHandler? Changed;

    public static (string? Message, double? Fraction) Current
    {
        get
        {
            lock (activities) return activities.Values.LastOrDefault();
        }
    }

    public CloudImportActivity() => SetProgress("Checking Cloud Storage access…", null);

    public void Update(EngineEvent update)
    {
        if (update.Data.TryGetProperty("networkActive", out _) ||
            update.Data.TryGetProperty("ping", out _) || update.Data.TryGetProperty("error", out _)) return;
        SetProgress(update.Message, update.Message.StartsWith("Downloading video:", StringComparison.Ordinal) ? update.Progress : null);
    }

    private void SetProgress(string message, double? fraction)
    {
        lock (activities) activities[id] = (message, fraction);
        Changed?.Invoke(null, EventArgs.Empty);
    }

    public void Dispose()
    {
        lock (activities) activities.Remove(id);
        Changed?.Invoke(null, EventArgs.Empty);
    }
}
