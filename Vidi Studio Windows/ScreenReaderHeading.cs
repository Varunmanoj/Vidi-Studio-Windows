using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Automation.Peers;

namespace Vidi_Studio_Windows;

internal static class ScreenReaderHeading
{
    internal static void MarkDialogTitle(Microsoft.UI.Xaml.Controls.ContentDialog dialog)
    {
        if (dialog.Title is FrameworkElement title) { Mark(title, AutomationHeadingLevel.Level1); return; }
        var expected = dialog.Title?.ToString();
        void Visit(DependencyObject node)
        {
            if (node is Microsoft.UI.Xaml.Controls.TextBlock text && text.Text == expected && !string.IsNullOrEmpty(expected))
                Mark(text, AutomationHeadingLevel.Level1);
            for (var index = 0; index < Microsoft.UI.Xaml.Media.VisualTreeHelper.GetChildrenCount(node); index++)
                Visit(Microsoft.UI.Xaml.Media.VisualTreeHelper.GetChild(node, index));
        }
        Visit(dialog);
    }

    internal static T Mark<T>(T element, AutomationHeadingLevel level = AutomationHeadingLevel.Level2) where T : FrameworkElement
    {
        AutomationProperties.SetHeadingLevel(element, level);
        return element;
    }
}
