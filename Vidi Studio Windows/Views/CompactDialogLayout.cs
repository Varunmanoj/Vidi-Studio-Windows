using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Markup;
using Microsoft.UI.Xaml.Media;
using System;
using System.Linq;

namespace Vidi_Studio_Windows.Views;

/// <summary>Shared form presentation; leaves values, headers, events and native dialog commands intact.</summary>
internal static class CompactDialogLayout
{
    private const string RowTag = "dialog-form-row";
    private const string CardTag = "dialog-form-card";

    internal static Border Card(UIElement content, string name = "Dialog options") => SettingsDialog.CompactCard(
        new AccessibilityGroup(name) { Child = content });

    internal static UIElement PrepareContent(UIElement content)
    {
        if (Header(content as FrameworkElement) is { } title && content is FrameworkElement field && IsInline(field)) {
            var rows = new StackPanel(); rows.Children.Add(Field(title, field));
            return Card(rows);
        }
        Prepare(content, false);
        return content;
    }

    internal static void PrepareDialog(ContentDialog dialog)
    {
        // The video browser already has a dedicated toolbar/list layout.
        if (dialog.Title as string == "My YouTube Videos") return;
        if (dialog.Content is UIElement content) dialog.Content = PrepareContent(content);
    }

    private static string? Header(FrameworkElement? field) => field switch {
        ComboBox box => box.Header as string,
        TextBox box => box.Header as string,
        PasswordBox box => box.Header as string,
        NumberBox box => box.Header as string,
        _ => null
    };
    private static bool IsInline(FrameworkElement field) => field.Tag as string != "voice-search-full-width" && (field is ComboBox or PasswordBox or NumberBox || field is TextBox { AcceptsReturn: false });

    internal static Grid Field(string label, FrameworkElement field)
    {
        // Keep Header for existing enablement logic and automation. Hide only its visual template.
        var hiddenHeader = (DataTemplate)XamlReader.Load("<DataTemplate xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation'><Grid Visibility='Collapsed'/></DataTemplate>");
        switch (field) {
            case ComboBox box: box.HeaderTemplate = hiddenHeader; break;
            case TextBox box: box.HeaderTemplate = hiddenHeader; break;
            case PasswordBox box: box.HeaderTemplate = hiddenHeader; break;
            case NumberBox box: box.HeaderTemplate = hiddenHeader; break;
        }
        void HideHeader(DependencyObject root) {
            for (var i=0; i<VisualTreeHelper.GetChildrenCount(root); i++) {
                var child = VisualTreeHelper.GetChild(root,i);
                if (child is FrameworkElement { Name: "HeaderContentPresenter" } presenter) presenter.Visibility = Visibility.Collapsed;
                else HideHeader(child);
            }
        }
        field.Loaded += (_,_) => HideHeader(field);
        field.ActualThemeChanged += (_,_) => HideHeader(field);
        var name = AutomationProperties.GetName(field);
        var margin = field.Margin; field.Margin = new Thickness(0); field.MinWidth = 0;
        if (!double.IsNaN(field.Width)) { field.MaxWidth = field.Width; field.Width = double.NaN; }
        var rows = new StackPanel();
        var row = SettingsDialog.AddSettingsRow(rows,label,field,true,360);
        rows.Children.Remove(row); row.Tag = RowTag; row.Margin = margin;
        if (!string.IsNullOrEmpty(name)) AutomationProperties.SetName(field,name);
        return row;
    }

    private static void Prepare(UIElement content, bool insideCard, bool preserveWidth = false)
    {
        if (content is FrameworkElement tagged && (tagged.Tag as string is RowTag or CardTag || tagged.Tag as string == "settings-row")) return;
        if (!preserveWidth && content is FrameworkElement sized && content is Panel or UserControl && !double.IsNaN(sized.Width)) {
            sized.MaxWidth = Math.Min(sized.MaxWidth,sized.Width); sized.Width = double.NaN;
        }
        switch (content) {
            case Border border when border.Child is { } child:
                Prepare(child,true,preserveWidth); return;
            case ScrollViewer scroll when scroll.Content is UIElement child:
                Prepare(child,insideCard,preserveWidth || (scroll.HorizontalScrollMode != ScrollMode.Disabled && scroll.HorizontalScrollBarVisibility != ScrollBarVisibility.Disabled)); return;
            case ContentControl control when control.Content is UIElement child:
                Prepare(child,insideCard,preserveWidth); return;
            case Panel panel:
                foreach (var child in panel.Children.ToArray()) Prepare(child,insideCard,preserveWidth);
                if (panel is StackPanel { Orientation: Orientation.Horizontal }) return;
                // Preserve Grid coordinates when a field already participates in a form grid.
                for (var i=0; i<panel.Children.Count; i++) {
                    if (panel.Children[i] is not FrameworkElement field || Header(field) is not { Length: >0 } label || !IsInline(field)) continue;
                    var column=Grid.GetColumn(field); var span=Grid.GetColumnSpan(field);
                    var gridRow=Grid.GetRow(field); var rowSpan=Grid.GetRowSpan(field);
                    panel.Children.RemoveAt(i); var row=Field(label,field);
                    Grid.SetColumn(row,column); Grid.SetColumnSpan(row,span); Grid.SetRow(row,gridRow); Grid.SetRowSpan(row,rowSpan);
                    panel.Children.Insert(i,row);
                }
                if (panel is not StackPanel stack) return;
                stack.Spacing = Math.Min(stack.Spacing,10);
                // Related buttons share an action row. Always detach from the known collection.
                GroupActions(stack);
                if (!insideCard) GroupFields(stack);
                return;
        }
    }

    private static bool Action(UIElement item) => item is Button or HyperlinkButton;
    private static void GroupActions(StackPanel panel)
    {
        for(var i=0;i<panel.Children.Count;i++) {
            var end=i; while(end<panel.Children.Count && Action(panel.Children[end])) end++;
            if(end-i<2)continue;
            var actions = new CompactActionsPanel();
            for(var count=end-i;count>0;count--) {var button=panel.Children[i];panel.Children.RemoveAt(i);actions.Children.Add(button);}
            panel.Children.Insert(i,actions);
        }
    }
    private static bool Form(UIElement child) => child is FrameworkElement element && element.Tag as string == RowTag || child is TextBox { AcceptsReturn: true };
    private static void GroupFields(StackPanel panel)
    {
        for(var i=0;i<panel.Children.Count;i++) {
            if(!Form(panel.Children[i]))continue;
            var rows=new StackPanel { Spacing=8 };
            while(i<panel.Children.Count && Form(panel.Children[i])) {var child=panel.Children[i];panel.Children.RemoveAt(i);rows.Children.Add(child);}
            var title=i>0 && panel.Children[i-1] is TextBlock heading && heading.FontWeight.Weight>=600 ? heading.Text : "Dialog options";
            var card=Card(rows,title); card.Tag=CardTag;panel.Children.Insert(i,card);
        }
    }
}
