using System;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using ICSharpCode.AvalonEdit;

namespace SquadDash.Tests;

[TestFixture]
internal sealed class AvalonEditClipboardGuardTests
{
    private const int ClipboardCannotOpen = unchecked((int)0x800401D0);
    private const int UnspecifiedNativeFailure = unchecked((int)0x80004005);

    [TestCase(true)]
    [TestCase(false)]
    public void Probe_PreservesClipboardAvailability(bool available)
    {
        Assert.That(AvalonEditClipboardGuard.ContainsTextSafely(() => available), Is.EqualTo(available));
    }

    [Test]
    public void Probe_ClipboardComException_DisablesPaste()
    {
        Assert.That(AvalonEditClipboardGuard.ContainsTextSafely(
            () => throw new COMException("Clipboard locked", ClipboardCannotOpen)), Is.False);
    }

    [Test]
    public void Probe_ClipboardExternalException_DisablesPaste()
    {
        Assert.That(AvalonEditClipboardGuard.ContainsTextSafely(
            () => throw new ExternalException("Clipboard unavailable", ClipboardCannotOpen)), Is.False);
    }

    [Test]
    public void Probe_UnrelatedException_Propagates()
    {
        var failure = new InvalidOperationException("Not a clipboard access failure");
        Assert.That(Assert.Throws<InvalidOperationException>(
            () => AvalonEditClipboardGuard.ContainsTextSafely(() => throw failure)), Is.SameAs(failure));
    }

    [TestCase(true)]
    [TestCase(false)]
    [Apartment(ApartmentState.STA)]
    public void RoutedQuery_HandlesPasteBeforeBubbling_WithoutASecondProbe(bool available)
    {
        var editor = new TextEditor();
        var calls = 0;
        AvalonEditClipboardGuard.Attach(editor, () =>
        {
            calls++;
            return available;
        });
        CommandManager.AddCanExecuteHandler(editor.TextArea, (_, _) =>
            Assert.Fail("The preview guard must prevent the unguarded bubbling query."));

        Assert.That(ApplicationCommands.Paste.CanExecute(null, editor.TextArea), Is.EqualTo(available));
        Assert.That(calls, Is.EqualTo(1));
    }

    [Test]
    [Apartment(ApartmentState.STA)]
    public void RoutedQuery_ClipboardLock_DisablesPaste_AndNextQueryRecovers()
    {
        var editor = new TextEditor();
        var locked = true;
        AvalonEditClipboardGuard.Attach(editor, () => locked
            ? throw new COMException("Clipboard locked", ClipboardCannotOpen)
            : true);
        var pasteItem = new MenuItem
        {
            CommandTarget = editor.TextArea,
            Command = ApplicationCommands.Paste,
        };

        Assert.That(pasteItem.IsEnabled, Is.False);
        Assert.That(ApplicationCommands.Paste.CanExecute(null, editor.TextArea), Is.False);
        locked = false;
        Assert.That(ApplicationCommands.Paste.CanExecute(null, editor.TextArea), Is.True);
        // Reassigning the command forces the same MenuItem query used by a requery sweep.
        pasteItem.Command = null;
        pasteItem.Command = ApplicationCommands.Paste;
        Assert.That(pasteItem.IsEnabled, Is.True);
    }

    [TestCase(true)]
    [TestCase(false)]
    [Apartment(ApartmentState.STA)]
    public void RoutedQuery_NoWritableDocument_DoesNotProbeClipboard(bool readOnly)
    {
        var editor = new TextEditor { IsReadOnly = readOnly };
        if (!readOnly)
            editor.Document = null;
        AvalonEditClipboardGuard.Attach(editor, () =>
            throw new AssertionException("An ineligible editor must not probe the clipboard."));

        Assert.That(ApplicationCommands.Paste.CanExecute(null, editor.TextArea), Is.False);
    }

    [Test]
    [Apartment(ApartmentState.STA)]
    public void RoutedQuery_OtherCommands_KeepTheirNormalHandlers()
    {
        var editor = new TextEditor { Text = "selected" };
        editor.Select(0, editor.Text.Length);
        AvalonEditClipboardGuard.Attach(editor, () =>
            throw new AssertionException("Copy must not probe the clipboard."));

        Assert.That(ApplicationCommands.Copy.CanExecute(null, editor.TextArea), Is.True);
    }

    [TestCase(true)]
    [TestCase(false)]
    [Apartment(ApartmentState.STA)]
    public void Execute_UnrelatedExternalException_Propagates_AndReentrancyResets(bool failInCanExecute)
    {
        var editor = new TextEditor();
        var failure = new ExternalException("Unexpected native failure", UnspecifiedNativeFailure);
        var throwFailure = true;
        var executed = 0;
        var parameter = new object();
        editor.TextArea.CommandBindings.Insert(0, new CommandBinding(
            ApplicationCommands.Paste,
            (_, e) =>
            {
                if (throwFailure && !failInCanExecute)
                    throw failure;
                Assert.That(e.Parameter, Is.SameAs(parameter));
                executed++;
            },
            (_, e) =>
            {
                if (throwFailure && failInCanExecute)
                    throw failure;
                e.CanExecute = true;
                e.Handled = true;
            }));
        AvalonEditClipboardGuard.Attach(editor, () => true);

        Assert.That(ApplicationCommands.Paste.CanExecute(parameter, editor.TextArea), Is.True);
        Assert.That(Assert.Throws<ExternalException>(
            () => ApplicationCommands.Paste.Execute(parameter, editor.TextArea)), Is.SameAs(failure));
        Assert.That(executed, Is.Zero);

        throwFailure = false;
        ApplicationCommands.Paste.Execute(parameter, editor.TextArea);
        Assert.That(executed, Is.EqualTo(1), "Reentrancy guard must reset after exception.");
    }

    [TestCase(true)]
    [TestCase(false)]
    [Apartment(ApartmentState.STA)]
    public void Execute_LockAfterRequery_IsGuarded_AndNativePasteRecovers(bool failInCanExecute)
    {
        var editor = new TextEditor();
        var locked = true;
        var executed = 0;
        var parameter = new object();
        editor.TextArea.CommandBindings.Insert(0, new CommandBinding(
            ApplicationCommands.Paste,
            (_, e) =>
            {
                if (locked)
                    throw new COMException("Clipboard locked", ClipboardCannotOpen);
                Assert.That(e.Parameter, Is.SameAs(parameter));
                executed++;
            },
            (_, e) =>
            {
                if (locked && failInCanExecute)
                    throw new COMException("Clipboard locked", ClipboardCannotOpen);
                e.CanExecute = true;
                e.Handled = true;
            }));
        AvalonEditClipboardGuard.Attach(editor, () => true);

        Assert.That(ApplicationCommands.Paste.CanExecute(parameter, editor.TextArea), Is.True);
        Assert.DoesNotThrow(() => ApplicationCommands.Paste.Execute(parameter, editor.TextArea));
        Assert.That(executed, Is.Zero);
        locked = false;
        ApplicationCommands.Paste.Execute(parameter, editor.TextArea);
        Assert.That(executed, Is.EqualTo(1), "The normal native paste handler must execute exactly once.");
    }
}
