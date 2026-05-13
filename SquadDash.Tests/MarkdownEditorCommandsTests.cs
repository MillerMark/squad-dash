using System.Threading;
using System.Windows;
using System.Windows.Controls;

namespace SquadDash.Tests;

// Tests must run on an STA thread because TextBox is a WPF control.
[TestFixture]
[Apartment(ApartmentState.STA)]
internal sealed class MarkdownEditorCommandsTests {

    // ── ApplyBold ────────────────────────────────────────────────────────────

    [Test]
    public void ApplyBold_WrapsSelection_WithAsterisks() {
        var tb = MakeBox("hello world");
        Select(tb, 6, 5); // "world"
        MarkdownEditorCommands.ApplyBold(tb);
        Assert.That(tb.Text, Is.EqualTo("hello **world**"));
    }

    [Test]
    public void ApplyBold_TrimsTrailingSpace_BeforeWrapping() {
        // Regression: voice dictation often appends a trailing space to a selection.
        // Bold should trim that space so the marker lands inside the word boundary.
        var tb = MakeBox("hello world ");
        Select(tb, 6, 6); // "world " (with trailing space)
        MarkdownEditorCommands.ApplyBold(tb);
        Assert.That(tb.Text, Is.EqualTo("hello **world** "));
    }

    [Test]
    public void ApplyBold_TrailingSpaceRemainsOutside_ResultingSelection() {
        // The selection after bold should cover only the bolded word, not the trailing space.
        var tb = MakeBox("hello world ");
        Select(tb, 6, 6); // "world "
        MarkdownEditorCommands.ApplyBold(tb);
        // SelectionStart stays at 6, SelectionLength covers "**world**" = 9 chars.
        Assert.Multiple(() => {
            Assert.That(tb.SelectionStart,  Is.EqualTo(6));
            Assert.That(tb.SelectionLength, Is.EqualTo(9)); // "**world**"
        });
    }

    [Test]
    public void ApplyBold_AllSpaceSelection_DoesNotCrash() {
        // Entirely whitespace selection should produce "****" (empty bold).
        var tb = MakeBox("   ");
        Select(tb, 0, 3); // "   "
        Assert.DoesNotThrow(() => MarkdownEditorCommands.ApplyBold(tb));
    }

    [Test]
    public void ApplyBold_NoSelection_InsertsEmptyMarkers_AtCaret() {
        var tb = MakeBox("hello");
        tb.CaretIndex = 5;
        MarkdownEditorCommands.ApplyBold(tb);
        Assert.Multiple(() => {
            Assert.That(tb.Text,       Is.EqualTo("hello****"));
            Assert.That(tb.CaretIndex, Is.EqualTo(7)); // cursor inside "**|**"
        });
    }

    // ── ApplyItalic ──────────────────────────────────────────────────────────

    [Test]
    public void ApplyItalic_WrapsSelection_WithAsterisks() {
        var tb = MakeBox("hello world");
        Select(tb, 6, 5); // "world"
        MarkdownEditorCommands.ApplyItalic(tb);
        Assert.That(tb.Text, Is.EqualTo("hello *world*"));
    }

    [Test]
    public void ApplyItalic_TrimsTrailingSpace_BeforeWrapping() {
        var tb = MakeBox("hello world ");
        Select(tb, 6, 6); // "world "
        MarkdownEditorCommands.ApplyItalic(tb);
        Assert.That(tb.Text, Is.EqualTo("hello *world* "));
    }

    [Test]
    public void ApplyItalic_NoSelection_InsertsEmptyMarkers_AtCaret() {
        var tb = MakeBox("hello");
        tb.CaretIndex = 5;
        MarkdownEditorCommands.ApplyItalic(tb);
        Assert.Multiple(() => {
            Assert.That(tb.Text,       Is.EqualTo("hello**"));
            Assert.That(tb.CaretIndex, Is.EqualTo(6));
        });
    }

    // ── ApplyBulletList ──────────────────────────────────────────────────────

    [Test]
    public void ApplyBulletList_SingleLine_AddsBulletPrefix() {
        var tb = MakeBox("hello world");
        Select(tb, 0, 11);
        MarkdownEditorCommands.ApplyBulletList(tb);
        Assert.That(tb.Text, Is.EqualTo("- hello world"));
    }

    [Test]
    public void ApplyBulletList_MultipleLines_PrefixesEachLine() {
        var tb = MakeBox("alpha\nbeta\ngamma");
        Select(tb, 0, 16);
        MarkdownEditorCommands.ApplyBulletList(tb);
        Assert.That(tb.Text, Is.EqualTo("- alpha\n- beta\n- gamma"));
    }

    [Test]
    public void ApplyBulletList_PartialSelection_ExpandsToFullLines() {
        var tb = MakeBox("alpha\nbeta\ngamma");
        Select(tb, 3, 5); // mid-alpha through mid-beta
        MarkdownEditorCommands.ApplyBulletList(tb);
        // alpha and beta lines both get prefixed
        Assert.That(tb.Text, Does.StartWith("- alpha\n- beta"));
    }

    [Test]
    public void ApplyBulletList_AlreadyBulleted_ReplacesExistingPrefix() {
        var tb = MakeBox("- item one\n* item two");
        Select(tb, 0, 21);
        MarkdownEditorCommands.ApplyBulletList(tb);
        Assert.That(tb.Text, Is.EqualTo("- item one\n- item two"));
    }

    [Test]
    public void ApplyBulletList_NoSelection_ConvertesCurrentLine() {
        var tb = MakeBox("no selection");
        tb.CaretIndex = 5;
        MarkdownEditorCommands.ApplyBulletList(tb);
        Assert.That(tb.Text, Is.EqualTo("- no selection"));
    }

    // ── ApplyNumberedList ────────────────────────────────────────────────────

    [Test]
    public void ApplyNumberedList_MultipleLines_NumbersSequentially() {
        var tb = MakeBox("alpha\nbeta\ngamma");
        Select(tb, 0, 16);
        MarkdownEditorCommands.ApplyNumberedList(tb);
        Assert.That(tb.Text, Is.EqualTo("1. alpha\n2. beta\n3. gamma"));
    }

    [Test]
    public void ApplyNumberedList_AlreadyBulleted_ReplacesWithNumbers() {
        var tb = MakeBox("- alpha\n- beta");
        Select(tb, 0, 14);
        MarkdownEditorCommands.ApplyNumberedList(tb);
        Assert.That(tb.Text, Is.EqualTo("1. alpha\n2. beta"));
    }

    [Test]
    public void ApplyNumberedList_NoSelection_ConvertesCurrentLine() {
        var tb = MakeBox("no selection");
        tb.CaretIndex = 5;
        MarkdownEditorCommands.ApplyNumberedList(tb);
        Assert.That(tb.Text, Is.EqualTo("1. no selection"));
    }

    // ── ContinueListOnEnter ──────────────────────────────────────────────────

    [Test]
    public void ContinueListOnEnter_HyphenBullet_ContinuesOnNextLine() {
        var tb = MakeBox("- first item");
        tb.CaretIndex = 12; // end of line
        var handled = MarkdownEditorCommands.ContinueListOnEnter(tb);
        Assert.Multiple(() => {
            Assert.That(handled,       Is.True);
            Assert.That(tb.Text,       Is.EqualTo("- first item\n- "));
            Assert.That(tb.CaretIndex, Is.EqualTo(15));
        });
    }

    [Test]
    public void ContinueListOnEnter_AsteriskBullet_ContinuesOnNextLine() {
        var tb = MakeBox("* item one");
        tb.CaretIndex = 10;
        var handled = MarkdownEditorCommands.ContinueListOnEnter(tb);
        Assert.That(handled, Is.True);
        Assert.That(tb.Text, Is.EqualTo("* item one\n* "));
    }

    [Test]
    public void ContinueListOnEnter_PlusBullet_ContinuesOnNextLine() {
        var tb = MakeBox("+ item one");
        tb.CaretIndex = 10;
        var handled = MarkdownEditorCommands.ContinueListOnEnter(tb);
        Assert.That(handled, Is.True);
        Assert.That(tb.Text, Is.EqualTo("+ item one\n+ "));
    }

    [Test]
    public void ContinueListOnEnter_OrderedList_IncrementsNumber() {
        var tb = MakeBox("1. first");
        tb.CaretIndex = 8;
        var handled = MarkdownEditorCommands.ContinueListOnEnter(tb);
        Assert.Multiple(() => {
            Assert.That(handled, Is.True);
            Assert.That(tb.Text, Is.EqualTo("1. first\n2. "));
        });
    }

    [Test]
    public void ContinueListOnEnter_OrderedListHighNumber_IncrementsCorrectly() {
        var tb = MakeBox("9. ninth");
        tb.CaretIndex = 8;
        MarkdownEditorCommands.ContinueListOnEnter(tb);
        Assert.That(tb.Text, Is.EqualTo("9. ninth\n10. "));
    }

    [Test]
    public void ContinueListOnEnter_MultiLineDocument_ContinuesFromCorrectLine() {
        var tb = MakeBox("intro\n- alpha\n- beta");
        tb.CaretIndex = 13; // end of "- alpha" line
        MarkdownEditorCommands.ContinueListOnEnter(tb);
        Assert.That(tb.Text, Is.EqualTo("intro\n- alpha\n- \n- beta"));
    }

    [Test]
    public void ContinueListOnEnter_NotAtEndOfLine_DoesNotHandle() {
        var tb = MakeBox("- item text");
        tb.CaretIndex = 4; // mid-line (after "- it")
        var handled = MarkdownEditorCommands.ContinueListOnEnter(tb);
        Assert.That(handled, Is.False);
    }

    [Test]
    public void ContinueListOnEnter_PlainLine_DoesNotHandle() {
        var tb = MakeBox("just text");
        tb.CaretIndex = 9;
        var handled = MarkdownEditorCommands.ContinueListOnEnter(tb);
        Assert.That(handled, Is.False);
    }

    [Test]
    public void ContinueListOnEnter_IndentedBullet_PreservesIndent() {
        var tb = MakeBox("  - nested");
        tb.CaretIndex = 10;
        MarkdownEditorCommands.ContinueListOnEnter(tb);
        Assert.That(tb.Text, Is.EqualTo("  - nested\n  - "));
    }

    [Test]
    public void ContinueListOnEnter_EmptyHyphenBullet_EscapesList() {
        var tb = MakeBox("- item\n- ");
        tb.CaretIndex = tb.Text.Length; // end of "- "
        var handled = MarkdownEditorCommands.ContinueListOnEnter(tb);
        Assert.That(handled, Is.True);
        Assert.That(tb.Text, Is.EqualTo("- item\n"));
        Assert.That(tb.CaretIndex, Is.EqualTo(7));
    }

    [Test]
    public void ContinueListOnEnter_EmptyAsteriskBullet_EscapesList() {
        var tb = MakeBox("* ");
        tb.CaretIndex = 2;
        var handled = MarkdownEditorCommands.ContinueListOnEnter(tb);
        Assert.That(handled, Is.True);
        Assert.That(tb.Text, Is.EqualTo(""));
        Assert.That(tb.CaretIndex, Is.EqualTo(0));
    }

    [Test]
    public void ContinueListOnEnter_EmptyNumberedBullet_EscapesList() {
        var tb = MakeBox("1. first\n2. ");
        tb.CaretIndex = tb.Text.Length;
        var handled = MarkdownEditorCommands.ContinueListOnEnter(tb);
        Assert.That(handled, Is.True);
        Assert.That(tb.Text, Is.EqualTo("1. first\n"));
        Assert.That(tb.CaretIndex, Is.EqualTo(9));
    }

    [Test]
    public void ContinueListOnEnter_EmptyBulletFollowedByMoreText_EscapesOnlyPrefix() {
        var tb = MakeBox("- item\n- \nmore");
        tb.CaretIndex = 9; // end of "- " on second line
        var handled = MarkdownEditorCommands.ContinueListOnEnter(tb);
        Assert.That(handled, Is.True);
        Assert.That(tb.Text, Is.EqualTo("- item\n\nmore"));
        Assert.That(tb.CaretIndex, Is.EqualTo(7));
    }

    // ── ApplyInlineCodeOrFence ───────────────────────────────────────────────

    [Test]
    public void ApplyInlineCodeOrFence_TrailingSpace_IsPreservedOutsideBacktick() {
        // Regression: buggy code computed trailingSpaces as raw[(raw.Length - raw.TrimEnd().Length)..]
        // which for "great " gave raw[1..] = "reat " instead of raw[5..] = " ".
        var tb = MakeBox("hello great world");
        Select(tb, 6, 6); // "great "
        MarkdownEditorCommands.ApplyInlineCodeOrFence(tb);
        Assert.That(tb.Text, Is.EqualTo("hello `great` world"));
    }

    [Test]
    public void ApplyInlineCodeOrFence_TrailingSpace_SelectionCoversWrappedWord() {
        var tb = MakeBox("hello great world");
        Select(tb, 6, 6); // "great "
        MarkdownEditorCommands.ApplyInlineCodeOrFence(tb);
        // After step 1, inner content (between backticks) is selected; step 2 skipped for single word.
        Assert.Multiple(() => {
            Assert.That(tb.SelectionStart,  Is.EqualTo(7));
            Assert.That(tb.SelectionLength, Is.EqualTo(5)); // "great"
        });
    }

    [Test]
    public void ApplyInlineCodeOrFence_LeadingSpace_IsPreservedOutsideBacktick() {
        var tb = MakeBox(" great");
        Select(tb, 0, 6); // " great"
        MarkdownEditorCommands.ApplyInlineCodeOrFence(tb);
        Assert.That(tb.Text, Is.EqualTo(" `great`"));
    }

    [Test]
    public void ApplyInlineCodeOrFence_LeadingSpace_SelectionStartsAfterLeadingSpace() {
        var tb = MakeBox(" great");
        Select(tb, 0, 6);
        MarkdownEditorCommands.ApplyInlineCodeOrFence(tb);
        // After step 1, inner content is selected; step 2 skipped for single word.
        Assert.Multiple(() => {
            Assert.That(tb.SelectionStart,  Is.EqualTo(2));
            Assert.That(tb.SelectionLength, Is.EqualTo(5)); // "great"
        });
    }

    [Test]
    public void ApplyInlineCodeOrFence_CleanSelection_WrapsInBackticks() {
        var tb = MakeBox("world");
        Select(tb, 0, 5);
        MarkdownEditorCommands.ApplyInlineCodeOrFence(tb);
        // After step 1, inner content is selected; step 2 skipped for single word.
        Assert.Multiple(() => {
            Assert.That(tb.Text,            Is.EqualTo("`world`"));
            Assert.That(tb.SelectionStart,  Is.EqualTo(1));
            Assert.That(tb.SelectionLength, Is.EqualTo(5)); // "world"
        });
    }

    [Test]
    public void ApplyInlineCodeOrFence_BothLeadingAndTrailingSpaces_SpacesRemainOutside() {
        var tb = MakeBox("  hello  ");
        Select(tb, 0, 9); // "  hello  "
        MarkdownEditorCommands.ApplyInlineCodeOrFence(tb);
        // After step 1, inner content is selected; step 2 skipped for single word.
        Assert.Multiple(() => {
            Assert.That(tb.Text,            Is.EqualTo("  `hello`  "));
            Assert.That(tb.SelectionStart,  Is.EqualTo(3));
            Assert.That(tb.SelectionLength, Is.EqualTo(5)); // "hello"
        });
    }

    [Test]
    public void ApplyInlineCodeOrFence_MultiWordSingleLine_ConvertsToCamelCase() {
        var tb = MakeBox("my method");
        Select(tb, 0, 9);
        MarkdownEditorCommands.ApplyInlineCodeOrFence(tb);
        // Step 2 fires (multi-word → camelCase transform), leaving caret after transformed text.
        Assert.That(tb.Text, Is.EqualTo("`myMethod`"));
    }

    [Test]
    public void ApplyInlineCodeOrFence_MultiLineSelection_WrapsInFence() {
        var tb = MakeBox("line1\nline2");
        Select(tb, 0, 11);
        MarkdownEditorCommands.ApplyInlineCodeOrFence(tb);
        const string expected = "```\nline1\nline2\n```";
        Assert.Multiple(() => {
            Assert.That(tb.Text,            Is.EqualTo(expected));
            Assert.That(tb.SelectionStart,  Is.EqualTo(0));
            Assert.That(tb.SelectionLength, Is.EqualTo(expected.Length));
        });
    }

    [Test]
    public void ApplyInlineCodeOrFence_MultiLineWithLeadingBlankLine_BlankLineMovesOutsideFence() {
        // Selection: "\nline1\nline2" — leading blank should move above the fence
        var tb = MakeBox("\nline1\nline2");
        Select(tb, 0, 12);
        MarkdownEditorCommands.ApplyInlineCodeOrFence(tb);
        const string expected = "\n```\nline1\nline2\n```";
        Assert.That(tb.Text, Is.EqualTo(expected));
    }

    [Test]
    public void ApplyInlineCodeOrFence_MultiLineWithTrailingBlankLine_BlankLineMovesOutsideFence() {
        // Selection: "line1\nline2\n" — trailing blank should move below the fence
        var tb = MakeBox("line1\nline2\n");
        Select(tb, 0, 12);
        MarkdownEditorCommands.ApplyInlineCodeOrFence(tb);
        const string expected = "```\nline1\nline2\n```\n";
        Assert.That(tb.Text, Is.EqualTo(expected));
    }

    [Test]
    public void ApplyInlineCodeOrFence_MultiLineWithLeadingAndTrailingBlanks_BlanksMovedOutside() {
        var tb = MakeBox("\n\nsome code\nmore code\n\n");
        Select(tb, 0, 22);
        MarkdownEditorCommands.ApplyInlineCodeOrFence(tb);
        const string expected = "\n\n```\nsome code\nmore code\n```\n\n";
        Assert.That(tb.Text, Is.EqualTo(expected));
    }

    [Test]
    public void ApplyInlineCodeOrFence_MultiLineNoBlankLines_UnchangedBehavior() {
        // No blank lines → result identical to before
        var tb = MakeBox("line1\nline2\nline3");
        Select(tb, 0, 17);
        MarkdownEditorCommands.ApplyInlineCodeOrFence(tb);
        const string expected = "```\nline1\nline2\nline3\n```";
        Assert.That(tb.Text, Is.EqualTo(expected));
    }

    [Test]
    public void ApplyInlineCodeOrFence_EmptySelection_ReturnsFalse_AndTextUnchanged() {
        var tb = MakeBox("hello");
        tb.CaretIndex = 2;
        var result = MarkdownEditorCommands.ApplyInlineCodeOrFence(tb);
        Assert.Multiple(() => {
            Assert.That(result,  Is.False);
            Assert.That(tb.Text, Is.EqualTo("hello"));
        });
    }

    // ── ApplyInlineQuote ─────────────────────────────────────────────────────

    [Test]
    public void ApplyInlineQuote_TrailingSpace_IsPreservedOutsideQuotes() {
        // Regression: same trailingSpaces bug as ApplyInlineCodeOrFence.
        var tb = MakeBox("great ");
        Select(tb, 0, 6); // "great "
        MarkdownEditorCommands.ApplyInlineQuote(tb);
        Assert.That(tb.Text, Is.EqualTo("\"great\" "));
    }

    [Test]
    public void ApplyInlineQuote_TrailingSpace_SelectionCoversWrappedWord() {
        var tb = MakeBox("great ");
        Select(tb, 0, 6);
        MarkdownEditorCommands.ApplyInlineQuote(tb);
        Assert.Multiple(() => {
            Assert.That(tb.SelectionStart,  Is.EqualTo(0));
            Assert.That(tb.SelectionLength, Is.EqualTo(7)); // "\"great\""
        });
    }

    [Test]
    public void ApplyInlineQuote_CleanSelection_WrapsInQuotes() {
        var tb = MakeBox("hello");
        Select(tb, 0, 5);
        MarkdownEditorCommands.ApplyInlineQuote(tb);
        Assert.Multiple(() => {
            Assert.That(tb.Text,            Is.EqualTo("\"hello\""));
            Assert.That(tb.SelectionStart,  Is.EqualTo(0));
            Assert.That(tb.SelectionLength, Is.EqualTo(7));
        });
    }

    [Test]
    public void ApplyInlineQuote_MultiLineSelection_ReturnsFalse_AndTextUnchanged() {
        var tb = MakeBox("hello\nworld");
        Select(tb, 0, 11);
        var result = MarkdownEditorCommands.ApplyInlineQuote(tb);
        Assert.Multiple(() => {
            Assert.That(result,  Is.False);
            Assert.That(tb.Text, Is.EqualTo("hello\nworld"));
        });
    }

    [Test]
    public void ApplyInlineQuote_EmptySelection_ReturnsFalse_AndTextUnchanged() {
        var tb = MakeBox("hello");
        tb.CaretIndex = 2;
        var result = MarkdownEditorCommands.ApplyInlineQuote(tb);
        Assert.Multiple(() => {
            Assert.That(result,  Is.False);
            Assert.That(tb.Text, Is.EqualTo("hello"));
        });
    }

    // ── ApplyInlineCodeOrFence two-step undo ─────────────────────────────────

    [Test]
    public void ApplyInlineCodeOrFence_MultiWord_TwoUndoRecords_IntermediateStateIsBacktickWrapped() {
        RunWithUndoBox("my variable", tb => {
            Select(tb, 0, 11);
            MarkdownEditorCommands.ApplyInlineCodeOrFence(tb);
            Assert.That(tb.Text, Is.EqualTo("`myVariable`"));
            Assert.That(tb.CanUndo, Is.True);

            // Undo step 2: reverts camelCase → inner content selected
            tb.Undo();
            Assert.Multiple(() => {
                Assert.That(tb.Text,            Is.EqualTo("`my variable`"));
                Assert.That(tb.SelectionStart,  Is.EqualTo(1));
                Assert.That(tb.SelectionLength, Is.EqualTo(11)); // "my variable"
            });

            // Undo step 1: reverts backtick wrap → original text
            tb.Undo();
            Assert.That(tb.Text, Is.EqualTo("my variable"));
        });
    }

    [Test]
    public void ApplyInlineCodeOrFence_SingleWord_OnlyOneUndoRecord() {
        RunWithUndoBox("word", tb => {
            Select(tb, 0, 4);
            MarkdownEditorCommands.ApplyInlineCodeOrFence(tb);
            Assert.That(tb.Text, Is.EqualTo("`word`"));
            Assert.That(tb.CanUndo, Is.True);

            // Single undo reverts the backtick wrap → original text
            tb.Undo();
            Assert.That(tb.Text, Is.EqualTo("word"));
            Assert.That(tb.CanUndo, Is.False);
        });
    }

    [Test]
    public void ApplyInlineCodeOrFence_FilenamePhraseWithDotWord_CorrectExtensionHandling() {
        var tb = MakeBox("my config dot yaml");
        Select(tb, 0, 18);
        MarkdownEditorCommands.ApplyInlineCodeOrFence(tb);
        Assert.That(tb.Text, Is.EqualTo("`myConfig.yaml`"));
    }

    // ── ApplyInlineParens ─────────────────────────────────────────────────────

    [Test]
    public void ApplyInlineParens_SingleLineSelection_EmbedsInParens() {
        var tb = MakeBox("hello world");
        Select(tb, 6, 5); // "world"
        var result = MarkdownEditorCommands.ApplyInlineParens(tb);
        Assert.That(result, Is.True);
        Assert.Multiple(() => {
            Assert.That(tb.Text,            Is.EqualTo("hello (world)"));
            Assert.That(tb.SelectionStart,  Is.EqualTo(6));
            Assert.That(tb.SelectionLength, Is.EqualTo(7)); // "(world)"
        });
    }

    [Test]
    public void ApplyInlineParens_LeadingAndTrailingSpaces_SpacesRemainOutsideParens() {
        var tb = MakeBox("  hello  ");
        Select(tb, 0, 9); // "  hello  "
        MarkdownEditorCommands.ApplyInlineParens(tb);
        Assert.Multiple(() => {
            Assert.That(tb.Text,            Is.EqualTo("  (hello)  "));
            Assert.That(tb.SelectionStart,  Is.EqualTo(2));
            Assert.That(tb.SelectionLength, Is.EqualTo(7)); // "(hello)"
        });
    }

    [Test]
    public void ApplyInlineParens_MultiLineSelection_ReturnsFalse_AndTextUnchanged() {
        var tb = MakeBox("hello\nworld");
        Select(tb, 0, 11);
        var result = MarkdownEditorCommands.ApplyInlineParens(tb);
        Assert.Multiple(() => {
            Assert.That(result,  Is.False);
            Assert.That(tb.Text, Is.EqualTo("hello\nworld"));
        });
    }

    [Test]
    public void ApplyInlineParens_EmptySelection_ReturnsFalse_AndTextUnchanged() {
        var tb = MakeBox("hello");
        tb.CaretIndex = 2;
        var result = MarkdownEditorCommands.ApplyInlineParens(tb);
        Assert.Multiple(() => {
            Assert.That(result,  Is.False);
            Assert.That(tb.Text, Is.EqualTo("hello"));
        });
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    private static TextBox MakeBox(string text) => new() { Text = text };

    // Creates a TextBox with undo enabled (requires ApplyTemplate to initialize the UndoManager).
    private static TextBox MakeUndoBox(string text) {
        var tb = new TextBox();
        tb.ApplyTemplate();       // initializes TextEditor which enables the UndoManager
        tb.IsUndoEnabled = false; // clear any initial records and pause recording
        tb.Text = text;           // set initial text without creating an undo record
        tb.IsUndoEnabled = true;  // re-enable; undo stack is now empty and ready
        return tb;
    }

    // Runs an action with a TextBox that has a fully-functional undo stack.
    // WPF undo requires the TextBox to be connected to a visual tree (Window).
    private static void RunWithUndoBox(string text, Action<TextBox> test) {
        if (Application.Current == null)
            _ = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        var tb = new TextBox();
        var win = new Window {
            Content = tb,
            Width = 1, Height = 1,
            ShowInTaskbar = false,
            WindowStyle = WindowStyle.None,
            ResizeMode = ResizeMode.NoResize,
            Left = -32000, Top = -32000,
            Opacity = 0
        };
        win.Show();
        try {
            tb.IsUndoEnabled = false;
            tb.Text = text;
            tb.IsUndoEnabled = true;
            test(tb);
        } finally {
            win.Close();
        }
    }

    private static void Select(TextBox tb, int start, int length) {
        tb.SelectionStart  = start;
        tb.SelectionLength = length;
    }
}

// ── DuplicateLineTests ───────────────────────────────────────────────────────

[TestFixture]
[Apartment(ApartmentState.STA)]
internal sealed class DuplicateLineTests {

    [Test]
    public void DuplicateLine_CaretInMiddleOfLine_DuplicatesLine_CaretAtStartOfNewLine() {
        var tb = MakeBox("hello world\nsecond line");
        tb.CaretIndex = 5; // middle of "hello world"
        MarkdownEditorCommands.DuplicateLine(tb);
        Assert.Multiple(() => {
            Assert.That(tb.Text,       Is.EqualTo("hello world\nhello world\nsecond line"));
            Assert.That(tb.CaretIndex, Is.EqualTo(12)); // start of new duplicate line
        });
    }

    [Test]
    public void DuplicateLine_CaretAtStartOfLine_DuplicatesLine_CaretAtStartOfNewLine() {
        var tb = MakeBox("hello world\nsecond line");
        tb.CaretIndex = 0; // start of "hello world"
        MarkdownEditorCommands.DuplicateLine(tb);
        Assert.Multiple(() => {
            Assert.That(tb.Text,       Is.EqualTo("hello world\nhello world\nsecond line"));
            Assert.That(tb.CaretIndex, Is.EqualTo(12));
        });
    }

    [Test]
    public void DuplicateLine_CaretAtEndOfLine_DuplicatesLine_CaretAtStartOfNewLine() {
        var tb = MakeBox("hello world\nsecond line");
        tb.CaretIndex = 11; // end of "hello world"
        MarkdownEditorCommands.DuplicateLine(tb);
        Assert.Multiple(() => {
            Assert.That(tb.Text,       Is.EqualTo("hello world\nhello world\nsecond line"));
            Assert.That(tb.CaretIndex, Is.EqualTo(12));
        });
    }

    [Test]
    public void DuplicateLine_SingleLineNoTrailingNewline_DuplicatesCorrectly() {
        var tb = MakeBox("only line");
        tb.CaretIndex = 4;
        MarkdownEditorCommands.DuplicateLine(tb);
        Assert.Multiple(() => {
            Assert.That(tb.Text,       Is.EqualTo("only line\nonly line"));
            Assert.That(tb.CaretIndex, Is.EqualTo(10)); // start of duplicate
        });
    }

    [Test]
    public void DuplicateLine_CaretOnLastLineOfMultiLine_DuplicatesLastLine() {
        var tb = MakeBox("first line\nlast line");
        tb.CaretIndex = 15; // somewhere in "last line"
        MarkdownEditorCommands.DuplicateLine(tb);
        Assert.Multiple(() => {
            Assert.That(tb.Text,       Is.EqualTo("first line\nlast line\nlast line"));
            Assert.That(tb.CaretIndex, Is.EqualTo(21)); // start of new duplicate
        });
    }

    private static TextBox MakeBox(string text) => new() { Text = text };
}
