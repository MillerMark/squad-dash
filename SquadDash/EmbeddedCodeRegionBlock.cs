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

    private readonly IRegionCodeEditor _editor;
    private readonly Control _editorControl;
    private RichTextBox? _hostRichTextBox;
    private bool _isClosingFromTrailingFenceLine;

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

    /// Clears <see cref="ActiveRegion"/> unconditionally. Called when a host-prompt gesture makes
    /// clear the user has left region editing (e.g. a click landing outside every region).
    internal static void ClearActiveRegion() => ActiveRegion = null;

    public static EmbeddedCodeRegionBlock Build(string content, string language)
        => new(content, language);

    /// <summary>
    /// Decides whether the host prompt <see cref="RichTextBox"/>'s
    /// <c>PreviewMouseLeftButtonDown</c> should claim (mark handled + force focus) a left-click
    /// that landed inside an embedded code region.
    /// <para>
    /// Returns <c>false</c> when the click landed on the region's nested content
    /// <see cref="TextBox"/> (<paramref name="hitContentTextBox"/>) or on the collapse toggle
    /// (<paramref name="hitToggle"/>). For a content-box click the host MUST NOT handle the
    /// Preview event: doing so suppresses the bubbling <c>MouseLeftButtonDown</c> the TextBox
    /// relies on for native caret placement, double-click word-select, click-drag and
    /// double-click-drag selection — the regression this guard previously caused.
    /// </para>
    /// <para>
    /// Returns <c>true</c> only for clicks on region chrome (background/label) with no nested
    /// editor or toggle under the cursor, where there is no native text selection to preserve
    /// and the gesture should instead focus the region's content.
    /// </para>
    /// </summary>
    public static bool ShouldClaimRegionMouseGesture(bool hitRegion, bool hitToggle, bool hitContentTextBox)
        => hitRegion && !hitToggle && !hitContentTextBox;

    private EmbeddedCodeRegionBlock(string content, string language)
    {
        Content = content ?? string.Empty;
        Language = language ?? string.Empty;
        Tag = "embedded-code-region";

        _editor = new AvalonEditRegionEditor(Content);
        _editorControl = _editor.Control;
        // Pull the current theme's surface/text colors once at construction so a region created
        // between theme switches is themed immediately (AvalonEdit colors are not DynamicResource-
        // driven; see ApplyEditorTheme and MainWindow.ApplyTheme's per-region fan-out).
        _editor.ApplyPalette(new ThemeResourcePaletteSource().CreatePalette());

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
            if (!ReferenceEquals(ActiveRegion, this))
                return;
            if (e.NewFocus is not DependencyObject newFocus)
                return;
            if (IsVisualAncestorOrSelf(newFocus, this))
                return; // Still inside this region.
            if (_hostRichTextBox is not null && IsVisualAncestorOrSelf(newFocus, _hostRichTextBox))
                return; // Host prompt grabbed focus — keep active so the net can redirect typing.
            ActiveRegion = null;
        };

        // Diagnostic trace for the Space-leak investigation: one Space press reveals whether the
        // TextArea genuinely owns OS keyboard focus at keydown time, or whether focus has leaked
        // back to the host (which is why the space lands outside the region).
        _editorControl.PreviewKeyDown += (_, e) =>
        {
            if (e.Key == Key.Space)
                Trace($"EMBEDDED_CODE_SPACE_KEY textAreaFocused={_editor.IsTextInputSurfaceFocused} focusWithin={_editor.IsKeyboardFocusWithin} active={ReferenceEquals(ActiveRegion, this)} keyboardFocus={DescribeInputObject(Keyboard.FocusedElement)}");
        };

        // Keep Content (the serialization source of truth read by ToFencedText/GetPlainText)
        // in sync with every edit made directly inside the region. The editor's TextChanged
        // never bubbles up as the host RichTextBox's own TextChanged (it only fires for
        // FlowDocument/TextContainer content changes, not a nested control's Text property),
        // so this is isolated from EmbeddedCodeRegionManager's fence-scanning rebuilds — and
        // because those rebuilds reattach this exact same BlockUIContainer/control instance
        // (see EmbeddedCodeRegionManager.RenderSegments's ExistingContainer branch) rather
        // than recreating it, in-progress edits are never lost across a rebuild.
        _editor.TextChanged += (_, _) =>
        {
            Content = _editor.Text;
            TryCloseOnTrailingFenceLine();
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
                    Clipboard.SetText(fencedText);

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
                        Trace($"EMBEDDED_CODE_KEY_EXIT reason={e.Key}-exit direction=Backward caret={_editor.CaretOffset} textLen={_editor.TextLength}");
                        FocusHostOutsideRegion(LogicalDirection.Backward);
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

        var languageLabel = new TextBlock
        {
            Text = string.IsNullOrEmpty(Language) ? "Auto Detect -- Plain Text" : Language,
            FontFamily = new FontFamily("Segoe UI"), // Variable width font
            FontSize = 12,
            Margin = new Thickness(8, 4, 8, 4),
            VerticalAlignment = VerticalAlignment.Center,
            Opacity = 0.7, // Subtle text
            FontStyle = FontStyles.Italic,
            Background = System.Windows.Media.Brushes.Transparent,
        };
        languageLabel.SetResourceReference(TextBlock.ForegroundProperty, "SubtleText");

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
        stack.Children.Add(languageLabel);
        stack.Children.Add(editorBorder);

        // The outer element (this Border) is purely a transparent layout host now; the
        // visible editor chrome is painted by the inner editorBorder above.
        BorderThickness = new Thickness(0);
        Margin = new Thickness(0, 2, 0, 2);
        Child = stack;

        // Route clicks on the transparent outer area or the language label to the content
        // editor to enable editing. Clicks on the editor itself are handled above. This
        // PreviewMouseDown ensures that even if a click lands on the label or background, we
        // still focus the content editor instead of selecting the whole hosted BlockUIContainer.
        PreviewMouseLeftButtonDown += (sender, e) =>
        {
            var clickedElement = InputHitTest(e.GetPosition(this)) as DependencyObject;
            Trace($"EMBEDDED_CODE_BORDER_MOUSE enter handled={e.Handled} hit={DescribeInputObject(clickedElement)} point=({e.GetPosition(this).X:F1},{e.GetPosition(this).Y:F1})");
            if (IsVisualAncestorOrSelf(clickedElement, _editorControl))
            {
                Trace("EMBEDDED_CODE_BORDER_MOUSE exit reason=content-editor");
                return; // Let the editor do normal caret placement, selection, and focus.
            }

            // Label/background clicks focus the editor without selecting the whole hosted block.
            _editor.FocusEditor();
            e.Handled = true;
            Trace($"EMBEDDED_CODE_BORDER_MOUSE exit reason=header-focus handled=True focused={_editor.IsKeyboardFocusWithin} keyboardFocus={DescribeInputObject(Keyboard.FocusedElement)}");
        };
    }

    internal void AttachHost(RichTextBox hostRichTextBox)
    {
        _hostRichTextBox = hostRichTextBox;
        _editor.MaxHeight = (double.IsNaN(hostRichTextBox.MaxHeight) || double.IsInfinity(hostRichTextBox.MaxHeight))
            ? 130
            : hostRichTextBox.MaxHeight / 2.0;
    }

    /// <summary>
    /// Pushes the given <paramref name="palette"/> onto this region's editor so its surface/text
    /// colors follow the active theme. Called from <c>MainWindow.ApplyTheme</c>'s per-region
    /// fan-out after a theme dictionary swap (AvalonEdit colors are not DynamicResource-driven,
    /// so they must be re-applied imperatively; see AVALONEDIT-20261014-001).
    /// </summary>
    public void ApplyEditorTheme(RegionEditorPalette palette) => _editor.ApplyPalette(palette);

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
    /// Gives keyboard focus to this region's content editor and places the caret at the specified
    /// position within the text. Used for smart arrow-key navigation to enter the region with
    /// caret at the correct position (start, end, or specific offset).
    /// </summary>
    public void FocusContentAtPosition(int caretPosition)
    {
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
        var selLength = _editor.SelectionLength;
        var caret = selLength > 0 ? _editor.SelectionStart : _editor.CaretOffset;
        var plan = VoiceInsertionHeuristics.PlanDictationInsertion(
            _editor.Text, caret, selLength, dictatedText, replacementRules);
        Trace($"EMBEDDED_CODE_DICTATION enter caret={caret} selection={selLength} textLen={_editor.TextLength} inserted-len={plan.ReplacedText.Length}");
        _editor.InsertText(plan.ReplacedText);
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
        var host = _hostRichTextBox;
        if (host is null)
            return false;

        return host.Document.Blocks
            .OfType<BlockUIContainer>()
            .Any(container => ReferenceEquals(container.Child, this));
    }

    internal void FocusContentAtPoint(Point pointInContentBox)
    {
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

    private void FocusHostOutsideRegion(LogicalDirection direction)
    {
        // A deliberate boundary exit (arrow/Tab/Ctrl+Up/Down or trailing-fence close): the user is
        // leaving the region, so it is no longer the active region for the typing-safety net.
        if (ReferenceEquals(ActiveRegion, this))
            ActiveRegion = null;

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

        var pointer = FindHostCaretPointer(host, direction);
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
}
