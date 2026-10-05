#if DEBUG
using Microsoft.UI.Xaml;
using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.UI.Xaml.Media.Imaging;
using Windows.Graphics.Imaging;
using Windows.Storage.Streams;

namespace Vidi_Studio_Windows;

public sealed partial class MainWindow
{
    private static int smokeTestClaimed;
    private async Task RunSmokeTestAsync(string report)
    {
        try
        {
            var folder = Environment.GetEnvironmentVariable("VIDISTUDIO_UI_SMOKE_PROJECT");
            if (folder == null) throw new InvalidOperationException("Smoke project is required.");
            Model.Open(folder);
            if (!MainMenu.Items.Select(item => item.Title).SequenceEqual(new[] { "File", "View", "Analysis", "Segments", "Playback", "Help" }))
                throw new InvalidOperationException("Editor menu set does not match the active page.");
            if (galleryMenus.Concat(editorMenus).SelectMany(menu => menu.Items).OfType<Microsoft.UI.Xaml.Controls.MenuFlyoutItem>().Any(item => item.Icon == null) ||
                SegmentMenu(Model.Project!.Cues[0]).Items.OfType<Microsoft.UI.Xaml.Controls.MenuFlyoutItem>().Any(item => item.Icon == null))
                throw new InvalidOperationException("A menu or segment action has no icon.");
            if (editorMenus[0].Items.OfType<Microsoft.UI.Xaml.Controls.MenuFlyoutItem>().First(item => item.Text == "Export").KeyboardAcceleratorTextOverride != "Ctrl+Shift+E" ||
                editorMenus[4].Items.OfType<Microsoft.UI.Xaml.Controls.MenuFlyoutItem>().First(item => item.Text == "Play video").KeyboardAcceleratorTextOverride != "Ctrl+Shift+Space")
                throw new InvalidOperationException("Menu commands do not show their keyboard shortcuts.");
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
            var darkBackground = ((Microsoft.UI.Xaml.Media.SolidColorBrush)Root.Background).Color;
            PreviewTheme("light");
            await Task.Delay(50);
            if (Root.RequestedTheme != ElementTheme.Light || Root.ActualTheme != ElementTheme.Light)
                throw new InvalidOperationException("Light appearance did not apply to the application root.");
            var lightBackground = ((Microsoft.UI.Xaml.Media.SolidColorBrush)Root.Background).Color;
            if (darkBackground.R + darkBackground.G + darkBackground.B >= lightBackground.R + lightBackground.G + lightBackground.B)
                throw new InvalidOperationException("Application background colors did not follow the selected appearance.");
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
            PreviewTheme(originalTheme);
            Section.SelectedIndex = 4; Seek(2.5); await Task.Delay(100); UpdateActiveSegment(true);
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
            Section.SelectedIndex = 1;
            Model.Settings.Set("summarySentenceHighlightColor", "#C0D8FF"); Model.Settings.Set("summaryWordHighlightColor", "#FFDA57");
            HighlightReadbackSentence("Native Windows summary."); HighlightReadbackWord(7, 7);
            if (activeSummaryBlock == null || activeSummaryBlock.TextHighlighters.Count != 2 ||
                ((Microsoft.UI.Xaml.Media.SolidColorBrush)activeSummaryBlock.TextHighlighters[0].Background).Color.B != 255 ||
                ((Microsoft.UI.Xaml.Media.SolidColorBrush)activeSummaryBlock.TextHighlighters[1].Background).Color.R != 255)
                throw new InvalidOperationException("Summary sentence or word highlighting did not use Settings colors.");
            await CaptureSmokeImage(Root, Path.Combine(Path.GetDirectoryName(report)!, "summary-highlight.png"));
            await CaptureSmokeImage(SummaryRichText, Path.Combine(Path.GetDirectoryName(report)!, "summary-text-highlight.png"));
            ClearSummaryHighlight();
            var spokenSummary = readback.SpeakAsync("Native Windows summary.");
            for (var attempt = 0; attempt < 40 && activeSummaryBlock?.TextHighlighters.Count != 2; attempt++) await Task.Delay(50);
            if (activeSummaryBlock?.TextHighlighters.Count != 2)
                throw new InvalidOperationException("Spoken summary did not synchronize its sentence and word highlight with the visible text.");
            readback.Stop();
            try { await spokenSummary; } catch (OperationCanceledException) { }
            await Task.Delay(500);
            Section.SelectedIndex = 0;
            await CaptureSmokeImage(Root, Path.Combine(Path.GetDirectoryName(report)!, "editor-transcript.png"));
            Section.SelectedIndex = 2;
            TogglePreview_Click(this, new RoutedEventArgs());
            await Task.Delay(200);
            await CaptureSmokeImage(Root, Path.Combine(Path.GetDirectoryName(report)!, "editor-descriptions.png"));
            ShowGallery_Click(this, new RoutedEventArgs());
            if (!MainMenu.Items.Select(item => item.Title).SequenceEqual(new[] { "File", "View", "Help" }))
                throw new InvalidOperationException("Gallery menu includes project-only commands.");
            if (TopOpenFolderButton.Visibility != Visibility.Collapsed)
                throw new InvalidOperationException("All Projects still shows the editor's Open folder button.");
            PreviewTheme("dark"); await Task.Delay(80);
            var searchOrigin = GallerySearchHost.TransformToVisual(Root).TransformPoint(new Windows.Foundation.Point());
            if (GallerySearchHost.Visibility != Visibility.Visible ||
                Math.Abs(searchOrigin.X + GallerySearchHost.ActualWidth / 2 - Root.ActualWidth / 2) > 24)
                throw new InvalidOperationException("All Projects search is not centered in the top header.");
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
                Model.Projects[0].DurationLabel != "00:08" || Model.Projects[0].AdSegmentLabel != "1")
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
            await CaptureSmokeImage(Root, Path.Combine(Path.GetDirectoryName(report)!, "gallery-table.png"));
            GalleryLayout_Click(this, new RoutedEventArgs());
            Model.Open(folder);
            if (TopOpenFolderButton.Visibility != Visibility.Visible)
                throw new InvalidOperationException("The editor does not show its project folder button.");
            if (GallerySearchHost.Visibility != Visibility.Collapsed)
                throw new InvalidOperationException("All Projects search remained in the editor header.");
            for (var i = 0; i < 6; i++) { Section.SelectedIndex = i; await Task.Delay(50); }
            ToggleGrid_Click(this, new RoutedEventArgs());
            ToggleGrid_Click(this, new RoutedEventArgs());
            ToggleLibrary_Click(this, new RoutedEventArgs());
            ToggleLibrary_Click(this, new RoutedEventArgs());
            ToggleInspector_Click(this, new RoutedEventArgs());
            ToggleInspector_Click(this, new RoutedEventArgs());
            var settings = new Views.SettingsDialog(Model, this);
            var display = settings.ShowAsync();
            await Task.Delay(250);
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
            settings.VerifyGeminiModelFiltering();
            await Task.Delay(100);
            await CaptureSmokeImage((FrameworkElement)settings.Content, Path.Combine(Path.GetDirectoryName(report)!, "settings-gemini.png"));
            settings.NavigateForSmokeTest(1);
            await Task.Delay(100);
            await CaptureSmokeImage((FrameworkElement)settings.Content, Path.Combine(Path.GetDirectoryName(report)!, "settings-narration.png"));
            settings.NavigateForSmokeTest(4);
            await Task.Delay(100);
            await CaptureSmokeImage((FrameworkElement)settings.Content, Path.Combine(Path.GetDirectoryName(report)!, "settings-tools.png"));
            foreach (var section in new[] { 1, 5, 6 })
            {
                settings.SelectSection(section);
                if (settings.SelectedSectionForSmokeTest != section)
                    throw new InvalidOperationException("Settings opened the wrong section.");
            }
            foreach (var section in new[] { 0, 1, 2, 3, 4, 5, 1, 0 }) { settings.NavigateForSmokeTest(section); await Task.Delay(200); }
            await settings.VerifyProviderSwitchingAsync();
            await settings.VerifyLocalVoiceSamplesAsync();
            settings.Hide(); await display;
            foreach (var section in new[] { 1, 5, 6 })
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
            if (openSettings?.SelectedSectionForSmokeTest != 1) throw new InvalidOperationException("Narration button opened the wrong Settings page.");
            HelpSettings_Click(this, new RoutedEventArgs());
            await Task.Delay(100);
            if (openSettings?.SelectedSectionForSmokeTest != 5) throw new InvalidOperationException("Help menu opened the wrong Settings page.");
            AboutSettings_Click(this, new RoutedEventArgs());
            await Task.Delay(100);
            if (openSettings?.SelectedSectionForSmokeTest != 6) throw new InvalidOperationException("About menu opened the wrong Settings page.");
            openSettings.Hide(); await openFromButton;
            PreviewTheme("dark");
            var newProject = new Views.NewProjectDialog(this, Root.ActualTheme, Model.Settings);
            var newProjectDisplay = ShowDialogAsync(newProject, 680, 780); await Task.Delay(100);
            if (newProjectDisplay.IsCompleted) await newProjectDisplay;
            PreviewTheme("light"); await Task.Delay(80);
            if (newProject.ActualTheme != ElementTheme.Light) throw new InvalidOperationException("An open New Project dialog did not switch to light appearance.");
            PreviewTheme("dark"); await Task.Delay(80);
            if (newProject.RequestedTheme != ElementTheme.Dark || newProject.IsPrimaryButtonEnabled)
                throw new InvalidOperationException("New Project did not follow dark mode or disable Create without a source.");
            await newProject.InspectVideoForSmokeTestAsync(Model.Project!.Resolve(Model.Project.Source));
            if (!newProject.IsPrimaryButtonEnabled || newProject.VideoPath == null || !newProject.DetailsForSmokeTest.Contains("320 × 180"))
                throw new InvalidOperationException("New Project did not preview local video details before creation: " + newProject.DetailsForSmokeTest);
            if (newProject.ActualWidth <= 0 || newProject.ActualHeight <= 0)
                throw new InvalidOperationException($"New Project dialog did not render: {newProject.ActualWidth} × {newProject.ActualHeight}.");
            await CaptureSmokeImage(newProject, Path.Combine(Path.GetDirectoryName(report)!, "new-project-dark.png"));
            newProject.SelectYouTubeForSmokeTest("https://www.youtube.com/watch?v=jfKfPfyJRdk");
            await Task.Delay(50);
            if (newProject.IsPrimaryButtonEnabled || !newProject.IsYouTube) throw new InvalidOperationException("New Project allowed a YouTube source before fetching its details.");
            newProject.SetYouTubeDetailsForSmokeTest("Public video preview");
            if (!newProject.IsPrimaryButtonEnabled) throw new InvalidOperationException("New Project did not enable Create after reviewing YouTube details.");
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
            if (replacementDialog.CanSaveForSmokeTest) throw new InvalidOperationException("Existing export replacement did not require an explicit selection.");
            child.Close();
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
            Section.SelectedIndex = 5;
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
            if (VideoPlayPauseButton.Content as string != "\uE769" || videoPlayMenuItem?.Text != "Pause video")
                throw new InvalidOperationException("The video transport did not switch from Play to Pause.");
            await Task.Delay(150);
            if (Math.Abs(narrationPlayer.PlaybackSession.Position.TotalSeconds - (Position - 2)) > .5) throw new InvalidOperationException("Narration position did not align with the video cue.");
            video.Pause(); await Task.Delay(250);
            if (VideoPlayPauseButton.Content as string != "\uE768" || videoPlayMenuItem?.Text != "Play video")
                throw new InvalidOperationException("The video transport did not switch from Pause to Play.");
            if (narrationPlayer.PlaybackSession.PlaybackState == Windows.Media.Playback.MediaPlaybackState.Playing) throw new InvalidOperationException("Narration kept playing while video was paused.");
            PlayPause_Click(this, new RoutedEventArgs());
            for (var attempt = 0; attempt < 20 && video.PlaybackSession.PlaybackState != Windows.Media.Playback.MediaPlaybackState.Playing; attempt++) await Task.Delay(50);
            UpdatePlaybackControls();
            if (VideoPlayPauseButton.Content as string != "\uE769" || videoPlayMenuItem?.Text != "Pause video")
                throw new InvalidOperationException("The video transport did not resume through its shared button.");
            Seek(6); video.Play(); await Task.Delay(250);
            if (narrationPlayer.PlaybackSession.PlaybackState == Windows.Media.Playback.MediaPlaybackState.Playing || activeCue != null) throw new InvalidOperationException("Narration remained active after seeking beyond its cue.");
            video.Pause(); Narration.IsOn = false;
            Model.Settings.Set("ttsMode", "windows-core");
            var firstReadback = readback.SpeakAsync("This first summary is replaced immediately.");
            var nextReadback = readback.SpeakAsync("This replacement summary must be cancellable.");
            await Task.Delay(150);
            if (readback.State == Services.SummaryReadbackState.Playing)
            {
                if (SummaryPlayButton.Content as string != "Pause" || summaryPlayMenuItem?.Text != "Pause summary")
                    throw new InvalidOperationException("Summary Play did not become Pause in the view and menu.");
                readback.Pause(); await Task.Delay(50);
                if (SummaryPlayButton.Content as string != "Resume" || summaryPlayMenuItem?.Text != "Resume summary")
                    throw new InvalidOperationException("Summary Pause did not become Resume in the view and menu.");
                readback.Resume();
            }
            readback.Stop(); await Task.Delay(50);
            if (SummaryPlayButton.Content as string != "Play" || summaryStopMenuItem?.IsEnabled != false)
                throw new InvalidOperationException("Summary Stop did not reset playback controls.");
            foreach (var task in new[] { firstReadback, nextReadback })
            {
                try { await task; } catch (OperationCanceledException) { }
            }
            ClearProject();
            if (Model.Project != null || EditorWorkspace.Visibility != Visibility.Collapsed || GalleryPanel.Visibility != Visibility.Visible || video.Source != null || narrationPlayer.Source != null || ProjectDetails.Children.Count != 0) throw new InvalidOperationException("Project close left stale editor state.");
            await File.WriteAllTextAsync(report, "PASS: app-wide live light/dark themes across gallery, editor, Settings, New Project and Export; visible segment checkboxes and edit multi-selection, select all, deselect and Done in both themes; menu shortcut labels; video timeline playhead, synchronized Transcript and AD Transcript rows, summary highlighting and playback, New Project inspection, edit persistence, undo/redo, OneCore and SAPI narration, subtitle export, six editor sections, and engine invocation.");
        }
        catch (Exception ex) { await File.WriteAllTextAsync(report, ex.ToString()); }
        finally
        {
            // The final window can abort the Close call while dispatcher shutdown completes.
            try { Close(); }
            catch (System.Runtime.InteropServices.COMException ex) when (ex.HResult == unchecked((int)0x80004004)) { }
            Application.Current.Exit();
        }
    }
    private async Task VerifySegmentSelectionForSmokeTestAsync(string report)
    {
        foreach (var theme in new[] { "dark", "light" })
        {
            PreviewTheme(theme);
            foreach (var section in new[] { 0, 2, 4 })
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
                row.IsSelected = true;
                if (Segments.SelectedItems.Count != 1 || !Segments.SelectedItems.Contains(row.Content))
                    throw new InvalidOperationException("Selecting a segment row did not update the edit selection.");
                if (section == 4)
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
    private static async Task CaptureSmokeImage(FrameworkElement element, string path)
    {
        var bitmap = new RenderTargetBitmap();
        await bitmap.RenderAsync(element);
        var pixels = await bitmap.GetPixelsAsync();
        using var reader = DataReader.FromBuffer(pixels);
        var bytes = new byte[pixels.Length]; reader.ReadBytes(bytes);
        using var file = File.Open(path, FileMode.Create);
        using var stream = file.AsRandomAccessStream();
        var encoder = await BitmapEncoder.CreateAsync(BitmapEncoder.PngEncoderId, stream);
        encoder.SetPixelData(BitmapPixelFormat.Bgra8, BitmapAlphaMode.Premultiplied, (uint)bitmap.PixelWidth, (uint)bitmap.PixelHeight, 96, 96, bytes);
        await encoder.FlushAsync();
    }
}
#endif
