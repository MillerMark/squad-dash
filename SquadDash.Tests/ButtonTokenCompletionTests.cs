using System;
using NUnit.Framework;

namespace SquadDash.Tests;

[TestFixture]
public class ButtonTokenCompletionTests
{
    [Test]
    public void Complete_ReplacesPrefixAndConsumesExistingClosingBrace()
    {
        const string text = "run {work} now";
        const string token = "{workspaceRoot}";
        var caret = text.IndexOf('}');
        var start = text.IndexOf('{');

        var insertion = ButtonTokenCompletion.Complete(text, caret, token);

        Assert.Multiple(() =>
        {
            Assert.That(insertion.Text, Is.EqualTo("run {workspaceRoot} now"));
            Assert.That(insertion.Start, Is.EqualTo(start));
            Assert.That(insertion.Length, Is.EqualTo(caret + 1 - start));
            Assert.That(insertion.CaretIndex, Is.EqualTo(start + token.Length));
        });
    }

    [Test]
    public void Complete_PreservesSurroundingMultilineText()
    {
        const string text = "first line\r\nsecond {work\r\nthird line";
        const string token = "{workspaceRoot}";
        var start = text.IndexOf("{work", StringComparison.Ordinal);
        var caret = start + "{work".Length;

        var insertion = ButtonTokenCompletion.Complete(text, caret, token);

        Assert.Multiple(() =>
        {
            Assert.That(insertion.Text, Is.EqualTo("first line\r\nsecond {workspaceRoot}\r\nthird line"));
            Assert.That(insertion.Start, Is.EqualTo(start));
            Assert.That(insertion.Length, Is.EqualTo("{work".Length));
            Assert.That(insertion.CaretIndex, Is.EqualTo(start + token.Length));
        });
    }

    [TestCase(-1)]
    [TestCase(4)]
    public void TryGetPrefix_InvalidCaret_ReturnsFalse(int caret)
    {
        var ok = ButtonTokenCompletion.TryGetPrefix("abc", caret, out var start, out var prefix);

        Assert.Multiple(() =>
        {
            Assert.That(ok, Is.False);
            Assert.That(start, Is.EqualTo(-1));
            Assert.That(prefix, Is.Empty);
        });
    }

    [Test]
    public void TryGetPrefix_InvalidPrefixDelimiter_ReturnsFalse()
    {
        var ok = ButtonTokenCompletion.TryGetPrefix("{wo rk", "{wo rk".Length, out var start, out var prefix);

        Assert.Multiple(() =>
        {
            Assert.That(ok, Is.False);
            Assert.That(start, Is.EqualTo(-1));
            Assert.That(prefix, Is.Empty);
        });
    }

    [Test]
    public void Complete_UnknownToken_ThrowsArgumentException()
    {
        var ex = Assert.Throws<ArgumentException>(() => ButtonTokenCompletion.Complete("x {work", 7, "{missingToken}"));
        Assert.That(ex!.ParamName, Is.EqualTo("token"));
    }

    [Test]
    public void Complete_InvalidCaretOrPrefix_ThrowsInvalidOperationException()
    {
        Assert.Multiple(() =>
        {
            Assert.That(() => ButtonTokenCompletion.Complete("abc", 99, "{workspaceRoot}"),
                Throws.TypeOf<InvalidOperationException>());
            Assert.That(() => ButtonTokenCompletion.Complete("run token", "run token".Length, "{workspaceRoot}"),
                Throws.TypeOf<InvalidOperationException>());
        });
    }
}
