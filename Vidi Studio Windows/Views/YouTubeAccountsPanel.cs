using System.Linq;

using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media.Imaging;
using System;
using System.Threading;
using Vidi_Studio_Windows.Services;

namespace Vidi_Studio_Windows.Views;

public sealed class YouTubeAccountsPanel : StackPanel
{
    private readonly TextBlock status = new() { TextWrapping = TextWrapping.Wrap };
    private readonly ComboBox accounts = new() { Header = "Google account and YouTube channel", HorizontalAlignment = HorizontalAlignment.Stretch };
    private readonly StackPanel profile = new() { Spacing = 8 };
    private readonly Button add = new() { Content = "Add Account" };
    private CancellationTokenSource? operation;
    public event EventHandler? AccountsChanged;
    public YouTubeAccountsPanel()
    {
        Spacing = 12;
        Children.Add(new TextBlock { Text = "YouTube Accounts", FontSize = 20 }); Children.Add(accounts); Children.Add(profile);
        Children.Add(add);
        var cancel = new Button { Content = "Cancel Sign-in" }; Children.Add(cancel); cancel.Click += (_, _) => operation?.Cancel();
        var signOut = new Button { Content = "Sign Out" }; Children.Add(signOut);
        var disconnect = new Button { Content = "Disconnect Google Account…" }; Children.Add(disconnect);
        var confirm = new CheckBox { Content = "Disconnect all VidiStudio connections for this Google account, including other computers" }; Children.Add(confirm);
        Children.Add(status);
        accounts.SelectionChanged += (_, _) => { if (accounts.SelectedItem is YouTubeAccount account) { YouTubeService.Shared.SelectedId = account.Id; ShowProfile(account); AccountsChanged?.Invoke(this, EventArgs.Empty); } };
        add.Click += async (_, _) => {
            operation?.Cancel(); operation = new(); add.IsEnabled = false; status.Text = "Complete Google sign-in in your browser.";
            try { await YouTubeService.Shared.SignInAsync(operation.Token); status.Text = "Account connected."; Refresh(); }
            catch (OperationCanceledException) { status.Text = "Sign-in cancelled or timed out."; }
            catch (Exception ex) { status.Text = ex.Message; }
            finally { add.IsEnabled = true; }
        };
        signOut.Click += (_, _) => { if (accounts.SelectedItem is YouTubeAccount account) { YouTubeService.Shared.SignOut(account.Id); Refresh(); } };
        disconnect.Click += async (_, _) => {
            if (confirm.IsChecked != true) { status.Text = "Confirm disconnecting this Google account. Other VidiStudio connections may need sign-in again."; return; }
            if (accounts.SelectedItem is not YouTubeAccount account) return;
            try { await YouTubeService.Shared.DisconnectAsync(account, CancellationToken.None); confirm.IsChecked = false; Refresh(); }
            catch (Exception ex) { status.Text = ex.Message; }
        };
        Unloaded += (_, _) => operation?.Cancel(); Refresh();
    }
    public void Refresh()
    {
        accounts.ItemsSource = YouTubeService.Shared.Accounts.ToArray(); accounts.SelectedItem = YouTubeService.Shared.Selected;
        if (accounts.SelectedItem is YouTubeAccount account) ShowProfile(account); else profile.Children.Clear();
        AccountsChanged?.Invoke(this, EventArgs.Empty);
    }
    private void ShowProfile(YouTubeAccount account)
    {
        profile.Children.Clear();
        if (Uri.TryCreate(account.Photo, UriKind.Absolute, out var photo)) profile.Children.Add(new Image { Source = new BitmapImage(photo), Width = 40, Height = 40, HorizontalAlignment = HorizontalAlignment.Left });
        profile.Children.Add(new TextBlock { Text = account.Name + "\n" + account.Email + "\nYouTube channel: " + account.ChannelName, TextWrapping = TextWrapping.Wrap });
    }
}
