using System;
using System.Linq;
using Microsoft.UI.Xaml.Controls;

namespace Vidi_Studio_Windows.Views;

/// <summary>Uses the native checkbox presentation with one target for video actions.</summary>
internal sealed class VideoBrowserSelection
{
    private readonly ListView list;
    private bool updating;
    internal bool Enabled => list.SelectionMode == ListViewSelectionMode.Multiple;

    internal VideoBrowserSelection(ListView list)
    {
        this.list = list;
        list.IsMultiSelectCheckBoxEnabled = true;
        // Run before consumers update metadata or query permissions for the selected object.
        list.SelectionChanged += (_, args) =>
        {
            if (updating || !Enabled || list.SelectedItems.Count < 2) return;
            SetTarget(args.AddedItems.LastOrDefault() ?? list.SelectedItem);
        };
    }

    internal void SetTarget(object? target)
    {
        updating = true;
        try
        {
            if (Enabled)
            {
                foreach (var item in list.SelectedItems.ToArray())
                    if (!ReferenceEquals(item, target)) list.SelectedItems.Remove(item);
                if (target != null && !list.SelectedItems.Contains(target)) list.SelectedItems.Add(target);
            }
            else list.SelectedItem = target;
        }
        finally { updating = false; }
    }

    internal void SetMode(bool enabled)
    {
        var current = list.SelectedItem;
        list.SelectionMode = enabled ? ListViewSelectionMode.Multiple : ListViewSelectionMode.Single;
        SetTarget(current);
    }

    internal ToggleMenuFlyoutItem ModeItem(bool available)
    {
        var item = new ToggleMenuFlyoutItem {
            Text = "Select", Icon = MenuIcons.Create("selection-toggle"),
            IsChecked = Enabled, IsEnabled = available
        };
        item.Click += (_, _) => SetMode(item.IsChecked);
        return item;
    }

    internal ToggleMenuFlyoutItem TargetItem(object target)
    {
        var item = new ToggleMenuFlyoutItem {
            Text = "Select Video", Icon = MenuIcons.Create("selection-toggle"),
            IsChecked = ReferenceEquals(list.SelectedItem, target)
        };
        item.Click += (_, _) => SetTarget(item.IsChecked ? target : null);
        return item;
    }
}
