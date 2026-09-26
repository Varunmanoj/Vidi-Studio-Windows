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
    internal static async Task<ContentDialogResult> ShowAsync(ContentDialog dialog, Window owner, int width = 680, int height = 460)
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
        void ApplyTheme(string theme)
        {
            var requested = theme switch { "light" => ElementTheme.Light, "dark" => ElementTheme.Dark, _ => ElementTheme.Default };
            WindowAppearance.ApplyTheme(window, root, requested);
            dialog.RequestedTheme = requested;
        }
        root.ActualThemeChanged += (_, _) => WindowAppearance.UpdateColors(window, root, root.ActualTheme);
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
            return await dialog.ShowAsync();
        }
        finally { App.ThemeChanged -= ApplyTheme; window.Close(); }
    }
}
