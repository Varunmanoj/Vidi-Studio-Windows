using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
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

/// <summary>Downloads and removes the Piper voice models used by the bundled speech engine.</summary>
public sealed class PiperVoicesDialog : ContentDialog
{
    private sealed record FilterOption(string Id, string Label)
    {
        public override string ToString() => Label;
    }

    /// <summary>The controls a voice row needs so in-place progress never rebuilds the whole list.</summary>
    private sealed class RowState
    {
        public Button Sample { get; } = new() { Width = 120, VerticalAlignment = VerticalAlignment.Center };
        public Button Action { get; } = new() { Width = 150, VerticalAlignment = VerticalAlignment.Center };
        public ProgressRing Ring { get; } = new()
        {
            Width = 18, Height = 18, IsActive = false,
            Visibility = Visibility.Collapsed, VerticalAlignment = VerticalAlignment.Center
        };
        public FontIcon SamplePlaying { get; } = new()
        {
            Glyph = "\uE9D9", FontSize = 16, Width = 20, Height = 20,
            Visibility = Visibility.Collapsed, VerticalAlignment = VerticalAlignment.Center
        };
        public Grid SampleStatus { get; } = new()
        {
            Width = 20, Height = 20, VerticalAlignment = VerticalAlignment.Center
        };
        public ProgressRing SampleLoading { get; } = new()
        {
            Width = 16, Height = 16, IsActive = false,
            Visibility = Visibility.Collapsed, VerticalAlignment = VerticalAlignment.Center
        };
        public RowState()
        {
            SampleStatus.Children.Add(SampleLoading);
            SampleStatus.Children.Add(SamplePlaying);
        }
        public ProgressBar Bar { get; } = new()
        {
            Minimum = 0, Maximum = 100, Height = 4, Margin = new Thickness(0, 4, 0, 0),
            Visibility = Visibility.Collapsed
        };
        public TextBlock ProgressText { get; } = new() { FontSize = 12, Opacity = .75, Visibility = Visibility.Collapsed };
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
    private bool busy;
    private string? downloadingVoiceId;
    private string? deletingVoiceId;
    private double downloadFraction;
    private string? sampleLoadingId;
    private string? sampleVoiceId;

    public PiperVoicesDialog()
    {
        Title = "Piper Voices";
        CloseButtonText = "Close";
        DefaultButton = ContentDialogButton.Close;
        Resources["ContentDialogMaxWidth"] = 820d;
        Resources["ContentDialogMaxHeight"] = 900d;
        var body = new StackPanel { Spacing = 10, Width = 720 };
        body.Children.Add(new TextBlock { TextWrapping = TextWrapping.Wrap, Opacity = .75,
            Text = "Download neural voices for offline narration. Filter or sort the catalog, then play a sample before downloading." });
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
        AutomationProperties.SetName(progress, "Voice download or removal progress");
        AutomationProperties.SetName(list, "Piper voice catalog");

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
        status.Text = PiperService.IsRuntimeAvailable
            ? "The bundled Piper speech engine is ready."
            : "The bundled Piper speech engine is missing. Reinstall Vidi Studio, or run script\\fetch-piper.ps1 and build the app again.";
    }

    private void PopulateFilters()
    {
        var languages = PiperService.Catalog
            .GroupBy(voice => voice.LanguageFamily, StringComparer.OrdinalIgnoreCase)
            .Select(group => new FilterOption(group.Key, group.First().LanguageName))
            .OrderBy(option => option.Label, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
        languages.Insert(0, new("", "All languages"));
        languageFilter.ItemsSource = languages;
        languageFilter.SelectedIndex = 0;

        var qualities = PiperService.Catalog
            .Select(voice => voice.Quality)
            .Where(quality => !string.IsNullOrWhiteSpace(quality))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderByDescending(PiperCatalogVoice.QualityRank)
            .Select(quality => new FilterOption(quality, PiperCatalogVoice.QualityLabelFor(quality)))
            .ToList();
        qualities.Insert(0, new("", "All qualities"));
        qualityFilter.ItemsSource = qualities;
        qualityFilter.SelectedIndex = 0;

        sortFilter.ItemsSource = new List<FilterOption>
        {
            new("language", "Language (A–Z)"),
            new("name", "Voice name (A–Z)"),
            new("quality", "Quality (high to low)"),
            new("size", "Size (smallest first)")
        };
        sortFilter.SelectedIndex = 0;
    }

    private void Rebuild()
    {
        var filter = search.Text.Trim();
        var language = (languageFilter.SelectedItem as FilterOption)?.Id ?? "";
        var quality = (qualityFilter.SelectedItem as FilterOption)?.Id ?? "";
        var sort = (sortFilter.SelectedItem as FilterOption)?.Id ?? "language";
        IEnumerable<PiperCatalogVoice> query = PiperService.Catalog.Where(voice =>
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
        var installed = PiperService.Catalog.Count(voice => PiperService.IsVoiceDownloaded(voice.Id));
        summary.Text = voices.Count == PiperService.Catalog.Count
            ? $"{PiperService.Catalog.Count} voices in the catalog · {installed} downloaded."
            : $"{voices.Count} of {PiperService.Catalog.Count} voices shown · {installed} downloaded.";
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
        var downloaded = PiperService.IsVoiceDownloaded(voice.Id);
        var downloading = downloadingVoiceId == voice.Id;
        var deleting = deletingVoiceId == voice.Id;
        var locked = busy || deletingVoiceId != null;

        var grid = new Grid { ColumnSpacing = 12, Padding = new Thickness(6) };
        for (var column = 0; column < 5; column++)
            grid.ColumnDefinitions.Add(column == 0 ? new ColumnDefinition() : new ColumnDefinition { Width = GridLength.Auto });
        var details = new StackPanel { Spacing = 2 };
        details.Children.Add(new TextBlock { Text = voice.DisplayName, FontWeight = FontWeights.SemiBold, TextWrapping = TextWrapping.Wrap });
        details.Children.Add(new TextBlock { Text = voice.Note, Opacity = .7, FontSize = 12 });

        state.Bar.Value = downloading ? downloadFraction * 100 : 0;
        state.Bar.Visibility = downloading ? Visibility.Visible : Visibility.Collapsed;
        AutomationProperties.SetName(state.Bar, "Download progress for " + voice.DisplayName);
        details.Children.Add(state.Bar);
        state.ProgressText.Text = downloading ? ProgressLabel(voice, downloadFraction) : "";
        state.ProgressText.Visibility = downloading ? Visibility.Visible : Visibility.Collapsed;
        details.Children.Add(state.ProgressText);
        grid.Children.Add(details);

        state.Ring.IsActive = downloading || deleting;
        state.Ring.Visibility = downloading || deleting ? Visibility.Visible : Visibility.Collapsed;
        AutomationProperties.SetName(state.Ring, (deleting ? "Removing " : "Downloading ") + voice.DisplayName);
        Grid.SetColumn(state.Ring, 1);
        grid.Children.Add(state.Ring);

        state.SamplePlaying.Visibility = sampleVoiceId == voice.Id ? Visibility.Visible : Visibility.Collapsed;
        AutomationProperties.SetName(state.SamplePlaying, "Playing a sample of " + voice.DisplayName);
        state.SampleLoading.IsActive = sampleLoadingId == voice.Id;
        state.SampleLoading.Visibility = sampleLoadingId == voice.Id ? Visibility.Visible : Visibility.Collapsed;
        AutomationProperties.SetName(state.SampleLoading, "Loading a sample of " + voice.DisplayName);
        Grid.SetColumn(state.SampleStatus, 2);
        grid.Children.Add(state.SampleStatus);

        state.Sample.Content = sampleLoadingId == voice.Id ? "Loading…" : sampleVoiceId == voice.Id ? "Stop sample" : "Play sample";
        state.Sample.IsEnabled = sampleLoadingId == null && !locked;
        AutomationProperties.SetName(state.Sample, (sampleVoiceId == voice.Id ? "Stop sample for " : "Play sample for ") + voice.DisplayName);
        AutomationProperties.SetHelpText(state.Sample,
            "Plays a short sample published with this voice. The preview needs the internet; a downloaded voice itself works offline.");
        state.Sample.Click += async (_, _) => await ToggleSampleAsync(voice);
        Grid.SetColumn(state.Sample, 3);
        grid.Children.Add(state.Sample);

        IconButtonContent.Set(state.Action,
            deleting ? "Deleting…" : downloading ? "Downloading…" : downloaded ? "Delete" : "Download",
            downloaded || deleting ? Symbol.Delete : Symbol.Download);
        IconButtonContent.SetDestructive(state.Action, downloaded || deleting);
        state.Action.IsEnabled = !locked && !downloading && !deleting;
        AutomationProperties.SetName(state.Action, deleting ? "Deleting " + voice.DisplayName
            : downloading ? "Downloading " + voice.DisplayName
            : (downloaded ? "Delete " : "Download ") + voice.DisplayName);
        AutomationProperties.SetHelpText(state.Action, deleting
            ? "Removes the downloaded model and its cached sample."
            : downloaded ? "Removes the downloaded model and its cached sample." : "Downloads and verifies this voice model.");
        state.Action.Click += async (_, _) =>
        {
            if (busy || deletingVoiceId != null) return;
            if (!downloaded) await DownloadAsync(voice);
            else await DeleteAsync(voice);
        };
        Grid.SetColumn(state.Action, 4);
        grid.Children.Add(state.Action);
        return grid;
    }

    private static string ProgressLabel(PiperCatalogVoice voice, double fraction)
    {
        var total = voice.ModelSize + voice.ConfigSize;
        return $"{fraction:P0} · {SizeText((long)(total * fraction))} of {SizeText(total)}";
    }

    private static string SizeText(long bytes) => bytes >= 1_000_000
        ? $"{bytes / 1_000_000.0:0.#} MB"
        : $"{Math.Max(1, bytes / 1_000)} KB";

    private void ReportDownload(PiperCatalogVoice voice, double value)
    {
        downloadFraction = Math.Clamp(value, 0, 1);
        progress.Value = downloadFraction * 100;
        status.Text = $"Downloading {voice.DisplayName}… {downloadFraction:P0}";
        if (!rows.TryGetValue(voice.Id, out var state)) return;
        state.Bar.Value = downloadFraction * 100;
        state.ProgressText.Text = ProgressLabel(voice, downloadFraction);
    }

    private async Task DownloadAsync(PiperCatalogVoice voice)
    {
        if (busy) return;
        busy = true;
        downloadingVoiceId = voice.Id;
        downloadFraction = 0;
        progress.IsIndeterminate = false;
        progress.Value = 0;
        progress.Visibility = Visibility.Visible;
        search.IsEnabled = false;
        languageFilter.IsEnabled = false;
        qualityFilter.IsEnabled = false;
        sortFilter.IsEnabled = false;
        Rebuild();
        status.Text = $"Downloading {voice.DisplayName}…";
        string message;
        try
        {
            var reporter = new Progress<double>(value => DispatcherQueue.TryEnqueue(() => ReportDownload(voice, value)));
            await PiperService.DownloadVoiceAsync(voice, reporter, lifetime.Token);
            message = $"{voice.DisplayName} is ready to use.";
        }
        catch (OperationCanceledException) { message = "The download was canceled."; }
        catch (Exception ex) { message = "The voice could not be downloaded. " + ex.Message; }
        finally
        {
            busy = false;
            downloadingVoiceId = null;
            downloadFraction = 0;
            progress.Visibility = Visibility.Collapsed;
            search.IsEnabled = true;
            languageFilter.IsEnabled = true;
            qualityFilter.IsEnabled = true;
            sortFilter.IsEnabled = true;
        }
        Rebuild();
        status.Text = message;
    }

    private async Task DeleteAsync(PiperCatalogVoice voice)
    {
        if (busy || deletingVoiceId != null) return;
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
        Rebuild();
        status.Text = message;
    }

    private async Task ToggleSampleAsync(PiperCatalogVoice voice)
    {
        if (sampleLoadingId != null || busy || deletingVoiceId != null) return;
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
        // Clear the id first so a Source change cannot look like a failed playback.
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
