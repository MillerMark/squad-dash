using System;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;

namespace SquadDash;

internal sealed class ButtonEditorWindow : ChromedWindow {
    private readonly ButtonEditorSession _session;
    private readonly Action _onSaved;
    private readonly TextBox _caption;
    private readonly TextBox _tooltip;
    private readonly TextBox _command;
    private readonly TextBox _directory;
    private readonly TextBox _sortOrder;
    private readonly TextBox _identity;
    private readonly ComboBox _commandType;
    private readonly TextBlock _error;
    private readonly Popup _tokens;
    private readonly ListBox _tokenList;
    private int _tokenStart = -1;
    private bool _closed;

    internal ButtonEditorWindow(MainWindow owner, ButtonEditorSession session, Action onSaved) {
        Owner = owner;
        _session = session;
        _onSaved = onSaved;
        Title = session.SavedDefinition is null ? "New button" : "Edit button";
        Width = 720;
        Height = 780;
        MinWidth = 460;
        MinHeight = 400;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        var root = new DockPanel { Margin = new Thickness(16) };
        ApplyOuterBorder(titleText: Title).Child = root;

        var footer = new StackPanel();
        DockPanel.SetDock(footer, Dock.Bottom);
        root.Children.Add(footer);
        _error = Label(string.Empty);
        _error.SetResourceReference(TextBlock.ForegroundProperty, "SystemErrorText");
        footer.Children.Add(_error);
        var actions = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        footer.Children.Add(actions);
        var save = MakeButton("Save");
        save.Click += (_, _) => Save();
        actions.Children.Add(save);
        var cancel = MakeButton("Cancel");
        cancel.Click += (_, _) => Close();
        actions.Children.Add(cancel);

        var fields = new StackPanel();
        root.Children.Add(new ScrollViewer {
            Content = fields,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled
        });
        var content = session.SavedDefinition?.Content;
        _identity = Input(session.SavedDefinition?.Id.ToString() ?? "(assigned on first save)");
        _identity.IsReadOnly = true;
        AddField("Stable ID", _identity);
        _caption = Input(content?.Caption ?? string.Empty);
        AddField("Caption (Markdown **bold** supported)", _caption);
        _tooltip = Input(content?.Tooltip ?? string.Empty);
        AddField("Tooltip", _tooltip);
        _commandType = new ComboBox { SelectedValuePath = "Tag" };
        _commandType.Items.Add(new ComboBoxItem { Content = "PowerShell", Tag = ButtonCommandType.PowerShell });
        _commandType.Items.Add(new ComboBoxItem { Content = "raw-cmd", Tag = ButtonCommandType.RawCommand });
        _commandType.SelectedValue = content?.CommandType ?? ButtonCommandType.PowerShell;
        _commandType.SetResourceReference(FrameworkElement.StyleProperty, "ThemedComboBoxStyle");
        _commandType.SetResourceReference(Control.BackgroundProperty, "InputSurface");
        _commandType.SetResourceReference(Control.ForegroundProperty, "LabelText");
        _commandType.SetResourceReference(Control.BorderBrushProperty, "InputBorder");
        AddField("Command type", _commandType);
        _command = Input(content?.CommandText ?? string.Empty);
        _command.AcceptsReturn = true;
        _command.AcceptsTab = true;
        _command.FontFamily = new FontFamily("Consolas");
        _command.TextWrapping = TextWrapping.NoWrap;
        _command.Height = 200;
        _command.VerticalScrollBarVisibility = ScrollBarVisibility.Auto;
        _command.HorizontalScrollBarVisibility = ScrollBarVisibility.Auto;
        AddField("Command (type { for tokens)", _command);
        _directory = Input(content?.WorkingDirectory ?? string.Empty);
        AddField("Working directory (optional; defaults to workspace root)", _directory);
        _sortOrder = Input(session.InitialSortOrder.ToString(CultureInfo.InvariantCulture));
        AddField("Sort order (zero-based)", _sortOrder);
        fields.Children.Add(Label("Tokens resolve when you run the saved button, in its originating workspace.\n" +
            ButtonPlaceholders.SubstitutionRule));
        foreach (var placeholder in ButtonPlaceholders.Catalog)
            fields.Children.Add(Label($"{placeholder.Token}: {placeholder.Description}\nPowerShell: {placeholder.PowerShellExample}\nraw-cmd: {placeholder.CmdExample}"));

        _tokenList = new ListBox {
            DisplayMemberPath = nameof(ButtonPlaceholder.Token),
            MinWidth = 220,
            MaxHeight = 160
        };
        _tokenList.SetResourceReference(Control.BackgroundProperty, "InputSurface");
        _tokenList.SetResourceReference(Control.ForegroundProperty, "LabelText");
        _tokenList.SetResourceReference(Control.BorderBrushProperty, "InputBorder");
        _tokenList.ItemContainerStyle = BuildTokenItemStyle();
        var popupContent = new StackPanel { MaxWidth = 420 };
        popupContent.Children.Add(_tokenList);
        var help = Label(string.Empty);
        popupContent.Children.Add(help);
        _tokenList.SelectionChanged += (_, _) => {
            if (_tokenList.SelectedItem is ButtonPlaceholder item)
                help.Text = $"{item.Description}\nResolved at invocation.\n{item.PowerShellExample}\n{item.CmdExample}\n{ButtonPlaceholders.SubstitutionRule}";
        };
        var popupBorder = new Border { Child = popupContent, Padding = new Thickness(8), BorderThickness = new Thickness(1) };
        popupBorder.SetResourceReference(Border.BackgroundProperty, "PopupSurface");
        popupBorder.SetResourceReference(Border.BorderBrushProperty, "InputBorder");
        _tokens = new Popup {
            Child = popupBorder, PlacementTarget = _command, Placement = PlacementMode.Relative,
            StaysOpen = false, AllowsTransparency = true
        };
        _tokens.Closed += (_, _) => _tokenStart = -1;
        _command.PreviewTextInput += (_, e) => {
            if (!e.Text.Contains('{')) return;
            Dispatcher.BeginInvoke(DispatcherPriority.Input, () => {
                if (_closed || !_command.IsKeyboardFocused) return;
                if (ButtonTokenCompletion.TryGetPrefix(_command.Text, _command.CaretIndex, out var start, out var prefix)) {
                    _tokenStart = start;
                    UpdateTokens();
                }
            });
        };
        _command.TextChanged += (_, _) => { if (_tokens.IsOpen) UpdateTokens(); };
        _command.SelectionChanged += (_, _) => { if (_tokens.IsOpen) UpdateTokens(); };
        _command.PreviewKeyDown += CommandKeyDown;
        _tokenList.PreviewMouseLeftButtonDown += (_, e) => {
            if (e.OriginalSource is DependencyObject source &&
                ItemsControl.ContainerFromElement(_tokenList, source) is ListBoxItem item) {
                _tokenList.SelectedItem = item.Content;
                CompleteToken();
                e.Handled = true;
            }
        };
        Deactivated += (_, _) => _tokens.IsOpen = false;
        Closed += (_, _) => {
            _closed = true;
            _session.Invalidate();
            _tokens.IsOpen = false;
        };

        void AddField(string label, Control control) {
            fields.Children.Add(Label(label));
            fields.Children.Add(control);
        }
    }

    internal void InvalidateWorkspace() {
        _session.Invalidate();
        Close();
    }

    private void Save() {
        var persisted = false;
        try {
            if (!int.TryParse(_sortOrder.Text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var order))
                throw new ArgumentException("Sort order must be a whole number.");
            if (_commandType.SelectedValue is not ButtonCommandType type)
                throw new ArgumentException("Select a command type.");
            _session.Save(new ButtonContent(_caption.Text, _tooltip.Text, _command.Text, type,
                string.IsNullOrWhiteSpace(_directory.Text) ? null : _directory.Text), order);
            persisted = true;
            _onSaved();
            Close();
        }
        catch (Exception ex) when (ButtonsUiErrors.IsExpected(ex)) {
            SquadDashTrace.Write("Buttons", $"Editor save failed: {ex}");
            _error.Text = persisted
                ? $"The button was saved, but the editor could not finish refreshing the panel: {ex.Message}"
                : ex.Message;
            _identity.Text = _session.SavedDefinition?.Id.ToString() ?? "(assigned on first save)";
        }
    }

    private void UpdateTokens() {
        if (!ButtonTokenCompletion.TryGetPrefix(_command.Text, _command.CaretIndex, out var start, out var prefix) ||
            start != _tokenStart || _command.SelectionLength != 0) {
            _tokens.IsOpen = false;
            return;
        }
        var matches = ButtonPlaceholders.Catalog.Where(item => item.Token.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)).ToArray();
        if (matches.Length == 0) {
            _tokens.IsOpen = false;
            return;
        }
        var previous = _tokenList.SelectedItem;
        _tokenList.ItemsSource = matches;
        _tokenList.SelectedItem = previous is ButtonPlaceholder choice && matches.Contains(choice) ? choice : matches[0];
        var rect = _command.GetRectFromCharacterIndex(_command.CaretIndex);
        if (!rect.IsEmpty) {
            _tokens.HorizontalOffset = rect.Left;
            _tokens.VerticalOffset = rect.Bottom;
        }
        _tokens.IsOpen = true;
    }

    private void CommandKeyDown(object sender, KeyEventArgs e) {
        if (!_tokens.IsOpen) return;
        switch (e.Key) {
            case Key.Escape:
                _tokens.IsOpen = false;
                break;
            case Key.Up:
                _tokenList.SelectedIndex = Math.Max(0, _tokenList.SelectedIndex - 1);
                break;
            case Key.Down:
                _tokenList.SelectedIndex = Math.Min(_tokenList.Items.Count - 1, _tokenList.SelectedIndex + 1);
                break;
            case Key.Enter:
            case Key.Tab:
                CompleteToken();
                break;
            default:
                return;
        }
        e.Handled = true;
    }

    private void CompleteToken() {
        if (_tokenList.SelectedItem is not ButtonPlaceholder item) return;
        var insertion = ButtonTokenCompletion.Complete(_command.Text, _command.CaretIndex, item.Token);
        _tokens.IsOpen = false;
        _command.Select(insertion.Start, insertion.Length);
        _command.SelectedText = item.Token;
        _command.CaretIndex = insertion.CaretIndex;
        _command.Focus();
    }

    private static TextBox Input(string text) {
        var box = new TextBox { Text = text, Padding = new Thickness(5), Margin = new Thickness(0, 0, 0, 4) };
        box.SetResourceReference(Control.BackgroundProperty, "InputSurface");
        box.SetResourceReference(Control.ForegroundProperty, "LabelText");
        box.SetResourceReference(Control.BorderBrushProperty, "InputBorder");
        box.SetResourceReference(TextBox.CaretBrushProperty, "LabelText");
        var menu = new ContextMenu();
        menu.SetResourceReference(FrameworkElement.StyleProperty, "ThemedContextMenuStyle");
        foreach (var command in new[] { ApplicationCommands.Undo, ApplicationCommands.Cut,
            ApplicationCommands.Copy, ApplicationCommands.Paste, ApplicationCommands.SelectAll }) {
            var item = new MenuItem { Header = command.Text, Command = command, CommandTarget = box };
            item.SetResourceReference(FrameworkElement.StyleProperty, "ThemedMenuItemStyle");
            menu.Items.Add(item);
        }
        box.ContextMenu = menu;
        return box;
    }

    private static Style BuildTokenItemStyle() {
        var style = new Style(typeof(ListBoxItem));
        style.Setters.Add(new Setter(Control.ForegroundProperty, new DynamicResourceExtension("LabelText")));
        var surface = new FrameworkElementFactory(typeof(Border)) { Name = "Surface" };
        surface.SetValue(Border.PaddingProperty, new Thickness(6, 4, 6, 4));
        surface.SetResourceReference(Border.BackgroundProperty, "InputSurface");
        surface.AppendChild(new FrameworkElementFactory(typeof(ContentPresenter)));
        var template = new ControlTemplate(typeof(ListBoxItem)) { VisualTree = surface };
        var hover = new Trigger { Property = UIElement.IsMouseOverProperty, Value = true };
        hover.Setters.Add(new Setter(Border.BackgroundProperty, new DynamicResourceExtension("HoverSurface"), "Surface"));
        template.Triggers.Add(hover);
        var selected = new Trigger { Property = ListBoxItem.IsSelectedProperty, Value = true };
        selected.Setters.Add(new Setter(Border.BackgroundProperty, new DynamicResourceExtension("ChipSelectedSurface"), "Surface"));
        template.Triggers.Add(selected);
        style.Setters.Add(new Setter(Control.TemplateProperty, template));
        return style;
    }

    private static TextBlock Label(string text) {
        var label = new TextBlock { Text = text, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 6, 0, 4) };
        label.SetResourceReference(TextBlock.ForegroundProperty, "LabelText");
        label.SetResourceReference(TextBlock.FontSizeProperty, "FontSizeSmall");
        return label;
    }

    private static Button MakeButton(string text) {
        var button = new Button { Content = text, MinWidth = 80, Margin = new Thickness(8, 8, 0, 0) };
        button.SetResourceReference(FrameworkElement.StyleProperty, "ThemedButtonStyle");
        return button;
    }
}
