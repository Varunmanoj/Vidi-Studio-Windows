using Microsoft.UI.Composition;
using Microsoft.UI.Composition.SystemBackdrops;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using System;
using System.IO;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using Windows.UI.ViewManagement;

namespace Vidi_Studio_Windows.Views;

/// <summary>The system material used behind a window's content and caption.</summary>
internal enum WindowMaterial
{
    None,
    Mica,
    Acrylic
}

/// <summary>
/// Applies the Windows material system consistently to every Vidi Studio window.
/// Windows 11 uses Mica; Windows 10 uses Desktop Acrylic; when neither material is
/// available (or high contrast is on) the window falls back to an opaque Fluent surface.
/// </summary>
internal static class WindowAppearance
{
    // Values mirror WinUI's LayerFillColorDefault so code-created windows match XAML surfaces.
    private static readonly Windows.UI.Color LightSurface = Windows.UI.Color.FromArgb(0x80, 255, 255, 255);
    private static readonly Windows.UI.Color DarkSurface = Windows.UI.Color.FromArgb(0x4C, 0x3A, 0x3A, 0x3A);
    private static readonly ConditionalWeakTable<Window, MaterialState> Materials = new();

    private sealed class MaterialState
    {
        internal WindowMaterial Kind { get; set; }
        internal MaterialBackdrop? Backdrop { get; set; }
    }

    internal static WindowMaterial ApplyMaterial(Window window)
    {
        var state = Materials.GetOrCreateValue(window);
        if (state.Kind != WindowMaterial.None && window.SystemBackdrop is not null) return state.Kind;

        state.Kind = WindowMaterial.None;
        state.Backdrop = null;
        // System backdrops are disabled in high contrast; keep the system colors instead.
        if (!HighContrast)
        {
            try
            {
                if (MicaController.IsSupported())
                {
                    var backdrop = new MaterialBackdrop(WindowMaterial.Mica);
                    window.SystemBackdrop = backdrop;
                    state.Backdrop = backdrop;
                    state.Kind = WindowMaterial.Mica;
                }
                else if (DesktopAcrylicController.IsSupported())
                {
                    var backdrop = new MaterialBackdrop(WindowMaterial.Acrylic);
                    window.SystemBackdrop = backdrop;
                    state.Backdrop = backdrop;
                    state.Kind = WindowMaterial.Acrylic;
                }
                if (state.Backdrop is { } attached)
                    window.Activated += (_, args) => attached.SetInputActive(args.WindowActivationState != WindowActivationState.Deactivated);
            }
            catch
            {
                // Missing composition support or a disabled DWM falls back to the solid surface.
            }
        }

        if (state.Kind == WindowMaterial.None) window.SystemBackdrop = null;
        return state.Kind;
    }

    internal static WindowMaterial MaterialFor(Window window) =>
        Materials.TryGetValue(window, out var state) ? state.Kind : WindowMaterial.None;

    internal static void ApplyIcon(Window window)
    {
        var icon = Path.Combine(AppContext.BaseDirectory, "Assets", "AppIcon.ico");
        if (!File.Exists(icon)) throw new FileNotFoundException("The Vidi Studio window icon is missing.", icon);
        window.AppWindow.SetIcon(icon);
    }

    internal static void ApplyTheme(Window window, FrameworkElement content, ElementTheme theme)
    {
        content.RequestedTheme = theme;
        var actual = theme == ElementTheme.Default ? content.ActualTheme : theme;
        ApplySurface(window, content, actual);
        UpdateTitleBar(window, actual);
    }

    /// <summary>
    /// Paints the app surface with a translucent Fluent layer so the system material is
    /// visible, or an opaque base color when the backdrop is unavailable.
    /// </summary>
    internal static void ApplySurface(Window window, FrameworkElement content, ElementTheme theme)
    {
        var brush = new SolidColorBrush(SurfaceColor(window, theme));
        switch (content)
        {
            case Panel panel: panel.Background = brush; break;
            case Border border: border.Background = brush; break;
            case Control control: control.Background = brush; break;
        }
    }

    /// <summary>
    /// Keeps the caption and menu strip transparent over the material while the content
    /// below uses the readable Fluent layer. Without a backdrop the whole window receives
    /// the opaque base color so the extended title bar is never left unpainted.
    /// </summary>
    internal static void ApplyWindowSurface(Window window, Panel root, FrameworkElement surface, ElementTheme theme)
    {
        root.Background = new SolidColorBrush(MaterialFor(window) == WindowMaterial.None
            ? BaseColor(theme)
            : Microsoft.UI.Colors.Transparent);
        ApplySurface(window, surface, theme);
    }

    internal static void Refresh(Window window, FrameworkElement content)
    {
        var actual = content.ActualTheme == ElementTheme.Default ? ElementTheme.Light : content.ActualTheme;
        ApplySurface(window, content, actual);
        UpdateTitleBar(window, actual);
    }

    /// <summary>
    /// Keeps the standard caption buttons over the material. On Mica the title bar and
    /// button backgrounds are transparent; on Windows 10 Acrylic the non-client frame
    /// cannot show the backdrop, so it gets the matching Fluent base color.
    /// </summary>
    internal static void UpdateTitleBar(Window window, ElementTheme theme)
    {
        if (Materials.TryGetValue(window, out var state)) state.Backdrop?.UpdateTheme(theme);
        if (window.AppWindow?.TitleBar is not { } titleBar || !AppWindowTitleBar.IsCustomizationSupported()) return;

        if (HighContrast)
        {
            var system = new UISettings();
            var systemBackground = system.GetColorValue(UIColorType.Background);
            var systemForeground = system.GetColorValue(UIColorType.Foreground);
            var subtle = Windows.UI.Color.FromArgb(40, systemForeground.R, systemForeground.G, systemForeground.B);
            titleBar.BackgroundColor = systemBackground;
            titleBar.InactiveBackgroundColor = systemBackground;
            titleBar.ForegroundColor = systemForeground;
            titleBar.InactiveForegroundColor = systemForeground;
            titleBar.ButtonBackgroundColor = systemBackground;
            titleBar.ButtonInactiveBackgroundColor = systemBackground;
            titleBar.ButtonForegroundColor = systemForeground;
            titleBar.ButtonInactiveForegroundColor = systemForeground;
            titleBar.ButtonHoverBackgroundColor = subtle;
            titleBar.ButtonHoverForegroundColor = systemForeground;
            titleBar.ButtonPressedBackgroundColor = subtle;
            titleBar.ButtonPressedForegroundColor = systemForeground;
            return;
        }

        var dark = theme == ElementTheme.Dark;
        var materialChrome = window.ExtendsContentIntoTitleBar && MaterialFor(window) != WindowMaterial.None;
        ApplyCaptionTheme(window, dark);
        var background = materialChrome ? Microsoft.UI.Colors.Transparent : BaseColor(theme);
        // A fully transparent button background makes DWM skip the caption glyphs on some
        // Windows 11 builds. A one-step alpha keeps the material visible and the buttons drawn.
        var captionBackground = materialChrome ? Windows.UI.Color.FromArgb(1, 255, 255, 255) : background;
        var foreground = dark ? Microsoft.UI.Colors.White : Microsoft.UI.Colors.Black;
        var inactive = dark ? Windows.UI.Color.FromArgb(255, 160, 160, 160) : Windows.UI.Color.FromArgb(255, 96, 96, 96);
        var hover = dark ? Windows.UI.Color.FromArgb(26, 255, 255, 255) : Windows.UI.Color.FromArgb(20, 0, 0, 0);
        var pressed = dark ? Windows.UI.Color.FromArgb(40, 255, 255, 255) : Windows.UI.Color.FromArgb(32, 0, 0, 0);

        titleBar.BackgroundColor = background;
        titleBar.InactiveBackgroundColor = background;
        titleBar.ForegroundColor = foreground;
        titleBar.InactiveForegroundColor = inactive;
        titleBar.ButtonBackgroundColor = captionBackground;
        titleBar.ButtonInactiveBackgroundColor = captionBackground;
        titleBar.ButtonForegroundColor = foreground;
        titleBar.ButtonInactiveForegroundColor = inactive;
        titleBar.ButtonHoverBackgroundColor = hover;
        titleBar.ButtonHoverForegroundColor = foreground;
        titleBar.ButtonPressedBackgroundColor = pressed;
        titleBar.ButtonPressedForegroundColor = foreground;
    }

    internal static Windows.UI.Color SurfaceColor(Window window, ElementTheme theme)
    {
        if (HighContrast || MaterialFor(window) == WindowMaterial.None) return BaseColor(theme);
        return theme == ElementTheme.Dark ? DarkSurface : LightSurface;
    }

    internal static Windows.UI.Color BaseColor(ElementTheme theme)
    {
        if (HighContrast)
        {
            try { return new UISettings().GetColorValue(UIColorType.Background); } catch { }
        }
        return theme == ElementTheme.Dark
            ? Windows.UI.Color.FromArgb(255, 32, 32, 32)
            : Windows.UI.Color.FromArgb(255, 243, 243, 243);
    }

    private static bool HighContrast
    {
        get
        {
            try { return new AccessibilitySettings().HighContrast; }
            catch { return false; }
        }
    }

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr window, int attribute, ref int value, int size);

    private static void ApplyCaptionTheme(Window window, bool dark)
    {
        try
        {
            var handle = WinRT.Interop.WindowNative.GetWindowHandle(window);
            if (handle == IntPtr.Zero) return;
            var enabled = dark ? 1 : 0;
            // DWMWA_USE_IMMERSIVE_DARK_MODE. Windows 10 20H1+ uses 20; earlier builds use 19.
            if (DwmSetWindowAttribute(handle, 20, ref enabled, sizeof(int)) != 0)
                DwmSetWindowAttribute(handle, 19, ref enabled, sizeof(int));
        }
        catch
        {
            // The caption keeps the system theme when DWM customization is unavailable.
        }
    }
}

/// <summary>
/// A theme-aware Mica or Desktop Acrylic backdrop. The built-in XAML backdrops take
/// their theme from the application, which cannot change after startup; this controller
/// follows the per-window light/dark preview instead.
/// </summary>
internal sealed class MaterialBackdrop : SystemBackdrop
{
    private readonly WindowMaterial kind;
    private MicaController? mica;
    private DesktopAcrylicController? acrylic;
    private SystemBackdropConfiguration? configuration;
    private ElementTheme theme = ElementTheme.Default;
    private bool inputActive = true;

    internal MaterialBackdrop(WindowMaterial kind) => this.kind = kind;

    internal void UpdateTheme(ElementTheme value)
    {
        theme = value;
        ApplyConfigurationTheme();
    }

    internal void SetInputActive(bool active)
    {
        inputActive = active;
        if (configuration is not null) configuration.IsInputActive = active;
    }

    private void ApplyConfigurationTheme()
    {
        if (configuration is null) return;
        configuration.Theme = theme == ElementTheme.Dark ? SystemBackdropTheme.Dark : SystemBackdropTheme.Light;
    }

    protected override void OnTargetConnected(ICompositionSupportsSystemBackdrop connectedTarget, XamlRoot xamlRoot)
    {
        base.OnTargetConnected(connectedTarget, xamlRoot);
        configuration = GetDefaultSystemBackdropConfiguration(connectedTarget, xamlRoot);
        configuration.IsInputActive = inputActive;
        ApplyConfigurationTheme();
        if (kind == WindowMaterial.Mica)
        {
            mica = new MicaController { Kind = MicaKind.Base };
            mica.AddSystemBackdropTarget(connectedTarget);
            mica.SetSystemBackdropConfiguration(configuration);
        }
        else if (kind == WindowMaterial.Acrylic)
        {
            acrylic = new DesktopAcrylicController();
            acrylic.AddSystemBackdropTarget(connectedTarget);
            acrylic.SetSystemBackdropConfiguration(configuration);
        }
    }

    protected override void OnTargetDisconnected(ICompositionSupportsSystemBackdrop disconnectedTarget)
    {
        if (mica is not null)
        {
            mica.RemoveSystemBackdropTarget(disconnectedTarget);
            mica.Dispose();
            mica = null;
        }
        if (acrylic is not null)
        {
            acrylic.RemoveSystemBackdropTarget(disconnectedTarget);
            acrylic.Dispose();
            acrylic = null;
        }
        configuration = null;
        base.OnTargetDisconnected(disconnectedTarget);
    }

    protected override void OnDefaultSystemBackdropConfigurationChanged(ICompositionSupportsSystemBackdrop target, XamlRoot xamlRoot)
    {
        // The base implementation asks for a fresh default configuration, which can be
        // invalid while a window is activating or closing. Our configuration is stable;
        // only its theme needs to follow the preview.
        ApplyConfigurationTheme();
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
        WindowAppearance.ApplyMaterial(window);
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
            backdrop.Color = WindowAppearance.BaseColor(theme);
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
        root.ActualThemeChanged += (_, _) => { WindowAppearance.Refresh(window, root); ApplySeamless(root.ActualTheme); };
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
        // Escape cancels the dialog, matching the Mac sheet's Cancel action. The accelerator is
        // scoped to both the window and the dialog so it works wherever focus sits.
        var dialogVisible = false;
        dialog.Opened += (_, _) => dialogVisible = true;
        dialog.Closed += (_, _) => dialogVisible = false;
        void AddEscapeAccelerator(UIElement scope)
        {
            var accelerator = new Microsoft.UI.Xaml.Input.KeyboardAccelerator { Key = Windows.System.VirtualKey.Escape };
            accelerator.Invoked += (_, args) => { args.Handled = true; if (dialogVisible) dialog.Hide(); };
            scope.KeyboardAccelerators.Add(accelerator);
        }
        AddEscapeAccelerator(root);
        AddEscapeAccelerator(dialog);
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
