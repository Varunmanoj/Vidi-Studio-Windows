using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Vidi_Studio_Windows.Models;
using Vidi_Studio_Windows.ViewModels;

namespace Vidi_Studio_Windows.Views;

/// <summary>Reviews TXT, SRT, and VTT timing before an import.</summary>
public sealed class TextTimingDialog : ContentDialog
{
    private sealed class TimingRow
    {
        public int SourceLine { get; init; }
        public string Text { get; init; } = "";
        public double Start { get; set; }
        public double End { get; set; }
        public TextBox StartBox { get; init; } = new();
        public TextBox EndBox { get; init; } = new();
        public TextBlock Error { get; init; } = new();
    }

    private readonly double duration;
    private readonly StudioViewModel model;
    private readonly StudioProject project;
    private readonly string mode;
    private readonly List<TimingRow> rows = new();
    private readonly StackPanel rowPanel = new() { Spacing = 6 };
    private readonly TextBlock summary = new() { TextWrapping = TextWrapping.Wrap, FontSize = 12, Opacity = .7 };
    private bool aiGenerated;
    private bool refreshing, aligning;

    public string? TimingPath { get; private set; }

    public TextTimingDialog(string sourceName, double duration, IReadOnlyList<ImportTimingInspector.Row> parsedRows,
                            string mode, StudioViewModel model, StudioProject project)
    {
        this.duration = duration;
        this.mode = mode;
        this.model = model;
        this.project = project;
        Title = sourceName.EndsWith(".txt", StringComparison.OrdinalIgnoreCase) ? "Set TXT Timing" : "Review Timing";
        PrimaryButtonText = "Save Timing";
        CloseButtonText = "Cancel";
        DefaultButton = ContentDialogButton.Primary;
        foreach (var row in parsedRows)
        {
            var item = new TimingRow
            {
                SourceLine = row.SourceLine,
                Text = row.Text,
                Start = row.Start ?? 0,
                End = row.End ?? 0
            };
            item.StartBox.TextChanged += (_, _) => { if (!refreshing) UpdateState(); };
            item.EndBox.TextChanged += (_, _) => { if (!refreshing) UpdateState(); };
            Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(item.StartBox, $"Start time for source line {row.SourceLine}");
            Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(item.EndBox, $"End time for source line {row.SourceLine}");
            rows.Add(item);
        }
        if (parsedRows.Any(row => row.Start == null || row.End == null)) Distribute();
        Content = BuildContent(sourceName);
        RefreshRows();
        PrimaryButtonClick += (_, args) =>
        {
            if (aligning || !Validate()) { args.Cancel = true; return; }
            WriteTiming();
        };
        UpdateState();
        Closing += (_, _) => { if (aligning) model.Cancel(); };
    }

    private UIElement BuildContent(string sourceName)
    {
        var stack = new StackPanel { Spacing = 10, Width = 760 };
        stack.Children.Add(new TextBlock
        {
            Text = $"Review the start and end time for every line in {sourceName}. Times must stay inside {Format(0)} to {Format(duration)} and must not overlap.",
            TextWrapping = TextWrapping.Wrap, Opacity = .75
        });
        var actions = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        var distribute = new Button { Content = "Distribute Evenly" };
        ToolTipService.SetToolTip(distribute, "Give every line an equal share of the video");
        distribute.Click += (_, _) => { if (!aligning) { Distribute(); RefreshRows(); UpdateState(); } };
        actions.Children.Add(distribute);
        var ai = new Button { Content = "Auto-time with Gemini", IsEnabled = model.CanUseAI };
        ToolTipService.SetToolTip(ai, model.HasApiKey
            ? "Ask Gemini for proposed timing; review every row before saving"
            : "Enter your Gemini API key in Settings to auto-time text");
        ai.Click += async (_, _) => await AlignAsync();
        actions.Children.Add(ai);
        stack.Children.Add(actions);
        stack.Children.Add(summary);
        stack.Children.Add(new ScrollViewer
        {
            Content = rowPanel,
            MaxHeight = 480,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto
        });
        stack.Children.Add(new TextBlock
        {
            Text = "Optional Gemini timing is a proposal only. Saving records that you reviewed it.",
            TextWrapping = TextWrapping.Wrap, FontSize = 12, Opacity = .65
        });
        return stack;
    }

    private void RefreshRows()
    {
        refreshing = true;
        try
        {
        rowPanel.Children.Clear();
        foreach (var row in rows)
        {
            var grid = new Grid { ColumnSpacing = 8 };
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(38) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(124) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(124) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            var line = new TextBlock { Text = row.SourceLine.ToString(), VerticalAlignment = VerticalAlignment.Center, Opacity = .65 };
            Grid.SetColumn(line, 0);
            row.StartBox.Text = Format(row.Start);
            row.StartBox.PlaceholderText = "00:00:00.000";
            row.StartBox.Header = null;
            ToolTipService.SetToolTip(row.StartBox, $"Start time for source line {row.SourceLine}");
            Grid.SetColumn(row.StartBox, 1);
            row.EndBox.Text = Format(row.End);
            row.EndBox.PlaceholderText = "00:00:00.000";
            ToolTipService.SetToolTip(row.EndBox, $"End time for source line {row.SourceLine}");
            Grid.SetColumn(row.EndBox, 2);
            var text = new StackPanel { Spacing = 2 };
            text.Children.Add(new TextBlock { Text = row.Text, TextWrapping = TextWrapping.Wrap, MaxLines = 2, TextTrimming = TextTrimming.CharacterEllipsis });
            row.Error.FontSize = 11;
            row.Error.Foreground = new Microsoft.UI.Xaml.Media.SolidColorBrush(Windows.UI.Color.FromArgb(255, 209, 52, 56));
            text.Children.Add(row.Error);
            Grid.SetColumn(text, 3);
            grid.Children.Add(line);
            grid.Children.Add(row.StartBox);
            grid.Children.Add(row.EndBox);
            grid.Children.Add(text);
            rowPanel.Children.Add(grid);
        }
        }
        finally { refreshing = false; }
    }

    private void Distribute()
    {
        if (rows.Count == 0 || duration <= 0) return;
        for (var index = 0; index < rows.Count; index++)
        {
            rows[index].Start = index * duration / rows.Count;
            rows[index].End = (index + 1) * duration / rows.Count;
        }
        aiGenerated = false;
    }

    private bool Validate()
    {
        var previousEnd = 0.0;
        var valid = true;
        foreach (var row in rows)
        {
            row.Error.Text = "";
            if (!TryParse(row.StartBox.Text, out var start) || !TryParse(row.EndBox.Text, out var end))
            {
                row.Error.Text = "Enter a valid start and end time.";
                valid = false;
                continue;
            }
            if (start < previousEnd - .001)
            {
                row.Error.Text = "Overlaps the previous line.";
                valid = false;
            }
            else if (end <= start)
            {
                row.Error.Text = "End must be after start.";
                valid = false;
            }
            else if (end > duration + .001)
            {
                row.Error.Text = "Ends after the video.";
                valid = false;
            }
            previousEnd = end;
        }
        return valid && rows.Count > 0;
    }

    private void UpdateState()
    {
        foreach (var row in rows)
        {
            if (TryParse(row.StartBox.Text, out var start)) row.Start = start;
            if (TryParse(row.EndBox.Text, out var end)) row.End = end;
        }
        IsPrimaryButtonEnabled = !aligning && Validate();
        summary.Text = $"{rows.Count} lines · {Format(duration)} video";
    }

    private async Task AlignAsync()
    {
        if (!model.CanUseAI || model.Busy || aligning || rows.Count == 0) return;
        aligning = true;
        IsPrimaryButtonEnabled = false;
        var input = Path.Combine(Path.GetTempPath(), "vidistudio-align-" + Guid.NewGuid() + ".txt");
        var output = Path.Combine(Path.GetTempPath(), "vidistudio-align-" + Guid.NewGuid() + ".json");
        try
        {
            await File.WriteAllTextAsync(input, string.Join("\n", rows.Select(row => row.Text)));
            await model.RunAsync("align-imported-text", new[]
            {
                "--input", input, "--output", output, "--ad-transcript-mode", mode
            });
            if (!File.Exists(output)) { summary.Text = "Gemini did not return a timing proposal. Nothing changed."; return; }
            using var document = JsonDocument.Parse(await File.ReadAllTextAsync(output));
            if (!document.RootElement.TryGetProperty("rows", out var proposed) || proposed.ValueKind != JsonValueKind.Array) return;
            var index = 0;
            foreach (var item in proposed.EnumerateArray())
            {
                if (index >= rows.Count) break;
                if (item.TryGetProperty("start", out var start) && start.ValueKind == JsonValueKind.Number) rows[index].Start = start.GetDouble();
                if (item.TryGetProperty("end", out var end) && end.ValueKind == JsonValueKind.Number) rows[index].End = end.GetDouble();
                index++;
            }
            aiGenerated = true;
            RefreshRows();
            UpdateState();
            summary.Text = $"Gemini proposed timing for {index} lines. Review every row before saving.";
        }
        catch (Exception ex)
        {
            summary.Text = "Gemini timing could not be prepared: " + ex.Message;
        }
        finally
        {
            if (File.Exists(input)) File.Delete(input);
            if (File.Exists(output)) File.Delete(output);
            aligning = false;
            UpdateState();
        }
    }

    private void WriteTiming()
    {
        var payload = new
        {
            aiGenerated,
            reviewed = true,
            rows = rows.Select(row => new
            {
                sourceLine = row.SourceLine,
                text = row.Text,
                start = row.Start,
                end = row.End
            })
        };
        var path = Path.Combine(Path.GetTempPath(), "vidistudio-timing-" + Guid.NewGuid() + ".json");
        File.WriteAllText(path, JsonSerializer.Serialize(payload, Json.Options));
        TimingPath = path;
    }

    private static bool TryParse(string? raw, out double seconds)
    {
        seconds = 0;
        if (string.IsNullOrWhiteSpace(raw)) return false;
        var parts = raw.Trim().Replace(',', '.').Split(':');
        if (parts.Length is < 2 or > 3) return false;
        var value = 0.0;
        for (var index = 0; index < parts.Length; index++)
        {
            if (!double.TryParse(parts[index], NumberStyles.Float, CultureInfo.InvariantCulture, out var part) || part < 0 ||
                (index > 0 && part >= 60)) return false;
            value = value * 60 + part;
        }
        seconds = value;
        return double.IsFinite(seconds);
    }

    private static string Format(double seconds)
    {
        var span = TimeSpan.FromSeconds(Math.Max(0, seconds));
        return span.ToString(@"hh\:mm\:ss\.fff");
    }
}
