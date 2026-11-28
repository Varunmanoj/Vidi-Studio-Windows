using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Markup;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Vidi_Studio_Windows.Services;
using Vidi_Studio_Windows.Models;

namespace Vidi_Studio_Windows.Views;

internal sealed class CloudVideosDialog : ContentDialog
{
    private readonly Window owner;
    private readonly SettingsStore settings;
    private bool selectConfiguredBucket;
    private readonly ComboBox projects = new() { Header = "Google Cloud project", DisplayMemberPath = nameof(CloudProject.Label), HorizontalAlignment = HorizontalAlignment.Stretch };
    private readonly ComboBox buckets = new() { Header = "Source bucket", HorizontalAlignment = HorizontalAlignment.Stretch };
    private readonly ComboBox sort = new() { ItemsSource = new[] { "Name A–Z", "Name Z–A", "Recently Modified", "Largest First" }, SelectedIndex = 0 };
    private readonly Microsoft.UI.Xaml.Controls.Primitives.ToggleButton listView = new() { Content = "List View" };
    private readonly Microsoft.UI.Xaml.Controls.Primitives.ToggleButton tableView = new() { Content = "Table View" };
    private readonly ScrollViewer videoScroll = new() { VerticalScrollMode = ScrollMode.Disabled, VerticalScrollBarVisibility = ScrollBarVisibility.Disabled };
    private bool tableLayout;
    private readonly TextBox search = new() { PlaceholderText = "Search videos" };
    private readonly VideoBrowserSelection videoSelection;
    private readonly ListView list = new() { Height = 220, SelectionMode = ListViewSelectionMode.Single };
    private readonly TextBlock status = new() { TextWrapping = TextWrapping.Wrap };
    private readonly StackPanel details = new() { Spacing = 4 };
    private readonly ProgressRing listingRing = new() { Width = 24, Height = 24 };
    private readonly TextBlock listingStatus = new() { TextWrapping = TextWrapping.Wrap, TextAlignment = TextAlignment.Center };
    private readonly StackPanel centeredLoading = new() { HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, Spacing = 8, Visibility = Visibility.Collapsed };
    private readonly ProgressRing progress = new() { Width = 18, Height = 18, Visibility = Visibility.Collapsed };
    private readonly ProgressBar transfer = new() { Minimum = 0, Maximum = 100, Visibility = Visibility.Collapsed };
    private readonly Button refresh = new() { Content = "Refresh" };
    private readonly DropDownButton actions = new() { Content = "Actions" };
    private readonly Button openEditor = new() { Content = "Open in Editor", IsEnabled = false };
    private readonly Button cancel = new() { Content = "Cancel" };
    private readonly Grid connectedBrowser = new() { RowSpacing = 12, MaxHeight = 710 };
    private readonly StackPanel signedOutBrowser = new() { Spacing = 12, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
    private readonly Button signIn = new() { Content = "Sign in with Google" };
    private readonly Button cancelSignIn = new() { Content = "Cancel Sign-in", Visibility = Visibility.Collapsed };
    private readonly TextBlock signInStatus = new() { TextWrapping = TextWrapping.Wrap };
    private readonly ProgressRing signInProgress = new() { Width = 18, Height = 18, Visibility = Visibility.Collapsed };
    private CancellationTokenSource? signInRequest;
    private readonly CancellationTokenSource lifetime = new();
    private CancellationTokenSource? permissionRequest;
    private CloudVideo[] videos = Array.Empty<CloudVideo>();
    private CloudVideo? permissionTarget;
    private CloudPermissions permissions;
    private bool loading;
    private bool populatingChoices;
    private bool closed;
    private string ProjectID => (projects.SelectedItem as CloudProject)?.Id ?? "";
    private string Bucket => buckets.SelectedItem as string ?? "";
    private CloudVideo? Selection => list.SelectedItem as CloudVideo;
    private CancellationTokenSource? importCancellation;
    private CloudVideo? previewedFile;
    private string? previewThumbnail;
    internal Func<CloudVideoSelection, Action<EngineEvent>, CancellationToken, Task<StudioProject?>>? ImportVideo { get; set; }
    internal StudioProject? ImportedProject { get; private set; }

#if DEBUG
    private Func<string[], List<EngineEvent>>? fixture;

    internal void UseFixtureForSmokeTest()
    {
        fixture = args =>
        {
            var data = args[0] == "cloud-projects"
                ? JsonSerializer.SerializeToElement(new { cloudProjects = new[] { new { id = "fixture-source", name = "Source project" } } })
                : args.Contains("--source-bucket")
                    ? JsonSerializer.SerializeToElement(new { cloudFiles = new[] {
                        new { name = "folder/zulu.mp4", generation = "12", size = "3260000000", video = true },
                        new { name = "alpha.mov", generation = "13", size = "100", video = true },
                        new { name = "notes.txt", generation = "14", size = "15", video = false }
                    } })
                    : JsonSerializer.SerializeToElement(new { cloudBuckets = new[] { "fixture-bucket", settings.Get("vertexBucket") } });
            return new() { new EngineEvent("Loaded", null, data) };
        };
    }

    internal void VerifySignedOutForSmokeTest()
    {
        if (signedOutBrowser.Visibility != Visibility.Visible || connectedBrowser.Visibility != Visibility.Collapsed ||
            !signIn.IsEnabled || signIn.Content as string != "Sign in with Google" || openEditor.IsEnabled ||
            refresh.IsEnabled || projects.Items.Count != 0 || signedOutBrowser.ContextFlyout == null)
            throw new InvalidOperationException("The signed-out Cloud browser must offer sign-in without listing any storage data.");
    }

    internal async Task VerifyBrowserForSmokeTestAsync(Window browserWindow)
    {
        await WaitUntilAsync(() => projects.Items.Count == 2 && !loading);
        projects.SelectedIndex = projects.Items.ToList().FindIndex(item => item is CloudProject { Id: "fixture-source" });
        await WaitUntilAsync(() => buckets.Items.Contains("fixture-bucket") && !loading);
        buckets.SelectedItem = "fixture-bucket";
        await WaitUntilAsync(() => list.Items.Count == 2 && !loading);
        if (list.Items[0] is not CloudVideo { Name: "alpha.mov" })
            throw new InvalidOperationException("Cloud videos must sort by name and exclude other files.");
        videoSelection.SetTarget(list.Items[0]);
        videoSelection.SetMode(true);
        if (!list.IsMultiSelectCheckBoxEnabled || !videoSelection.ModeItem(true).IsChecked)
            throw new InvalidOperationException("Cloud browser Select must use native checkboxes.");
        list.SelectedItems.Add(list.Items[1]);
        if (list.SelectedItems.Count != 1 || !ReferenceEquals(list.SelectedItem, list.Items[1]))
            throw new InvalidOperationException("Checking a video must replace the previous action target.");
        videoSelection.SetTarget(null);
        if (openEditor.IsEnabled) throw new InvalidOperationException("Unchecking the video must clear the action target.");
        videoSelection.SetMode(false);
        sort.SelectedIndex = 3;
        if (list.Items[0] is not CloudVideo { Name: "folder/zulu.mp4" })
            throw new InvalidOperationException("Cloud videos did not sort by byte size.");
        search.Text = "FOLDER/";
        await WaitUntilAsync(() => list.Items.Count == 1 && list.Items[0] is CloudVideo { Name: "folder/zulu.mp4" });
        list.SelectedIndex = 0;
        if (CaptureSelection() is not { Project: "fixture-source", Bucket: "fixture-bucket", Generation: "12" })
            throw new InvalidOperationException("Opening a video must keep its selected project, bucket and generation.");
        if (CanRename || CanDelete) throw new InvalidOperationException("Cloud mutation actions must be hidden when permissions are denied.");
        SetTableView(true);
        if (list.Header is not Grid columns || columns.Children[0] is not TextBlock { Text: "Thumbnail" } ||
            !columns.Children.OfType<TextBlock>().Any(label => label.Text == "Actions") ||
            tableView.IsChecked != true || listView.IsChecked != false ||
            AutomationProperties.GetName(list) != "Cloud videos table" ||
            videoScroll.HorizontalScrollMode != ScrollMode.Auto || CaptureSelection()?.Generation != "12")
            throw new InvalidOperationException("Cloud table view must show labeled columns, scroll, and preserve the selection.");
        videoSelection.SetMode(true);
        if (list.Header is not Grid selectionHeader ||
            !selectionHeader.Children.OfType<TextBlock>().Any(label => label.Text == "Select") ||
            selectionHeader.Children.OfType<Grid>().Single().Margin.Left <= 0 || CaptureSelection()?.Generation != "12")
            throw new InvalidOperationException("Cloud table selection must precede the thumbnail and preserve the selected row.");
        videoSelection.SetMode(false);
        DropDownButton? rowActions = null;
        await WaitUntilAsync(() =>
        {
            rowActions = ((list.ContainerFromIndex(0) as ListViewItem)?.ContentTemplateRoot as Grid)?
                .Children.OfType<DropDownButton>().FirstOrDefault();
            return rowActions?.Flyout != null;
        });
        if (rowActions?.Flyout is not MenuFlyout rowMenu || Grid.GetColumn(rowActions) != 5)
            throw new InvalidOperationException("Cloud table did not show the row Actions menu in the final column.");
        videoScroll.ChangeView(videoScroll.ScrollableWidth, null, null, true);
        await Task.Delay(100);
        rowMenu.ShowAt(rowActions);
        try
        {
            var items = rowMenu.Items.OfType<MenuFlyoutItem>().ToArray();
            if (!items.Any(item => item.Text == "Open in Editor" && item.IsEnabled) ||
                items.Any(item => item.Text is "Select Video" or "Rename Video…" or "Delete Video…"))
                throw new InvalidOperationException("Cloud row actions required Select or ignored the object permissions.");
        }
        finally { rowMenu.Hide(); }
        SetTableView(false);
        if (list.Header != null || listView.IsChecked != true || tableView.IsChecked != false ||
            AutomationProperties.GetName(list) != "Cloud videos list" || CaptureSelection()?.Generation != "12")
            throw new InvalidOperationException("Cloud list view must restore the active layout and keep the selected video.");
        if (!ReferenceEquals(actions.Parent, openEditor.Parent))
            throw new InvalidOperationException("Cloud browser Actions and Open in Editor must share one footer.");

        ImportVideo = (_, _, _) => throw new InvalidOperationException("An oversized cloud video reached the import callback.");
        ContentDialog? notice = null;
        void CaptureWarning(ContentDialog dialog, Window window)
        {
            if (dialog.Title as string != "Video Is Too Large") return;
            notice = dialog;
            window.DispatcherQueue.TryEnqueue(() => dialog.Hide());
        }
        WindowedDialog.ReadyToShow += CaptureWarning;
        try
        {
            videoSelection.SetMode(true);
            await MainWindow.DoubleClickVideoRowForSmokeTestAsync(browserWindow, list, 0);
            if (notice != null) throw new InvalidOperationException("Double-click opened a cloud video in Select mode.");
            videoSelection.SetMode(false);
            videoSelection.SetTarget(list.Items[0]);
            await MainWindow.DoubleClickVideoRowForSmokeTestAsync(browserWindow, list, 0);
            await WaitUntilAsync(() => notice != null);
        }
        finally { WindowedDialog.ReadyToShow -= CaptureWarning; }
        if (notice is not { Content: TextBlock warning, PrimaryButtonText: "Choose a Smaller Video", SecondaryButtonText: "Create Smaller AI Copy", CloseButtonText: "Cancel" } ||
            warning.Text != NewProjectDialog.AnalysisSizeWarning(3_260_000_000) || ImportedProject != null)
            throw new InvalidOperationException("Cloud import must warn before downloading an oversized video.");
    }

    internal async Task VerifyConfiguredImportForSmokeTestAsync(StudioProject project)
    {
        await WaitUntilAsync(() => list.Items.Count == 2 && !loading);
        if (ProjectID != settings.Get("vertexProject") || Bucket != settings.Get("vertexBucket") || openEditor.IsEnabled)
            throw new InvalidOperationException("The cloud browser must select the configured project and bucket and wait for a video.");
        ImportVideo = (selection, report, _) =>
        {
            if (selection.Bucket != Bucket || selection.Generation != "13")
                throw new InvalidOperationException("The cloud import lost the selected bucket and generation.");
            report(new EngineEvent("Downloading video: 50 of 100 bytes", .5, JsonSerializer.SerializeToElement(new { progress = .5 })));
            if (transfer.Visibility != Visibility.Visible || transfer.Value != 50 || openEditor.IsEnabled)
                throw new InvalidOperationException("The browser must show transfer progress while keeping import commands disabled.");
            report(new EngineEvent("Copying source video into local project", null, JsonSerializer.SerializeToElement(new {})));
            if (transfer.Visibility != Visibility.Collapsed || progress.Visibility != Visibility.Visible)
                throw new InvalidOperationException("Local project preparation must replace byte progress with its current step.");
            return Task.FromResult<StudioProject?>(project);
        };
        list.SelectedIndex = 0;
        await OpenSelectedVideoAsync();
        if (ImportedProject != project) throw new InvalidOperationException("The browser did not return the imported project.");
    }

    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        for (var attempt = 0; attempt < 60 && !condition(); attempt++) await Task.Delay(25);
        if (!condition()) throw new InvalidOperationException("Cloud browser did not load the fixture.");
    }
#endif

    public sealed record CloudProject(string Id, string Name)
    {
        public string Label => Id == Name ? Id : $"{Name} ({Id})";
    }

    public sealed record CloudVideo(string Name, string Generation, bool ProjectOwned, string? Size, string? ContentType,
        string? StorageClass, string? Created, string? Updated, string? Thumbnail)
    {
        public string Filename => Name.Split('/').Last();
        public string DisplaySize => NewProjectDialog.CloudFileSize(Size);
        public string DisplayType => ContentType ?? "—";
        public string DisplayUpdated => DateTimeOffset.TryParse(Updated, out var date) ? date.ToLocalTime().ToString("g") : "—";
        public ImageSource? ThumbnailImage => DecodeThumbnail(Thumbnail);
    }

    internal CloudVideosDialog(Window owner, ElementTheme theme, SettingsStore settings, bool selectConfiguredBucket = false)
    {
        this.owner = owner;
        this.settings = settings;
        this.selectConfiguredBucket = selectConfiguredBucket;
        videoSelection = new VideoBrowserSelection(list);
        RequestedTheme = theme;
        MaxWidth = 940;
        Title = "Open Cloud Videos";
        var itemStyle = new Style(typeof(ListViewItem));
        itemStyle.Setters.Add(new Setter(Control.HorizontalContentAlignmentProperty, HorizontalAlignment.Stretch));
        itemStyle.Setters.Add(new Setter(Control.PaddingProperty, new Thickness(0)));
        list.ItemContainerStyle = itemStyle;
        AutomationProperties.SetLiveSetting(status, AutomationLiveSetting.Polite);
        AutomationProperties.SetName(transfer, "Cloud video import progress");

        var selectors = new StackPanel { Spacing = 8 };
        SettingsDialog.CompactRow(selectors, "Google Cloud project", SettingsDialog.CompactActions(projects, refresh), true);
        SettingsDialog.CompactRow(selectors, "Source bucket", buckets, true);
        var filters = new Grid { ColumnSpacing = 8 };
        filters.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        filters.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        filters.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        filters.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        filters.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        var sortLabel = new TextBlock { Text = "Sort by", VerticalAlignment = VerticalAlignment.Center };
        AutomationProperties.SetName(search, "Search videos");
        AutomationProperties.SetName(sort, sortLabel.Text);
        filters.Children.Add(search);
        Grid.SetColumn(sortLabel, 1);
        filters.Children.Add(sortLabel);
        Grid.SetColumn(sort, 2);
        filters.Children.Add(sort);
        filters.SizeChanged += (_, _) =>
        {
            var narrow = filters.ActualWidth < 420;
            filters.RowSpacing = narrow ? 8 : 0;
            Grid.SetColumnSpan(search, narrow ? 3 : 1);
            Grid.SetRow(sortLabel, narrow ? 1 : 0);
            Grid.SetRow(sort, narrow ? 1 : 0);
        };
        var layoutRow = SettingsDialog.CompactActions(
            new TextBlock { Text = "Video layout", VerticalAlignment = VerticalAlignment.Center }, listView, tableView);
        layoutRow.ContextFlyout = LayoutMenu();
        AutomationProperties.SetName(projects, "Google Cloud project");
        projects.Header = null;
        buckets.Header = null;

        var stack = new StackPanel { Spacing = 10, MaxWidth = 880 };
        stack.Children.Add(CompactDialogLayout.Card(selectors, "Cloud Storage"));
        stack.Children.Add(CompactDialogLayout.Card(filters, "Video filters"));
        stack.Children.Add(layoutRow);
        var results = new Grid();
        videoScroll.Content = list;
        results.Children.Add(videoScroll);
        AutomationProperties.SetAccessibilityView(listingRing, AccessibilityView.Raw);
        AutomationProperties.SetLiveSetting(listingStatus, AutomationLiveSetting.Polite);
        centeredLoading.Children.Add(listingRing);
        centeredLoading.Children.Add(listingStatus);
        results.Children.Add(centeredLoading);
        stack.Children.Add(results);
        stack.Children.Add(new AccessibilityGroup("Selected video details") { Child = details });
        stack.Children.Add(progress);
        stack.Children.Add(transfer);
        stack.Children.Add(status);
        stack.Children.Add(vertexProject);
        connectedBrowser.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        connectedBrowser.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        var scroll = new ScrollViewer { Content = stack, MaxHeight = 650, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };
        connectedBrowser.Children.Add(scroll);
        var footer = SettingsDialog.CompactActions(actions, openEditor, cancel);
        Grid.SetRow(footer, 1);
        connectedBrowser.Children.Add(footer);
        var layout = new Grid();
        layout.Children.Add(connectedBrowser);
        layout.Children.Add(signedOutBrowser);
        BuildSignInPanel();
        Content = layout;

        projects.SelectionChanged += async (_, _) => { if (!populatingChoices) await LoadBucketsAsync(); };
        buckets.SelectionChanged += async (_, _) => { if (!populatingChoices) await LoadVideosAsync(); };
        search.TextChanged += (_, _) => Render();
        sort.SelectionChanged += (_, _) => Render();
        tableLayout = Windows.Storage.ApplicationData.Current.LocalSettings.Values["cloudStorageFileView"] as string == "Table";
        listView.Click += (_, _) => SetTableView(false);
        tableView.Click += (_, _) => SetTableView(true);
        videoSelection.ModeChanged += UpdateTableHeader;
        videoSelection.AddRowActions(item => ((CloudVideo)item).Filename, (menu, item) =>
        {
            videoSelection.SetTarget(item);
            PopulateMenu(menu, includeFilters: false);
        });
        list.SelectionChanged += async (_, _) =>
        {
            RenderDetails();
            UpdateActions();
            await CheckPermissionsAsync();
        };
        list.ContextRequested += (_, args) =>
        {
            var pointer = args.TryGetPosition(list, out var point);
            CloudVideo? target = null;
            var source = (pointer ? args.OriginalSource : FocusManager.GetFocusedElement(list.XamlRoot)) as FrameworkElement;
            for (var element = (DependencyObject?)source; element != null && element != list; element = VisualTreeHelper.GetParent(element))
                if (element is ListViewItem { Content: CloudVideo row }) { target = row; break; }
            if (target != null || pointer) videoSelection.SetTarget(target);
            var menu = CreateMenu(includeFilters: false);
            if (pointer) menu.ShowAt(list, point); else menu.ShowAt(source ?? list);
            args.Handled = true;
        };
        actions.Flyout = CreateMenu(includeFilters: true);
        search.ContextFlyout = SearchMenu();
        var refreshMenu = MenuAccessKeys.Create();
        refreshMenu.Items.Add(Item("Refresh", "Refresh Files", RefreshAsync));
        refresh.ContextFlyout = refreshMenu;
        refresh.Click += async (_, _) => await RefreshAsync();
        openEditor.Click += async (_, _) => await OpenSelectedVideoAsync();
        videoSelection.AddPrimaryAction(OpenSelectedVideoAsync);
        list.KeyDown += async (_, args) =>
        {
            if (args.Key != Windows.System.VirtualKey.Enter || !openEditor.IsEnabled) return;
            args.Handled = true;
            await OpenSelectedVideoAsync();
        };
        cancel.Click += (_, _) =>
        {
            if (importCancellation is { } cancellation)
            {
                status.Text = "Cancelling import…";
                cancellation.Cancel();
            }
            else Hide();
        };
        Opened += async (_, _) =>
        {
            if (settings.GetBool("vertexConnected")) await LoadProjectsAsync();
            else signIn.Focus(FocusState.Programmatic);
        };
        Closing += (_, _) => { closed = true; lifetime.Cancel(); permissionRequest?.Cancel(); };
        Closed += (_, _) => lifetime.Dispose();
        Render();
        UpdateActions();
    }

    private void BuildSignInPanel()
    {
        var note = new TextBlock { Text = "Sign in to Google Cloud to browse your storage buckets.", TextWrapping = TextWrapping.Wrap };
        AutomationProperties.SetLiveSetting(signInStatus, AutomationLiveSetting.Polite);
        var done = new Button { Content = "Done" };
        done.Click += (_, _) => Hide();
        signIn.Click += async (_, _) => await ConnectCloudAsync();
        cancelSignIn.Click += (_, _) => signInRequest?.Cancel();
        signedOutBrowser.Children.Add(note);
        signedOutBrowser.Children.Add(SettingsDialog.CompactActions(signIn, cancelSignIn, done));
        AutomationProperties.SetAccessibilityView(signInProgress, AccessibilityView.Raw);
        signedOutBrowser.Children.Add(SettingsDialog.CompactActions(signInProgress, signInStatus));
        var menu = MenuAccessKeys.Create();
        menu.Opening += (_, _) =>
        {
            menu.Items.Clear();
            menu.Items.Add(Item("Sign in with Google", "Sign in with Google", ConnectCloudAsync, signIn.IsEnabled));
            menu.Items.Add(Item("Cancel Sign-in", "Cancel Sign-in", () => { signInRequest?.Cancel(); return Task.CompletedTask; }, signInRequest != null));
            menu.Items.Add(Item("Done", "Done", () => { Hide(); return Task.CompletedTask; }));
        };
        signedOutBrowser.ContextFlyout = menu;
    }

    private async Task ConnectCloudAsync()
    {
        if (closed || signInRequest != null || settings.GetBool("vertexConnected")) return;
        using var request = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token);
        signInRequest = request;
        signIn.IsEnabled = false;
        cancelSignIn.Visibility = Visibility.Visible;
        signInProgress.IsActive = true;
        signInProgress.Visibility = Visibility.Visible;
        signInStatus.Text = "Waiting for Google sign-in…";
        try
        {
            await YouTubeService.Shared.SignInToCloudAsync(request.Token);
            if (closed) return;
            settings.Reload();
            UpdateActions();
            await LoadProjectsAsync();
        }
        catch (OperationCanceledException) { if (!closed) signInStatus.Text = "Sign-in cancelled."; }
        catch (Exception ex) { if (!closed) signInStatus.Text = ex.Message; }
        finally
        {
            signInRequest = null;
            signIn.IsEnabled = true;
            signInProgress.IsActive = false;
            cancelSignIn.Visibility = signInProgress.Visibility = Visibility.Collapsed;
        }
    }

    private MenuFlyout CreateMenu(bool includeFilters)
    {
        var menu = MenuAccessKeys.Create();
        menu.Opening += (_, _) => PopulateMenu(menu, includeFilters);
        return menu;
    }

    private void PopulateMenu(MenuFlyout menu, bool includeFilters)
    {
        menu.Items.Clear();
        var sourceForMenu = CaptureSource();
        menu.Items.Add(videoSelection.ModeItem(!loading && list.Items.Count > 0));
        if (long.TryParse(Selection?.Size, out var size) && size > NewProjectDialog.MaximumLocalVideoBytes)
            menu.Items.Add(Item("Create Smaller AI Copy", "Smaller AI Copy",
                () => sourceForMenu == CaptureSource() ? OpenSelectedVideoAsync(true) : Task.CompletedTask, openEditor.IsEnabled));
        menu.Items.Add(Item("Open in Editor", "Open in Editor",
            () => sourceForMenu == CaptureSource() ? OpenSelectedVideoAsync() : Task.CompletedTask, openEditor.IsEnabled));
        if (Selection is { } file && file.Thumbnail == null && previewedFile != file)
            menu.Items.Add(Item("Load Thumbnail", "Load Thumbnail", () => LoadThumbnailAsync(file), !loading));
        if (CanRename) menu.Items.Add(Item("Rename Video…", "cloud-rename", () => ManageSelectedAsync(true, sourceForMenu)));
        if (CanDelete) menu.Items.Add(Item("Delete Video…", "Delete Project…", () => ManageSelectedAsync(false, sourceForMenu)));
        menu.Items.Add(Item("Refresh", "Refresh Files", RefreshAsync, !loading));
        if (!includeFilters) return;
        menu.Items.Add(new MenuFlyoutSeparator());
        foreach (var item in SearchItems()) menu.Items.Add(item);
        menu.Items.Add(Choices("Google Cloud project", "Cloud Storage", projects));
        menu.Items.Add(Choices("Source bucket", "Cloud Storage", buckets));
        menu.Items.Add(Choices("Sort by", "sort-segments", sort));
        var layouts = new MenuFlyoutSubItem { Text = "Video layout", Icon = MenuIcons.Create("Table"), IsEnabled = listView.IsEnabled };
        foreach (var item in LayoutItems()) layouts.Items.Add(item);
        menu.Items.Add(layouts);
    }

    private void SetTableView(bool table)
    {
        tableLayout = table;
        Windows.Storage.ApplicationData.Current.LocalSettings.Values["cloudStorageFileView"] = table ? "Table" : "List";
        Render();
    }

    private IEnumerable<ToggleMenuFlyoutItem> LayoutItems()
    {
        foreach (var table in new[] { false, true })
        {
            var item = new ToggleMenuFlyoutItem {
                Text = table ? "Table View" : "List View", Icon = MenuIcons.Create(table ? "Table View" : "List"),
                IsChecked = tableLayout == table, IsEnabled = listView.IsEnabled
            };
            item.Click += (_, _) => SetTableView(table);
            yield return item;
        }
    }

    private MenuFlyout LayoutMenu()
    {
        var menu = MenuAccessKeys.Create();
        menu.Opening += (_, _) =>
        {
            menu.Items.Clear();
            foreach (var item in LayoutItems()) menu.Items.Add(item);
        };
        return menu;
    }

    private MenuFlyout SearchMenu()
    {
        var menu = MenuAccessKeys.Create();
        menu.Opening += (_, _) =>
        {
            menu.Items.Clear();
            foreach (var item in SearchItems()) menu.Items.Add(item);
        };
        return menu;
    }

    private MenuFlyoutItem[] SearchItems() => new[]
    {
        Item("Search Videos", "Search", () => { search.Focus(FocusState.Programmatic); return Task.CompletedTask; }, search.IsEnabled),
        Item("Clear Search", "Clear Search", () => { search.Text = ""; return Task.CompletedTask; }, search.Text.Length > 0)
    };

    private static MenuFlyoutSubItem Choices(string label, string icon, ComboBox box)
    {
        var menu = new MenuFlyoutSubItem { Text = label, Icon = MenuIcons.Create(icon), IsEnabled = box.IsEnabled };
        for (var index = 0; index < box.Items.Count; index++)
        {
            var choice = index;
            var text = box.Items[choice] is CloudProject project ? project.Label : box.Items[choice]?.ToString() ?? "";
            var item = new ToggleMenuFlyoutItem { Text = text, Icon = MenuIcons.Create(icon), IsChecked = box.SelectedIndex == choice };
            item.Click += (_, _) => box.SelectedIndex = choice;
            menu.Items.Add(item);
        }
        return menu;
    }

    private static MenuFlyoutItem Item(string title, string icon, Func<Task> run, bool enabled = true)
    {
        var item = new MenuFlyoutItem { Text = title, Icon = MenuIcons.Create(icon), IsEnabled = enabled };
        item.Click += async (_, _) => await run();
        return item;
    }

    private readonly TextBlock vertexProject = new() { TextWrapping = TextWrapping.Wrap };

    private void UpdateActions()
    {
        var connected = settings.GetBool("vertexConnected");
        connectedBrowser.Visibility = connected ? Visibility.Visible : Visibility.Collapsed;
        signedOutBrowser.Visibility = connected ? Visibility.Collapsed : Visibility.Visible;
        if (Content is Grid layout) layout.MinHeight = connected ? 0 : 340;
        var hasBucket = Bucket.Length > 0;
        projects.IsEnabled = connected && !loading;
        buckets.IsEnabled = connected && !loading && ProjectID.Length > 0;
        search.IsEnabled = sort.IsEnabled = listView.IsEnabled = tableView.IsEnabled = list.IsEnabled = connected && !loading && hasBucket;
        refresh.IsEnabled = actions.IsEnabled = connected && !loading;
        vertexProject.Visibility = ProjectID.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
        vertexProject.Text = $"Vertex AI project: {ProjectID}";
        openEditor.IsEnabled = connected && !loading && hasBucket && ProjectID.Length > 0 && Selection is not null;
    }

    private CloudVideoSelection? CaptureSelection() =>
        openEditor.IsEnabled && Selection is { } file ? new(ProjectID, Bucket, file.Name, file.Generation) : null;
    private Task OpenSelectedVideoAsync() => OpenSelectedVideoAsync(false);

    private async Task OpenSelectedVideoAsync(bool smallerAICopy)
    {
        if (CaptureSelection() is not { } selected || ImportVideo is not { } import) return;
        var importLimit = settings.Get("aiProvider") == "vertex" && settings.Get("vertexAuth", "signin") == "signin"
            ? NewProjectDialog.MaximumCloudVideoBytes : NewProjectDialog.MaximumLocalVideoBytes;
        if (!smallerAICopy && long.TryParse(Selection?.Size, out var size) && size > importLimit)
        {
            var warning = new ContentDialog
            {
                Title = "Video Is Too Large",
                Content = new TextBlock { Text = NewProjectDialog.AnalysisSizeWarning(size), TextWrapping = TextWrapping.Wrap },
                PrimaryButtonText = "Choose a Smaller Video",
                SecondaryButtonText = "Create Smaller AI Copy",
                CloseButtonText = "Cancel",
                DefaultButton = ContentDialogButton.Primary
            };
            var result = await WindowedDialog.ShowAsync(warning, owner, 720, 320);
            if (result == ContentDialogResult.Primary && !closed)
            {
                list.SelectedIndex = -1;
                list.Focus(FocusState.Keyboard);
            }
            if (result != ContentDialogResult.Secondary || closed || selected != CaptureSelection()) return;
            smallerAICopy = true;
        }
        selected = selected with { SmallerAICopy = smallerAICopy };
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token);
        importCancellation = cancellation;
        try
        {
            await WithLoadingAsync("Importing cloud video…", async () =>
            {
                ImportedProject = await import(selected, UpdateImportProgress, cancellation.Token);
                if (closed) { ImportedProject = null; return; }
                if (ImportedProject != null) Hide();
                else AnnounceImportStatus(cancellation.IsCancellationRequested ? "Import cancelled." : "Could not import the video.");
            });
        }
        finally
        {
            importCancellation = null;
            if (!closed) transfer.Visibility = Visibility.Collapsed;
        }
    }

    private int? announcedImportStep;

    private void AnnounceImportStatus(string message, int? step = null)
    {
        status.Text = message;
        if (step.HasValue && step == announcedImportStep) return;
        announcedImportStep = step;
        FrameworkElementAutomationPeer.CreatePeerForElement(status)?.RaiseNotificationEvent(
            AutomationNotificationKind.Other, AutomationNotificationProcessing.MostRecent, message, "cloud-import");
    }

    private void UpdateImportProgress(EngineEvent update)
    {
        if (closed || importCancellation is not { IsCancellationRequested: false } ||
            update.Data.TryGetProperty("networkActive", out _) || update.Data.TryGetProperty("ping", out _) ||
            update.Data.TryGetProperty("error", out _)) return;
        var byteTransfer = update.Message.StartsWith("Downloading video:", StringComparison.Ordinal) || update.Message.Contains("AI copy");
        if (byteTransfer && update.Progress is double fraction && double.IsFinite(fraction))
        {
            transfer.Value = Math.Clamp(fraction, 0, 1) * 100;
            AnnounceImportStatus(update.Message.Contains("AI copy") ? $"{update.Message}, {transfer.Value:F0} percent" : update.Message, (int)(transfer.Value / 10));
            transfer.Visibility = Visibility.Visible;
            progress.Visibility = Visibility.Collapsed;
        }
        else
        {
            AnnounceImportStatus(update.Message);
            transfer.Visibility = Visibility.Collapsed;
            progress.Visibility = Visibility.Visible;
        }
    }

    private bool CanRename => !loading && Selection is { ProjectOwned: false } && Selection == permissionTarget && permissions.Rename;
    private bool CanDelete => !loading && Selection is { ProjectOwned: false } && Selection == permissionTarget && permissions.Delete;

    private Task<List<EngineEvent>> RunAsync(string[] args, IProgress<EngineEvent>? updates = null)
    {
#if DEBUG
        if (fixture != null) return Task.FromResult(fixture(args));
#endif
        return new EngineClient(settings).RunAsync(args, updates, cancellation: lifetime.Token);
    }

    private async Task WithLoadingAsync(string message, Func<Task> operation, bool listing = false)
    {
        if (loading || closed || !settings.GetBool("vertexConnected")) return;
        loading = true;
        status.Text = message;
        if (listing)
        {
            status.Visibility = Visibility.Collapsed;
            centeredLoading.Visibility = Visibility.Visible;
            listingStatus.Text = message;
            listingRing.IsActive = true;
        }
        else
        {
            progress.Visibility = Visibility.Visible;
            progress.IsActive = true;
        }
        UpdateActions();
        try { await operation(); }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            if (!closed)
            {
                if (importCancellation != null) AnnounceImportStatus(ex.Message);
                else status.Text = ex.Message;
            }
        }
        finally
        {
            loading = false;
            listingRing.IsActive = progress.IsActive = false;
            centeredLoading.Visibility = progress.Visibility = Visibility.Collapsed;
            status.Visibility = Visibility.Visible;
            if (!closed) UpdateActions();
        }
    }

    private async Task LoadProjectsAsync()
    {
        var previous = ProjectID;
        var configured = settings.Get("vertexProject").Trim();
        await WithLoadingAsync("Loading Cloud projects…", async () =>
        {
            var choices = new List<CloudProject>();
            try
            {
                var events = await RunAsync(new[] { "cloud-projects" });
                foreach (var row in ResultRows(events, "cloudProjects"))
                    choices.Add(new(row.GetProperty("id").GetString()!, row.GetProperty("name").GetString()!));
            }
            catch (Exception ex) when (ex is not OperationCanceledException && configured.Length > 0)
            {
                status.Text = ex.Message;
            }
            if (closed) return;
            if (configured.Length > 0 && !choices.Any(project => project.Id == configured))
                choices.Insert(0, new(configured, configured));
            populatingChoices = true;
            projects.ItemsSource = choices;
            projects.SelectedItem = choices.FirstOrDefault(choice => choice.Id == previous) ??
                choices.FirstOrDefault(choice => choice.Id == configured);
            populatingChoices = false;
            if (choices.Count == 0) status.Text = "No accessible Cloud projects.";
        }, listing: true);
        if (!closed && ProjectID.Length > 0) await LoadBucketsAsync();
    }

    private async Task LoadBucketsAsync()
    {
        if (loading || closed) return;
        ResetVideos();
        populatingChoices = true;
        buckets.ItemsSource = null;
        populatingChoices = false;
        if (ProjectID.Length == 0) { UpdateActions(); return; }
        await WithLoadingAsync("Loading buckets…", async () =>
        {
            var configured = ProjectID == settings.Get("vertexProject") ? settings.Get("vertexBucket").Trim() : "";
            var choices = new List<string>();
            try
            {
                var events = await RunAsync(new[] { "cloud-browse", "--source-project", ProjectID });
                choices.AddRange(ResultRows(events, "cloudBuckets").Select(row => row.GetString()!));
                if (configured.Length > 0 && !choices.Contains(configured)) choices.Add(configured);
                status.Text = choices.Count == 0 ? "No buckets in this project." : "Choose a source bucket.";
            }
            catch (Exception ex) when (ex is not OperationCanceledException && configured.Length > 0)
            {
                choices.Add(configured);
                status.Text = ex.Message;
            }
            if (closed) return;
            buckets.ItemsSource = choices;
            if (selectConfiguredBucket && ProjectID == settings.Get("vertexProject"))
            {
                selectConfiguredBucket = false;
                if (choices.Contains(configured)) buckets.SelectedItem = configured;
            }
        }, listing: true);
        if (!closed && Bucket.Length > 0) await LoadVideosAsync();
    }

    private async Task LoadVideosAsync()
    {
        if (loading || closed) return;
        ResetVideos();
        if (Bucket.Length == 0) { UpdateActions(); return; }
        await WithLoadingAsync("Loading videos…", async () =>
        {
            var events = await RunAsync(new[] { "cloud-browse", "--source-project", ProjectID, "--source-bucket", Bucket });
            if (closed) return;
            videos = ResultRows(events, "cloudFiles").Where(row => row.GetProperty("video").GetBoolean())
                .Select(row => new CloudVideo(Value(row, "name")!, Value(row, "generation")!,
                    row.TryGetProperty("projectOwned", out var owned) && owned.GetBoolean(), Value(row, "size"),
                    Value(row, "contentType"), Value(row, "storageClass"), Value(row, "created"), Value(row, "updated"), Value(row, "thumbnail")))
                .ToArray();
            Render();
        }, listing: true);
    }

    private void ResetVideos()
    {
        previewedFile = null;
        previewThumbnail = null;
        videos = Array.Empty<CloudVideo>();
        list.SelectedItem = null;
        Render();
    }

    private Task RefreshAsync() => Bucket.Length > 0 ? LoadVideosAsync() : ProjectID.Length > 0 ? LoadBucketsAsync() : LoadProjectsAsync();

    private static IEnumerable<JsonElement> ResultRows(IEnumerable<EngineEvent> events, string key) =>
        events.SelectMany(entry => entry.Data.TryGetProperty(key, out var rows) ? rows.EnumerateArray().ToArray() : Array.Empty<JsonElement>());
    private static string? Value(JsonElement row, string key) => row.TryGetProperty(key, out var value) ? value.GetString() : null;

    private void Render()
    {
        var selected = Selection;
        var query = search.Text.Trim();
        var rows = videos.Where(row => query.Length == 0 || row.Name.Contains(query, StringComparison.OrdinalIgnoreCase));
        rows = sort.SelectedIndex switch
        {
            1 => rows.OrderByDescending(row => row.Name, StringComparer.CurrentCultureIgnoreCase),
            2 => rows.OrderByDescending(row => row.Updated, StringComparer.Ordinal).ThenBy(row => row.Name, StringComparer.CurrentCultureIgnoreCase),
            3 => rows.OrderByDescending(row => long.TryParse(row.Size, out var bytes) ? bytes : -1).ThenBy(row => row.Name, StringComparer.CurrentCultureIgnoreCase),
            _ => rows.OrderBy(row => row.Name, StringComparer.CurrentCultureIgnoreCase)
        };
        var visible = rows.ToArray();
        listView.IsChecked = !tableLayout;
        tableView.IsChecked = tableLayout;
        list.ItemTemplate = tableLayout ? TableTemplate : ListTemplate;
        UpdateTableHeader();
        videoScroll.HorizontalScrollMode = tableLayout ? ScrollMode.Auto : ScrollMode.Disabled;
        videoScroll.HorizontalScrollBarVisibility = tableLayout ? ScrollBarVisibility.Auto : ScrollBarVisibility.Disabled;
        AutomationProperties.SetName(list, tableLayout ? "Cloud videos table" : "Cloud videos list");
        list.ItemsSource = visible;
        videoSelection.SetTarget(visible.Contains(selected) ? selected : null);
        if (Bucket.Length > 0) status.Text = visible.Length == 0 ? "No matching videos" : $"{visible.Length} videos";
    }

    private void UpdateTableHeader()
    {
        var inset = videoSelection.Enabled ? VideoBrowserSelection.CheckboxColumnWidth : 0;
        list.Width = tableLayout ? 972 + inset : double.NaN;
        if (!tableLayout) { list.Header = null; return; }
        var header = (Grid)XamlReader.Load(TableHeader);
        if (videoSelection.Enabled)
        {
            header.Margin = new Thickness(inset, 0, 0, 0);
            var select = new TextBlock { Text = "Select", HorizontalAlignment = HorizontalAlignment.Left,
                Margin = new Thickness(4, 6, 0, 0) };
            var wrapper = new Grid();
            wrapper.Children.Add(header);
            wrapper.Children.Add(select);
            list.Header = wrapper;
        }
        else list.Header = header;
    }

    private void RenderDetails()
    {
        details.Children.Clear();
        if (Selection is not { } file) return;
        if (DecodeThumbnail(file.Thumbnail ?? (previewedFile == file ? previewThumbnail : null)) is { } bitmap)
        {
            var image = new Image { Width = 112, Height = 64, Source = bitmap, Stretch = Stretch.Uniform, HorizontalAlignment = HorizontalAlignment.Left };
            AutomationProperties.SetName(image, "Thumbnail for " + file.Filename);
            details.Children.Add(image);
        }
        else if (previewedFile != file)
        {
            var load = new Button { Content = "Load Thumbnail" };
            load.Click += async (_, _) => await LoadThumbnailAsync(file);
            var menu = MenuAccessKeys.Create();
            menu.Items.Add(Item("Load Thumbnail", "Load Thumbnail", () => LoadThumbnailAsync(file)));
            load.ContextFlyout = menu;
            details.Children.Add(load);
        }
        else details.Children.Add(new TextBlock { Text = "Thumbnail unavailable" });
        void Add(string label, string value) => details.Children.Add(new TextBlock { Text = label + ": " + value, TextWrapping = TextWrapping.Wrap });
        Add("Cloud path", $"gs://{Bucket}/{file.Name}");
        NewProjectDialog.AddCloudMetadata(Add, file.Size, file.ContentType, file.StorageClass, file.Created, file.Updated);
        if (file.ProjectOwned) details.Children.Add(new TextBlock { Text = "Manage this project-owned video from its project." });
    }

    private async Task LoadThumbnailAsync(CloudVideo file)
    {
        if (Selection != file || CaptureSource() is not { } source) return;
        await WithLoadingAsync("Loading thumbnail…", async () =>
        {
            var events = await RunAsync(source.Arguments("cloud-preview"));
            if (closed) return;
            previewThumbnail = null;
            foreach (var entry in events)
                if (entry.Data.TryGetProperty("cloudPreview", out var preview)) previewThumbnail = Value(preview, "thumbnail");
            previewedFile = file;
            if (previewThumbnail != null)
            {
                var updated = file with { Thumbnail = previewThumbnail };
                videos = videos.Select(video => video == file ? updated : video).ToArray();
                previewedFile = updated;
                Render();
                videoSelection.SetTarget(updated);
            }
            status.Text = previewThumbnail == null ? "Thumbnail unavailable" : "Thumbnail loaded";
            RenderDetails();
        });
    }

    private async Task CheckPermissionsAsync()
    {
        permissionRequest?.Cancel();
        permissionTarget = null;
        permissions = default;
        if (closed || Selection is not { ProjectOwned: false } file) return;
#if DEBUG
        if (fixture != null) return;
#endif
        using var request = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token);
        permissionRequest = request;
        try
        {
            var verified = await CloudPermissions.CheckAsync(settings, Bucket, file.Name, request.Token, ProjectID);
            if (!closed && !request.IsCancellationRequested && Selection == file)
            {
                permissions = verified;
                permissionTarget = file;
            }
        }
        finally { if (permissionRequest == request) permissionRequest = null; }
    }

    private Task ManageSelectedAsync(bool rename, CloudVideoSelection? source) =>
        source != null && source == CaptureSource() ? ManageAsync(rename) : Task.CompletedTask;

    private async Task ManageAsync(bool rename)
    {
        if ((rename ? !CanRename : !CanDelete) || CaptureSource() is not { } source) return;
        var filename = new TextBox { Header = "Video filename", Text = Selection!.Filename };
        var confirm = new ContentDialog
        {
            Title = rename ? "Rename Video" : "Delete Cloud Video?",
            Content = rename ? filename : new TextBlock { Text = $"Delete gs://{source.Bucket}/{source.Name} from Cloud Storage?", TextWrapping = TextWrapping.Wrap },
            PrimaryButtonText = rename ? "Rename" : "Delete Video",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Close
        };
        filename.TextChanged += (_, _) => confirm.IsPrimaryButtonEnabled = !string.IsNullOrWhiteSpace(filename.Text);
        if (await WindowedDialog.ShowAsync(confirm, owner, 540, 340) != ContentDialogResult.Primary ||
            source != CaptureSource() || (rename ? !CanRename : !CanDelete)) return;
        var applied = false;
        await WithLoadingAsync(rename ? "Renaming cloud video…" : "Deleting cloud video…", async () =>
        {
            var args = source.Arguments(rename ? "cloud-object-rename" : "cloud-object-delete");
            if (rename) args = args.Concat(new[] { "--new-name", filename.Text }).ToArray();
            await RunAsync(args, new Progress<EngineEvent>(entry =>
            {
                if (!closed && loading && !string.IsNullOrWhiteSpace(entry.Message) &&
                    !entry.Data.TryGetProperty("networkActive", out _)) status.Text = entry.Message;
            }));
            applied = true;
        });
        if (applied) await LoadVideosAsync();
        else await CheckPermissionsAsync();
    }

    private CloudVideoSelection? CaptureSource() => Selection is { } file ? new(ProjectID, Bucket, file.Name, file.Generation) : null;

    private static BitmapImage? DecodeThumbnail(string? thumbnail)
    {
        if (thumbnail is not { Length: > 0 and <= 4000 }) return null;
        try
        {
            using var bytes = new MemoryStream(Convert.FromBase64String(thumbnail));
            var bitmap = new BitmapImage { DecodePixelWidth = 112 };
            bitmap.SetSource(System.IO.WindowsRuntimeStreamExtensions.AsRandomAccessStream(bytes));
            return bitmap;
        }
        catch (Exception ex) when (ex is FormatException or ArgumentException or System.Runtime.InteropServices.COMException)
        {
            return null;
        }
    }

    private const string XamlNamespace = "xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation'";
    private const string ThumbnailCell = "<Grid Width='100' Height='60' AutomationProperties.AccessibilityView='Raw'><SymbolIcon Symbol='Video' Opacity='0.4'/><Image Source='{Binding ThumbnailImage}' Stretch='Uniform'/></Grid>";
    private static DataTemplate ListTemplate => (DataTemplate)XamlReader.Load(
        "<DataTemplate " + XamlNamespace + "><Grid ColumnSpacing='12' Padding='12,6'><Grid.ColumnDefinitions><ColumnDefinition Width='100'/><ColumnDefinition Width='*'/></Grid.ColumnDefinitions>" +
        ThumbnailCell + "<StackPanel Grid.Column='1' Spacing='4'><TextBlock Text='{Binding Filename}' FontWeight='SemiBold' TextWrapping='Wrap'/>" +
        "<TextBlock Text='{Binding Name}' TextTrimming='CharacterEllipsis'/><TextBlock Text='{Binding DisplaySize}'/></StackPanel></Grid></DataTemplate>");
    private static DataTemplate TableTemplate => (DataTemplate)XamlReader.Load(
        "<DataTemplate " + XamlNamespace + ">" + TableRow(ThumbnailCell, "{Binding Filename}", "{Binding Name}", "{Binding DisplaySize}", "{Binding DisplayUpdated}",
            $"<DropDownButton Name='{VideoBrowserSelection.ActionsButtonName}' Grid.Column='5' Content='Actions' VerticalAlignment='Center'/>") + "</DataTemplate>");
    private static string TableHeader => TableRow("<TextBlock Text='Thumbnail'/>", "Title", "Object path", "Size", "Modified",
        "<TextBlock Grid.Column='5' Text='Actions'/>", " " + XamlNamespace);
    private static string TableRow(string thumbnail, string name, string path, string size, string updated, string actions, string ns = "") =>
        $"<Grid{ns} ColumnSpacing='8' Padding='12,6'><Grid.ColumnDefinitions><ColumnDefinition Width='112'/><ColumnDefinition Width='184'/><ColumnDefinition Width='220'/><ColumnDefinition Width='92'/><ColumnDefinition Width='142'/><ColumnDefinition Width='116'/></Grid.ColumnDefinitions>" +
        thumbnail + $"<TextBlock Grid.Column='1' Text='{name}' TextTrimming='CharacterEllipsis' VerticalAlignment='Center'/><TextBlock Grid.Column='2' Text='{path}' TextTrimming='CharacterEllipsis' VerticalAlignment='Center'/>" +
        $"<TextBlock Grid.Column='3' Text='{size}' TextTrimming='CharacterEllipsis' VerticalAlignment='Center'/><TextBlock Grid.Column='4' Text='{updated}' TextTrimming='CharacterEllipsis' VerticalAlignment='Center'/>" +
        actions + "</Grid>";
}

internal sealed record CloudVideoSelection(string Project, string Bucket, string Name, string Generation, bool SmallerAICopy = false)
{
    internal string[] Arguments(string command) =>
        new[] { command, "--source-project", Project, "--source-bucket", Bucket, "--name", Name, "--source-generation", Generation };
}

internal record struct CloudPermissions(bool Rename, bool Delete)
{
    internal static async Task<CloudPermissions> CheckAsync(SettingsStore settings, string bucket, string name,
        CancellationToken cancellation, string? sourceProject = null)
    {
        if (!settings.GetBool("vertexConnected")) return default;
        try
        {
            var arguments = new List<string> { "cloud-object-permissions", "--source-bucket", bucket, "--name", name };
            if (!string.IsNullOrWhiteSpace(sourceProject)) arguments.AddRange(new[] { "--source-project", sourceProject });
            var events = await new EngineClient(settings).RunAsync(arguments, cancellation: cancellation);
            foreach (var entry in events)
                if (entry.Data.TryGetProperty("cloudPermissions", out var permissions))
                    return new(permissions.GetProperty("rename").GetBoolean(), permissions.GetProperty("delete").GetBoolean());
        }
        catch (Exception) { /* Keep mutation actions hidden when Cloud Storage cannot verify access. */ }
        return default;
    }
}
