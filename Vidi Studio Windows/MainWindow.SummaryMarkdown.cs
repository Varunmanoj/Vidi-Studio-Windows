using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using System;
using System.Linq;
using Vidi_Studio_Windows.Models;
using Windows.System;

namespace Vidi_Studio_Windows;

public sealed partial class MainWindow
{
    private void SummaryUndo_Click(object sender, RoutedEventArgs e)
    {
        SummaryEditorBox.Undo();
        SummaryEditorBox.Focus(FocusState.Programmatic);
    }

    private void SummaryRedo_Click(object sender, RoutedEventArgs e)
    {
        SummaryEditorBox.Redo();
        SummaryEditorBox.Focus(FocusState.Programmatic);
    }

    private void SummaryMarkdown_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: string command }) ApplySummaryMarkdown(command);
    }

    private void SummaryHeading_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (!summaryEditMode) return;
        var command = SummaryHeadingPicker.SelectedIndex switch
        {
            1 => "heading1", 2 => "heading2", 3 => "heading3",
            4 => "heading4", 5 => "heading5", 6 => "heading6", _ => "paragraph"
        };
        ApplySummaryMarkdown(command);
    }

    private void ApplySummaryMarkdown(string command)
    {
        var (text, start, length) = MarkdownCommands.Apply(
            SummaryEditorBox.Text ?? "", SummaryEditorBox.SelectionStart, SummaryEditorBox.SelectionLength, command);
        SummaryEditorBox.Text = text;
        SummaryEditorBox.SelectionStart = Math.Clamp(start, 0, text.Length);
        SummaryEditorBox.SelectionLength = Math.Clamp(length, 0, text.Length - SummaryEditorBox.SelectionStart);
        SummaryEditorBox.Focus(FocusState.Programmatic);
    }

    private void SummaryEditorBox_KeyDown(object sender, KeyRoutedEventArgs e)
    {
        var control = Microsoft.UI.Input.InputKeyboardSource.GetKeyStateForCurrentThread(VirtualKey.Control);
        var shift = Microsoft.UI.Input.InputKeyboardSource.GetKeyStateForCurrentThread(VirtualKey.Shift);
        var menu = Microsoft.UI.Input.InputKeyboardSource.GetKeyStateForCurrentThread(VirtualKey.Menu);
        var controlDown = control.HasFlag(Windows.UI.Core.CoreVirtualKeyStates.Down);
        var shiftDown = shift.HasFlag(Windows.UI.Core.CoreVirtualKeyStates.Down);
        var altDown = menu.HasFlag(Windows.UI.Core.CoreVirtualKeyStates.Down);
        if (controlDown)
        {
            string? command = e.Key switch
            {
                VirtualKey.B when !shiftDown && !altDown => "bold",
                VirtualKey.I when !shiftDown && !altDown => "italic",
                VirtualKey.I when shiftDown => "image",
                VirtualKey.X when shiftDown => "strikethrough",
                VirtualKey.K when !shiftDown && !altDown => "link",
                VirtualKey.K when altDown && shiftDown => "clearFormatting",
                VirtualKey.C when altDown && shiftDown => "codeBlock",
                VirtualKey.Q when altDown && shiftDown => "blockquote",
                VirtualKey.T when altDown => "table",
                VirtualKey.Number8 when shiftDown => "bulletedList",
                VirtualKey.Number7 when shiftDown => "numberedList",
                VirtualKey.Number9 when shiftDown => "taskList",
                VirtualKey.Number1 when altDown => "heading1",
                VirtualKey.Number2 when altDown => "heading2",
                VirtualKey.Number3 when altDown => "heading3",
                VirtualKey.Number4 when altDown => "heading4",
                VirtualKey.Number5 when altDown => "heading5",
                VirtualKey.Number6 when altDown => "heading6",
                VirtualKey.Number0 when altDown => "paragraph",
                VirtualKey.P when altDown && shiftDown => "preview",
                VirtualKey.M when altDown && shiftDown => "help",
                _ => null
            };
            if (command == "preview")
            {
                SummaryPreview_Click(this, new RoutedEventArgs());
                e.Handled = true;
                return;
            }
            if (command == "help")
            {
                SummaryMarkdownHelp_Click(this, new RoutedEventArgs());
                e.Handled = true;
                return;
            }
            if (command != null)
            {
                ApplySummaryMarkdown(command);
                e.Handled = true;
                return;
            }
        }
        if (e.Key != VirtualKey.Enter) return;
        var text = SummaryEditorBox.Text ?? "";
        var caret = SummaryEditorBox.SelectionStart;
        var lineStart = text.LastIndexOf('\n', Math.Max(0, caret - 1)) + 1;
        var lineEnd = text.IndexOf('\n', Math.Min(caret, text.Length));
        if (lineEnd < 0) lineEnd = text.Length;
        var line = text.Substring(lineStart, lineEnd - lineStart).TrimEnd('\r');
        if (MarkdownCommands.ListContinuation(line) is not { } prefix) return;
        if (line.Trim() == prefix.Trim())
        {
            SummaryEditorBox.Text = text.Remove(lineStart, lineEnd - lineStart);
            SummaryEditorBox.SelectionStart = lineStart;
        }
        else
        {
            SummaryEditorBox.Text = text.Insert(caret, "\n" + prefix);
            SummaryEditorBox.SelectionStart = caret + 1 + prefix.Length;
        }
        e.Handled = true;
    }

    private async void SummaryMarkdownHelp_Click(object sender, RoutedEventArgs e)
    {
        var text = new TextBlock
        {
            TextWrapping = TextWrapping.Wrap,
            IsTextSelectionEnabled = true,
            Text = "Markdown Help\n\n" +
                   "Bold: **text** (Ctrl-B)\nItalic: *text* (Ctrl-I)\nStrikethrough: ~~text~~ (Ctrl-Shift-X)\n" +
                   "Inline code: `code` (Ctrl-Alt-`)\nCode block: ``` on its own lines (Ctrl-Alt-Shift-C)\n" +
                   "Link: [text](url) (Ctrl-K)\nImage: ![alt](url) (Ctrl-Shift-I)\n" +
                   "Headings: # through ###### (Ctrl-Alt-1–6)\nParagraph: Ctrl-Alt-0\n" +
                   "Bulleted list: - item (Ctrl-Shift-8)\nNumbered list: 1. item (Ctrl-Shift-7)\n" +
                   "Task list: - [ ] item (Ctrl-Shift-9)\nBlockquote: > text (Ctrl-Alt-Shift-Q)\n" +
                   "Table: Ctrl-Alt-T\nHorizontal rule: --- (Ctrl-Alt--)\nClear formatting: Ctrl-Alt-Shift-K\n" +
                   "Preview: Ctrl-Alt-Shift-P\nMarkdown Help: Ctrl-Alt-Shift-M\nUndo/Redo: Ctrl-Z / Ctrl-Y\n\n" +
                   "Supported: headings, paragraphs, bold, italic, strikethrough, bulleted/numbered/task lists, blockquotes, inline and fenced code, links, tables, and rules. Images insert Markdown and show as alt text/link. Math, Mermaid, footnotes, and embedded remote images are not supported. Project List/Grid shortcuts are disabled while the Summary editor is active."
        };
        await ShowDialogAsync(new ContentDialog
        {
            Title = "Markdown Help",
            CloseButtonText = "Close",
            Content = new ScrollViewer { Content = text, MaxHeight = 520 }
        }, 720, 620);
    }

    private async void SummaryPreview_Click(object sender, RoutedEventArgs e)
    {
        var text = SummaryEditorBox.Text ?? "";
        var preview = new ContentDialog
        {
            Title = "Markdown Preview",
            CloseButtonText = "Close",
            Content = new ScrollViewer
            {
                Content = text.Length == 0
                    ? new TextBlock { Text = "Nothing to preview yet.", Opacity = .7 }
                    : BuildMarkdownView(text, trackReadback: false),
                MaxHeight = 520
            }
        };
        await ShowDialogAsync(preview, 720, 600);
    }
}
