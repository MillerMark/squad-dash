using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;

namespace SquadDash.GuidedTours;

/// <summary>
/// Attaches context-sensitive as-you-type autocomplete to a <see cref="TextBox"/> or the
/// inner <see cref="TextBox"/> of an editable <see cref="ComboBox"/>.
/// Shows a <see cref="Popup"/> suggestion list below the placement target.
/// <list type="bullet">
///   <item>↓/↑ arrows navigate the list</item>
///   <item>Tab / Enter accepts the focused item (or the first item if none focused)</item>
///   <item>Escape dismisses the popup</item>
///   <item>Any other key appends to the text source normally and re-filters</item>
/// </list>
/// </summary>
internal sealed class TourIntelliSenseHelper : IDisposable
{
    private readonly FrameworkElement                        _placementTarget;
    private readonly TextBox                                 _textSource;
    private readonly Func<string, IReadOnlyList<string>>     _suggestionsProvider;
    private readonly Action<string>                          _acceptCallback;
    private readonly Popup                                   _popup;
    private readonly ListBox                                 _listBox;
    private bool                                             _suppressUpdate;
    private bool                                             _disposed;
    private bool                                             _triggeredByTyping;
    private Window?                                          _ownerWindow;

    /// <param name="placementTarget">
    /// The control beneath which the popup appears (typically the ComboBox or TextBox itself).
    /// The popup width matches this control's <see cref="FrameworkElement.ActualWidth"/>.
    /// </param>
    /// <param name="textSource">
    /// The TextBox whose text drives filtering — may be the same as
    /// <paramref name="placementTarget"/> for a standalone TextBox, or the inner
    /// TextBox of an editable ComboBox.
    /// </param>
    /// <param name="suggestionsProvider">
    /// Called with the current text; returns the ordered list of matching suggestions to show.
    /// Return an empty list to hide the popup.
    /// </param>
    /// <param name="acceptCallback">
    /// Called with the accepted suggestion text.  The callback is responsible for updating
    /// the underlying control (e.g. setting <c>ComboBox.Text</c> or <c>TextBox.Text</c>).
    /// </param>
    public TourIntelliSenseHelper(
        FrameworkElement                    placementTarget,
        TextBox                             textSource,
        Func<string, IReadOnlyList<string>> suggestionsProvider,
        Action<string>                      acceptCallback)
    {
        _placementTarget   = placementTarget;
        _textSource        = textSource;
        _suggestionsProvider = suggestionsProvider;
        _acceptCallback    = acceptCallback;

        _listBox = new ListBox
        {
            Focusable                  = false,
            BorderThickness            = new Thickness(0),
            Padding                    = new Thickness(0),
            HorizontalContentAlignment = HorizontalAlignment.Stretch,
        };
        _listBox.SetResourceReference(ListBox.BackgroundProperty, "InputSurface");
        // PreviewMouseLeftButtonDown fires before WPF transfers keyboard focus away from the
        // text source, so we can accept the item before IsKeyboardFocusWithinChanged closes
        // the popup.  Using MouseLeftButtonUp instead would be too late — the popup is already
        // closed by the time that event fires.
        _listBox.PreviewMouseLeftButtonDown += OnListBoxPreviewMouseDown;

        var border = new Border
        {
            Child           = _listBox,
            BorderThickness = new Thickness(1),
            Padding         = new Thickness(0),
            MaxHeight       = 12 * 22 + 4,
        };
        border.SetResourceReference(Border.BackgroundProperty,  "InputSurface");
        border.SetResourceReference(Border.BorderBrushProperty, "InputBorder");

        _popup = new Popup
        {
            Child              = border,
            Placement          = PlacementMode.Bottom,
            PlacementTarget    = _placementTarget,
            StaysOpen          = true,
            AllowsTransparency = true,
            IsOpen             = false,
            Focusable          = false,
        };
        // Return keyboard focus to the text source whenever the popup opens so the
        // popup's Win32 HWND never activates and steals keystrokes from the editor.
        _popup.Opened += (_, _) => Keyboard.Focus(_textSource);
        _popup.SetBinding(Popup.WidthProperty,
            new Binding("ActualWidth") { Source = _placementTarget });

        _textSource.TextChanged    += OnTextChanged;
        _textSource.PreviewKeyDown += OnPreviewKeyDown;
        _textSource.PreviewTextInput += OnPreviewTextInput;
        _placementTarget.IsKeyboardFocusWithinChanged += OnFocusWithinChanged;

        // Close popup whenever the owner window moves or resizes so it doesn't
        // float at the original screen position after the user drags the window.
        if (_placementTarget.IsLoaded)
            HookOwnerWindow();
        else
            _placementTarget.Loaded += OnPlacementTargetLoaded;
    }

    private void OnFocusWithinChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (!(bool)e.NewValue)
            _popup.IsOpen = false;
    }

    private void OnPlacementTargetLoaded(object sender, RoutedEventArgs e)
    {
        _placementTarget.Loaded -= OnPlacementTargetLoaded;
        HookOwnerWindow();
    }

    private void HookOwnerWindow()
    {
        _ownerWindow = Window.GetWindow(_placementTarget);
        if (_ownerWindow is null) return;
        // Close popup when the window is dragged to a new position.
        // SizeChanged is intentionally NOT subscribed: the tour editor uses
        // SizeToContent=Height, so showing/hiding the status label causes SizeChanged
        // to fire and would incorrectly dismiss the suggestion popup.
        _ownerWindow.LocationChanged += OnWindowPositionChanged;
    }

    private void OnWindowPositionChanged(object? sender, EventArgs e) => _popup.IsOpen = false;

    private void OnTextChanged(object sender, TextChangedEventArgs e)
    {
        if (_suppressUpdate) return;
        UpdateSuggestions();
    }

    private void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        // Backspace and Delete modify text but do not fire PreviewTextInput, so
        // mark them as user-initiated so the popup can still update on deletion.
        if (e.Key is Key.Back or Key.Delete)
            _triggeredByTyping = true;

        if (!_popup.IsOpen) return;

        switch (e.Key)
        {
            case Key.Down:
                if (_listBox.Items.Count > 0)
                {
                    int next = _listBox.SelectedIndex + 1;
                    if (next >= _listBox.Items.Count) next = 0;
                    _listBox.SelectedIndex = next;
                    (_listBox.ItemContainerGenerator.ContainerFromIndex(next) as ListBoxItem)
                        ?.BringIntoView();
                }
                e.Handled = true;
                break;

            case Key.Up:
                if (_listBox.Items.Count > 0)
                {
                    int prev = _listBox.SelectedIndex <= 0
                        ? _listBox.Items.Count - 1
                        : _listBox.SelectedIndex - 1;
                    _listBox.SelectedIndex = prev;
                    (_listBox.ItemContainerGenerator.ContainerFromIndex(prev) as ListBoxItem)
                        ?.BringIntoView();
                }
                e.Handled = true;
                break;

            case Key.Tab:
            case Key.Enter:
                AcceptSelected();
                e.Handled = true;
                break;

            case Key.Escape:
                _popup.IsOpen = false;
                e.Handled = true;
                break;
        }
    }

    private void OnPreviewTextInput(object sender, TextCompositionEventArgs e) =>
        _triggeredByTyping = true;

    private void OnListBoxPreviewMouseDown(object sender, MouseButtonEventArgs e)
    {
        // Walk up from the element under the pointer to find the ListBoxItem.
        var dep = e.OriginalSource as DependencyObject;
        while (dep is not null and not ListBoxItem)
            dep = VisualTreeHelper.GetParent(dep);

        if (dep is ListBoxItem lbi && lbi.Content is string text)
        {
            // Prevent the click from transferring keyboard focus away from the text source,
            // which would otherwise close the popup before acceptance completes.
            e.Handled = true;
            AcceptItem(text);
        }
    }

    // ── Accept logic ──────────────────────────────────────────────────────────

    private void AcceptSelected()
    {
        var item = _listBox.SelectedItem as ListBoxItem
            ?? _listBox.Items.OfType<ListBoxItem>().FirstOrDefault();
        if (item?.Content is string text)
            AcceptItem(text);
        else
            _popup.IsOpen = false;
    }

    private void AcceptItem(string text)
    {
        _popup.IsOpen = false;
        _suppressUpdate = true;
        try   { _acceptCallback(text); }
        finally { _suppressUpdate = false; }
        // Only re-evaluate if the accepted text (up to the caret) ends with ": ", meaning
        // a parameter value is expected next.  Checking text[..caret] rather than
        // Text.EndsWith keeps multi-line boxes (commandsBefore/After) correct — the
        // current line may not be at the very end of all text.
        var fullText = _textSource.Text;
        var caret    = Math.Clamp(_textSource.CaretIndex, 0, fullText.Length);
        if (fullText[..caret].EndsWith(": "))
        {
            // Treat post-acceptance parameter prompting as user-initiated so the
            // popup opens to suggest parameter values.
            _triggeredByTyping = true;
            _textSource.Dispatcher.InvokeAsync(UpdateSuggestions, DispatcherPriority.Background);
        }
    }

    // ── Suggestion refresh ────────────────────────────────────────────────────

    private void UpdateSuggestions()
    {
        // Capture and reset the typing flag before any early-return so it is
        // never left stale from a previous keystroke.
        bool wasTyping      = _triggeredByTyping;
        _triggeredByTyping  = false;

        if (_suppressUpdate || _disposed) return;
        // Only show suggestions when the user is actively editing the field.
        if (!_textSource.IsKeyboardFocused) return;

        var suggestions = _suggestionsProvider(_textSource.Text);

        _listBox.Items.Clear();
        foreach (var s in suggestions.Take(12))
        {
            var item = new ListBoxItem
            {
                Content  = s,
                Height   = 22,
                Padding  = new Thickness(6, 2, 6, 2),
                Focusable = false,
            };
            item.SetResourceReference(ListBoxItem.ForegroundProperty, "LabelText");
            item.SetResourceReference(ListBoxItem.FontSizeProperty,   "FontSizeBody");
            _listBox.Items.Add(item);
        }

        if (suggestions.Count > 0 && wasTyping)
        {
            // Only open the popup when the update was triggered by actual typing
            // (not by caret movement, selection change, or programmatic text load).
            _popup.IsOpen          = true;
            _listBox.SelectedIndex = -1; // Tab selects first item if none highlighted
        }
        else if (suggestions.Count == 0)
        {
            _popup.IsOpen = false;
        }
        // If suggestions exist but update was not triggered by typing,
        // leave the popup in its current state (open → stays open, closed → stays closed).
    }

    // ── IDisposable ───────────────────────────────────────────────────────────

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _textSource.TextChanged      -= OnTextChanged;
        _textSource.PreviewKeyDown   -= OnPreviewKeyDown;
        _textSource.PreviewTextInput -= OnPreviewTextInput;
        _placementTarget.IsKeyboardFocusWithinChanged -= OnFocusWithinChanged;
        _placementTarget.Loaded -= OnPlacementTargetLoaded;
        _listBox.PreviewMouseLeftButtonDown -= OnListBoxPreviewMouseDown;
        if (_ownerWindow is not null)
        {
            _ownerWindow.LocationChanged -= OnWindowPositionChanged;
        }
        _popup.IsOpen = false;
    }
}
