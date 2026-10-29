using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using System.Threading.Tasks;

namespace Vidi_Studio_Windows;

public sealed partial class MainWindow
{
    private void AddOrImport_Click(object sender, RoutedEventArgs e)
    {
        if (Model.Busy || sender is not FrameworkElement anchor) return;
        var menu = new MenuFlyout();
        void Item(string label, Func<Task> action, bool enabled = true)
        {
            var item = new MenuFlyoutItem { Text = label, IsEnabled = enabled };
            Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(item, label);
            item.Click += async (_, _) => await action();
            menu.Items.Add(item);
        }
        switch (Section.SelectedIndex)
        {
            case 0:
                Item("Add Transcript Segment", () => EditSegment(null));
                menu.Items.Add(new MenuFlyoutSeparator());
                Item("Import Transcript…", () => ImportExistingAssetsAsync(Models.ImportScope.Transcript));
                break;
            case 1:
                Item("Write Summary", () => { SetSummaryEditMode(true); return Task.CompletedTask; }, !summaryEditMode);
                Item("Import Summary…", () => ImportExistingAssetsAsync(Models.ImportScope.Summary));
                break;
            case 2:
                Item("Add Audio Description Segment", () => EditSegment(null));
                menu.Items.Add(new MenuFlyoutSeparator());
                Item("Import Descriptions…", () => ImportExistingAssetsAsync(Models.ImportScope.Descriptions));
                break;
            case 4:
                Item("Import Descriptions…", () => ImportExistingAssetsAsync(Models.ImportScope.Descriptions));
                break;
            default:
                return;
        }
        menu.ShowAt(anchor);
    }
}
