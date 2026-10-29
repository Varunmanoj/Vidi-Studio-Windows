using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Vidi_Studio_Windows.Models;
using Vidi_Studio_Windows.Services;

namespace Vidi_Studio_Windows;

public sealed partial class MainWindow
{
    private static readonly (string Id, string Label)[] BulkOrigins =
    {
        ("ai", "AI-generated"),
        ("ai_modified", "AI-generated · modified"),
        ("imported", "Imported"),
        ("imported_modified", "Imported · modified"),
        ("manual", "Manually added"),
        ("unknown", "Origin unknown")
    };

    private void UpdateBulkActionState()
    {
        if (BulkOriginButton == null || BulkNarrateButton == null || BulkShiftButton == null ||
            SelectionMenuButton == null || SelectTranscriptsItem == null || SelectDescriptionsItem == null ||
            SelectDescriptionsAudioItem == null || SelectDescriptionsMissingAudioItem == null ||
            SelectOriginItem == null || MergeSelectedButton == null) return;
        var count = Segments.SelectedItems.Count;
        BulkOriginButton.IsEnabled = count > 0 && Model.Idle;
        var descriptions = SelectedSegments().Where(segment => segment.IsDescription).ToList();
        var descriptionCount = descriptions.Count;
        var project = Model.Project;
        var newCount = project == null ? 0 : descriptions.Count(segment => !HasSegmentAudio(project, segment));
        var existingCount = descriptionCount - newCount;
        var geminiReady = Model.CurrentNarrationEngine() != "gemini" || Model.CanUseAI;
        var narrationLabel = existingCount == 0
            ? $"Generate Audio for {newCount} Description{(newCount == 1 ? "" : "s")}"
            : newCount == 0
                ? $"Regenerate Audio for {existingCount} Description{(existingCount == 1 ? "" : "s")}"
                : $"Generate Audio for {newCount} New and Regenerate Audio for {existingCount} Existing Description{(existingCount == 1 ? "" : "s")}";
        BulkNarrateButton.IsEnabled = descriptionCount > 0 && Model.Idle && geminiReady;
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(BulkNarrateButton, narrationLabel);
        ToolTipService.SetToolTip(BulkNarrateButton,
            descriptionCount == 0
                ? "Select one or more descriptions; transcript segments do not have narration"
                : $"{narrationLabel}. Generates audio for the selected descriptions only, one after another, using the current voice. {existingCount} existing file{(existingCount == 1 ? "" : "s")} will be replaced; {newCount} new file{(newCount == 1 ? "" : "s")} will be created. Transcript segments are not narrated.");
        BulkShiftButton.IsEnabled = count > 0 && Model.Idle;
        SelectionMenuButton.IsEnabled = Segments.Items.Count > 0 && Model.Idle;
        SelectTranscriptsItem.Visibility = Section.SelectedIndex is 0 or 4 ? Visibility.Visible : Visibility.Collapsed;
        SelectDescriptionsItem.Visibility = Section.SelectedIndex is 2 or 4 ? Visibility.Visible : Visibility.Collapsed;
        var descriptionsVisible = Section.SelectedIndex is 2 or 4;
        SelectDescriptionsAudioItem.Visibility = descriptionsVisible ? Visibility.Visible : Visibility.Collapsed;
        SelectDescriptionsMissingAudioItem.Visibility = descriptionsVisible ? Visibility.Visible : Visibility.Collapsed;
        var originChoices = UnfilteredSectionRows().Select(segment => segment.Origin).Distinct()
            .OrderBy(OriginOrder).ToList();
        SelectOriginItem.Items.Clear();
        foreach (var origin in originChoices)
        {
            var item = new MenuFlyoutItem { Text = OriginLabel(origin), Tag = origin };
            item.Click += SelectOrigin_Click;
            SelectOriginItem.Items.Add(item);
        }
        SelectOriginItem.Visibility = originChoices.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        MergeSelectedButton.Visibility = editMode && Section.SelectedIndex == 0 && CanMergeSelected() ? Visibility.Visible : Visibility.Collapsed;
    }

    private List<Segment> SelectedSegments() => Segments.SelectedItems.Cast<Segment>().ToList();

    private void SelectTranscripts_Click(object sender, RoutedEventArgs e) => SelectSegmentsByKind(isDescription: false);

    private void SelectDescriptions_Click(object sender, RoutedEventArgs e) => SelectSegmentsByKind(isDescription: true);

    private void SelectSegmentsByKind(bool isDescription)
    {
        if (Model.Busy || Model.Project is not { } project) return;
        var ids = (isDescription ? project.Cues : project.Transcript).Select(segment => segment.Id).ToHashSet();
        Segments.SelectedItems.Clear();
        foreach (var segment in Segments.Items.OfType<Segment>()
                     .Where(segment => segment.IsDescription == isDescription && ids.Contains(segment.Id)))
            Segments.SelectedItems.Add(segment);
        UpdateBulkActionState();
    }

    private void SelectDescriptionsWithAudio_Click(object sender, RoutedEventArgs e) => SelectSegmentsByAudio(withAudio: true);

    private void SelectDescriptionsWithoutAudio_Click(object sender, RoutedEventArgs e) => SelectSegmentsByAudio(withAudio: false);

    private void SelectSegmentsByAudio(bool withAudio)
    {
        if (Model.Busy || Model.Project is not { } project) return;
        Segments.SelectedItems.Clear();
        foreach (var segment in Segments.Items.OfType<Segment>().Where(segment =>
                     segment.IsDescription && HasSegmentAudio(project, segment) == withAudio))
            Segments.SelectedItems.Add(segment);
        UpdateBulkActionState();
    }

    private void SelectOrigin_Click(object sender, RoutedEventArgs e)
    {
        if (Model.Busy || sender is not MenuFlyoutItem { Tag: string origin }) return;
        Segments.SelectedItems.Clear();
        foreach (var segment in Segments.Items.OfType<Segment>().Where(segment => segment.Origin == origin))
            Segments.SelectedItems.Add(segment);
        UpdateBulkActionState();
    }

    private void BulkOrigin_Click(object sender, RoutedEventArgs e)
    {
        if (Model.Busy || Segments.SelectedItems.Count == 0 || sender is not FrameworkElement anchor) return;
        var menu = new MenuFlyout();
        foreach (var (id, label) in BulkOrigins)
        {
            var item = new MenuFlyoutItem { Text = label, Tag = id };
            item.Click += async (_, _) =>
            {
                if (Model.Project is not { } project) return;
                foreach (var segment in SelectedSegments())
                {
                    segment.Origin = id;
                    segment.OriginOverride = true;
                }
                try { await Model.SaveSegmentsAsync(project.Transcript, project.Cues); }
                catch (Exception ex) { await ShowError(ex); }
            };
            menu.Items.Add(item);
        }
        menu.ShowAt(anchor);
    }


    private async void BulkNarrate_Click(object sender, RoutedEventArgs e)
    {
        if (Model.Busy || Model.Project is not { } project) return;
        var descriptions = SelectedSegments().Where(segment => segment.IsDescription).ToList();
        if (descriptions.Count == 0)
        {
            await ShowDialogAsync(new ContentDialog
            {
                Title = "Select descriptions first",
                Content = new TextBlock { Text = "Bulk narration applies only to selected Audio Description rows.", TextWrapping = TextWrapping.Wrap },
                CloseButtonText = "Close"
            });
            return;
        }
        var engine = Model.CurrentNarrationEngine();
        if (engine == "gemini" && !Model.CanUseAI)
        {
            await ShowSettingsSectionAsync(3);
            return;
        }
        if (engine == "piper" && !PiperService.IsVoiceDownloaded(Model.CurrentNarrationVoice()))
        {
            await ShowSettingsSectionAsync(2);
            return;
        }
        var newCount = descriptions.Count(segment => !HasSegmentAudio(project, segment));
        var existingCount = descriptions.Count - newCount;
        var action = existingCount == 0
            ? $"Generate Audio for {newCount} Description{(newCount == 1 ? "" : "s")}"
            : newCount == 0
                ? $"Regenerate Audio for {existingCount} Description{(existingCount == 1 ? "" : "s")}"
                : $"Generate Audio for {newCount} New and Regenerate Audio for {existingCount} Existing Description{(existingCount == 1 ? "" : "s")}";
        if (!await Confirm("Generate selected narration?",
                $"{action}, one after another, using {Model.CurrentNarrationSummary()}. {existingCount} existing file{(existingCount == 1 ? "" : "s")} will be replaced; {newCount} new file{(newCount == 1 ? "" : "s")} will be created.",
                "Generate")) return;
        var flags = descriptions.SelectMany(segment => new[] { "--segments", segment.Id }).ToList();
        try { await Model.RunAsync("synthesize-tts", flags); }
        catch (Exception ex) { await ShowError(ex); }
    }

    private async void BulkShift_Click(object sender, RoutedEventArgs e)
    {
        if (Model.Busy || Model.Project is not { } project || Segments.SelectedItems.Count == 0) return;
        var offset = new TextBox
        {
            Header = "Offset in seconds",
            Text = "1.0",
            PlaceholderText = "Use a negative value to move earlier"
        };
        var dialog = new ContentDialog
        {
            Title = "Shift Selected Timing",
            Content = new StackPanel
            {
                Spacing = 10,
                Children =
                {
                    new TextBlock
                    {
                        Text = $"Move all {Segments.SelectedItems.Count} selected segments by the same offset without changing their duration.",
                        TextWrapping = TextWrapping.Wrap
                    },
                    offset
                }
            },
            PrimaryButtonText = "Shift Timing",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Primary
        };
        if (await ShowDialogAsync(dialog, 520, 340) != ContentDialogResult.Primary) return;
        if (!double.TryParse(offset.Text.Replace(',', '.'), System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out var seconds) ||
            !double.IsFinite(seconds) || Math.Abs(seconds) < 0.0001)
        {
            await ShowDialogAsync(new ContentDialog
            {
                Title = "Enter a valid offset",
                Content = new TextBlock { Text = "Use a non-zero number of seconds, for example 0.5 or -1.25.", TextWrapping = TextWrapping.Wrap },
                CloseButtonText = "Close"
            });
            return;
        }
        var selected = SelectedSegments();
        var lowest = selected.Min(segment => segment.Start);
        var highest = selected.Max(segment => segment.End);
        if (lowest + seconds < -0.001 || highest + seconds > project.Duration + 0.001)
        {
            await ShowDialogAsync(new ContentDialog
            {
                Title = "Selection would leave the video",
                Content = new TextBlock { Text = "Choose a smaller offset so every selected segment stays inside the video.", TextWrapping = TextWrapping.Wrap },
                CloseButtonText = "Close"
            });
            return;
        }
        foreach (var segment in selected)
        {
            segment.Start += seconds;
            segment.End += seconds;
        }
        try { await Model.SaveSegmentsAsync(project.Transcript, project.Cues); }
        catch (Exception ex) { await ShowError(ex); }
    }

    private void InvertSelection_Click(object sender, RoutedEventArgs e)
    {
        if (Model.Busy) return;
        var selected = Segments.SelectedItems.Cast<Segment>().Select(segment => segment.Id).ToHashSet();
        Segments.SelectedItems.Clear();
        foreach (var segment in Segments.Items.OfType<Segment>().Where(segment => !selected.Contains(segment.Id)))
            Segments.SelectedItems.Add(segment);
        UpdateBulkActionState();
    }

    private bool CanMergeSelected()
    {
        if (Section.SelectedIndex != 0 || Model.Project is not { } project || Segments.SelectedItems.Count < 2) return false;
        var indices = Segments.SelectedItems.Cast<Segment>()
            .Select(segment => project.Transcript.FindIndex(item => item.Id == segment.Id))
            .Where(index => index >= 0).OrderBy(index => index).ToList();
        if (indices.Count != Segments.SelectedItems.Count) return false;
        return indices.Zip(indices.Skip(1), (left, right) => right == left + 1).All(ok => ok);
    }

    private async void MergeSelected_Click(object sender, RoutedEventArgs e)
    {
        if (Model.Busy || Model.Project is not { } project) return;
        if (!CanMergeSelected())
        {
            await ShowDialogAsync(new ContentDialog
            {
                Title = "Select consecutive transcript segments",
                Content = new TextBlock { Text = "Merge is available for two or more consecutive transcript rows in the Transcript section.", TextWrapping = TextWrapping.Wrap },
                CloseButtonText = "Close"
            });
            return;
        }
        var rows = Segments.SelectedItems.Cast<Segment>().OrderBy(segment => segment.Start).ToList();
        var first = rows[0];
        var last = rows[^1];
        var ids = rows.Select(segment => segment.Id).ToHashSet();
        var origins = rows.Select(segment => segment.Origin).Distinct().ToList();
        var merged = first;
        merged.End = last.End;
        merged.Text = string.Join(" ", rows.Select(segment => segment.Text.Trim()).Where(text => text.Length > 0));
        merged.Speaker = rows.Select(segment => segment.Speaker).FirstOrDefault(value => !string.IsNullOrWhiteSpace(value));
        merged.Audio = null; merged.AudioText = null; merged.AudioDuration = null; merged.AudioSettings = null;
        merged.AudioEngine = null; merged.AudioVoice = null; merged.AudioStart = null; merged.AudioEnd = null;
        merged.AudioError = null; merged.AudioOrigin = null; merged.AudioSourceName = null;
        merged.Origin = origins.Count == 1 ? origins[0] : "unknown";
        merged.OriginOverride = true;
        var insertionIndex = project.Transcript.FindIndex(segment => segment.Id == first.Id);
        var mergedRows = project.Transcript.Where(segment => !ids.Contains(segment.Id)).ToList();
        mergedRows.Insert(Math.Min(Math.Max(insertionIndex, 0), mergedRows.Count), merged);
        project.Transcript.Clear();
        project.Transcript.AddRange(mergedRows);
        try { await Model.SaveSegmentsAsync(project.Transcript, project.Cues); }
        catch (Exception ex) { await ShowError(ex); }
    }
}
