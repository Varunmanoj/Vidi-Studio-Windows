using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Vidi_Studio_Windows.Services;
using Windows.Media.Core;
using Windows.Storage;
using Windows.Storage.Pickers;

namespace Vidi_Studio_Windows.Views;

/// <summary>Inspects a source without creating a project or copying the video.</summary>
public sealed class NewProjectDialog : ContentDialog
{
    private readonly Window owner;
    private readonly SettingsStore settings;
    private readonly RadioButton local = new() { GroupName = "project-source" };
    private readonly RadioButton youtube = new() { GroupName = "project-source" };
    private readonly StackPanel localFields = new() { Spacing = 12, Width = 410 };
    private readonly StackPanel youtubeFields = new() { Spacing = 12, Width = 410 };
    private readonly TextBlock selectedFile = new() { Text = "No video selected", Opacity = .7, TextWrapping = TextWrapping.Wrap };
    private readonly TextBlock details = new() { TextWrapping = TextWrapping.Wrap };
    private readonly MediaPlayerElement preview = new() { Height = 180, AreTransportControlsEnabled = true, Visibility = Visibility.Collapsed };
    private readonly TextBox youtubeUrl = new() { PlaceholderText = "https://youtu.be/…", Header = "YouTube video URL", Width = 280, HorizontalAlignment = HorizontalAlignment.Left };
    private readonly Button fetchDetails = new() { Content = "Fetch Details", Width = 120, VerticalAlignment = VerticalAlignment.Bottom };
    private readonly Border youtubePreview = new() { Visibility = Visibility.Collapsed, Width = 410, CornerRadius = new CornerRadius(8), Padding = new Thickness(12), Background = new SolidColorBrush(Windows.UI.Color.FromArgb(24, 128, 128, 128)) };
    private readonly TextBlock youtubeStatus = new() { Width = 410, TextWrapping = TextWrapping.Wrap, Opacity = .7 };
    private readonly TextBlock sourceHeading = new() { Text = "Video File", FontSize = 18, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold };
    private static readonly HttpClient http = new() { Timeout = TimeSpan.FromSeconds(15) };
    private CancellationTokenSource? inspection;
    private CancellationTokenSource? youtubeInspection;
    private string? fetchedYouTubeId;
    private bool validLocal;
    public bool IsYouTube => youtube.IsChecked == true;
    public string? VideoPath { get; private set; }
    public string YouTubeUrl => youtubeUrl.Text.Trim();
#if DEBUG
    internal async Task InspectVideoForSmokeTestAsync(string path) => await InspectVideoAsync(await StorageFile.GetFileFromPathAsync(path));
    internal string DetailsForSmokeTest => details.Text;
    internal FrameworkElement YouTubeFieldsForSmokeTest => youtubeFields;
    internal void SelectYouTubeForSmokeTest(string url) { youtube.IsChecked = true; youtubeUrl.Text = url; UpdateYouTubeDetails(); }
    internal void SetYouTubeDetailsForSmokeTest(string title)
    {
        fetchedYouTubeId = ValidYouTubeId(YouTubeUrl);
        ShowYouTubePreview(fetchedYouTubeId!, title, "Test channel", $"https://i.ytimg.com/vi/{fetchedYouTubeId}/hqdefault.jpg");
        UpdateSource();
    }
#endif

    public NewProjectDialog(Window owner, ElementTheme theme, SettingsStore settings, bool openYouTube = false)
    {
        this.owner = owner; this.settings = settings;
        RequestedTheme = theme;
        Width = 520; MaxWidth = 560;
        Title = "New Project"; PrimaryButtonText = "Create Project"; CloseButtonText = "Cancel";
        DefaultButton = ContentDialogButton.Primary;
        local.Content = SourceLabel("\uE8B2", "Video File");
        youtube.Content = SourceLabel("\uE768", "YouTube Video");
        var chooser = new Button { Content = SourceLabel("\uE8B7", "Choose Video…"), HorizontalAlignment = HorizontalAlignment.Right };
        chooser.Click += async (_, _) => await ChooseVideoAsync();
        var fileRow = new Grid { ColumnSpacing = 12, Width = 410 };
        fileRow.ColumnDefinitions.Add(new ColumnDefinition());
        fileRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        fileRow.Children.Add(new TextBlock { Text = "Video file", VerticalAlignment = VerticalAlignment.Center });
        Grid.SetColumn(chooser, 1); fileRow.Children.Add(chooser);
        localFields.Children.Add(fileRow);
        localFields.Children.Add(selectedFile);
        localFields.Children.Add(preview);
        localFields.Children.Add(details);
        var youtubeRow = new Grid { ColumnSpacing = 10, Width = 410 };
        youtubeRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(280) });
        youtubeRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        youtubeRow.Children.Add(youtubeUrl);
        Grid.SetColumn(fetchDetails, 1); youtubeRow.Children.Add(fetchDetails);
        youtubeFields.Children.Add(youtubeRow);
        youtubeFields.Children.Add(youtubePreview);
        youtubeFields.Children.Add(youtubeStatus);
        fetchDetails.Click += async (_, _) => await FetchYouTubeDetailsAsync();
        var sourceOptions = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 12 };
        sourceOptions.Children.Add(local); sourceOptions.Children.Add(youtube);
        var sourceRow = new StackPanel { Spacing = 5, Width = 410 };
        sourceRow.Children.Add(new TextBlock { Text = "Project type" });
        sourceRow.Children.Add(sourceOptions);
        var contents = new StackPanel { Spacing = 18, Width = 440 };
        contents.Children.Add(new TextBlock { Text = "Choose a project source", FontSize = 18, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold });
        contents.Children.Add(new Border { Background = new SolidColorBrush(Windows.UI.Color.FromArgb(20, 128, 128, 128)), CornerRadius = new CornerRadius(8), Padding = new Thickness(12), Child = sourceRow });
        contents.Children.Add(sourceHeading);
        contents.Children.Add(new Border { Background = new SolidColorBrush(Windows.UI.Color.FromArgb(20, 128, 128, 128)), CornerRadius = new CornerRadius(8), Padding = new Thickness(14), Child = localFields });
        contents.Children.Add(new Border { Background = new SolidColorBrush(Windows.UI.Color.FromArgb(20, 128, 128, 128)), CornerRadius = new CornerRadius(8), Padding = new Thickness(14), Child = youtubeFields });
        Content = new ScrollViewer { Content = contents, MaxHeight = 650, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };
        local.Checked += (_, _) => UpdateSource(); youtube.Checked += (_, _) => UpdateSource();
        youtubeUrl.TextChanged += (_, _) => UpdateYouTubeDetails();
        PrimaryButtonClick += (_, args) => { if (!IsPrimaryButtonEnabled) args.Cancel = true; };
        Closing += (_, _) => { inspection?.Cancel(); youtubeInspection?.Cancel(); preview.Source = null; };
        if (openYouTube) youtube.IsChecked = true; else local.IsChecked = true;
        UpdateSource();
    }

    private static StackPanel SourceLabel(string glyph, string label)
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        row.Children.Add(new FontIcon { Glyph = glyph, FontSize = 18 });
        row.Children.Add(new TextBlock { Text = label, VerticalAlignment = VerticalAlignment.Center });
        return row;
    }
    private void UpdateSource()
    {
        if (localFields.Parent is Border localCard) localCard.Visibility = IsYouTube ? Visibility.Collapsed : Visibility.Visible;
        if (youtubeFields.Parent is Border youtubeCard) youtubeCard.Visibility = IsYouTube ? Visibility.Visible : Visibility.Collapsed;
        sourceHeading.Text = IsYouTube ? "YouTube Video" : "Video File";
        if (IsYouTube) preview.MediaPlayer?.Pause();
        fetchDetails.IsEnabled = ValidYouTubeId(YouTubeUrl) != null && youtubeInspection == null;
        IsPrimaryButtonEnabled = IsYouTube ? fetchedYouTubeId != null && fetchedYouTubeId == ValidYouTubeId(YouTubeUrl) : validLocal;
    }
    private static string? ValidYouTubeId(string text)
    {
        if (!Uri.TryCreate(text, UriKind.Absolute, out var uri) || uri.Scheme != "https") return null;
        var host = uri.Host.ToLowerInvariant();
        if (host is "youtu.be" or "www.youtu.be")
        {
            var id = uri.AbsolutePath.Trim('/'); return Regex.IsMatch(id, "^[A-Za-z0-9_-]{11}$") ? id : null;
        }
        if (host is not ("youtube.com" or "www.youtube.com" or "m.youtube.com" or "youtube-nocookie.com" or "www.youtube-nocookie.com")) return null;
        if (uri.AbsolutePath is "/watch")
        {
            var match = Regex.Match(uri.Query, @"(?:\?|&)v=([A-Za-z0-9_-]{11})(?:&|$)");
            return match.Success ? match.Groups[1].Value : null;
        }
        var path = uri.AbsolutePath.Trim('/').Split('/');
        return path.Length == 2 && (path[0] is "shorts" or "embed" or "live") && Regex.IsMatch(path[1], "^[A-Za-z0-9_-]{11}$") ? path[1] : null;
    }
    private void UpdateYouTubeDetails()
    {
        youtubeInspection?.Cancel(); youtubeInspection = null;
        fetchedYouTubeId = null; youtubePreview.Visibility = Visibility.Collapsed;
        youtubeStatus.Text = "";
        UpdateSource();
    }
    private async Task FetchYouTubeDetailsAsync()
    {
        var id = ValidYouTubeId(YouTubeUrl);
        if (id == null) return;
        youtubeInspection?.Cancel();
        var request = new CancellationTokenSource(); youtubeInspection = request;
        fetchedYouTubeId = null; youtubePreview.Visibility = Visibility.Collapsed;
        youtubeStatus.Text = "Fetching video details…"; UpdateSource();
        try
        {
            var url = "https://www.youtube.com/watch?v=" + id;
            using var response = await http.GetAsync("https://www.youtube.com/oembed?format=json&url=" + Uri.EscapeDataString(url), request.Token);
            response.EnsureSuccessStatusCode();
            using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(request.Token));
            if (youtubeInspection != request || request.IsCancellationRequested || id != ValidYouTubeId(YouTubeUrl)) return;
            var root = document.RootElement;
            var title = root.GetProperty("title").GetString() ?? "YouTube video";
            var author = root.TryGetProperty("author_name", out var name) ? name.GetString() ?? "" : "";
            var thumbnail = root.TryGetProperty("thumbnail_url", out var image) ? image.GetString() ?? "" : "";
            fetchedYouTubeId = id;
            ShowYouTubePreview(id, title, author, thumbnail);
            youtubeStatus.Text = "";
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            if (youtubeInspection == request)
                youtubeStatus.Text = "Video details could not be fetched. Check the URL and network connection, then retry. " + ex.Message;
        }
        finally
        {
            if (youtubeInspection == request) { youtubeInspection = null; UpdateSource(); }
            request.Dispose();
        }
    }
    private void ShowYouTubePreview(string id, string title, string author, string thumbnail)
    {
        var card = new Grid { ColumnSpacing = 10 };
        card.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(148) }); card.ColumnDefinitions.Add(new ColumnDefinition());
        var image = new Image { Width = 148, Height = 84, Stretch = Stretch.UniformToFill };
        if (Uri.TryCreate(thumbnail, UriKind.Absolute, out var imageUrl) && imageUrl.Scheme == "https") image.Source = new BitmapImage(imageUrl);
        card.Children.Add(image);
        var information = new StackPanel { Spacing = 5, VerticalAlignment = VerticalAlignment.Center };
        information.Children.Add(new TextBlock { Text = "Project name", Opacity = .65, FontSize = 12 });
        information.Children.Add(new TextBlock { Text = title, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold, TextWrapping = TextWrapping.Wrap });
        if (author.Length > 0) information.Children.Add(new TextBlock { Text = "Channel: " + author, TextWrapping = TextWrapping.Wrap });
        information.Children.Add(new TextBlock { Text = "https://www.youtube.com/watch?v=" + id, Opacity = .7, FontSize = 12, TextWrapping = TextWrapping.Wrap });
        Grid.SetColumn(information, 1); card.Children.Add(information);
        youtubePreview.Child = card; youtubePreview.Visibility = Visibility.Visible;
    }
    private async Task ChooseVideoAsync()
    {
        var picker = new FileOpenPicker();
        foreach (var ext in new[] { ".mp4", ".mov", ".m4v", ".mkv", ".avi", ".webm" }) picker.FileTypeFilter.Add(ext);
        WinRT.Interop.InitializeWithWindow.Initialize(picker, WinRT.Interop.WindowNative.GetWindowHandle(owner));
        var file = await picker.PickSingleFileAsync();
        if (file == null) return;
        await InspectVideoAsync(file);
    }
    private async Task InspectVideoAsync(StorageFile file)
    {
        inspection?.Cancel();
        var request = new CancellationTokenSource(); inspection = request;
        validLocal = false; VideoPath = null; UpdateSource();
        selectedFile.Text = file.Name;
        details.Text = "Reading video details…";
        preview.Source = MediaSource.CreateFromStorageFile(file); preview.Visibility = Visibility.Visible;
        try
        {
            var metadata = await ProbeAsync(file.Path, request.Token);
            if (inspection != request || request.IsCancellationRequested) return;
            VideoPath = file.Path;
            validLocal = true;
            details.Text = metadata;
            UpdateSource();
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            if (inspection != request) return;
            details.Text = "Could not read this video's details. Check FFprobe in Settings → Storage and tools. " + ex.Message;
        }
        finally { if (inspection == request) inspection = null; request.Dispose(); }
    }
    private async Task<string> ProbeAsync(string path, CancellationToken token)
    {
        var executable = settings.Get("ffprobePath");
        if (string.IsNullOrWhiteSpace(executable)) executable = "ffprobe.exe";
        var start = new ProcessStartInfo(executable) { UseShellExecute = false, CreateNoWindow = true,
            RedirectStandardOutput = true, RedirectStandardError = true };
        foreach (var arg in new[] { "-v", "error", "-show_format", "-show_streams", "-of", "json", path }) start.ArgumentList.Add(arg);
        using var process = Process.Start(start) ?? throw new IOException("FFprobe could not start.");
        using var registration = token.Register(() => { try { process.Kill(true); } catch (InvalidOperationException) { } });
        var output = process.StandardOutput.ReadToEndAsync(token);
        var error = process.StandardError.ReadToEndAsync(token);
        await process.WaitForExitAsync(token);
        if (process.ExitCode != 0) throw new IOException((await error).Trim());
        using var document = JsonDocument.Parse(await output);
        var root = document.RootElement;
        var video = root.GetProperty("streams").EnumerateArray().FirstOrDefault(s => s.TryGetProperty("codec_type", out var kind) && kind.GetString() == "video");
        if (video.ValueKind != JsonValueKind.Object) throw new InvalidDataException("No video stream was found.");
        var format = root.GetProperty("format");
        string Value(JsonElement node, string key) => node.TryGetProperty(key, out var value) ? value.ToString() : "";
        var duration = double.TryParse(Value(format, "duration"), NumberStyles.Float, CultureInfo.InvariantCulture, out var seconds) && seconds > 0
            ? TimeSpan.FromSeconds(seconds).ToString(@"hh\:mm\:ss") : "Unknown";
        var frameRate = Value(video, "avg_frame_rate");
        var rateParts = frameRate.Split('/');
        if (rateParts.Length == 2 && double.TryParse(rateParts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out var numerator)
            && double.TryParse(rateParts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out var denominator) && denominator > 0)
            frameRate = $"{numerator / denominator:0.##} fps";
        var audio = root.GetProperty("streams").EnumerateArray().FirstOrDefault(s => s.TryGetProperty("codec_type", out var kind) && kind.GetString() == "audio");
        var audioLabel = audio.ValueKind == JsonValueKind.Object ? Value(audio, "codec_name") : "None";
        var bytes = double.TryParse(Value(format, "size"), NumberStyles.Float, CultureInfo.InvariantCulture, out var size) ? size : new FileInfo(path).Length;
        return $"Duration: {duration}    Picture: {Value(video, "width")} × {Value(video, "height")}    Frame rate: {frameRate}\nVideo: {Value(video, "codec_name")}    Audio: {audioLabel}    Size: {bytes / 1024 / 1024:0.0} MB";
    }
}
