using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using System;
using System.Linq;
using Vidi_Studio_Windows.Models;
using Vidi_Studio_Windows.Services;
using Vidi_Studio_Windows.ViewModels;

namespace Vidi_Studio_Windows.Views;

public sealed partial class SettingsDialog
{
    private ToggleSwitch? projectNarrationToggle;
    private ToggleSwitch? projectFollowToggle;
    private bool applyingProjectPlaybackPreference;
#if DEBUG
    private bool projectsPageEmpty;
#endif

    /// <summary>
    /// Mirrors macOS Settings → Projects: the section always exists, but it lists the
    /// playback toggles only while a project is open and shows an empty state otherwise.
    /// </summary>
    private void BuildProjects(StackPanel panel)
    {
        projectNarrationToggle = null;
        projectFollowToggle = null;
        if (model.Project is not { } project || string.IsNullOrWhiteSpace(project.Id))
        {
#if DEBUG
            projectsPageEmpty = true;
#endif
            panel.Children.Add(new TextBlock
            {
                Text = "Open projects",
                FontSize = 20,
                FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                TextWrapping = TextWrapping.Wrap
            });
            panel.Children.Add(EmptyProjectsCard());
            return;
        }

#if DEBUG
        projectsPageEmpty = false;
#endif
        panel.Children.Add(new TextBlock
        {
            Text = project.Name,
            FontSize = 20,
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
            TextWrapping = TextWrapping.Wrap
        });
        var rows = new StackPanel { Spacing = 0 };
        var card = new Border
        {
            Style = (Style)Application.Current.Resources["SettingsProjectCardStyle"],
            Padding = new Thickness(14, 8, 14, 8),
            Child = rows
        };
        projectNarrationToggle = AddProjectToggle(rows,
            "Narration during playback",
            "Turns synchronized narration on or off for " + project.Name + " and remembers the choice for this project",
            ProjectPlaybackPreferences.NarrationEnabled(model.Settings, project),
            enabled => SetProjectNarration(project, enabled));
        projectFollowToggle = AddProjectToggle(rows,
            "Follow synchronized transcript",
            "Follows the active transcript or description while " + project.Name + " plays",
            ProjectPlaybackPreferences.FollowEnabled(model.Settings, project),
            enabled => SetProjectFollow(project, enabled));
        panel.Children.Add(card);
    }

    private ToggleSwitch? AddProjectToggle(StackPanel panel, string label, string hint, bool isOn, Action<bool> changed)
    {
        var row = new Grid { ColumnSpacing = 16, Padding = new Thickness(0, 10, 0, 10) };
        row.ColumnDefinitions.Add(new ColumnDefinition());
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        row.Children.Add(new TextBlock
        {
            Text = label,
            TextWrapping = TextWrapping.Wrap,
            VerticalAlignment = VerticalAlignment.Center
        });
        var toggle = new ToggleSwitch
        {
            IsOn = isOn,
            OnContent = "",
            OffContent = "",
            MinWidth = 0,
            VerticalAlignment = VerticalAlignment.Center
        };
        AutomationProperties.SetName(toggle, label);
        AutomationProperties.SetHelpText(toggle, hint);
        ToolTipService.SetToolTip(toggle, hint);
        toggle.Toggled += (_, _) =>
        {
            if (applyingProjectPlaybackPreference) return;
            changed(toggle.IsOn);
        };
        Grid.SetColumn(toggle, 1);
        row.Children.Add(toggle);
        panel.Children.Add(row);
        return toggle;
    }

    private void SetProjectNarration(StudioProject project, bool enabled)
    {
        ProjectPlaybackPreferences.SetNarrationEnabled(model.Settings, project, enabled);
        status.Text = enabled
            ? "Narration will play during this project's video playback."
            : "Narration is off during this project's video playback.";
    }

    private void SetProjectFollow(StudioProject project, bool enabled)
    {
        ProjectPlaybackPreferences.SetFollowEnabled(model.Settings, project, enabled);
        status.Text = enabled
            ? "The active transcript or description will follow playback in this project."
            : "Playback will not follow the active transcript or description in this project.";
    }

    /// <summary>
    /// Opening or closing a project while Settings is visible keeps the Projects page in step
    /// with the editor instead of leaving a stale list or empty state on screen.
    /// </summary>
    private void ReceiveModelProjectChange(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(StudioViewModel.Project) || closed || sectionList?.SelectedIndex != 1) return;
        DispatcherQueue.TryEnqueue(() =>
        {
            if (closed || sectionList?.SelectedIndex != 1 || currentSectionPanel is not { } panel) return;
            panel.Children.Clear();
            BuildProjects(panel);
        });
    }

    private void ReceiveProjectPlaybackPreference(ProjectPlaybackPreferenceChange change)
    {
        if (closed || model.Project is not { } project || project.Id != change.ProjectId) return;
        DispatcherQueue.TryEnqueue(() =>
        {
            if (closed || model.Project is not { } current || current.Id != change.ProjectId) return;
            applyingProjectPlaybackPreference = true;
            try
            {
                if (change.Preference == ProjectPlaybackPreference.Narration && projectNarrationToggle != null)
                    projectNarrationToggle.IsOn = change.Enabled;
                else if (change.Preference == ProjectPlaybackPreference.Follow && projectFollowToggle != null)
                    projectFollowToggle.IsOn = change.Enabled;
            }
            finally { applyingProjectPlaybackPreference = false; }
        });
    }

    private static Border EmptyProjectsCard()
    {
        var content = new StackPanel
        {
            Spacing = 10,
            Padding = new Thickness(24, 20, 24, 20),
            HorizontalAlignment = HorizontalAlignment.Center
        };
        content.Children.Add(new FontIcon
        {
            Glyph = "\uE8F1",
            FontSize = 34,
            Opacity = .65,
            HorizontalAlignment = HorizontalAlignment.Center
        });
        content.Children.Add(new TextBlock
        {
            Text = "No Open Projects",
            FontSize = 18,
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
            HorizontalAlignment = HorizontalAlignment.Center,
            TextAlignment = TextAlignment.Center
        });
        content.Children.Add(new TextBlock
        {
            Text = "Open a project to manage its project-specific playback settings.",
            TextWrapping = TextWrapping.Wrap,
            TextAlignment = TextAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Center,
            Opacity = .72,
            MaxWidth = 420
        });
        return new Border
        {
            Style = (Style)Application.Current.Resources["SettingsProjectCardStyle"],
            Padding = new Thickness(12),
            Child = content
        };
    }

#if DEBUG
    internal void VerifyEmptyProjectsSettingsForSmokeTest()
    {
        NavigateForSmokeTest(1);
        if (!projectsPageEmpty || projectNarrationToggle != null || projectFollowToggle != null)
            throw new InvalidOperationException("Projects settings did not show the No Open Projects empty state.");
        var card = currentSectionPanel!.Children.OfType<Border>().Single();
        var fill = ((SolidColorBrush)card.Background).Color;
        var surface = ((SolidColorBrush)layout.Background).Color;
        static double Blend(double foreground, double background, double opacity) => foreground * opacity + background * (1 - opacity);
        var red = Blend(fill.R, surface.R, fill.A / 255d);
        var green = Blend(fill.G, surface.G, fill.A / 255d);
        var blue = Blend(fill.B, surface.B, fill.A / 255d);
        if (!new Windows.UI.ViewManagement.AccessibilitySettings().HighContrast && (layout.ActualTheme == ElementTheme.Dark ? red + green + blue > 384 : red + green + blue < 540))
            throw new InvalidOperationException("The empty Projects card did not follow the Settings window theme.");
        static double Linear(double channel) => channel / 255d <= .04045 ? channel / 255d / 12.92 : Math.Pow((channel / 255d + .055) / 1.055, 2.4);
        static double Luminance(double r, double g, double b) => .2126 * Linear(r) + .7152 * Linear(g) + .0722 * Linear(b);
        var background = Luminance(red, green, blue);
        foreach (var text in ((StackPanel)card.Child).Children.OfType<TextBlock>())
        {
            var foreground = ((SolidColorBrush)text.Foreground).Color;
            var opacity = foreground.A / 255d * text.Opacity;
            var luminance = Luminance(Blend(foreground.R, red, opacity), Blend(foreground.G, green, opacity), Blend(foreground.B, blue, opacity));
            if ((Math.Max(background, luminance) + .05) / (Math.Min(background, luminance) + .05) < 4.5)
                throw new InvalidOperationException("The empty Projects message is unreadable in the current theme.");
        }
    }
    internal void VerifyProjectPlaybackSettingsForSmokeTest()
    {
        NavigateForSmokeTest(1);
        if (model.Project is not { } project)
            throw new InvalidOperationException("The Projects settings check needs an open project.");
        if (projectsPageEmpty || projectNarrationToggle == null || projectFollowToggle == null)
            throw new InvalidOperationException("Projects settings did not list the open project's playback toggles.");
        var originalNarration = projectNarrationToggle.IsOn;
        var originalFollow = projectFollowToggle.IsOn;
        try
        {
            var expectedNarration = !originalNarration;
            projectNarrationToggle.IsOn = expectedNarration;
            if (ProjectPlaybackPreferences.NarrationEnabled(model.Settings, project) != expectedNarration)
                throw new InvalidOperationException("Narration during playback was not saved in the project.");
            var expectedFollow = !originalFollow;
            projectFollowToggle.IsOn = expectedFollow;
            if (ProjectPlaybackPreferences.FollowEnabled(model.Settings, project) != expectedFollow)
                throw new InvalidOperationException("Follow synchronized transcript was not saved in the project.");
        }
        finally
        {
            projectNarrationToggle.IsOn = originalNarration;
            projectFollowToggle.IsOn = originalFollow;
        }
        if (ProjectPlaybackPreferences.NarrationEnabled(model.Settings, project) != originalNarration ||
            ProjectPlaybackPreferences.FollowEnabled(model.Settings, project) != originalFollow)
            throw new InvalidOperationException("Restoring the project playback toggles did not save the restored choices.");
    }
#endif
}
