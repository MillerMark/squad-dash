using System;
using System.Collections.Generic;
using System.Linq;

namespace SquadDash;

internal sealed class PromptQueueItem {
    public string Id             { get; } = Guid.NewGuid().ToString("N");
    public string Text           { get; set; } = "";
    public bool   IsEditing      { get; set; }
    public int    SequenceNumber { get; set; }
}

internal sealed class PromptQueue {
    private readonly List<PromptQueueItem> _items = new();

    public IReadOnlyList<PromptQueueItem> Items => _items;

    public void Enqueue(string text, int seqNum) =>
        _items.Add(new PromptQueueItem { Text = text, SequenceNumber = seqNum });

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

    public bool HasReadyItems => _items.Any(i => !i.IsEditing);

    public int Count => _items.Count;
}
