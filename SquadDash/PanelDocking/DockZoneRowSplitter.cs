#nullable enable

using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace SquadDash.PanelDocking;

/// <summary>
/// Vertical side-zone splitter that applies the same minimum/useful-maximum policy as the
/// top-zone splitter engine. The stock WPF <see cref="GridSplitter"/> only honors row minimums,
/// which allowed a compact panel such as Loop to consume nearly the entire zone.
/// </summary>
internal sealed class DockZoneRowSplitter : GridSplitter
{
    private readonly FrameworkElement _beforePanel;
    private readonly RowDefinition _beforeRow;
    private readonly FrameworkElement _afterPanel;
    private readonly RowDefinition _afterRow;

    private UIElement? _dragSurface;
    private DockResizeParticipant[]? _initialParticipants;
    private double _startY;

    public DockZoneRowSplitter(
        FrameworkElement beforePanel,
        RowDefinition beforeRow,
        FrameworkElement afterPanel,
        RowDefinition afterRow)
    {
        _beforePanel = beforePanel;
        _beforeRow = beforeRow;
        _afterPanel = afterPanel;
        _afterRow = afterRow;

        Cursor = Cursors.SizeNS;
        Focusable = false;
    }

    protected override void OnPreviewMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        base.OnPreviewMouseLeftButtonDown(e);

        if (e.ChangedButton != MouseButton.Left || Parent is not UIElement surface)
            return;

        _dragSurface = surface;
        _startY = e.GetPosition(surface).Y;
        _initialParticipants = BuildParticipants();
        CaptureMouse();
        e.Handled = true;
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);

        if (_initialParticipants is null || _dragSurface is null || e.LeftButton != MouseButtonState.Pressed)
            return;

        ApplyDelta(e.GetPosition(_dragSurface).Y - _startY);
        e.Handled = true;
    }

    protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonUp(e);

        if (_initialParticipants is null)
            return;

        if (_dragSurface is not null)
            ApplyDelta(e.GetPosition(_dragSurface).Y - _startY);

        CompleteDrag();
        e.Handled = true;
    }

    protected override void OnLostMouseCapture(MouseEventArgs e)
    {
        base.OnLostMouseCapture(e);
        CompleteDrag(releaseCapture: false);
    }

    private DockResizeParticipant[] BuildParticipants() =>
    [
        BuildParticipant(_beforePanel, _beforeRow),
        BuildParticipant(_afterPanel, _afterRow),
    ];

    private static DockResizeParticipant BuildParticipant(FrameworkElement panel, RowDefinition row)
    {
        var current = row.ActualHeight > 0
            ? row.ActualHeight
            : panel.ActualHeight > 0
                ? panel.ActualHeight
                : Math.Max(1, row.Height.Value);
        var minimum = panel is IDockResizeSizeHint hint
            ? hint.GetMinimumDockSize(DockResizeOrientation.Vertical)
            : row.MinHeight;
        minimum = Math.Max(row.MinHeight, minimum);
        var maximum = (panel as IDockResizeSizeHint)?.GetMaximumUsefulDockSize(DockResizeOrientation.Vertical);

        return new DockResizeParticipant(current, minimum, maximum);
    }

    private void ApplyDelta(double delta)
    {
        if (_initialParticipants is null)
            return;

        var resized = ResizeAdjacent(_initialParticipants[0], _initialParticipants[1], delta);
        _beforeRow.Height = new GridLength(Math.Max(1, resized[0]), GridUnitType.Star);
        _afterRow.Height = new GridLength(Math.Max(1, resized[1]), GridUnitType.Star);
    }

    internal static double[] ResizeAdjacent(
        DockResizeParticipant before,
        DockResizeParticipant after,
        double delta) =>
        // Chain mode treats growth forced by shrinking the other panel as a consequence. This
        // lets a user make Loop smaller when Inbox already absorbs unavoidable zone surplus,
        // while still preventing Loop itself from growing beyond its useful maximum.
        DockResizeEngine.Resize([before, after], 0, DockResizeMode.Chain, delta);

    private void CompleteDrag(bool releaseCapture = true)
    {
        if (_initialParticipants is null)
            return;

        _initialParticipants = null;
        _dragSurface = null;
        if (releaseCapture && IsMouseCaptured)
            ReleaseMouseCapture();

        SquadDashTrace.Write(
            TraceCategory.Docking,
            $"Side-zone row splitter completed: before={_beforeRow.ActualHeight:F0} after={_afterRow.ActualHeight:F0}");
    }
}
