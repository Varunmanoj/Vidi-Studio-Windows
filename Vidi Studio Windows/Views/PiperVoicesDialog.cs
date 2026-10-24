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

namespace Vidi_Studio_Windows.Views;

/// <summary>Downloads and removes the Piper voice models used by the bundled speech engine.</summary>
public sealed class PiperVoicesDialog : ContentDialog
{
    private sealed record FilterOption(string Id, string Label)
    {
        public override string ToString() => Label;
    }

    private readonly TextBox search = new() { Header = "Search voices", PlaceholderText = "Name, language, dialect, or quality" };
    private readonly ComboBox languageFilter = new() { Header = "Language", HorizontalAlignment = HorizontalAlignment.Stretch };
    private readonly ComboBox qualityFilter = new() { Header = "Quality", HorizontalAlignment = HorizontalAlignment.Stretch };
    private readonly ComboBox sortFilter = new() { Header = "Sort by", HorizontalAlignment = HorizontalAlignment.Stretch };
    private readonly ListView list = new() { SelectionMode = ListViewSelectionMode.None, Height = 320 };
    private readonly TextBlock summary = new() { TextWrapping = TextWrapping.Wrap, Opacity = .75 };
    private readonly TextBlock status = new() { TextWrapping = TextWrapping.Wrap };
    private readonly ProgressBar progress = new() { Minimum = 0, Maximum = 100, Visibility = Visibility.Collapsed };
    private readonly CancellationTokenSource lifetime = new();
    private readonly MediaPlayer samplePlayer = new() { AudioCategory = MediaPlayerAudioCategory.Speech };
    private bool busy;
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
            Text = "Choose the voices you want to use with Piper. Filter by language or quality, then use Play sample to hear a short hosted sample before downloading. Downloads can be large, so the progress bar below the list shows each file as it arrives. Downloaded voices work offline in live narration, generated narration audio, voice samples, and summary read-aloud." });
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
        list.ItemsSource = voices.Select(Row).ToList();
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
        var downloaded = PiperService.IsVoiceDownloaded(voice.Id);
        var grid = new Grid { ColumnSpacing = 12, Padding = new Thickness(6, 6, 6, 6) };
        grid.ColumnDefinitions.Add(new ColumnDefinition());
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var details = new StackPanel { Spacing = 2 };
        details.Children.Add(new TextBlock { Text = voice.DisplayName, FontWeight = FontWeights.SemiBold, TextWrapping = TextWrapping.Wrap });
        details.Children.Add(new TextBlock { Text = voice.Note, Opacity = .7, FontSize = 12 });
        grid.Children.Add(details);

        var sample = new Button
        {
            Content = sampleLoadingId == voice.Id ? "Loading…" : sampleVoiceId == voice.Id ? "Stop sample" : "Play sample",
            MinWidth = 110,
            IsEnabled = sampleLoadingId == null,
            VerticalAlignment = VerticalAlignment.Center
        };
        AutomationProperties.SetName(sample, (sampleVoiceId == voice.Id ? "Stop sample for " : "Play sample for ") + voice.DisplayName);
        AutomationProperties.SetHelpText(sample,
            "Plays a short sample published with this voice. The preview needs the internet; a downloaded voice itself works offline.");
        sample.Click += async (_, _) => await ToggleSampleAsync(voice);
        Grid.SetColumn(sample, 1);
        grid.Children.Add(sample);

        var action = new Button { Content = downloaded ? "Delete" : "Download", MinWidth = 100, VerticalAlignment = VerticalAlignment.Center };
        AutomationProperties.SetName(action, (downloaded ? "Delete " : "Download ") + voice.DisplayName);
        action.Click += async (_, _) =>
        {
            if (downloaded) { PiperService.DeleteVoice(voice.Id); Rebuild(); }
            else await DownloadAsync(voice);
        };
        Grid.SetColumn(action, 2);
        grid.Children.Add(action);
        return grid;
    }

    private async Task ToggleSampleAsync(PiperCatalogVoice voice)
    {
        if (sampleLoadingId != null) return;
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

    private async Task DownloadAsync(PiperCatalogVoice voice)
    {
        if (busy) return;
        busy = true;
        list.IsEnabled = false;
        search.IsEnabled = false;
        languageFilter.IsEnabled = false;
        qualityFilter.IsEnabled = false;
        sortFilter.IsEnabled = false;
        progress.Value = 0;
        progress.Visibility = Visibility.Visible;
        status.Text = $"Downloading {voice.DisplayName}…";
        string message;
        try
        {
            var reporter = new Progress<double>(value => DispatcherQueue.TryEnqueue(() =>
            {
                progress.Value = Math.Clamp(value, 0, 1) * 100;
                status.Text = $"Downloading {voice.DisplayName}… {value:P0}";
            }));
            await PiperService.DownloadVoiceAsync(voice, reporter, lifetime.Token);
            message = $"{voice.DisplayName} is ready to use.";
        }
        catch (OperationCanceledException) { message = "The download was canceled."; }
        catch (Exception ex) { message = "The voice could not be downloaded. " + ex.Message; }
        finally
        {
            busy = false;
            list.IsEnabled = true;
            search.IsEnabled = true;
            languageFilter.IsEnabled = true;
            qualityFilter.IsEnabled = true;
            sortFilter.IsEnabled = true;
            progress.Visibility = Visibility.Collapsed;
        }
        Rebuild();
        status.Text = message;
    }
}
