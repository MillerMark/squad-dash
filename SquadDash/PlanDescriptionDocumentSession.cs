using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;

namespace SquadDash;

/// <summary>Visible-only inspector consumers; owns all handlers for one composed document.</summary>
internal sealed class PlanDescriptionDocumentSession : IDisposable
{
    private readonly FlowDocumentScrollViewer _root;
    private readonly IDisposable _selections;
    private readonly List<FlowDocumentScrollViewer> _viewers = [];
    private int _findIndex = -1;
    private string _query = "";

    internal PlanDescriptionDocumentSession(FlowDocumentScrollViewer root)
    {
        _root = root;
        MarkdownVisibleContent.Bind(root.Document);
        _selections = MarkdownVisibleContent.TrackSelections(root);
        MarkdownVisibleContent.State(root.Document).VisibilityChanged += ResetFind;
        foreach (var (_, nested) in MarkdownVisibleContent.Documents(root.Document, visibleOnly: false))
        {
            var viewer = nested ?? root;
            DataObject.AddCopyingHandler(viewer, OnCopying);
            CommandManager.AddPreviewExecutedHandler(viewer, OnExecuted);
            CommandManager.AddPreviewCanExecuteHandler(viewer, OnCanExecute);
            var copy = new MenuItem { Header = "Copy", Command = ApplicationCommands.Copy, CommandTarget = viewer };
            viewer.ContextMenu = new ContextMenu { Items = { copy } };
            _viewers.Add(viewer);
        }
    }

    internal static string GetCopyText(FlowDocument document) => MarkdownVisibleContent.GetText(document);

    internal static IReadOnlyList<MarkdownVisibleContent.Match> FindAll(FlowDocument document, string text) =>
        MarkdownVisibleContent.FindAll(document, text).ToArray();

    internal bool FindNext(string text)
    {
        if (_query != text) ResetFind();
        _query = text;
        var matches = FindAll(_root.Document, text);
        if (matches.Count == 0) return false;
        _findIndex = (_findIndex + 1) % matches.Count;
        MarkdownVisibleContent.ClearSelections(_root.Document);
        matches[_findIndex].Select(_root);
        return true;
    }

    private void ResetFind() => _findIndex = -1;

    private void OnCanExecute(object sender, CanExecuteRoutedEventArgs e)
    {
        if (e.Command != ApplicationCommands.Copy) return;
        e.CanExecute = MarkdownVisibleContent.GetSelectedText(_root).Length > 0;
        e.Handled = true;
    }

    private void OnExecuted(object sender, ExecutedRoutedEventArgs e)
    {
        if (e.Command != ApplicationCommands.Copy) return;
        TryCopyText(MarkdownVisibleContent.GetSelectedText(_root));
        e.Handled = true;
    }

    private void OnCopying(object sender, DataObjectCopyingEventArgs e) => CopySelection(_root, e);

    internal static void CopySelection(FlowDocumentScrollViewer root, DataObjectCopyingEventArgs e,
        Action<string>? setClipboard = null)
    {
        e.CancelCommand();
        e.Handled = true;
        TryCopyText(MarkdownVisibleContent.GetSelectedText(root), setClipboard);
    }

    internal static bool TryCopyText(string text, Action<string>? setClipboard = null)
    {
        if (string.IsNullOrEmpty(text)) return false;
        try
        {
            (setClipboard ?? Clipboard.SetText)(text);
            return true;
        }
        catch (System.Runtime.InteropServices.ExternalException ex)
        {
            SquadDashTrace.Write("PlanDescription", $"Clipboard is busy; copy was cancelled: {ex.Message}");
            System.Media.SystemSounds.Exclamation.Play();
            return false;
        }
    }

    public void Dispose()
    {
        _selections.Dispose();
        MarkdownVisibleContent.State(_root.Document).VisibilityChanged -= ResetFind;
        foreach (var viewer in _viewers)
        {
            DataObject.RemoveCopyingHandler(viewer, OnCopying);
            CommandManager.RemovePreviewExecutedHandler(viewer, OnExecuted);
            CommandManager.RemovePreviewCanExecuteHandler(viewer, OnCanExecute);
            viewer.ContextMenu = null;
        }
        _viewers.Clear();
        MarkdownVisibleContent.Detach(_root.Document);
    }
}
