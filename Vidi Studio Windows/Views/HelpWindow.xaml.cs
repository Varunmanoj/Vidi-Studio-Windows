using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.Web.WebView2.Core;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Windows.Graphics;

namespace Vidi_Studio_Windows.Views;

/// <summary>A native, searchable Help book window with a table of contents and HTML reader.</summary>
public sealed partial class HelpWindow : Window
{
    private sealed class Manifest
    {
        public List<Page> Pages { get; set; } = new();
    }

    private sealed class Page
    {
        public string Slug { get; set; } = "";
        public string Title { get; set; } = "";
        public List<Section> Sections { get; set; } = new();
    }

    private sealed class Section
    {
        public string Id { get; set; } = "";
        public string Title { get; set; } = "";
    }

    public sealed class HelpNode
    {
        public string Title { get; init; } = "";
        public string Slug { get; init; } = "";
        public string? Anchor { get; init; }
        public bool IsPage => Anchor == null;
        public string Icon => IsPage ? "\uE8A5" : "\uE8FD";
    }

    private static readonly string HelpFolder = Path.Combine(AppContext.BaseDirectory, "Help");
    private readonly List<Page> pages = new();
    private readonly Dictionary<string, TreeViewNode> pageNodes = new();
    private readonly Dictionary<string, TreeViewNode> sectionNodes = new();
    private string currentSlug = "welcome";
    private string? pendingAnchor;
    private bool treeSelectionSuppressed;
    private bool webReady;

    public HelpWindow(ElementTheme theme = ElementTheme.Default)
    {
        InitializeComponent();
        Root.RequestedTheme = theme;
        Root.ActualThemeChanged += (_, _) => ApplyWebTheme();
        Closed += (_, _) =>
        {
            try { if (webReady) ContentView.Close(); } catch { }
        };
        try
        {
            var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(this);
            var windowId = Microsoft.UI.Win32Interop.GetWindowIdFromWindow(hwnd);
            var appWindow = Microsoft.UI.Windowing.AppWindow.GetFromWindowId(windowId);
            appWindow.Resize(new SizeInt32(1180, 820));
        }
        catch { }
        LoadManifest();
        RebuildToc();
        UpdateNavigation();
    }

    public void SetTheme(ElementTheme theme)
    {
        Root.RequestedTheme = theme;
        ApplyWebTheme();
    }

    private void ApplyWebTheme()
    {
        if (!webReady) return;
        var scheme = Root.ActualTheme == ElementTheme.Dark ? "dark" : "light";
        _ = ContentView.ExecuteScriptAsync($"document.documentElement.style.colorScheme='{scheme}'; document.body.style.colorScheme='{scheme}';");
    }

    private async void Root_Loaded(object sender, RoutedEventArgs e)
    {
        if (webReady) return;
        try
        {
            await ContentView.EnsureCoreWebView2Async();
            var core = ContentView.CoreWebView2;
            core.Settings.AreDevToolsEnabled = false;
            core.Settings.AreDefaultContextMenusEnabled = true;
            core.Settings.IsStatusBarEnabled = false;
            core.SetVirtualHostNameToFolderMapping("vidistudio.help", HelpFolder, CoreWebView2HostResourceAccessKind.Allow);
            core.NavigationStarting += Content_NavigationStarting;
            core.NavigationCompleted += Content_NavigationCompleted;
            core.NewWindowRequested += Content_NewWindowRequested;
            webReady = true;
            ApplyWebTheme();
            NavigateTo(currentSlug, pendingAnchor);
        }
        catch (Exception ex)
        {
            await ShowContentError("The Help reader could not start.", ex.Message);
        }
    }

    private void LoadManifest()
    {
        try
        {
            var path = Path.Combine(HelpFolder, "manifest.json");
            if (!File.Exists(path)) return;
            var manifest = JsonSerializer.Deserialize<Manifest>(File.ReadAllText(path),
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            if (manifest?.Pages is { Count: > 0 }) pages.AddRange(manifest.Pages);
        }
        catch { }
        if (pages.Count == 0)
            pages.Add(new Page { Slug = "welcome", Title = "Welcome to Vidi Studio" });
        currentSlug = pages[0].Slug;
    }

    private void RebuildToc()
    {
        var query = SearchBox.Text.Trim();
        TocTree.RootNodes.Clear();
        pageNodes.Clear();
        sectionNodes.Clear();
        var shown = 0;
        foreach (var page in pages)
        {
            var matchingSections = string.IsNullOrEmpty(query)
                ? page.Sections
                : page.Sections.Where(section => section.Title.Contains(query, StringComparison.OrdinalIgnoreCase)).ToList();
            if (!string.IsNullOrEmpty(query)
                && !page.Title.Contains(query, StringComparison.OrdinalIgnoreCase)
                && matchingSections.Count == 0) continue;
            var pageNode = new TreeViewNode { Content = NodeView(new HelpNode { Title = page.Title, Slug = page.Slug }), IsExpanded = true };
            foreach (var section in matchingSections)
            {
                var sectionNode = new TreeViewNode { Content = NodeView(new HelpNode { Title = section.Title, Slug = page.Slug, Anchor = section.Id }) };
                pageNode.Children.Add(sectionNode);
                sectionNodes[page.Slug + "#" + section.Id] = sectionNode;
            }
            TocTree.RootNodes.Add(pageNode);
            pageNodes[page.Slug] = pageNode;
            shown++;
        }
        SearchStatus.Text = string.IsNullOrEmpty(query)
            ? $"{pages.Count} chapters · {pages.Sum(page => page.Sections.Count)} topics"
            : $"{shown} matching chapter{(shown == 1 ? "" : "s")}";
        SelectToc(currentSlug);
    }

    private void Search_Changed(object sender, TextChangedEventArgs e) => RebuildToc();

    private static FrameworkElement NodeView(HelpNode node)
    {
        var panel = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Padding = new Thickness(2, 3, 2, 3) };
        panel.Children.Add(new FontIcon { Glyph = node.Icon, FontSize = 13, Opacity = .8 });
        panel.Children.Add(new TextBlock { Text = node.Title, TextWrapping = TextWrapping.Wrap, MaxWidth = 240 });
        panel.Tag = node;
        return panel;
    }

    private void Toc_SelectionChanged(TreeView sender, TreeViewSelectionChangedEventArgs args)
    {
        if (treeSelectionSuppressed || sender.SelectedNode?.Content is not FrameworkElement element ||
            element.Tag is not HelpNode node) return;
        NavigateTo(node.Slug, node.Anchor);
    }

    private void SelectToc(string slug, string? anchor = null)
    {
        if (!pageNodes.TryGetValue(slug, out var pageNode)) return;
        treeSelectionSuppressed = true;
        try
        {
            pageNode.IsExpanded = true;
            TocTree.SelectedNode = anchor != null && sectionNodes.TryGetValue(slug + "#" + anchor, out var sectionNode)
                ? sectionNode : pageNode;
        }
        finally { treeSelectionSuppressed = false; }
    }

    private void NavigateTo(string slug, string? anchor)
    {
        if (string.IsNullOrWhiteSpace(slug)) slug = currentSlug;
        var pageExists = pages.Any(page => page.Slug == slug);
        if (!pageExists) slug = currentSlug;
        currentSlug = slug;
        pendingAnchor = anchor;
        SelectToc(slug, anchor);
        UpdateNavigation();
        if (!webReady)
        {
            pendingAnchor = anchor;
            return;
        }
        var url = $"https://vidistudio.help/{slug}.html" + (string.IsNullOrEmpty(anchor) ? "" : "#" + Uri.EscapeDataString(anchor));
        if (ContentView.Source?.ToString() == url)
        {
            ScrollTo(anchor);
            return;
        }
        ContentView.Source = new Uri(url);
    }

    private void ScrollTo(string? anchor)
    {
        if (!webReady || string.IsNullOrEmpty(anchor)) return;
        _ = ContentView.ExecuteScriptAsync($"document.getElementById({JsonSerializer.Serialize(anchor)})?.scrollIntoView({{block:'start'}});");
    }

    private void Content_NavigationStarting(CoreWebView2 sender, CoreWebView2NavigationStartingEventArgs args)
    {
        if (!Uri.TryCreate(args.Uri, UriKind.Absolute, out var uri)) return;
        if (uri.Host != "vidistudio.help")
        {
            if (uri.Scheme is "http" or "https")
            {
                args.Cancel = true;
                OpenInBrowser(args.Uri);
            }
            return;
        }
        var slug = Path.GetFileNameWithoutExtension(uri.AbsolutePath);
        if (!pages.Any(page => page.Slug == slug)) return;
        currentSlug = slug;
        pendingAnchor = string.IsNullOrEmpty(uri.Fragment) ? null : uri.Fragment.TrimStart('#');
        SelectToc(slug, pendingAnchor);
    }

    private async void Content_NavigationCompleted(CoreWebView2 sender, CoreWebView2NavigationCompletedEventArgs args)
    {
        if (!args.IsSuccess)
        {
            await ShowContentError("This Help page could not be displayed.", "The local Help page is missing or unreadable.");
            return;
        }
        ApplyWebTheme();
        ScrollTo(pendingAnchor);
        pendingAnchor = null;
        UpdateNavigation();
    }

    private void Content_NewWindowRequested(CoreWebView2 sender, CoreWebView2NewWindowRequestedEventArgs args)
    {
        args.Handled = true;
        if (Uri.TryCreate(args.Uri, UriKind.Absolute, out var uri) && uri.Host == "vidistudio.help")
        {
            OpenInBrowser(new Uri(Path.Combine(HelpFolder, Path.GetFileName(uri.AbsolutePath))).AbsoluteUri);
            return;
        }
        OpenInBrowser(args.Uri);
    }

    private async Task ShowContentError(string title, string detail)
    {
        if (!webReady) return;
        var html = $"<html><body style='font-family:Segoe UI;padding:2rem'><h1>{System.Net.WebUtility.HtmlEncode(title)}</h1><p>{System.Net.WebUtility.HtmlEncode(detail)}</p><p>Help files are installed with Vidi Studio. Reinstall the app if the Help folder is missing.</p></body></html>";
        ContentView.NavigateToString(html);
        await Task.CompletedTask;
    }

    private int CurrentPageIndex() => pages.FindIndex(page => page.Slug == currentSlug);

    private void UpdateNavigation()
    {
        var index = CurrentPageIndex();
        PreviousButton.IsEnabled = index > 0;
        NextButton.IsEnabled = index >= 0 && index < pages.Count - 1;
        PageIndicator.Text = index >= 0
            ? $"{index + 1} of {pages.Count} · {pages[index].Title}"
            : "";
    }

    private void Previous_Click(object sender, RoutedEventArgs e)
    {
        var index = CurrentPageIndex();
        if (index > 0) NavigateTo(pages[index - 1].Slug, null);
    }

    private void Next_Click(object sender, RoutedEventArgs e)
    {
        var index = CurrentPageIndex();
        if (index >= 0 && index < pages.Count - 1) NavigateTo(pages[index + 1].Slug, null);
    }

    private static void OpenInBrowser(string uri)
    {
        try { Process.Start(new ProcessStartInfo(uri) { UseShellExecute = true }); } catch { }
    }
}
