using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;

namespace SquadDash;

internal sealed partial class ButtonsPanelController {
    private static readonly TimeSpan ReorderAnimationDuration = TimeSpan.FromMilliseconds(180);
    private FrameworkElement _reorderPanel = null!;
    private DispatcherTimer _reorderTimer = null!;
    private ButtonReorderGesture? _reorderGesture;
    private Button? _pressedButton;
    private readonly Dictionary<Guid, Button> _reorderButtons = [];
    private readonly Dictionary<Guid, double> _originalTops = [];
    private readonly Dictionary<Guid, double> _targetTops = [];
    private long _pressTimestamp;
    private long _dataRevision;
    private bool _settling;
    private bool _savingOrder;
    private bool _releasingCapture;

    internal Func<IReadOnlyList<Guid>, IReadOnlyList<Guid>, Task<IReadOnlyList<ButtonDefinition>>>? SaveOrderAsync { get; set; }
    internal Func<Task<IReadOnlyList<ButtonDefinition>>>? ReloadOrderAsync { get; set; }
    internal Action<Exception>? ReportReorderError { get; set; }

    private void InitializeReordering(FrameworkElement panel) {
        _reorderPanel = panel;
        _reorderTimer = new DispatcherTimer(DispatcherPriority.Input, panel.Dispatcher) {
            Interval = TimeSpan.FromMilliseconds(16)
        };
        _reorderTimer.Tick += (_, _) => ObserveReorderPointer();
        panel.AddHandler(Mouse.PreviewMouseMoveEvent, new MouseEventHandler((_, e) => {
            ObserveReorderPointer(allowDragStart: true);
            if (_reorderGesture?.SuppressExecution == true) e.Handled = true;
        }), true);
        panel.AddHandler(Mouse.PreviewMouseUpEvent, new MouseButtonEventHandler(OnReorderMouseUp), true);
        panel.AddHandler(Mouse.LostMouseCaptureEvent, new MouseEventHandler((_, _) => {
            if (!_releasingCapture && _reorderGesture?.Phase is
                ButtonReorderPhase.Pressed or ButtonReorderPhase.Dragging)
                CancelReorder();
        }), true);
        panel.MouseLeave += (_, _) => {
            if (_reorderGesture?.Phase == ButtonReorderPhase.Dragging &&
                !PanelBounds().Contains(Mouse.GetPosition(panel).X, Mouse.GetPosition(panel).Y))
                CancelReorder();
        };
        panel.AddHandler(Keyboard.PreviewKeyDownEvent, new KeyEventHandler((_, e) => {
            if (e.Key == Key.Escape && _reorderGesture is not null) {
                CancelReorder();
                e.Handled = true;
            }
        }), true);
        panel.IsVisibleChanged += (_, _) => { if (!panel.IsVisible) CancelReorder(); };
        panel.Unloaded += (_, _) => CancelReorder();
        panel.SizeChanged += (_, _) => CancelReorder();
        panel.AddHandler(ScrollViewer.ScrollChangedEvent,
            new ScrollChangedEventHandler((_, e) => {
                if (e.VerticalChange != 0 || e.HorizontalChange != 0) CancelReorder();
            }), true);
    }

    private void WireReorderButton(Button button) {
        button.PreviewMouseLeftButtonDown += (_, e) => {
            if (_settling || _savingOrder) {
                e.Handled = true;
                return;
            }
            // WPF performs normal Button capture after this preview; never execute on press.
            _pressedButton = button;
            _reorderButtons.Clear();
            _originalTops.Clear();
            _targetTops.Clear();
            _listPanel.UpdateLayout();
            foreach (var item in _listPanel.Children.OfType<Button>()) {
                var id = ((ButtonDefinition)item.Tag).Id;
                item.RenderTransform = new TranslateTransform();
                _reorderButtons.Add(id, item);
                _originalTops.Add(id, item.TranslatePoint(new Point(), _listPanel).Y);
            }
            var point = e.GetPosition(_reorderPanel);
            _reorderGesture = new ButtonReorderGesture(
                ViewModel.Buttons.OrderBy(item => item.SortOrder).Select(item => item.Id).ToArray(),
                _reorderButtons.Keys.ToArray(), ((ButtonDefinition)button.Tag).Id,
                point.X, point.Y, SystemParameters.MinimumHorizontalDragDistance,
                SystemParameters.MinimumVerticalDragDistance);
            _pressTimestamp = Stopwatch.GetTimestamp();
            _reorderTimer.Start();
        };
    }

    private ButtonReorderBounds PanelBounds() => new(0, 0, _reorderPanel.ActualWidth, _reorderPanel.ActualHeight);

    private ButtonReorderBounds DraggedBodyBounds() {
        if (_pressedButton is null) return default;
        // This is the actual animated button body, not a pointer-following decoration.
        var origin = _pressedButton.TranslatePoint(new Point(), _reorderPanel);
        var bounds = new Rect(origin, _pressedButton.RenderSize);
        for (var parent = VisualTreeHelper.GetParent(_pressedButton);
            parent is not null && !ReferenceEquals(parent, _reorderPanel);
            parent = VisualTreeHelper.GetParent(parent)) {
            if (parent is FrameworkElement { ClipToBounds: true } clip) {
                bounds.Intersect(new Rect(clip.TranslatePoint(new Point(), _reorderPanel), clip.RenderSize));
                if (bounds.IsEmpty) return default;
            }
        }
        return new(bounds.X, bounds.Y, bounds.Width, bounds.Height);
    }

    private void ObserveReorderPointer(bool allowDragStart = false) {
        var gesture = _reorderGesture;
        if (gesture is null) {
            _reorderTimer.Stop();
            return;
        }
        if (Mouse.LeftButton != MouseButtonState.Pressed) {
            if (gesture.Phase is ButtonReorderPhase.Pressed or ButtonReorderPhase.Dragging)
                CancelReorder();
            // Keep suppression through the complete routed mouse-up/Click delivery.
            if (!_settling && !_savingOrder) {
                _reorderTimer.Stop();
                _reorderPanel.Dispatcher.BeginInvoke(DispatcherPriority.ContextIdle, new Action(() => {
                    if (ReferenceEquals(gesture, _reorderGesture) && !_settling && !_savingOrder)
                        _reorderGesture = null;
                }));
            }
            return;
        }
        if (gesture.Phase is not (ButtonReorderPhase.Pressed or ButtonReorderPhase.Dragging)) return;
        var point = Mouse.GetPosition(_reorderPanel);
        var wasDragging = gesture.Phase == ButtonReorderPhase.Dragging;
        if (!ReferenceEquals(Mouse.Captured, _pressedButton) ||
            (wasDragging && !PanelBounds().Contains(point.X, point.Y))) {
            CancelReorder();
            return;
        }
        if (!wasDragging && !allowDragStart) return;
        if (!gesture.Move(Stopwatch.GetElapsedTime(_pressTimestamp), point.X, point.Y,
            PanelBounds().Contains(point.X, point.Y), ReferenceEquals(Mouse.Captured, _pressedButton))) {
            if (gesture.Phase == ButtonReorderPhase.Cancelled) RestoreReorder();
            return;
        }
        if (!wasDragging) {
            _pressedButton!.Cursor = Cursors.SizeNS;
            ToolTipService.SetIsEnabled(_pressedButton, false);
            Panel.SetZIndex(_pressedButton, 1);
        }
        var order = gesture.PreviewVisibleOrder;
        var tops = CalculateTops(order);
        var y = Mouse.GetPosition(_listPanel).Y;
        var target = order.Where(id => id != gesture.DraggedId)
            .Count(id => y > tops[id] + _reorderButtons[id].ActualHeight / 2);
        if (order[target] != gesture.DraggedId) {
            gesture.PreviewMove(target);
            AnimateOrder(gesture.PreviewVisibleOrder);
        }
    }

    private async void OnReorderMouseUp(object sender, MouseButtonEventArgs e) {
        if (e.ChangedButton != MouseButton.Left || _reorderGesture is not { } gesture) return;
        var wasPressed = gesture.Phase == ButtonReorderPhase.Pressed;
        if (!wasPressed && gesture.Phase != ButtonReorderPhase.Dragging) {
            e.Handled = true;
            return;
        }
        var point = e.GetPosition(_reorderPanel);
        var outcome = gesture.Release(point.X, point.Y, PanelBounds(), DraggedBodyBounds(),
            ReferenceEquals(Mouse.Captured, _pressedButton));
        if (outcome == ButtonReorderRelease.Click) {
            // Leave the event unhandled: ButtonBase performs the single release Click.
            _reorderGesture = null;
            _reorderTimer.Stop();
            return;
        }
        e.Handled = true;
        ReleaseReorderCapture();
        if (outcome != ButtonReorderRelease.Commit) {
            RestoreReorder();
            return;
        }
        var revision = _dataRevision;
        _savingOrder = true;
        try {
            var save = SaveOrderAsync ?? throw new InvalidOperationException("Button order storage is unavailable.");
            var saved = await save(gesture.PersistedOrder, gesture.PreviewFullOrder);
            if (revision == _dataRevision) {
                ViewModel.Buttons = saved;
                ViewModel.LoadError = null;
            }
            // Retain the existing visuals until the preview animation reaches its slots.
            await WaitForReorderAnimationAsync();
        }
        catch (Exception ex) when (ButtonsUiErrors.IsExpected(ex)) {
            SquadDashTrace.Write("Buttons", $"Reorder failed: {ex.Message}");
            gesture.Cancel();
            AnimateOriginalSlots();
            await WaitForReorderAnimationAsync();
            ReportReorderError?.Invoke(ex);
            if (revision == _dataRevision && ReloadOrderAsync is { } reload) {
                try {
                    var persisted = await reload();
                    if (revision == _dataRevision) {
                        ViewModel.Buttons = persisted;
                        ViewModel.LoadError = null;
                        var visible = persisted.OrderBy(item => item.SortOrder).Select(item => item.Id)
                            .Where(_reorderButtons.ContainsKey).ToArray();
                        if (visible.Length == _reorderButtons.Count) {
                            AnimateOrder(visible);
                            await WaitForReorderAnimationAsync();
                        }
                    }
                }
                catch (Exception reloadError) when (ButtonsUiErrors.IsExpected(reloadError)) {
                    SquadDashTrace.Write("Buttons", $"Reload after reorder failed: {reloadError.Message}");
                    if (revision == _dataRevision)
                        ViewModel.LoadError = $"Unable to reload buttons: {reloadError.Message}";
                    ReportReorderError?.Invoke(reloadError);
                }
            }
        }
        finally {
            _savingOrder = false;
            if (!_settling) {
                ResetReorderVisuals();
                Rebuild();
            }
        }
    }

    private void InvalidateReorder(bool dataChanged = false) {
        if (dataChanged) _dataRevision++;
        CancelReorder();
    }

    private void CancelReorder() {
        if (_reorderGesture?.Phase is not (ButtonReorderPhase.Pressed or ButtonReorderPhase.Dragging)) return;
        _reorderGesture.Cancel();
        RestoreReorder();
    }

    private void RestoreReorder() {
        ReleaseReorderCapture();
        if (_settling || _reorderGesture is null) return;
        _ = RestoreReorderAsync();
    }

    private async Task RestoreReorderAsync() {
        _settling = true;
        AnimateOriginalSlots();
        await WaitForReorderAnimationAsync();
        _settling = false;
        ResetReorderVisuals();
        Rebuild();
    }

    private Task WaitForReorderAnimationAsync() {
        var completion = new TaskCompletionSource();
        var timer = new DispatcherTimer(DispatcherPriority.Render, _reorderPanel.Dispatcher) {
            Interval = ReorderAnimationDuration
        };
        timer.Tick += (_, _) => {
            timer.Stop();
            completion.SetResult();
        };
        timer.Start();
        return completion.Task;
    }

    private Dictionary<Guid, double> CalculateTops(IReadOnlyList<Guid> order) {
        var tops = new Dictionary<Guid, double>();
        var top = 0d;
        foreach (var id in order) {
            var button = _reorderButtons[id];
            top += button.Margin.Top;
            tops[id] = top;
            top += button.ActualHeight + button.Margin.Bottom;
        }
        return tops;
    }

    private void AnimateOrder(IReadOnlyList<Guid> order) {
        foreach (var (id, top) in CalculateTops(order))
            AnimateOffset(id, top - _originalTops[id]);
    }

    private void AnimateOriginalSlots() {
        foreach (var id in _reorderButtons.Keys)
            AnimateOffset(id, 0);
    }

    private void AnimateOffset(Guid id, double destination) {
        var transform = (TranslateTransform)_reorderButtons[id].RenderTransform;
        if (_targetTops.TryGetValue(id, out var previous) && previous == destination) return;
        _targetTops[id] = destination;
        var animation = new DoubleAnimation(transform.Y, destination, ReorderAnimationDuration) {
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
        };
        transform.BeginAnimation(TranslateTransform.YProperty, animation, HandoffBehavior.SnapshotAndReplace);
    }

    private void ReleaseReorderCapture() {
        _releasingCapture = true;
        try {
            if (_pressedButton?.IsMouseCaptured == true) _pressedButton.ReleaseMouseCapture();
        }
        finally { _releasingCapture = false; }
    }

    private void ResetReorderVisuals() {
        if (_pressedButton is not null) {
            _pressedButton.ClearValue(FrameworkElement.CursorProperty);
            ToolTipService.SetIsEnabled(_pressedButton, true);
            Panel.SetZIndex(_pressedButton, 0);
        }
    }
}
