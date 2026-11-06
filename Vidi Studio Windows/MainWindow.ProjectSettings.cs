using Microsoft.UI.Xaml;
using System;
using System.Threading.Tasks;
using Vidi_Studio_Windows.Models;

namespace Vidi_Studio_Windows;

public sealed partial class MainWindow
{
    internal bool CanUseProjectLanguageSettings => !Model.Busy && !HasUnsavedLanguageSummary;
    internal Microsoft.UI.Xaml.Controls.StackPanel ProjectLanguageSettingsPanel(bool editing, Action<bool> setEditing, Action changed) =>
        BuildLanguageResultsPanel(Model.Project!, true, editing, setEditing, changed);

    internal async Task RunProjectSettingsActionAsync(string command)
    {
        if (!ready || Root.XamlRoot == null) {
            var loaded = new TaskCompletionSource<bool>();
            RoutedEventHandler? ready = null;
            ready = (_, _) => { Root.Loaded -= ready; loaded.TrySetResult(true); };
            Root.Loaded += ready;
            try { await loaded.Task.WaitAsync(TimeSpan.FromSeconds(5)); }
            finally { Root.Loaded -= ready; }
        }
        if (Model.Busy || Model.Project is not { } project) return;
        switch (command) {
            case "import-transcript": await ImportExistingAssetsAsync(ImportScope.Transcript); break;
            case "import-summary": await ImportExistingAssetsAsync(ImportScope.Summary); break;
            case "import-descriptions": await ImportExistingAssetsAsync(ImportScope.Descriptions); break;
            case "import-ad-audio": await ImportExistingAssetsAsync(ImportScope.ADAudio); break;
            case "import-assets": await ImportExistingAssetsAsync(); break;
            case "import-original-audio": await ImportExistingAssetsAsync(ImportScope.OriginalAudio); break;
            case "review-audio-timing": await ImportOriginalAudioAsync(null); break;
            case "youtube-captions": await ShowYouTubeDetailsAsync(true); break;
            case "export": await ShowAudioExportAsync(); break;
            case "rename-project": await RenameProjectAsync(project); break;
        }
    }
}
