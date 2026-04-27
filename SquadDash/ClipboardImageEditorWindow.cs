using System;
using System.IO;
using System.Linq;
using System.Windows;
using System.Text.Json;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Controls;
using System.Windows.Threading;
using System.Collections.Generic;
using System.Windows.Media.Imaging;

namespace SquadDash;

/// <summary>
/// Standalone image-editing dialog that pre-loads an image from the clipboard,
/// shows a resizable crop rectangle, and supports arrow and cursor-overlay annotations.
///
/// Pattern: code-behind only (no XAML), all colours via SetResourceReference —
/// consistent with <see cref="AgentInfoWindow"/> and <see cref="ScreenshotOverlayWindow"/>.
///
/// After <c>ShowDialog()</c> returns, check <see cref="Result"/>:
/// non-null = the user clicked "Insert Image" (cropped + annotated bitmap);
/// null = the user cancelled.
/// </summary>
internal sealed class ClipboardImageEditorWindow : Window
{
    // ── Result ────────────────────────────────────────────────────────────────

    internal BitmapSource? Result { get; private set; }

    // ── Hit zones ─────────────────────────────────────────────────────────────

    private enum HitZone { None, Move, NW, N, NE, E, SE, S, SW, W }

    // ── Constants ─────────────────────────────────────────────────────────────

    private const double HandleSize = 9.0;
    private const double HitPad = 5.0;
    private const double MinSize = 24.0;

    // ── Source image ──────────────────────────────────────────────────────────

    private readonly BitmapSource _clipboardImage;

    // ── Selection ─────────────────────────────────────────────────────────────

    private Rect _sel;
    private HitZone _activeZone = HitZone.None;
    private Point _dragStart;
    private Rect _dragOriginal;

    // ── Visual elements ───────────────────────────────────────────────────────

    private readonly Canvas _canvas;
    private readonly Rectangle _selBorderRect;
    private readonly Rectangle[] _handles = new Rectangle[8];

    // Dim strips: four black 50%-opacity rects that cover the area outside _sel
    private readonly Rectangle _dimTop, _dimBottom, _dimLeft, _dimRight;

    // ── Round corners ─────────────────────────────────────────────────────────

    private bool _roundCorners;

    // Physical-pixel corner radius applied to the output PNG when _roundCorners is true.
    // Windows 11 window corners are ~8 px
    private const int CornerRadiusPx = 8;

    // ── Zoom ──────────────────────────────────────────────────────────────────

    private double _zoom = 1.0;
    private readonly ScaleTransform _scaleTransform = new(1.0, 1.0);

    // ── Theme ─────────────────────────────────────────────────────────────────

    private readonly string _themeName;

    // ── Annotation — arrows ───────────────────────────────────────────────────

    private readonly List<AnnotationArrow> _arrows = new();
    private AnnotationArrow? _draggingArrow;
    private AnnotationArrow? _selectedArrow;
    private bool _inArrowMode;
    private Button? _addArrowBtn;

    // Arrow drag sub-state
    private bool _tailDragging;
    private bool _bodyDragging;
    private Point _tailDragStartMouse;
    private Point _bodyDragStartMouse;
    private double _bodyDragStartOffsetX;
    private double _bodyDragStartOffsetY;

    // Color picker
    private StackPanel? _colorPickerPanel;
    private AnnotationArrow? _colorPickerArrow;

    // Arrow creation defaults (shared with ScreenshotOverlayWindow via the same JSON file)
    private Color _defaultArrowColor = Color.FromRgb(255, 120, 20);
    private double _defaultArrowAngleDeg = 225.0;
    private double _defaultArrowLength = 15.0;
    private double _defaultTailLength = -1.0;

    // ── Annotation — cursor overlay ───────────────────────────────────────────

    private Image? _cursorImage;
    private bool _cursorEnabled;
    private bool _inCursorPlacementMode;
    private bool _draggingCursor;

    // ── Mode hint ─────────────────────────────────────────────────────────────

    private Border? _modeHintBorder;
    private TextBlock? _modeHintText;

    // ── Undo / redo ───────────────────────────────────────────────────────────

    private readonly Stack<EditorSnapshot> _undoStack = new();
    private readonly Stack<EditorSnapshot> _redoStack = new();
    private EditorSnapshot? _preDragSnapshot;
    private bool _suppressUndo;

    // ── New-selection rubber-band (when no crop region exists) ─────────────────

    private bool _creatingNewSel;
    private Point _newSelAnchor;

    // ────────────────────────────────────────────────────────────────────────
    // Constructor
    // ────────────────────────────────────────────────────────────────────────

    internal ClipboardImageEditorWindow(Window owner, BitmapSource clipboardImage)
    {
        _clipboardImage = clipboardImage ?? throw new ArgumentNullException(nameof(clipboardImage));
        _themeName = AgentStatusCard.IsDarkTheme ? "dark" : "light";

        Owner = owner;
        Title = "Edit Clipboard Image";
        WindowStyle = WindowStyle.SingleBorderWindow;
        ResizeMode = ResizeMode.CanResizeWithGrip;
        ShowInTaskbar = false;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        this.SetResourceReference(BackgroundProperty, "AppSurface");

        LoadArrowDefaults();

        // ── Compute display size ─────────────────────────────────────────────
        // Canvas shows image at full resolution but window is capped to 85% of work area.
        // The user can Ctrl+scroll to zoom in/out (the canvas stays at fixed logical size;
        // zoom is applied via ScaleTransform on the wrapper so DoInsertImage is unaffected).
        var workArea = SystemParameters.WorkArea;
        double imgW = clipboardImage.PixelWidth;
        double imgH = clipboardImage.PixelHeight;
        double maxWinW = workArea.Width * 0.85;
        double maxWinH = workArea.Height * 0.85;

        // Canvas is always the full image resolution (no pre-scaling).
        double dispW = imgW;
        double dispH = imgH;

        // Set initial window size: image + chrome, capped to 85% work area.
        Width = Math.Min(maxWinW, dispW + 20);
        Height = Math.Min(maxWinH, dispH + 110); // toolbar ~40 + chrome ~70

        // ── Canvas ───────────────────────────────────────────────────────────

        _canvas = new Canvas
        {
            Width = dispW,
            Height = dispH,
            ClipToBounds = true,
            Cursor = Cursors.Arrow,
            Background = Brushes.Transparent   // must be non-null for canvas to receive mouse events
        };

        // Image fills the entire canvas (background layer).
        var imageCtrl = new Image
        {
            Source = clipboardImage,
            Width = dispW,
            Height = dispH,
            Stretch = Stretch.Fill,
            IsHitTestVisible = false
        };
        RenderOptions.SetBitmapScalingMode(imageCtrl, BitmapScalingMode.HighQuality);
        _canvas.Children.Add(imageCtrl);
        Canvas.SetLeft(imageCtrl, 0);
        Canvas.SetTop(imageCtrl, 0);

        // Initial selection = full image.
        _sel = new Rect(0, 0, dispW, dispH);

        // ── Dim strips (outside selection) ───────────────────────────────────
        var dimBrush = new SolidColorBrush(Color.FromArgb(128, 0, 0, 0));
        _dimTop = new Rectangle { Fill = dimBrush, IsHitTestVisible = false };
        _dimBottom = new Rectangle { Fill = dimBrush, IsHitTestVisible = false };
        _dimLeft = new Rectangle { Fill = dimBrush, IsHitTestVisible = false };
        _dimRight = new Rectangle { Fill = dimBrush, IsHitTestVisible = false };
        foreach (var d in new[] { _dimTop, _dimBottom, _dimLeft, _dimRight })
        {
            Panel.SetZIndex(d, 2);
            _canvas.Children.Add(d);
        }

        // ── Selection border ─────────────────────────────────────────────────

        _selBorderRect = new Rectangle
        {
            StrokeThickness = 2,
            Fill = Brushes.Transparent,
            IsHitTestVisible = false
        };
        _selBorderRect.SetResourceReference(Shape.StrokeProperty, "DocumentLinkText");
        _canvas.Children.Add(_selBorderRect);
        Panel.SetZIndex(_selBorderRect, 5);

        // ── Resize handles ───────────────────────────────────────────────────

        for (int i = 0; i < 8; i++)
        {
            var h = new Rectangle
            {
                Width = HandleSize,
                Height = HandleSize,
                StrokeThickness = 1,
                RadiusX = 1.5,
                RadiusY = 1.5
            };
            h.SetResourceReference(Shape.FillProperty, "DocumentLinkText");
            h.SetResourceReference(Shape.StrokeProperty, "AppSurface");
            _handles[i] = h;
            _canvas.Children.Add(h);
        }

        // ── Canvas events ────────────────────────────────────────────────────

        _canvas.MouseDown += Canvas_MouseDown;
        _canvas.MouseMove += Canvas_MouseMove;
        _canvas.MouseUp += Canvas_MouseUp;
        KeyDown += Window_KeyDown;

        // Ctrl+scroll = zoom in/out. The ScaleTransform lives on the wrapper (not
        // the canvas), so DoInsertImage renders _canvas at its original logical size.
        PreviewMouseWheel += (_, e) =>
        {
            if ((Keyboard.Modifiers & ModifierKeys.Control) == 0) return;
            var factor = e.Delta > 0 ? 1.1 : 1.0 / 1.1;
            _zoom = Math.Max(0.1, Math.Min(8.0, _zoom * factor));
            _scaleTransform.ScaleX = _zoom;
            _scaleTransform.ScaleY = _zoom;
            e.Handled = true;
        };

        // ── Root layout: scrollable canvas on top, toolbar docked at the bottom
        var canvasWrapper = new Border
        {
            Child = _canvas,
            LayoutTransform = _scaleTransform,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(4)
        };

        var scrollViewer = new ScrollViewer
        {
            HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            Content = canvasWrapper
        };
        scrollViewer.SetResourceReference(BackgroundProperty, "AppSurface");

        var toolbar = BuildToolbar();
        var root = new DockPanel { LastChildFill = true };
        DockPanel.SetDock(toolbar, Dock.Bottom);
        root.Children.Add(toolbar);
        root.Children.Add(scrollViewer);
        Content = root;

        Loaded += (_, _) => RefreshLayout();
    }

    // ── Toolbar ───────────────────────────────────────────────────────────────

    private Border BuildToolbar()
    {
        _addArrowBtn = new Button { Content = "↗ Arrow", Width = 80, Height = 28, Margin = new Thickness(0, 0, 4, 0) };
        var cursorBtn = new Button { Content = "⌖ Cursor", Width = 80, Height = 28, Margin = new Thickness(0, 0, 4, 0) };
        var roundCornersBtn = new Button { Content = "⌐ Round Corners", Width = 112, Height = 28, Margin = new Thickness(0, 0, 4, 0), ToolTip = $"Mask the {CornerRadiusPx}px corners transparent in the output PNG" };
        var insertBtn = new Button { Content = "Insert Image", Width = 96, Height = 28, Margin = new Thickness(0, 0, 4, 0) };
        var cancelBtn = new Button { Content = "Cancel", Width = 70, Height = 28 };

        foreach (var btn in new[] { _addArrowBtn, cursorBtn, roundCornersBtn, insertBtn, cancelBtn })
            btn.SetResourceReference(Control.StyleProperty, "ThemedButtonStyle");

        _addArrowBtn.Click += (_, _) =>
        {
            if (_inArrowMode) { ExitArrowMode(); return; }
            EnterArrowMode();
            _addArrowBtn.Content = "✓ ↗ Arrow";
        };

        cursorBtn.Click += (_, _) =>
        {
            _cursorEnabled = !_cursorEnabled;
            if (_cursorEnabled)
            {
                _inCursorPlacementMode = true;
                cursorBtn.Content = "✓ ⌖ Cursor";
                ShowModeHint("Click to place the cursor indicator");
            }
            else
            {
                _inCursorPlacementMode = false;
                cursorBtn.Content = "⌖ Cursor";
                ToggleCursorOverlay(false);
                HideModeHint();
            }
        };

        roundCornersBtn.Click += (_, _) =>
        {
            _roundCorners = !_roundCorners;
            roundCornersBtn.Content = _roundCorners ? "✓ Round Corners" : "⌐ Round Corners";
        };

        insertBtn.Click += (_, _) => DoInsertImage();
        cancelBtn.Click += (_, _) => Close();

        var _zoomLabel = new TextBlock
        {
            Text = "100%",
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(8, 0, 4, 0),
            FontSize = 11
        };
        _zoomLabel.SetResourceReference(TextBlock.ForegroundProperty, "LabelText");

        var resetZoomBtn = new Button { Content = "1:1", Width = 36, Height = 28, Margin = new Thickness(0, 0, 4, 0), ToolTip = "Reset zoom to 100% (Ctrl+0)" };
        resetZoomBtn.SetResourceReference(Control.StyleProperty, "ThemedButtonStyle");
        resetZoomBtn.Click += (_, _) => { _zoom = 1.0; _scaleTransform.ScaleX = 1.0; _scaleTransform.ScaleY = 1.0; _zoomLabel.Text = "100%"; };

        // Update label whenever zoom changes
        PreviewMouseWheel += (_, e) =>
        {
            if ((Keyboard.Modifiers & ModifierKeys.Control) != 0)
                _zoomLabel.Text = $"{_zoom * 100:F0}%";
        };
        KeyDown += (_, e) =>
        {
            if (e.Key == Key.D0 && (Keyboard.Modifiers & ModifierKeys.Control) != 0)
            {
                _zoom = 1.0; _scaleTransform.ScaleX = 1.0; _scaleTransform.ScaleY = 1.0;
                _zoomLabel.Text = "100%";
                e.Handled = true;
            }
        };

        var row = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Margin = new Thickness(8, 6, 8, 6)
        };
        foreach (var btn in new[] { _addArrowBtn, cursorBtn, roundCornersBtn, insertBtn, cancelBtn })
            row.Children.Add(btn);
        row.Children.Add(_zoomLabel);
        row.Children.Add(resetZoomBtn);

        var border = new Border { BorderThickness = new Thickness(0, 1, 0, 0) };
        border.SetResourceReference(Border.BorderBrushProperty, "PopupBorder");
        border.Child = row;
        return border;
    }

    // ── Visual layout ─────────────────────────────────────────────────────────

    /// <summary>
    /// Repositions the selection border, resize handles, and mode-hint overlay to
    /// reflect the current <see cref="_sel"/> value.
    /// </summary>
    private void RefreshLayout()
    {
        // When no crop region exists hide all crop chrome so the full image is visible.
        if (_sel.IsEmpty)
        {
            foreach (var d in new[] { _dimTop, _dimBottom, _dimLeft, _dimRight })
                d.Width = d.Height = 0;
            _selBorderRect.Visibility = Visibility.Collapsed;
            foreach (var hdl in _handles) hdl.Visibility = Visibility.Collapsed;
            return;
        }

        // Restore chrome visibility in case it was previously collapsed.
        _selBorderRect.Visibility = Visibility.Visible;
        foreach (var hh2 in _handles) hh2.Visibility = Visibility.Visible;

        var s = _sel;
        var cx = s.Left + s.Width / 2;
        var cy = s.Top + s.Height / 2;
        var hh = HandleSize / 2;
        var w = _canvas.Width;
        var h = _canvas.Height;

        // ── Dim strips ───────────────────────────────────────────────────────
        // Top strip: full width, from y=0 to top of selection
        _dimTop.Width = w; _dimTop.Height = Math.Max(0, s.Top);
        Canvas.SetLeft(_dimTop, 0); Canvas.SetTop(_dimTop, 0);

        // Bottom strip: full width, from bottom of selection to canvas bottom
        _dimBottom.Width = w; _dimBottom.Height = Math.Max(0, h - s.Bottom);
        Canvas.SetLeft(_dimBottom, 0); Canvas.SetTop(_dimBottom, s.Bottom);

        // Left strip: between top and bottom strips, left of selection
        _dimLeft.Width = Math.Max(0, s.Left); _dimLeft.Height = s.Height;
        Canvas.SetLeft(_dimLeft, 0); Canvas.SetTop(_dimLeft, s.Top);

        // Right strip: between top and bottom strips, right of selection
        _dimRight.Width = Math.Max(0, w - s.Right); _dimRight.Height = s.Height;
        Canvas.SetLeft(_dimRight, s.Right); Canvas.SetTop(_dimRight, s.Top);

        // ── Selection border ─────────────────────────────────────────────────
        Canvas.SetLeft(_selBorderRect, s.Left);
        Canvas.SetTop(_selBorderRect, s.Top);
        _selBorderRect.Width = s.Width;
        _selBorderRect.Height = s.Height;

        // Handles: NW(0) N(1) NE(2) E(3) SE(4) S(5) SW(6) W(7)
        PlaceHandle(0, s.Left - hh, s.Top - hh);
        PlaceHandle(1, cx - hh, s.Top - hh);
        PlaceHandle(2, s.Right - hh, s.Top - hh);
        PlaceHandle(3, s.Right - hh, cy - hh);
        PlaceHandle(4, s.Right - hh, s.Bottom - hh);
        PlaceHandle(5, cx - hh, s.Bottom - hh);
        PlaceHandle(6, s.Left - hh, s.Bottom - hh);
        PlaceHandle(7, s.Left - hh, cy - hh);

        if (_modeHintBorder?.Visibility == Visibility.Visible)
            PositionModeHint();
    }

    private void PlaceHandle(int i, double left, double top)
    {
        Canvas.SetLeft(_handles[i], left);
        Canvas.SetTop(_handles[i], top);
    }

    // ── Hit testing ───────────────────────────────────────────────────────────

    private HitZone HitTest(Point pt)
    {
        var s = _sel;
        var cx = s.Left + s.Width / 2;
        var cy = s.Top + s.Height / 2;

        if (InHandleZone(pt, s.Left, s.Top)) return HitZone.NW;
        if (InHandleZone(pt, s.Right, s.Top)) return HitZone.NE;
        if (InHandleZone(pt, s.Right, s.Bottom)) return HitZone.SE;
        if (InHandleZone(pt, s.Left, s.Bottom)) return HitZone.SW;

        if (InHandleZone(pt, cx, s.Top)) return HitZone.N;
        if (InHandleZone(pt, s.Right, cy)) return HitZone.E;
        if (InHandleZone(pt, cx, s.Bottom)) return HitZone.S;
        if (InHandleZone(pt, s.Left, cy)) return HitZone.W;

        const double EdgeBand = 8.0;
        if (pt.Y >= s.Top - EdgeBand && pt.Y <= s.Top + EdgeBand && pt.X > s.Left && pt.X < s.Right) return HitZone.N;
        if (pt.Y >= s.Bottom - EdgeBand && pt.Y <= s.Bottom + EdgeBand && pt.X > s.Left && pt.X < s.Right) return HitZone.S;
        if (pt.X >= s.Left - EdgeBand && pt.X <= s.Left + EdgeBand && pt.Y > s.Top && pt.Y < s.Bottom) return HitZone.W;
        if (pt.X >= s.Right - EdgeBand && pt.X <= s.Right + EdgeBand && pt.Y > s.Top && pt.Y < s.Bottom) return HitZone.E;

        if (s.Contains(pt)) return HitZone.Move;
        return HitZone.None;
    }

    private static bool InHandleZone(Point pt, double cx, double cy)
    {
        var r = HandleSize / 2 + HitPad;
        return pt.X >= cx - r && pt.X <= cx + r &&
               pt.Y >= cy - r && pt.Y <= cy + r;
    }

    private static Cursor ZoneCursor(HitZone zone) => zone switch
    {
        HitZone.NW or HitZone.SE => Cursors.SizeNWSE,
        HitZone.NE or HitZone.SW => Cursors.SizeNESW,
        HitZone.N or HitZone.S => Cursors.SizeNS,
        HitZone.E or HitZone.W => Cursors.SizeWE,
        HitZone.Move => Cursors.SizeAll,
        _ => Cursors.Arrow
    };

    // ── Resize math ───────────────────────────────────────────────────────────

    [Flags]
    private enum Edge { Left = 1, Top = 2, Right = 4, Bottom = 8 }

    private static Rect ApplyEdges(Rect orig, double dx, double dy, Edge edges, double maxW, double maxH)
    {
        var l = orig.Left;
        var t = orig.Top;
        var r = orig.Right;
        var b = orig.Bottom;

        if ((edges & Edge.Left) != 0) l = Math.Min(l + dx, r - MinSize);
        if ((edges & Edge.Top) != 0) t = Math.Min(t + dy, b - MinSize);
        if ((edges & Edge.Right) != 0) r = Math.Max(r + dx, l + MinSize);
        if ((edges & Edge.Bottom) != 0) b = Math.Max(b + dy, t + MinSize);

        l = Math.Max(0, l);
        t = Math.Max(0, t);
        r = Math.Min(maxW, r);
        b = Math.Min(maxH, b);

        if (r - l < MinSize) r = l + MinSize;
        if (b - t < MinSize) b = t + MinSize;

        return new Rect(l, t, r - l, b - t);
    }

    private static Rect ClampRect(Rect rect, double maxW, double maxH)
    {
        var l = Math.Max(0, Math.Min(rect.Left, maxW - rect.Width));
        var t = Math.Max(0, Math.Min(rect.Top, maxH - rect.Height));
        return new Rect(l, t,
            Math.Min(rect.Width, maxW),
            Math.Min(rect.Height, maxH));
    }

    // ── Mouse ─────────────────────────────────────────────────────────────────

    private void Canvas_MouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.LeftButton != MouseButtonState.Pressed) return;
        SelectArrow(null);

        var pt = e.GetPosition(_canvas);
        var zone = HitTest(pt);

        // Resize handles take priority.
        if (zone is HitZone.NW or HitZone.N or HitZone.NE or
                    HitZone.E or HitZone.SE or HitZone.S or
                    HitZone.SW or HitZone.W)
        {
            _activeZone = zone;
            _dragStart = pt;
            _dragOriginal = _sel;
            _preDragSnapshot = CaptureSnapshot();
            _canvas.CaptureMouse();
            e.Handled = true;
            return;
        }

        // Cursor placement mode.
        if (_inCursorPlacementMode && _sel.Contains(pt))
        {
            PlaceCursorAtPoint(pt);
            e.Handled = true;
            return;
        }

        // Arrow placement mode: drop arrow at click point.
        if (_inArrowMode)
        {
            PlaceArrowAtPoint(pt);
            e.Handled = true;
            return;
        }

        // Move the selection.
        if (zone == HitZone.Move)
        {
            _activeZone = HitZone.Move;
            _dragStart = pt;
            _dragOriginal = _sel;
            _preDragSnapshot = CaptureSnapshot();
            _canvas.CaptureMouse();
            _canvas.Cursor = Cursors.SizeAll;
            e.Handled = true;
            return;
        }

        // Draw a new crop region from scratch when no selection exists.
        if (_sel.IsEmpty && !_inArrowMode && !_inCursorPlacementMode)
        {
            _creatingNewSel = true;
            _newSelAnchor = pt;
            _preDragSnapshot = CaptureSnapshot();
            _canvas.CaptureMouse();
            _canvas.Cursor = Cursors.Cross;
            e.Handled = true;
        }
    }

    private void Canvas_MouseMove(object sender, MouseEventArgs e)
    {
        // Rubber-band draw of a brand-new crop region.
        if (_creatingNewSel)
        {
            var pt = e.GetPosition(_canvas);
            var l = Math.Max(0, Math.Min(_newSelAnchor.X, pt.X));
            var t = Math.Max(0, Math.Min(_newSelAnchor.Y, pt.Y));
            var r = Math.Min(_canvas.Width, Math.Max(_newSelAnchor.X, pt.X));
            var b = Math.Min(_canvas.Height, Math.Max(_newSelAnchor.Y, pt.Y));
            if (r - l < MinSize) r = l + MinSize;
            if (b - t < MinSize) b = t + MinSize;
            _sel = new Rect(l, t, r - l, b - t);
            RefreshLayout();
            e.Handled = true;
            return;
        }

        if (_activeZone != HitZone.None)
        {
            var pt = e.GetPosition(_canvas);
            var dx = pt.X - _dragStart.X;
            var dy = pt.Y - _dragStart.Y;
            var w = _canvas.Width;
            var h = _canvas.Height;

            _sel = _activeZone switch
            {
                HitZone.Move => ClampRect(new Rect(_dragOriginal.X + dx, _dragOriginal.Y + dy, _dragOriginal.Width, _dragOriginal.Height), w, h),
                HitZone.NW => ApplyEdges(_dragOriginal, dx, dy, Edge.Left | Edge.Top, w, h),
                HitZone.N => ApplyEdges(_dragOriginal, dx, dy, Edge.Top, w, h),
                HitZone.NE => ApplyEdges(_dragOriginal, dx, dy, Edge.Right | Edge.Top, w, h),
                HitZone.E => ApplyEdges(_dragOriginal, dx, dy, Edge.Right, w, h),
                HitZone.SE => ApplyEdges(_dragOriginal, dx, dy, Edge.Right | Edge.Bottom, w, h),
                HitZone.S => ApplyEdges(_dragOriginal, dx, dy, Edge.Bottom, w, h),
                HitZone.SW => ApplyEdges(_dragOriginal, dx, dy, Edge.Left | Edge.Bottom, w, h),
                HitZone.W => ApplyEdges(_dragOriginal, dx, dy, Edge.Left, w, h),
                _ => _sel
            };
            RefreshLayout();
            e.Handled = true;
            return;
        }

        // Update cursor shape based on hover zone (not during arrow/cursor drag).
        if (!_draggingCursor && _draggingArrow == null && !_bodyDragging)
        {
            if (_sel.IsEmpty)
                _canvas.Cursor = Cursors.Cross;
            else
            {
                var hoverZone = HitTest(e.GetPosition(_canvas));
                _canvas.Cursor = ZoneCursor(hoverZone);
            }
        }
    }

    private void Canvas_MouseUp(object sender, MouseButtonEventArgs e)
    {
        if (_creatingNewSel)
        {
            _creatingNewSel = false;
            CommitDragUndo();
            _canvas.ReleaseMouseCapture();
            _canvas.Cursor = Cursors.Arrow;
            e.Handled = true;
            return;
        }

        if (_activeZone != HitZone.None)
        {
            CommitDragUndo();
            _activeZone = HitZone.None;
            _canvas.ReleaseMouseCapture();
            _canvas.Cursor = Cursors.Arrow;
            e.Handled = true;
        }
    }

    // ── Keyboard ──────────────────────────────────────────────────────────────

    private void Window_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            if (_inArrowMode) { ExitArrowMode(); e.Handled = true; return; }
            if (_inCursorPlacementMode)
            {
                _inCursorPlacementMode = false;
                _cursorEnabled = false;
                HideModeHint();
                e.Handled = true;
                return;
            }
            Close();
            e.Handled = true;
        }
        else if (e.Key == Key.Delete && _selectedArrow != null)
        {
            PushUndo();
            RemoveArrow(_selectedArrow);
            _selectedArrow = null;
            e.Handled = true;
        }
        else if (e.Key == Key.Delete && !_sel.IsEmpty)
        {
            PushUndo();
            _sel = Rect.Empty;
            RefreshLayout();
            e.Handled = true;
        }
        else if (e.Key == Key.Z && (Keyboard.Modifiers & ModifierKeys.Control) != 0)
        {
            PerformUndo();
            e.Handled = true;
        }
        else if (e.Key == Key.Y && (Keyboard.Modifiers & ModifierKeys.Control) != 0)
        {
            PerformRedo();
            e.Handled = true;
        }
    }

    // ── Arrow mode ────────────────────────────────────────────────────────────

    private void EnterArrowMode()
    {
        _inArrowMode = true;
        Cursor = Cursors.Cross;
        ShowModeHint("Click to place an arrow");
    }

    private void ExitArrowMode()
    {
        _inArrowMode = false;
        Cursor = Cursors.Arrow;
        HideModeHint();
        if (_addArrowBtn != null) _addArrowBtn.Content = "↗ Arrow";
    }

    /// <summary>
    /// Drops a new arrow with its pivot (target centre) at <paramref name="pt"/>.
    /// Unlike <see cref="ScreenshotOverlayWindow"/>, there is no element-highlight
    /// step — the click position is used directly.
    /// </summary>
    private void PlaceArrowAtPoint(Point pt)
    {
        var clamped = new Point(
            Math.Max(0, Math.Min(pt.X, _canvas.Width)),
            Math.Max(0, Math.Min(pt.Y, _canvas.Height)));

        // Use a 2×2 rect centred on the click as the "target bounds".
        var targetBounds = new Rect(clamped.X - 1, clamped.Y - 1, 2, 2);
        CreateArrow(targetBounds);
        ExitArrowMode();
    }

    private AnnotationArrow CreateArrow(Rect targetBounds)
    {
        if (!_suppressUndo) PushUndo();

        var center = new Point(
            targetBounds.Left + targetBounds.Width / 2,
            targetBounds.Top + targetBounds.Height / 2);

        double initialTailLength = _defaultTailLength > 0
            ? _defaultTailLength
            : ComputeInitialTailLength(center, _defaultArrowAngleDeg, _defaultArrowLength);

        // Shadow shapes (drawn first so they are below main arrow visuals).
        var shadowLine = new Polyline
        {
            Stroke = new SolidColorBrush(Color.FromArgb(102, 0, 0, 0)),
            StrokeThickness = 2.5,
            IsHitTestVisible = false
        };
        var shadowHead = new Polygon
        {
            Fill = new SolidColorBrush(Color.FromArgb(102, 0, 0, 0)),
            IsHitTestVisible = false
        };

        var colorBrush = new SolidColorBrush(_defaultArrowColor);

        var line = new Line
        {
            StrokeThickness = 2.5,
            Stroke = colorBrush,
            IsHitTestVisible = true,
            Cursor = Cursors.Arrow
        };
        var head = new Polygon
        {
            Fill = colorBrush,
            IsHitTestVisible = true,
            Cursor = Cursors.Arrow
        };
        var tipHandle = new Ellipse
        {
            Width = 8,
            Height = 8,
            Fill = colorBrush,
            Stroke = Brushes.White,
            StrokeThickness = 1.5,
            Cursor = Cursors.SizeAll,
            Visibility = Visibility.Hidden
        };
        var tailHandle = new Ellipse
        {
            Width = 8,
            Height = 8,
            Fill = colorBrush,
            Stroke = Brushes.White,
            StrokeThickness = 1.5,
            Cursor = Cursors.SizeAll,
            Visibility = Visibility.Hidden
        };

        _canvas.Children.Add(shadowLine);
        _canvas.Children.Add(shadowHead);
        _canvas.Children.Add(line);
        _canvas.Children.Add(head);
        _canvas.Children.Add(tailHandle);
        _canvas.Children.Add(tipHandle);
        Panel.SetZIndex(tipHandle, 10);
        Panel.SetZIndex(tailHandle, 10);
        Panel.SetZIndex(line, 5);
        Panel.SetZIndex(head, 5);
        Panel.SetZIndex(shadowLine, 2);
        Panel.SetZIndex(shadowHead, 2);

        var arrow = new AnnotationArrow
        {
            TargetElementName = string.Empty,
            TargetElementBounds = targetBounds,
            ArrowheadAngleDeg = _defaultArrowAngleDeg,
            ArrowLength = _defaultArrowLength,
            TailLength = initialTailLength,
            UserTailLength = _defaultTailLength,
            ArrowColor = _defaultArrowColor,
            Line = line,
            Head = head,
            TipHandle = tipHandle,
            TailHandle = tailHandle,
            TargetCenterOnCanvas = center,
            ShadowLine = shadowLine,
            ShadowHead = shadowHead
        };

        // ── Tip-handle drag: changes angle (pivot stays at target centre) ─────
        tipHandle.MouseLeftButtonDown += (_, e) =>
        {
            _preDragSnapshot = CaptureSnapshot();
            _draggingArrow = arrow;
            _tailDragging = false;
            tipHandle.CaptureMouse();
            e.Handled = true;
        };
        tipHandle.MouseMove += (_, e) =>
        {
            if (_draggingArrow != arrow || _tailDragging || _bodyDragging) return;
            var pivot = new Point(arrow.TargetCenterOnCanvas.X + arrow.OffsetX,
                                     arrow.TargetCenterOnCanvas.Y + arrow.OffsetY);
            var pt = e.GetPosition(_canvas);
            var dx = pt.X - pivot.X;
            var dy = pt.Y - pivot.Y;
            var dist = Math.Sqrt(dx * dx + dy * dy);
            var newAngle = Math.Atan2(dx, -dy) * 180.0 / Math.PI;
            if (newAngle < 0) newAngle += 360;
            arrow.ArrowheadAngleDeg = newAngle;
            arrow.ArrowLength = Math.Max(5.0, Math.Min(dist, ComputeMaxArrowheadOffset(pivot, newAngle)));
            double maxFromTip = ComputeMaxArrowheadOffset(pivot, newAngle) - arrow.ArrowLength;
            arrow.TailLength = arrow.UserTailLength > 0
                ? Math.Max(64, Math.Min(arrow.UserTailLength, maxFromTip))
                : ComputeInitialTailLength(pivot, newAngle, arrow.ArrowLength);
            UpdateArrowGeometry(arrow);
            e.Handled = true;
        };
        tipHandle.MouseLeftButtonUp += (_, e) =>
        {
            if (_draggingArrow != arrow || _tailDragging) return;
            _defaultArrowAngleDeg = arrow.ArrowheadAngleDeg;
            _defaultArrowLength = arrow.ArrowLength;
            SaveArrowDefaults();
            CommitDragUndo();
            _draggingArrow = null;
            tipHandle.ReleaseMouseCapture();
            e.Handled = true;
        };

        // ── Tail-handle drag: full rotation + length (pivot stays fixed) ──────
        tailHandle.MouseLeftButtonDown += (_, e) =>
        {
            _preDragSnapshot = CaptureSnapshot();
            _draggingArrow = arrow;
            _tailDragging = true;
            _tailDragStartMouse = e.GetPosition(_canvas);
            tailHandle.CaptureMouse();
            e.Handled = true;
        };
        tailHandle.MouseMove += (_, e) =>
        {
            if (_draggingArrow != arrow || !_tailDragging) return;
            var pivot = new Point(arrow.TargetCenterOnCanvas.X + arrow.OffsetX,
                                     arrow.TargetCenterOnCanvas.Y + arrow.OffsetY);
            var pt = e.GetPosition(_canvas);
            var dx = pt.X - pivot.X;
            var dy = pt.Y - pivot.Y;
            var dist = Math.Sqrt(dx * dx + dy * dy);
            if (dist < 1) { e.Handled = true; return; }
            var newAngle = Math.Atan2(dx, -dy) * 180.0 / Math.PI;
            if (newAngle < 0) newAngle += 360;
            arrow.ArrowheadAngleDeg = newAngle;
            const double MinTail = 64.0;
            var total = Math.Max(arrow.ArrowLength + MinTail, dist);
            arrow.TailLength = Math.Max(MinTail, total - arrow.ArrowLength);
            UpdateArrowGeometry(arrow);
            e.Handled = true;
        };
        tailHandle.MouseLeftButtonUp += (_, e) =>
        {
            if (_draggingArrow != arrow || !_tailDragging) return;
            arrow.UserTailLength = arrow.TailLength;
            _defaultTailLength = arrow.TailLength;
            _defaultArrowAngleDeg = arrow.ArrowheadAngleDeg;
            SaveArrowDefaults();
            CommitDragUndo();
            _draggingArrow = null;
            _tailDragging = false;
            tailHandle.ReleaseMouseCapture();
            e.Handled = true;
        };

        // ── Body drag (click line or head to select + move) ───────────────────
        AttachBodyDrag(line, arrow);
        AttachBodyDrag(head, arrow);

        tipHandle.MouseRightButtonDown += (_, e) =>
        {
            RemoveArrow(arrow);
            if (_selectedArrow == arrow) { _selectedArrow = null; HideColorPicker(); }
            e.Handled = true;
        };
        tailHandle.MouseRightButtonDown += (_, e) =>
        {
            RemoveArrow(arrow);
            if (_selectedArrow == arrow) { _selectedArrow = null; HideColorPicker(); }
            e.Handled = true;
        };

        _arrows.Add(arrow);
        UpdateArrowGeometry(arrow);
        SelectArrow(arrow);
        return arrow;
    }

    private void AttachBodyDrag(Shape shape, AnnotationArrow arrow)
    {
        shape.MouseLeftButtonDown += (_, e) =>
        {
            if (_draggingArrow != null) return;
            SelectArrow(arrow);
            _preDragSnapshot = CaptureSnapshot();
            _draggingArrow = arrow;
            _bodyDragging = true;
            _bodyDragStartMouse = e.GetPosition(_canvas);
            _bodyDragStartOffsetX = arrow.OffsetX;
            _bodyDragStartOffsetY = arrow.OffsetY;
            shape.CaptureMouse();
            e.Handled = true;
        };
        shape.MouseMove += (_, e) =>
        {
            if (_draggingArrow != arrow || !_bodyDragging) return;
            var pt = e.GetPosition(_canvas);
            arrow.OffsetX = _bodyDragStartOffsetX + (pt.X - _bodyDragStartMouse.X);
            arrow.OffsetY = _bodyDragStartOffsetY + (pt.Y - _bodyDragStartMouse.Y);
            UpdateArrowGeometry(arrow);
            if (_colorPickerArrow == arrow) ShowColorPicker(arrow);
            e.Handled = true;
        };
        shape.MouseLeftButtonUp += (_, e) =>
        {
            if (_draggingArrow != arrow || !_bodyDragging) return;
            CommitDragUndo();
            _draggingArrow = null;
            _bodyDragging = false;
            shape.ReleaseMouseCapture();
            e.Handled = true;
        };
        shape.MouseRightButtonDown += (_, e) =>
        {
            RemoveArrow(arrow);
            if (_selectedArrow == arrow) { _selectedArrow = null; HideColorPicker(); }
            e.Handled = true;
        };
    }

    // ── Arrow geometry ────────────────────────────────────────────────────────

    private static void UpdateArrowGeometry(AnnotationArrow arrow)
    {
        var brush = new SolidColorBrush(arrow.ArrowColor);
        arrow.Line.Stroke = brush;
        arrow.Head.Fill = brush;
        arrow.Head.Stroke = brush;

        var center = new Point(arrow.TargetCenterOnCanvas.X + arrow.OffsetX,
                               arrow.TargetCenterOnCanvas.Y + arrow.OffsetY);
        var rad = arrow.ArrowheadAngleDeg * Math.PI / 180.0;
        var ux = Math.Sin(rad);
        var uy = -Math.Cos(rad);

        var ahX = center.X + ux * arrow.ArrowLength;
        var ahY = center.Y + uy * arrow.ArrowLength;
        var tailX = center.X + ux * (arrow.ArrowLength + arrow.TailLength);
        var tailY = center.Y + uy * (arrow.ArrowLength + arrow.TailLength);

        arrow.Line.X1 = tailX; arrow.Line.Y1 = tailY;
        arrow.Line.X2 = ahX; arrow.Line.Y2 = ahY;

        const double HeadLen = 16.0;
        const double HeadHalf = 6.0;
        var baseX = ahX + ux * HeadLen;
        var baseY = ahY + uy * HeadLen;
        var px = -uy;
        var py = ux;

        arrow.Head.Points = new PointCollection
        {
            new Point(ahX,  ahY),
            new Point(baseX + px * HeadHalf, baseY + py * HeadHalf),
            new Point(baseX - px * HeadHalf, baseY - py * HeadHalf)
        };

        Canvas.SetLeft(arrow.TipHandle, ahX - 4);
        Canvas.SetTop(arrow.TipHandle, ahY - 4);
        Canvas.SetLeft(arrow.TailHandle, tailX - 4);
        Canvas.SetTop(arrow.TailHandle, tailY - 4);

        arrow.ShadowLine.Points = new PointCollection(new[]
        {
            new Point(tailX + 2, tailY + 2),
            new Point(ahX   + 2, ahY   + 2)
        });
        arrow.ShadowHead.Points = new PointCollection(
            arrow.Head.Points.Select(p => p + new Vector(2, 2)));
    }

    private double ComputeInitialTailLength(Point targetCenter, double angleDeg, double arrowheadOffset)
    {
        var rad = angleDeg * Math.PI / 180.0;
        var dx = Math.Sin(rad);
        var dy = -Math.Cos(rad);
        var ahX = targetCenter.X + dx * arrowheadOffset;
        var ahY = targetCenter.Y + dy * arrowheadOffset;
        var s = _sel;

        double tMin = double.MaxValue;
        if (Math.Abs(dx) > 1e-9)
        {
            var t = dx > 0 ? (s.Right - ahX) / dx : (s.Left - ahX) / dx;
            if (t > 0) tMin = Math.Min(tMin, t);
        }
        if (Math.Abs(dy) > 1e-9)
        {
            var t = dy > 0 ? (s.Bottom - ahY) / dy : (s.Top - ahY) / dy;
            if (t > 0) tMin = Math.Min(tMin, t);
        }
        return tMin < double.MaxValue ? Math.Max(64.0, Math.Min(128.0, tMin * 0.85)) : 80.0;
    }

    private double ComputeMaxArrowheadOffset(Point targetCenter, double angleDeg)
    {
        var rad = angleDeg * Math.PI / 180.0;
        var dx = Math.Sin(rad);
        var dy = -Math.Cos(rad);
        var s = _sel;

        double tMin = double.MaxValue;
        if (Math.Abs(dx) > 1e-9)
        {
            var t = dx > 0 ? (s.Right - targetCenter.X) / dx : (s.Left - targetCenter.X) / dx;
            if (t > 0) tMin = Math.Min(tMin, t);
        }
        if (Math.Abs(dy) > 1e-9)
        {
            var t = dy > 0 ? (s.Bottom - targetCenter.Y) / dy : (s.Top - targetCenter.Y) / dy;
            if (t > 0) tMin = Math.Min(tMin, t);
        }
        return tMin < double.MaxValue ? tMin : 80.0;
    }

    // ── Arrow selection ───────────────────────────────────────────────────────

    private void SelectArrow(AnnotationArrow? arrow)
    {
        if (_selectedArrow != null && _selectedArrow != arrow)
        {
            _selectedArrow.TipHandle.Visibility = Visibility.Hidden;
            _selectedArrow.TailHandle.Visibility = Visibility.Hidden;
        }
        _selectedArrow = arrow;
        if (arrow != null)
        {
            arrow.TipHandle.Visibility = Visibility.Visible;
            arrow.TailHandle.Visibility = Visibility.Visible;
            ShowColorPicker(arrow);
        }
        else
        {
            HideColorPicker();
        }
    }

    // ── Color picker ──────────────────────────────────────────────────────────

    private static Color[] GetArrowPalette(bool isDark) => isDark
        ? new[]
          {
              Color.FromRgb(255,  80,  80),
              Color.FromRgb(255, 160,  40),
              Color.FromRgb(255, 230,  60),
              Color.FromRgb( 80, 220,  80),
              Color.FromRgb( 80, 160, 255),
              Color.FromRgb(255, 255, 255)
          }
        : new[]
          {
              Color.FromRgb(180,  30,  30),
              Color.FromRgb(180,  80,   0),
              Color.FromRgb(140, 120,   0),
              Color.FromRgb( 20, 130,  20),
              Color.FromRgb( 20,  80, 200),
              Color.FromRgb(  0,   0,   0)
          };

    private void ShowColorPicker(AnnotationArrow arrow)
    {
        HideColorPicker();
        _colorPickerArrow = arrow;
        bool isDark = _themeName.IndexOf("dark", StringComparison.OrdinalIgnoreCase) >= 0;
        var palette = GetArrowPalette(isDark);

        _colorPickerPanel = new StackPanel { Orientation = Orientation.Horizontal };
        Panel.SetZIndex(_colorPickerPanel, 300);

        foreach (var color in palette)
        {
            var c = color;
            var dot = new Ellipse
            {
                Width = 16,
                Height = 16,
                Fill = new SolidColorBrush(c),
                Stroke = c == arrow.ArrowColor
                                      ? (isDark ? Brushes.White : Brushes.Black)
                                      : Brushes.Transparent,
                StrokeThickness = 2,
                Margin = new Thickness(3, 0, 3, 0),
                Cursor = Cursors.Hand
            };
            dot.MouseLeftButtonDown += (_, e) =>
            {
                arrow.ArrowColor = c;
                _defaultArrowColor = c;
                SaveArrowDefaults();
                UpdateArrowGeometry(arrow);
                ShowColorPicker(arrow);
                e.Handled = true;
            };
            _colorPickerPanel.Children.Add(dot);
        }

        _canvas.Children.Add(_colorPickerPanel);

        double cx = Canvas.GetLeft(arrow.TipHandle) + 4;
        double cy = Canvas.GetTop(arrow.TipHandle) + 4;
        _colorPickerPanel.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        double pw = _colorPickerPanel.DesiredSize.Width;
        Canvas.SetLeft(_colorPickerPanel, Math.Max(0, cx - pw / 2));
        Canvas.SetTop(_colorPickerPanel, Math.Max(0, cy - 30));
    }

    private void HideColorPicker()
    {
        if (_colorPickerPanel != null)
        {
            _canvas.Children.Remove(_colorPickerPanel);
            _colorPickerPanel = null;
        }
        _colorPickerArrow = null;
    }

    private void RemoveArrow(AnnotationArrow arrow)
    {
        if (!_suppressUndo) PushUndo();
        if (arrow == _colorPickerArrow) HideColorPicker();
        _canvas.Children.Remove(arrow.ShadowLine);
        _canvas.Children.Remove(arrow.ShadowHead);
        _canvas.Children.Remove(arrow.Line);
        _canvas.Children.Remove(arrow.Head);
        _canvas.Children.Remove(arrow.TipHandle);
        _canvas.Children.Remove(arrow.TailHandle);
        _arrows.Remove(arrow);
    }

    // ── Cursor overlay ────────────────────────────────────────────────────────

    private void ToggleCursorOverlay(bool enabled)
    {
        _cursorEnabled = enabled;
        if (!enabled && _cursorImage != null)
        {
            _cursorImage.Visibility = Visibility.Collapsed;
            _draggingCursor = false;
        }
    }

    private void EnsureCursorImageCreated()
    {
        if (_cursorImage != null) return;

        _cursorImage = new Image
        {
            Width = 22,
            Height = 26,
            Source = BuildCursorDrawing(),
            Cursor = Cursors.SizeAll,
            IsHitTestVisible = true
        };
        Panel.SetZIndex(_cursorImage, 100);

        _cursorImage.MouseLeftButtonDown += (_, e) =>
        {
            _preDragSnapshot = CaptureSnapshot();
            _draggingCursor = true;
            _cursorImage!.CaptureMouse();
            e.Handled = true;
        };
        _cursorImage.MouseMove += (_, e) =>
        {
            if (!_draggingCursor) return;
            var pt = e.GetPosition(_canvas);
            var x = Math.Max(_sel.Left, Math.Min(pt.X, _sel.Right - 20));
            var y = Math.Max(_sel.Top, Math.Min(pt.Y, _sel.Bottom - 24));
            Canvas.SetLeft(_cursorImage!, x);
            Canvas.SetTop(_cursorImage!, y);
            e.Handled = true;
        };
        _cursorImage.MouseLeftButtonUp += (_, e) =>
        {
            if (!_draggingCursor) return;
            CommitDragUndo();
            _draggingCursor = false;
            _cursorImage!.ReleaseMouseCapture();
            e.Handled = true;
        };

        _canvas.Children.Add(_cursorImage);
    }

    private void PlaceCursorAtPoint(Point pt)
    {
        PushUndo();
        var x = Math.Max(_sel.Left, Math.Min(pt.X, _sel.Right - 20));
        var y = Math.Max(_sel.Top, Math.Min(pt.Y, _sel.Bottom - 24));
        EnsureCursorImageCreated();
        Canvas.SetLeft(_cursorImage!, x);
        Canvas.SetTop(_cursorImage!, y);
        _cursorImage!.Visibility = Visibility.Visible;
        _inCursorPlacementMode = false;
        HideModeHint();
    }

    /// <summary>
    /// Builds a <see cref="DrawingImage"/> from the XAML cursor path data (black outline,
    /// white fill). Coordinates are in the original 212×295 canvas space; a transform
    /// normalises the clip region (59,22,121,259) to a display-friendly size.
    /// Intentional literal-color usage — cursor rendering.
    /// </summary>
    private static DrawingImage BuildCursorDrawing()
    {
        // Black outline — clipped to Rect(59,22,121,259) matching the XAML inner-Canvas clip
        const string blackData =
            "M59.3125,21.5625 L177.875,157.5625 177.875,157.5625 178.4375,158.5625" +
            " C179.375,161,178.6875,163.625,176.9375,165.25" +
            " L174.9375,166.5 174.6875,166.625 174.125,166.6875 174.1875,166.8125 140.375,170.3125" +
            " C151.5625,199.9375,162.125,228.1875,173.3125,257.8125" +
            " C173.4375,258.1875,173.5,258.5,173.625,258.875" +
            " C174.5625,261.3125,173.875,263.9375,172.125,265.625" +
            " L170.1875,266.8125 169.8125,266.9375 169.3125,267 169.3125,267.125 137.5,279" +
            " 137.5,278.9375 137.0625,279.1875 136.6875,279.3125 134.4375,279.6875" +
            " C132.8125,279.625,131.3125,278.9375,130.1875,277.75" +
            " L129,275.8125 128.5,274.5625 95.8125,187.0625 68,206.5625" +
            " 67.9375,206.4375 67.5,206.75 67.25,206.8125 64.9375,207.1875" +
            " C62.5,207.125,60.25,205.5625,59.375,203.125" +
            " L59.25,202.375 59.3125,21.5625 z";

        // White fill — no clip (matches the outer Canvas path in the XAML)
        const string whiteData =
            "M66.3125,40.0625" +
            " L121.8125,104.9375 169.875,160.625 131.0625,165.0625" +
            " C143.0625,196.75,154.8125,229.125,166.8125,260.875" +
            " L134.9375,272.875" +
            " C123.0625,241.1875,110.8125,208.9375,98.9375,177.25" +
            " L66.625,199.25 66,40.375 66.25,40.6875 66.3125,40.0625 z";

        // Clip matches the XAML RectangleGeometry Rect="59,22,121,259" (canvas coordinates)
        var clippedBlack = new DrawingGroup();
        clippedBlack.ClipGeometry = new RectangleGeometry(new Rect(59, 22, 121, 259));
        clippedBlack.Children.Add(new GeometryDrawing(Brushes.Black, null, Geometry.Parse(blackData)));

        var group = new DrawingGroup();
        group.Children.Add(clippedBlack);
        group.Children.Add(new GeometryDrawing(Brushes.White, null, Geometry.Parse(whiteData)));

        // Translate so the clip origin (59,22) becomes (0,0), then scale the 259-unit
        // clip height to 22 display pixels — matches the XAML Viewbox Height="42" proportions.
        const double displayHeight = 26.0;
        const double clipHeight = 259.0;
        const double s = displayHeight / clipHeight;
        var xf = new TransformGroup();
        xf.Children.Add(new TranslateTransform(-59, -22));
        xf.Children.Add(new ScaleTransform(s, s));
        group.Transform = xf;

        return new DrawingImage(group);
    }

    // ── Mode hint ─────────────────────────────────────────────────────────────

    private void ShowModeHint(string text)
    {
        if (_modeHintBorder == null)
        {
            _modeHintText = new TextBlock
            {
                FontSize = 11,
                FontFamily = new FontFamily("Segoe UI"),
                Foreground = new SolidColorBrush(Color.FromArgb(0xFF, 0xFF, 0xFF, 0xFF)),
                TextAlignment = TextAlignment.Center,
                IsHitTestVisible = false
            };
            _modeHintBorder = new Border
            {
                Background = new SolidColorBrush(Color.FromArgb(0xAA, 0x00, 0x00, 0x00)),
                CornerRadius = new CornerRadius(4),
                Padding = new Thickness(6, 4, 6, 4),
                Child = _modeHintText,
                IsHitTestVisible = false,
                Visibility = Visibility.Collapsed,
                MinWidth = 180
            };
            Panel.SetZIndex(_modeHintBorder, 150);
            _canvas.Children.Add(_modeHintBorder);
            _modeHintBorder.SizeChanged += (_, _) => PositionModeHint();
        }
        _modeHintText!.Text = text;
        _modeHintBorder.Visibility = Visibility.Visible;
        PositionModeHint();
        Dispatcher.InvokeAsync(PositionModeHint, DispatcherPriority.Loaded);
    }

    private void HideModeHint()
    {
        if (_modeHintBorder != null)
            _modeHintBorder.Visibility = Visibility.Collapsed;
    }

    private void PositionModeHint()
    {
        if (_modeHintBorder == null) return;
        _modeHintBorder.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        var w = _modeHintBorder.DesiredSize.Width;
        if (_sel.IsEmpty)
        {
            Canvas.SetLeft(_modeHintBorder, Math.Max(0, (_canvas.Width - w) / 2));
            Canvas.SetTop(_modeHintBorder, 8);
            return;
        }
        var cx = _sel.Left + _sel.Width / 2;
        Canvas.SetLeft(_modeHintBorder, Math.Max(_sel.Left, cx - w / 2));
        Canvas.SetTop(_modeHintBorder, _sel.Top + 8);
    }

    // ── Insert Image ──────────────────────────────────────────────────────────

    private void DoInsertImage()
    {
        // Hide chrome before rendering so handles/color-picker don't appear in the output.
        foreach (var h in _handles) h.Visibility = Visibility.Collapsed;
        _selBorderRect.Visibility = Visibility.Collapsed;
        HideColorPicker();
        HideModeHint();

        if (_selectedArrow != null)
        {
            _selectedArrow.TipHandle.Visibility = Visibility.Collapsed;
            _selectedArrow.TailHandle.Visibility = Visibility.Collapsed;
        }

        try
        {
            var dpi = VisualTreeHelper.GetDpi(this);
            var pxW = (int)Math.Round(_canvas.ActualWidth * dpi.DpiScaleX);
            var pxH = (int)Math.Round(_canvas.ActualHeight * dpi.DpiScaleY);
            if (pxW < 1 || pxH < 1) return;

            var rtb = new RenderTargetBitmap(
                pxW, pxH,
                dpi.PixelsPerInchX, dpi.PixelsPerInchY,
                PixelFormats.Pbgra32);
            rtb.Render(_canvas);
            rtb.Freeze();

            var cropSel = _sel.IsEmpty ? new Rect(0, 0, _canvas.ActualWidth, _canvas.ActualHeight) : _sel;
            var cropX = (int)Math.Round(cropSel.Left * dpi.DpiScaleX);
            var cropY = (int)Math.Round(cropSel.Top * dpi.DpiScaleY);
            var cropW = (int)Math.Round(cropSel.Width * dpi.DpiScaleX);
            var cropH = (int)Math.Round(cropSel.Height * dpi.DpiScaleY);

            cropX = Math.Max(0, cropX);
            cropY = Math.Max(0, cropY);
            cropW = Math.Max(1, Math.Min(cropW, pxW - cropX));
            cropH = Math.Max(1, Math.Min(cropH, pxH - cropY));

            BitmapSource bmp;
            if (cropX == 0 && cropY == 0 && cropW == pxW && cropH == pxH)
            {
                bmp = rtb;
            }
            else
            {
                var cropped = new CroppedBitmap(rtb, new Int32Rect(cropX, cropY, cropW, cropH));
                cropped.Freeze();
                bmp = cropped;
            }

            if (_roundCorners)
                bmp = ApplyRoundedCorners(bmp, CornerRadiusPx);

            Result = bmp;
        }
        finally
        {
            Close();
        }
    }

    /// <summary>
    /// Returns a copy of <paramref name="src"/> with the four corners made transparent.
    /// Pixels are zeroed (BGRA all 0) if they fall outside the arc of a circle with
    /// radius <paramref name="radiusPx"/> centred on each corner.
    /// </summary>
    private static BitmapSource ApplyRoundedCorners(BitmapSource src, int radiusPx)
    {
        var conv = new FormatConvertedBitmap(src, PixelFormats.Pbgra32, null, 0);
        int w = conv.PixelWidth;
        int h = conv.PixelHeight;
        int stride = w * 4;
        var pixels = new byte[stride * h];
        conv.CopyPixels(pixels, stride, 0);

        double r = radiusPx;
        for (int y = 0; y < radiusPx && y < h; y++)
        {
            for (int x = 0; x < radiusPx && x < w; x++)
            {
                double dx = x + 0.5 - r;
                double dy = y + 0.5 - r;
                if (dx * dx + dy * dy > r * r)
                {
                    int xr = w - 1 - x;
                    int yb = h - 1 - y;

                    int i = y * stride + x * 4;
                    pixels[i] = pixels[i + 1] = pixels[i + 2] = pixels[i + 3] = 0;

                    i = y * stride + xr * 4;
                    pixels[i] = pixels[i + 1] = pixels[i + 2] = pixels[i + 3] = 0;

                    i = yb * stride + x * 4;
                    pixels[i] = pixels[i + 1] = pixels[i + 2] = pixels[i + 3] = 0;

                    i = yb * stride + xr * 4;
                    pixels[i] = pixels[i + 1] = pixels[i + 2] = pixels[i + 3] = 0;
                }
            }
        }

        var wb = new WriteableBitmap(w, h, src.DpiX, src.DpiY, PixelFormats.Pbgra32, null);
        wb.WritePixels(new Int32Rect(0, 0, w, h), pixels, stride, 0);
        wb.Freeze();
        return wb;
    }

    // ── Undo / redo ───────────────────────────────────────────────────────────

    private EditorSnapshot CaptureSnapshot() => new(
        Sel: _sel,
        Arrows: _arrows.Select(a => new ArrowSnap(
                           a.TargetElementName, a.TargetElementBounds,
                           a.ArrowheadAngleDeg, a.ArrowLength, a.TailLength,
                           a.UserTailLength, a.ArrowColor, a.TargetCenterOnCanvas,
                           a.OffsetX, a.OffsetY)).ToList(),
        CursorEnabled: _cursorEnabled,
        CursorPos: _cursorImage != null
                           ? new Point(Canvas.GetLeft(_cursorImage), Canvas.GetTop(_cursorImage))
                           : default);

    private void PushUndo()
    {
        if (_suppressUndo) return;
        _undoStack.Push(CaptureSnapshot());
        _redoStack.Clear();
        TrimUndoStack();
    }

    private void CommitDragUndo()
    {
        if (_suppressUndo || _preDragSnapshot == null) return;
        _undoStack.Push(_preDragSnapshot);
        _preDragSnapshot = null;
        _redoStack.Clear();
        TrimUndoStack();
    }

    private void TrimUndoStack()
    {
        if (_undoStack.Count <= 50) return;
        var items = _undoStack.ToArray();
        _undoStack.Clear();
        foreach (var item in items.Take(50).Reverse())
            _undoStack.Push(item);
    }

    private void PerformUndo()
    {
        if (_undoStack.Count == 0) return;
        _redoStack.Push(CaptureSnapshot());
        RestoreSnapshot(_undoStack.Pop());
    }

    private void PerformRedo()
    {
        if (_redoStack.Count == 0) return;
        _undoStack.Push(CaptureSnapshot());
        RestoreSnapshot(_redoStack.Pop());
    }

    private void RestoreSnapshot(EditorSnapshot snap)
    {
        _suppressUndo = true;
        try
        {
            SelectArrow(null);
            foreach (var a in _arrows.ToList()) RemoveArrow(a);

            foreach (var s in snap.Arrows)
            {
                var a = CreateArrow(s.TargetElementBounds);
                a.ArrowheadAngleDeg = s.ArrowheadAngleDeg;
                a.ArrowLength = s.ArrowLength;
                a.TailLength = s.TailLength;
                a.UserTailLength = s.UserTailLength;
                a.ArrowColor = s.ArrowColor;
                a.TargetCenterOnCanvas = s.TargetCenterOnCanvas;
                a.OffsetX = s.OffsetX;
                a.OffsetY = s.OffsetY;
                UpdateArrowGeometry(a);
            }

            SelectArrow(null);
            _sel = snap.Sel;
            _cursorEnabled = snap.CursorEnabled;

            if (snap.CursorEnabled)
            {
                EnsureCursorImageCreated();
                Canvas.SetLeft(_cursorImage!, snap.CursorPos.X);
                Canvas.SetTop(_cursorImage!, snap.CursorPos.Y);
                _cursorImage!.Visibility = Visibility.Visible;
            }
            else if (_cursorImage != null)
            {
                _cursorImage.Visibility = Visibility.Collapsed;
                _draggingCursor = false;
            }

            RefreshLayout();
        }
        finally
        {
            _suppressUndo = false;
        }
    }

    // ── Arrow defaults — persist / load ───────────────────────────────────────

    private static string ArrowDefaultsPath =>
        System.IO.Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "SquadDash", "annotation-arrow-defaults.json");

    private void SaveArrowDefaults()
    {
        try
        {
            var dir = System.IO.Path.GetDirectoryName(ArrowDefaultsPath)!;
            Directory.CreateDirectory(dir);
            File.WriteAllText(ArrowDefaultsPath, JsonSerializer.Serialize(new
            {
                color = $"#{_defaultArrowColor.R:X2}{_defaultArrowColor.G:X2}{_defaultArrowColor.B:X2}",
                angleDeg = _defaultArrowAngleDeg,
                length = _defaultArrowLength,
                tailLen = _defaultTailLength
            }));
        }
        catch { /* non-critical */ }
    }

    private void LoadArrowDefaults()
    {
        try
        {
            if (!File.Exists(ArrowDefaultsPath)) return;
            using var doc = JsonDocument.Parse(File.ReadAllText(ArrowDefaultsPath));
            var root = doc.RootElement;
            if (root.TryGetProperty("color", out var col) &&
                col.GetString() is { Length: 7 } hex && hex[0] == '#')
            {
                _defaultArrowColor = Color.FromRgb(
                    Convert.ToByte(hex[1..3], 16),
                    Convert.ToByte(hex[3..5], 16),
                    Convert.ToByte(hex[5..7], 16));
            }
            if (root.TryGetProperty("angleDeg", out var ang)) _defaultArrowAngleDeg = ang.GetDouble();
            if (root.TryGetProperty("length", out var len)) _defaultArrowLength = len.GetDouble();
            if (root.TryGetProperty("tailLen", out var tl)) _defaultTailLength = tl.GetDouble();
        }
        catch { /* non-critical */ }
    }

    // ── Snapshot records ──────────────────────────────────────────────────────

    private sealed record EditorSnapshot(
        Rect Sel,
        IReadOnlyList<ArrowSnap> Arrows,
        bool CursorEnabled,
        Point CursorPos);

    private sealed record ArrowSnap(
        string TargetElementName,
        Rect TargetElementBounds,
        double ArrowheadAngleDeg,
        double ArrowLength,
        double TailLength,
        double UserTailLength,
        Color ArrowColor,
        Point TargetCenterOnCanvas,
        double OffsetX,
        double OffsetY);
}
