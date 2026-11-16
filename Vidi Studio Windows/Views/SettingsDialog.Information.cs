using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using Vidi_Studio_Windows.Services;

namespace Vidi_Studio_Windows.Views;

public sealed partial class SettingsDialog
{
    private static void AddInformationRow(StackPanel panel, string label, string detail)
    {
        var value = new TextBlock { Text = detail, TextWrapping = TextWrapping.Wrap, Opacity = .7 };
        CompactRow(panel,label,value,true);
    }

    private void BuildHelp(StackPanel panel)
    {
        var guide=CompactGroup(panel,"Help and keyboard shortcuts");
        var openHelp=new Button { Content="Open the full Help book" };
        ToolTipService.SetToolTip(openHelp,"Open the searchable Vidi Studio Help window (F1)");
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(openHelp,"Open the full Help book");
        openHelp.Click += (_,_) => { Hide(); App.OpenHelpWindow(menuOwner?.CurrentTheme ?? ElementTheme.Default); };
        CompactRow(guide,"VidiStudio Help",openHelp);
        var shortcuts=CompactGroup(panel,"Keyboard shortcuts");
        if(menuOwner!=null) foreach(var (label,shortcut) in menuOwner.DesktopShortcutReference) AddInformationRow(shortcuts,label,shortcut);
    }

    private void BuildAbout(StackPanel panel)
    {
        var group=CompactGroup(panel,"VidiStudio");
        string version;
        try
        {
            var package = Windows.ApplicationModel.Package.Current.Id.Version;
            version = $"{package.Major}.{package.Minor}.{package.Build}.{package.Revision}";
        }
        catch { version = Assembly.GetEntryAssembly()?.GetName().Version?.ToString() ?? "Unknown"; }
        AddInformationRow(group, "App version", version);
        group=CompactGroup(panel,"Keyboard shortcuts");
        if (menuOwner != null)
            foreach (var (label, shortcut) in menuOwner.DesktopShortcutReference.Where(entry => entry.Label == "Jump to Time…"))
                AddInformationRow(group, label, shortcut);

        group=CompactGroup(panel,"FFmpeg");
        group.Children.Add(new TextBlock { Text = "Vidi Studio uses the FFmpeg and FFprobe executables installed on this computer or set under Storage.", TextWrapping = TextWrapping.Wrap });
        var ffmpegDetails = new TextBlock { Text = "Checking installed FFmpeg…", TextWrapping = TextWrapping.Wrap, Opacity = .7 };
        CompactRow(group,"Installed version",ffmpegDetails,true);
        _ = ShowFfmpegDetailsAsync(ffmpegDetails);
        group.Children.Add(new HyperlinkButton { Content = "FFmpeg license and source information", NavigateUri = new Uri("https://ffmpeg.org/legal.html") });
        group.Children.Add(new TextBlock { Text = "FFmpeg is generally LGPL, but optional components can make a particular build GPL. The installed executable's configuration determines which terms apply.", TextWrapping = TextWrapping.Wrap, Opacity = .7 });
        group=CompactGroup(panel,"Piper voices");
        group.Children.Add(new TextBlock { Text = "Vidi Studio includes the Piper speech engine (piper.exe and its runtime files) from the rhasspy/piper project, release 2023.11.14-2, for on-device neural narration.", TextWrapping = TextWrapping.Wrap });
        AddInformationRow(group, "License", "MIT License");
        group.Children.Add(new TextBlock { Text = "Piper is Copyright (c) 2022 Rhasspy and contributors.", TextWrapping = TextWrapping.Wrap, Opacity = .7 });
        var piperSource = new HyperlinkButton { Content = "Piper source code and MIT license", NavigateUri = new Uri("https://github.com/rhasspy/piper") };
        var piperNotice = new Button { Content = "Open bundled Piper notice" };
        piperNotice.Click += async (_, _) =>
        {
            try
            {
                var path = Path.Combine(AppContext.BaseDirectory, "Assets", "piper-notice.txt");
                var file = await Windows.Storage.StorageFile.GetFileFromPathAsync(path);
                await Windows.System.Launcher.LaunchFileAsync(file);
            }
            catch (Exception ex)
            {
                model.Engine.Log(ex.ToString());
                status.Text = "The bundled Piper notice could not be opened.";
            }
        };
        group.Children.Add(CompactActions(piperSource,piperNotice));
        group.Children.Add(new TextBlock { Text = "Piper voices are downloaded on demand. Each voice remains subject to the license and terms published with that voice.", TextWrapping = TextWrapping.Wrap, Opacity = .7 });
        group=CompactGroup(panel,"Other components");
        group.Children.Add(new TextBlock { Text = "Vidi Studio also uses the Windows App SDK, .NET, and Python packages listed in the project dependency files. Gemini is an external service used with your own API key, which is stored securely in Windows Credential Manager rather than in a plain file.", TextWrapping = TextWrapping.Wrap, Opacity = .7 });
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
