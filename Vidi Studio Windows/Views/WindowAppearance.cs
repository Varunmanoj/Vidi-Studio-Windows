using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using System;
using System.IO;
using System.Threading.Tasks;

namespace Vidi_Studio_Windows.Views;

internal static class WindowAppearance
{
    internal static void ApplyIcon(Window window)
    {
        var icon = Path.Combine(AppContext.BaseDirectory, "Assets", "AppIcon.ico");
        if (!File.Exists(icon)) throw new FileNotFoundException("The Vidi Studio window icon is missing.", icon);
        window.AppWindow.SetIcon(icon);
    }

    internal static void ApplyTheme(Window window, FrameworkElement content, ElementTheme theme)
    {
        content.RequestedTheme = theme;
        UpdateColors(window, content, theme == ElementTheme.Default ? content.ActualTheme : theme);
    }

    internal static void UpdateColors(Window window, FrameworkElement content, ElementTheme theme)
    {
        if (window.AppWindow?.TitleBar is not { } titleBar) return;
        var dark = theme == ElementTheme.Dark;
        var background = dark ? Windows.UI.Color.FromArgb(255, 24, 24, 24) : Microsoft.UI.Colors.White;
        var foreground = dark ? Microsoft.UI.Colors.White : Microsoft.UI.Colors.Black;
        if (content is Panel panel) panel.Background = new SolidColorBrush(background);
        titleBar.BackgroundColor = background;
        titleBar.ForegroundColor = foreground;
        titleBar.ButtonBackgroundColor = background;
        titleBar.ButtonForegroundColor = foreground;
        titleBar.InactiveBackgroundColor = background;
        titleBar.ButtonInactiveBackgroundColor = background;
    }
}

/// <summary>Shows a WinUI content dialog in its own titled window with standard caption buttons.</summary>
internal static class WindowedDialog
{
    internal static async Task<ContentDialogResult> ShowAsync(ContentDialog dialog, Window owner, int width = 680, int height = 460, bool fitToContent = false)
    {
        var window = new Window { Title = dialog.Title?.ToString() ?? "Vidi Studio" };
        var root = new Grid();
        window.Content = root;
        WindowAppearance.ApplyIcon(window);
        if (window.AppWindow.Presenter is Microsoft.UI.Windowing.OverlappedPresenter presenter)
        {
            // A dialog window only shows Close; only regular tool windows can be minimized/maximized.
            presenter.IsMinimizable = !fitToContent;
            presenter.IsMaximizable = !fitToContent;
            presenter.IsResizable = !fitToContent;
        }
        // When the dialog should fill its own window, blend the ContentDialog smoke layer, card
        // background, and border into the window background so no gray frame shows around the card.
        var backdrop = new SolidColorBrush();
        void ApplySeamless(ElementTheme theme)
        {
            if (!fitToContent) return;
            backdrop.Color = theme == ElementTheme.Dark
                ? Windows.UI.Color.FromArgb(255, 24, 24, 24)
                : Microsoft.UI.Colors.White;
            root.Background = backdrop;
            dialog.Resources["ContentDialogSmokeFill"] = backdrop;
            dialog.Resources["ContentDialogBackground"] = backdrop;
            dialog.Resources["ContentDialogBorderBrush"] = backdrop;
            dialog.Resources["ContentDialogBorderWidth"] = new Thickness(0);
        }
        void ApplyTheme(string theme)
        {
            var requested = theme switch { "light" => ElementTheme.Light, "dark" => ElementTheme.Dark, _ => ElementTheme.Default };
            WindowAppearance.ApplyTheme(window, root, requested);
            dialog.RequestedTheme = requested;
            ApplySeamless(requested == ElementTheme.Default ? root.ActualTheme : requested);
        }
        root.ActualThemeChanged += (_, _) => { WindowAppearance.UpdateColors(window, root, root.ActualTheme); ApplySeamless(root.ActualTheme); };
        App.ThemeChanged += ApplyTheme;
        ApplyTheme(App.ActiveTheme ?? ((owner.Content as FrameworkElement)?.ActualTheme == ElementTheme.Dark ? "dark" : "light"));
        var area = owner.AppWindow;
        // AppWindow uses physical pixels; dialog measurements and requested sizes use XAML pixels.
        var scale = (owner.Content as FrameworkElement)?.XamlRoot?.RasterizationScale ?? 1;
        width = Math.Min((int)Math.Ceiling(width * scale), Math.Max(400, area.Size.Width - 24));
        height = Math.Min((int)Math.Ceiling(height * scale), Math.Max(320, area.Size.Height - 24));
        window.AppWindow.Resize(new Windows.Graphics.SizeInt32(width, height));
        window.AppWindow.Move(new Windows.Graphics.PointInt32(
            area.Position.X + Math.Max(0, (area.Size.Width - width) / 2),
            area.Position.Y + Math.Max(0, (area.Size.Height - height) / 2)));
        var loaded = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        root.Loaded += (_, _) => loaded.TrySetResult(true);
        window.Closed += (_, _) => { loaded.TrySetResult(false); dialog.Hide(); };
        window.Activate();
        try
        {
            if (!await loaded.Task) return ContentDialogResult.None;
            dialog.XamlRoot = root.XamlRoot;
            if (fitToContent) FitWindowToCard(dialog, window, root, owner);
            return await dialog.ShowAsync();
        }
        finally { App.ThemeChanged -= ApplyTheme; window.Close(); }
    }

    /// <summary>
    /// Sizes the hosting window to the rendered dialog card so the window itself is the dialog and
    /// no dimmed smoke area frames it. Runs once the card is on screen and follows later content
    /// changes (for example a media preview), always clamped to the owner window.
    /// </summary>
    private static void FitWindowToCard(ContentDialog dialog, Window window, Grid root, Window owner)
    {
        Border? card = null;
        void Resize()
        {
            if (dialog.Content is not FrameworkElement content) return;
            card ??= FindCard(content, root);
            if (card is null) return;
            var scroller = content as ScrollViewer;
            // Prefer the full content extent so the window grows when a preview or extra fields appear.
            var contentHeight = scroller is not null && scroller.ExtentHeight > 0
                ? scroller.ExtentHeight + Math.Max(0, card.ActualHeight - scroller.ActualHeight)
                : card.ActualHeight;
            if (contentHeight <= 0 || card.ActualWidth <= 0) return;
            var scale = root.XamlRoot?.RasterizationScale ?? 1;
            var area = owner.AppWindow;
            var logicalWidth = card.ActualWidth + card.Margin.Left + card.Margin.Right + 2;
            var logicalHeight = contentHeight + card.Margin.Top + card.Margin.Bottom + 2;
            var pixelWidth = (int)Math.Ceiling(Math.Min(logicalWidth * scale, area.Size.Width - 24));
            var pixelHeight = (int)Math.Ceiling(Math.Min(logicalHeight * scale, area.Size.Height - 24));
            if (Math.Abs(pixelWidth - window.AppWindow.Size.Width) < 4 && Math.Abs(pixelHeight - window.AppWindow.Size.Height) < 4) return;
            window.AppWindow.Resize(new Windows.Graphics.SizeInt32(Math.Max(360, pixelWidth), Math.Max(300, pixelHeight)));
            window.AppWindow.Move(new Windows.Graphics.PointInt32(
                area.Position.X + Math.Max(0, (area.Size.Width - pixelWidth) / 2),
                area.Position.Y + Math.Max(0, (area.Size.Height - pixelHeight) / 2)));
        }
        dialog.Opened += (_, _) =>
        {
            root.DispatcherQueue.TryEnqueue(Resize);
            if (dialog.Content is FrameworkElement content) content.SizeChanged += (_, _) => Resize();
            if ((dialog.Content as ScrollViewer)?.Content is FrameworkElement inner) inner.SizeChanged += (_, _) => Resize();
        };
    }

    /// <summary>Finds the ContentDialog card border that wraps the dialog content.</summary>
    private static Border? FindCard(FrameworkElement? content, FrameworkElement root)
    {
        // Walk the ancestors and keep the largest border that does not fill the whole host: that is
        // the dialog card, not the full-size smoke layer above it or an inner frame below it.
        var rootArea = root.ActualWidth * root.ActualHeight;
        Border? best = null;
        var bestArea = 0.0;
        DependencyObject? node = content;
        while (node is not null)
        {
            if (node is Border border)
            {
                var area = border.ActualWidth * border.ActualHeight;
                if (area > 0 && area < rootArea - 1 && area > bestArea) { bestArea = area; best = border; }
            }
            node = VisualTreeHelper.GetParent(node);
        }
        return best;
    }
}
