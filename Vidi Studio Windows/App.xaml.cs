using Microsoft.UI.Xaml;
using Microsoft.Windows.AppLifecycle;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Windows.ApplicationModel.Activation;
using Windows.ApplicationModel.DataTransfer;
using Windows.ApplicationModel.DataTransfer.ShareTarget;
using Windows.Storage;

namespace Vidi_Studio_Windows;

public partial class App : Application
{
    private static readonly List<MainWindow> Windows = new();
    private static AppInstance? mainInstance;
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
        // Startup milestones make a silent launch failure diagnosable from diagnostics.log.
        LogStartup($"App constructed · version {typeof(App).Assembly.GetName().Version}");
    }
    private static void LogStartup(string message)
    {
        try { new Services.EngineClient(new Services.SettingsStore()).Log(message); } catch { }
    }
    public static MainWindow OpenWindow(string? folder = null)
    {
        var window = new MainWindow(folder);
        Windows.Add(window);
        window.Closed += (_, _) => Windows.Remove(window);
        window.Activate(); return window;
    }
    protected override async void OnLaunched(Microsoft.UI.Xaml.LaunchActivatedEventArgs args)
    {
        var activation = AppInstance.GetCurrent().GetActivatedEventArgs();
        var launchArguments = activation.Data is Windows.ApplicationModel.Activation.LaunchActivatedEventArgs launch ? launch.Arguments : args.Arguments;
        LogStartup($"OnLaunched · kind {activation.Kind} · args {Truncate(launchArguments, 200)}");

#if DEBUG
        const string smokePrefix = "--vidi-smoke ";
        if (launchArguments.StartsWith(smokePrefix, StringComparison.Ordinal))
        {
            var json = System.Text.Encoding.UTF8.GetString(Convert.FromBase64String(launchArguments[smokePrefix.Length..]));
            var environment = System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, string>>(json)!;
            foreach (var key in new[] { "VIDISTUDIO_UI_SMOKE_REPORT", "VIDISTUDIO_UI_SMOKE_PROJECT", "VIDISTUDIO_CONFIG" })
                Environment.SetEnvironmentVariable(key, environment[key]);
            _ = OpenWindow();
            return;
        }
#endif

        if (launchArguments.Contains("--register-context-menu", StringComparison.OrdinalIgnoreCase))
        {
            Services.ShellIntegration.RegisterContextMenu();
            return;
        }
        if (launchArguments.Contains("--unregister-context-menu", StringComparison.OrdinalIgnoreCase))
        {
            Services.ShellIntegration.UnregisterContextMenu();
            return;
        }

        try
        {
            mainInstance = AppInstance.FindOrRegisterForKey("VidiStudioMainInstance");
            if (!mainInstance.IsCurrent)
            {
                LogStartup("Another Vidi Studio instance owns the single-instance key; redirecting activation and exiting this process.");
                await mainInstance.RedirectActivationToAsync(activation);
                System.Diagnostics.Process.GetCurrentProcess().Kill();
                return;
            }
        }
        catch { }

        try
        {
            var window = OpenWindow();
            LogStartup("Main window created.");
            try { Services.ShellIntegration.RegisterContextMenu(); } catch { }

            if (mainInstance != null)
            {
                mainInstance.Activated += (sender, redirectedArgs) =>
                {
                    window.DispatcherQueue.TryEnqueue(async () =>
                    {
                        window.Activate();
                        window.BringToForeground();
                        await HandleIncomingActivationAsync(window, redirectedArgs, null);
                    });
                };
            }

            await HandleIncomingActivationAsync(window, activation, launchArguments);
            LogStartup("Launch completed.");
        }
        catch (Exception ex)
        {
            LogStartup("Startup failed: " + ex);
            throw;
        }
    }

    private static string Truncate(string? value, int length) =>
        string.IsNullOrEmpty(value) ? "" : value.Length <= length ? value : value[..length] + "…";

    private static async Task HandleIncomingActivationAsync(MainWindow window, AppActivationArguments activation, string? rawLaunchArgs)
    {
        if (activation.Kind == ExtendedActivationKind.ShareTarget && activation.Data is ShareTargetActivatedEventArgs share)
        {
            void Receive(object sender, RoutedEventArgs e)
            {
                ((FrameworkElement)window.Content).Loaded -= Receive;
                _ = ReceiveShare(window, share.ShareOperation);
            }
            if (((FrameworkElement)window.Content).IsLoaded)
                _ = ReceiveShare(window, share.ShareOperation);
            else
                ((FrameworkElement)window.Content).Loaded += Receive;
            return;
        }

        var (videoPath, youtubeUrl) = ParseActivationTarget(activation, rawLaunchArgs);
        if (!string.IsNullOrEmpty(videoPath))
        {
            await window.OpenVideoFileAsync(videoPath);
        }
        else if (!string.IsNullOrEmpty(youtubeUrl))
        {
            await window.OpenYouTubeUrlAsync(youtubeUrl);
        }
    }

    private static readonly HashSet<string> VideoExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".mp4", ".mov", ".m4v", ".mkv", ".avi", ".webm", ".wmv"
    };

    public static (string? VideoPath, string? YouTubeUrl) ParseActivationTarget(AppActivationArguments activation, string? rawLaunchArgs)
    {
        if (activation.Kind == ExtendedActivationKind.File && activation.Data is IFileActivatedEventArgs fileArgs)
        {
            var file = fileArgs.Files?.FirstOrDefault() as IStorageFile;
            if (file != null && !string.IsNullOrWhiteSpace(file.Path))
                return (file.Path, null);
        }

        if (activation.Kind == ExtendedActivationKind.Protocol && activation.Data is IProtocolActivatedEventArgs protoArgs)
        {
            var result = ParseProtocolUri(protoArgs.Uri);
            if (result.VideoPath != null || result.YouTubeUrl != null)
                return result;
        }

        if (activation.Kind == ExtendedActivationKind.CommandLineLaunch && activation.Data is ICommandLineActivatedEventArgs cmdArgs)
        {
            var cmdLine = cmdArgs.Operation?.Arguments;
            if (!string.IsNullOrWhiteSpace(cmdLine))
            {
                var result = ParseTokens(SplitCommandLine(cmdLine));
                if (result.VideoPath != null || result.YouTubeUrl != null)
                    return result;
            }
        }

        if (activation.Kind == ExtendedActivationKind.Launch && activation.Data is ILaunchActivatedEventArgs launchArgs)
        {
            var cmdLine = launchArgs.Arguments;
            if (!string.IsNullOrWhiteSpace(cmdLine))
            {
                var result = ParseTokens(SplitCommandLine(cmdLine));
                if (result.VideoPath != null || result.YouTubeUrl != null)
                    return result;
            }
        }

        if (!string.IsNullOrWhiteSpace(rawLaunchArgs))
        {
            var result = ParseTokens(SplitCommandLine(rawLaunchArgs));
            if (result.VideoPath != null || result.YouTubeUrl != null)
                return result;
        }

        var envArgs = Environment.GetCommandLineArgs();
        if (envArgs.Length > 1)
        {
            var result = ParseTokens(envArgs.Skip(1));
            if (result.VideoPath != null || result.YouTubeUrl != null)
                return result;
        }

        return (null, null);
    }

    public static (string? VideoPath, string? YouTubeUrl) ParseProtocolUri(Uri uri)
    {
        if (uri == null) return (null, null);
        try
        {
            if (!uri.Scheme.Equals("vidistudio", StringComparison.OrdinalIgnoreCase)) return (null, null);
            if (!uri.Host.Equals("share", StringComparison.OrdinalIgnoreCase)) return (null, null);

            string? kind = null;
            string? source = null;
            var query = uri.Query.TrimStart('?');
            foreach (var part in query.Split('&', StringSplitOptions.RemoveEmptyEntries))
            {
                var idx = part.IndexOf('=');
                if (idx < 0) continue;
                var key = Uri.UnescapeDataString(part.Substring(0, idx));
                var val = Uri.UnescapeDataString(part.Substring(idx + 1));
                if (key.Equals("kind", StringComparison.OrdinalIgnoreCase)) kind = val;
                else if (key.Equals("source", StringComparison.OrdinalIgnoreCase)) source = val;
            }

            if (kind == "video" && !string.IsNullOrWhiteSpace(source))
            {
                if (source.StartsWith("file://", StringComparison.OrdinalIgnoreCase) && Uri.TryCreate(source, UriKind.Absolute, out var fileUri))
                    return (fileUri.LocalPath, null);
                return (source, null);
            }
            if (kind == "youtube" && !string.IsNullOrWhiteSpace(source))
            {
                return (null, source);
            }
        }
        catch { }
        return (null, null);
    }

    public static List<string> SplitCommandLine(string commandLine)
    {
        var args = new List<string>();
        if (string.IsNullOrWhiteSpace(commandLine)) return args;

        var sb = new StringBuilder();
        bool inQuotes = false;
        for (int i = 0; i < commandLine.Length; i++)
        {
            char c = commandLine[i];
            if (c == '"')
            {
                inQuotes = !inQuotes;
            }
            else if (char.IsWhiteSpace(c) && !inQuotes)
            {
                if (sb.Length > 0)
                {
                    args.Add(sb.ToString());
                    sb.Clear();
                }
            }
            else
            {
                sb.Append(c);
            }
        }
        if (sb.Length > 0)
        {
            args.Add(sb.ToString());
        }
        return args;
    }

    public static (string? VideoPath, string? YouTubeUrl) ParseTokens(IEnumerable<string> tokens)
    {
        var list = tokens.ToList();
        for (int i = 0; i < list.Count; i++)
        {
            var token = list[i].Trim();
            if (string.IsNullOrEmpty(token)) continue;

            if ((token.Equals("--video", StringComparison.OrdinalIgnoreCase) || token.Equals("-v", StringComparison.OrdinalIgnoreCase)) && i + 1 < list.Count)
            {
                return (CleanPath(list[i + 1]), null);
            }
            if ((token.Equals("--url", StringComparison.OrdinalIgnoreCase) || token.Equals("-u", StringComparison.OrdinalIgnoreCase)) && i + 1 < list.Count)
            {
                return (null, list[i + 1].Trim());
            }
            if (token.StartsWith("--video=", StringComparison.OrdinalIgnoreCase))
            {
                return (CleanPath(token.Substring(8)), null);
            }
            if (token.StartsWith("--url=", StringComparison.OrdinalIgnoreCase))
            {
                return (null, token.Substring(6).Trim());
            }
            if (token.StartsWith("vidistudio://", StringComparison.OrdinalIgnoreCase) && Uri.TryCreate(token, UriKind.Absolute, out var uri))
            {
                var parsed = ParseProtocolUri(uri);
                if (parsed.VideoPath != null || parsed.YouTubeUrl != null) return parsed;
            }
            if (IsSupportedVideoFile(token))
            {
                return (CleanPath(token), null);
            }
            if (IsYouTubeUrl(token))
            {
                return (null, token);
            }
        }
        return (null, null);
    }

    private static string CleanPath(string raw)
    {
        var path = raw.Trim().Trim('"', '\'');
        try { return Path.GetFullPath(path); } catch { return path; }
    }

    private static bool IsSupportedVideoFile(string token)
    {
        var clean = token.Trim().Trim('"', '\'');
        try
        {
            var ext = Path.GetExtension(clean);
            return !string.IsNullOrEmpty(ext) && VideoExtensions.Contains(ext);
        }
        catch { return false; }
    }

    private static bool IsYouTubeUrl(string token)
    {
        var clean = token.Trim().Trim('"', '\'');
        try
        {
            if (Uri.TryCreate(clean, UriKind.Absolute, out var uri))
            {
                var host = uri.Host.ToLowerInvariant();
                return host == "youtu.be" || host == "www.youtu.be" || host.Contains("youtube.com");
            }
        }
        catch { }
        return false;
    }

    private static async Task ReceiveShare(MainWindow window, ShareOperation share)
    {
        share.ReportStarted();
        try
        {
            if (share.Data.Contains(StandardDataFormats.StorageItems))
            {
                var items = await share.Data.GetStorageItemsAsync();
                if (items.Count != 1 || items[0] is not StorageFile file) { share.ReportError("Share one video file at a time."); return; }
                await window.OpenVideoFileAsync(file.Path);
                share.ReportDataRetrieved(); share.ReportCompleted();
            }
            else if (share.Data.Contains(StandardDataFormats.WebLink))
            {
                var uri = await share.Data.GetWebLinkAsync();
                await window.OpenYouTubeUrlAsync(uri.AbsoluteUri);
                share.ReportDataRetrieved(); share.ReportCompleted();
            }
            else { share.ReportError("Share one video file or public YouTube link."); return; }
        }
        catch (Exception ex)
        {
            window.Model.Engine.Log(ex.ToString());
            share.ReportError("Import could not finish. Open Vidi Studio and check your settings.");
        }
    }
}
