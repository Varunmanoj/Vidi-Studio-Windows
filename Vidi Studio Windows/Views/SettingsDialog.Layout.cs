using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using System;
using System.Linq;
using System.Runtime.InteropServices;
using Windows.Graphics;

namespace Vidi_Studio_Windows.Views;

public sealed partial class SettingsDialog
{
    [DllImport("user32.dll")]
    private static extern uint GetDpiForWindow(IntPtr hwnd);

    private void PositionOverOwner()
    {
        var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(owner);
        var area = DisplayArea.GetFromWindowId(owner.AppWindow.Id, DisplayAreaFallback.Nearest).WorkArea;
        var position = owner.AppWindow.Position;
        var size = owner.AppWindow.Size;
        var bounds = SettingsWindowPlacement.Calculate(
            new(position.X, position.Y, size.Width, size.Height),
            new(area.X, area.Y, area.Width, area.Height), GetDpiForWindow(hwnd) / 96d);
        AppWindow.MoveAndResize(new RectInt32(bounds.X, bounds.Y, bounds.Width, bounds.Height));
    }

    private static FrameworkElement? SettingsSwitchTrack(DependencyObject element)
    {
        if (element is FrameworkElement { Name: "OuterBorder" } track) return track;
        for (var index = 0; index < Microsoft.UI.Xaml.Media.VisualTreeHelper.GetChildrenCount(element); index++)
            if (SettingsSwitchTrack(Microsoft.UI.Xaml.Media.VisualTreeHelper.GetChild(element, index)) is { } found) return found;
        return null;
    }

    private static void ConfigureCompactToggle(ToggleSwitch toggle)
    {
        // The native template reserves a 12-DIP column for On/Off text even
        // when empty. Remove only that spacer, keeping the native track,
        // animations, focus visuals, hit target and automation peer.
        void RemoveEmptyContentGap()
        {
            if (SettingsSwitchTrack(toggle)?.Parent is Grid grid && grid.ColumnDefinitions.Count == 3)
            {
                grid.ColumnDefinitions[1].MinWidth = 0;
                grid.ColumnDefinitions[1].MaxWidth = 0;
                grid.ColumnDefinitions[1].Width = new GridLength(0);
            }
        }
        toggle.Loaded += (_, _) => RemoveEmptyContentGap();
        toggle.ActualThemeChanged += (_, _) => RemoveEmptyContentGap();
    }

    private static Border CompactSettingsControl(FrameworkElement control)
    {
        if (control is ToggleSwitch toggle) ConfigureCompactToggle(toggle);
        control.HorizontalAlignment = HorizontalAlignment.Center;
        control.VerticalAlignment = VerticalAlignment.Center;
        return new Border { Width = 54, Child = control };
    }

    private static Grid AddSettingsRow(StackPanel panel, string label, FrameworkElement control, bool stretchControl = false)
    {
        var row = new Grid { ColumnSpacing = 16, RowSpacing = 0, MinHeight = 36 };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        row.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        row.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        var title = new TextBlock { Text = label, TextWrapping = TextWrapping.Wrap, VerticalAlignment = VerticalAlignment.Center };
        row.Children.Add(title);
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetLabeledBy(control, title);
        if (control is not TextBlock) Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(control, label);
        control.VerticalAlignment = VerticalAlignment.Center;
        control.HorizontalAlignment = stretchControl ? HorizontalAlignment.Stretch : HorizontalAlignment.Right;
        Grid.SetColumn(control, 1); row.Children.Add(control);
        var preferredWidth = control.Width;
        row.SizeChanged += (_, args) =>
        {
            var stacked = args.NewSize.Width < 480;
            row.RowSpacing = stacked ? 8 : 0;
            control.Width = stacked && stretchControl ? double.NaN : preferredWidth;
            Grid.SetColumnSpan(title, stacked ? 2 : 1);
            Grid.SetRow(control, stacked ? 1 : 0);
            Grid.SetColumn(control, stacked ? 0 : 1);
            Grid.SetColumnSpan(control, stacked ? 2 : 1);
            control.MaxWidth = Math.Max(0, stacked ? args.NewSize.Width : (args.NewSize.Width - 16) / 2);
            control.HorizontalAlignment = stretchControl ? HorizontalAlignment.Stretch : HorizontalAlignment.Right;
        };
        panel.Children.Add(row);
        return row;
    }
#if DEBUG
    internal void VerifySidebarNavigationForSmokeTest()
    {
        var expected = new[] { "Appearance", "Projects", "Narration", "Gemini API", "YouTube Accounts" }
            .Concat(DeveloperModeEnabled ? new[] { "Developer prompts" } : Array.Empty<string>())
            .Concat(new[] { "Storage", "Help", "About" });
        var actual = sectionList!.Items.OfType<ListViewItem>().Where(item => item.Visibility == Visibility.Visible)
            .Select(Microsoft.UI.Xaml.Automation.AutomationProperties.GetName);
        if (!actual.SequenceEqual(expected)) throw new InvalidOperationException("Settings sidebar order differs from Mac.");
        var original = SelectedSection;
        var heading = layout.Children.OfType<TextBlock>().Single();
        foreach (var (id, title) in new[] { (8, "YouTube Accounts"), (1, "Projects"), (5, "Storage"), (6, "Help"), (7, "About") })
        {
            SelectSection(id);
            if (SelectedSection != id || heading.Text != title)
                throw new InvalidOperationException("Settings navigation lost its stable page ID or page heading.");
        }
        SelectSection(original);
    }
    internal async System.Threading.Tasks.Task VerifyLayoutForSmokeTestAsync()
    {
        var originalPosition = AppWindow.Position;
        var originalSize = AppWindow.Size;
        var originalSection = SelectedSection;
        var area = DisplayArea.GetFromWindowId(owner.AppWindow.Id, DisplayAreaFallback.Nearest).WorkArea;
        var current = AppWindow.Position;
        if (current.X < area.X || current.Y < area.Y || current.X + originalSize.Width > area.X + area.Width || current.Y + originalSize.Height > area.Y + area.Height)
            throw new InvalidOperationException("Settings must open entirely inside its owner's display work area.");
        try
        {
            var scale = GetDpiForWindow(WinRT.Interop.WindowNative.GetWindowHandle(owner)) / 96d;
            AppWindow.Resize(new SizeInt32(Math.Min(originalSize.Width, (int)(720 * scale)), Math.Min(originalSize.Height, (int)(540 * scale))));
            foreach (var section in new[] { 0, 1, 2, 3, 5, 6, 7, 8 })
            {
                NavigateForSmokeTest(section);
                await System.Threading.Tasks.Task.Delay(100);
                layout.UpdateLayout();
                if (sectionHost.ViewportHeight <= 0 || sectionHost.ViewportWidth <= 0)
                    throw new InvalidOperationException("Settings content must keep a bounded, usable scroll viewport when restored.");
                // Voice/model results and wrapping rows may change the extent after
                // navigation. Follow the current bottom until layout has settled.
                for (var attempt = 0; attempt < 20; attempt++)
                {
                    layout.UpdateLayout();
                    var bottom = sectionHost.ScrollableHeight;
                    sectionHost.ChangeView(null, bottom, null, true);
                    await System.Threading.Tasks.Task.Delay(50);
                    if (Math.Abs(sectionHost.ScrollableHeight - bottom) <= 2 &&
                        Math.Abs(sectionHost.VerticalOffset - sectionHost.ScrollableHeight) <= 2) break;
                }
                if (Math.Abs(sectionHost.VerticalOffset - sectionHost.ScrollableHeight) > 2)
                    throw new InvalidOperationException($"Settings section {section} cannot reach its last option: offset={sectionHost.VerticalOffset}, bottom={sectionHost.ScrollableHeight}, viewport={sectionHost.ViewportHeight}.");
                if (layout.Children.OfType<StackPanel>().Where(child => Grid.GetRow(child) == 3)
                    .SelectMany(footer => footer.Children).OfType<Button>().Any())
                    throw new InvalidOperationException("Automatically applied Settings must not show Save or Cancel buttons.");
                sectionHost.ChangeView(null, 0, null, true);
            }
        }
        finally
        {
            AppWindow.MoveAndResize(new RectInt32(originalPosition.X, originalPosition.Y, originalSize.Width, originalSize.Height));
            NavigateForSmokeTest(originalSection);
        }
    }
#endif
}
