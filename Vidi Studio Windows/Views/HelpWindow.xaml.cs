using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.Web.WebView2.Core;
using System;
using System.Threading.Tasks;
using Windows.Graphics;

namespace Vidi_Studio_Windows.Views;

public sealed partial class HelpWindow : Window
{
    internal const string GuideUrl = "https://vidistudio.varunmanojkumar.in/docs";
    private ulong navigationId;
    private bool webReady;
    private bool closed;

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern uint GetDpiForWindow(IntPtr window);

#if DEBUG
    internal string? LoadedPageForSmokeTest { get; private set; }
    internal async Task<string> ReaderStateForSmokeTest() => await ContentView.ExecuteScriptAsync("matchMedia('(prefers-color-scheme: dark)').matches");

    internal async Task VerifyGuideForSmokeTestAsync()
    {
        for (var i = 0; i < 200; i++)
        {
            if (LoadedPageForSmokeTest == GuideUrl && !LoadingRing.IsActive &&
                await ContentView.ExecuteScriptAsync("Boolean(document.querySelector('#docs-1-welcome') && document.querySelector('#docs-8-exports'))") == "true")
            {
                var image = await ContentView.ExecuteScriptAsync("""
                    (() => {
                        const img = document.querySelector('img[src*="/screenshots/mac-help/"]');
                        if (!img) return false;
                        img.loading = 'eager';
                        return img.complete && img.naturalWidth > 0;
                    })()
                    """);
                if (image == "true") return;
            }
            await Task.Delay(100);
        }
        throw new InvalidOperationException("Published Help or its screenshot did not render. " + StatusMessage.Text);
    }

    internal async Task VerifyOfflineRecoveryForSmokeTestAsync()
    {
        var core = ContentView.CoreWebView2;
        const string method = "Network.emulateNetworkConditions";
        await core.CallDevToolsProtocolMethodAsync("Network.enable", "{}");
        try
        {
            await core.CallDevToolsProtocolMethodAsync(method, """{"offline":true,"latency":0,"downloadThroughput":0,"uploadThroughput":0}""");
            OpenGuide();
            if (!LoadingRing.IsActive) throw new InvalidOperationException("Help progress missing.");
            for (var i = 0; i < 150 && StatusMessage.Text == ""; i++) await Task.Delay(100);
            if (StatusMessage.Text != "Documentation requires internet." || ContentView.Visibility != Visibility.Collapsed ||
                LoadingRing.IsActive || !BrowserButton.IsEnabled)
                throw new InvalidOperationException($"Offline status: {StatusMessage.Text}, loaded {LoadedPageForSmokeTest}, progress {LoadingRing.IsActive}, reader {ContentView.Visibility}.");
        }
        finally
        {
            await core.CallDevToolsProtocolMethodAsync(method, """{"offline":false,"latency":0,"downloadThroughput":-1,"uploadThroughput":-1}""");
        }
        OpenGuide();
        await VerifyGuideForSmokeTestAsync();
    }
#endif
    public HelpWindow(ElementTheme theme = ElementTheme.Default)
    {
        InitializeComponent();
        WindowAppearance.ApplyIcon(this);
        WindowAppearance.ApplyMaterial(this);
        Root.RequestedTheme = theme;
        App.ThemeChanged += ApplyAppTheme;
        Root.ActualThemeChanged += (_, _) => { WindowAppearance.Refresh(this, Root); ApplyWebTheme(); };
        WindowAppearance.ApplyTheme(this, Root, theme);
        Closed += (_, _) =>
        {
            closed = true;
            App.ThemeChanged -= ApplyAppTheme;
            try { if (webReady) ContentView.Close(); } catch { }
        };
        try
        {
            var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(this);
            var windowId = Microsoft.UI.Win32Interop.GetWindowIdFromWindow(hwnd);
            var appWindow = Microsoft.UI.Windowing.AppWindow.GetFromWindowId(windowId);
            var scale = Math.Max(1, GetDpiForWindow(hwnd) / 96d);
            var area = DisplayArea.GetFromWindowId(windowId, DisplayAreaFallback.Nearest).WorkArea;
            var width = Math.Min((int)Math.Round(1180 * scale), Math.Max(600, area.Width - 48));
            var height = Math.Min((int)Math.Round(820 * scale), Math.Max(400, area.Height - 48));
            appWindow.Resize(new SizeInt32(width, height));
            appWindow.Move(new PointInt32(area.X + (area.Width - width) / 2, area.Y + (area.Height - height) / 2));
        }
        catch { }
    }

    public void SetTheme(ElementTheme theme)
    {
        WindowAppearance.ApplyTheme(this, Root, theme);
        ApplyWebTheme();
    }

    private void ApplyAppTheme(string theme) => SetTheme(theme switch
    {
        "light" => ElementTheme.Light, "dark" => ElementTheme.Dark, _ => ElementTheme.Default
    });

    private void ApplyWebTheme()
    {
        if (!webReady) return;
        var scheme = Root.ActualTheme == ElementTheme.Dark ? "dark" : "light";
        ContentView.CoreWebView2.Profile.PreferredColorScheme = scheme == "dark" ? CoreWebView2PreferredColorScheme.Dark : CoreWebView2PreferredColorScheme.Light;
    }

    private async void Root_Loaded(object sender, RoutedEventArgs e)
    {
        Root.Loaded -= Root_Loaded;
        try
        {
            await ContentView.EnsureCoreWebView2Async();
            if (closed) return;
            var core = ContentView.CoreWebView2;
            // The public site's service worker can return a cached app shell while offline.
            // A Help visit must reach the website or show the native connection message.
            await core.CallDevToolsProtocolMethodAsync("Network.setBypassServiceWorker", """{"bypass":true}""");
            if (closed) return;
            core.Settings.IsStatusBarEnabled = false;
            core.NavigationStarting += Content_NavigationStarting;
            core.NavigationCompleted += Content_NavigationCompleted;
            core.NewWindowRequested += (_, args) =>
            {
                args.Handled = true;
                if (args.IsUserInitiated) OpenInBrowser(args.Uri);
            };
            webReady = true;
            ApplyWebTheme();
            OpenGuide();
        }
        catch { if (!closed) ShowMessage("The Help reader could not start. Check the Microsoft Edge WebView2 Runtime."); }
    }

    public void OpenGuide()
    {
        if (!webReady || closed) return;
#if DEBUG
        LoadedPageForSmokeTest = null;
#endif
        StatusMessage.Text = "";
        ContentView.Visibility = Visibility.Visible;
        SetLoading(true);
        var core = ContentView.CoreWebView2;
        var request = core.Environment.CreateWebResourceRequest(GuideUrl, "GET", null, "Cache-Control: no-cache");
        core.NavigateWithWebResourceRequest(request);
    }

    private static bool IsGuide(string? address) =>
        Uri.TryCreate(address, UriKind.Absolute, out var uri) && uri.GetLeftPart(UriPartial.Path).TrimEnd('/') == GuideUrl;

    private void Content_NavigationStarting(CoreWebView2 sender, CoreWebView2NavigationStartingEventArgs args)
    {
        if (IsGuide(args.Uri)) { navigationId = args.NavigationId; SetLoading(true); return; }
        args.Cancel = true;
        if (args.IsUserInitiated) OpenInBrowser(args.Uri);
    }

    private void Content_NavigationCompleted(CoreWebView2 sender, CoreWebView2NavigationCompletedEventArgs args)
    {
        if (closed || args.NavigationId != navigationId) return;
        SetLoading(false);
        if (args.WebErrorStatus == CoreWebView2WebErrorStatus.OperationCanceled) return;
        if (!args.IsSuccess || args.HttpStatusCode >= 400)
        {
            ShowMessage(args.HttpStatusCode >= 400 ? "Documentation is temporarily unavailable." : "Documentation requires internet.");
            return;
        }
#if DEBUG
        LoadedPageForSmokeTest = sender.Source;
#endif
    }

    private void SetLoading(bool loading)
    {
        LoadingRing.IsActive = loading;
        LoadingRing.Visibility = loading ? Visibility.Visible : Visibility.Collapsed;
    }

    private void ShowMessage(string message)
    {
        SetLoading(false);
        ContentView.Visibility = Visibility.Collapsed;
        StatusMessage.Text = message;
    }

    private void OpenGuideInBrowser(object sender, RoutedEventArgs e)
    {
        var address = webReady ? ContentView.CoreWebView2.Source : null;
        OpenInBrowser(IsGuide(address) ? address! : GuideUrl);
    }

    private static void OpenInBrowser(string address)
    {
        if (Uri.TryCreate(address, UriKind.Absolute, out var uri) && uri.Scheme is "https" or "http")
            _ = Windows.System.Launcher.LaunchUriAsync(uri);
    }
}
