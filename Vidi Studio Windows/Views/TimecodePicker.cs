using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using System;
using System.Globalization;
using System.Text.RegularExpressions;
using Windows.ApplicationModel.DataTransfer;
using Windows.System;

namespace Vidi_Studio_Windows.Views;

/// <summary>
/// A professional timecode picker providing dedicated hour, minute, second, and millisecond inputs
/// with inline steppers, keyboard shortcuts, paste/copy support, and playhead synchronization.
/// </summary>
public sealed class TimecodePicker : StackPanel
{
    private readonly TextBlock headerLabel = new()
    {
        FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
        FontSize = 13,
        VerticalAlignment = VerticalAlignment.Center
    };

    private readonly TextBlock timecodeBadge = new()
    {
        FontFamily = new FontFamily("Consolas, Segoe UI, monospace"),
        FontSize = 12,
        FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
        VerticalAlignment = VerticalAlignment.Center
    };

    private readonly Button copyButton = new()
    {
        Content = new FontIcon { Glyph = "\uE8C8", FontSize = 12 },
        Padding = new Thickness(5, 3, 5, 3),
        MinHeight = 26,
        MinWidth = 26,
        Background = new SolidColorBrush(Windows.UI.Color.FromArgb(0, 0, 0, 0)),
        BorderThickness = new Thickness(0),
        VerticalAlignment = VerticalAlignment.Center
    };

    private readonly Button pasteButton = new()
    {
        Content = new FontIcon { Glyph = "\uE77F", FontSize = 12 },
        Padding = new Thickness(5, 3, 5, 3),
        MinHeight = 26,
        MinWidth = 26,
        Background = new SolidColorBrush(Windows.UI.Color.FromArgb(0, 0, 0, 0)),
        BorderThickness = new Thickness(0),
        VerticalAlignment = VerticalAlignment.Center
    };

    private readonly Button playheadButton = new()
    {
        Content = new FontIcon { Glyph = "\uE823", FontSize = 12 },
        Padding = new Thickness(5, 3, 5, 3),
        MinHeight = 26,
        MinWidth = 26,
        Background = new SolidColorBrush(Windows.UI.Color.FromArgb(0, 0, 0, 0)),
        BorderThickness = new Thickness(0),
        VerticalAlignment = VerticalAlignment.Center,
        Visibility = Visibility.Collapsed
    };

    private readonly TimePartField hoursField = new("hr", 2, 99, 1, 5);
    private readonly TimePartField minutesField = new("min", 2, 59, 1, 5);
    private readonly TimePartField secondsField = new("sec", 2, 59, 1, 5);
    private readonly TimePartField msField = new("ms", 3, 999, 10, 100);

    private readonly TextBlock sepCol1 = CreateSeparator(":");
    private readonly TextBlock sepCol2 = CreateSeparator(":");
    private readonly TextBlock sepDot = CreateSeparator(".");

    private readonly TextBlock lblHr = CreateSubLabel("Hours");
    private readonly TextBlock lblMin = CreateSubLabel("Minutes");
    private readonly TextBlock lblSec = CreateSubLabel("Seconds");
    private readonly TextBlock lblMs = CreateSubLabel("Milliseconds");

    private double valueInSeconds;
    private double maximumSeconds = 360000;
    private double minimumSeconds = 0;
    private bool isUpdating;
    private Func<double>? currentPositionProvider;

    public event EventHandler<double>? ValueChanged;
    public event EventHandler<TimeSpan>? TimeSpanChanged;

    public string Header
    {
        get => headerLabel.Text;
        set
        {
            headerLabel.Text = value;
            AutomationProperties.SetName(this, value);
            hoursField.SetAccessibleName(value + " hours");
            minutesField.SetAccessibleName(value + " minutes");
            secondsField.SetAccessibleName(value + " seconds");
            msField.SetAccessibleName(value + " milliseconds");
            AutomationProperties.SetName(copyButton, "Copy " + value.ToLowerInvariant());
            AutomationProperties.SetName(pasteButton, "Paste " + value.ToLowerInvariant());
            AutomationProperties.SetName(playheadButton, "Set " + value.ToLowerInvariant() + " to current playhead position");
        }
    }

    public double Minimum
    {
        get => minimumSeconds;
        set
        {
            minimumSeconds = Math.Max(0, value);
            if (valueInSeconds < minimumSeconds) Value = minimumSeconds;
        }
    }

    public double Maximum
    {
        get => maximumSeconds;
        set
        {
            maximumSeconds = Math.Max(0, value);
            hoursField.Max = Math.Max(23, (int)(maximumSeconds / 3600));
            if (valueInSeconds > maximumSeconds) Value = maximumSeconds;
        }
    }

    public double Value
    {
        get => valueInSeconds;
        set
        {
            if (isUpdating) return;
            var target = double.IsFinite(value) ? Math.Max(0, value) : 0;
            valueInSeconds = target;
            UpdateInputsFromValue();
            UpdateBadge();
            ValueChanged?.Invoke(this, valueInSeconds);
            TimeSpanChanged?.Invoke(this, TimeSpan.FromSeconds(valueInSeconds));
        }
    }

    public TimeSpan TimeSpanValue
    {
        get => TimeSpan.FromSeconds(Value);
        set => Value = value.TotalSeconds;
    }

    public string FormattedTimecode => TimeSpan.FromSeconds(Math.Max(0, valueInSeconds)).ToString(@"hh\:mm\:ss\.fff");

    public TimecodePicker()
    {
        Spacing = 6;
        HorizontalAlignment = HorizontalAlignment.Stretch;

        BuildLayout();
        AttachEvents();
        UpdateThemeBrushes();

        ActualThemeChanged += (_, _) => UpdateThemeBrushes();
        Loaded += (_, _) => UpdateThemeBrushes();

        Value = 0;
    }

    public void SetCurrentPositionProvider(Func<double> provider, string tooltip = "Set to playhead position")
    {
        currentPositionProvider = provider;
        playheadButton.Visibility = provider != null ? Visibility.Visible : Visibility.Collapsed;
        ToolTipService.SetToolTip(playheadButton, tooltip);
    }

    private void BuildLayout()
    {
        // 1. Header Row
        var headerGrid = new Grid();
        headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        Grid.SetColumn(headerLabel, 0);

        var headerActions = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 4,
            VerticalAlignment = VerticalAlignment.Center
        };

        ToolTipService.SetToolTip(copyButton, "Copy timecode (hh:mm:ss.fff)");
        ToolTipService.SetToolTip(pasteButton, "Paste timecode");
        ToolTipService.SetToolTip(timecodeBadge, "Current timecode");

        headerActions.Children.Add(playheadButton);
        headerActions.Children.Add(copyButton);
        headerActions.Children.Add(pasteButton);
        headerActions.Children.Add(timecodeBadge);

        Grid.SetColumn(headerActions, 1);
        headerGrid.Children.Add(headerLabel);
        headerGrid.Children.Add(headerActions);

        // 2. Input Fields Row
        // Labels and fields use the same columns so the units stay aligned.
        var inputsGrid = new Grid { ColumnSpacing = 8, RowSpacing = 6 };
        inputsGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        inputsGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        inputsGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star), MinWidth = 64 }); // HH
        inputsGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });                                    // :
        inputsGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star), MinWidth = 72 }); // MM
        inputsGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });                                    // :
        inputsGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star), MinWidth = 72 }); // SS
        inputsGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });                                    // .
        inputsGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.3, GridUnitType.Star), MinWidth = 104 }); // MS

        Grid.SetColumn(hoursField, 0);
        Grid.SetColumn(sepCol1, 1);
        Grid.SetColumn(minutesField, 2);
        Grid.SetColumn(sepCol2, 3);
        Grid.SetColumn(secondsField, 4);
        Grid.SetColumn(sepDot, 5);
        Grid.SetColumn(msField, 6);

        inputsGrid.Children.Add(hoursField);
        inputsGrid.Children.Add(sepCol1);
        inputsGrid.Children.Add(minutesField);
        inputsGrid.Children.Add(sepCol2);
        inputsGrid.Children.Add(secondsField);
        inputsGrid.Children.Add(sepDot);
        inputsGrid.Children.Add(msField);

        foreach (var field in new FrameworkElement[] { hoursField, sepCol1, minutesField, sepCol2, secondsField, sepDot, msField })
            Grid.SetRow(field, 1);
        Grid.SetColumn(lblHr, 0);
        Grid.SetColumn(lblMin, 2);
        Grid.SetColumn(lblSec, 4);
        Grid.SetColumn(lblMs, 6);
        inputsGrid.Children.Add(lblHr);
        inputsGrid.Children.Add(lblMin);
        inputsGrid.Children.Add(lblSec);
        inputsGrid.Children.Add(lblMs);

        Children.Add(headerGrid);
        Children.Add(inputsGrid);
    }

    private static TextBlock CreateSeparator(string text) => new()
    {
        Text = text,
        FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
        FontSize = 15,
        VerticalAlignment = VerticalAlignment.Center,
        HorizontalAlignment = HorizontalAlignment.Center,
        Margin = new Thickness(1, 0, 1, 0)
    };

    private static TextBlock CreateSubLabel(string text) => new()
    {
        Text = text,
        FontSize = 12,
        Opacity = 0.8,
        HorizontalAlignment = HorizontalAlignment.Left
    };

    private void UpdateThemeBrushes()
    {
        var isDark = ActualTheme == ElementTheme.Dark;
        var primaryText = new SolidColorBrush(isDark ? Windows.UI.Color.FromArgb(255, 255, 255, 255) : Windows.UI.Color.FromArgb(255, 26, 26, 26));
        var secondaryText = new SolidColorBrush(isDark ? Windows.UI.Color.FromArgb(200, 200, 200, 200) : Windows.UI.Color.FromArgb(200, 90, 90, 90));

        headerLabel.Foreground = primaryText;
        timecodeBadge.Foreground = secondaryText;

        sepCol1.Foreground = secondaryText;
        sepCol2.Foreground = secondaryText;
        sepDot.Foreground = secondaryText;

        lblHr.Foreground = secondaryText;
        lblMin.Foreground = secondaryText;
        lblSec.Foreground = secondaryText;
        lblMs.Foreground = secondaryText;

        hoursField.ApplyTheme(isDark);
        minutesField.ApplyTheme(isDark);
        secondsField.ApplyTheme(isDark);
        msField.ApplyTheme(isDark);
    }

    private void AttachEvents()
    {
        hoursField.ValueChanged += (_, _) => OnFieldChanged();
        minutesField.ValueChanged += (_, _) => OnFieldChanged();
        secondsField.ValueChanged += (_, _) => OnFieldChanged();
        msField.ValueChanged += (_, _) => OnFieldChanged();

        // Linked stepping rollovers
        hoursField.StepRequested += (s, delta) => StepHours(delta);
        minutesField.StepRequested += (s, delta) => StepMinutes(delta);
        secondsField.StepRequested += (s, delta) => StepSeconds(delta);
        msField.StepRequested += (s, delta) => StepMs(delta);

        // Focus traversal
        hoursField.RequestNextFocus += (_, _) => minutesField.FocusInput();
        minutesField.RequestNextFocus += (_, _) => secondsField.FocusInput();
        secondsField.RequestNextFocus += (_, _) => msField.FocusInput();

        msField.RequestPrevFocus += (_, _) => secondsField.FocusInput();
        secondsField.RequestPrevFocus += (_, _) => minutesField.FocusInput();
        minutesField.RequestPrevFocus += (_, _) => hoursField.FocusInput();

        // Paste support forwarded from any field
        hoursField.TimecodePasted += (_, text) => ApplyPastedText(text);
        minutesField.TimecodePasted += (_, text) => ApplyPastedText(text);
        secondsField.TimecodePasted += (_, text) => ApplyPastedText(text);
        msField.TimecodePasted += (_, text) => ApplyPastedText(text);

        copyButton.Click += (_, _) =>
        {
            var dp = new DataPackage();
            dp.SetText(FormattedTimecode);
            Clipboard.SetContent(dp);
        };

        pasteButton.Click += async (_, _) => await TryPasteFromClipboardAsync();

        playheadButton.Click += (_, _) =>
        {
            if (currentPositionProvider != null)
            {
                Value = currentPositionProvider();
            }
        };
    }

    private void StepHours(int delta)
    {
        var h = Math.Clamp(hoursField.Value + delta, 0, hoursField.Max);
        hoursField.Value = h;
    }

    private void StepMinutes(int delta)
    {
        var m = minutesField.Value + delta;
        if (m > 59)
        {
            m = 0;
            StepHours(1);
        }
        else if (m < 0)
        {
            if (hoursField.Value > 0)
            {
                m = 59;
                StepHours(-1);
            }
            else
            {
                m = 0;
            }
        }
        minutesField.Value = m;
    }

    private void StepSeconds(int delta)
    {
        var s = secondsField.Value + delta;
        if (s > 59)
        {
            s = 0;
            StepMinutes(1);
        }
        else if (s < 0)
        {
            if (minutesField.Value > 0 || hoursField.Value > 0)
            {
                s = 59;
                StepMinutes(-1);
            }
            else
            {
                s = 0;
            }
        }
        secondsField.Value = s;
    }

    private void StepMs(int delta)
    {
        var ms = msField.Value + delta;
        if (ms > 999)
        {
            ms = 0;
            StepSeconds(1);
        }
        else if (ms < 0)
        {
            if (secondsField.Value > 0 || minutesField.Value > 0 || hoursField.Value > 0)
            {
                ms = 1000 + ms;
                StepSeconds(-1);
            }
            else
            {
                ms = 0;
            }
        }
        msField.Value = ms;
    }

    private void ApplyPastedText(string text)
    {
        if (TryParseTimecode(text, out var seconds))
        {
            Value = seconds;
        }
    }

    private async System.Threading.Tasks.Task TryPasteFromClipboardAsync()
    {
        try
        {
            var clip = Clipboard.GetContent();
            if (clip.Contains(StandardDataFormats.Text))
            {
                var text = await clip.GetTextAsync();
                ApplyPastedText(text);
            }
        }
        catch { }
    }

    public static bool TryParseTimecode(string? input, out double seconds)
    {
        seconds = 0;
        if (string.IsNullOrWhiteSpace(input)) return false;
        var s = input.Trim();

        // Match hh:mm:ss.fff or hh:mm:ss,fff or mm:ss.fff or mm:ss
        var matchHms = Regex.Match(s, @"^(?:(\d+):)?(\d{1,2}):(\d{1,2})(?:[\.,](\d{1,3}))?$");
        if (matchHms.Success)
        {
            var hrStr = matchHms.Groups[1].Value;
            var minStr = matchHms.Groups[2].Value;
            var secStr = matchHms.Groups[3].Value;
            var msStr = matchHms.Groups[4].Value;

            double h = string.IsNullOrEmpty(hrStr) ? 0 : double.Parse(hrStr, CultureInfo.InvariantCulture);
            double m = double.Parse(minStr, CultureInfo.InvariantCulture);
            double sec = double.Parse(secStr, CultureInfo.InvariantCulture);
            double ms = string.IsNullOrEmpty(msStr) ? 0 : double.Parse(msStr.PadRight(3, '0')[..3], CultureInfo.InvariantCulture);

            seconds = (h * 3600.0) + (m * 60.0) + sec + (ms / 1000.0);
            return true;
        }

        if (double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out var rawSec))
        {
            seconds = rawSec;
            return true;
        }

        return false;
    }

    private void OnFieldChanged()
    {
        if (isUpdating) return;
        isUpdating = true;
        try
        {
            double h = hoursField.Value;
            double m = minutesField.Value;
            double s = secondsField.Value;
            double ms = msField.Value;

            valueInSeconds = (h * 3600.0) + (m * 60.0) + s + (ms / 1000.0);

            UpdateBadge();

            ValueChanged?.Invoke(this, valueInSeconds);
            TimeSpanChanged?.Invoke(this, TimeSpan.FromSeconds(valueInSeconds));
        }
        finally
        {
            isUpdating = false;
        }
    }

    private void UpdateInputsFromValue()
    {
        var clamped = Math.Max(0, valueInSeconds);
        var ts = TimeSpan.FromSeconds(clamped);
        var h = (int)ts.TotalHours;
        var m = ts.Minutes;
        var s = ts.Seconds;
        var ms = ts.Milliseconds;

        hoursField.Value = h;
        minutesField.Value = m;
        secondsField.Value = s;
        msField.Value = ms;
    }

    private void UpdateBadge()
    {
        timecodeBadge.Text = FormattedTimecode;
    }

    /// <summary>
    /// Custom field widget representing a single timecode unit (HH, MM, SS, or MS)
    /// with dedicated styled steppers, wheel support, direct typing, and focus jumping.
    /// </summary>
    private sealed class TimePartField : Grid
    {
        private readonly Border frameBorder = new()
        {
            Height = 32,
            CornerRadius = new CornerRadius(4),
            BorderThickness = new Thickness(1),
            HorizontalAlignment = HorizontalAlignment.Stretch
        };

        private readonly TextBox textBox = new()
        {
            TextAlignment = TextAlignment.Center,
            FontSize = 13,
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
            FontFamily = new FontFamily("Consolas, Segoe UI Variable, monospace"),
            BorderThickness = new Thickness(0),
            Padding = new Thickness(2, 4, 2, 4),
            VerticalAlignment = VerticalAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Stretch
        };

        private readonly Button upButton = new()
        {
            Content = new FontIcon { Glyph = "\uE70E", FontSize = 7 },
            Padding = new Thickness(0),
            Height = 15,
            Width = 16,
            BorderThickness = new Thickness(0),
            CornerRadius = new CornerRadius(0, 3, 0, 0),
            VerticalAlignment = VerticalAlignment.Stretch,
            HorizontalAlignment = HorizontalAlignment.Stretch
        };

        private readonly Button downButton = new()
        {
            Content = new FontIcon { Glyph = "\uE70D", FontSize = 7 },
            Padding = new Thickness(0),
            Height = 15,
            Width = 16,
            BorderThickness = new Thickness(0),
            CornerRadius = new CornerRadius(0, 0, 3, 0),
            VerticalAlignment = VerticalAlignment.Stretch,
            HorizontalAlignment = HorizontalAlignment.Stretch
        };

        private int intValue;
        private bool isInternalChange;

        public int Digits { get; }
        public int Max { get; set; }
        public int Step { get; }
        public int LargeStep { get; }

        public event EventHandler? ValueChanged;
        public event EventHandler<int>? StepRequested;
        public event EventHandler? RequestNextFocus;
        public event EventHandler? RequestPrevFocus;
        public event EventHandler<string>? TimecodePasted;

        public int Value
        {
            get => intValue;
            set
            {
                var clamped = Math.Clamp(value, 0, Max);
                if (intValue == clamped && textBox.Text == Format(clamped)) return;
                intValue = clamped;
                isInternalChange = true;
                textBox.Text = Format(intValue);
                isInternalChange = false;
                ValueChanged?.Invoke(this, EventArgs.Empty);
            }
        }

        public TimePartField(string label, int digits, int max, int step, int largeStep)
        {
            Digits = digits;
            Max = max;
            Step = step;
            LargeStep = largeStep;

            HorizontalAlignment = HorizontalAlignment.Stretch;
            ToolTipService.SetToolTip(this, $"{label.ToUpperInvariant()} (0 - {max})");

            var innerGrid = new Grid();
            innerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            innerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            Grid.SetColumn(textBox, 0);

            var steppers = new Grid { Width = 16 };
            steppers.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            steppers.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

            Grid.SetRow(upButton, 0);
            Grid.SetRow(downButton, 1);
            steppers.Children.Add(upButton);
            steppers.Children.Add(downButton);

            Grid.SetColumn(steppers, 1);

            innerGrid.Children.Add(textBox);
            innerGrid.Children.Add(steppers);
            frameBorder.Child = innerGrid;

            Children.Add(frameBorder);

            Value = 0;

            AttachEvents();
        }

        public void SetAccessibleName(string name)
        {
            AutomationProperties.SetName(textBox, name);
            AutomationProperties.SetName(upButton, "Increase " + name.ToLowerInvariant());
            AutomationProperties.SetName(downButton, "Decrease " + name.ToLowerInvariant());
        }

        public void FocusInput()
        {
            textBox.Focus(FocusState.Programmatic);
            textBox.SelectAll();
        }

        private string Format(int val) => val.ToString(new string('0', Digits));

        private void AttachEvents()
        {
            upButton.Click += (_, _) => StepRequested?.Invoke(this, Step);
            downButton.Click += (_, _) => StepRequested?.Invoke(this, -Step);

            textBox.GotFocus += (_, _) => textBox.SelectAll();
            textBox.LostFocus += (_, _) =>
            {
                if (int.TryParse(textBox.Text, out var parsed))
                {
                    Value = parsed;
                }
                else
                {
                    textBox.Text = Format(intValue);
                }
            };

            textBox.PointerWheelChanged += (_, e) =>
            {
                var delta = e.GetCurrentPoint(this).Properties.MouseWheelDelta;
                if (delta > 0) StepRequested?.Invoke(this, Step);
                else if (delta < 0) StepRequested?.Invoke(this, -Step);
                e.Handled = true;
            };

            textBox.KeyDown += (s, e) =>
            {
                if (e.Key == VirtualKey.Up)
                {
                    StepRequested?.Invoke(this, Step);
                    e.Handled = true;
                }
                else if (e.Key == VirtualKey.Down)
                {
                    StepRequested?.Invoke(this, -Step);
                    e.Handled = true;
                }
                else if (e.Key == VirtualKey.PageUp)
                {
                    StepRequested?.Invoke(this, LargeStep);
                    e.Handled = true;
                }
                else if (e.Key == VirtualKey.PageDown)
                {
                    StepRequested?.Invoke(this, -LargeStep);
                    e.Handled = true;
                }
                else if (e.Key == (VirtualKey)186 || e.Key == VirtualKey.Divide || e.Key == (VirtualKey)190 || e.Key == VirtualKey.Decimal)
                {
                    RequestNextFocus?.Invoke(this, EventArgs.Empty);
                    e.Handled = true;
                }
                else if (e.Key == VirtualKey.Back && string.IsNullOrEmpty(textBox.Text))
                {
                    RequestPrevFocus?.Invoke(this, EventArgs.Empty);
                }
            };

            textBox.TextChanged += (_, _) =>
            {
                if (isInternalChange) return;

                var text = textBox.Text;
                if (text.Contains(':') || (Digits < 3 && text.Contains('.')))
                {
                    TimecodePasted?.Invoke(this, text);
                    return;
                }

                // Digits only
                var digitsOnly = Regex.Replace(text, @"\D", "");
                if (digitsOnly != text)
                {
                    isInternalChange = true;
                    textBox.Text = digitsOnly;
                    textBox.SelectionStart = digitsOnly.Length;
                    isInternalChange = false;
                    text = digitsOnly;
                }

                if (int.TryParse(text, out var parsed))
                {
                    intValue = Math.Clamp(parsed, 0, Max);
                    ValueChanged?.Invoke(this, EventArgs.Empty);

                    if (text.Length >= Digits)
                    {
                        RequestNextFocus?.Invoke(this, EventArgs.Empty);
                    }
                }
                else if (string.IsNullOrEmpty(text))
                {
                    intValue = 0;
                    ValueChanged?.Invoke(this, EventArgs.Empty);
                }
            };
        }

        public void ApplyTheme(bool isDark)
        {
            if (isDark)
            {
                frameBorder.Background = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 38, 38, 38));
                frameBorder.BorderBrush = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 60, 60, 60));
                textBox.Foreground = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 255, 255, 255));
                upButton.Foreground = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 200, 200, 200));
                downButton.Foreground = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 200, 200, 200));
                upButton.Background = new SolidColorBrush(Windows.UI.Color.FromArgb(0, 0, 0, 0));
                downButton.Background = new SolidColorBrush(Windows.UI.Color.FromArgb(0, 0, 0, 0));
            }
            else
            {
                frameBorder.Background = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 255, 255, 255));
                frameBorder.BorderBrush = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 215, 215, 215));
                textBox.Foreground = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 26, 26, 26));
                upButton.Foreground = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 80, 80, 80));
                downButton.Foreground = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 80, 80, 80));
                upButton.Background = new SolidColorBrush(Windows.UI.Color.FromArgb(0, 0, 0, 0));
                downButton.Background = new SolidColorBrush(Windows.UI.Color.FromArgb(0, 0, 0, 0));
            }
        }
    }
}
