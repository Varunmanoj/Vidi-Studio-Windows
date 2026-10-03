using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using System.Collections.ObjectModel;
using Vidi_Studio_Windows.Services;

namespace Vidi_Studio_Windows.Views;

public sealed partial class ExportProgressView : UserControl
{
    public ObservableCollection<ExportSession> Active => ExportSession.Active;
    public int SessionCount => Sessions.Items.Count;
    public ExportProgressView() => InitializeComponent();
    private void CancelExport_Click(object sender, RoutedEventArgs e)
    { if ((sender as FrameworkElement)?.Tag is ExportSession session) session.Cancel(); }
}
