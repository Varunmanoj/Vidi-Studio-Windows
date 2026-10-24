using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Vidi_Studio_Windows.Services;

namespace Vidi_Studio_Windows.Views;

/// <summary>Downloads and removes the Piper voice models used by the bundled speech engine.</summary>
public sealed class PiperVoicesDialog : ContentDialog
{
    private readonly TextBox search = new() { Header = "Search voices", PlaceholderText = "Language, name, or quality" };
    private readonly ListView list = new() { SelectionMode = ListViewSelectionMode.None, Height = 360 };
    private readonly TextBlock summary = new() { TextWrapping = TextWrapping.Wrap, Opacity = .75 };
    private readonly TextBlock status = new() { TextWrapping = TextWrapping.Wrap };
    private readonly ProgressBar progress = new() { Minimum = 0, Maximum = 100, Visibility = Visibility.Collapsed };
    private readonly CancellationTokenSource lifetime = new();
    private bool busy;

    public PiperVoicesDialog()
    {
        Title = "Piper Voices";
        CloseButtonText = "Close";
        DefaultButton = ContentDialogButton.Close;
        Resources["ContentDialogMaxWidth"] = 820d;
        Resources["ContentDialogMaxHeight"] = 900d;
        var body = new StackPanel { Spacing = 10, Width = 720 };
        body.Children.Add(new TextBlock { TextWrapping = TextWrapping.Wrap, Opacity = .75,
            Text = "Choose the voices you want to use with Piper. Downloads can be large, so the progress bar below the list shows each file as it arrives. Downloaded voices work offline in live narration, generated narration audio, voice samples, and summary read-aloud." });
        body.Children.Add(search);
        body.Children.Add(list);
        body.Children.Add(progress);
        body.Children.Add(status);
        body.Children.Add(summary);
        Content = body;
        search.TextChanged += (_, _) => Rebuild();
        Closed += (_, _) => lifetime.Cancel();
        Rebuild();
        status.Text = PiperService.IsRuntimeAvailable
            ? "The bundled Piper speech engine is ready."
            : "The bundled Piper speech engine is missing. Reinstall Vidi Studio, or run script\\fetch-piper.ps1 and build the app again.";
    }

    private void Rebuild()
    {
        var filter = search.Text.Trim();
        var voices = PiperService.Catalog.Where(voice =>
            filter.Length == 0 ||
            voice.DisplayName.Contains(filter, StringComparison.CurrentCultureIgnoreCase) ||
            voice.Language.Contains(filter, StringComparison.CurrentCultureIgnoreCase) ||
            voice.Quality.Contains(filter, StringComparison.CurrentCultureIgnoreCase)).ToList();
        list.ItemsSource = voices.Select(Row).ToList();
        var installed = PiperService.Catalog.Count(voice => PiperService.IsVoiceDownloaded(voice.Id));
        summary.Text = voices.Count == PiperService.Catalog.Count
            ? $"{PiperService.Catalog.Count} voices in the catalog · {installed} downloaded."
            : $"{voices.Count} of {PiperService.Catalog.Count} voices shown · {installed} downloaded.";
    }

    private UIElement Row(PiperCatalogVoice voice)
    {
        var downloaded = PiperService.IsVoiceDownloaded(voice.Id);
        var grid = new Grid { ColumnSpacing = 12, Padding = new Thickness(6, 6, 6, 6) };
        grid.ColumnDefinitions.Add(new ColumnDefinition());
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var details = new StackPanel { Spacing = 2 };
        details.Children.Add(new TextBlock { Text = voice.DisplayName, FontWeight = FontWeights.SemiBold, TextWrapping = TextWrapping.Wrap });
        details.Children.Add(new TextBlock { Text = voice.Note, Opacity = .7, FontSize = 12 });
        grid.Children.Add(details);
        var action = new Button { Content = downloaded ? "Delete" : "Download", MinWidth = 100, VerticalAlignment = VerticalAlignment.Center };
        AutomationProperties.SetName(action, (downloaded ? "Delete " : "Download ") + voice.DisplayName);
        action.Click += async (_, _) =>
        {
            if (downloaded) { PiperService.DeleteVoice(voice.Id); Rebuild(); }
            else await DownloadAsync(voice);
        };
        Grid.SetColumn(action, 1);
        grid.Children.Add(action);
        return grid;
    }

    private async Task DownloadAsync(PiperCatalogVoice voice)
    {
        if (busy) return;
        busy = true;
        list.IsEnabled = false;
        search.IsEnabled = false;
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
            progress.Visibility = Visibility.Collapsed;
        }
        Rebuild();
        status.Text = message;
    }
}
