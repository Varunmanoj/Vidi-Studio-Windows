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
            presenter.IsMinimizable = true;
            presenter.IsMaximizable = true;
            presenter.IsResizable = true;
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
            if (fitToContent) FitToContent(dialog, window, root.XamlRoot, owner);
            return await dialog.ShowAsync();
        }
        finally { App.ThemeChanged -= ApplyTheme; window.Close(); }
    }

    /// <summary>
    /// Sizes the hosting window to the dialog's own content so the window does not show a large
    /// empty smoke area around a small card. The window grows when later content appears (for
    /// example a media preview) and is always clamped to the owner window.
    /// </summary>
    private static void FitToContent(ContentDialog dialog, Window window, XamlRoot xamlRoot, Window owner)
    {
        if (dialog.Content is not FrameworkElement content) return;
        var measured = (content as ScrollViewer)?.Content as FrameworkElement ?? content;
        void Apply()
        {
            var desired = measured.DesiredSize;
            if (desired.Height <= 0) return;
            // ContentDialog chrome around the content: title, button row, and card padding.
            const double chromeHeight = 200;
            const double chromeWidth = 64;
            var scale = xamlRoot.RasterizationScale;
            var area = owner.AppWindow;
            var dialogWidth = double.IsNaN(dialog.Width) || dialog.Width <= 0 ? 520 : dialog.Width;
            var logicalWidth = Math.Min(dialogWidth + chromeWidth, 560);
            var logicalHeight = Math.Min(desired.Height + chromeHeight, 780);
            var pixelWidth = (int)Math.Ceiling(Math.Min(logicalWidth * scale, area.Size.Width - 24));
            var pixelHeight = (int)Math.Ceiling(Math.Min(logicalHeight * scale, area.Size.Height - 24));
            window.AppWindow.Resize(new Windows.Graphics.SizeInt32(Math.Max(380, pixelWidth), Math.Max(320, pixelHeight)));
            window.AppWindow.Move(new Windows.Graphics.PointInt32(
                area.Position.X + Math.Max(0, (area.Size.Width - pixelWidth) / 2),
                area.Position.Y + Math.Max(0, (area.Size.Height - pixelHeight) / 2)));
        }
        measured.SizeChanged += (_, _) => Apply();
        dialog.Opened += (_, _) => Apply();
        Apply();
    }
}
