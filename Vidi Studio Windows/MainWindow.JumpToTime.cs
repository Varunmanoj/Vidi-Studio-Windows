using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using System;
using System.Threading.Tasks;
using Vidi_Studio_Windows.Models;

namespace Vidi_Studio_Windows;

public sealed partial class MainWindow
{
    private async Task ShowJumpToTimeAsync()
    {
        if (!CanRunDesktopCommand("jump-to-time") || Model.Project is not { } project) return;
        var input = new TextBox { Header = "Time", Text = JumpTime.Format(Position), PlaceholderText = "hh:mm:ss.sss" };
        AutomationProperties.SetAutomationId(input, "jump-time-input");
        AutomationProperties.SetName(input, "Time");
        var error = new TextBlock { Text = $"Enter a time from 0 to {JumpTime.Format(project.Duration)}.", TextWrapping = TextWrapping.Wrap };
        var content = new StackPanel { Spacing = 16 };
        content.Children.Add(input); content.Children.Add(error);
        var dialog = new ContentDialog { Title = "Jump to Time", Content = content,
            PrimaryButtonText = "Jump", CloseButtonText = "Cancel", DefaultButton = ContentDialogButton.Primary };
        var menu = new MenuFlyout();
        var jump = new MenuFlyoutItem { Text = "Jump" };
        var cancel = new MenuFlyoutItem { Text = "Cancel" };
        // The dialog buttons and its context menu use the same validated submit path.
        bool submitted = false;
        bool Submit()
        {
            if (!ReferenceEquals(Model.Project, project) || !Model.Idle || JumpTime.Parse(input.Text, project.Duration) == null) return false;
            submitted = true;
            return true;
        }
        void Validate()
        {
            var valid = JumpTime.Parse(input.Text, project.Duration) != null;
            dialog.IsPrimaryButtonEnabled = valid; jump.IsEnabled = valid;
            error.Visibility = valid ? Visibility.Collapsed : Visibility.Visible;
        }
        input.TextChanged += (_, _) => Validate();
        dialog.PrimaryButtonClick += (_, args) => args.Cancel = !Submit();
        jump.Click += (_, _) => { if (Submit()) dialog.Hide(); };
        cancel.Click += (_, _) => dialog.Hide();
        menu.Items.Add(jump); menu.Items.Add(cancel); content.ContextFlyout = menu;
        dialog.Opened += (_, _) => { input.Focus(FocusState.Programmatic); input.SelectAll(); };
        Validate();
        await ShowDialogAsync(dialog, 460, 300);
        if (submitted && ReferenceEquals(Model.Project, project) && Model.Idle && JumpTime.Parse(input.Text, project.Duration) is { } target)
            await JumpToTimeAsync(target);
    }

    private async Task JumpToTimeAsync(double target)
    {
        var project = Model.Project;
        if (project == null) return;
        var wasPlaying = IsVideoPlaying();
        ClearSegmentPreview();
        await SeekAsync(target);
        if (!ReferenceEquals(Model.Project, project)) return;
        if (Model.Project?.IsYouTube == true && YouTubePlayer.CoreWebView2 != null)
        {
            await YouTubePlayer.ExecuteScriptAsync(wasPlaying ? "window.vidiPlay?.()" : "window.vidiPause?.()");
            youtubePlaying = wasPlaying;
        }
        else if (wasPlaying) video.Play();
        else video.Pause();
        UpdatePlaybackControls();
    }
}
