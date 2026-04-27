using System;
using System.Collections.Generic;
using System.Linq;

namespace SquadDash;

internal sealed record IntelliSenseState(
    char TriggerChar,
    int TriggerPosition,
    string Filter,
    bool FilterIncludesTrigger,
    IReadOnlyList<string> AllSuggestions,
    IReadOnlyList<string> FilteredSuggestions,
    int SelectedIndex);

internal static class IntelliSenseController {
    /// <summary>
    /// Returns non-null state if IntelliSense should open.
    /// triggerPosition = index of the trigger char in text.
    /// </summary>
    public static IntelliSenseState? TryActivate(
        char triggerChar,
        int triggerPosition,
        IReadOnlyList<string> suggestions,
        bool filterIncludesTrigger = false) {
        if (suggestions.Count == 0)
            return null;

        return new IntelliSenseState(
            TriggerChar: triggerChar,
            TriggerPosition: triggerPosition,
            Filter: string.Empty,
            FilterIncludesTrigger: filterIncludesTrigger,
            AllSuggestions: suggestions,
            FilteredSuggestions: suggestions,
            SelectedIndex: 0);
    }

    /// <summary>
    /// Call on every TextChanged. Returns updated state or null if should close.
    /// </summary>
    public static IntelliSenseState? UpdateFromText(
        IntelliSenseState state,
        string text,
        int caretIndex) {
        // If trigger char no longer exists at trigger position, close
        if (state.TriggerPosition >= text.Length || text[state.TriggerPosition] != state.TriggerChar)
            return null;

        // If caret is before or at trigger position, close
        if (caretIndex <= state.TriggerPosition)
            return null;

        var filter = state.FilterIncludesTrigger
            ? text[state.TriggerPosition..caretIndex]
            : text.Substring(state.TriggerPosition + 1, caretIndex - state.TriggerPosition - 1);

        var filtered = state.AllSuggestions
            .Where(s => s.StartsWith(filter, StringComparison.OrdinalIgnoreCase))
            .ToList();

        if (filtered.Count == 0)
            return null;

        var clampedIndex = Math.Clamp(state.SelectedIndex, 0, filtered.Count - 1);

        return state with {
            Filter = filter,
            FilteredSuggestions = filtered,
            SelectedIndex = clampedIndex
        };
    }

    /// <summary>
    /// delta = -1 for up, +1 for down. Clamps to valid range.
    /// </summary>
    public static IntelliSenseState MoveSelection(IntelliSenseState state, int delta) {
        var newIndex = Math.Clamp(
            state.SelectedIndex + delta,
            0,
            state.FilteredSuggestions.Count - 1);
        return state with { SelectedIndex = newIndex };
    }

    /// <summary>
    /// Returns (newText, newCaretIndex) after inserting the selected suggestion.
    /// Replaces from state.TriggerPosition to caretIndex with the suggestion text.
    /// </summary>
    public static (string NewText, int NewCaretIndex) Accept(
        IntelliSenseState state,
        string currentText,
        int caretIndex) {
        var suggestion = state.FilteredSuggestions[state.SelectedIndex];

        // Replace from TriggerPosition to caretIndex with the suggestion text
        var before = currentText[..state.TriggerPosition];
        var after = caretIndex < currentText.Length ? currentText[caretIndex..] : string.Empty;
        var newText = before + suggestion + after;
        var newCaret = state.TriggerPosition + suggestion.Length;

        return (newText, newCaret);
    }
}
