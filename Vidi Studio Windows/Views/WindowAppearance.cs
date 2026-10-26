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

    /// <summary>
    /// Flat, opaque surface for companion dialog windows. Dialogs do not use Mica/Acrylic or a
    /// translucent Fluent layer: light mode is plain white and dark mode is plain black, so the
    /// card never shows a gray wash over the desktop.
    /// </summary>
    internal static Windows.UI.Color DialogSurfaceColor(ElementTheme theme) =>
        theme == ElementTheme.Dark ? Microsoft.UI.Colors.Black : Microsoft.UI.Colors.White;

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

/// <summary>
/// Shows a WinUI content dialog as a native Vidi Studio window. The dialog content fills the
/// window on the same Mica-backed Fluent surface as the rest of the app instead of floating as
/// a bordered card over a dimmed smoke layer.
/// </summary>
internal static class WindowedDialog
{
    // Height of the extended title bar; the dialog content starts below its caption buttons.
    private const double TitleBarHeight = 32;
    private const double ContentTopInset = 36;

    internal static async Task<ContentDialogResult> ShowAsync(ContentDialog dialog, Window owner, int width = 680, int height = 460)
    {
        var window = new Window { Title = dialog.Title?.ToString() ?? "Vidi Studio" };
        var root = new Grid();
        window.Content = root;
        WindowAppearance.ApplyIcon(window);
        // Dialogs are flat companion windows: no Mica/Acrylic backdrop, so light mode is white and
        // dark mode is black instead of a translucent Fluent wash.
        window.SystemBackdrop = null;
        if (window.AppWindow.Presenter is Microsoft.UI.Windowing.OverlappedPresenter presenter)
        {
            // A dialog is a fixed-size companion; only the system Close button belongs in its caption.
            presenter.IsMinimizable = false;
            presenter.IsMaximizable = false;
            presenter.IsResizable = false;
        }
        // Flatten the ContentDialog chrome. The window surface is the dialog surface, so there is
        // no smoke layer, card fill, border, divider, or floating card inside the window.
        var transparent = new SolidColorBrush(Microsoft.UI.Colors.Transparent);
        dialog.Resources["ContentDialogSmokeFill"] = transparent;
        dialog.Resources["ContentDialogTopOverlay"] = transparent;
        dialog.Resources["ContentDialogBackground"] = transparent;
        dialog.Resources["ContentDialogBorderBrush"] = transparent;
        dialog.Resources["ContentDialogBorderWidth"] = new Thickness(0);
        dialog.Resources["ContentDialogSeparatorThickness"] = new Thickness(0);
        if (!dialog.Resources.ContainsKey("ContentDialogPadding"))
            dialog.Resources["ContentDialogPadding"] = new Thickness(24, ContentTopInset, 24, 24);
        // Allow wide editors to use the requested width instead of the 548 px ContentDialog default,
        // leaving room for the card's 24 px side padding and an optional scrollbar. Dialogs that
        // deliberately cap themselves with their own resource keep that cap.
        var availableCardWidth = Math.Max(480.0, width - 24.0);
        if (!dialog.Resources.ContainsKey("ContentDialogMinWidth"))
            dialog.Resources["ContentDialogMinWidth"] = Math.Min(520.0, availableCardWidth);
        if (dialog.Resources.ContainsKey("ContentDialogMaxWidth") && dialog.Resources["ContentDialogMaxWidth"] is double existingCap)
            dialog.Resources["ContentDialogMaxWidth"] = Math.Max(existingCap, availableCardWidth);
        else
            dialog.Resources["ContentDialogMaxWidth"] = availableCardWidth;
        // Extend the app surface into the caption so the window has the same material as the app
        // and the dialog title is shown once, inside the content, rather than twice.
        window.ExtendsContentIntoTitleBar = true;
        var titleBarDrag = new Border
        {
            Height = TitleBarHeight,
            VerticalAlignment = VerticalAlignment.Top,
            Background = transparent
        };
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(titleBarDrag, "Dialog title bar");
        root.Children.Add(titleBarDrag);
        window.SetTitleBar(titleBarDrag);
        void ApplyTheme(string theme)
        {
            var requested = theme switch { "light" => ElementTheme.Light, "dark" => ElementTheme.Dark, _ => ElementTheme.Default };
            WindowAppearance.ApplyTheme(window, root, requested);
            var actual = requested == ElementTheme.Default
                ? (root.ActualTheme == ElementTheme.Dark ? ElementTheme.Dark : ElementTheme.Light)
                : requested;
            ApplyFlatSurface(actual);
            dialog.RequestedTheme = requested;
            FlattenDialogSurface(dialog);
        }
        root.ActualThemeChanged += (_, _) =>
        {
            WindowAppearance.Refresh(window, root);
            ApplyFlatSurface(root.ActualTheme == ElementTheme.Dark ? ElementTheme.Dark : ElementTheme.Light);
            FlattenDialogSurface(dialog);
        };
        App.ThemeChanged += ApplyTheme;
        ApplyTheme(App.ActiveTheme ?? ((owner.Content as FrameworkElement)?.ActualTheme == ElementTheme.Dark ? "dark" : "light"));

        void ApplyFlatSurface(ElementTheme theme)
        {
            var surface = WindowAppearance.DialogSurfaceColor(theme);
            root.Background = new SolidColorBrush(surface);
            if (window.AppWindow?.TitleBar is { } titleBar && Microsoft.UI.Windowing.AppWindowTitleBar.IsCustomizationSupported())
            {
                titleBar.BackgroundColor = surface;
                titleBar.InactiveBackgroundColor = surface;
                titleBar.ButtonBackgroundColor = surface;
                titleBar.ButtonInactiveBackgroundColor = surface;
            }
        }
        // The template's smoke rectangle is what dims the whole dialog window in light mode. Clear
        // it in the visual tree as well as through the resource, because the template can resolve
        // its ThemeResource before the dialog-level override is observed.
        dialog.Loaded += (_, _) => FlattenDialogSurface(dialog);
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
            // Escape still cancels, but the accelerator must not draw a key-tip badge in the
            // dialog; native Windows dialogs do not advertise their Escape shortcut.
            scope.KeyboardAcceleratorPlacementMode = Microsoft.UI.Xaml.Input.KeyboardAcceleratorPlacementMode.Hidden;
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
            FitWindowToCard(dialog, window, root, owner);
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
            // The content is measured with an unbounded height, so its desired height is the full
            // dialog height even when a short initial window clipped it. An inner scroll viewer that
            // sets MaxHeight deliberately stays scrollable instead of growing the window past it.
            var contentHeight = Math.Max(content.DesiredSize.Height, content.ActualHeight);
            if (content is ScrollViewer scroller && scroller.ExtentHeight > 0)
            {
                contentHeight = scroller.ExtentHeight;
                if (!double.IsInfinity(scroller.MaxHeight) && scroller.MaxHeight > 0)
                    contentHeight = Math.Min(contentHeight, scroller.MaxHeight);
            }
            // Add the card chrome (title, padding, and buttons) that surrounds the content.
            var chromeHeight = Math.Max(0, card.ActualHeight - content.ActualHeight);
            var cardHeight = contentHeight + chromeHeight;
            if (!double.IsInfinity(card.MaxHeight) && card.MaxHeight > 0)
                cardHeight = Math.Min(cardHeight, card.MaxHeight);
            if (cardHeight <= 0 || card.ActualWidth <= 0) return;
            var scale = root.XamlRoot?.RasterizationScale ?? 1;
            var area = owner.AppWindow;
            var logicalWidth = card.ActualWidth + card.Margin.Left + card.Margin.Right + 2;
            var logicalHeight = cardHeight + card.Margin.Top + card.Margin.Bottom + 2;
            var pixelWidth = (int)Math.Ceiling(Math.Min(logicalWidth * scale, area.Size.Width - 24));
            var pixelHeight = (int)Math.Ceiling(Math.Min(logicalHeight * scale, area.Size.Height - 24));
            // AppWindow sizes are physical pixels. Scale the logical minimums too; using an
            // unscaled 480/240 px minimum shrank high-DPI dialogs below their XAML content
            // width/height, which clipped wrapped messages and lower list rows.
            var minPixelWidth = (int)Math.Ceiling(480 * scale);
            var minPixelHeight = (int)Math.Ceiling(240 * scale);
            var targetWidth = Math.Min(Math.Max(minPixelWidth, pixelWidth), Math.Max(400, area.Size.Width - 24));
            var targetHeight = Math.Min(Math.Max(minPixelHeight, pixelHeight), Math.Max(220, area.Size.Height - 24));
            if (Math.Abs(targetWidth - window.AppWindow.Size.Width) < 4 && Math.Abs(targetHeight - window.AppWindow.Size.Height) < 4) return;
            window.AppWindow.Resize(new Windows.Graphics.SizeInt32(targetWidth, targetHeight));
            window.AppWindow.Move(new Windows.Graphics.PointInt32(
                area.Position.X + Math.Max(0, (area.Size.Width - targetWidth) / 2),
                area.Position.Y + Math.Max(0, (area.Size.Height - targetHeight) / 2)));
        }
        dialog.Opened += (_, _) =>
        {
            FlattenDialogSurface(dialog);
            root.DispatcherQueue.TryEnqueue(Resize);
            if (dialog.Content is FrameworkElement content) content.SizeChanged += (_, _) => Resize();
            if ((dialog.Content as ScrollViewer)?.Content is FrameworkElement inner) inner.SizeChanged += (_, _) => Resize();
        };
    }

    /// <summary>
    /// Removes the instantiated ContentDialog's dimming layer and card chrome. The dialog window
    /// already paints the app's Fluent surface, so the template only needs to supply the title,
    /// content, and buttons.
    /// </summary>
    private static void FlattenDialogSurface(ContentDialog dialog)
    {
        var transparent = new SolidColorBrush(Microsoft.UI.Colors.Transparent);
        if (FindTemplateElement<Microsoft.UI.Xaml.Shapes.Rectangle>(dialog, "SmokeLayerBackground") is { } smoke)
        {
            // Removing the rectangle is stronger than clearing its fill: a visual state or a
            // template refresh can re-apply the theme brush, which is what previously left the
            // light-mode dialog covered by a 30% black smoke layer.
            if (VisualTreeHelper.GetParent(smoke) is Panel smokeHost) smokeHost.Children.Remove(smoke);
            else smoke.Fill = transparent;
        }
        ClearSmokeLayers(dialog, dialog, transparent);
        if (FindTemplateElement<Border>(dialog, "BackgroundElement") is { } card)
        {
            // The smoke layer is the rectangle that shares the template layout with the card. Clearing
            // it by position as well as by name keeps the white surface even if a template revision
            // renames the element.
            if (VisualTreeHelper.GetParent(card) is Panel layout)
            {
                foreach (var child in layout.Children)
                    if (child is Microsoft.UI.Xaml.Shapes.Rectangle smokeLayer)
                        smokeLayer.Fill = transparent;
            }
            card.Background = transparent;
            card.BorderThickness = new Thickness(0);
        }
        if (FindTemplateElement<Grid>(dialog, "CommandSpace") is { } commands)
            commands.Background = transparent;
        if (FindTemplateElement<ScrollViewer>(dialog, "ContentScrollViewer") is { } contentScroller)
        {
            if (contentScroller.Content is Grid contentGrid)
            {
                contentGrid.Background = transparent;
                // Remove the title/content separator that the template would otherwise draw.
                contentGrid.BorderThickness = new Thickness(0);
            }
            else if (contentScroller.Content is Panel contentPanel)
            {
                contentPanel.Background = transparent;
            }
        }
    }

    /// <summary>
    /// Clears every full-surface smoke rectangle in the dialog template, including rectangles
    /// that a template refresh recreated after the named lookup. Only the host-wide dimming layer
    /// is affected; dialog content is never a full-window rectangle.
    /// </summary>
    private static void ClearSmokeLayers(DependencyObject node, ContentDialog dialog, Brush transparent)
    {
        var hasDialogSize = dialog.ActualWidth > 0 && dialog.ActualHeight > 0;
        var count = VisualTreeHelper.GetChildrenCount(node);
        for (var i = 0; i < count; i++)
        {
            var child = VisualTreeHelper.GetChild(node, i);
            if (child is Microsoft.UI.Xaml.Shapes.Rectangle rectangle &&
                (rectangle.Name == "SmokeLayerBackground" ||
                 (hasDialogSize && rectangle.Fill is not null && rectangle.ActualWidth >= dialog.ActualWidth - 1 && rectangle.ActualHeight >= dialog.ActualHeight - 1)))
                rectangle.Fill = transparent;
            ClearSmokeLayers(child, dialog, transparent);
        }
    }

    /// <summary>Finds a named element in an instantiated control template.</summary>
    private static T? FindTemplateElement<T>(DependencyObject root, string name) where T : FrameworkElement
    {
        var count = VisualTreeHelper.GetChildrenCount(root);
        for (var i = 0; i < count; i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            if (child is T element && element.Name == name) return element;
            if (FindTemplateElement<T>(child, name) is { } found) return found;
        }
        return null;
    }

    /// <summary>Finds the ContentDialog card border that wraps the dialog content.</summary>
    private static Border? FindCard(FrameworkElement? content, FrameworkElement root)
    {
        // The template names the card BackgroundElement. Prefer it over the outer Container border,
        // which fills the ContentDialog control and can be larger than the card when a dialog sets
        // its own Width. Fall back to the largest border that does not fill the whole host.
        var rootArea = root.ActualWidth * root.ActualHeight;
        Border? fallback = null;
        var fallbackArea = 0.0;
        DependencyObject? node = content;
        while (node is not null)
        {
            if (node is Border border)
            {
                if (border.Name == "BackgroundElement") return border;
                var area = border.ActualWidth * border.ActualHeight;
                if (area > 0 && area < rootArea - 1 && area > fallbackArea) { fallbackArea = area; fallback = border; }
            }
            node = VisualTreeHelper.GetParent(node);
        }
        return fallback;
    }
}
