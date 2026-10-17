using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using System;
using System.Collections.Specialized;
using System.ComponentModel;
using Vidi_Studio_Windows.Services;
using Vidi_Studio_Windows.ViewModels;

namespace Vidi_Studio_Windows.Views;

public sealed partial class ProgressOverlay : UserControl
{
    private StudioViewModel? model;
    private bool observing;
    public int SessionCount => Exports.SessionCount;
    public ProgressOverlay()
    {
        InitializeComponent();
        Loaded += (_, _) => { Attach(); Update(); };
        Unloaded += (_, _) => Detach();
        SizeChanged += (_, _) => ProgressScroll.MaxHeight = Math.Max(0, ActualHeight - 100);
        ProgressCard.Translation = new System.Numerics.Vector3(0, 0, 24);
    }
    public void Observe(StudioViewModel viewModel)
    {
        Detach(); model = viewModel; Attach(); Update();
    }
    private void Attach()
    {
        if (observing || model == null) return;
        model.PropertyChanged += ModelChanged;
        ExportSession.Active.CollectionChanged += ExportsChanged;
        observing = true;
    }
    public void Detach()
    {
        if (!observing) return;
        model!.PropertyChanged -= ModelChanged;
        ExportSession.Active.CollectionChanged -= ExportsChanged;
        observing = false;
    }
    private void ModelChanged(object? sender, PropertyChangedEventArgs e)
    { if (e.PropertyName is nameof(StudioViewModel.Busy) or nameof(StudioViewModel.Status)) Update(); }
    private void ExportsChanged(object? sender, NotifyCollectionChangedEventArgs e) => Update();
    private void Update()
    {
        // Analysis stages use inline per-section spinners and the status bar
        // rather than this centered card; exports and other processing keep it.
        var processing = model?.Busy == true && !model.IsExporting && model?.IsAnalyzing != true;
        var exporting = ExportSession.Active.Count > 0;
        Visibility = processing || exporting ? Visibility.Visible : Visibility.Collapsed;
        Heading.Text = processing && exporting ? "Processing and exports" : exporting ? "Export progress" : "Processing";
        ProcessingPanel.Visibility = processing ? Visibility.Visible : Visibility.Collapsed;
        ProcessingRing.IsActive = processing;
        ProcessingStatus.Text = model?.Status ?? "";
        Exports.Visibility = exporting ? Visibility.Visible : Visibility.Collapsed;
    }
    private void CancelProcessing_Click(object sender, RoutedEventArgs e) => model?.Cancel();
#if DEBUG
    internal void VerifyCenteredForSmokeTest()
    {
        UpdateLayout();
        var position = ProgressCard.TransformToVisual(this).TransformPoint(new Windows.Foundation.Point());
        if (Visibility != Visibility.Visible || ProgressCard.ActualWidth < 200 ||
            Math.Abs(position.X + ProgressCard.ActualWidth / 2 - ActualWidth / 2) > 2 ||
            Math.Abs(position.Y + ProgressCard.ActualHeight / 2 - ActualHeight / 2) > 2)
            throw new InvalidOperationException("Progress card is not visible in the center of its window.");
    }
#endif
}
