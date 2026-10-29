using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using System;
using System.Diagnostics;
using System.IO;
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
        panel.Children.Add(new TextBlock { Text = "Full Help book", FontSize = 20, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold });
        panel.Children.Add(new TextBlock { Text = "Open the complete Vidi Studio guide in its own window with a searchable table of contents, formatted tables, and every chapter from the Mac Help book.", TextWrapping = TextWrapping.Wrap, Opacity = .75 });
        var openHelp = new Button
        {
            Content = "Open the full Help book",
            HorizontalAlignment = HorizontalAlignment.Left,
            Margin = new Thickness(0, 4, 0, 12)
        };
        ToolTipService.SetToolTip(openHelp, "Open the searchable Vidi Studio Help window (F1)");
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(openHelp, "Open the full Help book");
        openHelp.Click += (_, _) =>
        {
            Hide();
            App.OpenHelpWindow(menuOwner?.Root.ActualTheme ?? ElementTheme.Default);
        };
        panel.Children.Add(openHelp);
        panel.Children.Add(new TextBlock { Text = "Keyboard shortcuts", FontSize = 20, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold });
        panel.Children.Add(new TextBlock { Text = "Shortcuts work when a project is open and processing is idle.", TextWrapping = TextWrapping.Wrap });
        if (menuOwner != null)
            foreach (var (label, shortcut) in menuOwner.DesktopShortcutReference) AddInformationRow(panel, label, shortcut);
        panel.Children.Add(new TextBlock { Text = "File Explorer & desktop integration", FontSize = 17, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold, Margin = new Thickness(0, 18, 0, 0) });
        panel.Children.Add(new TextBlock { Text = "Right-click any supported video file (.mp4, .mov, .m4v, .mkv, .avi, .webm, .wmv) in File Explorer to access \"Describe with Vidi Studio\" directly in the Windows 11 modern context menu, or under Show more options / Shift+F10 in the legacy menu. The packaged app registers the modern menu automatically. For unpackaged builds or a missing legacy entry, turn on Developer mode and use \"Repair legacy File Explorer context menu\" under Storage. You can also share video files to Vidi Studio through the Windows Share flyout.", TextWrapping = TextWrapping.Wrap, Opacity = .8 });
        panel.Children.Add(new TextBlock { Text = "Menus follow the Mac layout: File, View, Playback, Project, Help. Creating new projects, local exports, and on-device narration do not require a Gemini API key; an active API key is only required when invoking automated AI features (transcription, summaries, audio description generation, and AI rewrites). Windows offers Windows OneCore, Windows SAPI 5, Piper, and Gemini voices; Piper voices download on demand and run offline. macOS offers Apple Voices and Gemini voices. Summary read-aloud always uses the same engine and voice chosen for narration.", TextWrapping = TextWrapping.Wrap, Opacity = .7 });
        panel.Children.Add(new TextBlock { Text = "AI availability", FontSize = 17, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold, Margin = new Thickness(0, 18, 0, 0) });
        panel.Children.Add(new TextBlock { Text = "When Gemini reports that a key has no remaining quota or credits, denies access, or is temporarily unavailable, a persistent warning pauses all AI reprocessing: transcripts, summaries, Audio Descriptions, re-analysis, frame text, AI timing, and Gemini narration. Existing work is unchanged and non-AI editing, imports, playback, text exports, and Combine remain available. Choose Retry AI after fixing billing, quota, permissions, or availability. An invalid key is reported separately. Project creation does not require a Gemini API key.", TextWrapping = TextWrapping.Wrap, Opacity = .75 });
        panel.Children.Add(new TextBlock { Text = "Existing assets", FontSize = 17, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold, Margin = new Thickness(0, 18, 0, 0) });
        panel.Children.Add(new TextBlock { Text = "Project > Import Existing Assets… brings a transcript, summary, AD transcript, or AD audio into an open project. Project > Import also offers scoped Import Transcript, Import Summary, and Import Descriptions commands. In the editor, Add shows only the manual and import choices for the current section: transcript, summary, or descriptions. New Project can also open the same import dialog after creating a project. Transcripts and AD transcripts accept SRT, VTT, and TXT. Timing is validated for every type: TXT must be reviewed in the timing editor, while SRT/VTT are checked before import and show a Timing Warnings dialog with Import Anyway, Review Timing, or Cancel. After a transcript or AD narration import, the report offers Edit Transcript Timings. Summaries accept Markdown or TXT, and AD audio accepts WAV, MP3, M4A, AAC, or FLAC. AD narration-only creates editable descriptions; Dialogue + AD combined is stored as one caption/export track. Narration-only audio is ducked under the original; a full AD mix is used directly. Combining keeps Original Audio and adds a switchable English - Audio Description track by default, while Open Audio Description is an explicit single mix. YouTube projects accept text assets only.", TextWrapping = TextWrapping.Wrap, Opacity = .75 });
        panel.Children.Add(new TextBlock { Text = "Import validation and provenance", TextWrapping = TextWrapping.Wrap, Opacity = .75, Margin = new Thickness(0, 8, 0, 0) });
        panel.Children.Add(new TextBlock { Text = "Every import reports imported, flagged, and omitted rows with source lines and reasons. Overlapping descriptions are kept, flagged Needs retiming, and block described exports until fixed; text exports remain available. Imported descriptions and transcripts carry Imported or Imported · modified provenance, while manual picks persist. AD Overview shows narration sources and warns about mixed or multiple imported source files. Per-segment Import narration audio… is available from each description's More options menu and validates that the audio fits the segment.", TextWrapping = TextWrapping.Wrap, Opacity = .75 });
        panel.Children.Add(new TextBlock { Text = "Markdown Summary", TextWrapping = TextWrapping.Wrap, Opacity = .75, Margin = new Thickness(0, 8, 0, 0) });
        panel.Children.Add(new TextBlock { Text = "The Summary editor supports headings, paragraphs, bold, italic, strikethrough, bulleted/numbered/task lists, blockquotes, inline and fenced code, links, images, tables, horizontal rules, preview, and Markdown Help. Shortcuts: Ctrl-B bold, Ctrl-I italic, Ctrl-Shift-X strikethrough, Ctrl-Alt-` inline code, Ctrl-Alt-Shift-C code block, Ctrl-K link, Ctrl-Shift-I image, Ctrl-Alt-1–6 headings, Ctrl-Alt-0 paragraph, Ctrl-Shift-8/7/9 lists, Ctrl-Alt-Shift-Q quote, Ctrl-Alt-T table, Ctrl-Alt-- rule, Ctrl-Alt-Shift-K clear, Ctrl-Alt-Shift-P preview, Ctrl-Alt-Shift-M help, Ctrl-Z/Ctrl-Y undo/redo. Project List/Grid shortcuts (Ctrl-Alt-1/2) are disabled while this editor is active so headings work. Math, Mermaid, footnotes, and embedded remote images are not supported.", TextWrapping = TextWrapping.Wrap, Opacity = .75 });
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
        panel.Children.Add(new TextBlock { Text = "Vidi Studio uses the FFmpeg and FFprobe executables installed on this computer or set under Storage.", TextWrapping = TextWrapping.Wrap });
        var ffmpegDetails = new TextBlock { Text = "Checking installed FFmpeg…", TextWrapping = TextWrapping.Wrap, Opacity = .7 };
        panel.Children.Add(ffmpegDetails);
        _ = ShowFfmpegDetailsAsync(ffmpegDetails);
        panel.Children.Add(new HyperlinkButton { Content = "FFmpeg license and source information", NavigateUri = new Uri("https://ffmpeg.org/legal.html") });
        panel.Children.Add(new TextBlock { Text = "FFmpeg is generally LGPL, but optional components can make a particular build GPL. The installed executable's configuration determines which terms apply.", TextWrapping = TextWrapping.Wrap, Opacity = .7 });
        panel.Children.Add(new TextBlock { Text = "Piper voices", FontSize = 17, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold, Margin = new Thickness(0, 18, 0, 0) });
        panel.Children.Add(new TextBlock { Text = "Vidi Studio includes the Piper speech engine (piper.exe and its runtime files) from the rhasspy/piper project, release 2023.11.14-2, for on-device neural narration.", TextWrapping = TextWrapping.Wrap });
        AddInformationRow(panel, "License", "MIT License");
        panel.Children.Add(new TextBlock { Text = "Piper is Copyright (c) 2022 Rhasspy and contributors.", TextWrapping = TextWrapping.Wrap, Opacity = .7 });
        panel.Children.Add(new HyperlinkButton { Content = "Piper source code and MIT license", NavigateUri = new Uri("https://github.com/rhasspy/piper") });
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
        panel.Children.Add(piperNotice);
        panel.Children.Add(new TextBlock { Text = "Piper voices are downloaded on demand. Each voice remains subject to the license and terms published with that voice.", TextWrapping = TextWrapping.Wrap, Opacity = .7 });
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
