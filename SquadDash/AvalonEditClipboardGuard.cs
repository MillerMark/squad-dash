using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;
using ICSharpCode.AvalonEdit;
using ICSharpCode.AvalonEdit.Editing;

namespace SquadDash;

internal static class AvalonEditClipboardGuard
{
    private const int ClipboardCannotOpen = unchecked((int)0x800401D0);

    internal static void Attach(TextEditor editor, Func<bool>? containsText = null)
    {
        var area = editor.TextArea;
        containsText ??= Clipboard.ContainsText;
        // The handler is owned only by the TextArea, so it needs no global unsubscription
        // and remains active when an editor is unloaded and later reattached.
        CommandManager.AddPreviewCanExecuteHandler(area, (_, e) =>
            PreviewCanExecute(area, e, containsText));

        var executingPaste = false;
        CommandManager.AddPreviewExecutedHandler(area, (_, e) =>
        {
            if (e.Command != ApplicationCommands.Paste || executingPaste)
                return;

            // WPF also invokes the binding's CanExecute directly during Execute, bypassing
            // preview queries. Re-enter once so that entire native paste runs inside the guard.
            executingPaste = true;
            try
            {
                ApplicationCommands.Paste.Execute(e.Parameter, area);
            }
            catch (ExternalException ex) when (ex.HResult == ClipboardCannotOpen)
            {
                // The clipboard can become unavailable between CanExecute and Execute.
            }
            finally
            {
                executingPaste = false;
                e.Handled = true;
            }
        });
    }

    private static void PreviewCanExecute(
        TextArea area, CanExecuteRoutedEventArgs e, Func<bool> containsText)
    {
        if (e.Command != ApplicationCommands.Paste)
            return;

        // Match AvalonEdit 6.3.1's CanPaste eligibility, but own the entire query.
        // A successful preliminary probe followed by AvalonEdit's probe would still race.
        e.CanExecute = area.Document is not null
            && area.ReadOnlySectionProvider.CanInsert(area.Caret.Offset)
            && ContainsTextSafely(containsText);
        e.Handled = true;
    }

    internal static bool ContainsTextSafely(Func<bool> containsText)
    {
        try
        {
            return containsText();
        }
        catch (ExternalException)
        {
            // Includes COMException/CLIPBRD_E_CANT_OPEN: retry on the next command requery.
            return false;
        }
    }
}
