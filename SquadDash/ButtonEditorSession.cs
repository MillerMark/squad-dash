using System;
using System.Linq;

namespace SquadDash;

/// <summary>A modeless editing lease tied to one workspace generation and one content revision.</summary>
internal sealed class ButtonEditorSession {
    private readonly ButtonsStore _store;
    private readonly Func<bool> _isCurrentWorkspace;
    private bool _closed;
    internal ButtonDefinition? SavedDefinition { get; private set; }
    internal int InitialSortOrder { get; private set; }

    internal ButtonEditorSession(ButtonsStore store, ButtonDefinition? definition,
        int newSortOrder, Func<bool> isCurrentWorkspace) {
        _store = store;
        SavedDefinition = definition;
        InitialSortOrder = definition?.SortOrder ?? newSortOrder;
        _isCurrentWorkspace = isCurrentWorkspace;
    }

    internal void Invalidate() => _closed = true;

    internal ButtonDefinition Save(ButtonContent content, int sortOrder) {
        if (_closed || !_isCurrentWorkspace())
            throw new ButtonConflictException("The workspace changed. Reopen the button editor in its workspace.");
        var items = _store.Load();
        var maximum = SavedDefinition is null ? items.Count : items.Count - 1;
        if (sortOrder < 0 || sortOrder > maximum)
            throw new ArgumentOutOfRangeException(nameof(sortOrder), $"Sort order must be between 0 and {Math.Max(0, maximum)}.");

        var changeOrder = SavedDefinition is null || sortOrder != InitialSortOrder;
        SavedDefinition = SavedDefinition is { } existing
            ? _store.Update(existing.Id, existing.ContentRevision, content)
            : _store.Create(content);
        // Retain the new identity/revision even if the separate reorder transaction fails.
        // A retry must edit this saved item, never create a duplicate.
        if (changeOrder && SavedDefinition.SortOrder != sortOrder) {
            try {
                var current = _store.Load();
                var expected = current.Select(item => item.Id).ToArray();
                var ordered = expected.Where(id => id != SavedDefinition.Id).ToList();
                ordered.Insert(sortOrder, SavedDefinition.Id);
                var result = _store.Reorder(expected, ordered);
                SavedDefinition = result.Single(item => item.Id == SavedDefinition.Id);
            }
            catch (Exception ex) when (ButtonsUiErrors.IsExpected(ex)) {
                throw new InvalidOperationException(
                    "Button content was saved, but its sort order was not. Retry Save to apply the order; Cancel will not undo the saved content.", ex);
            }
        }
        InitialSortOrder = SavedDefinition.SortOrder;
        return SavedDefinition;
    }
}
