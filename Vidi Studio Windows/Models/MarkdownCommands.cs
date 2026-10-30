using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace Vidi_Studio_Windows.Models;

/// <summary>Selection-aware Markdown commands shared by the summary toolbar and shortcuts.</summary>
public static class MarkdownCommands
{
    public static (string Text, int Start, int Length) Apply(string text, int start, int length, string command)
    {
        start = Math.Clamp(start, 0, text.Length);
        length = Math.Clamp(length, 0, text.Length - start);
        return command switch
        {
            "bold" => ToggleWrap(text, start, length, "**"),
            "italic" => ToggleWrap(text, start, length, "*"),
            "strikethrough" => ToggleWrap(text, start, length, "~~"),
            "inlineCode" => ToggleWrap(text, start, length, "`"),
            "codeBlock" => WrapBlock(text, start, length, "```"),
            "heading1" => SetHeading(text, start, length, 1),
            "heading2" => SetHeading(text, start, length, 2),
            "heading3" => SetHeading(text, start, length, 3),
            "heading4" => SetHeading(text, start, length, 4),
            "heading5" => SetHeading(text, start, length, 5),
            "heading6" => SetHeading(text, start, length, 6),
            "paragraph" => SetHeading(text, start, length, null),
            "blockquote" => PrefixLines(text, start, length, "> "),
            "bulletedList" => ToggleLinePrefix(text, start, length, "- "),
            "numberedList" => ToggleNumbered(text, start, length),
            "taskList" => ToggleLinePrefix(text, start, length, "- [ ] "),
            "link" => InsertLink(text, start, length, false),
            "image" => InsertLink(text, start, length, true),
            "table" => InsertBlock(text, start, length, "| Column 1 | Column 2 |\n| --- | --- |\n| Cell | Cell |\n"),
            "horizontalRule" => InsertBlock(text, start, length, "---\n"),
            "clearFormatting" => ClearFormatting(text, start, length),
            _ => (text, start, length)
        };
    }

    private static (string, int, int) ToggleWrap(string text, int start, int length, string marker)
    {
        if (length == 0)
            return (text.Insert(start, marker + marker), start + marker.Length, 0);
        var selected = text.Substring(start, length);
        var wrapped = start >= marker.Length && start + length + marker.Length <= text.Length
            && text.Substring(start - marker.Length, marker.Length) == marker
            && text.Substring(start + length, marker.Length) == marker;
        if (wrapped)
        {
            var value = text.Remove(start - marker.Length, marker.Length + length + marker.Length).Insert(start - marker.Length, selected);
            return (value, start - marker.Length, length);
        }
        return (text.Remove(start, length).Insert(start, marker + selected + marker), start + marker.Length, length);
    }

    private static (string, int, int) WrapBlock(string text, int start, int length, string fence)
    {
        var selected = text.Substring(start, length);
        var replacement = fence + "\n" + selected + "\n" + fence;
        return (text.Remove(start, length).Insert(start, replacement), start + fence.Length + 1, length);
    }

    private static (string, int, int) InsertBlock(string text, int start, int length, string block)
        => (text.Remove(start, length).Insert(start, block), start + block.Length, 0);

    private static (int, int) LineRange(string text, int start, int length)
    {
        var lineStart = start == 0 ? 0 : text.LastIndexOf('\n', start - 1) + 1;
        // A selection ending at the next line's start does not include that line.
        var endProbe = length > 0 ? start + length - 1 : start;
        var lineEnd = text.IndexOf('\n', Math.Min(endProbe, text.Length));
        if (lineEnd < 0) lineEnd = text.Length;
        return (lineStart, lineEnd - lineStart);
    }

    private static (string, int, int) SetHeading(string text, int start, int length, int? level)
    {
        var (lineStart, lineLength) = LineRange(text, start, length);
        var original = text.Substring(lineStart, lineLength);
        var lines = original.Split('\n').Select(line =>
        {
            var stripped = Regex.Replace(line, "^#{1,6}\\s+", "");
            return level is { } value ? new string('#', value) + " " + stripped : stripped;
        });
        var replacement = string.Join("\n", lines);
        return (text.Remove(lineStart, lineLength).Insert(lineStart, replacement), lineStart, replacement.Length);
    }

    private static (string, int, int) PrefixLines(string text, int start, int length, string prefix)
    {
        var (lineStart, lineLength) = LineRange(text, start, length);
        var replacement = string.Join("\n", text.Substring(lineStart, lineLength).Split('\n').Select(line => prefix + line));
        return (text.Remove(lineStart, lineLength).Insert(lineStart, replacement), lineStart, replacement.Length);
    }

    private static (string, int, int) ToggleLinePrefix(string text, int start, int length, string prefix)
    {
        var (lineStart, lineLength) = LineRange(text, start, length);
        var lines = text.Substring(lineStart, lineLength).Split('\n').ToList();
        var remove = lines.Count > 0 && lines.All(line => line.StartsWith(prefix, StringComparison.Ordinal));
        var replacement = string.Join("\n", lines.Select(line => remove ? line.Substring(prefix.Length) : prefix + line));
        return (text.Remove(lineStart, lineLength).Insert(lineStart, replacement), lineStart, replacement.Length);
    }

    private static (string, int, int) ToggleNumbered(string text, int start, int length)
    {
        var (lineStart, lineLength) = LineRange(text, start, length);
        var lines = text.Substring(lineStart, lineLength).Split('\n').ToList();
        var remove = lines.Count > 0 && lines.All(line => Regex.IsMatch(line, "^\\d+\\.\\s+"));
        var replacement = string.Join("\n", lines.Select((line, index) =>
            remove ? Regex.Replace(line, "^\\d+\\.\\s+", "") : $"{index + 1}. " + Regex.Replace(line, "^\\d+\\.\\s+", "")));
        return (text.Remove(lineStart, lineLength).Insert(lineStart, replacement), lineStart, replacement.Length);
    }

    private static (string, int, int) InsertLink(string text, int start, int length, bool image)
    {
        var label = length == 0 ? "text" : text.Substring(start, length);
        var prefix = image ? "![" : "[";
        var replacement = prefix + label + "](url)";
        var value = text.Remove(start, length).Insert(start, replacement);
        var urlStart = start + prefix.Length + label.Length + 2;
        return (value, urlStart, 3);
    }

    private static (string, int, int) ClearFormatting(string text, int start, int length)
    {
        if (length == 0) (start, length) = LineRange(text, start, length);
        var selected = text.Substring(start, length);
        var plain = Regex.Replace(selected, "^#{1,6}\\s+", "", RegexOptions.Multiline);
        plain = Regex.Replace(plain, "^>\\s?", "", RegexOptions.Multiline);
        plain = Regex.Replace(plain, "^(?:-\\s\\[[ xX]\\]\\s|-\\s|\\*\\s|\\d+\\.\\s)", "", RegexOptions.Multiline);
        plain = plain.Replace("**", "").Replace("~~", "").Replace("*", "").Replace("`", "");
        return (text.Remove(start, length).Insert(start, plain), start, plain.Length);
    }

    public static string? ListContinuation(string line)
    {
        var task = Regex.Match(line, "^(\\s*)-\\s\\[[ xX]\\]\\s+");
        if (task.Success) return task.Groups[1].Value + "- [ ] ";
        var bullet = Regex.Match(line, "^(\\s*)([-*+])\\s+");
        if (bullet.Success) return bullet.Groups[1].Value + bullet.Groups[2].Value + " ";
        var ordered = Regex.Match(line, "^(\\s*)(\\d+)\\.\\s+");
        if (ordered.Success && int.TryParse(ordered.Groups[2].Value, out var number) && number < int.MaxValue)
            return ordered.Groups[1].Value + (number + 1) + ". ";
        if (line.StartsWith("> ", StringComparison.Ordinal)) return "> ";
        return null;
    }

    public static bool IsEmptyListItem(string line) => Regex.IsMatch(line, @"^\s*(?:[-*+]\s*(?:\[[ xX]\]\s*)?|\d+\.\s*|>\s*)$");
}
