using System;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
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
        Loaded += (_, _) => { ConnectivityService.Shared.Changed += Refresh; Refresh(this, EventArgs.Empty); };
        Unloaded += (_, _) => ConnectivityService.Shared.Changed -= Refresh;
    }
    private void Refresh(object? sender, EventArgs e)
    {
        var notice = ConnectivityService.Shared.Notice;
        Visibility = notice == null ? Visibility.Collapsed : Visibility.Visible;
        message.Text = notice ?? "";
    }
}
