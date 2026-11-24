using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Input;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using Vidi_Studio_Windows.Services;
using Vidi_Studio_Windows.Views;

namespace Vidi_Studio_Windows;

public sealed partial class MainWindow
{
#if DEBUG
    private bool youtubeLayoutFixture;
#endif
    private Func<string, bool>? youtubeBrowserCanRun;
    private Action<string>? youtubeBrowserRun;
    private static void OpenProjectInYouTube(Models.StudioProject project) {
        if (YouTubeService.ProjectWatchURL(project.YoutubeURL) is { } url) System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(url) { UseShellExecute = true });
    }
    private readonly HashSet<string> youtubeInspectorLoaded = new();
    private bool youtubeInspectorRefreshing;
    private void PopulateYouTubeInspector(Models.StudioProject project) {
        if (project.YouTubeMetadata is JsonElement { ValueKind: JsonValueKind.Object } value) {
            string Get(string key) => value.TryGetProperty(key, out var found) && found.ValueKind is not (JsonValueKind.Null or JsonValueKind.Undefined) && found.ToString().Length > 0 ? found.ToString() : "Unavailable";
            void Row(string label, string key) => AddInfo(YouTubeInspectorDetails, label, Get(key));
            AddInfo(YouTubeInspectorDetails, "Information source", "YouTube Data API · saved in this project");
            Row("YouTube title", "title"); Row("Channel", "channelTitle"); Row("Visibility", "visibility");
            Row("Published", "publishedAt"); Row("Views", "viewCount"); Row("Likes", "likeCount"); Row("Comments", "commentCount");
            AddInfo(YouTubeInspectorDetails, "Captions", Get("captionsAvailable") == "True" ? "Available" : "Not reported");
            Row("Language", "language"); Row("Audio language", "audioLanguage"); Row("Category", "categoryID");
            Row("Embedding allowed", "embeddable"); Row("Processing", "uploadStatus"); Row("Live status", "liveStatus");
            Row("Video ID", "videoID"); Row("Channel ID", "channelID");
            if (value.TryGetProperty("fetchedAt", out var at) && at.TryGetDouble(out var seconds)) AddInfo(YouTubeInspectorDetails, "Fetched", DateTimeOffset.FromUnixTimeSeconds((long)seconds).LocalDateTime.ToString("g"));
            foreach (var field in new[] { ("Description", "description"), ("Tags", "tags") }) {
                if (Get(field.Item2) == "Unavailable") continue;
                var text = value.GetProperty(field.Item2);
                var display = text.ValueKind == JsonValueKind.Array ? string.Join(", ", text.EnumerateArray().Select(item => item.ToString())) : text.ToString();
                YouTubeInspectorDetails.Children.Add(new Expander { Header = field.Item1, Content = new TextBlock { Text = display, TextWrapping = TextWrapping.Wrap, IsTextSelectionEnabled = true }, HorizontalAlignment = HorizontalAlignment.Stretch });
            }
        }
        var openYouTube = new Button { Content = "Open in YouTube" }; ToolTipService.SetToolTip(openYouTube, openYouTube.Content); openYouTube.Click += (_, _) => OpenProjectInYouTube(project); YouTubeInspectorDetails.Children.Add(openYouTube);
        var refresh = new Button { Content = "Refresh YouTube Details", IsEnabled = !youtubeInspectorRefreshing && Model.Idle };
        ToolTipService.SetToolTip(refresh, refresh.Content);
        refresh.Click += async (_, _) => await RefreshYouTubeInspectorAsync(); YouTubeInspectorDetails.Children.Add(refresh);
        if (youtubeInspectorRefreshing) {
            var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
            row.Children.Add(new ProgressRing { IsActive = true, Width = 18, Height = 18 }); row.Children.Add(new TextBlock { Text = "Fetching YouTube details…", TextWrapping = TextWrapping.Wrap }); YouTubeInspectorDetails.Children.Add(row);
        }
        var missing = project.YouTubeMetadata is not JsonElement { ValueKind: JsonValueKind.Object };
        if (missing && !youtubeInspectorRefreshing && youtubeInspectorLoaded.Add(project.Folder)) _ = RefreshYouTubeInspectorAsync();
    }
    private async Task RefreshYouTubeInspectorAsync() {
        if (youtubeInspectorRefreshing || Model.Project is not { IsYouTube: true } project) return;
        youtubeInspectorRefreshing = true; UpdateInspector(project);
        var file = Path.Combine(Path.GetTempPath(), "vidistudio-youtube-" + Guid.NewGuid() + ".json");
        try {
            var url = new Uri(project.YoutubeURL ?? "");
            var id = url.Query.TrimStart('?').Split('&').FirstOrDefault(v => v.StartsWith("v="))?[2..] ?? "";
            var video = await YouTubeService.Shared.ProjectVideoAsync(id, project.PrivateYouTubeChannel, Model.Settings);
            video.RequireAvailable(allowPrivate: project.PrivateYouTubeChannel != null);
            while (Model.Busy && Model.Project?.Folder == project.Folder) await Task.Delay(100);
            if (Model.Project?.Folder != project.Folder) return;
            File.WriteAllText(file, video.Data.ToJsonString());
            await Model.RunAsync("update-youtube-metadata", new[] { "--youtube-metadata", file });
        } catch (Exception ex) { if (Model.Project?.Folder == project.Folder) await ShowErrorMessage("YouTube Details Could Not Be Refreshed", ex.Message); }
        finally {
            File.Delete(file); youtubeInspectorRefreshing = false;
            if (Model.Project is { } currentProject) UpdateInspector(currentProject);
        }
    }
    private async void YouTubeBrowser_Click(object sender, RoutedEventArgs e) => await ShowYouTubeBrowserAsync();
    private async Task<YouTubeVideo?> ShowYouTubeBrowserAsync(Window? pickerOwner = null)
    {
        if (youtubeBrowserRun != null || (pickerOwner == null && desktopEditorVisible)) return null;
        var chooseMode = pickerOwner != null;
        var openTitle = chooseMode ? "Choose Video" : "Open in Editor";
        var dialogOwner = pickerOwner ?? this;
        var service = YouTubeService.Shared;
        var layoutFixture = false;
#if DEBUG
        layoutFixture = youtubeLayoutFixture;
#endif
        var fixtureAccount = new YouTubeAccount("layout-fixture", "", "Sample account", "sample@example.com", "", "sample-channel", "Sample channel", "", "uploads");
        // Keep the controls above a bounded, independently scrolling video area.
        var panel = new Grid { RowSpacing = 12, MinWidth = 480 };
        for (var i = 0; i < 4; i++) panel.RowDefinitions.Add(new RowDefinition { Height = i == 2 ? new GridLength(1, GridUnitType.Star) : GridLength.Auto });
        var controls = new StackPanel { Spacing = 8 };
        var controlsHost = new ScrollViewer { Content = controls, HorizontalScrollMode = ScrollMode.Disabled, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        panel.Children.Add(controlsHost);
        var accountRow = new Grid { ColumnSpacing = 8, RowSpacing = 8 };
        accountRow.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); accountRow.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        foreach (var width in new[] { GridLength.Auto, new GridLength(1, GridUnitType.Star), GridLength.Auto, GridLength.Auto, GridLength.Auto }) accountRow.ColumnDefinitions.Add(new ColumnDefinition { Width = width });
        accountRow.Children.Add(new TextBlock { Text = "Google account and channel", VerticalAlignment = VerticalAlignment.Center });
        var account = new ComboBox { PlaceholderText = "Choose an account", HorizontalAlignment = HorizontalAlignment.Stretch, MinWidth = 160 };
        AutomationProperties.SetName(account, "Google account and channel"); Grid.SetColumn(account, 1); accountRow.Children.Add(account);
        var accountsButton = new Button { Content = "YouTube Accounts…" };
        var refresh = new Button { Content = "Refresh" };
        var done = new Button { Content = chooseMode ? "Cancel" : "Done" };
        var accountActions = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        accountActions.Children.Add(accountsButton); accountActions.Children.Add(refresh); accountActions.Children.Add(done);
        Grid.SetColumn(accountActions, 2); Grid.SetColumnSpan(accountActions, 3); accountRow.Children.Add(accountActions); controls.Children.Add(accountRow);
        accountRow.SizeChanged += (_, _) => {
            var narrow = accountRow.ActualWidth < 800;
            Grid.SetColumnSpan(account, narrow ? 4 : 1);
            Grid.SetRow(accountActions, narrow ? 1 : 0); Grid.SetColumn(accountActions, narrow ? 0 : 2); Grid.SetColumnSpan(accountActions, narrow ? 5 : 3);
        };
        var accountInfo = new TextBlock { TextWrapping = TextWrapping.Wrap, Opacity = 0.7 }; controls.Children.Add(accountInfo);
        var empty = new TextBlock { Text = "Add a Google account to browse its uploaded videos and playlists.", TextWrapping = TextWrapping.Wrap }; controls.Children.Add(empty);
        var filters = new StackPanel { Spacing = 12 }; controls.Children.Add(filters);
        var playlistRow = new Grid { ColumnSpacing = 8 };
        playlistRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto }); playlistRow.ColumnDefinitions.Add(new ColumnDefinition()); playlistRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        playlistRow.Children.Add(new TextBlock { Text = "Playlist", VerticalAlignment = VerticalAlignment.Center });
        var playlist = new ComboBox { HorizontalAlignment = HorizontalAlignment.Stretch }; AutomationProperties.SetName(playlist, "Playlist"); Grid.SetColumn(playlist, 1); playlistRow.Children.Add(playlist);
        var morePlaylists = new Button { Content = "Load More Playlists", Visibility = Visibility.Collapsed }; Grid.SetColumn(morePlaylists, 2); playlistRow.Children.Add(morePlaylists); filters.Children.Add(playlistRow);
        var playlistInfo = new TextBlock { TextWrapping = TextWrapping.Wrap, Visibility = Visibility.Collapsed, MaxHeight = 60 }; filters.Children.Add(playlistInfo);
        var filterRow = new Grid { ColumnSpacing = 8 };
        foreach (var width in new[] { new GridLength(1, GridUnitType.Star), GridLength.Auto, GridLength.Auto, GridLength.Auto, GridLength.Auto }) filterRow.ColumnDefinitions.Add(new ColumnDefinition { Width = width });
        var search = new TextBox { PlaceholderText = "Search loaded video titles" }; AutomationProperties.SetName(search, "Search loaded video titles"); filterRow.Children.Add(search);
        var visibility = new ComboBox { ItemsSource = new[] { "All", "Public", "Unlisted", "Private" }, SelectedIndex = 0, Width = 100 }; AutomationProperties.SetName(visibility, "Visibility");
        var sort = new ComboBox { ItemsSource = new[] { "Newest", "Oldest", "Title", "Duration", "Views" }, SelectedIndex = 0, Width = 100 }; AutomationProperties.SetName(sort, "Sort");
        var visibilityLabel = new TextBlock { Text = "Visibility", VerticalAlignment = VerticalAlignment.Center }; Grid.SetColumn(visibilityLabel, 1); filterRow.Children.Add(visibilityLabel);
        Grid.SetColumn(visibility, 2); filterRow.Children.Add(visibility);
        var sortLabel = new TextBlock { Text = "Sort", VerticalAlignment = VerticalAlignment.Center }; Grid.SetColumn(sortLabel, 3); filterRow.Children.Add(sortLabel);
        Grid.SetColumn(sort, 4); filterRow.Children.Add(sort); filters.Children.Add(filterRow);
        filterRow.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); filterRow.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        filterRow.RowSpacing = 8;
        filterRow.SizeChanged += (_, _) => {
            var narrow = filterRow.ActualWidth < 560;
            Grid.SetColumnSpan(search, narrow ? 5 : 1);
            foreach (var control in new FrameworkElement[] { visibilityLabel, visibility, sortLabel, sort }) Grid.SetRow(control, narrow ? 1 : 0);
        };
        var layoutRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        layoutRow.Children.Add(new TextBlock { Text = "Video layout", VerticalAlignment = VerticalAlignment.Center });
        var listLayout = new ToggleButton { Content = "List View" }; var tableLayout = new ToggleButton { Content = "Table View" };
        layoutRow.Children.Add(listLayout); layoutRow.Children.Add(tableLayout);
        var status = new TextBlock { TextWrapping = TextWrapping.Wrap }; AutomationProperties.SetLiveSetting(status, Microsoft.UI.Xaml.Automation.Peers.AutomationLiveSetting.Polite);
        var loadingRing = new ProgressRing { IsActive = false, Visibility = Visibility.Collapsed, Width = 20, Height = 20 };
        AutomationProperties.SetAccessibilityView(loadingRing, Microsoft.UI.Xaml.Automation.Peers.AccessibilityView.Raw);
        var loadingMessage = new TextBlock { TextWrapping = TextWrapping.Wrap, TextAlignment = TextAlignment.Center };
        AutomationProperties.SetLiveSetting(loadingMessage, Microsoft.UI.Xaml.Automation.Peers.AutomationLiveSetting.Polite);
        var loadingRow = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, Spacing = 8, Visibility = Visibility.Collapsed };
        loadingRow.Children.Add(loadingRing);
        loadingRow.Children.Add(loadingMessage);
        var browserOptions = new StackPanel { Spacing = 8 }; browserOptions.Children.Add(layoutRow); browserOptions.Children.Add(status); Grid.SetRow(browserOptions, 1); panel.Children.Add(browserOptions);
        var list = new ListView { SelectionMode = ListViewSelectionMode.Single };
        var videoSelection = new VideoBrowserSelection(list);
        var tableHeader = new Grid { Visibility = Visibility.Collapsed, ColumnSpacing = 8, Padding = new Thickness(12, 8, 12, 8) };
        var widths = new[] { 112.0, 280, 170, 100, 124, 130, 90, 90, 90, 110 };
        var headers = new[] { "Thumbnail", "Title", "Channel", "Visibility", "Duration", "Published", "Views", "Likes", "Comments", "Captions" };
        for (var column = 0; column < headers.Length; column++) {
            tableHeader.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(widths[column]) });
            var title = new TextBlock { Text = headers[column], FontWeight = Microsoft.UI.Text.FontWeights.SemiBold };
            Grid.SetColumn(title, column); tableHeader.Children.Add(title);
        }
        var videoHost = new Grid(); videoHost.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); videoHost.RowDefinitions.Add(new RowDefinition());
        videoHost.Children.Add(tableHeader); Grid.SetRow(list, 1); videoHost.Children.Add(list);
        var videoScroll = new ScrollViewer { Content = videoHost, HorizontalScrollMode = ScrollMode.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Auto, VerticalScrollMode = ScrollMode.Disabled, VerticalScrollBarVisibility = ScrollBarVisibility.Disabled };
        var results = new Grid();
        results.Children.Add(videoScroll);
        results.Children.Add(loadingRow);
        Grid.SetRow(results, 2);
        panel.Children.Add(results);
        var footer = new StackPanel { Spacing = 12 }; Grid.SetRow(footer, 3); panel.Children.Add(footer);
        var openingActions = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 }; footer.Children.Add(openingActions);
        var rowActions = new DropDownButton { Content = "Actions" }; openingActions.Children.Add(rowActions);
        var openEditor = new Button { Content = openTitle, IsEnabled = false }; openingActions.Children.Add(openEditor);
        bool OwnedPrivateVideo(YouTubeVideo video)
        {
            var owner = service.PrivateConnection(video.ChannelId, Model.Settings);
            return owner != null && account.SelectedItem is YouTubeAccount selected && owner.Id == selected.Id;
        }
        bool CanOpen(YouTubeVideo video) => video.Visibility != "private" || OwnedPrivateVideo(video);
        var openBrowser = new Button { Content = "Open in YouTube", IsEnabled = false }; openingActions.Children.Add(openBrowser);
        var chooseCaptions = new CheckBox { Content = "Choose YouTube captions after opening" };
        if (!chooseMode) footer.Children.Add(chooseCaptions);
        var captionMenu = MenuAccessKeys.Create();
        var captionChoice = new ToggleMenuFlyoutItem { Text = "Choose YouTube captions after opening", Icon = MenuIcons.Create("youtube-captions") };
        captionMenu.Opening += (_, _) => captionChoice.IsChecked = chooseCaptions.IsChecked == true;
        captionChoice.Click += (_, _) => chooseCaptions.IsChecked = captionChoice.IsChecked;
        captionMenu.Items.Add(captionChoice);
        chooseCaptions.ContextFlyout = captionMenu;
        var layout = Model.Settings.Get("youtubeVideoLayout", "list");
        var cells = new[] { "Thumbnail", "Title", "Channel", "Visibility", "DurationLabel", "PublishedLabel", "ViewsLabel", "LikesLabel", "CommentsLabel", "CaptionsLabel" };
        var columns = string.Join("", widths.Select(width => "<ColumnDefinition Width=\"" + width + "\"/>"));
        var row = "<Image Source=\"{Binding Thumbnail}\" Width=\"100\" Height=\"60\" Stretch=\"Uniform\" AutomationProperties.AccessibilityView=\"Raw\"/>";
        for (var column = 1; column < cells.Length; column++) row += "<TextBlock Grid.Column=\"" + column + "\" Text=\"{Binding " + cells[column] + "}\"" + (column == 4 ? " AutomationProperties.Name=\"{Binding DurationAnnouncement}\"" : "") + " TextTrimming=\"CharacterEllipsis\" VerticalAlignment=\"Center\"/>";
        var template = (DataTemplate)Microsoft.UI.Xaml.Markup.XamlReader.Load("<DataTemplate xmlns=\"http://schemas.microsoft.com/winfx/2006/xaml/presentation\"><Grid ColumnSpacing=\"8\" Padding=\"12,6\" AutomationProperties.Name=\"{Binding TableAccessibleLabel}\"><Grid.ColumnDefinitions>" + columns + "</Grid.ColumnDefinitions>" + row + "</Grid></DataTemplate>");
        var listTemplate = (DataTemplate)Microsoft.UI.Xaml.Markup.XamlReader.Load("<DataTemplate xmlns=\"http://schemas.microsoft.com/winfx/2006/xaml/presentation\"><Grid ColumnSpacing=\"12\" Padding=\"12,8\" AutomationProperties.Name=\"{Binding TableAccessibleLabel}\"><Grid.ColumnDefinitions><ColumnDefinition Width=\"100\"/><ColumnDefinition Width=\"*\"/></Grid.ColumnDefinitions><Grid AutomationProperties.AccessibilityView=\"Raw\"><SymbolIcon Symbol=\"Video\" Opacity=\"0.4\"/><Image Source=\"{Binding Thumbnail}\" Width=\"100\" Height=\"60\" Stretch=\"Uniform\"/></Grid><StackPanel Grid.Column=\"1\" Spacing=\"4\"><TextBlock Text=\"{Binding Title}\" FontWeight=\"SemiBold\" TextWrapping=\"Wrap\"/><TextBlock Text=\"{Binding ListSubtitle}\" AutomationProperties.Name=\"{Binding ListSubtitleAnnouncement}\"/><TextBlock Text=\"{Binding StatisticsLabel}\" TextWrapping=\"Wrap\"/><TextBlock Text=\"{Binding CaptionsLabel}\"/></StackPanel></Grid></DataTemplate>");
        void ApplyLayout() {
            var selection = list.SelectedItem;
            var table = layout == "table";
            listLayout.IsChecked = !table; tableLayout.IsChecked = table;
            tableHeader.Visibility = table ? Visibility.Visible : Visibility.Collapsed;
            videoScroll.HorizontalScrollMode = table ? ScrollMode.Auto : ScrollMode.Disabled;
            videoScroll.HorizontalScrollBarVisibility = table ? ScrollBarVisibility.Auto : ScrollBarVisibility.Disabled;
            videoHost.Width = table ? widths.Sum() + 8 * 9 + 24 : double.NaN;
            list.ItemTemplate = table ? template : listTemplate;
            AutomationProperties.SetName(list, table ? "YouTube videos table" : "YouTube videos list");
            var style = new Style(typeof(ListViewItem)); style.Setters.Add(new Setter(Control.HorizontalContentAlignmentProperty, HorizontalAlignment.Stretch)); style.Setters.Add(new Setter(Control.PaddingProperty, new Thickness(0))); list.ItemContainerStyle = style;
            videoSelection.SetTarget(selection);
            Model.Settings.Set("youtubeVideoLayout", table ? "table" : "list"); Model.Settings.Save();
            UpdateDesktopMenuStates();
        }
        void SetLayout(string value) { layout = value; ApplyLayout(); }
        listLayout.Click += (_, _) => SetLayout("list"); tableLayout.Click += (_, _) => SetLayout("table"); ApplyLayout();
        var more = new Button { Content = "Load More Videos", Visibility = Visibility.Collapsed }; footer.Children.Add(more);
        var videos = new List<YouTubeVideo>(); var playlists = new List<YouTubePlaylist>(); var videoPage = ""; var playlistPage = "";
        CancellationTokenSource? request = null; long generation = 0;
        var dialog = new ContentDialog { Title = "My YouTube Videos", Content = panel };
        using var browserLifetime = new CancellationTokenSource();
        var browserToken = browserLifetime.Token;
        dialog.Closing += (_, _) => browserLifetime.Cancel();
        AutomationProperties.SetAutomationId(panel, "youtube-browser-content");
        AutomationProperties.SetAutomationId(accountRow, "youtube-account-row");
        AutomationProperties.SetAutomationId(filterRow, "youtube-filter-row");
        AutomationProperties.SetAutomationId(list, "youtube-videos");
        var scale = Root.XamlRoot?.RasterizationScale ?? 1;
        // ContentDialog otherwise fits this window to its narrow minimum desired width.
        // Reserve the same broad browsing surface as Mac, bounded by the owner window.
        panel.Width = Math.Max(480, Math.Min(1120, AppWindow.Size.Width / scale - 96));
        panel.Height = Math.Max(240, Math.Min(680, AppWindow.Size.Height / scale - 160));
        controlsHost.MaxHeight = Math.Max(180, panel.Height - 260);
        done.Click += (_, _) => dialog.Hide();
        bool accountsOpen = false;
        async Task ShowAccounts() {
            if (accountsOpen) return;
            accountsOpen = true;
            var accounts = new YouTubeAccountsPanel(this, showsDone: true);
#if DEBUG
            if (layoutFixture) accounts.PreviewAccountsForSmokeTest(new[] { fixtureAccount });
#endif
            var accountDialog = new ContentDialog { Content = new ScrollViewer { Content = accounts, MaxHeight = Math.Max(180, panel.Height - 80) } };
            accounts.Done += (_, _) => accountDialog.Hide();
            accounts.AccountsChanged += (_, _) => ReloadAccounts();
            try { await WindowedDialog.ShowAsync(accountDialog, dialogOwner, 1040, 480); }
            finally { accountsOpen = false; ReloadAccounts(); }
        }
        accountsButton.Click += async (_, _) => await ShowAccounts();
        void Filter() {
            IEnumerable<YouTubeVideo> found = videos.Where(v => (visibility.SelectedIndex == 0 || string.Equals(v.Visibility, visibility.SelectedItem as string, StringComparison.OrdinalIgnoreCase)) && v.Title.Contains(search.Text, StringComparison.OrdinalIgnoreCase));
            found = sort.SelectedIndex switch { 1 => found.OrderBy(v => v.Published), 2 => found.OrderBy(v => v.Title, StringComparer.CurrentCultureIgnoreCase), 3 => found.OrderBy(v => v.Duration), 4 => found.OrderByDescending(v => ulong.TryParse(v.Data["viewCount"]?.ToString(), out var count) ? count : 0), _ => found.OrderByDescending(v => v.Published) };
            var selectedID = (list.SelectedItem as YouTubeVideo)?.Id;
            var shown = found.ToList(); list.ItemsSource = shown;
            videoSelection.SetTarget(shown.FirstOrDefault(video => video.Id == selectedID));
        }
        void StopLoading() {
            loadingRing.IsActive = false; loadingRing.Visibility = Visibility.Collapsed;
            loadingRow.Visibility = Visibility.Collapsed;
            more.IsEnabled = morePlaylists.IsEnabled = true; refresh.IsEnabled = account.SelectedItem is YouTubeAccount;
            openEditor.IsEnabled = list.SelectedItem is YouTubeVideo video && CanOpen(video);
            UpdateDesktopMenuStates();
        }
        async Task Load(bool reset, bool loadPlaylists = false, bool playlistsOnly = false) {
            request?.Cancel(); request = new(); var ct = request.Token; var stamp = ++generation;
            if (account.SelectedItem is not YouTubeAccount selected) { videos.Clear(); Filter(); StopLoading(); status.Text = ""; loadingRow.Visibility = Visibility.Collapsed; return; }
            var pagingVideos = !reset && !loadPlaylists && !playlistsOnly;
            var pagingPlaylists = playlistsOnly;
            loadingMessage.Text = pagingPlaylists ? "Loading more playlists…" : loadPlaylists ? "Loading playlists…" : pagingVideos ? "Loading more videos…" : "Loading videos…";
            loadingRow.Visibility = Visibility.Visible; openEditor.IsEnabled = false;
            loadingRing.IsActive = true; loadingRing.Visibility = Visibility.Visible;
            status.Text = "";
            more.IsEnabled = morePlaylists.IsEnabled = refresh.IsEnabled = false;
            try {
#if DEBUG
                if (layoutFixture) {
                    playlist.ItemsSource = new[] { new YouTubePlaylist("", "All uploads", "", "", "", "") }; playlist.SelectedIndex = 0;
                    videos.Clear();
                    for (var i = 0; i < 30; i++) videos.Add(new YouTubeVideo(new JsonObject { ["videoID"] = "fixture-" + i.ToString("000", System.Globalization.CultureInfo.InvariantCulture), ["title"] = "Sample video " + (i + 1), ["channelTitle"] = "Sample channel", ["visibility"] = i % 2 == 0 ? "unlisted" : "public", ["duration"] = 125.5 + i, ["publishedAt"] = "2026-11-01T00:00:00Z", ["thumbnailURL"] = Path.Combine(Model.Project!.Folder, "thumbnail.jpg") }));
                    more.Visibility = Visibility.Visible; Filter(); status.Text = videos.Count + " videos loaded."; return;
                }
#endif
                if (loadPlaylists || playlistsOnly) {
                    if (reset) { playlists.Clear(); playlistPage = ""; }
                    var (items, next) = await service.PlaylistsAsync(playlistPage, selected.Id, ct);
                    if (stamp != generation) return;
                    playlists.AddRange(items); playlistPage = next;
                    var choices = new List<YouTubePlaylist> { new("", "All uploads", "", "", "", "") }; choices.AddRange(playlists);
                    playlist.ItemsSource = choices; playlist.SelectedIndex = 0;
                    morePlaylists.Visibility = next.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
                    if (playlistsOnly) { status.Text = "Playlists loaded."; return; }
                }
                if (reset) { videos.Clear(); videoPage = ""; Filter(); }
                loadingMessage.Text = pagingVideos ? "Loading more videos…" : "Loading videos…";
                var chosen = playlist.SelectedItem as YouTubePlaylist;
                playlistInfo.Text = chosen == null ? "" : chosen.Description + (chosen.Published.Length > 0 ? "\nPublished: " + chosen.Published : "");
                playlistInfo.Visibility = string.IsNullOrWhiteSpace(playlistInfo.Text) ? Visibility.Collapsed : Visibility.Visible;
                var playlistId = string.IsNullOrEmpty(chosen?.Id) ? selected.Uploads : chosen.Id;
                if (playlistId.Length == 0)
                {
                    status.Text = "No uploaded videos were returned for this channel.";
                    return;
                }
                var (pageVideos, nextVideo) = await service.VideosAsync(playlistId, videoPage, selected.Id, ct);
                if (stamp != generation || selected.Id != service.SelectedId) return;
                videos.AddRange(pageVideos.Where(v => videos.All(existing => existing.Id != v.Id))); videoPage = nextVideo;
                more.Visibility = nextVideo.Length > 0 ? Visibility.Visible : Visibility.Collapsed; Filter(); status.Text = videos.Count + " videos loaded.";
            } catch (OperationCanceledException) { }
            catch (Exception ex) { if (stamp == generation) status.Text = ex.Message; }
            finally { if (stamp == generation) StopLoading(); }
        }
        void ReloadAccounts() {
            var choices = layoutFixture ? new[] { fixtureAccount } : service.Accounts.ToArray();
            if (account.ItemsSource is not YouTubeAccount[] existing || !existing.SequenceEqual(choices)) account.ItemsSource = choices;
            account.SelectedItem = layoutFixture ? fixtureAccount : service.Selected;
            ReloadAccountPresentation();
        }
        void ReloadAccountPresentation() {
            var connected = account.SelectedItem is YouTubeAccount;
            filters.Visibility = browserOptions.Visibility = videoScroll.Visibility = footer.Visibility = connected ? Visibility.Visible : Visibility.Collapsed;
            accountInfo.Visibility = connected ? Visibility.Visible : Visibility.Collapsed;
            accountInfo.Text = account.SelectedItem is YouTubeAccount selected ? selected.Email + " · YouTube channel: " + selected.ChannelName : "";
            empty.Visibility = connected ? Visibility.Collapsed : Visibility.Visible;
            refresh.IsEnabled = connected && !loadingRing.IsActive;
        }
        account.SelectionChanged += async (_, _) => { if (!layoutFixture && account.SelectedItem is YouTubeAccount a) service.SelectedId = a.Id; ReloadAccountPresentation(); await Load(true, true); };
        playlist.SelectionChanged += async (_, _) => { if (refresh.IsEnabled) await Load(true); };
        search.TextChanged += (_, _) => Filter(); visibility.SelectionChanged += (_, _) => Filter(); sort.SelectionChanged += (_, _) => Filter();
        list.SelectionChanged += (_, _) => openEditor.IsEnabled = list.SelectedItem is YouTubeVideo video && CanOpen(video) && refresh.IsEnabled;
        more.Click += async (_, _) => await Load(false); morePlaylists.Click += async (_, _) => await Load(false, false, true); refresh.Click += async (_, _) => await Load(true, true);
        openEditor.Click += (_, _) => { if (youtubeBrowserCanRun?.Invoke("youtube-open-editor") == true) youtubeBrowserRun?.Invoke("youtube-open-editor"); };
        void OpenBrowser() { if (list.SelectedItem is YouTubeVideo chosen) System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(chosen.Url) { UseShellExecute = true }); }
        openBrowser.Click += (_, _) => { if (youtubeBrowserCanRun?.Invoke("youtube-open-browser") == true) youtubeBrowserRun?.Invoke("youtube-open-browser"); };
        list.SelectionChanged += (_, _) => { openBrowser.IsEnabled = list.SelectedItem is YouTubeVideo; UpdateDesktopMenuStates(); };
        youtubeBrowserCanRun = id => !accountsOpen && (id is "youtube-accounts" or "youtube-list" or "youtube-table" || (id == "youtube-open-browser" ? list.SelectedItem is YouTubeVideo : id == "youtube-open-editor" ? list.SelectedItem is YouTubeVideo video && CanOpen(video) && refresh.IsEnabled : id == "youtube-load-videos" ? more.Visibility == Visibility.Visible && more.IsEnabled : id == "youtube-load-playlists" ? morePlaylists.Visibility == Visibility.Visible && morePlaylists.IsEnabled : refresh.IsEnabled));
        YouTubeVideo? chosenForProject = null;
        bool confirmingVideo = false;
        async Task ChooseForNewProjectAsync()
        {
            if (confirmingVideo || list.SelectedItem is not YouTubeVideo video ||
                account.SelectedItem is not YouTubeAccount selected || !CanOpen(video)) return;
            confirmingVideo = true;
            openEditor.IsEnabled = refresh.IsEnabled = account.IsEnabled = false;
            loadingMessage.Text = "Fetching video details…";
            loadingRow.Visibility = loadingRing.Visibility = Visibility.Visible;
            loadingRing.IsActive = true;
            try
            {
#if DEBUG
                var current = layoutFixture ? video : await service.VideoAsync(video.Id, browserToken, selected.Id);
#else
                var current = await service.VideoAsync(video.Id, browserToken, selected.Id);
#endif
                if (browserToken.IsCancellationRequested || !ReferenceEquals(list.SelectedItem, video) || account.SelectedItem is not YouTubeAccount active || active.Id != selected.Id) return;
                current.RequireAvailable(allowPrivate: OwnedPrivateVideo(current));
                if (playlist.SelectedItem is YouTubePlaylist chosen && chosen.Id.Length > 0)
                    current.Data["openedFromPlaylist"] = YouTubeService.PlaylistData(chosen);
                chosenForProject = current;
                dialog.Hide();
            }
            catch (OperationCanceledException) { }
            catch (Exception ex) { status.Text = ex.Message; }
            finally
            {
                confirmingVideo = false;
                account.IsEnabled = true;
                StopLoading();
            }
        }
        youtubeBrowserRun = id => {
            switch (id) {
            case "youtube-list": SetLayout("list"); break;
            case "youtube-table": SetLayout("table"); break;
            case "youtube-browser": search.Focus(FocusState.Programmatic); break;
            case "youtube-open-editor":
                if (chooseMode) _ = ChooseForNewProjectAsync();
                else dialog.Hide();
                break;
            case "youtube-open-browser": OpenBrowser(); break;
            case "youtube-accounts": _ = ShowAccounts(); break;
            case "youtube-refresh-list": _ = Load(true, true); break;
            case "youtube-load-videos": _ = Load(false); break;
            case "youtube-load-playlists": _ = Load(false, false, true); break;
            }
        };
        var browserMenu = new MenuBar(); var youtubeMenu = new MenuBarItem { Title = "YouTube" }; browserMenu.Items.Add(youtubeMenu); controls.Children.Insert(0, browserMenu);
        foreach (var id in new[] { "youtube-accounts", "youtube-list", "youtube-table", "youtube-open-editor", "youtube-open-browser", "youtube-refresh-list", "youtube-load-videos", "youtube-load-playlists" }) {
            var item = new MenuFlyoutItem { Text = id == "youtube-open-editor" ? openTitle : DesktopCommandTitle(id), Icon = DesktopCommandIcon(id) };
            if (DesktopShortcuts.TryGetValue(id, out var shortcut)) {
                item.KeyboardAcceleratorTextOverride = shortcut.Text;
                var accelerator = new Microsoft.UI.Xaml.Input.KeyboardAccelerator { Key = shortcut.Key, Modifiers = shortcut.Modifiers };
                accelerator.Invoked += (_, args) => { if (youtubeBrowserCanRun?.Invoke(id) == true) { youtubeBrowserRun?.Invoke(id); args.Handled = true; } };
                panel.KeyboardAccelerators.Add(accelerator);
            }
            void UpdateEnabled() {
                item.IsEnabled = youtubeBrowserCanRun?.Invoke(id) == true;
                if (id is "youtube-list" or "youtube-table") item.Icon = (layout == "table") == (id == "youtube-table") ? new SymbolIcon(Symbol.Accept) : DesktopCommandIcon(id);
            }
            youtubeMenu.PointerEntered += (_, _) => UpdateEnabled(); youtubeMenu.GettingFocus += (_, _) => UpdateEnabled();
            UpdateEnabled();
            item.Click += (_, _) => { if (youtubeBrowserCanRun?.Invoke(id) == true) youtubeBrowserRun?.Invoke(id); };
            youtubeMenu.Items.Add(item);
        }
        void PopulateVideoActions(MenuFlyout menu, YouTubeVideo? target)
        {
            menu.Items.Clear();
            menu.Items.Add(videoSelection.ModeItem(!loadingRing.IsActive && list.Items.Count > 0));
            if (target == null) return;
            if (videoSelection.Enabled) menu.Items.Add(videoSelection.TargetItem(target));
            foreach (var id in new[] { "youtube-open-editor", "youtube-open-browser" })
            {
                var item = new MenuFlyoutItem { Text = id == "youtube-open-editor" ? openTitle : DesktopCommandTitle(id), Icon = DesktopCommandIcon(id), Tag = id,
                    IsEnabled = youtubeBrowserCanRun?.Invoke(id) == true };
                item.Click += (_, _) =>
                {
                    if (ReferenceEquals(list.SelectedItem, target) && youtubeBrowserCanRun?.Invoke(id) == true)
                        youtubeBrowserRun?.Invoke(id);
                };
                menu.Items.Add(item);
            }
        }
        var selectedActions = MenuAccessKeys.Create();
        selectedActions.Opening += (_, _) => PopulateVideoActions(selectedActions, list.SelectedItem as YouTubeVideo);
        rowActions.Flyout = selectedActions;
        list.ContextRequested += (_, args) =>
        {
            var pointer = args.TryGetPosition(list, out var point);
            var element = (pointer ? args.OriginalSource : FocusManager.GetFocusedElement(list.XamlRoot)) as FrameworkElement;
            var row = element;
            while (row != null && row != list && row.DataContext is not YouTubeVideo)
                row = Microsoft.UI.Xaml.Media.VisualTreeHelper.GetParent(row) as FrameworkElement;
            if (row?.DataContext is YouTubeVideo clicked) videoSelection.SetTarget(clicked);
            else if (pointer) videoSelection.SetTarget(null);
            var menu = MenuAccessKeys.Create();
            PopulateVideoActions(menu, list.SelectedItem as YouTubeVideo);
            if (pointer) menu.ShowAt(list, point); else menu.ShowAt(element ?? list);
            args.Handled = true;
        };
        var openViaMenu = false;
        var originalRun = youtubeBrowserRun;
        youtubeBrowserRun = id => { if (id == "youtube-open-editor") openViaMenu = true; originalRun(id); };
        ReloadAccounts();
        var outcome = await WindowedDialog.ShowAsync(dialog, dialogOwner, 1200, 850); request?.Cancel(); generation++; StopLoading(); youtubeBrowserRun = null; youtubeBrowserCanRun = null;
        if (chooseMode) return chosenForProject;
        if ((outcome == ContentDialogResult.Primary || openViaMenu) && list.SelectedItem is YouTubeVideo video && CanOpen(video)) {
            var selectedAccount = (account.SelectedItem as YouTubeAccount)?.Id;
            await OpenSharedVideoAsync("Opening YouTube project…", "shared-youtube-opening", async () =>
            {
                var current = await service.VideoAsync(video.Id, accountId: selectedAccount);
                current.RequireAvailable(allowPrivate: OwnedPrivateVideo(current));
                Model.Reload();
                if (Model.ExistingProjectForYouTube(current.Url) is { } existing) return existing.Folder;
                if (playlist.SelectedItem is YouTubePlaylist chosen && chosen.Id.Length > 0)
                    current.Data["openedFromPlaylist"] = YouTubeService.PlaylistData(chosen);
                var metadata = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".json");
                try
                {
                    File.WriteAllText(metadata, current.Data.ToJsonString());
                    await Model.RunAsync("import-youtube", new[] { "--url", current.Url, "--library", Model.Settings.Get("libraryPath"), "--youtube-metadata", metadata }, false);
                    return Model.Project is { } project && SharedYouTubeLink.VideoId(project.YoutubeURL) == current.Id ? project.Folder : null;
                }
                finally { File.Delete(metadata); }
            }, chooseCaptions.IsChecked == true ? editor => editor.ShowYouTubeDetailsAsync(true) : null);
        }
        return null;
    }
    private async Task ShowYouTubeDetailsAsync(bool showCaptions = false)
    {
        if (Model.Project is not { IsYouTube: true } project) return;
        var service = YouTubeService.Shared;
        var panel = new StackPanel { Spacing = 12, MinWidth = 540 };
        var details = new TextBlock { TextWrapping = TextWrapping.Wrap, IsTextSelectionEnabled = true }; panel.Children.Add(details);
        var status = new TextBlock { TextWrapping = TextWrapping.Wrap }; panel.Children.Add(status);
        var account = new ComboBox { Header = "Caption account", ItemsSource = service.Accounts.ToArray(), SelectedItem = service.Selected }; panel.Children.Add(account);
        var captions = new ComboBox { Header = "Caption track", HorizontalAlignment = HorizontalAlignment.Stretch }; panel.Children.Add(captions);
        var load = new Button { Content = "Import YouTube Captions…" }; panel.Children.Add(load);
        var refresh = new Button { Content = "Refresh Details" }; panel.Children.Add(refresh);
        var dialog = new ContentDialog { Title = "YouTube Details", Content = new ScrollViewer { Content = panel }, PrimaryButtonText = "Import Selected Captions", CloseButtonText = "Close", SecondaryButtonText = "Delete Stored YouTube Data…", IsPrimaryButtonEnabled = false };
        using var cancellation = new CancellationTokenSource();
        var id = new Uri(project.YoutubeURL ?? "").Query.TrimStart('?').Split('&').FirstOrDefault(v => v.StartsWith("v="))?[2..] ?? "";
        async Task Refresh() {
            try {
                var video = await service.ProjectVideoAsync(id, project.PrivateYouTubeChannel, Model.Settings, cancellation.Token); details.Text = video.ToString() + "\n" + video.Data["description"] + "\nPublished: " + video.Published + "\nViews: " + (video.Data["viewCount"]?.ToString() ?? "Unavailable") + " · Likes: " + (video.Data["likeCount"]?.ToString() ?? "Unavailable") + " · Comments: " + (video.Data["commentCount"]?.ToString() ?? "Unavailable") + "\nLanguage: " + (video.Data["language"]?.ToString() ?? "Unavailable") + " · Audio language: " + (video.Data["audioLanguage"]?.ToString() ?? "Unavailable") + "\nCategory: " + (video.Data["categoryID"]?.ToString() ?? "Unavailable") + " · Embedding allowed: " + (video.Data["embeddable"]?.ToString() ?? "Unavailable") + "\nProcessing: " + (video.Data["uploadStatus"]?.ToString() ?? "Unavailable") + " · Live status: " + (video.Data["liveStatus"]?.ToString() ?? "Unavailable") + "\nTags: " + string.Join(", ", video.Data["tags"]?.AsArray().Select(t => t?.ToString()) ?? Array.Empty<string>());
                video.RequireAvailable(allowPrivate: project.PrivateYouTubeChannel != null); var file = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".json");
                try { File.WriteAllText(file, video.Data.ToJsonString()); await Model.RunAsync("update-youtube-metadata", new[] { "--youtube-metadata", file }); }
                finally { File.Delete(file); }
            } catch (Exception ex) { status.Text = ex.Message; }
        }
        async Task Captions() {
            if (account.SelectedItem is not YouTubeAccount selected) { status.Text = "Choose a connected account that can edit this video."; return; }
            try { var result = await service.CaptionsAsync(id, selected.Id, cancellation.Token); if (account.SelectedItem is YouTubeAccount currentAccount && currentAccount.Id == selected.Id) { captions.ItemsSource = result; status.Text = result.Count == 0 ? "No caption tracks were returned." : "Choose a language and track."; } }
            catch (Exception ex) { status.Text = ex.Message; }
        }
        load.Click += async (_, _) => await Captions(); refresh.Click += async (_, _) => await Refresh();
        account.SelectionChanged += (_, _) => { captions.ItemsSource = null; dialog.IsPrimaryButtonEnabled = false; };
        captions.SelectionChanged += (_, _) => dialog.IsPrimaryButtonEnabled = captions.SelectedItem is YouTubeCaption { Status: "serving" };
        dialog.Opened += async (_, _) => { await Refresh(); if (showCaptions) await Captions(); };
        var outcome = await ShowDialogAsync(dialog, 820, 720);
        if (outcome == ContentDialogResult.Secondary) {
            var confirmDelete = new ContentDialog { Title = "Delete stored YouTube data?", Content = "Removes fetched details, downloaded captions, imported YouTube caption rows (including edits), and transcript history. Other manually created rows remain. Nothing is deleted on YouTube.", PrimaryButtonText = "Delete Stored YouTube Data", CloseButtonText = "Cancel", DefaultButton = ContentDialogButton.Close };
            if (await ShowDialogAsync(confirmDelete, 650, 400) == ContentDialogResult.Primary) await Model.RunAsync("clear-youtube-data");
            return;
        }
        if (outcome != ContentDialogResult.Primary || captions.SelectedItem is not YouTubeCaption caption || account.SelectedItem is not YouTubeAccount selectedAccount) return;
        if (project.HasTranscript) {
            var confirm = new ContentDialog { Title = "Replace this project's transcript?", Content = "The selected caption track replaces your local transcript. Review description timing afterward.", PrimaryButtonText = "Replace Transcript", CloseButtonText = "Cancel", DefaultButton = ContentDialogButton.Close };
            if (await ShowDialogAsync(confirm, 600, 350) != ContentDialogResult.Primary) return;
        }
        var input = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".srt"); var provenance = Path.ChangeExtension(input, ".json");
        try {
            var data = await service.DownloadAsync(caption, selectedAccount.Id, CancellationToken.None); await File.WriteAllBytesAsync(input, data);
            await File.WriteAllTextAsync(provenance, new JsonObject { ["videoID"] = id, ["trackID"] = caption.Id, ["language"] = caption.Language, ["name"] = caption.Name, ["trackKind"] = caption.Kind }.ToJsonString());
            await Model.RunAsync("import-transcript", new[] { "--input", input, "--caption-provenance", provenance, "--replace-existing" });
        } catch (Exception ex) { await ShowError(ex); }
        finally { File.Delete(input); File.Delete(provenance); }
    }
}
