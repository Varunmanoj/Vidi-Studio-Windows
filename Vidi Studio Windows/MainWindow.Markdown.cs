using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Documents;
using Microsoft.UI.Xaml.Media;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace Vidi_Studio_Windows;

public sealed partial class MainWindow
{
    /// <summary>Builds a native Markdown reading view shared by Summary and preview.</summary>
    private StackPanel BuildMarkdownView(string markdown, bool trackReadback)
    {
        var panel = new StackPanel { Spacing = 10 };
        var lines = NormalizeSummaryText(markdown).Split('\n');
        var index = 0;
        while (index < lines.Length)
        {
            var line = lines[index];
            if (string.IsNullOrWhiteSpace(line)) { index++; continue; }
            if (line.TrimStart().StartsWith("```", StringComparison.Ordinal))
            {
                index++;
                var code = new List<string>();
                while (index < lines.Length && !lines[index].TrimStart().StartsWith("```", StringComparison.Ordinal))
                    code.Add(lines[index++]);
                index++;
                var codeText = new TextBlock
                {
                    Text = string.Join("\n", code), FontFamily = new FontFamily("Cascadia Mono"),
                    FontSize = 14, TextWrapping = TextWrapping.Wrap, IsTextSelectionEnabled = true,
                    Padding = new Thickness(12)
                };
                panel.Children.Add(new Border
                {
                    Background = new SolidColorBrush(Windows.UI.Color.FromArgb(20, 128, 128, 128)),
                    CornerRadius = new CornerRadius(6), Child = codeText
                });
                if (trackReadback) Track(codeText, codeText.Text);
                continue;
            }
            if (IsTableStart(lines, index))
            {
                var table = new List<List<string>>();
                table.Add(ParseCells(line));
                index += 2;
                while (index < lines.Length && lines[index].Contains('|') && !string.IsNullOrWhiteSpace(lines[index]))
                {
                    table.Add(ParseCells(lines[index]));
                    index++;
                }
                panel.Children.Add(BuildTable(table, trackReadback));
                continue;
            }
            if (Regex.IsMatch(line, @"^\s*([-*_]\s*){3,}$"))
            {
                panel.Children.Add(new Border
                {
                    Height = 1, Margin = new Thickness(0, 8, 0, 8),
                    Background = new SolidColorBrush(Windows.UI.Color.FromArgb(80, 128, 128, 128))
                });
                index++;
                continue;
            }
            var heading = Regex.Match(line, @"^(#{1,6})\s+(.*)");
            if (heading.Success)
            {
                var headingBlock = new TextBlock
                {
                    FontSize = heading.Groups[1].Length switch { 1 => 30, 2 => 26, 3 => 22, _ => 18 },
                    FontWeight = FontWeights.SemiBold,
                    Margin = new Thickness(0, panel.Children.Count == 0 ? 0 : 8, 0, 3),
                    TextWrapping = TextWrapping.Wrap,
                    IsTextSelectionEnabled = true
                };
                AddInlines(headingBlock, heading.Groups[2].Value);
                panel.Children.Add(headingBlock);
                if (trackReadback) Track(headingBlock, StripInline(heading.Groups[2].Value));
                index++;
                continue;
            }
            var task = Regex.Match(line, @"^\s*-\s\[([ xX])\]\s+(.*)");
            var block = new TextBlock
            {
                FontSize = 16, TextWrapping = TextWrapping.Wrap, IsTextSelectionEnabled = true,
                Margin = new Thickness(Regex.IsMatch(line, @"^\s*[-*+]\s|^\s*\d+\.\s|^>\s") ? 18 + line.TakeWhile(char.IsWhiteSpace).Count() * 6 : 0, 0, 0, 0)
            };
            var value = line;
            string plain;
            if (task.Success)
            {
                var bullet = task.Groups[1].Value.Trim().Length == 0 ? "☐ " : "☑ ";
                AddInlines(block, bullet + task.Groups[2].Value);
                plain = bullet + StripInline(task.Groups[2].Value);
            }
            else if (Regex.IsMatch(line, @"^\s*\d+\.\s+"))
            {
                var number = Regex.Match(line, @"^\s*(\d+)\.\s+(.*)");
                AddInlines(block, number.Groups[1].Value + ". " + number.Groups[2].Value);
                plain = number.Groups[1].Value + ". " + StripInline(number.Groups[2].Value);
            }
            else if (Regex.IsMatch(line, @"^\s*[-*+]\s+"))
            {
                var bulletText = Regex.Replace(line, @"^\s*[-*+]\s+", "");
                AddInlines(block, "• " + bulletText);
                plain = "• " + StripInline(bulletText);
            }
            else if (line.StartsWith("> ", StringComparison.Ordinal))
            {
                block.Foreground = (Brush)Application.Current.Resources["TextFillColorSecondaryBrush"];
                block.FontStyle = Windows.UI.Text.FontStyle.Italic;
                AddInlines(block, line[2..]);
                plain = StripInline(line[2..]);
            }
            else
            {
                // Markdown soft line breaks are one paragraph, as in the Mac reader.
                while (index + 1 < lines.Length && !string.IsNullOrWhiteSpace(lines[index + 1])
                    && !Regex.IsMatch(lines[index + 1], @"^\s*(?:#{1,6}\s|[-*+]\s|\d+\.\s|>|```|([-*_]\s*){3,}$)")
                    && !IsTableStart(lines, index + 1))
                    value += " " + lines[++index].Trim();
                AddInlines(block, value);
                plain = StripInline(value);
            }
            panel.Children.Add(block);
            if (trackReadback) Track(block, plain);
            index++;
        }
        return panel;
    }

    private void Track(TextBlock block, string plain)
    {
        block.SelectionChanged += (_, _) => UpdateDesktopMenuStates();
        summaryLines.Add((block, plain));
    }

    private static bool IsTableStart(string[] lines, int index)
    {
        if (index + 1 >= lines.Length || !lines[index].Contains('|')) return false;
        return Regex.IsMatch(lines[index + 1].Trim(), @"^\|?\s*:?-{3,}:?\s*(\|\s*:?-{3,}:?\s*)+\|?$");
    }

    private static List<string> ParseCells(string line)
        => line.Trim().Trim('|').Split('|').Select(cell => cell.Trim()).ToList();

    private Border BuildTable(List<List<string>> table, bool trackReadback)
    {
        var grid = new Grid
        {
            Background = new SolidColorBrush(Windows.UI.Color.FromArgb(14, 128, 128, 128)),
            BorderBrush = new SolidColorBrush(Windows.UI.Color.FromArgb(60, 128, 128, 128)),
            BorderThickness = new Thickness(1)
        };
        var columns = table.Max(row => row.Count);
        for (var column = 0; column < columns; column++)
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        for (var rowIndex = 0; rowIndex < table.Count; rowIndex++)
        {
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            for (var column = 0; column < columns; column++)
            {
                var value = column < table[rowIndex].Count ? table[rowIndex][column] : "";
                var cell = new TextBlock
                {
                    TextWrapping = TextWrapping.Wrap, FontSize = 14,
                    FontWeight = rowIndex == 0 ? FontWeights.SemiBold : FontWeights.Normal,
                    Padding = new Thickness(10, 7, 10, 7),
                    IsTextSelectionEnabled = true
                };
                AddInlines(cell, value);
                var border = new Border
                {
                    BorderBrush = new SolidColorBrush(Windows.UI.Color.FromArgb(45, 128, 128, 128)),
                    BorderThickness = new Thickness(0, 0, 1, rowIndex == table.Count - 1 ? 0 : 1),
                    Child = cell
                };
                Grid.SetRow(border, rowIndex);
                Grid.SetColumn(border, column);
                grid.Children.Add(border);
                if (trackReadback) Track(cell, StripInline(value));
            }
        }
        return new Border { Child = grid, CornerRadius = new CornerRadius(6), Margin = new Thickness(0, 4, 0, 8) };
    }

    private static readonly Regex InlinePattern = new(@"\\([\\`*_{}\[\]()#+\-.!~|>])|(!?)\[([^\]]+)\]\(([^\)]+)\)|`([^`]+)`|\*\*\*(.+?)\*\*\*|\*\*(.+?)\*\*|__(.+?)__|~~(.+?)~~|\*(.+?)\*|(?<!\w)_(.+?)_(?!\w)");

    private static void AddInlines(TextBlock block, string value) => AddInlineContent(block.Inlines, value);

    private static void AddInlineContent(InlineCollection inlines, string value)
    {
        var offset = 0;
        foreach (Match match in InlinePattern.Matches(value))
        {
            if (match.Index > offset) inlines.Add(new Run { Text = value[offset..match.Index] });
            if (match.Groups[1].Success) inlines.Add(new Run { Text = match.Groups[1].Value });
            else if (match.Groups[3].Success)
            {
                var link = new Hyperlink();
                AddInlineContent(link.Inlines, match.Groups[3].Value);
                if (Uri.TryCreate(match.Groups[4].Value, UriKind.Absolute, out var uri) && uri.Scheme is "https" or "http" or "mailto") link.NavigateUri = uri;
                inlines.Add(link);
            }
            else if (match.Groups[5].Success) inlines.Add(new Run { Text = match.Groups[5].Value, FontFamily = new FontFamily("Cascadia Mono") });
            else if (match.Groups[6].Success)
            {
                var bold = new Bold(); var italic = new Italic();
                AddInlineContent(italic.Inlines, match.Groups[6].Value); bold.Inlines.Add(italic); inlines.Add(bold);
            }
            else if (match.Groups[7].Success || match.Groups[8].Success)
            {
                var bold = new Bold(); AddInlineContent(bold.Inlines, match.Groups[7].Success ? match.Groups[7].Value : match.Groups[8].Value); inlines.Add(bold);
            }
            else if (match.Groups[9].Success) inlines.Add(new Run { Text = StripInline(match.Groups[9].Value), TextDecorations = Windows.UI.Text.TextDecorations.Strikethrough });
            else
            {
                var italic = new Italic(); AddInlineContent(italic.Inlines, match.Groups[10].Success ? match.Groups[10].Value : match.Groups[11].Value); inlines.Add(italic);
            }
            offset = match.Index + match.Length;
        }
        if (offset < value.Length) inlines.Add(new Run { Text = value[offset..] });
    }

    private static string StripInline(string value)
    {
        return InlinePattern.Replace(value, match =>
        {
            if (match.Groups[1].Success) return match.Groups[1].Value;
            if (match.Groups[3].Success) return StripInline(match.Groups[3].Value);
            if (match.Groups[5].Success) return match.Groups[5].Value;
            for (var group = 6; group <= 11; group++)
                if (match.Groups[group].Success) return StripInline(match.Groups[group].Value);
            return match.Value;
        });
    }
}
