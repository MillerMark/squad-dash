namespace SquadDash;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;

/// <summary>Represents a single entry inside a <see cref="CompactPickerButton"/> submenu.</summary>
internal readonly record struct SubitemEntry(string DisplayName, string Value, bool IsEnabled, bool IsSeparator) {
    internal static SubitemEntry Item(string displayName, string value) =>
        new(displayName, value, IsEnabled: true, IsSeparator: false);
    internal static SubitemEntry Header(string text) =>
        new(text, string.Empty, IsEnabled: false, IsSeparator: false);
    internal static readonly SubitemEntry Separator =
        new(string.Empty, string.Empty, IsEnabled: false, IsSeparator: true);
}

/// <summary>
/// A compact inline value-picker: displays the selected value as a chip-like button and
/// opens a themed context menu (disabled header + separator + options) on click.
/// Supports both simple menu items and submenu items.
/// </summary>
internal sealed class CompactPickerButton {

    private readonly string                                                                _headerText;
    private readonly IReadOnlyList<(string DisplayName, string Value)>                     _options;
    private readonly IReadOnlyList<(string DisplayName, string Value, SubitemEntry[]?)>? _optionsWithSubmenus;
    private readonly Action<string>?                                                       _onValueChanged;

    private string _selectedValue;

    // ── Public API ────────────────────────────────────────────────────────────

    /// <summary>The WPF element to insert into the visual tree.</summary>
    public Button Control { get; }

    /// <summary>The raw value of the currently-selected option.</summary>
    public string SelectedValue {
        get => _selectedValue;
        set {
            _selectedValue  = value;
            Control.Content = GetButtonLabel(value);
        }
    }

    // ── Construction ─────────────────────────────────────────────────────────

    private readonly Func<string, string>? _getButtonLabel;

    /// <param name="headerText">Text for the disabled menu header item (e.g. "Run Frequency").</param>
    /// <param name="options">Selectable options as (DisplayName, Value) pairs.</param>
    /// <param name="selectedValue">Initially selected raw value.</param>
    /// <param name="onValueChanged">Invoked with the new raw value when the user picks an option.</param>
    /// <param name="getButtonLabel">
    ///   Optional override for the button's displayed text. Receives the selected raw value and returns
    ///   the string to show on the button face. When <c>null</c> the option's DisplayName is used.
    /// </param>
    public CompactPickerButton(
        string                                            headerText,
        IReadOnlyList<(string DisplayName, string Value)> options,
        string                                            selectedValue,
        Action<string>?                                   onValueChanged = null,
        Func<string, string>?                             getButtonLabel = null) {

        _headerText             = headerText;
        _options                = options;
        _optionsWithSubmenus    = null;
        _selectedValue          = selectedValue;
        _onValueChanged         = onValueChanged;
        _getButtonLabel         = getButtonLabel;

        Control = new Button {
            Content         = GetButtonLabel(selectedValue),
            Padding         = new Thickness(5, 1, 5, 1),
            Margin          = new Thickness(0, 0, 4, 2),
            BorderThickness = new Thickness(1),
            BorderBrush     = System.Windows.Media.Brushes.Transparent,
            Background      = System.Windows.Media.Brushes.Transparent,
            ToolTip         = "Click to change",
        };
        Control.SetResourceReference(Button.StyleProperty,      "FlatButtonStyle");
        Control.SetResourceReference(Button.FontSizeProperty,   "FontSizeXSmall");
        Control.SetResourceReference(Button.ForegroundProperty, "SubtleText");

        // Show button chrome only on hover — border space is always reserved (thickness=1)
        // so layout doesn't shift and cause a hover-flicker loop.
        Control.MouseEnter += (_, _) => {
            Control.SetResourceReference(Button.BackgroundProperty,  "InputSurface");
            Control.SetResourceReference(Button.BorderBrushProperty, "InputBorder");
        };
        Control.MouseLeave += (_, _) => {
            Control.Background  = System.Windows.Media.Brushes.Transparent;
            Control.BorderBrush = System.Windows.Media.Brushes.Transparent;
        };

        Control.Click += OnButtonClick;
    }

    /// <summary>
    /// Overload that supports submenu items. Items can have optional subitems:
    /// when subitems is non-null, clicking the item opens a submenu; otherwise it selects the value.
    /// </summary>
    public CompactPickerButton(
        string                                                                        headerText,
        IReadOnlyList<(string DisplayName, string Value, SubitemEntry[]? Subitems)> optionsWithSubmenus,
        string                                                                        selectedValue,
        Action<string>?                                                              onValueChanged = null,
        Func<string, string>?                                                        getButtonLabel = null) {

        _headerText             = headerText;
        _options                = [];
        _optionsWithSubmenus    = optionsWithSubmenus;
        _selectedValue          = selectedValue;
        _onValueChanged         = onValueChanged;
        _getButtonLabel         = getButtonLabel;

        Control = new Button {
            Content         = GetButtonLabel(selectedValue),
            Padding         = new Thickness(5, 1, 5, 1),
            Margin          = new Thickness(0, 0, 4, 2),
            BorderThickness = new Thickness(1),
            BorderBrush     = System.Windows.Media.Brushes.Transparent,
            Background      = System.Windows.Media.Brushes.Transparent,
            ToolTip         = "Click to change",
        };
        Control.SetResourceReference(Button.StyleProperty,      "FlatButtonStyle");
        Control.SetResourceReference(Button.FontSizeProperty,   "FontSizeXSmall");
        Control.SetResourceReference(Button.ForegroundProperty, "SubtleText");

        // Show button chrome only on hover — border space is always reserved (thickness=1)
        // so layout doesn't shift and cause a hover-flicker loop.
        Control.MouseEnter += (_, _) => {
            Control.SetResourceReference(Button.BackgroundProperty,  "InputSurface");
            Control.SetResourceReference(Button.BorderBrushProperty, "InputBorder");
        };
        Control.MouseLeave += (_, _) => {
            Control.Background  = System.Windows.Media.Brushes.Transparent;
            Control.BorderBrush = System.Windows.Media.Brushes.Transparent;
        };

        Control.Click += OnButtonClick;
    }


    private string GetMenuDisplayName(string value) {
        foreach (var (displayName, v) in _options)
            if (string.Equals(v, value, StringComparison.OrdinalIgnoreCase))
                return displayName;
        return value;
    }

    // Returns the label shown on the button face; uses _getButtonLabel override when supplied.
    private string GetButtonLabel(string value) =>
        _getButtonLabel?.Invoke(value) ?? GetMenuDisplayName(value);

    private void OnButtonClick(object sender, RoutedEventArgs e) {
        SquadDashTrace.Write(TraceCategory.UI,
            $"[compact-picker] OnButtonClick: creating menu, selectedValue={_selectedValue}");
        
        var menu = new ContextMenu();
        menu.SetResourceReference(ContextMenu.StyleProperty, "ThemedContextMenuStyle");

        var header = new MenuItem { Header = _headerText, IsEnabled = false };
        header.SetResourceReference(MenuItem.StyleProperty, "ThemedMenuItemStyle");
        menu.Items.Add(header);

        var sep = new Separator();
        sep.SetResourceReference(Separator.StyleProperty, "ThemedMenuSeparatorStyle");
        menu.Items.Add(sep);

        // Handle simple options (no submenus)
        if (_optionsWithSubmenus == null) {
            foreach (var (displayName, value) in _options) {
                var capturedValue = value;
                var item = new MenuItem {
                    Header    = displayName,
                    IsChecked = string.Equals(value, _selectedValue, StringComparison.OrdinalIgnoreCase),
                };
                item.SetResourceReference(MenuItem.StyleProperty, "ThemedMenuItemStyle");
                item.Click += (_, _) => {
                    SquadDashTrace.Write(TraceCategory.UI,
                        $"[compact-picker] Menu item clicked: {displayName} ({capturedValue})");
                    SelectValue(capturedValue);
                };
                menu.Items.Add(item);
                SquadDashTrace.Write(TraceCategory.UI,
                    $"[compact-picker] Added menu item: {displayName} ({value}), IsChecked={item.IsChecked}");
            }
        } else {
            // Handle options with optional submenus
            foreach (var (displayName, value, subitems) in _optionsWithSubmenus) {
                var capturedValue = value;
                var capturedDisplayName = displayName;

                if (subitems == null || subitems.Length == 0) {
                    // Simple item without submenu
                    var item = new MenuItem {
                        Header    = displayName,
                        IsChecked = string.Equals(value, _selectedValue, StringComparison.OrdinalIgnoreCase),
                    };
                    item.SetResourceReference(MenuItem.StyleProperty, "ThemedMenuItemStyle");
                    item.Click += (_, _) => {
                        SquadDashTrace.Write(TraceCategory.UI,
                            $"[compact-picker] Menu item clicked: {capturedDisplayName} ({capturedValue})");
                        SelectValue(capturedValue);
                    };
                    menu.Items.Add(item);
                    SquadDashTrace.Write(TraceCategory.UI,
                        $"[compact-picker] Added menu item: {displayName} ({value}), IsChecked={item.IsChecked}");
                } else {
                    // Item with submenu — parent is checked when any enabled child matches
                    bool anyChildSelected = subitems.Any(s =>
                        !s.IsSeparator && s.IsEnabled &&
                        string.Equals(s.Value, _selectedValue, StringComparison.OrdinalIgnoreCase));
                    var parentItem = new MenuItem { Header = displayName, IsChecked = anyChildSelected };
                    parentItem.SetResourceReference(MenuItem.StyleProperty, "ThemedMenuItemStyle");

                    foreach (var subitem in subitems) {
                        if (subitem.IsSeparator) {
                            var subSep = new Separator();
                            subSep.SetResourceReference(Separator.StyleProperty, "ThemedMenuSeparatorStyle");
                            parentItem.Items.Add(subSep);
                            continue;
                        }

                        var capturedSubitemValue = subitem.Value;
                        var capturedSubitemDisplayName = subitem.DisplayName;
                        var submi = new MenuItem {
                            Header    = subitem.DisplayName,
                            IsEnabled = subitem.IsEnabled,
                            IsChecked = subitem.IsEnabled &&
                                        string.Equals(subitem.Value, _selectedValue, StringComparison.OrdinalIgnoreCase),
                        };
                        submi.SetResourceReference(MenuItem.StyleProperty, "ThemedMenuItemStyle");
                        if (subitem.IsEnabled) {
                            submi.Click += (_, _) => {
                                SquadDashTrace.Write(TraceCategory.UI,
                                    $"[compact-picker] Submenu item clicked: {capturedDisplayName} > {capturedSubitemDisplayName} ({capturedSubitemValue})");
                                SelectValue(capturedSubitemValue);
                            };
                        }
                        parentItem.Items.Add(submi);
                        SquadDashTrace.Write(TraceCategory.UI,
                            $"[compact-picker] Added submenu item: {capturedDisplayName} > {subitem.DisplayName} ({subitem.Value}), IsChecked={submi.IsChecked}");
                    }

                    menu.Items.Add(parentItem);
                }
            }
        }

        menu.PlacementTarget = Control;
        menu.Placement       = PlacementMode.Bottom;
        
        SquadDashTrace.Write(TraceCategory.UI,
            $"[compact-picker] Opening menu with {menu.Items.Count} items");
        
        menu.IsOpen          = true;
    }

    private void SelectValue(string value) {
        SquadDashTrace.Write(TraceCategory.UI,
            $"[compact-picker] SelectValue: oldValue={_selectedValue}, newValue={value}");
        
        if (string.Equals(value, _selectedValue, StringComparison.OrdinalIgnoreCase)) {
            SquadDashTrace.Write(TraceCategory.UI,
                $"[compact-picker] Value unchanged, returning early");
            return;
        }
        
        _selectedValue  = value;
        Control.Content = GetButtonLabel(value);
        
        SquadDashTrace.Write(TraceCategory.UI,
            $"[compact-picker] Invoking onValueChanged callback with value={value}");
        
        _onValueChanged?.Invoke(value);
        
        SquadDashTrace.Write(TraceCategory.UI,
            $"[compact-picker] onValueChanged callback completed");
    }
}
