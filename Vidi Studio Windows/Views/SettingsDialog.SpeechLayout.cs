using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using System;
using Vidi_Studio_Windows.Services;
using System.Collections.Generic;
using System.Linq;

namespace Vidi_Studio_Windows.Views;

// Measure related actions against the available page width. Rebuilding a page must
// not leave a row stacked because its first SizeChanged reported a transient width.
internal sealed class CompactActionsPanel : Panel
{
    private const double Gap = 8;
    private bool Fits(double width) => Children.Sum(c => c.DesiredSize.Width) + Gap * System.Math.Max(0,Children.Count - 1) <= width;
    protected override Windows.Foundation.Size MeasureOverride(Windows.Foundation.Size availableSize)
    {
        foreach (var child in Children) child.Measure(new Windows.Foundation.Size(availableSize.Width,double.PositiveInfinity));
        var horizontal = Fits(availableSize.Width);
        var width = horizontal ? Children.Sum(c => c.DesiredSize.Width) + Gap * System.Math.Max(0,Children.Count - 1) : Children.Select(c => c.DesiredSize.Width).DefaultIfEmpty(0).Max();
        var height = horizontal ? Children.Select(c => c.DesiredSize.Height).DefaultIfEmpty(0).Max() : Children.Sum(c => c.DesiredSize.Height) + Gap * System.Math.Max(0,Children.Count - 1);
        return new Windows.Foundation.Size(double.IsInfinity(availableSize.Width) ? width : availableSize.Width,height);
    }
    protected override Windows.Foundation.Size ArrangeOverride(Windows.Foundation.Size finalSize)
    {
        var horizontal = Fits(finalSize.Width); double offset = 0;
        foreach (var child in Children) {
            child.Arrange(new Windows.Foundation.Rect(horizontal ? offset : 0, horizontal ? 0 : offset,System.Math.Min(child.DesiredSize.Width,finalSize.Width),child.DesiredSize.Height));
            offset += (horizontal ? child.DesiredSize.Width : child.DesiredSize.Height) + Gap;
        }
        return finalSize;
    }
}

public sealed partial class SettingsDialog
{
    private void SetVoiceSampleMenu(Button button, string id, Func<bool> canPlay, Action play, Func<bool> active, Action stop) {
        var menu = MenuAccessKeys.Create();
        var start = new MenuFlyoutItem { Text = "Play Sample", Tag = id + "-play" };
        var stopItem = new MenuFlyoutItem { Text = "Stop Sample", Tag = id + "-stop" };
        start.Click += (_,_) => { if (canPlay()) play(); };
        stopItem.Click += (_,_) => { if (active()) stop(); };
        menu.Opening += (_,_) => { start.IsEnabled = canPlay(); stopItem.IsEnabled = active(); };
        menu.Items.Add(start); menu.Items.Add(stopItem); button.ContextFlyout = menu;
    }


    private static FontIcon ApiKeyStatusIcon(TextBlock status, string id = "sarvam-key-status")
    {
        var icon = (FontIcon)Microsoft.UI.Xaml.Markup.XamlReader.Load("<FontIcon xmlns=\"http://schemas.microsoft.com/winfx/2006/xaml/presentation\" FontSize=\"18\" Width=\"24\" VerticalAlignment=\"Center\"><FontIcon.Style><Style TargetType=\"FontIcon\"><Setter Property=\"Foreground\" Value=\"{ThemeResource TextFillColorSecondaryBrush}\"/></Style></FontIcon.Style></FontIcon>");
        AutomationProperties.SetAutomationId(icon,id);
        AutomationProperties.SetAccessibilityView(icon,Microsoft.UI.Xaml.Automation.Peers.AccessibilityView.Control);
        AutomationProperties.SetLiveSetting(icon,Microsoft.UI.Xaml.Automation.Peers.AutomationLiveSetting.Polite);
        void Update()
        {
            var verified = status.Text.StartsWith("API key verified",System.StringComparison.OrdinalIgnoreCase);
            var rejected = status.Text.Contains("rejected",System.StringComparison.OrdinalIgnoreCase);
            icon.Glyph = verified ? "\uE73E" : rejected ? "\uE711" : status.Text.StartsWith("Checking",System.StringComparison.Ordinal) ? "\uE823" : "\uE7BA";
            if (verified || rejected) icon.Foreground = new SolidColorBrush(verified ? (icon.ActualTheme == ElementTheme.Dark ? Windows.UI.Color.FromArgb(255,108,203,95) : Windows.UI.Color.FromArgb(255,16,124,16)) : Windows.UI.Color.FromArgb(255,229,43,50));
            else icon.ClearValue(IconElement.ForegroundProperty);
            AutomationProperties.SetName(icon,status.Text); ToolTipService.SetToolTip(icon,status.Text);
            Microsoft.UI.Xaml.Automation.Peers.FrameworkElementAutomationPeer.FromElement(icon)?.RaiseAutomationEvent(Microsoft.UI.Xaml.Automation.Peers.AutomationEvents.LiveRegionChanged);
        }
        status.RegisterPropertyChangedCallback(TextBlock.TextProperty,(_,_) => Update());
        icon.ActualThemeChanged += (_,_) => Update();
        Update(); return icon;
    }
#if DEBUG
    private static void VerifyKeyActionsForSmokeTest(Panel actions)
    {
        actions.UpdateLayout();
        var children = actions.Children.OfType<FrameworkElement>().ToArray();
        if (children.Length != 2) throw new System.InvalidOperationException("API key actions must stay grouped.");
        var requiredWidth = children.Sum(c => c.DesiredSize.Width) + 8;
        if (actions.ActualWidth >= requiredWidth) {
            var first = children[0].TransformToVisual(actions).TransformPoint(new Windows.Foundation.Point());
            var second = children[1].TransformToVisual(actions).TransformPoint(new Windows.Foundation.Point());
            if (System.Math.Abs(first.Y-second.Y) > 1 || second.X <= first.X)
                throw new System.InvalidOperationException("API key actions must sit beside each other whenever they fit.");
        }
    }
    internal void BringNarrationIntoViewForSmokeTest(bool key)
    {
        FrameworkElement target = key ? SpeechItems<PasswordBox>(sarvamControls).Single() : SpeechItems<AccessibilityGroup>(currentSectionPanel!).First();
        target.StartBringIntoView(new BringIntoViewOptions { VerticalAlignmentRatio = 0 });
    }
    internal void VerifyNarrationThemeForSmokeTest()
    {
        if (((FrameworkElement)Content).ActualTheme != ElementTheme.Dark) return;
        var cards = SpeechItems<AccessibilityGroup>(currentSectionPanel!).Where(c => c.Tag as string == "narration-card").Select(c => c.Background)
            .Concat(SpeechItems<Border>(currentSectionPanel!).Where(b => b.Tag as string == "settings-card" || ReferenceEquals(b,sarvamSettingsCard) || ReferenceEquals(b,summarySettingsCard)).Select(c => c.Background)).ToArray();
        if (cards.Length < 3) throw new System.InvalidOperationException("Narration theme verification must include local, Sarvam and Summary cards.");
        foreach (var background in cards) {
            if (background is not SolidColorBrush brush) throw new System.InvalidOperationException("Narration cards must expose their theme fill.");
            var color = brush.Color;
            var brightness = (color.R + color.G + color.B) / 3.0 * color.A / 255.0 + 32 * (1 - color.A / 255.0);
            if (brightness > 120) throw new System.InvalidOperationException("Dark Narration cards must not use a light-theme fill.");
        }
        var text = new TextBlock { Text = "API key verified" }; var icon = ApiKeyStatusIcon(text);
        if (AutomationProperties.GetName(icon) != text.Text || icon.Glyph != "\uE73E") throw new System.InvalidOperationException("Verified key needs an accessible inline checkmark.");
        text.Text = "Checking API key…";
        if (AutomationProperties.GetName(icon) != text.Text || icon.Glyph != "\uE823") throw new System.InvalidOperationException("Inline status must update while checking.");
    }
#endif

    private static Border SpeechSettingsCard(StackPanel content)
    {
        var card = (Border)Microsoft.UI.Xaml.Markup.XamlReader.Load("<Border xmlns=\"http://schemas.microsoft.com/winfx/2006/xaml/presentation\" Background=\"{ThemeResource CardBackgroundFillColorDefaultBrush}\" Padding=\"16\" CornerRadius=\"12\" Margin=\"0,8,0,8\"/>");
        card.Child = content;
        return card;
    }

    private static IEnumerable<T> SpeechItems<T>(DependencyObject root) where T : DependencyObject
    {
        if (root is T item) yield return item;
        IEnumerable<DependencyObject> children = root switch {
            Panel panel => panel.Children.Cast<DependencyObject>(),
            Border { Child: { } child } => new[] { child },
            ScrollViewer { Content: DependencyObject child } => new[] { child },
            ContentControl { Content: DependencyObject child } => new[] { child },
            _ => System.Array.Empty<DependencyObject>()
        };
        foreach (var child in children) foreach (var descendant in SpeechItems<T>(child)) yield return descendant;
    }

    private static void SpeechActionsRow(StackPanel panel, string id, params FrameworkElement[] controls)
    {
        var position = controls.Where(c => panel.Children.Contains(c)).Select(c => panel.Children.IndexOf(c)).DefaultIfEmpty(panel.Children.Count).Min();
        foreach (var control in controls) {
            if (panel.Children.Contains(control)) panel.Children.Remove(control);
            else if (control.Parent is Panel parent) parent.Children.Remove(control);
        }
        Panel row = id.StartsWith("elevenlabs-",StringComparison.Ordinal) || id is "sarvam-key-actions" or "gemini-key-actions" ? new CompactActionsPanel() : new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        row.HorizontalAlignment = HorizontalAlignment.Stretch;
        AutomationProperties.SetAutomationId(row,id);
        foreach (var control in controls) { control.VerticalAlignment = VerticalAlignment.Center; row.Children.Add(control); }
        if (row is StackPanel stack) stack.SizeChanged += (_,args) => stack.Orientation = args.NewSize.Width < (controls.Length <= 2 ? 360 : 520) ? Orientation.Vertical : Orientation.Horizontal;
        panel.Children.Insert(System.Math.Min(position,panel.Children.Count),row);
    }
}
