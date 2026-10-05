using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;

namespace SquadDash;

internal sealed partial class ButtonsPanelController {
    private readonly StackPanel _listPanel;
    private readonly Action _newButton;
    private readonly Action<ButtonDefinition> _editButton;
    private readonly Action<ButtonDefinition> _deleteButton;
    private readonly Action<ButtonDefinition> _execute;

    internal ButtonsPanelViewModel ViewModel { get; } = new();
    internal Action ClearFilterAction { get; set; }

    internal ButtonsPanelController(StackPanel listPanel, FrameworkElement panel, TextBox filterBox,
        Action newButton, Action<ButtonDefinition> editButton,
        Action<ButtonDefinition> deleteButton, Action<ButtonDefinition> execute) {
        _listPanel = listPanel;
        _newButton = newButton;
        _editButton = editButton;
        _deleteButton = deleteButton;
        _execute = execute;
        InitializeReordering(panel);
        ClearFilterAction = () => filterBox.Clear();
        panel.ContextMenu = BuildContextMenu();
        // TextBox has a built-in menu that would otherwise hide the panel's New action.
        filterBox.ContextMenu = BuildContextMenu();
        panel.AddHandler(ContextMenuService.ContextMenuOpeningEvent,
            new ContextMenuEventHandler((_, e) => {
                FrameworkElement target = panel;
                for (var source = e.OriginalSource as DependencyObject; source is not null;) {
                    if (source is Button { Tag: ButtonDefinition } item) {
                        target = item;
                        break;
                    }
                    if (ReferenceEquals(source, panel)) break;
                    source = source is Visual ? VisualTreeHelper.GetParent(source)
                        : LogicalTreeHelper.GetParent(source);
                }
                // Also override native child menus (for example the scrollbar's Scroll Here).
                var menu = target.ContextMenu!;
                e.Handled = true;
                menu.PlacementTarget = target;
                menu.Placement = e.CursorLeft < 0 ? PlacementMode.Bottom : PlacementMode.MousePoint;
                menu.IsOpen = true;
            }), handledEventsToo: true);
    }

    internal void Refresh(IReadOnlyList<ButtonDefinition> buttons) {
        InvalidateReorder(dataChanged: true);
        ViewModel.Buttons = buttons;
        ViewModel.LoadError = null;
        Rebuild();
    }

    internal void SetFilter(string text) {
        InvalidateReorder();
        ViewModel.FilterText = text.Trim();
        Rebuild();
    }

    internal void ShowError(string message) {
        InvalidateReorder(dataChanged: true);
        ViewModel.Buttons = [];
        ViewModel.LoadError = message;
        Rebuild();
    }

    private void Rebuild() {
        if (_settling || _savingOrder) return;
        _listPanel.Children.Clear();
        if (ViewModel.LoadError is { } error) {
            var text = new TextBlock { Text = error, TextWrapping = TextWrapping.Wrap };
            text.SetResourceReference(TextBlock.ForegroundProperty, "SystemErrorText");
            _listPanel.Children.Add(text);
            return;
        }
        foreach (var definition in ViewModel.Buttons.OrderBy(item => item.SortOrder)) {
            if (!ButtonCaptionPresentation.Matches(definition.Content.Caption, ViewModel.FilterText))
                continue;
            _listPanel.Children.Add(BuildButton(definition));
        }
        if (_listPanel.Children.Count != 0) return;
        if (ViewModel.FilterText.Length > 0) {
            _listPanel.Children.Add(FilterEmptyStateHelper.Build(ClearFilterAction));
        } else {
            var empty = new TextBlock {
                Text = "Right-click to create a new button.",
                Margin = new Thickness(4, 8, 4, 4),
                TextWrapping = TextWrapping.Wrap
            };
            empty.SetResourceReference(TextBlock.ForegroundProperty, "SubtleText");
            _listPanel.Children.Add(empty);
        }
    }

    internal Button BuildButton(ButtonDefinition definition) {
        var button = new Button {
            Content = ButtonCaptionPresentation.Render(definition.Content.Caption),
            Tag = definition,
            Margin = new Thickness(0, 4, 0, 0),
            Padding = new Thickness(8),
            ClickMode = ClickMode.Release,
            ToolTip = ToolTipHelper.MakeThemedToolTip(definition.Content.Tooltip),
            ContextMenu = BuildContextMenu(definition)
        };
        button.SetResourceReference(FrameworkElement.StyleProperty, "ThemedButtonStyle");
        button.SetResourceReference(Control.FontSizeProperty, "FontSizeNormal");
        ToolTipService.SetIsEnabled(button, true);
        WireReorderButton(button);
        button.Click += (_, e) => {
            e.Handled = true;
            RequestExecution(definition);
        };
        return button;
    }

    internal void RequestExecution(ButtonDefinition definition) {
        if (_settling || _savingOrder || _reorderGesture?.SuppressExecution == true) return;
        _execute(definition);
    }

    internal ContextMenu BuildContextMenu(ButtonDefinition? definition = null) {
        var menu = new ContextMenu();
        menu.SetResourceReference(FrameworkElement.StyleProperty, "ThemedContextMenuStyle");
        menu.SetResourceReference(Control.BackgroundProperty, "PopupSurface");
        menu.SetResourceReference(Control.ForegroundProperty, "LabelText");
        menu.SetResourceReference(Control.BorderBrushProperty, "InputBorder");
        Add("New button", _newButton);
        if (definition is not null) {
            var separator = new Separator();
            separator.SetResourceReference(FrameworkElement.StyleProperty, "ThemedSeparatorStyle");
            menu.Items.Add(separator);
            Add("Edit", () => _editButton(definition));
            Add("Delete", () => _deleteButton(definition));
            Add("Run", () => RequestExecution(definition));
        }
        return menu;

        void Add(string label, Action action) {
            var item = new MenuItem { Header = label };
            item.SetResourceReference(FrameworkElement.StyleProperty, "ThemedMenuItemStyle");
            item.Click += (_, e) => { e.Handled = true; action(); };
            menu.Items.Add(item);
        }
    }
}
