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
    private bool shortcutReferenceExpanded = false;
    private static void AddInformationRow(StackPanel panel, string label, string detail)
    {
        var value = new TextBlock { Text = detail, TextWrapping = TextWrapping.Wrap, Opacity = .7 };
        value.TextAlignment = TextAlignment.Right;
        CompactRow(panel,label,value,true);
    }

    private void BuildHelp(StackPanel panel)
    {
        status.Text = "";
        var guide=CompactGroup(panel,"Help and keyboard shortcuts");
        var openHelp=new Button { Content="Open VidiStudio Help", HorizontalAlignment = HorizontalAlignment.Left };
        ToolTipService.SetToolTip(openHelp,"Open the searchable Vidi Studio Help window (F1)");
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(openHelp,"Open VidiStudio Help");
        openHelp.Click += (_,_) => App.OpenHelpWindow(menuOwner?.CurrentTheme ?? ElementTheme.Default);
        guide.Children.Add(openHelp);
        guide.Children.Add(GeminiRowDivider());
        var shortcuts = new StackPanel();
        if(menuOwner!=null) foreach(var (label,shortcut) in menuOwner.DesktopShortcutReference) AddInformationRow(shortcuts,label,shortcut);
        var reference = new Expander { Header = "Keyboard shortcut reference", Content = shortcuts, IsExpanded = shortcutReferenceExpanded, HorizontalAlignment = HorizontalAlignment.Stretch };
        reference.Expanding += (_, _) => shortcutReferenceExpanded = true;
        reference.Collapsed += (_, _) => shortcutReferenceExpanded = false;
        var menu = MenuAccessKeys.Create();
        var toggle = new ToggleMenuFlyoutItem { Text = "Keyboard shortcut reference", Tag = "settings-shortcut-reference", Icon = new FontIcon { Glyph = "\uE765" } };
        toggle.Click += (_, _) => reference.IsExpanded = toggle.IsChecked;
        menu.Opening += (_, _) => toggle.IsChecked = reference.IsExpanded;
        menu.Items.Add(toggle); reference.ContextFlyout = menu;
        guide.Children.Add(reference);
    }

    private void BuildAbout(StackPanel panel)
    {
        status.Text = "";
        var group=CompactGroup(panel,"VidiStudio");
        string version;
        try
        {
            var package = Windows.ApplicationModel.Package.Current.Id.Version;
            version = $"{package.Major}.{package.Minor}.{package.Build}";
        }
        catch { version = Assembly.GetEntryAssembly()?.GetName().Version?.ToString(3) ?? "Unknown"; }
        AddInformationRow(group, "App version", version);
        group=CompactGroup(panel,"FFmpeg");
        var bundled = Path.Combine(AppContext.BaseDirectory, "engine-bin", "media", "ffmpeg.exe");
        var hasBundled = File.Exists(bundled);
        group.Children.Add(new TextBlock { Text = hasBundled
            ? "VidiStudio bundles FFmpeg and FFprobe for media processing and export."
            : "VidiStudio uses FFmpeg and FFprobe for media processing and export.", TextWrapping = TextWrapping.Wrap });
        var ffmpegVersion = new TextBlock { Text = "Checking…", TextWrapping = TextWrapping.Wrap, TextAlignment = TextAlignment.Right, Opacity = .7 };
        var ffmpegLicense = new TextBlock { Text = "Checking…", TextWrapping = TextWrapping.Wrap, TextAlignment = TextAlignment.Right, Opacity = .7 };
        CompactRow(group,hasBundled ? "Bundled version" : "Installed version",ffmpegVersion,true);
        CompactRow(group,"License",ffmpegLicense,true);
        _ = ShowFfmpegDetailsAsync(hasBundled ? bundled : SettingsStore.GetString(draft["ffmpegPath"]),ffmpegVersion,ffmpegLicense);
        var license = new Button { Content = "Open FFmpeg License" };
        license.Click += async (_, _) => await OpenFfmpegLicenseAsync();
        var licenseMenu = MenuAccessKeys.Create();
        var openLicense = new MenuFlyoutItem { Text = "Open FFmpeg License", Tag = "about-ffmpeg-license", Icon = new FontIcon { Glyph = "\uE8A5" } };
        openLicense.Click += async (_, _) => await OpenFfmpegLicenseAsync();
        licenseMenu.Items.Add(openLicense); license.ContextFlyout = licenseMenu;
        group.Children.Add(license);
        group.Children.Add(new HyperlinkButton { Content = "FFmpeg licensing and source information", NavigateUri = new Uri("https://ffmpeg.org/legal.html") });
        group=CompactGroup(panel,"Piper voices");
        group.Children.Add(new TextBlock { Text = "VidiStudio bundles the Piper speech engine (Piper 2023.11.14-2) for on-device neural narration.", TextWrapping = TextWrapping.Wrap });
        AddInformationRow(group, "License", "MIT License");
        group.Children.Add(new TextBlock { Text = "Piper is Copyright (c) 2022 Rhasspy and contributors.", TextWrapping = TextWrapping.Wrap, Opacity = .7 });
        var piperSource = new HyperlinkButton { Content = "Piper source code", NavigateUri = new Uri("https://github.com/rhasspy/piper") };
        var piperNotice = new Button { Content = "Open Piper License" };
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
        group.Children.Add(piperNotice);
        group.Children.Add(piperSource);
        group.Children.Add(new TextBlock { Text = "Piper voices are downloaded on demand. Each voice remains subject to the license and terms published with that voice.", TextWrapping = TextWrapping.Wrap, Opacity = .7 });
        group=CompactGroup(panel,"Third-party notices");
        group.Children.Add(new TextBlock { Text = "License files for bundled libraries and engine packages.", TextWrapping = TextWrapping.Wrap, Opacity = .7 });
        group.Children.Add(LicenseAction("Open Third-Party License Index", "about-license-index", "THIRD-PARTY-LICENSES.txt"));
        group.Children.Add(LicenseAction("Show All License Files in File Explorer", "about-license-folder", null));
    }

    private Button LicenseAction(string title, string id, string? file)
    {
        async Task Open()
        {
            try
            {
                var folder = Path.Combine(AppContext.BaseDirectory,"engine-bin","licenses");
                if (file == null) await Windows.System.Launcher.LaunchFolderPathAsync(folder);
                else await Windows.System.Launcher.LaunchFileAsync(await Windows.Storage.StorageFile.GetFileFromPathAsync(Path.Combine(folder,file)));
            }
            catch (Exception ex) { model.Engine.Log(ex.ToString()); status.Text = "The bundled license file could not be opened."; }
        }
        var button = new Button { Content = title };
        button.Click += async (_, _) => await Open();
        var menu = MenuAccessKeys.Create();
        var action = new MenuFlyoutItem { Text = title, Tag = id, Icon = new FontIcon { Glyph = "\uE8A5" } };
        action.Click += async (_, _) => await Open();
        menu.Items.Add(action); button.ContextFlyout = menu;
        return button;
    }

    private async Task OpenFfmpegLicenseAsync()
    {
        try
        {
            var file = await Windows.Storage.StorageFile.GetFileFromPathAsync(Path.Combine(AppContext.BaseDirectory,"Assets","FFmpeg-GPLv3.txt"));
            await Windows.System.Launcher.LaunchFileAsync(file);
        }
        catch (Exception ex) { model.Engine.Log(ex.ToString()); status.Text = "The bundled FFmpeg license could not be opened."; }
    }

    private async Task ShowFfmpegDetailsAsync(string executable, TextBlock version, TextBlock license)
    {
        using var timeout = System.Threading.CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token);
        timeout.CancelAfter(TimeSpan.FromSeconds(5));
        try
        {
            var info = new ProcessStartInfo(string.IsNullOrWhiteSpace(executable) ? "ffmpeg" : executable)
            {
                UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true
            };
            info.ArgumentList.Add("-version");
            using var process = Process.Start(info) ?? throw new InvalidOperationException();
            using var stop = timeout.Token.Register(() => { try { process.Kill(true); } catch (InvalidOperationException) { } });
            var output = process.StandardOutput.ReadToEndAsync();
            var error = process.StandardError.ReadToEndAsync();
            await process.WaitForExitAsync(timeout.Token);
            var details = await output + await error;
            if (process.ExitCode != 0) throw new InvalidOperationException("FFmpeg version check failed.");
            if (closed) return;
            var summary = FfmpegVersionInfo.Parse(details);
            version.Text = summary.Version;
            license.Text = summary.License;
        }
        catch (Exception) { if (!closed) { version.Text = "Unavailable"; license.Text = "Unavailable"; } }
    }
#if DEBUG
    internal async Task VerifyInformationForSmokeTestAsync()
    {
        var original = SelectedSection;
        SelectSection(6);
        var reference = SpeechItems<Expander>(currentSectionPanel!).Single(item => item.Header as string == "Keyboard shortcut reference");
        if (reference.IsExpanded || reference.ContextFlyout is not MenuFlyout referenceMenu)
            throw new InvalidOperationException("Help shortcut reference must start collapsed with a matching menu.");
        var referenceToggle = referenceMenu.Items.OfType<ToggleMenuFlyoutItem>().Single();
        var peer = new Microsoft.UI.Xaml.Automation.Peers.ToggleMenuFlyoutItemAutomationPeer(referenceToggle);
        ((Microsoft.UI.Xaml.Automation.Provider.IToggleProvider)peer.GetPattern(Microsoft.UI.Xaml.Automation.Peers.PatternInterface.Toggle)).Toggle();
        if (!reference.IsExpanded || !referenceToggle.IsChecked)
            throw new InvalidOperationException("Help reference menu did not expand the same shortcut list.");
        await Task.Delay(80);
        if (!SpeechItems<TextBlock>(currentSectionPanel!).Any(text => text.Text == "Jump to Time…") ||
            !SpeechItems<TextBlock>(currentSectionPanel!).Any(text => text.Text == "Ctrl+Shift+J"))
            throw new InvalidOperationException("Settings Help lost Jump to Time and its shortcut.");
        var shortcutLabels = SpeechItems<TextBlock>(reference).Select(text => text.Text).ToArray();
        var cloudLabel = Array.IndexOf(shortcutLabels, "Open Cloud Videos…");
        var youtubeLabel = Array.IndexOf(shortcutLabels, "My YouTube Videos…");
        if (cloudLabel < 0 || youtubeLabel <= cloudLabel ||
            !shortcutLabels.Contains("Ctrl+Alt+G") || !shortcutLabels.Contains("Ctrl+Alt+Y"))
            throw new InvalidOperationException("Settings Help must list both video browser shortcuts together.");
        var openHelp = SpeechItems<Button>(currentSectionPanel!).Single(button => button.Content as string == "Open VidiStudio Help");
        var helpPeer = new Microsoft.UI.Xaml.Automation.Peers.ButtonAutomationPeer(openHelp);
        ((Microsoft.UI.Xaml.Automation.Provider.IInvokeProvider)helpPeer.GetPattern(Microsoft.UI.Xaml.Automation.Peers.PatternInterface.Invoke)).Invoke();
        var help = App.HelpWindowForSmokeTest ?? throw new InvalidOperationException("Settings Help did not open the Help book.");
        await help.VerifyGuideForSmokeTestAsync();
        if (!AppWindow.IsVisible || closed) throw new InvalidOperationException("Opening Help must preserve Settings, as on Mac.");
        help.Close();
        SelectSection(7);
        for (var attempt = 0; attempt < 100 && SpeechItems<TextBlock>(currentSectionPanel!).Any(text => text.Text == "Checking…"); attempt++) await Task.Delay(60);
        var blocks = SpeechItems<TextBlock>(currentSectionPanel!).ToArray();
        if (blocks.Any(text => text.Text is "Keyboard shortcuts" or "Jump to Time…" or "Other components" || text.Text.StartsWith("ffmpeg version ")))
            throw new InvalidOperationException("About contains standalone shortcuts, Other components, or diagnostic version output.");
        var rows = SpeechItems<Grid>(currentSectionPanel!).Where(row => row.Tag as string == "settings-row").ToArray();
        foreach (var row in rows)
            if (row.Children[1] is not TextBlock { TextAlignment: TextAlignment.Right })
                throw new InvalidOperationException("About information values are not right aligned.");
        var version = (TextBlock)rows[0].Children[1];
        if (version.Text.Split('.').Length != 3) throw new InvalidOperationException("About must display the shared three-part app version.");
        var ffmpeg = rows.Single(row => ((TextBlock)row.Children[0]).Text is "Bundled version" or "Installed version");
        if (!System.Text.RegularExpressions.Regex.IsMatch(((TextBlock)ffmpeg.Children[1]).Text, @"^\d+(\.\d+)+$"))
            throw new InvalidOperationException("About must display a concise installed or bundled FFmpeg release.");
        var button = SpeechItems<Button>(currentSectionPanel!).Single(item => item.Content as string == "Open FFmpeg License");
        if (button.ContextFlyout is not MenuFlyout menu || menu.Items.OfType<MenuFlyoutItem>().Single().Tag as string != "about-ffmpeg-license" ||
            !File.Exists(Path.Combine(AppContext.BaseDirectory,"Assets","FFmpeg-GPLv3.txt")))
            throw new InvalidOperationException("About lost its bundled GPL text or matching license menu action.");
        SelectSection(6);
        var retained = SpeechItems<Expander>(currentSectionPanel!).Single(item => item.Header as string == "Keyboard shortcut reference");
        if (!retained.IsExpanded) throw new InvalidOperationException("Help reference expansion must survive Settings navigation, as on Mac.");
        retained.IsExpanded = false;
        shortcutReferenceExpanded = false;
        SelectSection(original);
    }
#endif
}
