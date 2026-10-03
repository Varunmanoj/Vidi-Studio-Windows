using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace Vidi_Studio_Windows.Services;

// Every editor and the library observe the same active exports.
public sealed class ExportSession : INotifyPropertyChanged
{
    public static ObservableCollection<ExportSession> Active { get; } = new();
    public string ProjectName { get; }
    private readonly Action cancel;
    private double highestFraction;
    private double? fraction;
    private string status;
    private bool cancelling;
    public event PropertyChangedEventHandler? PropertyChanged;
    public double Percent => (fraction ?? highestFraction) * 100;
    public bool IsIndeterminate => fraction == null;
    public bool CanCancel => !cancelling;
    public string StatusLabel => fraction is { } value ? $"{status} — {value:P0}" : status;
    public ExportSession(string projectName, Action cancel)
    { ProjectName = projectName; this.cancel = cancel; status = "Starting export…"; }
    public void Update(string message, double? value, bool hasFraction)
    {
        if (cancelling) return;
        status = message;
        if (hasFraction)
        {
            if (value is { } number && double.IsFinite(number))
                fraction = highestFraction = Math.Max(highestFraction, Math.Clamp(number, 0, .99));
            else fraction = null;
        }
        Changed(nameof(Percent)); Changed(nameof(IsIndeterminate)); Changed(nameof(StatusLabel));
    }
    public void Cancel()
    {
        if (cancelling) return;
        cancelling = true; status = "Stopping the export…";
        Changed(nameof(CanCancel)); Changed(nameof(StatusLabel)); cancel();
    }
    private void Changed([CallerMemberName] string? name = null) => PropertyChanged?.Invoke(this, new(name));
}
