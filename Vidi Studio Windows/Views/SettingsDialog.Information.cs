using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using System;
using System.Diagnostics;
using System.Reflection;
using System.Threading.Tasks;
using Vidi_Studio_Windows.Services;

namespace Vidi_Studio_Windows.Views;

public sealed partial class SettingsDialog
{
    private static void AddInformationRow(StackPanel panel, string label, string detail)
    {
        var row = new Grid { ColumnSpacing = 20, Padding = new Thickness(0, 9, 0, 9) };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(230) });
        row.ColumnDefinitions.Add(new ColumnDefinition());
        row.Children.Add(new TextBlock { Text = label, TextWrapping = TextWrapping.Wrap });
        var value = new TextBlock { Text = detail, TextWrapping = TextWrapping.Wrap, Opacity = .7 };
        Grid.SetColumn(value, 1); row.Children.Add(value); panel.Children.Add(row);
    }

    private void BuildHelp(StackPanel panel)
    {
        panel.Children.Add(new TextBlock { Text = "Keyboard shortcuts", FontSize = 20, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold });
        panel.Children.Add(new TextBlock { Text = "Shortcuts work when a project is open and processing is idle.", TextWrapping = TextWrapping.Wrap });
        if (menuOwner != null)
            foreach (var (label, shortcut) in menuOwner.DesktopShortcutReference) AddInformationRow(panel, label, shortcut);
        panel.Children.Add(new TextBlock { Text = "File Explorer & desktop integration", FontSize = 17, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold, Margin = new Thickness(0, 18, 0, 0) });
        panel.Children.Add(new TextBlock { Text = "Right-click any supported video file (.mp4, .mov, .m4v, .mkv, .avi, .webm, .wmv) in File Explorer to access \"Describe with Vidi Studio\" (directly or under Show more options / Shift+F10) or \"Open with > Vidi Studio\". You can also share video files to Vidi Studio through the Windows Share flyout. If you are running unpackaged builds and the context menu is not visible, use \"Register File Explorer context menu\" under Storage and tools.", TextWrapping = TextWrapping.Wrap, Opacity = .8 });
        panel.Children.Add(new TextBlock { Text = "Menus follow the Mac layout: File, View, Playback, Project, Help. Creating new projects, local exports, and on-device narration do not require a Gemini API key; an active API key is only required when invoking automated AI features (transcription, summaries, audio description generation, and AI rewrites). Built-in voices are platform-specific: Windows offers Windows OneCore and Windows SAPI 5 voices (macOS offers Apple Voices), and Gemini voices work on both platforms with your API key.", TextWrapping = TextWrapping.Wrap, Opacity = .7 });
    }

    private void BuildAbout(StackPanel panel)
    {
        panel.Children.Add(new TextBlock { Text = "About Vidi Studio", FontSize = 20, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold });
        string version;
        try
        {
            var package = Windows.ApplicationModel.Package.Current.Id.Version;
            version = $"{package.Major}.{package.Minor}.{package.Build}.{package.Revision}";
        }
        catch { version = Assembly.GetEntryAssembly()?.GetName().Version?.ToString() ?? "Unknown"; }
        AddInformationRow(panel, "App version", version);
        panel.Children.Add(new TextBlock { Text = "Media tools", FontSize = 17, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold, Margin = new Thickness(0, 18, 0, 0) });
        panel.Children.Add(new TextBlock { Text = "Vidi Studio uses the FFmpeg and FFprobe executables installed on this computer or set under Storage and tools.", TextWrapping = TextWrapping.Wrap });
        var ffmpegDetails = new TextBlock { Text = "Checking installed FFmpeg…", TextWrapping = TextWrapping.Wrap, Opacity = .7 };
        panel.Children.Add(ffmpegDetails);
        _ = ShowFfmpegDetailsAsync(ffmpegDetails);
        panel.Children.Add(new HyperlinkButton { Content = "FFmpeg license and source information", NavigateUri = new Uri("https://ffmpeg.org/legal.html") });
        panel.Children.Add(new TextBlock { Text = "FFmpeg is generally LGPL, but optional components can make a particular build GPL. The installed executable's configuration determines which terms apply.", TextWrapping = TextWrapping.Wrap, Opacity = .7 });
        panel.Children.Add(new TextBlock { Text = "Vidi Studio also uses the Windows App SDK, .NET, and Python packages listed in the project dependency files. Gemini is an external service used with your own API key, which is stored securely in Windows Credential Manager rather than in a plain file.", TextWrapping = TextWrapping.Wrap, Opacity = .7 });
    }

    private async Task ShowFfmpegDetailsAsync(TextBlock target)
    {
        try
        {
            var configured = SettingsStore.GetString(draft["ffmpegPath"]);
            var info = new ProcessStartInfo(string.IsNullOrWhiteSpace(configured) ? "ffmpeg" : configured)
            {
                UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true
            };
            info.ArgumentList.Add("-version");
            using var process = Process.Start(info) ?? throw new InvalidOperationException();
            var output = process.StandardOutput.ReadToEndAsync();
            var error = process.StandardError.ReadToEndAsync();
            await process.WaitForExitAsync(lifetime.Token);
            var details = await output + await error;
            if (closed || process.ExitCode != 0) return;
            var firstLine = details.Split('\n', 2)[0].Trim();
            var license = details.Contains("--enable-gpl", StringComparison.Ordinal) ? "GPL enabled in this build" : "GPL components not enabled in this build";
            target.Text = $"{firstLine}\n{license}. See FFmpeg's own license information for full terms.";
        }
        catch (Exception) { if (!closed) target.Text = "FFmpeg was not found at the configured path or on PATH."; }
    }
}
