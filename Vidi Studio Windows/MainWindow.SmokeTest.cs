#if DEBUG
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Documents;
using Microsoft.UI.Xaml.Input;
using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.UI.Xaml.Media.Imaging;
using Vidi_Studio_Windows.Services;
using Windows.Graphics.Imaging;
using Windows.Storage.Streams;

namespace Vidi_Studio_Windows;

public sealed partial class MainWindow
{
    private static int smokeTestClaimed;
    private System.Collections.Generic.List<string>? observedGenerationAnnouncements;
    private async Task VerifyGenerationAnnouncementsForSmokeTestAsync()
    {
        ShowEditor();
        Activate();
        await Task.Delay(250);
        observedGenerationAnnouncements = new();
        async Task VerifyStageAsync(string message)
        {
            for (var retry = 0; retry < 40 && observedGenerationAnnouncements.LastOrDefault() != message; retry++)
                await Task.Delay(25);
            if (!StatusRing.IsActive || StatusRing.Visibility != Visibility.Visible ||
                observedGenerationAnnouncements.LastOrDefault() != message)
                throw new InvalidOperationException("Missing notification from the visible progress indicator: " + message);
        }
        const string transcript = "Processing. Generating transcript.";
        const string summary = "Processing. Generating summary.";
        const string descriptions = "Processing. Generating descriptions.";
        try
        {
            Model.BeginGenerationForSmokeTest("regenerate-all");
            await VerifyStageAsync(transcript);
            Model.ReportStatus("Waiting for the transcript response…");
            Model.SetGenerationStageForSmokeTest("transcript");
            Model.SetGenerationStageForSmokeTest("summary");
            await VerifyStageAsync(summary);
            Model.SetGenerationStageForSmokeTest("descriptions");
            await VerifyStageAsync(descriptions);
            if (!observedGenerationAnnouncements.SequenceEqual(new[] { transcript, summary, descriptions }))
                throw new InvalidOperationException("Analysis stage notifications were repeated or out of order.");
            Model.EndGenerationForSmokeTest();
            foreach (var (command, message) in new[] {
                ("reanalyze-transcript-segment", transcript), ("generate-summary", summary), ("reanalyze-segment", descriptions)
            }) {
                observedGenerationAnnouncements.Clear();
                Model.BeginGenerationForSmokeTest(command);
                await VerifyStageAsync(message);
                Model.ReportStatus("Updating saved results…");
                await Task.Delay(100);
                if (observedGenerationAnnouncements.Count != 1)
                    throw new InvalidOperationException("Repeated progress announcement for " + command);
                Model.EndGenerationForSmokeTest();
            }
            observedGenerationAnnouncements.Clear();
            Model.BeginGenerationForSmokeTest("analyze", new[] { "--start-stage", "generate-summary" });
            await VerifyStageAsync(summary);
            if (observedGenerationAnnouncements.Count != 1)
                throw new InvalidOperationException("Announced a stage that Analyze Video skipped.");
        }
        finally
        {
            Model.EndGenerationForSmokeTest();
            observedGenerationAnnouncements = null;
        }
    }
    private DialogPresentationVerifier? dialogPresentationVerifier;
    private Window? settingsLifetimeTestHost;
    private async Task RunSmokeTestAsync(string report)
    {
        using var presentation = new DialogPresentationVerifier();
        dialogPresentationVerifier = presentation;
        try
        {
            var folder = Environment.GetEnvironmentVariable("VIDISTUDIO_UI_SMOKE_PROJECT");
            if (folder == null) throw new InvalidOperationException("Smoke project is required.");
            Model.Open(folder);
            if (Environment.GetEnvironmentVariable("VIDISTUDIO_UI_SMOKE_GENERATION_ANNOUNCEMENTS_ONLY") == "1")
            {
                await VerifyGenerationAnnouncementsForSmokeTestAsync();
                await File.WriteAllTextAsync(report, "PASS: visible editor spinner sent native UI Automation notifications for every active transcript, summary and description generation stage and for individual transcript and description re-analysis; duplicate updates and skipped stages were silent.");
                return;
            }
            if (Environment.GetEnvironmentVariable("VIDISTUDIO_UI_SMOKE_GEMINI_VOICES_ONLY") == "1")
            {
                ShowEditor();
                var geminiVoiceSettings = new Views.SettingsDialog(Model, this, 2);
                var geminiDisplay = geminiVoiceSettings.ShowAsync();
                var savedTheme = Model.Settings.Get("theme", "system");
                try
                {
                    await Task.Delay(250);
                    var ordinary = geminiVoiceSettings.AppWindow.Size;
                    foreach (var (theme, width) in new[] { ("light", ordinary.Width), ("dark", Math.Min(ordinary.Width, 740)) })
                    {
                        PreviewTheme(theme);
                        geminiVoiceSettings.AppWindow.Resize(new Windows.Graphics.SizeInt32(width, ordinary.Height));
                        await Task.Delay(150);
                        await geminiVoiceSettings.VerifyProviderSwitchingAsync();
                    }
                }
                finally { geminiVoiceSettings.Close(); await geminiDisplay; PreviewTheme(savedTheme); }
                await File.WriteAllTextAsync(report, "PASS: inline Gemini Narration controls before on-device Summary voice, full-width search, filtered accessible rows, selection retention, and playback lock in light ordinary and dark compact Settings.");
                return;
            }
            if (Environment.GetEnvironmentVariable("VIDISTUDIO_UI_SMOKE_SHELL_OPEN_ONLY") == "1")
            {
                await VerifyShellProjectOpeningForSmokeTestAsync(folder);
                await File.WriteAllTextAsync(report, "PASS: separate maximized editors, unchanged normal/maximized/minimized All Projects, accessible handoff and editor reuse. Saved private projects open without their owner account, with and without a local playback copy, while remote analysis stays blocked.");
                return;
            }
            if (Environment.GetEnvironmentVariable("VIDISTUDIO_UI_SMOKE_YOUTUBE_LAYOUT_ONLY") == "1")
            {
                await VerifyYouTubeLayoutForSmokeTestAsync(report);
                await File.WriteAllTextAsync(report, "PASS: Pasted YouTube links show unlisted visibility and clear the preview and block creation after a private or unavailable response. Packaged Google Cloud and YouTube sign-in return pages include the app icon and status. YouTube browser compact controls, independent scrolling list/table, selection/filter/layout controls and separate account window; profile/action row layout and links; My YouTube Videos absent and blocked in local and YouTube editors, present only in All Projects. Synthetic account/video data; no live sign-in or network calls.");
                return;
            }
            if (Environment.GetEnvironmentVariable("VIDISTUDIO_UI_SMOKE_PROJECT_SEARCH_ONLY") == "1")
            {
                ShowEditor();
                await Task.Delay(300);
                await VerifyEditorPaneGeometryAsync(report);
                await VerifyProjectSearchForSmokeTestAsync();
                await File.WriteAllTextAsync(report, "PASS: native editor/all-project search focus, live typing and immediate segment restoration after manual/icon/menu clearing; all saved versions and drafts; four prefix labels navigate before a colon or query, with tab isolation and scoped results; complete matching summary sentences; read-only results; selected version unchanged while typing; old-result opening; draft Review Later and Keep Both; all-occurrence highlights in four tabs; Appearance toggle/color/context menus, live sample preview with both matches and readable contrast, immediate preview updates, and live saved preferences; all-highlight default restoration from the button and menu below search settings; narrow layout; native spoken search counts, coalesced typing, zero matches and gallery clearing.");
                return;
            }
            if (Environment.GetEnvironmentVariable("VIDISTUDIO_UI_SMOKE_GALLERY_SETTINGS_ONLY") == "1")
            {
                await VerifyInspectorAIProcessingForSmokeTestAsync();
                await VerifyGalleryAndSettingsLayoutForSmokeTestAsync(report);
                await VerifySettingsWindowLifetimeForSmokeTestAsync();
                await File.WriteAllTextAsync(report, "PASS: Cloud browse handoff, configured bucket, import byte and step progress, and dialog completion; Help/About shared groups, Jump to Time reference and retained expansion, short FFmpeg/license rows and license actions, editor/gallery About menu while idle/processing and minimized, Help keeps Settings open, light/dark opaque Help caption hover/pressed colors; native inspector disclosure names and expanded/collapsed states; shared project language first, no per-section targets or review filter; spoken gallery/timeline durations; named inspector heading/actions/toggles and Start/End boundaries in all three segment tabs; aligned gallery controls and centered search at wide, compact and narrow widths in light/dark; editor preview tooltips, video and timeline collapse, and timeline preference restoration; tooltip coverage across all five editor sections and native pointer hover on Generate, Add, and Edit; Settings stable navigation, headings and bounded scrolling; Settings stays open with another main window, closes after the final main window and saves pending preferences.");
                return;
            }
            if (Environment.GetEnvironmentVariable("VIDISTUDIO_UI_SMOKE_CONTEXT_MENUS_ONLY") == "1")
            {
                ShowEditor();
                await Task.Delay(300);
                await VerifyMatchingContextMenusForSmokeTestAsync(report);
                await File.WriteAllTextAsync(report, "PASS: Mac/Windows context hierarchy, order, separators, dynamic titles, offline export, content deletion, developer/source filtering, clicked target guards, editor sidebar, row menus, summary reading menu and background scope.");
                return;
            }
            ShowEditor();
            if (Environment.GetEnvironmentVariable("VIDISTUDIO_UI_SMOKE_EXPORT_SOUND_ONLY") == "1")
            {
                var soundSettings = new Views.SettingsDialog(Model, this, 2);
                var soundDisplay = soundSettings.ShowAsync();
                try { await Task.Delay(200); await soundSettings.VerifyExportSoundForSmokeTestAsync(); await soundSettings.VerifySoundPreviewsForSmokeTestAsync(); }
                finally { soundSettings.Close(); await soundDisplay; }
                var initial = CompletionSound.PlayCountForSmokeTest;
                Model.Settings.Set("soundAfterExport", true);
                var soundResult = await Model.RunAsync("export-subtitles", new[] { "--content", "transcript", "--format", "txt", "--replace-existing" });
                if (!soundResult.Any(e => e.Data.TryGetProperty("export", out _)) || CompletionSound.PlayCountForSmokeTest != initial + 1)
                    throw new InvalidOperationException($"Successful export cue mismatch: {Model.Status}; events={soundResult.Count}; plays={CompletionSound.PlayCountForSmokeTest - initial}.");
                Model.Settings.Set("soundAfterExport", false);
                await Model.RunAsync("export-subtitles", new[] { "--content", "transcript", "--format", "txt", "--replace-existing" });
                if (CompletionSound.PlayCountForSmokeTest != initial + 1) throw new InvalidOperationException("Disabled cue played.");
                Model.Settings.Set("soundAfterExport", true);
                await Model.RunAsync("export-not-a-real-command");
                if (CompletionSound.PlayCountForSmokeTest != initial + 1) throw new InvalidOperationException("Failed export played a completion cue.");
                var cancelled = Model.RunAsync("export-subtitles", new[] { "--content", "transcript", "--format", "txt", "--replace-existing" });
                Model.Cancel();
                await cancelled;
                if (CompletionSound.PlayCountForSmokeTest != initial + 1) throw new InvalidOperationException("Cancelled export played a completion cue.");
                var generationInitial = CompletionSound.PlayCountForSmokeTest;
                string[] generationCommands = ["transcribe", "generate-summary", "generate-ad-cues", "analyze",
                    "regenerate-all", "reanalyze-transcript-segment", "reanalyze-segment"];
                foreach (var command in generationCommands)
                {
                    CompletionSound.Complete(command, false, true, true, false, false, true);
                    if (CompletionSound.PlayCountForSmokeTest != ++generationInitial)
                        throw new InvalidOperationException($"Successful {command} did not dispatch a completion cue.");
                    CompletionSound.Complete(command, true, false, true, false, false, true);
                    CompletionSound.Complete(command, false, true, false, false, false, true);
                    CompletionSound.Complete(command, false, true, true, true, false, true);
                    CompletionSound.Complete(command, false, true, true, false, false, false);
                    if (CompletionSound.PlayCountForSmokeTest != generationInitial)
                        throw new InvalidOperationException($"Disabled, failed, cancelled or incomplete {command} played a cue.");
                }
                Model.Settings.Save();
                await File.WriteAllTextAsync(report, "PASS: shared WAV, five native previews, independent export and generation switches with checked context menus; real text export and simulated generation/re-analysis completions dispatch one cue when enabled; disabled, failed, cancelled and incomplete operations remain silent.");
                return;
            }
            if (Environment.GetEnvironmentVariable("VIDISTUDIO_UI_SMOKE_JUMP_ONLY") == "1")
            {
                await VerifyJumpToTimeForSmokeTestAsync();
                await VerifyPrivateYouTubePlaybackForSmokeTestAsync();
                await File.WriteAllTextAsync(report, "PASS: Jump to Time, private YouTube playback, matching Studio links, and Project Settings imported filenames, deletion review, and refresh.");
                return;
            }
            VerifyProjectConfigurationDeveloperModeForSmokeTest();
            await VerifySegmentFiltersForSmokeTestAsync();
            if (Environment.GetEnvironmentVariable("VIDISTUDIO_UI_SMOKE_FILTERS_ONLY") == "1")
            {
                await File.WriteAllTextAsync(report, "PASS: native Apply Filter, Clear Filter, Cancel, and all four sort commands in Transcript, Descriptions, and AD Transcript, in viewing and editing modes, without project changes.");
                return;
            }
            await VerifyTimingValidationNavigationForSmokeTestAsync();
            if (Environment.GetEnvironmentVariable("VIDISTUDIO_UI_SMOKE_DIALOGS_ONLY") == "1")
            {
                await VerifyCompanionDialogsForSmokeTestAsync(report);
                await VerifyDuplicateProjectDialogForSmokeTestAsync(report);
                await VerifyRegenerationConfirmationsForSmokeTestAsync();
                await File.WriteAllTextAsync(report, "PASS: one hidden, content-fitted native window per dialog presentation, no visible startup resize or nested animation; light/dark initial presentation and live light/dark/system themes; dynamic content growth, cancellation before presentation, delete gating, Primary/Secondary/Cancel results, and duplicate New Project guidance with no writes to the existing project. All three saved generation actions show a named, Cancel-default confirmation and Cancel leaves the project untouched.");
                return;
            }
            VerifyContextualMenusForSmokeTest();
            Section.SelectedIndex = 2; UpdateDesktopMenuStates();
            if (Model.Project is not { } openedProject ||
                Narration.IsOn != ProjectPlaybackPreferences.NarrationEnabled(Model.Settings, openedProject) ||
                Follow.IsOn != ProjectPlaybackPreferences.FollowEnabled(Model.Settings, openedProject))
                throw new InvalidOperationException("Opening a project did not restore its saved playback toggles.");
            if (!MainMenu.Items.Select(item => item.Title).SequenceEqual(new[] { "File", "Edit", "View", "Playback", "Project", "YouTube", "Help" }))
                throw new InvalidOperationException("Editor menu set does not match the active page.");
            var copyMenu = editorMenus.Single(menu => menu.Title == "Edit");
            if (!copyMenu.Items.OfType<MenuFlyoutItem>().Where(item => item.Visibility == Visibility.Visible).Select(item => (string)item.Tag).SequenceEqual(new[] { "copy-to", "segment-edit-mode" }))
                throw new InvalidOperationException("Edit must expose Copy and Translate followed by Edit/Done Editing.");
            var youtubeMenu = editorMenus.Single(menu => menu.Title == "YouTube");
            if (!youtubeMenu.Items.OfType<MenuFlyoutItem>().Select(item => (string)item.Tag).SequenceEqual(new[] {
                "youtube-browser", "youtube-accounts", "youtube-list", "youtube-table", "youtube-open-editor",
                "youtube-open-browser", "youtube-refresh-list", "youtube-load-videos", "youtube-load-playlists" }))
                throw new InvalidOperationException("YouTube menu commands do not match the shared desktop layout.");
            if (galleryMenus.Concat(editorMenus).SelectMany(menu => menu.Items).OfType<Microsoft.UI.Xaml.Controls.MenuFlyoutItem>().Any(item => item.Icon == null) ||
                SegmentMenu(Model.Project!.Cues[0]).Items.OfType<Microsoft.UI.Xaml.Controls.MenuFlyoutItem>().Any(item => item.Icon == null))
                throw new InvalidOperationException("A menu or segment action has no icon.");
            if (desktopMenuItems["settings"].Any(item => item.KeyboardAcceleratorTextOverride != "Ctrl+,") ||
                desktopMenuItems["project-settings"].Any(item => item.KeyboardAcceleratorTextOverride != "Ctrl+Shift+,"))
                throw new InvalidOperationException("File Settings commands did not display their keyboard shortcuts.");
            if (desktopMenuItems["export"].Last().KeyboardAcceleratorTextOverride != "Ctrl+Shift+E" ||
                desktopMenuItems["play"].Last().KeyboardAcceleratorTextOverride != "Ctrl+Shift+Space")
                throw new InvalidOperationException("Menu commands do not show their keyboard shortcuts.");
            var narrationMenu = editorMenus.First(menu => menu.Title == "Project").Items
                .OfType<Microsoft.UI.Xaml.Controls.MenuFlyoutSubItem>().Single(item => item.Text == "Narration");
            var narrationGroups = narrationMenu.Items.OfType<Microsoft.UI.Xaml.Controls.MenuFlyoutSubItem>().ToList();
            if (!narrationGroups.Select(item => item.Text).SequenceEqual(new[] { "AD Segment Generation", "Narration Audio" }) ||
                narrationGroups.Any(item => item.Icon == null))
                throw new InvalidOperationException("Narration menu groups must match the Inspector groups.");
            var generationMenu = narrationGroups[0];
            var audioMenu = narrationGroups[1];
            if (!generationMenu.Items.OfType<Microsoft.UI.Xaml.Controls.MenuFlyoutItem>().Select(item => (string)item.Tag)
                .SequenceEqual(new[] { "generate-ad-cues", "delete-descriptions" }))
                throw new InvalidOperationException("AD generation and deletion must be grouped separately from narration audio.");
            var reviewIssueItem = audioMenu.Items.OfType<Microsoft.UI.Xaml.Controls.MenuFlyoutItem>()
                .FirstOrDefault(item => item.Text == "Review Narration Issues…");
            // Nested MenuFlyoutSubItem children report IsEnabled=false until their flyout is
            // opened, so verify the command gate and the item metadata instead of the unfocused
            // flyout's effective enabled state.
            if (reviewIssueItem is not { Icon: not null } || (string?)reviewIssueItem.Tag != "review-narration-issues" ||
                !CanRunDesktopCommand("review-narration-issues"))
                throw new InvalidOperationException(
                    $"Project → Narration is missing the narration issue review action. " +
                    $"text={reviewIssueItem?.Text ?? "<missing>"}, enabled={reviewIssueItem?.IsEnabled}, " +
                    $"icon={reviewIssueItem?.Icon is not null}, editor={desktopEditorVisible}, idle={Model.Idle}.");
            var narrationReviewItems = audioMenu.Items.OfType<Microsoft.UI.Xaml.Controls.MenuFlyoutItem>().ToList();
            var issuesIndex = narrationReviewItems.FindIndex(item => (string?)item.Tag == "review-narration-issues");
            if (issuesIndex < 0 || issuesIndex + 1 >= narrationReviewItems.Count ||
                (string?)narrationReviewItems[issuesIndex + 1].Tag != "review-narration-voices" ||
                narrationReviewItems[issuesIndex + 1].Icon == null ||
                !CanRunDesktopCommand("review-narration-voices"))
                throw new InvalidOperationException("Review Narration Voices must follow Review Narration Issues.");
            var projectGroups = editorMenus.First(menu => menu.Title == "Project").Items
                .OfType<Microsoft.UI.Xaml.Controls.MenuFlyoutSubItem>().Select(item => item.Text);
            if (!narrationReviewItems.Select(item => (string)item.Tag).SequenceEqual(new[] {
                "synthesize-tts", "synthesize-changed", "review-narration-issues", "review-narration-voices", "delete-narration" }) ||
                (string)InspectorSynthesizeNarrationButton.Tag != "synthesize-tts")
                throw new InvalidOperationException("Narration Audio must have exactly two bulk generation actions.");
            if (!projectGroups.SequenceEqual(new[] { "Editor Sections", "Segments", "Analysis", "YouTube", "Narration", "Import" }))
                throw new InvalidOperationException("Project submenu nesting/order drifted from Mac.");
            var projectYoutubeMenu = editorMenus.Single(menu => menu.Title == "Project").Items
                .OfType<MenuFlyoutSubItem>().Single(item => item.Text == "YouTube");
            if (!projectYoutubeMenu.Items.OfType<MenuFlyoutItem>().Select(item => (string)item.Tag)
                .SequenceEqual(new[] { "youtube-details", "youtube-refresh", "youtube-captions" }))
                throw new InvalidOperationException("Project YouTube commands drifted from the shared desktop layout.");
            if (desktopMenuItems.SelectMany(pair => pair.Value).Any(item => item.Icon == null))
                throw new InvalidOperationException("A nested desktop menu command has no icon.");
            var fallbackIcons = desktopMenuItems.Where(pair => pair.Value.Any(item => item.Icon is FontIcon { Glyph: "\uE8A5" or "\uE712" }))
                .Select(pair => pair.Key).Concat(desktopMenuGroups.Where(group => group.Item.Icon is FontIcon { Glyph: "\uE8A5" or "\uE712" }).Select(group => group.Name));
            if (fallbackIcons.Any())
                throw new InvalidOperationException("Desktop actions still use generic document or fallback icons: " + string.Join(", ", fallbackIcons));
            var savedMenuKey = Model.Settings.Get("apiKey");
            var savedMenuTts = Model.Settings.Get("ttsMode", "windows-core");
            Model.Settings.Set("apiKey", ""); Model.Settings.Set("ttsMode", "windows-core");
            UpdateDesktopMenuStates();
            var canExportOffline = CanRunDesktopCommand("export");
            VerifyProjectMenuEntriesForSmokeTest();
            var canSynthesizeOffline = CanRunDesktopCommand("synthesize-tts");
            var canReviewIssuesOffline = CanRunDesktopCommand("review-narration-issues");
            var canReviewVoicesOffline = CanRunDesktopCommand("review-narration-voices");
            var canTranscribeOffline = CanRunDesktopCommand("transcribe");
            var canCreateOffline = CanRunDesktopCommand("new-project");
            if (!canExportOffline || !canSynthesizeOffline ||
                !canReviewIssuesOffline || !canReviewVoicesOffline || canTranscribeOffline ||
                !canCreateOffline)
                // New Project stays available without an API key, matching the Mac menu; its dialog applies the key gate.
                throw new InvalidOperationException(
                    $"Offline menu actions and AI prerequisites are not independently guarded. " +
                    $"export={canExportOffline}, synthesize={canSynthesizeOffline}, " +
                    $"issues={canReviewIssuesOffline}, voices={canReviewVoicesOffline}, " +
                    $"transcribe={canTranscribeOffline}, new-project={canCreateOffline}, idle={Model.Idle}, editor={desktopEditorVisible}, cues={Model.Project?.Cues.Count}");
            var originalAudio = Model.Project!.Cues.Select(c => (Cue: c, Audio: c.Audio)).ToList();
            try
            {
                foreach (var entry in originalAudio) entry.Cue.Audio = null;
                UpdateApiKeyUi();
                if (DesktopCommandTitle("synthesize-tts") != "Generate All AD Audio" ||
                    CanRunDesktopCommand("synthesize-changed") || OverviewGenerateChangedAudioButton.IsEnabled || InspectorSynthesizeChangedButton.IsEnabled)
                    throw new InvalidOperationException("A new project must offer Generate All and disable partial repair.");
                Model.Project.Cues[0].Audio = "audio-description/smoke-missing.mp3";
                UpdateApiKeyUi();
                if (DesktopCommandTitle("synthesize-tts") != "Regenerate All AD Audio…" ||
                    !CanRunDesktopCommand("synthesize-changed") || !OverviewGenerateChangedAudioButton.IsEnabled || !InspectorSynthesizeChangedButton.IsEnabled)
                    throw new InvalidOperationException("Existing audio must switch all controls to Regenerate All and allow missing audio repair.");
            }
            finally
            {
                foreach (var entry in originalAudio) entry.Cue.Audio = entry.Audio;
                UpdateApiKeyUi();
            }
            Model.Settings.Set("apiKey", savedMenuKey); Model.Settings.Set("ttsMode", savedMenuTts);
            if (RenameProjectTitleButton.Visibility != Visibility.Visible ||
                Microsoft.UI.Xaml.Automation.AutomationProperties.GetName(RenameProjectTitleButton) != "Rename project" ||
                ToolTipService.GetToolTip(RenameProjectTitleButton) == null || !CanRunDesktopCommand("rename-project"))
                throw new InvalidOperationException("The editor title needs an available, labeled Rename pencil button.");
            await VerifyProjectMenuActionsForSmokeTestAsync();
            await VerifyEditorProjectNavigationForSmokeTestAsync();
            VerifyDuplicateProjectPathsForSmokeTest();
            VerifyRemoveFromListForSmokeTest();
            VerifyEditorActionMenusForSmokeTest();
            var originalSection = Section.SelectedIndex;
            foreach (var sectionIndex in new[] { 0, 2, 3 })
            {
                Section.SelectedIndex = sectionIndex;
                if (editMode) EditMode_Click(this, new RoutedEventArgs());
                RenderSection();
                await Task.Delay(80);
                VerifySegmentMoreButtonsForSmokeTest(false);
                VerifyEditorPrimaryActionForSmokeTest(false);
                await RunDesktopCommandAsync("segment-edit-mode");
                await Task.Delay(80);
                VerifySegmentMoreButtonsForSmokeTest(true);
                VerifyEditorPrimaryActionForSmokeTest(true);
                EditMode_Click(this, new RoutedEventArgs());
                await Task.Delay(80);
                VerifySegmentMoreButtonsForSmokeTest(false);
            }
            Section.SelectedIndex = originalSection;
            var oldOverlayMode = Model.Settings.Get("captionDisplayMode", "off");
            await RunDesktopCommandAsync("caption-transcript");
            if (Model.Settings.Get("captionDisplayMode") != "transcript")
                throw new InvalidOperationException("Transcript overlay command is not connected.");
            SetCaptionMode(oldOverlayMode);
            if (CanRunDesktopCommand("summary-play"))
                throw new InvalidOperationException("Summary menu reading was enabled outside Summary.");
            if (desktopMenuItems["summary-next-sentence"].Last() is not { Text: "Next Summary Sentence", KeyboardAcceleratorTextOverride: "Ctrl+Shift+]" } ||
                desktopMenuItems["summary-previous-paragraph"].Last() is not { Text: "Previous Summary Paragraph", KeyboardAcceleratorTextOverride: "Ctrl+Alt+[" })
                throw new InvalidOperationException("Summary sentence and paragraph menu commands or shortcuts are missing.");
            if (SummaryPreviousSentenceButton.Content as string != "\uE892" || SummaryNextSentenceButton.Content as string != "\uE893" ||
                SummaryPreviousParagraphButton.Content as string != "\uE70E" || SummaryNextParagraphButton.Content as string != "\uE70D")
                throw new InvalidOperationException("Summary previous and next sentence or paragraph buttons are missing.");
            UpdateDesktopMenuStates();
            var originalThumbnailsVisible = ThumbnailImagesRow.Visibility == Visibility.Visible;
            if (originalThumbnailsVisible) await RunDesktopCommandAsync("toggle-thumbnail-images");
            Root.UpdateLayout();
            if (ThumbnailImagesRow.Visibility != Visibility.Collapsed || Thumbnails.Children.Count != 0 ||
                DesktopCommandTitle("toggle-thumbnail-images") != "Show Thumbnail Images" ||
                Playhead.Visibility != Visibility.Visible || TimelinePanel.Visibility != Visibility.Visible)
                throw new InvalidOperationException("Hiding thumbnail images must keep seeking and timeline tracks available.");
            await RunDesktopCommandAsync("toggle-thumbnail-images");
            if (DesktopCommandTitle("toggle-thumbnail-images") != "Hide Thumbnail Images" ||
                Model.Settings.Get("thumbnailImagesVisible", "false") != "true")
                throw new InvalidOperationException("Showing thumbnail images must update its command title and saved preference.");
            var originalTheme = Model.Settings.Get("theme", "system");
            var thumbnailProject = Models.StudioProject.Load(folder);
            var thumbnail = File.Exists(Path.Combine(folder, "youtube-thumbnail.jpg")) ? "youtube-thumbnail.jpg" : thumbnailProject.Thumbnails[0];
            thumbnailProject.SourceType = "youtube";
            thumbnailProject.Thumbnails = new() { thumbnail };
            RenderTimelineThumbnails(thumbnailProject);
            await Task.Delay(100);
            VerifyTimelineThumbnailsForSmokeTest(1);
            await CaptureSmokeImage(TimelinePanel, Path.Combine(Path.GetDirectoryName(report)!, "youtube-thumbnail-strip.png"));
            var initialThumbnailWidth = Thumbnails.ActualWidth;
            ToggleInspector_Click(this, new RoutedEventArgs());
            VerifyEditorPrimaryActionForSmokeTest(false);
            await Task.Delay(50);
            VerifyTimelineThumbnailsForSmokeTest(1);
            if (Thumbnails.ActualWidth <= initialThumbnailWidth) throw new InvalidOperationException("Thumbnail strip did not expand when the inspector closed.");
            ToggleInspector_Click(this, new RoutedEventArgs());
            thumbnailProject.SourceType = "local";
            thumbnailProject.Thumbnails = Enumerable.Repeat(thumbnail, 8).ToList();
            RenderTimelineThumbnails(thumbnailProject);
            await Task.Delay(100);
            VerifyTimelineThumbnailsForSmokeTest(8);
            thumbnailProject.Thumbnails.Clear();
            RenderTimelineThumbnails(thumbnailProject);
            if (ThumbnailEmpty.Visibility != Visibility.Visible) throw new InvalidOperationException("Missing thumbnails have no placeholder.");
            RenderTimelineThumbnails(Model.Project!);
            PreviewTheme("dark");
            await Task.Delay(50);
            if (Root.RequestedTheme != ElementTheme.Dark || Root.ActualTheme != ElementTheme.Dark)
                throw new InvalidOperationException("Dark appearance did not apply to the application root.");
            var darkBackground = ((Microsoft.UI.Xaml.Media.SolidColorBrush)AppSurface.Background).Color;
            PreviewTheme("light");
            await Task.Delay(50);
            if (Root.RequestedTheme != ElementTheme.Light || Root.ActualTheme != ElementTheme.Light)
                throw new InvalidOperationException("Light appearance did not apply to the application root.");
            var lightBackground = ((Microsoft.UI.Xaml.Media.SolidColorBrush)AppSurface.Background).Color;
            if (darkBackground.R + darkBackground.G + darkBackground.B >= lightBackground.R + lightBackground.G + lightBackground.B)
                throw new InvalidOperationException("Application background colors did not follow the selected appearance.");
            var material = Views.WindowAppearance.MaterialFor(this);
            if (Microsoft.UI.Composition.SystemBackdrops.MicaController.IsSupported() && material != Views.WindowMaterial.Mica)
                throw new InvalidOperationException("Mica is supported but the main window did not use it.");
            if (!Microsoft.UI.Composition.SystemBackdrops.MicaController.IsSupported() &&
                Microsoft.UI.Composition.SystemBackdrops.DesktopAcrylicController.IsSupported() && material != Views.WindowMaterial.Acrylic)
                throw new InvalidOperationException("Desktop Acrylic is supported but the main window did not use it.");
            if (material != Views.WindowMaterial.None && (darkBackground.A == 255 || lightBackground.A == 255))
                throw new InvalidOperationException("The application surface is opaque, so the system material cannot show through.");
            if (material != Views.WindowMaterial.None && ((Microsoft.UI.Xaml.Media.SolidColorBrush)Root.Background).Color.A != 0)
                throw new InvalidOperationException("The title bar and menu strip are painted over the system material.");
            if (!ExtendsContentIntoTitleBar || AppTitleBar.ActualHeight < 30)
                throw new InvalidOperationException("The main window does not use the extended material title bar.");
            if (material == Views.WindowMaterial.Mica && (AppWindow.TitleBar.ButtonBackgroundColor is not { } caption || caption.A > 1))
                throw new InvalidOperationException("Mica caption buttons are not transparent.");
            PreviewTheme(originalTheme);
            if (Model.Project?.Cues[0].Origin != "ai") throw new InvalidOperationException("Description origin was not preserved while reading project JSON.");
            Model.Settings.Set("captionDisplayMode", "combined");
            UpdateCaptions(.5);
            if (CaptionText.Text != "Hello." || DescriptionCaptionBox.Visibility != Visibility.Collapsed) throw new InvalidOperationException("Dialogue caption timing failed.");
            UpdateCaptions(3);
            if (DescriptionCaptionText.Text != "A blue screen appears." || DialogueCaptionBox.Visibility != Visibility.Collapsed) throw new InvalidOperationException("Description caption timing failed.");
            var adColor = ((Microsoft.UI.Xaml.Media.SolidColorBrush)DescriptionCaptionText.Foreground).Color;
            if (adColor.R != 255 || adColor.G != 214 || adColor.B != 10) throw new InvalidOperationException("Description caption style failed.");
            UpdateCaptions(5);
            if (CaptionOverlay.Visibility != Visibility.Collapsed) throw new InvalidOperationException("Expired captions remained visible.");
            Model.Settings.Set("captionDisplayMode", "off");
            Section.SelectedIndex = 0; Seek(.5); await Task.Delay(100); UpdateActiveSegment(true);
            if (highlightedSegment?.Id != "t1" || !highlightedSegment.IsActive)
                throw new InvalidOperationException("Transcript follow highlight did not synchronize with video time.");
            Follow.IsOn = false;
            if (highlightedSegment.IsActive) throw new InvalidOperationException("Transcript highlight stayed on when Follow was disabled.");
            Follow.IsOn = true;
            if (!highlightedSegment.IsActive) throw new InvalidOperationException("Transcript highlight did not return when Follow was enabled.");
            Section.SelectedIndex = 2; Seek(2.5); await Task.Delay(100); UpdateActiveSegment(true);
            if (highlightedSegment?.Id != "a1" || !highlightedSegment.IsActive)
                throw new InvalidOperationException("Descriptions follow highlight did not synchronize with video time.");
            await VerifySegmentSelectionForSmokeTestAsync(report);
            await VerifyTimecodePickerForSmokeTestAsync(report);
            PreviewTheme(originalTheme);
            Section.SelectedIndex = 3; Seek(2.5); await Task.Delay(100); UpdateActiveSegment(true);
            if (highlightedSegment?.Id != "a1" || !highlightedSegment.IsActive)
                throw new InvalidOperationException("AD Transcript follow highlight did not synchronize with video time.");
            Model.Project!.Transcript[0].End = 3;
            UpdateActiveSegment(true);
            if (!Model.Project.Transcript[0].IsActive || !Model.Project.Cues[0].IsActive)
                throw new InvalidOperationException("Overlapping transcript and description rows did not both highlight.");
            Model.Project.Transcript[0].End = 1;
            UpdateActiveSegment(true);
            if (TimelineScrubSurface.ActualWidth > 0 && Microsoft.UI.Xaml.Controls.Canvas.GetLeft(TimelineCursor) < TimelineScrubSurface.ActualWidth * .2)
                throw new InvalidOperationException("Timeline playhead did not follow the video position.");
            if (ThumbnailScrubSurface.ActualWidth > 0 && Microsoft.UI.Xaml.Controls.Canvas.GetLeft(ThumbnailCursor) < ThumbnailScrubSurface.ActualWidth * .2)
                throw new InvalidOperationException("Thumbnail playhead did not follow the video position.");
            if (!originalThumbnailsVisible) await RunDesktopCommandAsync("toggle-thumbnail-images");
            if (Section.Items.Count != 5 || Section.Items.OfType<ComboBoxItem>().Any(item => item.Content as string == "Created Exports"))
                throw new InvalidOperationException("The editor section selector still offers Created Exports.");
            Section.SelectedIndex = 1;
            var savedOrigin = Model.Project!.SummaryOrigin;
            foreach (var origin in new[] { "ai", "manual", "imported", "unknown" })
            {
                Model.Project.SummaryOrigin = origin;
                RefreshSummarySourceBadge();
                var expectedOrigin = new Models.Segment { Origin = origin };
                if (summaryEditMode || SummarySourceBadge.Visibility != Visibility.Visible || SummarySourceText.Text != expectedOrigin.OriginLabel || SummarySourceIcon.Glyph != expectedOrigin.OriginGlyph)
                    throw new InvalidOperationException("Summary reading mode lost the source label/icon: " + origin);
            }
            Model.Project.SummaryOrigin = savedOrigin;
            RefreshSummarySourceBadge();
            Model.Settings.Set("summarySentenceHighlightColor", "#C0D8FF"); Model.Settings.Set("summaryWordHighlightColor", "#FFDA57");
            var fixtureSentence = summaryLines.First(line => line.Text.Length > 7).Text;
            HighlightReadbackSentence(fixtureSentence); HighlightReadbackWord(0, 6);
            if (activeSummaryBlock == null || activeSummaryBlock.TextHighlighters.Count != 2 ||
                ((Microsoft.UI.Xaml.Media.SolidColorBrush)activeSummaryBlock.TextHighlighters[0].Background).Color.B != 255 ||
                ((Microsoft.UI.Xaml.Media.SolidColorBrush)activeSummaryBlock.TextHighlighters[1].Background).Color.R != 255)
                throw new InvalidOperationException("Summary sentence or word highlighting did not use Settings colors.");
            var zoomBlock = activeSummaryBlock;
            var zoomSelection = SummarySelection();
            var previousPreview = PreviewPanel.Visibility;
            PreviewPanel.Visibility = Visibility.Collapsed;
            await RunDesktopCommandAsync("summary-zoom-in");
            await Task.Delay(150);
            SummaryPanel.UpdateLayout(); ReflowSummaryZoom(); UpdateSummaryZoomControls();
            if (Math.Abs(SummaryPanel.ZoomFactor - 1.1) > .01 || SummaryZoomFooter.Visibility != Visibility.Visible
                || !ReferenceEquals(activeSummaryBlock, zoomBlock) || activeSummaryBlock.TextHighlighters.Count != 2
                || SummarySelection() != zoomSelection || SummaryZoomContent.Width * SummaryPanel.ZoomFactor > SummaryPanel.ViewportWidth + 1)
                throw new InvalidOperationException("Summary zoom lost wrapping, highlight, selection, or footer controls.");
            PreviewPanel.Visibility = Visibility.Visible; UpdateSummaryZoomControls();
            if (SummaryZoomFooter.Visibility != Visibility.Collapsed) throw new InvalidOperationException("Summary slider remains visible with preview.");
            await RunDesktopCommandAsync("summary-zoom-reset"); await Task.Delay(150);
            PreviewPanel.Visibility = previousPreview; UpdateSummaryZoomControls();
            await CaptureSmokeImage(Root, Path.Combine(Path.GetDirectoryName(report)!, "summary-highlight.png"));
            await CaptureSmokeImage(SummaryRichText, Path.Combine(Path.GetDirectoryName(report)!, "summary-text-highlight.png"));
            ClearSummaryHighlight();
            var spokenSummary = readback.SpeakAsync(fixtureSentence);
            await Task.Delay(100);
            var sourceDuringZoom = readback.State;
            await RunDesktopCommandAsync("summary-zoom-in");
            if (sourceDuringZoom != SummaryReadbackState.Stopped && readback.State == SummaryReadbackState.Stopped)
                throw new InvalidOperationException("Zoom stopped Summary reading.");
            for (var attempt = 0; attempt < 40 && activeSummaryBlock?.TextHighlighters.Count != 2; attempt++) await Task.Delay(50);
            if (activeSummaryBlock?.TextHighlighters.Count != 2)
                throw new InvalidOperationException("Spoken summary did not synchronize its sentence and word highlight with the visible text.");
            readback.Stop();
            try { await spokenSummary; } catch (OperationCanceledException) { }
            await Task.Delay(500);
            var savedSummary = Model.Project!.Summary;
            var legacySummary = new string('\uFEFF', 5) + "# Performance Summary: Job Interview Comedy Skit\n\n### Overview\nA scene.";
            var legacyView = BuildMarkdownView(legacySummary, trackReadback: false);
            var legacyTitle = legacyView.Children.OfType<TextBlock>().First();
            if (legacyTitle.FontSize != 30 || legacyTitle.Inlines.OfType<Run>().FirstOrDefault()?.Text != "Performance Summary: Job Interview Comedy Skit")
                throw new InvalidOperationException("A legacy summary's repeated BOMs prevented the document title from rendering.");
            RenderMarkdown(savedSummary);
            await Task.Delay(100);
            await CaptureSmokeImage(SummaryRichText, Path.Combine(Path.GetDirectoryName(report)!, "summary-presentation.png"));
            if (savedSummary.StartsWith('\uFEFF') && (summaryLines.First().Text.StartsWith('#') || summaryLines.First().Text.Contains('\uFEFF')))
                throw new InvalidOperationException("The provided legacy summary fixture still contains visible heading syntax or encoding marks.");
            var carriageReturnView = BuildMarkdownView("# Heading\r\rA **bold** line.\r\r- Item", trackReadback: false);
            if (carriageReturnView.Children.OfType<TextBlock>().Count() < 3)
                throw new InvalidOperationException("Markdown using TextBox carriage-return line breaks did not render as separate blocks.");
            var richSummary = BuildMarkdownView("A ~~removed~~ word and __bold__.\nA soft line break.", trackReadback: false);
            var richBlock = richSummary.Children.OfType<TextBlock>().Single();
            if (!richBlock.Inlines.OfType<Run>().Any(run => run.TextDecorations == Windows.UI.Text.TextDecorations.Strikethrough) ||
                !richBlock.Inlines.OfType<Bold>().Any() || StripInline("![alt](https://example.com/image.png) and ~~old~~") != "alt and old")
                throw new InvalidOperationException("Markdown reading and speech text do not agree on formatting.");
            try
            {
                SetSummaryEditMode(true);
                VerifyEditorPrimaryActionForSmokeTest(true);
                if (FindButton(SummaryEditorPanel, "Save Summary") != null)
                    throw new InvalidOperationException("Summary editing exposes a duplicate Save/Done button.");
                if (CanRunDesktopCommand("project-list") || CanRunDesktopCommand("project-grid")) throw new InvalidOperationException("Project layout shortcuts conflict with Markdown headings.");
                SummaryEditorBox.Text = "hello";
                SummaryEditorBox.Select(0, 5);
                ApplySummaryMarkdown("bold");
                if (SummaryEditorBox.Text != "**hello**") throw new InvalidOperationException("Markdown toolbar lost the selection.");
                SummaryUndo_Click(this, new RoutedEventArgs());
                if (SummaryEditorBox.Text != "hello") throw new InvalidOperationException("Markdown formatting cannot be undone.");
                SummaryRedo_Click(this, new RoutedEventArgs());
                if (SummaryEditorBox.Text != "**hello**") throw new InvalidOperationException("Markdown formatting cannot be redone.");
                SummaryEditorBox.Text = "# Edited summary\n\nA **bold** line.\n\n- First item";
                await SaveSummaryChangesAsync();
                SetSummaryEditMode(false);
                if (SummaryPanel.Visibility != Visibility.Visible || SummaryEditorPanel.Visibility != Visibility.Collapsed)
                    throw new InvalidOperationException("Saving the summary did not return to the reading view.");
                var renderedSummary = SummaryRichText.Children.OfType<TextBlock>().ToList();
                if (renderedSummary.Count < 3)
                    throw new InvalidOperationException(
                        $"Saving the summary did not render its Markdown blocks. children={SummaryRichText.Children.Count} textBlocks={renderedSummary.Count} " +
                        $"editorLength={SummaryEditorBox.Text?.Length ?? -1} projectLength={Model.Project?.Summary?.Length ?? -1} " +
                        $"summaryTextLength={SummaryText.Text?.Length ?? -1} panel={SummaryPanel.Visibility} editor={SummaryEditorPanel.Visibility} " +
                        $"types={string.Join(",", SummaryRichText.Children.Select(child => child.GetType().Name))}");
                if (renderedSummary.Any(block => (block.Text ?? "").Contains("**") || block.Inlines.OfType<Run>().Any(run => (run.Text ?? "").Contains("**"))))
                    throw new InvalidOperationException("The saved summary still shows raw Markdown markers.");
                if (!renderedSummary.Any(block => block.Inlines.OfType<Bold>().Any()))
                    throw new InvalidOperationException("The saved summary did not render bold Markdown.");
                if (!renderedSummary.Any(block => (block.Text ?? "").Contains('•') || block.Inlines.OfType<Run>().Any(run => (run.Text ?? "").Contains('•'))))
                    throw new InvalidOperationException("The saved summary did not render its list item.");
                if (!renderedSummary.Any(block => block.FontSize > 16))
                    throw new InvalidOperationException("The saved summary did not render its heading.");
            }
            finally
            {
                SummaryEditorBox.Text = savedSummary ?? "";
                await SaveSummaryChangesAsync();
                SetSummaryEditMode(false);
            }
            Section.SelectedIndex = 0;
            await CaptureSmokeImage(Root, Path.Combine(Path.GetDirectoryName(report)!, "editor-transcript.png"));
            Section.SelectedIndex = 2;
            TogglePreview_Click(this, new RoutedEventArgs());
            var inspectorGroups = ((StackPanel)InspectorScrollViewer.Content).Children.OfType<Expander>().ToArray();
            var generationGroup = inspectorGroups.SingleOrDefault(group => group.Content is Views.AccessibilityGroup { Child: var child } && ReferenceEquals(child, ContextActions));
            var narrationGroup = inspectorGroups.SingleOrDefault(group => group.Content is Views.AccessibilityGroup { Child: var child } && ReferenceEquals(child, NarrationActions));
            if (InspectorSectionTitle.Text != "AD Segment Generation" ||
                ContextActions.Visibility != Visibility.Visible || NarrationActions.Visibility != Visibility.Visible ||
                generationGroup == null || narrationGroup == null || ReferenceEquals(generationGroup, narrationGroup) ||
                !ReferenceEquals(generationGroup.Header, InspectorSectionTitle) || narrationGroup.Header is not TextBlock { Text: "Narration Audio" })
                throw new InvalidOperationException("AD generation and Narration Audio must have separate sibling Inspector expanders.");
            await Task.Delay(200);
            await CaptureSmokeImage(Root, Path.Combine(Path.GetDirectoryName(report)!, "editor-descriptions.png"));
            foreach (var index in new[] { 1, 2, 3, 4 }) {
                Section.SelectedIndex = index; UpdateSectionPresentation();
                if (NarrationActions.Visibility != Visibility.Visible || !InspectorSpeechLanguage.Text.StartsWith("Language: ") ||
                    InspectorNarrationControls.Visibility != (index is 2 or 3 ? Visibility.Visible : Visibility.Collapsed))
                    throw new InvalidOperationException("Speech engine/language/voice inspector visibility is inconsistent.");
            }
            Section.SelectedIndex = 2; UpdateSectionPresentation();
            var savedResults = editorMenus.First(menu => menu.Title == "View").Items.OfType<MenuFlyoutSubItem>().Single(item => item.Tag as string == "saved-results");
            if (savedResults.Items.OfType<MenuFlyoutSubItem>().Any() || savedResults.Text != SavedResultsMenuTitle || savedResults.IsEnabled != CanRunDesktopCommand("saved-results"))
                throw new InvalidOperationException("Saved Results menu groups or guarded state differ from the inspector.");
            var languages = editorMenus.First(menu => menu.Title == "View").Items.OfType<MenuFlyoutSubItem>().Single(item => item.Tag as string == "project-language");
            if (languages.Items.OfType<ToggleMenuFlyoutItem>().Count() != Model.Project!.AvailableViewingLanguages.Count ||
                languages.Items.OfType<ToggleMenuFlyoutItem>().Count(item => item.IsChecked) != (Model.Project.AvailableViewingLanguages.Contains(Model.Project.SelectedLanguage) ? 1 : 0))
                throw new InvalidOperationException("Project Language menu choices/checked state differ from the inspector.");
            var fileDeletes = editorMenus.First(menu => menu.Title == "File").Items.OfType<MenuFlyoutItem>()
                .Where(item => item.Tag as string is "delete-project" or "delete-content").ToList();
            if (fileDeletes.Count != 2 || !CanRunDesktopCommand("delete-project") || !CanRunDesktopCommand("delete-content"))
                throw new InvalidOperationException("Editor File deletion commands are missing or unavailable.");
            using (var deletion = DeletionActivity.Begin("Deleting selected content…", Model.Project!.Name)) {
                UpdateProcessingChrome();
                if (!StatusRing.IsActive || StatusStageTitle.Visibility != Visibility.Visible || CancelProcessingButton.Visibility != Visibility.Collapsed)
                    throw new InvalidOperationException("Deletion progress is missing or incorrectly offers cancellation.");
            }
            UpdateProcessingChrome();
            ShowGallery_Click(this, new RoutedEventArgs());
            if (!MainMenu.Items.Select(item => item.Title).SequenceEqual(new[] { "File", "Edit", "View", "Playback", "Project", "YouTube", "Help" }))
                throw new InvalidOperationException("Gallery menu hierarchy drifted from the shared desktop layout.");
            GalleryGrid.SelectedItem = null; GalleryList.SelectedItem = null;
            UpdateDesktopMenuStates();
            var galleryEdit = galleryMenus.Single(menu => menu.Title == "Edit");
            if (galleryEdit.Visibility != Visibility.Visible || galleryEdit.Items.OfType<MenuFlyoutItem>().Any(item => item.Visibility == Visibility.Visible && item.IsEnabled))
                throw new InvalidOperationException("Gallery Edit must be present with selected-project commands disabled when nothing is selected.");
            if (CanRunDesktopCommand("delete-project") || CanRunDesktopCommand("delete-content") || CanRunDesktopCommand("export") || CanRunDesktopCommand("review-narration-issues") || CanRunDesktopCommand("review-narration-voices"))
                throw new InvalidOperationException("Project-only menu commands remained enabled in the gallery.");
            GalleryGrid.SelectedItem = null;
            GalleryList.SelectedItem = null;
            var expectedFilterLanguages = new[] { "all" }.Concat(Model.ProjectFilterLanguages.Select(language => language.Id)).ToList();
            if (!(ProjectLanguageFilter.ItemsSource as System.Collections.Generic.IEnumerable<Models.OutputLanguage>)!.Select(language => language.Id).SequenceEqual(expectedFilterLanguages) ||
                libraryLanguageMenus.Any(menu => !menu.Items.OfType<ToggleMenuFlyoutItem>().Select(item => item.Text).SequenceEqual(new[] { "All Languages" }.Concat(Model.ProjectFilterLanguages.Select(language => language.Title)))))
                throw new InvalidOperationException("The editor project-list language filter and View menu must show library content languages plus All Languages.");
            var galleryViewMenu = galleryMenus.Single(menu => menu.Title == "View");
            if (!galleryViewMenu.Items.OfType<MenuFlyoutSubItem>().Where(item => item.Visibility == Visibility.Visible)
                .Select(item => item.Text).SequenceEqual(new[] { "Filter Projects by Source", "Filter Projects by Language", "Filter Projects by AI Provider", "Sort Projects" }))
                throw new InvalidOperationException("All Projects View must show library filter and sort submenus.");
            await VerifyProjectSortingForSmokeTestAsync();
            if (desktopMenuItems["preview"].Any(item => item.Visibility == Visibility.Visible) ||
                desktopMenuItems["show-inspector"].Any(item => item.Visibility == Visibility.Visible) ||
                desktopMenuItems["language-results"].Any(item => item.Visibility == Visibility.Visible))
                throw new InvalidOperationException("Editor-only commands remained visible in All Projects.");
            if (CanRunDesktopCommand("rename-project") || CanRunDesktopCommand("trash-project"))
                throw new InvalidOperationException("Project management requires a gallery selection or an open editor.");
            GalleryGrid.SelectedItem = Model.Projects.First();
            UpdateDesktopMenuStates();
            if (!CanRunDesktopCommand("open-gallery-window") || galleryEdit.Items.OfType<MenuFlyoutItem>().Where(item => item.Visibility == Visibility.Visible).Any(item => item.Icon == null))
                throw new InvalidOperationException("Gallery Edit must expose icon-bearing commands for the selected project.");
            if (!galleryEdit.Items.OfType<MenuFlyoutItem>().Where(item => item.Visibility == Visibility.Visible).Select(item => item.Tag as string).SequenceEqual(new[] { "focus-search", "clear-search", "rename-project" }))
                throw new InvalidOperationException("All Projects Edit must contain Search Projects, Clear Search and Rename Project.");
            if (!galleryMenus.Single(menu => menu.Title == "File").Items.OfType<MenuFlyoutItem>().Any(item => item.Tag as string == "open-gallery-window" && item.Visibility == Visibility.Visible))
                throw new InvalidOperationException("Open in New Window belongs in All Projects File.");
            if (!CanRunDesktopCommand("rename-project") || !CanRunDesktopCommand("trash-project") || !CanRunDesktopCommand("project-settings") || !CanRunDesktopCommand("open-selected-project") || !CanRunDesktopCommand("export"))
                throw new InvalidOperationException("Selected gallery projects must support Rename and Recycle Bin without opening an editor.");
            GalleryGrid.SelectedItem = null;
            await VerifyRemoveFromListFileMenuForSmokeTestAsync();
            PreviewTheme("dark"); await Task.Delay(80);
            VerifyGalleryToolbarBoundsForSmokeTest();
            if (GalleryPanel.ActualTheme != ElementTheme.Dark || GalleryGrid.ActualTheme != ElementTheme.Dark || GalleryTable.ActualTheme != ElementTheme.Dark)
                throw new InvalidOperationException("All Projects did not update every view to dark appearance.");
            await CaptureSmokeImage(Root, Path.Combine(Path.GetDirectoryName(report)!, "gallery-dark.png"));
            PreviewTheme("light"); await Task.Delay(80);
            if (GalleryPanel.ActualTheme != ElementTheme.Light || GalleryGrid.ActualTheme != ElementTheme.Light || GalleryTable.ActualTheme != ElementTheme.Light)
                throw new InvalidOperationException("All Projects did not update every view to light appearance.");
            await CaptureSmokeImage(Root, Path.Combine(Path.GetDirectoryName(report)!, "gallery-light.png"));
            PreviewTheme(originalTheme);
            await Task.Delay(200);
            await CaptureSmokeImage(Root, Path.Combine(Path.GetDirectoryName(report)!, "gallery.png"));
            GalleryLayout_Click(this, new RoutedEventArgs());
            if (GalleryTable.Visibility != Visibility.Visible || GalleryList.Items.Count != 1 ||
                Model.Projects[0].DurationLabel != "8s" || Model.Projects[0].AdSegmentLabel != "1")
                throw new InvalidOperationException("All Projects table did not display the project metadata columns.");
            PreviewTheme("dark"); await Task.Delay(80);
            if (GalleryList.ActualTheme != ElementTheme.Dark) throw new InvalidOperationException("All Projects table did not change to dark appearance.");
            await CaptureSmokeImage(Root, Path.Combine(Path.GetDirectoryName(report)!, "gallery-table-dark.png"));
            PreviewTheme("light"); await Task.Delay(80);
            if (GalleryList.ActualTheme != ElementTheme.Light) throw new InvalidOperationException("All Projects table did not change to light appearance.");
            PreviewTheme(originalTheme);
            await Task.Delay(250);
            if (GalleryList.ContainerFromIndex(0) is not Microsoft.UI.Xaml.Controls.ListViewItem tableRow ||
                tableRow.ActualHeight < 30)
                throw new InvalidOperationException("All Projects table row was not rendered.");
            var cachedGalleryProject = Model.Projects[0];
            Model.RefreshProjectIntegrity();
            if (Model.ReloadIfProjectsChanged(GallerySearch.Text, gallerySourceFilter) ||
                Model.Projects.Count != 1 || !ReferenceEquals(cachedGalleryProject, Model.Projects[0]))
                throw new InvalidOperationException("An unchanged gallery was rebuilt while the app remained open.");
            Model.Reload();
            if (Model.Projects.Count != 1 || !ReferenceEquals(cachedGalleryProject, Model.Projects[0]))
                throw new InvalidOperationException("A repeated gallery reload replaced an unchanged project instance.");
            await CaptureSmokeImage(Root, Path.Combine(Path.GetDirectoryName(report)!, "gallery-table.png"));
            GalleryLayout_Click(this, new RoutedEventArgs());
            Model.Open(folder);
            ShowEditor();
            if (InspectorSettingsButton.Visibility != Visibility.Visible || (string)InspectorSettingsButton.Content != "\uE713")
                throw new InvalidOperationException("The inspector footer does not expose its Settings gear.");
            if (GallerySearchHost.Visibility != Visibility.Collapsed)
                throw new InvalidOperationException("All Projects search remained in the editor header.");
            for (var i = 0; i < 5; i++) { Section.SelectedIndex = i; await Task.Delay(50); }
            ToggleGrid_Click(this, new RoutedEventArgs());
            ToggleGrid_Click(this, new RoutedEventArgs());
            ToggleLibrary_Click(this, new RoutedEventArgs());
            ToggleLibrary_Click(this, new RoutedEventArgs());
            var expectedGrid = ProjectGrid.Visibility;
            ToggleGrid_Click(this, new RoutedEventArgs());
            if (ProjectGrid.Visibility == expectedGrid ||
                EditorProjectLayoutIcon.Glyph != (ProjectGrid.Visibility == Visibility.Visible ? "\uE8FD" : "\uE80A"))
                throw new InvalidOperationException("The bottom-left project layout button did not switch grid and list views.");
            ToggleGrid_Click(this, new RoutedEventArgs());
            var expectedLibrary = ProjectSplitView.IsPaneOpen;
            ToggleLibrary_Click(this, new RoutedEventArgs());
            if (ProjectSplitView.IsPaneOpen == expectedLibrary || EditorProjectListButton.Visibility != Visibility.Visible)
                throw new InvalidOperationException("The bottom-left project list button did not hide the project list while staying available.");
            ToggleLibrary_Click(this, new RoutedEventArgs());
            await VerifyAboutMenuForSmokeTestAsync();
            await VerifyProjectPaneTransitionsForSmokeTestAsync();
            await VerifyAboutMenuForSmokeTestAsync();
            await VerifyInspectorToggleForSmokeTestAsync(report);
            var titleBarVisibility = AppTitleBar.Visibility;
            FullScreen_Click(this, new RoutedEventArgs());
            if (!fullScreenVideo || !ReferenceEquals(PreviewPanel.Parent, Root) || AppTitleBar.Visibility != Visibility.Collapsed ||
                MainMenu.Visibility != Visibility.Collapsed ||
                AppWindow.Presenter.Kind != Microsoft.UI.Windowing.AppWindowPresenterKind.FullScreen)
                throw new InvalidOperationException("Full-screen video did not promote the preview, hide the editor chrome, or switch the window presenter.");
            FullScreen_Click(this, new RoutedEventArgs());
            if (fullScreenVideo || ReferenceEquals(PreviewPanel.Parent, Root) || AppTitleBar.Visibility != titleBarVisibility ||
                AppWindow.Presenter.Kind == Microsoft.UI.Windowing.AppWindowPresenterKind.FullScreen)
                throw new InvalidOperationException("Exiting full-screen video did not restore the editor layout and window presenter.");
            ToggleInspector_Click(this, new RoutedEventArgs());
            ToggleInspector_Click(this, new RoutedEventArgs());
            var settings = new Views.SettingsDialog(Model, this);
            var display = settings.ShowAsync();
            await Task.Delay(250);
            settings.VerifySidebarNavigationForSmokeTest();
            await settings.VerifyInformationForSmokeTestAsync();
            settings.VerifyPiperSampleMenuForSmokeTest();
            await settings.VerifySarvamSettingsForSmokeTestAsync();
            await settings.VerifyLayoutForSmokeTestAsync();
            await settings.VerifyAutomaticSettingsForSmokeTestAsync();
            settings.VerifyDeveloperModeGateForSmokeTest();
            settings.VerifyProjectPlaybackSettingsForSmokeTest();
            await settings.VerifyAdvancedOptionsForSmokeTestAsync();
            PreviewTheme("dark"); await Task.Delay(80);
            if (((FrameworkElement)settings.Content).ActualTheme != ElementTheme.Dark || Root.ActualTheme != ElementTheme.Dark)
                throw new InvalidOperationException("An open Settings window did not follow the app's dark theme.");
            PreviewTheme("light"); await Task.Delay(80);
            if (((FrameworkElement)settings.Content).ActualTheme != ElementTheme.Light || Root.ActualTheme != ElementTheme.Light)
                throw new InvalidOperationException("An open Settings window did not follow the app's light theme.");
            PreviewTheme(originalTheme);
            await CaptureSmokeImage((FrameworkElement)settings.Content, Path.Combine(Path.GetDirectoryName(report)!, "settings-appearance.png"));
            var previewSession = new Services.ExportSession("Windows UI smoke fixture", () => { });
            previewSession.Update("Creating audio export", .42, true);
            Services.ExportSession.Active.Add(previewSession);
            try
            {
                ActiveExports.VerifyCenteredForSmokeTest();
                ((Microsoft.UI.Xaml.Controls.Grid)settings.Content).Children.OfType<Views.ProgressOverlay>().Single().VerifyCenteredForSmokeTest();
                PreviewTheme("dark"); await Task.Delay(80);
                await CaptureSmokeImage(Root, Path.Combine(Path.GetDirectoryName(report)!, "progress-center-dark.png"));
                await CaptureSmokeImage((FrameworkElement)settings.Content, Path.Combine(Path.GetDirectoryName(report)!, "settings-progress-center.png"));
                PreviewTheme("light"); await Task.Delay(80);
                await CaptureSmokeImage(Root, Path.Combine(Path.GetDirectoryName(report)!, "progress-center-light.png"));
            }
            finally { Services.ExportSession.Active.Remove(previewSession); PreviewTheme(originalTheme); }
            await settings.VerifyWindowThemeAsync();
            await settings.VerifyExportSoundForSmokeTestAsync();
            await settings.VerifySummaryHighlightAppearanceForSmokeTestAsync();
            settings.VerifyGeminiModelFiltering();
            await Task.Delay(100);
            await CaptureSmokeImage((FrameworkElement)settings.Content, Path.Combine(Path.GetDirectoryName(report)!, "settings-gemini.png"));
            settings.NavigateForSmokeTest(1);
            await Task.Delay(100);
            await CaptureSmokeImage((FrameworkElement)settings.Content, Path.Combine(Path.GetDirectoryName(report)!, "settings-projects.png"));
            settings.NavigateForSmokeTest(2);
            await Task.Delay(100);
            await CaptureSmokeImage((FrameworkElement)settings.Content, Path.Combine(Path.GetDirectoryName(report)!, "settings-narration.png"));
            settings.NavigateForSmokeTest(5);
            await Task.Delay(100);
            await CaptureSmokeImage((FrameworkElement)settings.Content, Path.Combine(Path.GetDirectoryName(report)!, "settings-tools.png"));
            foreach (var section in new[] { 2, 6, 7 })
            {
                settings.SelectSection(section);
                if (settings.SelectedSectionForSmokeTest != section)
                    throw new InvalidOperationException("Settings opened the wrong section.");
            }
            foreach (var section in new[] { 0, 1, 2, 3, 4, 5, 6, 7, 2, 0 }) { settings.NavigateForSmokeTest(section); await Task.Delay(200); }
            await settings.VerifyProviderSwitchingAsync();
            await settings.VerifyLocalVoiceSamplesAsync();
            var originalAutoApplySize = Model.Settings.Number("captionFontSize", 24);
            settings.StageFinalSettingsEditForSmokeTest();
            settings.Hide(); await display;
            var closedPreferences = System.Text.Json.Nodes.JsonNode.Parse(File.ReadAllText(Services.SettingsStore.ConfigPath))!;
            if (closedPreferences["captionFontSize"]!.GetValue<int>() != 33 || CaptionText.FontSize != 33)
                throw new InvalidOperationException("Closing Settings lost its final edit or failed to update an open editor.");
            Model.Settings.Set("captionFontSize", originalAutoApplySize);
            Model.Settings.Save();
            App.ApplySettingsChanges(new[] { "captionFontSize" });
            foreach (var section in new[] { 2, 6, 7 })
            {
                var targeted = new Views.SettingsDialog(Model, this, section);
                var targetedDisplay = targeted.ShowAsync();
                await Task.Delay(100);
                if (targeted.SelectedSectionForSmokeTest != section)
                    throw new InvalidOperationException("A Settings entry point did not open its selected section.");
                targeted.Hide(); await targetedDisplay;
            }
            var openFromButton = ShowSettingsSectionAsync(0);
            await Task.Delay(100);
            Settings_Click(new Microsoft.UI.Xaml.Controls.Button { Tag = "Narration" }, new RoutedEventArgs());
            await Task.Delay(100);
            if (openSettings?.SelectedSectionForSmokeTest != 2) throw new InvalidOperationException("Narration button opened the wrong Settings page.");
            HelpSettings_Click(this, new RoutedEventArgs());
            Views.HelpWindow? helpWindow = null;
            for (var attempt = 0; attempt < 100; attempt++)
            {
                helpWindow = App.HelpWindowForSmokeTest;
                if (helpWindow?.LoadedPageForSmokeTest is not null) break;
                await Task.Delay(100);
            }
            if (helpWindow is null)
                throw new InvalidOperationException("Help menu did not open the Help book window.");
            await helpWindow.VerifyGuideForSmokeTestAsync();
            await helpWindow.VerifyOfflineRecoveryForSmokeTestAsync();
            foreach (var theme in new[] { "dark", "light" })
            {
                PreviewTheme(theme); await Task.Delay(150);
                VerifyHelpCaptionForSmokeTest(helpWindow, theme == "dark");
                if ((await helpWindow.ReaderStateForSmokeTest()) != (theme == "dark" ? "true" : "false"))
                    throw new InvalidOperationException("Live Help did not follow the app theme.");
            }
            helpWindow.Close();
            await Task.Delay(200);
            if (App.HelpWindowForSmokeTest != null)
                throw new InvalidOperationException("The Help book window did not close.");
            AboutSettings_Click(this, new RoutedEventArgs());
            await Task.Delay(100);
            if (openSettings?.SelectedSectionForSmokeTest != 7) throw new InvalidOperationException("About menu opened the wrong Settings page.");
            openSettings.Hide(); await openFromButton;
            PreviewTheme("dark");
            var newProject = new Views.NewProjectDialog(this, Root.ActualTheme, Model.Settings);
            var newProjectDisplay = ShowDialogAsync(newProject, Views.NewProjectDialog.WindowWidth, Views.NewProjectDialog.WindowHeight);
            await Task.Delay(100);
            if (newProjectDisplay.IsCompleted) await newProjectDisplay;
            PreviewTheme("light"); await Task.Delay(80);
            if (newProject.ActualTheme != ElementTheme.Light) throw new InvalidOperationException("An open New Project dialog did not switch to light appearance.");
            PreviewTheme("dark"); await Task.Delay(80);
            if (newProject.RequestedTheme != ElementTheme.Dark || newProject.IsPrimaryButtonEnabled)
                throw new InvalidOperationException("New Project did not follow dark mode or disable Create without a source.");
            await VerifyDialogThemesForSmokeTestAsync(newProject, report, "new-project-window");
            PreviewTheme("dark");
            newProject.VerifyLocalAccessibilityForSmokeTest(selected: false);
            await newProject.InspectVideoForSmokeTestAsync(Model.Project!.Resolve(Model.Project.Source));
            newProject.VerifyLocalAccessibilityForSmokeTest(selected: true);
            if (!newProject.IsPrimaryButtonEnabled || newProject.VideoPath == null || !newProject.DetailsForSmokeTest.Contains("320 × 180"))
                throw new InvalidOperationException("New Project did not preview local video details before creation: " + newProject.DetailsForSmokeTest);
            if (newProject.ActualWidth <= 0 || newProject.ActualHeight <= 0)
                throw new InvalidOperationException($"New Project dialog did not render: {newProject.ActualWidth} × {newProject.ActualHeight}.");
            await CaptureSmokeImage(newProject, Path.Combine(Path.GetDirectoryName(report)!, "new-project-dark.png"));
            newProject.SetAssetsForSmokeTest(new Models.ImportAssetSelection(ADTranscriptPath: "descriptions.srt"));
            if (newProject.IsPrimaryButtonEnabled) throw new InvalidOperationException("AD-only import allowed creation without a transcript.");
            newProject.SetAssetsForSmokeTest(new Models.ImportAssetSelection(TranscriptPath: "transcript.srt", ADAudioPath: "narration.wav"));
            if (!newProject.IsPrimaryButtonEnabled || newProject.SecondaryButtonText != "Create Project & Combine") throw new InvalidOperationException("New Project does not offer optional audio combination.");
            await Task.Delay(100);
            newProject.ScrollToAssetsForSmokeTest();
            await Task.Delay(100);
            await CaptureSmokeImage(newProject, Path.Combine(Path.GetDirectoryName(report)!, "new-project-assets-dark.png"));
            newProject.SelectYouTubeForSmokeTest("https://www.youtube.com/watch?v=jfKfPfyJRdk");
            await Task.Delay(50);
            if (newProject.IsPrimaryButtonEnabled || !newProject.IsYouTube) throw new InvalidOperationException("New Project allowed a YouTube source before fetching its details.");
            newProject.SetYouTubeDetailsForSmokeTest("Public video preview");
            if (!newProject.IsPrimaryButtonEnabled) throw new InvalidOperationException("New Project did not enable Create after reviewing YouTube details.");
            if (newProject.AssetSelection.ADAudioPath != "narration.wav" || newProject.AssetSelection.TranscriptPath != "transcript.srt" ||
                newProject.SecondaryButtonText != "Create Project & Combine" || !newProject.IsSecondaryButtonEnabled)
                throw new InvalidOperationException("YouTube creation did not preserve imported assets and its described-audio Combine action.");
            await CaptureSmokeImage(newProject, Path.Combine(Path.GetDirectoryName(report)!, "new-project-youtube-dark.png"));
            newProject.Hide(); await newProjectDisplay;
            PreviewTheme(originalTheme);
            var export = new Views.ExportDialog(Model);
            if (Model.Project!.HasCompleteExportNarration || export.AudioChoiceForSmokeTest(1) || export.AudioChoiceForSmokeTest(2))
                throw new InvalidOperationException("Missing narration left narration-dependent audio enabled.");
            if (!export.CaptionChoiceForSmokeTest(3)) throw new InvalidOperationException("Available AD Transcript text was disabled.");
            var exportDisplay = ShowDialogAsync(export, 1040, 760); await Task.Delay(250);
            PreviewTheme("dark"); await Task.Delay(80);
            if (export.ActualTheme != ElementTheme.Dark) throw new InvalidOperationException("An open Export dialog did not switch to dark appearance.");
            PreviewTheme("light"); await Task.Delay(80);
            if (export.ActualTheme != ElementTheme.Light) throw new InvalidOperationException("An open Export dialog did not switch to light appearance.");
            await VerifyDialogThemesForSmokeTestAsync(export, report, "export-window");
            PreviewTheme(originalTheme);
            if (export.FooterForSmokeTest.HorizontalAlignment != HorizontalAlignment.Center ||
                export.FooterForSmokeTest.Children.Count != 2)
                throw new InvalidOperationException("Export actions are not centered in the dialog.");
            await CaptureSmokeImage(export, Path.Combine(Path.GetDirectoryName(report)!, "export-dialog.png"));
            foreach (var (section, command) in new[] { (0, "export-closed-caption-video"), (1, "export-original-audio"),
                (2, "export-subtitles"), (3, "export-hls") })
            {
                export.NavigateForSmokeTest(section);
                await Task.Delay(100);
                if (export.RequestForSmokeTest().Command != command)
                    throw new InvalidOperationException("Export category did not select its engine command.");
                await CaptureSmokeImage(export,
                    Path.Combine(Path.GetDirectoryName(report)!, $"export-{section}.png"));
            }
            var originalVideo = export.RequestForSmokeTest(0, 0, 0);
            if (originalVideo.Command != "export-original-video") throw new InvalidOperationException("Original video export is misrouted.");
            var dualCaptions = export.RequestForSmokeTest(0, 0, 5);
            if (dualCaptions.Command != "export-dual-closed-caption-video") throw new InvalidOperationException("Dual-caption video export is misrouted.");
            var describedCaptions = export.RequestForSmokeTest(0, 2, 4);
            if (describedCaptions.Command != "export-open-audio-description-video" || !describedCaptions.Arguments.Contains("combined") || !describedCaptions.Arguments.Contains("open"))
                throw new InvalidOperationException("Described video caption export is misrouted.");
            if (export.CanSaveForSmokeTest) throw new InvalidOperationException("Save bypassed missing narration.");
            export.FormatsForSmokeTest("m4v", "mp3");
            if (!export.RequestForSmokeTest(0, 0, 1).Arguments.Contains("m4v") || !export.RequestForSmokeTest(1, 0, 0).Arguments.Contains("mp3"))
                throw new InvalidOperationException("Selected formats did not reach export commands.");
            var summaryExport = export.RequestForSmokeTest(2, 1, 0);
            if (summaryExport.Command != "export-subtitles" || !summaryExport.Arguments.Contains("md")) throw new InvalidOperationException("Summary Markdown export is misrouted.");
            var jsonExport = export.RequestForSmokeTest(2, 0, 3);
            if (!jsonExport.Arguments.Contains("json")) throw new InvalidOperationException("JSON transcript export is misrouted.");
            var webExport = export.RequestForSmokeTest(3, 0, 5);
            if (webExport.Command != "export-hls" || !webExport.Arguments.Contains("dual-closed") || !webExport.Arguments.Contains("original"))
                throw new InvalidOperationException("Web Playback export is misrouted.");
            export.Hide(); await exportDisplay;
            PreviewTheme("light");
            var (deleteDialog, deleteBoxes) = CreateDeleteGeneratedDialog();
            var deleteDisplay = ShowDialogAsync(deleteDialog, 620, 820);
            await Task.Delay(200);
            if (deleteDialog.IsPrimaryButtonEnabled)
                throw new InvalidOperationException("Delete generated content allowed deletion with nothing selected.");
            deleteBoxes[0].IsChecked = true;
            Button? deleteButton = null;
            for (var attempt = 0; attempt < 40; attempt++)
            {
                deleteButton = FindButton(deleteDialog, "Delete selected");
                if (deleteButton is { IsEnabled: true }) break;
                await Task.Delay(25);
            }
            if (deleteButton is not { IsEnabled: true })
                throw new InvalidOperationException("Delete generated content did not enable its primary action in the template.");
            await Task.Delay(100);
            if (!deleteDialog.IsPrimaryButtonEnabled)
                throw new InvalidOperationException("Delete generated content did not enable deletion after a selection.");
            var deletePositions = deleteBoxes.Select(box =>
                $"{box.Content}:h{box.ActualHeight:F0}@y{box.TransformToVisual(deleteDialog).TransformPoint(new Windows.Foundation.Point()).Y:F0}/{box.Visibility}");
            if (deleteBoxes.Any(box => box.ActualHeight <= 0 || box.Visibility != Visibility.Visible))
                throw new InvalidOperationException("Delete generated content did not render every option: " + string.Join(", ", deletePositions));
            await VerifyDialogThemesForSmokeTestAsync(deleteDialog, report, "delete-generated-window");
            await CaptureSmokeImage(deleteDialog, Path.Combine(Path.GetDirectoryName(report)!, "delete-generated-light.png"));
            deleteDialog.Hide(); await deleteDisplay;
            var errorDialog = new ContentDialog
            {
                Title = "Action could not finish",
                Content = DialogMessage("Check your settings and the diagnostic log, then retry. Your saved project remains available."),
                CloseButtonText = "Close"
            };
            var errorDisplay = ShowDialogAsync(errorDialog);
            await Task.Delay(250);
            var errorLines = ((StackPanel)errorDialog.Content).Children.OfType<TextBlock>().ToList();
            if (errorLines.Count < 2 || errorLines.Any(line => line.ActualHeight <= 0))
                throw new InvalidOperationException(
                    $"The error message did not wrap: lines={errorLines.Count}, " +
                    $"heights={string.Join(',', errorLines.Select(line => line.ActualHeight.ToString("F0")))}, " +
                    $"text={string.Join(" / ", errorLines.Select(line => line.Text))}, " +
                    $"dialog={errorDialog.ActualWidth:F0}x{errorDialog.ActualHeight:F0}.");
            await VerifyDialogThemesForSmokeTestAsync(errorDialog, report, "error-dialog-window");
            await CaptureSmokeImage(errorDialog, Path.Combine(Path.GetDirectoryName(report)!, "error-dialog-light.png"));
            errorDialog.Hide(); await errorDisplay;
            await VerifyCompactDialogFormsForSmokeTestAsync(report);
            await VerifyDialogCommandsForSmokeTestAsync(report);
            PreviewTheme(originalTheme);
            var result = await Model.RunAsync("doctor", needsProject: false);
            if (result.Count == 0) throw new InvalidOperationException("Engine doctor failed.");
            var original = Model.Project!.Cues[0].Text;
            Model.Project.Cues[0].Text = "A blue screen.";
            await Model.SaveSegmentsAsync(Model.Project.Transcript, Model.Project.Cues);
            if (Model.Project!.Cues[0].Text != "A blue screen." || Model.Project.Cues[0].Origin != "ai_modified") throw new InvalidOperationException("Native edit persistence or origin tracking failed: " + Model.Status);
            await Model.RunAsync("undo-cues");
            if (Model.Project!.Cues[0].Text != original) throw new InvalidOperationException("Native undo failed: " + Model.Status);
            await Model.RunAsync("redo-cues");
            if (Model.Project!.Cues[0].Text != "A blue screen.") throw new InvalidOperationException("Native redo failed: " + Model.Status);
            foreach (var provider in new[] { "windows-core", "sapi5" })
            {
                Model.Settings.Set("ttsMode", provider);
                var narrationTask = Model.RunAsync("synthesize-tts");
                ActiveExports.VerifyCenteredForSmokeTest();
                await narrationTask;
                if (ActiveExports.Visibility != Visibility.Collapsed) throw new InvalidOperationException("Processing progress did not close after narration finished.");
                if (Model.Status != "Complete" || !Model.Project!.Cues[0].AudioReady) throw new InvalidOperationException(provider + " narration failed: " + Model.Status);
            }
            if (!Model.Project!.HasCompleteExportNarration) throw new InvalidOperationException("Current narration was rejected for export.");
            var readyExport = new Views.ExportDialog(Model);
            readyExport.VerifyExportCategoriesForSmokeTest();
            if (!readyExport.AudioChoiceForSmokeTest(1) || !readyExport.AudioChoiceForSmokeTest(2)) throw new InvalidOperationException("Current narration did not enable audio choices.");
            var cue = Model.Project.Cues[0];
            var savedText = cue.Text;
            cue.Text += " changed";
            if (Model.Project.HasCompleteExportNarration) throw new InvalidOperationException("Outdated narration was accepted.");
            cue.Text = savedText;
            var savedDuration = Model.Project.Duration;
            Model.Project.Duration = double.NaN;
            if (Model.Project.HasCompleteExportNarration) throw new InvalidOperationException("Invalid duration was accepted.");
            Model.Project.Duration = savedDuration;
            var savedType = Model.Project.SourceType;
            Model.Project.SourceType = "youtube";
            var youtubeExport = new Views.ExportDialog(Model);
            if (youtubeExport.RequestForSmokeTest(1, 0, 0).Command != "export-youtube-original-audio" ||
                youtubeExport.RequestForSmokeTest(1, 1, 0).Command != "export-audio" ||
                youtubeExport.RequestForSmokeTest(1, 2, 0).Command != "export-youtube-described-audio")
                throw new InvalidOperationException("YouTube audio choices are misrouted.");
            Model.Project.SourceType = savedType;
            var originalName = Model.Project!.Name;
            await Model.RunAsync("rename-project", new[] { "--name", "Renamed native project" });
            Model.Reload("renamed native");
            if (Model.Projects.Count != 1 || Model.Projects[0].Name != "Renamed native project") throw new InvalidOperationException("Project rename/search failed.");
            Model.Reload("no matching project title");
            if (Model.Projects.Count != 0) throw new InvalidOperationException("Project search did not filter results.");
            await Model.RunAsync("rename-project", new[] { "--name", originalName });
            var child = App.OpenWindow(folder);
            await Task.Delay(300);
            if (child.Model.Project?.Folder != Model.Project?.Folder || ReferenceEquals(child.Model, Model)) throw new InvalidOperationException("Independent editor window did not open.");
            PreviewTheme("dark"); await Task.Delay(80);
            if (child.Root.ActualTheme != ElementTheme.Dark || Root.ActualTheme != ElementTheme.Dark)
                throw new InvalidOperationException("Another open editor did not follow the app-wide dark theme.");
            App.CommitTheme("dark");
            if (child.Model.Settings.Get("theme") != "dark" || Model.Settings.Get("theme") != "dark")
                throw new InvalidOperationException("Saved theme selection did not update all open editor settings.");
            App.CommitTheme(originalTheme);
            child.Section.SelectedIndex = 1;
            if (Section.SelectedIndex == 1) throw new InvalidOperationException("Editor view selection leaked between windows.");
            var mediaExportTask = Model.RunAsync("export-original-audio", new[] { "--media-format", "wav", "--replace-existing" });
            ActiveExports.VerifyCenteredForSmokeTest();
            child.ActiveExports.VerifyCenteredForSmokeTest();
            if (Model.ExportSessions.Count != 1 || child.Model.ExportSessions.Count != 1 || child.ActiveExports.SessionCount != 1)
                throw new InvalidOperationException("Export activity did not appear in another editor.");
            var session = Model.ExportSessions[0];
            var observedProgress = new System.Collections.Generic.List<double>();
            session.PropertyChanged += (_, e) => { if (e.PropertyName == nameof(session.Percent) && !session.IsIndeterminate) observedProgress.Add(session.Percent); };
            await mediaExportTask;
            if (ActiveExports.Visibility != Visibility.Collapsed || child.ActiveExports.Visibility != Visibility.Collapsed)
                throw new InvalidOperationException("Centered export progress did not close in every window.");
            if (Model.Status != "Complete" || Model.ExportSessions.Count != 0 || !observedProgress.Any(p => p > 0 && p < 100) ||
                !observedProgress.SequenceEqual(observedProgress.OrderBy(p => p)))
                throw new InvalidOperationException("Export progress/completion was not monotonic or did not clear.");
            var replacementDialog = new Views.ExportDialog(Model);
            replacementDialog.RequestForSmokeTest(1, 0, 0);
            if (!replacementDialog.CanSaveForSmokeTest) throw new InvalidOperationException("Save must remain available before checking destination collisions.");
            var childClosed = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            child.Closed += (_, _) => childClosed.TrySetResult(true);
            await child.RunDesktopCommandAsync("close-window");
            if (await Task.WhenAny(childClosed.Task, Task.Delay(3000)) != childClosed.Task || !childClosed.Task.Result)
                throw new InvalidOperationException("Close Window did not close the editor window.");
            child.UpdateDesktopMenuStates();
            child.UpdatePlaybackControls();
            if (child.IsVideoPlaying() || child.CanRunDesktopCommand("play") || child.CanRunDesktopCommand("settings"))
                throw new InvalidOperationException("A closed editor still accepts commands or queries disposed playback state.");
            await Model.RunAsync("export-subtitles", new[] { "--content", "combined", "--format", "srt", "--replace-existing" });
            if (Model.Status != "Complete") throw new InvalidOperationException("Native subtitle export failed: " + Model.Status);
            var subtitle = Model.Project!.Exports.FirstOrDefault(path => path.EndsWith(".srt", StringComparison.OrdinalIgnoreCase));
            if (subtitle == null || !File.ReadAllText(Model.Project.Resolve(subtitle)).Contains("A blue screen.")) throw new InvalidOperationException("Exported subtitles are missing the edited description.");
            if (!InspectorExports.Children.OfType<Microsoft.UI.Xaml.Controls.StackPanel>()
                .SelectMany(section => section.Children.OfType<Microsoft.UI.Xaml.Controls.Grid>())
                .Any(row => row.Children.OfType<Microsoft.UI.Xaml.Controls.Button>().LastOrDefault()?.Tag as string == subtitle))
                throw new InvalidOperationException("Created exports do not have their own information actions.");
            var syntheticExports = new[] {
                $"exports/{Model.Project.Name} - Video.mp4", $"exports/{Model.Project.Name} - Audio.mp3",
                $"exports/{Model.Project.Name} - Transcript.vtt", $"exports/{Model.Project.Name} - Summary.md",
                $"exports/{Model.Project.Name} - Descriptions.json", $"exports/{Model.Project.Name} - Web Playback"
            };
            Model.Project.Exports.AddRange(syntheticExports);
            PopulateExportGroups(InspectorExports, Model.Project);
            PopulateExportGroups(ExportGroups, Model.Project);
            var expectedExportGroups = new[] { "Video", "Audio", "Transcript", "Summary", "Descriptions", "AD Transcript", "Web Playback" };
            foreach (var panel in new[] { InspectorExports, ExportGroups })
            {
                var headings = panel.Children.OfType<Microsoft.UI.Xaml.Controls.StackPanel>()
                    .Select(section => section.Children.OfType<Microsoft.UI.Xaml.Controls.StackPanel>().First())
                    .Select(heading => (Icon: heading.Children.OfType<Microsoft.UI.Xaml.Controls.FontIcon>().FirstOrDefault(),
                        Name: heading.Children.OfType<Microsoft.UI.Xaml.Controls.TextBlock>().FirstOrDefault()?.Text)).ToList();
                if (!headings.Select(heading => heading.Name).SequenceEqual(expectedExportGroups) ||
                    headings.Any(heading => heading.Icon == null))
                    throw new InvalidOperationException("Created exports were not grouped with matching format icons.");
            }
            var previousSection = Section.SelectedIndex;
            Section.SelectedIndex = 3;
            await Task.Delay(100);
            await CaptureSmokeImage(Root, Path.Combine(Path.GetDirectoryName(report)!, "export-groups.png"));
            Section.SelectedIndex = previousSection;
            Model.Project.Exports.RemoveAll(relative => syntheticExports.Contains(relative));
            PopulateExportGroups(InspectorExports, Model.Project);
            PopulateExportGroups(ExportGroups, Model.Project);
            video.Volume = narrationPlayer.Volume = 0;
            Model.Settings.Set("narrationPlayback", "generated");
            Narration.IsOn = true;
            Seek(2.1); video.Play();
            for (var attempt = 0; attempt < 30 && (video.PlaybackSession.PlaybackState != Windows.Media.Playback.MediaPlaybackState.Playing || narrationPlayer.Source == null); attempt++) await Task.Delay(100);
            if (video.PlaybackSession.PlaybackState != Windows.Media.Playback.MediaPlaybackState.Playing || narrationPlayer.Source == null) throw new InvalidOperationException("Native synchronized narration did not start during its cue.");
            UpdatePlaybackControls();
            if (VideoPlayPauseButton.Content as string != "\uE769" || videoPlayMenuItem?.Text != "Pause Video" ||
                videoPlayMenuItem?.Icon is not Microsoft.UI.Xaml.Controls.FontIcon { Glyph: "\uE769" })
                throw new InvalidOperationException("The video transport did not switch from Play to Pause.");
            await Task.Delay(150);
            if (Math.Abs(narrationPlayer.PlaybackSession.Position.TotalSeconds - (Position - 2)) > .5) throw new InvalidOperationException("Narration position did not align with the video cue.");
            video.Pause(); await Task.Delay(250);
            if (VideoPlayPauseButton.Content as string != "\uE768" || videoPlayMenuItem?.Text != "Play Video" ||
                videoPlayMenuItem?.Icon is not Microsoft.UI.Xaml.Controls.FontIcon { Glyph: "\uE768" })
                throw new InvalidOperationException("The video transport did not switch from Pause to Play.");
            if (narrationPlayer.PlaybackSession.PlaybackState == Windows.Media.Playback.MediaPlaybackState.Playing) throw new InvalidOperationException("Narration kept playing while video was paused.");
            PlayPause_Click(this, new RoutedEventArgs());
            for (var attempt = 0; attempt < 20 && video.PlaybackSession.PlaybackState != Windows.Media.Playback.MediaPlaybackState.Playing; attempt++) await Task.Delay(50);
            UpdatePlaybackControls();
            if (VideoPlayPauseButton.Content as string != "\uE769" || videoPlayMenuItem?.Text != "Pause Video" ||
                videoPlayMenuItem?.Icon is not Microsoft.UI.Xaml.Controls.FontIcon { Glyph: "\uE769" })
                throw new InvalidOperationException("The video transport did not resume through its shared button.");
            Seek(6); video.Play(); await Task.Delay(250);
            if (narrationPlayer.PlaybackSession.PlaybackState == Windows.Media.Playback.MediaPlaybackState.Playing || activeCue != null) throw new InvalidOperationException("Narration remained active after seeking beyond its cue.");
            video.Pause(); Narration.IsOn = false;
            Model.Settings.Set("ttsMode", "windows-core");
            Section.SelectedIndex = 1;
            var firstReadback = readback.SpeakAsync("This first summary is replaced immediately.");
            var nextReadback = readback.SpeakAsync("This replacement summary must be cancellable.");
            await Task.Delay(150);
            if (readback.State == Services.SummaryReadbackState.Playing)
            {
                if (SummaryPlayButton.Content as string != "Pause" || CanRunDesktopCommand("summary-play") || !CanRunDesktopCommand("summary-pause"))
                    throw new InvalidOperationException("Summary Play did not become Pause in the view and menu.");
                readback.Pause(); await Task.Delay(50);
                if (SummaryPlayButton.Content as string != "Resume" || !CanRunDesktopCommand("summary-play") || CanRunDesktopCommand("summary-pause"))
                    throw new InvalidOperationException("Summary Pause did not become Resume in the view and menu.");
                readback.Resume();
            }
            readback.Stop(); await Task.Delay(50);
            if (SummaryPlayButton.Content as string != "Play" || CanRunDesktopCommand("summary-stop"))
                throw new InvalidOperationException("Summary Stop did not reset playback controls.");
            foreach (var task in new[] { firstReadback, nextReadback })
            {
                try { await task; } catch (OperationCanceledException) { }
            }
            await VerifyExistingAssetEngineWorkflowForSmokeTestAsync(folder);
            ClearProject();
            if (Model.Project != null || EditorWorkspace.Visibility != Visibility.Collapsed || GalleryPanel.Visibility != Visibility.Visible || TopInspectorToggle.Visibility != Visibility.Collapsed || video.Source != null || narrationPlayer.Source != null || ProjectDetails.Children.Count != 0) throw new InvalidOperationException("Project close left stale editor state.");
            var emptySettings = new Views.SettingsDialog(Model, this, 1);
            var emptyDisplay = emptySettings.ShowAsync();
            await Task.Delay(250);
            foreach (var theme in new[] { "dark", "light", "dark" })
            {
                PreviewTheme(theme);
                await Task.Delay(100);
                emptySettings.VerifyEmptyProjectsSettingsForSmokeTest();
                await CaptureSmokeImage((FrameworkElement)emptySettings.Content, Path.Combine(Path.GetDirectoryName(report)!, $"settings-projects-empty-{theme}.png"));
            }
            PreviewTheme(originalTheme);
            emptySettings.Hide(); await emptyDisplay;
            await File.WriteAllTextAsync(report, "PASS: native light/dark UI, menus and segment selection; summary Markdown reading, saving, undo/redo and speech; New Project asset validation and optional Combine; external project folder persistence; SRT/VTT/TXT timing review; real transcript/description/summary/audio imports and two-track combination; Help search/navigation/theme; OneCore/SAPI narration, subtitle export, and five editor sections.");
        }
        catch (Exception ex) { await File.WriteAllTextAsync(report, ex.ToString()); }
        finally
        {
            dialogPresentationVerifier = null;
            App.HelpWindowForSmokeTest?.Close();
            openSettings?.Hide();
            // The final window can abort the Close call while dispatcher shutdown completes.
            try { if (!windowClosed) Close(); }
            catch (System.Runtime.InteropServices.COMException ex) when (ex.HResult == unchecked((int)0x80004004)) { }
            try { settingsLifetimeTestHost?.Close(); }
            catch (System.Runtime.InteropServices.COMException ex) when (ex.HResult == unchecked((int)0x80004004)) { }
            Application.Current.Exit();
        }
    }
    private async Task VerifyHelpWindowForSmokeTestAsync(string report)
    {
        var originalTheme = Model.Settings.Get("theme", "system");
        App.OpenHelpWindow(Root.ActualTheme);
        var help = App.HelpWindowForSmokeTest ?? throw new InvalidOperationException("Help did not open from the active window.");
        try
        {
            await help.VerifyGuideForSmokeTestAsync();
            await help.VerifyOfflineRecoveryForSmokeTestAsync();
            foreach (var theme in new[] { "light", "dark" })
            {
                PreviewTheme(theme); await Task.Delay(150);
                VerifyHelpCaptionForSmokeTest(help, theme == "dark");
                if (!(await help.ReaderStateForSmokeTest()).Contains(theme == "dark" ? "true" : "false")) throw new InvalidOperationException("Help reader theme differs from its window.");
                await CaptureSmokeImage((FrameworkElement)help.Content, Path.Combine(Path.GetDirectoryName(report)!, "help-information-" + theme + ".png"));
            }
        }
        finally { help.Close(); PreviewTheme(originalTheme); }
    }

    private static void VerifyHelpCaptionForSmokeTest(Views.HelpWindow help, bool dark)
    {
        var caption = help.AppWindow.TitleBar;
        if (caption.ButtonHoverBackgroundColor is not { A: 255 } hover || caption.ButtonPressedBackgroundColor is not { A: 255 } pressed ||
            (!dark && (hover.R < 200 || hover.G < 200 || hover.B < 200)) || (dark && (hover.R > 90 || hover.G > 90 || hover.B > 90)))
            throw new InvalidOperationException("Help caption hover and pressed backgrounds must be opaque and follow the window theme.");
    }

    private async Task VerifyAboutMenuForSmokeTestAsync()
    {
        var busy = typeof(ViewModels.StudioViewModel).GetProperty(nameof(ViewModels.StudioViewModel.Busy))!;
        var original = Model.Busy;
        try
        {
            foreach (var processing in new[] { false, true })
            {
                busy.SetValue(Model, processing);
                UpdateDesktopMenuStates();
                if (!CanRunDesktopCommand("about") || !CanRunDesktopCommand("help") || !CanRunDesktopCommand("settings"))
                    throw new InvalidOperationException("Editor or gallery processing disabled application information commands.");
                var item = desktopMenuItems["about"].First(item => item.Visibility == Visibility.Visible);
                var peer = new Microsoft.UI.Xaml.Automation.Peers.MenuFlyoutItemAutomationPeer(item);
                var invoke = (Microsoft.UI.Xaml.Automation.Provider.IInvokeProvider)peer.GetPattern(Microsoft.UI.Xaml.Automation.Peers.PatternInterface.Invoke);
                invoke.Invoke();
                await Task.Delay(200);
                var settings = openSettings ?? throw new InvalidOperationException("Help > About did not open Settings.");
                if (!settings.AppWindow.IsVisible || settings.SelectedSectionForSmokeTest != 7) throw new InvalidOperationException("Help > About opened the wrong page.");
                settings.SelectSection(0);
                if (settings.AppWindow.Presenter is Microsoft.UI.Windowing.OverlappedPresenter presenter) presenter.Minimize();
                invoke.Invoke();
                await Task.Delay(200);
                if (!ReferenceEquals(settings, openSettings) || settings.SelectedSectionForSmokeTest != 7 ||
                    settings.AppWindow.Presenter is Microsoft.UI.Windowing.OverlappedPresenter { State: Microsoft.UI.Windowing.OverlappedPresenterState.Minimized })
                    throw new InvalidOperationException("Help > About failed to restore and reuse Settings.");
                settings.Hide();
                await Task.Delay(100);
            }
        }
        finally { busy.SetValue(Model, original); openSettings?.Hide(); UpdateDesktopMenuStates(); }
    }

    private async Task OpenSavedPrivateProjectForSmokeTestAsync(Models.StudioProject project, bool approve)
    {
        var opened = new TaskCompletionSource<ContentDialog>(TaskCreationOptions.RunContinuationsAsynchronously);
        void Capture(ContentDialog dialog, Window window)
        {
            if (dialog.Title as string == YouTubeVideo.PrivateOpenTitle) opened.TrySetResult(dialog);
        }
        Views.WindowedDialog.ReadyToShow += Capture;
        var opening = OpenGalleryProjectAsync(project);
        try
        {
            var dialog = await opened.Task.WaitAsync(TimeSpan.FromSeconds(15));
            if (dialog.DefaultButton != ContentDialogButton.Close || dialog.PrimaryButtonText != "Open Anyway" || dialog.CloseButtonText != "Cancel")
                throw new InvalidOperationException("The saved-private warning did not offer the expected choices.");
            var label = approve ? "Open Anyway" : "Cancel";
            var button = FindButton(dialog, label) ?? throw new InvalidOperationException("Missing saved-private action: " + label);
            var peer = new Microsoft.UI.Xaml.Automation.Peers.ButtonAutomationPeer(button);
            ((Microsoft.UI.Xaml.Automation.Provider.IInvokeProvider)peer.GetPattern(Microsoft.UI.Xaml.Automation.Peers.PatternInterface.Invoke)).Invoke();
            await opening.WaitAsync(TimeSpan.FromSeconds(10));
        }
        finally
        {
            Views.WindowedDialog.ReadyToShow -= Capture;
            if (!opening.IsCompleted && opened.Task.IsCompletedSuccessfully) opened.Task.Result.Hide();
        }
    }

    private async Task VerifyShellProjectOpeningForSmokeTestAsync(string fixtureFolder)
    {
        var library = Path.GetDirectoryName(fixtureFolder)!;
        if (!string.Equals(Path.GetFullPath(Model.Settings.Get("libraryPath")), Path.GetFullPath(library), StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Shell-open smoke test must use the isolated project library.");
        var source = Path.Combine(library, $"shell-open-{Guid.NewGuid():N}.mp4");
        File.Copy(Path.Combine(fixtureFolder, "source.mp4"), source);
        try
        {
            var opening = OpenVideoFileAsync(source);
            if (!SharedVideoRing.IsActive || SharedVideoProgress.Visibility != Visibility.Visible ||
                SharedVideoProgressText.Text != "Importing video…" ||
                Microsoft.UI.Xaml.Automation.AutomationProperties.GetAutomationId(SharedVideoRing) != "shared-file-opening")
                throw new InvalidOperationException("The external file import did not expose progress.");
            await opening;
            if (SharedVideoRing.IsActive || SharedVideoProgress.Visibility != Visibility.Collapsed ||
                !IsProjectLibrary || GalleryPanel.Visibility != Visibility.Visible)
                throw new InvalidOperationException("The import did not return the library to its normal layout.");
            var imported = Model.Project;
            if (imported == null || imported.Folder == fixtureFolder)
                throw new InvalidOperationException("The shell import did not create a separate project.");
            var deadline = DateTime.UtcNow.AddSeconds(5);
            while (!App.OpenEditorProjects.Any(project => project.Folder == imported.Folder) && DateTime.UtcNow < deadline)
                await Task.Delay(50);
            if (!App.OpenEditorProjects.Any(project => project.Folder == imported.Folder))
                throw new InvalidOperationException("The shell import did not open an editor.");
            var editor = App.EditorForProject(imported);
            if (ReferenceEquals(editor, this) || !ReferenceEquals(App.ProjectLibrary(), this))
                throw new InvalidOperationException("Opening the editor replaced All Projects.");

            var count = App.WindowCountForSmokeTest;
            await OpenVideoFileAsync(source);
            if (App.WindowCountForSmokeTest != count || !ReferenceEquals(editor, App.EditorForProject(imported)))
                throw new InvalidOperationException("A repeat shell activation opened a duplicate editor.");

            var closed = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            editor.Closed += (_, _) => closed.TrySetResult(true);
            await editor.RunDesktopCommandAsync("close-window");
            await closed.Task.WaitAsync(TimeSpan.FromSeconds(5));
            for (var retry = 0; retry < 30 && !searchAnnouncementWindowActive; retry++) await Task.Delay(50);
            if (!ReferenceEquals(App.ProjectLibrary(), this) || windowClosed ||
                GalleryPanel.Visibility != Visibility.Visible || App.WindowCountForSmokeTest != count - 1 ||
                !searchAnnouncementWindowActive)
                throw new InvalidOperationException("File > Close Window did not return focus to All Projects.");

            var presenter = (Microsoft.UI.Windowing.OverlappedPresenter)AppWindow.Presenter;
            var initialState = presenter.State;
            try
            {
                foreach (var expected in new[]
                {
                    Microsoft.UI.Windowing.OverlappedPresenterState.Restored,
                    Microsoft.UI.Windowing.OverlappedPresenterState.Maximized,
                    Microsoft.UI.Windowing.OverlappedPresenterState.Minimized
                })
                {
                    if (expected == Microsoft.UI.Windowing.OverlappedPresenterState.Restored) presenter.Restore();
                    else if (expected == Microsoft.UI.Windowing.OverlappedPresenterState.Maximized) presenter.Maximize();
                    else presenter.Minimize();
                    await Task.Delay(100);
                    if (presenter.State != expected) throw new InvalidOperationException($"Could not set All Projects to {expected}.");
                    var position = AppWindow.Position;
                    var size = AppWindow.Size;
                    // Exercise the gallery command path and then the same editor's normal reuse path.
                    await OpenGalleryProjectAsync(imported, newWindow: true);
                    deadline = DateTime.UtcNow.AddSeconds(5);
                    while (!App.OpenEditorProjects.Any(project => project.Folder == imported.Folder) && DateTime.UtcNow < deadline)
                        await Task.Delay(50);
                    if (!App.OpenEditorProjects.Any(project => project.Folder == imported.Folder))
                        throw new InvalidOperationException($"No editor opened with All Projects {expected}.");
                    editor = App.EditorForProject(imported);
                    if (editor.AppWindow.Presenter is not Microsoft.UI.Windowing.OverlappedPresenter
                        { State: Microsoft.UI.Windowing.OverlappedPresenterState.Maximized })
                        throw new InvalidOperationException("A new editor was not maximized in windowed mode.");
                    await OpenGalleryProjectAsync(imported);
                    if (App.WindowCountForSmokeTest != count ||
                        editor.AppWindow.Presenter is not Microsoft.UI.Windowing.OverlappedPresenter
                        { State: Microsoft.UI.Windowing.OverlappedPresenterState.Maximized })
                        throw new InvalidOperationException("Activating an existing editor changed its maximized state or created a duplicate.");
                    if (presenter.State != expected || !AppWindow.Position.Equals(position) || !AppWindow.Size.Equals(size))
                        throw new InvalidOperationException($"Opening an editor changed All Projects from {expected}.");
                    await OpenVideoFileAsync(source);
                    if (presenter.State != expected || App.WindowCountForSmokeTest != count)
                        throw new InvalidOperationException($"External opening changed All Projects from {expected} or duplicated the editor.");
                    closed = new(TaskCreationOptions.RunContinuationsAsynchronously);
                    editor.Closed += (_, _) => closed.TrySetResult(true);
                    await editor.RunDesktopCommandAsync("close-window");
                    await closed.Task.WaitAsync(TimeSpan.FromSeconds(5));
                    if (presenter.State != expected) throw new InvalidOperationException($"Closing an editor changed All Projects from {expected}.");
                }
                presenter.Restore();
                foreach (var hasCopy in new[] { false, true })
                {
                    // Let native close/collection events from the previous editor settle.
                    await Task.Delay(350);
                    Model.Engine.Log($"Window smoke: opening a saved private project with local playback copy: {hasCopy}.");
                    var privateFolder = Path.Combine(library, $"private-open-{Guid.NewGuid():N}");
                    Directory.CreateDirectory(privateFolder);
                    var manifestPath = Path.Combine(privateFolder, "project.json");
                    try
                    {
                        var manifest = System.Text.Json.Nodes.JsonNode.Parse(File.ReadAllText(Path.Combine(fixtureFolder, "project.json")))!.AsObject();
                        manifest["id"] = Path.GetFileName(privateFolder);
                        manifest["sourceType"] = "youtube";
                        manifest["youtubeURL"] = "https://www.youtube.com/watch?v=TESTvideo01";
                        manifest["youtubePrivateChannelID"] = "smoke-unconnected-owner";
                        if (hasCopy)
                        {
                            File.Copy(source, Path.Combine(privateFolder, "source.mp4"));
                            manifest["youtubePlaybackVideo"] = new System.Text.Json.Nodes.JsonObject
                            {
                                ["path"] = "source.mp4", ["thumbnail"] = "", ["sourceName"] = "source.mp4",
                                ["duration"] = manifest["duration"]!.GetValue<double>()
                            };
                        }
                        var saved = manifest.ToJsonString();
                        File.WriteAllText(manifestPath, saved);
                        var privateProject = Models.StudioProject.TryLoad(privateFolder)
                            ?? throw new InvalidOperationException("The private test manifest did not load.");
                        try
                        {
                            App.OpenOrActivateEditor(privateFolder);
                            throw new InvalidOperationException("The private editor opened through an unapproved route.");
                        }
                        catch (PrivateYouTubeAccessException) { }
                        await OpenSavedPrivateProjectForSmokeTestAsync(privateProject, approve: false);
                        if (App.OpenEditorProjects.Any(project => project.Folder == privateFolder))
                            throw new InvalidOperationException("Cancel opened the saved private project.");
                        await OpenSavedPrivateProjectForSmokeTestAsync(privateProject, approve: true);
                        deadline = DateTime.UtcNow.AddSeconds(8);
                        while (!App.OpenEditorProjects.Any(project => project.Folder == privateFolder) && DateTime.UtcNow < deadline)
                            await Task.Delay(50);
                        if (!App.OpenEditorProjects.Any(project => project.Folder == privateFolder))
                            throw new InvalidOperationException("The approved private editor did not open.");
                        editor = App.EditorForProject(privateProject);
                        await Task.Delay(500);
                        if (editor.Model.Project is not { RequiresLocalYouTubePlayback: true, HasSummary: true, HasTranscript: true } opened ||
                            editor.Model.CanUseAI || opened.UsesYouTubePlayer ||
                            (opened.ActiveLocalYouTubePlaybackPath != null) != hasCopy ||
                            (editor.PrivateYouTubePlaybackPanel.Visibility == Visibility.Visible) == hasCopy ||
                            App.WindowCountForSmokeTest != count || !IsProjectLibrary)
                            throw new InvalidOperationException("The saved private project must open with its results, local playback state and remote analysis safeguards.");
                        var previousProvider = editor.Model.Settings.Get("aiProvider");
                        foreach (var provider in new[] { "developer", "vertex" })
                        {
                            editor.Model.Settings.Set("aiProvider", provider);
                            if (editor.Model.CanUseAI || editor.CanRunDesktopCommand("analyze") || editor.CanRunDesktopCommand("transcribe"))
                                throw new InvalidOperationException("Analysis was enabled for a private project with " + provider);
                        }
                        editor.Model.Settings.Set("aiProvider", previousProvider);
                        Model.Engine.Log($"Window smoke: verified the private editor with local playback copy: {hasCopy}.");
                        closed = new(TaskCreationOptions.RunContinuationsAsynchronously);
                        editor.Closed += (_, _) => closed.TrySetResult(true);
                        await editor.RunDesktopCommandAsync("close-window");
                        await closed.Task.WaitAsync(TimeSpan.FromSeconds(5));
                        if (File.ReadAllText(manifestPath) != saved)
                            throw new InvalidOperationException("Opening the saved private project changed the file.");
                    }
                    finally { Directory.Delete(privateFolder, true); }
                }
            }
            finally
            {
                if (initialState == Microsoft.UI.Windowing.OverlappedPresenterState.Maximized) presenter.Maximize();
                else presenter.Restore();
            }
        }
        finally { File.Delete(source); }
    }

    private async Task VerifySettingsWindowLifetimeForSmokeTestAsync()
    {
        // An auxiliary test host keeps the dispatcher alive to inspect the final close.
        settingsLifetimeTestHost = new Window { Content = new TextBlock { Text = "Settings lifetime verification" } };
        settingsLifetimeTestHost.Activate();
        var other = App.OpenWindow();
        var settings = new Views.SettingsDialog(Model, this);
        var closed = false;
        settings.Closed += (_, _) => closed = true;
        var display = settings.ShowAsync();
        await Task.Delay(200);
        CloseWindowDeferred();
        await Task.Delay(200);
        if (closed || !App.HasMainWindows) throw new InvalidOperationException("Settings closed while another main window remained open.");
        settings.StageFinalSettingsEditForSmokeTest();
        other.CloseWindowDeferred();
        if (await Task.WhenAny(display, Task.Delay(3000)) != display || !closed || App.HasMainWindows)
            throw new InvalidOperationException("Settings remained open after the final main window closed.");
        Model.Settings.Reload();
        if (Model.Settings.Get("captionFontSize") != "33") throw new InvalidOperationException("Final Settings edits were not saved before automatic closure.");
    }

    private void VerifyGalleryToolbarBoundsForSmokeTest()
    {
        if (GalleryToolbar.Visibility != Visibility.Visible || GallerySearchHost.Visibility != Visibility.Visible)
            throw new InvalidOperationException("Gallery controls must share a header-free toolbar.");
        var controls = new FrameworkElement[] { GalleryFilterProjects, GalleryLayoutButton };
        var centers = controls.Select(control => control.TransformToVisual(Root).TransformPoint(new Windows.Foundation.Point(0, control.ActualHeight / 2)).Y).ToArray();
        if (centers.Max() - centers.Min() > 1)
            throw new InvalidOperationException("Gallery Filter Projects and layout controls do not share a baseline.");
        if (GalleryImport.Content is not SymbolIcon { Symbol: Symbol.Add } || GalleryOpenProject.Content is not FontIcon { Glyph: "\uE8B7" } ||
            Microsoft.UI.Xaml.Automation.AutomationProperties.GetName(GalleryImport) != "New Project" ||
            Microsoft.UI.Xaml.Automation.AutomationProperties.GetName(GalleryOpenProject) != "Open Project Folder")
            throw new InvalidOperationException("Gallery creation/folder actions need native icons and accessible names.");
        var buttons = GalleryToolbarActions.Children.OfType<Button>().ToArray();
        if (buttons.Length != 3 || buttons[0].Content as string != "My YouTube Videos…" || buttons[1] != GalleryImport || buttons[2] != GalleryOpenProject)
            throw new InvalidOperationException("Gallery actions must show My YouTube Videos, New Project and Open Project Folder in order.");
        var searchCenter = GallerySearchHost.TransformToVisual(GalleryToolbar).TransformPoint(new Windows.Foundation.Point(GallerySearchHost.ActualWidth / 2, 0));
        var searchBottom = GallerySearchHost.TransformToVisual(Root).TransformPoint(new Windows.Foundation.Point(0, GallerySearchHost.ActualHeight)).Y;
        if (Math.Abs(searchCenter.X - GalleryToolbar.ActualWidth / 2) > 1 ||
            searchBottom > controls.Min(control => control.TransformToVisual(Root).TransformPoint(new Windows.Foundation.Point()).Y))
            throw new InvalidOperationException("Gallery search must stay centered above the controls.");
        var elements = controls.Concat(new FrameworkElement[] { GalleryToolbarActions, GallerySearchHost }).ToArray();
        var bounds = elements.Select(element =>
        {
            var point = element.TransformToVisual(Root).TransformPoint(new Windows.Foundation.Point());
            return new Windows.Foundation.Rect(point.X, point.Y, element.ActualWidth, element.ActualHeight);
        }).ToArray();
        for (var i = 0; i < bounds.Length; i++)
        {
            var rect = bounds[i];
            if (rect.Width <= 0 || rect.Height <= 0 || rect.Left < 0 || rect.Right > Root.ActualWidth + 1)
                throw new InvalidOperationException("A gallery toolbar control is clipped outside its window.");
            for (var j = i + 1; j < bounds.Length; j++)
                if (Math.Min(rect.Right, bounds[j].Right) - Math.Max(rect.Left, bounds[j].Left) > 1 &&
                    Math.Min(rect.Bottom, bounds[j].Bottom) - Math.Max(rect.Top, bounds[j].Top) > 1)
                    throw new InvalidOperationException("Gallery toolbar controls overlap.");
        }
    }

    private async Task VerifyProjectSortingForSmokeTestAsync()
    {
        foreach (var sort in new[] { "opened", "name", "duration", "type" })
        {
            await RunDesktopCommandAsync("projects-sort-" + sort);
            if (Model.ProjectSort != sort || !Model.Projects.SequenceEqual(Models.ProjectListSort.Apply(Model.Projects, sort, Model.Settings.Data["projectLastOpened"] as System.Text.Json.Nodes.JsonObject)))
                throw new InvalidOperationException("Project sort menu did not apply " + sort);
            if (desktopMenuItems["projects-sort-" + sort].Any(item => item.Visibility != Visibility.Visible || !item.IsEnabled || item.Icon is not FontIcon { Glyph: "\uE73E" }))
                throw new InvalidOperationException("Project sort choice is not checked and enabled: " + sort);
        }
        await RunDesktopCommandAsync("projects-sort-opened");
        if (desktopMenuItems["website"].Any(item => item.Visibility != Visibility.Visible || !item.IsEnabled || item.Text != "VidiStudio Website"))
            throw new InvalidOperationException("Help website command is missing.");
    }

    private async Task VerifyProjectAIProviderFilteringForSmokeTestAsync()
    {
        await RunDesktopCommandAsync("projects-reset-filters");
        var library = Model.Settings.Get("libraryPath");
        var prefix = "Provider fixture " + Guid.NewGuid().ToString("N");
        var originalOptions = Model.ProjectFilterAIProviders;
        if (!Models.SavedAIProcessing.ProjectFilterOptions(Array.Empty<Models.StudioProject>()).SequenceEqual(new[] { "all" }))
            throw new InvalidOperationException("An empty library must offer only All AI Providers.");
        var cases = new[]
        {
            (" multiple", """
                [
                  {"id":"summary","kind":"summary","language":"en","aiProcessing":{"text":{"provider":"gemini-developer","source":"recorded"}},"payload":{"summary":"Test"}},
                  {"id":"old","kind":"summary","language":"hi","aiProcessing":{"text":{"provider":"vertex-ai","source":"assigned"}},"payload":{"summary":"Older"}},
                  {"id":"draft","kind":"summary","language":"en","state":"draft","aiProcessing":{"text":{"provider":"vertex-express","source":"recorded"}},"payload":{"summary":"Draft"}}
                ]
                """),
            (" unknown", """[{"id":"legacy","kind":"summary","origin":"ai","payload":{"summary":"Legacy"}}]"""),
            (" none", """
                [{"id":"manual","kind":"summary","origin":"manual","payload":{"summary":"Manual"}},
                 {"id":"narration","kind":"descriptions","origin":"manual","aiProcessing":{"narration":{"provider":"vertex-express","source":"recorded"}},"payload":{}}]
                """)
        };
        var folders = cases.Select(test => Path.Combine(library, prefix + test.Item1)).ToArray();
        try
        {
            for (var index = 0; index < cases.Length; index++)
            {
                Directory.CreateDirectory(folders[index]);
                using var results = JsonDocument.Parse(cases[index].Item2);
                File.WriteAllText(Path.Combine(folders[index], "project.json"), JsonSerializer.Serialize(new
                {
                    schemaVersion = 3, id = prefix + cases[index].Item1, name = prefix + cases[index].Item1,
                    source = "source.mp4", duration = 1, languageResults = results.RootElement, createdAt = 1, updatedAt = 1
                }));
                _ = Models.StudioProject.Load(folders[index]);
            }
            GallerySearch.Text = prefix;
            await Task.Delay(150);
            FilterGallery();
            var menu = libraryAIProviderMenus.First(item => item.Visibility == Visibility.Visible);
            var menuOptions = menu.Items.OfType<ToggleMenuFlyoutItem>().ToArray();
            var represented = originalOptions.Concat(new[] { "gemini-developer", "vertex-ai", "none", "unknown" }).ToHashSet();
            var expectedOptions = new[] { "all" }.Concat(Models.SavedAIProcessing.ProviderChoices.Where(represented.Contains));
            if (!menuOptions.Select(option => option.Tag as string).SequenceEqual(expectedOptions) ||
                menuOptions.Any(option => option.Icon == null) ||
                Microsoft.UI.Xaml.Automation.AutomationProperties.GetName(GalleryFilterProjects) != "Filter Projects")
                throw new InvalidOperationException("Filter Projects and View must offer only accessible choices represented in the library.");

            string[] VisibleProjects() => Model.Projects.Select(project => project.Name).ToArray();
            void Check(string expected, string option)
            {
                if (!VisibleProjects().SequenceEqual(expected.Length == 0 ? Array.Empty<string>() : new[] { prefix + expected }) ||
                    !menuOptions.Where(item => item.IsChecked).Select(item => item.Tag as string).SequenceEqual(new[] { option }))
                    throw new InvalidOperationException("The project selection or checked filter is incorrect for " + option);
            }
            if (!VisibleProjects().Order().SequenceEqual(cases.Select(test => prefix + test.Item1).Order()))
                throw new InvalidOperationException($"All AI Providers expected {cases.Length} fixtures, saw {VisibleProjects().Length} projects. " +
                    $"Search matches fixture prefix: {GallerySearch.Text == prefix}. Library unchanged: {Model.Settings.Get("libraryPath") == library}. " +
                    $"Gallery ready: {galleryReady}. Source: {gallerySourceFilter}. Language: {Model.LanguageFilter}. Provider: {Model.AIProviderFilter}.");
            await RunDialogAsync(apply: false, fromToolbar: true);
            if (Model.AIProviderFilter != "all" || Model.LanguageFilter != "all" || gallerySourceFilter != "all" || Model.ProjectSort != "opened" ||
                VisibleProjects().Length != cases.Length)
                throw new InvalidOperationException("Cancel changed the applied project filters.");
            await RunDialogAsync(apply: true, fromToolbar: false);
            Check(" multiple", "gemini-developer");
            if (Model.LanguageFilter != "en" || gallerySourceFilter != "local" || Model.ProjectSort != "name" || GallerySearch.Text != prefix)
                throw new InvalidOperationException("Filter did not apply the combined filters and sort or changed search.");
            ApplyGalleryFilters("all", "all", "all");

            foreach (var (filter, project) in new[] { ("gemini-developer", " multiple"), ("vertex-ai", " multiple"),
                     ("unknown", " unknown"), ("none", " none") })
            {
                SelectProviderInView(filter);
                await Task.Delay(80);
                Check(project, filter);
            }
            GalleryGrid.SelectedItem = Model.Projects.Single();
            GalleryList.SelectedItem = Model.Projects.Single();
            SelectProviderInView("vertex-ai");
            await Task.Delay(100);
            Check(" multiple", "vertex-ai");
            if (SelectedGalleryProject() != null)
                throw new InvalidOperationException("View did not clear the hidden project selection.");
            Directory.Delete(folders[0], recursive: true);
            FilterGallery();
            if (!originalOptions.Contains("vertex-ai") &&
                (Model.AIProviderFilter != "all" || menu.Items.Any(item => item.Tag as string == "vertex-ai") ||
                 !VisibleProjects().Order().SequenceEqual(new[] { prefix + " none", prefix + " unknown" })))
                throw new InvalidOperationException("Removing the last project for a provider must restore All AI Providers.");

            void SelectProviderInView(string filter)
            {
                var item = menuOptions.Single(option => option.Tag as string == filter);
                var peer = Microsoft.UI.Xaml.Automation.Peers.FrameworkElementAutomationPeer.CreatePeerForElement(item);
                if (peer?.GetPattern(Microsoft.UI.Xaml.Automation.Peers.PatternInterface.Invoke)
                    is Microsoft.UI.Xaml.Automation.Provider.IInvokeProvider invoke) invoke.Invoke();
                else if (peer?.GetPattern(Microsoft.UI.Xaml.Automation.Peers.PatternInterface.Toggle)
                    is Microsoft.UI.Xaml.Automation.Provider.IToggleProvider toggle) toggle.Toggle();
                else throw new InvalidOperationException("View has no accessible provider action.");
            }

            async Task RunDialogAsync(bool apply, bool fromToolbar)
            {
                ContentDialog? dialog = null;
                void Capture(ContentDialog candidate, Window _) { if (candidate.Title as string == "Filter Projects") dialog = candidate; }
                Views.WindowedDialog.ReadyToShow += Capture;
                try
                {
                    if (fromToolbar) Press(GalleryFilterProjects);
                    else
                    {
                        var command = desktopMenuItems["projects-filter-dialog"].First(item => item.Visibility == Visibility.Visible);
                        var peer = new Microsoft.UI.Xaml.Automation.Peers.MenuFlyoutItemAutomationPeer(command);
                        ((Microsoft.UI.Xaml.Automation.Provider.IInvokeProvider)peer.GetPattern(
                            Microsoft.UI.Xaml.Automation.Peers.PatternInterface.Invoke)).Invoke();
                    }
                    for (var attempt = 0; attempt < 80 && dialog == null; attempt++) await Task.Delay(30);
                    if (dialog == null) throw new InvalidOperationException("Filter Projects did not open.");
                    VerifyCompactDialogLayout(dialog);
                    var candidates = DialogControls((UIElement)dialog.Content).OfType<ComboBox>().ToArray();
                    ComboBox Picker(string name) => candidates.Single(picker => Microsoft.UI.Xaml.Automation.AutomationProperties.GetName(picker) == name);
                    var source = Picker("Source");
                    var language = Picker("Language");
                    var provider = Picker("AI Provider");
                    var sort = Picker("Sort by");
                    var options = (Models.OutputLanguage[])provider.ItemsSource;
                    if (!menuOptions.Select(option => option.Text).SequenceEqual(options.Select(option => option.Title)) ||
                        (provider.SelectedItem as Models.OutputLanguage)?.Id != Model.AIProviderFilter ||
                        (sort.SelectedItem as Models.OutputLanguage)?.Id != Model.ProjectSort ||
                        !((Models.OutputLanguage[])sort.ItemsSource).Select(option => option.Title).SequenceEqual(
                          new[] { "opened", "name", "duration", "type" }.Select(id => DesktopCommandTitle("projects-sort-" + id))))
                        throw new InvalidOperationException("The dialog must match View choices and begin with the applied provider.");
                    source.SelectedIndex = 1;
                    language.SelectedItem = ((Models.OutputLanguage[])language.ItemsSource).Single(option => option.Id == "en");
                    provider.SelectedItem = options.Single(option => option.Id == "gemini-developer");
                    sort.SelectedItem = ((Models.OutputLanguage[])sort.ItemsSource).Single(option => option.Id == "name");
                    if (VisibleProjects().Length != cases.Length)
                        throw new InvalidOperationException("Draft changes filtered the gallery before confirmation.");
                    Press(FindButton(dialog, apply ? "Filter" : "Cancel") ?? throw new InvalidOperationException("Missing dialog action."));
                    for (var attempt = 0; attempt < 80 && projectFiltersOpen; attempt++) await Task.Delay(30);
                    if (projectFiltersOpen) throw new InvalidOperationException("Filter Projects did not close.");
                }
                finally
                {
                    if (projectFiltersOpen) dialog?.Hide();
                    Views.WindowedDialog.ReadyToShow -= Capture;
                }
            }

            static void Press(Button button)
            {
                var peer = new Microsoft.UI.Xaml.Automation.Peers.ButtonAutomationPeer(button);
                ((Microsoft.UI.Xaml.Automation.Provider.IInvokeProvider)peer.GetPattern(
                    Microsoft.UI.Xaml.Automation.Peers.PatternInterface.Invoke)).Invoke();
            }
        }
        finally
        {
            foreach (var folder in folders)
                if (Directory.Exists(folder)) Directory.Delete(folder, recursive: true);
            var sortBeforeReset = Model.ProjectSort;
            await RunDesktopCommandAsync("projects-reset-filters");
            if (Model.AIProviderFilter != "all" || Model.ProjectSort != sortBeforeReset)
                throw new InvalidOperationException("Reset Project Filters must restore all providers and preserve sorting.");
            await RunDesktopCommandAsync("projects-sort-opened");
        }
    }

    internal static async Task DoubleClickVideoRowForSmokeTestAsync(Window window, ListView list, int index)
    {
        var handle = WinRT.Interop.WindowNative.GetWindowHandle(window);
        window.Activate();
        FocusTooltipTestWindow(handle);
        list.ScrollIntoView(list.Items[index]);
        await Task.Delay(120);
        if (window.Content is not FrameworkElement root || list.ContainerFromIndex(index) is not ListViewItem row ||
            !GetTooltipTestCursor(out var original))
            throw new InvalidOperationException("Cannot locate the video row for double-click.");
        var position = row.TransformToVisual(root).TransformPoint(new Windows.Foundation.Point(85, row.ActualHeight / 2));
        var scale = root.XamlRoot.RasterizationScale;
        var point = new TooltipTestPoint { X = (int)(position.X * scale), Y = (int)(position.Y * scale) };
        if (!MapTooltipClientPoint(handle, ref point) || TooltipRootWindow(TooltipWindowAt(point), 2) != handle ||
            !MoveTooltipTestCursor(point.X, point.Y))
            throw new InvalidOperationException("The video row is not reachable by the test pointer.");
        try
        {
            for (var click = 0; click < 2; click++)
            {
                SendTooltipMouseMove(2, 0, 0, 0, UIntPtr.Zero);
                SendTooltipMouseMove(4, 0, 0, 0, UIntPtr.Zero);
                await Task.Delay(65);
            }
            await Task.Delay(150);
        }
        finally { MoveTooltipTestCursor(original.X, original.Y); }
    }

    private async Task VerifyYouTubeLayoutForSmokeTestAsync(string report)
    {
        foreach (var cloud in new[] { true, false })
        foreach (var receivedCode in new[] { true, false })
        {
            var response = System.Text.Encoding.UTF8.GetString(GoogleSignInReturn.Response(cloud, receivedCode));
            var parts = response.Split("\r\n\r\n", 2);
            var service = cloud ? "Google Cloud" : "YouTube";
            var heading = receivedCode ? "Return to VidiStudio" : "Sign-in didn’t finish";
            if (parts.Length != 2 ||
                !parts[0].Contains($"Content-Length: {System.Text.Encoding.UTF8.GetByteCount(parts[1])}\r\n") ||
                !parts[0].Contains("Cache-Control: no-store") ||
                !parts[1].Contains($"{service} sign-in") ||
                !parts[1].Contains($"<h1 id=\"page-heading\">{heading}</h1>") ||
                !parts[1].Contains("data:image/png;base64,") ||
                !parts[1].Contains("prefers-color-scheme: dark") ||
                (!receivedCode && !parts[1].Contains("start a new sign-in in VidiStudio")))
                throw new InvalidOperationException("The packaged Google sign-in return page is missing its branding or expected status.");
        }
        static System.Collections.Generic.IEnumerable<FrameworkElement> Elements(DependencyObject root) {
            if (root is FrameworkElement element) yield return element;
            for (var i = 0; i < Microsoft.UI.Xaml.Media.VisualTreeHelper.GetChildrenCount(root); i++)
                foreach (var child in Elements(Microsoft.UI.Xaml.Media.VisualTreeHelper.GetChild(root, i))) yield return child;
        }
        static void Click(Button button) {
            var peer = new Microsoft.UI.Xaml.Automation.Peers.ButtonAutomationPeer(button);
            ((Microsoft.UI.Xaml.Automation.Provider.IInvokeProvider)peer.GetPattern(Microsoft.UI.Xaml.Automation.Peers.PatternInterface.Invoke)).Invoke();
        }
        var inspectorFixture = new Models.StudioProject
        {
            SourceType = "youtube",
            YoutubeURL = "https://www.youtube.com/watch?v=M7lc1UVf-VE",
            YouTubeMetadata = JsonSerializer.SerializeToElement(new { title = "Saved video title", channelTitle = "Saved channel", videoID = "M7lc1UVf-VE", fetchedAt = 1 })
        };
        YouTubeInspectorDetails.Children.Clear();
        try
        {
            PopulateYouTubeInspector(inspectorFixture);
            var rows = YouTubeInspectorDetails.Children.OfType<Grid>().ToArray();
            var fields = rows.SelectMany(row => row.Children.OfType<TextBlock>()).Select(text => text.Text).ToArray();
            if (!fields.Take(4).SequenceEqual(new[] { "YouTube title", "Saved video title", "Channel", "Saved channel" }) ||
                !fields.Contains("Fetched") || fields.Contains("Information source"))
                throw new InvalidOperationException("YouTube Information must start with the title and channel and retain Fetched.");
            foreach (var (label, value, id) in new[] {
                ("Video ID", "M7lc1UVf-VE", "copy-youtube-video-id-youtube-information"),
                ("Source URL", inspectorFixture.YoutubeURL, "copy-youtube-url-youtube-information")
            })
            {
                var row = rows.Single(item => item.Children.OfType<TextBlock>().FirstOrDefault()?.Text == label);
                if (row.Children.OfType<TextBlock>().Last().Text != value ||
                    AutomationProperties.GetAutomationId(row.Children.OfType<Button>().Single()) != id ||
                    row.ContextFlyout is not MenuFlyout menu || !Equals(menu.Items.OfType<MenuFlyoutItem>().Single().Tag, id))
                    throw new InvalidOperationException("YouTube Information must provide a copy button and menu for " + label);
            }
        }
        finally { YouTubeInspectorDetails.Children.Clear(); }
        var originalLayout = Model.Settings.Get("youtubeVideoLayout", "list");
        var originalTheme = Model.Settings.Get("theme", "system");
        var scale = Root.XamlRoot.RasterizationScale;
        if (AppWindow.Presenter is Microsoft.UI.Windowing.OverlappedPresenter presenter) presenter.Restore();
        ShowEditor(); await Task.Delay(150);
        if (LibraryCommandVisible("youtube-browser") || CanRunDesktopCommand("youtube-browser") ||
            Elements(EditorProjectHeader).OfType<Button>().Any(button => button.Content as string == "My YouTube Videos…"))
            throw new InvalidOperationException("My YouTube Videos leaked into the editor.");
        ShowGallery_Click(this, new RoutedEventArgs());
        if (!LibraryCommandVisible("youtube-browser")) throw new InvalidOperationException("Missing gallery YouTube command.");
        youtubeLayoutFixture = true;
        try {
            foreach (var theme in new[] { "light", "dark" }) foreach (var width in new[] { 1120, 720 }) {
                PreviewTheme(theme); AppWindow.Resize(new Windows.Graphics.SizeInt32((int)(width * scale), (int)(780 * scale))); await Task.Delay(100);
                ContentDialog? active = null; Window? browserWindow = null;
                void Capture(ContentDialog dialog, Window window) { if (dialog.Title as string == "My YouTube Videos") { active = dialog; browserWindow = window; } }
                Views.WindowedDialog.ReadyToShow += Capture;
                var running = RunDesktopCommandAsync("youtube-browser");
                try {
                    for (var attempt = 0; attempt < 100 && active == null; attempt++) await Task.Delay(30);
                    if (active == null || browserWindow == null) throw new InvalidOperationException("YouTube browser did not open.");
                    await Task.Delay(200); var content = (Grid)active.Content; content.UpdateLayout();
                    var windowScale = ((FrameworkElement)browserWindow.Content).XamlRoot.RasterizationScale;
                    var visibleWidth = browserWindow.AppWindow.ClientSize.Width / windowScale;
                    if (visibleWidth < content.Width + 40 || Math.Abs(content.Width - Math.Max(480, Math.Min(1120, AppWindow.Size.Width / Root.XamlRoot.RasterizationScale - 96))) > 2)
                        throw new InvalidOperationException("YouTube browser did not retain its wide content surface: " + visibleWidth + "/" + content.Width);
                    var accountSelector = Elements(content).OfType<ComboBox>().Single(box => Microsoft.UI.Xaml.Automation.AutomationProperties.GetName(box) == "Google account and channel");
                    var refreshButton = Elements(content).OfType<Button>().Single(button => button.Content as string == "Refresh");
                    if (content.Width >= 800 && Math.Abs(accountSelector.TransformToVisual(content).TransformPoint(new Windows.Foundation.Point()).Y - refreshButton.TransformToVisual(content).TransformPoint(new Windows.Foundation.Point()).Y) > 2)
                        throw new InvalidOperationException("Wide account controls still wrap onto another row.");
                    var upperControls = Elements(content).OfType<ScrollViewer>().Single(view => view.Content is StackPanel stack && stack.Children.OfType<MenuBar>().Any());
                    if (content.Width >= 800 && content.Height >= 500 && upperControls.ScrollableHeight > 1)
                        throw new InvalidOperationException("Wide browser still clips its upper controls.");
                    var list = Elements(content).OfType<ListView>().Single();
                    if (list.Items.Count != 30 || list.ActualHeight < 120 || list.ActualWidth > content.ActualWidth + 1)
                        throw new InvalidOperationException("Video list is missing or clipped: " + list.ActualWidth + "x" + list.ActualHeight);
                    var layoutChoice = Elements(content).OfType<Microsoft.UI.Xaml.Controls.Primitives.ToggleButton>().Single(button => button.Content as string == "Table View");
                    var layoutBounds = layoutChoice.TransformToVisual(content).TransformPoint(new Windows.Foundation.Point());
                    if (layoutBounds.Y < 0 || layoutBounds.Y + layoutChoice.ActualHeight > content.ActualHeight) throw new InvalidOperationException("Video layout choice is clipped.");
                    if (Elements(content).OfType<Expander>().Any()) throw new InvalidOperationException("Accounts are still inline.");
                    var done = Elements(content).OfType<Button>().Single(button => button.Content as string == "Done");
                    var open = Elements(content).OfType<Button>().Single(button => button.Content as string == "Open in Editor");
                    if (done.TransformToVisual(content).TransformPoint(new Windows.Foundation.Point()).Y > list.TransformToVisual(content).TransformPoint(new Windows.Foundation.Point()).Y || open.ActualHeight < 20)
                        throw new InvalidOperationException("Opening buttons are not beside/below the browser controls.");
                    list.SelectedIndex = 0; await Task.Delay(80);
                    if (!open.IsEnabled) throw new InvalidOperationException("Selection did not enable opening.");
                    youtubeBrowserRun!("youtube-table"); await Task.Delay(100);
                    if (list.SelectedIndex != 0 || Microsoft.UI.Xaml.Automation.AutomationProperties.GetName(list) != "YouTube videos table") throw new InvalidOperationException("Table lost selection.");
                    var rowButton = Elements(list).OfType<DropDownButton>().First(button => button.Name == Views.VideoBrowserSelection.ActionsButtonName);
                    if (rowButton.Flyout is not MenuFlyout rowMenu || !Microsoft.UI.Xaml.Automation.AutomationProperties.GetName(rowButton).StartsWith("Actions for "))
                        throw new InvalidOperationException("Table row Actions did not expose an accessible menu.");
                    var tableScroll = Elements(content).OfType<ScrollViewer>().Single(view => view.Content is Grid host && host.Children.Contains(list));
                    if (Grid.GetColumn(rowButton) != 10)
                        throw new InvalidOperationException("Actions must be the final YouTube table column.");
                    tableScroll.ChangeView(tableScroll.ScrollableWidth, null, null, true);
                    await Task.Delay(100);
                    rowMenu.ShowAt(rowButton);
                    try
                    {
                        var items = rowMenu.Items.OfType<MenuFlyoutItem>().ToArray();
                        if (!items.Any(item => item.Text == "Open in Editor" && item.IsEnabled) ||
                            !items.Any(item => item.Text == "Open in YouTube Studio" && item.IsEnabled) ||
                            items.Any(item => item.Text == "Select Video"))
                            throw new InvalidOperationException("Video row actions must be available with Select mode off.");
                    }
                    finally { rowMenu.Hide(); }
                    await CaptureSmokeImage((FrameworkElement)browserWindow.Content, Path.Combine(Path.GetDirectoryName(report)!, $"youtube-table-{theme}-{width}.png"));
                    youtubeBrowserRun!("youtube-list"); await Task.Delay(100);
                    list.ScrollIntoView(list.Items.Last()); await Task.Delay(80);
                    var scroll = Elements(list).OfType<ScrollViewer>().First();
                    if (scroll.ScrollableHeight <= 0) throw new InvalidOperationException("Video rows do not scroll independently.");
                    var search = Elements(content).OfType<TextBox>().Single(field => Microsoft.UI.Xaml.Automation.AutomationProperties.GetName(field) == "Search loaded video titles"); search.Text = "no matching video"; await Task.Delay(80);
                    if (list.Items.Count != 0 || open.IsEnabled) throw new InvalidOperationException("Filter left a stale selection.");
                    search.Text = ""; await Task.Delay(80);
                    await CaptureSmokeImage((FrameworkElement)browserWindow.Content, Path.Combine(Path.GetDirectoryName(report)!, $"youtube-list-{theme}-{width}.png"));
                    if (theme == "light" && width == 1120)
                    {
                        list.SelectedIndex = 0;
                        await DoubleClickVideoRowForSmokeTestAsync(browserWindow, list, 0);
                        await running.WaitAsync(TimeSpan.FromSeconds(15));
                    }
                    else { Click(done); await running.WaitAsync(TimeSpan.FromSeconds(15)); }
                } finally { active?.Hide(); Views.WindowedDialog.ReadyToShow -= Capture; await running; }
                var accounts = new Views.YouTubeAccountsPanel(this, showsDone: true);
                accounts.PreviewAccountsForSmokeTest(new[] { new YouTubeAccount("layout-a", "", "Sample Google account", "sample@example.com", "", "", "Sample channel", "", ""), new YouTubeAccount("layout-b", "", "Second account", "second@example.com", "", "", "Second channel", "", "") });
                var accountDialog = new ContentDialog { Content = new ScrollViewer { Content = accounts, MaxHeight = 500 } };
                Window? accountWindow = null;
                void CaptureAccounts(ContentDialog dialog, Window window) { if (dialog == accountDialog) accountWindow = window; }
                Views.WindowedDialog.ReadyToShow += CaptureAccounts;
                var accountTask = Views.WindowedDialog.ShowAsync(accountDialog, this, 1100, 500);
                try {
                    for (var attempt = 0; attempt < 100 && accountWindow == null; attempt++) await Task.Delay(30);
                    await Task.Delay(150);
                    if (Elements(accounts).OfType<ComboBox>().Any() || Elements(accounts).OfType<HyperlinkButton>().Count() != 2 || Elements(accounts).OfType<Button>().Count(button => button.Content as string == "Disconnect YouTube Account…") != 2)
                        throw new InvalidOperationException("Account rows/links do not match Mac.");
                    foreach (var button in Elements(accounts).OfType<Button>().Where(button => button.Visibility == Visibility.Visible)) {
                        var point = button.TransformToVisual(accounts).TransformPoint(new Windows.Foundation.Point());
                        if (point.X < -1 || point.X + button.ActualWidth > accounts.ActualWidth + 1) throw new InvalidOperationException("Account action clipped at " + width);
                    }
                    await CaptureSmokeImage((FrameworkElement)accountWindow!.Content, Path.Combine(Path.GetDirectoryName(report)!, $"youtube-accounts-{theme}-{width}.png"));
                } finally { accountDialog.Hide(); Views.WindowedDialog.ReadyToShow -= CaptureAccounts; await accountTask; }
            }
            PreviewTheme("light");
            var lightPicker = new Views.NewProjectDialog(this, ElementTheme.Light, Model.Settings, openYouTube: true);
            var lightProject = ShowDialogAsync(lightPicker, Views.NewProjectDialog.WindowWidth, Views.NewProjectDialog.WindowHeight);
            try
            {
                for (var retry = 0; retry < 100 && !lightPicker.IsLoaded; retry++) await Task.Delay(30);
                if (!lightPicker.IsLoaded) throw new InvalidOperationException("Light New Project did not open.");
                await lightPicker.VerifyYouTubeLinkForSmokeTestAsync(card => CaptureSmokeImage(card,
                    Path.Combine(Path.GetDirectoryName(report)!, "youtube-fetch-error-light.png")));
            }
            finally { lightPicker.Hide(); await lightProject; }

            PreviewTheme("dark");
            AppWindow.Resize(new Windows.Graphics.SizeInt32((int)(1120 * scale), (int)(780 * scale)));
            (YouTubeVideo Video, bool OpenInEditor)? pickerResult = null;
            var picker = new Views.NewProjectDialog(this, Root.ActualTheme, Model.Settings, openYouTube: true)
            {
                BrowseYouTubeVideo = async owner => pickerResult = await ShowYouTubeBrowserAsync(owner)
            };
            var newProject = ShowDialogAsync(picker, Views.NewProjectDialog.WindowWidth, Views.NewProjectDialog.WindowHeight);
            try
            {
                for (var retry = 0; retry < 100 && !picker.IsLoaded; retry++) await Task.Delay(30);
                if (!picker.IsLoaded) throw new InvalidOperationException("New Project did not open.");
                picker.SetYouTubeCaptionsForSmokeTest();
                await picker.VerifyYouTubeLinkForSmokeTestAsync(card => CaptureSmokeImage(card,
                    Path.Combine(Path.GetDirectoryName(report)!, "youtube-fetch-error-dark.png")));
                foreach (var action in new[] { "Cancel", "Row action List View", "Row action Table View", "Choose Video", "Open in Editor" })
                {
                    picker.SelectYouTubeForSmokeTest("");
                    pickerResult = null;
                    ContentDialog? browser = null;
                    void CapturePicker(ContentDialog dialog, Window _)
                    {
                        if (dialog.Title as string == "My YouTube Videos") browser = dialog;
                    }
                    Views.WindowedDialog.ReadyToShow += CapturePicker;
                    var browsing = picker.BrowseYouTubeForSmokeTestAsync();
                    try
                    {
                        for (var retry = 0; retry < 100 && browser == null && !browsing.IsCompleted; retry++) await Task.Delay(30);
                        if (browser == null || newProject.IsCompleted) throw new InvalidOperationException("YouTube picker did not keep New Project open.");
                        var nested = (Grid)browser.Content;
                        var videos = Elements(nested).OfType<ListView>().Single();
                        for (var retry = 0; retry < 100 && videos.Items.Count != 30; retry++) await Task.Delay(30);
                        if (videos.Items.Count != 30 || videos.Tag is not Views.VideoBrowserSelection selection)
                            throw new InvalidOperationException("YouTube picker did not load its videos.");
                        if (action != "Cancel") videos.SelectedIndex = 0;
                        if (action == "Open in Editor")
                            youtubeBrowserRun?.Invoke("youtube-open-direct");
                        else if (action.StartsWith("Row action ", StringComparison.Ordinal))
                        {
                            youtubeBrowserRun?.Invoke(action == "Row action Table View" ? "youtube-table" : "youtube-list");
                            await selection.ActivateRowAsync(videos.Items[0]);
                        }
                        else
                        {
                            if (action == "Choose Video")
                            {
                                selection.SetMode(true);
                                await selection.ActivateRowAsync(videos.Items[0]);
                                if (browsing.IsCompleted || pickerResult != null)
                                    throw new InvalidOperationException("A row action confirmed a video in Select mode.");
                            }
                            var button = Elements(nested).OfType<Button>().Single(candidate => candidate.Content as string == action);
                            if (!button.IsEnabled) throw new InvalidOperationException("YouTube picker action was disabled.");
                            Click(button);
                        }
                        try { await browsing.WaitAsync(TimeSpan.FromSeconds(10)); }
                        catch (TimeoutException ex) { throw new InvalidOperationException($"The YouTube picker did not close after {action}.", ex); }
                        if (action == "Open in Editor") await newProject.WaitAsync(TimeSpan.FromSeconds(10));
                        if (newProject.IsCompleted != (action == "Open in Editor") ||
                            picker.OpenSelectedYouTubeInEditor != (action == "Open in Editor") || !picker.ChooseYouTubeCaptions ||
                            (action != "Cancel" ? picker.SelectedYouTubeVideo?.Id != "fixture-000"
                                                : picker.SelectedYouTubeVideo != null || picker.IsPrimaryButtonEnabled))
                            throw new InvalidOperationException($"YouTube picker failed after {action}. " +
                                $"Browser returned: {pickerResult?.Video.Id ?? "none"}; " +
                                $"Video: {picker.SelectedYouTubeVideo?.Id ?? "none"}; captions: {picker.ChooseYouTubeCaptions}; " +
                                $"direct open: {picker.OpenSelectedYouTubeInEditor}; project dialog closed: {newProject.IsCompleted}.");
                    }
                    finally { if (!browsing.IsCompleted) browser?.Hide(); Views.WindowedDialog.ReadyToShow -= CapturePicker; await browsing; }
                }
            }
            finally { picker.Hide(); await newProject; }
        } finally { youtubeLayoutFixture = false; Model.Settings.Set("youtubeVideoLayout", originalLayout); Model.Settings.Save(); PreviewTheme(originalTheme); }
    }

    private async Task VerifyInspectorAIProcessingForSmokeTestAsync()
    {
        System.Collections.Generic.IEnumerable<StackPanel> Rows() => InspectorAIProcessingRows.Children.OfType<StackPanel>();
        bool HasValue(string value) => Rows().SelectMany(row => row.Children.OfType<Grid>())
            .SelectMany(row => row.Children.OfType<TextBlock>()).Any(text => text.Text == value);
        ShowEditor();
        var originalSection = Section.SelectedIndex;
        for (var section = 0; section < 5; section++)
        {
            Section.SelectedIndex = section;
            RefreshLanguagePanel();
            if (!HasValue("Not recorded"))
                throw new InvalidOperationException("The inspector must show legacy provider status on each editor tab.");
        }
        Section.SelectedIndex = 0;
        RefreshLanguagePanel();
        if (!Rows().SelectMany(row => row.Children.OfType<DropDownButton>()).Any(button => button.Content as string == "Assign Transcript Provider"))
            throw new InvalidOperationException("The legacy transcript must offer provider assignment.");
        await Model.RunAsync("assign-result-ai-provider", new[] {
            "--result-id", Model.Project!.ActiveResultID("transcript"), "--ai-task", "text", "--ai-provider", "vertex-ai"
        }, throwOnProcessingError: true);
        RefreshLanguagePanel();
        if (!HasValue("Vertex AI (assigned)"))
            throw new InvalidOperationException("The assigned provider did not appear after saving.");
        Section.SelectedIndex = 1;
        RefreshLanguagePanel();
        if (HasValue("Vertex AI (assigned)"))
            throw new InvalidOperationException("Transcript attribution leaked into the Summary tab.");
        Section.SelectedIndex = originalSection;
        RefreshLanguagePanel();
    }

    private async Task VerifyGalleryAndSettingsLayoutForSmokeTestAsync(string report)
    {
        ShowGallery_Click(this, new RoutedEventArgs());
        // Run the File actions through their normal command dispatcher and inspect the opened dialog.
        var originalConnection = Model.Settings.Data["vertexConnected"]?.DeepClone();
        try {
            foreach (var connected in new[] { false, true }) {
                Model.Settings.Data["vertexConnected"] = connected;
                UpdateDesktopMenuStates();
                var file = MainMenu.Items.Single(menu => menu.Title == "File");
                var expected = new[] { "new-project", "new-youtube-project", "new-cloud-project" };
                var entries = file.Items.OfType<MenuFlyoutItem>().Where(row => row.Visibility == Visibility.Visible)
                    .Take(expected.Length).ToArray();
                if (!entries.Select(row => row.Tag as string).SequenceEqual(expected) || entries[2].IsEnabled != connected)
                    throw new InvalidOperationException("File must show all project commands and disable New Cloud Project before sign-in.");
                var browserGroup = file.Items.Where(row => row.Visibility == Visibility.Visible)
                    .Skip(expected.Length).Take(5).ToArray();
                if (browserGroup.Length != 5 || browserGroup[0] is not MenuFlyoutSeparator ||
                    browserGroup[1] is not MenuFlyoutItem { Tag: "open-cloud-videos" } cloudEntry ||
                    !cloudEntry.IsEnabled || cloudEntry.KeyboardAcceleratorTextOverride != "Ctrl+Alt+G" ||
                    browserGroup[2] is not MenuFlyoutItem { Tag: "youtube-browser" } ||
                    browserGroup[3] is not MenuFlyoutSeparator ||
                    browserGroup[4] is not MenuFlyoutItem { Tag: "open-project" })
                    throw new InvalidOperationException("File must give Cloud and YouTube video browsers their own group between menu dividers.");
                if (!connected)
                {
                    var ready = new TaskCompletionSource<Views.CloudVideosDialog>(TaskCreationOptions.RunContinuationsAsynchronously);
                    void CaptureCloud(ContentDialog dialog, Window window)
                    {
                        if (dialog is Views.CloudVideosDialog cloudDialog) ready.TrySetResult(cloudDialog);
                    }
                    Views.WindowedDialog.ReadyToShow += CaptureCloud;
                    var opening = RunDesktopCommandAsync("open-cloud-videos");
                    try
                    {
                        if (await Task.WhenAny(ready.Task, opening, Task.Delay(5000)) != ready.Task)
                            throw new InvalidOperationException("The File command did not open the signed-out Cloud browser.");
                        ready.Task.Result.VerifySignedOutForSmokeTest();
                    }
                    finally
                    {
                        Views.WindowedDialog.ReadyToShow -= CaptureCloud;
                        if (ready.Task.IsCompletedSuccessfully) ready.Task.Result.Hide();
                        await opening.WaitAsync(TimeSpan.FromSeconds(10));
                    }
                }
                foreach (var id in expected) {
                    // Closing each companion window can reload the real connection state.
                    Model.Settings.Data["vertexConnected"] = connected;
                    if (id == "new-cloud-project" && !connected) {
                        if (CanRunDesktopCommand(id))
                            throw new InvalidOperationException("A signed-out File command must be disabled: " + id);
                        await RunDesktopCommandAsync(id).WaitAsync(TimeSpan.FromSeconds(3));
                        continue;
                    }
                    var ready = new TaskCompletionSource<Views.NewProjectDialog>(TaskCreationOptions.RunContinuationsAsynchronously);
                    void Capture(ContentDialog dialog, Window _) {
                        if (dialog is Views.NewProjectDialog newProject) ready.TrySetResult(newProject);
                    }
                    Views.WindowedDialog.ReadyToShow += Capture;
                    if (!CanRunDesktopCommand(id) || Model.Busy)
                        throw new InvalidOperationException("File project command is unavailable in an idle gallery: " + id);
                    var command = RunDesktopCommandAsync(id);
                    Views.NewProjectDialog? projectDialog = null;
                    try {
                        var first = await Task.WhenAny(ready.Task, command).WaitAsync(TimeSpan.FromSeconds(20));
                        if (first != ready.Task)
                            throw new InvalidOperationException("File project command completed without opening its dialog: " + id + ", Cloud connected: " + Model.Settings.GetBool("vertexConnected"));
                        projectDialog = await ready.Task;
                        projectDialog.VerifyInitialSourceForSmokeTest(id);
                        if (id == "new-project") projectDialog.VerifyLocalAccessibilityForSmokeTest(selected: false);
                    } finally {
                        Views.WindowedDialog.ReadyToShow -= Capture;
                        projectDialog?.Hide();
                        await command.WaitAsync(TimeSpan.FromSeconds(15));
                    }
                }
            }
        } finally {
            Model.Settings.Data["vertexConnected"] = originalConnection;
            UpdateDesktopMenuStates();
        }
        var browserSettings = new SettingsStore();
        browserSettings.Data["vertexConnected"] = true;
        browserSettings.Data["vertexProject"] = "fixture-destination";
        browserSettings.Data["vertexBucket"] = "fixture-output";
        var browser = new Views.CloudVideosDialog(this, Root.ActualTheme, browserSettings);
        browser.UseFixtureForSmokeTest();
        var readyCloudBrowser = new TaskCompletionSource<Window>(TaskCreationOptions.RunContinuationsAsynchronously);
        void CaptureCloudBrowser(ContentDialog dialog, Window window)
        {
            if (dialog == browser) readyCloudBrowser.TrySetResult(window);
        }
        Views.WindowedDialog.ReadyToShow += CaptureCloudBrowser;
        var browsing = ShowDialogAsync(browser, 820, 820);
        try { await browser.VerifyBrowserForSmokeTestAsync(await readyCloudBrowser.Task.WaitAsync(TimeSpan.FromSeconds(5))); }
        finally { Views.WindowedDialog.ReadyToShow -= CaptureCloudBrowser; browser.Hide(); await browsing; }
        await VerifyAccessibleMediaForSmokeTestAsync();
        await VerifyMenuAccessForSmokeTestAsync(report);
        VerifyCloudImportEditorProgressForSmokeTest();
        await VerifyInspectorDisclosureForSmokeTestAsync();
        await VerifyEditorSectionToolbarForSmokeTestAsync(report);
        await VerifyAboutMenuForSmokeTestAsync();
        ShowGallery_Click(this, new RoutedEventArgs());
        await VerifyAboutMenuForSmokeTestAsync();
        await VerifyHelpWindowForSmokeTestAsync(report);
        await VerifyProjectSortingForSmokeTestAsync();
        await VerifyProjectAIProviderFilteringForSmokeTestAsync();
        var originalTheme = Model.Settings.Get("theme", "system");
        var scale = Root.XamlRoot.RasterizationScale;
        if (AppWindow.Presenter is Microsoft.UI.Windowing.OverlappedPresenter presenter) presenter.Restore();
        foreach (var theme in new[] { "light", "dark" })
        {
            PreviewTheme(theme);
            foreach (var width in new[] { 1120, 880, 640 })
            {
                AppWindow.Resize(new Windows.Graphics.SizeInt32((int)(width * scale), (int)(640 * scale)));
                await Task.Delay(150);
                Root.UpdateLayout();
                VerifyGalleryToolbarBoundsForSmokeTest();
                await CaptureSmokeImage(Root, Path.Combine(Path.GetDirectoryName(report)!, $"gallery-toolbar-{theme}-{width}.png"));
            }
        }
        var cloudPickerSettings = new SettingsStore();
        cloudPickerSettings.Data["vertexBucket"] = "qa-destination";
        cloudPickerSettings.Data["vertexProject"] = "qa-project";
        foreach (var connected in new[] { false, true }) {
            cloudPickerSettings.Data["vertexConnected"] = connected;
            var picker = new Views.NewProjectDialog(this, Root.ActualTheme, cloudPickerSettings);
            var showing = ShowDialogAsync(picker, Views.NewProjectDialog.WindowWidth, Views.NewProjectDialog.WindowHeight);
            try {
                for (var i = 0; i < 100 && !picker.IsLoaded && !showing.IsCompleted; i++) await Task.Delay(40);
                if (showing.IsCompleted) throw new InvalidOperationException("New Project closed before inspecting cloud options");
                await picker.VerifyCloudBrowserForSmokeTestAsync(connected, Models.StudioProject.Load(Environment.GetEnvironmentVariable("VIDISTUDIO_UI_SMOKE_PROJECT")!));
                if (connected) await showing.WaitAsync(TimeSpan.FromSeconds(10));
                else await CaptureSmokeImage((FrameworkElement)picker.Content, Path.Combine(Path.GetDirectoryName(report)!, "cloud-new-project-signed-out.png"));
            } finally { picker.Hide(); await showing; }
        }
        var display = RunDesktopCommandAsync("youtube-accounts");
        for (var attempt = 0; attempt < 100 && openSettings == null; attempt++) await Task.Delay(20);
        var settings = openSettings ?? throw new InvalidOperationException("YouTube Accounts menu did not open the shared Settings window.");
        try
        {
            await Task.Delay(150);
            if (settings.SelectedSectionForSmokeTest != 8) throw new InvalidOperationException("YouTube Accounts opened the wrong Settings page.");
            var accountPanel = new Views.YouTubeAccountsPanel(settings);
            if (accountPanel.Children.OfType<CheckBox>().Any()) throw new InvalidOperationException("YouTube Accounts must not require a disconnect checkbox.");
            var disconnectConfirmation = Views.YouTubeAccountsPanel.CreateDisconnectConfirmation();
            if (disconnectConfirmation.PrimaryButtonText != "Disconnect" || disconnectConfirmation.CloseButtonText != "Cancel" || disconnectConfirmation.DefaultButton != ContentDialogButton.Close)
                throw new InvalidOperationException("Disconnect must use the same cancel-default confirmation as Mac.");
            settings.SelectSection(5);
            await RunDesktopCommandAsync("youtube-accounts");
            if (openSettings != settings || settings.SelectedSectionForSmokeTest != 8)
                throw new InvalidOperationException("YouTube Accounts did not reuse Settings or select its page.");
            settings.VerifySidebarNavigationForSmokeTest();
            await settings.VerifyInformationForSmokeTestAsync();
            await settings.VerifyLayoutForSmokeTestAsync();
            await settings.VerifySarvamSettingsForSmokeTestAsync(async () => {
                foreach (var theme in new[] { "dark", "light" }) {
                    PreviewTheme(theme); await Task.Delay(150); settings.VerifyNarrationThemeForSmokeTest();
                    settings.BringNarrationIntoViewForSmokeTest(false); await Task.Delay(150);
                    await CaptureSmokeImage((FrameworkElement)settings.Content, Path.Combine(Path.GetDirectoryName(report)!, "settings-layout-narration-" + theme + ".png"));
                    settings.BringNarrationIntoViewForSmokeTest(true); await Task.Delay(150);
                    await CaptureSmokeImage((FrameworkElement)settings.Content, Path.Combine(Path.GetDirectoryName(report)!, "settings-layout-sarvam-" + theme + ".png"));
                }
                PreviewTheme(originalTheme);
            }, tag => CaptureSmokeImage((FrameworkElement)settings.Content, Path.Combine(Path.GetDirectoryName(report)!, "sarvam-controls-"+tag+".png")));
            await settings.VerifyElevenLabsForSmokeTestAsync(theme => CaptureSmokeImage((FrameworkElement)settings.Content, Path.Combine(Path.GetDirectoryName(report)!, "elevenlabs-" + theme + ".png")));
            await settings.VerifyGeminiKeyLayoutForSmokeTestAsync(async () => {
                foreach (var theme in new[] { "dark", "light" }) {
                    PreviewTheme(theme); await Task.Delay(150);
                    settings.BringGeminiIntoViewForSmokeTest(false); await Task.Delay(150); settings.VerifyGeminiGroupsForSmokeTest();
                    await CaptureSmokeImage((FrameworkElement)settings.Content, Path.Combine(Path.GetDirectoryName(report)!, "settings-layout-gemini-" + theme + ".png"));
                    settings.BringGeminiIntoViewForSmokeTest(true); await Task.Delay(150);
                    await CaptureSmokeImage((FrameworkElement)settings.Content, Path.Combine(Path.GetDirectoryName(report)!, "settings-layout-gemini-models-" + theme + ".png"));
                }
                PreviewTheme(originalTheme);
            });
            settings.VerifyGeminiModelFiltering();
            await settings.VerifyVertexConnectionForSmokeTestAsync(() => CaptureSmokeImage((FrameworkElement)settings.Content, Path.Combine(Path.GetDirectoryName(report)!, "settings-vertex-signin.png")));
            ShowEditor();
            try {
                await settings.VerifyCompactPagesForSmokeTestAsync((section, theme) =>
                    CaptureSmokeImage((FrameworkElement)settings.Content, Path.Combine(Path.GetDirectoryName(report)!, $"settings-compact-{section}-{theme}.png")));
            }
            finally { ShowGallery_Click(this, new RoutedEventArgs()); PreviewTheme(originalTheme); }
            foreach (var (id, title) in new[] { (8, "youtube"), (1, "projects"), (5, "storage") })
            {
                settings.SelectSection(id);
                await Task.Delay(100);
                await CaptureSmokeImage((FrameworkElement)settings.Content, Path.Combine(Path.GetDirectoryName(report)!, $"settings-layout-{title}.png"));
            }
            await settings.VerifyWindowThemeAsync();
            await settings.VerifyExportSoundForSmokeTestAsync();
            await settings.VerifySummaryHighlightAppearanceForSmokeTestAsync();
            await settings.VerifyProviderSwitchingAsync();
        }
        finally { settings.Close(); await display; PreviewTheme(originalTheme); }
    }

    private async Task VerifyExistingAssetEngineWorkflowForSmokeTestAsync(string originalFolder)
    {
        var testRoot = Path.Combine(Path.GetDirectoryName(originalFolder)!, "asset-workflow");
        Directory.CreateDirectory(testRoot);
        var source = Model.Project!.Resolve(Model.Project.Source);
        async Task Run(string command, string[] args, bool needsProject = true)
        {
            if ((await Model.RunAsync(command, args, needsProject)).Count == 0)
                throw new InvalidOperationException($"{command} failed in the native app: {Model.Status}");
        }
        await Run("import-video", new[] { "--video", source, "--library", Path.Combine(testRoot, Guid.NewGuid().ToString("N")) }, false);
        var importedFolder = Model.Project!.Folder;
        if (!File.Exists(Path.Combine(importedFolder, "project.json")) || importedFolder == originalFolder)
            throw new InvalidOperationException("Project creation did not create a new portable folder.");
        var transcript = Path.Combine(testRoot, "transcript.srt");
        var descriptions = Path.Combine(testRoot, "descriptions.vtt");
        var summary = Path.Combine(testRoot, "summary.md");
        var audio = Path.Combine(testRoot, "narration.wav");
        await File.WriteAllTextAsync(transcript, "1\n00:00:00,000 --> 00:00:01,000\nImported dialogue.\n");
        await File.WriteAllTextAsync(descriptions, "WEBVTT\n\n00:02.000 --> 00:03.000\nImported description.\n");
        await File.WriteAllTextAsync(summary, "# Imported summary\n\nA **reviewed** scene.");
        var timingRows = Models.ImportTimingInspector.ReadRows(descriptions);
        var timingDialog = new Views.TextTimingDialog("descriptions.vtt", 8, timingRows, "narration", Model, Model.Project);
        if (!timingDialog.IsPrimaryButtonEnabled || timingRows.Count != 1 || timingRows[0].Start != 2)
            throw new InvalidOperationException("VTT review did not preserve the subtitle times.");
        var start = new System.Diagnostics.ProcessStartInfo(Model.Settings.Get("ffmpegPath", "ffmpeg.exe")) { UseShellExecute = false, CreateNoWindow = true };
        foreach (var arg in new[] { "-y", "-v", "error", "-f", "lavfi", "-i", "sine=frequency=660:sample_rate=48000", "-t", "8", audio }) start.ArgumentList.Add(arg);
        using (var process = System.Diagnostics.Process.Start(start)!) { await process.WaitForExitAsync(); if (process.ExitCode != 0) throw new InvalidOperationException("Import test audio could not be prepared."); }
        await Run("import-transcript", new[] { "--input", transcript });
        await Run("import-ad-transcript", new[] { "--input", descriptions, "--ad-transcript-mode", "narration" });
        await Run("import-summary", new[] { "--input", summary });
        await Run("import-ad-audio", new[] { "--input", audio, "--audio-mode", "narration" });
        if (Model.Project!.Transcript.Count != 1 || Model.Project.Cues.Count != 1 || Model.Project.SummaryOrigin != "imported" || Model.Project.Exports.Count != 0)
            throw new InvalidOperationException("Import did not retain all selected assets without automatically combining audio.");
        await Run("combine-imported-audio", new[] { "--audio-presentation", "selectable", "--captions", "none", "--media-format", "mov" });
        if (Model.Project!.Exports.Count != 1) throw new InvalidOperationException("Optional Combine did not create an export.");
        var reopened = new ViewModels.StudioViewModel();
        reopened.Reload();
        if (!reopened.Projects.Any(project => project.Folder == importedFolder)) throw new InvalidOperationException("A project outside the default library was forgotten on reopen.");
        Model.Open(originalFolder);
    }
    private async Task VerifyRegenerationConfirmationsForSmokeTestAsync()
    {
        var project = Model.Project ?? throw new InvalidOperationException("A saved project fixture is required.");
        var manifest = Path.Combine(project.Folder, "project.json");
        var before = await File.ReadAllBytesAsync(manifest);
        foreach (var (command, name) in new[] {
            ("transcribe", "Transcript"), ("generate-summary", "Summary"), ("generate-ad-cues", "Audio Descriptions")
        })
        {
            var title = $"Regenerate {name}?";
            var ready = new TaskCompletionSource<ContentDialog>(TaskCreationOptions.RunContinuationsAsynchronously);
            void Capture(ContentDialog dialog, Window window)
            {
                if (dialog.Title as string == title && window.Title == title) ready.TrySetResult(dialog);
            }
            Views.WindowedDialog.ReadyToShow += Capture;
            try
            {
                var regeneration = Model.RunAsync(command);
                var dialog = await ready.Task.WaitAsync(TimeSpan.FromSeconds(10));
                await Task.Delay(1000);
                if (dialog.DefaultButton != ContentDialogButton.Close || Model.Busy ||
                    dialog.PrimaryButtonText != $"Regenerate {name}" ||
                    dialog.Content is not TextBlock { Text: var message } ||
                    !message.Contains("Your saved versions will remain available."))
                    throw new InvalidOperationException($"The {name} warning is incomplete or processing started early.");
                var cancel = FindButton(dialog, "Cancel") ?? throw new InvalidOperationException("Missing Cancel.");
                var peer = new Microsoft.UI.Xaml.Automation.Peers.ButtonAutomationPeer(cancel);
                ((Microsoft.UI.Xaml.Automation.Provider.IInvokeProvider)peer.GetPattern(
                    Microsoft.UI.Xaml.Automation.Peers.PatternInterface.Invoke)).Invoke();
                if ((await regeneration.WaitAsync(TimeSpan.FromSeconds(10))).Count != 0 ||
                    !before.SequenceEqual(await File.ReadAllBytesAsync(manifest)))
                    throw new InvalidOperationException($"Cancelling {name} regeneration changed the project.");
            }
            finally
            {
                Views.WindowedDialog.ReadyToShow -= Capture;
                if (ready.Task.IsCompletedSuccessfully) ready.Task.Result.Hide();
            }
        }
    }

    private async Task VerifyCompanionDialogsForSmokeTestAsync(string report)
    {
        // Focused dialog validation does not depend on speech engines or editor selection tests.
        // It is opt-in; the full smoke suite still runs every existing assertion by default.
        var originalTheme = Model.Settings.Get("theme", "system");
        try
        {
            foreach (var initialTheme in new[] { "light", "dark" })
            {
                PreviewTheme(initialTheme);
                var (deleteDialog, boxes) = CreateDeleteGeneratedDialog();
                var cloudSettings = new SettingsStore();
                cloudSettings.Data["vertexConnected"] = true;
                var dialogs = new (ContentDialog Dialog, int Width, int Height, string Image)[]
                {
                    (new Views.NewProjectDialog(this, Root.ActualTheme, cloudSettings), Views.NewProjectDialog.WindowWidth,
                        Views.NewProjectDialog.WindowHeight, "new-project-window"),
                    (new Views.ExportDialog(Model), 1040, 760, "export-window"),
                    (deleteDialog, 620, 820, "delete-generated-window"),
                    (new ContentDialog { Title = "Action could not finish", CloseButtonText = "Close",
                        Content = DialogMessage("Check your settings and the diagnostic log, then retry. Your saved project remains available.") }, 680, 460, "error-dialog-window")
                };
                foreach (var (dialog, width, height, image) in dialogs)
                {
                    var acceleratorCount = dialog.KeyboardAccelerators.Count;
                    var display = ShowDialogAsync(dialog, width, height);
                    await Task.Delay(350);
                    await VerifyDialogThemesForSmokeTestAsync(dialog, report, image);
                    VerifyCompactDialogLayout(dialog);
                    if (dialog is Views.NewProjectDialog newProject)
                    {
                        var window = dialogPresentationVerifier!.WindowFor(dialog).AppWindow;
                        var size = window.Size;
                        var position = window.Position;
                        var root = (UIElement)dialog.XamlRoot.Content;
                        var title = DialogElements(dialog).OfType<TextBlock>().First(element => element.Text == "New Project");
                        var cancel = FindButton(dialog, "Cancel") ?? throw new InvalidOperationException("Missing Cancel button.");
                        var titleOrigin = title.TransformToVisual(root).TransformPoint(new Windows.Foundation.Point());
                        var cancelOrigin = cancel.TransformToVisual(root).TransformPoint(new Windows.Foundation.Point());
                        var labels = newProject.SourceLabelsForSmokeTest;
                        var labelOrigins = labels.Select(label => label.TransformToVisual(root).TransformPoint(new Windows.Foundation.Point())).ToArray();
                        var location = FindButton(dialog, "Choose Project Location…") ?? throw new InvalidOperationException("Missing project location button.");
                        var locationBottom = location.TransformToVisual(root).TransformPoint(new Windows.Foundation.Point(0, location.ActualHeight)).Y;
                        if (!newProject.LocalFormFitsForSmokeTest || cancelOrigin.Y - locationBottom is < 0 or > 64)
                            throw new InvalidOperationException($"Unexpected local form spacing above actions: {cancelOrigin.Y - locationBottom}.");
                        static bool StaysInPlace(Windows.Foundation.Point before, Windows.Foundation.Point after) =>
                            Math.Abs(before.X - after.X) <= 1 && Math.Abs(before.Y - after.Y) <= 1;
                        await newProject.VerifySourceSwitchesForSmokeTestAsync(async source =>
                        {
                            var titleNow = title.TransformToVisual(root).TransformPoint(new Windows.Foundation.Point());
                            var cancelNow = cancel.TransformToVisual(root).TransformPoint(new Windows.Foundation.Point());
                            if (!window.Size.Equals(size) || !window.Position.Equals(position) ||
                                !StaysInPlace(titleOrigin, titleNow) || !StaysInPlace(cancelOrigin, cancelNow))
                                throw new InvalidOperationException(
                                    $"The {source} project source moved the window, title or actions. " +
                                    $"Size {size.Width}×{size.Height} to {window.Size.Width}×{window.Size.Height}, " +
                                    $"position {position.X},{position.Y} to {window.Position.X},{window.Position.Y}, " +
                                    $"title {titleOrigin} to {titleNow}, Cancel {cancelOrigin} to {cancelNow}.");
                            for (var i = 0; i < labels.Length; i++)
                                if (!StaysInPlace(labelOrigins[i], labels[i].TransformToVisual(root).TransformPoint(new Windows.Foundation.Point())))
                                    throw new InvalidOperationException($"The {source} project source moved source label {i}.");
                            VerifyCompactDialogLayout(dialog);
                            await CaptureSmokeImage((FrameworkElement)root, Path.Combine(Path.GetDirectoryName(report)!, $"new-project-{source}-{initialTheme}.png"));
                        });
                    }
                    if (ReferenceEquals(dialog, deleteDialog))
                    {
                        if (dialog.IsPrimaryButtonEnabled) throw new InvalidOperationException("Delete was enabled without a selection.");
                        boxes[0].IsChecked = true;
                        await Task.Delay(80);
                        if (FindButton(dialog, "Delete selected") is not { IsEnabled: true } ||
                            boxes.Any(box => box.ActualHeight <= 0 || box.TransformToVisual(dialog).TransformPoint(new Windows.Foundation.Point()).Y + box.ActualHeight > dialog.ActualHeight))
                            throw new InvalidOperationException("Delete selection gating or option layout failed.");
                    }
                    dialog.Hide();
                    if (await display != ContentDialogResult.None) throw new InvalidOperationException("Hide did not cancel the dialog.");
                    if (dialog.KeyboardAccelerators.Count != acceleratorCount)
                        throw new InvalidOperationException("The closed dialog retained a host keyboard accelerator.");
                }
            }
            await VerifyCompactDialogFormsForSmokeTestAsync(report);
            await VerifyDialogCommandsForSmokeTestAsync(report);
            await VerifyDialogContentGrowthForSmokeTestAsync(report);
            await VerifyDialogCanceledBeforePresentationForSmokeTestAsync();
        }
        finally { PreviewTheme(originalTheme); }
    }
    private async Task VerifyDialogContentGrowthForSmokeTestAsync(string report)
    {
        var content = new StackPanel { Spacing = 12 };
        content.Children.Add(new TextBox { Header = "Project name", Text = "Dialog layout fixture" });
        var dialog = Dialog("Content changes", content);
        var display = ShowDialogAsync(dialog);
        await Task.Delay(350);
        await VerifyDialogThemesForSmokeTestAsync(dialog, report, "dynamic-dialog-window");
        var window = dialogPresentationVerifier!.WindowFor(dialog);
        var before = window.AppWindow.Size.Height;
        var added = new TextBlock { Text = "A preview loaded without opening a replacement window.", Height = 320, TextWrapping = TextWrapping.Wrap };
        content.Children.Add(added);
        for (var attempt = 0; attempt < 40 && window.AppWindow.Size.Height <= before + 40; attempt++) await Task.Delay(25);
        if (window.AppWindow.Size.Height <= before + 40 || !window.AppWindow.IsVisible ||
            !ReferenceEquals(window, dialogPresentationVerifier.WindowFor(dialog)))
            throw new InvalidOperationException("A genuine content change did not resize the existing dialog window.");
        dialog.Hide();
        await display;
    }

    private async Task VerifyDuplicateProjectDialogForSmokeTestAsync(string report)
    {
        var project = Model.Project!;
        var manifest = Path.Combine(project.Folder, "project.json");
        var original = await File.ReadAllBytesAsync(manifest);
        var library = Model.Settings.Get("libraryPath");
        var folders = Directory.GetDirectories(library);
        var sourceDialog = new TaskCompletionSource<Views.NewProjectDialog>(TaskCreationOptions.RunContinuationsAsynchronously);
        var notice = new TaskCompletionSource<ContentDialog>(TaskCreationOptions.RunContinuationsAsynchronously);
        void Capture(ContentDialog dialog, Window window)
        {
            if (dialog is Views.NewProjectDialog newProject) sourceDialog.TrySetResult(newProject);
            else notice.TrySetResult(dialog);
        }
        void Invoke(ContentDialog dialog, string label)
        {
            var button = FindButton(dialog, label) ?? throw new InvalidOperationException("Missing dialog button: " + label);
            var peer = new Microsoft.UI.Xaml.Automation.Peers.ButtonAutomationPeer(button);
            ((Microsoft.UI.Xaml.Automation.Provider.IInvokeProvider)peer.GetPattern(Microsoft.UI.Xaml.Automation.Peers.PatternInterface.Invoke)).Invoke();
        }

        Views.WindowedDialog.ReadyToShow += Capture;
        var create = ShowNewProjectDialogAsync(false);
        try
        {
            var dialog = await sourceDialog.Task.WaitAsync(TimeSpan.FromSeconds(15));
            dialog.VerifyLocalAccessibilityForSmokeTest(selected: false);
            await dialog.InspectVideoForSmokeTestAsync(project.Resolve(project.Source));
            dialog.VerifyLocalAccessibilityForSmokeTest(selected: true);
            Invoke(dialog, "Create Project");
            var failure = await notice.Task.WaitAsync(TimeSpan.FromSeconds(20));
            if (failure.Title?.ToString() != "Project Already Exists" ||
                !Model.Status.Contains("creates a separate project", StringComparison.Ordinal) ||
                failure.DefaultButton != ContentDialogButton.Close ||
                failure.ContextFlyout is not MenuFlyout options ||
                !options.Items.OfType<MenuFlyoutItem>().Select(item => (item.Text, item.Icon != null))
                    .SequenceEqual(new[] { ("Okay", true), ("Import Anyway", true) }))
                throw new InvalidOperationException("New Project hid the duplicate diagnosis: " + failure.Title + " / " + Model.Status);
            await VerifyDialogThemesForSmokeTestAsync(failure, report, "duplicate-project-dialog");
            VerifyCompactDialogLayout(failure);
            Invoke(failure, "Okay");
            await create.WaitAsync(TimeSpan.FromSeconds(10));
            if (Model.Busy || Model.Project?.Folder != project.Folder ||
                !original.SequenceEqual(await File.ReadAllBytesAsync(manifest)) ||
                !folders.ToHashSet(StringComparer.OrdinalIgnoreCase).SetEquals(Directory.GetDirectories(library)))
                throw new InvalidOperationException("A rejected duplicate changed the project, library or busy state.");
            var contextPrompt = new TaskCompletionSource<ContentDialog>(TaskCreationOptions.RunContinuationsAsynchronously);
            void CaptureContext(ContentDialog dialog, Window window) => contextPrompt.TrySetResult(dialog);
            Views.WindowedDialog.ReadyToShow += CaptureContext;
            try
            {
                var decision = ShowImportDecisionAsync("Import Warning", "A usable file has a timing difference.", true);
                var warning = await contextPrompt.Task.WaitAsync(TimeSpan.FromSeconds(10));
                var menu = (MenuFlyout)warning.ContextFlyout;
                menu.ShowAt(warning);
                var item = menu.Items.OfType<MenuFlyoutItem>().Single(option => option.Text == "Import Anyway");
                var peer = new Microsoft.UI.Xaml.Automation.Peers.MenuFlyoutItemAutomationPeer(item);
                ((Microsoft.UI.Xaml.Automation.Provider.IInvokeProvider)peer.GetPattern(
                    Microsoft.UI.Xaml.Automation.Peers.PatternInterface.Invoke)).Invoke();
                if (await decision.WaitAsync(TimeSpan.FromSeconds(10)) != ContentDialogResult.Primary)
                    throw new InvalidOperationException("The import context command did not select the override.");
            }
            finally
            {
                if (contextPrompt.Task.IsCompletedSuccessfully) contextPrompt.Task.Result.Hide();
                Views.WindowedDialog.ReadyToShow -= CaptureContext;
            }
        }
        finally
        {
            Views.WindowedDialog.ReadyToShow -= Capture;
            if (!create.IsCompleted)
            {
                if (sourceDialog.Task.IsCompletedSuccessfully) sourceDialog.Task.Result.Hide();
                if (notice.Task.IsCompletedSuccessfully) notice.Task.Result.Hide();
            }
        }
    }
    private async Task VerifyDialogCanceledBeforePresentationForSmokeTestAsync()
    {
        var dialog = Dialog("Canceled while preparing", new TextBox { Text = "Never shown" });
        Window? canceledWindow = null;
        void Cancel(ContentDialog candidate, Window window)
        {
            if (!ReferenceEquals(candidate, dialog)) return;
            canceledWindow = window;
            window.DispatcherQueue.TryEnqueue(() => window.Close());
        }
        Views.WindowedDialog.Preparing += Cancel;
        try
        {
            if (await ShowDialogAsync(dialog) != ContentDialogResult.None || canceledWindow is null ||
                dialogPresentationVerifier!.WasReadyToShow(dialog))
                throw new InvalidOperationException("A dialog canceled during preparation was still presented.");
        }
        finally { Views.WindowedDialog.Preparing -= Cancel; }
    }
    private async Task VerifyDialogCommandsForSmokeTestAsync(string report)
    {
        foreach (var voiceDialog in new ContentDialog[] { new Views.PiperVoicesDialog(), new Views.PiperDeleteVoicesDialog() })
        {
            var voiceDisplay = ShowDialogAsync(voiceDialog, 820, 900);
            await Task.Delay(350);
            await VerifyDialogThemesForSmokeTestAsync(voiceDialog, report, voiceDialog.GetType().Name);
            voiceDialog.Hide();
            if (await voiceDisplay != ContentDialogResult.None)
                throw new InvalidOperationException("Closing a voice dialog did not return the cancel result.");
        }
        // InPlace must preserve native command results, including secondary actions.
        foreach (var expected in new[] { ContentDialogResult.Primary, ContentDialogResult.Secondary, ContentDialogResult.None })
        {
            var prompt = Dialog("Confirm project action", new TextBox { Header = "Project name", Text = "Smoke fixture" });
            prompt.SecondaryButtonText = "Review";
            var promptDisplay = ShowDialogAsync(prompt);
            await Task.Delay(350);
            await VerifyDialogThemesForSmokeTestAsync(prompt, report, "confirmation-window");
            var label = expected switch { ContentDialogResult.Primary => "Save", ContentDialogResult.Secondary => "Review", _ => "Cancel" };
            var button = FindButton(prompt, label) ?? throw new InvalidOperationException("Missing native dialog command: " + label);
            var peer = new Microsoft.UI.Xaml.Automation.Peers.ButtonAutomationPeer(button);
            ((Microsoft.UI.Xaml.Automation.Provider.IInvokeProvider)peer.GetPattern(Microsoft.UI.Xaml.Automation.Peers.PatternInterface.Invoke)).Invoke();
            if (await promptDisplay != expected)
                throw new InvalidOperationException("In-place dialog returned the wrong command result: " + label);
        }
    }
    private async Task VerifySegmentFiltersForSmokeTestAsync()
    {
        var originalSection = Section.SelectedIndex;
        var originalEditing = editMode;
        var originalFilters = segmentFilters.ToDictionary(pair => pair.Key, pair => pair.Value.Clone());
        var originalSorts = segmentSorts.ToDictionary(pair => pair.Key, pair => pair.Value);
        var manifestPath = Path.Combine(Model.Project!.Folder, "project.json");
        var manifest = await File.ReadAllBytesAsync(manifestPath);
        try
        {
            foreach (var section in new[] { 0, 2, 3 })
            foreach (var editing in new[] { false, true })
            {
                Section.SelectedIndex = section;
                if (editMode != editing) EditMode_Click(this, new RoutedEventArgs());
                var allRows = UnfilteredSectionRows().ToList();
                segmentFilter.Clear();
                RenderSection();
                foreach (var command in new[] { "sort-start", "sort-duration-asc", "sort-duration-desc", "sort-origin" })
                {
                    if (editing && Segments.Items.Count > 0) Segments.SelectedItems.Add(Segments.Items[0]);
                    await RunDesktopCommandAsync(command);
                    if (Segments.Items.Count != allRows.Count || Segments.SelectedItems.Count != 0 ||
                        Segments.SelectionMode != (editing ? ListViewSelectionMode.Multiple : ListViewSelectionMode.None))
                        throw new InvalidOperationException("Sorting did not preserve rows and clear the editable selection: " + command);
                }
                segmentFilter.Origins.Add("origin-not-present-in-this-section");
                SetSegmentSort("durationAscending");
                if (ClearSegmentFilterButton.Visibility != Visibility.Visible || ClearSegmentSortButton.Visibility != Visibility.Visible ||
                    !CanRunDesktopCommand("clear-segment-filter") || !CanRunDesktopCommand("clear-segment-sort"))
                    throw new InvalidOperationException("Active filter/sort must expose toolbar and menu clear actions.");
                await RunDesktopCommandAsync("clear-segment-sort");
                if (ClearSegmentSortButton.Visibility != Visibility.Collapsed || !segmentFilter.IsActive)
                    throw new InvalidOperationException("Clear Sort must retain the active filter.");
                await RunDesktopCommandAsync("clear-segment-filter");
                if (ClearSegmentFilterButton.Visibility != Visibility.Collapsed || segmentFilter.IsActive || Segments.Items.Count != allRows.Count)
                    throw new InvalidOperationException("Clear Filter must restore rows and hide its toolbar action.");
                foreach (var result in new[] { ContentDialogResult.Secondary, ContentDialogResult.Primary, ContentDialogResult.None })
                {
                    segmentFilter.Clear();
                    segmentFilter.Origins.Add("origin-not-present-in-this-section");
                    segmentFilter.Audio = section == 0 ? "all" : "withAudio";
                    RenderSection();
                    ContentDialog? filterDialog = null;
                    void Capture(ContentDialog candidate, Window window) => filterDialog = candidate;
                    Views.WindowedDialog.Preparing += Capture;
                    try
                    {
                        var display = ShowSegmentFilterAsync();
                        for (var attempt = 0; attempt < 80 && filterDialog is null; attempt++) await Task.Delay(25);
                        if (filterDialog is null) throw new InvalidOperationException("The segment filter dialog did not open.");
                        await dialogPresentationVerifier!.WaitUntilReadyAsync(filterDialog);
                        var panel = (StackPanel)((ScrollViewer)filterDialog.Content).Content;
                        var controls = DialogControls(panel).OfType<StackPanel>().Single(child => child.Tag as string == "segment-filter-controls");
                        var mode = DialogControls(controls).OfType<ComboBox>().Single(combo => Microsoft.UI.Xaml.Automation.AutomationProperties.GetAutomationId(combo) == "segment-filter-type");
                        mode.SelectedIndex = 1;
                        var details = DialogControls(controls).OfType<StackPanel>().Single(child => child.Tag as string == "segment-filter-details");
                        var checks = (StackPanel)((ScrollViewer)details.Children[0]).Content;
                        if (checks.Children.Count != allRows.Count) throw new InvalidOperationException("Filter checklist includes missing or wrong-result rows.");
                        if (checks.Children.FirstOrDefault() is Border border && border.Child is CheckBox check)
                        {
                            check.IsChecked = true;
                            if (border.Background == null) throw new InvalidOperationException("Checked filter row lacks system accent highlight.");
                            check.IsChecked = false;
                        }
                        mode.SelectedIndex = 2;
                        if (DialogControls(details).OfType<NumberBox>().Count() != 2)
                            throw new InvalidOperationException("Segment ranges need separate Start and End controls.");
                        mode.SelectedIndex = 0;
                        var savedOnly = panel.Children.OfType<StackPanel>().First();
                        if (DialogControls(savedOnly).OfType<Button>().Any() || DialogControls(savedOnly).OfType<ComboBox>().Any(combo => (combo.Header?.ToString() ?? "").Contains("Language")))
                            throw new InvalidOperationException("Filter exposes copy or language controls.");
                        if (result == ContentDialogResult.Primary)
                        {
                            foreach (var box in panel.Children.OfType<CheckBox>()) box.IsChecked = false;
                            if (DialogControls(panel).OfType<ComboBox>().FirstOrDefault(box => box.Header as string == "Audio") is { } audio) audio.SelectedIndex = 0;
                        }
                        var label = result switch { ContentDialogResult.Secondary => "Clear Filter", ContentDialogResult.Primary => "Apply Filter", _ => "Cancel" };
                        var button = FindButton(filterDialog, label) ?? throw new InvalidOperationException("Missing filter action: " + label);
                        var peer = new Microsoft.UI.Xaml.Automation.Peers.ButtonAutomationPeer(button);
                        ((Microsoft.UI.Xaml.Automation.Provider.IInvokeProvider)peer.GetPattern(Microsoft.UI.Xaml.Automation.Peers.PatternInterface.Invoke)).Invoke();
                        await display;
                        var visible = Segments.Items.Cast<Models.Segment>().ToList();
                        if (result == ContentDialogResult.None)
                        {
                            if (!segmentFilter.IsActive || visible.Count != 0)
                                throw new InvalidOperationException("Cancel changed the active segment filter.");
                        }
                        else if (segmentFilter.IsActive || visible.Count != allRows.Count || allRows.Any(row => !visible.Contains(row)))
                            throw new InvalidOperationException($"{label} did not restore all section rows (section {section}, editing {editing}).");
                        if (Segments.SelectionMode != (editing ? ListViewSelectionMode.Multiple : ListViewSelectionMode.None))
                            throw new InvalidOperationException("Filtering changed the editor selection mode.");
                    }
                    finally
                    {
                        Views.WindowedDialog.Preparing -= Capture;
                        filterDialog?.Hide();
                    }
                }
            }
            var finalManifest = await File.ReadAllBytesAsync(manifestPath);
            if (!manifest.SequenceEqual(finalManifest))
                throw new InvalidOperationException("Filtering modified the saved project.");
        }
        finally
        {
            segmentFilters.Clear();
            foreach (var pair in originalFilters) segmentFilters[pair.Key] = pair.Value;
            segmentSorts.Clear();
            foreach (var pair in originalSorts) segmentSorts[pair.Key] = pair.Value;
            Section.SelectedIndex = originalSection;
            if (editMode != originalEditing) EditMode_Click(this, new RoutedEventArgs());
            RenderSection();
        }
    }

    private async Task VerifySegmentSelectionForSmokeTestAsync(string report)
    {
        foreach (var theme in new[] { "dark", "light" })
        {
            PreviewTheme(theme);
            foreach (var section in new[] { 0, 2, 3 })
            {
                Section.SelectedIndex = section;
                if (!editMode) EditMode_Click(this, new RoutedEventArgs());
                Deselect_Click(this, new RoutedEventArgs());
                await Task.Delay(80);
                Root.UpdateLayout();
                if (Segments.SelectionMode != Microsoft.UI.Xaml.Controls.ListViewSelectionMode.Multiple ||
                    EditToolbar.Visibility != Visibility.Visible ||
                    Segments.ContainerFromIndex(0) is not Microsoft.UI.Xaml.Controls.ListViewItem row)
                    throw new InvalidOperationException("Edit mode did not show selectable segment rows.");
                var presenter = FindSelectionPresenter(row);
                if (presenter == null || !presenter.SelectionCheckMarkVisualEnabled ||
                    presenter.CheckBoxBorderBrush is not Microsoft.UI.Xaml.Media.SolidColorBrush border || border.Color.A == 0 ||
                    presenter.CheckBoxSelectedBrush is not Microsoft.UI.Xaml.Media.SolidColorBrush fill || fill.Color.A == 0 ||
                    presenter.CheckBrush is not Microsoft.UI.Xaml.Media.SolidColorBrush check || check.Color.A == 0 ||
                    Math.Abs(check.Color.R - fill.Color.R) + Math.Abs(check.Color.G - fill.Color.G) + Math.Abs(check.Color.B - fill.Color.B) < 100)
                    throw new InvalidOperationException($"Segment checkboxes have no visible themed border, selected fill or contrasting checkmark ({theme}).");
                if (section == 2)
                    await CaptureSmokeImage(Root, Path.Combine(Path.GetDirectoryName(report)!, $"description-checkboxes-{theme}-unchecked.png"));
                var selectedItem = Segments.Items[0];
                row.IsSelected = true;
                // Theme/layout changes may recycle the container during SelectionChanged.
                if (Segments.SelectedItems.Count != 1 || !Segments.SelectedItems.Contains(selectedItem))
                    throw new InvalidOperationException($"Selecting a segment row did not update the edit selection: theme={theme}, section={section}, count={Segments.SelectedItems.Count}, row={row.Content?.GetType().Name}, items={Segments.Items.Count}, selected={row.IsSelected}, same={ReferenceEquals(Segments.Items[0], row.Content)}.");
                if (section == 3)
                {
                    var second = (Microsoft.UI.Xaml.Controls.ListViewItem)Segments.ContainerFromIndex(1);
                    second.IsSelected = true;
                    if (Segments.SelectedItems.Count != 2) throw new InvalidOperationException("Selecting another row cleared the first selection.");
                    second.IsSelected = false;
                    if (Segments.SelectedItems.Count != 1) throw new InvalidOperationException("Deselecting one row cleared another selection.");
                }
                if (section == 2)
                {
                    await Task.Delay(80);
                    await CaptureSmokeImage(Root, Path.Combine(Path.GetDirectoryName(report)!, $"description-checkboxes-{theme}-checked.png"));
                    if (!Model.Project!.Cues[0].IsActive) throw new InvalidOperationException("Editing selection cleared the playback highlight.");
                }
                SelectAll_Click(this, new RoutedEventArgs());
                if (Segments.SelectedItems.Count != Segments.Items.Count) throw new InvalidOperationException("Select all missed segment rows.");
                Deselect_Click(this, new RoutedEventArgs());
                if (Segments.SelectedItems.Count != 0) throw new InvalidOperationException("Deselect left selected segment rows.");
                EditMode_Click(this, new RoutedEventArgs());
                if (Segments.SelectionMode != Microsoft.UI.Xaml.Controls.ListViewSelectionMode.None || EditToolbar.Visibility != Visibility.Collapsed)
                    throw new InvalidOperationException("Done did not exit segment selection mode.");
            }
        }
        Section.SelectedIndex = 2;
    }
    private async Task VerifyTimecodePickerForSmokeTestAsync(string report)
    {
        // 1. Parsing verification
        if (!Views.TimecodePicker.TryParseTimecode("01:23:45.678", out var s1) || Math.Abs(s1 - 5025.678) > 0.001)
            throw new InvalidOperationException("TimecodePicker did not parse hh:mm:ss.fff timecode correctly.");
        if (!Views.TimecodePicker.TryParseTimecode("02:15.500", out var s2) || Math.Abs(s2 - 135.5) > 0.001)
            throw new InvalidOperationException("TimecodePicker did not parse mm:ss.fff timecode correctly.");
        if (!Views.TimecodePicker.TryParseTimecode("45.250", out var s3) || Math.Abs(s3 - 45.25) > 0.001)
            throw new InvalidOperationException("TimecodePicker did not parse seconds float correctly.");
        if (!Views.TimecodePicker.TryParseTimecode("00:00:05", out var s4) || Math.Abs(s4 - 5.0) > 0.001)
            throw new InvalidOperationException("TimecodePicker did not parse hh:mm:ss correctly.");

        // 2. Picker state verification
        var picker = new Views.TimecodePicker { Header = "Start time", Value = 3661.125, Maximum = 7200 };
        if (picker.FormattedTimecode != "01:01:01.125")
            throw new InvalidOperationException($"TimecodePicker formatted timecode was '{picker.FormattedTimecode}', expected '01:01:01.125'.");
        if (Math.Abs(picker.TimeSpanValue.TotalSeconds - 3661.125) > 0.001)
            throw new InvalidOperationException("TimecodePicker TimeSpanValue does not match Value.");

        picker.Value = 75.5;
        if (picker.FormattedTimecode != "00:01:15.500")
            throw new InvalidOperationException($"TimecodePicker formatted timecode was '{picker.FormattedTimecode}', expected '00:01:15.500'.");

        // 3. Visual rendering verification in dark and light themes
        var testContainer = new Microsoft.UI.Xaml.Controls.Border
        {
            Width = 600,
            Padding = new Thickness(16),
            Background = Application.Current.Resources["LayerFillColorAltBrush"] as Microsoft.UI.Xaml.Media.Brush,
            CornerRadius = new CornerRadius(8),
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center
        };

        var timingEditor = new Views.SegmentTimingEditor(1.0, 3.0, 600, () => 1.5);
        testContainer.Child = timingEditor;
        Root.Children.Add(testContainer);
        try
        {
            foreach (var theme in new[] { "dark", "light" })
            {
                PreviewTheme(theme);
                testContainer.RequestedTheme = theme == "dark" ? ElementTheme.Dark : ElementTheme.Light;
                testContainer.Background = new Microsoft.UI.Xaml.Media.SolidColorBrush(
                    theme == "dark" ? Windows.UI.Color.FromArgb(255, 32, 32, 32) : Windows.UI.Color.FromArgb(255, 245, 245, 245));
                await Task.Delay(100);
                Root.UpdateLayout();
                VerifyTimingEditorLayoutForSmokeTest(timingEditor);
                timingEditor.EndPicker.Value = 4.25;
                if (!timingEditor.DurationLabel.Text.Contains(3.25.ToString("F3")))
                    throw new InvalidOperationException("Segment duration did not follow the end time.");
                timingEditor.EndPicker.Value = 3.0;
                await CaptureSmokeImage(testContainer, Path.Combine(Path.GetDirectoryName(report)!, $"timing-pickers-{theme}.png"));
            }

            // Capture complete dialog layouts
            var transcriptDialog = CreateSampleSegmentDialogContent(false, 1.0, 3.0, "I need you to tell me everything.", "e.g. Dialogue is in French with English subtitles...");
            transcriptDialog.RequestedTheme = ElementTheme.Dark;
            ((Microsoft.UI.Xaml.Controls.Border)transcriptDialog).Background = new Microsoft.UI.Xaml.Media.SolidColorBrush(Windows.UI.Color.FromArgb(255, 32, 32, 32));
            Root.Children.Add(transcriptDialog);
            await Task.Delay(100);
            Root.UpdateLayout();
            await CaptureSmokeImage(transcriptDialog, Path.Combine(Path.GetDirectoryName(report)!, "edit-transcript-dialog-dark.png"));
            Root.Children.Remove(transcriptDialog);

            var descriptionDialog = CreateSampleSegmentDialogContent(true, 2.0, 5.0, "A character turns slowly and looks around.", "e.g. Focus on facial expression...");
            descriptionDialog.RequestedTheme = ElementTheme.Dark;
            ((Microsoft.UI.Xaml.Controls.Border)descriptionDialog).Background = new Microsoft.UI.Xaml.Media.SolidColorBrush(Windows.UI.Color.FromArgb(255, 32, 32, 32));
            Root.Children.Add(descriptionDialog);
            await Task.Delay(100);
            Root.UpdateLayout();
            await CaptureSmokeImage(descriptionDialog, Path.Combine(Path.GetDirectoryName(report)!, "edit-description-dialog-dark.png"));
            Root.Children.Remove(descriptionDialog);
        }
        finally
        {
            Root.Children.Remove(testContainer);
        }
    }
    private static void VerifyTimingEditorLayoutForSmokeTest(Views.SegmentTimingEditor editor)
    {
        var start = editor.StartPicker;
        var end = editor.EndPicker;
        var duration = editor.DurationLabel;
        var startY = start.TransformToVisual(editor).TransformPoint(new Windows.Foundation.Point()).Y;
        var endY = end.TransformToVisual(editor).TransformPoint(new Windows.Foundation.Point()).Y;
        var durationY = duration.TransformToVisual(editor).TransformPoint(new Windows.Foundation.Point()).Y;
        if (startY + start.ActualHeight >= durationY || durationY + duration.ActualHeight >= endY ||
            Math.Abs(start.ActualWidth - editor.ActualWidth) > 1 || Math.Abs(end.ActualWidth - editor.ActualWidth) > 1)
            throw new InvalidOperationException("Start and end times must occupy separate full-width rows with duration between them.");
        void CheckPicker(Views.TimecodePicker picker)
        {
            var grid = picker.Children.OfType<Grid>().Last();
            var labels = grid.Children.OfType<TextBlock>().Where(label => Grid.GetRow(label) == 0).ToList();
            if (!labels.Select(label => label.Text).SequenceEqual(new[] { "Hours", "Minutes", "Seconds", "Milliseconds" }))
                throw new InvalidOperationException("Timing units must be fully named above their fields.");
            void CheckFields(DependencyObject element)
            {
                if (element is TextBox field && (field.ActualWidth < 24 ||
                    !Microsoft.UI.Xaml.Automation.AutomationProperties.GetName(field).StartsWith(picker.Header)))
                    throw new InvalidOperationException("A time field clips its digits or lacks its start/end accessibility label.");
                for (var i = 0; i < Microsoft.UI.Xaml.Media.VisualTreeHelper.GetChildrenCount(element); i++)
                    CheckFields(Microsoft.UI.Xaml.Media.VisualTreeHelper.GetChild(element, i));
            }
            CheckFields(picker);
        }
        CheckPicker(start);
        CheckPicker(end);
    }
    private FrameworkElement CreateSampleSegmentDialogContent(bool isDescription, double start, double end, string text, string hint)
    {
        var timingEditor = new Views.SegmentTimingEditor(start, end, 600, () => 1.5);
        var timingTitle = new TextBlock { Text = "Timing", FontWeight = Microsoft.UI.Text.FontWeights.SemiBold, FontSize = 15 };

        var textBox = new Microsoft.UI.Xaml.Controls.TextBox
        {
            Header = isDescription ? "Audio description text" : "Transcript text",
            Text = text,
            AcceptsReturn = true,
            TextWrapping = TextWrapping.Wrap,
            MinHeight = isDescription ? 120 : 90
        };

        var hintBox = new Microsoft.UI.Xaml.Controls.TextBox
        {
            Header = "Optional hint for Gemini",
            Text = hint,
            AcceptsReturn = true,
            TextWrapping = TextWrapping.Wrap,
            MinHeight = 60
        };

        var contentStack = new Microsoft.UI.Xaml.Controls.StackPanel { Width = 580, Spacing = 14 };
        contentStack.Children.Add(timingTitle);
        contentStack.Children.Add(timingEditor);
        contentStack.Children.Add(new Microsoft.UI.Xaml.Controls.TextBlock { Text = isDescription ? "Audio Description" : "Transcript", FontWeight = Microsoft.UI.Text.FontWeights.SemiBold, FontSize = 15 });
        contentStack.Children.Add(textBox);
        contentStack.Children.Add(new Microsoft.UI.Xaml.Controls.TextBlock { Text = "Hint for Gemini (Optional)", FontWeight = Microsoft.UI.Text.FontWeights.SemiBold, FontSize = 15 });
        contentStack.Children.Add(hintBox);

        var container = new Microsoft.UI.Xaml.Controls.Border
        {
            Width = 620,
            Padding = new Thickness(20),
            CornerRadius = new CornerRadius(8),
            Child = contentStack
        };
        return container;
    }
    private async Task VerifyMatchingContextMenusForSmokeTestAsync(string report)
    {
        var project = Model.Project!;
        var source = project.SourceType; var url = project.YoutubeURL;
        var developer = Model.Settings.Data["developerMode"]?.DeepClone();
        string[] Entries(System.Collections.Generic.IEnumerable<MenuFlyoutItemBase> entries) => entries.Select(item => item switch {
            MenuFlyoutSeparator => "-", MenuFlyoutSubItem group => group.Text + "[" + string.Join(",", Entries(group.Items)) + "]",
            MenuFlyoutItem action => action.Text, _ => "?" }).ToArray();
        try {
            foreach (var youtube in new[] { false, true }) foreach (var dev in new[] { false, true }) {
                project.SourceType = youtube ? "youtube" : "local";
                project.YoutubeURL = youtube ? "https://www.youtube.com/watch?v=dQw4w9WgXcQ" : null;
                Model.Settings.Data["developerMode"] = dev;
                desktopEditorVisible = false;
                var menu = ProjectMenu(project);
                var expected = new System.Collections.Generic.List<string> { "Open in New Window", "-", "Rename" };
                if (youtube) expected.Add("Open in YouTube");
                expected.AddRange(new[] { "Open Project Settings…", "Open in Project Folder" });
                if (dev) expected.Add("Open Project Configuration");
                expected.Add("Analysis[" + string.Join(",", new[] { DesktopCommandTitle("prepare-video", project), "-", "Analyze Video…", "-", DesktopCommandTitle("transcribe", project), DesktopCommandTitle("generate-summary", project), DesktopCommandTitle("generate-ad-cues", project) }) + "]");
                expected.AddRange(new[] { "Export…", "Remove from List", "-", "Choose Content to Delete…", "Delete Project…" });
                if (!Entries(menu.Items).SequenceEqual(expected)) throw new InvalidOperationException("Gallery context hierarchy differs: " + string.Join(" | ", Entries(menu.Items)));
                if (!CanRunProjectContextCommand("export", project, false)) throw new InvalidOperationException("Offline export must not require Gemini access.");
                foreach (var item in menu.Items.OfType<MenuFlyoutItem>()) if (item.Icon == null || item.Tag == null) throw new InvalidOperationException("Context action missing icon or ID.");
                foreach (var (id, glyph) in new[] { ("delete-project", "\uE74D"), ("delete-content", "\uE75C") }) {
                    var item = menu.Items.OfType<MenuFlyoutItem>().Single(item => item.Tag as string == id);
                    if (item.Icon is not FontIcon icon || icon.Glyph != glyph) throw new InvalidOperationException("Incorrect project context delete glyph: " + id);
                }
                foreach (var (id, glyph) in new[] { ("delete-project", "\uE74D"), ("trash-project", "\uE74D"), ("delete-content", "\uE75C"),
                    ("delete-transcript", "\uE74D"), ("delete-summary", "\uE74D"), ("delete-descriptions", "\uE74D"), ("delete-current", "\uE74D"),
                    ("segment-delete-selected", "\uE74D"), ("segment-one-delete", "\uE74D"), ("delete-narration", "\uE74F"), ("segment-one-delete-audio", "\uE74F") })
                    if (DesktopCommandIcon(id) is not FontIcon icon || icon.Glyph != glyph) throw new InvalidOperationException("Incorrect shared delete command glyph: " + id);

                desktopEditorVisible = true;
                if (CanRunProjectContextCommand("export", project, false)) throw new InvalidOperationException("Stale gallery popup may not run in an editor.");
                var sidebar = ProjectMenu(project);
                var sidebarExpected = new System.Collections.Generic.List<string> { "Open in This Window" };
                if (youtube) sidebarExpected.Add("Open in YouTube");
                sidebarExpected.AddRange(new[] { "Open in New Window", "Open Project Settings…", "-", "Remove from List", "Delete Project…" });
                if (!Entries(sidebar.Items).SequenceEqual(sidebarExpected)) throw new InvalidOperationException("Editor project list context differs.");
                if (sidebar.Items.OfType<MenuFlyoutItem>().Single(item => item.Tag as string == "trash-project").Icon is not FontIcon sidebarDelete || sidebarDelete.Glyph != "\uE74D")
                    throw new InvalidOperationException("Editor project-list delete icon must be trash.");
                foreach (var item in desktopMenuItems.Where(pair => pair.Key is "delete-project" or "delete-content" or "delete-current" or "delete-descriptions" or "delete-narration" or "segment-delete-selected" or "trash-project").SelectMany(pair => pair.Value)) {
                    var expectedGlyph = item.Tag as string == "delete-content" ? "\uE75C" : item.Tag as string == "delete-narration" ? "\uE74F" : "\uE74D";
                    if (item.Icon is not FontIcon icon || icon.Glyph != expectedGlyph) throw new InvalidOperationException("Editor menu deletion icon has fallen back: " + item.Tag);
                }

            }
            project.SourceType = source; project.YoutubeURL = url;
            Section.SelectedIndex = 1; summaryEditMode = false;
            var reading = SummaryReadingMenu();
            if (!Entries(reading.Items).SequenceEqual(new[] { "Read from Start", "Read from Selection", "Read Selected Text Only", "-", "Edit Summary…", "-", "Stop" })) throw new InvalidOperationException("Summary reading menu differs.");
            foreach (var section in new[] { 0, 2, 3 }) {
                Section.SelectedIndex = section;
                foreach (var row in section == 0 ? project.Transcript : project.Cues) {
                    var menu = SegmentMenu(row);
                    if (menu.Items.OfType<MenuFlyoutItem>().Any(item => item.Icon == null)) throw new InvalidOperationException("Row menu lacks icon.");
                    foreach (var item in menu.Items.OfType<MenuFlyoutItem>().Where(item => item.Tag as string is "segment-one-delete" or "segment-one-delete-audio")) {
                        var expectedGlyph = item.Tag as string == "segment-one-delete-audio" ? "\uE74F" : "\uE74D";
                        if (item.Icon is not FontIcon icon || icon.Glyph != expectedGlyph) throw new InvalidOperationException("Editor row delete icon is incorrect: " + item.Tag);
                    }

                    var more = SegmentMenu(row, moreActionsOnly: true).Items.OfType<MenuFlyoutItem>().Select(item => item.Tag as string);
                    var expectedMore = row.IsDescription ? new[] { "segment-one-rewrite", "segment-one-synthesize", "segment-one-import-audio", "segment-one-delete-audio", "segment-one-delete" } : new[] { "segment-one-edit", "segment-one-reanalyze", "segment-one-delete" };
                    if (!more.SequenceEqual(expectedMore.Where(id => RowCommandVisible(id, row.IsDescription)))) throw new InvalidOperationException("More Actions differs from Mac.");
                }
            }
            Section.SelectedIndex = 4;
            var background = (MenuFlyout)EditorWorkspace.ContextFlyout;
            background.ShowAt(EditorWorkspace); await Task.Delay(150);
            if (background.Items.OfType<MenuFlyoutItem>().Any(item => item.Tag as string == "copy-to" && item.Visibility == Visibility.Visible)) throw new InvalidOperationException("Overview context must omit Copy and Translate.");
            background.Hide();
            Section.SelectedIndex = 0;
            var popup = ProjectMenu(project); popup.ShowAt(EditorWorkspace); await Task.Delay(200);
            await CaptureSmokeImage(Root, Path.Combine(Path.GetDirectoryName(report)!, "matching-context-menu-editor.png")); popup.Hide();
        }
        finally { project.SourceType = source; project.YoutubeURL = url; Model.Settings.Data["developerMode"] = developer; }
    }

    private void VerifyProjectConfigurationDeveloperModeForSmokeTest()
    {
        var saved = Model.Settings.Data["developerMode"]?.DeepClone();
        try
        {
            foreach (var enabled in new[] { false, true, false })
            {
                Model.Settings.Data["developerMode"] = enabled;
                UpdateDesktopMenuStates();
                var expected = enabled ? Visibility.Visible : Visibility.Collapsed;
                if (desktopMenuItems["open-configuration"].Any(item => item.Visibility != expected) ||
                    InspectorOpenConfigurationButton.Visibility != expected ||
                    CanRunDesktopCommand("open-configuration") != enabled ||
                    ProjectMenu(Model.Project!).Items.OfType<MenuFlyoutItem>().Any(item => (string?)item.Tag == "open-configuration"))
                    throw new InvalidOperationException("Project configuration must follow Developer mode in menus, context actions, inspector, and command guards.");
            }
        }
        finally
        {
            Model.Settings.Data["developerMode"] = saved;
            UpdateDesktopMenuStates();
        }
    }

    private async Task VerifyEditorProjectNavigationForSmokeTestAsync()
    {
        var original = Model.Project!;
        var originalSummary = original.Summary;
        var locations = Model.Settings.Data["projectLocations"]?.DeepClone();
        var wasVisible = ProjectSplitView.IsPaneOpen;
        var wasGrid = ProjectGrid.Visibility == Visibility.Visible;
        var folder = Path.Combine(Path.GetDirectoryName(original.Folder)!, "editor-navigation-" + Guid.NewGuid());
        var windowCount = App.WindowCountForSmokeTest;
        Directory.CreateDirectory(folder);
        try
        {
            foreach (var file in Directory.EnumerateFiles(original.Folder)) File.Copy(file, Path.Combine(folder, Path.GetFileName(file)));
            var manifest = System.Text.Json.Nodes.JsonNode.Parse(File.ReadAllText(Path.Combine(folder, "project.json")))!.AsObject();
            manifest["id"] = "editor-navigation-" + Guid.NewGuid();
            manifest["name"] = "Editor navigation fixture";
            File.WriteAllText(Path.Combine(folder, "project.json"), manifest.ToJsonString(Models.Json.Options));
            Model.Open(original.Folder);
            Model.Reload();
            ShowEditor();
            SetProjectLayout(true);
            var target = Models.StudioProject.Load(folder);
            var actions = ProjectMenu(target).Items.OfType<MenuFlyoutItem>().Select(item => (string?)item.Tag).ToList();
            foreach (var id in new[] { "open-project-here", "open-editor-window", "remove-from-list", "trash-project" })
                if (!actions.Contains(id)) throw new InvalidOperationException("Editor project context action is missing: " + id);
            foreach (var button in new[] { EditorRemoveProjectButton, EditorDeleteProjectButton })
                if (string.IsNullOrWhiteSpace(Microsoft.UI.Xaml.Automation.AutomationProperties.GetName(button)) || ToolTipService.GetToolTip(button) == null)
                    throw new InvalidOperationException("Project action icon lacks its label or tooltip.");
            Section.SelectedIndex = 1;
            SetSummaryEditMode(true);
            SummaryEditorBox.Text = originalSummary + "\n\nSaved before project switching.";
            await SwitchProjectAsync(target);
            if (Model.Project?.Folder != target.Folder || App.WindowCountForSmokeTest != windowCount || summaryEditMode)
                throw new InvalidOperationException("Grid navigation did not reuse the current editor and finish summary editing.");
            var saved = System.Text.Json.Nodes.JsonNode.Parse(File.ReadAllText(Path.Combine(original.Folder, "project.json")))!;
            if (!saved["summary"]!.GetValue<string>().EndsWith("Saved before project switching.", StringComparison.Ordinal))
                throw new InvalidOperationException("Switching discarded the edited summary.");
            SetProjectLayout(false);
            await SwitchProjectAsync(original);
            if (Model.Project?.Folder != original.Folder || App.WindowCountForSmokeTest != windowCount)
                throw new InvalidOperationException("List navigation did not reuse the editor.");
            UpdateEditorProjectButtons();
            if (!CanRunDesktopCommand("remove-from-list"))
                throw new InvalidOperationException("Remove from List is unavailable for the selected editor-browser project.");
            RemoveListedProject_Click(this, new RoutedEventArgs());
            if (Model.Project?.Folder != original.Folder || Model.Projects.Any(project => project.Folder == original.Folder))
                throw new InvalidOperationException("Removing the editor-browser entry closed the editor or left its entry visible.");
        }
        finally
        {
            Model.Settings.Data["projectLocations"] = locations;
            Model.Settings.Save();
            Model.Open(original.Folder);
            await Model.SaveSummaryAsync(originalSummary);
            if (Directory.Exists(folder)) Directory.Delete(folder, true);
            Model.Reload();
            SetProjectLayout(wasGrid);
            ProjectSplitView.IsPaneOpen = wasVisible;
            UpdateEditorProjectButtons();
            Section.SelectedIndex = 0;
        }
    }
    private void VerifyDuplicateProjectPathsForSmokeTest()
    {
        var project = Model.Project!;
        var originalLocations = Model.Settings.Data["projectLocations"]?.DeepClone();
        var originalLibrary = Model.Settings.Get("libraryPath");
        var manifest = Path.Combine(project.Folder, "project.json");
        var bytes = File.ReadAllBytes(manifest);
        try
        {
            Model.Settings.Set("libraryPath", Path.GetDirectoryName(project.Folder)!);
            Model.Settings.Data["projectLocations"] = new System.Text.Json.Nodes.JsonArray(
                project.Folder, project.Folder + Path.DirectorySeparatorChar,
                Path.Combine(project.Folder, "."), project.Folder.StartsWith(@"\\", StringComparison.Ordinal)
                    ? @"\\?\UNC\" + project.Folder[2..] : @"\\?\" + project.Folder);
            Model.Settings.Save();
            Model.Reload();
            var identity = Services.ProjectFolderIdentity.Normalize(project.Folder);
            if (Model.Projects.Count(entry => string.Equals(Services.ProjectFolderIdentity.Normalize(entry.Folder), identity, StringComparison.OrdinalIgnoreCase)) != 1)
                throw new InvalidOperationException("Project path aliases produced duplicate gallery entries.");
            var cached = Model.Projects.Single(entry => string.Equals(entry.Folder, identity, StringComparison.OrdinalIgnoreCase));
            if (Model.ReloadIfProjectsChanged() || !Model.Projects.Contains(cached))
                throw new InvalidOperationException("Duplicate saved locations caused repeated gallery rebuilding.");
            Model.Open(project.Folder + Path.DirectorySeparatorChar);
            if (Model.Projects.Count(entry => string.Equals(entry.Folder, identity, StringComparison.OrdinalIgnoreCase)) != 1 ||
                !File.ReadAllBytes(manifest).SequenceEqual(bytes))
                throw new InvalidOperationException("Opening a path alias duplicated a project or changed its files.");
        }
        finally
        {
            Model.Settings.Set("libraryPath", originalLibrary);
            Model.Settings.Data["projectLocations"] = originalLocations;
            Model.Settings.Save();
            Model.Reload();
        }
    }
    private async Task VerifyRemoveFromListFileMenuForSmokeTestAsync()
    {
        var wasGrid = GalleryGrid.Visibility == Visibility.Visible;
        var folder = Model.Projects.First().Folder;
        var manifest = Path.Combine(folder, "project.json");
        var original = File.ReadAllBytes(manifest);
        try
        {
            foreach (var useGrid in new[] { true, false })
            {
                if ((GalleryGrid.Visibility == Visibility.Visible) != useGrid) GalleryLayout_Click(this, new RoutedEventArgs());
                GalleryGrid.SelectedItem = null;
                GalleryList.SelectedItem = null;
                if (CanRunDesktopCommand("remove-from-list"))
                    throw new InvalidOperationException("File Remove from List is enabled with no gallery selection.");
                var project = Model.Projects.Single(entry => entry.Folder == folder);
                if (useGrid) GalleryGrid.SelectedItem = project;
                else GalleryList.SelectedItem = project;
                UpdateDesktopMenuStates();
                var item = desktopMenuItems["remove-from-list"].First();
                if (!CanRunDesktopCommand("remove-from-list") || item.Text != "Remove from List" || item.Icon == null ||
                    (bool)item.ReadLocalValue(Control.IsEnabledProperty) != true)
                    throw new InvalidOperationException("File Remove from List did not follow the gallery selection.");
                GalleryLayout_Click(this, new RoutedEventArgs());
                if (SelectedGalleryProject()?.Folder != folder)
                    throw new InvalidOperationException("Switching gallery layouts lost the selected project.");
                GalleryLayout_Click(this, new RoutedEventArgs());
                try
                {
                    await RunDesktopCommandAsync("remove-from-list");
                    if (Model.Projects.Any(entry => entry.Folder == folder) || !Directory.Exists(folder) ||
                        !File.ReadAllBytes(manifest).SequenceEqual(original) || CanRunDesktopCommand("remove-from-list"))
                        throw new InvalidOperationException("File Remove from List changed project files or retained a stale selection.");
                }
                finally { Model.Open(folder); }
            }
        }
        finally
        {
            GalleryGrid.SelectedItem = null;
            GalleryList.SelectedItem = null;
            if ((GalleryGrid.Visibility == Visibility.Visible) != wasGrid) GalleryLayout_Click(this, new RoutedEventArgs());
            UpdateDesktopMenuStates();
        }
    }
    private void VerifyRemoveFromListForSmokeTest()
    {
        if (CanRunDesktopCommand("remove-from-list"))
            throw new InvalidOperationException("File Remove from List must require a visible project browser selection.");
        var project = Model.Project!;
        var manifest = Path.Combine(project.Folder, "project.json");
        var original = File.ReadAllBytes(manifest);
        var menu = ProjectMenu(project);
        var item = menu.Items.OfType<MenuFlyoutItem>().SingleOrDefault(entry => (string?)entry.Tag == "remove-from-list");
        if (item is not { Text: "Remove from List", Icon: not null } ||
            Microsoft.UI.Xaml.Automation.AutomationProperties.GetAutomationId(item) != "remove-from-list")
            throw new InvalidOperationException("The project context menu is missing Remove from List.");
        try
        {
            Model.RemoveProjectFromList(project);
            Model.Reload();
            if (Model.Projects.Any(entry => entry.Folder == project.Folder) || !Directory.Exists(project.Folder) ||
                !File.ReadAllBytes(manifest).SequenceEqual(original) || Model.Project?.Folder != project.Folder)
                throw new InvalidOperationException("Remove from List changed files, closed the editor, or returned after refresh.");
            if (Model.ReloadIfProjectsChanged())
                throw new InvalidOperationException("A hidden library folder caused repeated gallery refreshes.");
        }
        finally { Model.Open(project.Folder); }
        if (!Model.Projects.Any(entry => entry.Folder == project.Folder))
            throw new InvalidOperationException("Opening a removed project did not restore its list entry.");
    }
    private void VerifyEditorActionMenusForSmokeTest()
    {
        var originalSection = Section.SelectedIndex;
        var originalEditing = editMode;
        var originalSummaryEditing = summaryEditMode;
        var originalSelectionMode = Segments.SelectionMode;
        var expectedActions = Array.Empty<string>();
        void Verify(params string[] expected)
        {
            expectedActions = expected;
            var menu = SegmentMenu();
            var actual = menu.Items.Select(item => item is MenuFlyoutSeparator ? "-" : (string)((MenuFlyoutItem)item).Tag);
            if (!actual.SequenceEqual(expected))
                throw new InvalidOperationException($"Editor actions do not match section {Section.SelectedIndex} (editing={editMode}, summaryEditing={summaryEditMode}).");
            foreach (var item in menu.Items.OfType<MenuFlyoutItem>())
            {
                var id = (string)item.Tag;
                if (item.Icon == null || item.Text != DesktopCommandTitle(id) ||
                    (bool)item.ReadLocalValue(Control.IsEnabledProperty) != CanRunDesktopCommand(id))
                    throw new InvalidOperationException($"Editor action {id} has drifted from the shared command title, icon, or guard.");
            }
        }
        try
        {
            foreach (var section in new[] { 0, 1, 2, 3, 4 })
            {
                Section.SelectedIndex = section;
                editMode = false;
                Segments.SelectionMode = ListViewSelectionMode.None;
                summaryEditMode = false;
                if (section == 1)
                {
                    Verify("segment-edit-mode", "generate-summary", "delete-current");
                    summaryEditMode = true;
                    Verify("segment-edit-mode");
                    summaryEditMode = false;
                }
                else if (section == 4) Verify("synthesize-tts", "synthesize-changed");
                else
                {
                    Verify();
                    editMode = true;
                    Segments.SelectionMode = ListViewSelectionMode.Multiple;
                    if (section == 0)
                        Verify("add", "segment-edit-selected", "segment-select-all", "segment-deselect-all", "segment-delete-selected", "undo", "redo");
                    else if (section == 2)
                        Verify("add", "segment-edit-selected", "segment-select-all", "segment-deselect-all", "segment-delete-selected", "undo", "redo", "-", "synthesize-tts", "synthesize-changed");
                    else
                        Verify("segment-edit-selected", "segment-select-all", "segment-deselect-all", "segment-delete-selected", "undo", "redo", "-", "synthesize-tts", "synthesize-changed");
                    // Selection changes must refresh availability when the flyout is rebuilt.
                    SelectAll_Click(this, new RoutedEventArgs());
                    Verify(expectedActions);
                    foreach (var id in new[] { "segment-edit-selected", "segment-deselect-all", "segment-delete-selected" })
                        if (Segments.Items.Count > 0 && !CanRunDesktopCommand(id))
                            throw new InvalidOperationException($"Editor action {id} is unavailable with selected rows.");
                    Deselect_Click(this, new RoutedEventArgs());
                    Verify(expectedActions);
                    if (CanRunDesktopCommand("segment-edit-selected") || CanRunDesktopCommand("segment-delete-selected"))
                        throw new InvalidOperationException("Selection actions stayed enabled after deselecting all rows.");
                }
            }
        }
        finally
        {
            editMode = originalEditing;
            summaryEditMode = originalSummaryEditing;
            Segments.SelectionMode = originalSelectionMode;
            Section.SelectedIndex = originalSection;
            RenderSection();
        }
    }
    private void VerifySegmentMoreButtonsForSmokeTest(bool editing)
    {
        Root.UpdateLayout();
        var expected = editing ? Visibility.Visible : Visibility.Collapsed;
        if (EditorMoreButton.Visibility != expected || Segments.Items.OfType<Models.Segment>().Any(row => row.IsEditing != editing))
            throw new InvalidOperationException("More actions did not follow segment Edit/View mode.");
        void CheckRows(DependencyObject element)
        {
            if (element is Microsoft.UI.Xaml.Controls.Button button &&
                Microsoft.UI.Xaml.Automation.AutomationProperties.GetName(button) == "More segment options" && button.Visibility != expected)
                throw new InvalidOperationException("A segment More button has the wrong Edit/View visibility.");
            for (var i = 0; i < Microsoft.UI.Xaml.Media.VisualTreeHelper.GetChildrenCount(element); i++)
                CheckRows(Microsoft.UI.Xaml.Media.VisualTreeHelper.GetChild(element, i));
        }
        CheckRows(Segments);
    }
    private static Microsoft.UI.Xaml.Controls.Primitives.ListViewItemPresenter? FindSelectionPresenter(DependencyObject element)
    {
        if (element is Microsoft.UI.Xaml.Controls.Primitives.ListViewItemPresenter presenter) return presenter;
        for (var i = 0; i < Microsoft.UI.Xaml.Media.VisualTreeHelper.GetChildrenCount(element); i++)
            if (FindSelectionPresenter(Microsoft.UI.Xaml.Media.VisualTreeHelper.GetChild(element, i)) is { } child) return child;
        return null;
    }
    private void VerifyTimelineThumbnailsForSmokeTest(int count)
    {
        Root.UpdateLayout();
        var tiles = Thumbnails.Children.OfType<Microsoft.UI.Xaml.Controls.Grid>().ToList();
        if (tiles.Count != count || ThumbnailEmpty.Visibility != Visibility.Collapsed ||
            tiles.Any(tile => Math.Abs(tile.ActualHeight - 42) > 1 || tile.Background is not Microsoft.UI.Xaml.Media.ImageBrush brush || brush.Stretch != Microsoft.UI.Xaml.Media.Stretch.UniformToFill) ||
            Math.Abs(tiles.Sum(tile => tile.ActualWidth) + (count - 1) * 2 - ThumbnailScrubSurface.ActualWidth) > 2 ||
            Math.Abs(Thumbnails.ActualWidth - TranscriptTrack.ActualWidth) > 2)
            throw new InvalidOperationException("Timeline thumbnails did not fill and align with the timeline tracks.");
    }
    private async Task VerifyProjectPaneTransitionsForSmokeTestAsync()
    {
        if (ProjectSplitView.DisplayMode != SplitViewDisplayMode.Inline || ProjectSplitView.PanePlacement != SplitViewPanePlacement.Left ||
            InspectorSplitView.DisplayMode != SplitViewDisplayMode.Inline || InspectorSplitView.PanePlacement != SplitViewPanePlacement.Right)
            throw new InvalidOperationException("Project and inspector panes must use native inline slide transitions.");
        var wasOpen = ProjectSplitView.IsPaneOpen;
        try
        {
            ProjectSplitView.IsPaneOpen = true;
            await Task.Delay(260);
            if (EditorProjectLayoutButton.Visibility != Visibility.Visible || EditorProjectLayoutButton.ActualWidth <= 0)
                throw new InvalidOperationException("Opening the project pane did not show its layout button.");
            if (!Search.Focus(FocusState.Programmatic)) throw new InvalidOperationException("The open project pane is not keyboard reachable.");
            await RunDesktopCommandAsync("show-projects");
            await Task.Delay(160);
            if (ProjectSplitView.IsPaneOpen || EditorProjectLayoutButton.Visibility != Visibility.Collapsed ||
                !ReferenceEquals(FocusManager.GetFocusedElement(Root.XamlRoot), EditorProjectListButton))
                throw new InvalidOperationException("Closing the project pane did not preserve a reachable keyboard focus.");
            // Reversing an unfinished transition must leave the final requested state usable.
            await RunDesktopCommandAsync("show-projects");
            await RunDesktopCommandAsync("show-projects");
            await RunDesktopCommandAsync("show-projects");
            await Task.Delay(260);
            if (!ProjectSplitView.IsPaneOpen || LibraryPanel.ActualWidth <= 0 || EditorProjectLayoutButton.Visibility != Visibility.Visible ||
                DesktopCommandTitle("show-projects") != "Hide Project List")
                throw new InvalidOperationException("Rapid project-pane toggles lost their final layout or menu state.");
            if (!EditorProjectLayoutButton.Focus(FocusState.Programmatic))
                throw new InvalidOperationException("The project pane layout button is not keyboard reachable.");
            await RunDesktopCommandAsync("show-projects");
            await Task.Delay(260);
            if (EditorProjectLayoutButton.Visibility != Visibility.Collapsed ||
                !ReferenceEquals(FocusManager.GetFocusedElement(Root.XamlRoot), EditorProjectListButton))
                throw new InvalidOperationException("Hiding the layout button did not return focus to the project pane toggle.");
        }
        finally
        {
            ProjectSplitView.IsPaneOpen = wasOpen;
            UpdateEditorProjectButtons();
            UpdateDesktopMenuStates();
        }
    }

    private async Task VerifyInspectorToggleForSmokeTestAsync(string report)
    {
        var originalTheme = App.ActiveTheme ?? Model.Settings.Get("theme", "system");
        var originalOpen = InspectorSplitView.IsPaneOpen;
        try
        {
            foreach (var theme in new[] { "light", "dark" })
            {
                PreviewTheme(theme);
                if (!InspectorSplitView.IsPaneOpen) await RunDesktopCommandAsync("show-inspector");
                await Task.Delay(100);
                if (TopInspectorToggle.Visibility != Visibility.Visible || !TopInspectorToggle.IsEnabled ||
                    TopInspectorToggle.Content as string != "\uE8A0" ||
                    Microsoft.UI.Xaml.Automation.AutomationProperties.GetName(TopInspectorToggle) != "Hide Inspector")
                    throw new InvalidOperationException("The editor header is missing its labeled native inspector button.");
                await CaptureSmokeImage(ProjectHeader, Path.Combine(Path.GetDirectoryName(report)!, $"inspector-header-{theme}.png"));
                var peer = Microsoft.UI.Xaml.Automation.Peers.FrameworkElementAutomationPeer.CreatePeerForElement(TopInspectorToggle)!;
                ((Microsoft.UI.Xaml.Automation.Provider.IToggleProvider)peer.GetPattern(Microsoft.UI.Xaml.Automation.Peers.PatternInterface.Toggle)).Toggle();
                await Task.Delay(160);
                if (InspectorSplitView.IsPaneOpen || TopInspectorToggle.Visibility != Visibility.Visible ||
                    Microsoft.UI.Xaml.Automation.AutomationProperties.GetName(TopInspectorToggle) != "Show Inspector" ||
                    ToolTipService.GetToolTip(TopInspectorToggle) as string != "Show Inspector (Ctrl+Shift+B)" ||
                    desktopMenuItems["show-inspector"].Any(item => item.Text != "Show Inspector"))
                    throw new InvalidOperationException("The header button did not close the inspector and synchronize its menu and accessible label.");
                await RunDesktopCommandAsync("show-inspector");
                await Task.Delay(160);
                if (!InspectorSplitView.IsPaneOpen ||
                    Microsoft.UI.Xaml.Automation.AutomationProperties.GetName(TopInspectorToggle) != "Hide Inspector")
                    throw new InvalidOperationException("The menu/shortcut inspector action did not synchronize the header button.");
                if (!InspectorSettingsButton.Focus(FocusState.Programmatic))
                    throw new InvalidOperationException("The inspector footer is not keyboard reachable.");
                await RunDesktopCommandAsync("show-inspector");
                await Task.Delay(160);
                if (!ReferenceEquals(FocusManager.GetFocusedElement(Root.XamlRoot), TopInspectorToggle))
                    throw new InvalidOperationException("Closing the inspector left focus inside its hidden pane.");
                await RunDesktopCommandAsync("show-inspector");
            }
        }
        finally
        {
            if (InspectorSplitView.IsPaneOpen != originalOpen) ToggleInspector_Click(this, new RoutedEventArgs());
            PreviewTheme(originalTheme);
        }
    }

    private void VerifyEditorPrimaryActionForSmokeTest(bool editing)
    {
        UpdateDesktopMenuStates();
        var editTitle = DesktopCommandTitle("segment-edit-mode");
        if (FindButton(LanguageResultsPanel, editTitle) is not { } inspectorEdit ||
            inspectorEdit.IsEnabled != CanRunDesktopCommand("segment-edit-mode") ||
            desktopMenuItems["segment-edit-mode"].Any(item => item.Text != editTitle || item.IsEnabled != inspectorEdit.IsEnabled) ||
            editTitle.StartsWith("Done Editing", StringComparison.Ordinal) != editing)
            throw new InvalidOperationException("Inspector, Edit menu, and Project menu must share the current Edit/Done state.");
        if (EditModeButton.Visibility != Visibility.Visible ||
            EditModeButton.Content is not FontIcon icon || icon.Glyph != (editing ? "\uE73E" : "\uE70F") ||
            !ReferenceEquals(EditModeButton.Style, Application.Current.Resources["AccentButtonStyle"]) ||
            ToolTipService.GetToolTip(EditModeButton) == null ||
            string.IsNullOrWhiteSpace(Microsoft.UI.Xaml.Automation.AutomationProperties.GetName(EditModeButton)))
            throw new InvalidOperationException("Each editable section needs one accent-colour, icon-only Edit/Done button with accessible metadata.");
        if ((GenerateCurrent.Visibility == Visibility.Visible) != (Section.SelectedIndex <= 2))
            throw new InvalidOperationException("Generation must remain visible in the toolbar on Transcript, Summary, and Descriptions, regardless of inspector visibility.");
        if (Section.SelectedIndex <= 2)
        {
            var expectedGlyph = Section.SelectedIndex switch { 0 => "\uE90A", 1 => "\uE8E4", _ => "\uE8B9" };
            if (GenerateCurrent.Content is not FontIcon generateIcon || generateIcon.Glyph != expectedGlyph ||
                InspectorGenerate.Content is not StackPanel inspectorContent || inspectorContent.Children[0] is not FontIcon inspectorIcon ||
                inspectorIcon.Glyph != expectedGlyph ||
                string.IsNullOrWhiteSpace(Microsoft.UI.Xaml.Automation.AutomationProperties.GetName(InspectorGenerate)))
                throw new InvalidOperationException("Section generation icons must follow the content type in both toolbar and inspector.");
        }
    }

    private static Button? FindButton(DependencyObject root, string content)
    {
        var count = Microsoft.UI.Xaml.Media.VisualTreeHelper.GetChildrenCount(root);
        for (var i = 0; i < count; i++)
        {
            var child = Microsoft.UI.Xaml.Media.VisualTreeHelper.GetChild(root, i);
            if (child is Button button && string.Equals(button.Content as string, content, StringComparison.Ordinal)) return button;
            if (FindButton(child, content) is { } found) return found;
        }
        return null;
    }
    private void VerifyLanguageInspectorForSmokeTest()
    {
        if (Model.Project is not { } project) throw new InvalidOperationException("Language controls require a project.");
        var previousSection = Section.SelectedIndex; var previousEdit = editMode; var previousSummaryEdit = summaryEditMode;
        try {
            summaryEditMode = false;
            foreach (var (tab, kind) in new[] { (0, "transcript"), (1, "summary"), (2, "descriptions") }) {
                Section.SelectedIndex = tab; editMode = false;
                var panel = BuildLanguageResultsPanel(project);
                if (panel.Children.OfType<Button>().Any(button => button.Content as string == "Keep All Sections in Sync"))
                    throw new InvalidOperationException("Sync action must be absent from inspector view mode.");
                var copyButtons = panel.Children.OfType<Button>().Where(button => button.Content?.ToString()?.StartsWith("Copy and Translate", StringComparison.Ordinal) == true).ToArray();
                if (copyButtons.Length != 1 || copyButtons[0].Content?.ToString() != CurrentSectionCopyTitle || !CopyDefaultKinds(true).SequenceEqual(new[] { kind }) || !CopyDefaultKinds(false).SequenceEqual(new[] { "transcript", "summary", "descriptions" }))
                    throw new InvalidOperationException("The inspector must offer one named current-section Copy and Translate action, while Edit defaults to all sections.");
                var headers = panel.Children.OfType<ComboBox>().Select(c => c.Header?.ToString()).ToArray();
                if (headers.Any(header => header?.EndsWith(" Generation Language") == true || header == "Review language filter") ||
                    panel.Children.OfType<Control>().FirstOrDefault() is not ComboBox { Header: "View Project Language" })
                    throw new InvalidOperationException("The inspector must start with one shared language and omit repeated language choices.");
                if (languageControls.Any(pair => pair.requiresEdit && pair.control.IsEnabled)) throw new InvalidOperationException("Language mutation controls must be disabled in view mode.");
                if (tab == 1) summaryEditMode = true; else editMode = true;
                var editingPanel = BuildLanguageResultsPanel(project);
                if (editingPanel.Children.OfType<Button>().Any(button => button.Content as string == "Keep All Sections in Sync") ||
                    editingPanel.Children.OfType<ComboBox>().Any(picker => picker.Header?.ToString()?.EndsWith(" Generation Language") == true))
                    throw new InvalidOperationException("Editing must keep the single shared language without sync or section targets.");
                if (!HasUnsavedLanguageSummary && languageControls.Any(pair => pair.requiresEdit && !pair.control.IsEnabled)) throw new InvalidOperationException("Language controls must be available in Edit mode, including empty sections.");
                summaryEditMode = false;
            }
        } finally { summaryEditMode = previousSummaryEdit; editMode = previousEdit; Section.SelectedIndex = previousSection; RefreshLanguagePanel(); }
    }
    private void VerifySegmentEditorAccessForSmokeTest()
    {
        var previousSection = Section.SelectedIndex; var previousEdit = editMode;
        try {
            foreach (var tab in new[] { 0, 2, 3 }) {
                Section.SelectedIndex = tab; editMode = false;
                if (CanRunDesktopCommand("add") != (tab is 0 or 2)) throw new InvalidOperationException("View mode must allow adding segments in Transcript and Descriptions.");
                var row = tab == 0 ? Model.Project?.Transcript.FirstOrDefault() : Model.Project?.Cues.FirstOrDefault();
                if (row != null && !SegmentMenu(row).Items.OfType<MenuFlyoutItem>().First(item => (string?)item.Tag == "segment-one-edit").IsEnabled) throw new InvalidOperationException("Context editing must be available in view mode.");
                if (editMode || CanRunDesktopCommand("segment-edit-selected")) throw new InvalidOperationException("Single-segment context editing must leave bulk Edit mode off.");
                editMode = true;
                if (CanRunDesktopCommand("add") != (tab is 0 or 2)) throw new InvalidOperationException("Edit mode must keep Add scoped to Transcript and Descriptions.");
                var singleActions = new[] { "segment-one-edit", "segment-one-play", "segment-one-skip", "segment-one-preview", "segment-one-preview-stop", "segment-one-synthesize", "segment-one-rewrite", "segment-one-import-audio", "segment-one-delete-audio", "segment-one-reanalyze" };
                Segments.SelectedItems.Clear(); UpdateDesktopMenuStates();
                if (singleActions.Any(CanRunDesktopCommand) || CanRunDesktopCommand("segment-delete-selected"))
                    throw new InvalidOperationException("Empty selection must disable row actions and Delete Selected.");
                var visibleRows = Segments.Items.OfType<Models.Segment>().ToArray();
                foreach (var target in visibleRows.GroupBy(segment => segment.IsDescription).Select(group => group.First())) {
                    Segments.SelectedItems.Clear(); Segments.SelectedItems.Add(target); UpdateDesktopMenuStates();
                    var contextIDs = SegmentMenu(target).Items.OfType<MenuFlyoutItem>().Select(item => (string)item.Tag).ToArray();
                    if (contextIDs.Any(id => !RowCommandVisible(id, target.IsDescription)) ||
                        target.IsDescription && contextIDs.Contains("segment-one-reanalyze") ||
                        !target.IsDescription && contextIDs.Any(id => id is "segment-one-rewrite" or "segment-one-synthesize" or "segment-one-import-audio"))
                        throw new InvalidOperationException("Row context menus must match the target row kind and tab.");
                    foreach (var action in singleActions) {
                        if (CanRunDesktopCommand(action) != CanRunRowCommand(action, target))
                            throw new InvalidOperationException("Edit and context actions must share availability for exactly one row.");
                    }
                    if (!CanRunDesktopCommand("segment-delete-selected")) throw new InvalidOperationException("One selected row must allow deletion.");
                }
                if (visibleRows.Length > 1) {
                    Segments.SelectedItems.Clear(); Segments.SelectedItems.Add(visibleRows[0]); Segments.SelectedItems.Add(visibleRows[1]); UpdateDesktopMenuStates();
                    if (singleActions.Any(CanRunDesktopCommand) || !CanRunDesktopCommand("segment-delete-selected"))
                        throw new InvalidOperationException("Multiple selected rows must disable individual actions but allow Delete Selected.");
                }
                Segments.SelectedItems.Clear();
                if (row != null)
                {
                    if (SegmentMenu(row).Items.OfType<MenuFlyoutItem>().Any(item => item.Text == "Copy and Translate…"))
                        throw new InvalidOperationException("Individual row context and More Actions menus must omit Copy and Translate.");
                    if (!editorMenus.Single(menu => menu.Title == "Edit").Items.OfType<MenuFlyoutItem>().Any(item => (string?)item.Tag == "copy-to"))
                        throw new InvalidOperationException("Edit must retain Copy and Translate.");
                }
            }
        } finally { editMode = previousEdit; Section.SelectedIndex = previousSection; RefreshLanguagePanel(); }
    }
    private void VerifyInspectorContentStatusForSmokeTest()
    {
        if (Model.Project is not { } project) throw new InvalidOperationException("Content status requires a project.");
        var previousSection = Section.SelectedIndex;
        try {
            foreach (var tab in new[] { 0, 1, 2, 3, 4 }) {
                Section.SelectedIndex = tab;
                UpdateADAudioReadiness(project);
                var view = tab == 4 ? project.OverviewProject : project;
                var expected = view.ContentStatus(tab, project.SelectedLanguage)!;
                if (tab == 4 && !OverviewVoice.Text.Contains("\n" + InspectorSpeechLanguage.Text + "\n")) throw new InvalidOperationException("Overview must use the same selected voice language and dialect as the inspector.");
                var ad = tab is 2 or 3 or 4;
                if (expected.Title.Contains("AD audio") != ad) throw new InvalidOperationException("Inspector status leaked another tab's content.");
                if (Microsoft.UI.Xaml.Automation.AutomationProperties.GetName(inspectorActiveAudioReadiness) != expected.AccessibleName)
                    throw new InvalidOperationException("Active inspector status does not match the displayed tab and language.");
                var titles = inspectorAudioReadiness.Children.OfType<StackPanel>().Select(row => row.Children.OfType<TextBlock>().Single().Text);
                if (!titles.SequenceEqual(view.ContentStatuses(tab).Select(status => status.Title))) throw new InvalidOperationException("Per-language status rows do not match the tab.");
            }
            foreach (var rows in new[] { project.Transcript, project.Cues })
                for (var i = 0; i < rows.Count; i++)
                    if (rows[i].SegmentNumber != i + 1 || rows[i].SegmentNumberText != $"#{i + 1}" || !rows[i].SegmentNumberLabel.Contains($"segment {i + 1}"))
                        throw new InvalidOperationException("Segment numbers must match the complete selected result before filtering or sorting.");
        } finally { Section.SelectedIndex = previousSection; UpdateADAudioReadiness(project); }
    }
    private async Task VerifyJumpToTimeForSmokeTestAsync()
    {
        var path = Path.Combine(Model.Project!.Folder, "project.json");
        var before = await File.ReadAllBytesAsync(path);
        Section.SelectedIndex = 0;
        StopProjectPlayback();
        await SeekAsync(0);
        await Task.Delay(500);
        UpdateDesktopMenuStates();
        var item = desktopMenuItems["jump-to-time"].Last();
        if (!item.IsEnabled || item.Text != "Jump to Time…" || item.KeyboardAcceleratorTextOverride != "Ctrl+Shift+J")
            throw new InvalidOperationException("Jump to Time menu/shortcut is missing.");
        async Task DialogJump(string value, bool cancel = false)
        {
            var opened = new TaskCompletionSource<ContentDialog>(TaskCreationOptions.RunContinuationsAsynchronously);
            void Capture(ContentDialog dialog, Window window)
            { if (dialog.Title?.ToString() == "Jump to Time") opened.TrySetResult(dialog); }
            Views.WindowedDialog.ReadyToShow += Capture;
            ContentDialog? active = null;
            Task? running = null;
            try
            {
                running = RunDesktopCommandAsync("jump-to-time");
                active = await opened.Task.WaitAsync(TimeSpan.FromSeconds(15));
                await Task.Delay(200);
                var content = (StackPanel)active.Content;
                var field = DialogControls(content).OfType<TextBox>().Single(box => Microsoft.UI.Xaml.Automation.AutomationProperties.GetAutomationId(box) == "jump-time-input");
                field.Text = "-1";
                await Task.Delay(80);
                if (active.IsPrimaryButtonEnabled) throw new InvalidOperationException("Negative time accepted.");
                field.Text = "99:00";
                await Task.Delay(80);
                if (active.IsPrimaryButtonEnabled) throw new InvalidOperationException("Out-of-range time accepted.");
                field.Text = value;
                await Task.Delay(80);
                if (!active.IsPrimaryButtonEnabled || content.ContextFlyout is not MenuFlyout menu || menu.Items.Count != 2)
                    throw new InvalidOperationException("Valid time or dialog menu rejected.");
                var button = FindButton(active, cancel ? "Cancel" : "Jump") ?? throw new InvalidOperationException("Missing dialog button.");
                var peer = new Microsoft.UI.Xaml.Automation.Peers.ButtonAutomationPeer(button);
                ((Microsoft.UI.Xaml.Automation.Provider.IInvokeProvider)peer.GetPattern(Microsoft.UI.Xaml.Automation.Peers.PatternInterface.Invoke)).Invoke();
                await running.WaitAsync(TimeSpan.FromSeconds(15));
            }
            finally { active?.Hide(); Views.WindowedDialog.ReadyToShow -= Capture; }
        }
        await DialogJump("00:00:02.500");
        await Task.Delay(400);
        if (IsVideoPlaying() || Math.Abs(Position - 2.5) > .2) throw new InvalidOperationException("Stopped jump started playback or missed time.");
        await DialogJump("4");
        await Task.Delay(300);
        if (IsVideoPlaying() || Math.Abs(Position - 4) > .2) throw new InvalidOperationException("Paused jump failed.");
        await DialogJump("1", true);
        if (IsVideoPlaying() || Math.Abs(Position - 4) > .2) throw new InvalidOperationException("Cancel moved paused video.");
        StartVideoPlayback();
        await Task.Delay(350);
        await DialogJump("1");
        await Task.Delay(400);
        if (!IsVideoPlaying() || Position < 1 || Position > 3) throw new InvalidOperationException("Playing jump did not continue from target.");
        await DialogJump("0", true);
        if (!IsVideoPlaying()) throw new InvalidOperationException("Cancel paused active playback.");
        StopProjectPlayback();
        if (!before.SequenceEqual(await File.ReadAllBytesAsync(path))) throw new InvalidOperationException("Jump rewrote project data.");
    }

    private async Task VerifyPrivateYouTubePlaybackForSmokeTestAsync()
    {
        var original = Model.Project ?? throw new InvalidOperationException("Playback fixture missing.");
        var path = Path.Combine(original.Folder, "project.json");
        var saved = await File.ReadAllBytesAsync(path);
        const string link = "https://www.youtube.com/watch?v=dQw4w9WgXcQ";
        try
        {
            var manifest = System.Text.Json.Nodes.JsonNode.Parse(saved)!.AsObject();
            manifest["sourceType"] = "youtube";
            manifest["source"] = "";
            manifest["youtubeURL"] = link;
            manifest["youtubePrivateChannelID"] = "fixture-channel";
            manifest["youtubeMetadata"] = new System.Text.Json.Nodes.JsonObject
            {
                ["channelID"] = "fixture-channel", ["visibility"] = "private"
            };
            await File.WriteAllTextAsync(path, manifest.ToJsonString());
            Model.Project = Models.StudioProject.Load(original.Folder);
            RefreshProject();
            var previousSection = Section.SelectedIndex;
            try
            {
                foreach (var section in new[] { 0, 1, 2 })
                {
                    Section.SelectedIndex = section;
                    RenderSection();
                    UpdateApiKeyUi();
                    if (!PrivateYouTubeAnalysisBar.IsOpen || PrivateYouTubeAnalysisBar.Message != YouTubeVideo.PrivateAnalysisMessage ||
                        ApiKeyWarningBar.IsOpen || AiWarningBar.IsOpen || GenerateCurrent.IsEnabled ||
                        AutomationProperties.GetHelpText(GenerateCurrent) != YouTubeVideo.PrivateAnalysisMessage)
                        throw new InvalidOperationException("Private YouTube analysis status differed on section " + section);
                }
            }
            finally { Section.SelectedIndex = previousSection; RenderSection(); }
            var inspectorWasOpen = InspectorSplitView.IsPaneOpen;
            var beforeRefresh = await File.ReadAllBytesAsync(path);
            var signInDialog = new TaskCompletionSource<ContentDialog>(TaskCreationOptions.RunContinuationsAsynchronously);
            void CaptureSignInDialog(ContentDialog dialog, Window window)
            {
                if (dialog.Title as string == "YouTube Sign-In Required") signInDialog.TrySetResult(dialog);
            }
            Views.WindowedDialog.ReadyToShow += CaptureSignInDialog;
            try
            {
                if (inspectorWasOpen) ToggleInspector_Click(this, new RoutedEventArgs());
                await RefreshYouTubeInspectorAsync(showPrivateSignInDialog: false).WaitAsync(TimeSpan.FromSeconds(10));
                if (signInDialog.Task.IsCompleted)
                    throw new InvalidOperationException("Automatic private refresh opened a sign-in dialog.");
                var refreshing = RunDesktopCommandAsync("youtube-refresh");
                var dialog = await signInDialog.Task.WaitAsync(TimeSpan.FromSeconds(10));
                var message = string.Join(" ", DialogControls((UIElement)dialog.Content).OfType<TextBlock>().Select(item => item.Text));
                var inline = YouTubeInspectorDetails.Children.OfType<TextBlock>().Any(
                    item => item.Text == YouTubeVideo.PrivateRefreshSignInMessage);
                var refresh = YouTubeInspectorDetails.Children.OfType<Button>().Single(
                    item => AutomationProperties.GetName(item) == "Refresh YouTube Details");
                if (!InspectorSplitView.IsPaneOpen || youtubeInspectorRefreshing || !refresh.IsEnabled || inline ||
                    message != YouTubeVideo.PrivateRefreshSignInMessage ||
                    !beforeRefresh.SequenceEqual(await File.ReadAllBytesAsync(path)))
                    throw new InvalidOperationException("Signed-out private refresh did not show the sign-in dialog and preserve saved details.");
                var okay = FindButton(dialog, "OK") ?? throw new InvalidOperationException("YouTube sign-in dialog omitted OK.");
                var peer = new Microsoft.UI.Xaml.Automation.Peers.ButtonAutomationPeer(okay);
                ((Microsoft.UI.Xaml.Automation.Provider.IInvokeProvider)peer.GetPattern(Microsoft.UI.Xaml.Automation.Peers.PatternInterface.Invoke)).Invoke();
                await refreshing.WaitAsync(TimeSpan.FromSeconds(10));
            }
            finally
            {
                Views.WindowedDialog.ReadyToShow -= CaptureSignInDialog;
                if (signInDialog.Task.IsCompletedSuccessfully) signInDialog.Task.Result.Hide();
                if (InspectorSplitView.IsPaneOpen != inspectorWasOpen) ToggleInspector_Click(this, new RoutedEventArgs());
            }
            async Task CheckAttachmentDeletionChoices(bool audioAvailable, bool videoAvailable)
            {
                var opened = new TaskCompletionSource<ContentDialog>(TaskCreationOptions.RunContinuationsAsynchronously);
                void Capture(ContentDialog dialog, Window window)
                {
                    if (dialog.Title as string == "Delete Content by Language") opened.TrySetResult(dialog);
                }
                Views.WindowedDialog.ReadyToShow += Capture;
                var showing = ShowLanguageDeletionAsync();
                ContentDialog? active = null;
                try
                {
                    active = await opened.Task.WaitAsync(TimeSpan.FromSeconds(15));
                    var boxes = DialogControls((UIElement)active.Content).OfType<CheckBox>().ToArray();
                    foreach (var (id, label, available) in new[]
                    {
                        ("delete-original-audio", "Original Audio File", audioAvailable),
                        ("delete-local-playback-video", "Local Playback Video", videoAvailable)
                    })
                    {
                        var box = boxes.Single(item => Microsoft.UI.Xaml.Automation.AutomationProperties.GetAutomationId(item) == id);
                        if (box.Content as string != label || box.IsEnabled != available || box.IsChecked == true ||
                            box.ContextFlyout is not MenuFlyout menu || menu.Items.Single() is not ToggleMenuFlyoutItem option ||
                            option.Text != label || option.IsEnabled != available || option.Icon == null)
                            throw new InvalidOperationException("The deletion checkbox or its context action is wrong for " + label);
                    }
                }
                finally
                {
                    Views.WindowedDialog.ReadyToShow -= Capture;
                    active?.Hide();
                }
                await showing.WaitAsync(TimeSpan.FromSeconds(15));
            }
            void CheckSettingsPlaybackControls(Models.StudioProject sample, string expectedTitle, bool canChoose, bool canRemove)
            {
                var controls = BuildYouTubePlaybackControls(sample, _ => Task.CompletedTask, inProjectSettings: true);
                var choose = controls.Children.OfType<Button>().Single();
                var fileRow = controls.Children.OfType<Grid>().SingleOrDefault();
                var trash = fileRow?.Children.OfType<Button>().SingleOrDefault();
                var items = ((MenuFlyout)controls.ContextFlyout).Items.OfType<MenuFlyoutItem>().ToArray();
                if (AutomationProperties.GetName(choose) != expectedTitle || choose.IsEnabled != canChoose ||
                    (trash != null) != canRemove || items.Length != (canRemove ? 2 : 1) ||
                    items[0].Text != expectedTitle || items[0].IsEnabled != canChoose || items.Any(item => item.Icon == null))
                    throw new InvalidOperationException("Project Settings playback controls and context menu disagree with the attachment state.");
                if (canChoose && !canRemove && fileRow != null || (AutomationProperties.GetHelpText(choose) == "Imported") != canRemove)
                    throw new InvalidOperationException("Settings playback showed an empty filename or the wrong imported status.");
                if (trash != null && (AutomationProperties.GetName(trash) != "Remove Local Playback Video…" ||
                    !trash.IsEnabled || items[1].Text != AutomationProperties.GetName(trash) || !items[1].IsEnabled))
                    throw new InvalidOperationException("Project Settings did not expose the named trash action and context command.");
            }
            CheckSettingsPlaybackControls(Model.Project!, "Choose Local Playback Video…", canChoose: true, canRemove: false);
            await CheckAttachmentDeletionChoices(audioAvailable: false, videoAvailable: false);
            UpdateDesktopMenuStates();
            if (ChoosePrivatePlaybackVideo.Visibility != Visibility.Visible ||
                PrivateYouTubePlaybackPanel.Visibility != Visibility.Visible || YouTubePlayer.Visibility != Visibility.Collapsed ||
                !CanRunDesktopCommand("choose-playback-video") || CanRunDesktopCommand("remove-playback-video"))
                throw new InvalidOperationException("Private YouTube preview did not offer the playback chooser.");
            if (new Views.ExportDialog(Model, this).VideoCategoryForSmokeTest)
                throw new InvalidOperationException("A private project without a playback copy exposed Video in Export.");
            foreach (var transport in new[] { "play", "fullscreen", "back", "forward", "jump-to-time" })
                if (CanRunDesktopCommand(transport)) throw new InvalidOperationException("Unavailable video enabled " + transport);
            var account = new YouTubeAccount("playback-test-account", "", "Test", "", "", "fixture-channel", "Test Channel", "", "");
            bool ShowsStudioInInspector() => YouTubeInspectorDetails.Children.OfType<StackPanel>()
                .SelectMany(row => row.Children.OfType<Button>()).Any(button => button.Content as string == "Open in YouTube Studio");
            void CheckSettingsStudioLink(bool connected)
            {
                var links = BuildYouTubeProjectLinks(Model.Project!);
                var expected = connected ? new[] { "Open in YouTube", "Open in YouTube Studio" } : new[] { "Open in YouTube" };
                var menu = (MenuFlyout)links.ContextFlyout;
                if (!links.Children.OfType<Button>().Select(button => button.Content as string).SequenceEqual(expected) ||
                    !menu.Items.OfType<MenuFlyoutItem>().Select(item => item.Text).SequenceEqual(expected) ||
                    menu.Items.OfType<MenuFlyoutItem>().Any(item => item.Icon == null))
                    throw new InvalidOperationException("Project Settings must expose the Studio link and menu only for a matching channel.");
            }
            var studioMenu = desktopMenuItems["youtube-project-open-studio"].Last();
            CheckSettingsStudioLink(connected: false);
            if (studioMenu.Visibility != Visibility.Visible || studioMenu.IsEnabled || ShowsStudioInInspector())
                throw new InvalidOperationException("The unmatched Studio action was not disabled in the menu and hidden in the inspector.");
            YouTubeService.Shared.Accounts.Add(account);
            try
            {
                ApplySettingsChanges(new[] { "youtubeAccounts" });
                CheckSettingsStudioLink(connected: true);
                if (!studioMenu.IsEnabled || !ShowsStudioInInspector())
                    throw new InvalidOperationException("Studio was unavailable for a confirmed channel match.");
            }
            finally
            {
                YouTubeService.Shared.Accounts.Remove(account);
                ApplySettingsChanges(new[] { "youtubeAccounts" });
            }
            if (studioMenu.IsEnabled || ShowsStudioInInspector())
                throw new InvalidOperationException("Disconnecting a YouTube account left the Studio action available.");
            CheckSettingsStudioLink(connected: false);
            var playerMenu = desktopMenuGroups.Last(group => group.Name == "Video Player").Item;
            var choices = playerMenu.Items.OfType<ToggleMenuFlyoutItem>().ToArray();
            var chooseCopy = playerMenu.Items.OfType<MenuFlyoutItem>().Single(item => Equals(item.Tag, "choose-playback-video"));
            var removeCopy = playerMenu.Items.OfType<MenuFlyoutItem>().Single(item => Equals(item.Tag, "remove-playback-video"));
            if (playerMenu.Visibility != Visibility.Visible || !playerMenu.IsEnabled || !chooseCopy.IsEnabled ||
                removeCopy.Visibility != Visibility.Collapsed || choices.Any(choice => choice.IsEnabled || choice.IsChecked))
                throw new InvalidOperationException("Private YouTube must offer the file chooser with both player choices disabled before attachment.");
            var youtubeProject = Model.Project!;
            youtubeProject.OriginalAudio = new Models.OriginalAudioAsset { Path = "imports/original.wav", SourceName = "original.wav" };
            try
            {
                await CheckAttachmentDeletionChoices(audioAvailable: true, videoAvailable: false);
                CheckSettingsPlaybackControls(youtubeProject, "Choose Local Playback Video…", canChoose: false, canRemove: false);
                UpdateInspector(youtubeProject);
                UpdatePlaybackControls();
                UpdateDesktopMenuStates();
                if (!playerMenu.IsEnabled || chooseCopy.IsEnabled || ChoosePrivatePlaybackVideo.IsEnabled ||
                    !CanRunDesktopCommand("remove-original-audio"))
                    throw new InvalidOperationException("A saved original audio selection must disable video import while allowing audio removal.");
            }
            finally
            {
                youtubeProject.OriginalAudio = null;
                UpdateInspector(youtubeProject);
                UpdatePlaybackControls();
                UpdateDesktopMenuStates();
            }
            var import = new Views.ImportAssetsDialog(this, Model.Project!);
            var importReady = new TaskCompletionSource<Window>(TaskCreationOptions.RunContinuationsAsynchronously);
            void CaptureImport(ContentDialog dialog, Window window)
            {
                if (ReferenceEquals(import, dialog)) importReady.TrySetResult(window);
            }
            Views.WindowedDialog.ReadyToShow += CaptureImport;
            var showingImport = ShowDialogAsync(import, 800, 760);
            try
            {
                var companion = await importReady.Task.WaitAsync(TimeSpan.FromSeconds(15));
                if (!ReferenceEquals(companion, import.CompanionWindow))
                    throw new InvalidOperationException("The playback picker must use the active import window.");
                import.VerifyPlaybackSelectionForSmokeTest();
            }
            finally
            {
                Views.WindowedDialog.ReadyToShow -= CaptureImport;
                import.Hide();
                await showingImport.WaitAsync(TimeSpan.FromSeconds(15));
                import.CleanupYouTubeCaptions();
            }
            void AssertPlayerChoices(Models.StudioProject sample, bool hasCopy)
            {
                var channel = sample.YouTubePrivateChannelId;
                var metadata = sample.YouTubeMetadata;
                var originalAsset = sample.YouTubePlaybackVideo;
                try
                {
                    sample.YouTubePrivateChannelId = null;
                    foreach (var privacy in new[] { "public", "unlisted" })
                    {
                        sample.YouTubeMetadata = JsonSerializer.SerializeToElement(new { visibility = privacy });
                        UpdateDesktopMenuStates();
                        var web = (ToggleMenuFlyoutItem)desktopMenuItems["use-youtube-player"].Last();
                        var local = (ToggleMenuFlyoutItem)desktopMenuItems["use-local-video-player"].Last();
                        if (playerMenu.Visibility != Visibility.Visible || !playerMenu.IsEnabled ||
                            !CanRunDesktopCommand("choose-playback-video") || web.IsEnabled != hasCopy || local.IsEnabled != hasCopy)
                            throw new InvalidOperationException($"Player availability is wrong for {privacy}, copy={hasCopy}: " +
                                $"scope={playerMenu.Visibility}, submenu enabled={playerMenu.IsEnabled}, web enabled={web.IsEnabled}, " +
                                $"local enabled={local.IsEnabled}, choose enabled={CanRunDesktopCommand("choose-playback-video")}, " +
                                $"local command enabled={CanRunDesktopCommand("use-local-video-player")}, same project={ReferenceEquals(sample, Model.Project)}.");
                        if (hasCopy && originalAsset != null)
                        {
                            if (!local.IsChecked || web.IsChecked) throw new InvalidOperationException("Local playback must be the default after copying.");
                            sample.YouTubePlaybackVideo = originalAsset with { UseYouTubePlayer = true };
                            UpdateDesktopMenuStates();
                            if (!web.IsChecked || local.IsChecked || !sample.HasVideoExportSource)
                                throw new InvalidOperationException("YouTube playback did not keep the local video available to export.");
                            sample.YouTubePlaybackVideo = originalAsset;
                        }
                    }
                }
                finally
                {
                    sample.YouTubePrivateChannelId = channel;
                    sample.YouTubeMetadata = metadata;
                    sample.YouTubePlaybackVideo = originalAsset;
                    UpdateDesktopMenuStates();
                }
            }
            AssertPlayerChoices(Model.Project!, hasCopy: false);
            var before = original.Resolve(original.Source);
            var beforeDurationCheck = await File.ReadAllTextAsync(path);
            try
            {
                manifest["duration"] = 3723.456;
                await File.WriteAllTextAsync(path, manifest.ToJsonString());
                Model.Project = Models.StudioProject.Load(original.Folder);
                string token;
                var unchanged = await File.ReadAllBytesAsync(path);
                try
                {
                    await Model.RunAsync("attach-youtube-playback", new[] { "--video", before }, throwOnProcessingError: true);
                    throw new InvalidOperationException("The playback import accepted the mismatched test video without confirmation.");
                }
                catch (ProcessingException ex)
                {
                    if (!ex.Message.Contains("YouTube duration: 1 hour, 2 minutes, 3 seconds, 456 milliseconds\nLocal video duration:") ||
                        ex.ImportIssue?.Token is not { } accepted)
                        throw new InvalidOperationException("The playback warning did not preserve the readable durations and override.", ex);
                    token = accepted;
                }
                if (!unchanged.SequenceEqual(await File.ReadAllBytesAsync(path)))
                    throw new InvalidOperationException("The playback warning changed the saved project.");
                await Model.RunAsync("attach-youtube-playback", new[] { "--video", before, "--accept-import-warning", token }, throwOnProcessingError: true);
                if (Model.Project?.LocalYouTubePlaybackPath == null || Model.Project?.YouTubePlaybackVideo?.TimingAcceptedFor != 3723.456)
                    throw new InvalidOperationException("The accepted local video was not available to the player.");
                await Model.RunAsync("remove-youtube-playback");
            }
            finally
            {
                await File.WriteAllTextAsync(path, beforeDurationCheck);
                Model.Project = Models.StudioProject.Load(original.Folder);
                RefreshProject();
            }
            var audioWarning = new Models.OriginalAudioInspection { AudioDuration = 3723.456, VideoDuration = 3722, Difference = 1.456 }.Warning;
            if (!audioWarning.Contains("Audio duration: 1 hour, 2 minutes, 3 seconds, 456 milliseconds\nVideo duration: 1 hour, 2 minutes, 2 seconds\nDifference: 1 second, 456 milliseconds"))
                throw new InvalidOperationException("The original audio confirmation did not show readable durations.");
            var progressSeen = false;
            await Model.RunAsync("attach-youtube-playback", new[] { "--video", before }, throwOnProcessingError: true,
                observeProgress: update => progressSeen |= update.Data.TryGetProperty("contentTransferProgress", out _));
            if (!progressSeen) throw new InvalidOperationException("No playback copy progress reached the editor.");
            var project = Model.Project!;
            var asset = project.YouTubePlaybackVideo ?? throw new InvalidOperationException("Playback clip did not persist.");
            CheckSettingsPlaybackControls(project, "Replace Local Playback Video…", canChoose: true, canRemove: true);
            await CheckAttachmentDeletionChoices(audioAvailable: false, videoAvailable: true);
            if (project.YoutubeURL != link || project.Duration != original.Duration ||
                project.LocalYouTubePlaybackPath is not { } copy || copy == before ||
                project.ThumbnailUri?.LocalPath != project.Resolve(asset.Thumbnail) ||
                Player.Visibility != Visibility.Visible || YouTubePlayer.Visibility != Visibility.Collapsed)
                throw new InvalidOperationException("Private YouTube copy and thumbnail did not reach the native viewer.");
            if (!new Views.ExportDialog(Model, this).VideoCategoryForSmokeTest)
                throw new InvalidOperationException("The video export category remained hidden after choosing a private playback file.");
            if (!CanRunDesktopCommand("play") || !CanRunDesktopCommand("jump-to-time") ||
                CanRunDesktopCommand("import-original-audio") || AudioSourcePanel.Visibility == Visibility.Visible)
                throw new InvalidOperationException("A playable copy did not enable the transport and hide standalone audio.");
            UpdateDesktopMenuStates();
            var trash = YouTubeInspectorDetails.Children.OfType<StackPanel>().SelectMany(panel => panel.Children.OfType<Grid>())
                .SelectMany(row => row.Children.OfType<Button>()).SingleOrDefault(button =>
                    Microsoft.UI.Xaml.Automation.AutomationProperties.GetName(button) == "Remove Local Playback Video…");
            if (removeCopy.Visibility != Visibility.Visible || !removeCopy.IsEnabled ||
                trash?.Content is not FontIcon { Glyph: "\uE74D" } || choices[0].IsEnabled || choices[0].IsChecked ||
                choices[1].IsEnabled || !choices[1].IsChecked)
                throw new InvalidOperationException("The private playback copy did not expose the trash button and matching menu command.");
            AssertPlayerChoices(project, hasCopy: true);
            await VerifySettingsImportedAssetsForSmokeTestAsync(asset.SourceName);
            await SeekAsync(2.5);
            await Task.Delay(500);
            if (Math.Abs(Position - 2.5) > .2 || Math.Abs(Playhead.Value - 2.5) > .2)
                throw new InvalidOperationException("Private playback position diverged from the project timeline.");
            var request = new { resultIDs = Array.Empty<string>(), components = new[] { "youtubePlaybackVideo" },
                exportPaths = Array.Empty<string>(), playbackVideoPath = asset.Path };
            await ChangeYouTubePlaybackCopyAsync(() => RunResultRequestAsync("delete-results", request));
            if (Model.Project?.YouTubePlaybackVideo != null || File.Exists(copy) || !File.Exists(before) ||
                PrivateYouTubePlaybackPanel.Visibility != Visibility.Visible)
                throw new InvalidOperationException("Deleting the attachment did not restore the chooser or retain the original file.");
        }
        finally
        {
            await File.WriteAllBytesAsync(path, saved);
            Model.Project = Models.StudioProject.Load(original.Folder);
            RefreshProject();
        }
    }

    private async Task VerifySettingsImportedAssetsForSmokeTestAsync(string playbackName)
    {
        static System.Collections.Generic.IEnumerable<DependencyObject> Elements(DependencyObject root)
        {
            yield return root;
            for (var index = 0; index < Microsoft.UI.Xaml.Media.VisualTreeHelper.GetChildrenCount(root); index++)
                foreach (var element in Elements(Microsoft.UI.Xaml.Media.VisualTreeHelper.GetChild(root, index)))
                    yield return element;
        }
        var input = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + "-settings-summary.txt");
        try
        {
            await File.WriteAllTextAsync(input, "A saved summary imported for the Settings playback test.");
            await Model.RunAsync("import-summary", new[] { "--input", input, "--replace-existing" }, throwOnProcessingError: true);
            var imported = Model.Project!.SavedResults.Single(result => result.ImportedSourceName == Path.GetFileName(input));
            var settings = new Views.SettingsDialog(Model, this, 1, Model.Project);
            var showing = settings.ShowAsync();
            var opened = new TaskCompletionSource<ContentDialog>(TaskCreationOptions.RunContinuationsAsynchronously);
            void Capture(ContentDialog dialog, Window window)
            {
                if (dialog.Title as string == "Delete Content by Language") opened.TrySetResult(dialog);
            }
            Views.WindowedDialog.ReadyToShow += Capture;
            ContentDialog? active = null;
            try
            {
                await Task.Delay(250);
                Expander Group(string header) => Elements((DependencyObject)settings.Content).OfType<Expander>()
                    .Single(expander => expander.Header as string == header);
                DependencyObject Assets() => (DependencyObject)Group("Imported assets").Content;
                bool ShowsFile(string filename) => Elements(Assets()).OfType<TextBlock>().Any(text => text.Text == filename);
                Button SummaryImport() => Elements((DependencyObject)Group("Import into this project").Content).OfType<Button>()
                    .Single(item => AutomationProperties.GetName(item) == "Import Summary…");
                Group("Imported assets").StartBringIntoView(new BringIntoViewOptions { AnimationDesired = false });
                await Task.Delay(200);
                if (!ShowsFile(playbackName) || !ShowsFile(Path.GetFileName(input)))
                    throw new InvalidOperationException("Project Settings did not show the saved playback and imported summary filenames. Rows: " +
                        string.Join(", ", Elements(Assets()).OfType<TextBlock>().Select(text => text.Text)));
                if (imported.State == "draft" && !Elements(Assets()).OfType<TextBlock>().Any(text => text.Text.EndsWith("Review needed", StringComparison.Ordinal)))
                    throw new InvalidOperationException("The completed replacement import must show its review status.");
                var summaryImport = SummaryImport();
                if (AutomationProperties.GetHelpText(summaryImport) != "Imported" ||
                    summaryImport.Content is not StackPanel status || !status.Children.OfType<FontIcon>().Any(icon => icon.Glyph == "\uE73E"))
                    throw new InvalidOperationException("The imported summary did not get a named checkmark.");
                var button = Elements(Assets()).OfType<Button>().Single(item => AutomationProperties.GetName(item) == "Choose What to Delete…");
                if (!button.IsEnabled || button.ContextFlyout is not MenuFlyout menu ||
                    menu.Items.Single() is not MenuFlyoutItem { Text: "Choose What to Delete…", IsEnabled: true, Icon: not null })
                    throw new InvalidOperationException("Project Settings did not expose the enabled deletion command and menu action.");
                void Invoke(Button action)
                {
                    var peer = new Microsoft.UI.Xaml.Automation.Peers.ButtonAutomationPeer(action);
                    ((Microsoft.UI.Xaml.Automation.Provider.IInvokeProvider)peer.GetPattern(Microsoft.UI.Xaml.Automation.Peers.PatternInterface.Invoke)).Invoke();
                }
                Invoke(button);
                active = await opened.Task.WaitAsync(TimeSpan.FromSeconds(15));
                Invoke(FindButton(active, "Cancel") ?? throw new InvalidOperationException("The Settings deletion dialog has no Cancel action."));
                await Task.Delay(250);
                if (!ShowsFile(Path.GetFileName(input))) throw new InvalidOperationException("Cancel removed the imported summary.");
                await RunResultRequestAsync("delete-results",
                    new { resultIDs = new[] { imported.Id }, components = new[] { "summary" }, exportPaths = Array.Empty<string>() });
                await Task.Delay(200);
                if (ShowsFile(Path.GetFileName(input)) || !ShowsFile(playbackName) ||
                    AutomationProperties.GetHelpText(SummaryImport()) == "Imported")
                    throw new InvalidOperationException("Project Settings did not refresh after deleting just the imported summary.");
            }
            finally
            {
                Views.WindowedDialog.ReadyToShow -= Capture;
                active?.Hide();
                settings.Close();
                await showing;
            }
        }
        finally { File.Delete(input); }
    }

    private void VerifyContextualMenusForSmokeTest()
    {
        if (Model.Project is not { } project) throw new InvalidOperationException("Contextual menu smoke fixture requires a project.");
        var originalSection = Section.SelectedIndex;
        var originalSource = project.SourceType;
        var originalEdit = editMode;
        var originalSummaryEdit = summaryEditMode;
        var originalUrl = project.YoutubeURL;
        var scopes = new[] {
            (Id: "transcribe", Tabs: new[] { 0 }), (Id: "generate-summary", Tabs: new[] { 1 }),
            (Id: "generate-ad-cues", Tabs: new[] { 2 }), (Id: "import-transcript", Tabs: new[] { 0 }),
            (Id: "import-summary", Tabs: new[] { 1 }), (Id: "import-descriptions", Tabs: new[] { 2 }),
            (Id: "synthesize-tts", Tabs: new[] { 2, 3, 4 }), (Id: "review-narration-issues", Tabs: new[] { 2, 3, 4 }),
            (Id: "narration", Tabs: new[] { 2, 3 }), (Id: "add", Tabs: new[] { 0, 2 }),
            (Id: "undo", Tabs: new[] { 0, 2 }), (Id: "import-ad-audio", Tabs: new[] { 2, 3 }),
            (Id: "summary-play", Tabs: new[] { 1 }), (Id: "caption-ad-transcript", Tabs: new[] { 2, 3 }),
            (Id: "segment-select-all", Tabs: new[] { 0, 2, 3 }), (Id: "segment-delete-selected", Tabs: new[] { 0, 2, 3 })
        };
        try
        {
            foreach (var youtube in new[] { false, true })
            {
                project.SourceType = youtube ? "youtube" : "local";
                project.YoutubeURL = youtube ? "https://www.youtube.com/watch?v=aqz-KE-bpKQ" : null;
                for (var tab = 0; tab < 5; tab++)
                {
                    summaryEditMode = false;
                    Section.SelectedIndex = tab;
                    foreach (var editing in new[] { false, true })
                    {
                    editMode = editing; summaryEditMode = tab == 1 && editing;
                    UpdateDesktopMenuStates();
                    foreach (var (id, tabs) in scopes)
                    {
                        var editOnly = id is "undo" or "redo" or "reanalyze-selection" or "segment-select-all" or "segment-deselect-all" or "segment-edit-selected" or "segment-delete-selected";
                        var summaryViewOnly = tab == 1 && (id.StartsWith("summary-", StringComparison.Ordinal) || id is "generate-summary" or "import-summary" or "delete-current");
                        var visible = tabs.Contains(tab) && (!editOnly || editing) && (!summaryViewOnly || !editing);
                        var item = desktopMenuItems[id].Last();
                        if ((item.Visibility == Visibility.Visible) != visible || (!visible && CanRunDesktopCommand(id)))
                            throw new InvalidOperationException($"Menu/accelerator escaped its tab: {id}, tab={tab}, YouTube={youtube}.");
                    }
                    foreach (var id in new[] { "youtube-details", "youtube-refresh", "youtube-captions", "import-original-audio" })
                    {
                        var visible = youtube && (id != "youtube-captions" || tab == 0);
                        if ((desktopMenuItems[id].Last().Visibility == Visibility.Visible) != visible || (!visible && CanRunDesktopCommand(id)))
                            throw new InvalidOperationException($"YouTube command escaped its source/tab: {id}.");
                    }
                    foreach (var (name, group) in desktopMenuGroups)
                    {
                        bool? visible = name switch {
                            "Summary Reading" => tab == 1 && !editing, "Segments" => tab is 0 or 2 or 3,
                            "Narration" or "Narration Audio" => tab is 2 or 3 or 4,
                            "AD Segment Generation" => tab == 2, "YouTube" => youtube, _ => null
                        };
                        if (visible is { } expected && (group.Visibility == Visibility.Visible) != expected)
                            throw new InvalidOperationException($"Unrelated submenu visible: {name}, tab={tab}.");
                    }
                    if ((editorMenus.Single(menu => menu.Title == "YouTube").Visibility == Visibility.Visible) != youtube)
                        throw new InvalidOperationException("Top-level YouTube menu does not follow the source type.");
                    }
                }
            }
        }
        finally
        {
            project.SourceType = originalSource; project.YoutubeURL = originalUrl;
            summaryEditMode = false;
            Section.SelectedIndex = originalSection;
            editMode = originalEdit; summaryEditMode = originalSummaryEdit; UpdateDesktopMenuStates();
        }
    }

    private void VerifyProjectMenuEntriesForSmokeTest()
    {
        VerifyInspectorContentStatusForSmokeTest();
        VerifySegmentEditorAccessForSmokeTest();
        VerifyLanguageInspectorForSmokeTest();
        if (DesktopCommandTitle("language-results") != "Language and Saved Results…" || !CanRunDesktopCommand("language-results"))
            throw new InvalidOperationException("Language and saved results must be available offline in the active editor.");
        var projectMenu = editorMenus.Single(menu => menu.Title == "Project");
        var tail = projectMenu.Items.TakeLast(3).Select(item => item is MenuFlyoutSeparator ? "-" : (string)((MenuFlyoutItem)item).Tag);
        if (!tail.SequenceEqual(new[] { "-", "rename-project", "trash-project" }))
            throw new InvalidOperationException("Project management actions have the wrong order or separator.");
        foreach (var (id, title) in new[] { ("rename-project", "Rename Project…"), ("trash-project", "Move Project to Recycle Bin…") })
            if (desktopMenuItems[id].Last() is not { IsEnabled: true, Icon: not null } item || item.Text != title || !CanRunDesktopCommand(id))
                throw new InvalidOperationException("Project management must be available offline in the active editor: " + id);
        if (desktopMenuItems["import-assets"].Last() is not { IsEnabled: true, Icon: not null } importItem || importItem.Text != "Import Existing Assets…" || !CanRunDesktopCommand("import-assets"))
            throw new InvalidOperationException("Import Existing Assets must be available offline in the active editor.");
        if (desktopMenuItems["import-ad-audio"].Last() is not { IsEnabled: true, Icon: not null } adAudio || adAudio.Text != "Import AD Audio…" || !CanRunDesktopCommand("import-ad-audio"))
            throw new InvalidOperationException("Import AD Audio must be available offline in the active editor.");
        var youtube = Model.Project?.IsYouTube == true;
        var originalAudio = desktopMenuItems["import-original-audio"].Last();
        if (originalAudio.Icon == null || originalAudio.Text != "Import Original Audio…" || originalAudio.IsEnabled != youtube || CanRunDesktopCommand("import-original-audio") != youtube)
            throw new InvalidOperationException("Import Original Audio must share the YouTube-only command guard.");
    }
    private async Task VerifyTimingValidationNavigationForSmokeTestAsync()
    {
        if (Model.Project is not { } project || project.Transcript.FirstOrDefault() is not { } transcript || project.Cues.FirstOrDefault() is not { } cue)
            throw new InvalidOperationException("Timing validation smoke fixture needs transcript and description rows.");
        var manifestPath = Path.Combine(project.Folder, "project.json");
        var originalManifest = await File.ReadAllBytesAsync(manifestPath);
        var originalSection = Section.SelectedIndex;
        Section.SelectedIndex = 0;
        var first = new TaskCompletionSource<ContentDialog>(TaskCreationOptions.RunContinuationsAsynchronously);
        var restored = new TaskCompletionSource<ContentDialog>(TaskCreationOptions.RunContinuationsAsynchronously);
        var count = 0;
        void Capture(ContentDialog dialog, Window window)
        {
            if (dialog.Title?.ToString() != "Edit Transcript Segment") return;
            if (++count == 1) first.TrySetResult(dialog); else restored.TrySetResult(dialog);
        }
        void Invoke(Button button)
        {
            var peer = new Microsoft.UI.Xaml.Automation.Peers.ButtonAutomationPeer(button);
            ((Microsoft.UI.Xaml.Automation.Provider.IInvokeProvider)peer.GetPattern(Microsoft.UI.Xaml.Automation.Peers.PatternInterface.Invoke)).Invoke();
        }
        Views.WindowedDialog.ReadyToShow += Capture;
        ContentDialog? active = null;
        Task? editing = null;
        try
        {
            editing = EditSegment(transcript);
            active = await first.Task.WaitAsync(TimeSpan.FromSeconds(15));
            var layout = (Grid)active.Content;
            var controls = (StackPanel)((ScrollViewer)layout.Children[0]).Content;
            var timing = controls.Children.OfType<Views.SegmentTimingEditor>().Single();
            var show = layout.Children.OfType<Button>().Single();
            var texts = new[] { DialogControls(controls).OfType<TextBox>().Single(box => box.Header as string == "Transcript text"), DialogControls(controls).OfType<TextBox>().Single(box => box.Header as string == "Optional hint for Gemini") };
            timing.StartPicker.Value = Math.Max(0, cue.Start);
            timing.EndPicker.Value = Math.Min(project.Duration, cue.End);
            texts[0].Text = "Unsaved validation smoke draft";
            texts[1].Text = "Preserve this Gemini hint";
            var end = timing.EndPicker.Value;
            var errorRows = ((StackPanel)DialogControls(controls).OfType<Border>().Single(border => Microsoft.UI.Xaml.Automation.AutomationProperties.GetAutomationId(border) == "segment-timing-validation").Child)
                .Children.OfType<StackPanel>().Last().Children.OfType<TextBlock>();
            if (!errorRows.Any(row => row.Text.Contains($"audio description segment #{cue.SegmentNumber} ")))
                throw new InvalidOperationException("Timing validation did not identify the description's visible segment number.");
            if (show.Visibility != Visibility.Visible || active.IsPrimaryButtonEnabled || show.ContextFlyout is not MenuFlyout menu ||
                menu.Items.OfType<MenuFlyoutItem>().Single().Tag?.ToString() != "show-validation-issue-menu" ||
                Microsoft.UI.Xaml.Automation.AutomationProperties.GetAutomationId(show) != "show-validation-issue")
                throw new InvalidOperationException("Validation footer/menu guards are missing.");
            Invoke(show);
            var reopened = await Task.WhenAny(restored.Task, editing!);
            if (ReferenceEquals(reopened, editing)) { await editing!; throw new InvalidOperationException("The segment editor ended before reopening its validation draft."); }
            active = await restored.Task.WaitAsync(TimeSpan.FromSeconds(30));
            if (texts[0].Text != "Unsaved validation smoke draft" || texts[1].Text != "Preserve this Gemini hint" ||
                timing.EndPicker.Value != end || Section.SelectedIndex != 0 || validationShowcaseActive || validationHighlightKey != null)
                throw new InvalidOperationException("Validation showcase lost draft state or did not return to Transcript.");
            active.Hide();
            await editing.WaitAsync(TimeSpan.FromSeconds(15));
            if (!originalManifest.SequenceEqual(await File.ReadAllBytesAsync(manifestPath)))
                throw new InvalidOperationException("Showing validation issues or Cancel changed the project.");
        }
        finally
        {
            active?.Hide();
            Views.WindowedDialog.ReadyToShow -= Capture;
            Section.SelectedIndex = originalSection;
        }
    }

    private async Task VerifyProjectMenuActionsForSmokeTestAsync()
    {
        var originalName = Model.Project!.Name;
        var folder = Model.Project.Folder;
        foreach (var (id, name) in new (string Id, string? Name)[]
            { ("rename-project", "Renamed project menu fixture"), ("rename-project", originalName), ("trash-project", null) })
        {
            var presented = new TaskCompletionSource<ContentDialog>(TaskCreationOptions.RunContinuationsAsynchronously);
            void Capture(ContentDialog dialog, Window window)
            {
                var title = id == "rename-project" ? "Rename project" : "Move project to Recycle Bin";
                if (dialog.Title?.ToString() == title) presented.TrySetResult(dialog);
            }
            Views.WindowedDialog.ReadyToShow += Capture;
            try
            {
                var command = RunDesktopCommandAsync(id);
                var first = await Task.WhenAny(command, presented.Task);
                if (first == command) { await command; throw new InvalidOperationException("Project menu action did not open its dialog: " + id); }
                var dialog = await presented.Task.WaitAsync(TimeSpan.FromSeconds(10));
                if (name is not null) DialogControls((UIElement)dialog.Content).OfType<TextBox>().Single(box => box.Header as string == "Project name").Text = name;
                var button = FindButton(dialog, name is null ? "Cancel" : "Save")
                    ?? throw new InvalidOperationException("Missing project dialog action.");
                var peer = new Microsoft.UI.Xaml.Automation.Peers.ButtonAutomationPeer(button);
                ((Microsoft.UI.Xaml.Automation.Provider.IInvokeProvider)peer.GetPattern(Microsoft.UI.Xaml.Automation.Peers.PatternInterface.Invoke)).Invoke();
                await command.WaitAsync(TimeSpan.FromSeconds(30));
                if (desktopEditorVisible && name != null && (ProjectTitle.Text != name || Title != name + " — Vidi Studio"))
                    throw new InvalidOperationException("Renaming did not update the visible project heading and window title.");
                if (Model.Project?.Name != (name ?? originalName) || Model.Project.Folder != folder || !Directory.Exists(folder))
                    throw new InvalidOperationException("Project rename or canceled Recycle Bin action affected the wrong project.");
            }
            finally { Views.WindowedDialog.ReadyToShow -= Capture; }
        }
    }
    private async Task VerifyDialogThemesForSmokeTestAsync(ContentDialog dialog, string report, string imageName)
    {
        var originalTheme = App.ActiveTheme ?? Model.Settings.Get("theme", "system");
        if (dialogPresentationVerifier is { } presentation) await presentation.WaitUntilReadyAsync(dialog);
        dialogPresentationVerifier?.Verify(dialog);
        VerifySingleDialogSurfaceForSmokeTest(dialog);
        if (dialog.XamlRoot?.Content is FrameworkElement initialSurface)
            await CaptureSmokeImage(initialSurface, Path.Combine(Path.GetDirectoryName(report)!, $"{imageName}-initial-{originalTheme}.png"));
        foreach (var theme in new[] { "dark", "light", "system" })
        {
            PreviewTheme(theme);
            await Task.Delay(300);
            if (dialog.XamlRoot?.Content is not Panel root || !ReferenceEquals(dialog.Parent, root))
                throw new InvalidOperationException("The dialog is not hosted in the window's live visual tree.");
            var expected = theme == "system" ? Root.ActualTheme : theme == "dark" ? ElementTheme.Dark : ElementTheme.Light;
            if (dialog.ActualTheme != expected || root.ActualTheme != expected ||
                root.Background is not Microsoft.UI.Xaml.Media.SolidColorBrush surface ||
                surface.Color != Views.WindowAppearance.SurfaceColor(this, expected))
                throw new InvalidOperationException($"{dialog.Title}: the {theme} surface does not match Settings and the app.");
            // The original regression lived in a SECOND popup, not below the ContentDialog.
            // Inspect the whole XamlRoot so a transparent card cannot hide a dimmed window.
            if (Microsoft.UI.Xaml.Media.VisualTreeHelper.GetOpenPopupsForXamlRoot(dialog.XamlRoot).Count != 0)
                throw new InvalidOperationException($"{dialog.Title}: a dialog or smoke popup still covers the {theme} window.");
            if (FindSmokeRectangle(root) is { Fill: Microsoft.UI.Xaml.Media.SolidColorBrush smoke } && smoke.Color.A != 0)
                throw new InvalidOperationException($"{dialog.Title}: the {theme} window still has a dimming layer.");
            if (dialog.Foreground is not Microsoft.UI.Xaml.Media.SolidColorBrush foreground ||
                (expected == ElementTheme.Dark ? foreground.Color.R < 200 : foreground.Color.R > 80))
                throw new InvalidOperationException($"{dialog.Title}: the {theme} text did not follow the Fluent theme.");
            VerifySingleDialogSurfaceForSmokeTest(dialog);
            await CaptureSmokeImage(root, Path.Combine(Path.GetDirectoryName(report)!, $"{imageName}-{theme}.png"));
        }
        PreviewTheme(originalTheme);
    }
    private sealed class DialogPresentationVerifier : IDisposable
    {
        private sealed class Presentation
        {
            internal required Window Window;
            internal int ReadyCount;
            internal Windows.Graphics.SizeInt32 ReadySize;
            internal bool StartupVerified;
            internal bool Closed;
            internal string? Failure;
            internal readonly TaskCompletionSource<bool> Ready = new(TaskCreationOptions.RunContinuationsAsynchronously);
        }
        private readonly System.Collections.Generic.Dictionary<ContentDialog, Presentation> dialogs = new();
        internal DialogPresentationVerifier()
        {
            Views.WindowedDialog.Preparing += OnPreparing;
            Views.WindowedDialog.ReadyToShow += OnReadyToShow;
        }
        private void OnPreparing(ContentDialog dialog, Window window)
        {
            // Validation intentionally closes the editor before showcasing a row, then
            // reopens the same draft. Forbid overlapping hosts, not a later presentation.
            if (dialogs.TryGetValue(dialog,out var previous)) {
                if (!previous.Closed) throw new InvalidOperationException("A second native window was created for the same dialog presentation.");
                if (previous.Failure is not null) throw new InvalidOperationException(previous.Failure);
            }
            var state = new Presentation { Window = window };
            dialogs[dialog] = state;
            window.Closed += (_,_) => state.Closed = true;
            window.AppWindow.Changed += (_, _) =>
            {
                if (window.AppWindow is not { } appWindow) return;
                if (appWindow.IsVisible && state.ReadyCount == 0)
                    state.Failure = "The dialog window became visible before content measurement finished.";
                if (appWindow.IsVisible && state.ReadyCount > 0 && !state.StartupVerified &&
                    (Math.Abs(appWindow.Size.Width - state.ReadySize.Width) > 2 ||
                     Math.Abs(appWindow.Size.Height - state.ReadySize.Height) > 2))
                    state.Failure = "The dialog changed size after becoming visible.";
            };
            window.Activated += (_, args) =>
            {
                // WinUI can report a focus activation while preparing a still-hidden tree.
                // The regression is a visible staging window, not that internal notification.
                if (window.AppWindow?.IsVisible == true && args.WindowActivationState != WindowActivationState.Deactivated && state.ReadyCount == 0)
                    state.Failure = "The dialog window was activated before it was ready.";
            };
        }
        private void OnReadyToShow(ContentDialog dialog, Window window)
        {
            var state = dialogs[dialog];
            state.ReadyCount++;
            state.ReadySize = window.AppWindow.Size;
            if (window.AppWindow.IsVisible || !ReferenceEquals(state.Window, window) ||
                dialog.Content is not FrameworkElement { ActualWidth: > 0, ActualHeight: > 0 })
                state.Failure = "The final dialog must be fully laid out in its one hidden window before activation.";
            state.Ready.TrySetResult(true);
        }
        internal async Task WaitUntilReadyAsync(ContentDialog dialog)
        {
            // Preparation is asynchronous, especially under x64 emulation. Observe the actual
            // lifecycle boundary rather than assuming it completed within a fixed 350 ms.
            await dialogs[dialog].Ready.Task.WaitAsync(TimeSpan.FromSeconds(10));
            await Task.Delay(100);
        }
        internal void Verify(ContentDialog dialog)
        {
            if (!dialogs.TryGetValue(dialog, out var state) || state.ReadyCount != 1)
                throw new InvalidOperationException("The dialog was not prepared exactly once before presentation.");
            var size = state.Window.AppWindow.Size;
            if (!state.Window.AppWindow.IsVisible || state.Failure is not null || Math.Abs(size.Width - state.ReadySize.Width) > 2 || Math.Abs(size.Height - state.ReadySize.Height) > 2)
                throw new InvalidOperationException($"{dialog.Title}: startup resized a visible dialog: " +
                    $"{state.ReadySize.Width}x{state.ReadySize.Height} -> {size.Width}x{size.Height}. {state.Failure}");
            state.StartupVerified = true;
        }
        internal Window WindowFor(ContentDialog dialog) => dialogs[dialog].Window;
        internal bool WasReadyToShow(ContentDialog dialog) => dialogs[dialog].ReadyCount != 0;
        public void Dispose()
        {
            Views.WindowedDialog.Preparing -= OnPreparing;
            Views.WindowedDialog.ReadyToShow -= OnReadyToShow;
        }
    }
    private static void VerifySingleDialogSurfaceForSmokeTest(ContentDialog dialog)
    {
        Border? card = null;
        for (DependencyObject? node = dialog.Content as DependencyObject; node is not null;
             node = Microsoft.UI.Xaml.Media.VisualTreeHelper.GetParent(node))
            if (node is Border { Name: "BackgroundElement" } border) { card = border; break; }
        if (card is null) throw new InvalidOperationException("The native dialog card did not render.");
        for (DependencyObject? node = card; node is not null; node = Microsoft.UI.Xaml.Media.VisualTreeHelper.GetParent(node))
            if (node is Border { Name: "Container" } container &&
                VisualStateManager.GetVisualStateGroups(container).Any(group => group.Name == "DialogShowingStates" && group.Transitions.Count != 0))
                throw new InvalidOperationException("The native window still has a second dialog opening/closing animation.");
        static bool Transparent(Microsoft.UI.Xaml.Media.Brush? brush) =>
            brush is null || brush is Microsoft.UI.Xaml.Media.SolidColorBrush { Color.A: 0 };
        if (!Transparent(dialog.Background) || !Transparent(card.Background) ||
            card.BorderThickness != new Thickness(0) || card.CornerRadius != new CornerRadius(0) ||
            card.Shadow is not null || Math.Abs(card.Translation.Z) > .001)
            throw new InvalidOperationException($"{dialog.Title}: a second dialog surface remains: " +
                $"background={dialog.Background}, card={card.Background}, corners={card.CornerRadius}, " +
                $"shadow={card.Shadow is not null}, elevation={card.Translation.Z}.");
    }
    private static Microsoft.UI.Xaml.Shapes.Rectangle? FindSmokeRectangle(DependencyObject root)
    {
        var count = Microsoft.UI.Xaml.Media.VisualTreeHelper.GetChildrenCount(root);
        for (var i = 0; i < count; i++)
        {
            var child = Microsoft.UI.Xaml.Media.VisualTreeHelper.GetChild(root, i);
            if (child is Microsoft.UI.Xaml.Shapes.Rectangle { Name: "SmokeLayerBackground" } rectangle) return rectangle;
            if (FindSmokeRectangle(child) is { } found) return found;
        }
        return null;
    }
    private static async Task CaptureSmokeImage(FrameworkElement element, string path)
    {
        var bitmap = new RenderTargetBitmap();
        await bitmap.RenderAsync(element);
        var pixels = await bitmap.GetPixelsAsync();
        using var reader = DataReader.FromBuffer(pixels);
        var bytes = new byte[pixels.Length]; reader.ReadBytes(bytes);
        // RenderTargetBitmap omits Mica and preserves translucent fills. Flatten onto
        // the matching theme surface so PNG viewers don't show black text on black
        // transparency or turn translucent selected rows into solid white blocks.
        var surface = element.ActualTheme == ElementTheme.Dark ? 32 : 248;
        for (var i = 0; i < bytes.Length; i += 4)
        {
            var fill = (surface * (255 - bytes[i + 3]) + 127) / 255;
            bytes[i] = (byte)Math.Min(255, bytes[i] + fill);
            bytes[i + 1] = (byte)Math.Min(255, bytes[i + 1] + fill);
            bytes[i + 2] = (byte)Math.Min(255, bytes[i + 2] + fill);
            bytes[i + 3] = 255;
        }
        using var file = File.Open(path, FileMode.Create);
        using var stream = file.AsRandomAccessStream();
        var encoder = await BitmapEncoder.CreateAsync(BitmapEncoder.PngEncoderId, stream);
        encoder.SetPixelData(BitmapPixelFormat.Bgra8, BitmapAlphaMode.Ignore, (uint)bitmap.PixelWidth, (uint)bitmap.PixelHeight, 96, 96, bytes);
        await encoder.FlushAsync();
    }

    private void VerifyCloudImportEditorProgressForSmokeTest()
    {
        if (!desktopEditorVisible) throw new InvalidOperationException("The editor must be open for Cloud Storage progress.");
        using (var cloudImport = new CloudImportActivity())
        {
            UpdateProcessingChrome();
            if (StatusStageTitle.Text != "Checking Cloud Storage access…" || !StatusRing.IsActive)
                throw new InvalidOperationException("The editor must show the access check before downloading.");
            cloudImport.Update(new EngineEvent("Downloading video: 50 of 100 bytes", .5, JsonSerializer.SerializeToElement(new { progress = .5 })));
            UpdateProcessingChrome();
            if (ContentTransferProgressBar.Visibility != Visibility.Visible || ContentTransferProgressBar.Value != .5 ||
                Microsoft.UI.Xaml.Automation.AutomationProperties.GetName(ContentTransferProgressBar) != "Cloud video download progress")
                throw new InvalidOperationException("The editor must expose the download fraction with an accessible name.");
            cloudImport.Update(new EngineEvent("Verifying the downloaded video…", null, JsonSerializer.SerializeToElement(new { })));
            UpdateProcessingChrome();
            if (StatusStageTitle.Text != "Verifying the downloaded video…" || ContentTransferProgressBar.Visibility != Visibility.Collapsed)
                throw new InvalidOperationException("The editor must show verification after the byte transfer.");
        }
        UpdateProcessingChrome();
        if (ContentTransferProgressBar.Visibility != Visibility.Collapsed || StatusRing.IsActive)
            throw new InvalidOperationException("Cloud Storage progress must clear when the operation finishes.");
    }
}
#endif
