using System.Collections.Generic;
using System.Linq;

namespace Vidi_Studio_Windows;

public sealed partial class MainWindow
{
    private string segmentSort = "start";

    private IEnumerable<Models.Segment> ApplySegmentSort(IEnumerable<Models.Segment> rows) => segmentSort switch
    {
        "durationAscending" => rows.OrderBy(segment => segment.End - segment.Start).ThenBy(segment => segment.Start),
        "durationDescending" => rows.OrderByDescending(segment => segment.End - segment.Start).ThenBy(segment => segment.Start),
        "origin" => rows.OrderBy(segment => OriginOrder(segment.Origin)).ThenBy(segment => segment.Start),
        _ => rows.OrderBy(segment => segment.Start).ThenBy(segment => segment.Id)
    };

    private void SetSegmentSort(string sort)
    {
        segmentSort = sort;
        Segments.SelectedItems.Clear();
        RenderSection();
        UpdateBulkActionState();
        UpdateDesktopMenuStates();
    }

    private void SortStart_Click(object sender, Microsoft.UI.Xaml.RoutedEventArgs e) => SetSegmentSort("start");
    private void SortDurationAsc_Click(object sender, Microsoft.UI.Xaml.RoutedEventArgs e) => SetSegmentSort("durationAscending");
    private void SortDurationDesc_Click(object sender, Microsoft.UI.Xaml.RoutedEventArgs e) => SetSegmentSort("durationDescending");
    private void SortOrigin_Click(object sender, Microsoft.UI.Xaml.RoutedEventArgs e) => SetSegmentSort("origin");
}
