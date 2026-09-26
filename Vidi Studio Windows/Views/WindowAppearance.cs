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
        void Update()
        {
            var dark = content.ActualTheme == ElementTheme.Dark;
            var background = dark ? Windows.UI.Color.FromArgb(255, 24, 24, 24) : Microsoft.UI.Colors.White;
            var foreground = dark ? Microsoft.UI.Colors.White : Microsoft.UI.Colors.Black;
            if (content is Panel panel) panel.Background = new SolidColorBrush(background);
            window.AppWindow.TitleBar.BackgroundColor = background;
            window.AppWindow.TitleBar.ForegroundColor = foreground;
            window.AppWindow.TitleBar.ButtonBackgroundColor = background;
            window.AppWindow.TitleBar.ButtonForegroundColor = foreground;
            window.AppWindow.TitleBar.InactiveBackgroundColor = background;
            window.AppWindow.TitleBar.ButtonInactiveBackgroundColor = background;
        }
        content.ActualThemeChanged += (_, _) => Update();
        Update();
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
        var theme = (owner.Content as FrameworkElement)?.ActualTheme ?? ElementTheme.Default;
        WindowAppearance.ApplyTheme(window, root, theme);
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
            dialog.RequestedTheme = root.ActualTheme;
            return await dialog.ShowAsync();
        }
        finally { window.Close(); }
    }
}
