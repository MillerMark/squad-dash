using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Documents;
using System.Windows.Threading;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Media3D;

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
/// <see cref="Language"/> is a data-model field only, per plan scope — it participates in
/// fence serialization (<see cref="ToFencedText"/>) but no language-picker UI is provided in
/// this step; a future plan may add one.
/// </para>
/// </summary>
public sealed class EmbeddedCodeRegionBlock : Border
{
    /// Fence content, excluding the opening/closing ``` markers and the language tag.
    public string Content { get; set; }

    /// Optional language tag (e.g. "python"). Empty string means no language tag.
    public new string Language { get; set; }

    private readonly TextBox _contentBox;
    private RichTextBox? _hostRichTextBox;
    private bool _isClosingFromTrailingFenceLine;

    public static EmbeddedCodeRegionBlock Build(string content, string language)
        => new(content, language);

    private EmbeddedCodeRegionBlock(string content, string language)
    {
        Content = content ?? string.Empty;
        Language = language ?? string.Empty;
        Tag = "embedded-code-region";

        _contentBox = new TextBox
        {
            Text = Content,
            AcceptsReturn = true,
            AcceptsTab = true,
            TextWrapping = TextWrapping.NoWrap,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            FontFamily = new FontFamily("Consolas"),
            FontSize = 12,
            BorderThickness = new Thickness(0),
            Padding = new Thickness(8),
            Margin = new Thickness(0),
            MaxHeight = 130,
        };
        _contentBox.SetResourceReference(Control.BackgroundProperty, "CodeSurface");
        _contentBox.SetResourceReference(Control.ForegroundProperty, "CodeText");

        // Hosted controls inside a RichTextBox's BlockUIContainer need to claim the mouse
        // gesture explicitly. If the event reaches the host RichTextBox unhandled, WPF
        // selects the entire BlockUIContainer as an object instead of placing the nested
        // editor caret.
        _contentBox.AddHandler(
            UIElement.PreviewMouseLeftButtonDownEvent,
            new MouseButtonEventHandler((_, e) =>
            {
                var point = e.GetPosition(_contentBox);
                Trace($"EMBEDDED_CODE_CONTENT_MOUSE enter handled={e.Handled} point=({point.X:F1},{point.Y:F1}) caret={_contentBox.CaretIndex} selection={_contentBox.SelectionStart}+{_contentBox.SelectionLength} focused={_contentBox.IsKeyboardFocusWithin} source={DescribeInputObject(e.OriginalSource)}");
                FocusContentAtPoint(point);
                e.Handled = true;
                Trace($"EMBEDDED_CODE_CONTENT_MOUSE exit handled=True caret={_contentBox.CaretIndex} selection={_contentBox.SelectionStart}+{_contentBox.SelectionLength} focused={_contentBox.IsKeyboardFocusWithin} keyboardFocus={DescribeInputObject(Keyboard.FocusedElement)}");
            }),
            handledEventsToo: true);

        // Keep Content (the serialization source of truth read by ToFencedText/GetPlainText)
        // in sync with every edit made directly inside the region. _contentBox's TextChanged
        // never bubbles up as the host RichTextBox's own TextChanged (it only fires for
        // FlowDocument/TextContainer content changes, not a nested control's Text property),
        // so this is isolated from EmbeddedCodeRegionManager's fence-scanning rebuilds — and
        // because those rebuilds reattach this exact same BlockUIContainer/control instance
        // (see EmbeddedCodeRegionManager.RenderSegments's ExistingContainer branch) rather
        // than recreating it, in-progress edits are never lost across a rebuild.
        _contentBox.TextChanged += (_, _) =>
        {
            Content = _contentBox.Text;
            TryCloseOnTrailingFenceLine();
        };

        // Handle Ctrl+C (Copy) and Ctrl+X (Cut) to provide fenced text to clipboard
        _contentBox.PreviewKeyDown += (_, e) =>
        {
            // When user selects all or partial content and copies/cuts, provide the fenced representation
            if ((e.Key == Key.C || e.Key == Key.X) && (Keyboard.Modifiers & ModifierKeys.Control) != 0)
            {
                if (_contentBox.SelectionLength > 0)
                {
                    // User selected text within the region. Get the selected text and wrap in fences.
                    var selectedText = _contentBox.SelectedText;
                    var fencedText = EmbeddedFenceDetector.BuildFencedText(selectedText, Language);
                    Clipboard.SetText(fencedText);
                    
                    if (e.Key == Key.X)
                    {
                        // For Cut, delete the selection after copying to clipboard
                        _contentBox.SelectedText = string.Empty;
                    }
                    
                    e.Handled = true;
                    return;
                }
            }
        };

        // Allow navigation exits from the region: Tab, arrow keys to move focus out
        _contentBox.PreviewKeyDown += (_, e) =>
        {
            var traceNavigationKey = e.Key is Key.Tab or Key.Down or Key.Up or Key.Left or Key.Right;
            if (traceNavigationKey)
            {
                Trace(
                    $"EMBEDDED_CODE_KEY enter key={e.Key} modifiers={Keyboard.Modifiers} handled={e.Handled} caret={_contentBox.CaretIndex} line={GetCaretLineIndexForTrace()} lineCount={GetLineCountForTrace()} selection={_contentBox.SelectionStart}+{_contentBox.SelectionLength}");
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
                    _contentBox.Text, _contentBox.CaretIndex, _contentBox.SelectionLength, e.Key);

                switch (decision)
                {
                    case EmbeddedCaretNavMath.Decision.ExitBackward:
                        Trace($"EMBEDDED_CODE_KEY_EXIT reason={e.Key}-exit direction=Backward caret={_contentBox.CaretIndex} textLen={_contentBox.Text.Length}");
                        FocusHostOutsideRegion(LogicalDirection.Backward);
                        e.Handled = true;
                        return;

                    case EmbeddedCaretNavMath.Decision.ExitForward:
                        Trace($"EMBEDDED_CODE_KEY_EXIT reason={e.Key}-exit direction=Forward caret={_contentBox.CaretIndex} textLen={_contentBox.Text.Length}");
                        FocusHostOutsideRegion(LogicalDirection.Forward);
                        e.Handled = true;
                        return;

                    case EmbeddedCaretNavMath.Decision.PassThrough:
                        Trace($"EMBEDDED_CODE_KEY pass-through reason={e.Key}-within-region caret={_contentBox.CaretIndex} line={GetCaretLineIndexForTrace()}");
                        // Don't set e.Handled; let the TextBox move the caret within the region.
                        break;
                }
            }

            if (traceNavigationKey && !e.Handled)
                Trace($"EMBEDDED_CODE_KEY pass-through key={e.Key} caret={_contentBox.CaretIndex} line={GetCaretLineIndexForTrace()}");
        };

        var languageLabel = new TextBlock
        {
            Text = string.IsNullOrEmpty(Language) ? "Auto Detect -- Plain Text" : Language,
            FontFamily = new FontFamily("Segoe UI"), // Variable width font
            FontSize = 12,
            Margin = new Thickness(8, 4, 8, 4),
            VerticalAlignment = VerticalAlignment.Center,
            Opacity = 0.7, // Subtle text
        };
        languageLabel.SetResourceReference(TextBlock.ForegroundProperty, "SubtleText");

        // The editor rectangle chrome now lives on an INNER border so the label can sit
        // visually above (outside) it. The outer EmbeddedCodeRegionBlock itself stays
        // transparent (see below).
        var editorBorder = new Border
        {
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(3),
            Child = _contentBox,
        };
        editorBorder.SetResourceReference(BackgroundProperty, "CodeSurface");
        editorBorder.SetResourceReference(BorderBrushProperty, "SubtleBorder");

        var stack = new StackPanel();
        stack.Children.Add(languageLabel);
        stack.Children.Add(editorBorder);

        // The outer element (this Border) is purely a transparent layout host now; the
        // visible editor chrome is painted by the inner editorBorder above.
        BorderThickness = new Thickness(0);
        Margin = new Thickness(0, 2, 0, 2);
        Child = stack;

        // Route clicks on the transparent outer area or the language label to the content
        // TextBox to enable editing. Clicks on the TextBox itself are handled above. This
        // PreviewMouseDown ensures that even if a click lands on the label or background, we
        // still focus the content box instead of selecting the whole hosted BlockUIContainer.
        PreviewMouseLeftButtonDown += (sender, e) =>
        {
            var clickedElement = InputHitTest(e.GetPosition(this)) as DependencyObject;
            Trace($"EMBEDDED_CODE_BORDER_MOUSE enter handled={e.Handled} hit={DescribeInputObject(clickedElement)} point=({e.GetPosition(this).X:F1},{e.GetPosition(this).Y:F1})");
            if (IsVisualAncestorOrSelf(clickedElement, _contentBox))
            {
                Trace("EMBEDDED_CODE_BORDER_MOUSE exit reason=content-textbox");
                return; // Let TextBox do normal caret placement, selection, and focus.
            }

            // Label/background clicks focus the editor without selecting the whole hosted block.
            _contentBox.Focus();
            Keyboard.Focus(_contentBox);
            e.Handled = true;
            Trace($"EMBEDDED_CODE_BORDER_MOUSE exit reason=header-focus handled=True focused={_contentBox.IsKeyboardFocusWithin} keyboardFocus={DescribeInputObject(Keyboard.FocusedElement)}");
        };
    }

    internal void AttachHost(RichTextBox hostRichTextBox)
    {
        _hostRichTextBox = hostRichTextBox;
        _contentBox.MaxHeight = (double.IsNaN(hostRichTextBox.MaxHeight) || double.IsInfinity(hostRichTextBox.MaxHeight))
            ? 130
            : hostRichTextBox.MaxHeight / 2.0;
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
        Trace($"EMBEDDED_CODE_FOCUS_END enter textLen={_contentBox.Text.Length} caretBefore={_contentBox.CaretIndex} selection={_contentBox.SelectionStart}+{_contentBox.SelectionLength} visible={_contentBox.IsVisible} enabled={_contentBox.IsEnabled} keyboardFocus={DescribeInputObject(Keyboard.FocusedElement)}");
        ClearHostSelection();
        _contentBox.CaretIndex = _contentBox.Text.Length;
        // FocusManager.SetFocusedElement sets logical/keyboard focus within the control's
        // focus scope even before the element has gone through a layout pass against a real
        // PresentationSource (e.g. right after this region is created, before WPF has had a
        // chance to render it) — plain Focus()/Keyboard.Focus() require IsVisible to already
        // be true, which isn't guaranteed synchronously at creation time. Keyboard.Focus is
        // still attempted afterward so a live, already-rendered app also gets real OS
        // keyboard input focus, not just the logical bookkeeping.
        FocusManager.SetFocusedElement(FocusManager.GetFocusScope(_contentBox), _contentBox);
        Keyboard.Focus(_contentBox);
        Trace($"EMBEDDED_CODE_FOCUS_END exit caret={_contentBox.CaretIndex} focused={_contentBox.IsKeyboardFocusWithin} keyboardFocus={DescribeInputObject(Keyboard.FocusedElement)}");
    }

    /// <summary>
    /// Gives keyboard focus to this region's content box and places the caret at the specified
    /// position within the text. Used for smart arrow-key navigation to enter the region with
    /// caret at the correct position (start, end, or specific offset).
    /// </summary>
    public void FocusContentAtPosition(int caretPosition)
    {
        var clampedPosition = Math.Clamp(caretPosition, 0, _contentBox.Text.Length);
        Trace($"EMBEDDED_CODE_FOCUS_POSITION enter position={caretPosition} clamped={clampedPosition} textLen={_contentBox.Text.Length} caretBefore={_contentBox.CaretIndex} visible={_contentBox.IsVisible}");
        ClearHostSelection();
        _contentBox.CaretIndex = clampedPosition;
        _contentBox.SelectionLength = 0;
        FocusManager.SetFocusedElement(FocusManager.GetFocusScope(_contentBox), _contentBox);
        Keyboard.Focus(_contentBox);
        Trace($"EMBEDDED_CODE_FOCUS_POSITION exit caret={_contentBox.CaretIndex} focused={_contentBox.IsKeyboardFocusWithin}");
    }

    internal void FocusContentAtPoint(Point pointInContentBox)
    {
        Trace($"EMBEDDED_CODE_FOCUS_POINT enter point=({pointInContentBox.X:F1},{pointInContentBox.Y:F1}) textLen={_contentBox.Text.Length} caretBefore={_contentBox.CaretIndex} selection={_contentBox.SelectionStart}+{_contentBox.SelectionLength} visible={_contentBox.IsVisible} enabled={_contentBox.IsEnabled} keyboardFocus={DescribeInputObject(Keyboard.FocusedElement)}");
        ClearHostSelection();
        FocusManager.SetFocusedElement(FocusManager.GetFocusScope(_contentBox), _contentBox);
        _contentBox.Focus();
        Keyboard.Focus(_contentBox);

        var caretIndex = GetCaretIndexFromPoint(pointInContentBox);
        _contentBox.CaretIndex = caretIndex;
        _contentBox.SelectionLength = 0;
        Trace($"EMBEDDED_CODE_FOCUS_POINT exit caret={_contentBox.CaretIndex} focused={_contentBox.IsKeyboardFocusWithin} keyboardFocus={DescribeInputObject(Keyboard.FocusedElement)}");
    }

    private void ClearHostSelection()
    {
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
    /// Guarded against re-entrancy because it mutates <c>_contentBox.Text</c> from within that
    /// same TextBox's own TextChanged handler.
    /// </summary>
    private void TryCloseOnTrailingFenceLine()
    {
        if (_isClosingFromTrailingFenceLine) return;

        var lines = Content.Split('\n');
        if (lines[^1].Trim() != "```") return;

        _isClosingFromTrailingFenceLine = true;
        try
        {
            var withoutClosingLine = string.Join("\n", lines.Take(lines.Length - 1));
            Content = withoutClosingLine;
            _contentBox.Text = withoutClosingLine;
            FocusHostOutsideRegion(LogicalDirection.Forward);
        }
        finally
        {
            _isClosingFromTrailingFenceLine = false;
        }
    }

    private int GetCaretIndexFromPoint(Point point)
    {
        if (_contentBox.Text.Length == 0)
            return 0;

        var index = _contentBox.GetCharacterIndexFromPoint(point, snapToText: true);
        if (index < 0)
            return _contentBox.Text.Length;

        index = Math.Clamp(index, 0, _contentBox.Text.Length);
        if (index < _contentBox.Text.Length)
        {
            var rect = _contentBox.GetRectFromCharacterIndex(index, trailingEdge: true);
            if (!rect.IsEmpty && point.X > rect.X)
                index++;
        }

        return Math.Clamp(index, 0, _contentBox.Text.Length);
    }

    private void FocusHostOutsideRegion(LogicalDirection direction)
    {
        var host = _hostRichTextBox;
        if (host is null)
        {
            Trace($"EMBEDDED_CODE_FOCUS_HOST_OUTSIDE fallback=no-host direction={direction}");
            _contentBox.MoveFocus(new TraversalRequest(
                direction == LogicalDirection.Forward
                    ? FocusNavigationDirection.Next
                    : FocusNavigationDirection.Previous));
            return;
        }

        var pointer = FindHostCaretPointer(host, direction);
        if (pointer is null)
        {
            Trace($"EMBEDDED_CODE_FOCUS_HOST_OUTSIDE fallback=no-pointer direction={direction} hostCaretOffset={GetHostCaretOffsetForTrace(host)}");
            _contentBox.MoveFocus(new TraversalRequest(
                direction == LogicalDirection.Forward
                    ? FocusNavigationDirection.Next
                    : FocusNavigationDirection.Previous));
            return;
        }

        Trace($"EMBEDDED_CODE_FOCUS_HOST_OUTSIDE schedule direction={direction} hostCaretOffset={GetHostCaretOffsetForTrace(host)}");
        host.Dispatcher.BeginInvoke(DispatcherPriority.Input, () =>
        {
            host.Focus();
            Keyboard.Focus(host);
            host.CaretPosition = pointer;
            host.Selection.Select(pointer, pointer);
            Trace($"EMBEDDED_CODE_FOCUS_HOST_OUTSIDE applied direction={direction} hostCaretOffset={GetHostCaretOffsetForTrace(host)} keyboardFocus={DescribeInputObject(Keyboard.FocusedElement)}");
        });
    }

    private TextPointer? FindHostCaretPointer(RichTextBox host, LogicalDirection direction)
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

            if (i > 0 && blocks[i - 1] is Paragraph previous)
                return previous.ContentEnd;
            var before = InsertHostParagraphWithoutUndo(host, container, after: false);
            return before.ContentEnd;
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
        var caret = Math.Clamp(_contentBox.CaretIndex, 0, _contentBox.Text.Length);
        return _contentBox.Text[..caret].Count(c => c == '\n');
    }

    private int GetLineCountForTrace() =>
        _contentBox.Text.Length == 0 ? 1 : _contentBox.Text.Count(c => c == '\n') + 1;

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
}
