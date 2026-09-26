using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using System;
using System.Diagnostics;
using System.Reflection;
using System.Threading.Tasks;

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
        panel.Children.Add(new TextBlock { Text = "Project editor shortcuts work when a project is open and processing is idle. Timed segments can also be opened with the keyboard.", TextWrapping = TextWrapping.Wrap });
        AddInformationRow(panel, "Import video", "Ctrl + N");
        AddInformationRow(panel, "Open project folder", "Ctrl + Shift + O");
        AddInformationRow(panel, "Export project", "Ctrl + Shift + E");
        AddInformationRow(panel, "Play or pause", "Ctrl + Shift + Space");
        AddInformationRow(panel, "Editor sections", "Ctrl + 1 through Ctrl + 5");
        AddInformationRow(panel, "Add segment", "Ctrl + Shift + N");
        AddInformationRow(panel, "Follow active segment", "Ctrl + Shift + F");
        AddInformationRow(panel, "Generate narration", "Ctrl + Shift + D");
        AddInformationRow(panel, "Undo / redo segment", "Ctrl + Alt + Z / Ctrl + Alt + Shift + Z");
        AddInformationRow(panel, "Show or hide preview", "Ctrl + Shift + V");
        AddInformationRow(panel, "Rewind / forward five seconds", "Ctrl + Alt + Left / Right");
        AddInformationRow(panel, "Full-screen video", "Ctrl + Alt + F");
        panel.Children.Add(new TextBlock { Text = "You can also use the File, View, Analysis, Segments and Playback menus in an open project. All Projects shows only its own File, View and Help menus.", TextWrapping = TextWrapping.Wrap, Opacity = .7 });
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
        panel.Children.Add(new TextBlock { Text = "Vidi Studio launches the FFmpeg and FFprobe executables installed on this computer or configured under Storage and tools. They are separate processes and are not bundled in this Windows package.", TextWrapping = TextWrapping.Wrap });
        var ffmpegDetails = new TextBlock { Text = "Checking installed FFmpeg…", TextWrapping = TextWrapping.Wrap, Opacity = .7 };
        panel.Children.Add(ffmpegDetails);
        _ = ShowFfmpegDetailsAsync(ffmpegDetails);
        panel.Children.Add(new HyperlinkButton { Content = "FFmpeg license and source information", NavigateUri = new Uri("https://ffmpeg.org/legal.html") });
        panel.Children.Add(new TextBlock { Text = "FFmpeg is generally LGPL, but optional components can make a particular build GPL. The installed executable's configuration determines which terms apply.", TextWrapping = TextWrapping.Wrap, Opacity = .7 });
        panel.Children.Add(new TextBlock { Text = "Vidi Studio also uses the Windows App SDK, .NET, and Python packages listed in the project dependency files. Gemini is an external service used with your own API key.", TextWrapping = TextWrapping.Wrap, Opacity = .7 });
    }

    private async Task ShowFfmpegDetailsAsync(TextBlock target)
    {
        try
        {
            var configured = draft["ffmpegPath"]?.GetValue<string>();
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
