using System;
using System.Linq;
using System.Windows.Controls;

namespace SquadDash;

internal sealed class PanelMenuItemSorter
{
    private bool _isSorting;

    internal void Sort(ItemCollection? items)
    {
        if (items is null || _isSorting)
            return;

        _isSorting = true;
        try
        {
            var panels = items.Cast<object>()
                .TakeWhile(item => item is not Separator)
                .Select((item, index) => (Item: item as MenuItem, Index: index))
                .Where(entry => entry.Item is { IsCheckable: true })
                .ToArray();
            var sorted = panels.Select(entry => entry.Item!)
                .OrderBy(item => item.Header as string ?? string.Empty, StringComparer.OrdinalIgnoreCase)
                .ToArray();

            if (panels.Select(entry => entry.Item).SequenceEqual(sorted))
                return;

            // Remove backwards, then restore the original slots without replacing controls.
            for (var i = panels.Length - 1; i >= 0; i--)
                items.RemoveAt(panels[i].Index);
            for (var i = 0; i < panels.Length; i++)
                items.Insert(panels[i].Index, sorted[i]);
        }
        finally
        {
            _isSorting = false;
        }
    }
}
