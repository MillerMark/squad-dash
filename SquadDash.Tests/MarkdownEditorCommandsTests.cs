using System.Threading;
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

    // ── Helpers ──────────────────────────────────────────────────────────────

    private static TextBox MakeBox(string text) => new() { Text = text };

    private static void Select(TextBox tb, int start, int length) {
        tb.SelectionStart  = start;
        tb.SelectionLength = length;
    }
}
