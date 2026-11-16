using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media.Imaging;
using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Vidi_Studio_Windows.Services;

namespace Vidi_Studio_Windows.Views;

public sealed class YouTubeAccountsPanel : StackPanel
{
    private readonly TextBlock status = new() { TextWrapping = TextWrapping.Wrap, Visibility = Visibility.Collapsed };
    private readonly StackPanel profiles = new() { Spacing = 12 };
    private readonly CompactActionsPanel links = new();
    private readonly Button add = new() { Content = "Add Account" };
    private readonly Button cancel = new() { Content = "Cancel Sign-in", Visibility = Visibility.Collapsed };
    private readonly ProgressRing progress = new() { Width = 18, Height = 18, Visibility = Visibility.Collapsed };
    private CancellationTokenSource? operation;
    private readonly Window owner;
    public event EventHandler? AccountsChanged;
    public event EventHandler? Done;
    public YouTubeAccountsPanel(Window owner, bool showsDone = false)
    {
        this.owner = owner; Spacing = 12;
        if (showsDone) {
            var header = new Grid(); header.ColumnDefinitions.Add(new ColumnDefinition()); header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            header.Children.Add(ScreenReaderHeading.Mark(new TextBlock { Text = "YouTube Accounts", FontSize = 24, VerticalAlignment = VerticalAlignment.Center }));
            var done = new Button { Content = "Done" }; done.Click += (_, _) => Done?.Invoke(this, EventArgs.Empty); Grid.SetColumn(done, 1); header.Children.Add(done); Children.Add(header);
        }
        Children.Add(ScreenReaderHeading.Mark(new TextBlock { Text="Google accounts",FontWeight=Microsoft.UI.Text.FontWeights.SemiBold }));
        Children.Add(profiles);
        var actions = SettingsDialog.CompactActions(add,progress,cancel);
        var connections=SettingsDialog.CompactGroup(this,"Account connections"); connections.Children.Add(actions);
        AutomationProperties.SetAccessibilityView(progress, Microsoft.UI.Xaml.Automation.Peers.AccessibilityView.Raw);
        AutomationProperties.SetLiveSetting(status, Microsoft.UI.Xaml.Automation.Peers.AutomationLiveSetting.Polite); connections.Children.Add(status);
        add.Click += async (_, _) => await SignIn(); cancel.Click += (_, _) => operation?.Cancel();
        SettingsDialog.CompactGroup(this,"Google and YouTube").Children.Add(links);
        AddLink("Manage Google account access", "https://myaccount.google.com/connections");
        AddLink("YouTube Terms of Service", "https://www.youtube.com/t/terms");
        var menu = MenuAccessKeys.Create();
        AddMenu(menu, "Add Account", () => _ = SignIn(), () => operation == null);
        AddMenu(menu, "Cancel Sign-in", () => operation?.Cancel(), () => operation != null);
        ContextFlyout = menu;
        Unloaded += (_, _) => operation?.Cancel(); Refresh();
    }
    private void ShowStatus(string text) { status.Text = text; status.Visibility = text.Length == 0 ? Visibility.Collapsed : Visibility.Visible; }
    private void AddLink(string title, string url)
    {
        var link = new HyperlinkButton { Content = title, NavigateUri = new Uri(url), Padding = new Thickness(0), HorizontalAlignment = HorizontalAlignment.Left };
        var menu = MenuAccessKeys.Create(); AddMenu(menu, title, () => _ = Windows.System.Launcher.LaunchUriAsync(new Uri(url)), () => true); link.ContextFlyout = menu;
        links.Children.Add(link);
    }
    private static void AddMenu(MenuFlyout menu, string title, Action action, Func<bool> available)
    {
        var item = new MenuFlyoutItem { Text = title, IsEnabled = available() };
        menu.Opening += (_, _) => item.IsEnabled = available(); item.Click += (_, _) => { if (available()) action(); }; menu.Items.Add(item);
    }
    private async Task SignIn()
    {
        if (operation != null) return;
        operation = new(); add.IsEnabled = false; cancel.Visibility = progress.Visibility = Visibility.Visible; progress.IsActive = true;
        ShowStatus("Waiting for Google sign-in"); RefreshRows();
        try { await YouTubeService.Shared.SignInAsync(operation.Token); ShowStatus(""); }
        catch (OperationCanceledException) { ShowStatus("Sign-in cancelled or timed out."); }
        catch (Exception ex) { ShowStatus(ex.Message); }
        finally { operation.Dispose(); operation = null; add.IsEnabled = true; cancel.Visibility = progress.Visibility = Visibility.Collapsed; progress.IsActive = false; Refresh(); }
    }
    private void Switch(YouTubeAccount account) { if (operation != null) return; YouTubeService.Shared.SelectedId = account.Id; Refresh(); }
    private void SignOut(YouTubeAccount account) { if (operation != null) return; YouTubeService.Shared.SignOut(account.Id); Refresh(); }
    private async Task Disconnect(YouTubeAccount account)
    {
        if (operation != null) return;
        if (await WindowedDialog.ShowAsync(CreateDisconnectConfirmation(), owner, width: 560, height: 300) != ContentDialogResult.Primary) return;
        try { await YouTubeService.Shared.DisconnectAsync(account, CancellationToken.None); Refresh(); }
        catch (Exception ex) { ShowStatus(ex.Message); }
    }
    internal static ContentDialog CreateDisconnectConfirmation() => new()
    {
        Title = "Disconnect this Google account?",
        Content = new TextBlock { Text = "Disconnect this account across VidiStudio connections, including other computers? Local projects stay on this computer.", TextWrapping = TextWrapping.Wrap },
        PrimaryButtonText = "Disconnect", CloseButtonText = "Cancel", DefaultButton = ContentDialogButton.Close
    };
    public void Refresh() { RefreshRows(); AccountsChanged?.Invoke(this, EventArgs.Empty); }
#if DEBUG
    internal void PreviewAccountsForSmokeTest(YouTubeAccount[] accounts) => RefreshRows(accounts);
#endif
    private void RefreshRows() => RefreshRows(YouTubeService.Shared.Accounts.ToArray());
    private void RefreshRows(YouTubeAccount[] accounts)
    {
        profiles.Children.Clear();
        if(accounts.Length==0) profiles.Children.Add(SettingsDialog.CompactCard(new TextBlock { Text="No accounts connected",TextWrapping=TextWrapping.Wrap }));
        foreach (var account in accounts) {
            var row = new Grid { ColumnSpacing = 12, RowSpacing = 8 };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto }); row.ColumnDefinitions.Add(new ColumnDefinition()); row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            row.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); row.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            var photo = new Image { Width = 40, Height = 40, VerticalAlignment = VerticalAlignment.Center };
            if (Uri.TryCreate(account.Photo, UriKind.Absolute, out var uri)) photo.Source = new BitmapImage(uri);
            AutomationProperties.SetName(photo, "Google profile photo"); row.Children.Add(photo);
            var details = new StackPanel(); details.Children.Add(new TextBlock { Text = account.Name, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold, TextWrapping = TextWrapping.Wrap });
            details.Children.Add(new TextBlock { Text = account.Email, Opacity = 0.7, TextWrapping = TextWrapping.Wrap }); details.Children.Add(new TextBlock { Text = "YouTube channel: " + account.ChannelName, TextWrapping = TextWrapping.Wrap });
            Grid.SetColumn(details, 1); row.Children.Add(details);
            var actions = new CompactActionsPanel { VerticalAlignment = VerticalAlignment.Center };
            var selected = YouTubeService.Shared.SelectedId == account.Id;
            var change = new Button { Content = selected ? "Selected" : "Switch Account", IsEnabled = !selected && operation == null }; change.Click += (_, _) => Switch(account); actions.Children.Add(change);
            var signOut = new Button { Content = "Sign Out", IsEnabled = operation == null }; signOut.Click += (_, _) => SignOut(account); actions.Children.Add(signOut);
            var disconnect = new Button { Content = "Disconnect Google Account…", IsEnabled = operation == null }; disconnect.Click += async (_, _) => await Disconnect(account); actions.Children.Add(disconnect);
            Grid.SetColumn(actions, 2); row.Children.Add(actions);
            var menu = MenuAccessKeys.Create();
            AddMenu(menu, "Switch Account", () => Switch(account), () => operation == null && YouTubeService.Shared.SelectedId != account.Id);
            AddMenu(menu, "Sign Out", () => SignOut(account), () => operation == null);
            AddMenu(menu, "Disconnect Google Account…", () => _ = Disconnect(account), () => operation == null); row.ContextFlyout = menu;
            void Arrange() {
                var required=40+24+180+actions.Children.Cast<FrameworkElement>().Sum(c => c.DesiredSize.Width)+16;
                var narrow = row.ActualWidth < required;
                Grid.SetColumn(actions, narrow ? 1 : 2); Grid.SetRow(actions, narrow ? 1 : 0); Grid.SetColumnSpan(actions, narrow ? 2 : 1);
            }
            row.SizeChanged += (_, _) => Arrange(); profiles.Children.Add(SettingsDialog.CompactCard(row));
        }
    }
}
