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
        var panel = new StackPanel { Spacing = 8 };
        var lines = markdown.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
        var index = 0;
        while (index < lines.Length)
        {
            var line = lines[index];
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
                    FontSize = 28 - heading.Groups[1].Length * 2,
                    FontWeight = FontWeights.SemiBold,
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
                Margin = new Thickness(Regex.IsMatch(line, @"^\s*[-*]\s|^\s*\d+\.\s|^>\s") ? 18 : 0, 0, 0, 0)
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
            else if (Regex.IsMatch(line, @"^\s*[-*]\s+"))
            {
                var bulletText = Regex.Replace(line, @"^\s*[-*]\s+", "");
                AddInlines(block, "• " + bulletText);
                plain = "• " + StripInline(bulletText);
            }
            else if (line.StartsWith("> ", StringComparison.Ordinal))
            {
                block.Foreground = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 120, 120, 120));
                block.FontStyle = Windows.UI.Text.FontStyle.Italic;
                AddInlines(block, line[2..]);
                plain = StripInline(line[2..]);
            }
            else
            {
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

    private static void AddInlines(TextBlock block, string value)
    {
        var pattern = new Regex(@"\*\*(.+?)\*\*|\*(.+?)\*|`([^`]+)`|\[([^\]]+)\]\(([^\)]+)\)");
        var offset = 0;
        foreach (Match match in pattern.Matches(value))
        {
            if (match.Index > offset) block.Inlines.Add(new Run { Text = value[offset..match.Index] });
            if (match.Groups[1].Success)
            {
                var bold = new Bold(); bold.Inlines.Add(new Run { Text = match.Groups[1].Value }); block.Inlines.Add(bold);
            }
            else if (match.Groups[2].Success)
            {
                var italic = new Italic(); italic.Inlines.Add(new Run { Text = match.Groups[2].Value }); block.Inlines.Add(italic);
            }
            else if (match.Groups[3].Success)
            {
                block.Inlines.Add(new Run { Text = match.Groups[3].Value, FontFamily = new FontFamily("Cascadia Mono") });
            }
            else if (match.Groups[4].Success)
            {
                var link = new Hyperlink();
                link.Inlines.Add(new Run { Text = match.Groups[4].Value });
                if (Uri.TryCreate(match.Groups[5].Value, UriKind.Absolute, out var uri)) link.NavigateUri = uri;
                block.Inlines.Add(link);
            }
            offset = match.Index + match.Length;
        }
        if (offset < value.Length) block.Inlines.Add(new Run { Text = value[offset..] });
    }

    private static string StripInline(string value)
    {
        var plain = Regex.Replace(value, @"\*\*(.+?)\*\*", "$1");
        plain = Regex.Replace(plain, @"\*(.+?)\*", "$1");
        plain = Regex.Replace(plain, @"`([^`]+)`", "$1");
        return Regex.Replace(plain, @"\[([^\]]+)\]\(([^\)]+)\)", "$1");
    }
}
