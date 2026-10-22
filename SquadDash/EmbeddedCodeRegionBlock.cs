using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Documents;
using System.Windows.Threading;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Media3D;
using VoiceHeuristics;

namespace SquadDash;

/// <summary>
/// An embedded code region hosted inside a plain-text <see cref="RichTextBox"/>'s
/// <see cref="System.Windows.Documents.FlowDocument"/> via a
/// <see cref="System.Windows.Documents.BlockUIContainer"/>. Created by
/// <see cref="EmbeddedCodeRegionManager"/> when the user types or pastes a triple-backtick
/// fenced block into the prompt editor (Step 2 of the "Upgrade Prompt Editor with Embedded
/// Code Regions" plan).
/// <para>
/// Visually reuses the established Consolas + "CodeSurface"/"CodeText" code-block convention
/// from <see cref="CopyableCodeBlockBuilder"/> so embedded regions look consistent with the
/// read-only code blocks already rendered in transcripts.
/// </para>
/// <para>
/// The language picker writes <see cref="Language"/> directly so mode changes participate in
/// fence serialization (<see cref="ToFencedText"/>) and the shared prompt undo timeline.
/// </para>
/// </summary>
public sealed class EmbeddedCodeRegionBlock : Border
{
    /// Fence content, excluding the opening/closing ``` markers and the language tag.
    public string Content { get; set; }

    /// Persistent fence token: empty for auto detect, "text" for plain text, otherwise a grammar.
    public new string Language
    {
        get => _language;
        set
        {
            if (_disposed)
                return;

            var token = value ?? string.Empty;
            if (_language == token)
                return;
            AnyRegionContentEditing?.Invoke(this, EventArgs.Empty);
            _language = token;
            PickerModeChanged?.Invoke(this, EventArgs.Empty);
            UpdateLanguageLabel();
            AnyRegionContentEdited?.Invoke(this, EventArgs.Empty);
        }
    }

    /// Selected mode and token drive highlighting; auto-detection never changes the stored token.
    public RegionLanguageMode PickerMode => RegionLanguagePicker.GetMode(Language);
    public event EventHandler? PickerModeChanged;

    private string _language;
    private readonly TextBlock _languageLabel;
    private readonly Button _languageButton;
    private readonly Popup _languagePopup;
    internal Popup LanguagePickerPopup => _languagePopup;
    private readonly IRegionCodeEditor _editor;
    private readonly Control _editorControl;
    private RichTextBox? _hostRichTextBox;
    private bool _isClosingFromTrailingFenceLine;
    private bool _disposed;

    /// <summary>
    /// The embedded code region the user most recently entered (via click, arrow-key entry, or a
    /// just-typed opening fence), tracked independently of <see cref="Keyboard.FocusedElement"/>.
    /// <para>
    /// This exists because the host prompt <see cref="RichTextBox"/> can grab OS keyboard focus
    /// back from the AvalonEdit <c>TextArea</c> while the region's caret is still visibly blinking
    /// inside it. In that leaked state a space/printable keystroke would otherwise land in the
    /// host prompt. The host's <c>PreviewTextInput</c> guard uses this reference (plus
    /// <see cref="EmbeddedRegionInputRouter"/>) to redirect the character back into the region the
    /// user is actually working in. It is deliberately NOT cleared merely because the host stole
    /// focus — only on a genuine exit (<see cref="FocusHostOutsideRegion"/>), a click outside any
    /// region, or focus moving to an unrelated control.
    /// </para>
    /// </summary>
    internal static EmbeddedCodeRegionBlock? ActiveRegion { get; private set; }

    /// <summary>
    /// Raised whenever a region's language token or text INSIDE its editor changes (the region's own
    /// AvalonEdit edit, which never surfaces as the host <see cref="RichTextBox"/>'s TextChanged).
    /// The single-authority prompt undo/redo owner (<c>MainWindow</c> driving
    /// <see cref="PromptUndoHistory"/>, PROMPTUNDO-20261015-003) subscribes once so in-region typing
    /// pushes a snapshot onto the one shared timeline, exactly like host-text edits do.
    /// </summary>
    internal static event EventHandler? AnyRegionContentEdited;

    /// Raised before a region document/token update, while its original selection and text still exist.
    internal static event EventHandler? AnyRegionContentEditing;

    /// Clears <see cref="ActiveRegion"/> unconditionally. Called when a host-prompt gesture makes
    /// clear the user has left region editing (e.g. a click landing outside every region).
    internal static void ClearActiveRegion() => ActiveRegion = null;

    internal bool IsDisposed => _disposed;

    internal void DisposeRegionResources()
    {
        if (_disposed)
            return;

        _disposed = true;
        if (ReferenceEquals(ActiveRegion, this))
            ActiveRegion = null;

        _languagePopup.IsOpen = false;
        PickerModeChanged = null;
        _hostRichTextBox = null;

        if (_editor is IDisposable disposable)
            disposable.Dispose();
    }

    public static EmbeddedCodeRegionBlock Build(string content, string language)
        => new(content, language);

    /// <summary>
    /// Decides whether the host prompt <see cref="RichTextBox"/>'s
    /// <c>PreviewMouseLeftButtonDown</c> should claim (mark handled + force focus) a left-click
    /// that landed inside an embedded code region.
    /// <para>
    /// Returns <c>false</c> when the click landed on the region's nested content
    /// <see cref="TextBox"/> (<paramref name="hitContentTextBox"/>) or on the collapse toggle
    /// (<paramref name="hitToggle"/>) or language picker (<paramref name="hitLanguagePicker"/>).
    /// For a content-box click the host MUST NOT handle the
    /// Preview event: doing so suppresses the bubbling <c>MouseLeftButtonDown</c> the TextBox
    /// relies on for native caret placement, double-click word-select, click-drag and
    /// double-click-drag selection — the regression this guard previously caused.
    /// </para>
    /// <para>
    /// Returns <c>true</c> only for clicks on region chrome with no nested
    /// editor, toggle, or picker under the cursor, where there is no native interaction to preserve
    /// and the gesture should instead focus the region's content.
    /// </para>
    /// </summary>
    public static bool ShouldClaimRegionMouseGesture(
        bool hitRegion, bool hitToggle, bool hitContentTextBox, bool hitLanguagePicker = false)
        => hitRegion && !hitToggle && !hitContentTextBox && !hitLanguagePicker;

    private EmbeddedCodeRegionBlock(string content, string language)
    {
        Content = content ?? string.Empty;
        _language = language ?? string.Empty;
        Tag = "embedded-code-region";

        _editor = new AvalonEditRegionEditor(Content);
        _editorControl = _editor.Control;
        _editor.SetLanguage(Language);
        PickerModeChanged += (_, _) => _editor.SetLanguage(Language);
        _editor.HighlightingChanged += (_, _) => UpdateLanguageLabel();

        // Hosted controls inside a RichTextBox's BlockUIContainer must stop the left-button
        // gesture from reaching the host RichTextBox — otherwise WPF object-selects the whole
        // BlockUIContainer instead of leaving the nested editor's caret/selection intact.
        //
        // The subtlety: we must NOT do this at the Preview (tunneling) stage, and we must NOT
        // reset the caret/selection ourselves. AvalonEdit's TextArea implements single-click
        // caret, double-click word-select, click-drag and double-click-drag selection on the
        // BUBBLING MouseLeftButtonDown it receives. Marking the gesture handled at Preview (or
        // calling FocusContentAtPoint, which collapses the selection) cancels all of that native
        // behavior — that was the selection bug. So we attach at the bubbling stage with
        // handledEventsToo: by the time this runs AvalonEdit has already applied the user's
        // native gesture, and we only need to (a) keep the host's own selection collapsed and
        // (b) mark the event handled so it never bubbles up to the host for object-selection.
        _editorControl.AddHandler(
            UIElement.MouseLeftButtonDownEvent,
            new MouseButtonEventHandler((_, e) =>
            {
                var point = e.GetPosition(_editorControl);
                Trace($"EMBEDDED_CODE_CONTENT_MOUSE enter handled={e.Handled} point=({point.X:F1},{point.Y:F1}) caret={_editor.CaretOffset} selection={_editor.SelectionStart}+{_editor.SelectionLength} focused={_editor.IsKeyboardFocusWithin} source={DescribeInputObject(e.OriginalSource)}");
                // Collapse the host RichTextBox selection, and ensure keyboard focus has landed
                // in the region — but deliberately leave the editor's caret/selection exactly
                // as AvalonEdit's native handling just set them, so native text selection works.
                ClearHostSelection();
                if (!_editor.IsKeyboardFocusWithin)
                    _editor.FocusEditor();
                e.Handled = true;
                Trace($"EMBEDDED_CODE_CONTENT_MOUSE exit handled=True caret={_editor.CaretOffset} selection={_editor.SelectionStart}+{_editor.SelectionLength} focused={_editor.IsKeyboardFocusWithin} keyboardFocus={DescribeInputObject(Keyboard.FocusedElement)}");
            }),
            handledEventsToo: true);

        // Keyboard-focus tracking for the active-region safety net. When the editor's text-input
        // surface gains keyboard focus, this region becomes the active one. When it loses focus,
        // we KEEP it active if the host prompt grabbed focus (the leak we defend against) or if
        // focus stayed within this region, but clear it when focus genuinely moved to an unrelated
        // control. (A sibling region that gains focus overwrites ActiveRegion via its own
        // GotKeyboardFocus, which fires after this LostKeyboardFocus.)
        _editorControl.GotKeyboardFocus += (_, _) => ActiveRegion = this;
        _editorControl.LostKeyboardFocus += (_, e) =>
        {
            var newFocus = e.NewFocus as DependencyObject;
            var focusStayedInRegion = newFocus is not null && IsVisualAncestorOrSelf(newFocus, this);

            // "Only one live selection at a time": when keyboard focus genuinely leaves this region
            // — to the host prompt (clicking/selecting outside) or to any other control — collapse
            // this region's own selection to a zero-length caret so two highlighted selections can't
            // coexist. This is computed independently of the ActiveRegion leak-defense bookkeeping
            // below and runs EVEN in the "host grabbed focus" branch, because losing keyboard focus
            // to the host is exactly the "caret moved outside" case that must be cleared. It is
            // selection-only (SelectionLength=0 maps to TextArea.ClearSelection) — the region text
            // and caret position are untouched. We never clear while focus stays within this same
            // region, so in-region selecting and fenced Ctrl+C/Ctrl+X (read while focused) are
            // unaffected.
            if (!focusStayedInRegion && _editor.SelectionLength > 0)
            {
                _editor.SelectionLength = 0;
                Trace("EMBEDDED_CODE_SELECTION_CLEARED reason=lost-focus");
            }

            if (!ReferenceEquals(ActiveRegion, this))
                return;
            if (newFocus is null)
                return;
            if (focusStayedInRegion)
                return; // Still inside this region.
            if (_hostRichTextBox is not null && IsVisualAncestorOrSelf(newFocus, _hostRichTextBox))
                return; // Host prompt grabbed focus — keep active so the net can redirect typing.
            ActiveRegion = null;
        };

        // Deterministic Space handling: insert an unmodified Space into THIS region's own document
        // at PreviewKeyDown — before WPF's text-input pipeline can divert it to the host RichTextBox,
        // which special-cases Space in its own TextBoxBase editor and otherwise steals the keystroke
        // (the space would land in the host prompt instead of the focused region). See
        // EmbeddedRegionSpaceRouter for the pure decision. Diagnostic trace retained.
        _editorControl.PreviewKeyDown += (_, e) =>
        {
            if (e.Key != Key.Space)
                return;

            // Focus is genuinely inside THIS region only when the editor owns keyboard focus AND
            // this region is the tracked active one — mirrors how the nav/redirect handlers gate
            // themselves, so a stale/unfocused region never swallows a Space.
            var regionHasFocus = _editor.IsKeyboardFocusWithin && ReferenceEquals(ActiveRegion, this);
            Trace($"EMBEDDED_CODE_SPACE_KEY textAreaFocused={_editor.IsTextInputSurfaceFocused} focusWithin={_editor.IsKeyboardFocusWithin} active={ReferenceEquals(ActiveRegion, this)} keyboardFocus={DescribeInputObject(Keyboard.FocusedElement)}");

            if (!EmbeddedRegionSpaceRouter.ShouldInsertSpaceIntoRegion(e.Key, Keyboard.Modifiers, regionHasFocus))
                return;

            // Insert the space authoritatively before WPF's text-input pipeline can divert it to the
            // host RichTextBox (which special-cases Space in its own editor and would otherwise steal
            // it). Marking KeyDown handled suppresses the subsequent TextInput, so there is no
            // double-insert and no host leak. Modified combos (Shift+Space Smooth Dictation, etc.) are
            // never reached here because the router requires ModifierKeys.None.
            _editor.InsertText(" ");
            e.Handled = true;
            Trace($"EMBEDDED_CODE_SPACE_INSERT caret={_editor.CaretOffset} textLen={_editor.TextLength}");
        };

        // Keep Content (the serialization source of truth read by ToFencedText/GetPlainText)
        // in sync with every edit made directly inside the region. The editor's TextChanged
        // never bubbles up as the host RichTextBox's own TextChanged (it only fires for
        // FlowDocument/TextContainer content changes, not a nested control's Text property),
        // so this is isolated from EmbeddedCodeRegionManager's fence-scanning rebuilds — and
        // because those rebuilds reattach this exact same BlockUIContainer/control instance
        // (see EmbeddedCodeRegionManager.RenderSegments's ExistingContainer branch) rather
        // than recreating it, in-progress edits are never lost across a rebuild.
        _editor.TextChanging += (_, _) => AnyRegionContentEditing?.Invoke(this, EventArgs.Empty);
        _editor.TextChanged += (_, _) =>
        {
            Content = _editor.Text;
            TryCloseOnTrailingFenceLine();
            // Notify the single-authority undo/redo owner that in-region text changed, so it can
            // push a snapshot onto the one shared prompt timeline (host TextChanged does NOT fire
            // for a nested editor's own edits). PROMPTUNDO-20261015-003.
            AnyRegionContentEdited?.Invoke(this, EventArgs.Empty);
        };

        // Handle Ctrl+C (Copy) and Ctrl+X (Cut) to provide fenced text to clipboard
        _editorControl.PreviewKeyDown += (_, e) =>
        {
            // When user selects all or partial content and copies/cuts, provide the fenced representation
            if ((e.Key == Key.C || e.Key == Key.X) && (Keyboard.Modifiers & ModifierKeys.Control) != 0)
            {
                if (_editor.SelectionLength > 0)
                {
                    // User selected text within the region. Get the selected text and wrap in fences.
                    var selectedText = _editor.SelectedText;
                    var fencedText = EmbeddedFenceDetector.BuildFencedText(selectedText, Language);
                    if (!TrySetClipboardText(fencedText))
                    {
                        e.Handled = true;
                        return;
                    }

                    if (e.Key == Key.X)
                    {
                        // For Cut, delete the selection after copying to clipboard.
                        _editor.RemoveSelectedText();
                    }

                    e.Handled = true;
                    return;
                }
            }
        };

        // Allow navigation exits from the region: Tab, arrow keys to move focus out
        _editorControl.PreviewKeyDown += (_, e) =>
        {
            var traceNavigationKey = e.Key is Key.Tab or Key.Down or Key.Up or Key.Left or Key.Right;
            if (traceNavigationKey)
            {
                Trace(
                    $"EMBEDDED_CODE_KEY enter key={e.Key} modifiers={Keyboard.Modifiers} handled={e.Handled} caret={_editor.CaretOffset} line={GetCaretLineIndexForTrace()} lineCount={GetLineCountForTrace()} selection={_editor.SelectionStart}+{_editor.SelectionLength}");
            }

            // Shift+Tab moves focus to the previous text position outside the region.
            if (e.Key == Key.Tab && (Keyboard.Modifiers & ModifierKeys.Shift) != 0)
            {
                Trace("EMBEDDED_CODE_KEY_EXIT reason=shift-tab direction=Backward");
                FocusHostOutsideRegion(LogicalDirection.Backward);
                e.Handled = true;
                return;
            }

            // Tab always moves focus to the next text position after the region.
            if (e.Key == Key.Tab)
            {
                Trace("EMBEDDED_CODE_KEY_EXIT reason=tab direction=Forward");
                FocusHostOutsideRegion(LogicalDirection.Forward);
                e.Handled = true;
                return;
            }

            // Ctrl+Down always moves focus out (convention for exiting nested editors)
            if (e.Key == Key.Down && (Keyboard.Modifiers & ModifierKeys.Control) != 0)
            {
                Trace("EMBEDDED_CODE_KEY_EXIT reason=ctrl-down direction=Forward");
                FocusHostOutsideRegion(LogicalDirection.Forward);
                e.Handled = true;
                return;
            }

            // Ctrl+Up always moves focus out (convention for exiting nested editors)
            if (e.Key == Key.Up && (Keyboard.Modifiers & ModifierKeys.Control) != 0)
            {
                Trace("EMBEDDED_CODE_KEY_EXIT reason=ctrl-up direction=Backward");
                FocusHostOutsideRegion(LogicalDirection.Backward);
                e.Handled = true;
                return;
            }

            // Unmodified Left/Right/Up/Down: a PURE, position-only decision that depends solely on
            // (text, caretIndex, selectionLength, key) — never on how the caret entered the region or
            // WPF's remembered desired-X column. See EmbeddedCaretNavMath for the decision table.
            if (e.Key is Key.Left or Key.Right or Key.Up or Key.Down)
            {
                var decision = EmbeddedCaretNavMath.Decide(
                    _editor.Text, _editor.CaretOffset, _editor.SelectionLength, e.Key);

                switch (decision)
                {
                    case EmbeddedCaretNavMath.Decision.ExitBackward:
                        var landAtStart = EmbeddedCaretNavMath.ExitBackwardLandsAtParagraphStart(_editor.CaretOffset, e.Key);
                        Trace($"EMBEDDED_CODE_KEY_EXIT reason={e.Key}-exit direction=Backward caret={_editor.CaretOffset} textLen={_editor.TextLength} landAtStart={landAtStart}");
                        FocusHostOutsideRegion(LogicalDirection.Backward, landAtStart);
                        e.Handled = true;
                        return;

                    case EmbeddedCaretNavMath.Decision.ExitForward:
                        Trace($"EMBEDDED_CODE_KEY_EXIT reason={e.Key}-exit direction=Forward caret={_editor.CaretOffset} textLen={_editor.TextLength}");
                        FocusHostOutsideRegion(LogicalDirection.Forward);
                        e.Handled = true;
                        return;

                    case EmbeddedCaretNavMath.Decision.PassThrough:
                        Trace($"EMBEDDED_CODE_KEY pass-through reason={e.Key}-within-region caret={_editor.CaretOffset} line={GetCaretLineIndexForTrace()}");
                        // Don't set e.Handled; let the editor move the caret within the region.
                        break;
                }
            }

            if (traceNavigationKey && !e.Handled)
                Trace($"EMBEDDED_CODE_KEY pass-through key={e.Key} caret={_editor.CaretOffset} line={GetCaretLineIndexForTrace()}");
        };

        // Backspace in a COMPLETELY EMPTY region removes the whole inline region from the prompt
        // and lands the caret where the region was, so typing continues in-place — the intuitive
        // "backspace at the start of an empty block deletes the block". Runs at PreviewKeyDown so
        // it beats AvalonEdit's own Backspace handling (same tunneling stage the Space/nav handlers
        // use). Gated to genuine focus in THIS region exactly like those handlers. A non-empty
        // region, or a modified Backspace (Ctrl/Alt/Shift), or any other key (Delete included)
        // falls through untouched to normal editing. See EmbeddedRegionBackspaceRouter.
        _editorControl.PreviewKeyDown += (_, e) =>
        {
            if (e.Key is not (Key.Back or Key.Delete))
                return;

            var regionHasFocus = _editor.IsKeyboardFocusWithin && ReferenceEquals(ActiveRegion, this);
            if (!regionHasFocus)
                return;

            if (EmbeddedRegionBackspaceRouter.ShouldRemoveEmptyRegionOnBackspace(e.Key, Keyboard.Modifiers, _editor.TextLength))
            {
                Trace($"EMBEDDED_CODE_BACKSPACE_REMOVE enter textLen={_editor.TextLength} active={ReferenceEquals(ActiveRegion, this)} keyboardFocus={DescribeInputObject(Keyboard.FocusedElement)}");
                if (TryRemoveEmptyRegionFromHost())
                    e.Handled = true;
                return;
            }

            if (TryHandleRegionDeleteKey(e.Key, Keyboard.Modifiers))
                e.Handled = true;
        };

        _languageLabel = new TextBlock
        {
            Text = RegionLanguagePicker.GetLabel(Language, _editor.HighlightingName),
            FontFamily = new FontFamily("Segoe UI"), // Variable width font
            FontSize = 12,
            VerticalAlignment = VerticalAlignment.Center,
            FontStyle = FontStyles.Italic,
            Background = System.Windows.Media.Brushes.Transparent,
        };
        var labelContent = new StackPanel { Orientation = Orientation.Horizontal };
        labelContent.Children.Add(_languageLabel);
        var arrow = new System.Windows.Shapes.Path
        {
            Data = Geometry.Parse("M 0,0 L 8,0 L 4,4 Z"),
            Margin = new Thickness(6, 0, 0, 0),
            VerticalAlignment = VerticalAlignment.Center,
        };
        arrow.SetResourceReference(System.Windows.Shapes.Shape.FillProperty, "LabelText");
        labelContent.Children.Add(arrow);
        _languageButton = new Button
        {
            Content = labelContent,
            HorizontalAlignment = HorizontalAlignment.Left,
            Margin = new Thickness(8, 2, 8, 2),
            ToolTip = "Choose code language",
            Cursor = Cursors.Hand,
        };
        _languageButton.SetResourceReference(StyleProperty, "RegionLanguageButtonStyle");
        System.Windows.Automation.AutomationProperties.SetName(
            _languageButton, $"Code language: {_languageLabel.Text}");
        _languagePopup = BuildLanguagePopup();
        _languageButton.Click += (_, e) =>
        {
            _languagePopup.IsOpen = !_languagePopup.IsOpen;
            e.Handled = true;
        };
        Unloaded += (_, _) => _languagePopup.IsOpen = false;

        // The editor rectangle chrome now lives on an INNER border so the label can sit
        // visually above (outside) it. The outer EmbeddedCodeRegionBlock itself stays
        // transparent (see below).
        var editorBorder = new Border
        {
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(3),
            Child = _editorControl,
        };
        editorBorder.SetResourceReference(BackgroundProperty, "CodeSurface");

        // Render the editor rectangle's BORDER at half the themed contrast WITHOUT fading
        // the content box, its CodeSurface background, or its text. We hold the live themed
        // "SubtleBorder" brush on the Border's (otherwise unused) Tag via a DynamicResource so
        // it re-resolves across theme switches, then bind a dedicated half-opacity
        // SolidColorBrush's Color to that resolved color. Only the border line gets the 0.5
        // alpha; the shared "SubtleBorder" resource is never mutated in place.
        editorBorder.SetResourceReference(FrameworkElement.TagProperty, "SubtleBorder");
        var halfContrastBorderBrush = new SolidColorBrush { Opacity = 0.5 };
        System.Windows.Data.BindingOperations.SetBinding(
            halfContrastBorderBrush,
            SolidColorBrush.ColorProperty,
            new System.Windows.Data.Binding("Tag.Color") { Source = editorBorder });
        editorBorder.BorderBrush = halfContrastBorderBrush;

        var stack = new StackPanel();
        stack.Children.Add(_languageButton);
        stack.Children.Add(editorBorder);
        // Keep the Popup unparented: WPF then isolates its input route from the document.
        // Parenting it here lets region/host preview handlers swallow option and outside clicks.

        // The outer element (this Border) is purely a transparent layout host now; the
        // visible editor chrome is painted by the inner editorBorder above.
        BorderThickness = new Thickness(0);
        Margin = new Thickness(0, 2, 0, 2);
        Child = stack;

        // Background clicks focus the editor; native editor and picker clicks must pass through.
        PreviewMouseLeftButtonDown += (sender, e) =>
        {
            var clickedElement = InputHitTest(e.GetPosition(this)) as DependencyObject;
            Trace($"EMBEDDED_CODE_BORDER_MOUSE enter handled={e.Handled} hit={DescribeInputObject(clickedElement)} point=({e.GetPosition(this).X:F1},{e.GetPosition(this).Y:F1})");
            if (IsVisualAncestorOrSelf(clickedElement, _editorControl) ||
                IsLanguagePickerHit(clickedElement))
            {
                Trace("EMBEDDED_CODE_BORDER_MOUSE exit reason=nested-control");
                return; // Let the editor select or the language button open its picker.
            }

            // Background clicks focus the editor without selecting the whole hosted block.
            _editor.FocusEditor();
            e.Handled = true;
            Trace($"EMBEDDED_CODE_BORDER_MOUSE exit reason=header-focus handled=True focused={_editor.IsKeyboardFocusWithin} keyboardFocus={DescribeInputObject(Keyboard.FocusedElement)}");
        };
    }

    internal bool IsLanguagePickerHit(DependencyObject? element)
        => IsVisualAncestorOrSelf(element, _languageButton);

    internal static EmbeddedCodeRegionBlock? FindContainingRegion(DependencyObject? element)
    {
        while (element is not null)
        {
            if (element is EmbeddedCodeRegionBlock region)
                return region;
            // The picker has no document parent; its placement target retains region ownership.
            element = element is Popup popup
                ? popup.PlacementTarget
                : LogicalTreeHelper.GetParent(element) ??
                    (element is Visual or Visual3D ? VisualTreeHelper.GetParent(element) : null);
        }
        return null;
    }

    private Popup BuildLanguagePopup()
    {
        var items = new StackPanel();
        KeyboardNavigation.SetDirectionalNavigation(items, KeyboardNavigationMode.Cycle);
        KeyboardNavigation.SetTabNavigation(items, KeyboardNavigationMode.Cycle);
        var border = new Border
        {
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(3),
            Padding = new Thickness(4),
            Child = new ScrollViewer
            {
                MaxHeight = 320,
                MinWidth = 180,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
                Content = items,
            },
        };
        border.SetResourceReference(BackgroundProperty, "CardSurface");
        border.SetResourceReference(BorderBrushProperty, "SubtleBorder");
        var popup = new Popup
        {
            PlacementTarget = _languageButton,
            Placement = PlacementMode.Bottom,
            StaysOpen = false,
            AllowsTransparency = true,
            Child = border,
        };
        foreach (var option in RegionLanguagePicker.Options)
        {
            var button = new Button
            {
                Content = option.DisplayName,
                Tag = option,
                Padding = new Thickness(8, 4, 8, 4),
            };
            button.SetResourceReference(StyleProperty, "RegionLanguageButtonStyle");
            button.Click += (_, e) =>
            {
                Language = option.FenceToken;
                popup.IsOpen = false;
                _languageButton.Focus();
                e.Handled = true;
            };
            items.Children.Add(button);
        }
        popup.Opened += (_, _) =>
        {
            Button? selected = null;
            foreach (Button button in items.Children)
            {
                var option = (RegionLanguageOption)button.Tag;
                var isSelected = string.Equals(option.FenceToken, Language, StringComparison.OrdinalIgnoreCase);
                button.FontWeight = isSelected ? FontWeights.Bold : FontWeights.Normal;
                System.Windows.Automation.AutomationProperties.SetHelpText(button, isSelected ? "Selected" : "");
                if (isSelected)
                    selected = button;
            }
            var focusTarget = selected ?? (Button)items.Children[0];
            focusTarget.Focus();
            focusTarget.BringIntoView();
        };
        border.PreviewKeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape)
            {
                popup.IsOpen = false;
                _languageButton.Focus();
                e.Handled = true;
            }
            else if (e.Key is Key.Enter or Key.Space && Keyboard.Modifiers == ModifierKeys.None)
            {
                var button = items.Children.Cast<Button>().FirstOrDefault(
                    item => IsVisualAncestorOrSelf(e.OriginalSource as DependencyObject, item));
                if (button is not null)
                {
                    e.Handled = true;
                    button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                }
            }
        };
        return popup;
    }

    internal void AttachHost(RichTextBox hostRichTextBox)
    {
        if (_disposed)
            return;

        _hostRichTextBox = hostRichTextBox;
        _editor.MaxHeight = (double.IsNaN(hostRichTextBox.MaxHeight) || double.IsInfinity(hostRichTextBox.MaxHeight))
            ? 130
            : hostRichTextBox.MaxHeight / 2.0;

        // Code regions render at the SAME point size as the surrounding prompt text (1:1). The
        // host RichTextBox's FontSize already tracks the prompt font size (set by
        // MainWindow.ApplyPromptFontSize), so a region that is newly created OR rehydrated after
        // the user zoomed the prompt picks up the current size here, at attach time, rather than
        // staying at the hardcoded construction default until the next ApplyPromptFontSize.
        var hostFontSize = hostRichTextBox.FontSize;
        if (!double.IsNaN(hostFontSize) && hostFontSize > 0)
            _editor.EditorFontSize = hostFontSize;
    }

    /// <summary>
    /// Sets this region editor's point size so code regions scale together with the surrounding
    /// prompt text. Called from <c>MainWindow.ApplyPromptFontSize</c>'s per-region fan-out when
    /// the user Ctrl+wheels the prompt font size (AvalonEdit font size is not DynamicResource-
    /// driven, so it must be pushed imperatively — mirrors <see cref="ApplyEditorTheme"/>).
    /// </summary>
    public void ApplyEditorFontSize(double fontSize)
    {
        if (_disposed)
            return;

        if (double.IsNaN(fontSize) || fontSize <= 0)
            return;
        _editor.EditorFontSize = fontSize;
    }

    /// <summary>
    /// Pushes the given <paramref name="palette"/> onto this region's editor so its surface/text
    /// and syntax colors follow the active theme. Called from <c>MainWindow.ApplyTheme</c>'s per-region
    /// fan-out after a theme dictionary swap (AvalonEdit colors are not DynamicResource-driven,
    /// so they must be re-applied imperatively; see AVALONEDIT-20261014-001).
    /// </summary>
    public void ApplyEditorTheme(RegionEditorPalette palette)
    {
        if (!_disposed)
            _editor.ApplyPalette(palette);
    }

    public void NotifyThemeChanged(RegionEditorPalette palette) => ApplyEditorTheme(palette);

    private void UpdateLanguageLabel()
    {
        _languageLabel.Text = RegionLanguagePicker.GetLabel(Language, _editor.HighlightingName);
        System.Windows.Automation.AutomationProperties.SetName(
            _languageButton, $"Code language: {_languageLabel.Text}");
    }

    /// Serializes this region back into an equivalent triple-backtick fenced block:
    /// a bare fence if no language is set, or ```&lt;language&gt; if one is present.
    /// This is the contract consumed by <see cref="RichTextBoxExtensions.GetPlainText"/>.
    public string ToFencedText() => EmbeddedFenceDetector.BuildFencedText(Content, Language);

    /// <summary>
    /// Gives keyboard focus to this region's content box and places the caret at the end of
    /// its (ordinarily empty) text. Called immediately after
    /// <see cref="EmbeddedCodeRegionManager.TryConvertOpenFence"/> creates a region from a
    /// just-typed opening fence marker, so the user's very next keystroke lands in the
    /// region's content instead of back out in the surrounding prompt text.
    /// </summary>
    public void FocusContent()
    {
        if (_disposed)
            return;

        Trace($"EMBEDDED_CODE_FOCUS_END enter textLen={_editor.TextLength} caretBefore={_editor.CaretOffset} selection={_editor.SelectionStart}+{_editor.SelectionLength} visible={_editorControl.IsVisible} enabled={_editorControl.IsEnabled} keyboardFocus={DescribeInputObject(Keyboard.FocusedElement)}");
        ActiveRegion = this;
        ClearHostSelection();
        _editor.CaretOffset = _editor.TextLength;
        // FocusEditor sets logical/keyboard focus within the control's focus scope even before
        // the element has gone through a layout pass against a real PresentationSource (e.g. right
        // after this region is created, before WPF has had a chance to render it) and then also
        // attempts real OS keyboard focus for the already-rendered case.
        _editor.FocusEditor();
        Trace($"EMBEDDED_CODE_FOCUS_END exit caret={_editor.CaretOffset} focused={_editor.IsKeyboardFocusWithin} keyboardFocus={DescribeInputObject(Keyboard.FocusedElement)}");
    }

    /// <summary>
    /// True when keyboard focus is currently inside this region's content editor. Used by
    /// <see cref="PromptUndoManager.CaptureSnapshot"/> to decide whether the live caret/anchor are
    /// region-local (and must be mapped into the absolute flat prompt coordinate space) rather than
    /// read from the host <see cref="RichTextBox"/> caret, which only ever sits at the region's
    /// element boundary while focus is really inside the nested editor.
    /// </summary>
    internal bool HasContentKeyboardFocus => !_disposed && _editor.IsKeyboardFocusWithin;

    /// <summary>
    /// Applies a Backspace/Delete key directly to the nested editor and marks the key handled
    /// before WPF's parent <see cref="RichTextBox"/> can translate it into its own native
    /// editing command. That parent path can fail-fast around hosted <see cref="BlockUIContainer"/>
    /// regions, so deletion that belongs to a region must never fall through to it.
    /// </summary>
    internal bool TryHandleRegionDeleteKey(Key key, ModifierKeys modifiers)
    {
        if (_disposed)
            return false;

        if (key is not (Key.Back or Key.Delete))
            return false;
        if (modifiers != ModifierKeys.None)
            return false;

        ActiveRegion = this;
        ClearHostSelection();

        var selectionLength = _editor.SelectionLength;
        if (selectionLength > 0)
        {
            var start = _editor.SelectionStart;
            Trace($"EMBEDDED_CODE_REGION_DELETE selection key={key} start={start} length={selectionLength}");
            _editor.DeleteText(start, selectionLength);
            _editor.FocusEditor();
            return true;
        }

        var caret = _editor.CaretOffset;
        if (key == Key.Back)
        {
            if (caret <= 0)
            {
                _editor.FocusEditor();
                Trace("EMBEDDED_CODE_REGION_DELETE noop reason=start-of-region");
                return true;
            }

            Trace($"EMBEDDED_CODE_REGION_DELETE backward offset={caret - 1}");
            _editor.DeleteText(caret - 1, 1);
            _editor.FocusEditor();
            return true;
        }

        if (caret >= _editor.TextLength)
        {
            _editor.FocusEditor();
            Trace("EMBEDDED_CODE_REGION_DELETE noop reason=end-of-region");
            return true;
        }

        Trace($"EMBEDDED_CODE_REGION_DELETE forward offset={caret}");
        _editor.DeleteText(caret, 1);
        _editor.FocusEditor();
        return true;
    }

    /// <summary>
    /// The editor's current selection as (anchorLocal, caretLocal) offsets within the region's
    /// content text, preserving direction: for a forward selection the anchor is the lower offset
    /// and the caret the higher; for a backward selection they swap; with no selection both equal
    /// the caret. These are the region-local halves that
    /// <see cref="PromptUndoManager.CaptureSnapshot"/> lifts into the absolute flat coordinate space
    /// by adding the region's content-base offset.
    /// </summary>
    internal (int anchorLocal, int caretLocal) GetContentSelectionLocal()
        => _disposed ? (0, 0) : _editor.GetSelectionOffsets();

    /// <summary>
    /// Restores a selection inside this region's content editor from region-local
    /// <paramref name="anchorLocal"/>/<paramref name="caretLocal"/> offsets (preserving direction),
    /// routes keyboard focus into the editor, and marks this the active region. The in-region arm of
    /// <see cref="PromptUndoManager.ApplySnapshot"/> for a selection that lives wholly inside this
    /// region (including a collapsed caret, where the two offsets are equal).
    /// </summary>
    public void RestoreContentSelection(int anchorLocal, int caretLocal)
    {
        if (_disposed)
            return;

        Trace($"EMBEDDED_CODE_RESTORE_SELECTION enter anchor={anchorLocal} caret={caretLocal} textLen={_editor.TextLength} visible={_editorControl.IsVisible}");
        ActiveRegion = this;
        ClearHostSelection();
        _editor.RestoreSelection(anchorLocal, caretLocal);
        _editor.FocusEditor();
        Trace($"EMBEDDED_CODE_RESTORE_SELECTION exit caret={_editor.CaretOffset} selection={_editor.SelectionStart}+{_editor.SelectionLength} focused={_editor.IsKeyboardFocusWithin}");
    }

    /// <summary>
    /// Gives keyboard focus to this region's content editor and places the caret at the specified
    /// position within the text. Used for smart arrow-key navigation to enter the region with
    /// caret at the correct position (start, end, or specific offset).
    /// </summary>
    public void FocusContentAtPosition(int caretPosition)
    {
        if (_disposed)
            return;

        var clampedPosition = Math.Clamp(caretPosition, 0, _editor.TextLength);
        Trace($"EMBEDDED_CODE_FOCUS_POSITION enter position={caretPosition} clamped={clampedPosition} textLen={_editor.TextLength} caretBefore={_editor.CaretOffset} visible={_editorControl.IsVisible}");
        ActiveRegion = this;
        ClearHostSelection();
        _editor.CaretOffset = clampedPosition;
        _editor.SelectionLength = 0;
        _editor.FocusEditor();
        Trace($"EMBEDDED_CODE_FOCUS_POSITION exit caret={_editor.CaretOffset} focused={_editor.IsKeyboardFocusWithin}");
    }

    /// <summary>
    /// Inserts voice-dictated <paramref name="dictatedText"/> at this region's current caret
    /// (applying the shared <see cref="VoiceInsertionHeuristics"/> leading-space / replacement
    /// conditioning), so push-to-talk dictation while the region holds keyboard focus lands
    /// INSIDE the region instead of leaking out to the host prompt. The editor's
    /// <c>TextChanged</c>→<see cref="Content"/> sync keeps the model in step.
    /// </summary>
    public void InsertDictation(string dictatedText, IReadOnlyList<VoiceReplacementRule> replacementRules)
    {
        if (_disposed)
            return;

        var selLength = _editor.SelectionLength;
        var caret = selLength > 0 ? _editor.SelectionStart : _editor.CaretOffset;
        var plan = VoiceInsertionHeuristics.PlanDictationInsertion(
            _editor.Text, caret, selLength, dictatedText, replacementRules);
        SquadDashTrace.Write(
            TraceCategory.Avalon,
            $"AVALON_PTT_REGION_PLAN caret={caret} selection={selLength} textLen={_editor.TextLength} " +
            $"caretSource=current-region liveCaret={_editor.CaretOffset} " +
            $"planCaret={plan.CaretIndex} planSelection={plan.SelectionLength} insertedLen={plan.ReplacedText.Length} " +
            $"planCaretAfterInsert={plan.CaretIndexAfterInsert} planCaretAfterReplace={plan.CaretIndexAfterReplace}");
        Trace($"EMBEDDED_CODE_DICTATION enter caret={caret} selection={selLength} textLen={_editor.TextLength} inserted-len={plan.ReplacedText.Length}");
        _editor.InsertText(plan.ReplacedText);
        SquadDashTrace.Write(
            TraceCategory.Avalon,
            $"AVALON_PTT_REGION_INSERT_POST caret={_editor.CaretOffset} textLen={_editor.TextLength}");
        Trace($"EMBEDDED_CODE_DICTATION exit caret={_editor.CaretOffset} textLen={_editor.TextLength}");
    }

    /// <summary>
    /// Inserts a printable character (a space or any other text) that the host prompt intercepted
    /// in its <c>PreviewTextInput</c> guard because keyboard focus had leaked back to the host
    /// while this region was still the <see cref="ActiveRegion"/>. The character lands at the
    /// region's caret (replacing any selection) and keyboard focus is re-asserted onto the editor
    /// so the next keystroke routes natively, closing the leak. This is the safety net that makes
    /// a space-outside-the-region leak impossible even if focus ownership is momentarily imperfect.
    /// </summary>
    internal void InsertHostText(string text)
    {
        if (_disposed)
            return;

        Trace($"EMBEDDED_CODE_HOST_TEXT enter textLen-in={text?.Length ?? 0} caretBefore={_editor.CaretOffset} focusWithin={_editor.IsKeyboardFocusWithin} textAreaFocused={_editor.IsTextInputSurfaceFocused}");
        ClearHostSelection();
        _editor.InsertText(text ?? string.Empty);
        if (!_editor.IsKeyboardFocusWithin)
            _editor.FocusEditor();
        Trace($"EMBEDDED_CODE_HOST_TEXT exit caret={_editor.CaretOffset} textLen={_editor.TextLength} focusWithin={_editor.IsKeyboardFocusWithin} textAreaFocused={_editor.IsTextInputSurfaceFocused}");
    }

    /// <summary>
    /// True when this region is still a live child of its host prompt's document (i.e. it has not
    /// been removed by a prompt submit/clear). The host's typing-safety net checks this before
    /// routing a character into <see cref="ActiveRegion"/> so a stale reference to a removed
    /// region never swallows ordinary host typing.
    /// </summary>
    internal bool IsAttachedToHost()
    {
        if (_disposed)
            return false;

        var host = _hostRichTextBox;
        if (host is null)
            return false;

        return host.Document.Blocks
            .OfType<BlockUIContainer>()
            .Any(container => ReferenceEquals(container.Child, this));
    }

    internal void FocusContentAtPoint(Point pointInContentBox)
    {
        if (_disposed)
            return;

        Trace($"EMBEDDED_CODE_FOCUS_POINT enter point=({pointInContentBox.X:F1},{pointInContentBox.Y:F1}) textLen={_editor.TextLength} caretBefore={_editor.CaretOffset} selection={_editor.SelectionStart}+{_editor.SelectionLength} visible={_editorControl.IsVisible} enabled={_editorControl.IsEnabled} keyboardFocus={DescribeInputObject(Keyboard.FocusedElement)}");
        ActiveRegion = this;
        ClearHostSelection();
        _editor.FocusEditor();

        var caretIndex = GetCaretIndexFromPoint(pointInContentBox);
        _editor.CaretOffset = caretIndex;
        _editor.SelectionLength = 0;
        Trace($"EMBEDDED_CODE_FOCUS_POINT exit caret={_editor.CaretOffset} focused={_editor.IsKeyboardFocusWithin} keyboardFocus={DescribeInputObject(Keyboard.FocusedElement)}");
    }

    private void ClearHostSelection()
    {
        if (_disposed)
            return;

        var host = _hostRichTextBox;
        if (host is null)
        {
            Trace("EMBEDDED_CODE_CLEAR_HOST_SELECTION skipped reason=no-host");
            return;
        }

        var collapseTo = host.Selection.End;
        Trace($"EMBEDDED_CODE_CLEAR_HOST_SELECTION enter selectionEmpty={host.Selection.IsEmpty} selectionLength={GetHostSelectionLengthForTrace(host)} caretOffset={GetHostCaretOffsetForTrace(host)}");
        host.Selection.Select(collapseTo, collapseTo);
        Trace($"EMBEDDED_CODE_CLEAR_HOST_SELECTION exit selectionEmpty={host.Selection.IsEmpty} selectionLength={GetHostSelectionLengthForTrace(host)} caretOffset={GetHostCaretOffsetForTrace(host)}");
    }

    /// <summary>
    /// Judgment call for "how does the user finish an instantly-opened region": if the last
    /// line the user typed into the content box is a bare closing ``` by itself, treat that
    /// as "done" — strip the marker line back out of <see cref="Content"/> (it's implicit in
    /// <see cref="ToFencedText"/> already) and move keyboard focus back out to whatever's next
    /// in the surrounding prompt so ordinary typing resumes immediately after the region,
    /// mirroring how a pasted/complete fence already behaves. The region stays expanded.
    /// Guarded against re-entrancy because it mutates the editor's text from within that
    /// same editor's own TextChanged handler.
    /// </summary>
    private void TryCloseOnTrailingFenceLine()
    {
        if (_disposed)
            return;

        if (_isClosingFromTrailingFenceLine) return;

        var lines = Content.Split('\n');
        if (lines[^1].Trim() != "```") return;

        _isClosingFromTrailingFenceLine = true;
        try
        {
            var withoutClosingLine = string.Join("\n", lines.Take(lines.Length - 1));
            Content = withoutClosingLine;
            _editor.Text = withoutClosingLine;
            FocusHostOutsideRegion(LogicalDirection.Forward);
        }
        finally
        {
            _isClosingFromTrailingFenceLine = false;
        }
    }

    private int GetCaretIndexFromPoint(Point point) => _editor.GetOffsetFromPoint(point);

    private void FocusHostOutsideRegion(LogicalDirection direction, bool landAtParagraphStart = false)
    {
        if (_disposed)
            return;

        // A deliberate boundary exit (arrow/Tab/Ctrl+Up/Down or trailing-fence close): the user is
        // leaving the region, so it is no longer the active region for the typing-safety net.
        if (ReferenceEquals(ActiveRegion, this))
            ActiveRegion = null;

        // Collapse this region's own selection immediately on a keyboard-driven exit so the exit is
        // deterministic and there is no timing gap waiting for the async LostKeyboardFocus to fire.
        // Selection-only (SelectionLength=0 maps to TextArea.ClearSelection) — the region text is
        // never changed and the caret position is preserved. "Only one live selection at a time."
        if (_editor.SelectionLength > 0)
        {
            _editor.SelectionLength = 0;
            Trace("EMBEDDED_CODE_SELECTION_CLEARED reason=exit");
        }

        var host = _hostRichTextBox;
        if (host is null)
        {
            Trace($"EMBEDDED_CODE_FOCUS_HOST_OUTSIDE fallback=no-host direction={direction}");
            _editorControl.MoveFocus(new TraversalRequest(
                direction == LogicalDirection.Forward
                    ? FocusNavigationDirection.Next
                    : FocusNavigationDirection.Previous));
            return;
        }

        var pointer = FindHostCaretPointer(host, direction, landAtParagraphStart);
        if (pointer is null)
        {
            Trace($"EMBEDDED_CODE_FOCUS_HOST_OUTSIDE fallback=no-pointer direction={direction} hostCaretOffset={GetHostCaretOffsetForTrace(host)}");
            _editorControl.MoveFocus(new TraversalRequest(
                direction == LogicalDirection.Forward
                    ? FocusNavigationDirection.Next
                    : FocusNavigationDirection.Previous));
            return;
        }

        Trace($"EMBEDDED_CODE_FOCUS_HOST_OUTSIDE schedule direction={direction} hostCaretOffset={GetHostCaretOffsetForTrace(host)}");
        host.Dispatcher.BeginInvoke(DispatcherPriority.Input, () =>
        {
            if (_disposed || !ReferenceEquals(_hostRichTextBox, host) || !IsAttachedToHost())
            {
                Trace($"EMBEDDED_CODE_FOCUS_HOST_OUTSIDE skipped reason=stale-region direction={direction}");
                return;
            }

            var livePointer = FindHostCaretPointer(host, direction, landAtParagraphStart);
            if (livePointer is null)
            {
                Trace($"EMBEDDED_CODE_FOCUS_HOST_OUTSIDE skipped reason=no-live-pointer direction={direction}");
                return;
            }

            ApplyHostCaretSafely(host, livePointer, $"EMBEDDED_CODE_FOCUS_HOST_OUTSIDE direction={direction}");
        });
    }

    /// <summary>
    /// Removes this (necessarily empty) region's <see cref="BlockUIContainer"/> from the host
    /// prompt document and schedules focus/caret back onto the host where the region used to be,
    /// so Backspace in an empty region deletes the region and typing continues in-place. Returns
    /// <see langword="true"/> when a container was found and removed.
    /// <para>
    /// The caret landing is computed BEFORE the removal, mirroring <see cref="FindHostCaretPointer"/>'s
    /// forward/backward preference: the START of the paragraph that FOLLOWS the region if present,
    /// else the END of the paragraph BEFORE it; if neither neighbor exists a blank paragraph is kept
    /// so the <see cref="RichTextBox"/> stays editable and the caret lands there.
    /// </para>
    /// <para>
    /// No separate source of truth needs updating: the host FlowDocument IS the single source of
    /// truth for embedded regions — <see cref="EmbeddedCodeRegionManager.ConvertAllFences"/> /
    /// rebuilds scan <c>host.Document.Blocks</c>, and the logical/draft buffer is derived from the
    /// same blocks via <see cref="RichTextBoxExtensions.GetPlainText"/> (which serializes each live
    /// region through <see cref="ToFencedText"/>). Removing the container from <c>Blocks</c> here
    /// therefore guarantees the empty region does not resurrect on a later rebuild or on a
    /// <c>SetPromptTextBoxLogicalBuffer</c> rehydration round-trip.
    /// </para>
    /// </summary>
    internal bool TryRemoveEmptyRegionFromHost()
    {
        var host = _hostRichTextBox;
        if (host is null)
        {
            Trace("EMBEDDED_CODE_BACKSPACE_REMOVE fallback=no-host");
            return false;
        }

        var blocks = host.Document.Blocks.ToList();
        var index = -1;
        BlockUIContainer? container = null;
        for (var i = 0; i < blocks.Count; i++)
        {
            if (blocks[i] is BlockUIContainer { Child: EmbeddedCodeRegionBlock region } buc &&
                ReferenceEquals(region, this))
            {
                container = buc;
                index = i;
                break;
            }
        }

        if (container is null)
        {
            Trace("EMBEDDED_CODE_BACKSPACE_REMOVE fallback=not-found");
            return false;
        }

        // Compute the neighbors BEFORE removing, so the landing pointer can be resolved on blocks
        // that survive the removal (prefer the following paragraph's start, else the preceding
        // paragraph's end — matching the forward/backward exit convention in FindHostCaretPointer).
        var following = index + 1 < blocks.Count ? blocks[index + 1] as Paragraph : null;
        var preceding = index > 0 ? blocks[index - 1] as Paragraph : null;

        // The user is leaving the region (it's being deleted): it is no longer the active region
        // for the typing-safety net — mirrors FocusHostOutsideRegion.
        if (ReferenceEquals(ActiveRegion, this))
            ActiveRegion = null;

        var undoWasEnabled = host.IsUndoEnabled;
        if (undoWasEnabled)
            host.IsUndoEnabled = false;

        HostCaretTarget? landing;
        try
        {
            host.Document.Blocks.Remove(container);

            if (following is not null)
                landing = new HostCaretTarget(following, UseContentStart: true);
            else if (preceding is not null)
                landing = new HostCaretTarget(preceding, UseContentStart: false);
            else
            {
                // Region was the only/sole-remaining block: keep the RichTextBox editable by
                // ensuring a blank paragraph exists, and land the caret there.
                if (host.Document.Blocks.Count == 0)
                    host.Document.Blocks.Add(new Paragraph(new Run(string.Empty)) { Margin = new Thickness(0) });
                landing = host.Document.Blocks.FirstBlock is Paragraph only
                    ? new HostCaretTarget(only, UseContentStart: true)
                    : null;
            }
        }
        finally
        {
            if (undoWasEnabled)
                host.IsUndoEnabled = true;
        }

        DisposeRegionResources();

        Trace($"EMBEDDED_CODE_BACKSPACE_REMOVE removed index={index} following={following is not null} preceding={preceding is not null} blocksAfter={host.Document.Blocks.Count}");

        host.Dispatcher.BeginInvoke(DispatcherPriority.Input, () =>
        {
            var livePointer = landing?.Resolve(host);
            if (livePointer is null)
            {
                Trace("EMBEDDED_CODE_BACKSPACE_REMOVE skipped reason=stale-landing");
                return;
            }

            ApplyHostCaretSafely(host, livePointer, "EMBEDDED_CODE_BACKSPACE_REMOVE");
        });

        return true;
    }

    private readonly record struct HostCaretTarget(Paragraph Paragraph, bool UseContentStart)
    {
        public TextPointer? Resolve(RichTextBox host)
        {
            if (!ContainsBlock(host.Document, Paragraph))
                return null;

            return UseContentStart ? Paragraph.ContentStart : Paragraph.ContentEnd;
        }
    }

    private static void ApplyHostCaretSafely(RichTextBox host, TextPointer pointer, string tracePrefix)
    {
        try
        {
            host.Focus();
            Keyboard.Focus(host);
            host.CaretPosition = pointer;
            host.Selection.Select(pointer, pointer);
            Trace($"{tracePrefix} applied hostCaretOffset={GetHostCaretOffsetForTrace(host)} keyboardFocus={DescribeInputObject(Keyboard.FocusedElement)}");
        }
        catch (Exception ex)
        {
            Trace($"{tracePrefix} skipped reason={ex.GetType().Name}:{ex.Message}");
        }
    }

    private static bool ContainsBlock(FlowDocument document, Block block)
    {
        foreach (var candidate in document.Blocks)
        {
            if (ReferenceEquals(candidate, block))
                return true;
        }

        return false;
    }

    private TextPointer? FindHostCaretPointer(RichTextBox host, LogicalDirection direction, bool landAtParagraphStart = false)
    {
        var blocks = host.Document.Blocks.ToList();
        for (var i = 0; i < blocks.Count; i++)
        {
            if (blocks[i] is not BlockUIContainer { Child: EmbeddedCodeRegionBlock region } container ||
                !ReferenceEquals(region, this))
                continue;

            if (direction == LogicalDirection.Forward)
            {
                if (i + 1 < blocks.Count && blocks[i + 1] is Paragraph next)
                    return next.ContentStart;
                var inserted = InsertHostParagraphWithoutUndo(host, container, after: true);
                return inserted.ContentStart;
            }

            // Backward exit normally lands at the END of the paragraph above the region. Change 2:
            // when Up is pressed at the START of the region's first line, preserve the column and
            // land at the START of that paragraph instead.
            if (i > 0 && blocks[i - 1] is Paragraph previous)
                return landAtParagraphStart ? previous.ContentStart : previous.ContentEnd;
            var before = InsertHostParagraphWithoutUndo(host, container, after: false);
            return landAtParagraphStart ? before.ContentStart : before.ContentEnd;
        }

        return null;
    }

    private static Paragraph InsertHostParagraphWithoutUndo(RichTextBox host, BlockUIContainer container, bool after)
    {
        var paragraph = new Paragraph(new Run(string.Empty)) { Margin = new Thickness(0) };
        var undoWasEnabled = host.IsUndoEnabled;
        if (undoWasEnabled)
            host.IsUndoEnabled = false;

        try
        {
            if (after)
                host.Document.Blocks.InsertAfter(container, paragraph);
            else
                host.Document.Blocks.InsertBefore(container, paragraph);
        }
        finally
        {
            if (undoWasEnabled)
                host.IsUndoEnabled = true;
        }

        return paragraph;
    }

    private static bool IsVisualAncestorOrSelf(DependencyObject? current, DependencyObject target)
    {
        while (current is not null)
        {
            if (ReferenceEquals(current, target))
                return true;

            current = current is Visual or Visual3D
                ? VisualTreeHelper.GetParent(current)
                : LogicalTreeHelper.GetParent(current);
        }

        return false;
    }

    private int GetCaretLineIndexForTrace()
    {
        var text = _editor.Text;
        var caret = Math.Clamp(_editor.CaretOffset, 0, text.Length);
        return text[..caret].Count(c => c == '\n');
    }

    private int GetLineCountForTrace()
    {
        var text = _editor.Text;
        return text.Length == 0 ? 1 : text.Count(c => c == '\n') + 1;
    }

    private static string GetHostCaretOffsetForTrace(RichTextBox host)
    {
        try
        {
            return host.GetCaretOffset().ToString();
        }
        catch (Exception ex)
        {
            return $"error:{ex.GetType().Name}";
        }
    }

    private static string GetHostSelectionLengthForTrace(RichTextBox host)
    {
        try
        {
            return host.GetSelectionLength().ToString();
        }
        catch (Exception ex)
        {
            return $"error:{ex.GetType().Name}";
        }
    }

    private static string DescribeInputObject(object? value)
    {
        if (value is null)
            return "(null)";

        var typeName = value.GetType().Name;
        return value is FrameworkElement { Name.Length: > 0 } element
            ? $"{typeName}#{element.Name}"
            : typeName;
    }

    private static void Trace(string message) => SquadDashTrace.Write("UI", message);

    private static bool TrySetClipboardText(string text)
    {
        try
        {
            Clipboard.SetDataObject(text, copy: true);
            return true;
        }
        catch (System.Runtime.InteropServices.COMException ex)
        {
            Trace($"EMBEDDED_CODE_CLIPBOARD skipped reason={ex.GetType().Name}:{ex.Message}");
            return false;
        }
    }
}
