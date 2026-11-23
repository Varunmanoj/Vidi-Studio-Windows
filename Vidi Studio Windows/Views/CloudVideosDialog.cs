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
    private readonly ComboBox sort = new() { Header = "Sort by", ItemsSource = new[] { "Name A–Z", "Name Z–A", "Recently Modified", "Largest First" }, SelectedIndex = 0 };
    private readonly ComboBox fileView = new() { Header = "File view", ItemsSource = new[] { "List", "Table" } };
    private readonly TextBox search = new() { Header = "Search videos", PlaceholderText = "Name or object path" };
    private readonly ListView list = new() { Height = 220, SelectionMode = ListViewSelectionMode.Single };
    private readonly TextBlock status = new() { TextWrapping = TextWrapping.Wrap };
    private readonly StackPanel details = new() { Spacing = 4 };
    private readonly ProgressRing progress = new() { Width = 18, Height = 18, Visibility = Visibility.Collapsed };
    private readonly ProgressBar transfer = new() { Minimum = 0, Maximum = 100, Visibility = Visibility.Collapsed };
    private readonly Button refresh = new() { Content = "Refresh" };
    private readonly DropDownButton actions = new() { Content = "Actions" };
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
                        new { name = "folder/zulu.mp4", generation = "12", size = "5000", video = true },
                        new { name = "alpha.mov", generation = "13", size = "100", video = true },
                        new { name = "notes.txt", generation = "14", size = "15", video = false }
                    } })
                    : JsonSerializer.SerializeToElement(new { cloudBuckets = new[] { "fixture-bucket", settings.Get("vertexBucket") } });
            return new() { new EngineEvent("Loaded", null, data) };
        };
    }

    internal async Task VerifyBrowserForSmokeTestAsync()
    {
        await WaitUntilAsync(() => projects.Items.Count == 2 && !loading);
        projects.SelectedIndex = projects.Items.ToList().FindIndex(item => item is CloudProject { Id: "fixture-source" });
        await WaitUntilAsync(() => buckets.Items.Contains("fixture-bucket") && !loading);
        buckets.SelectedItem = "fixture-bucket";
        await WaitUntilAsync(() => list.Items.Count == 2 && !loading);
        if (list.Items[0] is not CloudVideo { Name: "alpha.mov" })
            throw new InvalidOperationException("Cloud videos must sort by name and exclude other files.");
        sort.SelectedIndex = 3;
        if (list.Items[0] is not CloudVideo { Name: "folder/zulu.mp4" })
            throw new InvalidOperationException("Cloud videos did not sort by byte size.");
        search.Text = "FOLDER/";
        await WaitUntilAsync(() => list.Items.Count == 1 && list.Items[0] is CloudVideo { Name: "folder/zulu.mp4" });
        list.SelectedIndex = 0;
        if (CaptureSelection() is not { Project: "fixture-source", Bucket: "fixture-bucket", Generation: "12" })
            throw new InvalidOperationException("Opening a video must keep its selected project, bucket and generation.");
        if (CanRename || CanDelete) throw new InvalidOperationException("Cloud mutation actions must be hidden when permissions are denied.");
        fileView.SelectedIndex = 1;
        if (list.Header == null || CaptureSelection()?.Generation != "12")
            throw new InvalidOperationException("Cloud table view must preserve the selection.");
    }

    internal async Task VerifyConfiguredImportForSmokeTestAsync(StudioProject project)
    {
        await WaitUntilAsync(() => list.Items.Count == 2 && !loading);
        if (ProjectID != settings.Get("vertexProject") || Bucket != settings.Get("vertexBucket") || IsPrimaryButtonEnabled)
            throw new InvalidOperationException("The cloud browser must select the configured project and bucket and wait for a video.");
        ImportVideo = (selection, report, _) =>
        {
            if (selection.Bucket != Bucket || selection.Generation != "13")
                throw new InvalidOperationException("The cloud import lost the selected bucket and generation.");
            report(new EngineEvent("Downloading video: 50 of 100 bytes", .5, JsonSerializer.SerializeToElement(new { progress = .5 })));
            if (transfer.Visibility != Visibility.Visible || transfer.Value != 50 || IsPrimaryButtonEnabled)
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
    }

    internal CloudVideosDialog(Window owner, ElementTheme theme, SettingsStore settings, bool selectConfiguredBucket = false)
    {
        this.owner = owner;
        this.settings = settings;
        this.selectConfiguredBucket = selectConfiguredBucket;
        RequestedTheme = theme;
        MaxWidth = 780;
        Title = "Open Cloud Videos";
        PrimaryButtonText = "Open in Editor";
        CloseButtonText = "Cancel";
        DefaultButton = ContentDialogButton.Primary;
        IsPrimaryButtonEnabled = false;
        AutomationProperties.SetName(list, "Bucket video list");
        AutomationProperties.SetLiveSetting(status, AutomationLiveSetting.Polite);
        AutomationProperties.SetName(transfer, "Cloud video import progress");

        var selectors = new StackPanel { Spacing = 8 };
        SettingsDialog.CompactRow(selectors, "Google Cloud project", projects, true);
        SettingsDialog.CompactRow(selectors, "Source bucket", buckets, true);
        var filters = new StackPanel { Spacing = 8 };
        SettingsDialog.CompactRow(filters, "Search videos", search, true);
        SettingsDialog.CompactRow(filters, "Sort by", sort, true);
        SettingsDialog.CompactRow(filters, "File view", fileView, true);
        search.Header = null;
        sort.Header = null;
        fileView.Header = null;
        projects.Header = null;
        buckets.Header = null;

        var stack = new StackPanel { Spacing = 10, MaxWidth = 720 };
        stack.Children.Add(CompactDialogLayout.Card(selectors, "Cloud Storage"));
        stack.Children.Add(CompactDialogLayout.Card(filters, "Video filters"));
        stack.Children.Add(list);
        stack.Children.Add(new AccessibilityGroup("Selected video details") { Child = details });
        stack.Children.Add(progress);
        stack.Children.Add(transfer);
        stack.Children.Add(status);
        stack.Children.Add(vertexProject);
        stack.Children.Add(SettingsDialog.CompactActions(refresh, actions));
        Content = new ScrollViewer { Content = stack, MaxHeight = 650, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };

        projects.SelectionChanged += async (_, _) => { if (!populatingChoices) await LoadBucketsAsync(); };
        buckets.SelectionChanged += async (_, _) => { if (!populatingChoices) await LoadVideosAsync(); };
        search.TextChanged += (_, _) => Render();
        sort.SelectionChanged += (_, _) => Render();
        fileView.SelectedIndex = Windows.Storage.ApplicationData.Current.LocalSettings.Values["cloudStorageFileView"] as string == "Table" ? 1 : 0;
        fileView.SelectionChanged += (_, _) =>
        {
            Windows.Storage.ApplicationData.Current.LocalSettings.Values["cloudStorageFileView"] = fileView.SelectedIndex == 1 ? "Table" : "List";
            Render();
        };
        list.SelectionChanged += async (_, _) =>
        {
            RenderDetails();
            UpdateActions();
            await CheckPermissionsAsync();
        };
        list.RightTapped += (_, args) =>
        {
            CloudVideo? target = null;
            for (var element = args.OriginalSource as DependencyObject; element != null && element != list; element = VisualTreeHelper.GetParent(element))
                if (element is ListViewItem { Content: CloudVideo row }) { target = row; break; }
            list.SelectedItem = target;
        };
        list.ContextFlyout = CreateMenu(includeFilters: false);
        actions.Flyout = CreateMenu(includeFilters: true);
        search.ContextFlyout = SearchMenu();
        var refreshMenu = MenuAccessKeys.Create();
        refreshMenu.Items.Add(Item("Refresh", "Refresh Files", RefreshAsync));
        refresh.ContextFlyout = refreshMenu;
        refresh.Click += async (_, _) => await RefreshAsync();
        PrimaryButtonClick += async (_, args) =>
        {
            args.Cancel = true;
            await OpenSelectedVideoAsync();
        };
        CloseButtonClick += (_, args) =>
        {
            if (importCancellation == null) return;
            args.Cancel = true;
            status.Text = "Cancelling import…";
            importCancellation.Cancel();
        };
        Opened += async (_, _) => await LoadProjectsAsync();
        Closing += (_, _) => { closed = true; lifetime.Cancel(); permissionRequest?.Cancel(); };
        Closed += (_, _) => lifetime.Dispose();
        Render();
        UpdateActions();
    }

    private MenuFlyout CreateMenu(bool includeFilters)
    {
        var menu = MenuAccessKeys.Create();
        menu.Opening += (_, _) =>
        {
            menu.Items.Clear();
            menu.Items.Add(Item("Open in Editor", "Open in Editor", OpenSelectedVideoAsync, IsPrimaryButtonEnabled));
            if (Selection is { } file && file.Thumbnail == null && previewedFile != file)
                menu.Items.Add(Item("Load Thumbnail", "Load Thumbnail", () => LoadThumbnailAsync(file), !loading));
            if (CanRename) menu.Items.Add(Item("Rename Video…", "cloud-rename", () => ManageAsync(true)));
            if (CanDelete) menu.Items.Add(Item("Delete Video…", "Delete Project…", () => ManageAsync(false)));
            menu.Items.Add(Item("Refresh", "Refresh Files", RefreshAsync, !loading));
            if (!includeFilters) return;
            menu.Items.Add(new MenuFlyoutSeparator());
            foreach (var item in SearchItems()) menu.Items.Add(item);
            menu.Items.Add(Choices("Google Cloud project", "Cloud Storage", projects));
            menu.Items.Add(Choices("Source bucket", "Cloud Storage", buckets));
            menu.Items.Add(Choices("Sort by", "sort-segments", sort));
            menu.Items.Add(Choices("File view", "List", fileView));
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
        var hasBucket = Bucket.Length > 0;
        projects.IsEnabled = connected && !loading;
        buckets.IsEnabled = connected && !loading && ProjectID.Length > 0;
        search.IsEnabled = sort.IsEnabled = fileView.IsEnabled = list.IsEnabled = connected && !loading && hasBucket;
        refresh.IsEnabled = actions.IsEnabled = connected && !loading;
        vertexProject.Visibility = ProjectID.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
        vertexProject.Text = $"Vertex AI project: {ProjectID}";
        IsPrimaryButtonEnabled = connected && !loading && hasBucket && ProjectID.Length > 0 && Selection is not null;
    }

    private CloudVideoSelection? CaptureSelection() =>
        IsPrimaryButtonEnabled && Selection is { } file ? new(ProjectID, Bucket, file.Name, file.Generation) : null;
    private async Task OpenSelectedVideoAsync()
    {
        if (CaptureSelection() is not { } selected || ImportVideo is not { } import) return;
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
        var byteTransfer = update.Message.StartsWith("Downloading video:", StringComparison.Ordinal);
        if (byteTransfer && update.Progress is double fraction && double.IsFinite(fraction))
        {
            transfer.Value = Math.Clamp(fraction, 0, 1) * 100;
            AnnounceImportStatus(update.Message, (int)(transfer.Value / 10));
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

    private async Task WithLoadingAsync(string message, Func<Task> operation)
    {
        if (loading || closed || !settings.GetBool("vertexConnected")) return;
        loading = true;
        status.Text = message;
        progress.Visibility = Visibility.Visible;
        progress.IsActive = true;
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
            if (!closed) { progress.IsActive = false; progress.Visibility = Visibility.Collapsed; UpdateActions(); }
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
        });
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
        });
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
        });
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
        var table = fileView.SelectedIndex == 1;
        list.ItemTemplate = table ? TableTemplate : ListTemplate;
        list.Header = table ? XamlReader.Load(TableHeader) : null;
        list.ItemsSource = visible;
        list.SelectedItem = visible.Contains(selected) ? selected : null;
        if (Bucket.Length > 0) status.Text = visible.Length == 0 ? "No matching videos" : $"{visible.Length} videos";
    }

    private void RenderDetails()
    {
        details.Children.Clear();
        if (Selection is not { } file) return;
        if ((file.Thumbnail ?? (previewedFile == file ? previewThumbnail : null)) is { Length: > 0 and <= 4000 } thumbnail)
        {
            try
            {
                using var bytes = new MemoryStream(Convert.FromBase64String(thumbnail));
                var bitmap = new BitmapImage { DecodePixelWidth = 112 };
                bitmap.SetSource(System.IO.WindowsRuntimeStreamExtensions.AsRandomAccessStream(bytes));
                var image = new Image { Width = 112, Height = 64, Source = bitmap, Stretch = Stretch.Uniform, HorizontalAlignment = HorizontalAlignment.Left };
                AutomationProperties.SetName(image, "Thumbnail for " + file.Filename);
                details.Children.Add(image);
            }
            catch (Exception ex) when (ex is FormatException or ArgumentException or System.Runtime.InteropServices.COMException) { }
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
            var verified = await CloudPermissions.CheckAsync(settings, Bucket, file.Name, request.Token);
            if (!closed && !request.IsCancellationRequested && Selection == file)
            {
                permissions = verified;
                permissionTarget = file;
            }
        }
        finally { if (permissionRequest == request) permissionRequest = null; }
    }

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
        if (await WindowedDialog.ShowAsync(confirm, owner, 540, 340) != ContentDialogResult.Primary) return;
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

    private const string XamlNamespace = "xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation'";
    private static DataTemplate ListTemplate => (DataTemplate)XamlReader.Load(
        "<DataTemplate " + XamlNamespace + "><StackPanel Spacing='2'><TextBlock Text='{Binding Filename}' FontWeight='SemiBold'/>" +
        "<TextBlock Text='{Binding Name}' TextTrimming='CharacterEllipsis'/></StackPanel></DataTemplate>");
    private static DataTemplate TableTemplate => (DataTemplate)XamlReader.Load(
        "<DataTemplate " + XamlNamespace + ">" + TableRow("{Binding Filename}", "{Binding Name}", "{Binding DisplaySize}", "{Binding DisplayUpdated}") + "</DataTemplate>");
    private static string TableHeader => TableRow("Video", "Object path", "Size", "Modified", " " + XamlNamespace);
    private static string TableRow(string name, string path, string size, string updated, string ns = "") =>
        $"<Grid{ns} ColumnSpacing='8'><Grid.ColumnDefinitions><ColumnDefinition Width='2*'/><ColumnDefinition Width='2*'/><ColumnDefinition Width='*'/><ColumnDefinition Width='1.5*'/></Grid.ColumnDefinitions>" +
        $"<TextBlock Text='{name}' TextTrimming='CharacterEllipsis'/><TextBlock Grid.Column='1' Text='{path}' TextTrimming='CharacterEllipsis'/>" +
        $"<TextBlock Grid.Column='2' Text='{size}' TextTrimming='CharacterEllipsis'/><TextBlock Grid.Column='3' Text='{updated}' TextTrimming='CharacterEllipsis'/></Grid>";
}

internal sealed record CloudVideoSelection(string Project, string Bucket, string Name, string Generation)
{
    internal string[] Arguments(string command) =>
        new[] { command, "--source-project", Project, "--source-bucket", Bucket, "--name", Name, "--source-generation", Generation };
}

internal record struct CloudPermissions(bool Rename, bool Delete)
{
    internal static async Task<CloudPermissions> CheckAsync(SettingsStore settings, string bucket, string name, CancellationToken cancellation)
    {
        if (!settings.GetBool("vertexConnected")) return default;
        try
        {
            var events = await new EngineClient(settings).RunAsync(
                new[] { "cloud-object-permissions", "--source-bucket", bucket, "--name", name }, cancellation: cancellation);
            foreach (var entry in events)
                if (entry.Data.TryGetProperty("cloudPermissions", out var permissions))
                    return new(permissions.GetProperty("rename").GetBoolean(), permissions.GetProperty("delete").GetBoolean());
        }
        catch (Exception) { /* Keep mutation actions hidden when Cloud Storage cannot verify access. */ }
        return default;
    }
}
