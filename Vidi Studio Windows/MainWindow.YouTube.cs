using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
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
        var openYouTube = new Button { Content = "Open in YouTube" }; openYouTube.Click += (_, _) => OpenProjectInYouTube(project); YouTubeInspectorDetails.Children.Add(openYouTube);
        var refresh = new Button { Content = "Refresh YouTube Details", IsEnabled = !youtubeInspectorRefreshing && Model.Idle };
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
            var video = await YouTubeService.Shared.VideoAsync(id);
            video.RequireAvailable();
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
    private async Task ShowYouTubeBrowserAsync()
    {
        if (youtubeBrowserRun != null) { youtubeBrowserRun("youtube-browser"); return; }
        var service = YouTubeService.Shared;
        var panel = new StackPanel { Spacing = 12, MinWidth = 680 };
        var accounts = new YouTubeAccountsPanel(this); var accountPanel = new Expander { Header = "YouTube Accounts", Content = accounts, HorizontalAlignment = HorizontalAlignment.Stretch }; panel.Children.Add(accountPanel);
        var account = new ComboBox { Header = "Google account and YouTube channel", HorizontalAlignment = HorizontalAlignment.Stretch }; panel.Children.Add(account);
        var playlist = new ComboBox { Header = "Playlist", HorizontalAlignment = HorizontalAlignment.Stretch }; panel.Children.Add(playlist);
        var playlistInfo = new TextBlock { TextWrapping = TextWrapping.Wrap }; panel.Children.Add(playlistInfo);
        var morePlaylists = new Button { Content = "Load More Playlists", Visibility = Visibility.Collapsed }; panel.Children.Add(morePlaylists);
        var search = new TextBox { Header = "Search loaded video titles" }; panel.Children.Add(search);
        var visibility = new ComboBox { Header = "Visibility", ItemsSource = new[] { "All", "public", "unlisted", "private" }, SelectedIndex = 0 }; panel.Children.Add(visibility);
        var sort = new ComboBox { Header = "Sort", ItemsSource = new[] { "Newest", "Oldest", "Title", "Duration", "Views" }, SelectedIndex = 0 }; panel.Children.Add(sort);
        panel.Children.Add(new TextBlock { Text = "Search, filters, and sorting apply to loaded videos. Load more to include additional results.", TextWrapping = TextWrapping.Wrap });
        var refresh = new Button { Content = "Refresh" }; panel.Children.Add(refresh);
        var status = new TextBlock { TextWrapping = TextWrapping.Wrap }; AutomationProperties.SetLiveSetting(status, Microsoft.UI.Xaml.Automation.Peers.AutomationLiveSetting.Polite); var loadingRing = new ProgressRing { IsActive = false, Visibility = Visibility.Collapsed, Width = 20, Height = 20 };
        AutomationProperties.SetAccessibilityView(loadingRing, Microsoft.UI.Xaml.Automation.Peers.AccessibilityView.Raw);
        var loadingRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        loadingRow.Children.Add(loadingRing); loadingRow.Children.Add(status); panel.Children.Add(loadingRow);
        var list = new ListView { Height = 280, SelectionMode = ListViewSelectionMode.Single };
        var tableHeader = new Grid { Visibility = Visibility.Collapsed, ColumnSpacing = 8, Padding = new Thickness(12, 8, 12, 8) };
        var widths = new[] { 90.0, 260, 150, 90, 124, 130, 90, 90, 90, 110 };
        var headers = new[] { "Thumbnail", "Title", "Channel", "Visibility", "Duration", "Published", "Views", "Likes", "Comments", "Captions" };
        for (var column = 0; column < headers.Length; column++) {
            tableHeader.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(widths[column]) });
            var title = new TextBlock { Text = headers[column], FontWeight = Microsoft.UI.Text.FontWeights.SemiBold };
            Grid.SetColumn(title, column); tableHeader.Children.Add(title);
        }
        var videoHost = new Grid(); videoHost.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); videoHost.RowDefinitions.Add(new RowDefinition());
        videoHost.Children.Add(tableHeader); Grid.SetRow(list, 1); videoHost.Children.Add(list);
        var videoScroll = new ScrollViewer { Content = videoHost, HorizontalScrollMode = ScrollMode.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Auto, VerticalScrollMode = ScrollMode.Disabled, VerticalScrollBarVisibility = ScrollBarVisibility.Disabled };
        panel.Children.Add(videoScroll);
        var layout = new ComboBox { Header = "Video layout", ItemsSource = new[] { "List View", "Table View" }, SelectedIndex = Model.Settings.Get("youtubeVideoLayout", "list") == "table" ? 1 : 0 };
        panel.Children.Insert(panel.Children.IndexOf(videoScroll), layout);
        var cells = new[] { "Thumbnail", "Title", "Channel", "Visibility", "DurationLabel", "PublishedLabel", "ViewsLabel", "LikesLabel", "CommentsLabel", "CaptionsLabel" };
        var columns = string.Join("", widths.Select(width => "<ColumnDefinition Width=\"" + width + "\"/>"));
        var row = "<Image Source=\"{Binding Thumbnail}\" Width=\"80\" Height=\"45\" Stretch=\"Uniform\" AutomationProperties.AccessibilityView=\"Raw\"/>";
        for (var column = 1; column < cells.Length; column++) row += "<TextBlock Grid.Column=\"" + column + "\" Text=\"{Binding " + cells[column] + "}\" TextTrimming=\"CharacterEllipsis\" VerticalAlignment=\"Center\"/>";
        var template = (DataTemplate)Microsoft.UI.Xaml.Markup.XamlReader.Load("<DataTemplate xmlns=\"http://schemas.microsoft.com/winfx/2006/xaml/presentation\"><Grid ColumnSpacing=\"8\" Padding=\"12,6\" AutomationProperties.Name=\"{Binding TableAccessibleLabel}\"><Grid.ColumnDefinitions>" + columns + "</Grid.ColumnDefinitions>" + row + "</Grid></DataTemplate>");
        var listTemplate = (DataTemplate)Microsoft.UI.Xaml.Markup.XamlReader.Load("<DataTemplate xmlns=\"http://schemas.microsoft.com/winfx/2006/xaml/presentation\"><Grid ColumnSpacing=\"12\" Padding=\"12,8\" AutomationProperties.Name=\"{Binding TableAccessibleLabel}\"><Grid.ColumnDefinitions><ColumnDefinition Width=\"100\"/><ColumnDefinition Width=\"*\"/></Grid.ColumnDefinitions><Grid AutomationProperties.AccessibilityView=\"Raw\"><SymbolIcon Symbol=\"Video\" Opacity=\"0.4\"/><Image Source=\"{Binding Thumbnail}\" Width=\"100\" Height=\"60\" Stretch=\"Uniform\"/></Grid><StackPanel Grid.Column=\"1\" Spacing=\"4\"><TextBlock Text=\"{Binding Title}\" FontWeight=\"SemiBold\" TextWrapping=\"Wrap\"/><TextBlock Text=\"{Binding ListSubtitle}\"/><TextBlock Text=\"{Binding StatisticsLabel}\" TextWrapping=\"Wrap\"/><TextBlock Text=\"{Binding CaptionsLabel}\"/></StackPanel></Grid></DataTemplate>");
        void ApplyLayout() {
            var selection = list.SelectedItem;
            var table = layout.SelectedIndex == 1;
            tableHeader.Visibility = table ? Visibility.Visible : Visibility.Collapsed;
            videoHost.Width = table ? widths.Sum() + 8 * 9 + 24 : double.NaN;
            list.ItemTemplate = table ? template : listTemplate;
            AutomationProperties.SetName(list, table ? "YouTube videos table" : "YouTube videos list");
            var style = new Style(typeof(ListViewItem)); style.Setters.Add(new Setter(Control.HorizontalContentAlignmentProperty, HorizontalAlignment.Stretch)); style.Setters.Add(new Setter(Control.PaddingProperty, new Thickness(0))); list.ItemContainerStyle = style;
            list.SelectedItem = selection;
            Model.Settings.Set("youtubeVideoLayout", table ? "table" : "list"); Model.Settings.Save();
            UpdateDesktopMenuStates();
        }
        layout.SelectionChanged += (_, _) => ApplyLayout(); ApplyLayout();
        var more = new Button { Content = "Load More Videos", Visibility = Visibility.Collapsed }; panel.Children.Add(more);
        var videos = new List<YouTubeVideo>(); var playlists = new List<YouTubePlaylist>(); var videoPage = ""; var playlistPage = "";
        CancellationTokenSource? request = null; long generation = 0;
        var dialog = new ContentDialog { Title = "My YouTube Videos", Content = new ScrollViewer { Content = panel }, PrimaryButtonText = "Open in Editor", CloseButtonText = "Close", IsPrimaryButtonEnabled = false };
        void Filter() {
            IEnumerable<YouTubeVideo> found = videos.Where(v => (visibility.SelectedIndex == 0 || v.Visibility == visibility.SelectedItem as string) && v.Title.Contains(search.Text, StringComparison.OrdinalIgnoreCase));
            found = sort.SelectedIndex switch { 1 => found.OrderBy(v => v.Published), 2 => found.OrderBy(v => v.Title, StringComparer.CurrentCultureIgnoreCase), 3 => found.OrderBy(v => v.Duration), 4 => found.OrderByDescending(v => ulong.TryParse(v.Data["viewCount"]?.ToString(), out var count) ? count : 0), _ => found.OrderByDescending(v => v.Published) };
            list.ItemsSource = found.ToList();
        }
        void SetLoadButton(Button button, string title, bool active) {
            if (!active) { button.Content = title; AutomationProperties.SetName(button, title); return; }
            var content = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
            var ring = new ProgressRing { IsActive = true, Width = 16, Height = 16 };
            AutomationProperties.SetAccessibilityView(ring, Microsoft.UI.Xaml.Automation.Peers.AccessibilityView.Raw);
            content.Children.Add(ring); content.Children.Add(new TextBlock { Text = title == "Load More Videos" ? "Loading more videos…" : "Loading more playlists…" });
            button.Content = content; AutomationProperties.SetName(button, title == "Load More Videos" ? "Loading more videos" : "Loading more playlists");
        }
        void StopLoading() {
            loadingRing.IsActive = false; loadingRing.Visibility = Visibility.Collapsed;
            SetLoadButton(more, "Load More Videos", false); SetLoadButton(morePlaylists, "Load More Playlists", false);
            more.IsEnabled = morePlaylists.IsEnabled = refresh.IsEnabled = true;
        }
        async Task Load(bool reset, bool loadPlaylists = false, bool playlistsOnly = false) {
            request?.Cancel(); request = new(); var ct = request.Token; var stamp = ++generation;
            if (account.SelectedItem is not YouTubeAccount selected) { videos.Clear(); Filter(); StopLoading(); status.Text = "Choose an account to load videos."; return; }
            var pagingVideos = !reset && !loadPlaylists && !playlistsOnly;
            var pagingPlaylists = playlistsOnly;
            status.Text = pagingPlaylists ? "Loading more playlists…" : loadPlaylists ? "Loading playlists…" : pagingVideos ? "Loading more videos…" : "Loading videos…";
            loadingRing.IsActive = true; loadingRing.Visibility = Visibility.Visible;
            SetLoadButton(more, "Load More Videos", pagingVideos); SetLoadButton(morePlaylists, "Load More Playlists", pagingPlaylists);
            more.IsEnabled = morePlaylists.IsEnabled = refresh.IsEnabled = false;
            try {
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
                status.Text = pagingVideos ? "Loading more videos…" : "Loading videos…";
                var chosen = playlist.SelectedItem as YouTubePlaylist;
                playlistInfo.Text = chosen == null ? "" : chosen.Description + (chosen.Published.Length > 0 ? "\nPublished: " + chosen.Published : "");
                var (results, nextVideo) = await service.VideosAsync(string.IsNullOrEmpty(chosen?.Id) ? selected.Uploads : chosen.Id, videoPage, selected.Id, ct);
                if (stamp != generation || selected.Id != service.SelectedId) return;
                videos.AddRange(results.Where(v => videos.All(existing => existing.Id != v.Id))); videoPage = nextVideo;
                more.Visibility = nextVideo.Length > 0 ? Visibility.Visible : Visibility.Collapsed; Filter(); status.Text = videos.Count + " videos loaded.";
            } catch (OperationCanceledException) { }
            catch (Exception ex) { if (stamp == generation) status.Text = ex.Message; }
            finally { if (stamp == generation) StopLoading(); }
        }
        void ReloadAccounts() { account.ItemsSource = service.Accounts.ToArray(); account.SelectedItem = service.Selected; }
        accounts.AccountsChanged += (_, _) => ReloadAccounts();
        account.SelectionChanged += async (_, _) => { if (account.SelectedItem is YouTubeAccount a) service.SelectedId = a.Id; await Load(true, true); };
        playlist.SelectionChanged += async (_, _) => { if (refresh.IsEnabled) await Load(true); };
        search.TextChanged += (_, _) => Filter(); visibility.SelectionChanged += (_, _) => Filter(); sort.SelectionChanged += (_, _) => Filter();
        list.SelectionChanged += (_, _) => dialog.IsPrimaryButtonEnabled = list.SelectedItem != null;
        more.Click += async (_, _) => await Load(false); morePlaylists.Click += async (_, _) => await Load(false, false, true); refresh.Click += async (_, _) => await Load(true, true);
        var openBrowser = new Button { Content = "Open in YouTube", IsEnabled = false }; panel.Children.Add(openBrowser);
        void OpenBrowser() { if (list.SelectedItem is YouTubeVideo chosen) System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(chosen.Url) { UseShellExecute = true }); }
        openBrowser.Click += (_, _) => OpenBrowser();
        list.SelectionChanged += (_, _) => { openBrowser.IsEnabled = list.SelectedItem is YouTubeVideo; UpdateDesktopMenuStates(); };
        youtubeBrowserCanRun = id => id is "youtube-accounts" or "youtube-list" or "youtube-table" || (id is "youtube-open-editor" or "youtube-open-browser" ? list.SelectedItem is YouTubeVideo && refresh.IsEnabled : id == "youtube-load-videos" ? more.Visibility == Visibility.Visible && more.IsEnabled : id == "youtube-load-playlists" ? morePlaylists.Visibility == Visibility.Visible && morePlaylists.IsEnabled : refresh.IsEnabled);
        youtubeBrowserRun = id => {
            switch (id) {
            case "youtube-list": layout.SelectedIndex = 0; break;
            case "youtube-table": layout.SelectedIndex = 1; break;
            case "youtube-browser": search.Focus(FocusState.Programmatic); break;
            case "youtube-open-editor": dialog.Hide(); break;
            case "youtube-open-browser": OpenBrowser(); break;
            case "youtube-accounts": accountPanel.IsExpanded = true; break;
            case "youtube-refresh-list": _ = Load(true, true); break;
            case "youtube-load-videos": _ = Load(false); break;
            case "youtube-load-playlists": _ = Load(false, false, true); break;
            }
        };
        var browserMenu = new MenuBar(); var youtubeMenu = new MenuBarItem { Title = "YouTube" }; browserMenu.Items.Add(youtubeMenu); panel.Children.Insert(0, browserMenu);
        foreach (var id in new[] { "youtube-browser", "youtube-accounts", "youtube-list", "youtube-table", "youtube-open-editor", "youtube-open-browser", "youtube-refresh-list", "youtube-load-videos", "youtube-load-playlists" }) {
            var item = new MenuFlyoutItem { Text = DesktopCommandTitle(id) };
            if (DesktopShortcuts.TryGetValue(id, out var shortcut)) {
                item.KeyboardAcceleratorTextOverride = shortcut.Text;
                var accelerator = new Microsoft.UI.Xaml.Input.KeyboardAccelerator { Key = shortcut.Key, Modifiers = shortcut.Modifiers };
                accelerator.Invoked += (_, args) => { if (youtubeBrowserCanRun?.Invoke(id) == true) { youtubeBrowserRun?.Invoke(id); args.Handled = true; } };
                panel.KeyboardAccelerators.Add(accelerator);
            }
            void UpdateEnabled() {
                item.IsEnabled = youtubeBrowserCanRun?.Invoke(id) == true;
                if (id is "youtube-list" or "youtube-table") item.Icon = (layout.SelectedIndex == 1) == (id == "youtube-table") ? new SymbolIcon(Symbol.Accept) : null;
            }
            youtubeMenu.PointerEntered += (_, _) => UpdateEnabled(); youtubeMenu.GettingFocus += (_, _) => UpdateEnabled();
            UpdateEnabled();
            item.Click += (_, _) => { if (youtubeBrowserCanRun?.Invoke(id) == true) youtubeBrowserRun?.Invoke(id); };
            youtubeMenu.Items.Add(item);
        }
        var openViaMenu = false;
        var originalRun = youtubeBrowserRun;
        youtubeBrowserRun = id => { if (id == "youtube-open-editor") openViaMenu = true; originalRun(id); };
        ReloadAccounts();
        var outcome = await ShowDialogAsync(dialog, 960, 800); request?.Cancel(); generation++; StopLoading(); youtubeBrowserRun = null; youtubeBrowserCanRun = null;
        if ((outcome == ContentDialogResult.Primary || openViaMenu) && list.SelectedItem is YouTubeVideo video) {
            try { var current = await service.VideoAsync(video.Id); current.RequireAvailable(); await OpenYouTubeUrlAsync(current.Url);
                if (playlist.SelectedItem is YouTubePlaylist chosen && chosen.Id.Length > 0 && Model.Project?.YoutubeURL == current.Url) {
                    current.Data["openedFromPlaylist"] = YouTubeService.PlaylistData(chosen);
                    var contextFile = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".json");
                    try { File.WriteAllText(contextFile, current.Data.ToJsonString()); await Model.RunAsync("update-youtube-metadata", new[] { "--youtube-metadata", contextFile }); }
                    finally { File.Delete(contextFile); }
                }
            }
            catch (Exception ex) { await ShowErrorMessage("Video Cannot Be Opened", ex.Message); }
        }
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
                var video = await service.VideoWithPlaylistsAsync(id, cancellation.Token); details.Text = video.ToString() + "\n" + video.Data["description"] + "\nPublished: " + video.Published + "\nViews: " + (video.Data["viewCount"]?.ToString() ?? "Unavailable") + " · Likes: " + (video.Data["likeCount"]?.ToString() ?? "Unavailable") + " · Comments: " + (video.Data["commentCount"]?.ToString() ?? "Unavailable") + "\nLanguage: " + (video.Data["language"]?.ToString() ?? "Unavailable") + " · Audio language: " + (video.Data["audioLanguage"]?.ToString() ?? "Unavailable") + "\nCategory: " + (video.Data["categoryID"]?.ToString() ?? "Unavailable") + " · Embedding allowed: " + (video.Data["embeddable"]?.ToString() ?? "Unavailable") + "\nProcessing: " + (video.Data["uploadStatus"]?.ToString() ?? "Unavailable") + " · Live status: " + (video.Data["liveStatus"]?.ToString() ?? "Unavailable") + "\nTags: " + string.Join(", ", video.Data["tags"]?.AsArray().Select(t => t?.ToString()) ?? Array.Empty<string>());
                video.RequireAvailable(); var file = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".json");
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
