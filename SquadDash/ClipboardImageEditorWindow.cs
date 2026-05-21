using System;
using System.IO;
using System.Linq;
using System.Windows;
using System.Text.Json;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Threading.Tasks;
using System.Windows.Controls;
using System.Windows.Threading;
using System.Collections.Generic;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;
using System.Windows.Media.Animation;

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
internal sealed class ClipboardImageEditorWindow : Window {
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

    // Mutable working image — starts as _clipboardImage, replaced after each Enter-crop.
    private BitmapSource _workingImage = null!;

    // Image control on the canvas whose Source we update after Enter-crop.
    private Image _imageCtrl = null!;

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
    // Inverse-zoom transforms applied to the badges so they stay at a constant screen size.
    private readonly ScaleTransform _dimWidthBadgeScale = new(1.0, 1.0);
    private readonly ScaleTransform _dimHeightBadgeScale = new(1.0, 1.0);

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
    private Canvas _overlayCanvas = null!;

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
    private Button? _moveSelectBtn;
    private Button? _cropBtn;

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

    // Last-used arrow angle/tail and rect size — used for click-without-drag placement.
    private double _lastDragArrowAngleDeg = 225.0;  // same as _defaultArrowAngleDeg default
    private double _lastDragArrowTailLength = 80.0;
    private double _lastDragRectWidth = 120.0;
    private double _lastDragRectHeight = 80.0;

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
    private TextBlock? _eyedropperTooltipRgbText;
    private TextBlock? _eyedropperTooltipHslText;
    private byte[]? _cachedPixels;
    private int _cachedStride;

    // ── Mode hint ─────────────────────────────────────────────────────────────

    private Border? _modeHintBorder;
    private TextBlock? _modeHintText;
    private DispatcherTimer? _modeHintFadeTimer;

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

    // ── Annotation — multi-drop mode ──────────────────────────────────────────

    /// <summary>
    /// When true, arrow/rect mode stays active after each placement so the user can
    /// drop multiple shapes without re-clicking the toolbar button.
    /// Activated by Shift+clicking the toolbar button; exited by ESC or switching tool.
    /// </summary>
    private bool _inArrowMultiDropMode;
    private bool _inRectMultiDropMode;
    private bool _inMoveMode;
    private bool _inCropMode;

    // ── Annotation — text labels ───────────────────────────────────────────────

    private readonly List<AnnotationText> _texts = new();
    private bool _inTextMode;
    private Button? _addTextBtn;
    private TextBox? _activeTextBox;
    private AnnotationText? _editingText;
    private bool _inTextMultiDropMode;
    private AnnotationText? _selectedText;
    private AnnotationText? _colorPickerText;
    private Rectangle? _textSelectionRect;
    private List<Rectangle> _textResizeHandles = new();
    private bool _draggingTextHandle;
    private int _textHandleDragIndex;
    private Point _textHandleDragStart;
    private double _textHandleDragOrigFontSize;
    private double _textHandleDragOrigDisplayW;
    private double _textHandleDragOrigDisplayH;
    private Rect _textHandleDragOrigBounds;

    // DPI scale of the source image (monitor scale or bitmap-embedded DPI / 96).
    // Used to downscale the output bitmap so it pastes at the correct apparent size.
    private double _effectiveScaleX = 1.0;
    private double _effectiveScaleY = 1.0;
    private AnnotationText? _textHandleDragAnnotation;
    // Set in CommitActiveTextBox when the commit is triggered by a LostFocus (i.e. a canvas click).
    // Canvas_MouseDown reads and clears this flag to prevent immediately deselecting the annotation
    // that was just committed by the same click.
    private bool _suppressNextTextDeselect;
    // Set by Canvas_PreviewMLBD when a click lands on empty canvas while a text annotation is being
    // edited (no annotation hit in the Preview hit-test).  CommitActiveTextBox reads this flag to
    // decide whether to deselect the annotation after committing (empty-canvas click → deselect,
    // so handles disappear) versus keep it selected (Enter / explicit exit → handles stay).
    private bool _pendingTextCommitDeselect;
    // Canvas-level text annotation drag — handles clicks in the selection-rect border zone
    // (the 4–8 px margin around the TextBlock that display.MouseLeftButtonDown misses).
    private bool _canvasTextDragActive;
    private AnnotationText? _canvasTextDragAnnotation;
    private Point _canvasTextDragStart;
    private Rect _canvasTextDragOrigBounds;
    private Color _defaultTextFgColor = Colors.White;
    private Color _defaultTextBgColor = Colors.Black;
    private bool _textDragCreating;
    private Point _textDragStart;
    private Rectangle? _textDragPreview;

    // ── Annotation text-box voice / PTT ───────────────────────────────────────
    private readonly PttTextBoxAttachment _textBoxPttAttachment;

    // ────────────────────────────────────────────────────────────────────────

    internal ClipboardImageEditorWindow(Window owner, BitmapSource clipboardImage, bool isPromptMode = false) {
        _clipboardImage = clipboardImage ?? throw new ArgumentNullException(nameof(clipboardImage));

        _workingImage = clipboardImage;
        _isPromptMode = isPromptMode;
        _themeName = AgentStatusCard.IsDarkTheme ? "dark" : "light";

        Owner = owner;
        Title = "Edit Clipboard Image";
        WindowStyle = WindowStyle.SingleBorderWindow;
        ResizeMode = ResizeMode.CanResizeWithGrip;
        ShowInTaskbar = false;
        WindowStartupLocation = WindowStartupLocation.Manual;
        this.SetResourceReference(BackgroundProperty, "AppSurface");

        _textBoxPttAttachment = new PttTextBoxAttachment(() => new ApplicationSettingsStore().Load(), this, Dispatcher);
        Closed += (_, _) => _textBoxPttAttachment.Dispose();

        LoadArrowDefaults();
        LoadTextDefaults();

        // Resolve owner screen position early so all monitor lookups use the physical
        // top-left of the owner window — reliable even when the window is maximized.
        // PointToScreen returns physical pixel coords; TransformFromDevice converts them
        // to WPF logical units.  We use the physical coords directly for MonitorFromPoint
        // so we never misidentify the monitor (GetWindowRect on a maximized window returns
        // an inflated rect that can straddle the boundary between adjacent monitors).
        var ownerTopLeft = owner.PointToScreen(new Point(0, 0));

        // For MonitorFromPoint we need true physical Win32 screen coordinates.
        // PointToScreen in a System DPI-aware process may return DPI-virtualized
        // coordinates; GetWindowRect always returns physical pixel coordinates.
        var ownerHelper2 = new System.Windows.Interop.WindowInteropHelper(owner);
        IntPtr ownerHwnd = ownerHelper2.EnsureHandle();
        Point physOwnerCenter = ownerTopLeft; // fallback: use PointToScreen result
        if (ownerHwnd != IntPtr.Zero && GetWindowRect(ownerHwnd, out Rect32 ownerWr)) {
            physOwnerCenter = new Point(
                (ownerWr.Left + ownerWr.Right) / 2,
                (ownerWr.Top + ownerWr.Bottom) / 2);
            SquadDashTrace.Write("UI",
                $"[ClipboardImageEditor] ownerHwnd={ownerHwnd:X} ownerWr=({ownerWr.Left},{ownerWr.Top},{ownerWr.Right},{ownerWr.Bottom}) " +
                $"physCenter=({(int)physOwnerCenter.X},{(int)physOwnerCenter.Y}) " +
                $"ptsOwnerTopLeft=({(int)ownerTopLeft.X},{(int)ownerTopLeft.Y})");
        }
        else {
            SquadDashTrace.Write("UI",
                $"[ClipboardImageEditor] GetWindowRect failed hwnd={ownerHwnd:X}, using PointToScreen coords");
        }

        var pSrc = PresentationSource.FromVisual(owner);
        var toLogical = pSrc?.CompositionTarget.TransformFromDevice ?? Matrix.Identity;

        // ── Compute display size ─────────────────────────────────────────────
        // We need to convert physical pixel counts to logical (WPF) display units.
        // Two complementary sources of information:
        //   1. Bitmap DpiX — PrintScreen on a scaled monitor embeds the actual DPI
        //      (e.g. DpiX=144 on a 150% monitor). Use this when available.
        //   2. Current monitor scale — Snipping Tool and many clipboard sources strip
        //      DPI metadata and store 96 regardless. Fall back to the monitor the
        //      owner window is on, which gives the correct result when pasting on
        //      the same monitor as the screenshot was taken.
        var monitorArea = pSrc != null
            ? GetMonitorWorkAreaRect(ownerTopLeft, toLogical)
            : GetMonitorWorkAreaRect(owner);
        double imgW = clipboardImage.PixelWidth;
        double imgH = clipboardImage.PixelHeight;
        double maxWinW = monitorArea.Width * 0.95;
        double maxWinH = monitorArea.Height * 0.95;

        // Use GetDpiForMonitor (shcore.dll) instead of TransformToDevice — this bypasses WPF's
        // DPI virtualization and returns the actual per-monitor scale even when the process is
        // only System DPI aware (which makes TransformToDevice always return 1.0).
        var hMonOwner = MonitorFromPoint(
            new POINT { X = (int)physOwnerCenter.X, Y = (int)physOwnerCenter.Y },
            MONITOR_DEFAULTTONEAREST);
        double rawMonitorScale = GetRawMonitorScale(hMonOwner);
        double monitorScaleX = rawMonitorScale;
        double monitorScaleY = rawMonitorScale;
        double bitmapDpiScaleX = clipboardImage.DpiX / 96.0;
        double bitmapDpiScaleY = clipboardImage.DpiY / 96.0;
        // Prefer bitmap-embedded DPI when it's non-trivial; otherwise use monitor scale.
        double effectiveScaleX = bitmapDpiScaleX > 1.05 ? bitmapDpiScaleX : monitorScaleX;
        double effectiveScaleY = bitmapDpiScaleY > 1.05 ? bitmapDpiScaleY : monitorScaleY;
        _effectiveScaleX = effectiveScaleX;
        _effectiveScaleY = effectiveScaleY;

        // Downscale the working image to logical pixel size so the canvas always operates in
        // logical pixels and the output bitmap is naturally the right size for pasting.
        // 795×660 on a 150% monitor → 530×440 logical.
        if (effectiveScaleX > 1.05 || effectiveScaleY > 1.05) {
            int outW = Math.Max(1, (int)Math.Round(imgW / effectiveScaleX));
            int outH = Math.Max(1, (int)Math.Round(imgH / effectiveScaleY));
            _workingImage = DownscaleHighQuality(clipboardImage, outW, outH);
            imgW = outW;
            imgH = outH;
        }

        double dispW = imgW;
        double dispH = imgH;
        // Canvas coords == logical pixels, scale factor is 1:1.
        _canvasScaleX = 1.0;
        _canvasScaleY = 1.0;

        SquadDashTrace.Write("UI",
            $"[ClipboardImageEditor] DPI: hMonOwner={hMonOwner:X} bitmap={clipboardImage.DpiX:F1}x{clipboardImage.DpiY:F1} " +
            $"rawMonitor={rawMonitorScale:F3} bitmapDpiScale={bitmapDpiScaleX:F3}x{bitmapDpiScaleY:F3} " +
            $"effective={effectiveScaleX:F3}x{effectiveScaleY:F3} " +
            $"pixels={clipboardImage.PixelWidth}x{clipboardImage.PixelHeight} logical={imgW:F0}x{imgH:F0} " +
            $"canvasScale={_canvasScaleX:F3}x{_canvasScaleY:F3} baseZoom=1.0");

        const double MinWindowWidth = 580;
        const double toolbarH = 110.0;

        // Base zoom: show at native pixel size (1:1). Scale down only if needed to fit on screen.
        double baseZoom = 1.0;
        double fitZoomW = (maxWinW - 24) / dispW;
        double fitZoomH = (maxWinH - toolbarH) / dispH;
        _zoom = Math.Min(baseZoom, Math.Min(fitZoomW, fitZoomH));
        _scaleTransform.ScaleX = _zoom;
        _scaleTransform.ScaleY = _zoom;

        // Window size = scaled image + chrome, capped to work area.
        Width = Math.Max(MinWindowWidth, Math.Min(maxWinW, dispW * _zoom + 24));
        Height = Math.Min(maxWinH, dispH * _zoom + toolbarH);
        MinWidth = MinWindowWidth;

        // Center dialog on owner's actual screen position.
        // ownerTopLeft (physical pixels from PointToScreen) and toLogical are resolved above.
        Point waTopLeft = default, waBottomRight = default;
        if (pSrc != null) {
            var logicalOrigin = toLogical.Transform(ownerTopLeft);
            double ownerLogicalW = owner.ActualWidth;
            double ownerLogicalH = owner.ActualHeight;
            Left = logicalOrigin.X + (ownerLogicalW - Width) / 2.0;
            Top = logicalOrigin.Y + (ownerLogicalH - Height) / 2.0;

            // Clamp to monitor work area identified via the physical owner top-left.
            // Using ownerTopLeft (physical pixels from PointToScreen) for MonitorFromPoint
            // ensures we pick the correct monitor even when the owner is maximized, because
            // GetWindowRect on a maximized window returns an inflated rect that can straddle
            // the boundary between adjacent monitors (e.g. secondary above primary at Y<0).
            var work = GetMonitorWorkAreaRect(ownerTopLeft, toLogical);
            waTopLeft = new Point(work.Left, work.Top);
            waBottomRight = new Point(work.Right, work.Bottom);
            Left = Math.Max(waTopLeft.X, Math.Min(Left, waBottomRight.X - Width));
            Top = Math.Max(waTopLeft.Y, Math.Min(Top, waBottomRight.Y - Height));
        }
        else {
            // Fallback: use monitor work area center (pSrc null means window not yet rendered)
            var work = GetMonitorWorkAreaRect(owner);
            Left = work.Left + (work.Width - Width) / 2.0;
            Top = work.Top + (work.Height - Height) / 2.0;
        }

        // Write to SquadDash trace log (visible in the Trace panel) for diagnostics
        SquadDashTrace.Write("UI",
            $"[ClipboardImageEditor] owner.WindowState={owner.WindowState} " +
            $"owner.ActualWidth={owner.ActualWidth:F0} owner.ActualHeight={owner.ActualHeight:F0} " +
            $"ownerTopLeft={ownerTopLeft.X:F0},{ownerTopLeft.Y:F0} " +
            $"workArea=({waTopLeft.X:F0},{waTopLeft.Y:F0},{waBottomRight.X:F0},{waBottomRight.Y:F0}) " +
            $"dialog Left={Left:F0} Top={Top:F0} Width={Width:F0} Height={Height:F0}");

        // ── Canvas ───────────────────────────────────────────────────────────

        _canvas = new Canvas {
            Width = dispW,
            Height = dispH,
            ClipToBounds = true,
            Cursor = Cursors.Arrow,
            Background = Brushes.Transparent,   // must be non-null for canvas to receive mouse events
            UseLayoutRounding = true,
            SnapsToDevicePixels = true
        };

        // Image fills the entire canvas (background layer).
        _imageCtrl = new Image {
            Source = clipboardImage,
            Width = dispW,
            Height = dispH,
            Stretch = Stretch.Fill,
            IsHitTestVisible = false,
            UseLayoutRounding = true,
            SnapsToDevicePixels = true
        };
        RenderOptions.SetBitmapScalingMode(_imageCtrl, BitmapScalingMode.HighQuality);
        _canvas.Children.Add(_imageCtrl);
        Canvas.SetLeft(_imageCtrl, 0);
        Canvas.SetTop(_imageCtrl, 0);

        // No initial crop selection — user drags one if they want to crop.
        // DoInsertImage falls back to the full canvas rect when _sel.IsEmpty.
        _sel = Rect.Empty;

        // ── Dim strips (outside selection) ───────────────────────────────────
        var dimBrush = new SolidColorBrush(Color.FromArgb(128, 0, 0, 0));
        _dimTop = new Rectangle { Fill = dimBrush, IsHitTestVisible = false };
        _dimBottom = new Rectangle { Fill = dimBrush, IsHitTestVisible = false };
        _dimLeft = new Rectangle { Fill = dimBrush, IsHitTestVisible = false };
        _dimRight = new Rectangle { Fill = dimBrush, IsHitTestVisible = false };
        foreach (var d in new[] { _dimTop, _dimBottom, _dimLeft, _dimRight }) {
            Panel.SetZIndex(d, 2);
            _canvas.Children.Add(d);
        }

        // ── Selection border ─────────────────────────────────────────────────

        _selBorderRect = new Rectangle {
            StrokeThickness = 2,
            Fill = Brushes.Transparent,
            IsHitTestVisible = false
        };
        _selBorderRect.SetResourceReference(Shape.StrokeProperty, "DocumentLinkText");
        _canvas.Children.Add(_selBorderRect);
        Panel.SetZIndex(_selBorderRect, 5);

        // ── Resize handles ───────────────────────────────────────────────────

        // Handle order (matches RefreshLayout comment): NW(0) N(1) NE(2) E(3) SE(4) S(5) SW(6) W(7)
        static Cursor HandleResizeCursor(int idx) => idx switch {
            0 or 4 => Cursors.SizeNWSE,   // NW, SE
            2 or 6 => Cursors.SizeNESW,   // NE, SW
            1 or 5 => Cursors.SizeNS,     // N, S
            3 or 7 => Cursors.SizeWE,     // E, W
            _ => Cursors.Arrow
        };

        for (int i = 0; i < 8; i++) {
            var hIdx = i;
            var hCursor = HandleResizeCursor(i);
            var h = new Rectangle {
                Width = HandleSize,
                Height = HandleSize,
                StrokeThickness = 1,
                RadiusX = 1.5,
                RadiusY = 1.5,
                Cursor = hCursor
            };
            h.SetResourceReference(Shape.FillProperty, "DocumentLinkText");
            h.SetResourceReference(Shape.StrokeProperty, "AppSurface");
            // Set the canvas cursor on MouseEnter so it matches regardless of tool mode.
            h.MouseEnter += (_, _) => _canvas.Cursor = hCursor;
            h.MouseLeave += (_, _) => _canvas.Cursor = ActiveToolCursor();
            _handles[i] = h;
            _canvas.Children.Add(h);
            Panel.SetZIndex(h, 10); // above dim strips (2) and sel border (5)
        }

        // ── Dimension readout badges ─────────────────────────────────────────
        // Width badge: shown below the selection, centred on the bottom edge.
        // Height badge: shown to the right of the selection, centred on the right edge.
        var badgeBg = new SolidColorBrush(Color.FromArgb(200, 0, 0, 0));
        _dimWidthLabel = new TextBlock {
            Foreground = Brushes.White,
            FontSize = (double)Application.Current.Resources["FontSizeSmall"],
            FontWeight = FontWeights.SemiBold,
            IsHitTestVisible = false
        };
        _dimWidthBadge = new Border {
            Background = badgeBg,
            CornerRadius = new CornerRadius(3),
            Padding = new Thickness(4, 2, 4, 2),
            Child = _dimWidthLabel,
            IsHitTestVisible = false,
            Visibility = Visibility.Collapsed,
            RenderTransform = _dimWidthBadgeScale,
            RenderTransformOrigin = new Point(0, 0)
        };
        _canvas.Children.Add(_dimWidthBadge);
        Panel.SetZIndex(_dimWidthBadge, 15);

        _dimHeightLabel = new TextBlock {
            Foreground = Brushes.White,
            FontSize = (double)Application.Current.Resources["FontSizeSmall"],
            FontWeight = FontWeights.SemiBold,
            IsHitTestVisible = false
        };
        _dimHeightBadge = new Border {
            Background = badgeBg,
            CornerRadius = new CornerRadius(3),
            Padding = new Thickness(4, 2, 4, 2),
            Child = _dimHeightLabel,
            IsHitTestVisible = false,
            Visibility = Visibility.Collapsed,
            RenderTransform = _dimHeightBadgeScale,
            RenderTransformOrigin = new Point(0, 0)
        };
        _canvas.Children.Add(_dimHeightBadge);
        Panel.SetZIndex(_dimHeightBadge, 15);

        // ── Canvas events ────────────────────────────────────────────────────

        _canvas.PreviewMouseLeftButtonDown += Canvas_PreviewMouseLeftButtonDown;
        _canvas.MouseDown += Canvas_MouseDown;
        _canvas.MouseMove += Canvas_MouseMove;
        _canvas.MouseUp += Canvas_MouseUp;

        // Reset to standard arrow when mouse leaves the canvas area (toolbar, buttons, etc.
        // should always show the normal cursor), and restore tool cursor on re-entry.
        _canvas.MouseLeave += (_, _) => Cursor = Cursors.Arrow;
        _canvas.MouseEnter += (_, _) => Cursor = ActiveToolCursor();
        KeyDown += Window_KeyDown;

        PreviewKeyDown += (_, e) => {
            // Double-Ctrl voice dictation when an annotation text box has focus.
            if (_activeTextBox is not null && _activeTextBox.IsKeyboardFocusWithin) {
                if (_textBoxPttAttachment.HandlePreviewKeyDown(e, _activeTextBox)) {
                    e.Handled = true;
                    return;
                }
            }

            if (e.Key == Key.Enter && Keyboard.FocusedElement is not TextBox) {
                e.Handled = true;
                if (!_sel.IsEmpty)
                    DoCropInPlace();
                else
                    DoInsertImage();
                return;
            }
            if (e.Key == Key.Space && !_isPanMode) {
                if (Keyboard.FocusedElement is TextBox) return;
                _isPanMode = true;
                _canvas.Cursor = AnnotationCursors.OpenHand;
                _canvas.ForceCursor = true;   // prevent child elements (shapes) from overriding cursor
                _scrollViewer.Cursor = AnnotationCursors.OpenHand;
                e.Handled = true;
            }
        };

        PreviewKeyUp += (_, e) => {
            if (_activeTextBox is not null && _activeTextBox.IsKeyboardFocusWithin) {
                if (_textBoxPttAttachment.HandlePreviewKeyUp(e)) {
                    e.Handled = true;
                    return;
                }
            }

            if (e.Key == Key.Space && _isPanMode) {
                _isPanMode = false;
                _canvas.ForceCursor = false;
                if (_isPanning) {
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
        PreviewMouseWheel += (_, e) => {
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
                (_scrollViewer.ScrollableWidth > 0 || _scrollViewer.ScrollableHeight > 0)) {
                double imagePointX = (_scrollViewer.HorizontalOffset + mouseInViewport.X) / oldZoom;
                double imagePointY = (_scrollViewer.VerticalOffset + mouseInViewport.Y) / oldZoom;
                _scrollViewer.ScrollToHorizontalOffset(imagePointX * _zoom - mouseInViewport.X);
                _scrollViewer.ScrollToVerticalOffset(imagePointY * _zoom - mouseInViewport.Y);
            }

            e.Handled = true;
        };

        // ── Root layout: scrollable canvas on top, toolbar docked at the bottom
        var canvasWrapper = new Border {
            Child = _canvas,
            LayoutTransform = _scaleTransform,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(4)
        };

        _overlayCanvas = new Canvas {
            IsHitTestVisible = false,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Stretch
        };

        var canvasGrid = new Grid();
        canvasGrid.Children.Add(canvasWrapper);
        canvasGrid.Children.Add(_overlayCanvas);

        _scrollViewer = new ScrollViewer {
            HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            Content = canvasGrid
        };
        _scrollViewer.SetResourceReference(BackgroundProperty, "AppSurface");

        _scrollViewer.PreviewMouseLeftButtonDown += (_, e) => {
            if (!_isPanMode) return;
            _isPanning = true;
            _panStartMouse = e.GetPosition(_scrollViewer);
            _panStartH = _scrollViewer.HorizontalOffset;
            _panStartV = _scrollViewer.VerticalOffset;
            _scrollViewer.CaptureMouse();
            _canvas.Cursor = AnnotationCursors.ClosedHand;
            _scrollViewer.Cursor = AnnotationCursors.ClosedHand;
            e.Handled = true;
        };

        _scrollViewer.PreviewMouseMove += (_, e) => {
            if (!_isPanning) return;
            var pos = e.GetPosition(_scrollViewer);
            double dx = pos.X - _panStartMouse.X;
            double dy = pos.Y - _panStartMouse.Y;
            _scrollViewer.ScrollToHorizontalOffset(_panStartH - dx);
            _scrollViewer.ScrollToVerticalOffset(_panStartV - dy);
            e.Handled = true;
        };

        _scrollViewer.PreviewMouseLeftButtonUp += (_, e) => {
            if (!_isPanning) return;
            _isPanning = false;
            _scrollViewer.ReleaseMouseCapture();
            _canvas.Cursor = _isPanMode ? AnnotationCursors.OpenHand : Cursors.Arrow;
            _scrollViewer.Cursor = _isPanMode ? AnnotationCursors.OpenHand : null;
            e.Handled = true;
        };

        var toolbar = BuildToolbar();
        var root = new DockPanel { LastChildFill = true };
        DockPanel.SetDock(toolbar, Dock.Bottom);
        root.Children.Add(toolbar);
        root.Children.Add(_scrollViewer);
        Content = root;

        Loaded += (_, _) => {
            RefreshLayout();
            UpdateWindowSizeForZoom(); // center on first paint
            EnterCropMode();
        };
    }

    // ── Toolbar ───────────────────────────────────────────────────────────────

    private Border BuildToolbar() {
        _moveSelectBtn = new Button {
            Content = MakeToolIcon("ImageEditorMoveIcon"),
            Width = 32,
            Height = 28,
            Padding = new Thickness(4, 3, 4, 3),
            Margin = new Thickness(0, 0, 4, 0),
            ToolTip = "Move/Select (M or V) — select and move existing annotations"
        };
        _cropBtn = new Button {
            Content = MakeToolIcon("ImageEditorCropIcon"),
            Width = 32,
            Height = 28,
            Padding = new Thickness(4, 3, 4, 3),
            Margin = new Thickness(0, 0, 4, 0),
            ToolTip = "Crop (C) — drag to define a crop region, Enter or double-click to apply"
        };
        _addArrowBtn = new Button {
            Content = MakeToolIcon("ImageEditorArrowIcon"),
            Width = 32,
            Height = 28,
            Padding = new Thickness(4, 3, 4, 3),
            Margin = new Thickness(0, 0, 4, 0),
            ToolTip = "Arrow (drag to draw) · Shift+click for multi-drop"
        };
        _addRectBtn = new Button {
            Content = MakeToolIcon("ImageEditorRectIcon"),
            Width = 32,
            Height = 28,
            Padding = new Thickness(4, 3, 4, 3),
            Margin = new Thickness(0, 0, 4, 0),
            ToolTip = "Rectangle annotation (drag to draw) · Shift+click for multi-drop"
        };
        _addTextBtn = new Button {
            Content = MakeToolIcon("ImageEditorTextIcon"),
            Width = 32,
            Height = 28,
            Padding = new Thickness(4, 3, 4, 3),
            Margin = new Thickness(0, 0, 4, 0),
            ToolTip = "Text label \u00B7 click to place \u00B7 Shift+click for multi-drop \u00B7 double-click to re-edit"
        };
        var cursorBtn = new Button {
            Content = MakeToolIcon("ImageEditorCursorIcon"),
            Width = 32,
            Height = 28,
            Padding = new Thickness(4, 3, 4, 3),
            Margin = new Thickness(0, 0, 4, 0),
            ToolTip = "Add a mouse-cursor indicator"
        };
        _eyedropperBtn = new Button {
            Content = MakeToolIcon("ImageEditorEyedropperIcon"),
            Width = 32,
            Height = 28,
            Padding = new Thickness(4, 3, 4, 3),
            Margin = new Thickness(0, 0, 4, 0),
            ToolTip = "Pick a color from the image"
        };
        var roundCornersBtn = new Button {
            Content = MakeToolIcon("ImageEditorRoundCornersIcon"),
            Width = 32,
            Height = 28,
            Padding = new Thickness(4, 3, 4, 3),
            Margin = new Thickness(0, 0, 4, 0),
            ToolTip = $"Mask the {CornerRadiusPx}px corners transparent in the output PNG"
        };

        string insertLabel = _isPromptMode ? "Attach Image" : "Insert Image";
        string insertTooltip = _isPromptMode
            ? "Attach this image to the prompt"
            : "Insert this image into the documentation";
        var insertBtn = new Button {
            Content = insertLabel,
            Width = _isPromptMode ? 100 : 96,
            Height = 28,
            Margin = new Thickness(0, 0, 4, 0),
            ToolTip = insertTooltip
        };
        var cancelBtn = new Button { Content = "Cancel", Width = 70, Height = 28 };

        _eyedropperSwatch = new Border {
            Width = 20,
            Height = 20,
            CornerRadius = new CornerRadius(3),
            Background = Brushes.Transparent,
            BorderBrush = new SolidColorBrush(Color.FromRgb(160, 160, 160)),
            BorderThickness = new Thickness(1),
            Margin = new Thickness(0, 0, 4, 0),
            VerticalAlignment = VerticalAlignment.Center,
            Cursor = Cursors.Hand,
            ToolTip = "Copy",
            Visibility = Visibility.Collapsed
        };
        _eyedropperSwatch.MouseLeftButtonUp += (_, _) => {
            if (_eyedropperHexLabel != null && !string.IsNullOrEmpty(_eyedropperHexLabel.Text))
                Clipboard.SetText(_eyedropperHexLabel.Text);
        };
        _eyedropperHexLabel = new TextBlock {
            Text = string.Empty,
            VerticalAlignment = VerticalAlignment.Center,
            FontFamily = new FontFamily("Consolas"),
            FontSize = (double)Application.Current.Resources["FontSizeNormal"],
            Margin = new Thickness(0, 0, 8, 0),
            Cursor = Cursors.Hand,
            ToolTip = "Copy"
        };
        _eyedropperHexLabel.MouseLeftButtonUp += (_, _) => {
            if (!string.IsNullOrEmpty(_eyedropperHexLabel.Text))
                Clipboard.SetText(_eyedropperHexLabel.Text);
        };
        _eyedropperHexLabel.SetResourceReference(TextBlock.ForegroundProperty, "LabelText");
        _eyedropperHexLabel.ContextMenu = new ContextMenu();
        var copyHexItem = new MenuItem { Header = "Copy" };
        copyHexItem.Click += (_, _) => {
            if (!string.IsNullOrEmpty(_eyedropperHexLabel.Text))
                Clipboard.SetText(_eyedropperHexLabel.Text);
        };
        _eyedropperHexLabel.ContextMenu.Items.Add(copyHexItem);

        // In prompt mode the Round Corners option is not relevant — hide it.
        if (_isPromptMode)
            roundCornersBtn.Visibility = Visibility.Collapsed;

        var styleButtons = _isPromptMode
            ? new[] { _moveSelectBtn, _cropBtn, _addArrowBtn, _addRectBtn, _addTextBtn, cursorBtn, _eyedropperBtn, insertBtn, cancelBtn }
            : new[] { _moveSelectBtn, _cropBtn, _addArrowBtn, _addRectBtn, _addTextBtn, cursorBtn, _eyedropperBtn, roundCornersBtn, insertBtn, cancelBtn };
        foreach (var btn in styleButtons)
            btn.SetResourceReference(Control.StyleProperty, "ThemedButtonStyle");

        _moveSelectBtn.Click += (_, _) => {
            ExitAllToolModes();
            EnterMoveMode();
        };

        _cropBtn.Click += (_, _) => {
            ExitAllToolModes();
            EnterCropMode();
        };

        _addArrowBtn.Click += (_, _) => {
            bool isShift = (Keyboard.Modifiers & ModifierKeys.Shift) != 0;
            if (_inArrowMode && !isShift) { ExitArrowMode(returnToMove: true); return; }
            ExitAllToolModes();
            EnterArrowMode();
            if (isShift) {
                _inArrowMultiDropMode = true;
                ShowModeHint("Multi-drop: drag to place arrows · ESC to exit");
            }
            _addArrowBtn.Content = MakeToolIcon("ImageEditorArrowIcon", active: true, multiDrop: isShift);
        };

        _addRectBtn.Click += (_, _) => {
            bool isShift = (Keyboard.Modifiers & ModifierKeys.Shift) != 0;
            if (_inRectMode && !isShift) { ExitRectMode(returnToMove: true); return; }
            ExitAllToolModes();
            EnterRectMode();
            if (isShift) {
                _inRectMultiDropMode = true;
                ShowModeHint("Multi-drop: drag to place rectangles · ESC to exit");
            }
            _addRectBtn.Content = MakeToolIcon("ImageEditorRectIcon", active: true, multiDrop: isShift);
        };

        _addTextBtn.Click += (_, _) => {
            bool isShift = (Keyboard.Modifiers & ModifierKeys.Shift) != 0;
            if (_inTextMode && !isShift) { ExitTextMode(returnToMove: true); return; }
            ExitAllToolModes();
            EnterTextMode();
            if (isShift) {
                _inTextMultiDropMode = true;
                ShowModeHint("Multi-drop: click to place text · ESC to exit");
            }
            _addTextBtn.Content = MakeToolIcon("ImageEditorTextIcon", active: true, multiDrop: isShift);
        };

        cursorBtn.Click += (_, _) => {
            // Bug fix: if cursor was already placed (_cursorEnabled true but placement mode
            // exited), re-enter placement mode instead of toggling off.
            if (_cursorEnabled && !_inCursorPlacementMode) {
                ExitAllToolModes();
                _inMoveMode = false;
                _inCropMode = false;
                _cursorEnabled = true;
                _inCursorPlacementMode = true;
                cursorBtn.Content = MakeToolIcon("ImageEditorCursorIcon", active: true);
                _canvas.Cursor = AnnotationCursors.DropCursorTool;
                ShowModeHint("Click to place the cursor indicator");
                return;
            }
            ExitAllToolModes();
            _cursorEnabled = !_cursorEnabled;
            if (_cursorEnabled) {
                _inMoveMode = false;
                _inCropMode = false;
                _inCursorPlacementMode = true;
                cursorBtn.Content = MakeToolIcon("ImageEditorCursorIcon", active: true);
                _canvas.Cursor = AnnotationCursors.DropCursorTool;
                ShowModeHint("Click to place the cursor indicator");
            }
            else {
                _inCursorPlacementMode = false;
                cursorBtn.Content = MakeToolIcon("ImageEditorCursorIcon");
                _canvas.Cursor = Cursors.Arrow;
                ToggleCursorOverlay(false);
                HideModeHint();
                EnterMoveMode();
            }
        };

        _eyedropperBtn.Click += (_, _) => {
            if (_inEyedropperMode) { ExitEyedropperMode(); EnterMoveMode(); return; }
            ExitAllToolModes();
            EnterEyedropperMode();
            _eyedropperBtn.Content = MakeToolIcon("ImageEditorEyedropperIcon", active: true);
        };

        roundCornersBtn.Click += (_, _) => {
            _roundCorners = !_roundCorners;
            roundCornersBtn.Content = _roundCorners ? "✓ Round Corners" : "⌐ Round Corners";
        };

        insertBtn.Click += (_, _) => DoInsertImage();
        cancelBtn.Click += (_, _) => Close();

        string copyTooltip = _sel.IsEmpty
            ? "Copy annotated image to clipboard"
            : "Copy cropped region to clipboard";
        var copyBtn = new Button {
            Content = "Copy",
            Width = 60,
            Height = 28,
            Margin = new Thickness(0, 0, 4, 0),
            ToolTip = copyTooltip
        };
        copyBtn.SetResourceReference(Control.StyleProperty, "ThemedButtonStyle");
        copyBtn.Click += (_, _) => DoCopyToClipboard();

        _zoomLabel = new TextBlock {
            Text = $"{_zoom * 100:F0}%",
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(8, 0, 4, 0),
            FontSize = (double)Application.Current.Resources["FontSizeSmall"]
        };
        _zoomLabel.SetResourceReference(TextBlock.ForegroundProperty, "LabelText");

        var resetZoomBtn = new Button { Content = "1:1", Width = 36, Height = 28, Margin = new Thickness(0, 0, 4, 0), ToolTip = "Reset zoom to 100% (Ctrl+0)" };
        resetZoomBtn.SetResourceReference(Control.StyleProperty, "ThemedButtonStyle");
        resetZoomBtn.Click += (_, _) => { _zoom = 1.0; _scaleTransform.ScaleX = 1.0; _scaleTransform.ScaleY = 1.0; _zoomLabel.Text = "100%"; UpdateWindowSizeForZoom(); };

        // Update label whenever zoom changes via keyboard shortcut
        KeyDown += (_, e) => {
            if (e.Key == Key.D0 && (Keyboard.Modifiers & ModifierKeys.Control) != 0) {
                _zoom = 1.0; _scaleTransform.ScaleX = 1.0; _scaleTransform.ScaleY = 1.0;
                _zoomLabel.Text = "100%";
                UpdateWindowSizeForZoom();
                e.Handled = true;
            }
        };

        // ── Toolbar layout: DockPanel so action buttons are always at the far right ─────
        // Right-docked sub-panel (must be added FIRST — DockPanel rule)
        var rightStack = new StackPanel {
            Orientation = Orientation.Horizontal,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(4, 0, 0, 0)
        };
        rightStack.Children.Add(copyBtn);
        rightStack.Children.Add(insertBtn);
        rightStack.Children.Add(cancelBtn);
        DockPanel.SetDock(rightStack, Dock.Right);

        // Tool buttons + aux controls fill the remaining left space
        var leftStack = new StackPanel {
            Orientation = Orientation.Horizontal,
            VerticalAlignment = VerticalAlignment.Center
        };
        leftStack.Children.Add(_moveSelectBtn);
        leftStack.Children.Add(_cropBtn);
        leftStack.Children.Add(_addArrowBtn);
        leftStack.Children.Add(_addRectBtn);
        leftStack.Children.Add(_addTextBtn);
        leftStack.Children.Add(cursorBtn);
        leftStack.Children.Add(_eyedropperBtn);
        leftStack.Children.Add(_eyedropperSwatch);
        leftStack.Children.Add(_eyedropperHexLabel);
        leftStack.Children.Add(roundCornersBtn);
        leftStack.Children.Add(_zoomLabel);
        leftStack.Children.Add(resetZoomBtn);

        var row = new DockPanel {
            LastChildFill = true,
            Margin = new Thickness(8, 6, 8, 6)
        };
        row.Children.Add(rightStack);  // right-docked must come before fill child
        row.Children.Add(leftStack);

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
    private UIElement MakeToolIcon(string resourceKey, bool active = false, bool multiDrop = false) {
        var resource = TryFindResource(resourceKey);
        if (resource == null)
            return new TextBlock { Text = resourceKey };

        // Clone the resource element — TryFindResource returns the same singleton instance
        // every time, which WPF rejects if already parented to another element.
        UIElement icon;
        try {
            var xaml = System.Windows.Markup.XamlWriter.Save(resource);
            icon = (UIElement)System.Windows.Markup.XamlReader.Parse(xaml);
        }
        catch {
            // Fallback: if serialization fails (e.g. dynamic resources), return a placeholder.
            return new TextBlock { Text = resourceKey };
        }

        if (!active) return icon;

        // Active state: icon + rounded accent underline at the bottom, matching the
        // document-chip selection indicator style used in agent cards.
        // multiDrop adds a slightly narrower bar (with bigger margin) to distinguish
        // multi-drop mode from single-drop activation.
        var accent = new Border {
            Height = 2,
            VerticalAlignment = VerticalAlignment.Bottom,
            CornerRadius = new CornerRadius(1.5),
            Margin = multiDrop ? new Thickness(6, 0, 6, 0) : new Thickness(2, 0, 2, 0)
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
    [System.Runtime.InteropServices.DllImport("shcore.dll")]
    private static extern int GetDpiForMonitor(IntPtr hmonitor, uint dpiType, out uint dpiX, out uint dpiY);
    private const uint MDT_EFFECTIVE_DPI = 0;
    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern IntPtr SetThreadDpiAwarenessContext(IntPtr dpiContext);
    // Per-Monitor DPI Aware V2 — required so GetDpiForMonitor returns the true per-monitor
    // DPI rather than the system DPI (which it returns for System-DPI-aware threads).
    private static readonly IntPtr DPI_AWARENESS_CONTEXT_PER_MONITOR_AWARE_V2 = new IntPtr(-4);

    [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
    private struct Rect32 { public int Left, Top, Right, Bottom; }
    [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
    private struct POINT { public int X, Y; }
    [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
    private struct MonitorInfo {
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
    private static Rect GetMonitorWorkAreaRect(Window w) {
        try {
            var helper = new System.Windows.Interop.WindowInteropHelper(w);
            IntPtr hwnd = helper.Handle;

            // Strategy 1: GetWindowRect gives actual pixel bounds even when maximized,
            // unlike WPF Left/Top which reflects restore bounds. MonitorFromPoint on the
            // center is more reliable than MonitorFromWindow when HWND may be transitional.
            if (hwnd != IntPtr.Zero && GetWindowRect(hwnd, out Rect32 wr)) {
                int cx = (wr.Left + wr.Right) / 2;
                int cy = (wr.Top + wr.Bottom) / 2;
                var hMon = MonitorFromPoint(new POINT { X = cx, Y = cy }, MONITOR_DEFAULTTONEAREST);
                if (hMon != IntPtr.Zero) {
                    var mi = new MonitorInfo { cbSize = (uint)System.Runtime.InteropServices.Marshal.SizeOf<MonitorInfo>() };
                    if (GetMonitorInfo(hMon, ref mi)) {
                        // Use GetDpiForMonitor for accurate scale — TransformToDevice returns 1.0 for System DPI aware apps.
                        double s = GetRawMonitorScale(hMon);
                        return new Rect(
                            mi.rcWork.Left / s,
                            mi.rcWork.Top / s,
                            (mi.rcWork.Right - mi.rcWork.Left) / s,
                            (mi.rcWork.Bottom - mi.rcWork.Top) / s);
                    }
                }
            }

            // Strategy 2: HWND not yet available — estimate screen center from WPF logical coords.
            // WPF Left/Top are in logical units; for System DPI aware apps these are already at
            // scale 1.0, so we use physical coords from GetWindowRect center when available.
            // Fallback: use the primary monitor scale as an approximation.
            double fallbackScale = 1.0;
            if (hwnd != IntPtr.Zero) {
                var hMonFallback = MonitorFromPoint(new POINT { X = (int)w.Left, Y = (int)w.Top }, MONITOR_DEFAULTTONEAREST);
                fallbackScale = GetRawMonitorScale(hMonFallback);
            }
            int wpfCx = (int)((w.Left + w.Width / 2) * fallbackScale);
            int wpfCy = (int)((w.Top + w.Height / 2) * fallbackScale);
            var hMon2 = MonitorFromPoint(new POINT { X = wpfCx, Y = wpfCy }, MONITOR_DEFAULTTONEAREST);
            if (hMon2 != IntPtr.Zero) {
                double s2 = GetRawMonitorScale(hMon2);
                var mi2 = new MonitorInfo { cbSize = (uint)System.Runtime.InteropServices.Marshal.SizeOf<MonitorInfo>() };
                if (GetMonitorInfo(hMon2, ref mi2)) {
                    return new Rect(
                        mi2.rcWork.Left / s2,
                        mi2.rcWork.Top / s2,
                        (mi2.rcWork.Right - mi2.rcWork.Left) / s2,
                        (mi2.rcWork.Bottom - mi2.rcWork.Top) / s2);
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
    private static Rect GetMonitorWorkAreaRect(Point physPt, Matrix transformFromDevice) {
        try {
            var hMon = MonitorFromPoint(new POINT { X = (int)physPt.X, Y = (int)physPt.Y }, MONITOR_DEFAULTTONEAREST);
            if (hMon != IntPtr.Zero) {
                var mi = new MonitorInfo { cbSize = (uint)System.Runtime.InteropServices.Marshal.SizeOf<MonitorInfo>() };
                if (GetMonitorInfo(hMon, ref mi)) {
                    var tl = transformFromDevice.Transform(new Point(mi.rcWork.Left, mi.rcWork.Top));
                    var br = transformFromDevice.Transform(new Point(mi.rcWork.Right, mi.rcWork.Bottom));
                    return new Rect(tl, br);
                }
            }
        }
        catch { }
        return SystemParameters.WorkArea;
    }

    /// <summary>
    /// Returns the raw effective scale factor for the monitor identified by <paramref name="hMon"/>
    /// using <c>GetDpiForMonitor(MDT_EFFECTIVE_DPI)</c> called from a Per-Monitor DPI Aware V2
    /// thread context. This is required because calling GetDpiForMonitor from a System-DPI-aware
    /// thread returns the system DPI (96) for all monitors, not the true per-monitor DPI.
    /// Returns 1.0 on failure.
    /// </summary>
    private static double GetRawMonitorScale(IntPtr hMon) {
        if (hMon == IntPtr.Zero) return 1.0;
        // Temporarily switch thread DPI awareness to Per-Monitor V2 so GetDpiForMonitor
        // returns the real per-monitor DPI instead of the system DPI.
        var prevCtx = SetThreadDpiAwarenessContext(DPI_AWARENESS_CONTEXT_PER_MONITOR_AWARE_V2);
        try {
            int hr = GetDpiForMonitor(hMon, MDT_EFFECTIVE_DPI, out uint dpiX, out _);
            SquadDashTrace.Write("UI", $"[GetRawMonitorScale] hMon={hMon:X} hr={hr:X} dpiX={dpiX}");
            if (hr == 0)
                return dpiX / 96.0;
        }
        catch (Exception ex) {
            SquadDashTrace.Write("UI", $"[GetRawMonitorScale] exception: {ex.GetType().Name}: {ex.Message}");
        }
        finally {
            if (prevCtx != IntPtr.Zero)
                SetThreadDpiAwarenessContext(prevCtx);
        }
        return 1.0;
    }

    /// <summary>
    /// Downscales a BitmapSource using GDI+ HighQualityBicubic interpolation, which
    /// produces significantly sharper results than WPF's TransformedBitmap (bilinear).
    /// </summary>
    private static BitmapSource DownscaleHighQuality(BitmapSource source, int outW, int outH) {
        // Convert BitmapSource → System.Drawing.Bitmap in Bgra32
        var fmt = System.Drawing.Imaging.PixelFormat.Format32bppPArgb;
        int srcStride = source.PixelWidth * 4;
        var srcPixels = new byte[srcStride * source.PixelHeight];
        source.CopyPixels(srcPixels, srcStride, 0);

        using var srcBmp = new System.Drawing.Bitmap(source.PixelWidth, source.PixelHeight, fmt);
        {
            var bd = srcBmp.LockBits(
                new System.Drawing.Rectangle(0, 0, srcBmp.Width, srcBmp.Height),
                System.Drawing.Imaging.ImageLockMode.WriteOnly, fmt);
            System.Runtime.InteropServices.Marshal.Copy(srcPixels, 0, bd.Scan0, srcPixels.Length);
            srcBmp.UnlockBits(bd);
        }

        using var dstBmp = new System.Drawing.Bitmap(outW, outH, fmt);
        using (var g = System.Drawing.Graphics.FromImage(dstBmp)) {
            g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
            g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.HighQuality;
            g.PixelOffsetMode = System.Drawing.Drawing2D.PixelOffsetMode.HighQuality;
            g.CompositingQuality = System.Drawing.Drawing2D.CompositingQuality.HighQuality;
            g.DrawImage(srcBmp, 0, 0, outW, outH);
        }

        // Convert System.Drawing.Bitmap back to BitmapSource
        var dstStride = outW * 4;
        var dstPixels = new byte[dstStride * outH];
        {
            var bd = dstBmp.LockBits(
                new System.Drawing.Rectangle(0, 0, outW, outH),
                System.Drawing.Imaging.ImageLockMode.ReadOnly, fmt);
            System.Runtime.InteropServices.Marshal.Copy(bd.Scan0, dstPixels, 0, dstPixels.Length);
            dstBmp.UnlockBits(bd);
        }

        var result = BitmapSource.Create(outW, outH, 96, 96,
            PixelFormats.Pbgra32, null, dstPixels, dstStride);
        result.Freeze();
        return result;
    }

    /// <summary>
    /// Resizes the window to fit the scaled image within the current monitor's work area.
    /// Preserves the current window center so zooming never jumps the window to a different
    /// monitor (important for monitors with negative screen coordinates).
    /// </summary>
    private void UpdateWindowSizeForZoom() {
        var work = GetMonitorWorkAreaRect(this);
        const double toolbarH = 110.0;
        const double minW = 580.0;

        double scaledImgW = _canvas.Width * _zoom;
        double scaledImgH = _canvas.Height * _zoom;
        double desiredW = Math.Max(minW, scaledImgW + 24);
        double desiredH = scaledImgH + toolbarH;

        double newW = Math.Min(work.Width, desiredW);
        double newH = Math.Min(work.Height, desiredH);

        // Keep the current window center fixed — don't re-centre on the monitor.
        // This prevents the window from jumping to a different monitor when the work-area
        // DIP conversion is imprecise (e.g. monitors with negative screen coordinates).
        double newLeft = Left + (Width - newW) / 2.0;
        double newTop = Top + (Height - newH) / 2.0;

        // Clamp to stay fully within the current monitor's work area.
        if (newLeft < work.Left) newLeft = work.Left;
        if (newTop < work.Top) newTop = work.Top;
        if (newLeft + newW > work.Right) newLeft = work.Right - newW;
        if (newTop + newH > work.Bottom) newTop = work.Bottom - newH;

        Width = newW;
        Height = newH;
        Left = newLeft;
        Top = newTop;
    }

    /// <summary>
    /// Repositions the selection border, resize handles, and mode-hint overlay to
    /// reflect the current <see cref="_sel"/> value.
    /// </summary>
    private void RefreshLayout() {
        // When no crop region exists hide all crop chrome so the full image is visible.
        if (_sel.IsEmpty) {
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
        var handleScreenSize = HandleSize / _zoom;
        foreach (var hdl in _handles) {
            hdl.Width = handleScreenSize;
            hdl.Height = handleScreenSize;
        }
        var hh = handleScreenSize / 2;
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
        _dimWidthBadge.Visibility = Visibility.Visible;
        _dimHeightBadge.Visibility = Visibility.Visible;

        // Scale badges inversely to _zoom so they stay at constant screen size.
        double invZ = 1.0 / _zoom;
        _dimWidthBadgeScale.ScaleX = invZ;
        _dimWidthBadgeScale.ScaleY = invZ;
        _dimHeightBadgeScale.ScaleX = invZ;
        _dimHeightBadgeScale.ScaleY = invZ;

        // Force measure so DesiredSize is accurate for positioning.
        _dimWidthBadge.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        _dimHeightBadge.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));

        // After the inverse RenderTransform the badge occupies DesiredSize/zoom canvas
        // logical units, so scale down the footprint used for collision/positioning.
        var bwW = _dimWidthBadge.DesiredSize.Width * invZ;
        var bwH = _dimWidthBadge.DesiredSize.Height * invZ;
        var bhW = _dimHeightBadge.DesiredSize.Width * invZ;
        var bhH = _dimHeightBadge.DesiredSize.Height * invZ;

        // Gap stays at a constant 5 screen-pixels — convert to canvas logical units.
        double BadgeGap = 5.0 * invZ;
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

    private void PlaceHandle(int i, double left, double top) {
        Canvas.SetLeft(_handles[i], left);
        Canvas.SetTop(_handles[i], top);
    }

    // ── Hit testing ───────────────────────────────────────────────────────────

    private HitZone HitTest(Point pt) {
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

        double edgeBand = 8.0 / _zoom;
        if (pt.Y >= s.Top - edgeBand && pt.Y <= s.Top + edgeBand && pt.X > s.Left && pt.X < s.Right) return HitZone.N;
        if (pt.Y >= s.Bottom - edgeBand && pt.Y <= s.Bottom + edgeBand && pt.X > s.Left && pt.X < s.Right) return HitZone.S;
        if (pt.X >= s.Left - edgeBand && pt.X <= s.Left + edgeBand && pt.Y > s.Top && pt.Y < s.Bottom) return HitZone.W;
        if (pt.X >= s.Right - edgeBand && pt.X <= s.Right + edgeBand && pt.Y > s.Top && pt.Y < s.Bottom) return HitZone.E;

        if (s.Contains(pt)) return HitZone.Move;
        return HitZone.None;
    }

    private bool InHandleZone(Point pt, double cx, double cy) {
        var r = (HandleSize / 2 + HitPad) / _zoom;
        return pt.X >= cx - r && pt.X <= cx + r &&
               pt.Y >= cy - r && pt.Y <= cy + r;
    }

    private static Cursor ZoneCursor(HitZone zone) => zone switch {
        HitZone.NW or HitZone.SE => Cursors.SizeNWSE,
        HitZone.NE or HitZone.SW => Cursors.SizeNESW,
        HitZone.N or HitZone.S => Cursors.SizeNS,
        HitZone.E or HitZone.W => Cursors.SizeWE,
        HitZone.Move => Cursors.SizeAll,
        _ => Cursors.Arrow
    };

    /// <summary>Returns the cursor that should be shown while the mouse is over the canvas.</summary>
    private Cursor ActiveToolCursor() {
        if (_isPanMode) return AnnotationCursors.OpenHand;
        if (_inEyedropperMode) return AnnotationCursors.EyedropperTool;
        if (_inArrowMode) return AnnotationCursors.ArrowTool;
        if (_inRectMode) return AnnotationCursors.RectTool;
        if (_inTextMode) return AnnotationCursors.TextTool;
        if (_inCursorPlacementMode) return AnnotationCursors.DropCursorTool;
        if (_inCropMode) return AnnotationCursors.CropTool;
        if (_inMoveMode) return Cursors.Arrow;
        return _canvas.Cursor ?? Cursors.Arrow;
    }

    // ── Resize math ───────────────────────────────────────────────────────────

    [Flags]
    private enum Edge { Left = 1, Top = 2, Right = 4, Bottom = 8 }

    private static Rect ApplyEdges(Rect orig, double dx, double dy, Edge edges, double maxW, double maxH) {
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

    private static Rect ClampRect(Rect rect, double maxW, double maxH) {
        var l = Math.Max(0, Math.Min(rect.Left, maxW - rect.Width));
        var t = Math.Max(0, Math.Min(rect.Top, maxH - rect.Height));
        return new Rect(l, t,
            Math.Min(rect.Width, maxW),
            Math.Min(rect.Height, maxH));
    }

    // ── Mouse ─────────────────────────────────────────────────────────────────

    // Tunneling PreviewMouseLeftButtonDown fires BEFORE any child element's MouseLeftButtonDown,
    // even if those children set e.Handled = true.  We use this to intercept text annotation
    // body drags before child handlers can interfere — the same pattern the resize handles use.
    private void Canvas_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e) {
        // When actively editing a text annotation and clicking canvas background, flag for deselect
        // BEFORE LostFocus fires. The early-return guard below would skip this otherwise.
        if (_inTextMode && e.ClickCount == 1 && (_activeTextBox != null || _editingText != null))
            _pendingTextCommitDeselect = true;

        if (e.ClickCount != 1 || _inTextMode || _inArrowMode || _inRectMode
            || _inCursorPlacementMode || _inEyedropperMode || _suppressNextTextDeselect)
            return;

        var pt = e.GetPosition(_canvas);
        SquadDashTrace.Write("AnnotatorDrag", $"Canvas_PreviewMLBD: pt=({pt.X:F0},{pt.Y:F0}) selectedText={_selectedText != null} texts={_texts.Count}");

        // Search ALL text annotations (selected or not) for a hit in the extended bounds zone.
        // This allows a single click to both select and drag — the same UX as clicking on the TextBlock.
        foreach (var ann in _texts) {
            // Determine the on-screen position: prefer Display canvas position over Bounds
            // because Bounds.(Width/Height) may be 0 for auto-sized annotations.
            double left, top, dispW, dispH;
            if (ann.Display != null) {
                left = Canvas.GetLeft(ann.Display);
                top = Canvas.GetTop(ann.Display);
                dispW = ann.Display.ActualWidth > 0 ? ann.Display.ActualWidth : ann.Display.DesiredSize.Width;
                dispH = ann.Display.ActualHeight > 0 ? ann.Display.ActualHeight : ann.Display.DesiredSize.Height;
            }
            else if (ann == _editingText && _activeTextBox != null) {
                // Still being edited — use the live TextBox for accurate hit bounds.
                left  = Canvas.GetLeft(_activeTextBox);
                top   = Canvas.GetTop(_activeTextBox);
                dispW = _activeTextBox.ActualWidth  > 0 ? _activeTextBox.ActualWidth  : (_activeTextBox.DesiredSize.Width  > 0 ? _activeTextBox.DesiredSize.Width  : ann.Bounds.Width);
                dispH = _activeTextBox.ActualHeight > 0 ? _activeTextBox.ActualHeight : (_activeTextBox.DesiredSize.Height > 0 ? _activeTextBox.DesiredSize.Height : 20);
            }
            else if (!ann.Bounds.IsEmpty && (ann.Bounds.Width > 0 || ann.Bounds.Height > 0)) {
                left = ann.Bounds.Left;
                top = ann.Bounds.Top;
                dispW = ann.Bounds.Width > 0 ? ann.Bounds.Width : 30;
                dispH = ann.Bounds.Height > 0 ? ann.Bounds.Height : 20;
            }
            else continue;

            if (dispW < 1 || dispH < 1) continue;

            // Hit zone = displayed area + 4px L/R + 2px T/B (matches the dashed selection rect)
            var hitBounds = new Rect(left - 4, top - 2, dispW + 8, dispH + 4);
            SquadDashTrace.Write("AnnotatorDrag", $"Canvas_PreviewMLBD: ann=({left:F0},{top:F0} {dispW:F0}×{dispH:F0}) hitBounds=({hitBounds.Left:F0},{hitBounds.Top:F0} {hitBounds.Width:F0}×{hitBounds.Height:F0}) inBounds={hitBounds.Contains(pt)}");

            if (!hitBounds.Contains(pt)) continue;

            // Hit — select the annotation if not already selected, then start drag.
            if (_selectedText != ann)
                SelectText(ann);

            _preDragSnapshot = CaptureSnapshot();
            _canvasTextDragActive = true;
            _canvasTextDragAnnotation = ann;
            _canvasTextDragStart = pt;
            // Capture current canvas position as drag origin (Bounds may have stale/zero W/H)
            _canvasTextDragOrigBounds = new Rect(left, top, dispW, dispH);
            _canvas.CaptureMouse();
            SquadDashTrace.Write("AnnotatorDrag", $"Canvas text-body drag start at ({pt.X:F0},{pt.Y:F0}) orig=({left:F0},{top:F0} {dispW:F0}×{dispH:F0})");
            e.Handled = true;
            return;
        }

        // The click landed on empty canvas (no annotation hit).  If a text annotation is currently
        // being edited, tell CommitActiveTextBox (which fires via LostFocus moments later) to
        // deselect after committing so the resize handles disappear as the user expects.
        if (_activeTextBox != null || _editingText != null)
            _pendingTextCommitDeselect = true;
    }

    private void Canvas_MouseDown(object sender, MouseButtonEventArgs e) {
        if (e.LeftButton != MouseButtonState.Pressed) return;

        // If a child element (e.g. a text annotation display TextBlock) already captured the mouse
        // during its own MouseLeftButtonDown handler (which runs before this MouseDown bubble), let
        // that element manage the drag.  Without this guard, Canvas_MouseDown would deselect the
        // annotation and steal mouse capture, silently breaking text annotation dragging.
        if (Mouse.Captured is not null && !ReferenceEquals(Mouse.Captured, _canvas)) {
            SquadDashTrace.Write("AnnotatorDrag", $"Canvas_MouseDown: skip — mouse already captured by {Mouse.Captured.GetType().Name}");
            return;
        }

        // Note: text resize handle hits and text annotation body drags are handled via
        // PreviewMouseLeftButtonDown (tunnel phase) so they fire before LostFocus or child
        // MouseLeftButtonDown handlers can interfere.

        // Eyedropper mode: sample pixel on click.
        if (_inEyedropperMode) {
            var ept = e.GetPosition(_canvas);
            var ec = SamplePixelAtCanvasPoint(ept);
            UpdateEyedropperResult(ec);
            ExitEyedropperMode();
            EnterMoveMode();
            e.Handled = true;
            return;
        }

        if (e.ClickCount == 2 && !_sel.IsEmpty && !_inArrowMode && !_inRectMode && !_inTextMode) {
            var dpt = e.GetPosition(_canvas);
            if (_sel.Contains(dpt)) {
                DoCropInPlace();
                e.Handled = true;
                return;
            }
        }

        // Clicking canvas background deselects any selected text annotation.
        // MUST run BEFORE SelectArrow/SelectAnnotationRect: both call HideColorPicker() which
        // clears _selectedText without removing resize handles, causing the check below to be
        // skipped and leaving the handles orphaned on the canvas.
        // Skip deselect if a text annotation was just committed by this same click (LostFocus → commit → select).
        if (_selectedText != null) {
            if (_suppressNextTextDeselect)
                _suppressNextTextDeselect = false;
            else
                SelectText(null);
        }

        SelectArrow(null);
        SelectAnnotationRect(null);

        var pt = e.GetPosition(_canvas);

        var zone = HitTest(pt);

        // Resize handles take priority.
        if (zone is HitZone.NW or HitZone.N or HitZone.NE or
                    HitZone.E or HitZone.SE or HitZone.S or
                    HitZone.SW or HitZone.W) {
            _activeZone = zone;
            _dragStart = pt;
            _dragOriginal = _sel;
            _preDragSnapshot = CaptureSnapshot();
            _canvas.CaptureMouse();
            e.Handled = true;
            return;
        }

        // Cursor placement mode.
        if (_inCursorPlacementMode && (_sel.IsEmpty || _sel.Contains(pt))) {
            PlaceCursorAtPoint(pt);
            e.Handled = true;
            return;
        }

        // Arrow placement mode: start drag to define tail→head.
        if (_inArrowMode) {
            _creatingArrowByDrag = true;
            _arrowDragTailPt = pt;
            _preDragSnapshot = CaptureSnapshot();
            _canvas.CaptureMouse();
            _arrowDragPreviewLine = new Line {
                Stroke = new SolidColorBrush(_defaultArrowColor),
                StrokeThickness = 2.5,
                Opacity = 0.7,
                IsHitTestVisible = false,
                X1 = pt.X,
                Y1 = pt.Y,
                X2 = pt.X,
                Y2 = pt.Y
            };
            _arrowDragPreviewHead = new Polygon {
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
        if (_inRectMode) {
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

        // Text placement mode: start drag to define text box dimensions.
        if (_inTextMode) {
            if (_activeTextBox != null) {
                // A text box is already active; let its LostFocus commit+exit — don't start another.
                e.Handled = true;
                return;
            }
            _textDragStart = pt;
            _textDragCreating = true;
            _canvas.CaptureMouse();
            e.Handled = true;
            return;
        }

        // Move the selection.
        if (zone == HitZone.Move) {
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
        if (!_inArrowMode && !_inCursorPlacementMode && !_inRectMode && !_inTextMode && !_inMoveMode) {
            _creatingNewSel = true;
            _newSelAnchor = pt;
            _preDragSnapshot = CaptureSnapshot();
            _canvas.CaptureMouse();
            _canvas.Cursor = AnnotationCursors.CropTool;
            e.Handled = true;
        }
    }

    private void Canvas_MouseMove(object sender, MouseEventArgs e) {
        // Pan mode owns the cursor and all mouse interaction while Space is held.
        if (_isPanMode) return;

        // Live preview for arrow drag-to-draw.
        if (_creatingArrowByDrag && _arrowDragPreviewLine != null && _arrowDragPreviewHead != null) {
            var headPt = e.GetPosition(_canvas);
            var tailPt = _arrowDragTailPt;
            _arrowDragPreviewLine.X1 = tailPt.X;
            _arrowDragPreviewLine.Y1 = tailPt.Y;
            _arrowDragPreviewLine.X2 = headPt.X;
            _arrowDragPreviewLine.Y2 = headPt.Y;

            var dx = headPt.X - tailPt.X;
            var dy = headPt.Y - tailPt.Y;
            var dist2 = Math.Sqrt(dx * dx + dy * dy);
            if (dist2 > 4) {
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
                // Show crosshair at the future pivot center (ArrowLength × 0.9 past the tip).
                ShowCrosshair(headPt.X + ux2 * _defaultArrowLength * 0.9, headPt.Y + uy2 * _defaultArrowLength * 0.9);
            }
            else {
                HideCrosshair();
            }
            e.Handled = true;
            return;
        }

        // Text drag: show a dashed preview rectangle while the user defines the text box width.
        if (_textDragCreating) {
            var curPt = e.GetPosition(_canvas);
            var l = Math.Min(_textDragStart.X, curPt.X);
            var t = Math.Min(_textDragStart.Y, curPt.Y);
            var r = Math.Max(_textDragStart.X, curPt.X);
            var b = Math.Max(_textDragStart.Y, curPt.Y);
            if (r - l >= 5) {
                if (_textDragPreview == null) {
                    _textDragPreview = new Rectangle {
                        Stroke = Brushes.White,
                        StrokeThickness = 1.5,
                        Fill = new SolidColorBrush(Color.FromArgb(20, 255, 255, 255)),
                        IsHitTestVisible = false,
                    };
                    Panel.SetZIndex(_textDragPreview, 200);
                    _canvas.Children.Add(_textDragPreview);
                }
                Canvas.SetLeft(_textDragPreview, l);
                Canvas.SetTop(_textDragPreview, t);
                _textDragPreview.Width = r - l;
                _textDragPreview.Height = Math.Max(b - t, AnnotationText.MinFontSize * 1.5);
            }
            else if (_textDragPreview != null) {
                _canvas.Children.Remove(_textDragPreview);
                _textDragPreview = null;
            }
            e.Handled = true;
            return;
        }

        // Rubber-band draw of an annotation rectangle.
        if (_creatingAnnotRect) {
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
        if (_creatingNewSel) {
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

        if (_activeZone != HitZone.None) {
            var pt = e.GetPosition(_canvas);
            var dx = pt.X - _dragStart.X;
            var dy = pt.Y - _dragStart.Y;
            var w = _canvas.Width;
            var h = _canvas.Height;

            _sel = _activeZone switch {
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

        if (_canvasTextDragActive && _canvasTextDragAnnotation != null) {
            var movePt = e.GetPosition(_canvas);
            var ann = _canvasTextDragAnnotation;
            var newX = Math.Max(0, Math.Min(_canvasTextDragOrigBounds.X + (movePt.X - _canvasTextDragStart.X), _canvas.Width - 20));
            var newY = Math.Max(0, Math.Min(_canvasTextDragOrigBounds.Y + (movePt.Y - _canvasTextDragStart.Y), _canvas.Height - 16));
            ann.Bounds = new Rect(newX, newY, ann.Bounds.Width, ann.Bounds.Height);
            if (ann.Display != null) {
                Canvas.SetLeft(ann.Display, newX);
                Canvas.SetTop(ann.Display, newY);
            }
            if (ann.Shadow != null) {
                Canvas.SetLeft(ann.Shadow, newX + 1.5);
                Canvas.SetTop(ann.Shadow, newY + 1.5);
            }
            // Keep the live TextBox in sync when dragging while in edit mode.
            if (_activeTextBox != null && ann == _editingText) {
                Canvas.SetLeft(_activeTextBox, newX);
                Canvas.SetTop(_activeTextBox, newY);
            }
            UpdateTextSelectionBorder();
            e.Handled = true;
            return;
        }

        if (_draggingTextHandle && _textHandleDragAnnotation != null) {
            var pt = e.GetPosition(_canvas);
            var ann = _textHandleDragAnnotation;
            double origW = _textHandleDragOrigDisplayW > 0 ? _textHandleDragOrigDisplayW : 80;
            double origH = _textHandleDragOrigDisplayH > 0 ? _textHandleDragOrigDisplayH : 20;
            double dx = pt.X - _textHandleDragStart.X;
            double dy = pt.Y - _textHandleDragStart.Y;

            // Scale factors per handle: how much does this handle movement grow/shrink the box?
            double sx = 1.0, sy = 1.0;
            switch (_textHandleDragIndex) {
                case 0: sx = (origW - dx) / origW; sy = (origH - dy) / origH; break; // NW
                case 1: sx = (origW + dx) / origW; sy = (origH - dy) / origH; break; // NE
                case 2: sx = (origW - dx) / origW; sy = (origH + dy) / origH; break; // SW
                case 3: sx = (origW + dx) / origW; sy = (origH + dy) / origH; break; // SE
                case 4: sy = (origH - dy) / origH; break; // N
                case 5: sx = (origW + dx) / origW; break; // E
                case 6: sy = (origH + dy) / origH; break; // S
                case 7: sx = (origW - dx) / origW; break; // W
            }

            // Uniform font-size scale: take the larger factor so the text grows to fill the box.
            double scale = Math.Max(sx, sy);
            scale = Math.Max(0.2, scale);
            double newFontSize = Math.Max(AnnotationText.MinFontSize,
                                 Math.Min(_textHandleDragOrigFontSize * scale, 150.0));
            ann.FontSize = newFontSize;
            if (ann.Display != null) ann.Display.FontSize = newFontSize;
            if (ann.Shadow != null) ann.Shadow.FontSize = newFontSize;
            if (_activeTextBox != null && ann == _editingText) _activeTextBox.FontSize = newFontSize;

            // Compute expected pixel size from the font scale for live handle positioning
            // (layout hasn't run yet so ActualWidth/Height would be stale).
            double fontScale = newFontSize / _textHandleDragOrigFontSize;
            double expectedW = origW * fontScale;
            double expectedH = origH * fontScale;

            bool isFixedWidth = _textHandleDragOrigBounds.Width > 0;
            if (isFixedWidth) {
                double newW = Math.Max(20, origW * Math.Max(0.2, sx));
                if (ann.Display != null) ann.Display.Width = newW;
                if (ann.Shadow != null) ann.Shadow.Width = newW;
                // Also resize the live TextBox if this annotation is being edited.
                if (_activeTextBox != null && ann == _editingText) _activeTextBox.Width = newW;

                // Re-measure at the new width: wrapped text may need more height when the
                // box is made narrower, so never let height be less than the content requires.
                if (ann.Display != null) {
                    ann.Display.Measure(new Size(newW, double.PositiveInfinity));
                    expectedH = Math.Max(expectedH, ann.Display.DesiredSize.Height);
                }

                ann.Bounds = new Rect(_textHandleDragOrigBounds.Left, _textHandleDragOrigBounds.Top, newW, expectedH);
                expectedW = newW;
            }
            else {
                // NoWrap text: clamp to natural text dimensions so the box can never be made
                // smaller than its content (prevents overflow outside the background rect).
                if (ann.Display != null) {
                    ann.Display.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
                    expectedW = Math.Max(expectedW, ann.Display.DesiredSize.Width);
                    expectedH = Math.Max(expectedH, ann.Display.DesiredSize.Height);
                }
                ann.Bounds = new Rect(_textHandleDragOrigBounds.Left, _textHandleDragOrigBounds.Top, expectedW, expectedH);
            }

            // Update selection rect live.
            if (_textSelectionRect != null) {
                _textSelectionRect.Width = Math.Max(30, expectedW + 8);
                _textSelectionRect.Height = Math.Max(20, expectedH + 4);
                Canvas.SetLeft(_textSelectionRect, ann.Bounds.Left - 4);
                Canvas.SetTop(_textSelectionRect, ann.Bounds.Top - 2);
            }
            PositionTextResizeHandles(ann);
            e.Handled = true;
            return;
        }

        // Eyedropper mode: show live color tooltip.
        if (_inEyedropperMode) {
            _canvas.Cursor = Cursors.Cross;
            var ept = e.GetPosition(_canvas);
            var ec = SamplePixelAtCanvasPoint(ept);
            ShowEyedropperTooltip(ept, ec);
            return;
        }

        // Update cursor shape based on hover zone (not during arrow/cursor drag).
        // Skip the override when the mouse is directly over a text resize handle — its own Cursor wins.
        if (_textResizeHandles.Count > 0
            && Mouse.DirectlyOver is Rectangle hoveredHandle
            && _textResizeHandles.Contains(hoveredHandle))
            return;

        // Skip the override when the mouse is directly over an arrow endpoint handle — its own Cursor wins.
        if (_selectedArrow != null && Mouse.DirectlyOver is Ellipse hoveredEndpoint
            && (hoveredEndpoint == _selectedArrow.TipHandle || hoveredEndpoint == _selectedArrow.TailHandle))
            return;

        if (!_draggingCursor && _draggingArrow == null && !_bodyDragging && _draggingAnnotRect == null) {
            // In a tool mode keep the tool cursor — don't override it with zone/cross cursors.
            if (_inArrowMode)
                _canvas.Cursor = AnnotationCursors.ArrowTool;
            else if (_inRectMode)
                _canvas.Cursor = AnnotationCursors.RectTool;
            else if (_inTextMode)
                _canvas.Cursor = AnnotationCursors.TextTool;
            else if (_inCursorPlacementMode)
                _canvas.Cursor = AnnotationCursors.DropCursorTool;
            else if (_sel.IsEmpty && _inCropMode)
                _canvas.Cursor = AnnotationCursors.CropTool;
            else if (_sel.IsEmpty && _inMoveMode)
                _canvas.Cursor = Cursors.Arrow;
            else if (!_sel.IsEmpty) {
                var hoverZone = HitTest(e.GetPosition(_canvas));
                // Outside the crop rect: show crop cursor when in crop mode; arrow in move mode.
                _canvas.Cursor = hoverZone == HitZone.None
                    ? (_inCropMode ? AnnotationCursors.CropTool : Cursors.Arrow)
                    : ZoneCursor(hoverZone);
            }

            // Hover cursor: show the standard arrow pointer over any draggable annotation
            // (arrow shaft/head, rect border, cursor indicator) in the neutral tool state.
            // This overrides whatever crop/cross cursor was set above.
            if (!_inArrowMode && !_inRectMode && !_inTextMode && !_inCursorPlacementMode && !_inEyedropperMode
                && IsHoveringOverAnnotation(e.GetPosition(_canvas)))
                _canvas.Cursor = Cursors.Arrow;

            // Override with directional cursor when hovering over a selected annotation rect's edge/corner
            // — but not while a tool mode is active (clicking would draw a new shape, not resize).
            if (_selectedAnnotRect != null && !_inArrowMode && !_inRectMode) {
                var az = HitTestAnnotRect(_selectedAnnotRect, e.GetPosition(_canvas));
                if (az != HitZone.None)
                    _canvas.Cursor = ZoneCursor(az);
            }
        }
    }

    private void Canvas_MouseUp(object sender, MouseButtonEventArgs e) {
        if (_canvasTextDragActive) {
            SquadDashTrace.Write("AnnotatorDrag", $"Canvas text-body drag end at ({e.GetPosition(_canvas).X:F0},{e.GetPosition(_canvas).Y:F0})");
            CommitDragUndo();
            _canvasTextDragActive = false;
            _canvasTextDragAnnotation = null;
            _canvas.ReleaseMouseCapture();
            if (_selectedText != null) SelectText(_selectedText);  // refresh handles position
            e.Handled = true;
            return;
        }

        if (_textDragCreating) {
            _textDragCreating = false;
            _canvas.ReleaseMouseCapture();
            if (_textDragPreview != null) {
                _canvas.Children.Remove(_textDragPreview);
                _textDragPreview = null;
            }
            var upPt = e.GetPosition(_canvas);
            var dragW = Math.Abs(upPt.X - _textDragStart.X);
            if (dragW < 5)
                BeginEditText(_textDragStart);
            else
                BeginEditTextWithWidth(_textDragStart, dragW);
            e.Handled = true;
            return;
        }

        if (_creatingArrowByDrag) {
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

            if (dist >= 40.0) {
                _preDragSnapshot = null; // let CreateArrow handle its own undo push
                PlaceArrowFromDrag(tailPt, headPt, dist);
            }
            else if (dist < 5.0) {
                // Click without drag: drop an arrow at the click point using the last-used angle/length.
                _preDragSnapshot = null;
                var rad = _lastDragArrowAngleDeg * Math.PI / 180.0;
                var ux = Math.Sin(rad);
                var uy = -Math.Cos(rad);
                var synTailPt = new Point(headPt.X + ux * _lastDragArrowTailLength,
                                          headPt.Y + uy * _lastDragArrowTailLength);
                PlaceArrowFromDrag(synTailPt, headPt, _lastDragArrowTailLength);
            }
            else {
                _preDragSnapshot = null;
            }
            e.Handled = true;
            return;
        }

        if (_creatingAnnotRect) {
            _creatingAnnotRect = false;
            _canvas.ReleaseMouseCapture();
            if (_annotRectPreview != null) {
                _annotRectPreview.Visibility = Visibility.Hidden;
                if (_annotRectPreview.Width >= MinSize && _annotRectPreview.Height >= MinSize) {
                    var bounds = new Rect(
                        Canvas.GetLeft(_annotRectPreview),
                        Canvas.GetTop(_annotRectPreview),
                        _annotRectPreview.Width,
                        _annotRectPreview.Height);
                    _preDragSnapshot = null; // let CreateAnnotationRect handle its own undo push
                    _lastDragRectWidth = bounds.Width;
                    _lastDragRectHeight = bounds.Height;
                    CreateAnnotationRect(bounds);
                }
                else if (_annotRectPreview.Width < 5 && _annotRectPreview.Height < 5) {
                    // Click without drag: drop a rectangle centered on the click point at last-used size.
                    _preDragSnapshot = null;
                    var rw = _lastDragRectWidth;
                    var rh = _lastDragRectHeight;
                    var rx = Math.Max(0, Math.Min(_canvas.Width - rw, _annotRectAnchor.X - rw / 2));
                    var ry = Math.Max(0, Math.Min(_canvas.Height - rh, _annotRectAnchor.Y - rh / 2));
                    CreateAnnotationRect(new Rect(rx, ry, rw, rh));
                }
            }
            CommitDragUndo();
            if (_inRectMultiDropMode) {
                // Multi-drop: stay in rect mode so the next drag places another rectangle.
                _canvas.Cursor = AnnotationCursors.RectTool;
                ShowModeHint("Multi-drop: drag to place rectangles · ESC to exit");
            }
            else {
                ExitRectMode(returnToMove: true);
            }
            e.Handled = true;
            return;
        }

        if (_creatingNewSel) {
            _creatingNewSel = false;
            CommitDragUndo();
            _canvas.ReleaseMouseCapture();
            _canvas.Cursor = Cursors.Arrow;
            e.Handled = true;
            return;
        }

        if (_activeZone != HitZone.None) {
            CommitDragUndo();
            _activeZone = HitZone.None;
            _canvas.ReleaseMouseCapture();
            _canvas.Cursor = Cursors.Arrow;
            e.Handled = true;
        }

        if (_draggingTextHandle) {
            _draggingTextHandle = false;
            _canvas.ReleaseMouseCapture();
            CommitDragUndo();
            var finishedAnn = _textHandleDragAnnotation;
            _textHandleDragAnnotation = null;
            if (finishedAnn != null)
                UpdateTextDisplay(finishedAnn);
        }
    }

    // ── Keyboard ──────────────────────────────────────────────────────────────

    private void Window_KeyDown(object sender, KeyEventArgs e) {
        if (e.Key == Key.Escape) {
            // Priority 1 — cancel an active crop rectangle (drawing in progress or live selection).
            if (_creatingNewSel) {
                _creatingNewSel = false;
                _canvas.ReleaseMouseCapture();
                // Restore the pre-drag snapshot so any previously-existing crop region is preserved.
                if (_preDragSnapshot != null) { RestoreSnapshot(_preDragSnapshot); _preDragSnapshot = null; }
                else { _sel = Rect.Empty; RefreshLayout(); }
                e.Handled = true;
                return;
            }
            if (!_sel.IsEmpty) {
                _sel = Rect.Empty;
                RefreshLayout();
                e.Handled = true;
                return;
            }

            if (_inArrowMode) {
                if (_creatingArrowByDrag) {
                    _creatingArrowByDrag = false;
                    _canvas.ReleaseMouseCapture();
                    if (_arrowDragPreviewLine != null) { _canvas.Children.Remove(_arrowDragPreviewLine); _arrowDragPreviewLine = null; }
                    if (_arrowDragPreviewHead != null) { _canvas.Children.Remove(_arrowDragPreviewHead); _arrowDragPreviewHead = null; }
                    HideCrosshair();
                    _preDragSnapshot = null;
                }
                ExitArrowMode(returnToMove: true); e.Handled = true; return;
            }
            if (_inRectMode) { ExitRectMode(returnToMove: true); e.Handled = true; return; }
            if (_inTextMode) {
                // TextBox ESC is already handled in CreateTextBoxOverlay's KeyDown (e.Handled=true there),
                // so this branch handles text mode with no active textbox — just exit the mode.
                if (_activeTextBox == null) ExitTextMode(returnToMove: true);
                e.Handled = true;
                return;
            }
            if (_inCursorPlacementMode) {
                _inCursorPlacementMode = false;
                _cursorEnabled = false;
                _canvas.Cursor = Cursors.Arrow;
                HideModeHint();
                EnterMoveMode();
                e.Handled = true;
                return;
            }
            // Priority 2 — deselect a selected annotation (arrow, rect, or text label).
            if (_selectedArrow != null) { SelectArrow(null); e.Handled = true; return; }
            if (_selectedAnnotRect != null) { SelectAnnotationRect(null); e.Handled = true; return; }
            if (_selectedText != null) { SelectText(null); e.Handled = true; return; }

            // Priority 3 — no active selection: close only when NOT in the neutral move mode.
            if (!_inMoveMode) {
                if (HasChanges()) {
                    var answer = MessageBox.Show(
                        this,
                        "You have unsaved annotations. Discard changes and close?",
                        "Discard Changes?",
                        MessageBoxButton.YesNo,
                        MessageBoxImage.Question,
                        MessageBoxResult.No);
                    if (answer == MessageBoxResult.Yes)
                        Close();
                    // MessageBoxResult.No → return to editor, do nothing
                }
                else {
                    Close();
                }
            }
            e.Handled = true;
        }
        else if (e.Key == Key.Delete && _selectedArrow != null) {
            PushUndo();
            RemoveArrow(_selectedArrow);
            _selectedArrow = null;
            e.Handled = true;
        }
        else if (e.Key == Key.Delete && _selectedAnnotRect != null) {
            PushUndo();
            RemoveAnnotationRect(_selectedAnnotRect);
            _selectedAnnotRect = null;
            e.Handled = true;
        }
        else if (e.Key == Key.Delete && !_sel.IsEmpty) {
            PushUndo();
            _sel = Rect.Empty;
            RefreshLayout();
            e.Handled = true;
        }
        else if (e.Key == Key.Z && (Keyboard.Modifiers & ModifierKeys.Control) != 0) {
            if (Keyboard.FocusedElement is TextBox) return; // let TextBox handle its own undo
            PerformUndo();
            e.Handled = true;
        }
        else if (e.Key == Key.Y && (Keyboard.Modifiers & ModifierKeys.Control) != 0) {
            if (Keyboard.FocusedElement is TextBox) return;
            PerformRedo();
            e.Handled = true;
        }
        else if (e.Key is Key.Return or Key.Enter && Keyboard.Modifiers == ModifierKeys.None) {
            if (!_sel.IsEmpty)
                DoCropInPlace();
            else
                DoInsertImage();
            e.Handled = true;
        }

        // Tool keyboard shortcuts — ignored when a TextBox has keyboard focus.
        if (!e.Handled && Keyboard.FocusedElement is not TextBox) {
            bool shift = (Keyboard.Modifiers & ModifierKeys.Shift) != 0;
            if (e.Key == Key.M || e.Key == Key.V) {
                ExitAllToolModes();
                EnterMoveMode();
                e.Handled = true;
            }
            else if (e.Key == Key.A && !shift) {
                ExitAllToolModes();
                EnterArrowMode();
                if (_addArrowBtn != null) _addArrowBtn.Content = MakeToolIcon("ImageEditorArrowIcon", active: true);
                e.Handled = true;
            }
            else if (e.Key == Key.A && shift) {
                ExitAllToolModes();
                EnterArrowMode();
                _inArrowMultiDropMode = true;
                ShowModeHint("Multi-drop: drag to place arrows · ESC to exit");
                if (_addArrowBtn != null) _addArrowBtn.Content = MakeToolIcon("ImageEditorArrowIcon", active: true, multiDrop: true);
                e.Handled = true;
            }
            else if (e.Key == Key.R && !shift) {
                ExitAllToolModes();
                EnterRectMode();
                if (_addRectBtn != null) _addRectBtn.Content = MakeToolIcon("ImageEditorRectIcon", active: true);
                e.Handled = true;
            }
            else if (e.Key == Key.R && shift) {
                ExitAllToolModes();
                EnterRectMode();
                _inRectMultiDropMode = true;
                ShowModeHint("Multi-drop: drag to place rectangles · ESC to exit");
                if (_addRectBtn != null) _addRectBtn.Content = MakeToolIcon("ImageEditorRectIcon", active: true, multiDrop: true);
                e.Handled = true;
            }
            else if (e.Key == Key.T) {
                ExitAllToolModes();
                EnterTextMode();
                if (_addTextBtn != null) _addTextBtn.Content = MakeToolIcon("ImageEditorTextIcon", active: true);
                e.Handled = true;
            }
        }
    }

    // ── Arrow mode ────────────────────────────────────────────────────────────

    private void EnterArrowMode() {
        if (_selectedAnnotRect != null) SelectAnnotationRect(null);
        if (_selectedText != null) SelectText(null);
        _inMoveMode = false;
        _inCropMode = false;
        _inArrowMode = true;
        Cursor = AnnotationCursors.ArrowTool;
        _canvas.Cursor = AnnotationCursors.ArrowTool;
        ShowModeHint("Drag to draw an arrow");
    }

    private void EnterMoveMode() {
        if (_selectedText != null) SelectText(null);
        _inMoveMode = true;
        _inCropMode = false;
        _canvas.Cursor = Cursors.Arrow;
        Cursor = Cursors.Arrow;
        HideModeHint();
        if (_moveSelectBtn != null) _moveSelectBtn.Content = MakeToolIcon("ImageEditorMoveIcon", active: true);
        if (_cropBtn != null) _cropBtn.Content = MakeToolIcon("ImageEditorCropIcon");
    }

    private void ExitMoveMode() {
        _inMoveMode = false;
        if (_moveSelectBtn != null) _moveSelectBtn.Content = MakeToolIcon("ImageEditorMoveIcon");
    }

    private void EnterCropMode() {
        _inCropMode = true;
        _inMoveMode = false;
        _canvas.Cursor = AnnotationCursors.CropTool;
        Cursor = Cursors.Arrow;
        HideModeHint();
        if (_cropBtn != null) _cropBtn.Content = MakeToolIcon("ImageEditorCropIcon", active: true);
        if (_moveSelectBtn != null) _moveSelectBtn.Content = MakeToolIcon("ImageEditorMoveIcon");
    }

    private void ExitCropMode() {
        _inCropMode = false;
        if (_cropBtn != null) _cropBtn.Content = MakeToolIcon("ImageEditorCropIcon");
    }

    private void ExitArrowMode(bool returnToMove = false) {
        _inArrowMode = false;
        _inArrowMultiDropMode = false;
        Cursor = Cursors.Arrow;
        _canvas.Cursor = Cursors.Arrow;
        HideModeHint();
        if (_addArrowBtn != null) _addArrowBtn.Content = MakeToolIcon("ImageEditorArrowIcon");
        if (returnToMove) EnterMoveMode();
    }

    private void ExitAllToolModes() {
        if (_inArrowMode) ExitArrowMode();
        if (_inRectMode) ExitRectMode();
        if (_inTextMode) ExitTextMode();
        if (_activeTextBox != null) CommitActiveTextBox(); // commit in-progress edit even if text mode was already exited
        if (_inEyedropperMode) ExitEyedropperMode();
        SelectText(null);
    }

    /// <summary>
    /// Creates an arrow where <paramref name="tailPt"/> is the blunt tail end
    /// and <paramref name="headPt"/> is the arrowhead tip (pointy end).
    /// </summary>
    private void PlaceArrowFromDrag(Point tailPt, Point headPt, double dist) {
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

        // Remember this drag's angle and tail length for subsequent click-without-drag placement.
        _lastDragArrowAngleDeg = angleDeg;
        _lastDragArrowTailLength = tailLen;

        _defaultArrowAngleDeg = savedAngle;
        _defaultTailLength = savedTailLen;

        SelectArrow(arrow);
        if (_inArrowMultiDropMode) {
            // Multi-drop: stay in arrow mode so the next drag places another arrow.
            _canvas.Cursor = AnnotationCursors.ArrowTool;
            ShowModeHint("Multi-drop: drag to place arrows · ESC to exit");
        }
        else {
            ExitArrowMode(returnToMove: true);
        }
    }

    private AnnotationArrow CreateArrow(Rect targetBounds) {
        if (!_suppressUndo) PushUndo();

        var center = new Point(
            targetBounds.Left + targetBounds.Width / 2,
            targetBounds.Top + targetBounds.Height / 2);

        double initialTailLength = _defaultTailLength > 0
            ? _defaultTailLength
            : ComputeInitialTailLength(center, _defaultArrowAngleDeg, _defaultArrowLength);

        // Shadow shapes (drawn first so they are below main arrow visuals).
        var shadowLine = new Polyline {
            Stroke = new SolidColorBrush(Color.FromArgb(102, 0, 0, 0)),
            StrokeThickness = 2.5,
            IsHitTestVisible = false
        };
        var shadowHead = new Polygon {
            Fill = new SolidColorBrush(Color.FromArgb(102, 0, 0, 0)),
            IsHitTestVisible = false
        };

        var colorBrush = new SolidColorBrush(_defaultArrowColor);

        var line = new Line {
            StrokeThickness = 2.5,
            Stroke = colorBrush,
            IsHitTestVisible = true,
            Cursor = Cursors.Arrow
        };
        var hitLine = new Line {
            StrokeThickness = 9,
            Stroke = Brushes.Transparent,
            IsHitTestVisible = true,
            Cursor = Cursors.Arrow
        };
        var head = new Polygon {
            Fill = colorBrush,
            IsHitTestVisible = true,
            Cursor = Cursors.Arrow
        };
        var tipHandle = new Ellipse {
            Width = 8,
            Height = 8,
            Fill = Brushes.White,
            Stroke = Brushes.Black,
            StrokeThickness = 1,
            Cursor = AnnotationCursors.RotateEndpoint,
            Visibility = Visibility.Hidden
        };
        var tailHandle = new Ellipse {
            Width = 8,
            Height = 8,
            Fill = Brushes.White,
            Stroke = Brushes.Black,
            StrokeThickness = 1,
            Cursor = AnnotationCursors.RotateEndpoint,
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

        var arrow = new AnnotationArrow {
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
        tipHandle.MouseLeftButtonDown += (_, e) => {
            _preDragSnapshot = CaptureSnapshot();
            _draggingArrow = arrow;
            _tailDragging = false;
            HideColorPicker();
            tipHandle.CaptureMouse();
            e.Handled = true;
        };
        tipHandle.MouseMove += (_, e) => {
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
        tipHandle.MouseLeftButtonUp += (_, e) => {
            if (_draggingArrow != arrow || _tailDragging) return;
            _defaultArrowAngleDeg = arrow.ArrowheadAngleDeg;
            _lastDragArrowAngleDeg = arrow.ArrowheadAngleDeg;
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
        tailHandle.MouseLeftButtonDown += (_, e) => {
            _preDragSnapshot = CaptureSnapshot();
            _draggingArrow = arrow;
            _tailDragging = true;
            _tailDragStartMouse = e.GetPosition(_canvas);
            HideColorPicker();
            tailHandle.CaptureMouse();
            e.Handled = true;
        };
        tailHandle.MouseMove += (_, e) => {
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
        tailHandle.MouseLeftButtonUp += (_, e) => {
            if (_draggingArrow != arrow || !_tailDragging) return;
            arrow.UserTailLength = arrow.TailLength;
            _defaultTailLength = arrow.TailLength;
            _lastDragArrowTailLength = arrow.TailLength;
            _defaultArrowAngleDeg = arrow.ArrowheadAngleDeg;
            _lastDragArrowAngleDeg = arrow.ArrowheadAngleDeg;
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

        tipHandle.MouseRightButtonDown += (_, e) => {
            RemoveArrow(arrow);
            if (_selectedArrow == arrow) { _selectedArrow = null; HideColorPicker(); }
            e.Handled = true;
        };
        tailHandle.MouseRightButtonDown += (_, e) => {
            RemoveArrow(arrow);
            if (_selectedArrow == arrow) { _selectedArrow = null; HideColorPicker(); }
            e.Handled = true;
        };

        _arrows.Add(arrow);
        UpdateArrowGeometry(arrow);
        SelectArrow(arrow);
        return arrow;
    }

    private void AttachBodyDrag(Shape shape, AnnotationArrow arrow) {
        shape.MouseLeftButtonDown += (_, e) => {
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
        shape.MouseMove += (_, e) => {
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
        shape.MouseLeftButtonUp += (_, e) => {
            if (_draggingArrow != arrow || !_bodyDragging) return;
            HideCrosshair();
            CommitDragUndo();
            _draggingArrow = null;
            _bodyDragging = false;
            ShowColorPicker(arrow);
            shape.ReleaseMouseCapture();
            e.Handled = true;
        };
        shape.MouseRightButtonDown += (_, e) => {
            RemoveArrow(arrow);
            if (_selectedArrow == arrow) { _selectedArrow = null; HideColorPicker(); }
            e.Handled = true;
        };
    }

    // ── Arrow geometry ────────────────────────────────────────────────────────

    private static void UpdateArrowGeometry(AnnotationArrow arrow) {
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

    private void EnsureCrosshairLines() {
        if (_crosshairRedH != null) return;
        const double Thick = 1.5;
        _crosshairWhiteH = new Line { Stroke = System.Windows.Media.Brushes.White, StrokeThickness = Thick + 1.0, Opacity = 0.5, IsHitTestVisible = false };
        _crosshairWhiteV = new Line { Stroke = System.Windows.Media.Brushes.White, StrokeThickness = Thick + 1.0, Opacity = 0.5, IsHitTestVisible = false };
        _crosshairRedH = new Line { Stroke = System.Windows.Media.Brushes.Red, StrokeThickness = Thick, Opacity = 0.5, IsHitTestVisible = false };
        _crosshairRedV = new Line { Stroke = System.Windows.Media.Brushes.Red, StrokeThickness = Thick, Opacity = 0.5, IsHitTestVisible = false };
        foreach (var l in new[] { _crosshairWhiteH, _crosshairWhiteV, _crosshairRedH, _crosshairRedV }) {
            Panel.SetZIndex(l, 100);
            l.Visibility = Visibility.Collapsed;
            _canvas.Children.Add(l);
        }
    }

    private void ShowCrosshair(double cx, double cy) {
        EnsureCrosshairLines();
        const double Half = 10.0;
        const double Shadow = 1.0;   // white offset (1px right + 1px down) behind the red lines
        _crosshairWhiteH!.X1 = cx - Half + Shadow; _crosshairWhiteH.Y1 = cy + Shadow;
        _crosshairWhiteH.X2 = cx + Half + Shadow; _crosshairWhiteH.Y2 = cy + Shadow;
        _crosshairWhiteV!.X1 = cx + Shadow; _crosshairWhiteV.Y1 = cy - Half + Shadow;
        _crosshairWhiteV.X2 = cx + Shadow; _crosshairWhiteV.Y2 = cy + Half + Shadow;
        _crosshairRedH!.X1 = cx - Half; _crosshairRedH.Y1 = cy;
        _crosshairRedH.X2 = cx + Half; _crosshairRedH.Y2 = cy;
        _crosshairRedV!.X1 = cx; _crosshairRedV.Y1 = cy - Half;
        _crosshairRedV.X2 = cx; _crosshairRedV.Y2 = cy + Half;
        _crosshairWhiteH.Visibility = _crosshairWhiteV.Visibility =
        _crosshairRedH.Visibility = _crosshairRedV.Visibility = Visibility.Visible;
    }

    private void HideCrosshair() {
        if (_crosshairRedH is null) return;
        _crosshairWhiteH!.Visibility = _crosshairWhiteV!.Visibility =
        _crosshairRedH.Visibility = _crosshairRedV!.Visibility = Visibility.Collapsed;
    }

    private double ComputeInitialTailLength(Point targetCenter, double angleDeg, double arrowheadOffset) {
        var rad = angleDeg * Math.PI / 180.0;
        var dx = Math.Sin(rad);
        var dy = -Math.Cos(rad);
        var ahX = targetCenter.X + dx * arrowheadOffset;
        var ahY = targetCenter.Y + dy * arrowheadOffset;
        var s = _sel.IsEmpty ? new Rect(0, 0, _canvas.Width, _canvas.Height) : _sel;

        double tMin = double.MaxValue;
        if (Math.Abs(dx) > 1e-9) {
            var t = dx > 0 ? (s.Right - ahX) / dx : (s.Left - ahX) / dx;
            if (t > 0) tMin = Math.Min(tMin, t);
        }
        if (Math.Abs(dy) > 1e-9) {
            var t = dy > 0 ? (s.Bottom - ahY) / dy : (s.Top - ahY) / dy;
            if (t > 0) tMin = Math.Min(tMin, t);
        }
        return tMin < double.MaxValue ? Math.Max(64.0, Math.Min(128.0, tMin * 0.85)) : 80.0;
    }

    private double ComputeMaxArrowheadOffset(Point targetCenter, double angleDeg) {
        var rad = angleDeg * Math.PI / 180.0;
        var dx = Math.Sin(rad);
        var dy = -Math.Cos(rad);
        var s = _sel.IsEmpty ? new Rect(0, 0, _canvas.Width, _canvas.Height) : _sel;

        double tMin = double.MaxValue;
        if (Math.Abs(dx) > 1e-9) {
            var t = dx > 0 ? (s.Right - targetCenter.X) / dx : (s.Left - targetCenter.X) / dx;
            if (t > 0) tMin = Math.Min(tMin, t);
        }
        if (Math.Abs(dy) > 1e-9) {
            var t = dy > 0 ? (s.Bottom - targetCenter.Y) / dy : (s.Top - targetCenter.Y) / dy;
            if (t > 0) tMin = Math.Min(tMin, t);
        }
        return tMin < double.MaxValue ? tMin : 80.0;
    }

    // ── Arrow selection ───────────────────────────────────────────────────────

    private void SelectArrow(AnnotationArrow? arrow) {
        if (_selectedArrow != null && _selectedArrow != arrow) {
            _selectedArrow.TipHandle.Visibility = Visibility.Hidden;
            _selectedArrow.TailHandle.Visibility = Visibility.Hidden;
        }
        _selectedArrow = arrow;
        if (arrow != null) {
            if (_selectedText != null) SelectText(null);
            if (_selectedAnnotRect != null) SelectAnnotationRect(null);
            arrow.TipHandle.Visibility = Visibility.Visible;
            arrow.TailHandle.Visibility = Visibility.Visible;
            ShowColorPicker(arrow);
        }
        else {
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

    /// <summary>
    /// Returns the foreground color palette for text annotations, adapted to the background color.
    /// White bg → dark colors (white hidden); black bg → bright colors (black hidden);
    /// transparent bg → medium-brightness set with both white and black.
    /// </summary>
    private static Color[] GetTextFgPalette(Color bgColor) {
        if (bgColor.A == 0) {
            // Transparent background: medium-brightness set (current arrow palette).
            return GetArrowPalette();
        }
        if (bgColor.R > 200 && bgColor.G > 200 && bgColor.B > 200) {
            // White (or near-white) background: dark/saturated colors only, no white.
            return new[]
            {
                Color.FromRgb(0xCC, 0x00, 0x00), // dark red
                Color.FromRgb(0xCC, 0x66, 0x00), // dark orange
                Color.FromRgb(0x00, 0x66, 0x00), // dark green
                Color.FromRgb(0x00, 0x00, 0xCC), // dark blue
                Color.FromRgb(0x66, 0x00, 0xCC), // dark purple
                Color.FromRgb(0x66, 0x66, 0x66), // gray
                Color.FromRgb(0x00, 0x00, 0x00), // black
            };
        }
        // Black (or near-black) background: bright/light colors only, no black.
        return new[]
        {
            Color.FromRgb(0xFF, 0x44, 0x44), // bright red
            Color.FromRgb(0x44, 0x88, 0xFF), // bright blue
            Color.FromRgb(0x44, 0xFF, 0x44), // bright green
            Color.FromRgb(0xFF, 0xFF, 0x44), // bright yellow
            Color.FromRgb(0x44, 0xFF, 0xFF), // bright cyan
            Color.FromRgb(0xFF, 0x44, 0xFF), // bright magenta
            Color.FromRgb(0xFF, 0xFF, 0xFF), // white
        };
    }

    /// <summary>
    /// Returns true if <paramref name="color"/> is in the text foreground palette for
    /// <paramref name="bgColor"/> (by exact RGB match).
    /// </summary>
    private static bool IsColorInTextFgPalette(Color color, Color bgColor)
        => GetTextFgPalette(bgColor).Any(c => c.R == color.R && c.G == color.G && c.B == color.B);

    private void ShowColorPicker(AnnotationArrow arrow) {
        HideColorPicker();
        _colorPickerArrow = arrow;
        var palette = GetArrowPalette();

        _colorPickerPanel = new StackPanel { Orientation = Orientation.Horizontal };
        Panel.SetZIndex(_colorPickerPanel, 300);

        foreach (var color in palette) {
            var c = color;
            bool isSelected = c == arrow.ArrowColor;
            var swatch = MakeColorSwatch(c, isSelected, picked => {
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

    private static string ColorName(Color c) => c switch {
        { R: 255, G: 0, B: 0 } => "Red", { R: 0, G: 200, B: 0 } or { R: 0, G: 255, B: 0 } => "Green",
        { R: 0, G: 0, B: 255 } => "Blue",
        { R: 255, G: 255, B: 0 } => "Yellow", { R: 255, G: 165, B: 0 } or { R: 255, G: 120, B: 20 } => "Orange",
        { R: 255, G: 255, B: 255 } => "White",
        { R: 0, G: 0, B: 0 } => "Black",
        _ => $"#{c.R:X2}{c.G:X2}{c.B:X2}"
    };

    private static FrameworkElement MakeColorSwatch(Color c, bool isSelected, Action<Color> onPick) {
        var tip = $"Set text color to {ColorName(c)}";
        if (isSelected) {
            var grid = new Grid { Width = 20, Height = 20, Margin = new Thickness(3, 0, 3, 0), Cursor = Cursors.Hand, ToolTip = tip };
            grid.Children.Add(new Ellipse { Fill = Brushes.Black });
            grid.Children.Add(new Ellipse {
                Width = 16,
                Height = 16,
                Fill = new SolidColorBrush(c),
                Stroke = Brushes.White,
                StrokeThickness = 1.5,
            });
            grid.MouseLeftButtonDown += (_, e) => { onPick(c); e.Handled = true; };
            return grid;
        }
        else {
            var dot = new Ellipse {
                Width = 16,
                Height = 16,
                Fill = new SolidColorBrush(c),
                Stroke = new SolidColorBrush(Color.FromRgb(0x88, 0x88, 0x88)),
                StrokeThickness = 1,
                Margin = new Thickness(3, 0, 3, 0),
                Cursor = Cursors.Hand,
                ToolTip = tip,
            };
            dot.MouseLeftButtonDown += (_, e) => { onPick(c); e.Handled = true; };
            return dot;
        }
    }

    private void HideColorPicker() {
        if (_colorPickerPanel != null) {
            _canvas.Children.Remove(_colorPickerPanel);
            _colorPickerPanel = null;
        }
        _colorPickerArrow = null;
        _colorPickerRect = null;
        _colorPickerText = null;
        _selectedText = null;
        // Don't remove handles while a text box is actively being edited.
        if (_activeTextBox == null) {
            RemoveTextResizeHandles();
            if (_textSelectionRect != null) {
                _canvas.Children.Remove(_textSelectionRect);
                _textSelectionRect = null;
            }
        }
    }

    private void RemoveArrow(AnnotationArrow arrow) {
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

    private void EnterRectMode() {
        SelectArrow(null);
        if (_selectedText != null) SelectText(null);
        _inMoveMode = false;
        _inCropMode = false;
        _inRectMode = true;
        Cursor = AnnotationCursors.RectTool;
        _canvas.Cursor = AnnotationCursors.RectTool;
        ShowModeHint("Drag to draw a rectangle");
    }

    private void ExitRectMode(bool returnToMove = false) {
        _inRectMode = false;
        _inRectMultiDropMode = false;
        Cursor = Cursors.Arrow;
        _canvas.Cursor = Cursors.Arrow;
        HideModeHint();
        if (_addRectBtn != null) _addRectBtn.Content = MakeToolIcon("ImageEditorRectIcon");
        if (returnToMove) EnterMoveMode();
    }

    private Rectangle EnsureAnnotRectPreview() {
        if (_annotRectPreview != null) return _annotRectPreview;
        _annotRectPreview = new Rectangle {
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

    private AnnotationRect CreateAnnotationRect(Rect bounds, Color? color = null) {
        if (!_suppressUndo) PushUndo();

        var rectColor = color ?? _defaultRectColor;
        var brush = new SolidColorBrush(rectColor);

        var shadow = new Rectangle {
            Fill = Brushes.Transparent,
            Stroke = new SolidColorBrush(Color.FromArgb(102, 0, 0, 0)),
            StrokeThickness = 2.5,
            RadiusX = 4,
            RadiusY = 4,
            IsHitTestVisible = false
        };

        var border = new Rectangle {
            Fill = Brushes.Transparent,
            Stroke = brush,
            StrokeThickness = 2.5,
            RadiusX = 4,
            RadiusY = 4,
            IsHitTestVisible = true
            // Cursor not set — inherits from canvas; Canvas_MouseMove controls it dynamically.
        };

        var hitZone = new Rectangle {
            Fill = Brushes.Transparent,
            StrokeThickness = 0,
            IsHitTestVisible = true
            // Cursor not set — inherits from canvas; Canvas_MouseMove controls it dynamically.
        };

        var handles = new Rectangle[8];
        for (int i = 0; i < 8; i++) {
            var cursor = i switch {
                0 or 3 => Cursors.SizeNWSE,  // NW, SE
                1 or 2 => Cursors.SizeNESW,  // NE, SW
                4 or 5 => Cursors.SizeNS,    // N, S
                6 or 7 => Cursors.SizeWE,    // W, E
                _ => Cursors.SizeAll
            };
            handles[i] = new Rectangle {
                Width = 8,
                Height = 8,
                Fill = Brushes.White,
                Stroke = Brushes.Black,
                StrokeThickness = 1,
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

        var annotRect = new AnnotationRect {
            Bounds = bounds,
            RectColor = rectColor,
            Border = border,
            Shadow = shadow,
            Handles = handles,
            HitZoneRect = hitZone
        };

        border.MouseLeftButtonDown += (_, e) => {
            if (_draggingAnnotRect != null || _inArrowMode || _inRectMode) return;
            // Only initiate drag when clicking on or near the border edge — not the interior.
            if (!IsOnRectBorder(annotRect.Bounds, e.GetPosition(_canvas), 6.0)) return;
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
        border.MouseMove += (_, e) => {
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
        border.MouseLeftButtonUp += (_, e) => {
            if (_draggingAnnotRect != annotRect || !_annotRectBodyDragging) return;
            CommitDragUndo();
            _draggingAnnotRect = null;
            _annotRectBodyDragging = false;
            border.ReleaseMouseCapture();
            e.Handled = true;
        };
        border.MouseRightButtonDown += (_, e) => {
            PushUndo();
            RemoveAnnotationRect(annotRect);
            if (_selectedAnnotRect == annotRect) { _selectedAnnotRect = null; HideColorPicker(); }
            e.Handled = true;
        };

        hitZone.MouseLeftButtonDown += (_, e) => {
            if (_draggingAnnotRect != null || _inArrowMode || _inRectMode) return;
            // Only initiate drag when clicking on or near the border edge — not the interior.
            if (!IsOnRectBorder(annotRect.Bounds, e.GetPosition(_canvas), 6.0)) return;
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
        hitZone.MouseMove += (_, e) => {
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
        hitZone.MouseLeftButtonUp += (_, e) => {
            if (_draggingAnnotRect != annotRect || !_annotRectBodyDragging) return;
            CommitDragUndo();
            _draggingAnnotRect = null;
            _annotRectBodyDragging = false;
            hitZone.ReleaseMouseCapture();
            e.Handled = true;
        };
        hitZone.MouseRightButtonDown += (_, e) => {
            PushUndo();
            RemoveAnnotationRect(annotRect);
            if (_selectedAnnotRect == annotRect) { _selectedAnnotRect = null; HideColorPicker(); }
            e.Handled = true;
        };

        for (int i = 0; i < 8; i++) {
            int handleIdx = i;
            var handle = handles[i];

            handle.MouseLeftButtonDown += (_, e) => {
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
            handle.MouseMove += (_, e) => {
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
            handle.MouseLeftButtonUp += (_, e) => {
                if (_draggingAnnotRect != annotRect || _annotRectBodyDragging || _draggingAnnotRectHandleIdx != handleIdx) return;
                CommitDragUndo();
                // Remember the final size so the next click-without-drag uses it.
                _lastDragRectWidth = annotRect.Bounds.Width;
                _lastDragRectHeight = annotRect.Bounds.Height;
                _draggingAnnotRect = null;
                _draggingAnnotRectHandleIdx = -1;
                handle.ReleaseMouseCapture();
                e.Handled = true;
            };
            handle.MouseRightButtonDown += (_, e) => {
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

    private static void UpdateRectGeometry(AnnotationRect rect) {
        var b = rect.Bounds;
        var brush = new SolidColorBrush(rect.RectColor);
        rect.Border.Stroke = brush;

        Canvas.SetLeft(rect.Shadow, b.Left + 2);
        Canvas.SetTop(rect.Shadow, b.Top + 2);
        rect.Shadow.Width = b.Width;
        rect.Shadow.Height = b.Height;

        Canvas.SetLeft(rect.Border, b.Left);
        Canvas.SetTop(rect.Border, b.Top);
        rect.Border.Width = b.Width;
        rect.Border.Height = b.Height;

        // Handles: NW(0), NE(1), SW(2), SE(3), N(4), S(5), W(6), E(7)
        PlaceRectHandle(rect.Handles[0], b.Left, b.Top);
        PlaceRectHandle(rect.Handles[1], b.Right, b.Top);
        PlaceRectHandle(rect.Handles[2], b.Left, b.Bottom);
        PlaceRectHandle(rect.Handles[3], b.Right, b.Bottom);
        PlaceRectHandle(rect.Handles[4], b.Left + b.Width / 2, b.Top);
        PlaceRectHandle(rect.Handles[5], b.Left + b.Width / 2, b.Bottom);
        PlaceRectHandle(rect.Handles[6], b.Left, b.Top + b.Height / 2);
        PlaceRectHandle(rect.Handles[7], b.Right, b.Top + b.Height / 2);

        if (rect.HitZoneRect != null) {
            const double hp = 3.0;
            Canvas.SetLeft(rect.HitZoneRect, b.Left - hp);
            Canvas.SetTop(rect.HitZoneRect, b.Top - hp);
            rect.HitZoneRect.Width = b.Width + hp * 2;
            rect.HitZoneRect.Height = b.Height + hp * 2;
        }
    }

    private static void PlaceRectHandle(Rectangle h, double cx, double cy) {
        Canvas.SetLeft(h, cx - 4);
        Canvas.SetTop(h, cy - 4);
    }

    private static HitZone HitTestAnnotRect(AnnotationRect r, Point pt) {
        const double ep = 6.0;
        var b = r.Bounds;

        // Corners (check first — tighter region)
        if (Math.Abs(pt.X - b.Left) <= ep && Math.Abs(pt.Y - b.Top) <= ep) return HitZone.NW;
        if (Math.Abs(pt.X - b.Right) <= ep && Math.Abs(pt.Y - b.Top) <= ep) return HitZone.NE;
        if (Math.Abs(pt.X - b.Left) <= ep && Math.Abs(pt.Y - b.Bottom) <= ep) return HitZone.SW;
        if (Math.Abs(pt.X - b.Right) <= ep && Math.Abs(pt.Y - b.Bottom) <= ep) return HitZone.SE;

        // Edges
        if (Math.Abs(pt.Y - b.Top) <= ep && pt.X > b.Left && pt.X < b.Right) return HitZone.N;
        if (Math.Abs(pt.Y - b.Bottom) <= ep && pt.X > b.Left && pt.X < b.Right) return HitZone.S;
        if (Math.Abs(pt.X - b.Left) <= ep && pt.Y > b.Top && pt.Y < b.Bottom) return HitZone.W;
        if (Math.Abs(pt.X - b.Right) <= ep && pt.Y > b.Top && pt.Y < b.Bottom) return HitZone.E;

        if (b.Contains(pt)) return HitZone.Move;
        return HitZone.None;
    }

    /// <summary>
    /// Returns true when <paramref name="pt"/> lies within <paramref name="tol"/> pixels of any
    /// of the four edges of <paramref name="bounds"/> but NOT solidly in the interior.
    /// Used to restrict rect annotation dragging and hover cursors to the visible border only.
    /// </summary>
    private static bool IsOnRectBorder(Rect bounds, Point pt, double tol) {
        // Must be within the outer envelope (rect expanded by tol on every side).
        if (pt.X < bounds.Left - tol || pt.X > bounds.Right + tol ||
            pt.Y < bounds.Top - tol || pt.Y > bounds.Bottom + tol)
            return false;

        // If the point is further than tol from EVERY edge it is solidly in the interior.
        var innerLeft = bounds.Left + tol;
        var innerTop = bounds.Top + tol;
        var innerRight = bounds.Right - tol;
        var innerBottom = bounds.Bottom - tol;
        return !(innerLeft < innerRight && innerTop < innerBottom &&
                 pt.X > innerLeft && pt.X < innerRight &&
                 pt.Y > innerTop && pt.Y < innerBottom);
    }

    /// <summary>
    /// Euclidean distance from <paramref name="p"/> to the nearest point on segment
    /// <paramref name="a"/>→<paramref name="b"/>.
    /// </summary>
    private static double PointToSegmentDist(Point p, Point a, Point b) {
        var dx = b.X - a.X;
        var dy = b.Y - a.Y;
        if (dx == 0.0 && dy == 0.0)
            return Math.Sqrt((p.X - a.X) * (p.X - a.X) + (p.Y - a.Y) * (p.Y - a.Y));
        var t = Math.Max(0.0, Math.Min(1.0,
            ((p.X - a.X) * dx + (p.Y - a.Y) * dy) / (dx * dx + dy * dy)));
        var cx = a.X + t * dx;
        var cy = a.Y + t * dy;
        return Math.Sqrt((p.X - cx) * (p.X - cx) + (p.Y - cy) * (p.Y - cy));
    }

    /// <summary>
    /// Returns true when <paramref name="pt"/> is close enough to any draggable annotation
    /// (arrow shaft/head, rect border, or cursor indicator) to warrant showing
    /// <see cref="Cursors.Arrow"/> as a hover cue.
    /// </summary>
    private bool IsHoveringOverAnnotation(Point pt) {
        const double ArrowTol = 6.0;
        const double RectTol = 6.0;
        const double CursorTol = 16.0;

        // Arrows: check proximity to shaft segment and arrowhead tip.
        foreach (var arrow in _arrows) {
            if (PointToSegmentDist(pt,
                    new Point(arrow.Line.X1, arrow.Line.Y1),
                    new Point(arrow.Line.X2, arrow.Line.Y2)) <= ArrowTol)
                return true;

            if (arrow.Head.Points.Count > 0) {
                var tip = arrow.Head.Points[0];
                var tdx = pt.X - tip.X; var tdy = pt.Y - tip.Y;
                if (Math.Sqrt(tdx * tdx + tdy * tdy) <= ArrowTol) return true;
            }
        }

        // Rect annotation borders (not interior).
        foreach (var r in _annotRects) {
            if (IsOnRectBorder(r.Bounds, pt, RectTol)) return true;
        }

        // Cursor indicator image.
        if (_cursorEnabled && _cursorImage != null) {
            var cx = Canvas.GetLeft(_cursorImage);
            var cy = Canvas.GetTop(_cursorImage);
            var cdx = pt.X - cx; var cdy = pt.Y - cy;
            if (Math.Sqrt(cdx * cdx + cdy * cdy) <= CursorTol) return true;
        }

        // Text annotation labels (allow drag/select click anywhere inside bounds).
        foreach (var t in _texts) {
            if (t.Bounds.Contains(pt)) return true;
        }

        return false;
    }

    private void RemoveAnnotationRect(AnnotationRect rect) {
        if (!_suppressUndo) PushUndo();
        if (rect == _colorPickerRect) HideColorPicker();
        _canvas.Children.Remove(rect.Shadow);
        _canvas.Children.Remove(rect.Border);
        _canvas.Children.Remove(rect.HitZoneRect);
        foreach (var h in rect.Handles) _canvas.Children.Remove(h);
        _annotRects.Remove(rect);
    }

    private void SelectAnnotationRect(AnnotationRect? rect) {
        if (_selectedAnnotRect != null && _selectedAnnotRect != rect)
            foreach (var h in _selectedAnnotRect.Handles) h.Visibility = Visibility.Hidden;

        _selectedAnnotRect = rect;
        if (rect != null) {
            if (_selectedText != null) SelectText(null);
            SelectArrow(null);
            foreach (var h in rect.Handles) h.Visibility = Visibility.Visible;
            ShowColorPickerForRect(rect);
        }
        else {
            HideColorPicker();
        }
    }

    private void ShowColorPickerForRect(AnnotationRect rect) {
        HideColorPicker();
        _colorPickerRect = rect;
        var palette = GetArrowPalette();

        _colorPickerPanel = new StackPanel { Orientation = Orientation.Horizontal };
        Panel.SetZIndex(_colorPickerPanel, 300);

        foreach (var color in palette) {
            var c = color;
            bool isSelected = c == rect.RectColor;
            var swatch = MakeColorSwatch(c, isSelected, picked => {
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

    private void ToggleCursorOverlay(bool enabled) {
        _cursorEnabled = enabled;
        if (!enabled && _cursorImage != null) {
            _cursorImage.Visibility = Visibility.Collapsed;
            _draggingCursor = false;
        }
    }

    private void EnsureCursorImageCreated() {
        if (_cursorImage != null) return;

        _cursorImage = new Image {
            Width = 16,
            Height = 26,
            Source = (DrawingImage)Application.Current.FindResource("ImageEditorCursorDrawing"),
            Cursor = Cursors.SizeAll,
            IsHitTestVisible = true
        };
        Panel.SetZIndex(_cursorImage, 100);

        _cursorImage.MouseLeftButtonDown += (_, e) => {
            _preDragSnapshot = CaptureSnapshot();
            _draggingCursor = true;
            _cursorImage!.CaptureMouse();
            e.Handled = true;
        };
        _cursorImage.MouseMove += (_, e) => {
            if (!_draggingCursor) return;
            var pt = e.GetPosition(_canvas);
            double x, y;
            if (_sel.IsEmpty) {
                x = Math.Max(0, Math.Min(pt.X, _canvas.Width - 20));
                y = Math.Max(0, Math.Min(pt.Y, _canvas.Height - 24));
            }
            else {
                x = Math.Max(_sel.Left, Math.Min(pt.X, _sel.Right - 20));
                y = Math.Max(_sel.Top, Math.Min(pt.Y, _sel.Bottom - 24));
            }
            Canvas.SetLeft(_cursorImage!, x);
            Canvas.SetTop(_cursorImage!, y);
            e.Handled = true;
        };
        _cursorImage.MouseLeftButtonUp += (_, e) => {
            if (!_draggingCursor) return;
            CommitDragUndo();
            _draggingCursor = false;
            _cursorImage!.ReleaseMouseCapture();
            e.Handled = true;
        };

        _canvas.Children.Add(_cursorImage);
    }

    private void PlaceCursorAtPoint(Point pt) {
        PushUndo();
        double x, y;
        if (!_sel.IsEmpty) {
            x = Math.Max(_sel.Left, Math.Min(pt.X, _sel.Right - 20));
            y = Math.Max(_sel.Top, Math.Min(pt.Y, _sel.Bottom - 24));
        }
        else {
            x = pt.X;
            y = pt.Y;
        }
        EnsureCursorImageCreated();
        Canvas.SetLeft(_cursorImage!, x);
        Canvas.SetTop(_cursorImage!, y);
        _cursorImage!.Visibility = Visibility.Visible;
        _inCursorPlacementMode = false;
        HideModeHint();
        EnterMoveMode();
    }

    // ── Mode hint ─────────────────────────────────────────────────────────────

    private void ShowModeHint(string text) {
        if (_modeHintBorder == null) {
            _modeHintText = new TextBlock {
                FontSize = (double)Application.Current.Resources["FontSizeSmall"],
                FontFamily = new FontFamily("Segoe UI"),
                Foreground = new SolidColorBrush(Color.FromArgb(0xFF, 0xFF, 0xFF, 0xFF)),
                TextAlignment = TextAlignment.Center,
                IsHitTestVisible = false
            };
            _modeHintBorder = new Border {
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

        // Cancel any running fade/timer and reset to fully opaque.
        _modeHintFadeTimer?.Stop();
        _modeHintBorder.BeginAnimation(UIElement.OpacityProperty, null);
        _modeHintBorder.Opacity = 1.0;

        _modeHintText!.Text = text;
        _modeHintBorder.Visibility = Visibility.Visible;
        PositionModeHint();
        Dispatcher.InvokeAsync(PositionModeHint, DispatcherPriority.Loaded);

        // After 5 seconds, fade out over 1.5 seconds then collapse.
        _modeHintFadeTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(5) };
        _modeHintFadeTimer.Tick += (_, _) => {
            _modeHintFadeTimer!.Stop();
            if (_modeHintBorder == null) return;
            var anim = new DoubleAnimation(1.0, 0.0, new Duration(TimeSpan.FromSeconds(1.5))) {
                FillBehavior = FillBehavior.HoldEnd
            };
            anim.Completed += (_, _) => {
                if (_modeHintBorder != null) {
                    _modeHintBorder.BeginAnimation(UIElement.OpacityProperty, null);
                    _modeHintBorder.Opacity = 1.0;
                    _modeHintBorder.Visibility = Visibility.Collapsed;
                }
            };
            _modeHintBorder.BeginAnimation(UIElement.OpacityProperty, anim);
        };
        _modeHintFadeTimer.Start();
    }

    private void HideModeHint() {
        _modeHintFadeTimer?.Stop();
        if (_modeHintBorder != null) {
            _modeHintBorder.BeginAnimation(UIElement.OpacityProperty, null);
            _modeHintBorder.Opacity = 1.0;
            _modeHintBorder.Visibility = Visibility.Collapsed;
        }
    }

    private void PositionModeHint() {
        if (_modeHintBorder == null) return;
        _modeHintBorder.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        var w = _modeHintBorder.DesiredSize.Width;
        if (_sel.IsEmpty) {
            Canvas.SetLeft(_modeHintBorder, Math.Max(0, (_canvas.Width - w) / 2));
            Canvas.SetTop(_modeHintBorder, 8);
            return;
        }
        var cx = _sel.Left + _sel.Width / 2;
        Canvas.SetLeft(_modeHintBorder, Math.Max(_sel.Left, cx - w / 2));
        Canvas.SetTop(_modeHintBorder, _sel.Top + 8);
    }

    // ── Eyedropper ────────────────────────────────────────────────────────────

    private void EnterEyedropperMode() {
        _inMoveMode = false;
        _inCropMode = false;
        _inEyedropperMode = true;
        _canvas.Cursor = AnnotationCursors.EyedropperTool;
        ShowModeHint("Hover to preview color — click to capture");
    }

    private void ExitEyedropperMode() {
        _inEyedropperMode = false;
        _canvas.Cursor = Cursors.Arrow;
        HideModeHint();
        HideEyedropperTooltip();
        if (_eyedropperBtn != null) _eyedropperBtn.Content = MakeToolIcon("ImageEditorEyedropperIcon");
    }

    private void CachePixels() {
        var conv = new FormatConvertedBitmap(_workingImage, PixelFormats.Bgra32, null, 0);
        _cachedStride = conv.PixelWidth * 4;
        _cachedPixels = new byte[_cachedStride * conv.PixelHeight];
        conv.CopyPixels(_cachedPixels, _cachedStride, 0);
    }

    private Color SamplePixelAtCanvasPoint(Point pt) {
        if (_cachedPixels == null) CachePixels();
        // Canvas coordinates are in logical (96-dpi) units; convert to image pixels.
        int px = (int)Math.Max(0, Math.Min(pt.X * _canvasScaleX, _workingImage.PixelWidth - 1));
        int py = (int)Math.Max(0, Math.Min(pt.Y * _canvasScaleY, _workingImage.PixelHeight - 1));
        int offset = py * _cachedStride + px * 4;
        byte bv = _cachedPixels![offset];
        byte gv = _cachedPixels[offset + 1];
        byte rv = _cachedPixels[offset + 2];
        return Color.FromRgb(rv, gv, bv);
    }

    private static (double H, double S, double L) RgbToHsl(Color c) {
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

    private void ShowEyedropperTooltip(Point pt, Color color) {
        if (_eyedropperTooltipBorder == null) {
            _eyedropperTooltipSwatch = new Border {
                Width = 36,
                Height = 36,
                CornerRadius = new CornerRadius(3),
                Margin = new Thickness(0, 0, 8, 0),
                BorderThickness = new Thickness(1),
                BorderBrush = new SolidColorBrush(Color.FromArgb(0x60, 0xFF, 0xFF, 0xFF)),
                IsHitTestVisible = false
            };
            _eyedropperTooltipRgbText = new TextBlock {
                FontSize = (double)Application.Current.Resources["FontSizeNormal"],
                FontFamily = new FontFamily("Consolas"),
                Foreground = Brushes.White,
                IsHitTestVisible = false
            };
            _eyedropperTooltipHslText = new TextBlock {
                FontSize = (double)Application.Current.Resources["FontSizeNormal"],
                FontFamily = new FontFamily("Consolas"),
                Foreground = Brushes.White,
                IsHitTestVisible = false,
                Margin = new Thickness(0, 5, 0, 0)
            };
            var textStack = new StackPanel {
                Orientation = Orientation.Vertical,
                IsHitTestVisible = false,
                VerticalAlignment = VerticalAlignment.Center
            };
            textStack.Children.Add(_eyedropperTooltipRgbText);
            textStack.Children.Add(_eyedropperTooltipHslText);
            var row = new StackPanel {
                Orientation = Orientation.Horizontal,
                IsHitTestVisible = false
            };
            row.Children.Add(_eyedropperTooltipSwatch);
            row.Children.Add(textStack);
            _eyedropperTooltipBorder = new Border {
                Background = new SolidColorBrush(Color.FromArgb(0xCC, 0x10, 0x10, 0x10)),
                CornerRadius = new CornerRadius(4),
                Padding = new Thickness(6, 4, 6, 4),
                Child = row,
                IsHitTestVisible = false,
                Visibility = Visibility.Collapsed
            };
            Panel.SetZIndex(_eyedropperTooltipBorder, 200);
            _overlayCanvas.Children.Add(_eyedropperTooltipBorder);
        }
        _eyedropperTooltipSwatch!.Background = new SolidColorBrush(color);
        var (h, s, l) = RgbToHsl(color);
        _eyedropperTooltipRgbText!.Text = $"R:{color.R}  G:{color.G}  B:{color.B}";
        _eyedropperTooltipHslText!.Text = $"H:{h:F0}°  S:{s:F0}%  L:{l:F0}%";
        _eyedropperTooltipBorder.Visibility = Visibility.Visible;
        _eyedropperTooltipBorder.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        var tw = _eyedropperTooltipBorder.DesiredSize.Width;
        var th = _eyedropperTooltipBorder.DesiredSize.Height;
        // Find where _canvas origin sits inside _overlayCanvas (accounts for centering + margin after crop).
        var canvasOrigin = _canvas.TranslatePoint(new Point(0, 0), _overlayCanvas);
        double ox = canvasOrigin.X;
        double oy = canvasOrigin.Y;
        double tx = ox + pt.X * _zoom + 14;
        double ty = oy + pt.Y * _zoom - th - 6;
        double canvasScreenWidth = _canvas.Width * _zoom;
        if (tx + tw > ox + canvasScreenWidth) tx = ox + pt.X * _zoom - tw - 6;
        if (ty < oy) ty = oy + pt.Y * _zoom + 14;
        Canvas.SetLeft(_eyedropperTooltipBorder, tx);
        Canvas.SetTop(_eyedropperTooltipBorder, ty);
    }

    private void HideEyedropperTooltip() {
        if (_eyedropperTooltipBorder != null)
            _eyedropperTooltipBorder.Visibility = Visibility.Collapsed;
    }

    private void UpdateEyedropperResult(Color color) {
        if (_eyedropperSwatch != null) {
            _eyedropperSwatch.Background = new SolidColorBrush(color);
            _eyedropperSwatch.Visibility = Visibility.Visible;
        }
        if (_eyedropperHexLabel != null)
            _eyedropperHexLabel.Text = $"#{color.R:X2}{color.G:X2}{color.B:X2}";
    }

    // ── Text annotations ──────────────────────────────────────────────────────

    private void EnterTextMode() {
        if (_selectedText != null) SelectText(null);
        _inMoveMode = false;
        _inCropMode = false;
        _inTextMode = true;
        _canvas.Cursor = AnnotationCursors.TextTool;
        ShowModeHint("Click to place text · ESC to exit");
    }

    private void ExitTextMode(bool returnToMove = false) {
        CommitActiveTextBox();
        _inTextMultiDropMode = false;
        _inTextMode = false;
        _canvas.Cursor = Cursors.Arrow;
        HideModeHint();
        if (_addTextBtn != null) _addTextBtn.Content = MakeToolIcon("ImageEditorTextIcon");
        if (returnToMove) EnterMoveMode();
    }

    /// <summary>
    /// Creates a new text annotation at <paramref name="pt"/> and opens a TextBox overlay for editing.
    /// </summary>
    private void BeginEditText(Point pt) {
        PushUndo(); // save state before the annotation is born
        var annotation = new AnnotationText {
            FontSize = AnnotationText.MaxFontSize,
            TextColor = _defaultTextFgColor,
            BackgroundColor = _defaultTextBgColor
        };
        // Shift up so the click point is the baseline (cap-height ≈ FontSize × 0.85)
        var adjustedPt = new Point(pt.X, Math.Max(0, pt.Y - annotation.FontSize * 0.85));
        annotation.Bounds = new Rect(adjustedPt.X, adjustedPt.Y, 0, 0);
        _texts.Add(annotation);
        _editingText = annotation;
        CreateTextBoxOverlay(annotation);
        // Exit text mode immediately so cursor and toolbar revert; the TextBox manages its own lifecycle.
        if (!_inTextMultiDropMode) {
            _inTextMode = false;
            _canvas.Cursor = Cursors.Arrow;
            HideModeHint();
            if (_addTextBtn != null) _addTextBtn.Content = MakeToolIcon("ImageEditorTextIcon");
            EnterMoveMode();
        }
    }

    /// <summary>
    /// Creates a width-constrained text annotation.The drag width defines the text box width;
    /// text wraps within that fixed width.
    /// </summary>
    private void BeginEditTextWithWidth(Point topLeft, double width) {
        PushUndo();
        var fixedWidth = Math.Max(60, width);
        var annotation = new AnnotationText {
            Bounds = new Rect(topLeft.X, topLeft.Y, fixedWidth, 0),
            FontSize = AnnotationText.MaxFontSize,
            TextColor = _defaultTextFgColor,
            BackgroundColor = _defaultTextBgColor
        };
        _texts.Add(annotation);
        _editingText = annotation;

        var tb = new TextBox {
            FontFamily = new FontFamily("Calibri"),
            FontSize = annotation.FontSize,
            FontWeight = FontWeights.Bold,
            Foreground = new SolidColorBrush(annotation.TextColor),
            Background = annotation.BackgroundColor.A == 0
                ? new SolidColorBrush(Color.FromArgb(40, 0, 0, 0))
                : new SolidColorBrush(annotation.BackgroundColor),
            BorderBrush = new SolidColorBrush(Color.FromArgb(180, 255, 255, 255)),
            BorderThickness = new Thickness(1),
            AcceptsReturn = false,
            TextWrapping = TextWrapping.Wrap,
            Width = fixedWidth,
            MinWidth = 60,
            Padding = new Thickness(4, 2, 4, 2),
            CaretBrush = annotation.BackgroundColor.A > 0 && annotation.BackgroundColor.R < 128
                ? Brushes.White
                : Brushes.Black,
            SelectionBrush = new SolidColorBrush(Color.FromArgb(120, 100, 160, 255)),
            Text = annotation.Text
        };

        tb.KeyDown += (_, e) => {
            if (e.Key is Key.Return or Key.Enter) {
                CommitActiveTextBox();
                e.Handled = true;
            }
            else if (e.Key == Key.Escape) {
                var editingCopy = _editingText;
                var wasNew = string.IsNullOrEmpty(editingCopy?.Text ?? string.Empty);
                var tbRef2 = _activeTextBox;
                _activeTextBox = null;
                _editingText = null;
                _canvas.Children.Remove(tbRef2);
                if (wasNew && editingCopy != null)
                    _texts.Remove(editingCopy);
                else if (editingCopy?.Display != null) {
                    editingCopy.Display.Visibility = Visibility.Visible;
                    if (editingCopy.Shadow != null) editingCopy.Shadow.Visibility = Visibility.Visible;
                }
                if (_undoStack.Count > 0) _undoStack.Pop();
                e.Handled = true;
            }
        };

        tb.LostFocus += (_, _) => {
            if (_activeTextBox == tb)
                CommitActiveTextBox();
        };

        Canvas.SetLeft(tb, annotation.Bounds.Left);
        Canvas.SetTop(tb, annotation.Bounds.Top);
        Panel.SetZIndex(tb, 200);
        _canvas.Children.Add(tb);
        _activeTextBox = tb;

        tb.Focus();
        tb.SelectAll();
        // Exit text mode immediately so cursor and toolbar revert; the TextBox manages its own lifecycle.
        if (!_inTextMultiDropMode) {
            _inTextMode = false;
            _canvas.Cursor = Cursors.Arrow;
            HideModeHint();
            if (_addTextBtn != null) _addTextBtn.Content = MakeToolIcon("ImageEditorTextIcon");
            EnterMoveMode();
        }

        // Show resize handles around the active TextBox immediately (same as CreateTextBoxOverlay).
        AddTextResizeHandles(annotation);
        tb.Loaded += (_, _) =>
            Dispatcher.BeginInvoke(DispatcherPriority.Render, () => PositionTextResizeHandles(annotation));
        tb.SizeChanged += (_, _) =>
            Dispatcher.BeginInvoke(DispatcherPriority.Render, () => PositionTextResizeHandles(annotation));

        // Show the color picker immediately so the user can set bg/fg color as they type.
        ShowColorPickerForText(annotation);
    }
    /// Double-clicking a text label calls this.
    /// </summary>
    private void BeginEditText(AnnotationText existing) {
        PushUndo(); // save state in case the user actually changes the text
        _editingText = existing;
        if (existing.Display != null) existing.Display.Visibility = Visibility.Collapsed;
        if (existing.Shadow != null) existing.Shadow.Visibility = Visibility.Collapsed;
        if (!_inTextMode) {
            ExitAllToolModes();
            _inTextMode = true;
            _canvas.Cursor = AnnotationCursors.TextTool;
            if (_addTextBtn != null) _addTextBtn.Content = MakeToolIcon("ImageEditorTextIcon", active: true);
            ShowModeHint("Click to place text · ESC to exit");
        }
        CreateTextBoxOverlay(existing);
    }

    /// <summary>
    /// Places a live TextBox on the canvas at the annotation's current position.
    /// </summary>
    private void CreateTextBoxOverlay(AnnotationText annotation) {
        var tb = new TextBox {
            FontFamily = new FontFamily("Calibri"),
            FontSize = annotation.FontSize,
            FontWeight = FontWeights.Bold,
            Foreground = new SolidColorBrush(annotation.TextColor),
            Background = annotation.BackgroundColor.A == 0
                ? new SolidColorBrush(Color.FromArgb(40, 0, 0, 0))
                : new SolidColorBrush(annotation.BackgroundColor),
            BorderBrush = new SolidColorBrush(Color.FromArgb(180, 255, 255, 255)),
            BorderThickness = new Thickness(1),
            AcceptsReturn = false,
            TextWrapping = TextWrapping.NoWrap,
            MinWidth = 60,
            Padding = new Thickness(4, 2, 4, 2),
            CaretBrush = annotation.BackgroundColor.A > 0 && annotation.BackgroundColor.R < 128
                ? Brushes.White
                : Brushes.Black,
            SelectionBrush = new SolidColorBrush(Color.FromArgb(120, 100, 160, 255)),
            Text = annotation.Text
        };

        // Auto-shrink/grow font to keep text within canvas right edge.
        tb.TextChanged += (_, _) => {
            if (string.IsNullOrEmpty(tb.Text)) { tb.FontSize = AnnotationText.MaxFontSize; return; }
            double maxW = _canvas.Width - annotation.Bounds.Left - 8;
            AdjustTextFontSize(tb, Math.Max(80, maxW));
        };

        // Enter = commit; ESC = cancel (handled before Window_KeyDown sees them).
        tb.KeyDown += (_, e) => {
            if (e.Key is Key.Return or Key.Enter) {
                CommitActiveTextBox();
                e.Handled = true;
            }
            else if (e.Key == Key.Escape) {
                var editingCopy = _editingText;
                var wasNew = string.IsNullOrEmpty(editingCopy?.Text ?? string.Empty);
                var tbRef = _activeTextBox;

                // Clear state before removing from canvas (prevents LostFocus re-entry).
                _activeTextBox = null;
                _editingText = null;
                _canvas.Children.Remove(tbRef);

                if (wasNew && editingCopy != null) {
                    _texts.Remove(editingCopy); // never committed — no visuals to remove
                }
                else if (editingCopy?.Display != null) {
                    editingCopy.Display.Visibility = Visibility.Visible;
                    if (editingCopy.Shadow != null) editingCopy.Shadow.Visibility = Visibility.Visible;
                }

                // Discard the BeginEditText undo push — nothing actually changed.
                if (_undoStack.Count > 0) _undoStack.Pop();
                RemoveTextResizeHandles();
                e.Handled = true;
            }
        };

        // LostFocus (e.g. clicking elsewhere) commits the annotation.
        tb.LostFocus += (_, _) => {
            if (_activeTextBox == tb)
                CommitActiveTextBox();
        };

        Canvas.SetLeft(tb, annotation.Bounds.Left);
        Canvas.SetTop(tb, annotation.Bounds.Top);
        Panel.SetZIndex(tb, 200);
        _canvas.Children.Add(tb);
        _activeTextBox = tb;

        tb.Focus();
        tb.CaretIndex = tb.Text.Length; // place caret at end; SelectAll can be distracting for re-edit
        if (string.IsNullOrEmpty(annotation.Text)) tb.SelectAll();

        // Show resize handles immediately around the active TextBox so the user can see them
        // as soon as the text box is placed — before typing or committing anything.
        AddTextResizeHandles(annotation);
        tb.Loaded += (_, _) =>
            Dispatcher.BeginInvoke(DispatcherPriority.Render, () => PositionTextResizeHandles(annotation));
        tb.SizeChanged += (_, _) =>
            Dispatcher.BeginInvoke(DispatcherPriority.Render, () => PositionTextResizeHandles(annotation));

        // Show the color picker immediately so the user can set bg/fg color as they type.
        ShowColorPickerForText(annotation);
    }

    /// <summary>
    /// Commits the active TextBox: writes back text/fontSize/bounds to the annotation,
    /// updates the display TextBlock, and removes the TextBox from the canvas.
    /// If the text is empty the annotation is silently discarded.
    /// </summary>
    private void CommitActiveTextBox() {
        if (_activeTextBox == null || _editingText == null) return;

        if (_textBoxPttAttachment.IsActive)
            _ = _textBoxPttAttachment.StopAsync();

        var text = _activeTextBox.Text;
        var editCopy = _editingText;
        var tbRef = _activeTextBox;
        bool autoExit = _inTextMode && !_inTextMultiDropMode;
        bool inMultiDrop = _inTextMultiDropMode;

        _activeTextBox = null;
        _editingText = null;
        _canvas.Children.Remove(tbRef);

        if (string.IsNullOrWhiteSpace(text)) {
            _suppressUndo = true;
            try { _texts.Remove(editCopy); }
            finally { _suppressUndo = false; }
            if (autoExit) ExitTextMode(returnToMove: true);
        }
        else {
            editCopy.Text = text;
            editCopy.FontSize = tbRef.FontSize;
            editCopy.Bounds = new Rect(Canvas.GetLeft(tbRef), Canvas.GetTop(tbRef), double.IsNaN(tbRef.Width) ? 0 : tbRef.Width, 0);
            UpdateTextDisplay(editCopy);
            if (autoExit) ExitTextMode(returnToMove: true);
            // Select the committed annotation (unless in multi-drop mode) so resize handles appear.
            // But if the commit was triggered by a click on empty canvas (_pendingTextCommitDeselect),
            // deselect instead so the handles disappear — that is the user's intent when clicking away.
            if (!inMultiDrop) {
                if (_pendingTextCommitDeselect) {
                    // Click-away on empty canvas: commit the text but clear selection so handles go away.
                    _pendingTextCommitDeselect = false;
                    SelectText(null);
                }
                else {
                    // Enter key / explicit exit: keep annotation selected so resize handles stay visible
                    // and the user can immediately reposition or resize without a second click.
                    _suppressNextTextDeselect = true;
                    SelectText(editCopy);
                }
            }
        }
    }

    /// <summary>
    /// Creates or updates the shadow + display TextBlocks for a committed annotation.
    /// </summary>
    private void UpdateTextDisplay(AnnotationText annotation) {
        // During active editing of a brand-new annotation (Display not yet created),
        // update the live TextBox colors directly instead of creating Display/Shadow early.
        if (annotation.Display == null && _editingText == annotation && _activeTextBox != null) {
            _activeTextBox.Foreground = new SolidColorBrush(annotation.TextColor);
            _activeTextBox.Background = annotation.BackgroundColor.A == 0
                ? new SolidColorBrush(Color.FromArgb(40, 0, 0, 0))
                : new SolidColorBrush(annotation.BackgroundColor);
            _activeTextBox.CaretBrush = annotation.BackgroundColor.A > 0 && annotation.BackgroundColor.R < 128
                ? Brushes.White
                : Brushes.Black;
            return;
        }

        if (annotation.Display == null) {
            var shadow = new TextBlock {
                FontFamily = new FontFamily("Calibri"),
                FontWeight = FontWeights.Bold,
                Foreground = new SolidColorBrush(Color.FromArgb(160, 0, 0, 0)),
                IsHitTestVisible = false,
                Visibility = annotation.BackgroundColor.A == 0 ? Visibility.Visible : Visibility.Collapsed,
            };

            var display = new TextBlock {
                FontFamily = new FontFamily("Calibri"),
                FontWeight = FontWeights.Bold,
                Foreground = new SolidColorBrush(annotation.TextColor),
                IsHitTestVisible = true,
                Cursor = Cursors.SizeAll,
                Background = annotation.BackgroundColor.A == 0
                    ? Brushes.Transparent
                    : new SolidColorBrush(annotation.BackgroundColor),
                Padding = annotation.BackgroundColor.A > 0
                    ? new Thickness(4, 1, 4, 2)
                    : new Thickness(0),
            };

            // Local drag state — captured per-annotation in the closure.
            Point dragStart = default;
            Rect dragOrigBounds = default;
            bool isDragging = false;

            display.MouseLeftButtonDown += (_, e) => {
                // In tool placement/eyedropper modes, let event bubble to canvas
                if (_inCursorPlacementMode || _inEyedropperMode) return;

                if (e.ClickCount == 2) {
                    BeginEditText(annotation);
                    e.Handled = true;
                    return;
                }
                if (!_inTextMode && _selectedText != annotation)
                    SelectText(annotation);
                _preDragSnapshot = CaptureSnapshot();
                isDragging = true;
                dragStart = e.GetPosition(_canvas);
                dragOrigBounds = annotation.Bounds;
                display.CaptureMouse();
                SquadDashTrace.Write("AnnotatorDrag", $"TextAnnotation drag start at ({dragStart.X:F0},{dragStart.Y:F0}) captured={Mouse.Captured?.GetType().Name ?? "null"}");
                e.Handled = true;
            };
            display.MouseMove += (_, e) => {
                if (isDragging) {
                    var pt = e.GetPosition(_canvas);
                    var newX = Math.Max(0, Math.Min(dragOrigBounds.X + (pt.X - dragStart.X), _canvas.Width - 20));
                    var newY = Math.Max(0, Math.Min(dragOrigBounds.Y + (pt.Y - dragStart.Y), _canvas.Height - 16));
                    annotation.Bounds = new Rect(newX, newY, annotation.Bounds.Width, annotation.Bounds.Height);
                    Canvas.SetLeft(display, newX);
                    Canvas.SetTop(display, newY);
                    Canvas.SetLeft(shadow, newX + 1.5);
                    Canvas.SetTop(shadow, newY + 1.5);
                    UpdateTextSelectionBorder();
                    e.Handled = true;
                    return;
                }
                // Body of annotation always shows the move cursor — resize handles show their own cursors.
            };
            display.MouseLeftButtonUp += (_, e) => {
                if (!isDragging) return;
                SquadDashTrace.Write("AnnotatorDrag", $"TextAnnotation drag end at ({e.GetPosition(_canvas).X:F0},{e.GetPosition(_canvas).Y:F0}) → bounds=({annotation.Bounds.X:F0},{annotation.Bounds.Y:F0})");
                CommitDragUndo();
                isDragging = false;
                display.ReleaseMouseCapture();
                // Explicitly re-select so resize handles remain visible after drag.
                SelectText(annotation);
                e.Handled = true;
            };
            display.MouseRightButtonDown += (_, e) => {
                if (_selectedText == annotation) SelectText(null);
                PushUndo();
                RemoveTextAnnotation(annotation);
                e.Handled = true;
            };

            annotation.Shadow = shadow;
            annotation.Display = display;
            Panel.SetZIndex(shadow, 19);
            Panel.SetZIndex(display, 20);
            _canvas.Children.Add(shadow);
            _canvas.Children.Add(display);
        }

        annotation.Display.Text = annotation.Text;
        annotation.Display.FontSize = annotation.FontSize;
        annotation.Shadow!.Text = annotation.Text;
        annotation.Shadow.FontSize = annotation.FontSize;

        // Update colors (handles re-render after color change)
        annotation.Display.Foreground = new SolidColorBrush(annotation.TextColor);
        annotation.Display.Background = annotation.BackgroundColor.A == 0
            ? Brushes.Transparent
            : new SolidColorBrush(annotation.BackgroundColor);
        annotation.Display.Padding = annotation.BackgroundColor.A > 0
            ? new Thickness(4, 1, 4, 2)
            : new Thickness(0);
        annotation.Shadow.Visibility = annotation.BackgroundColor.A == 0
            ? Visibility.Visible
            : Visibility.Collapsed;

        // Apply fixed-width wrapping when annotation was drag-drawn with an explicit width.
        bool hasFixedWidth = annotation.Bounds.Width > 0;
        if (hasFixedWidth) {
            annotation.Display.Width = annotation.Bounds.Width;
            annotation.Display.TextWrapping = TextWrapping.Wrap;
            annotation.Shadow!.Width = annotation.Bounds.Width;
            annotation.Shadow.TextWrapping = TextWrapping.Wrap;
        }
        else {
            annotation.Display.Width = double.NaN;
            annotation.Display.TextWrapping = TextWrapping.NoWrap;
            annotation.Shadow!.Width = double.NaN;
            annotation.Shadow.TextWrapping = TextWrapping.NoWrap;
        }

        // Measure so Bounds.Width/Height reflect the rendered size (needed for crop-in-place).
        double measureW = hasFixedWidth ? annotation.Bounds.Width : double.PositiveInfinity;
        annotation.Display.Measure(new Size(measureW, double.PositiveInfinity));
        annotation.Bounds = new Rect(
            annotation.Bounds.Left,
            annotation.Bounds.Top,
            hasFixedWidth ? annotation.Bounds.Width : Math.Max(annotation.Bounds.Width, annotation.Display.DesiredSize.Width),
            Math.Max(annotation.Bounds.Height, annotation.Display.DesiredSize.Height));

        Canvas.SetLeft(annotation.Display, annotation.Bounds.Left);
        Canvas.SetTop(annotation.Display, annotation.Bounds.Top);
        Canvas.SetLeft(annotation.Shadow, annotation.Bounds.Left + 1.5);
        Canvas.SetTop(annotation.Shadow, annotation.Bounds.Top + 1.5);
        annotation.Display.Visibility = Visibility.Visible;
        annotation.Shadow.Visibility = annotation.BackgroundColor.A == 0
            ? Visibility.Visible
            : Visibility.Collapsed;
    }

    /// <summary>
    /// Adjusts <paramref name="tb"/>.FontSize so the text fits within <paramref name="maxWidth"/>,
    /// growing back toward <see cref="AnnotationText.MaxFontSize"/> when content shrinks.
    /// </summary>
    private static void AdjustTextFontSize(TextBox tb, double maxWidth) {
        var typeface = new Typeface(
            new FontFamily("Calibri"), FontStyles.Normal, FontWeights.Bold, FontStretches.Normal);

        double fs = AnnotationText.MaxFontSize;
        while (fs > AnnotationText.MinFontSize) {
            var ft = new FormattedText(
                tb.Text,
                System.Globalization.CultureInfo.CurrentCulture,
                FlowDirection.LeftToRight,
                typeface,
                fs,
                Brushes.White,
                1.0);
            if (ft.Width <= maxWidth) break;
            fs -= 1.0;
        }
        tb.FontSize = Math.Max(AnnotationText.MinFontSize, fs);
    }

    private void RemoveTextAnnotation(AnnotationText annotation) {
        if (_selectedText == annotation) { _selectedText = null; HideColorPicker(); }
        if (!_suppressUndo) PushUndo();
        if (annotation.Display != null) _canvas.Children.Remove(annotation.Display);
        if (annotation.Shadow != null) _canvas.Children.Remove(annotation.Shadow);
        _texts.Remove(annotation);
    }

    // ── Crop In-Place ─────────────────────────────────────────────────────────

    /// <summary>
    /// Commits the current crop selection in-place: the working image is replaced with
    /// the cropped bitmap, annotations outside the crop rect are removed, and those
    /// that overlap are shifted so their coordinates are relative to the new origin.
    /// The full editor state before the crop is pushed onto the undo stack, so
    /// Ctrl+Z restores the original image, annotations, canvas size, and crop rect.
    /// </summary>
    private void DoCropInPlace() {
        if (_sel.IsEmpty) return;

        CommitActiveTextBox(); // finalize any in-progress text edit before cropping
        PushUndo();  // full snapshot before crop — Ctrl+Z will restore everything

        var sel = _sel;

        // Crop _workingImage to the selection (pixel coordinates).
        var pxW = _workingImage.PixelWidth;
        var pxH = _workingImage.PixelHeight;
        var cropX = (int)Math.Round(sel.Left * _canvasScaleX);
        var cropY = (int)Math.Round(sel.Top * _canvasScaleY);
        var cropW = (int)Math.Round(sel.Width * _canvasScaleX);
        var cropH = (int)Math.Round(sel.Height * _canvasScaleY);
        cropX = Math.Max(0, cropX);
        cropY = Math.Max(0, cropY);
        cropW = Math.Max(1, Math.Min(cropW, pxW - cropX));
        cropH = Math.Max(1, Math.Min(cropH, pxH - cropY));

        var cropped = new CroppedBitmap(_workingImage, new Int32Rect(cropX, cropY, cropW, cropH));
        cropped.Freeze();
        SquadDashTrace.Write("UI",
            $"[ClipboardImageEditor] CropInPlace: before={pxW}x{pxH}px sel={sel.Left:F1},{sel.Top:F1},{sel.Width:F1}x{sel.Height:F1} " +
            $"cropPx={cropX},{cropY},{cropW}x{cropH} canvasScale={_canvasScaleX:F3}x{_canvasScaleY:F3}");
        _workingImage = cropped;
        _cachedPixels = null;

        // Resize the image control and canvas to the selection's logical dimensions.
        // _canvasScaleX/Y are unchanged — DPI ratio stays constant after crop.
        var newW = sel.Width;
        var newH = sel.Height;
        _imageCtrl.Source = cropped;
        _imageCtrl.Width = newW;
        _imageCtrl.Height = newH;
        _canvas.Width = newW;
        _canvas.Height = newH;

        // Shift/remove annotations so their coords are relative to the new origin.
        var dx = sel.Left;
        var dy = sel.Top;

        foreach (var arrow in _arrows.ToList()) {
            // Arrow spans from target center through arrowhead to tail end.
            var rad = arrow.ArrowheadAngleDeg * Math.PI / 180.0;
            var ux = Math.Sin(rad);
            var uy = -Math.Cos(rad);
            var cx = arrow.TargetCenterOnCanvas.X + arrow.OffsetX;
            var cy = arrow.TargetCenterOnCanvas.Y + arrow.OffsetY;
            var ahX = cx + ux * arrow.ArrowLength;
            var ahY = cy + uy * arrow.ArrowLength;
            var tlX = cx + ux * (arrow.ArrowLength + arrow.TailLength);
            var tlY = cy + uy * (arrow.ArrowLength + arrow.TailLength);
            var bbox = new Rect(
                Math.Min(cx, Math.Min(ahX, tlX)),
                Math.Min(cy, Math.Min(ahY, tlY)),
                Math.Abs(Math.Max(cx, Math.Max(ahX, tlX)) - Math.Min(cx, Math.Min(ahX, tlX))),
                Math.Abs(Math.Max(cy, Math.Max(ahY, tlY)) - Math.Min(cy, Math.Min(ahY, tlY))));

            if (!sel.IntersectsWith(bbox)) {
                RemoveArrow(arrow);
            }
            else {
                arrow.TargetCenterOnCanvas = new Point(
                    arrow.TargetCenterOnCanvas.X - dx,
                    arrow.TargetCenterOnCanvas.Y - dy);
                UpdateArrowGeometry(arrow);
            }
        }

        foreach (var ar in _annotRects.ToList()) {
            if (!sel.IntersectsWith(ar.Bounds)) {
                RemoveAnnotationRect(ar);
            }
            else {
                ar.Bounds = new Rect(ar.Bounds.Left - dx, ar.Bounds.Top - dy,
                                     ar.Bounds.Width, ar.Bounds.Height);
                UpdateRectGeometry(ar);
            }
        }

        if (_cursorEnabled && _cursorImage != null) {
            const double CursorW = 22, CursorH = 26;
            var curX = Canvas.GetLeft(_cursorImage);
            var curY = Canvas.GetTop(_cursorImage);
            var cursorBounds = new Rect(curX, curY, CursorW, CursorH);
            if (!sel.IntersectsWith(cursorBounds)) {
                _cursorEnabled = false;
                _cursorImage.Visibility = Visibility.Collapsed;
            }
            else {
                Canvas.SetLeft(_cursorImage, curX - dx);
                Canvas.SetTop(_cursorImage, curY - dy);
            }
        }

        // Shift or remove text annotations relative to the new origin.
        _suppressUndo = true;
        try {
            foreach (var t in _texts.ToList()) {
                if (!sel.IntersectsWith(t.Bounds)) {
                    RemoveTextAnnotation(t);
                }
                else {
                    t.Bounds = new Rect(t.Bounds.Left - dx, t.Bounds.Top - dy,
                                        t.Bounds.Width, t.Bounds.Height);
                    UpdateTextDisplay(t);
                }
            }
        }
        finally { _suppressUndo = false; }

        // Clear the selection and deselect any annotation so we start fresh.
        _sel = Rect.Empty;
        SelectArrow(null);
        SelectAnnotationRect(null);
        HideColorPicker();
        RefreshLayout();

        // Re-fit zoom and snap the window around the newly cropped (smaller) image,
        // mirroring the initial-open behaviour: never zoom in past 100%, shrink if needed.
        var cropWork = GetMonitorWorkAreaRect(this);
        const double CropToolbarH = 110.0;
        double fitW = (cropWork.Width * 0.95 - 24) / _canvas.Width;
        double fitH = (cropWork.Height * 0.95 - CropToolbarH) / _canvas.Height;
        _zoom = Math.Min(1.0, Math.Min(fitW, fitH));
        _scaleTransform.ScaleX = _zoom;
        _scaleTransform.ScaleY = _zoom;
        if (_zoomLabel != null) _zoomLabel.Text = $"{_zoom * 100:F0}%";
        UpdateWindowSizeForZoom();

        // Center the scroll view so the freshly-cropped image is visible and not clipped.
        Dispatcher.BeginInvoke(DispatcherPriority.Render, () => {
            _scrollViewer.ScrollToHorizontalOffset(_scrollViewer.ScrollableWidth / 2);
            _scrollViewer.ScrollToVerticalOffset(_scrollViewer.ScrollableHeight / 2);
        });
    }

    // ── Insert Image ──────────────────────────────────────────────────────────

    private void DoInsertImage() {
        CommitActiveTextBox(); // finalize any in-progress text edit so it renders in the output

        // Hide chrome before rendering so handles/color-picker don't appear in the output.
        foreach (var h in _handles) h.Visibility = Visibility.Collapsed;
        _selBorderRect.Visibility = Visibility.Collapsed;
        _dimWidthBadge.Visibility = Visibility.Collapsed;
        _dimHeightBadge.Visibility = Visibility.Collapsed;
        HideColorPicker();
        HideModeHint();
        HideEyedropperTooltip();

        if (_selectedArrow != null) {
            _selectedArrow.TipHandle.Visibility = Visibility.Collapsed;
            _selectedArrow.TailHandle.Visibility = Visibility.Collapsed;
        }

        foreach (var ar in _annotRects)
            foreach (var h in ar.Handles) h.Visibility = Visibility.Collapsed;

        try {
            Result = RenderFinalBitmap();
        }
        finally {
            Close();
        }
    }

    private void DoCopyToClipboard() {
        CommitActiveTextBox();

        // Hide chrome so handles/picker don't appear in the rendered output.
        foreach (var h in _handles) h.Visibility = Visibility.Collapsed;
        _selBorderRect.Visibility = Visibility.Collapsed;
        _dimWidthBadge.Visibility = Visibility.Collapsed;
        _dimHeightBadge.Visibility = Visibility.Collapsed;
        HideColorPicker();
        HideModeHint();
        HideEyedropperTooltip();

        if (_selectedArrow != null) {
            _selectedArrow.TipHandle.Visibility = Visibility.Collapsed;
            _selectedArrow.TailHandle.Visibility = Visibility.Collapsed;
        }

        foreach (var ar in _annotRects)
            foreach (var h in ar.Handles) h.Visibility = Visibility.Collapsed;

        try {
            var bmp = RenderFinalBitmap();
            if (bmp != null)
                Clipboard.SetImage(bmp);
        }
        finally {
            // Restore chrome visibility so the user can continue editing.
            RefreshLayout();
            if (_selectedArrow != null) {
                _selectedArrow.TipHandle.Visibility = Visibility.Visible;
                _selectedArrow.TailHandle.Visibility = Visibility.Visible;
            }
            foreach (var ar in _annotRects)
                foreach (var h in ar.Handles) h.Visibility = Visibility.Visible;
        }
    }

    private BitmapSource? RenderFinalBitmap() {
        // Always render at original pixel dimensions regardless of monitor DPI or
        // the display scaling applied in the constructor.
        var pxW = _workingImage.PixelWidth;
        var pxH = _workingImage.PixelHeight;
        if (pxW < 1 || pxH < 1) return null;

        // Use a DrawingVisual so we can scale from canvas logical size to pixel size.
        var rtb = new RenderTargetBitmap(pxW, pxH, 96, 96, PixelFormats.Pbgra32);
        var dv = new DrawingVisual();
        using (var ctx = dv.RenderOpen()) {
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
        if (cropX == 0 && cropY == 0 && cropW == pxW && cropH == pxH) {
            bmp = rtb;
        }
        else {
            var cropped = new CroppedBitmap(rtb, new Int32Rect(cropX, cropY, cropW, cropH));
            cropped.Freeze();
            bmp = cropped;
        }

        if (_roundCorners)
            bmp = ApplyRoundedCorners(bmp, CornerRadiusPx);

        SquadDashTrace.Write("UI",
            $"[ClipboardImageEditor] RenderFinal: workingImage={pxW}x{pxH}px " +
            $"canvas={_canvas.ActualWidth:F1}x{_canvas.ActualHeight:F1} sel={(_sel.IsEmpty ? "none" : $"{cropSel.Width:F1}x{cropSel.Height:F1}@{cropSel.Left:F1},{cropSel.Top:F1}")} " +
            $"cropPx={cropX},{cropY},{cropW}x{cropH} " +
            $"effectiveScale={_effectiveScaleX:F3}x{_effectiveScaleY:F3} " +
            $"output={bmp.PixelWidth}x{bmp.PixelHeight}px " +
            $"dpi={bmp.DpiX:F1}x{bmp.DpiY:F1} canvasScale={_canvasScaleX:F3}x{_canvasScaleY:F3}");

        return bmp;
    }

    /// <summary>
    /// Returns a copy of <paramref name="src"/> with the four corners made transparent.
    /// Pixels are zeroed (BGRA all 0) if they fall outside the arc of a circle with
    /// radius <paramref name="radiusPx"/> centred on each corner.
    /// </summary>
    private static BitmapSource ApplyRoundedCorners(BitmapSource src, int radiusPx) {
        var conv = new FormatConvertedBitmap(src, PixelFormats.Pbgra32, null, 0);
        int w = conv.PixelWidth;
        int h = conv.PixelHeight;
        int stride = w * 4;
        var pixels = new byte[stride * h];
        conv.CopyPixels(pixels, stride, 0);

        double r = radiusPx;
        for (int y = 0; y < radiusPx && y < h; y++) {
            for (int x = 0; x < radiusPx && x < w; x++) {
                double dx = x + 0.5 - r;
                double dy = y + 0.5 - r;
                if (dx * dx + dy * dy > r * r) {
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
        Texts: _texts.Select(t => new TextSnap(t.Bounds, t.Text, t.FontSize, t.TextColor, t.BackgroundColor)).ToList(),
        CursorEnabled: _cursorEnabled,
        CursorPos: _cursorImage != null
                           ? new Point(Canvas.GetLeft(_cursorImage), Canvas.GetTop(_cursorImage))
                           : default,
        WorkingImage: _workingImage,
        CanvasW: _canvas.Width,
        CanvasH: _canvas.Height,
        CanvasScaleX: _canvasScaleX,
        CanvasScaleY: _canvasScaleY);

    private void PushUndo() {
        if (_suppressUndo) return;
        _undoStack.Push(CaptureSnapshot());
        _redoStack.Clear();
        TrimUndoStack();
    }

    private void CommitDragUndo() {
        if (_suppressUndo || _preDragSnapshot == null) return;
        _undoStack.Push(_preDragSnapshot);
        _preDragSnapshot = null;
        _redoStack.Clear();
        TrimUndoStack();
    }

    private void TrimUndoStack() {
        if (_undoStack.Count <= 50) return;
        var items = _undoStack.ToArray();
        _undoStack.Clear();
        foreach (var item in items.Take(50).Reverse())
            _undoStack.Push(item);
    }

    private void PerformUndo() {
        if (_undoStack.Count == 0) return;
        _redoStack.Push(CaptureSnapshot());
        RestoreSnapshot(_undoStack.Pop());
    }

    private void PerformRedo() {
        if (_redoStack.Count == 0) return;
        _undoStack.Push(CaptureSnapshot());
        RestoreSnapshot(_redoStack.Pop());
    }

    private void RestoreSnapshot(EditorSnapshot snap) {
        _suppressUndo = true;
        // Capture canvas dimensions before restore so we can detect a crop/uncrop.
        double preRestoreW = _canvas.Width;
        double preRestoreH = _canvas.Height;
        try {
            SelectArrow(null);
            SelectAnnotationRect(null);
            foreach (var a in _arrows.ToList()) RemoveArrow(a);
            foreach (var r in _annotRects.ToList()) RemoveAnnotationRect(r);

            foreach (var s in snap.Arrows) {
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
            foreach (var t in _texts.ToList()) RemoveTextAnnotation(t);
            _sel = snap.Sel;
            _cursorEnabled = snap.CursorEnabled;

            // Restore working image and canvas dimensions (changed by Enter-crop).
            _workingImage = snap.WorkingImage;
            _canvasScaleX = snap.CanvasScaleX;
            _canvasScaleY = snap.CanvasScaleY;
            _canvas.Width = snap.CanvasW;
            _canvas.Height = snap.CanvasH;
            _imageCtrl.Source = snap.WorkingImage;
            _imageCtrl.Width = snap.CanvasW;
            _imageCtrl.Height = snap.CanvasH;
            _cachedPixels = null;

            if (snap.CursorEnabled) {
                EnsureCursorImageCreated();
                Canvas.SetLeft(_cursorImage!, snap.CursorPos.X);
                Canvas.SetTop(_cursorImage!, snap.CursorPos.Y);
                _cursorImage!.Visibility = Visibility.Visible;
            }
            else if (_cursorImage != null) {
                _cursorImage.Visibility = Visibility.Collapsed;
                _draggingCursor = false;
            }

            foreach (var ts in snap.Texts) {
                var a = new AnnotationText {
                    Bounds = ts.Bounds,
                    Text = ts.Text,
                    FontSize = ts.FontSize,
                    TextColor = ts.TextColor,
                    BackgroundColor = ts.BackgroundColor
                };
                _texts.Add(a);
                UpdateTextDisplay(a);
            }

            RefreshLayout();

            // When undo/redo changes the canvas size (crop or uncrop), resize the window
            // to fit the new image — mirrors the resize that DoCropInPlace performs.
            // Use snap.CanvasW/H directly (not _canvas.Width which was just set from snap)
            // to make the comparison explicit and unambiguous.
            SquadDashTrace.Write("UI",
                $"[ClipboardImageEditor] RestoreSnapshot: preW={preRestoreW:F1} preH={preRestoreH:F1} " +
                $"snapW={snap.CanvasW:F1} snapH={snap.CanvasH:F1} " +
                $"canvasW={_canvas.Width:F1} canvasH={_canvas.Height:F1}");
            if (Math.Abs(snap.CanvasW - preRestoreW) > 0.5 || Math.Abs(snap.CanvasH - preRestoreH) > 0.5) {
                var undoWork = GetMonitorWorkAreaRect(this);
                const double UndoCropToolbarH = 110.0;
                double undoFitW = (undoWork.Width * 0.95 - 24) / snap.CanvasW;
                double undoFitH = (undoWork.Height * 0.95 - UndoCropToolbarH) / snap.CanvasH;
                _zoom = Math.Min(1.0, Math.Min(undoFitW, undoFitH));
                _scaleTransform.ScaleX = _zoom;
                _scaleTransform.ScaleY = _zoom;
                if (_zoomLabel != null) _zoomLabel.Text = $"{_zoom * 100:F0}%";
                UpdateWindowSizeForZoom();
                SquadDashTrace.Write("UI",
                    $"[ClipboardImageEditor] RestoreSnapshot resize: zoom={_zoom:F3} " +
                    $"winW={Width:F1} winH={Height:F1}");
                Dispatcher.BeginInvoke(DispatcherPriority.Render, () => {
                    _scrollViewer.ScrollToHorizontalOffset(_scrollViewer.ScrollableWidth / 2);
                    _scrollViewer.ScrollToVerticalOffset(_scrollViewer.ScrollableHeight / 2);
                });
            }
        }
        finally {
            _suppressUndo = false;
        }
    }

    // ── Arrow defaults — persist / load ───────────────────────────────────────

    private static string TextAnnotDefaultsPath =>
        System.IO.Path.Combine(SquadDashPaths.AppData, "annotation-text-defaults.json");

    private void SaveTextDefaults() {
        try {
            var dir = System.IO.Path.GetDirectoryName(TextAnnotDefaultsPath)!;
            Directory.CreateDirectory(dir);
            File.WriteAllText(TextAnnotDefaultsPath, JsonSerializer.Serialize(new {
                fgColor = $"#{_defaultTextFgColor.R:X2}{_defaultTextFgColor.G:X2}{_defaultTextFgColor.B:X2}",
                bgColor = _defaultTextBgColor.A == 0
                    ? "transparent"
                    : $"#{_defaultTextBgColor.R:X2}{_defaultTextBgColor.G:X2}{_defaultTextBgColor.B:X2}",
            }));
        }
        catch { /* non-critical */ }
    }

    private void LoadTextDefaults() {
        try {
            if (!File.Exists(TextAnnotDefaultsPath)) return;
            using var doc = JsonDocument.Parse(File.ReadAllText(TextAnnotDefaultsPath));
            var root = doc.RootElement;
            if (root.TryGetProperty("fgColor", out var fg) &&
                fg.GetString() is { Length: 7 } fgHex && fgHex[0] == '#') {
                _defaultTextFgColor = Color.FromRgb(
                    Convert.ToByte(fgHex[1..3], 16),
                    Convert.ToByte(fgHex[3..5], 16),
                    Convert.ToByte(fgHex[5..7], 16));
            }
            if (root.TryGetProperty("bgColor", out var bg)) {
                var bgStr = bg.GetString();
                if (bgStr == "transparent")
                    _defaultTextBgColor = Colors.Transparent;
                else if (bgStr is { Length: 7 } bgHex && bgHex[0] == '#') {
                    _defaultTextBgColor = Color.FromRgb(
                        Convert.ToByte(bgHex[1..3], 16),
                        Convert.ToByte(bgHex[3..5], 16),
                        Convert.ToByte(bgHex[5..7], 16));
                }
            }
        }
        catch { /* non-critical */ }
    }

    private static string ArrowDefaultsPath =>
        System.IO.Path.Combine(SquadDashPaths.AppData, "annotation-arrow-defaults.json");

    private void SaveArrowDefaults() {
        try {
            var dir = System.IO.Path.GetDirectoryName(ArrowDefaultsPath)!;
            Directory.CreateDirectory(dir);
            File.WriteAllText(ArrowDefaultsPath, JsonSerializer.Serialize(new {
                color = $"#{_defaultArrowColor.R:X2}{_defaultArrowColor.G:X2}{_defaultArrowColor.B:X2}",
                angleDeg = _defaultArrowAngleDeg,
                length = _defaultArrowLength,
                tailLen = _defaultTailLength
            }));
        }
        catch { /* non-critical */ }
    }

    private void LoadArrowDefaults() {
        try {
            if (!File.Exists(ArrowDefaultsPath)) return;
            using var doc = JsonDocument.Parse(File.ReadAllText(ArrowDefaultsPath));
            var root = doc.RootElement;
            if (root.TryGetProperty("color", out var col) &&
                col.GetString() is { Length: 7 } hex && hex[0] == '#') {
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

    private void SelectText(AnnotationText? annotation) {
        if (_textSelectionRect != null) {
            _canvas.Children.Remove(_textSelectionRect);
            _textSelectionRect = null;
        }

        if (annotation != null) {
            SelectArrow(null);
            SelectAnnotationRect(null);
            ShowColorPickerForText(annotation);
            _selectedText = annotation;

            var b = annotation.Bounds;
            // When still editing, use the live TextBox dimensions — ann.Bounds.Height is 0 until commit.
            double bLeft = b.Left, bTop = b.Top, sw, sh;
            if (annotation == _editingText && _activeTextBox != null) {
                bLeft = Canvas.GetLeft(_activeTextBox);
                bTop  = Canvas.GetTop(_activeTextBox);
                double tbW = _activeTextBox.ActualWidth  > 0 ? _activeTextBox.ActualWidth  : _activeTextBox.DesiredSize.Width;
                double tbH = _activeTextBox.ActualHeight > 0 ? _activeTextBox.ActualHeight : _activeTextBox.DesiredSize.Height;
                sw = Math.Max(tbW + 8, 30);
                sh = Math.Max(tbH + 4, 20);
            } else {
                sw = Math.Max(b.Width  > 0 ? b.Width  + 8 : 30, 30);
                sh = Math.Max(b.Height > 0 ? b.Height + 4 : 20, 20);
            }
            _textSelectionRect = new Rectangle {
                Width = sw,
                Height = sh,
                Stroke = new SolidColorBrush(Color.FromRgb(0x1E, 0x6F, 0xCC)),
                StrokeThickness = 1.5 / _zoom,
                Fill = Brushes.Transparent,
                IsHitTestVisible = false,
                Effect = new DropShadowEffect {
                    BlurRadius = 3,
                    ShadowDepth = 1,
                    Color = Color.FromRgb(0x93, 0xC5, 0xFD),
                    Opacity = 0.9,
                    Direction = 315
                }
            };
            Panel.SetZIndex(_textSelectionRect, 21);
            Canvas.SetLeft(_textSelectionRect, bLeft - 4);
            Canvas.SetTop(_textSelectionRect, bTop - 2);
            _canvas.Children.Add(_textSelectionRect);
            AddTextResizeHandles(annotation);
        }
        else {
            RemoveTextResizeHandles();
            _selectedText = null;
            HideColorPicker();
        }
    }

    private void UpdateTextSelectionBorder() {
        if (_textSelectionRect == null || _selectedText == null) return;
        var b = _selectedText.Bounds;
        double bLeft = b.Left, bTop = b.Top, sw, sh;
        // When still editing, derive dimensions from the live TextBox — ann.Bounds.Height is 0 until commit.
        if (_selectedText == _editingText && _activeTextBox != null) {
            bLeft = Canvas.GetLeft(_activeTextBox);
            bTop  = Canvas.GetTop(_activeTextBox);
            double tbW = _activeTextBox.ActualWidth  > 0 ? _activeTextBox.ActualWidth  : _activeTextBox.DesiredSize.Width;
            double tbH = _activeTextBox.ActualHeight > 0 ? _activeTextBox.ActualHeight : _activeTextBox.DesiredSize.Height;
            sw = Math.Max(tbW + 8, 30);
            sh = Math.Max(tbH + 4, 20);
        } else {
            sw = Math.Max(b.Width  > 0 ? b.Width  + 8 : 30, 30);
            sh = Math.Max(b.Height > 0 ? b.Height + 4 : 20, 20);
        }
        _textSelectionRect.Width = sw;
        _textSelectionRect.Height = sh;
        _textSelectionRect.StrokeThickness = 1.5 / _zoom;
        Canvas.SetLeft(_textSelectionRect, bLeft - 4);
        Canvas.SetTop(_textSelectionRect, bTop - 2);
        PositionTextResizeHandles(_selectedText);
    }

    private void RefreshTextAnnotation(AnnotationText annotation) {
        if (annotation.Display != null) annotation.Display.FontSize = annotation.FontSize;
        if (annotation.Shadow != null) annotation.Shadow.FontSize = annotation.FontSize;
        UpdateTextSelectionBorder();
    }

    private void AddTextResizeHandles(AnnotationText annotation) {
        RemoveTextResizeHandles();
        // Handle order: 0=NW, 1=NE, 2=SW, 3=SE, 4=N, 5=E, 6=S, 7=W
        Cursor[] handleCursors =
        {
            Cursors.SizeNWSE, // 0 NW
            Cursors.SizeNESW, // 1 NE
            Cursors.SizeNESW, // 2 SW
            Cursors.SizeNWSE, // 3 SE
            Cursors.SizeNS,   // 4 N
            Cursors.SizeWE,   // 5 E
            Cursors.SizeNS,   // 6 S
            Cursors.SizeWE,   // 7 W
        };
        for (int i = 0; i < 8; i++) {
            var handle = new Rectangle {
                Fill = Brushes.White,
                Stroke = Brushes.Black,
                StrokeThickness = 1,
                Width = HandleSize / _zoom,
                Height = HandleSize / _zoom,
                IsHitTestVisible = true,
                Cursor = handleCursors[i],
            };
            Panel.SetZIndex(handle, 22);
            _canvas.Children.Add(handle);
            _textResizeHandles.Add(handle);

            // Wire drag in the PREVIEW (tunnel) phase so it fires before TextBox LostFocus
            // can recreate handles at position (0,0) and break the coordinate hit-test.
            int idx = i;
            handle.PreviewMouseLeftButtonDown += (_, ev) => {
                var target = _selectedText ?? _editingText;
                if (target == null) return;
                double dispW = target.Display?.ActualWidth ?? (_activeTextBox?.ActualWidth ?? 80);
                double dispH = target.Display?.ActualHeight ?? (_activeTextBox?.ActualHeight ?? 20);
                _draggingTextHandle = true;
                _textHandleDragIndex = idx;
                _textHandleDragStart = ev.GetPosition(_canvas);
                _textHandleDragOrigFontSize = target.FontSize;
                _textHandleDragOrigDisplayW = dispW > 0 ? dispW : 80;
                _textHandleDragOrigDisplayH = dispH > 0 ? dispH : 20;
                _textHandleDragOrigBounds = target.Bounds;
                _textHandleDragAnnotation = target;
                _preDragSnapshot = CaptureSnapshot();
                _canvas.CaptureMouse();
                ev.Handled = true;
            };
        }
        // Defer positioning until after layout so Bounds/DesiredSize are valid on first placement.
        Dispatcher.BeginInvoke(DispatcherPriority.Render, () => PositionTextResizeHandles(annotation));
    }

    private void PositionTextResizeHandles(AnnotationText annotation) {
        if (_textResizeHandles.Count != 8) return;
        double hs = HandleSize / _zoom / 2;
        double l, t, w, h;
        if (annotation.Display != null) {
            // Committed annotation — use Bounds with fallback to Display.DesiredSize.
            l = annotation.Bounds.Left - 4;
            t = annotation.Bounds.Top - 2;
            w = (annotation.Bounds.Width > 0 ? annotation.Bounds.Width : annotation.Display.DesiredSize.Width) + 8;
            h = (annotation.Bounds.Height > 0 ? annotation.Bounds.Height : annotation.Display.DesiredSize.Height) + 4;
        }
        else if (_activeTextBox != null) {
            // Still editing — position handles around the live TextBox itself.
            l = Canvas.GetLeft(_activeTextBox) - 4;
            t = Canvas.GetTop(_activeTextBox) - 2;
            w = (_activeTextBox.ActualWidth > 0 ? _activeTextBox.ActualWidth : _activeTextBox.DesiredSize.Width) + 8;
            h = (_activeTextBox.ActualHeight > 0 ? _activeTextBox.ActualHeight : _activeTextBox.DesiredSize.Height) + 4;
        }
        else return;
        // 8 handles: 4 corners (NW,NE,SW,SE) + 4 edge midpoints (N,E,S,W)
        Point[] positions =
        {
            new Point(l,          t),           // NW corner
            new Point(l + w,      t),           // NE corner
            new Point(l,          t + h),       // SW corner
            new Point(l + w,      t + h),       // SE corner
            new Point(l + w / 2,  t),           // N midpoint
            new Point(l + w,      t + h / 2),   // E midpoint
            new Point(l + w / 2,  t + h),       // S midpoint
            new Point(l,          t + h / 2),   // W midpoint
        };
        for (int i = 0; i < 8; i++) {
            Canvas.SetLeft(_textResizeHandles[i], positions[i].X - hs);
            Canvas.SetTop(_textResizeHandles[i], positions[i].Y - hs);
        }
    }

    private void RemoveTextResizeHandles() {
        foreach (var h in _textResizeHandles) _canvas.Children.Remove(h);
        _textResizeHandles.Clear();
    }

    private void ShowColorPickerForText(AnnotationText annotation) {
        HideColorPicker();
        _colorPickerText = annotation;

        var outerPanel = new StackPanel { Orientation = Orientation.Vertical };
        Panel.SetZIndex(outerPanel, 300);

        var bgRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 3) };
        var bgChoices = new[] { Colors.Black, Colors.White, Colors.Transparent };
        foreach (var bgColor in bgChoices) {
            var c = bgColor;
            bool isSelected = c.A == 0
                ? annotation.BackgroundColor.A == 0
                : annotation.BackgroundColor.A > 0
                  && annotation.BackgroundColor.R == c.R
                  && annotation.BackgroundColor.G == c.G
                  && annotation.BackgroundColor.B == c.B;

            var swatch = MakeBgSwatch(c, isSelected, picked => {
                annotation.BackgroundColor = picked;
                _defaultTextBgColor = picked;

                // Auto-adjust text color if it is invisible on the new background.
                if (!IsColorInTextFgPalette(annotation.TextColor, picked)) {
                    // White bg → default to black text; black bg → default to white text.
                    annotation.TextColor = (picked.A > 0 && picked.R > 200 && picked.G > 200 && picked.B > 200)
                        ? Colors.Black
                        : Colors.White;
                    _defaultTextFgColor = annotation.TextColor;
                }

                SaveTextDefaults();
                UpdateTextDisplay(annotation);
                UpdateTextSelectionBorder();
                ShowColorPickerForText(annotation);
            });
            bgRow.Children.Add(swatch);
        }

        var fgRow = new StackPanel { Orientation = Orientation.Horizontal };
        foreach (var color in GetTextFgPalette(annotation.BackgroundColor)) {
            var c = color;
            bool isSelected = c.R == annotation.TextColor.R
                           && c.G == annotation.TextColor.G
                           && c.B == annotation.TextColor.B;
            var swatch = MakeColorSwatch(c, isSelected, picked => {
                annotation.TextColor = picked;
                _defaultTextFgColor = picked;
                SaveTextDefaults();
                UpdateTextDisplay(annotation);
                ShowColorPickerForText(annotation);
            });
            fgRow.Children.Add(swatch);
        }

        outerPanel.Children.Add(bgRow);
        outerPanel.Children.Add(fgRow);
        _colorPickerPanel = outerPanel;
        _canvas.Children.Add(outerPanel);

        outerPanel.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        double pw = outerPanel.DesiredSize.Width;
        double ph = outerPanel.DesiredSize.Height;
        double cx = annotation.Bounds.Left + Math.Max(annotation.Bounds.Width, 0) / 2;
        double cy = annotation.Bounds.Top;
        // Prefer above the annotation; flip below if it would overlap (not enough vertical space).
        double annotBottom = cy + Math.Max(annotation.Bounds.Height, 20);
        double topAbove = cy - ph - 8;
        double pickerTop = topAbove >= 0 ? topAbove : annotBottom + 4;
        Canvas.SetLeft(outerPanel, Math.Max(0, Math.Min(cx - pw / 2, _canvas.Width - pw - 4)));
        Canvas.SetTop(outerPanel, Math.Max(0, pickerTop));
    }

    private static FrameworkElement MakeBgSwatch(Color bgColor, bool isSelected, Action<Color> onPick) {
        string tip = bgColor.A == 0
            ? "Transparent background (no fill)"
            : bgColor.R == 0
                ? "Black background"
                : "White background";

        FrameworkElement fill;
        if (bgColor.A == 0) {
            // Transparent: checker + red diagonal line to indicate "no fill"
            var checkerGrid = new Grid { Width = 16, Height = 16 };
            checkerGrid.Children.Add(new Rectangle { Width = 16, Height = 16, Fill = MakeCheckerBrush(), RadiusX = 2, RadiusY = 2 });
            checkerGrid.Children.Add(new Line {
                X1 = 1,
                Y1 = 1,
                X2 = 15,
                Y2 = 15,
                Stroke = Brushes.Red,
                StrokeThickness = 1.5,
                StrokeStartLineCap = PenLineCap.Round,
                StrokeEndLineCap = PenLineCap.Round,
                IsHitTestVisible = false
            });
            fill = checkerGrid;
        }
        else {
            fill = new Rectangle {
                Width = 16,
                Height = 16,
                Fill = new SolidColorBrush(bgColor),
                RadiusX = 2,
                RadiusY = 2,
            };
        }

        if (isSelected) {
            var grid = new Grid { Width = 20, Height = 20, Margin = new Thickness(3, 0, 3, 0), Cursor = Cursors.Hand, ToolTip = tip };
            grid.Children.Add(new Rectangle { Fill = Brushes.Black, RadiusX = 3, RadiusY = 3 });
            var inner = new Border {
                Width = 16,
                Height = 16,
                BorderBrush = Brushes.White,
                BorderThickness = new Thickness(1.5),
                CornerRadius = new CornerRadius(2),
                Child = fill,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
            };
            grid.Children.Add(inner);
            grid.MouseLeftButtonDown += (_, e) => { onPick(bgColor); e.Handled = true; };
            return grid;
        }
        else {
            var grid = new Grid { Width = 16, Height = 16, Margin = new Thickness(3, 0, 3, 0), Cursor = Cursors.Hand, ToolTip = tip };
            grid.Children.Add(fill);
            grid.Children.Add(new Rectangle {
                Fill = Brushes.Transparent,
                Stroke = new SolidColorBrush(Color.FromRgb(0x88, 0x88, 0x88)),
                StrokeThickness = 1,
                RadiusX = 2,
                RadiusY = 2,
            });
            grid.MouseLeftButtonDown += (_, e) => { onPick(bgColor); e.Handled = true; };
            return grid;
        }
    }

    private static Brush MakeCheckerBrush() {
        var dg = new DrawingGroup();
        dg.Children.Add(new GeometryDrawing(Brushes.LightGray, null,
            new RectangleGeometry(new Rect(0, 0, 8, 8))));
        var checkGroup = new GeometryGroup();
        checkGroup.Children.Add(new RectangleGeometry(new Rect(0, 0, 4, 4)));
        checkGroup.Children.Add(new RectangleGeometry(new Rect(4, 4, 4, 4)));
        dg.Children.Add(new GeometryDrawing(Brushes.White, null, checkGroup));
        return new DrawingBrush {
            Drawing = dg,
            TileMode = TileMode.Tile,
            Viewport = new Rect(0, 0, 8, 8),
            ViewportUnits = BrushMappingMode.Absolute,
        };
    }

    // ── Snapshot records ──────────────────────────────────────────────────────

    private sealed record EditorSnapshot(
        Rect Sel,
        IReadOnlyList<ArrowSnap> Arrows,
        IReadOnlyList<RectSnap> Rects,
        IReadOnlyList<TextSnap> Texts,
        bool CursorEnabled,
        Point CursorPos,
        BitmapSource WorkingImage,
        double CanvasW,
        double CanvasH,
        double CanvasScaleX,
        double CanvasScaleY);

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

    private sealed record TextSnap(Rect Bounds, string Text, double FontSize, Color TextColor, Color BackgroundColor);

    // ── Change detection ──────────────────────────────────────────────────────

    /// <summary>
    /// Returns <see langword="true"/> if the user has made any editable change since the
    /// dialog was opened: placed at least one annotation arrow or rectangle, placed a
    /// cursor-indicator overlay, or defined a crop/region selection.
    ///
    /// Used by the Escape-key handler to decide whether to show a "Discard changes?"
    /// confirmation before closing.
    /// </summary>
    private bool HasChanges()
        => _arrows.Count > 0
        || _annotRects.Count > 0
        || _texts.Count > 0
        || (_cursorEnabled && _cursorImage != null)
        || !_sel.IsEmpty;
}
