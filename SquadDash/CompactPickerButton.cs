namespace SquadDash;

using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;

/// <summary>
/// A compact inline value-picker: displays the selected value as a chip-like button and
/// opens a themed context menu (disabled header + separator + options) on click.
/// </summary>
internal sealed class CompactPickerButton {

    private readonly string                                            _headerText;
    private readonly IReadOnlyList<(string DisplayName, string Value)> _options;
    private readonly Action<string>?                                   _onValueChanged;

    private string _selectedValue;

    // ── Public API ────────────────────────────────────────────────────────────

    /// <summary>The WPF element to insert into the visual tree.</summary>
    public Button Control { get; }

    /// <summary>The raw value of the currently-selected option.</summary>
    public string SelectedValue {
        get => _selectedValue;
        set {
            _selectedValue  = value;
            Control.Content = GetDisplayName(value);
        }
    }

    // ── Construction ─────────────────────────────────────────────────────────

    /// <param name="headerText">Text for the disabled menu header item (e.g. "Run Frequency").</param>
    /// <param name="options">Selectable options as (DisplayName, Value) pairs.</param>
    /// <param name="selectedValue">Initially selected raw value.</param>
    /// <param name="onValueChanged">Invoked with the new raw value when the user picks an option.</param>
    public CompactPickerButton(
        string                                            headerText,
        IReadOnlyList<(string DisplayName, string Value)> options,
        string                                            selectedValue,
        Action<string>?                                   onValueChanged = null) {

        _headerText     = headerText;
        _options        = options;
        _selectedValue  = selectedValue;
        _onValueChanged = onValueChanged;

        Control = new Button {
            Content         = GetDisplayName(selectedValue),
            Padding         = new Thickness(5, 1, 5, 1),
            Margin          = new Thickness(0, 0, 4, 2),
            BorderThickness = new Thickness(1),
        };
        Control.SetResourceReference(Button.StyleProperty,       "FlatButtonStyle");
        Control.SetResourceReference(Button.FontSizeProperty,    "FontSizeXSmall");
        Control.SetResourceReference(Button.ForegroundProperty,  "SubtleText");
        Control.SetResourceReference(Button.BackgroundProperty,  "InputSurface");
        Control.SetResourceReference(Button.BorderBrushProperty, "InputBorder");
        Control.Click += OnButtonClick;
    }

    // ── Private helpers ───────────────────────────────────────────────────────

    private string GetDisplayName(string value) {
        foreach (var (displayName, v) in _options)
            if (string.Equals(v, value, StringComparison.OrdinalIgnoreCase))
                return displayName;
        return value;
    }

    private void OnButtonClick(object sender, RoutedEventArgs e) {
        var menu = new ContextMenu();
        menu.SetResourceReference(ContextMenu.StyleProperty, "ThemedContextMenuStyle");

        var header = new MenuItem { Header = _headerText, IsEnabled = false };
        header.SetResourceReference(MenuItem.StyleProperty, "ThemedMenuItemStyle");
        menu.Items.Add(header);

        var sep = new Separator();
        sep.SetResourceReference(Separator.StyleProperty, "ThemedMenuSeparatorStyle");
        menu.Items.Add(sep);

        foreach (var (displayName, value) in _options) {
            var capturedValue = value;
            var item = new MenuItem {
                Header    = displayName,
                IsChecked = string.Equals(value, _selectedValue, StringComparison.OrdinalIgnoreCase),
            };
            item.SetResourceReference(MenuItem.StyleProperty, "ThemedMenuItemStyle");
            item.Click += (_, _) => SelectValue(capturedValue);
            menu.Items.Add(item);
        }

        menu.PlacementTarget = Control;
        menu.Placement       = PlacementMode.Bottom;
        menu.IsOpen          = true;
    }

    private void SelectValue(string value) {
        if (string.Equals(value, _selectedValue, StringComparison.OrdinalIgnoreCase)) return;
        _selectedValue  = value;
        Control.Content = GetDisplayName(value);
        _onValueChanged?.Invoke(value);
    }
}
