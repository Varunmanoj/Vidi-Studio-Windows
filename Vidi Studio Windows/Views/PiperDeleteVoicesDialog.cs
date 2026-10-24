using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Vidi_Studio_Windows.Services;
using Windows.Media.Core;
using Windows.Media.Playback;
using Windows.System;

namespace Vidi_Studio_Windows.Views;

/// <summary>Lists only downloaded Piper voices so they can be sampled and removed.</summary>
public sealed class PiperDeleteVoicesDialog : ContentDialog
{
    private sealed record FilterOption(string Id, string Label)
    {
        public override string ToString() => Label;
    }

    private sealed class RowState
    {
        public Button Sample { get; } = new() { Width = 120, VerticalAlignment = VerticalAlignment.Center };
        public Button Action { get; } = new() { Width = 110, VerticalAlignment = VerticalAlignment.Center };
        public ProgressRing Ring { get; } = new()
        {
            Width = 18, Height = 18, IsActive = false,
            Visibility = Visibility.Collapsed, VerticalAlignment = VerticalAlignment.Center
        };
    }

    private readonly TextBox search = new() { Header = "Search voices", PlaceholderText = "Name, language, dialect, or quality" };
    private readonly ComboBox languageFilter = new() { Header = "Language", HorizontalAlignment = HorizontalAlignment.Stretch };
    private readonly ComboBox qualityFilter = new() { Header = "Quality", HorizontalAlignment = HorizontalAlignment.Stretch };
    private readonly ComboBox sortFilter = new() { Header = "Sort by", HorizontalAlignment = HorizontalAlignment.Stretch };
    private readonly ListView list = new() { SelectionMode = ListViewSelectionMode.None, Height = 300 };
    private readonly TextBlock summary = new() { TextWrapping = TextWrapping.Wrap, Opacity = .75 };
    private readonly TextBlock status = new() { TextWrapping = TextWrapping.Wrap };
    private readonly ProgressBar progress = new() { Minimum = 0, Maximum = 100, Visibility = Visibility.Collapsed };
    private readonly CancellationTokenSource lifetime = new();
    private readonly MediaPlayer samplePlayer = new() { AudioCategory = MediaPlayerAudioCategory.Speech };
    private readonly Dictionary<string, RowState> rows = new(StringComparer.OrdinalIgnoreCase);
    private bool deleting;
    private string? deletingVoiceId;
    private string? sampleLoadingId;
    private string? sampleVoiceId;

    public PiperDeleteVoicesDialog()
    {
        Title = "Delete Piper Voices";
        CloseButtonText = "Close";
        DefaultButton = ContentDialogButton.Close;
        Resources["ContentDialogMaxWidth"] = 760d;
        Resources["ContentDialogMaxHeight"] = 860d;
        var body = new StackPanel { Spacing = 10, Width = 660 };
        body.Children.Add(search);
        var filters = new Grid { ColumnSpacing = 10 };
        for (var column = 0; column < 3; column++) filters.ColumnDefinitions.Add(new ColumnDefinition());
        Grid.SetColumn(languageFilter, 0); filters.Children.Add(languageFilter);
        Grid.SetColumn(qualityFilter, 1); filters.Children.Add(qualityFilter);
        Grid.SetColumn(sortFilter, 2); filters.Children.Add(sortFilter);
        body.Children.Add(filters);
        body.Children.Add(list);
        body.Children.Add(progress);
        body.Children.Add(status);
        body.Children.Add(summary);
        Content = body;
        AutomationProperties.SetName(progress, "Voice removal progress");
        AutomationProperties.SetName(list, "Downloaded Piper voices");

        PopulateFilters();
        search.TextChanged += (_, _) => Rebuild();
        languageFilter.SelectionChanged += (_, _) => Rebuild();
        qualityFilter.SelectionChanged += (_, _) => Rebuild();
        sortFilter.SelectionChanged += (_, _) => Rebuild();
        samplePlayer.CommandManager.IsEnabled = false;
        samplePlayer.MediaEnded += (_, _) => DispatcherQueue.TryEnqueue(() => FinishSample("Sample finished."));
        samplePlayer.MediaFailed += (_, _) => DispatcherQueue.TryEnqueue(() =>
            FinishSample("The sample could not be played. Check the internet connection and try again."));
        Closed += (_, _) => { lifetime.Cancel(); samplePlayer.Dispose(); };
        // Escape closes the dialog from anywhere, including the search box.
        body.KeyDown += (_, args) =>
        {
            if (args.Key != VirtualKey.Escape) return;
            args.Handled = true;
            Hide();
        };
        Rebuild();
        status.Text = Downloaded().Count == 0
            ? "No Piper voices are downloaded, so there is nothing to delete."
            : "Choose a downloaded voice to sample or remove.";
    }

    private static List<PiperCatalogVoice> Downloaded() =>
        PiperService.Catalog.Where(voice => PiperService.IsVoiceDownloaded(voice.Id)).ToList();

    private void PopulateFilters()
    {
        var voices = Downloaded();
        var language = (languageFilter.SelectedItem as FilterOption)?.Id ?? "";
        var quality = (qualityFilter.SelectedItem as FilterOption)?.Id ?? "";

        var languages = voices
            .GroupBy(voice => voice.LanguageFamily, StringComparer.OrdinalIgnoreCase)
            .Select(group => new FilterOption(group.Key, group.First().LanguageName))
            .OrderBy(option => option.Label, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
        languages.Insert(0, new("", "All languages"));
        languageFilter.ItemsSource = languages;
        languageFilter.SelectedItem = languages.FirstOrDefault(option => option.Id == language) ?? languages[0];

        var qualities = voices
            .Select(voice => voice.Quality)
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderByDescending(PiperCatalogVoice.QualityRank)
            .Select(value => new FilterOption(value, PiperCatalogVoice.QualityLabelFor(value)))
            .ToList();
        qualities.Insert(0, new("", "All qualities"));
        qualityFilter.ItemsSource = qualities;
        qualityFilter.SelectedItem = qualities.FirstOrDefault(option => option.Id == quality) ?? qualities[0];

        if (sortFilter.ItemsSource == null)
        {
            sortFilter.ItemsSource = new List<FilterOption>
            {
                new("language", "Language (A–Z)"),
                new("name", "Voice name (A–Z)"),
                new("quality", "Quality (high to low)"),
                new("size", "Size (smallest first)")
            };
            sortFilter.SelectedIndex = 0;
        }
    }

    private void Rebuild()
    {
        var filter = search.Text.Trim();
        var language = (languageFilter.SelectedItem as FilterOption)?.Id ?? "";
        var quality = (qualityFilter.SelectedItem as FilterOption)?.Id ?? "";
        var sort = (sortFilter.SelectedItem as FilterOption)?.Id ?? "language";
        var installed = Downloaded();
        IEnumerable<PiperCatalogVoice> query = installed.Where(voice =>
            (language.Length == 0 || voice.LanguageFamily.Equals(language, StringComparison.OrdinalIgnoreCase)) &&
            (quality.Length == 0 || voice.Quality.Equals(quality, StringComparison.OrdinalIgnoreCase)) &&
            (filter.Length == 0 || Matches(voice, filter)));
        query = sort switch
        {
            "name" => query.OrderBy(voice => voice.Name, StringComparer.CurrentCultureIgnoreCase)
                .ThenBy(voice => voice.LanguageLabel, StringComparer.CurrentCultureIgnoreCase)
                .ThenByDescending(voice => PiperCatalogVoice.QualityRank(voice.Quality)),
            "quality" => query.OrderByDescending(voice => PiperCatalogVoice.QualityRank(voice.Quality))
                .ThenBy(voice => voice.LanguageLabel, StringComparer.CurrentCultureIgnoreCase)
                .ThenBy(voice => voice.Name, StringComparer.CurrentCultureIgnoreCase),
            "size" => query.OrderBy(voice => voice.ModelSize)
                .ThenBy(voice => voice.LanguageLabel, StringComparer.CurrentCultureIgnoreCase)
                .ThenBy(voice => voice.Name, StringComparer.CurrentCultureIgnoreCase),
            _ => query.OrderBy(voice => voice.LanguageLabel, StringComparer.CurrentCultureIgnoreCase)
                .ThenBy(voice => voice.Name, StringComparer.CurrentCultureIgnoreCase)
                .ThenByDescending(voice => PiperCatalogVoice.QualityRank(voice.Quality))
        };
        var voices = query.ToList();
        rows.Clear();
        var items = new List<UIElement>(voices.Count);
        foreach (var voice in voices) items.Add(Row(voice));
        list.ItemsSource = items;
        summary.Text = voices.Count == installed.Count
            ? $"{installed.Count} downloaded voice{(installed.Count == 1 ? "" : "s")}."
            : $"{voices.Count} of {installed.Count} downloaded voices shown.";
    }

    private static bool Matches(PiperCatalogVoice voice, string filter) =>
        voice.DisplayName.Contains(filter, StringComparison.CurrentCultureIgnoreCase) ||
        voice.Language.Contains(filter, StringComparison.CurrentCultureIgnoreCase) ||
        voice.Country.Contains(filter, StringComparison.CurrentCultureIgnoreCase) ||
        voice.Quality.Contains(filter, StringComparison.CurrentCultureIgnoreCase) ||
        voice.QualityLabel.Contains(filter, StringComparison.CurrentCultureIgnoreCase);

    private UIElement Row(PiperCatalogVoice voice)
    {
        var state = new RowState();
        rows[voice.Id] = state;
        var removing = deletingVoiceId == voice.Id;
        var locked = deleting || deletingVoiceId != null;

        var grid = new Grid { ColumnSpacing = 12, Padding = new Thickness(6) };
        for (var column = 0; column < 4; column++)
            grid.ColumnDefinitions.Add(column == 0 ? new ColumnDefinition() : new ColumnDefinition { Width = GridLength.Auto });
        var details = new StackPanel { Spacing = 2 };
        details.Children.Add(new TextBlock { Text = voice.DisplayName, FontWeight = FontWeights.SemiBold, TextWrapping = TextWrapping.Wrap });
        details.Children.Add(new TextBlock { Text = voice.Note, Opacity = .7, FontSize = 12 });
        grid.Children.Add(details);

        state.Ring.IsActive = removing;
        state.Ring.Visibility = removing ? Visibility.Visible : Visibility.Collapsed;
        AutomationProperties.SetName(state.Ring, "Removing " + voice.DisplayName);
        Grid.SetColumn(state.Ring, 1);
        grid.Children.Add(state.Ring);

        state.Sample.Content = sampleLoadingId == voice.Id ? "Loading…" : sampleVoiceId == voice.Id ? "Stop sample" : "Play sample";
        state.Sample.IsEnabled = sampleLoadingId == null && !locked;
        AutomationProperties.SetName(state.Sample, (sampleVoiceId == voice.Id ? "Stop sample for " : "Play sample for ") + voice.DisplayName);
        AutomationProperties.SetHelpText(state.Sample,
            "Plays a short sample published with this voice. The preview needs the internet; a downloaded voice itself works offline.");
        state.Sample.Click += async (_, _) => await ToggleSampleAsync(voice);
        Grid.SetColumn(state.Sample, 2);
        grid.Children.Add(state.Sample);

        IconButtonContent.Set(state.Action, removing ? "Deleting…" : "Delete", Symbol.Delete);
        IconButtonContent.SetDestructive(state.Action, true);
        state.Action.IsEnabled = !locked;
        AutomationProperties.SetName(state.Action, removing ? "Deleting " + voice.DisplayName : "Delete " + voice.DisplayName);
        AutomationProperties.SetHelpText(state.Action, "Removes the downloaded model and its cached sample.");
        state.Action.Click += async (_, _) =>
        {
            if (deleting || deletingVoiceId != null) return;
            await DeleteAsync(voice);
        };
        Grid.SetColumn(state.Action, 3);
        grid.Children.Add(state.Action);
        return grid;
    }

    private async Task DeleteAsync(PiperCatalogVoice voice)
    {
        if (deleting || deletingVoiceId != null) return;
        if (sampleVoiceId == voice.Id) StopSample(null, rebuild: false);
        deletingVoiceId = voice.Id;
        progress.IsIndeterminate = true;
        progress.Visibility = Visibility.Visible;
        Rebuild();
        status.Text = $"Removing {voice.DisplayName} and its cached sample…";
        string message;
        try
        {
            await Task.Run(() => PiperService.DeleteVoice(voice.Id));
            message = $"{voice.DisplayName} was removed.";
        }
        catch (Exception ex) { message = "The voice could not be removed. " + ex.Message; }
        finally
        {
            deletingVoiceId = null;
            progress.IsIndeterminate = false;
            progress.Visibility = Visibility.Collapsed;
        }
        PopulateFilters();
        Rebuild();
        status.Text = message;
    }

    private async Task ToggleSampleAsync(PiperCatalogVoice voice)
    {
        if (sampleLoadingId != null || deleting || deletingVoiceId != null) return;
        if (sampleVoiceId == voice.Id)
        {
            StopSample("Sample stopped.");
            return;
        }
        StopSample(null, rebuild: false);
        sampleLoadingId = voice.Id;
        Rebuild();
        status.Text = $"Loading a sample of {voice.DisplayName}…";
        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token);
            timeout.CancelAfter(TimeSpan.FromSeconds(30));
            var path = await PiperService.GetSampleAsync(voice, timeout.Token);
            if (lifetime.IsCancellationRequested) return;
            samplePlayer.Source = MediaSource.CreateFromUri(new Uri(path));
            samplePlayer.Play();
            sampleVoiceId = voice.Id;
            status.Text = $"Playing a sample of {voice.DisplayName}.";
        }
        catch (OperationCanceledException)
        {
            if (!lifetime.IsCancellationRequested)
                status.Text = "The sample took too long to load. Check the internet connection and try again.";
        }
        catch (Exception ex)
        {
            status.Text = "The sample could not be played. " + FriendlySampleError(ex);
        }
        finally
        {
            sampleLoadingId = null;
            Rebuild();
        }
    }

    private void StopSample(string? message, bool rebuild = true)
    {
        sampleVoiceId = null;
        samplePlayer.Pause();
        samplePlayer.Source = null;
        if (message != null) status.Text = message;
        if (rebuild) Rebuild();
    }

    private void FinishSample(string message)
    {
        if (sampleVoiceId == null) return;
        sampleVoiceId = null;
        samplePlayer.Source = null;
        status.Text = message;
        Rebuild();
    }

    private static string FriendlySampleError(Exception ex) => ex is HttpRequestException or IOException
        ? "Check the internet connection and try again."
        : ex.Message;
}
