using System;
using System.Collections.Generic;
using System.Linq;

namespace SquadDash;

internal sealed class PromptQueueItem {
    public string Id             { get; } = Guid.NewGuid().ToString("N");
    public string Text           { get; set; } = "";
    public bool   IsDictated     { get; set; }
    public bool   IsEditing      { get; set; }
    public int    SequenceNumber { get; set; }
    public int    CaretIndex     { get; set; }
    public int    SelectionStart { get; set; }
    public int    SelectionLength { get; set; }
}

internal sealed class PromptQueue {
    private readonly List<PromptQueueItem> _items = new();

    public IReadOnlyList<PromptQueueItem> Items => _items;

    public void Enqueue(string text, int seqNum, bool isDictated = false) =>
        _items.Add(new PromptQueueItem { Text = text, SequenceNumber = seqNum, IsDictated = isDictated });

    /// <summary>Removes and returns the first non-editing item, or null if none exists.</summary>
    public PromptQueueItem? DequeueFirstReady() {
        var item = _items.FirstOrDefault(i => !i.IsEditing);
        if (item is not null)
            _items.Remove(item);
        return item;
    }

    public void Remove(string id) {
        var item = _items.FirstOrDefault(i => i.Id == id);
        if (item is not null)
            _items.Remove(item);
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
    /// Reassigns SequenceNumber values 1..N in current list order.
    /// Call after any reordering operation.
    /// </summary>
    public void RenumberSequentially() {
        for (int i = 0; i < _items.Count; i++)
            _items[i].SequenceNumber = i + 1;
    }

    public bool HasReadyItems => _items.Any(i => !i.IsEditing);

    public int Count => _items.Count;
}
