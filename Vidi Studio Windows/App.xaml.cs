using Microsoft.UI.Xaml;
using Microsoft.Windows.AppLifecycle;
using System;
using System.Collections.Generic;
using Windows.ApplicationModel.Activation;
using Windows.ApplicationModel.DataTransfer;
using Windows.ApplicationModel.DataTransfer.ShareTarget;
using Windows.Storage;

namespace Vidi_Studio_Windows;

public partial class App : Application
{
    private static readonly List<MainWindow> Windows = new();
    internal static event Action<string>? ThemeChanged;
    internal static string? ActiveTheme { get; private set; }
    internal static void PreviewTheme(string theme)
    {
        ActiveTheme = theme is "light" or "dark" ? theme : "system";
        ThemeChanged?.Invoke(ActiveTheme);
    }
    internal static void CommitTheme(string theme)
    {
        foreach (var window in Windows) window.Model.Settings.Set("theme", theme);
        PreviewTheme(theme);
    }
    public App()
    {
        InitializeComponent();
        UnhandledException += (_, e) =>
        {
            try { new Services.EngineClient(new Services.SettingsStore()).Log(e.Exception.ToString()); } catch { }
#if DEBUG
            var report = Environment.GetEnvironmentVariable("VIDISTUDIO_UI_SMOKE_REPORT");
            if (!string.IsNullOrEmpty(report)) System.IO.File.WriteAllText(report, e.Exception.ToString());
#endif
        };
    }
    public static MainWindow OpenWindow(string? folder = null)
    {
        var window = new MainWindow(folder);
        Windows.Add(window);
        window.Closed += (_, _) => Windows.Remove(window);
        window.Activate(); return window;
    }
    protected override void OnLaunched(Microsoft.UI.Xaml.LaunchActivatedEventArgs args)
    {
        var activation = AppInstance.GetCurrent().GetActivatedEventArgs();
#if DEBUG
        var launchArguments = activation.Data is Windows.ApplicationModel.Activation.LaunchActivatedEventArgs launch ? launch.Arguments : args.Arguments;
        const string smokePrefix = "--vidi-smoke ";
        if (launchArguments.StartsWith(smokePrefix, StringComparison.Ordinal))
        {
            var json = System.Text.Encoding.UTF8.GetString(Convert.FromBase64String(launchArguments[smokePrefix.Length..]));
            var environment = System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, string>>(json)!;
            foreach (var key in new[] { "VIDISTUDIO_UI_SMOKE_REPORT", "VIDISTUDIO_UI_SMOKE_PROJECT", "VIDISTUDIO_CONFIG" })
                Environment.SetEnvironmentVariable(key, environment[key]);
        }
#endif
        var window = OpenWindow();
        if (activation.Kind == ExtendedActivationKind.ShareTarget && activation.Data is ShareTargetActivatedEventArgs share)
        {
            async void Receive(object sender, RoutedEventArgs e)
            {
                ((FrameworkElement)window.Content).Loaded -= Receive;
                await ReceiveShare(window, share.ShareOperation);
            }
            ((FrameworkElement)window.Content).Loaded += Receive;
        }
    }
    private static async System.Threading.Tasks.Task ReceiveShare(MainWindow window, ShareOperation share)
    {
        share.ReportStarted();
        try
        {
            List<Services.EngineEvent> result;
            if (share.Data.Contains(StandardDataFormats.StorageItems))
            {
                var items = await share.Data.GetStorageItemsAsync();
                if (items.Count != 1 || items[0] is not StorageFile file) { share.ReportError("Share one video file at a time."); return; }
                result = await window.Model.RunAsync("import-video", new[] { "--video", file.Path, "--library", window.Model.Settings.Get("libraryPath") }, false);
            }
            else if (share.Data.Contains(StandardDataFormats.WebLink))
            {
                var uri = await share.Data.GetWebLinkAsync();
                result = await window.Model.RunAsync("import-youtube", new[] { "--url", uri.AbsoluteUri, "--library", window.Model.Settings.Get("libraryPath") }, false);
            }
            else { share.ReportError("Share one video file or public YouTube link."); return; }
            if (result.Count == 0) share.ReportError("Import could not finish. Check Vidi Studio settings and retry.");
            else { share.ReportDataRetrieved(); share.ReportCompleted(); }
        }
        catch (Exception ex)
        {
            window.Model.Engine.Log(ex.ToString());
            share.ReportError("Import could not finish. Open Vidi Studio and check your settings.");
        }
    }
}
