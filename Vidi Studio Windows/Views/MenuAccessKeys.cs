using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Vidi_Studio_Windows.Views;

internal static class MenuAccessKeys
{
    internal static MenuFlyout Create()
    {
        var menu = new MenuFlyout();
        menu.Opening += Prepare;
        menu.Opened += Display;
        return menu;
    }

    internal static void Assign(IEnumerable<MenuFlyoutItemBase> entries)
    {
        // ToggleMenuFlyoutItem inherits MenuFlyoutItem and shares its key scope.
        var choices = entries.Where(entry => entry is MenuFlyoutItem or MenuFlyoutSubItem).ToArray();
        var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        string Sequence(int number)
        {
            const string digits = "0123456789ABCDEFGHIJKLMNOPQRSTUVWXYZ";
            var value = "";
            do { value = digits[number % 36] + value; number /= 36; } while (number > 0);
            return value;
        }
        var width = Sequence(Math.Max(0, choices.Length - 1)).Length;
        for (var index = 0; index < choices.Length; index++)
        {
            var entry = choices[index];
            var text = entry switch { MenuFlyoutItem item => item.Text, MenuFlyoutSubItem group => group.Text, _ => "" };
            // Large scopes use equal-length sequences: a short key must never
            // shadow the prefix of another choice's key.
            entry.AccessKey = choices.Length > 36 ? Sequence(index).PadLeft(width, '0')
                : text.ToUpperInvariant().Where(char.IsAsciiLetterOrDigit).Select(ch => ch.ToString())
                    .Concat("ABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789".Select(ch => ch.ToString())).First(used.Add);
            if (entry is MenuFlyoutSubItem submenu) Assign(submenu.Items);
        }
    }

    internal static void Prepare(object? sender, object args)
    {
        if (sender is MenuFlyout menu) Assign(menu.Items);
    }

    internal static void Display(object? sender, object args)
    {
        if (sender is not MenuFlyout menu) return;
        // Popup items belong to their actual window's XamlRoot, including Settings/dialogs.
        var root = menu.Items.OfType<FrameworkElement>().FirstOrDefault(item => item.XamlRoot != null)?.XamlRoot;
        if (root != null) root.Content.DispatcherQueue.TryEnqueue(() => AccessKeyManager.EnterDisplayMode(root));
    }
}
