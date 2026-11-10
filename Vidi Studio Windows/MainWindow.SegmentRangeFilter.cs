using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using System;
using System.Collections.Generic;
using System.Linq;
using Vidi_Studio_Windows.Models;

namespace Vidi_Studio_Windows;

public sealed partial class MainWindow
{
    private static void FilterActionMenu(Button button, string title, Action action)
    {
        var menu = new MenuFlyout(); var item = new MenuFlyoutItem { Text = title };
        item.Click += (_, _) => action(); menu.Items.Add(item); button.ContextFlyout = menu;
    }
    private StackPanel BuildSegmentRangeFilterControls(List<Segment> rows, SegmentRangeFilter draft, double duration, Action changed)
    {
        var modes = new[] { "all", "selection", "numberRanges", "timeRanges" };
        var titles = new[] { "All segments", "Select segment numbers", "Segment number ranges", "Time ranges" };
        var root = new StackPanel { Spacing = 10 };
        var mode = new ComboBox { Header = "Filter type", ItemsSource = titles, SelectedIndex = Math.Max(0, Array.IndexOf(modes, draft.Mode)), HorizontalAlignment = HorizontalAlignment.Stretch };
        AutomationProperties.SetAutomationId(mode, "segment-filter-type");
        var details = new StackPanel { Spacing = 12 };
        var modeMenu = new MenuFlyout();
        foreach (var index in Enumerable.Range(0, modes.Length))
        {
            var item = new ToggleMenuFlyoutItem { Text = titles[index], Tag = "filter-mode-" + modes[index] };
            item.Click += (_, _) => mode.SelectedIndex = index;
            modeMenu.Opening += (_, _) => item.IsChecked = draft.Mode == modes[index];
            modeMenu.Items.Add(item);
        }
        mode.ContextFlyout = modeMenu;
        root.Children.Add(mode); root.Children.Add(details);
        void AddNumberRange() { draft.NumberRanges.Add(new() { IsDescription = Section.SelectedIndex == 2 }); Render(); changed(); }
        void AddTimeRange() { draft.TimeRanges.Add(new() { End = double.IsFinite(duration) && duration > 0 ? duration : 1 }); Render(); changed(); }
        void Render()
        {
            details.Children.Clear();
            if (draft.Mode == "selection")
            {
                var checks = new StackPanel { Spacing = 6 };
                foreach (var segment in rows)
                {
                    var key = ValidationKey(segment);
                    var label = $"{(segment.IsDescription ? "Audio description" : "Transcript")} #{segment.SegmentNumber} · {TimeSpan.FromSeconds(segment.Start):hh\\:mm\\:ss\\.fff}–{TimeSpan.FromSeconds(segment.End):hh\\:mm\\:ss\\.fff} · {segment.Text}";
                    var check = new CheckBox { Content = new TextBlock { Text = label, TextWrapping = TextWrapping.Wrap }, IsChecked = draft.SelectedIds.Contains(key), HorizontalAlignment = HorizontalAlignment.Stretch };
                    AutomationProperties.SetName(check, label);
                    AutomationProperties.SetAutomationId(check, "filter-segment-" + key);
                    var border = new Border { Child = check, Padding = new Thickness(8), CornerRadius = new CornerRadius(6), BorderThickness = new Thickness(1) };
                    void Update()
                    {
                        var selected = check.IsChecked == true;
                        if (selected) draft.SelectedIds.Add(key); else draft.SelectedIds.Remove(key);
                        var accent = (Windows.UI.Color)Application.Current.Resources["SystemAccentColor"];
                        border.Background = selected ? new SolidColorBrush(Windows.UI.Color.FromArgb(45, accent.R, accent.G, accent.B)) : null;
                        border.BorderBrush = selected ? new SolidColorBrush(accent) : null;
                        changed();
                    }
                    check.Checked += (_, _) => Update(); check.Unchecked += (_, _) => Update();
                    var menu = new MenuFlyout();
                    var item = new ToggleMenuFlyoutItem { Text = label, Tag = "filter-segment-number-menu" };
                    item.Click += (_, _) => check.IsChecked = item.IsChecked;
                    menu.Opening += (_, _) => item.IsChecked = check.IsChecked == true;
                    menu.Items.Add(item); check.ContextFlyout = menu;
                    Update(); checks.Children.Add(border);
                }
                details.Children.Add(new ScrollViewer { Content = checks, MaxHeight = 260 });
            }
            if (draft.Mode == "numberRanges")
            {
                foreach (var range in draft.NumberRanges.ToArray())
                {
                    var group = new StackPanel { Spacing = 8 };
                    if (Section.SelectedIndex == 4)
                    {
                        var type = new ComboBox { Header = "Segment type", ItemsSource = new[] { "Transcript", "Audio description" }, SelectedIndex = range.IsDescription ? 1 : 0 };
                        type.SelectionChanged += (_, _) => { range.IsDescription = type.SelectedIndex == 1; Render(); changed(); };
                        group.Children.Add(type);
                    }
                    var count = rows.Count(row => row.IsDescription == range.IsDescription);
                    var start = new NumberBox { Header = "Start segment", Minimum = 1, Maximum = Math.Max(1, count), Value = range.Start, SpinButtonPlacementMode = NumberBoxSpinButtonPlacementMode.Inline, SmallChange = 1, Width = 180, IsEnabled = count > 0 };
                    var end = new NumberBox { Header = "End segment", Minimum = 1, Maximum = Math.Max(1, count), Value = range.End, SpinButtonPlacementMode = NumberBoxSpinButtonPlacementMode.Inline, SmallChange = 1, Width = 180, IsEnabled = count > 0 };
                    AutomationProperties.SetAutomationId(start, "filter-start-segment"); AutomationProperties.SetAutomationId(end, "filter-end-segment");
                    start.ValueChanged += (_, _) => { range.Start = start.Value; changed(); };
                    end.ValueChanged += (_, _) => { range.End = end.Value; changed(); };
                    group.Children.Add(new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10, Children = { start, end } });
                    var remove = new Button { Content = "Remove Range" };
                    void Remove() { draft.NumberRanges.Remove(range); Render(); changed(); }
                    remove.Click += (_, _) => Remove(); FilterActionMenu(remove, "Remove Range", Remove); group.Children.Add(remove);
                    details.Children.Add(group);
                }
                var add = new Button { Content = "Add Segment Range" };
                add.Click += (_, _) => AddNumberRange(); FilterActionMenu(add, "Add Segment Range", AddNumberRange); details.Children.Add(add);
            }
            if (draft.Mode == "timeRanges")
            {
                foreach (var range in draft.TimeRanges.ToArray())
                {
                    var timing = new Views.SegmentTimingEditor(range.Start, range.End, duration, () => Position);
                    timing.StartPicker.ValueChanged += (_, _) => { range.Start = timing.StartPicker.Value; changed(); };
                    timing.EndPicker.ValueChanged += (_, _) => { range.End = timing.EndPicker.Value; changed(); };
                    details.Children.Add(timing);
                    var remove = new Button { Content = "Remove Range" };
                    void Remove() { draft.TimeRanges.Remove(range); Render(); changed(); }
                    remove.Click += (_, _) => Remove(); FilterActionMenu(remove, "Remove Range", Remove); details.Children.Add(remove);
                }
                var add = new Button { Content = "Add Time Range" };
                add.Click += (_, _) => AddTimeRange(); FilterActionMenu(add, "Add Time Range", AddTimeRange); details.Children.Add(add);
                var matching = new ComboBox { Header = "Time matching", ItemsSource = new[] { "Overlaps a time range", "Fully inside a time range" }, SelectedIndex = draft.Contained ? 1 : 0 };
                matching.SelectionChanged += (_, _) => { draft.Contained = matching.SelectedIndex == 1; changed(); };
                var menu = new MenuFlyout();
                foreach (var contained in new[] { false, true })
                {
                    var item = new ToggleMenuFlyoutItem { Text = contained ? "Fully inside a time range" : "Overlaps a time range" };
                    item.Click += (_, _) => matching.SelectedIndex = contained ? 1 : 0;
                    menu.Opening += (_, _) => item.IsChecked = draft.Contained == contained; menu.Items.Add(item);
                }
                matching.ContextFlyout = menu; details.Children.Add(matching);
            }
        }
        mode.SelectionChanged += (_, _) =>
        {
            draft.Mode = modes[Math.Max(0, mode.SelectedIndex)];
            if (draft.Mode == "numberRanges" && draft.NumberRanges.Count == 0) AddNumberRange();
            else if (draft.Mode == "timeRanges" && draft.TimeRanges.Count == 0) AddTimeRange();
            else { Render(); changed(); }
        };
        Render(); return root;
    }
}
