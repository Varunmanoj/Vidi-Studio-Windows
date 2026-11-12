using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Controls;
using System;
using System.Linq;
using System.Threading.Tasks;

namespace Vidi_Studio_Windows;

public sealed partial class MainWindow
{
#if DEBUG
    private async Task VerifyAccessibleMediaForSmokeTestAsync()
    {
        var project = Model.Project ?? throw new InvalidOperationException("Accessibility fixture missing.");
        ShowGallery_Click(this, new RoutedEventArgs());
        foreach (var grid in new[] { true, false })
        {
            if ((GalleryGrid.Visibility == Visibility.Visible) != grid) GalleryLayout_Click(this, new RoutedEventArgs());
            ListViewBase list = grid ? GalleryGrid : GalleryList;
            var listed = Model.Projects.First(item => string.Equals(item.Folder, project.Folder, StringComparison.OrdinalIgnoreCase));
            list.ScrollIntoView(listed); await Task.Delay(150); Root.UpdateLayout();
            if (list.ContainerFromItem(listed) is not FrameworkElement container ||
                FrameworkElementAutomationPeer.CreatePeerForElement(container)?.GetName() != listed.ProjectAnnouncement ||
                !project.ProjectAnnouncement.Contains("Duration: " + Models.NarrationTimeFormat.SpokenProjectDuration(project.Duration)))
                throw new InvalidOperationException("Gallery item has no spoken media duration.");
        }
        ShowEditor(); await Task.Delay(250);
        if (AutomationProperties.GetHeadingLevel(ProjectTitle) != AutomationHeadingLevel.Level1 ||
            AutomationProperties.GetHeadingLevel(EditorSectionHeading) != AutomationHeadingLevel.Level2 ||
            AutomationProperties.GetHeadingLevel(InspectorSectionTitle) != AutomationHeadingLevel.Level2)
            throw new InvalidOperationException("Editor/inspector heading levels are missing.");
        var duration = Models.NarrationTimeFormat.SpokenProjectDuration(project.Duration);
        if (!AutomationProperties.GetName(TimeLabel).Contains(duration) || !AutomationProperties.GetItemStatus(Playhead).Contains(duration))
            throw new InvalidOperationException("Timeline has no spoken media time/duration.");
        bool Visible(FrameworkElement element)
        {
            for (DependencyObject? node = element; node != null; node = Microsoft.UI.Xaml.Media.VisualTreeHelper.GetParent(node))
                if (node is UIElement { Visibility: Visibility.Collapsed }) return false;
            return element.XamlRoot != null;
        }
        string PeerName(FrameworkElement element) => FrameworkElementAutomationPeer.CreatePeerForElement(element)?.GetName() ?? "";
        if (PeerName(InspectorProjectInformationHeading) != "Project Information" ||
            AutomationProperties.GetHeadingLevel(InspectorProjectInformationHeading) != AutomationHeadingLevel.Level2 ||
            PeerName(InspectorDeleteContentButton) != "Choose content to delete" ||
            PeerName(Follow) != "Follow active segment")
            throw new InvalidOperationException($"Inspector names: heading=[{PeerName(InspectorProjectInformationHeading)}] level={AutomationProperties.GetHeadingLevel(InspectorProjectInformationHeading)}, delete=[{PeerName(InspectorDeleteContentButton)}], narration=[{PeerName(Narration)}], follow=[{PeerName(Follow)}].");
        var previousSection = Section.SelectedIndex;
        try
        {
            foreach (var section in new[] { 0, 2, 4 })
            {
                Section.SelectedIndex = section; RenderSection(); await Task.Delay(200); Root.UpdateLayout();
                if (Narration.Visibility == Visibility.Visible && PeerName(Narration) != "Narration")
                    throw new InvalidOperationException("Visible narration toggle has no accessible name.");
                var rows = ((System.Collections.Generic.IEnumerable<Models.Segment>)Segments.ItemsSource).ToArray();
                if (rows.Length == 0) throw new InvalidOperationException("Timing accessibility fixture has no rows.");
                foreach (var row in rows.Take(2))
                {
                    Segments.ScrollIntoView(row); await Task.Delay(100); Root.UpdateLayout();
                    if (Segments.ContainerFromItem(row) is not FrameworkElement container || PeerName(container) != row.SegmentAnnouncement ||
                        !PeerName(container).Contains("Start time: ") || !PeerName(container).Contains("End time: "))
                        throw new InvalidOperationException("Segment row does not announce both named time boundaries.");
                }
                foreach (var button in new Button[] { InspectorGenerate, InspectorRewriteButton, InspectorSynthesizeNarrationButton,
                    InspectorSynthesizeChangedButton, InspectorDeleteNarrationButton, InspectorDeleteContentButton, InspectorExportButton })
                    if (Visible(button) && string.IsNullOrWhiteSpace(PeerName(button))) throw new InvalidOperationException("Inspector action has no accessible name: " + button.Name);
                if (Visible(InspectorSynthesizeNarrationButton) && PeerName(InspectorSynthesizeNarrationButton) != InspectorSynthesizeNarrationLabel.Text)
                    throw new InvalidOperationException("Narration action's accessible name is stale.");
            }
        }
        finally { Section.SelectedIndex = previousSection; RenderSection(); }
        var iconButton = new Button();
        Views.IconButtonContent.Set(iconButton, "Preview sound", Symbol.Play);
        Root.Children.Add(iconButton);
        try { Root.UpdateLayout(); if (PeerName(iconButton) != "Preview sound") throw new InvalidOperationException("Composite icon button has no accessible name."); }
        finally { Root.Children.Remove(iconButton); }
        ShowGallery_Click(this, new RoutedEventArgs());
    }
#endif
}
