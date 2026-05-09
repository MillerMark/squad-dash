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

    // Dimension readout labels shown while a crop selection exists
    private readonly Border _dimWidthBadge;
    private readonly TextBlock _dimWidthLabel;
    private readonly Border _dimHeightBadge;
    private readonly TextBlock _dimHeightLabel;

    // Zoom percentage label (declared as field so the wheel handler can update it)
    private TextBlock? _zoomLabel;

    // ScrollViewer wrapping the canvas (field so the wheel handler can adjust offsets)
    private ScrollViewer _scrollViewer = null!;

    // ── Round corners ─────────────────────────────────────────────────────────

    private bool _roundCorners;

    // Physical-pixel corner radius applied to the output PNG when _roundCorners is true.
    // Windows 11 window corners are ~8 px
    private const int CornerRadiusPx = 8;

    // ── Zoom ──────────────────────────────────────────────────────────────────

    private double _zoom = 1.0;
    private readonly ScaleTransform _scaleTransform = new(1.0, 1.0);

    // Scale factor: canvas logical units per image pixel (< 1 when image DPI > 96).
    private double _canvasScaleX = 1.0;
    private double _canvasScaleY = 1.0;

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

    // Arrow drag-to-draw state
    private bool _creatingArrowByDrag;
    private Point _arrowDragTailPt;
    private Line? _arrowDragPreviewLine;
    private Polygon? _arrowDragPreviewHead;

    // Crosshair shown at target point while dragging arrow tip/tail
    private Line? _crosshairWhiteH;
    private Line? _crosshairWhiteV;
    private Line? _crosshairRedH;
    private Line? _crosshairRedV;

    // Color picker
    private StackPanel? _colorPickerPanel;
    private AnnotationArrow? _colorPickerArrow;
    private AnnotationRect? _colorPickerRect;

    // Arrow creation defaults (shared with ScreenshotOverlayWindow via the same JSON file)
    private Color _defaultArrowColor = Color.FromRgb(255, 120, 20);
    private double _defaultArrowAngleDeg = 225.0;
    private double _defaultArrowLength = 15.0;
    private double _defaultTailLength = -1.0;

    // ── Annotation — rectangles ───────────────────────────────────────────────

    private readonly List<AnnotationRect> _annotRects = new();
    private AnnotationRect? _selectedAnnotRect;
    private bool _inRectMode;
    private Button? _addRectBtn;

    // Rect drag sub-state
    private AnnotationRect? _draggingAnnotRect;
    private int _draggingAnnotRectHandleIdx = -1; // -1=body, 0=NW,1=NE,2=SW,3=SE
    private Point _annotRectDragStart;
    private Rect _annotRectDragOriginal;
    private bool _annotRectBodyDragging;

    // Rubber-band state for drawing a new annotation rect
    private bool _creatingAnnotRect;
    private Point _annotRectAnchor;
    private Rectangle? _annotRectPreview;

    // Rect annotation defaults
    private Color _defaultRectColor = Color.FromRgb(255, 80, 80);

    // ── Annotation — cursor overlay ───────────────────────────────────────────

    private Image? _cursorImage;
    private bool _cursorEnabled;
    private bool _inCursorPlacementMode;
    private bool _draggingCursor;

    // ── Eyedropper ────────────────────────────────────────────────────────────

    private bool _inEyedropperMode;
    private Button? _eyedropperBtn;
    private Border? _eyedropperSwatch;
    private TextBlock? _eyedropperHexLabel;
    private Border? _eyedropperTooltipBorder;
    private Border? _eyedropperTooltipSwatch;
    private TextBlock? _eyedropperTooltipText;
    private byte[]? _cachedPixels;
    private int _cachedStride;

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

    // ── Editor mode ───────────────────────────────────────────────────────────

    /// <summary>
    /// When true the editor was opened to annotate a clipboard image for a prompt attachment
    /// (Ctrl+V / Shift+Insert in the prompt box). Round-corners button is hidden and the
    /// Insert button label/tooltip reflect the prompt use case.
    /// When false the editor was opened from a documentation panel context.
    /// </summary>
    private readonly bool _isPromptMode;

    // ── Spacebar pan mode ─────────────────────────────────────────────────────

    private bool _isPanMode;      // true while Space is held and focus not in a TextBox
    private bool _isPanning;      // true while actively dragging to pan
    private Point _panStartMouse; // viewport coords at drag start
    private double _panStartH;    // HorizontalOffset at drag start
    private double _panStartV;    // VerticalOffset at drag start

    // Custom hand cursors — lazy-initialised, generated programmatically at runtime
    private static Cursor? _openHandCursor;
    private static Cursor? _closedHandCursor;
    private static Cursor OpenHandCursor   => _openHandCursor   ??= CreateCursorFromDrawing(CreateOpenHandDrawing(),   32, 32, 10, 3);
    private static Cursor ClosedHandCursor => _closedHandCursor ??= CreateCursorFromDrawing(CreateClosedHandDrawing(), 32, 32, 14, 14);

    // ────────────────────────────────────────────────────────────────────────

    internal ClipboardImageEditorWindow(Window owner, BitmapSource clipboardImage, bool isPromptMode = false)
    {
        _clipboardImage = clipboardImage ?? throw new ArgumentNullException(nameof(clipboardImage));
        _isPromptMode = isPromptMode;
        _themeName = AgentStatusCard.IsDarkTheme ? "dark" : "light";

        Owner = owner;
        Title = "Edit Clipboard Image";
        WindowStyle = WindowStyle.SingleBorderWindow;
        ResizeMode = ResizeMode.CanResizeWithGrip;
        ShowInTaskbar = false;
        WindowStartupLocation = WindowStartupLocation.Manual;
        this.SetResourceReference(BackgroundProperty, "AppSurface");

        LoadArrowDefaults();

        // Resolve owner screen position early so all monitor lookups use the physical
        // top-left of the owner window — reliable even when the window is maximized.
        // PointToScreen returns physical pixel coords; TransformFromDevice converts them
        // to WPF logical units.  We use the physical coords directly for MonitorFromPoint
        // so we never misidentify the monitor (GetWindowRect on a maximized window returns
        // an inflated rect that can straddle the boundary between adjacent monitors).
        var ownerTopLeft = owner.PointToScreen(new Point(0, 0));
        var pSrc = PresentationSource.FromVisual(owner);
        var toLogical = pSrc?.CompositionTarget.TransformFromDevice ?? Matrix.Identity;

        // ── Compute display size ─────────────────────────────────────────────
        // Canvas shows image at its intended logical size, correcting for source DPI.
        // Screenshots taken on a 150%-scaled monitor arrive with DpiX/Y=144; without
        // correction the canvas would be 1.5× too large.  We normalise to 96 dpi so
        // the image appears at the intended physical size on any monitor.
        // Ctrl+scroll zoom is applied via ScaleTransform on the wrapper so
        // DoInsertImage always renders at the original pixel dimensions.
        var monitorArea = pSrc != null
            ? GetMonitorWorkAreaRect(ownerTopLeft, toLogical)
            : GetMonitorWorkAreaRect(owner);
        double imgW = clipboardImage.PixelWidth;
        double imgH = clipboardImage.PixelHeight;
        double maxWinW = monitorArea.Width  * 0.95;
        double maxWinH = monitorArea.Height * 0.95;

        double imageDpiX = clipboardImage.DpiX > 0 ? clipboardImage.DpiX : 96.0;
        double imageDpiY = clipboardImage.DpiY > 0 ? clipboardImage.DpiY : 96.0;
        // Logical (WPF) canvas size normalised to 96 dpi.
        double dispW = imgW * 96.0 / imageDpiX;
        double dispH = imgH * 96.0 / imageDpiY;
        // Pixels per canvas logical unit — used for pixel sampling and export crop.
        _canvasScaleX = imgW / dispW;  // = imageDpiX / 96
        _canvasScaleY = imgH / dispH;

        const double MinWindowWidth = 580;
        const double toolbarH = 110.0;

        // Compute initial zoom so the image fits inside the capped window on first open.
        // Never zoom in (max 1.0), only zoom out if the image is too large.
        double fitZoomW = (maxWinW - 24) / dispW;
        double fitZoomH = (maxWinH - toolbarH) / dispH;
        _zoom = Math.Min(1.0, Math.Min(fitZoomW, fitZoomH));
        _scaleTransform.ScaleX = _zoom;
        _scaleTransform.ScaleY = _zoom;

        // Window size = scaled image + chrome, capped to work area.
        Width  = Math.Max(MinWindowWidth, Math.Min(maxWinW, dispW * _zoom + 24));
        Height = Math.Min(maxWinH, dispH * _zoom + toolbarH);
        MinWidth = MinWindowWidth;

        // Center dialog on owner's actual screen position.
        // ownerTopLeft (physical pixels from PointToScreen) and toLogical are resolved above.
        Point waTopLeft = default, waBottomRight = default;
        if (pSrc != null)
        {
            var logicalOrigin = toLogical.Transform(ownerTopLeft);
            double ownerLogicalW = owner.ActualWidth;
            double ownerLogicalH = owner.ActualHeight;
            Left = logicalOrigin.X + (ownerLogicalW - Width)  / 2.0;
            Top  = logicalOrigin.Y + (ownerLogicalH - Height) / 2.0;

            // Clamp to monitor work area identified via the physical owner top-left.
            // Using ownerTopLeft (physical pixels from PointToScreen) for MonitorFromPoint
            // ensures we pick the correct monitor even when the owner is maximized, because
            // GetWindowRect on a maximized window returns an inflated rect that can straddle
            // the boundary between adjacent monitors (e.g. secondary above primary at Y<0).
            var work = GetMonitorWorkAreaRect(ownerTopLeft, toLogical);
            waTopLeft     = new Point(work.Left,  work.Top);
            waBottomRight = new Point(work.Right, work.Bottom);
            Left = Math.Max(waTopLeft.X, Math.Min(Left, waBottomRight.X - Width));
            Top  = Math.Max(waTopLeft.Y, Math.Min(Top,  waBottomRight.Y - Height));
        }
        else
        {
            // Fallback: use monitor work area center (pSrc null means window not yet rendered)
            var work = GetMonitorWorkAreaRect(owner);
            Left = work.Left + (work.Width  - Width)  / 2.0;
            Top  = work.Top  + (work.Height - Height) / 2.0;
        }

        // Write to SquadDash trace log (visible in the Trace panel) for diagnostics
        SquadDashTrace.Write("UI",
            $"[ClipboardImageEditor] owner.WindowState={owner.WindowState} " +
            $"owner.ActualWidth={owner.ActualWidth:F0} owner.ActualHeight={owner.ActualHeight:F0} " +
            $"ownerTopLeft={ownerTopLeft.X:F0},{ownerTopLeft.Y:F0} " +
            $"workArea=({waTopLeft.X:F0},{waTopLeft.Y:F0},{waBottomRight.X:F0},{waBottomRight.Y:F0}) " +
            $"dialog Left={Left:F0} Top={Top:F0} Width={Width:F0} Height={Height:F0}");

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

        // No initial crop selection — user drags one if they want to crop.
        // DoInsertImage falls back to the full canvas rect when _sel.IsEmpty.
        _sel = Rect.Empty;

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
            Panel.SetZIndex(h, 10); // above dim strips (2) and sel border (5)
        }

        // ── Dimension readout badges ─────────────────────────────────────────
        // Width badge: shown below the selection, centred on the bottom edge.
        // Height badge: shown to the right of the selection, centred on the right edge.
        var badgeBg = new SolidColorBrush(Color.FromArgb(200, 0, 0, 0));
        _dimWidthLabel = new TextBlock
        {
            Foreground = Brushes.White,
            FontSize = 11,
            FontWeight = FontWeights.SemiBold,
            IsHitTestVisible = false
        };
        _dimWidthBadge = new Border
        {
            Background = badgeBg,
            CornerRadius = new CornerRadius(3),
            Padding = new Thickness(4, 2, 4, 2),
            Child = _dimWidthLabel,
            IsHitTestVisible = false,
            Visibility = Visibility.Collapsed
        };
        _canvas.Children.Add(_dimWidthBadge);
        Panel.SetZIndex(_dimWidthBadge, 15);

        _dimHeightLabel = new TextBlock
        {
            Foreground = Brushes.White,
            FontSize = 11,
            FontWeight = FontWeights.SemiBold,
            IsHitTestVisible = false
        };
        _dimHeightBadge = new Border
        {
            Background = badgeBg,
            CornerRadius = new CornerRadius(3),
            Padding = new Thickness(4, 2, 4, 2),
            Child = _dimHeightLabel,
            IsHitTestVisible = false,
            Visibility = Visibility.Collapsed
        };
        _canvas.Children.Add(_dimHeightBadge);
        Panel.SetZIndex(_dimHeightBadge, 15);

        // ── Canvas events ────────────────────────────────────────────────────

        _canvas.MouseDown += Canvas_MouseDown;
        _canvas.MouseMove += Canvas_MouseMove;
        _canvas.MouseUp += Canvas_MouseUp;
        KeyDown += Window_KeyDown;

        PreviewKeyDown += (_, e) =>
        {
            if (e.Key == Key.Enter && Keyboard.FocusedElement is not TextBox)
            {
                e.Handled = true;
                DoInsertImage();
                return;
            }
            if (e.Key == Key.Space && !_isPanMode)
            {
                if (Keyboard.FocusedElement is TextBox) return;
                _isPanMode = true;
                _canvas.Cursor = OpenHandCursor;
                _canvas.ForceCursor = true;   // prevent child elements (shapes) from overriding cursor
                _scrollViewer.Cursor = OpenHandCursor;
                e.Handled = true;
            }
        };

        PreviewKeyUp += (_, e) =>
        {
            if (e.Key == Key.Space && _isPanMode)
            {
                _isPanMode = false;
                _canvas.ForceCursor = false;
                if (_isPanning)
                {
                    _isPanning = false;
                    _scrollViewer.ReleaseMouseCapture();
                }
                _canvas.Cursor = Cursors.Arrow;
                _scrollViewer.Cursor = null;
                e.Handled = true;
            }
        };

        // Ctrl+scroll = zoom in/out. The ScaleTransform lives on the wrapper (not
        // the canvas), so DoInsertImage renders _canvas at its original logical size.
        PreviewMouseWheel += (_, e) =>
        {
            if ((Keyboard.Modifiers & ModifierKeys.Control) == 0) return;

            var mouseInViewport = e.GetPosition(_scrollViewer);
            double oldZoom = _zoom;

            var factor = e.Delta > 0 ? 1.1 : 1.0 / 1.1;
            _zoom = Math.Max(0.1, Math.Min(8.0, _zoom * factor));
            _scaleTransform.ScaleX = _zoom;
            _scaleTransform.ScaleY = _zoom;
            if (_zoomLabel != null) _zoomLabel.Text = $"{_zoom * 100:F0}%";
            UpdateWindowSizeForZoom();

            // Force a synchronous layout pass so scroll extents reflect the new zoom.
            _scrollViewer.UpdateLayout();

            // Pin the image pixel under the cursor when scrollbars are active.
            // Skip if any drag is in progress (mouse is captured by a canvas element).
            if (Mouse.Captured == null &&
                (_scrollViewer.ScrollableWidth > 0 || _scrollViewer.ScrollableHeight > 0))
            {
                double imagePointX = (_scrollViewer.HorizontalOffset + mouseInViewport.X) / oldZoom;
                double imagePointY = (_scrollViewer.VerticalOffset + mouseInViewport.Y) / oldZoom;
                _scrollViewer.ScrollToHorizontalOffset(imagePointX * _zoom - mouseInViewport.X);
                _scrollViewer.ScrollToVerticalOffset(imagePointY * _zoom - mouseInViewport.Y);
            }

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

        _scrollViewer = new ScrollViewer
        {
            HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            Content = canvasWrapper
        };
        _scrollViewer.SetResourceReference(BackgroundProperty, "AppSurface");

        _scrollViewer.PreviewMouseLeftButtonDown += (_, e) =>
        {
            if (!_isPanMode) return;
            _isPanning = true;
            _panStartMouse = e.GetPosition(_scrollViewer);
            _panStartH = _scrollViewer.HorizontalOffset;
            _panStartV = _scrollViewer.VerticalOffset;
            _scrollViewer.CaptureMouse();
            _canvas.Cursor = ClosedHandCursor;
            _scrollViewer.Cursor = ClosedHandCursor;
            e.Handled = true;
        };

        _scrollViewer.PreviewMouseMove += (_, e) =>
        {
            if (!_isPanning) return;
            var pos = e.GetPosition(_scrollViewer);
            double dx = pos.X - _panStartMouse.X;
            double dy = pos.Y - _panStartMouse.Y;
            _scrollViewer.ScrollToHorizontalOffset(_panStartH - dx);
            _scrollViewer.ScrollToVerticalOffset(_panStartV - dy);
            e.Handled = true;
        };

        _scrollViewer.PreviewMouseLeftButtonUp += (_, e) =>
        {
            if (!_isPanning) return;
            _isPanning = false;
            _scrollViewer.ReleaseMouseCapture();
            _canvas.Cursor = _isPanMode ? OpenHandCursor : Cursors.Arrow;
            _scrollViewer.Cursor = _isPanMode ? OpenHandCursor : null;
            e.Handled = true;
        };

        var toolbar = BuildToolbar();
        var root = new DockPanel { LastChildFill = true };
        DockPanel.SetDock(toolbar, Dock.Bottom);
        root.Children.Add(toolbar);
        root.Children.Add(_scrollViewer);
        Content = root;

        Loaded += (_, _) =>
        {
            RefreshLayout();
            UpdateWindowSizeForZoom(); // center on first paint
        };
    }

    // ── Toolbar ───────────────────────────────────────────────────────────────

    private Border BuildToolbar()
    {
        _addArrowBtn = new Button
        {
            Content  = MakeToolIcon("ImageEditorArrowIcon"),
            Width    = 32, Height = 28,
            Padding  = new Thickness(4, 3, 4, 3),
            Margin   = new Thickness(0, 0, 4, 0),
            ToolTip  = "Arrow (drag to draw)"
        };
        _addRectBtn = new Button
        {
            Content  = MakeToolIcon("ImageEditorRectIcon"),
            Width    = 32, Height = 28,
            Padding  = new Thickness(4, 3, 4, 3),
            Margin   = new Thickness(0, 0, 4, 0),
            ToolTip  = "Rectangle annotation (drag to draw)"
        };
        var cursorBtn = new Button
        {
            Content  = MakeToolIcon("ImageEditorCursorIcon"),
            Width    = 32, Height = 28,
            Padding  = new Thickness(4, 3, 4, 3),
            Margin   = new Thickness(0, 0, 4, 0),
            ToolTip  = "Add a mouse-cursor indicator"
        };
        _eyedropperBtn = new Button
        {
            Content  = MakeToolIcon("ImageEditorEyedropperIcon"),
            Width    = 32, Height = 28,
            Padding  = new Thickness(4, 3, 4, 3),
            Margin   = new Thickness(0, 0, 4, 0),
            ToolTip  = "Pick a color from the image"
        };
        var roundCornersBtn = new Button
        {
            Content  = MakeToolIcon("ImageEditorRoundCornersIcon"),
            Width    = 32, Height = 28,
            Padding  = new Thickness(4, 3, 4, 3),
            Margin   = new Thickness(0, 0, 4, 0),
            ToolTip  = $"Mask the {CornerRadiusPx}px corners transparent in the output PNG"
        };

        string insertLabel   = _isPromptMode ? "Attach Image"  : "Insert Image";
        string insertTooltip = _isPromptMode
            ? "Attach this image to the prompt"
            : "Insert this image into the documentation";
        var insertBtn = new Button
        {
            Content = insertLabel,
            Width   = _isPromptMode ? 100 : 96,
            Height  = 28,
            Margin  = new Thickness(0, 0, 4, 0),
            ToolTip = insertTooltip
        };
        var cancelBtn = new Button { Content = "Cancel", Width = 70, Height = 28 };

        _eyedropperSwatch = new Border
        {
            Width = 20, Height = 20,
            CornerRadius = new CornerRadius(3),
            Background = Brushes.Transparent,
            BorderBrush = new SolidColorBrush(Color.FromRgb(160, 160, 160)),
            BorderThickness = new Thickness(1),
            Margin = new Thickness(0, 0, 4, 0),
            VerticalAlignment = VerticalAlignment.Center,
            ToolTip = "Sampled color",
            Visibility = Visibility.Collapsed
        };
        _eyedropperHexLabel = new TextBlock
        {
            Text = "",
            VerticalAlignment = VerticalAlignment.Center,
            FontFamily = new FontFamily("Consolas"),
            FontSize = 13,
            Margin = new Thickness(0, 0, 8, 0)
        };
        _eyedropperHexLabel.SetResourceReference(TextBlock.ForegroundProperty, "LabelText");
        _eyedropperHexLabel.ContextMenu = new ContextMenu();
        var copyHexItem = new MenuItem { Header = "Copy" };
        copyHexItem.Click += (_, _) =>
        {
            if (!string.IsNullOrEmpty(_eyedropperHexLabel.Text))
                Clipboard.SetText(_eyedropperHexLabel.Text);
        };
        _eyedropperHexLabel.ContextMenu.Items.Add(copyHexItem);

        // In prompt mode the Round Corners option is not relevant — hide it.
        if (_isPromptMode)
            roundCornersBtn.Visibility = Visibility.Collapsed;

        var styleButtons = _isPromptMode
            ? new[] { _addArrowBtn, _addRectBtn, cursorBtn, _eyedropperBtn, insertBtn, cancelBtn }
            : new[] { _addArrowBtn, _addRectBtn, cursorBtn, _eyedropperBtn, roundCornersBtn, insertBtn, cancelBtn };
        foreach (var btn in styleButtons)
            btn.SetResourceReference(Control.StyleProperty, "ThemedButtonStyle");

        _addArrowBtn.Click += (_, _) =>
        {
            if (_inEyedropperMode) ExitEyedropperMode();
            if (_inArrowMode) { ExitArrowMode(); return; }
            EnterArrowMode();
            _addArrowBtn.Content = MakeToolIcon("ImageEditorArrowIcon", active: true);
        };

        _addRectBtn.Click += (_, _) =>
        {
            if (_inEyedropperMode) ExitEyedropperMode();
            if (_inRectMode) { ExitRectMode(); return; }
            EnterRectMode();
            _addRectBtn.Content = MakeToolIcon("ImageEditorRectIcon", active: true);
        };

        cursorBtn.Click += (_, _) =>
        {
            if (_inEyedropperMode) ExitEyedropperMode();
            _cursorEnabled = !_cursorEnabled;
            if (_cursorEnabled)
            {
                _inCursorPlacementMode = true;
                cursorBtn.Content = MakeToolIcon("ImageEditorCursorIcon", active: true);
                ShowModeHint("Click to place the cursor indicator");
            }
            else
            {
                _inCursorPlacementMode = false;
                cursorBtn.Content = MakeToolIcon("ImageEditorCursorIcon");
                ToggleCursorOverlay(false);
                HideModeHint();
            }
        };

        _eyedropperBtn.Click += (_, _) =>
        {
            if (_inEyedropperMode) { ExitEyedropperMode(); return; }
            EnterEyedropperMode();
            _eyedropperBtn.Content = "✓ ⊕ Color";
        };

        roundCornersBtn.Click += (_, _) =>
        {
            _roundCorners = !_roundCorners;
            roundCornersBtn.Content = _roundCorners ? "✓ Round Corners" : "⌐ Round Corners";
        };

        insertBtn.Click += (_, _) => DoInsertImage();
        cancelBtn.Click += (_, _) => Close();

        _zoomLabel = new TextBlock
        {
            Text = $"{_zoom * 100:F0}%",
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(8, 0, 4, 0),
            FontSize = 11
        };
        _zoomLabel.SetResourceReference(TextBlock.ForegroundProperty, "LabelText");

        var resetZoomBtn = new Button { Content = "1:1", Width = 36, Height = 28, Margin = new Thickness(0, 0, 4, 0), ToolTip = "Reset zoom to 100% (Ctrl+0)" };
        resetZoomBtn.SetResourceReference(Control.StyleProperty, "ThemedButtonStyle");
        resetZoomBtn.Click += (_, _) => { _zoom = 1.0; _scaleTransform.ScaleX = 1.0; _scaleTransform.ScaleY = 1.0; _zoomLabel.Text = "100%"; UpdateWindowSizeForZoom(); };

        // Update label whenever zoom changes via keyboard shortcut
        KeyDown += (_, e) =>
        {
            if (e.Key == Key.D0 && (Keyboard.Modifiers & ModifierKeys.Control) != 0)
            {
                _zoom = 1.0; _scaleTransform.ScaleX = 1.0; _scaleTransform.ScaleY = 1.0;
                _zoomLabel.Text = "100%";
                UpdateWindowSizeForZoom();
                e.Handled = true;
            }
        };

        var row = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Margin = new Thickness(8, 6, 8, 6)
        };
        row.Children.Add(_addArrowBtn);
        row.Children.Add(_addRectBtn);
        row.Children.Add(cursorBtn);
        row.Children.Add(_eyedropperBtn);
        row.Children.Add(_eyedropperSwatch);
        row.Children.Add(_eyedropperHexLabel);
        row.Children.Add(roundCornersBtn);
        row.Children.Add(insertBtn);
        row.Children.Add(cancelBtn);
        row.Children.Add(_zoomLabel);
        row.Children.Add(resetZoomBtn);

        var border = new Border { BorderThickness = new Thickness(0, 1, 0, 0) };
        border.SetResourceReference(Border.BorderBrushProperty, "PopupBorder");
        border.Child = row;
        return border;
    }

    /// <summary>
    /// Creates button content displaying the named icon resource (a Viewbox from
    /// ImageEditorIcons.xaml). When <paramref name="active"/> is true a small accent
    /// underline is added so the user can see which tool is currently engaged.
    /// </summary>
    private UIElement MakeToolIcon(string resourceKey, bool active = false)
    {
        var resource = TryFindResource(resourceKey);
        if (resource == null)
            return new TextBlock { Text = resourceKey };

        // Clone the resource element — TryFindResource returns the same singleton instance
        // every time, which WPF rejects if already parented to another element.
        UIElement icon;
        try
        {
            var xaml = System.Windows.Markup.XamlWriter.Save(resource);
            icon = (UIElement)System.Windows.Markup.XamlReader.Parse(xaml);
        }
        catch
        {
            // Fallback: if serialization fails (e.g. dynamic resources), return a placeholder.
            return new TextBlock { Text = resourceKey };
        }

        if (!active) return icon;

        // Active state: stack icon over a 2px accent bar at the bottom.
        var accent = new Border
        {
            Height = 2,
            VerticalAlignment = VerticalAlignment.Bottom
        };
        accent.SetResourceReference(Border.BackgroundProperty, "AccentBrush");

        var grid = new Grid();
        grid.Children.Add(icon);
        grid.Children.Add(accent);
        return grid;
    }

    // ── Visual layout ─────────────────────────────────────────────────────────

    // P/Invoke for per-monitor work area (used when System.Windows.Forms is unavailable)
    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern IntPtr MonitorFromWindow(IntPtr hwnd, uint dwFlags);
    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern bool GetMonitorInfo(IntPtr hMonitor, ref MonitorInfo lpmi);
    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern bool GetWindowRect(IntPtr hWnd, out Rect32 lpRect);
    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern IntPtr MonitorFromPoint(POINT pt, uint dwFlags);

    [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
    private struct Rect32 { public int Left, Top, Right, Bottom; }
    [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
    private struct POINT { public int X, Y; }
    [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
    private struct MonitorInfo
    {
        public uint cbSize;
        public Rect32 rcMonitor;
        public Rect32 rcWork;
        public uint dwFlags;  // required field — was missing, causing GetMonitorInfo to return false
    }

    private const uint MONITOR_DEFAULTTONEAREST = 2;

    /// <summary>
    /// Returns the work area of the monitor that <paramref name="w"/> is on as a WPF DIP
    /// <see cref="Rect"/> (origin = top-left of work area in screen coordinates).
    /// Uses GetWindowRect → center point → MonitorFromPoint for robustness when the window
    /// is maximized or the HWND is not yet materialized. Falls back through WPF coordinates
    /// and finally to <see cref="SystemParameters.WorkArea"/> (primary monitor).
    /// </summary>
    private static Rect GetMonitorWorkAreaRect(Window w)
    {
        try
        {
            var helper = new System.Windows.Interop.WindowInteropHelper(w);
            IntPtr hwnd = helper.Handle;

            // Strategy 1: GetWindowRect gives actual pixel bounds even when maximized,
            // unlike WPF Left/Top which reflects restore bounds. MonitorFromPoint on the
            // center is more reliable than MonitorFromWindow when HWND may be transitional.
            if (hwnd != IntPtr.Zero && GetWindowRect(hwnd, out Rect32 wr))
            {
                int cx = (wr.Left + wr.Right)  / 2;
                int cy = (wr.Top  + wr.Bottom) / 2;
                var hMon = MonitorFromPoint(new POINT { X = cx, Y = cy }, MONITOR_DEFAULTTONEAREST);
                if (hMon != IntPtr.Zero)
                {
                    var mi = new MonitorInfo { cbSize = (uint)System.Runtime.InteropServices.Marshal.SizeOf<MonitorInfo>() };
                    if (GetMonitorInfo(hMon, ref mi))
                    {
                        var source = System.Windows.Interop.HwndSource.FromHwnd(hwnd);
                        double sx = source?.CompositionTarget?.TransformToDevice.M11 ?? 1.0;
                        double sy = source?.CompositionTarget?.TransformToDevice.M22 ?? 1.0;
                        return new Rect(
                            mi.rcWork.Left   / sx,
                            mi.rcWork.Top    / sy,
                            (mi.rcWork.Right  - mi.rcWork.Left) / sx,
                            (mi.rcWork.Bottom - mi.rcWork.Top)  / sy);
                    }
                }
            }

            // Strategy 2: HWND not yet available — use WPF Left/Top to estimate screen center.
            // For maximized windows this is the restore position, but still beats falling back
            // all the way to the primary monitor.
            double dpiX = 1.0, dpiY = 1.0;
            if (hwnd != IntPtr.Zero)
            {
                var src = System.Windows.Interop.HwndSource.FromHwnd(hwnd);
                dpiX = src?.CompositionTarget?.TransformToDevice.M11 ?? 1.0;
                dpiY = src?.CompositionTarget?.TransformToDevice.M22 ?? 1.0;
            }
            int wpfCx = (int)((w.Left + w.Width  / 2) * dpiX);
            int wpfCy = (int)((w.Top  + w.Height / 2) * dpiY);
            var hMon2 = MonitorFromPoint(new POINT { X = wpfCx, Y = wpfCy }, MONITOR_DEFAULTTONEAREST);
            if (hMon2 != IntPtr.Zero)
            {
                var mi2 = new MonitorInfo { cbSize = (uint)System.Runtime.InteropServices.Marshal.SizeOf<MonitorInfo>() };
                if (GetMonitorInfo(hMon2, ref mi2))
                {
                    return new Rect(
                        mi2.rcWork.Left   / dpiX,
                        mi2.rcWork.Top    / dpiY,
                        (mi2.rcWork.Right  - mi2.rcWork.Left) / dpiX,
                        (mi2.rcWork.Bottom - mi2.rcWork.Top)  / dpiY);
                }
            }
        }
        catch { }
        return SystemParameters.WorkArea; // last resort: primary monitor
    }

    /// <summary>
    /// Returns the work area of the monitor that contains <paramref name="physPt"/>
    /// (physical screen pixel coordinates, e.g. from <c>PointToScreen</c>) as a WPF DIP
    /// <see cref="Rect"/>, using <paramref name="transformFromDevice"/> to convert from
    /// physical pixels to logical units.
    /// </summary>
    private static Rect GetMonitorWorkAreaRect(Point physPt, Matrix transformFromDevice)
    {
        try
        {
            var hMon = MonitorFromPoint(new POINT { X = (int)physPt.X, Y = (int)physPt.Y }, MONITOR_DEFAULTTONEAREST);
            if (hMon != IntPtr.Zero)
            {
                var mi = new MonitorInfo { cbSize = (uint)System.Runtime.InteropServices.Marshal.SizeOf<MonitorInfo>() };
                if (GetMonitorInfo(hMon, ref mi))
                {
                    var tl = transformFromDevice.Transform(new Point(mi.rcWork.Left,  mi.rcWork.Top));
                    var br = transformFromDevice.Transform(new Point(mi.rcWork.Right, mi.rcWork.Bottom));
                    return new Rect(tl, br);
                }
            }
        }
        catch { }
        return SystemParameters.WorkArea;
    }

    /// <summary>
    /// Resizes the window to fit the scaled image within the current monitor's work area.
    /// Preserves the current window center so zooming never jumps the window to a different
    /// monitor (important for monitors with negative screen coordinates).
    /// </summary>
    private void UpdateWindowSizeForZoom()
    {
        var work = GetMonitorWorkAreaRect(this);
        const double toolbarH = 110.0;
        const double minW = 580.0;

        double scaledImgW = _canvas.Width  * _zoom;
        double scaledImgH = _canvas.Height * _zoom;
        double desiredW = Math.Max(minW, scaledImgW + 24);
        double desiredH = scaledImgH + toolbarH;

        double newW = Math.Min(work.Width,  desiredW);
        double newH = Math.Min(work.Height, desiredH);

        // Keep the current window center fixed — don't re-centre on the monitor.
        // This prevents the window from jumping to a different monitor when the work-area
        // DIP conversion is imprecise (e.g. monitors with negative screen coordinates).
        double newLeft = Left + (Width  - newW) / 2.0;
        double newTop  = Top  + (Height - newH) / 2.0;

        // Clamp to stay fully within the current monitor's work area.
        if (newLeft < work.Left) newLeft = work.Left;
        if (newTop  < work.Top)  newTop  = work.Top;
        if (newLeft + newW > work.Right)  newLeft = work.Right  - newW;
        if (newTop  + newH > work.Bottom) newTop  = work.Bottom - newH;

        Width  = newW;
        Height = newH;
        Left   = newLeft;
        Top    = newTop;
    }

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
            _dimWidthBadge.Visibility = Visibility.Collapsed;
            _dimHeightBadge.Visibility = Visibility.Collapsed;
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

        // ── Dimension readout badges ─────────────────────────────────────────
        // Canvas logical coords = pixels (canvas is always full image resolution).
        var pixW = (int)Math.Round(s.Width);
        var pixH = (int)Math.Round(s.Height);
        _dimWidthLabel.Text = $"{pixW} px";
        _dimHeightLabel.Text = $"{pixH} px";
        // Badges are always shown during an active crop drag in both doc-editor and prompt modes.
        // DoInsertImage() collapses them immediately before the RenderTargetBitmap call so they
        // are never baked into the output image.
        _dimWidthBadge.Visibility  = Visibility.Visible;
        _dimHeightBadge.Visibility = Visibility.Visible;

        // Force measure so DesiredSize is accurate for positioning.
        _dimWidthBadge.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        _dimHeightBadge.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));

        var bwW = _dimWidthBadge.DesiredSize.Width;
        var bwH = _dimWidthBadge.DesiredSize.Height;
        var bhW = _dimHeightBadge.DesiredSize.Width;
        var bhH = _dimHeightBadge.DesiredSize.Height;

        // Width badge: centred below the selection bottom edge (or above if too close to canvas bottom)
        const double BadgeGap = 5.0;
        var wBadgeLeft = cx - bwW / 2;
        var wBadgeTop = s.Bottom + BadgeGap;
        if (wBadgeTop + bwH > h) wBadgeTop = s.Top - bwH - BadgeGap;
        Canvas.SetLeft(_dimWidthBadge, Math.Max(0, Math.Min(w - bwW, wBadgeLeft)));
        Canvas.SetTop(_dimWidthBadge, Math.Max(0, wBadgeTop));

        // Height badge: centred to the right of the selection right edge (or left if too close to canvas right)
        var hBadgeLeft = s.Right + BadgeGap;
        var hBadgeTop = cy - bhH / 2;
        if (hBadgeLeft + bhW > w) hBadgeLeft = s.Left - bhW - BadgeGap;
        Canvas.SetLeft(_dimHeightBadge, Math.Max(0, hBadgeLeft));
        Canvas.SetTop(_dimHeightBadge, Math.Max(0, Math.Min(h - bhH, hBadgeTop)));

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

        // Eyedropper mode: sample pixel on click.
        if (_inEyedropperMode)
        {
            var ept = e.GetPosition(_canvas);
            var ec = SamplePixelAtCanvasPoint(ept);
            UpdateEyedropperResult(ec);
            e.Handled = true;
            return;
        }

        SelectArrow(null);
        SelectAnnotationRect(null);

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

        // Arrow placement mode: start drag to define tail→head.
        if (_inArrowMode)
        {
            _creatingArrowByDrag = true;
            _arrowDragTailPt = pt;
            _preDragSnapshot = CaptureSnapshot();
            _canvas.CaptureMouse();
            _arrowDragPreviewLine = new Line
            {
                Stroke = new SolidColorBrush(_defaultArrowColor),
                StrokeThickness = 2.5,
                Opacity = 0.7,
                IsHitTestVisible = false,
                X1 = pt.X, Y1 = pt.Y, X2 = pt.X, Y2 = pt.Y
            };
            _arrowDragPreviewHead = new Polygon
            {
                Fill = new SolidColorBrush(_defaultArrowColor),
                Opacity = 0.7,
                IsHitTestVisible = false
            };
            Panel.SetZIndex(_arrowDragPreviewLine, 99);
            Panel.SetZIndex(_arrowDragPreviewHead, 99);
            _canvas.Children.Add(_arrowDragPreviewLine);
            _canvas.Children.Add(_arrowDragPreviewHead);
            e.Handled = true;
            return;
        }

        // Rect drawing mode: start rubber-band.
        if (_inRectMode)
        {
            _creatingAnnotRect = true;
            _annotRectAnchor = pt;
            _preDragSnapshot = CaptureSnapshot();
            _canvas.CaptureMouse();
            var preview = EnsureAnnotRectPreview();
            preview.Visibility = Visibility.Visible;
            Canvas.SetLeft(preview, pt.X);
            Canvas.SetTop(preview, pt.Y);
            preview.Width = 1;
            preview.Height = 1;
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

        // Draw a new crop region from scratch — works whether or not a selection already exists.
        // Clicking outside the current selection (zone == None) replaces it; undo restores the old one.
        if (!_inArrowMode && !_inCursorPlacementMode && !_inRectMode)
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
        // Pan mode owns the cursor and all mouse interaction while Space is held.
        if (_isPanMode) return;

        // Live preview for arrow drag-to-draw.
        if (_creatingArrowByDrag && _arrowDragPreviewLine != null && _arrowDragPreviewHead != null)
        {
            var headPt = e.GetPosition(_canvas);
            var tailPt = _arrowDragTailPt;
            _arrowDragPreviewLine.X1 = tailPt.X;
            _arrowDragPreviewLine.Y1 = tailPt.Y;
            _arrowDragPreviewLine.X2 = headPt.X;
            _arrowDragPreviewLine.Y2 = headPt.Y;

            var dx = headPt.X - tailPt.X;
            var dy = headPt.Y - tailPt.Y;
            var dist2 = Math.Sqrt(dx * dx + dy * dy);
            if (dist2 > 4)
            {
                var ux2 = dx / dist2; var uy2 = dy / dist2;
                const double HeadLen = 16.0;
                const double HeadHalf = 6.0;
                var baseX = headPt.X - ux2 * HeadLen;
                var baseY = headPt.Y - uy2 * HeadLen;
                var px = -uy2; var py = ux2;
                // Stop the shaft at the head base so the line and polygon don't overlap.
                // When both shapes share Opacity < 1 and overlap, WPF composites them
                // independently, making the head appear semi-transparent over the shaft.
                _arrowDragPreviewLine.X2 = baseX;
                _arrowDragPreviewLine.Y2 = baseY;
                _arrowDragPreviewHead.Points = new PointCollection
                {
                    headPt,
                    new Point(baseX + px * HeadHalf, baseY + py * HeadHalf),
                    new Point(baseX - px * HeadHalf, baseY - py * HeadHalf)
                };
                // Show crosshair at the future pivot center (ArrowLength past the tip).
                ShowCrosshair(headPt.X + ux2 * _defaultArrowLength * 1.5, headPt.Y + uy2 * _defaultArrowLength * 1.5);
            }
            else
            {
                HideCrosshair();
            }
            e.Handled = true;
            return;
        }

        // Rubber-band draw of an annotation rectangle.
        if (_creatingAnnotRect)
        {
            var pt2 = e.GetPosition(_canvas);
            var l = Math.Max(0, Math.Min(_annotRectAnchor.X, pt2.X));
            var t = Math.Max(0, Math.Min(_annotRectAnchor.Y, pt2.Y));
            var r = Math.Min(_canvas.Width, Math.Max(_annotRectAnchor.X, pt2.X));
            var b2 = Math.Min(_canvas.Height, Math.Max(_annotRectAnchor.Y, pt2.Y));
            if (r - l < 4) r = l + 4;
            if (b2 - t < 4) b2 = t + 4;
            var preview = EnsureAnnotRectPreview();
            Canvas.SetLeft(preview, l);
            Canvas.SetTop(preview, t);
            preview.Width = r - l;
            preview.Height = b2 - t;
            e.Handled = true;
            return;
        }

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

        // Eyedropper mode: show live color tooltip.
        if (_inEyedropperMode)
        {
            _canvas.Cursor = Cursors.Cross;
            var ept = e.GetPosition(_canvas);
            var ec = SamplePixelAtCanvasPoint(ept);
            ShowEyedropperTooltip(ept, ec);
            return;
        }

        // Update cursor shape based on hover zone (not during arrow/cursor drag).
        if (!_draggingCursor && _draggingArrow == null && !_bodyDragging && _draggingAnnotRect == null)
        {
            if (_sel.IsEmpty)
                _canvas.Cursor = Cursors.Cross;
            else
            {
                var hoverZone = HitTest(e.GetPosition(_canvas));
                _canvas.Cursor = ZoneCursor(hoverZone);
            }

            // Override with directional cursor when hovering over a selected annotation rect's edge/corner.
            if (_selectedAnnotRect != null)
            {
                var az = HitTestAnnotRect(_selectedAnnotRect, e.GetPosition(_canvas));
                if (az != HitZone.None)
                    _canvas.Cursor = ZoneCursor(az);
            }
        }
    }

    private void Canvas_MouseUp(object sender, MouseButtonEventArgs e)
    {
        if (_creatingArrowByDrag)
        {
            _creatingArrowByDrag = false;
            _canvas.ReleaseMouseCapture();
            if (_arrowDragPreviewLine != null) { _canvas.Children.Remove(_arrowDragPreviewLine); _arrowDragPreviewLine = null; }
            if (_arrowDragPreviewHead != null) { _canvas.Children.Remove(_arrowDragPreviewHead); _arrowDragPreviewHead = null; }
            HideCrosshair();

            var headPt = e.GetPosition(_canvas);
            var tailPt = _arrowDragTailPt;
            var dx = headPt.X - tailPt.X;
            var dy = headPt.Y - tailPt.Y;
            var dist = Math.Sqrt(dx * dx + dy * dy);

            if (dist >= 20.0)
            {
                _preDragSnapshot = null; // let CreateArrow handle its own undo push
                PlaceArrowFromDrag(tailPt, headPt, dist);
            }
            else
            {
                _preDragSnapshot = null;
            }
            e.Handled = true;
            return;
        }

        if (_creatingAnnotRect)
        {
            _creatingAnnotRect = false;
            _canvas.ReleaseMouseCapture();
            _canvas.Cursor = Cursors.Arrow;
            if (_annotRectPreview != null)
            {
                _annotRectPreview.Visibility = Visibility.Hidden;
                if (_annotRectPreview.Width >= MinSize && _annotRectPreview.Height >= MinSize)
                {
                    var bounds = new Rect(
                        Canvas.GetLeft(_annotRectPreview),
                        Canvas.GetTop(_annotRectPreview),
                        _annotRectPreview.Width,
                        _annotRectPreview.Height);
                    CreateAnnotationRect(bounds);
                }
            }
            CommitDragUndo();
            ExitRectMode();
            e.Handled = true;
            return;
        }

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
            if (_inArrowMode)
            {
                if (_creatingArrowByDrag)
                {
                    _creatingArrowByDrag = false;
                    _canvas.ReleaseMouseCapture();
                    if (_arrowDragPreviewLine != null) { _canvas.Children.Remove(_arrowDragPreviewLine); _arrowDragPreviewLine = null; }
                    if (_arrowDragPreviewHead != null) { _canvas.Children.Remove(_arrowDragPreviewHead); _arrowDragPreviewHead = null; }
                    HideCrosshair();
                    _preDragSnapshot = null;
                }
                ExitArrowMode(); e.Handled = true; return;
            }
            if (_inRectMode) { ExitRectMode(); e.Handled = true; return; }
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
        else if (e.Key == Key.Delete && _selectedAnnotRect != null)
        {
            PushUndo();
            RemoveAnnotationRect(_selectedAnnotRect);
            _selectedAnnotRect = null;
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
        else if (e.Key is Key.Return or Key.Enter && Keyboard.Modifiers == ModifierKeys.None)
        {
            DoInsertImage();
            e.Handled = true;
        }
    }

    // ── Arrow mode ────────────────────────────────────────────────────────────

    private void EnterArrowMode()
    {
        _inArrowMode = true;
        Cursor = Cursors.Cross;
        ShowModeHint("Drag to draw an arrow");
    }

    private void ExitArrowMode()
    {
        _inArrowMode = false;
        Cursor = Cursors.Arrow;
        HideModeHint();
        if (_addArrowBtn != null) _addArrowBtn.Content = MakeToolIcon("ImageEditorArrowIcon");
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

    /// <summary>
    /// Creates an arrow where <paramref name="tailPt"/> is the blunt tail end
    /// and <paramref name="headPt"/> is the arrowhead tip (pointy end).
    /// </summary>
    private void PlaceArrowFromDrag(Point tailPt, Point headPt, double dist)
    {
        headPt = new Point(
            Math.Max(0, Math.Min(headPt.X, _canvas.Width)),
            Math.Max(0, Math.Min(headPt.Y, _canvas.Height)));
        tailPt = new Point(
            Math.Max(0, Math.Min(tailPt.X, _canvas.Width)),
            Math.Max(0, Math.Min(tailPt.Y, _canvas.Height)));

        // ux,uy = direction from arrowhead tip toward tail (UpdateArrowGeometry convention)
        var ux = (tailPt.X - headPt.X) / dist;
        var uy = (tailPt.Y - headPt.Y) / dist;

        double arrowLen = _defaultArrowLength;
        // TailLength represents the shaft length from center to tail-end.
        // With center placed at headPt - ux*arrowLen, tailX = center + ux*(arrowLen+tailLen).
        // We want tailX == tailPt, so: tailLen = dist (not dist - arrowLen).
        double tailLen = Math.Max(20.0, dist);

        // ux = sin(rad), uy = -cos(rad) => rad = atan2(ux, -uy)
        double angleDeg = Math.Atan2(ux, -uy) * 180.0 / Math.PI;

        // Center such that ahX = center.X + ux*arrowLen = headPt.X
        double centerX = headPt.X - ux * arrowLen;
        double centerY = headPt.Y - uy * arrowLen;

        var targetBounds = new Rect(centerX - 1, centerY - 1, 2, 2);

        var savedAngle = _defaultArrowAngleDeg;
        var savedTailLen = _defaultTailLength;
        _defaultArrowAngleDeg = angleDeg;
        _defaultTailLength = tailLen;

        var arrow = CreateArrow(targetBounds);

        _defaultArrowAngleDeg = savedAngle;
        _defaultTailLength = savedTailLen;

        SelectArrow(arrow);
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
        var hitLine = new Line
        {
            StrokeThickness = 9,
            Stroke = Brushes.Transparent,
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
        _canvas.Children.Add(hitLine);
        Panel.SetZIndex(hitLine, 6);  // just above line (5) so it intercepts first

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
            ShadowHead = shadowHead,
            HitLine = hitLine
        };

        // ── Tip-handle drag: changes angle (pivot stays at target centre) ─────
        tipHandle.MouseLeftButtonDown += (_, e) =>
        {
            _preDragSnapshot = CaptureSnapshot();
            _draggingArrow = arrow;
            _tailDragging = false;
            HideColorPicker();
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
            ShowCrosshair(pivot.X, pivot.Y);
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
            HideCrosshair();
            ShowColorPicker(arrow);
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
            HideColorPicker();
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
            ShowCrosshair(pivot.X, pivot.Y);
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
            HideCrosshair();
            ShowColorPicker(arrow);
            tailHandle.ReleaseMouseCapture();
            e.Handled = true;
        };

        // ── Body drag (click line or head to select + move) ───────────────────
        AttachBodyDrag(line, arrow);
        AttachBodyDrag(head, arrow);
        AttachBodyDrag(hitLine, arrow);

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
            HideColorPicker();
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
            ShowCrosshair(
                arrow.TargetCenterOnCanvas.X + arrow.OffsetX,
                arrow.TargetCenterOnCanvas.Y + arrow.OffsetY);
            e.Handled = true;
        };
        shape.MouseLeftButtonUp += (_, e) =>
        {
            if (_draggingArrow != arrow || !_bodyDragging) return;
            HideCrosshair();
            CommitDragUndo();
            _draggingArrow = null;
            _bodyDragging = false;
            ShowColorPicker(arrow);
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

        const double HeadLen = 16.0;
        const double HeadHalf = 6.0;
        var baseX = ahX + ux * HeadLen;
        var baseY = ahY + uy * HeadLen;

        // Stop shaft at the head base so it doesn't poke through the arrowhead tip.
        arrow.Line.X1 = tailX; arrow.Line.Y1 = tailY;
        arrow.Line.X2 = baseX; arrow.Line.Y2 = baseY;

        arrow.HitLine.X1 = arrow.Line.X1;
        arrow.HitLine.Y1 = arrow.Line.Y1;
        arrow.HitLine.X2 = arrow.Line.X2;
        arrow.HitLine.Y2 = arrow.Line.Y2;
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
            new Point(baseX + 2, baseY + 2)
        });
        arrow.ShadowHead.Points = new PointCollection(
            arrow.Head.Points.Select(p => p + new Vector(2, 2)));
    }

    // ── Crosshair overlay (shown while dragging arrow tip/tail) ──────────────

    private void EnsureCrosshairLines()
    {
        if (_crosshairRedH != null) return;
        const double Thick = 1.5;
        _crosshairWhiteH = new Line { Stroke = System.Windows.Media.Brushes.White, StrokeThickness = Thick + 1.0, Opacity = 0.5, IsHitTestVisible = false };
        _crosshairWhiteV = new Line { Stroke = System.Windows.Media.Brushes.White, StrokeThickness = Thick + 1.0, Opacity = 0.5, IsHitTestVisible = false };
        _crosshairRedH   = new Line { Stroke = System.Windows.Media.Brushes.Red,   StrokeThickness = Thick,                         IsHitTestVisible = false };
        _crosshairRedV   = new Line { Stroke = System.Windows.Media.Brushes.Red,   StrokeThickness = Thick,                         IsHitTestVisible = false };
        foreach (var l in new[] { _crosshairWhiteH, _crosshairWhiteV, _crosshairRedH, _crosshairRedV })
        {
            Panel.SetZIndex(l, 100);
            l.Visibility = Visibility.Collapsed;
            _canvas.Children.Add(l);
        }
    }

    private void ShowCrosshair(double cx, double cy)
    {
        EnsureCrosshairLines();
        const double Half   = 10.0;
        const double Shadow = 1.0;   // white offset (1px right + 1px down) behind the red lines
        _crosshairWhiteH!.X1 = cx - Half + Shadow; _crosshairWhiteH.Y1 = cy + Shadow;
        _crosshairWhiteH.X2  = cx + Half + Shadow; _crosshairWhiteH.Y2 = cy + Shadow;
        _crosshairWhiteV!.X1 = cx + Shadow; _crosshairWhiteV.Y1 = cy - Half + Shadow;
        _crosshairWhiteV.X2  = cx + Shadow; _crosshairWhiteV.Y2 = cy + Half + Shadow;
        _crosshairRedH!.X1 = cx - Half; _crosshairRedH.Y1 = cy;
        _crosshairRedH.X2  = cx + Half; _crosshairRedH.Y2 = cy;
        _crosshairRedV!.X1 = cx; _crosshairRedV.Y1 = cy - Half;
        _crosshairRedV.X2  = cx; _crosshairRedV.Y2 = cy + Half;
        _crosshairWhiteH.Visibility = _crosshairWhiteV.Visibility =
        _crosshairRedH.Visibility   = _crosshairRedV.Visibility   = Visibility.Visible;
    }

    private void HideCrosshair()
    {
        if (_crosshairRedH is null) return;
        _crosshairWhiteH!.Visibility = _crosshairWhiteV!.Visibility =
        _crosshairRedH.Visibility    = _crosshairRedV!.Visibility   = Visibility.Collapsed;
    }

    private double ComputeInitialTailLength(Point targetCenter, double angleDeg, double arrowheadOffset)
    {
        var rad = angleDeg * Math.PI / 180.0;
        var dx = Math.Sin(rad);
        var dy = -Math.Cos(rad);
        var ahX = targetCenter.X + dx * arrowheadOffset;
        var ahY = targetCenter.Y + dy * arrowheadOffset;
        var s = _sel.IsEmpty ? new Rect(0, 0, _canvas.Width, _canvas.Height) : _sel;

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
        var s = _sel.IsEmpty ? new Rect(0, 0, _canvas.Width, _canvas.Height) : _sel;

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

    private static Color[] GetArrowPalette() => new[]
    {
        Color.FromRgb(220,  50,  50),
        Color.FromRgb(255, 140,   0),
        Color.FromRgb(240, 210,  40),
        Color.FromRgb( 50, 185,  50),
        Color.FromRgb( 50, 130, 230),
        Color.FromRgb(255, 255, 255),
        Color.FromRgb(  0,   0,   0),
    };

    private void ShowColorPicker(AnnotationArrow arrow)
    {
        HideColorPicker();
        _colorPickerArrow = arrow;
        var palette = GetArrowPalette();

        _colorPickerPanel = new StackPanel { Orientation = Orientation.Horizontal };
        Panel.SetZIndex(_colorPickerPanel, 300);

        foreach (var color in palette)
        {
            var c = color;
            bool isSelected = c == arrow.ArrowColor;
            var swatch = MakeColorSwatch(c, isSelected, picked =>
            {
                arrow.ArrowColor = picked;
                _defaultArrowColor = picked;
                SaveArrowDefaults();
                UpdateArrowGeometry(arrow);
                ShowColorPicker(arrow);
            });
            _colorPickerPanel.Children.Add(swatch);
        }

        _canvas.Children.Add(_colorPickerPanel);

        double cx = Canvas.GetLeft(arrow.TipHandle) + 4;
        double cy = Canvas.GetTop(arrow.TipHandle) + 4;
        _colorPickerPanel.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        double pw = _colorPickerPanel.DesiredSize.Width;
        Canvas.SetLeft(_colorPickerPanel, Math.Max(0, cx - pw / 2));
        Canvas.SetTop(_colorPickerPanel, Math.Max(0, cy - 30));
    }

    private static FrameworkElement MakeColorSwatch(Color c, bool isSelected, Action<Color> onPick)
    {
        if (isSelected)
        {
            var grid = new Grid { Width = 20, Height = 20, Margin = new Thickness(3, 0, 3, 0), Cursor = Cursors.Hand };
            grid.Children.Add(new Ellipse { Fill = Brushes.Black });
            grid.Children.Add(new Ellipse
            {
                Width = 16,
                Height = 16,
                Fill = new SolidColorBrush(c),
                Stroke = Brushes.White,
                StrokeThickness = 1.5,
            });
            grid.MouseLeftButtonDown += (_, e) => { onPick(c); e.Handled = true; };
            return grid;
        }
        else
        {
            var dot = new Ellipse
            {
                Width = 16,
                Height = 16,
                Fill = new SolidColorBrush(c),
                Stroke = new SolidColorBrush(Color.FromRgb(0x88, 0x88, 0x88)),
                StrokeThickness = 1,
                Margin = new Thickness(3, 0, 3, 0),
                Cursor = Cursors.Hand,
            };
            dot.MouseLeftButtonDown += (_, e) => { onPick(c); e.Handled = true; };
            return dot;
        }
    }

    private void HideColorPicker()
    {
        if (_colorPickerPanel != null)
        {
            _canvas.Children.Remove(_colorPickerPanel);
            _colorPickerPanel = null;
        }
        _colorPickerArrow = null;
        _colorPickerRect = null;
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
        _canvas.Children.Remove(arrow.HitLine);
        _arrows.Remove(arrow);
    }

    // ── Rect mode ─────────────────────────────────────────────────────────────

    private void EnterRectMode()
    {
        _inRectMode = true;
        Cursor = Cursors.Cross;
        ShowModeHint("Drag to draw a rectangle");
    }

    private void ExitRectMode()
    {
        _inRectMode = false;
        Cursor = Cursors.Arrow;
        HideModeHint();
        if (_addRectBtn != null) _addRectBtn.Content = MakeToolIcon("ImageEditorRectIcon");
    }

    private Rectangle EnsureAnnotRectPreview()
    {
        if (_annotRectPreview != null) return _annotRectPreview;
        _annotRectPreview = new Rectangle
        {
            Stroke = new SolidColorBrush(_defaultRectColor),
            StrokeThickness = 2,
            StrokeDashArray = new DoubleCollection { 4, 2 },
            Fill = Brushes.Transparent,
            RadiusX = 4,
            RadiusY = 4,
            IsHitTestVisible = false,
            Visibility = Visibility.Hidden
        };
        Panel.SetZIndex(_annotRectPreview, 50);
        _canvas.Children.Add(_annotRectPreview);
        return _annotRectPreview;
    }

    private AnnotationRect CreateAnnotationRect(Rect bounds, Color? color = null)
    {
        if (!_suppressUndo) PushUndo();

        var rectColor = color ?? _defaultRectColor;
        var brush = new SolidColorBrush(rectColor);

        var shadow = new Rectangle
        {
            Fill = Brushes.Transparent,
            Stroke = new SolidColorBrush(Color.FromArgb(102, 0, 0, 0)),
            StrokeThickness = 2.5,
            RadiusX = 4,
            RadiusY = 4,
            IsHitTestVisible = false
        };

        var border = new Rectangle
        {
            Fill = Brushes.Transparent,
            Stroke = brush,
            StrokeThickness = 2.5,
            RadiusX = 4,
            RadiusY = 4,
            Cursor = Cursors.SizeAll,
            IsHitTestVisible = true
        };

        var hitZone = new Rectangle
        {
            Fill = Brushes.Transparent,
            StrokeThickness = 0,
            IsHitTestVisible = true,
            Cursor = Cursors.SizeAll
        };

        var handles = new Ellipse[8];
        for (int i = 0; i < 8; i++)
        {
            var cursor = i switch
            {
                0 or 3 => Cursors.SizeNWSE,  // NW, SE
                1 or 2 => Cursors.SizeNESW,  // NE, SW
                4 or 5 => Cursors.SizeNS,    // N, S
                6 or 7 => Cursors.SizeWE,    // W, E
                _ => Cursors.SizeAll
            };
            handles[i] = new Ellipse
            {
                Width = 8,
                Height = 8,
                Fill = brush,
                Stroke = Brushes.White,
                StrokeThickness = 1.5,
                Cursor = cursor,
                Visibility = Visibility.Hidden
            };
            _canvas.Children.Add(handles[i]);
            Panel.SetZIndex(handles[i], 10);
        }

        _canvas.Children.Add(shadow);
        _canvas.Children.Add(border);
        Panel.SetZIndex(shadow, 2);
        Panel.SetZIndex(border, 5);
        _canvas.Children.Add(hitZone);
        Panel.SetZIndex(hitZone, 4);  // just below border (5) — still catches clicks outside border

        var annotRect = new AnnotationRect
        {
            Bounds = bounds,
            RectColor = rectColor,
            Border = border,
            Shadow = shadow,
            Handles = handles,
            HitZoneRect = hitZone
        };

        border.MouseLeftButtonDown += (_, e) =>
        {
            if (_draggingAnnotRect != null || _inArrowMode || _inRectMode) return;
            SelectAnnotationRect(annotRect);
            _preDragSnapshot = CaptureSnapshot();
            _draggingAnnotRect = annotRect;
            _annotRectBodyDragging = true;
            _draggingAnnotRectHandleIdx = -1;
            _annotRectDragStart = e.GetPosition(_canvas);
            _annotRectDragOriginal = annotRect.Bounds;
            border.CaptureMouse();
            e.Handled = true;
        };
        border.MouseMove += (_, e) =>
        {
            if (_draggingAnnotRect != annotRect || !_annotRectBodyDragging) return;
            var pt = e.GetPosition(_canvas);
            var dx = pt.X - _annotRectDragStart.X;
            var dy = pt.Y - _annotRectDragStart.Y;
            var cw = _canvas.Width;
            var ch = _canvas.Height;
            var nb = new Rect(
                Math.Max(0, Math.Min(_annotRectDragOriginal.X + dx, cw - _annotRectDragOriginal.Width)),
                Math.Max(0, Math.Min(_annotRectDragOriginal.Y + dy, ch - _annotRectDragOriginal.Height)),
                _annotRectDragOriginal.Width,
                _annotRectDragOriginal.Height);
            annotRect.Bounds = nb;
            UpdateRectGeometry(annotRect);
            if (_colorPickerRect == annotRect) ShowColorPickerForRect(annotRect);
            e.Handled = true;
        };
        border.MouseLeftButtonUp += (_, e) =>
        {
            if (_draggingAnnotRect != annotRect || !_annotRectBodyDragging) return;
            CommitDragUndo();
            _draggingAnnotRect = null;
            _annotRectBodyDragging = false;
            border.ReleaseMouseCapture();
            e.Handled = true;
        };
        border.MouseRightButtonDown += (_, e) =>
        {
            PushUndo();
            RemoveAnnotationRect(annotRect);
            if (_selectedAnnotRect == annotRect) { _selectedAnnotRect = null; HideColorPicker(); }
            e.Handled = true;
        };

        hitZone.MouseLeftButtonDown += (_, e) =>
        {
            if (_draggingAnnotRect != null || _inArrowMode || _inRectMode) return;
            SelectAnnotationRect(annotRect);
            _preDragSnapshot = CaptureSnapshot();
            _draggingAnnotRect = annotRect;
            _annotRectBodyDragging = true;
            _draggingAnnotRectHandleIdx = -1;
            _annotRectDragStart = e.GetPosition(_canvas);
            _annotRectDragOriginal = annotRect.Bounds;
            hitZone.CaptureMouse();
            e.Handled = true;
        };
        hitZone.MouseMove += (_, e) =>
        {
            if (_draggingAnnotRect != annotRect || !_annotRectBodyDragging) return;
            var pt = e.GetPosition(_canvas);
            var dx = pt.X - _annotRectDragStart.X;
            var dy = pt.Y - _annotRectDragStart.Y;
            var cw = _canvas.Width;
            var ch = _canvas.Height;
            var nb = new Rect(
                Math.Max(0, Math.Min(_annotRectDragOriginal.X + dx, cw - _annotRectDragOriginal.Width)),
                Math.Max(0, Math.Min(_annotRectDragOriginal.Y + dy, ch - _annotRectDragOriginal.Height)),
                _annotRectDragOriginal.Width,
                _annotRectDragOriginal.Height);
            annotRect.Bounds = nb;
            UpdateRectGeometry(annotRect);
            if (_colorPickerRect == annotRect) ShowColorPickerForRect(annotRect);
            e.Handled = true;
        };
        hitZone.MouseLeftButtonUp += (_, e) =>
        {
            if (_draggingAnnotRect != annotRect || !_annotRectBodyDragging) return;
            CommitDragUndo();
            _draggingAnnotRect = null;
            _annotRectBodyDragging = false;
            hitZone.ReleaseMouseCapture();
            e.Handled = true;
        };
        hitZone.MouseRightButtonDown += (_, e) =>
        {
            PushUndo();
            RemoveAnnotationRect(annotRect);
            if (_selectedAnnotRect == annotRect) { _selectedAnnotRect = null; HideColorPicker(); }
            e.Handled = true;
        };

        for (int i = 0; i < 8; i++)
        {
            int handleIdx = i;
            var handle = handles[i];

            handle.MouseLeftButtonDown += (_, e) =>
            {
                if (_draggingAnnotRect != null) return;
                _preDragSnapshot = CaptureSnapshot();
                _draggingAnnotRect = annotRect;
                _draggingAnnotRectHandleIdx = handleIdx;
                _annotRectBodyDragging = false;
                _annotRectDragStart = e.GetPosition(_canvas);
                _annotRectDragOriginal = annotRect.Bounds;
                handle.CaptureMouse();
                e.Handled = true;
            };
            handle.MouseMove += (_, e) =>
            {
                if (_draggingAnnotRect != annotRect || _annotRectBodyDragging || _draggingAnnotRectHandleIdx != handleIdx) return;
                var pt = e.GetPosition(_canvas);
                var dx = pt.X - _annotRectDragStart.X;
                var dy = pt.Y - _annotRectDragStart.Y;
                var o = _annotRectDragOriginal;
                var cw = _canvas.Width;
                var ch = _canvas.Height;

                double nl = o.Left, nt = o.Top, nr = o.Right, nb2 = o.Bottom;
                // 0=NW(left+top), 1=NE(right+top), 2=SW(left+bottom), 3=SE(right+bottom), 4=N, 5=S, 6=W, 7=E
                if (handleIdx == 0 || handleIdx == 2 || handleIdx == 6) nl = Math.Max(0, Math.Min(nl + dx, nr - MinSize));
                if (handleIdx == 1 || handleIdx == 3 || handleIdx == 7) nr = Math.Max(nl + MinSize, Math.Min(nr + dx, cw));
                if (handleIdx == 0 || handleIdx == 1 || handleIdx == 4) nt = Math.Max(0, Math.Min(nt + dy, nb2 - MinSize));
                if (handleIdx == 2 || handleIdx == 3 || handleIdx == 5) nb2 = Math.Max(nt + MinSize, Math.Min(nb2 + dy, ch));

                annotRect.Bounds = new Rect(nl, nt, nr - nl, nb2 - nt);
                UpdateRectGeometry(annotRect);
                if (_colorPickerRect == annotRect) ShowColorPickerForRect(annotRect);
                e.Handled = true;
            };
            handle.MouseLeftButtonUp += (_, e) =>
            {
                if (_draggingAnnotRect != annotRect || _annotRectBodyDragging || _draggingAnnotRectHandleIdx != handleIdx) return;
                CommitDragUndo();
                _draggingAnnotRect = null;
                _draggingAnnotRectHandleIdx = -1;
                handle.ReleaseMouseCapture();
                e.Handled = true;
            };
            handle.MouseRightButtonDown += (_, e) =>
            {
                PushUndo();
                RemoveAnnotationRect(annotRect);
                if (_selectedAnnotRect == annotRect) { _selectedAnnotRect = null; HideColorPicker(); }
                e.Handled = true;
            };
        }

        _annotRects.Add(annotRect);
        UpdateRectGeometry(annotRect);
        SelectAnnotationRect(annotRect);
        return annotRect;
    }

    private static void UpdateRectGeometry(AnnotationRect rect)
    {
        var b = rect.Bounds;
        var brush = new SolidColorBrush(rect.RectColor);
        rect.Border.Stroke = brush;
        foreach (var h in rect.Handles) h.Fill = brush;

        Canvas.SetLeft(rect.Shadow, b.Left + 2);
        Canvas.SetTop(rect.Shadow, b.Top + 2);
        rect.Shadow.Width = b.Width;
        rect.Shadow.Height = b.Height;

        Canvas.SetLeft(rect.Border, b.Left);
        Canvas.SetTop(rect.Border, b.Top);
        rect.Border.Width = b.Width;
        rect.Border.Height = b.Height;

        // Handles: NW(0), NE(1), SW(2), SE(3), N(4), S(5), W(6), E(7)
        PlaceRectHandle(rect.Handles[0], b.Left,                  b.Top);
        PlaceRectHandle(rect.Handles[1], b.Right,                 b.Top);
        PlaceRectHandle(rect.Handles[2], b.Left,                  b.Bottom);
        PlaceRectHandle(rect.Handles[3], b.Right,                 b.Bottom);
        PlaceRectHandle(rect.Handles[4], b.Left + b.Width  / 2,  b.Top);
        PlaceRectHandle(rect.Handles[5], b.Left + b.Width  / 2,  b.Bottom);
        PlaceRectHandle(rect.Handles[6], b.Left,                  b.Top + b.Height / 2);
        PlaceRectHandle(rect.Handles[7], b.Right,                 b.Top + b.Height / 2);

        if (rect.HitZoneRect != null)
        {
            const double hp = 3.0;
            Canvas.SetLeft(rect.HitZoneRect, b.Left - hp);
            Canvas.SetTop(rect.HitZoneRect, b.Top - hp);
            rect.HitZoneRect.Width = b.Width + hp * 2;
            rect.HitZoneRect.Height = b.Height + hp * 2;
        }
    }

    private static void PlaceRectHandle(Ellipse h, double cx, double cy)
    {
        Canvas.SetLeft(h, cx - 4);
        Canvas.SetTop(h, cy - 4);
    }

    private static HitZone HitTestAnnotRect(AnnotationRect r, Point pt)
    {
        const double ep = 6.0;
        var b = r.Bounds;

        // Corners (check first — tighter region)
        if (Math.Abs(pt.X - b.Left)  <= ep && Math.Abs(pt.Y - b.Top)    <= ep) return HitZone.NW;
        if (Math.Abs(pt.X - b.Right) <= ep && Math.Abs(pt.Y - b.Top)    <= ep) return HitZone.NE;
        if (Math.Abs(pt.X - b.Left)  <= ep && Math.Abs(pt.Y - b.Bottom) <= ep) return HitZone.SW;
        if (Math.Abs(pt.X - b.Right) <= ep && Math.Abs(pt.Y - b.Bottom) <= ep) return HitZone.SE;

        // Edges
        if (Math.Abs(pt.Y - b.Top)    <= ep && pt.X > b.Left && pt.X < b.Right)  return HitZone.N;
        if (Math.Abs(pt.Y - b.Bottom) <= ep && pt.X > b.Left && pt.X < b.Right)  return HitZone.S;
        if (Math.Abs(pt.X - b.Left)   <= ep && pt.Y > b.Top  && pt.Y < b.Bottom) return HitZone.W;
        if (Math.Abs(pt.X - b.Right)  <= ep && pt.Y > b.Top  && pt.Y < b.Bottom) return HitZone.E;

        if (b.Contains(pt)) return HitZone.Move;
        return HitZone.None;
    }

    private void RemoveAnnotationRect(AnnotationRect rect)
    {
        if (!_suppressUndo) PushUndo();
        if (rect == _colorPickerRect) HideColorPicker();
        _canvas.Children.Remove(rect.Shadow);
        _canvas.Children.Remove(rect.Border);
        _canvas.Children.Remove(rect.HitZoneRect);
        foreach (var h in rect.Handles) _canvas.Children.Remove(h);
        _annotRects.Remove(rect);
    }

    private void SelectAnnotationRect(AnnotationRect? rect)
    {
        if (_selectedAnnotRect != null && _selectedAnnotRect != rect)
            foreach (var h in _selectedAnnotRect.Handles) h.Visibility = Visibility.Hidden;

        _selectedAnnotRect = rect;
        if (rect != null)
        {
            SelectArrow(null);
            foreach (var h in rect.Handles) h.Visibility = Visibility.Visible;
            ShowColorPickerForRect(rect);
        }
        else
        {
            HideColorPicker();
        }
    }

    private void ShowColorPickerForRect(AnnotationRect rect)
    {
        HideColorPicker();
        _colorPickerRect = rect;
        var palette = GetArrowPalette();

        _colorPickerPanel = new StackPanel { Orientation = Orientation.Horizontal };
        Panel.SetZIndex(_colorPickerPanel, 300);

        foreach (var color in palette)
        {
            var c = color;
            bool isSelected = c == rect.RectColor;
            var swatch = MakeColorSwatch(c, isSelected, picked =>
            {
                rect.RectColor = picked;
                _defaultRectColor = picked;
                UpdateRectGeometry(rect);
                ShowColorPickerForRect(rect);
            });
            _colorPickerPanel.Children.Add(swatch);
        }

        _canvas.Children.Add(_colorPickerPanel);

        double cx = rect.Bounds.Left + rect.Bounds.Width / 2;
        double cy = rect.Bounds.Top;
        _colorPickerPanel.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        double pw = _colorPickerPanel.DesiredSize.Width;
        Canvas.SetLeft(_colorPickerPanel, Math.Max(0, cx - pw / 2));
        Canvas.SetTop(_colorPickerPanel, Math.Max(0, cy - 30));
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

    // ── Eyedropper ────────────────────────────────────────────────────────────

    private void EnterEyedropperMode()
    {
        _inEyedropperMode = true;
        _canvas.Cursor = Cursors.Cross;
        ShowModeHint("Hover to preview color — click to capture");
    }

    private void ExitEyedropperMode()
    {
        _inEyedropperMode = false;
        _canvas.Cursor = Cursors.Arrow;
        HideModeHint();
        HideEyedropperTooltip();
        if (_eyedropperBtn != null) _eyedropperBtn.Content = "⊕ Color";
    }

    private void CachePixels()
    {
        var conv = new FormatConvertedBitmap(_clipboardImage, PixelFormats.Bgra32, null, 0);
        _cachedStride = conv.PixelWidth * 4;
        _cachedPixels = new byte[_cachedStride * conv.PixelHeight];
        conv.CopyPixels(_cachedPixels, _cachedStride, 0);
    }

    private Color SamplePixelAtCanvasPoint(Point pt)
    {
        if (_cachedPixels == null) CachePixels();
        // Canvas coordinates are in logical (96-dpi) units; convert to image pixels.
        int px = (int)Math.Max(0, Math.Min(pt.X * _canvasScaleX, _clipboardImage.PixelWidth - 1));
        int py = (int)Math.Max(0, Math.Min(pt.Y * _canvasScaleY, _clipboardImage.PixelHeight - 1));
        int offset = py * _cachedStride + px * 4;
        byte bv = _cachedPixels![offset];
        byte gv = _cachedPixels[offset + 1];
        byte rv = _cachedPixels[offset + 2];
        return Color.FromRgb(rv, gv, bv);
    }

    private static (double H, double S, double L) RgbToHsl(Color c)
    {
        double r = c.R / 255.0, g = c.G / 255.0, b = c.B / 255.0;
        double max = Math.Max(r, Math.Max(g, b));
        double min = Math.Min(r, Math.Min(g, b));
        double l = (max + min) / 2.0;
        if (max == min) return (0, 0, l * 100);
        double d = max - min;
        double s = l > 0.5 ? d / (2.0 - max - min) : d / (max + min);
        double h;
        if (max == r) h = (g - b) / d + (g < b ? 6 : 0);
        else if (max == g) h = (b - r) / d + 2;
        else h = (r - g) / d + 4;
        return (h / 6.0 * 360.0, s * 100.0, l * 100.0);
    }

    private void ShowEyedropperTooltip(Point pt, Color color)
    {
        if (_eyedropperTooltipBorder == null)
        {
            _eyedropperTooltipSwatch = new Border
            {
                Width = 36, Height = 36,
                CornerRadius = new CornerRadius(3),
                Margin = new Thickness(0, 0, 8, 0),
                BorderThickness = new Thickness(1),
                BorderBrush = new SolidColorBrush(Color.FromArgb(0x60, 0xFF, 0xFF, 0xFF)),
                IsHitTestVisible = false
            };
            _eyedropperTooltipText = new TextBlock
            {
                FontSize = 13,
                FontFamily = new FontFamily("Consolas"),
                Foreground = Brushes.White,
                IsHitTestVisible = false,
                VerticalAlignment = VerticalAlignment.Center
            };
            var row = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                IsHitTestVisible = false
            };
            row.Children.Add(_eyedropperTooltipSwatch);
            row.Children.Add(_eyedropperTooltipText);
            _eyedropperTooltipBorder = new Border
            {
                Background = new SolidColorBrush(Color.FromArgb(0xCC, 0x10, 0x10, 0x10)),
                CornerRadius = new CornerRadius(4),
                Padding = new Thickness(6, 4, 6, 4),
                Child = row,
                IsHitTestVisible = false,
                Visibility = Visibility.Collapsed
            };
            Panel.SetZIndex(_eyedropperTooltipBorder, 200);
            _canvas.Children.Add(_eyedropperTooltipBorder);
        }
        _eyedropperTooltipSwatch!.Background = new SolidColorBrush(color);
        var (h, s, l) = RgbToHsl(color);
        _eyedropperTooltipText!.Text =
            $"R:{color.R}  G:{color.G}  B:{color.B}\nH:{h:F0}°  S:{s:F0}%  L:{l:F0}%";
        _eyedropperTooltipBorder.Visibility = Visibility.Visible;
        _eyedropperTooltipBorder.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        var tw = _eyedropperTooltipBorder.DesiredSize.Width;
        var th = _eyedropperTooltipBorder.DesiredSize.Height;
        double tx = pt.X + 14;
        double ty = pt.Y - th - 6;
        if (tx + tw > _canvas.Width)  tx = pt.X - tw - 6;
        if (ty < 0)                   ty = pt.Y + 14;
        Canvas.SetLeft(_eyedropperTooltipBorder, tx);
        Canvas.SetTop(_eyedropperTooltipBorder, ty);
    }

    private void HideEyedropperTooltip()
    {
        if (_eyedropperTooltipBorder != null)
            _eyedropperTooltipBorder.Visibility = Visibility.Collapsed;
    }

    private void UpdateEyedropperResult(Color color)
    {
        if (_eyedropperSwatch != null)
        {
            _eyedropperSwatch.Background = new SolidColorBrush(color);
            _eyedropperSwatch.Visibility = Visibility.Visible;
        }
        if (_eyedropperHexLabel != null)
            _eyedropperHexLabel.Text = $"#{color.R:X2}{color.G:X2}{color.B:X2}";
    }

    // ── Insert Image ──────────────────────────────────────────────────────────

    private void DoInsertImage()
    {
        // Hide chrome before rendering so handles/color-picker don't appear in the output.
        foreach (var h in _handles) h.Visibility = Visibility.Collapsed;
        _selBorderRect.Visibility  = Visibility.Collapsed;
        _dimWidthBadge.Visibility  = Visibility.Collapsed;
        _dimHeightBadge.Visibility = Visibility.Collapsed;
        HideColorPicker();
        HideModeHint();
        HideEyedropperTooltip();

        if (_selectedArrow != null)
        {
            _selectedArrow.TipHandle.Visibility = Visibility.Collapsed;
            _selectedArrow.TailHandle.Visibility = Visibility.Collapsed;
        }

        foreach (var ar in _annotRects)
            foreach (var h in ar.Handles) h.Visibility = Visibility.Collapsed;

        try
        {
            // Always render at original pixel dimensions regardless of monitor DPI or
            // the display scaling applied in the constructor.
            var pxW = _clipboardImage.PixelWidth;
            var pxH = _clipboardImage.PixelHeight;
            if (pxW < 1 || pxH < 1) return;

            // Use a DrawingVisual so we can scale from canvas logical size to pixel size.
            var rtb = new RenderTargetBitmap(pxW, pxH, 96, 96, PixelFormats.Pbgra32);
            var dv = new DrawingVisual();
            using (var ctx = dv.RenderOpen())
            {
                // VisualBrush with Stretch=Fill maps the full canvas to the target rect,
                // preserving every original pixel even when canvas was DPI-downscaled.
                var vb = new VisualBrush(_canvas) { Stretch = Stretch.Fill };
                ctx.DrawRectangle(vb, null, new Rect(0, 0, pxW, pxH));
            }
            rtb.Render(dv);
            rtb.Freeze();

            // Crop: convert canvas logical selection coords back to pixel coords.
            var cropSel = _sel.IsEmpty ? new Rect(0, 0, _canvas.ActualWidth, _canvas.ActualHeight) : _sel;
            var cropX = (int)Math.Round(cropSel.Left * _canvasScaleX);
            var cropY = (int)Math.Round(cropSel.Top * _canvasScaleY);
            var cropW = (int)Math.Round(cropSel.Width * _canvasScaleX);
            var cropH = (int)Math.Round(cropSel.Height * _canvasScaleY);

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
        Rects: _annotRects.Select(r => new RectSnap(r.Bounds, r.RectColor)).ToList(),
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
            SelectAnnotationRect(null);
            foreach (var a in _arrows.ToList()) RemoveArrow(a);
            foreach (var r in _annotRects.ToList()) RemoveAnnotationRect(r);

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
            SelectAnnotationRect(null);

            foreach (var rs in snap.Rects)
                CreateAnnotationRect(rs.Bounds, rs.RectColor);

            SelectAnnotationRect(null);
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
        IReadOnlyList<RectSnap> Rects,
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

    private sealed record RectSnap(Rect Bounds, Color RectColor);

    // ── Custom hand cursors ───────────────────────────────────────────────────

    private static Cursor CreateCursorFromDrawing(Drawing drawing, int widthPx, int heightPx, int hotX, int hotY)
    {
        var rtb = new RenderTargetBitmap(widthPx, heightPx, 96, 96, PixelFormats.Pbgra32);
        var dv = new DrawingVisual();
        using (var dc = dv.RenderOpen())
            dc.DrawDrawing(drawing);
        rtb.Render(dv);

        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(rtb));
        using var pngStream = new MemoryStream();
        encoder.Save(pngStream);
        var png = pngStream.ToArray();

        using var cur = new MemoryStream();
        using var bw = new BinaryWriter(cur);
        bw.Write((short)0);
        bw.Write((short)2);
        bw.Write((short)1);
        bw.Write((byte)widthPx);
        bw.Write((byte)heightPx);
        bw.Write((byte)0);
        bw.Write((byte)0);
        bw.Write((short)hotX);
        bw.Write((short)hotY);
        bw.Write((int)png.Length);
        bw.Write((int)22);
        bw.Write(png);
        cur.Position = 0;
        return new Cursor(cur);
    }

    private static Drawing CreateOpenHandDrawing()
    {
        var dg = new DrawingGroup();
        using (var dc = dg.Open())
        {
            var fill   = new SolidColorBrush(Color.FromRgb(255, 252, 242));
            var stroke = new Pen(new SolidColorBrush(Color.FromRgb(35, 35, 35)), 1.3)
                { LineJoin = PenLineJoin.Round, StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round };

            // Palm
            dc.DrawRoundedRectangle(fill, stroke, new Rect(5, 16, 18, 13), 3, 3);

            // Thumb (left side)
            var thumbGeo = new PathGeometry();
            var thumbFig = new PathFigure { StartPoint = new Point(5, 20), IsClosed = true };
            thumbFig.Segments.Add(new BezierSegment(new Point(2, 19), new Point(1, 15), new Point(3, 12), true));
            thumbFig.Segments.Add(new BezierSegment(new Point(4, 9),  new Point(6, 10), new Point(6, 13), true));
            thumbFig.Segments.Add(new LineSegment(new Point(5, 16), true));
            thumbGeo.Figures.Add(thumbFig);
            dc.DrawGeometry(fill, stroke, thumbGeo);

            // Four fingers: index, middle, ring, pinky
            double[] fingerX    = { 7,  10, 13, 17 };
            double[] fingerW    = { 3,   3,  3,  2.5 };
            double[] fingerTopY = { 5,   3,  4,   7 };
            for (int i = 0; i < 4; i++)
                dc.DrawRoundedRectangle(fill, stroke,
                    new Rect(fingerX[i], fingerTopY[i], fingerW[i], 16 - fingerTopY[i] + 1),
                    1.5, 1.5);
        }
        return dg;
    }

    private static Drawing CreateClosedHandDrawing()
    {
        var dg = new DrawingGroup();
        using (var dc = dg.Open())
        {
            var fill   = new SolidColorBrush(Color.FromRgb(255, 252, 242));
            var stroke = new Pen(new SolidColorBrush(Color.FromRgb(35, 35, 35)), 1.3)
                { LineJoin = PenLineJoin.Round, StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round };

            // Knuckle row
            dc.DrawRoundedRectangle(fill, stroke, new Rect(5, 11, 19, 8), 3, 3);

            // Palm
            dc.DrawRoundedRectangle(fill, stroke, new Rect(5, 17, 19, 11), 3, 3);

            // Thumb tucked left
            dc.DrawRoundedRectangle(fill, stroke, new Rect(2, 14, 5, 7), 2, 2);

            // Finger separation lines on knuckle row
            var linePen = new Pen(new SolidColorBrush(Color.FromRgb(35, 35, 35)), 0.8);
            dc.DrawLine(linePen, new Point(10, 11), new Point(10, 19));
            dc.DrawLine(linePen, new Point(14, 11), new Point(14, 19));
            dc.DrawLine(linePen, new Point(18, 11), new Point(18, 19));
        }
        return dg;
    }
}
