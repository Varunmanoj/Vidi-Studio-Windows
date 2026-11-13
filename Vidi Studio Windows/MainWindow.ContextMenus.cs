using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Documents;
using System;
using System.Linq;
using Vidi_Studio_Windows.Services;

namespace Vidi_Studio_Windows;

public sealed partial class MainWindow
{
    private void ContextMenuAccess_Opening(object sender, object args) => Views.MenuAccessKeys.Prepare(sender, args);
    private void ContextMenuAccess_Opened(object sender, object args) => Views.MenuAccessKeys.Display(sender, args);

    private MenuFlyout SummaryReadingMenu()
    {
        var menu = Vidi_Studio_Windows.Views.MenuAccessKeys.Create();
        foreach (var (id, title) in new[] { ("summary-from-start", "Read from Start"),
            ("summary-from-selection", "Read from Selection"), ("summary-read-selection", "Read Selected Text Only"),
            ("segment-edit-mode", "Edit Summary…"), ("summary-stop", "Stop") })
        {
            if (id is "segment-edit-mode" or "summary-stop") menu.Items.Add(new MenuFlyoutSeparator());
            var item = new MenuFlyoutItem { Text = title, Tag = id, Icon = DesktopCommandIcon(id) };
            menu.Opening += (_, _) => item.IsEnabled = Section.SelectedIndex == 1 && !summaryEditMode && CanRunDesktopCommand(id);
            item.Click += async (_, _) => {
                if (Section.SelectedIndex != 1 || summaryEditMode) return;
                try { await RunDesktopCommandAsync(id); }
                catch (Exception ex) { await ShowError(ex); }
            };
            menu.Items.Add(item);
        }
        return menu;
    }

    private void AttachSummaryTextMenu(TextBlock block, string plain)
    {
        block.ContextRequested += (_, args) => {
            if (Section.SelectedIndex != 1 || summaryEditMode || !summaryLines.Any(row => row.Block == block)) return;
            var pointer = args.TryGetPosition(block, out var point);
            var anchor = Math.Clamp(block.SelectionStart.Offset, 0, plain.Length);
            if (pointer) {
                // TextBlock has no GetPositionFromPoint. Resolve the closest native caret rectangle.
                var nearest = double.MaxValue;
                for (var index = 0; index < plain.Length; index++) {
                    var position = block.ContentStart.GetPositionAtOffset(index, LogicalDirection.Forward);
                    if (position == null) continue;
                    var rect = position.GetCharacterRect(LogicalDirection.Forward);
                    var dy = point.Y < rect.Top ? rect.Top - point.Y : point.Y > rect.Bottom ? point.Y - rect.Bottom : 0;
                    var dx = point.X - rect.Left;
                    var distance = dy * dy * 10000 + dx * dx;
                    if (distance < nearest) { nearest = distance; anchor = index; }
                }
                var start = block.SelectionStart.Offset;
                if (block.SelectedText.Length > 0 && anchor >= start && anchor <= start + block.SelectedText.Length) anchor = start;
            }
            var target = Model.Project;
            var selected = block.SelectedText;
            var indexInSummary = summaryLines.TakeWhile(row => row.Block != block).Sum(row => row.Text.Length + 1) + anchor;
            var text = string.Join("\n", summaryLines.Select(row => row.Text));
            var menu = Vidi_Studio_Windows.Views.MenuAccessKeys.Create();
            void Item(string id, string title, bool enabled, Action action) {
                var item = new MenuFlyoutItem { Text = title, Tag = id, Icon = DesktopCommandIcon(id), IsEnabled = enabled };
                item.Click += (_, _) => {
                    if (Model.Project != target || Section.SelectedIndex != 1 || summaryEditMode || !summaryLines.Any(row => row.Block == block)) return;
                    action();
                };
                menu.Items.Add(item);
            }
            async void Read(string value) {
                if (!CanRunDesktopCommand("summary-from-start")) return;
                try { await readback.SpeakAsync(value); } catch (Exception ex) { await ShowError(ex); }
            }
            Item("summary-from-selection", "Read Summary from Here", CanRunDesktopCommand("summary-from-start"), () => Read(text[Math.Clamp(indexInSummary, 0, text.Length)..]));
            Item("summary-read-selection", "Read Selected Text", selected.Length > 0 && CanRunDesktopCommand("summary-from-start"), () => Read(selected));
            menu.Items.Add(new MenuFlyoutSeparator());
            var playing = readback.State == SummaryReadbackState.Playing;
            Item(playing ? "summary-pause" : "summary-play", playing ? "Pause Summary Reading" : "Resume Summary Reading",
                readback.State is SummaryReadbackState.Playing or SummaryReadbackState.Paused,
                async () => await RunDesktopCommandAsync(playing ? "summary-pause" : "summary-play"));
            Item("summary-stop", "Stop Summary Reading", CanRunDesktopCommand("summary-stop"), async () => await RunDesktopCommandAsync("summary-stop"));
            menu.Items.Add(new MenuFlyoutSeparator());
            Item("copy", "Copy", selected.Length > 0, () => { var data = new Windows.ApplicationModel.DataTransfer.DataPackage(); data.SetText(selected); Windows.ApplicationModel.DataTransfer.Clipboard.SetContent(data); });
            Item("select-all", "Select All", plain.Length > 0, block.SelectAll);
            if (pointer) menu.ShowAt(block, point); else menu.ShowAt(block);
            args.Handled = true;
        };
    }
}
