using System;
using System.Collections.Generic;
using System.Linq;

namespace SquadDash;

internal enum ButtonReorderPhase { Pressed, Dragging, Committed, Cancelled, ClickReleased }
internal enum ButtonReorderRelease { Click, Commit, Cancel }

internal readonly record struct ButtonReorderBounds(double X, double Y, double Width, double Height) {
    internal bool Contains(double x, double y) =>
        Width > 0 && Height > 0 && x >= X && x <= X + Width && y >= Y && y <= Y + Height;
}

/// <summary>UI-free decisions; elapsed time and coordinates are supplied by the input adapter.</summary>
internal sealed class ButtonReorderGesture {
    internal static readonly TimeSpan HoldInterval = TimeSpan.FromMilliseconds(350);
    private readonly double _startX;
    private readonly double _startY;
    private readonly double _thresholdX;
    private readonly double _thresholdY;
    private readonly Guid[] _persisted;
    private readonly Guid[] _visible;
    private Guid[] _preview;

    internal ButtonReorderPhase Phase { get; private set; } = ButtonReorderPhase.Pressed;
    internal bool SuppressExecution { get; private set; }
    internal Guid DraggedId { get; }
    internal IReadOnlyList<Guid> PersistedOrder => Array.AsReadOnly(_persisted);
    internal IReadOnlyList<Guid> OriginalVisibleOrder => Array.AsReadOnly(_visible);
    internal IReadOnlyList<Guid> PreviewVisibleOrder => Array.AsReadOnly(_preview);
    internal IReadOnlyList<Guid> PreviewFullOrder => MapVisibleOrder(_persisted, _visible, _preview);

    internal ButtonReorderGesture(IReadOnlyList<Guid> persistedOrder, IReadOnlyList<Guid> visibleOrder,
        Guid draggedId, double startX, double startY, double thresholdX, double thresholdY) {
        _persisted = persistedOrder.ToArray();
        _visible = visibleOrder.ToArray();
        _preview = _visible.ToArray();
        MapVisibleOrder(_persisted, _visible, _preview);
        if (!_visible.Contains(draggedId))
            throw new ArgumentException("The dragged button must be visible.", nameof(draggedId));
        if (!double.IsFinite(thresholdX) || !double.IsFinite(thresholdY) || thresholdX < 0 || thresholdY < 0)
            throw new ArgumentOutOfRangeException(nameof(thresholdX));
        DraggedId = draggedId;
        _startX = startX;
        _startY = startY;
        _thresholdX = thresholdX;
        _thresholdY = thresholdY;
    }

    internal bool Move(TimeSpan elapsed, double x, double y, bool insidePanel, bool captureValid) {
        if (Phase is not (ButtonReorderPhase.Pressed or ButtonReorderPhase.Dragging)) return false;
        if (!captureValid || (!insidePanel && Phase == ButtonReorderPhase.Dragging)) {
            Cancel();
            return false;
        }
        if (insidePanel && Phase == ButtonReorderPhase.Pressed && elapsed >= HoldInterval &&
            (Math.Abs(x - _startX) > _thresholdX || Math.Abs(y - _startY) > _thresholdY)) {
            Phase = ButtonReorderPhase.Dragging;
            SuppressExecution = true;
        }
        return Phase == ButtonReorderPhase.Dragging;
    }

    internal void PreviewMove(int targetIndex) {
        if (Phase == ButtonReorderPhase.Dragging)
            _preview = MoveVisible(_preview, DraggedId, targetIndex);
    }

    internal ButtonReorderRelease Release(double x, double y,
        ButtonReorderBounds panelBounds, ButtonReorderBounds buttonBounds, bool captureValid) {
        if (!captureValid || !panelBounds.Contains(x, y) || !buttonBounds.Contains(x, y)) {
            Cancel();
            return ButtonReorderRelease.Cancel;
        }
        if (Phase == ButtonReorderPhase.Dragging) {
            Phase = ButtonReorderPhase.Committed;
            return ButtonReorderRelease.Commit;
        }
        if (Phase == ButtonReorderPhase.Pressed) {
            Phase = ButtonReorderPhase.ClickReleased;
            return ButtonReorderRelease.Click;
        }
        return ButtonReorderRelease.Cancel;
    }

    internal void Cancel() {
        SuppressExecution = true;
        Phase = ButtonReorderPhase.Cancelled;
        _preview = _visible.ToArray();
    }

    internal static Guid[] MoveVisible(IReadOnlyList<Guid> visibleOrder, Guid draggedId, int targetIndex) {
        var moved = visibleOrder.ToList();
        if (targetIndex < 0 || targetIndex >= moved.Count)
            throw new ArgumentOutOfRangeException(nameof(targetIndex));
        if (!moved.Remove(draggedId))
            throw new ArgumentException("The dragged button must be visible.", nameof(draggedId));
        moved.Insert(targetIndex, draggedId);
        return moved.ToArray();
    }

    /// <summary>Replaces only visible slots; hidden IDs never change their full-list positions.</summary>
    internal static Guid[] MapVisibleOrder(IReadOnlyList<Guid> persistedOrder,
        IReadOnlyList<Guid> originalVisibleOrder, IReadOnlyList<Guid> previewVisibleOrder) {
        var visible = originalVisibleOrder.ToHashSet();
        if (persistedOrder.Distinct().Count() != persistedOrder.Count ||
            visible.Count != originalVisibleOrder.Count ||
            previewVisibleOrder.Count != visible.Count ||
            !visible.SetEquals(previewVisibleOrder) ||
            !persistedOrder.Where(visible.Contains).SequenceEqual(originalVisibleOrder))
            throw new ArgumentException("Visible orders must be permutations of the persisted visible slots.");
        var next = 0;
        return persistedOrder.Select(id => visible.Contains(id) ? previewVisibleOrder[next++] : id).ToArray();
    }
}
