using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Markup;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using System;
using System.Diagnostics;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Vidi_Studio_Windows.Services;
using Vidi_Studio_Windows.Models;
using Windows.Media.Core;
using Windows.Storage;
using Windows.Storage.Pickers;

namespace Vidi_Studio_Windows.Views;

/// <summary>Inspects a source without creating a project or copying the video.</summary>
public sealed class NewProjectDialog : ContentDialog
{
    internal const int WindowWidth = 760;
    internal const int WindowHeight = 544;

    internal Func<Window, Task<(YouTubeVideo Video, bool OpenInEditor)?>>? BrowseYouTubeVideo { get; set; }
    internal YouTubeVideo? SelectedYouTubeVideo { get; private set; }
    internal bool OpenSelectedYouTubeInEditor { get; private set; }
    public bool ChooseYouTubeCaptions => chooseYouTubeCaptions.IsChecked == true && IsYouTube;
    private readonly CheckBox chooseYouTubeCaptions = new() { Content = "Choose YouTube captions after creating" };
    private readonly Window owner;
    private readonly SettingsStore settings;
    private readonly CompactActionsPanel sourceOptions = new() { VerticalAlignment = VerticalAlignment.Center };
    private readonly TextBlock sourceLabel = new() { Text = "Project type", VerticalAlignment = VerticalAlignment.Center };
    private readonly ScrollViewer form = new()
    {
        HorizontalContentAlignment = HorizontalAlignment.Stretch,
        HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
        VerticalScrollBarVisibility = ScrollBarVisibility.Auto
    };
    private readonly RadioButton local = new() { GroupName = "project-source" };
    private readonly RadioButton cloud = new() { GroupName = "project-source", Content = "Cloud Storage" };
    private readonly StackPanel cloudModeFields = new();
    private readonly Grid videoFileRow = new() { ColumnSpacing = 12 };
    private readonly ComboBox cloudMode = new() { Header = "Video source", ItemsSource = new[] { "Upload Local Video", "Choose from Cloud Storage" }, SelectedIndex = 0 };
    private readonly Button browseCloud = new() { HorizontalAlignment = HorizontalAlignment.Right };
    private bool browsing;
    internal Window? CompanionWindow { get; set; }
    internal Func<Window, string, Task<StudioProject?>>? BrowseCloudVideo { get; set; }
    internal StudioProject? ImportedCloudProject { get; private set; }
    public bool IsCloud => cloud.IsChecked == true;
    private readonly RadioButton youtube = new() { GroupName = "project-source" };
    private readonly StackPanel localFields = new() { Spacing = 12 };
    private readonly StackPanel youtubeFields = new() { Spacing = 12 };
    private readonly TextBlock selectedFile = new() { Text = "No video selected", Opacity = .7, TextWrapping = TextWrapping.Wrap };
    private readonly TextBlock details = new() { TextWrapping = TextWrapping.Wrap, Visibility = Visibility.Collapsed };
    private readonly MediaPlayerElement preview = new() { Height = 160, AreTransportControlsEnabled = true, Visibility = Visibility.Collapsed };
    private readonly TextBox youtubeUrl = new() { PlaceholderText = "https://youtu.be/…", Header = "YouTube video URL", Width = 470, HorizontalAlignment = HorizontalAlignment.Left };
    private readonly Button fetchDetails = new() { Content = "Fetch Details", Width = 120, VerticalAlignment = VerticalAlignment.Bottom };
    private readonly RadioButton createDescription = new() { GroupName = "ad-mode", Content = "Create Audio Description in VidiStudio", IsChecked = true };
    private readonly RadioButton importDescription = new() { GroupName = "ad-mode", Content = "I already have Audio Description files" };
    private readonly TextBlock descriptionGuidance = new() { TextWrapping = TextWrapping.Wrap, Opacity = .65, FontSize = 12 };
    private readonly Border localCard;
    private readonly Border youtubeCard;
    public bool ImportAssetsAfterCreation => importDescription.IsChecked == true;
    private readonly ImportAssetsPicker assets;
    private readonly ImportAssetsPicker originalAssets;
    private readonly Border originalAssetsCard;
    private readonly Border assetsCard;
    private readonly TextBlock libraryLocation = new() { TextWrapping = TextWrapping.Wrap, Opacity = .7, FontSize = 12 };
    public string LibraryPath { get; private set; }
    public ImportAssetSelection AssetSelection => (ImportAssetsAfterCreation ? assets.Selection : new ImportAssetSelection()) with
    {
        OriginalAudioPath = IsYouTube ? originalAssets.Selection.OriginalAudioPath : null,
        OriginalAudioPlayback = originalAssets.Selection.OriginalAudioPlayback,
        OriginalAudioTranscription = originalAssets.Selection.OriginalAudioTranscription
    };
    public bool CombineRequested { get; private set; }
    private readonly Border youtubePreview = new() { Visibility = Visibility.Collapsed, CornerRadius = new CornerRadius(8), Padding = new Thickness(12), Background = new SolidColorBrush(Windows.UI.Color.FromArgb(24, 128, 128, 128)) };
    private readonly ProgressRing youtubeLoadingRing = new() { Width = 24, Height = 24, HorizontalAlignment = HorizontalAlignment.Center };
    private readonly StackPanel youtubeLoading = new() { HorizontalAlignment = HorizontalAlignment.Center, Spacing = 8, Visibility = Visibility.Collapsed };
    private readonly Border youtubeError;
    private readonly TextBlock youtubeStatus;
    private readonly TextBlock sourceHeading = new() { Text = "Video File", FontSize = 15, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold, Margin = new Thickness(2, 4, 0, 0) };
    private static readonly HttpClient http = new() { Timeout = TimeSpan.FromSeconds(15) };
    private CancellationTokenSource? inspection;
    private CancellationTokenSource? youtubeInspection;
    private string? fetchedYouTubeId;
    internal enum VideoImportChoice { Local, Cloud, Smaller, Cancel }
    internal const long MaximumLocalVideoBytes = 2_000_000_000;
    internal const long MaximumCloudVideoBytes = 2L * 1024 * 1024 * 1024 - 500 * 1024;

    private bool validLocal;
    private bool videoRequiresCloud;
    public bool IsYouTube => youtube.IsChecked == true;
    public string? VideoPath { get; private set; }
    public string YouTubeUrl => youtubeUrl.Text.Trim();
#if DEBUG
    internal Task BrowseYouTubeForSmokeTestAsync() => BrowseYouTubeVideosAsync();
    internal void SetYouTubeCaptionsForSmokeTest() => chooseYouTubeCaptions.IsChecked = true;
    internal void VerifyInitialSourceForSmokeTest(string command)
    {
        bool matches = command switch {
            "new-project" => local.IsChecked == true && videoFileRow.Visibility == Visibility.Visible,
            "new-youtube-project" => IsYouTube && youtubeFields.Visibility == Visibility.Visible,
            "new-cloud-project" => IsCloud && cloudMode.SelectedIndex == 1 && browseCloud.Visibility == Visibility.Visible,
            _ => false
        };
        if (!matches || IsPrimaryButtonEnabled)
            throw new InvalidOperationException("New Project opened with the wrong source or enabled creation prematurely: " + command);
    }

    internal async Task VerifyCloudBrowserForSmokeTestAsync(bool connected, StudioProject project)
    {
        if (cloud.Visibility != Visibility.Visible || cloud.IsEnabled != connected)
            throw new InvalidOperationException("Cloud Storage must be visible and enabled only after Google sign-in.");
        if (!connected) {
            if (cloudModeFields.Visibility != Visibility.Collapsed || browseCloud.Visibility != Visibility.Collapsed)
                throw new InvalidOperationException("Cloud transfer controls shown before sign-in.");
            return;
        }
        var attempts = 0;
        BrowseCloudVideo = async (window, library) =>
        {
            if (library != LibraryPath) throw new InvalidOperationException("Cloud import lost the selected project location.");
            if (++attempts == 1) return null;
            var browser = new CloudVideosDialog(window, ActualTheme, settings, selectConfiguredBucket: true);
            browser.UseFixtureForSmokeTest();
            var showing = WindowedDialog.ShowAsync(browser, window, 820, 820);
            try { await browser.VerifyConfiguredImportForSmokeTestAsync(project); }
            finally { browser.Hide(); await showing; }
            return browser.ImportedProject;
        };
        cloud.IsChecked = true;
        cloudMode.SelectedIndex = 1;
        await Task.Yield();
        if (attempts != 1 || browseCloud.Visibility != Visibility.Visible || videoFileRow.Visibility != Visibility.Collapsed || IsPrimaryButtonEnabled)
            throw new InvalidOperationException("Choosing an existing cloud video must open the separate browser.");
        if (ImportedCloudProject != null)
            throw new InvalidOperationException("Cancelling the browser must keep New Project available to retry.");
        await BrowseCloudVideosAsync();
        if (attempts != 2 || ImportedCloudProject != project)
            throw new InvalidOperationException("New Project did not accept the project from the separate browser.");
    }
    internal async Task InspectVideoForSmokeTestAsync(string path) => await InspectVideoAsync(await StorageFile.GetFileFromPathAsync(path));
    internal string DetailsForSmokeTest => details.Text;
    internal FrameworkElement YouTubeFieldsForSmokeTest => youtubeFields;
    internal void SetAssetsForSmokeTest(ImportAssetSelection selection) { importDescription.IsChecked = true; assets.SetFilesForSmokeTest(selection); }
    internal void ScrollToAssetsForSmokeTest() => form.ChangeView(null, form.ScrollableHeight, null, true);
    internal FrameworkElement[] SourceLabelsForSmokeTest => new FrameworkElement[] { sourceLabel, local, youtube, cloud, sourceHeading };
    internal bool LocalFormFitsForSmokeTest => form.ScrollableHeight <= 1;
    internal async Task VerifySourceSwitchesForSmokeTestAsync(Func<string, Task> verifyBounds)
    {
        foreach (var (name, choice, mode) in new[] { ("youtube", youtube, 0), ("upload", cloud, 0), ("bucket", cloud, 1), ("local", local, 0) })
        {
            cloudMode.SelectedIndex = mode;
            choice.IsChecked = true;
            await Task.Delay(150);
            if (choice == youtube && form.ScrollableHeight <= 0)
                throw new InvalidOperationException("The YouTube form must scroll in the fixed window.");
            await verifyBounds(name);
        }
    }
    internal void SelectYouTubeForSmokeTest(string url) { youtube.IsChecked = true; youtubeUrl.Text = url; UpdateYouTubeDetails(); }
    internal void SetYouTubeDetailsForSmokeTest(string title)
    {
        fetchedYouTubeId = ValidYouTubeId(YouTubeUrl);
        ShowYouTubePreview(fetchedYouTubeId!, title, "Test channel", $"https://i.ytimg.com/vi/{fetchedYouTubeId}/hqdefault.jpg");
        UpdateSource();
    }
    internal async Task VerifyYouTubeLinkForSmokeTestAsync(Func<FrameworkElement, Task> capture)
    {
        SelectYouTubeForSmokeTest("https://www.youtube.com/watch?v=jNQXAC9IVRw");
        var data = new System.Text.Json.Nodes.JsonObject
        {
            ["videoID"] = "jNQXAC9IVRw",
            ["title"] = "Test video",
            ["visibility"] = "unlisted",
            ["duration"] = 30.0
        };
        await FetchYouTubeDetailsAsync((id, _, _) => Task.FromResult(new YouTubeVideo(data)));
        if (!IsPrimaryButtonEnabled || SelectedYouTubeVideo?.Visibility != "unlisted" ||
            youtubeError.Visibility != Visibility.Collapsed ||
            youtubePreview.Child is not Grid previewCard ||
            !previewCard.Children.OfType<StackPanel>().Single().Children.OfType<TextBlock>().Any(text => text.Text == "Visibility: Unlisted"))
            throw new InvalidOperationException("Fetch Details did not show the unlisted video visibility and enable Create Project.");

        foreach (var message in new[] { YouTubeVideo.PrivateVideoMessage, YouTubeLinkIssueException.UnavailableMessage })
        {
            await FetchYouTubeDetailsAsync((_, _, _) => Task.FromException<YouTubeVideo>(new YouTubeLinkIssueException(message)));
            if (IsPrimaryButtonEnabled || SelectedYouTubeVideo != null || youtubePreview.Visibility != Visibility.Collapsed ||
                youtubeError.Visibility != Visibility.Visible || youtubeStatus.Text != message)
                throw new InvalidOperationException("Fetch Details did not clear the preview and block creation for an inaccessible link.");
            var icon = (FontIcon)((Grid)youtubeError.Child).Children[0];
            youtubeError.UpdateLayout();
            if (icon.Glyph != "\uE711" || icon.ActualWidth <= 0 || youtubeStatus.ActualWidth <= 0 ||
                youtubeStatus.FontSize < 16 || youtubeStatus.TextWrapping != TextWrapping.Wrap ||
                youtubeStatus.Foreground is not SolidColorBrush textBrush || icon.Foreground is not SolidColorBrush iconBrush ||
                !textBrush.Color.Equals(iconBrush.Color) || textBrush.Color.R <= textBrush.Color.G || textBrush.Color.R <= textBrush.Color.B)
                throw new InvalidOperationException("The YouTube error must show larger wrapping red text beside a matching cross.");
        }
        await capture(youtubeError);
    }
#endif

    public NewProjectDialog(Window owner, ElementTheme theme, SettingsStore settings, bool openYouTube = false, bool openCloud = false)
    {
        this.owner = owner; this.settings = settings;
        Loaded += (_, _) => ConnectivityService.Shared.Changed += ConnectivityChanged;
        Unloaded += (_, _) => ConnectivityService.Shared.Changed -= ConnectivityChanged;
        LibraryPath = settings.Get("libraryPath");
        RequestedTheme = theme;
        Width = 760; MaxWidth = 760;
        Resources["ContentDialogMaxWidth"] = 760d;
        Resources["ContentDialogMaxHeight"] = 1000d;
        Title = "New Project"; PrimaryButtonText = "Create Project"; CloseButtonText = "Cancel";
        FullSizeDesired = true;
        DefaultButton = ContentDialogButton.Primary;
        local.VerticalContentAlignment = VerticalAlignment.Center;
        youtube.VerticalContentAlignment = VerticalAlignment.Center;
        cloud.VerticalContentAlignment = VerticalAlignment.Center;
        local.Content = SourceLabel("\uE8B2", "Video File");
        youtube.Content = SourceLabel("\uE768", "YouTube Video");
        cloud.Content = SourceLabel("\uE753", "Cloud Storage");
        cloud.IsEnabled = settings.GetBool("vertexConnected");
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(cloud, "Cloud Storage");
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetHelpText(cloud, "Cloud Storage requires Google sign-in in AI settings.");
        var chooser = new Button { Content = SourceLabel("\uE8B7", "Choose Video…"), HorizontalAlignment = HorizontalAlignment.Right };
        chooser.Click += async (_, _) => await ChooseVideoAsync();
        var fileRow = videoFileRow;
        fileRow.ColumnDefinitions.Add(new ColumnDefinition());
        fileRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        fileRow.Children.Add(new TextBlock { Text = "Video file", VerticalAlignment = VerticalAlignment.Center });
        Grid.SetColumn(chooser, 1); fileRow.Children.Add(chooser);
        cloudModeFields.Children.Add(cloudMode);
        browseCloud.Content = SourceLabel("\uE753", "Choose from Cloud Storage…");
        browseCloud.Click += async (_, _) => await BrowseCloudVideosAsync();
        var cloudMenu = MenuAccessKeys.Create();
        var browseItem = new MenuFlyoutItem { Text = "Choose from Cloud Storage…", Icon = MenuIcons.Create("open-cloud-videos") };
        browseItem.Click += async (_, _) => await BrowseCloudVideosAsync();
        cloudMenu.Opening += (_, _) => browseItem.IsEnabled = CanBrowseCloud;
        cloudMenu.Items.Add(browseItem);
        browseCloud.ContextFlyout = cloudMenu;
        cloudModeFields.Children.Add(browseCloud);
        localFields.Children.Add(cloudModeFields);
        localFields.Children.Add(fileRow);
        localFields.Children.Add(selectedFile);
        localFields.Children.Add(preview);
        localFields.Children.Add(details);
        var youtubeRow = new Grid { ColumnSpacing = 8 };
        youtubeRow.ColumnDefinitions.Add(new ColumnDefinition());
        youtubeRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        youtubeUrl.Header = null; youtubeUrl.Width = double.NaN; youtubeUrl.HorizontalAlignment = HorizontalAlignment.Stretch;
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(youtubeUrl,"YouTube video URL");
        youtubeRow.Children.Add(youtubeUrl);
        Grid.SetColumn(fetchDetails,1); youtubeRow.Children.Add(fetchDetails);
        var browseYouTube = new Button { Content = SourceLabel("\uE768", "My YouTube Videos…") };
        browseYouTube.Click += async (_, _) => await BrowseYouTubeVideosAsync();
        var youtubeMenu = MenuAccessKeys.Create();
        var browseYouTubeItem = new MenuFlyoutItem { Text = "My YouTube Videos…", Icon = MenuIcons.Create("youtube-browser") };
        browseYouTubeItem.Click += async (_, _) => await BrowseYouTubeVideosAsync();
        var captionsItem = new ToggleMenuFlyoutItem { Text = "Choose YouTube captions after creating", Icon = MenuIcons.Create("youtube-captions") };
        youtubeMenu.Opening += (_, _) => captionsItem.IsChecked = ChooseYouTubeCaptions;
        captionsItem.Click += (_, _) => chooseYouTubeCaptions.IsChecked = captionsItem.IsChecked;
        youtubeMenu.Items.Add(browseYouTubeItem);
        youtubeMenu.Items.Add(captionsItem);
        youtubeFields.ContextFlyout = youtubeMenu;
        youtubeFields.Children.Add(browseYouTube);
        youtubeFields.Children.Add(chooseYouTubeCaptions);
        SettingsDialog.CompactRow(youtubeFields,"YouTube video URL",youtubeRow,true);
        youtubeFields.Children.Add(youtubePreview);
        youtubeLoading.Children.Add(youtubeLoadingRing);
        youtubeLoading.Children.Add(new TextBlock { Text = "Fetching video details…", TextAlignment = TextAlignment.Center });
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(youtubeLoadingRing, "Fetching video details");
        youtubeFields.Children.Add(youtubeLoading);
        youtubeError = (Border)XamlReader.Load("""
            <Border xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
                    Background="{ThemeResource SystemFillColorCriticalBackgroundBrush}"
                    Visibility="Collapsed" Padding="10" CornerRadius="8">
                <Grid ColumnSpacing="10">
                    <Grid.ColumnDefinitions>
                        <ColumnDefinition Width="Auto"/>
                        <ColumnDefinition Width="*"/>
                    </Grid.ColumnDefinitions>
                    <FontIcon Glyph="&#xE711;" FontFamily="Segoe Fluent Icons" FontSize="17"
                              VerticalAlignment="Top" Margin="0,2,0,0" AutomationProperties.AccessibilityView="Raw"
                              Foreground="{ThemeResource SystemFillColorCriticalBrush}"/>
                    <TextBlock Grid.Column="1" TextWrapping="Wrap" FontSize="16" FontWeight="SemiBold"
                               Foreground="{ThemeResource SystemFillColorCriticalBrush}"/>
                </Grid>
            </Border>
            """);
        youtubeStatus = (TextBlock)((Grid)youtubeError.Child).Children[1];
        youtubeFields.Children.Add(youtubeError);
        fetchDetails.Click += async (_, _) => await FetchYouTubeDetailsAsync(YouTubeService.Shared.VideoForNewProjectAsync);
        sourceOptions.Children.Add(local); sourceOptions.Children.Add(youtube);
        sourceOptions.Children.Add(cloud);
        cloud.Checked += (_,_) => UpdateSource();
        cloudMode.SelectionChanged += async (_, _) =>
        {
            UpdateSource();
            if (IsLoaded && cloudMode.SelectedIndex == 1) await BrowseCloudVideosAsync();
        };
        var sourceChoices = new Grid { ColumnSpacing=16, Tag="dialog-form-row", MinHeight=36 };
        sourceChoices.ColumnDefinitions.Add(new ColumnDefinition { Width=GridLength.Auto });
        sourceChoices.ColumnDefinitions.Add(new ColumnDefinition());
        sourceChoices.RowDefinitions.Add(new RowDefinition { Height=GridLength.Auto });
        sourceChoices.RowDefinitions.Add(new RowDefinition { Height=GridLength.Auto });
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(sourceOptions,"Project type");
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetLabeledBy(sourceOptions,sourceLabel);
        sourceChoices.Children.Add(sourceLabel);Grid.SetColumn(sourceOptions,1);sourceChoices.Children.Add(sourceOptions);
        sourceChoices.SizeChanged += (_,args) => {
            var narrow=args.NewSize.Width<520;
            Grid.SetColumnSpan(sourceLabel,narrow?2:1);Grid.SetRow(sourceOptions,narrow?1:0);
            Grid.SetColumn(sourceOptions,narrow?0:1);Grid.SetColumnSpan(sourceOptions,narrow?2:1);
            sourceChoices.RowSpacing=narrow?8:0;
        };
        var layout = new Grid();
        layout.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        layout.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        layout.RowDefinitions.Add(new RowDefinition());
        layout.Children.Add(new ConnectivityNotice());
        var sourceCard = CompactDialogLayout.Card(sourceChoices, "Project source");
        sourceCard.Margin = new Thickness(0, 0, 0, 14);
        Grid.SetRow(sourceCard, 1); layout.Children.Add(sourceCard);

        var contents = new StackPanel { Spacing = 14 };
        contents.Children.Add(sourceHeading);
        localCard = CompactDialogLayout.Card(localFields,"Video file");
        youtubeCard = CompactDialogLayout.Card(youtubeFields,"YouTube video");
        contents.Children.Add(localCard);
        contents.Children.Add(youtubeCard);
        contents.Children.Add(new TextBlock { Text = "Audio Description", FontSize = 15, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold, Margin = new Thickness(2, 4, 0, 0) });
        var audioPanel = new StackPanel { Spacing = 6 };
        audioPanel.Children.Add(createDescription);
        audioPanel.Children.Add(importDescription);

        contents.Children.Add(CompactDialogLayout.Card(audioPanel,"Audio Description"));
        assets = new ImportAssetsPicker(owner, width: 630, showOriginal: false);
        assetsCard = new Border { Padding = new Thickness(14), CornerRadius = new CornerRadius(8), Child = assets };
        contents.Children.Add(assetsCard);
        originalAssets = new ImportAssetsPicker(owner, ImportScope.OriginalAudio, width: 630);
        originalAssets.SetContext(true, false);
        originalAssetsCard = new Border { Padding = new Thickness(14), CornerRadius = new CornerRadius(8), Child = originalAssets };
        contents.Children.Add(originalAssetsCard);
        originalAssets.SelectionChanged += (_, _) => UpdateCreationState();
        assets.SelectionChanged += (_, _) => UpdateCreationState();
        createDescription.Checked += (_, _) => UpdateCreationState();
        importDescription.Checked += (_, _) => UpdateCreationState();
        ToolTipService.SetToolTip(importDescription, "Choose existing transcript, summary, AD transcript, or AD audio files before creating the project");
        var location = new StackPanel { Spacing = 6 };
        var chooseLocation = new Button { Content = "Choose Project Location…" };
        ToolTipService.SetToolTip(chooseLocation, "Choose the parent folder; a new project folder is created inside it");
        chooseLocation.Click += async (_, _) =>
        {
            try
            {
                var picker = new FolderPicker(); picker.FileTypeFilter.Add("*");
                WinRT.Interop.InitializeWithWindow.Initialize(picker, WinRT.Interop.WindowNative.GetWindowHandle(owner));
                if (await picker.PickSingleFolderAsync() is { } folder) { LibraryPath = folder.Path; UpdateCreationState(); }
            }
            catch (Exception ex) { libraryLocation.Text = "The folder chooser could not open: " + ex.Message; }
        };
        var locationRow = new Grid { ColumnSpacing = 8 };
        locationRow.ColumnDefinitions.Add(new ColumnDefinition());
        locationRow.ColumnDefinitions.Add(new ColumnDefinition { Width=GridLength.Auto });
        libraryLocation.VerticalAlignment = VerticalAlignment.Center;
        locationRow.Children.Add(libraryLocation); Grid.SetColumn(chooseLocation,1); locationRow.Children.Add(chooseLocation);
        location.Children.Add(locationRow);
        contents.Children.Add(CompactDialogLayout.Card(location,"Project location"));
        form.Content = contents;
        Grid.SetRow(form, 2); layout.Children.Add(form);
        Content = layout;
        details.RegisterPropertyChangedCallback(TextBlock.TextProperty, (_, _) => UpdateLocalDetailsVisibility());
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetLiveSetting(youtubeStatus, Microsoft.UI.Xaml.Automation.Peers.AutomationLiveSetting.Polite);
        youtubeStatus.RegisterPropertyChangedCallback(TextBlock.TextProperty, (_, _) =>
        {
            youtubeError.Visibility = string.IsNullOrEmpty(youtubeStatus.Text) ? Visibility.Collapsed : Visibility.Visible;
            if (youtubeError.Visibility == Visibility.Visible)
                Microsoft.UI.Xaml.Automation.Peers.FrameworkElementAutomationPeer.FromElement(youtubeStatus)?
                    .RaiseAutomationEvent(Microsoft.UI.Xaml.Automation.Peers.AutomationEvents.LiveRegionChanged);
        });
        local.Checked += (_, _) => UpdateSource(); youtube.Checked += (_, _) => UpdateSource();
        youtubeUrl.TextChanged += (_, _) => UpdateYouTubeDetails();
        PrimaryButtonClick += (_, args) => { if (!IsPrimaryButtonEnabled) args.Cancel = true; };
        SecondaryButtonClick += (_, args) => { args.Cancel = !IsSecondaryButtonEnabled; CombineRequested = !args.Cancel; };
        Closing += (_, _) => { inspection?.Cancel(); youtubeInspection?.Cancel(); preview.Source = null; };
        Opened += (_, _) => UpdateSource();
        if (openCloud && cloud.IsEnabled) { cloudMode.SelectedIndex = 1; cloud.IsChecked = true; }
        else if (openYouTube) youtube.IsChecked = true; else local.IsChecked = true;
        UpdateSource();
    }

    private static StackPanel SourceLabel(string glyph, string label)
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, VerticalAlignment = VerticalAlignment.Center };
        row.Children.Add(new FontIcon { Glyph = glyph, FontSize = 16, VerticalAlignment = VerticalAlignment.Center });
        row.Children.Add(new TextBlock { Text = label, VerticalAlignment = VerticalAlignment.Center });
        return row;
    }
    private void UpdateSource()
    {
        var selectObject = IsCloud && cloudMode.SelectedIndex == 1;
        cloudModeFields.Visibility = IsCloud ? Visibility.Visible : Visibility.Collapsed;
        browseCloud.Visibility = selectObject ? Visibility.Visible : Visibility.Collapsed;
        browseCloud.IsEnabled = CanBrowseCloud;
        selectedFile.Visibility = selectObject ? Visibility.Collapsed : Visibility.Visible;
        preview.Visibility = !selectObject && validLocal ? Visibility.Visible : Visibility.Collapsed;
        videoFileRow.Visibility = selectObject ? Visibility.Collapsed : Visibility.Visible;
        UpdateLocalDetailsVisibility();
        localCard.Visibility = IsYouTube ? Visibility.Collapsed : Visibility.Visible;
        youtubeCard.Visibility = IsYouTube ? Visibility.Visible : Visibility.Collapsed;
        youtubeLoadingRing.IsActive = IsYouTube && youtubeInspection != null;
        youtubeLoading.Visibility = youtubeLoadingRing.IsActive ? Visibility.Visible : Visibility.Collapsed;
        sourceHeading.Text = IsCloud ? "Cloud Storage" : IsYouTube ? "YouTube Video" : "Video File";
        if (IsYouTube) preview.MediaPlayer?.Pause();
        fetchDetails.IsEnabled = ValidYouTubeId(YouTubeUrl) != null && youtubeInspection == null && ConnectivityService.Shared.Available(OnlineService.YouTube);
        ToolTipService.SetToolTip(fetchDetails, ConnectivityService.Shared.Reason(OnlineService.YouTube) ?? "Check the video's availability and visibility.");
        assets.SetContext(IsYouTube, false);
        UpdateCreationState();
    }

    private void UpdateLocalDetailsVisibility()
    {
        details.Visibility = videoFileRow.Visibility == Visibility.Visible && !string.IsNullOrEmpty(details.Text)
            ? Visibility.Visible : Visibility.Collapsed;
    }
    private bool CanBrowseCloud => !browsing && IsCloud && cloudMode.SelectedIndex == 1 &&
        settings.GetBool("vertexConnected") && BrowseCloudVideo != null && CompanionWindow != null;

    private async Task BrowseCloudVideosAsync()
    {
        if (!CanBrowseCloud || BrowseCloudVideo is not { } browse || CompanionWindow is not { } window) return;
        browsing = true;
        form.IsEnabled = local.IsEnabled = youtube.IsEnabled = cloud.IsEnabled = browseCloud.IsEnabled = false;
        try
        {
            ImportedCloudProject = await browse(window, LibraryPath);
            if (ImportedCloudProject != null) Hide();
        }
        finally
        {
            browsing = false;
            form.IsEnabled = local.IsEnabled = youtube.IsEnabled = true;
            cloud.IsEnabled = settings.GetBool("vertexConnected");
            UpdateSource();
        }
    }

    internal static string CloudFileSize(string? value) =>
        long.TryParse(value, out var bytes) ? $"{bytes:N0} bytes" : "—";

    internal static void AddCloudMetadata(Action<string, string> add, string? size, string? type,
        string? storageClass, string? created, string? updated)
    {
        if (size != null) add("File size", CloudFileSize(size));
        if (type != null) add("Content type", type);
        if (storageClass != null) add("Storage class", storageClass);
        if (created != null) add("Created", FormatDate(created));
        if (updated != null) add("Updated", FormatDate(updated));
        static string FormatDate(string value) => DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture,
            DateTimeStyles.None, out var date) ? date.LocalDateTime.ToString("g") : value;
    }

    private void UpdateCreationState()
    {
        if (assets == null) return;
        assetsCard.Visibility = ImportAssetsAfterCreation ? Visibility.Visible : Visibility.Collapsed;
        originalAssetsCard.Visibility = IsYouTube ? Visibility.Visible : Visibility.Collapsed;
        descriptionGuidance.Text = ImportAssetsAfterCreation ? "Choose existing files below. Text timing is reviewed before import. Combining audio is optional." : "Generate a transcript, summary, and descriptions after creating the project.";
        var sourceReady = IsYouTube ? fetchedYouTubeId != null && fetchedYouTubeId == ValidYouTubeId(YouTubeUrl) && ConnectivityService.Shared.Available(OnlineService.YouTube) : validLocal && !videoRequiresCloud;
        if (IsYouTube && SelectedYouTubeVideo is { Visibility: "private" } video)
            sourceReady &= YouTubeService.Shared.PrivateConnection(video.ChannelId, settings) != null;
        if (IsCloud) sourceReady = CanUploadCloudVideo(settings) && cloudMode.SelectedIndex == 0 && validLocal;
        var assetsReady = !ImportAssetsAfterCreation || !assets.Selection.HasAny || assets.CanImport;
        IsPrimaryButtonEnabled = sourceReady && assetsReady && !string.IsNullOrWhiteSpace(LibraryPath);
        PrimaryButtonText = ImportAssetsAfterCreation ? "Create Project & Open Editor" : "Create Project";
        SecondaryButtonText = ImportAssetsAfterCreation && assets.Selection.ADAudioPath != null ? "Create Project & Combine" : "";
        IsSecondaryButtonEnabled = IsPrimaryButtonEnabled;
        libraryLocation.Text = "A new portable project folder will be created in:\n" + LibraryPath;
    }
    private void ConnectivityChanged(object? sender, EventArgs e) => UpdateSource();
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
    private async Task BrowseYouTubeVideosAsync()
    {
        if (browsing || !IsYouTube || youtubeInspection != null ||
            BrowseYouTubeVideo is not { } browse || CompanionWindow is not { } window) return;
        browsing = true;
        try
        {
            if (await browse(window) is { } result && CompanionWindow == window)
            {
                youtubeUrl.Text = result.Video.Url;
                ApplyYouTubePreview(result.Video);
                UpdateCreationState();
                if (result.OpenInEditor && IsPrimaryButtonEnabled)
                {
                    OpenSelectedYouTubeInEditor = true;
                    Hide();
                }
            }
        }
        catch (Exception ex) { youtubeStatus.Text = ex.Message; }
        finally { browsing = false; UpdateSource(); }
    }

    private void ApplyYouTubePreview(YouTubeVideo video)
    {
        SelectedYouTubeVideo = video;
        fetchedYouTubeId = video.Id;
        ShowYouTubePreview(video.Id, video.Title, video.Channel, video.Thumbnail, video.Visibility);
        youtubeStatus.Text = "";
    }

    private void UpdateYouTubeDetails()
    {
        youtubeInspection?.Cancel(); youtubeInspection = null;
        SelectedYouTubeVideo = null;
        fetchedYouTubeId = null; youtubePreview.Visibility = Visibility.Collapsed;
        youtubeStatus.Text = "";
        UpdateSource();
    }
    private async Task FetchYouTubeDetailsAsync(Func<string, SettingsStore, CancellationToken, Task<YouTubeVideo>> fetchVideo)
    {
        if (!ConnectivityService.Shared.Available(OnlineService.YouTube)) { youtubeStatus.Text = ConnectivityService.Shared.Reason(OnlineService.YouTube); return; }
        var id = ValidYouTubeId(YouTubeUrl);
        if (id == null) return;
        youtubeInspection?.Cancel();
        var request = new CancellationTokenSource(); youtubeInspection = request;
        SelectedYouTubeVideo = null;
        fetchedYouTubeId = null; youtubePreview.Visibility = Visibility.Collapsed;
        youtubeStatus.Text = ""; UpdateSource();
        try
        {
            var video = await fetchVideo(id, settings, request.Token);
            if (youtubeInspection != request || request.IsCancellationRequested || id != ValidYouTubeId(YouTubeUrl)) return;
            ApplyYouTubePreview(video);
        }
        catch (OperationCanceledException) { }
        catch (YouTubeLinkIssueException ex)
        {
            if (youtubeInspection == request) youtubeStatus.Text = ex.Message;
        }
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
    private void ShowYouTubePreview(string id, string title, string author, string thumbnail, string visibility = "")
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
        if (visibility.Length > 0) information.Children.Add(new TextBlock { Text = "Visibility: " + CultureInfo.InvariantCulture.TextInfo.ToTitleCase(visibility), TextWrapping = TextWrapping.Wrap });
        information.Children.Add(new TextBlock { Text = "https://www.youtube.com/watch?v=" + id, Opacity = .7, FontSize = 12, TextWrapping = TextWrapping.Wrap });
        Grid.SetColumn(information, 1); card.Children.Add(information);
        youtubePreview.Child = card; youtubePreview.Visibility = Visibility.Visible;
    }
    private async Task ChooseVideoAsync()
    {
        var pickerOwner = CompanionWindow ?? owner;
        while (await PickVideoAsync(pickerOwner) is { } file)
        {
            var choice = await CheckVideoSizeAsync(pickerOwner, settings, file.Path, IsCloud);
            if (choice == VideoImportChoice.Smaller) continue;
            if (choice == VideoImportChoice.Cancel) return;
            if (choice == VideoImportChoice.Cloud)
            {
                cloudMode.SelectedIndex = 0;
                cloud.IsChecked = true;
            }
            await InspectVideoAsync(file);
            return;
        }
    }

    internal static async Task<StorageFile?> PickVideoAsync(Window owner)
    {
        var picker = new FileOpenPicker();
        foreach (var ext in new[] { ".mp4", ".mov", ".m4v", ".mkv", ".avi", ".webm" }) picker.FileTypeFilter.Add(ext);
        WinRT.Interop.InitializeWithWindow.Initialize(picker, WinRT.Interop.WindowNative.GetWindowHandle(owner));
        return await picker.PickSingleFileAsync();
    }

    private static bool CanUploadCloudVideo(SettingsStore settings) =>
        CredentialStore.HasGoogleSignIn &&
        !string.IsNullOrWhiteSpace(settings.Get("vertexProject")) &&
        !string.IsNullOrWhiteSpace(settings.Get("vertexBucket"));

    internal static string AnalysisSizeWarning(long size) =>
        $"This video is {size / 1_000_000_000d:0.00} GB and is too large for analysis. Choose a smaller video.";

    internal static async Task<VideoImportChoice> CheckVideoSizeAsync(Window owner, SettingsStore settings, string path, bool asCloud = false)
    {
        long size;
        try { size = new FileInfo(path).Length; }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            await WindowedDialog.ShowAsync(new ContentDialog
            {
                Title = "Video File Unavailable",
                Content = new TextBlock { Text = "VidiStudio couldn’t open this video. Choose an available video and try again.", TextWrapping = TextWrapping.Wrap },
                CloseButtonText = "OK"
            }, owner, 620, 300);
            return VideoImportChoice.Cancel;
        }

        if (asCloud && size <= MaximumCloudVideoBytes) return VideoImportChoice.Cloud;
        if (!asCloud && size <= MaximumLocalVideoBytes) return VideoImportChoice.Local;
        var fitsCloud = size <= MaximumCloudVideoBytes;
        var offerUpload = fitsCloud && CanUploadCloudVideo(settings);
        var sizeLabel = $"{size / 1_000_000_000d:0.00} GB";
        var message = !fitsCloud
            ? AnalysisSizeWarning(size)
                : offerUpload
                ? $"This video is {sizeLabel}. Video File projects accept videos up to 2 GB. Choose a smaller video or upload this one to Cloud Storage."
                : $"This video is {sizeLabel}. Video File projects accept videos up to 2 GB. Choose a smaller video and try again.";
        var alert = new ContentDialog
        {
            Title = "Video Is Too Large",
            Content = new TextBlock { Text = message, TextWrapping = TextWrapping.Wrap },
            PrimaryButtonText = "Choose a Smaller Video",
            SecondaryButtonText = offerUpload ? "Upload to Cloud Storage" : "",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Primary
        };
        var result = await WindowedDialog.ShowAsync(alert, owner, 720, 320);
        return result switch
        {
            ContentDialogResult.Primary => VideoImportChoice.Smaller,
            ContentDialogResult.Secondary when offerUpload => VideoImportChoice.Cloud,
            _ => VideoImportChoice.Cancel
        };
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
            videoRequiresCloud = new FileInfo(file.Path).Length > MaximumLocalVideoBytes;
            VideoPath = file.Path;
            validLocal = true;
            details.Text = metadata;
            UpdateSource();
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            if (inspection != request) return;
            details.Text = "Could not read this video's details. Check FFprobe in Settings → Storage. " + ex.Message;
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
