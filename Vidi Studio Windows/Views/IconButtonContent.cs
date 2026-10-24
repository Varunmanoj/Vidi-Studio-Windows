using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace Vidi_Studio_Windows.Views;

/// <summary>Fluent icon and label content shared by the Piper voice action buttons.</summary>
internal static class IconButtonContent
{
    /// <summary>High-contrast red used for destructive voice actions.</summary>
    internal static readonly Windows.UI.Color Destructive = Windows.UI.Color.FromArgb(255, 229, 43, 50);

    internal static void Set(Button button, string text, Symbol symbol)
    {
        var content = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
        content.Children.Add(new SymbolIcon(symbol) { VerticalAlignment = VerticalAlignment.Center });
        content.Children.Add(new TextBlock { Text = text, VerticalAlignment = VerticalAlignment.Center });
        button.Content = content;
    }

    /// <summary>Colors a destructive action red and restores the normal label color otherwise.</summary>
    internal static void SetDestructive(Button button, bool destructive)
    {
        if (destructive) button.Foreground = new SolidColorBrush(Destructive);
        else button.ClearValue(Control.ForegroundProperty);
    }
}
