using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Automation.Peers;
using System;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Runtime.InteropServices;
using Vidi_Studio_Windows.Services;
using Vidi_Studio_Windows.ViewModels;

namespace Vidi_Studio_Windows.Views;

public sealed partial class ProgressOverlay : UserControl
{
    private StudioViewModel? model;
    private Window? owner;
    private Func<bool>? canAnnounce;
    private bool observing;
    private bool announcementQueued;
    private string? lastCopyAnnouncement;
    public int SessionCount => Exports.SessionCount;
    internal bool IsShowingProcessing => Visibility == Visibility.Visible && ProcessingPanel.Visibility == Visibility.Visible;
    public ProgressOverlay()
    {
        InitializeComponent();
        Loaded += (_, _) => { Attach(); Update(); };
        Unloaded += (_, _) => Detach();
        SizeChanged += (_, _) => ProgressScroll.MaxHeight = Math.Max(0, ActualHeight - 100);
        ProgressCard.Translation = new System.Numerics.Vector3(0, 0, 24);
    }
    public void Observe(StudioViewModel viewModel, Window window, Func<bool>? canAnnounce = null)
    {
        Detach();
        model = viewModel;
        owner = window;
        this.canAnnounce = canAnnounce;
        Attach();
        Update();
    }
    private void Attach()
    {
        if (observing || model == null) return;
        model.PropertyChanged += ModelChanged;
        if (owner != null) owner.Activated += OwnerActivated;
        ExportSession.Active.CollectionChanged += ExportsChanged;
        observing = true;
    }
    public void Detach()
    {
        if (!observing) return;
        model!.PropertyChanged -= ModelChanged;
        if (owner != null) owner.Activated -= OwnerActivated;
        ExportSession.Active.CollectionChanged -= ExportsChanged;
        observing = false;
    }
    private void ModelChanged(object? sender, PropertyChangedEventArgs e)
    { if (e.PropertyName is nameof(StudioViewModel.Busy) or nameof(StudioViewModel.Status)) Update(); }
    private void ExportsChanged(object? sender, NotifyCollectionChangedEventArgs e) => Update();
    private void OwnerActivated(object sender, WindowActivatedEventArgs args)
    {
        if (args.WindowActivationState != WindowActivationState.Deactivated) Update();
    }
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
        var fraction = model?.AICopyProgress;
        CopyProgressPanel.Visibility = processing && fraction.HasValue ? Visibility.Visible : Visibility.Collapsed;
        CopyProgress.Value = (fraction ?? 0) * 100;
        CopyPercent.Text = fraction?.ToString("P0") ?? "";
        if (processing && fraction is { } percent && owner is { } window &&
            GetForegroundWindow() == WinRT.Interop.WindowNative.GetWindowHandle(window))
        {
            var announcement = $"{ProcessingStatus.Text}, {(int)(percent * 10) * 10} percent";
            if (lastCopyAnnouncement != announcement && FrameworkElementAutomationPeer.CreatePeerForElement(ProcessingStatus) is { } peer)
            {
                lastCopyAnnouncement = announcement;
                peer.RaiseNotificationEvent(AutomationNotificationKind.Other, AutomationNotificationProcessing.MostRecent, announcement, "smaller-ai-copy");
            }
        }
        if (!processing) lastCopyAnnouncement = null;
        QueueProcessingAnnouncement();
    }

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    private void QueueProcessingAnnouncement()
    {
        if (announcementQueued || !observing || !IsLoaded || !IsShowingProcessing ||
            model?.HasAnnouncedProcessing != false || model.AICopyProgress != null || canAnnounce?.Invoke() == false) return;

        // Busy and text can change in the same dispatcher turn. Post after the card and
        // its text are visible, and let only the foreground surface announce this operation.
        announcementQueued = DispatcherQueue.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Low, () =>
        {
            announcementQueued = false;
            if (!observing || !IsLoaded || !IsShowingProcessing || owner is not { } window ||
                GetForegroundWindow() != WinRT.Interop.WindowNative.GetWindowHandle(window) ||
                canAnnounce?.Invoke() == false || model?.HasAnnouncedProcessing != false) return;
            if (FrameworkElementAutomationPeer.CreatePeerForElement(ProcessingStatus) is not { } peer) return;
            model.HasAnnouncedProcessing = true;
            var message = string.IsNullOrWhiteSpace(ProcessingStatus.Text) ? "Processing." : "Processing. " + ProcessingStatus.Text;
            peer.RaiseNotificationEvent(AutomationNotificationKind.Other,
                AutomationNotificationProcessing.ImportantMostRecent, message, "processing-start");
        });
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
