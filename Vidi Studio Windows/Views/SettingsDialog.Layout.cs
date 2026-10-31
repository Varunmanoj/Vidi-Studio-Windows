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
            control.Width = stacked ? double.NaN : preferredWidth;
            Grid.SetColumnSpan(title, stacked ? 2 : 1);
            Grid.SetRow(control, stacked ? 1 : 0);
            Grid.SetColumn(control, stacked ? 0 : 1);
            Grid.SetColumnSpan(control, stacked ? 2 : 1);
            control.MaxWidth = Math.Max(0, stacked ? args.NewSize.Width : (args.NewSize.Width - 16) / 2);
            control.HorizontalAlignment = stacked || stretchControl ? HorizontalAlignment.Stretch : HorizontalAlignment.Right;
        };
        panel.Children.Add(row);
        return row;
    }
#if DEBUG
    internal async System.Threading.Tasks.Task VerifyLayoutForSmokeTestAsync()
    {
        var originalPosition = AppWindow.Position;
        var originalSize = AppWindow.Size;
        var originalSection = sectionList!.SelectedIndex;
        var area = DisplayArea.GetFromWindowId(owner.AppWindow.Id, DisplayAreaFallback.Nearest).WorkArea;
        var current = AppWindow.Position;
        if (current.X < area.X || current.Y < area.Y || current.X + originalSize.Width > area.X + area.Width || current.Y + originalSize.Height > area.Y + area.Height)
            throw new InvalidOperationException("Settings must open entirely inside its owner's display work area.");
        try
        {
            var scale = GetDpiForWindow(WinRT.Interop.WindowNative.GetWindowHandle(owner)) / 96d;
            AppWindow.Resize(new SizeInt32(Math.Min(originalSize.Width, (int)(720 * scale)), Math.Min(originalSize.Height, (int)(540 * scale))));
            foreach (var section in new[] { 0, 3, 5, 6, 7 })
            {
                NavigateForSmokeTest(section);
                await System.Threading.Tasks.Task.Delay(100);
                layout.UpdateLayout();
                if (sectionHost.ViewportHeight <= 0 || sectionHost.ViewportWidth <= 0)
                    throw new InvalidOperationException("Settings content must keep a bounded, usable scroll viewport when restored.");
                sectionHost.ChangeView(null, sectionHost.ScrollableHeight, null, true);
                await System.Threading.Tasks.Task.Delay(60);
                if (Math.Abs(sectionHost.VerticalOffset - sectionHost.ScrollableHeight) > 2)
                    throw new InvalidOperationException("The last settings option cannot be reached without maximizing.");
                foreach (var footer in layout.Children.OfType<Grid>().Where(child => Grid.GetRow(child) == 3))
                    foreach (var button in footer.Children.OfType<Button>())
                    {
                        var point = button.TransformToVisual(layout).TransformPoint(new Windows.Foundation.Point());
                        if (point.X < 0 || point.Y < 0 || point.X + button.ActualWidth > layout.ActualWidth + 1 || point.Y + button.ActualHeight > layout.ActualHeight + 1)
                            throw new InvalidOperationException("Settings Save/Cancel must remain visible in a restored window.");
                    }
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
