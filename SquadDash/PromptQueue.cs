using System;
using System.Collections.Generic;
using System.Linq;

namespace SquadDash;

internal sealed class PromptQueueItem {
    public string Id             { get; } = Guid.NewGuid().ToString("N");
    public string Text           { get; set; } = "";
    public bool   IsDictated     { get; set; }
    public bool   IsFromRemote   { get; set; }
    public bool   IsEditing      { get; set; }
    public bool   IsSystemInjected { get; set; }  // true for auto-injected follow-ups (not user-typed)
    public int    SequenceNumber { get; set; }
    /// <summary>Session-unique creation number. Assigned once at enqueue; never renumbered.</summary>
    public int    QueueNumber    { get; set; }
    public int    CaretIndex     { get; set; }
    public int    SelectionStart { get; set; }
    public int    SelectionLength { get; set; }
    // ── Sim fields (set by /test-queue; ignored by non-sim code paths) ────
    public bool    IsSimEntry       { get; set; }
    public string? SimResponse      { get; set; }
    public int     SimDelaySeconds  { get; set; }
    /// <summary>Optional tag identifying the feature that queued this item (e.g. "branch-indicator").</summary>
    public string? SourceTag        { get; set; }
}

internal sealed class PromptQueue {
    private readonly List<PromptQueueItem> _items = new();

    public IReadOnlyList<PromptQueueItem> Items => _items;

    /// <summary>Fired when an item is removed for any reason (user delete, dispatch, clear).</summary>
    public event Action<PromptQueueItem>? ItemRemoved;

    public void Enqueue(string text, int seqNum, bool isDictated = false, bool isFromRemote = false, bool isSystemInjected = false, string? sourceTag = null) =>
        _items.Add(new PromptQueueItem { Text = text, SequenceNumber = seqNum, IsDictated = isDictated, IsFromRemote = isFromRemote, IsSystemInjected = isSystemInjected, SourceTag = sourceTag });

    /// <summary>Adds a fully-constructed item (e.g. a sim item) to the back of the queue.</summary>
    public void EnqueueItem(PromptQueueItem item) => _items.Add(item);

    /// <summary>Adds a fully-constructed item to the front of the queue.</summary>
    public void EnqueueItemAtFront(PromptQueueItem item) => _items.Insert(0, item);

    /// <summary>Removes and returns the first non-editing item, or null if none exists.</summary>
    public PromptQueueItem? DequeueFirstReady() {
        var item = _items.FirstOrDefault(i => !i.IsEditing && i.SourceTag != "guided-tour-dummy");
        if (item is not null)
        {
            _items.Remove(item);
            ItemRemoved?.Invoke(item);
        }
        return item;
    }

    public void Remove(string id) {
        var item = _items.FirstOrDefault(i => i.Id == id);
        if (item is not null)
        {
            _items.Remove(item);
            ItemRemoved?.Invoke(item);
        }
    }

    /// <summary>Inserts a new item at index 0, making it the next item to dispatch.</summary>
    public PromptQueueItem EnqueueAtFront(string text, int seqNum, string? sourceTag = null) {
        var item = new PromptQueueItem { Text = text, SequenceNumber = seqNum, SourceTag = sourceTag };
        _items.Insert(0, item);
        return item;
    }

    /// <summary>
    /// Moves the item with the given id to the front of the queue (index 0),
    /// making it the next item to be dispatched.
    /// </summary>
    public void MoveToFront(string id) {
        var index = _items.FindIndex(i => i.Id == id);
        if (index <= 0) return; // already first or not found
        var item = _items[index];
        _items.RemoveAt(index);
        _items.Insert(0, item);
    }

    /// <summary>
    /// Moves the item with <paramref name="id"/> to <paramref name="newIndex"/> within the
    /// internal list.  The index is applied <em>after</em> the item has been removed, so
    /// valid values are 0 … Count-1.  Out-of-range values are clamped automatically.
    /// </summary>
    public void Reorder(string id, int newIndex)
    {
        var item = _items.FirstOrDefault(i => i.Id == id);
        if (item is null) return;
        _items.Remove(item);
        newIndex = Math.Clamp(newIndex, 0, _items.Count);
        _items.Insert(newIndex, item);
    }

    /// <summary>
    /// Reassigns SequenceNumber values 1..N in current list order.
    /// Call after any reordering operation.
    /// </summary>
    public void RenumberSequentially(){
        for (int i = 0; i < _items.Count; i++)
            _items[i].SequenceNumber = i + 1;
    }

    /// <summary>
    /// Removes all items whose <see cref="PromptQueueItem.SourceTag"/> matches <paramref name="tag"/>.
    /// </summary>
    public void RemoveByTag(string tag)
    {
        var toRemove = _items.Where(i => i.SourceTag == tag).ToList();
        foreach (var item in toRemove)
        {
            _items.Remove(item);
            ItemRemoved?.Invoke(item);
        }
    }

    public bool HasReadyItems => _items.Any(i => !i.IsEditing);

    public int Count => _items.Count;
}
