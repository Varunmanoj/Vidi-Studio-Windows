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
    public static DeletionActivity? BeginForCommand(string command, string project) =>
        command is "delete-results" or "delete-generated" or "delete-export" or "delete-segment-audio"
            ? Begin("Deleting selected content…", project) : null;
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
