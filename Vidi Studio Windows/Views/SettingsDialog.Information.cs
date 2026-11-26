using Microsoft.UI.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Controls;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using Vidi_Studio_Windows.Services;
using Windows.System;
using Windows.UI.Core;

namespace Vidi_Studio_Windows.Views;

public sealed partial class SettingsDialog
{
    private Action? focusHelpShortcutSearch;
    private bool shortcutSearchWindowActive;
#if DEBUG
    private Action<string>? shortcutSearchAnnouncedForSmokeTest;
#endif
    private const string ShortcutSearchHint = "Type an action name or press a shortcut.";
    private bool shortcutReferenceExpanded = false;
    private readonly HashSet<string> expandedShortcutSections = new();
    private readonly HashSet<string> collapsedShortcutSearchSections = new();
    private string shortcutSearchQuery = "";
    private (string Text, string Key)? capturedShortcutSearch;

    private static readonly Dictionary<string, string> ShortcutKeyNames = new()
    {
        ["control"] = "ctrl", ["escape"] = "esc", ["return"] = "enter",
        ["comma"] = ",", ["period"] = ".", ["slash"] = "/", ["equals"] = "=", ["minus"] = "-"
    };

    private static string ShortcutKeyWords(string text) =>
        string.Join(" ", text.ToLowerInvariant().Replace('+', ' ').Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)
            .Select(word => ShortcutKeyNames.GetValueOrDefault(word, word)));

    private static string SingleShortcutAnnouncement(string label, string shortcut)
    {
        var keys = shortcut.Split('+').Select(key => key switch
        {
            "Ctrl" => "Control", "," => "Comma", "=" => "Equals", "-" => "Minus",
            "[" => "Left bracket", "]" => "Right bracket",
            "Left" => "Left arrow", "Right" => "Right arrow",
            _ => key
        });
        return $"1 shortcut found. {label.TrimEnd('.', '…')}. {string.Join(" plus ", keys)}.";
    }

    private static bool IsShortcutKeyDown(VirtualKey key) =>
        InputKeyboardSource.GetKeyStateForCurrentThread(key).HasFlag(CoreVirtualKeyStates.Down);

    private static VirtualKeyModifiers ShortcutModifiers()
    {
        var modifiers = VirtualKeyModifiers.None;
        if (IsShortcutKeyDown(VirtualKey.Control)) modifiers |= VirtualKeyModifiers.Control;
        if (IsShortcutKeyDown(VirtualKey.Menu)) modifiers |= VirtualKeyModifiers.Menu;
        if (IsShortcutKeyDown(VirtualKey.Shift)) modifiers |= VirtualKeyModifiers.Shift;
        return modifiers;
    }

    private void HandleHelpShortcutSearch(object sender, Microsoft.UI.Xaml.Input.KeyRoutedEventArgs e)
    {
        if (focusHelpShortcutSearch is not { } focus || e.Key != VirtualKey.F ||
            ShortcutModifiers() != VirtualKeyModifiers.Control) return;
        e.Handled = true;
        if (!e.KeyStatus.WasKeyDown) focus();
    }

    private static string? CaptureShortcutText(VirtualKey key, VirtualKeyModifiers modifiers)
    {
        var control = modifiers.HasFlag(VirtualKeyModifiers.Control);
        var alt = modifiers.HasFlag(VirtualKeyModifiers.Menu);
        var shift = modifiers.HasFlag(VirtualKeyModifiers.Shift);
        if (key == VirtualKey.Tab) return null;
        // Leave text editing, selection, and insertion-point movement with the TextBox.
        if (control && !alt && (key is VirtualKey.Left or VirtualKey.Right or VirtualKey.Up or VirtualKey.Down or VirtualKey.Home or VirtualKey.End ||
            !shift && key is VirtualKey.A or VirtualKey.C or VirtualKey.V or VirtualKey.X or VirtualKey.Y or VirtualKey.Z or VirtualKey.Back or VirtualKey.Delete or VirtualKey.Insert ||
            shift && key == VirtualKey.Z)) return null;
        if (!control && (alt || shift || (int)key < (int)VirtualKey.F1 || (int)key > (int)VirtualKey.F24)) return null;

        var keyName = key switch
        {
            >= VirtualKey.A and <= VirtualKey.Z => key.ToString(),
            >= VirtualKey.Number0 and <= VirtualKey.Number9 => ((int)key - (int)VirtualKey.Number0).ToString(),
            >= VirtualKey.NumberPad0 and <= VirtualKey.NumberPad9 => "Num " + ((int)key - (int)VirtualKey.NumberPad0),
            >= VirtualKey.F1 and <= VirtualKey.F24 => key.ToString(),
            VirtualKey.Enter or VirtualKey.Escape or VirtualKey.Space or VirtualKey.Delete or
                VirtualKey.Left or VirtualKey.Right or VirtualKey.Up or VirtualKey.Down or VirtualKey.Home or
                VirtualKey.End or VirtualKey.PageUp or VirtualKey.PageDown or VirtualKey.Insert => key.ToString(),
            VirtualKey.Back => "Backspace", VirtualKey.Add => "Num Plus", VirtualKey.Subtract => "Num Minus",
            (VirtualKey)186 => ";", (VirtualKey)187 => "=", (VirtualKey)188 => ",", (VirtualKey)189 => "-",
            (VirtualKey)190 => ".", (VirtualKey)191 => "/", (VirtualKey)192 => "`", (VirtualKey)219 => "[",
            (VirtualKey)220 => "\\", (VirtualKey)221 => "]", (VirtualKey)222 => "'",
            _ => null
        };
        if (keyName is null) return null;
        return (control ? "Ctrl+" : "") + (alt ? "Alt+" : "") + (shift ? "Shift+" : "") + keyName;
    }

    private static TextBlock AddInformationRow(StackPanel panel, string label, string detail)
    {
        var value = new TextBlock { Text = detail, TextWrapping = TextWrapping.Wrap, Opacity = .7 };
        value.TextAlignment = TextAlignment.Right;
        CompactRow(panel,label,value,true);
        return value;
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
        var shortcuts = new StackPanel { Spacing = 6 };
        var reference = new Expander
        {
            Header = "Keyboard shortcut reference", Content = shortcuts, IsExpanded = shortcutReferenceExpanded,
            HorizontalAlignment = HorizontalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Stretch
        };
        reference.RegisterPropertyChangedCallback(Expander.IsExpandedProperty, (_, _) => shortcutReferenceExpanded = reference.IsExpanded);
        var referenceMenu = SetShortcutDisclosureMenu(reference, "Keyboard shortcut reference", "settings-shortcut-reference");
        guide.Children.Add(reference);
        if (menuOwner == null) return;

        var search = new TextBox { PlaceholderText = "Search shortcuts", Text = shortcutSearchQuery };
        AutomationProperties.SetName(search, "Search shortcuts");
        AutomationProperties.SetHelpText(search, ShortcutSearchHint);
        ToolTipService.SetToolTip(search, ShortcutSearchHint);
        AutomationProperties.SetAutomationId(search, "settings-shortcut-reference-search");
        var hint = new TextBlock { Text = ShortcutSearchHint, TextWrapping = TextWrapping.Wrap, Opacity = .75 };
        AutomationProperties.SetAccessibilityView(hint, AccessibilityView.Raw);
        AutomationProperties.SetAutomationId(hint, "settings-shortcut-reference-search-hint");
        var searchRow = new Grid { ColumnSpacing = 12 };
        searchRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(3, GridUnitType.Star) });
        searchRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(2, GridUnitType.Star) });
        search.VerticalAlignment = hint.VerticalAlignment = VerticalAlignment.Center;
        searchRow.Children.Add(search);
        Grid.SetColumn(hint, 1);
        searchRow.Children.Add(hint);
        shortcuts.Children.Add(searchRow);
        var results = new TextBlock { Opacity = .7, TextWrapping = TextWrapping.Wrap };
        AutomationProperties.SetAutomationId(results, "settings-shortcut-reference-results");


        var groups = menuOwner.DesktopShortcutReference.Select(group =>
        {
            var rows = new StackPanel();
            var section = new Expander
            {
                Header = group.Title, Content = rows,
                HorizontalAlignment = HorizontalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Stretch
            };
            ScreenReaderHeading.Mark(section, AutomationHeadingLevel.Level3);
            var id = "settings-shortcut-section-" + group.Title.ToLowerInvariant().Replace(' ', '-');
            SetShortcutDisclosureMenu(section, group.Title, id);
            return (group.Title, Rows: group.Rows.ToArray(), Panel: rows, Section: section);
        }).ToArray();
        var refreshing = false;
        var isSearching = false;
        referenceMenu.Items.Add(new MenuFlyoutSeparator());
        var expand = BulkAction("Expand All", "settings-shortcut-reference-expand-all", "\uE70D", true);
        var collapse = BulkAction("Collapse All", "settings-shortcut-reference-collapse-all", "\uE70E", false);
        shortcuts.Children.Add(CompactActions(expand.Button, collapse.Button));

        referenceMenu.Items.Add(new MenuFlyoutSeparator());
        void FocusSearch()
        {
            reference.IsExpanded = true;
            search.LayoutUpdated -= FocusSearchAfterLayout;
            search.LayoutUpdated += FocusSearchAfterLayout;
            search.DispatcherQueue.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Low, () =>
            {
                reference.UpdateLayout();
                TryFocusSearch();
            });
        }
        void FocusSearchAfterLayout(object? sender, object args) => TryFocusSearch();
        void TryFocusSearch()
        {
            if (closed || !reference.IsExpanded || !ReferenceEquals(currentSectionPanel, panel) ||
                (search.IsLoaded && search.ActualHeight > 0 && search.Focus(FocusState.Programmatic)))
                search.LayoutUpdated -= FocusSearchAfterLayout;
        }
        focusHelpShortcutSearch = FocusSearch;
        var focusSearch = new MenuFlyoutItem
        {
            Text = "Search Shortcuts", Tag = "settings-shortcut-reference-focus-search",
            Icon = new FontIcon { Glyph = "\uE721" }, KeyboardAcceleratorTextOverride = "Ctrl+F"
        };
        focusSearch.Click += (_, _) => FocusSearch();
        referenceMenu.Items.Add(focusSearch);
        var clearSearch = new MenuFlyoutItem
        {
            Text = "Clear Shortcut Search", Tag = "settings-shortcut-reference-clear-search", Icon = new FontIcon { Glyph = "\uE711" }
        };
        clearSearch.Click += (_, _) =>
        {
            search.Text = "";
            if (reference.IsExpanded) search.DispatcherQueue.TryEnqueue(() => search.Focus(FocusState.Programmatic));
        };
        referenceMenu.Items.Add(clearSearch);

        shortcuts.Children.Add(results);
        foreach (var group in groups) shortcuts.Children.Add(group.Section);
        foreach (var group in groups)
        {
            group.Section.RegisterPropertyChangedCallback(Expander.IsExpandedProperty, (_, _) =>
            {
                if (refreshing) return;
                if (isSearching)
                {
                    if (group.Section.IsExpanded) collapsedShortcutSearchSections.Remove(group.Title);
                    else collapsedShortcutSearchSections.Add(group.Title);
                }
                else
                {
                    if (group.Section.IsExpanded) expandedShortcutSections.Add(group.Title);
                    else expandedShortcutSections.Remove(group.Title);
                }
                UpdateBulkActions();
            });
        }

        var resultAnnouncementTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(350) };
        var pendingAnnouncement = "";
        resultAnnouncementTimer.Tick += (_, _) =>
        {
            resultAnnouncementTimer.Stop();
            if (closed || !shortcutSearchWindowActive || !reference.IsExpanded || !search.IsLoaded) return;
            var peer = FrameworkElementAutomationPeer.CreatePeerForElement(search);
            if (peer == null) return;
            peer.RaiseNotificationEvent(AutomationNotificationKind.Other, AutomationNotificationProcessing.MostRecent,
                pendingAnnouncement, "settings-shortcut-reference-results");
#if DEBUG
            shortcutSearchAnnouncedForSmokeTest?.Invoke(pendingAnnouncement);
#endif
        };
        panel.Unloaded += (_, _) =>
        {
            resultAnnouncementTimer.Stop();
            search.LayoutUpdated -= FocusSearchAfterLayout;
        };

        search.PreviewKeyDown += (_, e) =>
        {
            var modifiers = ShortcutModifiers();
            if (IsShortcutKeyDown(VirtualKey.RightMenu) && !IsShortcutKeyDown(VirtualKey.LeftMenu) && modifiers.HasFlag(VirtualKeyModifiers.Control))
                return; // AltGr can type characters on international layouts.
            var text = CaptureShortcutText(e.Key, modifiers);
            if (text is null) return;
            e.Handled = true;
            if (e.KeyStatus.WasKeyDown) return;

            collapsedShortcutSearchSections.Clear();
            capturedShortcutSearch = (text, MainWindow.ShortcutReferenceKey(e.Key, modifiers) ?? text);
            if (search.Text == text) RefreshSearch(announce: true);
            else search.Text = text;
            search.SelectionStart = text.Length;
        };
        search.TextChanged += (_, _) =>
        {
            if (capturedShortcutSearch?.Text != search.Text) capturedShortcutSearch = null;
            var queryChanged = shortcutSearchQuery.Trim() != search.Text.Trim();
            if (queryChanged) collapsedShortcutSearchSections.Clear();
            shortcutSearchQuery = search.Text;
            RefreshSearch(announce: queryChanged);
        };
        RefreshSearch();

        void RefreshSearch(bool announce = false)
        {
            var query = search.Text.Trim();
            isSearching = query.Length != 0;
            var actionWords = query.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
            var exactKey = capturedShortcutSearch is { } capture ? ShortcutKeyWords(capture.Key) : null;
            var keyQuery = ShortcutKeyWords(query);
            var count = 0;
            (string Label, string Shortcut)? firstMatch = null;
            refreshing = true;
            foreach (var group in groups)
            {
                group.Panel.Children.Clear();
                var rowCount = 0;
                foreach (var (label, shortcut) in group.Rows)
                {
                    if (isSearching && !Matches(label, shortcut)) continue;
                    firstMatch ??= (label, shortcut);
                    var key = AddInformationRow(group.Panel, label, shortcut);
                    key.ClearValue(AutomationProperties.LabeledByProperty);
                    rowCount++;
                }
                group.Section.Visibility = rowCount > 0 ? Visibility.Visible : Visibility.Collapsed;
                group.Section.IsExpanded = isSearching
                    ? rowCount > 0 && !collapsedShortcutSearchSections.Contains(group.Title)
                    : expandedShortcutSections.Contains(group.Title);
                count += rowCount;
            }
            refreshing = false;
            var message = count switch { 0 => "No shortcuts found", 1 => "1 shortcut found", _ => $"{count} shortcuts found" };
            results.Text = message;
            results.Visibility = isSearching ? Visibility.Visible : Visibility.Collapsed;
            if (announce)
            {
                pendingAnnouncement = !isSearching ? "Shortcut search cleared"
                    : count == 1 && firstMatch is { } match ? SingleShortcutAnnouncement(match.Label, match.Shortcut)
                    : message;
                resultAnnouncementTimer.Stop();
                resultAnnouncementTimer.Start();
            }
            clearSearch.IsEnabled = search.Text.Length != 0;
            UpdateBulkActions();

            bool Matches(string label, string shortcut)
            {
                var key = ShortcutKeyWords(shortcut);
                if (exactKey is not null) return key == exactKey;
                return actionWords.All(word => label.Contains(word, StringComparison.OrdinalIgnoreCase)) ||
                    (keyQuery.Length > 0 && (" " + key + " ").Contains(" " + keyQuery + " "));
            }
        }

        void UpdateBulkActions()
        {
            expand.Button.IsEnabled = expand.Command.IsEnabled = CanSetAllSections(true);
            collapse.Button.IsEnabled = collapse.Command.IsEnabled = CanSetAllSections(false);
        }

        bool CanSetAllSections(bool expanded) =>
            groups.Any(group => group.Section.Visibility == Visibility.Visible && group.Section.IsExpanded != expanded);

        void SetAllSections(bool expanded)
        {
            if (!CanSetAllSections(expanded)) return;
            foreach (var group in groups)
                if (group.Section.Visibility == Visibility.Visible) group.Section.IsExpanded = expanded;
            if (expanded) reference.IsExpanded = true;
        }

        (Button Button, MenuFlyoutItem Command) BulkAction(string title, string id, string glyph, bool expanded)
        {
            var content = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
            var icon = new FontIcon { Glyph = glyph, FontSize = 12 };
            AutomationProperties.SetAccessibilityView(icon, AccessibilityView.Raw);
            content.Children.Add(icon);
            content.Children.Add(new TextBlock { Text = title });
            var button = new Button { Content = content };
            AutomationProperties.SetName(button, title);
            AutomationProperties.SetAutomationId(button, id);
            var command = new MenuFlyoutItem { Text = title, Tag = id, Icon = new FontIcon { Glyph = glyph } };
            button.Click += (_, _) => SetAllSections(expanded);
            command.Click += (_, _) => SetAllSections(expanded);
            referenceMenu.Items.Add(command);
            return (button, command);
        }
    }

    private static MenuFlyout SetShortcutDisclosureMenu(Expander disclosure, string title, string id)
    {
        var menu = MenuAccessKeys.Create();
        var toggle = new ToggleMenuFlyoutItem { Text = title, Tag = id, Icon = new FontIcon { Glyph = "\uE765" } };
        toggle.Click += (_, _) => disclosure.IsExpanded = toggle.IsChecked;
        menu.Opening += (_, _) => toggle.IsChecked = disclosure.IsExpanded;
        menu.Items.Add(toggle);
        disclosure.ContextFlyout = menu;
        return menu;
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
        var groups = (StackPanel)reference.Content;
        var sections = groups.Children.OfType<Expander>().ToArray();
        var expected = menuOwner!.DesktopShortcutReference.ToArray();
        if (!sections.Select(section => section.Header as string).SequenceEqual(expected.Select(group => group.Title)) ||
            sections.Any(section => section.IsExpanded || AutomationProperties.GetHeadingLevel(section) != AutomationHeadingLevel.Level3))
            throw new InvalidOperationException("Settings Help shortcut sections must begin collapsed in order as screen-reader headings.");
        if (sections.Length != expected.Length ||
            sections.Where((section, index) => !SpeechItems<TextBlock>(section)
                .Select(text => text.Text).SequenceEqual(expected[index].Rows.SelectMany(row => new[] { row.Label, row.Shortcut }))).Any())
            throw new InvalidOperationException("Settings Help shortcut rows must appear under the matching headings.");
        const string expandAll = "settings-shortcut-reference-expand-all";
        const string collapseAll = "settings-shortcut-reference-collapse-all";
        Button BulkButton(string id) => SpeechItems<Button>(groups).Single(button => AutomationProperties.GetAutomationId(button) == id);
        MenuFlyoutItem BulkCommand(string id) => referenceMenu.Items.OfType<MenuFlyoutItem>().Single(item => Equals(item.Tag, id));
        async Task InvokeAsync(AutomationPeer peer)
        {
            ((Microsoft.UI.Xaml.Automation.Provider.IInvokeProvider)peer.GetPattern(PatternInterface.Invoke)).Invoke();
            await Task.Delay(80);
        }
        foreach (var (id, title) in new[] { (expandAll, "Expand All"), (collapseAll, "Collapse All") })
            if (AutomationProperties.GetName(BulkButton(id)) != title ||
                BulkCommand(id).Text != title || BulkCommand(id).Icon == null)
                throw new InvalidOperationException("Shortcut bulk controls must have matching accessible menu actions.");
        void VerifyBulkState(bool canExpand, bool canCollapse)
        {
            if (BulkButton(expandAll).IsEnabled != canExpand || BulkCommand(expandAll).IsEnabled != canExpand ||
                BulkButton(collapseAll).IsEnabled != canCollapse || BulkCommand(collapseAll).IsEnabled != canCollapse)
                throw new InvalidOperationException("Shortcut bulk buttons and menu items must reflect the open sections.");
        }
        VerifyBulkState(canExpand: true, canCollapse: false);
        await InvokeAsync(new ButtonAutomationPeer(BulkButton(expandAll)));
        if (sections.Any(section => !section.IsExpanded))
            throw new InvalidOperationException("Expand All must open every shortcut section.");
        VerifyBulkState(canExpand: false, canCollapse: true);
        reference.IsExpanded = false;
        await InvokeAsync(new MenuFlyoutItemAutomationPeer(BulkCommand(collapseAll)));
        if (reference.IsExpanded || sections.Any(section => section.IsExpanded))
            throw new InvalidOperationException("Collapse All must clear the sections without opening a closed reference.");
        await InvokeAsync(new MenuFlyoutItemAutomationPeer(BulkCommand(expandAll)));
        if (!reference.IsExpanded || sections.Any(section => !section.IsExpanded))
            throw new InvalidOperationException("Expand All from the menu must reveal the reference and every section.");
        await InvokeAsync(new ButtonAutomationPeer(BulkButton(collapseAll)));
        if (!reference.IsExpanded || sections.Any(section => section.IsExpanded))
            throw new InvalidOperationException("Collapse All must leave the reference open for further navigation.");
        var playback = sections.Single(section => section.Header as string == "Video Playback");
        var playbackPeer = FrameworkElementAutomationPeer.CreatePeerForElement(playback)!;
        var playbackDisclosure = (Microsoft.UI.Xaml.Automation.Provider.IExpandCollapseProvider)playbackPeer.GetPattern(PatternInterface.ExpandCollapse);
        playbackDisclosure.Expand();
        var projects = sections[0];
        var projectsMenu = (MenuFlyout)projects.ContextFlyout;
        var projectsToggle = projectsMenu.Items.OfType<ToggleMenuFlyoutItem>().Single();
        var projectsPeer = new ToggleMenuFlyoutItemAutomationPeer(projectsToggle);
        ((Microsoft.UI.Xaml.Automation.Provider.IToggleProvider)projectsPeer.GetPattern(PatternInterface.Toggle)).Toggle();
        if (!playback.IsExpanded || !projects.IsExpanded || sections.Count(section => section.IsExpanded) != 2)
            throw new InvalidOperationException("Shortcut sections must expand independently through accessibility and their menus.");
        playbackDisclosure.Collapse();
        if (playback.IsExpanded || !projects.IsExpanded)
            throw new InvalidOperationException("Collapsing one shortcut section must keep other sections open.");
        VerifyBulkState(canExpand: true, canCollapse: true);
        var newProjectKey = SpeechItems<TextBlock>(sections[0]).Single(text => text.Text == "Ctrl+N");
        if (Microsoft.UI.Xaml.Automation.Peers.FrameworkElementAutomationPeer.CreatePeerForElement(newProjectKey)?.GetName() != newProjectKey.Text)
            throw new InvalidOperationException("Settings Help must expose the shortcut itself after its action.");
        if (!SpeechItems<TextBlock>(currentSectionPanel!).Any(text => text.Text == "Jump to Time…") ||
            !SpeechItems<TextBlock>(currentSectionPanel!).Any(text => text.Text == "Ctrl+Shift+J"))
            throw new InvalidOperationException("Settings Help lost Jump to Time and its shortcut.");
        var shortcutLabels = SpeechItems<TextBlock>(reference).Select(text => text.Text).ToArray();
        foreach (var (label, key) in new[] {
            ("Generate or Regenerate Transcript…", "Ctrl+Alt+Shift+T"),
            ("Generate or Regenerate Summary…", "Ctrl+Alt+Shift+S"),
            ("Generate or Regenerate Audio Descriptions…", "Ctrl+Alt+Shift+D"),
            ("Generate or Regenerate All AD Audio…", "Ctrl+Alt+Shift+N")
        })
        {
            var index = Array.IndexOf(shortcutLabels, label);
            if (index < 0 || index + 1 >= shortcutLabels.Length || shortcutLabels[index + 1] != key)
                throw new InvalidOperationException("Settings Help lost the generation shortcut for " + label);
        }
        var cloudLabel = Array.IndexOf(shortcutLabels, "Open Cloud Videos…");
        var youtubeLabel = Array.IndexOf(shortcutLabels, "My YouTube Videos…");
        if (cloudLabel < 0 || youtubeLabel <= cloudLabel ||
            !shortcutLabels.Contains("Ctrl+Alt+G") || !shortcutLabels.Contains("Ctrl+Alt+Y"))
            throw new InvalidOperationException("Settings Help must list both video browser shortcuts together.");
        var search = SpeechItems<TextBox>(groups).Single(box =>
            AutomationProperties.GetAutomationId(box) == "settings-shortcut-reference-search");
        var searchPeer = new TextBoxAutomationPeer(search);
        var hint = SpeechItems<TextBlock>(groups).Single(text => text.Text == ShortcutSearchHint);
        if (search.Parent is not Grid searchRow || !ReferenceEquals(hint.Parent, searchRow) || Grid.GetColumn(hint) != 1 ||
            hint.TextWrapping != TextWrapping.Wrap || AutomationProperties.GetHelpText(search) != hint.Text ||
            AutomationProperties.GetAccessibilityView(hint) != AccessibilityView.Raw)
            throw new InvalidOperationException("Shortcut search must show its wrapping hint beside the field and expose it once as field help.");
        var focusSearch = BulkCommand("settings-shortcut-reference-focus-search");
        var clearSearch = BulkCommand("settings-shortcut-reference-clear-search");
        if (searchPeer.GetName() != "Search shortcuts" || focusSearch.Icon == null || clearSearch.Icon == null || clearSearch.IsEnabled)
            throw new InvalidOperationException("Shortcut search must have an accessible field and matching search commands.");
        reference.IsExpanded = false;
        await InvokeAsync(new MenuFlyoutItemAutomationPeer(focusSearch));
        for (var attempt = 0; attempt < 40 && search.FocusState == FocusState.Unfocused; attempt++) await Task.Delay(25);
        if (!reference.IsExpanded || search.FocusState == FocusState.Unfocused)
            throw new InvalidOperationException("Search Shortcuts must open the reference and focus the field.");
        void VerifyJumpToTimeResult()
        {
            if (!playback.IsExpanded || sections.Count(section => section.Visibility == Visibility.Visible) != 1 ||
                !SpeechItems<TextBlock>(playback).Select(text => text.Text).SequenceEqual(new[] { "Jump to Time…", "Ctrl+Shift+J" }))
                throw new InvalidOperationException("A matching query must filter rows and open the collapsed playback section.");
            VerifyBulkState(canExpand: false, canCollapse: true);
        }
        search.Text = "jump to time";
        await Task.Delay(80);
        VerifyJumpToTimeResult();
        await InvokeAsync(new ButtonAutomationPeer(BulkButton(collapseAll)));
        VerifyBulkState(canExpand: true, canCollapse: false);
        search.Text = "Control + Shift + J";
        await Task.Delay(80);
        VerifyJumpToTimeResult();
        search.Text = "no such shortcut";
        await Task.Delay(80);
        if (sections.Any(section => section.Visibility == Visibility.Visible) ||
            !SpeechItems<TextBlock>(groups).Any(text => text.Text == "No shortcuts found"))
            throw new InvalidOperationException("Shortcut search must show an accessible empty state.");
        VerifyBulkState(canExpand: false, canCollapse: false);
        await InvokeAsync(new MenuFlyoutItemAutomationPeer(clearSearch));
        if (search.Text != "" || clearSearch.IsEnabled ||
            sections.Any(section => section.Visibility != Visibility.Visible) || playback.IsExpanded || !projects.IsExpanded)
            throw new InvalidOperationException("Clearing the query must restore the previous section expansion.");
        var announcements = new List<string>();
        shortcutSearchAnnouncedForSmokeTest = announcements.Add;
        try
        {
            foreach (var (query, expectedStatus) in new[] {
                ("Jump to Time", "1 shortcut found. Jump to Time. Control plus Shift plus J."),
                ("New Project", "1 shortcut found. New Project. Control plus N."),
                ("Ctrl+,", "1 shortcut found. Settings. Control plus Comma."),
                ("zzzz unavailable shortcut", "No shortcuts found"),
                ("different unavailable shortcut", "No shortcuts found"),
                ("Ctrl", $"{expected.SelectMany(group => group.Rows).Count(row => row.Shortcut.StartsWith("Ctrl+"))} shortcuts found")
            })
            {
                var previousCount = announcements.Count;
                search.Text = query;
                for (var attempt = 0; attempt < 50 && announcements.Count == previousCount; attempt++) await Task.Delay(25);
                if (announcements.Count != previousCount + 1 || announcements[^1] != expectedStatus)
                    throw new InvalidOperationException("Shortcut search did not emit the result count for " + query);
            }
            search.Text = "";
        }
        finally { shortcutSearchAnnouncedForSmokeTest = null; }
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
        var retainedSections = ((StackPanel)retained.Content).Children.OfType<Expander>();
        if (!retained.IsExpanded || !retainedSections.Where(section => section.IsExpanded)
            .Select(section => section.Header as string).SequenceEqual(new[] { "Projects and Windows" }))
            throw new InvalidOperationException("Help reference and section expansion must survive Settings navigation, as on Mac.");
        retained.IsExpanded = false;
        shortcutReferenceExpanded = false;
        expandedShortcutSections.Clear();
        SelectSection(original);
    }
#endif
}
