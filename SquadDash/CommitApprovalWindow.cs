namespace SquadDash;

using System;
using System.Collections.Generic;
using System.Windows;

/// <summary>
/// Stub — full implementation provided by Lyra (UI specialist).
/// This class declares the contract that MainWindow depends on.
/// </summary>
internal sealed class CommitApprovalWindow : Window {
    private readonly Action<CommitApprovalItem>                   _onItemChanged;
    private readonly Action<IReadOnlyList<CommitApprovalItem>>    _onItemsRemoved;
    private readonly Action<DateTimeOffset>                       _onScrollToTurn;

    public CommitApprovalWindow(
        Action<CommitApprovalItem>                onItemChanged,
        Action<IReadOnlyList<CommitApprovalItem>> onItemsRemoved,
        Action<DateTimeOffset>                    onScrollToTurn) {
        _onItemChanged  = onItemChanged;
        _onItemsRemoved = onItemsRemoved;
        _onScrollToTurn = onScrollToTurn;
    }

    public void ReplaceAllItems(IReadOnlyList<CommitApprovalItem> items) { }

    public void AddItem(CommitApprovalItem item) { }
}
