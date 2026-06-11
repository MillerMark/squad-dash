using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Shapes;

namespace SquadDash;

/// <summary>
/// Developer window that displays all named theme color tokens with live
/// brightness/saturation sliders. Supports Dark/Light theme switching,
/// live preview, and writing modified colors back to the source XAML files.
/// </summary>
internal sealed class ThemeColorsWindow : Window
{
    // ── MainWindow reference (for SwitchTheme / ActiveThemeName) ─────────
    private readonly MainWindow _mainWindow;

    // ── Snapshots taken at window open ────────────────────────────────────
    // keyed by theme name ("Dark" / "Light") → resource key → original Color
    private readonly Dictionary<string, Dictionary<string, Color>> _originalColors = new(StringComparer.OrdinalIgnoreCase);
    private readonly string _snapshotThemeName;

    // Pending user overrides per theme
    private readonly Dictionary<string, Dictionary<string, Color>> _pendingByTheme = new(StringComparer.OrdinalIgnoreCase);

    // ── UI controls ───────────────────────────────────────────────────────
    private readonly TextBox _filterBox;
    private readonly ListBox _listBox;
    private readonly Border _swatchBorder;
    private readonly TextBlock _keyLabel;
    private readonly Slider _brightnessSlider;
    private readonly Slider _saturationSlider;
    private readonly TextBlock _brightnessValueLabel;
    private readonly TextBlock _saturationValueLabel;

    // Currently selected key
    private string? _selectedKey;

    // Suppress slider callbacks while programmatically updating values
    private bool _suppressSliderEvents;

    // ── Constructor ───────────────────────────────────────────────────────
    internal ThemeColorsWindow(MainWindow mainWindow)
    {
        _mainWindow = mainWindow;
        _snapshotThemeName = mainWindow.ActiveThemeName;

        // ── Snapshot both themes ─────────────────────────────────────────
        SnapshotBothThemes();

        // ── Window properties ─────────────────────────────────────────────
        Title = "Theme Colors";
        Width = 700;
        Height = 520;
        ShowInTaskbar = false;
        WindowStyle = WindowStyle.ToolWindow;
        ResizeMode = ResizeMode.CanResize;
        SetResourceReference(BackgroundProperty, "AppSurface");

        // ── Root layout ───────────────────────────────────────────────────
        var root = new DockPanel();
        Content = root;

        // ── Footer (Cancel / OK) ──────────────────────────────────────────
        var footer = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(8, 6, 12, 8)
        };
        DockPanel.SetDock(footer, Dock.Bottom);
        root.Children.Add(footer);

        var cancelBtn = new Button { Content = "Cancel", Width = 80, Height = 28, Margin = new Thickness(0, 0, 8, 0) };
        cancelBtn.SetResourceReference(StyleProperty, "ThemedButtonStyle");
        cancelBtn.Click += CancelButton_Click;
        footer.Children.Add(cancelBtn);

        var okBtn = new Button { Content = "OK", Width = 80, Height = 28 };
        okBtn.SetResourceReference(StyleProperty, "ThemedButtonStyle");
        okBtn.Click += OkButton_Click;
        footer.Children.Add(okBtn);

        // ── Footer separator ──────────────────────────────────────────────
        var footerSep = new Separator();
        DockPanel.SetDock(footerSep, Dock.Bottom);
        root.Children.Add(footerSep);

        // ── Theme selector (top) ──────────────────────────────────────────
        var themeBar = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Margin = new Thickness(8, 6, 8, 4)
        };
        DockPanel.SetDock(themeBar, Dock.Top);
        root.Children.Add(themeBar);

        var themeLabel = new TextBlock
        {
            Text = "Theme:",
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 8, 0)
        };
        themeLabel.SetResourceReference(TextBlock.ForegroundProperty, "LabelText");
        themeBar.Children.Add(themeLabel);

        var darkRadio = new RadioButton { Content = "Dark", Margin = new Thickness(0, 0, 12, 0), GroupName = "ThemeSelector" };
        darkRadio.SetResourceReference(StyleProperty, "ThemedRadioButtonStyle");
        themeBar.Children.Add(darkRadio);

        var lightRadio = new RadioButton { Content = "Light", GroupName = "ThemeSelector" };
        lightRadio.SetResourceReference(StyleProperty, "ThemedRadioButtonStyle");
        themeBar.Children.Add(lightRadio);

        // Set initial radio selection
        if (string.Equals(_snapshotThemeName, "Dark", StringComparison.OrdinalIgnoreCase))
            darkRadio.IsChecked = true;
        else
            lightRadio.IsChecked = true;

        darkRadio.Checked += (_, _) => SwitchToTheme("Dark");
        lightRadio.Checked += (_, _) => SwitchToTheme("Light");

        // ── Theme bar separator ───────────────────────────────────────────
        var themeBarSep = new Separator();
        DockPanel.SetDock(themeBarSep, Dock.Top);
        root.Children.Add(themeBarSep);

        // ── Filter row ────────────────────────────────────────────────────
        var filterRow = new DockPanel { Margin = new Thickness(8, 4, 8, 4), LastChildFill = true };
        DockPanel.SetDock(filterRow, Dock.Top);
        root.Children.Add(filterRow);

        var filterLabel = new TextBlock
        {
            Text = "Filter:",
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 6, 0)
        };
        filterLabel.SetResourceReference(TextBlock.ForegroundProperty, "LabelText");
        DockPanel.SetDock(filterLabel, Dock.Left);
        filterRow.Children.Add(filterLabel);

        _filterBox = new TextBox { Height = 24 };
        _filterBox.SetResourceReference(BackgroundProperty, "InputSurface");
        _filterBox.SetResourceReference(ForegroundProperty, "LabelText");
        _filterBox.SetResourceReference(BorderBrushProperty, "InputBorder");
        _filterBox.TextChanged += (_, _) => RebuildList();
        filterRow.Children.Add(_filterBox);

        // ── Body: list + detail ───────────────────────────────────────────
        var body = new Grid();
        body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(40, GridUnitType.Star) });
        body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Pixel) });
        body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(60, GridUnitType.Star) });
        root.Children.Add(body);

        // ── Splitter ──────────────────────────────────────────────────────
        var splitter = new GridSplitter
        {
            Width = 1,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Stretch
        };
        splitter.SetResourceReference(BackgroundProperty, "PanelBorder");
        Grid.SetColumn(splitter, 1);
        body.Children.Add(splitter);

        // ── ListBox (left) ────────────────────────────────────────────────
        _listBox = new ListBox
        {
            BorderThickness = new Thickness(0),
            Padding = new Thickness(0)
        };
        _listBox.SetResourceReference(BackgroundProperty, "RosterPanelSurface");
        _listBox.SetResourceReference(BorderBrushProperty, "RosterPanelBorder");
        Grid.SetColumn(_listBox, 0);
        body.Children.Add(_listBox);

        _listBox.SelectionChanged += ListBox_SelectionChanged;

        // ── Detail panel (right) ──────────────────────────────────────────
        var detailPanel = new StackPanel { Margin = new Thickness(16, 12, 16, 12) };
        Grid.SetColumn(detailPanel, 2);
        body.Children.Add(detailPanel);

        // Swatch
        _swatchBorder = new Border
        {
            Width = 48,
            Height = 48,
            BorderThickness = new Thickness(1),
            Margin = new Thickness(0, 0, 0, 8),
            HorizontalAlignment = HorizontalAlignment.Left
        };
        _swatchBorder.SetResourceReference(Border.BorderBrushProperty, "PanelBorder");
        detailPanel.Children.Add(_swatchBorder);

        // Key label
        _keyLabel = new TextBlock
        {
            FontSize = 13,
            Margin = new Thickness(0, 0, 0, 16),
            TextWrapping = TextWrapping.Wrap
        };
        _keyLabel.SetResourceReference(TextBlock.ForegroundProperty, "LabelText");
        detailPanel.Children.Add(_keyLabel);

        // Brightness slider
        (_brightnessSlider, _brightnessValueLabel) = BuildSliderRow(detailPanel, "Brightness");
        _brightnessSlider.ValueChanged += BrightnessSlider_ValueChanged;

        // Saturation slider
        (_saturationSlider, _saturationValueLabel) = BuildSliderRow(detailPanel, "Saturation");
        _saturationSlider.ValueChanged += SaturationSlider_ValueChanged;

        // ── Populate list ─────────────────────────────────────────────────
        RebuildList();
    }

    // ── Snapshot ──────────────────────────────────────────────────────────

    private void SnapshotBothThemes()
    {
        foreach (var themeName in new[] { "Dark", "Light" })
        {
            var uri = new Uri($"Themes/{themeName}.xaml", UriKind.Relative);
            var dict = new ResourceDictionary { Source = uri };
            var snapshot = new Dictionary<string, Color>(StringComparer.Ordinal);
            foreach (var key in dict.Keys)
            {
                if (key is string keyStr && dict[key] is SolidColorBrush brush)
                    snapshot[keyStr] = brush.Color;
            }
            _originalColors[themeName] = snapshot;
        }
    }

    // ── List population ───────────────────────────────────────────────────

    private void RebuildList()
    {
        _listBox.Items.Clear();
        _selectedKey = null;
        _keyLabel.Text = string.Empty;
        _swatchBorder.Background = null;

        var currentTheme = _mainWindow.ActiveThemeName;
        if (!_originalColors.TryGetValue(currentTheme, out var snapshot))
            return;

        // Apply any pending overrides into live resources first
        if (_pendingByTheme.TryGetValue(currentTheme, out var pending))
        {
            foreach (var (key, color) in pending)
                Application.Current.Resources[key] = new SolidColorBrush(color);
        }

        var filter = _filterBox?.Text?.Trim() ?? string.Empty;

        foreach (var kvp in snapshot.OrderBy(k => k.Key, StringComparer.OrdinalIgnoreCase))
        {
            var key = kvp.Key;
            if (filter.Length > 0 && key.IndexOf(filter, StringComparison.OrdinalIgnoreCase) < 0)
                continue;

            var color = GetEffectiveColor(currentTheme, key);

            var row = BuildListRow(key, color);
            row.Tag = key;
            _listBox.Items.Add(row);
        }
    }

    private static StackPanel BuildListRow(string key, Color color)
    {
        var row = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Margin = new Thickness(4, 2, 4, 2)
        };

        var swatch = new Rectangle
        {
            Width = 24,
            Height = 24,
            Fill = new SolidColorBrush(color),
            Margin = new Thickness(0, 0, 8, 0),
            VerticalAlignment = VerticalAlignment.Center
        };
        row.Children.Add(swatch);

        var label = new TextBlock
        {
            Text = key,
            VerticalAlignment = VerticalAlignment.Center,
            FontSize = 12
        };
        label.SetResourceReference(TextBlock.ForegroundProperty, "LabelText");
        row.Children.Add(label);

        return row;
    }

    // ── Theme switching ───────────────────────────────────────────────────

    private void SwitchToTheme(string themeName)
    {
        if (string.Equals(_mainWindow.ActiveThemeName, themeName, StringComparison.OrdinalIgnoreCase))
            return;

        _mainWindow.SwitchTheme(themeName);
        RebuildList();

        // Reset sliders
        _suppressSliderEvents = true;
        _brightnessSlider.Value = 0;
        _saturationSlider.Value = 0;
        _suppressSliderEvents = false;
    }

    // ── List selection ────────────────────────────────────────────────────

    private void ListBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_listBox.SelectedItem is not StackPanel row || row.Tag is not string key)
        {
            _selectedKey = null;
            return;
        }
        _selectedKey = key;

        var currentTheme = _mainWindow.ActiveThemeName;
        var color = GetEffectiveColor(currentTheme, key);
        _swatchBorder.Background = new SolidColorBrush(color);
        _keyLabel.Text = key;

        // Restore sliders to the pending adjustment for this key (if any).
        // Reverse-engineer approximate slider values from the HSL delta between
        // the original and pending colors. 8-bit quantization means this is not
        // exact, but it's close enough for UX feedback.
        _suppressSliderEvents = true;
        if (_pendingByTheme.TryGetValue(currentTheme, out var pending) &&
            pending.TryGetValue(key, out var pendingColor) &&
            _originalColors.TryGetValue(currentTheme, out var snap) &&
            snap.TryGetValue(key, out var origColor))
        {
            ColorUtilities.RgbToHsl(origColor.R, origColor.G, origColor.B, out _, out double sOrig, out double lOrig);
            ColorUtilities.RgbToHsl(pendingColor.R, pendingColor.G, pendingColor.B, out _, out double sPend, out double lPend);
            double approxBrightness = Math.Round((lPend - lOrig) * 200.0);
            double approxSaturation = Math.Round((sPend - sOrig) * 200.0);
            _brightnessSlider.Value = Math.Clamp(approxBrightness, _brightnessSlider.Minimum, _brightnessSlider.Maximum);
            _saturationSlider.Value = Math.Clamp(approxSaturation, _saturationSlider.Minimum, _saturationSlider.Maximum);
        }
        else
        {
            _brightnessSlider.Value = 0;
            _saturationSlider.Value = 0;
        }
        _suppressSliderEvents = false;
    }

    // ── Slider handlers ───────────────────────────────────────────────────

    private void BrightnessSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        _brightnessValueLabel.Text = $"{(int)e.NewValue:+0;-0;0}";
        if (!_suppressSliderEvents) ApplyAdjustment();
    }

    private void SaturationSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        _saturationValueLabel.Text = $"{(int)e.NewValue:+0;-0;0}";
        if (!_suppressSliderEvents) ApplyAdjustment();
    }

    private void ApplyAdjustment()
    {
        if (_selectedKey is null) return;
        var currentTheme = _mainWindow.ActiveThemeName;
        if (!_originalColors.TryGetValue(currentTheme, out var snap)) return;
        if (!snap.TryGetValue(_selectedKey, out var origColor)) return;

        var adjusted = AdjustColor(origColor, _brightnessSlider.Value, _saturationSlider.Value);

        // Update live resources
        Application.Current.Resources[_selectedKey] = new SolidColorBrush(adjusted);

        // Track pending change
        if (!_pendingByTheme.ContainsKey(currentTheme))
            _pendingByTheme[currentTheme] = new Dictionary<string, Color>(StringComparer.Ordinal);
        _pendingByTheme[currentTheme][_selectedKey] = adjusted;

        // Update swatch
        _swatchBorder.Background = new SolidColorBrush(adjusted);

        // Update the list row swatch too
        UpdateListRowSwatch(_selectedKey, adjusted);
    }

    // ── Color math ────────────────────────────────────────────────────────

    private static Color AdjustColor(Color original, double brightness, double saturation)
    {
        ColorUtilities.RgbToHsl(original.R, original.G, original.B, out double h, out double s, out double l);

        l = Math.Clamp(l + brightness / 100.0 * 0.5, 0.0, 1.0);
        s = Math.Clamp(s + saturation / 100.0 * 0.5, 0.0, 1.0);

        ColorUtilities.HslToRgb(h, s, l, out byte r, out byte g, out byte b);
        return Color.FromRgb(r, g, b);
    }

    // ── Helpers ───────────────────────────────────────────────────────────

    private Color GetEffectiveColor(string themeName, string key)
    {
        if (_pendingByTheme.TryGetValue(themeName, out var pending) &&
            pending.TryGetValue(key, out var pendingColor))
            return pendingColor;
        if (_originalColors.TryGetValue(themeName, out var snap) &&
            snap.TryGetValue(key, out var origColor))
            return origColor;
        return Colors.Transparent;
    }

    private void UpdateListRowSwatch(string key, Color color)
    {
        foreach (var item in _listBox.Items)
        {
            if (item is StackPanel row && row.Tag is string rowKey &&
                string.Equals(rowKey, key, StringComparison.Ordinal) &&
                row.Children.Count > 0 && row.Children[0] is Rectangle rect)
            {
                rect.Fill = new SolidColorBrush(color);
                break;
            }
        }
    }

    private static (Slider slider, TextBlock valueLabel) BuildSliderRow(Panel parent, string labelText)
    {
        var container = new Grid { Margin = new Thickness(0, 0, 0, 12) };
        container.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        container.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        container.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(80) });
        container.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        container.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(40) });

        var label = new TextBlock
        {
            Text = labelText,
            VerticalAlignment = VerticalAlignment.Center,
            FontSize = 12
        };
        label.SetResourceReference(TextBlock.ForegroundProperty, "BodyText");
        Grid.SetRow(label, 0);
        Grid.SetColumn(label, 0);
        container.Children.Add(label);

        var slider = new Slider
        {
            Minimum = -100,
            Maximum = 100,
            Value = 0,
            SmallChange = 1,
            LargeChange = 10,
            TickFrequency = 10,
            IsSnapToTickEnabled = false,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(4, 0, 4, 0)
        };
        Grid.SetRow(slider, 0);
        Grid.SetColumn(slider, 1);
        container.Children.Add(slider);

        var valueLabel = new TextBlock
        {
            Text = "0",
            VerticalAlignment = VerticalAlignment.Center,
            FontSize = 11,
            Width = 36,
            TextAlignment = TextAlignment.Right
        };
        valueLabel.SetResourceReference(TextBlock.ForegroundProperty, "BodyText");
        Grid.SetRow(valueLabel, 0);
        Grid.SetColumn(valueLabel, 2);
        container.Children.Add(valueLabel);

        parent.Children.Add(container);
        return (slider, valueLabel);
    }

    // ── Cancel / OK ───────────────────────────────────────────────────────

    private void CancelButton_Click(object sender, RoutedEventArgs e)
    {
        RevertAllThemes();
        _mainWindow.SwitchTheme(_snapshotThemeName);
        Close();
    }

    private void OkButton_Click(object sender, RoutedEventArgs e)
    {
        WriteChangesToSourceFiles();
        Close();
    }

    private void RevertAllThemes()
    {
        // Remove every app-level resource entry that ApplyAdjustment wrote across
        // all themes.  App-level entries override merged-dictionary values, so
        // leaving them in place after Cancel would keep polluted colors visible
        // even after SwitchTheme reloads the correct XAML.  Removing them lets
        // the subsequent SwitchTheme(_snapshotThemeName) call in CancelButton_Click
        // restore every key to its original XAML value cleanly.
        foreach (var (_, pending) in _pendingByTheme)
        {
            foreach (var key in pending.Keys)
                Application.Current.Resources.Remove(key);
        }
    }

    // ── Write changes to source XAML files ───────────────────────────────

    private void WriteChangesToSourceFiles()
    {
        foreach (var (themeName, changes) in _pendingByTheme)
        {
            if (changes.Count == 0) continue;

            var xamlPath = FindThemeSourceFile(themeName);
            if (xamlPath is null)
            {
                MessageBox.Show(
                    $"Could not locate Themes/{themeName}.xaml source file. Changes for '{themeName}' theme were not saved.",
                    "Theme Colors", MessageBoxButton.OK, MessageBoxImage.Warning);
                continue;
            }

            var lines = File.ReadAllLines(xamlPath);
            for (int i = 0; i < lines.Length; i++)
            {
                var line = lines[i];
                // Match: x:Key="SomeKey" ... Color="#RRGGBB"
                var keyMatch = Regex.Match(line, @"x:Key=""([^""]+)""");
                if (!keyMatch.Success) continue;
                var key = keyMatch.Groups[1].Value;
                if (!changes.TryGetValue(key, out var newColor)) continue;

                var hex = $"#{newColor.R:X2}{newColor.G:X2}{newColor.B:X2}";
                lines[i] = Regex.Replace(line, @"Color=""#[0-9A-Fa-f]{6,8}""", $"Color=\"{hex}\"");
            }
            File.WriteAllLines(xamlPath, lines);
        }
    }

    private static string? FindThemeSourceFile(string themeName)
    {
        var fileName = $"{themeName}.xaml";
        // Walk up from the executable location looking for SquadDash\Themes\<theme>.xaml
        var dir = new DirectoryInfo(AppDomain.CurrentDomain.BaseDirectory);
        for (int depth = 0; depth < 8 && dir is not null; depth++, dir = dir.Parent)
        {
            // Check <dir>\Themes\<theme>.xaml
            var direct = System.IO.Path.Combine(dir.FullName, "Themes", fileName);
            if (File.Exists(direct)) return direct;

            // Check <dir>\SquadDash\Themes\<theme>.xaml (when running from repo root)
            var inProject = System.IO.Path.Combine(dir.FullName, "SquadDash", "Themes", fileName);
            if (File.Exists(inProject)) return inProject;
        }
        return null;
    }
}
