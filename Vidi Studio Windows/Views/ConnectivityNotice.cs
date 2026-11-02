using System;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Automation.Peers;
using Vidi_Studio_Windows.Services;
namespace Vidi_Studio_Windows.Views;

public sealed class ConnectivityNotice : StackPanel
{
    private readonly TextBlock message = new() { TextWrapping = TextWrapping.Wrap };
    public ConnectivityNotice()
    {
        Spacing = 6; Margin = new Thickness(12);
        var retry = new Button { Content = "Check Connection" };
        retry.Click += (_, _) => ConnectivityService.Shared.Check();
        Children.Add(message); Children.Add(retry);
        AutomationProperties.SetLiveSetting(message, AutomationLiveSetting.Polite);
        Loaded += (_, _) => { ConnectivityService.Shared.Changed += Refresh; Refresh(this, EventArgs.Empty); };
        Unloaded += (_, _) => ConnectivityService.Shared.Changed -= Refresh;
    }
    private void Refresh(object? sender, EventArgs e)
    {
        var notice = ConnectivityService.Shared.Notice;
        Visibility = notice == null ? Visibility.Collapsed : Visibility.Visible;
        var changed = message.Text != notice;
        message.Text = notice ?? "";
        if (changed && FrameworkElementAutomationPeer.FromElement(message) is { } peer)
            peer.RaiseNotificationEvent(AutomationNotificationKind.Other, AutomationNotificationProcessing.ImportantMostRecent, notice ?? "Connection restored. Online features are available.", "connectivity");
    }
}
