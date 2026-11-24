using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;

namespace Vidi_Studio_Windows.Views;

/// <summary>Uses the native checkbox presentation with one target for video actions.</summary>
internal sealed class VideoBrowserSelection
{
    private readonly ListView list;
    private bool updating;
    private Func<Task>? primaryAction;
    internal bool Enabled => list.SelectionMode == ListViewSelectionMode.Multiple;
    internal event Action? ModeChanged;
    internal const double CheckboxColumnWidth = 42;
    internal const string ActionsButtonName = "VideoRowActions";

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
        ModeChanged?.Invoke();
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

    internal void AddPrimaryAction(Func<Task> run)
    {
        primaryAction = run;
        list.AddHandler(UIElement.DoubleTappedEvent, new DoubleTappedEventHandler(async (_, args) =>
        {
            if (Enabled || !list.IsEnabled) return;
            for (var element = args.OriginalSource as DependencyObject;
                 element != null && element != list; element = VisualTreeHelper.GetParent(element))
            {
                // Embedded row buttons keep their own action.
                if (element is ButtonBase) return;
                if (element is not ListViewItem row) continue;
                args.Handled = true;
                await ActivateRowAsync(row.Content);
                return;
            }
        }), handledEventsToo: true);
    }

    internal Task ActivateRowAsync(object? target) =>
        !Enabled && list.IsEnabled && target != null && ReferenceEquals(list.SelectedItem, target) && primaryAction is { } run
            ? run() : Task.CompletedTask;

    internal void AddRowActions(Func<object, string> title, Action<MenuFlyout, object> populate)
    {
        list.ContainerContentChanging += (_, args) =>
        {
            if (args.InRecycleQueue) return;
            void Attach(ListViewBase _, ContainerContentChangingEventArgs row)
            {
                if (row.ItemContainer.ContentTemplateRoot is not Grid grid ||
                    grid.Children.OfType<DropDownButton>().FirstOrDefault(control => control.Name == ActionsButtonName) is not { } button) return;
                AutomationProperties.SetName(button, "Actions for " + title(row.Item));
                if (button.Flyout != null) return;
                var menu = MenuAccessKeys.Create();
                menu.Opening += (_, _) =>
                {
                    menu.Items.Clear();
                    if (button.DataContext is { } target && list.Items.Contains(target))
                        populate(menu, target);
                };
                button.Flyout = menu;
            }
            if (args.ItemContainer.ContentTemplateRoot is Grid) Attach(list, args);
            else args.RegisterUpdateCallback(Attach);
        };
    }
}
