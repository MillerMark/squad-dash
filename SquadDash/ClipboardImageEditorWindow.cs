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
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Threading;
using System.Collections.Generic;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;
using System.Windows.Media.Animation;

namespace SquadDash;

/// <summary>
/// Standalone image-editing window that pre-loads an image from the clipboard,
/// shows a resizable crop rectangle, and supports arrow and cursor-overlay annotations.
///
/// Pattern: code-behind only (no XAML), all colours via SetResourceReference —
/// consistent with <see cref="AgentInfoWindow"/> and <see cref="ScreenshotOverlayWindow"/>.
///
/// Modeless: call <c>Show()</c> (not <c>ShowDialog()</c>) and subscribe to
/// <see cref="ImageAccepted"/> to receive the rendered bitmap when the user clicks
/// "Insert Image". Multiple instances may be open simultaneously.
/// <see cref="Result"/> is also set for convenience at the moment the event fires.
/// </summary>
internal sealed class ClipboardImageEditorWindow : ChromedWindow {
    // ── Result / callback ─────────────────────────────────────────────────────

    internal BitmapSource? Result { get; private set; }

    /// <summary>
    /// The un-annotated working image (source) at the moment the user accepted the result.
    /// Set alongside <see cref="Result"/> just before <see cref="ImageAccepted"/> fires.
    /// Null until the user confirms.
    /// </summary>
    internal BitmapSource? SourceImage { get; private set; }

    /// <summary>
    /// Serialisable annotation state captured just before <see cref="ImageAccepted"/> fires.
    /// Null until the user confirms, or when there are no annotations and no crop selection.
    /// </summary>
    internal ClipboardAnnotationState? AnnotationState { get; private set; }

    /// <summary>
    /// Fired (on the UI thread) when the user clicks "Insert Image" / "Attach Image".
    /// The argument is the rendered, annotated bitmap. The window closes immediately after.
    /// </summary>
    internal event Action<BitmapSource>? ImageAccepted;

    // ── Hit zones ─────────────────────────────────────────────────────────────

    private enum HitZone { None, Move, NW, N, NE, E, SE, S, SW, W }

    // ── Constants ─────────────────────────────────────────────────────────────

    private const double HandleSize = 9.0;
    private const double HitPad = 5.0;
    private const double MinSize = 4.0;

    // ── Source image ──────────────────────────────────────────────────────────

    private readonly BitmapSource _clipboardImage;

    // Mutable working image — starts as _clipboardImage, replaced after each Enter-crop.
    private BitmapSource _workingImage = null!;

    // The original image as passed to the constructor (after DPI normalisation),
    // never mutated by crop operations. Saved as the .source.png sidecar so that
    // re-opening the editor always has access to the full original.
    private BitmapSource _originalImage = null!;

    // Accumulated canvas-space crop offset from the original image origin.
    // Updated by DoCropInPlace; used in CaptureAnnotationState so that on
    // re-open the editor can show the original image with the prior crop pre-selected.
    private double _appliedCropOffsetX;
    private double _appliedCropOffsetY;

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
    private Grid _scrollExtentSurface = null!;

    // ── Round corners ─────────────────────────────────────────────────────────

    private bool _roundCorners;

    // Physical-pixel corner radius applied to the output PNG when _roundCorners is true.
    // Windows 11 window corners are ~8 px
    private const int CornerRadiusPx = 8;

    // ── Zoom ──────────────────────────────────────────────────────────────────

    private double _zoom = 1.0;
    private readonly ScaleTransform _scaleTransform = new(1.0, 1.0);
    private Canvas _overlayCanvas = null!;
    private const double CanvasViewportPadding = 4.0;

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
    private Button? _cursorBtn;

    // Arrow drag sub-state
    private bool _tailDragging;
    private bool _bodyDragging;
    private Point _tailDragStartMouse;
    private Point _bodyDragStartMouse;
    private double _bodyDragStartOffsetX;
    private double _bodyDragStartOffsetY;
    private Point _arrowCloneDragOriginalCenter; // drag-start center for Shift axis constraint
    private AnnotationArrow? _arrowMoveStandbyClone; // hidden clone kept during move drag for late-Ctrl copy

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
    private FrameworkElement? _colorPickerPanel;
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
    private Point _rectCloneDragOriginalCenter; // drag-start center for Shift axis constraint
    private AnnotationRect? _rectMoveStandbyClone; // hidden clone kept during move drag for late-Ctrl copy

    // Rubber-band state for drawing a new annotation rect
    private bool _creatingAnnotRect;
    private Point _annotRectAnchor;
    private Rectangle? _annotRectPreview;

    // Rect annotation defaults
    private Color _defaultRectColor = Color.FromRgb(255, 80, 80);

    // ── Annotation — measurement lines ────────────────────────────────────────

    private readonly List<AnnotationMeasureLine> _measureLines = new();
    private AnnotationMeasureLine? _selectedMeasureLine;
    private AnnotationMeasureLine? _colorPickerMeasureLine;
    private bool _inMeasureLineMode;
    private Button? _addMeasureLineBtn;
    private Color _defaultMeasureLineColor = Color.FromRgb(255, 120, 20);

    // Measure-line body-drag sub-state
    private AnnotationMeasureLine? _draggingMeasureLine;
    private Point _measureLineDragStart;
    private Point _measureLineDragOrigStart;
    private Point _measureLineDragOrigEnd;
    private Point _mlCloneDragOriginalCenter; // drag-start center for Shift axis constraint
    private AnnotationMeasureLine? _mlMoveStandbyClone; // hidden clone kept during move drag for late-Ctrl copy

    // Measure-line handle-drag sub-state
    private bool _mlDraggingHandle;   // true when an endpoint handle is being dragged
    private bool _mlDraggingHandle1;  // true = StartPt handle, false = EndPt handle

    // Measure-line drag-to-draw state
    private bool _creatingMeasureLine;
    private Point _measureLineAnchor;
    private Line? _mlPreviewLine;
    private Polygon? _mlPreviewHead1;
    private Polygon? _mlPreviewHead2;
    private Line? _mlPreviewCap1;
    private Line? _mlPreviewCap2;
    private Border? _mlPreviewBadge;
    private TextBlock? _mlPreviewBadgeText;
    private bool _mlPreviewIsHorizontal;

    // ── Annotation — X shapes ─────────────────────────────────────────────────

    private readonly List<AnnotationX> _annotXShapes = new();
    private AnnotationX? _selectedAnnotX;
    private bool _inXMode;
    private Button? _addXBtn;
    private AnnotationX? _colorPickerX;

    // X drag sub-state
    private AnnotationX? _draggingAnnotX;
    private Point _annotXDragStart;
    private Rect _annotXDragOriginal;
    private bool _annotXBodyDragging;
    private int _draggingAnnotXHandleIdx = -1;
    private Point _xCloneDragOriginalCenter; // drag-start center for Shift axis constraint
    private AnnotationX? _xMoveStandbyClone; // hidden clone kept during move drag for late-Ctrl copy

    // Rubber-band state for drawing a new X
    private bool _creatingAnnotX;
    private Point _annotXAnchor;
    private Line? _annotXPreviewLine1;
    private Line? _annotXPreviewLine2;

    // X annotation defaults
    private Color _defaultXColor = Color.FromRgb(255, 80, 80);

    // Last-used X size for click-to-place
    private double _lastDragXWidth = 80.0;
    private double _lastDragXHeight = 80.0;

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
    private bool _selectedCursor;

    // ── Eyedropper ────────────────────────────────────────────────────────────

    private bool _inEyedropperMode;
    private Button? _eyedropperBtn;
    private Border? _eyedropperSwatch;
    private TextBlock? _eyedropperHexLabel;
    private Border? _eyedropperTooltipBorder;
    private Border? _eyedropperTooltipSwatch;
    private TextBlock? _eyedropperTooltipRgbText;
    private TextBlock? _eyedropperTooltipHslText;
    private TextBlock? _eyedropperTooltipHexText;
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
    private readonly bool _isUpdateMode;

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
    private bool _inMeasureLineMultiDropMode;
    private bool _inXMultiDropMode;
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
    private Point _textCloneDragOriginalCenter; // drag-start center for Shift axis constraint
    private AnnotationText? _textMoveStandbyClone; // hidden clone kept during canvas-drag for late-Ctrl copy
    private Color _defaultTextFgColor = Colors.White;
    private Color _defaultTextBgColor = Colors.Black;
    private bool _textDragCreating;
    private Point _textDragStart;
    private Rectangle? _textDragPreview;

    // ── Annotation text-box voice / PTT ───────────────────────────────────────
    private readonly PttTextBoxAttachment _textBoxPttAttachment;

    // ── CamelCase navigation ──────────────────────────────────────────────────
    private readonly CamelCaseTextBoxAttachment _camelCaseNav = new();

    // ── Re-edit initial state ─────────────────────────────────────────────────

    /// <summary>
    /// When non-null, the <see cref="System.Windows.FrameworkElement.Loaded"/> handler
    /// restores this annotation state instead of entering crop mode.
    /// </summary>
    private readonly ClipboardAnnotationState? _initialState;

    private string? _openStateSnapshotJson;

    // ── Session persistence ───────────────────────────────────────────────────

    /// <summary>Stable identifier for this editor instance; used as the file key in the state store.</summary>
    private readonly string _editorId = Guid.NewGuid().ToString("N");

    /// <summary>
    /// Full session state to restore on <see cref="System.Windows.FrameworkElement.Loaded"/>.
    /// Provided by the startup restoration path (Phase 4).
    /// </summary>
    private readonly ClipboardEditorSessionState? _sessionState;

    // ────────────────────────────────────────────────────────────────────────

    internal ClipboardImageEditorWindow(Window owner, BitmapSource clipboardImage, bool isPromptMode = false,
                                        ClipboardAnnotationState? initialState = null, bool isUpdateMode = false,
                                        ClipboardEditorSessionState? sessionState = null)
        : base(captionHeight: 36) {
        _clipboardImage = clipboardImage ?? throw new ArgumentNullException(nameof(clipboardImage));

        _workingImage = clipboardImage;
        _isPromptMode = isPromptMode;
        _isUpdateMode = isUpdateMode;
        _themeName = AgentStatusCard.IsDarkTheme ? "dark" : "light";
        _initialState = initialState;
        _sessionState = sessionState;

        // Owner is intentionally not set — this window is modeless and fully independent.
        Title = "Edit Clipboard Image";
        ShowInTaskbar = true;
        WindowStartupLocation = WindowStartupLocation.Manual;

        _textBoxPttAttachment = new PttTextBoxAttachment(() => new ApplicationSettingsStore().Load(), this, Dispatcher);
        Closed += (_, _) => _textBoxPttAttachment.Dispose();
        Closing += (_, _) => { _ = DeleteStateAsync(); };

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
            SquadDashTrace.Write("ImageEditor",
                $"[ClipboardImageEditor] ownerHwnd={ownerHwnd:X} ownerWr=({ownerWr.Left},{ownerWr.Top},{ownerWr.Right},{ownerWr.Bottom}) " +
                $"physCenter=({(int)physOwnerCenter.X},{(int)physOwnerCenter.Y}) " +
                $"ptsOwnerTopLeft=({(int)ownerTopLeft.X},{(int)ownerTopLeft.Y})");
        }
        else {
            SquadDashTrace.Write("ImageEditor",
                $"[ClipboardImageEditor] GetWindowRect failed hwnd={ownerHwnd:X}, using PointToScreen coords");
        }

        // ── Compute display size ─────────────────────────────────────────────
        // We need to convert physical pixel counts to logical (WPF) display units.
        // Two complementary sources of information:
        //   1. Bitmap DpiX — PrintScreen on a scaled monitor embeds the actual DPI
        //      (e.g. DpiX=144 on a 150% monitor). Use this when available.
        //   2. Current monitor scale — Snipping Tool and many clipboard sources strip
        //      DPI metadata and store 96 regardless. Fall back to the monitor the
        //      owner window is on, which gives the correct result when pasting on
        //      the same monitor as the screenshot was taken.
        //
        // Use GetWindowRect-based physical lookup (GetMonitorWorkAreaRect(Window))
        // rather than PointToScreen + TransformFromDevice.  PointToScreen returns
        // DPI-virtualised coordinates for System-DPI-aware processes, and
        // TransformFromDevice reflects the *system* DPI — not the per-monitor DPI —
        // so converting physical rcWork values through it gives physical-pixel units
        // instead of WPF DIPs on monitors whose per-monitor DPI differs from the
        // system DPI.  GetWindowRect always returns physical pixels; dividing by the
        // raw monitor scale (GetRawMonitorScale) yields the correct WPF DIP bounds
        // that match Window.Left / Window.Top.
        var monitorArea = GetMonitorWorkAreaRect(owner);
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

        // Preserve all physical pixels; only relabel DPI metadata to 96.
        // The canvas operates in physical-pixel units and _zoom compensates so the
        // window appears at the correct logical size on the screen.
        _workingImage = DpiHelper.NormalizeTo96Dpi(clipboardImage);
        _originalImage = _workingImage;

        double dispW = imgW;
        double dispH = imgH;
        // Canvas coords == logical pixels, scale factor is 1:1.
        _canvasScaleX = 1.0;
        _canvasScaleY = 1.0;

        SquadDashTrace.Write("ImageEditor",
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

        // Center the editor on the owner window's monitor work area.
        // monitorArea is already in WPF DIP units (GetWindowRect + raw-scale division),
        // matching Window.Left / Window.Top regardless of per-monitor DPI differences.
        Left = monitorArea.Left + (monitorArea.Width  - Width)  / 2.0;
        Top  = monitorArea.Top  + (monitorArea.Height - Height) / 2.0;
        // Clamp so the window stays fully within the work area.
        Left = Math.Max(monitorArea.Left, Math.Min(Left, monitorArea.Right  - Width));
        Top  = Math.Max(monitorArea.Top,  Math.Min(Top,  monitorArea.Bottom - Height));

        // Write to SquadDash trace log (visible in the Trace panel) for diagnostics
        SquadDashTrace.Write("ImageEditor",
            $"[ClipboardImageEditor] owner.WindowState={owner.WindowState} " +
            $"owner.ActualWidth={owner.ActualWidth:F0} owner.ActualHeight={owner.ActualHeight:F0} " +
            $"ownerTopLeft={ownerTopLeft.X:F0},{ownerTopLeft.Y:F0} " +
            $"workArea=({monitorArea.Left:F0},{monitorArea.Top:F0},{monitorArea.Right:F0},{monitorArea.Bottom:F0}) " +
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
            // Alt+Left / Alt+Right: camelCase segment navigation
            if (_activeTextBox is not null && _activeTextBox.IsKeyboardFocusWithin) {
                if (_camelCaseNav.HandlePreviewKeyDown(e, _activeTextBox)) {
                    e.Handled = true;
                    return;
                }
            }

            // Double-Ctrl voice dictation when an annotation text box has focus.
            if (_activeTextBox is not null && _activeTextBox.IsKeyboardFocusWithin) {
                if (_textBoxPttAttachment.HandlePreviewKeyDown(e, _activeTextBox)) {
                    e.Handled = true;
                    return;
                }
            }

            if (e.Key == Key.LeftCtrl || e.Key == Key.RightCtrl)
                RefreshDragCtrlState();

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

            if (e.Key == Key.LeftCtrl || e.Key == Key.RightCtrl)
                RefreshDragCtrlState();

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
            ApplyZoom(_zoom * factor);

            // Mark the scroll viewer dirty BEFORE resizing the window so that the
            // layout pass triggered by UpdateWindowSizeForZoom (via SizeChanged →
            // _scrollViewer.UpdateLayout) picks up the new scale in the same pass.
            // Calling UpdateLayout() here first would cause a visible flicker because
            // the intermediate state (new scale, old window size) would be rendered.
            _scrollViewer.InvalidateMeasure();

            UpdateWindowSizeForZoom();

            // One final flush in case the viewport-size change from the window resize
            // left any pending layout work (e.g. scrollbar re-computation).
            _scrollViewer.UpdateLayout();
            LogScrollViewerState("ZoomApplied");

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
            VerticalAlignment = VerticalAlignment.Center
        };

        _overlayCanvas = new Canvas {
            IsHitTestVisible = false,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Stretch
        };

        _scrollExtentSurface = new Grid {
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Top,
            ClipToBounds = false
        };
        _scrollExtentSurface.Children.Add(canvasWrapper);
        _scrollExtentSurface.Children.Add(_overlayCanvas);

        _scrollViewer = new ScrollViewer {
            HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            Content = _scrollExtentSurface
        };
        _scrollExtentSurface.SetBinding(
            FrameworkElement.MinWidthProperty,
            new Binding(nameof(ScrollViewer.ViewportWidth)) { Source = _scrollViewer });
        _scrollExtentSurface.SetBinding(
            FrameworkElement.MinHeightProperty,
            new Binding(nameof(ScrollViewer.ViewportHeight)) { Source = _scrollViewer });
        UpdateScrollExtentSurfaceSize();
        _scrollViewer.SetResourceReference(StyleProperty, "RosterScrollViewerStyle");
        _scrollViewer.SetResourceReference(BackgroundProperty, "ImageEditorSurround");

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

        var titleText = new TextBlock {
            Text = "Edit Image",
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(10, 0, 0, 0)
        };
        titleText.SetResourceReference(TextBlock.ForegroundProperty, "SubtleText");

        var titleStrip = new Border {
            Height = 36,
            Child = titleText
        };

        var toolbar = BuildToolbar();
        var root = new DockPanel { LastChildFill = true };
        DockPanel.SetDock(titleStrip, Dock.Top);
        DockPanel.SetDock(toolbar, Dock.Bottom);
        root.Children.Add(titleStrip);
        root.Children.Add(toolbar);
        root.Children.Add(_scrollViewer);
        System.Windows.Shell.WindowChrome.SetIsHitTestVisibleInChrome(_scrollViewer, true);
        var outerBorder = ApplyOuterBorder();
        outerBorder.Child = root;

        Loaded += async (_, _) => {
            RefreshLayout();
            UpdateWindowSizeForZoom(); // center on first paint
            if (_sessionState != null)
                await RestoreFromSessionStateAsync(_sessionState);
            else if (_initialState != null)
            {
                RestoreAnnotationState(_initialState);
                _openStateSnapshotJson = System.Text.Json.JsonSerializer.Serialize(CaptureAnnotationState());
            }
            else
                EnterCropMode();
        };

        SizeChanged += (_, _) => {
            _scrollViewer.UpdateLayout();
            LogScrollViewerState("SizeChanged");
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
            ToolTip = "Move/Select (V) — select and move existing annotations"
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
        _addMeasureLineBtn = new Button {
            Content = MakeToolIcon("ImageEditorMeasureLineIcon"),
            Width = 32,
            Height = 28,
            Padding = new Thickness(4, 3, 4, 3),
            Margin = new Thickness(0, 0, 4, 0),
            ToolTip = "Dimension line (D) \u00B7 drag horizontally or vertically to measure pixel distance"
        };
        _addXBtn = new Button {
            Content = MakeToolIcon("ImageEditorXIcon"),
            Width = 32,
            Height = 28,
            Padding = new Thickness(4, 3, 4, 3),
            Margin = new Thickness(0, 0, 4, 0),
            ToolTip = "X annotation (X) · drag to draw · Shift+click for multi-drop"
        };
        _cursorBtn = new Button {
            Content = MakeToolIcon("ImageEditorCursorIcon"),
            Width = 32,
            Height = 28,
            Padding = new Thickness(4, 3, 4, 3),
            Margin = new Thickness(0, 0, 4, 0),
            ToolTip = "Mouse cursor indicator (M) — click to place a cursor overlay"
        };
        _eyedropperBtn = new Button {
            Content = MakeToolIcon("ImageEditorEyedropperIcon"),
            Width = 32,
            Height = 28,
            Padding = new Thickness(4, 3, 4, 3),
            Margin = new Thickness(0, 0, 4, 0),
            ToolTip = "Eyedropper (I) — pick a color from the image"
        };
        var roundCornersBtn = new Button {
            Content = MakeToolIcon("ImageEditorRoundCornersIcon"),
            Width = 32,
            Height = 28,
            Padding = new Thickness(4, 3, 4, 3),
            Margin = new Thickness(0, 0, 4, 0),
            ToolTip = $"Mask the {CornerRadiusPx}px corners transparent in the output PNG"
        };

        string insertLabel = _isUpdateMode ? "Update Image" : _isPromptMode ? "Attach Image" : "Insert Image";
        string insertTooltip = _isUpdateMode
            ? "Update the attached image with your edits"
            : _isPromptMode
                ? "Attach this image to the prompt"
                : "Insert this image into the documentation";
        var insertBtn = new Button {
            Content = insertLabel,
            Width = _isUpdateMode ? 110 : _isPromptMode ? 100 : 96,
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
        _eyedropperHexLabel.ContextMenu.SetResourceReference(ContextMenu.StyleProperty, "ThemedContextMenuStyle");
        var copyHexItem = new MenuItem { Header = "Copy" };
        copyHexItem.SetResourceReference(MenuItem.StyleProperty, "ThemedMenuItemStyle");
        copyHexItem.Click += (_, _) => {
            if (!string.IsNullOrEmpty(_eyedropperHexLabel.Text))
                Clipboard.SetText(_eyedropperHexLabel.Text);
        };
        _eyedropperHexLabel.ContextMenu.Items.Add(copyHexItem);

        // In prompt mode the Round Corners option is not relevant — hide it.
        if (_isPromptMode)
            roundCornersBtn.Visibility = Visibility.Collapsed;

        var styleButtons = _isPromptMode
            ? new[] { _moveSelectBtn, _cropBtn, _addArrowBtn, _addRectBtn, _addTextBtn, _addMeasureLineBtn, _addXBtn, _cursorBtn, _eyedropperBtn, insertBtn, cancelBtn }
            : new[] { _moveSelectBtn, _cropBtn, _addArrowBtn, _addRectBtn, _addTextBtn, _addMeasureLineBtn, _addXBtn, _cursorBtn, _eyedropperBtn, roundCornersBtn, insertBtn, cancelBtn };
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

        _addMeasureLineBtn!.Click += (_, _) => {
            if (_inMeasureLineMode) { ExitMeasureLineMode(returnToMove: true); return; }
            bool isShift = (Keyboard.Modifiers & ModifierKeys.Shift) != 0;
            ExitAllToolModes();
            EnterMeasureLineMode();
            if (isShift) {
                _inMeasureLineMultiDropMode = true;
                ShowModeHint("Multi-drop: drag to place dimension lines · ESC to exit");
                _addMeasureLineBtn.Content = MakeToolIcon("ImageEditorMeasureLineIcon", active: true, multiDrop: true);
            }
            else {
                _addMeasureLineBtn.Content = MakeToolIcon("ImageEditorMeasureLineIcon", active: true);
            }
        };

        _addXBtn!.Click += (_, _) => {
            bool isShift = (Keyboard.Modifiers & ModifierKeys.Shift) != 0;
            if (_inXMode && !isShift) { ExitXMode(returnToMove: true); return; }
            ExitAllToolModes();
            _inXMultiDropMode = isShift;
            EnterXMode();
            if (isShift) ShowModeHint("Multi-drop: drag to place X shapes · ESC to exit");
            _addXBtn.Content = MakeToolIcon("ImageEditorXIcon", active: true, multiDrop: isShift);
        };

        _cursorBtn.Click += (_, _) => {
            // Bug fix: if cursor was already placed (_cursorEnabled true but placement mode
            // exited), re-enter placement mode instead of toggling off.
            if (_cursorEnabled && !_inCursorPlacementMode) {
                ExitAllToolModes();
                _inMoveMode = false;
                _inCropMode = false;
                _cursorEnabled = true;
                _inCursorPlacementMode = true;
                _cursorBtn.Content = MakeToolIcon("ImageEditorCursorIcon", active: true);
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
                _cursorBtn.Content = MakeToolIcon("ImageEditorCursorIcon", active: true);
                _canvas.Cursor = AnnotationCursors.DropCursorTool;
                ShowModeHint("Click to place the cursor indicator");
            }
            else {
                _inCursorPlacementMode = false;
                _cursorBtn.Content = MakeToolIcon("ImageEditorCursorIcon");
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
        resetZoomBtn.Click += (_, _) => { ApplyZoom(1.0); UpdateWindowSizeForZoom(); _scrollViewer.InvalidateMeasure(); _scrollViewer.InvalidateArrange(); _scrollViewer.UpdateLayout(); };

        // Update label whenever zoom changes via keyboard shortcut
        KeyDown += (_, e) => {
            if (e.Key == Key.D0 && (Keyboard.Modifiers & ModifierKeys.Control) != 0) {
                ApplyZoom(1.0);
                UpdateWindowSizeForZoom();
                _scrollViewer.InvalidateMeasure();
                _scrollViewer.InvalidateArrange();
                _scrollViewer.UpdateLayout();
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
        leftStack.Children.Add(_addMeasureLineBtn);
        leftStack.Children.Add(_addXBtn);
        leftStack.Children.Add(_cursorBtn);
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

            if (hwnd != IntPtr.Zero && GetWindowRect(hwnd, out Rect32 wr)) {
                int cx = (wr.Left + wr.Right) / 2;
                int cy = (wr.Top + wr.Bottom) / 2;
                var hMon = MonitorFromPoint(new POINT { X = cx, Y = cy }, MONITOR_DEFAULTTONEAREST);
                if (hMon != IntPtr.Zero) {
                    var mi = new MonitorInfo { cbSize = (uint)System.Runtime.InteropServices.Marshal.SizeOf<MonitorInfo>() };
                    if (GetMonitorInfo(hMon, ref mi)) {
                        // Use PresentationSource transform when available — this is the exact
                        // physical-to-WPF-DIP matrix WPF uses for this window, regardless of
                        // whether the process is System-DPI-aware or Per-Monitor-DPI-aware.
                        var ps = System.Windows.PresentationSource.FromVisual(w);
                        var tfm = ps?.CompositionTarget?.TransformFromDevice;
                        if (tfm.HasValue) {
                            var tl = tfm.Value.Transform(new Point(mi.rcWork.Left, mi.rcWork.Top));
                            var br = tfm.Value.Transform(new Point(mi.rcWork.Right, mi.rcWork.Bottom));
                            SquadDashTrace.Write("ImageEditor",
                                $"[GetMonitorWorkAreaRect] PresentationSource path hMon={hMon:X} " +
                                $"phys=({mi.rcWork.Left},{mi.rcWork.Top},{mi.rcWork.Right},{mi.rcWork.Bottom}) " +
                                $"wpf=({tl.X:F1},{tl.Y:F1},{br.X:F1},{br.Y:F1}) " +
                                $"scale=({tfm.Value.M11:F3},{tfm.Value.M22:F3})");
                            return new Rect(tl, br);
                        }
                        // Fallback: use GetRawMonitorScale (per-monitor DPI) — may be wrong for
                        // System-DPI-aware processes on non-primary monitors, but better than nothing.
                        double s = GetRawMonitorScale(hMon);
                        SquadDashTrace.Write("ImageEditor",
                            $"[GetMonitorWorkAreaRect] rawScale fallback hMon={hMon:X} scale={s:F3} " +
                            $"phys=({mi.rcWork.Left},{mi.rcWork.Top},{mi.rcWork.Right},{mi.rcWork.Bottom}) " +
                            $"wpf=({mi.rcWork.Left/s:F1},{mi.rcWork.Top/s:F1},{mi.rcWork.Right/s:F1},{mi.rcWork.Bottom/s:F1})");
                        return new Rect(
                            mi.rcWork.Left / s,
                            mi.rcWork.Top / s,
                            (mi.rcWork.Right - mi.rcWork.Left) / s,
                            (mi.rcWork.Bottom - mi.rcWork.Top) / s);
                    }
                }
            }
        }
        catch (Exception ex) {
            SquadDashTrace.Write("ImageEditor", $"[GetMonitorWorkAreaRect] exception: {ex.GetType().Name}: {ex.Message}");
        }
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
            SquadDashTrace.Write("ImageEditor", $"[GetRawMonitorScale] hMon={hMon:X} hr={hr:X} dpiX={dpiX}");
            if (hr == 0)
                return dpiX / 96.0;
        }
        catch (Exception ex) {
            SquadDashTrace.Write("ImageEditor", $"[GetRawMonitorScale] exception: {ex.GetType().Name}: {ex.Message}");
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

    private void ApplyZoom(double zoom) {
        _zoom = Math.Max(0.1, Math.Min(8.0, zoom));
        _scaleTransform.ScaleX = _zoom;
        _scaleTransform.ScaleY = _zoom;
        if (_zoomLabel != null)
            _zoomLabel.Text = $"{_zoom * 100:F0}%";
        UpdateScrollExtentSurfaceSize();
    }

    private void UpdateScrollExtentSurfaceSize() {
        if (_scrollExtentSurface is null || _canvas is null)
            return;

        _scrollExtentSurface.Width = Math.Ceiling(_canvas.Width * _zoom + CanvasViewportPadding * 2.0);
        _scrollExtentSurface.Height = Math.Ceiling(_canvas.Height * _zoom + CanvasViewportPadding * 2.0);
        _scrollExtentSurface.InvalidateMeasure();
        _scrollExtentSurface.InvalidateArrange();
    }

    /// <summary>
    /// Writes a single <see cref="TraceCategory.ImageEditor"/> entry capturing the current
    /// scroll viewer layout state (zoom, canvas size, viewport, extent, scrollbar visibility).
    /// <paramref name="context"/> is a short label that identifies the call site
    /// (e.g. "ZoomApplied", "SizeChanged").
    /// </summary>
    private void LogScrollViewerState(string context) {
        var hVis  = _scrollViewer.HorizontalScrollBarVisibility;
        var vVis  = _scrollViewer.VerticalScrollBarVisibility;
        var hComp = _scrollViewer.ComputedHorizontalScrollBarVisibility;
        var vComp = _scrollViewer.ComputedVerticalScrollBarVisibility;
        SquadDashTrace.Write(TraceCategory.ImageEditor,
            $"[ImageEditor] {context}: zoom={_zoom:F2}, " +
            $"canvas={_canvas.ActualWidth:F0}x{_canvas.ActualHeight:F0}, " +
            $"viewport={_scrollViewer.ViewportWidth:F0}x{_scrollViewer.ViewportHeight:F0}, " +
            $"extent={_scrollViewer.ExtentWidth:F0}x{_scrollViewer.ExtentHeight:F0}, " +
            $"H-vis={hVis}({hComp}), V-vis={vVis}({vComp})");
    }

    /// <summary>
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

        double prevLeft = Left, prevTop = Top, prevW = Width, prevH = Height;

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

        SquadDashTrace.Write("ImageEditor",
            $"[UpdateWindowSizeForZoom] zoom={_zoom:F2} " +
            $"canvas={_canvas.Width:F0}x{_canvas.Height:F0} " +
            $"work=({work.Left:F0},{work.Top:F0},{work.Right:F0},{work.Bottom:F0}) " +
            $"prev=({prevLeft:F0},{prevTop:F0},{prevW:F0}x{prevH:F0}) " +
            $"desired=({desiredW:F0}x{desiredH:F0}) new=({newLeft:F0},{newTop:F0},{newW:F0}x{newH:F0})");

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
        if (_inMeasureLineMode) return AnnotationCursors.MeasureLineTool;
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

        if (e.ClickCount != 1 || _inTextMode || _inArrowMode || _inRectMode || _inMeasureLineMode
            || _inCursorPlacementMode || _inEyedropperMode || _suppressNextTextDeselect)
            return;

        var pt = e.GetPosition(_canvas);
        SquadDashTrace.Write("ImageEditor", $"Canvas_PreviewMLBD: pt=({pt.X:F0},{pt.Y:F0}) selectedText={_selectedText != null} texts={_texts.Count}");

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
            SquadDashTrace.Write("ImageEditor", $"Canvas_PreviewMLBD: ann=({left:F0},{top:F0} {dispW:F0}×{dispH:F0}) hitBounds=({hitBounds.Left:F0},{hitBounds.Top:F0} {hitBounds.Width:F0}×{hitBounds.Height:F0}) inBounds={hitBounds.Contains(pt)}");

            if (!hitBounds.Contains(pt)) continue;

            // Hit — select the annotation if not already selected, then start drag.
            if (_selectedText != ann)
                SelectText(ann);

            // Ctrl+drag — duplicate annotation and drag the clone
            if ((Keyboard.Modifiers & ModifierKeys.Control) != 0) {
                PushUndo();
                var clone = new AnnotationText {
                    Bounds = ann.Bounds,
                    Text = ann.Text,
                    FontSize = ann.FontSize,
                    TextColor = ann.TextColor,
                    BackgroundColor = ann.BackgroundColor
                };
                _texts.Add(clone);
                UpdateTextDisplay(clone);
                SelectText(clone);
                _preDragSnapshot = null;
                _canvasTextDragActive = true;
                _canvasTextDragAnnotation = clone;
                _canvasTextDragStart = pt;
                _canvasTextDragOrigBounds = clone.Bounds;
                // Track clone drag for Shift-constraint
                _textCloneDragOriginalCenter = new Point(clone.Bounds.X + clone.Bounds.Width / 2, clone.Bounds.Y + clone.Bounds.Height / 2);
                _canvas.CaptureMouse();
                e.Handled = true;
                return;
            }

            _preDragSnapshot = CaptureSnapshot();
            _suppressUndo = true;
            var textStandby = new AnnotationText {
                Bounds = ann.Bounds, Text = ann.Text, FontSize = ann.FontSize,
                TextColor = ann.TextColor, BackgroundColor = ann.BackgroundColor
            };
            _texts.Add(textStandby);
            UpdateTextDisplay(textStandby);
            _suppressUndo = false;
            SetAnnotationVisible(textStandby, (Keyboard.Modifiers & ModifierKeys.Control) != 0);
            _textMoveStandbyClone = textStandby;
            _canvasTextDragActive = true;
            _canvasTextDragAnnotation = ann;
            _canvasTextDragStart = pt;
            // Capture current canvas position as drag origin (Bounds may have stale/zero W/H)
            _canvasTextDragOrigBounds = new Rect(left, top, dispW, dispH);
            _canvas.CaptureMouse();
            SquadDashTrace.Write("ImageEditor", $"Canvas text-body drag start at ({pt.X:F0},{pt.Y:F0}) orig=({left:F0},{top:F0} {dispW:F0}×{dispH:F0})");
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
            SquadDashTrace.Write("ImageEditor", $"Canvas_MouseDown: skip — mouse already captured by {Mouse.Captured.GetType().Name}");
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

        SquadDashTrace.Write("ImageEditor", $"Canvas_MouseDown: OriginalSource={e.OriginalSource?.GetType().Name ?? "null"} isArrowHitLine={_arrows.Any(a => a.HitLine == e.OriginalSource)} Mouse.Captured={(Mouse.Captured?.GetType().Name ?? "null")}");
        // Skip deselect if the click landed on an arrow hitLine — hitLine.MouseLeftButtonDown
        // handles selection.  Without this guard, SelectArrow(null) would fire before the
        // hitLine handler runs (MouseDown fires before MouseLeftButtonDown).
        if (!(e.OriginalSource is Line arrowSrcLine && _arrows.Any(a => a.HitLine == arrowSrcLine)))
            SelectArrow(null);
        SelectAnnotationRect(null);
        // Skip deselect if the click landed on a measure line's transparent hit zone —
        // hitLine.MouseLeftButtonDown already selected it, but Canvas_MouseDown can still
        // fire before the Handled flag propagates across the MouseDown/MouseLeftButtonDown alias boundary.
        if (!(e.OriginalSource is Line srcLine && _measureLines.Any(m => m.HitLine == srcLine)))
            SelectMeasureLine(null);
        SelectAnnotationX(null);
        _selectedCursor = false;

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

        // X drawing mode: start rubber-band.
        if (_inXMode) {
            _creatingAnnotX = true;
            _annotXAnchor = pt;
            _preDragSnapshot = CaptureSnapshot();
            _canvas.CaptureMouse();
            var (pl1, pl2) = EnsureAnnotXPreview();
            pl1.Visibility = Visibility.Visible;
            pl2.Visibility = Visibility.Visible;
            pl1.X1 = pt.X; pl1.Y1 = pt.Y; pl1.X2 = pt.X; pl1.Y2 = pt.Y;
            pl2.X1 = pt.X; pl2.Y1 = pt.Y; pl2.X2 = pt.X; pl2.Y2 = pt.Y;
            e.Handled = true;
            return;
        }

        // Measure-line drawing mode: start drag-to-draw.
        if (_inMeasureLineMode) {
            _creatingMeasureLine = true;
            _measureLineAnchor = pt;
            _preDragSnapshot = CaptureSnapshot();
            _canvas.CaptureMouse();
            EnsureMlPreview(isHorizontal: true);
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
        // Skip if the click landed on an arrow hitLine — hitLine.MouseLeftButtonDown handles it.
        // (Same pattern as the measure-line guard above.)
        bool clickedArrowHitLine = e.OriginalSource is Line hl && _arrows.Any(a => a.HitLine == hl);
        if (!_inArrowMode && !_inCursorPlacementMode && !_inRectMode && !_inTextMode && !_inMoveMode
            && !clickedArrowHitLine) {
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

            // Shift-snap: constrain to nearest horizontal or vertical axis.
            if ((Keyboard.Modifiers & ModifierKeys.Shift) != 0)
                headPt = SnapArrowToAxis(tailPt, headPt);

            _arrowDragPreviewLine.X1 = tailPt.X;
            _arrowDragPreviewLine.Y1 = tailPt.Y;
            _arrowDragPreviewLine.X2 = headPt.X;
            _arrowDragPreviewLine.Y2 = headPt.Y;

            var dx = headPt.X - tailPt.X;
            var dy = headPt.Y - tailPt.Y;
            var dist2 = Math.Sqrt(dx * dx + dy * dy);
            if (dist2 > 0) {
                var ux2 = dx / dist2; var uy2 = dy / dist2;
                // Cap arrowhead to half the drag distance so it never swallows the shaft.
                double HeadLen = Math.Min(16.0, dist2 * 0.5);
                double HeadHalf = 6.0 * HeadLen / 16.0;
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
                HideCrosshair();
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

        // Rubber-band draw of an X annotation.
        if (_creatingAnnotX) {
            var cur = e.GetPosition(_canvas);
            double x = Math.Min(_annotXAnchor.X, cur.X);
            double y = Math.Min(_annotXAnchor.Y, cur.Y);
            double w = Math.Abs(cur.X - _annotXAnchor.X);
            double h = Math.Abs(cur.Y - _annotXAnchor.Y);
            var (pl1, pl2) = EnsureAnnotXPreview();
            pl1.X1 = x; pl1.Y1 = y; pl1.X2 = x + w; pl1.Y2 = y + h;
            pl2.X1 = x + w; pl2.Y1 = y; pl2.X2 = x; pl2.Y2 = y + h;
            e.Handled = true;
            return;
        }

        // Live preview for measure-line drag-to-draw.
        if (_creatingMeasureLine) {
            var cur = e.GetPosition(_canvas);
            var dx = cur.X - _measureLineAnchor.X;
            var dy = cur.Y - _measureLineAnchor.Y;
            bool isH = Math.Abs(dx) >= Math.Abs(dy);
            Point p1, p2;
            if (isH) {
                double y = _measureLineAnchor.Y;
                p1 = new Point(Math.Min(_measureLineAnchor.X, cur.X), y);
                p2 = new Point(Math.Max(_measureLineAnchor.X, cur.X), y);
            }
            else {
                double x = _measureLineAnchor.X;
                p1 = new Point(x, Math.Min(_measureLineAnchor.Y, cur.Y));
                p2 = new Point(x, Math.Max(_measureLineAnchor.Y, cur.Y));
            }
            EnsureMlPreview(isH);
            UpdateMlPreview(p1, p2, isH);
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
            
            // Apply Shift-key alignment constraint (move or clone drag)
            if ((Keyboard.Modifiers & ModifierKeys.Shift) != 0) {
                var mouseDX = movePt.X - _canvasTextDragStart.X;
                var mouseDY = movePt.Y - _canvasTextDragStart.Y;
                if (Math.Abs(mouseDX) >= Math.Abs(mouseDY))
                    newY = _canvasTextDragOrigBounds.Y;
                else
                    newX = _canvasTextDragOrigBounds.X;
            }
            
            ann.Bounds = new Rect(newX, newY, ann.Bounds.Width, ann.Bounds.Height);
            if (ann.Display != null) {
                Canvas.SetLeft(ann.Display, newX);
                Canvas.SetTop(ann.Display, newY);
            }
            // Keep the live TextBox in sync when dragging while in edit mode.
            if (_activeTextBox != null && ann == _editingText) {
                Canvas.SetLeft(_activeTextBox, newX);
                Canvas.SetTop(_activeTextBox, newY);
            }
            if (_textMoveStandbyClone != null)
                SetAnnotationVisible(_textMoveStandbyClone, (Keyboard.Modifiers & ModifierKeys.Control) != 0);
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
            _canvas.Cursor = AnnotationCursors.EyedropperTool;
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
            else if (_inXMode)
                _canvas.Cursor = AnnotationCursors.XTool;
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
            if (!_inArrowMode && !_inRectMode && !_inXMode && !_inTextMode && !_inCursorPlacementMode && !_inEyedropperMode
                && IsHoveringOverAnnotation(e.GetPosition(_canvas)))
                _canvas.Cursor = Cursors.Arrow;

            // Override with directional cursor when hovering over a selected annotation rect's corner handle.
            // Edge midpoints (N/S/E/W) show SizeAll — dragging those moves the whole rect, not resizes it.
            // The resize-handle Rectangle elements themselves still override with their own directional cursor
            // when the mouse is directly over one (WPF element Cursor takes priority over Canvas.Cursor).
            // — but not while a tool mode is active (clicking would draw a new shape, not resize).
            if (_selectedAnnotRect != null && !_inArrowMode && !_inRectMode) {
                var az = HitTestAnnotRect(_selectedAnnotRect, e.GetPosition(_canvas));
                if (az != HitZone.None)
                    _canvas.Cursor = (az is HitZone.N or HitZone.S or HitZone.E or HitZone.W)
                        ? Cursors.SizeAll
                        : ZoneCursor(az);
            }
        }
    }

    private void Canvas_MouseUp(object sender, MouseButtonEventArgs e) {
        if (_canvasTextDragActive) {
            SquadDashTrace.Write("ImageEditor", $"Canvas text-body drag end at ({e.GetPosition(_canvas).X:F0},{e.GetPosition(_canvas).Y:F0})");
            if (_textMoveStandbyClone != null) {
                if ((Keyboard.Modifiers & ModifierKeys.Control) != 0)
                    SetAnnotationVisible(_textMoveStandbyClone, true);
                else {
                    _suppressUndo = true;
                    RemoveTextAnnotation(_textMoveStandbyClone);
                    _suppressUndo = false;
                }
                _textMoveStandbyClone = null;
            }
            CommitDragUndo();
            _canvasTextDragActive = false;
            _canvasTextDragAnnotation = null;
            _canvas.ReleaseMouseCapture();
            _canvas.Cursor = Cursors.Arrow;
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

            // Shift-snap: constrain to nearest horizontal or vertical axis.
            if ((Keyboard.Modifiers & ModifierKeys.Shift) != 0)
                headPt = SnapArrowToAxis(tailPt, headPt);

            var dx = headPt.X - tailPt.X;
            var dy = headPt.Y - tailPt.Y;
            var dist = Math.Sqrt(dx * dx + dy * dy);

            if (dist >= 1.0) {
                // Any meaningful drag produces an arrow exactly matching the drag vector.
                _preDragSnapshot = null; // let CreateArrow handle its own undo push
                PlaceArrowFromDrag(tailPt, headPt, dist);
            }
            else {
                // Click without drag: drop an arrow at the click point using the last-used angle/length.
                _preDragSnapshot = null;
                var rad = _lastDragArrowAngleDeg * Math.PI / 180.0;
                var ux = Math.Sin(rad);
                var uy = -Math.Cos(rad);
                var synTailPt = new Point(headPt.X + ux * _lastDragArrowTailLength,
                                          headPt.Y + uy * _lastDragArrowTailLength);
                PlaceArrowFromDrag(synTailPt, headPt, _lastDragArrowTailLength);
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

        if (_creatingAnnotX) {
            _creatingAnnotX = false;
            _canvas.ReleaseMouseCapture();
            RemoveAnnotXPreview();

            var cur = e.GetPosition(_canvas);
            double dx = Math.Abs(cur.X - _annotXAnchor.X);
            double dy = Math.Abs(cur.Y - _annotXAnchor.Y);
            bool wasDrag = dx >= MinSize && dy >= MinSize;

            if (wasDrag) {
                var bounds = new Rect(
                    Math.Min(_annotXAnchor.X, cur.X),
                    Math.Min(_annotXAnchor.Y, cur.Y),
                    dx, dy);
                _preDragSnapshot = null;
                _lastDragXWidth = bounds.Width;
                _lastDragXHeight = bounds.Height;
                CreateAnnotationX(bounds);
            }
            else if (dx < 5 && dy < 5) {
                _preDragSnapshot = null;
                var rw = _lastDragXWidth;
                var rh = _lastDragXHeight;
                var rx = Math.Max(0, Math.Min(_canvas.Width - rw, _annotXAnchor.X - rw / 2));
                var ry = Math.Max(0, Math.Min(_canvas.Height - rh, _annotXAnchor.Y - rh / 2));
                CreateAnnotationX(new Rect(rx, ry, rw, rh));
            }
            else {
                _preDragSnapshot = null;
            }

            CommitDragUndo();
            if (_inXMultiDropMode) {
                _canvas.Cursor = AnnotationCursors.XTool;
                ShowModeHint("Multi-drop: drag to place X shapes · ESC to exit");
            }
            else {
                ExitXMode(returnToMove: true);
            }
            e.Handled = true;
            return;
        }

        if (_creatingMeasureLine) {
            _creatingMeasureLine = false;
            _canvas.ReleaseMouseCapture();
            RemoveMlPreview();

            var upPt = e.GetPosition(_canvas);
            var dx2 = upPt.X - _measureLineAnchor.X;
            var dy2 = upPt.Y - _measureLineAnchor.Y;
            bool isH2 = Math.Abs(dx2) >= Math.Abs(dy2);
            double span = isH2 ? Math.Abs(dx2) : Math.Abs(dy2);

            if (span >= 2.0) {
                Point p1, p2;
                if (isH2) {
                    double y = _measureLineAnchor.Y;
                    p1 = new Point(Math.Min(_measureLineAnchor.X, upPt.X), y);
                    p2 = new Point(Math.Max(_measureLineAnchor.X, upPt.X), y);
                }
                else {
                    double x = _measureLineAnchor.X;
                    p1 = new Point(x, Math.Min(_measureLineAnchor.Y, upPt.Y));
                    p2 = new Point(x, Math.Max(_measureLineAnchor.Y, upPt.Y));
                }
                _preDragSnapshot = null; // let CreateMeasureLine push its own undo entry
                var newLine = CreateMeasureLine(p1, p2, isH2);
                SelectMeasureLine(newLine);
            }
            else {
                _preDragSnapshot = null;
            }
            CommitDragUndo();
            if (_inMeasureLineMultiDropMode) {
                // Multi-drop: stay in measure-line mode; reset canvas cursor for next drag
                _canvas.Cursor = AnnotationCursors.MeasureLineTool;
                ShowModeHint("Multi-drop: drag to place dimension lines · ESC to exit");
            }
            else {
                ExitMeasureLineMode(returnToMove: true);
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
            if (_inXMode) {
                if (_creatingAnnotX) {
                    _creatingAnnotX = false;
                    _canvas.ReleaseMouseCapture();
                    RemoveAnnotXPreview();
                    _preDragSnapshot = null;
                }
                ExitXMode(returnToMove: true); e.Handled = true; return;
            }
            if (_inMeasureLineMode) {
                if (_creatingMeasureLine) {
                    _creatingMeasureLine = false;
                    _canvas.ReleaseMouseCapture();
                    RemoveMlPreview();
                    _preDragSnapshot = null;
                }
                ExitMeasureLineMode(returnToMove: true);
                e.Handled = true;
                return;
            }
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
            // Priority 2 — deselect a selected annotation (arrow, rect, X, measure line, or text label).
            if (_selectedArrow != null) { SelectArrow(null); e.Handled = true; return; }
            if (_selectedAnnotRect != null) { SelectAnnotationRect(null); e.Handled = true; return; }
            if (_selectedAnnotX != null) { SelectAnnotationX(null); e.Handled = true; return; }
            if (_selectedMeasureLine != null) { SelectMeasureLine(null); e.Handled = true; return; }
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
        else if (e.Key == Key.Delete && _selectedAnnotX != null) {
            PushUndo();
            RemoveAnnotationX(_selectedAnnotX);
            _selectedAnnotX = null;
            e.Handled = true;
        }
        else if (e.Key == Key.Delete && _selectedMeasureLine != null) {
            PushUndo();
            RemoveMeasureLine(_selectedMeasureLine);
            _selectedMeasureLine = null;
            e.Handled = true;
        }
        else if (e.Key == Key.Delete && _selectedText != null && _editingText == null) {
            PushUndo();
            var toDelete = _selectedText;
            SelectText(null);
            RemoveTextAnnotation(toDelete);
            e.Handled = true;
        }
        else if (e.Key == Key.Delete && _selectedCursor) {
            PushUndo();
            _cursorEnabled = false;
            if (_cursorImage != null) {
                _cursorImage.Visibility = Visibility.Collapsed;
            }
            _selectedCursor = false;
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
        else if (e.Key == Key.Z && Keyboard.Modifiers == (ModifierKeys.Control | ModifierKeys.Shift)) {
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
            if (e.Key == Key.V) {
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
            else if (e.Key == Key.X && !shift) {
                ExitAllToolModes();
                EnterXMode();
                if (_addXBtn != null) _addXBtn.Content = MakeToolIcon("ImageEditorXIcon", active: true);
                e.Handled = true;
            }
            else if (e.Key == Key.X && shift) {
                ExitAllToolModes();
                _inXMultiDropMode = true;
                EnterXMode();
                ShowModeHint("Multi-drop: drag to place X shapes · ESC to exit");
                if (_addXBtn != null) _addXBtn.Content = MakeToolIcon("ImageEditorXIcon", active: true, multiDrop: true);
                e.Handled = true;
            }
            else if (e.Key == Key.T) {
                ExitAllToolModes();
                EnterTextMode();
                if (_addTextBtn != null) _addTextBtn.Content = MakeToolIcon("ImageEditorTextIcon", active: true);
                e.Handled = true;
            }
            else if (e.Key == Key.D && !shift) {
                ExitAllToolModes();
                EnterMeasureLineMode();
                if (_addMeasureLineBtn != null) _addMeasureLineBtn.Content = MakeToolIcon("ImageEditorMeasureLineIcon", active: true);
                e.Handled = true;
            }
            else if (e.Key == Key.D && shift) {
                ExitAllToolModes();
                EnterMeasureLineMode();
                _inMeasureLineMultiDropMode = true;
                ShowModeHint("Multi-drop: drag to place dimension lines · ESC to exit");
                if (_addMeasureLineBtn != null) _addMeasureLineBtn.Content = MakeToolIcon("ImageEditorMeasureLineIcon", active: true, multiDrop: true);
                e.Handled = true;
            }
            else if (e.Key == Key.C) {
                ExitAllToolModes();
                EnterCropMode();
                e.Handled = true;
            }
            else if (e.Key == Key.I) {
                if (_inEyedropperMode) { ExitEyedropperMode(); EnterMoveMode(); }
                else { ExitAllToolModes(); EnterEyedropperMode(); if (_eyedropperBtn != null) _eyedropperBtn.Content = MakeToolIcon("ImageEditorEyedropperIcon", active: true); }
                e.Handled = true;
            }
            else if (e.Key == Key.M) {
                if (_cursorEnabled && !_inCursorPlacementMode) {
                    ExitAllToolModes();
                    _inMoveMode = false;
                    _inCropMode = false;
                    _cursorEnabled = true;
                    _inCursorPlacementMode = true;
                    if (_cursorBtn != null) _cursorBtn.Content = MakeToolIcon("ImageEditorCursorIcon", active: true);
                    _canvas.Cursor = AnnotationCursors.DropCursorTool;
                    ShowModeHint("Click to place the cursor indicator");
                }
                else {
                    ExitAllToolModes();
                    _cursorEnabled = !_cursorEnabled;
                    if (_cursorEnabled) {
                        _inMoveMode = false;
                        _inCropMode = false;
                        _inCursorPlacementMode = true;
                        if (_cursorBtn != null) _cursorBtn.Content = MakeToolIcon("ImageEditorCursorIcon", active: true);
                        _canvas.Cursor = AnnotationCursors.DropCursorTool;
                        ShowModeHint("Click to place the cursor indicator");
                    }
                    else {
                        _inCursorPlacementMode = false;
                        if (_cursorBtn != null) _cursorBtn.Content = MakeToolIcon("ImageEditorCursorIcon");
                        _canvas.Cursor = Cursors.Arrow;
                        ToggleCursorOverlay(false);
                        HideModeHint();
                        EnterMoveMode();
                    }
                }
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
        if (_inXMode) ExitXMode();
        if (_inTextMode) ExitTextMode();
        if (_activeTextBox != null) CommitActiveTextBox(); // commit in-progress edit even if text mode was already exited
        if (_inEyedropperMode) ExitEyedropperMode();
        if (_inMeasureLineMode) ExitMeasureLineMode();
        SelectText(null);
    }

    // ── Measure-line mode ─────────────────────────────────────────────────────

    private void EnterMeasureLineMode() {
        if (_selectedArrow != null) SelectArrow(null);
        if (_selectedAnnotRect != null) SelectAnnotationRect(null);
        if (_selectedText != null) SelectText(null);
        _inMoveMode = false;
        _inCropMode = false;
        _inMeasureLineMode = true;
        _canvas.Cursor = AnnotationCursors.MeasureLineTool;
        ShowModeHint("Drag to draw a dimension line · ESC to exit");
        if (_addMeasureLineBtn != null) _addMeasureLineBtn.Content = MakeToolIcon("ImageEditorMeasureLineIcon", active: true);
    }

    private void ExitMeasureLineMode(bool returnToMove = false) {
        _inMeasureLineMode = false;
        _inMeasureLineMultiDropMode = false;
        _canvas.Cursor = Cursors.Arrow;
        Cursor = Cursors.Arrow;
        HideModeHint();
        if (_addMeasureLineBtn != null) _addMeasureLineBtn.Content = MakeToolIcon("ImageEditorMeasureLineIcon");
        if (returnToMove) EnterMoveMode();
    }

    // Ensure preview shapes exist for the current isHorizontal orientation.
    private void EnsureMlPreview(bool isHorizontal) {
        if (_mlPreviewLine != null && _mlPreviewIsHorizontal == isHorizontal) return;
        RemoveMlPreview();
        _mlPreviewIsHorizontal = isHorizontal;

        var stroke = new SolidColorBrush(_defaultMeasureLineColor);

        _mlPreviewLine = new Line {
            Stroke = stroke, StrokeThickness = 2, Opacity = 0.7,
            IsHitTestVisible = false
        };
        Panel.SetZIndex(_mlPreviewLine, 99);
        _canvas.Children.Add(_mlPreviewLine);

        for (int i = 0; i < 2; i++) {
            var head = new Polygon {
                Fill = stroke, Opacity = 0.7, IsHitTestVisible = false
            };
            Panel.SetZIndex(head, 99);
            _canvas.Children.Add(head);
            if (i == 0) _mlPreviewHead1 = head;
            else _mlPreviewHead2 = head;
        }
        for (int i = 0; i < 2; i++) {
            var cap = new Line {
                Stroke = stroke, StrokeThickness = 1.0, Opacity = 0.7,
                IsHitTestVisible = false
            };
            Panel.SetZIndex(cap, 99);
            _canvas.Children.Add(cap);
            if (i == 0) _mlPreviewCap1 = cap;
            else _mlPreviewCap2 = cap;
        }

        _mlPreviewBadgeText = new TextBlock {
            Foreground = Brushes.White, FontSize = 12, FontWeight = FontWeights.SemiBold,
            IsHitTestVisible = false
        };
        _mlPreviewBadge = new Border {
            Background = new SolidColorBrush(Color.FromArgb(200, 0, 0, 0)),
            CornerRadius = new CornerRadius(3),
            Padding = new Thickness(4, 2, 4, 2),
            Child = _mlPreviewBadgeText,
            Opacity = 0.7,
            IsHitTestVisible = false
        };
        Panel.SetZIndex(_mlPreviewBadge, 99);
        _canvas.Children.Add(_mlPreviewBadge);
    }

    private void RemoveMlPreview() {
        if (_mlPreviewLine != null) { _canvas.Children.Remove(_mlPreviewLine); _mlPreviewLine = null; }
        if (_mlPreviewHead1 != null) { _canvas.Children.Remove(_mlPreviewHead1); _mlPreviewHead1 = null; }
        if (_mlPreviewHead2 != null) { _canvas.Children.Remove(_mlPreviewHead2); _mlPreviewHead2 = null; }
        if (_mlPreviewCap1 != null) { _canvas.Children.Remove(_mlPreviewCap1); _mlPreviewCap1 = null; }
        if (_mlPreviewCap2 != null) { _canvas.Children.Remove(_mlPreviewCap2); _mlPreviewCap2 = null; }
        if (_mlPreviewBadge != null) { _canvas.Children.Remove(_mlPreviewBadge); _mlPreviewBadge = null; _mlPreviewBadgeText = null; }
    }

    private void UpdateMlPreview(Point p1, Point p2, bool isH) {
        const double aLen = 14.0, aHalf = 6.0, capHalf = 9.0, arrowGap = 1.0;

        double span = isH ? Math.Abs(p2.X - p1.X) : Math.Abs(p2.Y - p1.Y);
        double innerSpan = Math.Max(0, span - 2 * arrowGap);
        double arrowScale = (innerSpan < 2 * aLen) ? innerSpan / (2 * aLen) : 1.0;
        double scaledLen = aLen * arrowScale;
        double scaledHalf = aHalf * arrowScale;

        if (isH) {
            double tip1X = p1.X + arrowGap;
            double tip2X = p2.X - arrowGap;
            double lineInset = scaledLen / 3.0;
            if (_mlPreviewLine != null) {
                _mlPreviewLine.X1 = tip1X + lineInset; _mlPreviewLine.Y1 = p1.Y;
                _mlPreviewLine.X2 = tip2X - lineInset; _mlPreviewLine.Y2 = p2.Y;
            }
            _mlPreviewHead1?.Points.Clear();
            _mlPreviewHead1?.Points.Add(new Point(tip1X, p1.Y));
            _mlPreviewHead1?.Points.Add(new Point(tip1X + scaledLen, p1.Y - scaledHalf));
            _mlPreviewHead1?.Points.Add(new Point(tip1X + scaledLen, p1.Y + scaledHalf));
            _mlPreviewHead2?.Points.Clear();
            _mlPreviewHead2?.Points.Add(new Point(tip2X, p2.Y));
            _mlPreviewHead2?.Points.Add(new Point(tip2X - scaledLen, p2.Y - scaledHalf));
            _mlPreviewHead2?.Points.Add(new Point(tip2X - scaledLen, p2.Y + scaledHalf));
            if (_mlPreviewCap1 != null) { _mlPreviewCap1.X1 = p1.X; _mlPreviewCap1.Y1 = p1.Y - capHalf; _mlPreviewCap1.X2 = p1.X; _mlPreviewCap1.Y2 = p1.Y + capHalf; }
            if (_mlPreviewCap2 != null) { _mlPreviewCap2.X1 = p2.X; _mlPreviewCap2.Y1 = p2.Y - capHalf; _mlPreviewCap2.X2 = p2.X; _mlPreviewCap2.Y2 = p2.Y + capHalf; }
        }
        else {
            double tip1Y = p1.Y + arrowGap;
            double tip2Y = p2.Y - arrowGap;
            double lineInset = scaledLen / 3.0;
            if (_mlPreviewLine != null) {
                _mlPreviewLine.X1 = p1.X; _mlPreviewLine.Y1 = tip1Y + lineInset;
                _mlPreviewLine.X2 = p2.X; _mlPreviewLine.Y2 = tip2Y - lineInset;
            }
            _mlPreviewHead1?.Points.Clear();
            _mlPreviewHead1?.Points.Add(new Point(p1.X, tip1Y));
            _mlPreviewHead1?.Points.Add(new Point(p1.X - scaledHalf, tip1Y + scaledLen));
            _mlPreviewHead1?.Points.Add(new Point(p1.X + scaledHalf, tip1Y + scaledLen));
            _mlPreviewHead2?.Points.Clear();
            _mlPreviewHead2?.Points.Add(new Point(p2.X, tip2Y));
            _mlPreviewHead2?.Points.Add(new Point(p2.X - scaledHalf, tip2Y - scaledLen));
            _mlPreviewHead2?.Points.Add(new Point(p2.X + scaledHalf, tip2Y - scaledLen));
            if (_mlPreviewCap1 != null) { _mlPreviewCap1.X1 = p1.X - capHalf; _mlPreviewCap1.Y1 = p1.Y; _mlPreviewCap1.X2 = p1.X + capHalf; _mlPreviewCap1.Y2 = p1.Y; }
            if (_mlPreviewCap2 != null) { _mlPreviewCap2.X1 = p2.X - capHalf; _mlPreviewCap2.Y1 = p2.Y; _mlPreviewCap2.X2 = p2.X + capHalf; _mlPreviewCap2.Y2 = p2.Y; }
        }

        if (_mlPreviewBadge != null && _mlPreviewBadgeText != null) {
            double canvasDist = isH ? Math.Abs(p2.X - p1.X) : Math.Abs(p2.Y - p1.Y);
            double scaleX = _canvasScaleX > 0 ? _canvasScaleX : 1.0;
            double scaleY = _canvasScaleY > 0 ? _canvasScaleY : 1.0;
            int px = (int)Math.Round(canvasDist / (isH ? scaleX : scaleY));
            _mlPreviewBadgeText.Text = $"{px} px";
            _mlPreviewBadge.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            double bw = _mlPreviewBadge.DesiredSize.Width;
            double bh = _mlPreviewBadge.DesiredSize.Height;
            double midX = (p1.X + p2.X) / 2;
            double midY = (p1.Y + p2.Y) / 2;
            const double labelPerpOutside = 8.0;
            double bx, by;
            if (isH) {
                bool inside = span >= bw + 50.0;
                bx = midX - bw / 2;
                by = inside ? midY - bh / 2 : midY - labelPerpOutside - bh;
            }
            else {
                bool inside = span >= bh + 50.0;
                by = midY - bh / 2;
                bx = inside ? midX - bw / 2 : midX - labelPerpOutside - bw;
            }
            bx = Math.Max(0, Math.Min(_canvas.Width - bw, bx));
            by = Math.Max(0, Math.Min(_canvas.Height - bh, by));
            Canvas.SetLeft(_mlPreviewBadge, bx);
            Canvas.SetTop(_mlPreviewBadge, by);
        }
    }

    private AnnotationMeasureLine CreateMeasureLine(Point p1, Point p2, bool isH, Color? color = null) {
        if (!_suppressUndo) PushUndo();
        var lineColor = color ?? _defaultMeasureLineColor;
        var stroke = new SolidColorBrush(lineColor);

        var main = new Line { Stroke = stroke, StrokeThickness = 2, IsHitTestVisible = false };
        Panel.SetZIndex(main, 5);
        _canvas.Children.Add(main);

        var head1 = new Polygon { Fill = stroke, IsHitTestVisible = false };
        Panel.SetZIndex(head1, 5);
        _canvas.Children.Add(head1);

        var head2 = new Polygon { Fill = stroke, IsHitTestVisible = false };
        Panel.SetZIndex(head2, 5);
        _canvas.Children.Add(head2);

        var cap1 = new Line { Stroke = stroke, StrokeThickness = 1.0, Opacity = 0.5, IsHitTestVisible = false };
        Panel.SetZIndex(cap1, 5);
        _canvas.Children.Add(cap1);

        var cap2 = new Line { Stroke = stroke, StrokeThickness = 1.0, Opacity = 0.5, IsHitTestVisible = false };
        Panel.SetZIndex(cap2, 5);
        _canvas.Children.Add(cap2);

        // Badge
        var labelText = new TextBlock {
            Foreground = Brushes.White,
            FontSize = 12,
            FontWeight = FontWeights.SemiBold,
            IsHitTestVisible = false
        };
        var badge = new Border {
            Background = new SolidColorBrush(Color.FromArgb(255, 0, 0, 0)),
            CornerRadius = new CornerRadius(3),
            Padding = new Thickness(4, 2, 4, 2),
            Child = labelText,
            IsHitTestVisible = false
        };
        Panel.SetZIndex(badge, 5);
        _canvas.Children.Add(badge);

        // Hit line (transparent, wider for clicking)
        var hitLine = new Line { Stroke = Brushes.Transparent, StrokeThickness = 14, IsHitTestVisible = true, Cursor = Cursors.Arrow };
        Panel.SetZIndex(hitLine, 4);
        _canvas.Children.Add(hitLine);

        // Endpoint drag handles — visible only when the line is selected
        var endpointCursor = isH ? Cursors.SizeWE : Cursors.SizeNS;
        var handle1 = new Ellipse {
            Width = 8, Height = 8, Fill = Brushes.White, Stroke = Brushes.Black, StrokeThickness = 1,
            Cursor = endpointCursor, Visibility = Visibility.Hidden, IsHitTestVisible = true
        };
        var handle2 = new Ellipse {
            Width = 8, Height = 8, Fill = Brushes.White, Stroke = Brushes.Black, StrokeThickness = 1,
            Cursor = endpointCursor, Visibility = Visibility.Hidden, IsHitTestVisible = true
        };
        Panel.SetZIndex(handle1, 10);
        Panel.SetZIndex(handle2, 10);
        _canvas.Children.Add(handle1);
        _canvas.Children.Add(handle2);

        var ml = new AnnotationMeasureLine {
            StartPt = p1, EndPt = p2, IsHorizontal = isH, LineColor = lineColor,
            MainLine = main, Head1 = head1, Head2 = head2, Cap1 = cap1, Cap2 = cap2,
            LabelBadge = badge, LabelText = labelText, HitLine = hitLine,
            Handle1 = handle1, Handle2 = handle2
        };
        _measureLines.Add(ml);

        // Body drag — select + translate the whole line
        hitLine.MouseLeftButtonDown += (_, e2) => {
            e2.Handled = true;  // mark immediately so Canvas_MouseDown doesn't deselect
            if ((Keyboard.Modifiers & ModifierKeys.Control) != 0) {
                // Ctrl+drag: duplicate the measure line; original stays, clone is dragged.
                PushUndo();
                _suppressUndo = true;
                var clone = CreateMeasureLine(ml.StartPt, ml.EndPt, ml.IsHorizontal, ml.LineColor);
                _suppressUndo = false;
                SelectMeasureLine(clone);
                _draggingMeasureLine = clone;
                _measureLineDragStart = e2.GetPosition(_canvas);
                _measureLineDragOrigStart = clone.StartPt;
                _measureLineDragOrigEnd = clone.EndPt;
                _preDragSnapshot = null;
                // Track clone drag for Shift-constraint
                _mlCloneDragOriginalCenter = new Point(
                    (clone.StartPt.X + clone.EndPt.X) / 2,
                    (clone.StartPt.Y + clone.EndPt.Y) / 2);
                clone.HitLine.CaptureMouse();
                return;
            }
            SelectMeasureLine(ml);
            _preDragSnapshot = CaptureSnapshot();
            _suppressUndo = true;
            var mlStandby = CreateMeasureLine(ml.StartPt, ml.EndPt, ml.IsHorizontal, ml.LineColor);
            _suppressUndo = false;
            SetAnnotationVisible(mlStandby, (Keyboard.Modifiers & ModifierKeys.Control) != 0);
            _mlMoveStandbyClone = mlStandby;
            // CreateMeasureLine called SelectMeasureLine(standby), hiding ml's handles. Restore them.
            mlStandby.Handle1.Visibility = Visibility.Collapsed;
            mlStandby.Handle2.Visibility = Visibility.Collapsed;
            ml.Handle1.Visibility = Visibility.Visible;
            ml.Handle2.Visibility = Visibility.Visible;
            _selectedMeasureLine = ml;
            HideColorPicker();
            _draggingMeasureLine = ml;
            _measureLineDragStart = e2.GetPosition(_canvas);
            _measureLineDragOrigStart = ml.StartPt;
            _measureLineDragOrigEnd = ml.EndPt;
            _mlCloneDragOriginalCenter = new Point(
                (ml.StartPt.X + ml.EndPt.X) / 2,
                (ml.StartPt.Y + ml.EndPt.Y) / 2);
            hitLine.CaptureMouse();
        };
        hitLine.MouseMove += (_, e2) => {
            if (_draggingMeasureLine != ml || _mlDraggingHandle) return;
            var pt = e2.GetPosition(_canvas);
            var dx = pt.X - _measureLineDragStart.X;
            var dy = pt.Y - _measureLineDragStart.Y;
            
            // Apply Shift-key alignment constraint (move or clone drag)
            if ((Keyboard.Modifiers & ModifierKeys.Shift) != 0) {
                if (Math.Abs(dx) >= Math.Abs(dy)) dy = 0;
                else dx = 0;
            }
            ml.StartPt = new Point(_measureLineDragOrigStart.X + dx, _measureLineDragOrigStart.Y + dy);
            ml.EndPt   = new Point(_measureLineDragOrigEnd.X   + dx, _measureLineDragOrigEnd.Y   + dy);
            UpdateMeasureLineGeometry(ml);
            RepositionMeasureLineColorPicker(ml);
            if (_mlMoveStandbyClone != null)
                SetAnnotationVisible(_mlMoveStandbyClone, (Keyboard.Modifiers & ModifierKeys.Control) != 0);
            e2.Handled = true;
        };
        hitLine.MouseLeftButtonUp+= (_, e2) => {
            if (_draggingMeasureLine != ml || _mlDraggingHandle) return;
            if (_mlMoveStandbyClone != null) {
                if ((Keyboard.Modifiers & ModifierKeys.Control) != 0)
                    SetAnnotationVisible(_mlMoveStandbyClone, true);
                else {
                    _suppressUndo = true;
                    RemoveMeasureLine(_mlMoveStandbyClone);
                    _suppressUndo = false;
                }
                _mlMoveStandbyClone = null;
            }
            CommitDragUndo();
            _draggingMeasureLine = null;
            hitLine.ReleaseMouseCapture();
            _canvas.Cursor = Cursors.Arrow;
            ShowColorPickerForMeasureLine(ml);
            e2.Handled = true;
        };
        hitLine.MouseRightButtonDown += (_, e2) => {
            SelectMeasureLine(ml);
            ShowColorPickerForMeasureLine(ml);
            e2.Handled = true;
        };

        // Handle1 drag — moves StartPt along the constrained axis
        handle1.MouseLeftButtonDown += (_, e2) => {
            SelectMeasureLine(ml);
            _preDragSnapshot = CaptureSnapshot();
            _draggingMeasureLine = ml;
            _mlDraggingHandle = true;
            _mlDraggingHandle1 = true;
            handle1.CaptureMouse();
            e2.Handled = true;
        };
        handle1.MouseMove += (_, e2) => {
            if (_draggingMeasureLine != ml || !_mlDraggingHandle || !_mlDraggingHandle1) return;
            var pt = e2.GetPosition(_canvas);
            ml.StartPt = ml.IsHorizontal
                ? new Point(Math.Min(pt.X, ml.EndPt.X - 8), ml.StartPt.Y)
                : new Point(ml.StartPt.X, Math.Min(pt.Y, ml.EndPt.Y - 8));
            UpdateMeasureLineGeometry(ml);
            RepositionMeasureLineColorPicker(ml);
            e2.Handled = true;
        };
        handle1.MouseLeftButtonUp += (_, e2) => {
            if (_draggingMeasureLine != ml || !_mlDraggingHandle) return;
            CommitDragUndo();
            _draggingMeasureLine = null;
            _mlDraggingHandle = false;
            handle1.ReleaseMouseCapture();
            e2.Handled = true;
        };

        // Handle2 drag — moves EndPt along the constrained axis
        handle2.MouseLeftButtonDown += (_, e2) => {
            SelectMeasureLine(ml);
            _preDragSnapshot = CaptureSnapshot();
            _draggingMeasureLine = ml;
            _mlDraggingHandle = true;
            _mlDraggingHandle1 = false;
            handle2.CaptureMouse();
            e2.Handled = true;
        };
        handle2.MouseMove += (_, e2) => {
            if (_draggingMeasureLine != ml || !_mlDraggingHandle || _mlDraggingHandle1) return;
            var pt = e2.GetPosition(_canvas);
            ml.EndPt = ml.IsHorizontal
                ? new Point(Math.Max(pt.X, ml.StartPt.X + 8), ml.EndPt.Y)
                : new Point(ml.EndPt.X, Math.Max(pt.Y, ml.StartPt.Y + 8));
            UpdateMeasureLineGeometry(ml);
            RepositionMeasureLineColorPicker(ml);
            e2.Handled = true;
        };
        handle2.MouseLeftButtonUp += (_, e2) => {
            if (_draggingMeasureLine != ml || !_mlDraggingHandle) return;
            CommitDragUndo();
            _draggingMeasureLine = null;
            _mlDraggingHandle = false;
            handle2.ReleaseMouseCapture();
            e2.Handled = true;
        };

        UpdateMeasureLineGeometry(ml);
        return ml;
    }

    private void UpdateMeasureLineGeometry(AnnotationMeasureLine ml) {
        const double aLen = 14.0, aHalf = 6.0, capHalf = 9.0, arrowGap = 1.0;

        var p1 = ml.StartPt;
        var p2 = ml.EndPt;
        var stroke = new SolidColorBrush(ml.LineColor);
        ml.MainLine.Stroke = stroke;
        ml.Head1.Fill = stroke;
        ml.Head2.Fill = stroke;
        ml.Cap1.Stroke = stroke;
        ml.Cap2.Stroke = stroke;

        double span = ml.IsHorizontal ? Math.Abs(p2.X - p1.X) : Math.Abs(p2.Y - p1.Y);
        double innerSpan = Math.Max(0, span - 2 * arrowGap);
        double arrowScale = (innerSpan < 2 * aLen) ? innerSpan / (2 * aLen) : 1.0;
        double scaledLen = aLen * arrowScale;
        double scaledHalf = aHalf * arrowScale;

        if (ml.IsHorizontal) {
            double tip1X = p1.X + arrowGap;
            double tip2X = p2.X - arrowGap;
            double lineInset = scaledLen / 3.0;
            ml.MainLine.X1 = tip1X + lineInset; ml.MainLine.Y1 = p1.Y;
            ml.MainLine.X2 = tip2X - lineInset; ml.MainLine.Y2 = p2.Y;
            ml.HitLine.X1 = p1.X; ml.HitLine.Y1 = p1.Y;
            ml.HitLine.X2 = p2.X; ml.HitLine.Y2 = p2.Y;
            SetArrowHeadPoints(ml.Head1,        tip1X,             p1.Y,             scaledLen, scaledHalf, goingLeft: true);
            SetArrowHeadPoints(ml.Head2,        tip2X,             p2.Y,             scaledLen, scaledHalf, goingLeft: false);
            ml.Cap1.X1 = p1.X; ml.Cap1.Y1 = p1.Y - capHalf; ml.Cap1.X2 = p1.X; ml.Cap1.Y2 = p1.Y + capHalf;
            ml.Cap2.X1 = p2.X; ml.Cap2.Y1 = p2.Y - capHalf; ml.Cap2.X2 = p2.X; ml.Cap2.Y2 = p2.Y + capHalf;
        }
        else {
            double tip1Y = p1.Y + arrowGap;
            double tip2Y = p2.Y - arrowGap;
            double lineInset = scaledLen / 3.0;
            ml.MainLine.X1 = p1.X; ml.MainLine.Y1 = tip1Y + lineInset;
            ml.MainLine.X2 = p2.X; ml.MainLine.Y2 = tip2Y - lineInset;
            ml.HitLine.X1 = p1.X; ml.HitLine.Y1 = p1.Y;
            ml.HitLine.X2 = p2.X; ml.HitLine.Y2 = p2.Y;
            SetArrowHeadPoints(ml.Head1,        p1.X,             tip1Y,             scaledLen, scaledHalf, goingLeft: true,  vertical: true);
            SetArrowHeadPoints(ml.Head2,        p2.X,             tip2Y,             scaledLen, scaledHalf, goingLeft: false, vertical: true);
            ml.Cap1.X1 = p1.X - capHalf; ml.Cap1.Y1 = p1.Y; ml.Cap1.X2 = p1.X + capHalf; ml.Cap1.Y2 = p1.Y;
            ml.Cap2.X1 = p2.X - capHalf; ml.Cap2.Y1 = p2.Y; ml.Cap2.X2 = p2.X + capHalf; ml.Cap2.Y2 = p2.Y;
        }

        // Label text
        double canvasDist = ml.IsHorizontal ? Math.Abs(p2.X - p1.X) : Math.Abs(p2.Y - p1.Y);
        double scaleX = _canvasScaleX > 0 ? _canvasScaleX : 1.0;
        double scaleY = _canvasScaleY > 0 ? _canvasScaleY : 1.0;
        double scale = ml.IsHorizontal ? scaleX : scaleY;
        int px = (int)Math.Round(canvasDist / scale);
        ml.LabelText.Text = $"{px} px";

        ml.LabelBadge.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        double bw = ml.LabelBadge.DesiredSize.Width;
        double bh = ml.LabelBadge.DesiredSize.Height;
        double midX = (p1.X + p2.X) / 2;
        double midY = (p1.Y + p2.Y) / 2;
        const double labelPerpOutside = 8.0;

        double bx, by;
        if (ml.IsHorizontal) {
            bool inside = span >= bw + 50.0;
            bx = midX - bw / 2;
            by = inside ? midY - bh / 2 : midY - labelPerpOutside - bh;
        }
        else {
            bool inside = span >= bh + 50.0;
            by = midY - bh / 2;
            bx = inside ? midX - bw / 2 : midX - labelPerpOutside - bw;
        }
        bx = Math.Max(0, Math.Min(_canvas.Width - bw, bx));
        by = Math.Max(0, Math.Min(_canvas.Height - bh, by));
        Canvas.SetLeft(ml.LabelBadge, bx);
        Canvas.SetTop(ml.LabelBadge, by);

        // Position endpoint handles (centred on each endpoint)
        Canvas.SetLeft(ml.Handle1, p1.X - 4);
        Canvas.SetTop(ml.Handle1,  p1.Y - 4);
        Canvas.SetLeft(ml.Handle2, p2.X - 4);
        Canvas.SetTop(ml.Handle2,  p2.Y - 4);
    }

    private static void SetArrowHeadPoints(Polygon poly, double x, double y, double aLen, double aHalf, bool goingLeft, bool vertical = false) {
        poly.Points.Clear();
        if (!vertical) {
            // Horizontal: tip at (x,y), base extends inward (+X for left-pointing, -X for right-pointing)
            double bx = goingLeft ? x + aLen : x - aLen;
            poly.Points.Add(new Point(x, y));
            poly.Points.Add(new Point(bx, y - aHalf));
            poly.Points.Add(new Point(bx, y + aHalf));
        }
        else {
            // Vertical: tip at (x,y), base extends inward (+Y for top-pointing, -Y for bottom-pointing)
            double by = goingLeft ? y + aLen : y - aLen;
            poly.Points.Add(new Point(x, y));
            poly.Points.Add(new Point(x - aHalf, by));
            poly.Points.Add(new Point(x + aHalf, by));
        }
    }

    private void RemoveMeasureLine(AnnotationMeasureLine ml) {
        if (!_suppressUndo) PushUndo();
        if (ml == _colorPickerMeasureLine) HideColorPicker();
        _canvas.Children.Remove(ml.MainLine);
        _canvas.Children.Remove(ml.Head1);
        _canvas.Children.Remove(ml.Head2);
        _canvas.Children.Remove(ml.Cap1);
        _canvas.Children.Remove(ml.Cap2);
        _canvas.Children.Remove(ml.LabelBadge);
        _canvas.Children.Remove(ml.HitLine);
        _canvas.Children.Remove(ml.Handle1);
        _canvas.Children.Remove(ml.Handle2);
        _measureLines.Remove(ml);
    }

    private void SelectMeasureLine(AnnotationMeasureLine? ml) {
        if (_selectedMeasureLine != null && _selectedMeasureLine != ml) {
            _selectedMeasureLine.Handle1.Visibility = Visibility.Hidden;
            _selectedMeasureLine.Handle2.Visibility = Visibility.Hidden;
        }
        _selectedMeasureLine = ml;
        if (ml != null) {
            if (_selectedArrow != null) SelectArrow(null);
            if (_selectedAnnotRect != null) SelectAnnotationRect(null);
            if (_selectedAnnotX != null) SelectAnnotationX(null);
            if (_selectedText != null) SelectText(null);
            _selectedCursor = false;
            ml.Handle1.Visibility = Visibility.Visible;
            ml.Handle2.Visibility = Visibility.Visible;
            ShowColorPickerForMeasureLine(ml);
        }
        else {
            HideColorPicker();
        }
    }

    private void ShowColorPickerForMeasureLine(AnnotationMeasureLine ml) {
        HideColorPicker();
        _colorPickerMeasureLine = ml;

        _colorPickerPanel = MakeColorPickerGrid(GetAnnotationPalette(), ml.LineColor, picked => {
            ml.LineColor = picked;
            _defaultMeasureLineColor = picked;
            UpdateMeasureLineGeometry(ml);
            ShowColorPickerForMeasureLine(ml);
        });
        Panel.SetZIndex(_colorPickerPanel, 300);
        _canvas.Children.Add(_colorPickerPanel);

        double midX = (ml.StartPt.X + ml.EndPt.X) / 2;
        double midY = (ml.StartPt.Y + ml.EndPt.Y) / 2;
        _colorPickerPanel.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        double pw = _colorPickerPanel.DesiredSize.Width;
        double ph = _colorPickerPanel.DesiredSize.Height;
        double cx, cy;
        if (ml.IsHorizontal) {
            // Horizontal line: place picker above the midpoint (original behaviour).
            cx = Math.Max(0, Math.Min(_canvas.Width  - pw, midX - pw / 2));
            cy = Math.Max(0, Math.Min(_canvas.Height - ph, midY - ph - 20));
        } else {
            // Vertical line: place picker to the left or right, whichever has more space.
            double spaceRight = _canvas.Width - midX;
            double spaceLeft  = midX;
            cx = spaceRight >= spaceLeft
                ? Math.Min(_canvas.Width  - pw, midX + 20)
                : Math.Max(0, midX - pw - 20);
            cy = Math.Max(0, Math.Min(_canvas.Height - ph, midY - ph / 2));
        }
        Canvas.SetLeft(_colorPickerPanel, cx);
        Canvas.SetTop(_colorPickerPanel, cy);
    }

    /// <summary>Moves the color picker panel to track the current midpoint of <paramref name="ml"/> without rebuilding it.</summary>
    private void RepositionMeasureLineColorPicker(AnnotationMeasureLine ml) {
        if (_colorPickerMeasureLine != ml || _colorPickerPanel == null) return;
        double midX = (ml.StartPt.X + ml.EndPt.X) / 2;
        double midY = (ml.StartPt.Y + ml.EndPt.Y) / 2;
        double pw = _colorPickerPanel.DesiredSize.Width;
        double ph = _colorPickerPanel.DesiredSize.Height;
        double cx, cy;
        if (ml.IsHorizontal) {
            cx = Math.Max(0, Math.Min(_canvas.Width  - pw, midX - pw / 2));
            cy = Math.Max(0, Math.Min(_canvas.Height - ph, midY - ph - 20));
        } else {
            double spaceRight = _canvas.Width - midX;
            double spaceLeft  = midX;
            cx = spaceRight >= spaceLeft
                ? Math.Min(_canvas.Width  - pw, midX + 20)
                : Math.Max(0, midX - pw - 20);
            cy = Math.Max(0, Math.Min(_canvas.Height - ph, midY - ph / 2));
        }
        Canvas.SetLeft(_colorPickerPanel, cx);
        Canvas.SetTop(_colorPickerPanel, cy);
    }
    /// <summary>Snaps <paramref name="head"/> to the closest horizontal or vertical line through <paramref name="tail"/>.</summary>
    private static Point SnapArrowToAxis(Point tail, Point head)
    {
        var dx = Math.Abs(head.X - tail.X);
        var dy = Math.Abs(head.Y - tail.Y);
        return dx >= dy
            ? new Point(head.X, tail.Y)   // horizontal
            : new Point(tail.X, head.Y);  // vertical
    }

    /// <summary>
    /// When dragging with Shift held, constrains the drag to either horizontal or vertical alignment
    /// based on which direction the user has moved more from the initial click point.
    /// This provides intuitive dynamic axis detection that updates on each mouse move.
    /// </summary>
    private static Point ConstrainPositionToAxis(Point initialClickPoint, Point originalObjectPos, Point currentPos)
    {
        // Calculate distances from initial click point to current mouse position
        var horizontalDistance = Math.Abs(currentPos.X - initialClickPoint.X);
        var verticalDistance = Math.Abs(currentPos.Y - initialClickPoint.Y);
        
        // Determine constraint axis based on which direction user moved more
        if (horizontalDistance > verticalDistance)
            // User moved more horizontally: constrain to horizontal alignment (lock Y to original position)
            return new Point(currentPos.X, originalObjectPos.Y);
        else
            // User moved more vertically (or equal): constrain to vertical alignment (lock X to original position)
            return new Point(originalObjectPos.X, currentPos.Y);
    }

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

        double tailLen  = dist;

        // ux = sin(rad), uy = -cos(rad) => rad = atan2(ux, -uy)
        double angleDeg = Math.Atan2(ux, -uy) * 180.0 / Math.PI;

        // Pivot = arrowhead tip. ArrowLength = 0 so arrowhead is exactly at the pivot;
        // tail end = center + ux * tailLen = headPt + ux * dist = tailPt.
        double centerX = headPt.X;
        double centerY = headPt.Y;
        var targetBounds = new Rect(centerX - 1, centerY - 1, 2, 2);

        var savedAngle    = _defaultArrowAngleDeg;
        var savedTailLen  = _defaultTailLength;
        var savedArrowLen = _defaultArrowLength;
        _defaultArrowAngleDeg = angleDeg;
        _defaultTailLength    = tailLen;
        _defaultArrowLength   = 0;   // arrowhead tip at pivot — no offset
        var arrow = CreateArrow(targetBounds);

        // Remember this drag's angle and tail length for subsequent click-without-drag placement.
        _lastDragArrowAngleDeg = angleDeg;
        _lastDragArrowTailLength = tailLen;

        _defaultArrowAngleDeg = savedAngle;
        _defaultTailLength    = savedTailLen;
        _defaultArrowLength   = savedArrowLen;

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
            Visibility = Visibility.Collapsed
        };
        var tailHandle = new Ellipse {
            Width = 8,
            Height = 8,
            Fill = Brushes.White,
            Stroke = Brushes.Black,
            StrokeThickness = 1,
            Cursor = AnnotationCursors.RotateEndpoint,
            Visibility = Visibility.Collapsed
        };

        _canvas.Children.Add(line);
        _canvas.Children.Add(head);
        _canvas.Children.Add(tailHandle);
        _canvas.Children.Add(tipHandle);
        Panel.SetZIndex(tipHandle, 10);
        Panel.SetZIndex(tailHandle, 10);
        Panel.SetZIndex(line, 5);
        Panel.SetZIndex(head, 5);
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
            arrow.ArrowLength = Math.Max(1.0, Math.Min(dist, ComputeMaxArrowheadOffset(pivot, newAngle)));
            double maxFromTip = ComputeMaxArrowheadOffset(pivot, newAngle) - arrow.ArrowLength;
            arrow.TailLength = arrow.UserTailLength > 0
                ? Math.Max(1.0, Math.Min(arrow.UserTailLength, maxFromTip))
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
            const double MinTail = 1.0;
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
            SquadDashTrace.Write("ImageEditor", $"AttachBodyDrag.MouseLeftButtonDown: shape={shape.GetType().Name} ctrl={(Keyboard.Modifiers & ModifierKeys.Control) != 0} _draggingArrow={_draggingArrow != null}");
            if (_draggingArrow != null) {
                SquadDashTrace.Write("ImageEditor", $"AttachBodyDrag: early return — _draggingArrow already set");
                return;
            }
            if ((Keyboard.Modifiers & ModifierKeys.Control) != 0) {
                // Ctrl+drag: duplicate the arrow; original stays, clone is dragged.
                PushUndo();
                _suppressUndo = true;
                var clone = CreateArrow(arrow.TargetElementBounds);
                _suppressUndo = false;
                SquadDashTrace.Write("ImageEditor", $"AttachBodyDrag.Ctrl: clone created, clone.TargetCenterOnCanvas=({clone.TargetCenterOnCanvas.X:F1},{clone.TargetCenterOnCanvas.Y:F1}) original.TargetCenterOnCanvas=({arrow.TargetCenterOnCanvas.X:F1},{arrow.TargetCenterOnCanvas.Y:F1}) original.OffsetX={arrow.OffsetX:F1}");
                clone.TargetCenterOnCanvas = arrow.TargetCenterOnCanvas;
                clone.ArrowheadAngleDeg = arrow.ArrowheadAngleDeg;
                clone.ArrowLength = arrow.ArrowLength;
                clone.TailLength = arrow.TailLength;
                clone.UserTailLength = arrow.UserTailLength;
                clone.ArrowColor = arrow.ArrowColor;
                clone.OffsetX = arrow.OffsetX;
                clone.OffsetY = arrow.OffsetY;
                UpdateArrowGeometry(clone);
                HideColorPicker();
                _draggingArrow = clone;
                _bodyDragging = true;
                _bodyDragStartMouse = e.GetPosition(_canvas);
                _bodyDragStartOffsetX = clone.OffsetX;
                _bodyDragStartOffsetY = clone.OffsetY;
                _preDragSnapshot = null;
                _arrowCloneDragOriginalCenter = new Point(
                    clone.TargetCenterOnCanvas.X + clone.OffsetX,
                    clone.TargetCenterOnCanvas.Y + clone.OffsetY);
                SquadDashTrace.Write("ImageEditor", $"AttachBodyDrag.Ctrl: about to CaptureMouse on clone.HitLine, current Mouse.Captured={(Mouse.Captured?.GetType().Name ?? "null")}");
                bool captured = clone.HitLine.CaptureMouse();
                SquadDashTrace.Write("ImageEditor", $"AttachBodyDrag.Ctrl: CaptureMouse returned {captured}, Mouse.Captured={(Mouse.Captured?.GetType().Name ?? "null")}");
                if (!captured) {
                    // CaptureMouse failed — mouse events won't reach the clone, so _draggingArrow
                    // would be stuck non-null and block all future Ctrl+drag attempts. Roll back.
                    SquadDashTrace.Write("ImageEditor", $"AttachBodyDrag.Ctrl: CaptureMouse failed — rolling back clone");
                    RemoveArrow(clone);
                    _draggingArrow = null;
                    _bodyDragging = false;
                    return;
                }
                e.Handled = true;
                return;
            }
            SelectArrow(arrow);
            HideColorPicker();
            _preDragSnapshot = CaptureSnapshot();
            _suppressUndo = true;
            var arrowStandby = CreateArrow(arrow.TargetElementBounds);
            _suppressUndo = false;
            arrowStandby.TargetCenterOnCanvas = arrow.TargetCenterOnCanvas;
            arrowStandby.ArrowheadAngleDeg = arrow.ArrowheadAngleDeg;
            arrowStandby.ArrowLength = arrow.ArrowLength;
            arrowStandby.TailLength = arrow.TailLength;
            arrowStandby.UserTailLength = arrow.UserTailLength;
            arrowStandby.ArrowColor = arrow.ArrowColor;
            arrowStandby.OffsetX = arrow.OffsetX;
            arrowStandby.OffsetY = arrow.OffsetY;
            UpdateArrowGeometry(arrowStandby);
            _arrowMoveStandbyClone = arrowStandby;
            SetAnnotationVisible(_arrowMoveStandbyClone, (Keyboard.Modifiers & ModifierKeys.Control) != 0);
            // CreateArrow called SelectArrow(standby), hiding arrow's handles. Restore them.
            arrowStandby.TipHandle.Visibility = Visibility.Collapsed;
            arrowStandby.TailHandle.Visibility = Visibility.Collapsed;
            arrow.TipHandle.Visibility = Visibility.Visible;
            arrow.TailHandle.Visibility = Visibility.Visible;
            _selectedArrow = arrow;
            HideColorPicker();
            _draggingArrow = arrow;
            _bodyDragging = true;
            _bodyDragStartMouse = e.GetPosition(_canvas);
            _bodyDragStartOffsetX = arrow.OffsetX;
            _bodyDragStartOffsetY = arrow.OffsetY;
            _arrowCloneDragOriginalCenter = new Point(
                arrow.TargetCenterOnCanvas.X + arrow.OffsetX,
                arrow.TargetCenterOnCanvas.Y + arrow.OffsetY);
            shape.CaptureMouse();
            e.Handled = true;
        };
        shape.MouseMove += (_, e) => {
            SquadDashTrace.Write("ImageEditor", $"AttachBodyDrag.MouseMove: _draggingArrow==arrow={_draggingArrow == arrow} _bodyDragging={_bodyDragging}");
            if (_draggingArrow != arrow || !_bodyDragging) return;
            var pt = e.GetPosition(_canvas);
            
            var mouseDX = pt.X - _bodyDragStartMouse.X;
            var mouseDY = pt.Y - _bodyDragStartMouse.Y;
            if ((Keyboard.Modifiers & ModifierKeys.Shift) != 0) {
                if (Math.Abs(mouseDX) >= Math.Abs(mouseDY)) mouseDY = 0;
                else mouseDX = 0;
            }
            arrow.OffsetX = _bodyDragStartOffsetX + mouseDX;
            arrow.OffsetY = _bodyDragStartOffsetY + mouseDY;
            
            UpdateArrowGeometry(arrow);
            if (_arrowMoveStandbyClone != null)
                SetAnnotationVisible(_arrowMoveStandbyClone, (Keyboard.Modifiers & ModifierKeys.Control) != 0);
            ShowCrosshair(
                arrow.TargetCenterOnCanvas.X + arrow.OffsetX,
                arrow.TargetCenterOnCanvas.Y + arrow.OffsetY);
            e.Handled = true;
        };
        shape.MouseLeftButtonUp += (_, e) => {
            SquadDashTrace.Write("ImageEditor", $"AttachBodyDrag.MouseLeftButtonUp: _draggingArrow==arrow={_draggingArrow == arrow} _bodyDragging={_bodyDragging}");
            if (_draggingArrow != arrow || !_bodyDragging) return;
            HideCrosshair();
            if (_arrowMoveStandbyClone != null) {
                if ((Keyboard.Modifiers & ModifierKeys.Control) != 0)
                    SetAnnotationVisible(_arrowMoveStandbyClone, true);
                else {
                    _suppressUndo = true;
                    RemoveArrow(_arrowMoveStandbyClone);
                    _suppressUndo = false;
                }
                _arrowMoveStandbyClone = null;
            }
            _canvas.Cursor = Cursors.Arrow;
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

        // Scale arrowhead proportionally to total arrow length (tail → arrowhead tip).
        // Standard arrowhead is 16px deep, 12px wide. If it exceeds 1/3 of arrow length, scale down.
        const double StandardHeadLen = 16.0;
        const double StandardHeadHalf = 6.0;
        
        double fullArrowLength = arrow.ArrowLength + arrow.TailLength;
        double maxArrowheadDepth = fullArrowLength * 0.45;
        
        double HeadLen = StandardHeadLen;
        if (HeadLen > maxArrowheadDepth)
        {
            HeadLen = maxArrowheadDepth;
        }
        
        double HeadHalf = StandardHeadHalf * HeadLen / StandardHeadLen;
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
            _selectedArrow.TipHandle.Visibility = Visibility.Collapsed;
            _selectedArrow.TailHandle.Visibility = Visibility.Collapsed;
        }
        _selectedArrow = arrow;
        if (arrow != null) {
            if (_selectedText != null) SelectText(null);
            if (_selectedAnnotRect != null) SelectAnnotationRect(null);
            if (_selectedMeasureLine != null) {
                _selectedMeasureLine.Handle1.Visibility = Visibility.Hidden;
                _selectedMeasureLine.Handle2.Visibility = Visibility.Hidden;
                _selectedMeasureLine = null;
            }
            if (_selectedAnnotX != null) SelectAnnotationX(null);
            _selectedCursor = false;
            arrow.TipHandle.Visibility = Visibility.Visible;
            arrow.TailHandle.Visibility = Visibility.Visible;
            ShowColorPicker(arrow);
        }
        else {
            HideColorPicker();
        }
    }

    // ── Color picker ──────────────────────────────────────────────────────────

    private static Color[] GetAnnotationPalette() => new[]
    {
        // Row 0 — Light
        Color.FromRgb(255, 160, 160), Color.FromRgb(255, 210, 150), Color.FromRgb(255, 245, 150),
        Color.FromRgb(150, 230, 150), Color.FromRgb(160, 210, 255), Color.FromRgb(210, 160, 255),
        Color.FromRgb(255, 255, 255), Color.FromRgb( 97,  97,  97),
        // Row 1 — Base
        Color.FromRgb(220,  50,  50), Color.FromRgb(255, 140,   0), Color.FromRgb(240, 210,  40),
        Color.FromRgb( 50, 185,  50), Color.FromRgb( 50, 130, 230), Color.FromRgb(160,  50, 220),
        Color.FromRgb(209, 209, 209), Color.FromRgb( 51,  51,  51),
        // Row 2 — Dark
        Color.FromRgb(140,  20,  20), Color.FromRgb(180,  80,   0), Color.FromRgb(160, 130,   0),
        Color.FromRgb( 20, 110,  20), Color.FromRgb( 20,  70, 160), Color.FromRgb( 90,  20, 140),
        Color.FromRgb(157, 157, 157), Color.FromRgb(  0,   0,   0),
    };

    private void ShowColorPicker(AnnotationArrow arrow) {
        HideColorPicker();
        _colorPickerArrow = arrow;

        _colorPickerPanel = MakeColorPickerGrid(GetAnnotationPalette(), arrow.ArrowColor, picked => {
            arrow.ArrowColor = picked;
            _defaultArrowColor = picked;
            SaveArrowDefaults();
            UpdateArrowGeometry(arrow);
            ShowColorPicker(arrow);
        });
        Panel.SetZIndex(_colorPickerPanel, 300);
        _canvas.Children.Add(_colorPickerPanel);

        double tipX  = Canvas.GetLeft(arrow.TipHandle)  + 4;
        double tipY  = Canvas.GetTop(arrow.TipHandle)   + 4;
        double tailX = Canvas.GetLeft(arrow.TailHandle) + 4;
        double tailY = Canvas.GetTop(arrow.TailHandle)  + 4;
        double midX  = (tipX + tailX) / 2;
        double arrowMinY = Math.Min(tipY, tailY);
        double arrowMaxY = Math.Max(tipY, tailY);
        _colorPickerPanel.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        double pw = _colorPickerPanel.DesiredSize.Width;
        double ph = _colorPickerPanel.DesiredSize.Height;
        // Prefer above the arrow's topmost point; fall back to below if too close to canvas top.
        double cy = arrowMinY - ph - 8;
        if (cy < 0)
            cy = Math.Min(_canvas.Height - ph, arrowMaxY + 8);
        Canvas.SetLeft(_colorPickerPanel, Math.Max(0, Math.Min(_canvas.Width - pw, midX - pw / 2)));
        Canvas.SetTop(_colorPickerPanel, Math.Max(0, cy));
    }

    private static string ColorName(Color c) => c switch {
        { R: 255, G: 0, B: 0 } => "Red", { R: 0, G: 200, B: 0 } or { R: 0, G: 255, B: 0 } => "Green",
        { R: 0, G: 0, B: 255 } => "Blue",
        { R: 255, G: 255, B: 0 } => "Yellow", { R: 255, G: 165, B: 0 } or { R: 255, G: 120, B: 20 } => "Orange",
        { R: 255, G: 255, B: 255 } => "White",
        { R: 0, G: 0, B: 0 } => "Black",
        // Annotation palette — Row 0 (Light)
        { R: 255, G: 160, B: 160 } => "Light Red",
        { R: 255, G: 210, B: 150 } => "Light Orange",
        { R: 255, G: 245, B: 150 } => "Light Yellow",
        { R: 150, G: 230, B: 150 } => "Light Green",
        { R: 160, G: 210, B: 255 } => "Light Blue",
        { R: 210, G: 160, B: 255 } => "Light Purple",
        { R: 204, G: 204, B: 204 } => "Light Gray",
        // Row 1 (Base)
        { R: 220, G:  50, B:  50 } => "Red",
        { R: 255, G: 140, B:   0 } => "Orange",
        { R: 240, G: 210, B:  40 } => "Yellow",
        { R:  50, G: 185, B:  50 } => "Green",
        { R:  50, G: 130, B: 230 } => "Blue",
        { R: 160, G:  50, B: 220 } => "Purple",
        { R: 209, G: 209, B: 209 } => "Light Gray",
        { R:  97, G:  97, B:  97 } => "Dark Gray",
        // Row 2 (Dark)
        { R: 140, G:  20, B:  20 } => "Dark Red",
        { R: 180, G:  80, B:   0 } => "Dark Orange",
        { R: 160, G: 130, B:   0 } => "Dark Yellow",
        { R:  20, G: 110, B:  20 } => "Dark Green",
        { R:  20, G:  70, B: 160 } => "Dark Blue",
        { R:  90, G:  20, B: 140 } => "Dark Purple",
        { R: 157, G: 157, B: 157 } => "Med Gray",
        { R:  51, G:  51, B:  51 } => "Dark Gray",
        _ => $"#{c.R:X2}{c.G:X2}{c.B:X2}"
    };

    private static FrameworkElement MakeColorPickerGrid(Color[] palette, Color selectedColor, Action<Color> onPick) {
        var uniformGrid = new UniformGrid {
            Rows = 3,
            Columns = 8,
            Background = new SolidColorBrush(Color.FromRgb(0xBB, 0xBB, 0xBB)),
        };
        foreach (var color in palette) {
            var c = color;
            bool isSelected = c.R == selectedColor.R && c.G == selectedColor.G && c.B == selectedColor.B;
            FrameworkElement cell;
            if (isSelected) {
                var cellGrid = new Grid {
                    Width = 18, Height = 18,
                    Margin = new Thickness(0, 0, 1, 1),
                    Cursor = Cursors.Hand,
                    ToolTip = ColorName(c),
                };
                cellGrid.Children.Add(new Rectangle { Fill = new SolidColorBrush(c) });
                cellGrid.Children.Add(new Rectangle { Stroke = Brushes.White, StrokeThickness = 2, Fill = Brushes.Transparent });
                cellGrid.MouseLeftButtonDown += (_, e) => { onPick(c); e.Handled = true; };
                cell = cellGrid;
            } else {
                var rect = new Rectangle {
                    Width = 18, Height = 18,
                    Fill = new SolidColorBrush(c),
                    Margin = new Thickness(0, 0, 1, 1),
                    Cursor = Cursors.Hand,
                    ToolTip = ColorName(c),
                };
                rect.MouseLeftButtonDown += (_, e) => { onPick(c); e.Handled = true; };
                cell = rect;
            }
            uniformGrid.Children.Add(cell);
        }
        return new Border {
            Background = new SolidColorBrush(Color.FromRgb(0xBB, 0xBB, 0xBB)),
            Padding = new Thickness(1),
            CornerRadius = new CornerRadius(2),
            Child = uniformGrid,
        };
    }

    private void HideColorPicker(){
        if (_colorPickerPanel != null) {
            _canvas.Children.Remove(_colorPickerPanel);
            _colorPickerPanel = null;
        }
        _colorPickerArrow = null;
        _colorPickerRect = null;
        _colorPickerText = null;
        _colorPickerMeasureLine = null;
        _colorPickerX = null;
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
        _canvas.Children.Remove(arrow.Line);
        _canvas.Children.Remove(arrow.Head);
        _canvas.Children.Remove(arrow.TipHandle);
        _canvas.Children.Remove(arrow.TailHandle);
        _canvas.Children.Remove(arrow.HitLine);
        _arrows.Remove(arrow);
    }

    // ── Standby-clone visibility helpers ─────────────────────────────────────
    // Used by move-drag to show/hide the hidden original during late-Ctrl copy.
    private static void SetAnnotationVisible(AnnotationArrow a, bool visible) {
        var v = visible ? Visibility.Visible : Visibility.Collapsed;
        a.Line.Visibility = v; a.Head.Visibility = v; a.HitLine.Visibility = v;
    }
    private static void SetAnnotationVisible(AnnotationMeasureLine ml, bool visible) {
        var v = visible ? Visibility.Visible : Visibility.Collapsed;
        ml.MainLine.Visibility = v; ml.Head1.Visibility = v; ml.Head2.Visibility = v;
        ml.Cap1.Visibility = v; ml.Cap2.Visibility = v;
        ml.LabelBadge.Visibility = v; ml.HitLine.Visibility = v;
    }
    private static void SetAnnotationVisible(AnnotationX x, bool visible) {
        var v = visible ? Visibility.Visible : Visibility.Collapsed;
        x.Line1.Visibility = v; x.Line2.Visibility = v; x.HitZoneRect.Visibility = v;
    }
    private static void SetAnnotationVisible(AnnotationRect r, bool visible) {
        var v = visible ? Visibility.Visible : Visibility.Collapsed;
        r.Border.Visibility = v; r.HitZoneRect.Visibility = v;
    }
    private static void SetAnnotationVisible(AnnotationText t, bool visible) {
        if (t.Display != null) t.Display.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
    }

    /// <summary>
    /// Called whenever Ctrl key state may have changed during an active body drag.
    /// Updates standby-clone visibility and cursor for all annotation types.
    /// </summary>
    private void RefreshDragCtrlState() {
        bool ctrl = (Keyboard.Modifiers & ModifierKeys.Control) != 0;
        bool anyDrag = false;
        if (_arrowMoveStandbyClone != null)    { SetAnnotationVisible(_arrowMoveStandbyClone, ctrl);  anyDrag = true; }
        if (_mlMoveStandbyClone != null)       { SetAnnotationVisible(_mlMoveStandbyClone, ctrl);     anyDrag = true; }
        if (_xMoveStandbyClone != null)        { SetAnnotationVisible(_xMoveStandbyClone, ctrl);      anyDrag = true; }
        if (_rectMoveStandbyClone != null)     { SetAnnotationVisible(_rectMoveStandbyClone, ctrl);   anyDrag = true; }
        if (_textMoveStandbyClone != null)     { SetAnnotationVisible(_textMoveStandbyClone, ctrl);   anyDrag = true; }
        if (anyDrag)
            _canvas.Cursor = ctrl ? Cursors.Cross : Cursors.SizeAll;
    }



    private void EnterXMode() {
        SelectArrow(null);
        if (_selectedText != null) SelectText(null);
        _inMoveMode = false;
        _inCropMode = false;
        _inXMode = true;
        _canvas.Cursor = AnnotationCursors.XTool;
        ShowModeHint("Drag to draw an X");
    }

    private void ExitXMode(bool returnToMove = false) {
        _inXMode = false;
        _inXMultiDropMode = false;
        Cursor = Cursors.Arrow;
        _canvas.Cursor = Cursors.Arrow;
        HideModeHint();
        RemoveAnnotXPreview();
        if (_addXBtn != null) _addXBtn.Content = MakeToolIcon("ImageEditorXIcon");
        if (returnToMove) EnterMoveMode();
    }

    private void RemoveAnnotXPreview() {
        if (_annotXPreviewLine1 != null) {
            _canvas.Children.Remove(_annotXPreviewLine1);
            _annotXPreviewLine1 = null;
        }
        if (_annotXPreviewLine2 != null) {
            _canvas.Children.Remove(_annotXPreviewLine2);
            _annotXPreviewLine2 = null;
        }
    }

    private (Line l1, Line l2) EnsureAnnotXPreview() {
        if (_annotXPreviewLine1 == null) {
            _annotXPreviewLine1 = new Line {
                Stroke = new SolidColorBrush(_defaultXColor),
                StrokeThickness = 4,
                StrokeDashArray = new DoubleCollection { 4, 2 },
                IsHitTestVisible = false,
                Opacity = 0.7,
                Visibility = Visibility.Hidden
            };
            Panel.SetZIndex(_annotXPreviewLine1, 50);
            _canvas.Children.Add(_annotXPreviewLine1);
        }
        if (_annotXPreviewLine2 == null) {
            _annotXPreviewLine2 = new Line {
                Stroke = new SolidColorBrush(_defaultXColor),
                StrokeThickness = 4,
                StrokeDashArray = new DoubleCollection { 4, 2 },
                IsHitTestVisible = false,
                Opacity = 0.7,
                Visibility = Visibility.Hidden
            };
            Panel.SetZIndex(_annotXPreviewLine2, 50);
            _canvas.Children.Add(_annotXPreviewLine2);
        }
        return (_annotXPreviewLine1, _annotXPreviewLine2);
    }

    private AnnotationX CreateAnnotationX(Rect bounds, Color? color = null) {
        if (!_suppressUndo) PushUndo();

        var xColor = color ?? _defaultXColor;
        var brush = new SolidColorBrush(xColor) { Opacity = 0.8 };

        var line1 = new Line { Stroke = brush, StrokeThickness = 6.0, StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round, IsHitTestVisible = false };
        var line2 = new Line { Stroke = brush, StrokeThickness = 6.0, StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round, IsHitTestVisible = false };

        var hitZone = new Rectangle {
            Fill = Brushes.Transparent,
            StrokeThickness = 0,
            IsHitTestVisible = true
        };

        var handles = new Rectangle[8];
        for (int i = 0; i < 8; i++) {
            var cursor = i switch {
                0 or 3 => Cursors.SizeNWSE,
                1 or 2 => Cursors.SizeNESW,
                4 or 5 => Cursors.SizeNS,
                6 or 7 => Cursors.SizeWE,
                _ => Cursors.SizeAll
            };
            handles[i] = new Rectangle {
                Width = 8, Height = 8,
                Fill = Brushes.White, Stroke = Brushes.Black, StrokeThickness = 1,
                Cursor = cursor,
                Visibility = Visibility.Hidden
            };
            _canvas.Children.Add(handles[i]);
            Panel.SetZIndex(handles[i], 10);
        }

        _canvas.Children.Add(line1);
        _canvas.Children.Add(line2);
        Panel.SetZIndex(line1, 5);
        Panel.SetZIndex(line2, 5);
        _canvas.Children.Add(hitZone);
        Panel.SetZIndex(hitZone, 4);

        var annotX = new AnnotationX {
            Bounds = bounds, XColor = xColor,
            Line1 = line1, Line2 = line2,
            Handles = handles, HitZoneRect = hitZone
        };

        hitZone.MouseLeftButtonDown += (_, e) => {
            if (_draggingAnnotX != null || _inArrowMode || _inXMode) return;
            if ((Keyboard.Modifiers & ModifierKeys.Control) != 0) {
                // Ctrl+drag: duplicate the X shape; original stays, clone is dragged.
                PushUndo();
                _suppressUndo = true;
                var clone = CreateAnnotationX(annotX.Bounds, annotX.XColor);
                _suppressUndo = false;
                _draggingAnnotX = clone;
                _annotXBodyDragging = true;
                _draggingAnnotXHandleIdx = -1;
                _annotXDragStart = e.GetPosition(_canvas);
                _annotXDragOriginal = clone.Bounds;
                _preDragSnapshot = null;
                // Track clone drag for Shift-constraint
                _xCloneDragOriginalCenter = new Point(
                    clone.Bounds.X + clone.Bounds.Width / 2,
                    clone.Bounds.Y + clone.Bounds.Height / 2);
                clone.HitZoneRect.CaptureMouse();
                e.Handled = true;
                return;
            }
            SelectAnnotationX(annotX);
            _preDragSnapshot = CaptureSnapshot();
            _suppressUndo = true;
            var xStandby = CreateAnnotationX(annotX.Bounds, annotX.XColor);
            _suppressUndo = false;
            SetAnnotationVisible(xStandby, (Keyboard.Modifiers & ModifierKeys.Control) != 0);
            _xMoveStandbyClone = xStandby;
            // CreateAnnotationX called SelectAnnotationX(standby), hiding annotX's handles. Restore them.
            foreach (var h in xStandby.Handles) h.Visibility = Visibility.Collapsed;
            foreach (var h in annotX.Handles) h.Visibility = Visibility.Visible;
            _selectedAnnotX = annotX;
            HideColorPicker();
            _draggingAnnotX = annotX;
            _annotXBodyDragging = true;
            _draggingAnnotXHandleIdx = -1;
            _annotXDragStart = e.GetPosition(_canvas);
            _annotXDragOriginal = annotX.Bounds;
            _xCloneDragOriginalCenter = new Point(
                annotX.Bounds.X + annotX.Bounds.Width / 2,
                annotX.Bounds.Y + annotX.Bounds.Height / 2);
            hitZone.CaptureMouse();
            e.Handled = true;
        };
        hitZone.MouseMove += (_, e) => {
            if (_draggingAnnotX != annotX || !_annotXBodyDragging) return;
            var pt = e.GetPosition(_canvas);
            var dx = pt.X - _annotXDragStart.X;
            var dy = pt.Y - _annotXDragStart.Y;
            var cw = _canvas.Width; var ch = _canvas.Height;
            
            // Apply Shift-key alignment constraint (move or clone drag)
            if ((Keyboard.Modifiers & ModifierKeys.Shift) != 0) {
                if (Math.Abs(dx) >= Math.Abs(dy)) dy = 0;
                else dx = 0;
            }
            
            var nb = new Rect(
                Math.Max(0, Math.Min(_annotXDragOriginal.X + dx, cw - _annotXDragOriginal.Width)),
                Math.Max(0, Math.Min(_annotXDragOriginal.Y + dy, ch - _annotXDragOriginal.Height)),
                _annotXDragOriginal.Width, _annotXDragOriginal.Height);
            annotX.Bounds = nb;
            UpdateXGeometry(annotX);
            if (_xMoveStandbyClone != null)
                SetAnnotationVisible(_xMoveStandbyClone, (Keyboard.Modifiers & ModifierKeys.Control) != 0);
            e.Handled = true;
        };
        hitZone.MouseLeftButtonUp += (_, e) => {
            if (_draggingAnnotX != annotX || !_annotXBodyDragging) return;
            _annotXBodyDragging = false;
            _draggingAnnotX = null;
            if (_xMoveStandbyClone != null) {
                if ((Keyboard.Modifiers & ModifierKeys.Control) != 0)
                    SetAnnotationVisible(_xMoveStandbyClone, true);
                else {
                    _suppressUndo = true;
                    RemoveAnnotationX(_xMoveStandbyClone);
                    _suppressUndo = false;
                }
                _xMoveStandbyClone = null;
            }
            hitZone.ReleaseMouseCapture();
            CommitDragUndo();
            _canvas.Cursor = Cursors.Arrow;
            ShowColorPickerForX(annotX);
            e.Handled = true;
        };
        hitZone.MouseRightButtonDown += (_, e) => {
            PushUndo();
            RemoveAnnotationX(annotX);
            if (_selectedAnnotX == annotX) { _selectedAnnotX = null; HideColorPicker(); }
            e.Handled = true;
        };

        for (int i = 0; i < 8; i++) {
            int idx = i;
            handles[i].MouseLeftButtonDown += (_, e) => {
                if (_draggingAnnotX != null) return;
                SelectAnnotationX(annotX);
                _preDragSnapshot = CaptureSnapshot();
                _draggingAnnotX = annotX;
                _annotXBodyDragging = false;
                _draggingAnnotXHandleIdx = idx;
                _annotXDragStart = e.GetPosition(_canvas);
                _annotXDragOriginal = annotX.Bounds;
                handles[idx].CaptureMouse();
                e.Handled = true;
            };
            handles[i].MouseMove += (_, e) => {
                if (_draggingAnnotX != annotX || _draggingAnnotXHandleIdx != idx) return;
                var pt = e.GetPosition(_canvas);
                var dx = pt.X - _annotXDragStart.X;
                var dy = pt.Y - _annotXDragStart.Y;
                var ob = _annotXDragOriginal;
                double nx = ob.X, ny = ob.Y, nr = ob.Right, nb2 = ob.Bottom;
                if (idx == 0 || idx == 2 || idx == 6) nx = Math.Min(ob.X + dx, ob.Right - MinSize);
                if (idx == 1 || idx == 3 || idx == 7) nr = Math.Max(ob.Right + dx, ob.X + MinSize);
                if (idx == 0 || idx == 1 || idx == 4) ny = Math.Min(ob.Y + dy, ob.Bottom - MinSize);
                if (idx == 2 || idx == 3 || idx == 5) nb2 = Math.Max(ob.Bottom + dy, ob.Y + MinSize);
                annotX.Bounds = new Rect(nx, ny, nr - nx, nb2 - ny);
                UpdateXGeometry(annotX);
                e.Handled = true;
            };
            handles[i].MouseLeftButtonUp += (_, e) => {
                if (_draggingAnnotX != annotX || _draggingAnnotXHandleIdx != idx) return;
                _draggingAnnotX = null;
                _draggingAnnotXHandleIdx = -1;
                handles[idx].ReleaseMouseCapture();
                CommitDragUndo();
                e.Handled = true;
            };
        }

        _annotXShapes.Add(annotX);
        UpdateXGeometry(annotX);
        SelectAnnotationX(annotX);
        return annotX;
    }

    private void UpdateXGeometry(AnnotationX x) {
        var b = x.Bounds;
        var brush = new SolidColorBrush(x.XColor) { Opacity = 0.8 };

        x.Line1.X1   = b.Left;       x.Line1.Y1   = b.Top;
        x.Line1.X2   = b.Right;      x.Line1.Y2   = b.Bottom;

        x.Line2.X1   = b.Right;      x.Line2.Y1   = b.Top;
        x.Line2.X2   = b.Left;       x.Line2.Y2   = b.Bottom;

        x.Line1.Stroke   = brush;
        x.Line2.Stroke   = brush;

        Canvas.SetLeft(x.HitZoneRect, b.X - 3);
        Canvas.SetTop(x.HitZoneRect, b.Y - 3);
        x.HitZoneRect.Width  = b.Width  + 6;
        x.HitZoneRect.Height = b.Height + 6;

        var hps = new Point[] {
            new(b.Left, b.Top), new(b.Right, b.Top),
            new(b.Left, b.Bottom), new(b.Right, b.Bottom),
            new(b.Left + b.Width / 2, b.Top), new(b.Left + b.Width / 2, b.Bottom),
            new(b.Left, b.Top + b.Height / 2), new(b.Right, b.Top + b.Height / 2)
        };
        for (int i = 0; i < 8; i++) {
            Canvas.SetLeft(x.Handles[i], hps[i].X - 4);
            Canvas.SetTop(x.Handles[i],  hps[i].Y - 4);
        }
    }

    private void RemoveAnnotationX(AnnotationX x) {
        if (!_suppressUndo) PushUndo();
        if (x == _colorPickerX) HideColorPicker();
        _canvas.Children.Remove(x.Line1);
        _canvas.Children.Remove(x.Line2);
        _canvas.Children.Remove(x.HitZoneRect);
        foreach (var h in x.Handles) _canvas.Children.Remove(h);
        _annotXShapes.Remove(x);
    }

    private void SelectAnnotationX(AnnotationX? x) {
        if (_selectedAnnotX != null && _selectedAnnotX != x)
            foreach (var h in _selectedAnnotX.Handles) h.Visibility = Visibility.Hidden;
        _selectedAnnotX = x;
        if (x != null) {
            if (_selectedText != null) SelectText(null);
            SelectArrow(null);
            if (_selectedAnnotRect != null) SelectAnnotationRect(null);
            if (_selectedMeasureLine != null) {
                _selectedMeasureLine.Handle1.Visibility = Visibility.Hidden;
                _selectedMeasureLine.Handle2.Visibility = Visibility.Hidden;
                _selectedMeasureLine = null;
            }
            _selectedCursor = false;
            foreach (var h in x.Handles) h.Visibility = Visibility.Visible;
            ShowColorPickerForX(x);
        }
        else {
            if (_colorPickerX != null) HideColorPicker();
        }
    }

    private void ShowColorPickerForX(AnnotationX x) {
        HideColorPicker();
        _colorPickerX = x;

        _colorPickerPanel = MakeColorPickerGrid(GetAnnotationPalette(), x.XColor, picked => {
            x.XColor = picked;
            _defaultXColor = picked;
            UpdateXGeometry(x);
            ShowColorPickerForX(x);
        });
        Panel.SetZIndex(_colorPickerPanel, 300);
        _canvas.Children.Add(_colorPickerPanel);

        _colorPickerPanel.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        double pw = _colorPickerPanel.DesiredSize.Width;
        double ph = _colorPickerPanel.DesiredSize.Height;
        var b = x.Bounds;
        double centerX = b.Left + b.Width  / 2;
        double centerY = b.Top  + b.Height / 2;
        const double pad = 12;
        double spaceAbove = b.Top;
        double spaceBelow = _canvas.Height - b.Bottom;
        double spaceLeft  = b.Left;
        double spaceRight = _canvas.Width  - b.Right;
        double cx, cy;
        if (spaceAbove >= spaceBelow && spaceAbove >= spaceLeft && spaceAbove >= spaceRight) {
            cx = Math.Max(0, Math.Min(_canvas.Width - pw, centerX - pw / 2));
            cy = Math.Max(0, b.Top - ph - pad);
        } else if (spaceBelow >= spaceLeft && spaceBelow >= spaceRight) {
            cx = Math.Max(0, Math.Min(_canvas.Width - pw, centerX - pw / 2));
            cy = Math.Min(_canvas.Height - ph, b.Bottom + pad);
        } else if (spaceRight >= spaceLeft) {
            cx = Math.Min(_canvas.Width  - pw, b.Right + pad);
            cy = Math.Max(0, Math.Min(_canvas.Height - ph, centerY - ph / 2));
        } else {
            cx = Math.Max(0, b.Left - pw - pad);
            cy = Math.Max(0, Math.Min(_canvas.Height - ph, centerY - ph / 2));
        }
        Canvas.SetLeft(_colorPickerPanel, cx);
        Canvas.SetTop(_colorPickerPanel, cy);
    }

    // ── Rect mode ─────────────────────────────────────────────────────────────

    private void EnterRectMode() {
        SelectArrow(null);
        if (_selectedText != null) SelectText(null);
        _inMoveMode = false;
        _inCropMode = false;
        _inRectMode = true;
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
        var brush = new SolidColorBrush(rectColor) { Opacity = 0.8 };

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

        _canvas.Children.Add(border);
        Panel.SetZIndex(border, 5);
        _canvas.Children.Add(hitZone);
        Panel.SetZIndex(hitZone, 4);  // just below border (5) — still catches clicks outside border

        var annotRect = new AnnotationRect {
            Bounds = bounds,
            RectColor = rectColor,
            Border = border,
            Handles = handles,
            HitZoneRect = hitZone
        };

        border.MouseLeftButtonDown += (_, e) => {
            if (_draggingAnnotRect != null || _inArrowMode || _inRectMode) return;
            // Only initiate drag when clicking on or near the border edge — not the interior.
            if (!IsOnRectBorder(annotRect.Bounds, e.GetPosition(_canvas), 6.0)) return;
            if ((Keyboard.Modifiers & ModifierKeys.Control) != 0) {
                // Ctrl+drag: duplicate the rect; original stays, clone is dragged.
                PushUndo();
                _suppressUndo = true;
                var clone = CreateAnnotationRect(annotRect.Bounds, annotRect.RectColor);
                _suppressUndo = false;
                _draggingAnnotRect = clone;
                _annotRectBodyDragging = true;
                _draggingAnnotRectHandleIdx = -1;
                _annotRectDragStart = e.GetPosition(_canvas);
                _annotRectDragOriginal = clone.Bounds;
                _preDragSnapshot = null;
                // Track clone drag for Shift-constraint
                _rectCloneDragOriginalCenter = new Point(
                    clone.Bounds.X + clone.Bounds.Width / 2,
                    clone.Bounds.Y + clone.Bounds.Height / 2);
                clone.Border.CaptureMouse();
                e.Handled = true;
                return;
            }
            SelectAnnotationRect(annotRect);
            _preDragSnapshot = CaptureSnapshot();
            _suppressUndo = true;
            var rectStandby = CreateAnnotationRect(annotRect.Bounds, annotRect.RectColor);
            _suppressUndo = false;
            SetAnnotationVisible(rectStandby, (Keyboard.Modifiers & ModifierKeys.Control) != 0);
            _rectMoveStandbyClone = rectStandby;
            // CreateAnnotationRect called SelectAnnotationRect(standby), hiding annotRect's handles. Restore them.
            foreach (var h in rectStandby.Handles) h.Visibility = Visibility.Collapsed;
            foreach (var h in annotRect.Handles) h.Visibility = Visibility.Visible;
            _selectedAnnotRect = annotRect;
            HideColorPicker();
            _draggingAnnotRect = annotRect;
            _annotRectBodyDragging = true;
            _draggingAnnotRectHandleIdx = -1;
            _annotRectDragStart = e.GetPosition(_canvas);
            _annotRectDragOriginal = annotRect.Bounds;
            _rectCloneDragOriginalCenter = new Point(
                annotRect.Bounds.X + annotRect.Bounds.Width / 2,
                annotRect.Bounds.Y + annotRect.Bounds.Height / 2);
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
            
            // Apply Shift-key alignment constraint (move or clone drag)
            if ((Keyboard.Modifiers & ModifierKeys.Shift) != 0) {
                if (Math.Abs(dx) >= Math.Abs(dy)) dy = 0;
                else dx = 0;
            }
            
            var nb = new Rect(
                Math.Max(0, Math.Min(_annotRectDragOriginal.X + dx, cw - _annotRectDragOriginal.Width)),
                Math.Max(0, Math.Min(_annotRectDragOriginal.Y + dy, ch - _annotRectDragOriginal.Height)),
                _annotRectDragOriginal.Width,
                _annotRectDragOriginal.Height);
            annotRect.Bounds = nb;
            UpdateRectGeometry(annotRect);
            if (_colorPickerRect == annotRect) ShowColorPickerForRect(annotRect);
            if (_rectMoveStandbyClone != null)
                SetAnnotationVisible(_rectMoveStandbyClone, (Keyboard.Modifiers & ModifierKeys.Control) != 0);
            e.Handled = true;
        };
        border.MouseLeftButtonUp += (_, e) => {
            if (_draggingAnnotRect != annotRect || !_annotRectBodyDragging) return;
            if (_rectMoveStandbyClone != null) {
                if ((Keyboard.Modifiers & ModifierKeys.Control) != 0)
                    SetAnnotationVisible(_rectMoveStandbyClone, true);
                else {
                    _suppressUndo = true;
                    RemoveAnnotationRect(_rectMoveStandbyClone);
                    _suppressUndo = false;
                }
                _rectMoveStandbyClone = null;
            }
            CommitDragUndo();
            _draggingAnnotRect = null;
            _annotRectBodyDragging = false;
            border.ReleaseMouseCapture();
            _canvas.Cursor = Cursors.Arrow;
            ShowColorPickerForRect(annotRect);
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
            if ((Keyboard.Modifiers & ModifierKeys.Control) != 0) {
                // Ctrl+drag: duplicate the rect; original stays, clone is dragged.
                PushUndo();
                _suppressUndo = true;
                var clone = CreateAnnotationRect(annotRect.Bounds, annotRect.RectColor);
                _suppressUndo = false;
                _draggingAnnotRect = clone;
                _annotRectBodyDragging = true;
                _draggingAnnotRectHandleIdx = -1;
                _annotRectDragStart = e.GetPosition(_canvas);
                _annotRectDragOriginal = clone.Bounds;
                _preDragSnapshot = null;
                // Track clone drag for Shift-constraint
                _rectCloneDragOriginalCenter = new Point(
                    clone.Bounds.X + clone.Bounds.Width / 2,
                    clone.Bounds.Y + clone.Bounds.Height / 2);
                clone.HitZoneRect.CaptureMouse();
                e.Handled = true;
                return;
            }
            SelectAnnotationRect(annotRect);
            _preDragSnapshot = CaptureSnapshot();
            if (_rectMoveStandbyClone == null) {
                _suppressUndo = true;
                var rectStandby2 = CreateAnnotationRect(annotRect.Bounds, annotRect.RectColor);
                _suppressUndo = false;
                SetAnnotationVisible(rectStandby2, (Keyboard.Modifiers & ModifierKeys.Control) != 0);
                _rectMoveStandbyClone = rectStandby2;
                // CreateAnnotationRect called SelectAnnotationRect(standby), hiding annotRect's handles. Restore them.
                foreach (var h in rectStandby2.Handles) h.Visibility = Visibility.Collapsed;
                foreach (var h in annotRect.Handles) h.Visibility = Visibility.Visible;
                _selectedAnnotRect = annotRect;
                HideColorPicker();
            }
            _draggingAnnotRect = annotRect;
            _annotRectBodyDragging = true;
            _draggingAnnotRectHandleIdx = -1;
            _annotRectDragStart = e.GetPosition(_canvas);
            _annotRectDragOriginal = annotRect.Bounds;
            _rectCloneDragOriginalCenter = new Point(
                annotRect.Bounds.X + annotRect.Bounds.Width / 2,
                annotRect.Bounds.Y + annotRect.Bounds.Height / 2);
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
            
            // Apply Shift-key alignment constraint (move or clone drag)
            if ((Keyboard.Modifiers & ModifierKeys.Shift) != 0) {
                if (Math.Abs(dx) >= Math.Abs(dy)) dy = 0;
                else dx = 0;
            }
            
            var nb = new Rect(
                Math.Max(0, Math.Min(_annotRectDragOriginal.X + dx, cw - _annotRectDragOriginal.Width)),
                Math.Max(0, Math.Min(_annotRectDragOriginal.Y + dy, ch - _annotRectDragOriginal.Height)),
                _annotRectDragOriginal.Width,
                _annotRectDragOriginal.Height);
            annotRect.Bounds = nb;
            UpdateRectGeometry(annotRect);
            if (_colorPickerRect == annotRect) ShowColorPickerForRect(annotRect);
            if (_rectMoveStandbyClone != null)
                SetAnnotationVisible(_rectMoveStandbyClone, (Keyboard.Modifiers & ModifierKeys.Control) != 0);
            e.Handled = true;
        };
        hitZone.MouseLeftButtonUp += (_, e) => {
            if (_draggingAnnotRect != annotRect || !_annotRectBodyDragging) return;
            if (_rectMoveStandbyClone != null) {
                if ((Keyboard.Modifiers & ModifierKeys.Control) != 0)
                    SetAnnotationVisible(_rectMoveStandbyClone, true);
                else {
                    _suppressUndo = true;
                    RemoveAnnotationRect(_rectMoveStandbyClone);
                    _suppressUndo = false;
                }
                _rectMoveStandbyClone = null;
            }
            CommitDragUndo();
            _draggingAnnotRect = null;
            _annotRectBodyDragging = false;
            hitZone.ReleaseMouseCapture();
            _canvas.Cursor = Cursors.Arrow;
            ShowColorPickerForRect(annotRect);
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
        var brush = new SolidColorBrush(rect.RectColor) { Opacity = 0.8 };
        rect.Border.Stroke = brush;

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

        // Measure lines: proximity to shaft segment.
        const double MlTol = 6.0;
        foreach (var ml in _measureLines) {
            if (PointToSegmentDist(pt, ml.StartPt, ml.EndPt) <= MlTol) return true;
        }

        // X annotations: proximity to either diagonal line
        const double XTol = 6.0;
        foreach (var x in _annotXShapes) {
            var b = x.Bounds;
            if (PointToSegmentDist(pt, b.TopLeft, b.BottomRight) <= XTol) return true;
            if (PointToSegmentDist(pt, b.TopRight, b.BottomLeft) <= XTol) return true;
        }

        return false;
    }

    private void RemoveAnnotationRect(AnnotationRect rect) {
        if (!_suppressUndo) PushUndo();
        if (rect == _colorPickerRect) HideColorPicker();
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
            if (_selectedMeasureLine != null) {
                _selectedMeasureLine.Handle1.Visibility = Visibility.Hidden;
                _selectedMeasureLine.Handle2.Visibility = Visibility.Hidden;
                _selectedMeasureLine = null;
            }
            if (_selectedAnnotX != null) SelectAnnotationX(null);
            _selectedCursor = false;
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

        _colorPickerPanel = MakeColorPickerGrid(GetAnnotationPalette(), rect.RectColor, picked => {
            rect.RectColor = picked;
            _defaultRectColor = picked;
            UpdateRectGeometry(rect);
            ShowColorPickerForRect(rect);
        });
        Panel.SetZIndex(_colorPickerPanel, 300);
        _canvas.Children.Add(_colorPickerPanel);

        _colorPickerPanel.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        double pw = _colorPickerPanel.DesiredSize.Width;
        double ph = _colorPickerPanel.DesiredSize.Height;
        var b = rect.Bounds;
        const double Gap = 8;

        // Try above, below, right, left — pick the first that fits fully on canvas.
        // Fall back to the position with the most clearance if nothing fits cleanly.
        double cx, cy;
        if (b.Top - ph - Gap >= 0) {
            // Above
            cx = Math.Max(0, Math.Min(_canvas.Width - pw, b.Left + b.Width / 2 - pw / 2));
            cy = b.Top - ph - Gap;
        } else if (b.Bottom + ph + Gap <= _canvas.Height) {
            // Below
            cx = Math.Max(0, Math.Min(_canvas.Width - pw, b.Left + b.Width / 2 - pw / 2));
            cy = b.Bottom + Gap;
        } else if (b.Right + pw + Gap <= _canvas.Width) {
            // Right
            cx = b.Right + Gap;
            cy = Math.Max(0, Math.Min(_canvas.Height - ph, b.Top + b.Height / 2 - ph / 2));
        } else if (b.Left - pw - Gap >= 0) {
            // Left
            cx = b.Left - pw - Gap;
            cy = Math.Max(0, Math.Min(_canvas.Height - ph, b.Top + b.Height / 2 - ph / 2));
        } else {
            // Nothing fits cleanly — place above, clamped to canvas
            cx = Math.Max(0, Math.Min(_canvas.Width - pw, b.Left + b.Width / 2 - pw / 2));
            cy = Math.Max(0, b.Top - ph - Gap);
        }
        Canvas.SetLeft(_colorPickerPanel, cx);
        Canvas.SetTop(_colorPickerPanel, cy);
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
            SelectArrow(null);
            SelectAnnotationRect(null);
            SelectMeasureLine(null);
            SelectAnnotationX(null);
            SelectText(null);
            _selectedCursor = true;
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
            _eyedropperTooltipHexText = new TextBlock {
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
            textStack.Children.Add(_eyedropperTooltipHexText);
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
        _eyedropperTooltipHexText!.Text = $"#{color.R:X2}{color.G:X2}{color.B:X2}";
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
            AnnotationText? standbyText = null;

            display.MouseLeftButtonDown += (_, e) => {
                // In tool placement/eyedropper modes, let event bubble to canvas
                if (_inCursorPlacementMode || _inEyedropperMode) return;

                if (e.ClickCount == 2) {
                    BeginEditText(annotation);
                    e.Handled = true;
                    return;
                }

                // Ctrl+drag — duplicate annotation and drag the clone
                if ((Keyboard.Modifiers & ModifierKeys.Control) != 0) {
                    PushUndo();
                    var clone = new AnnotationText {
                        Bounds = annotation.Bounds,
                        Text = annotation.Text,
                        FontSize = annotation.FontSize,
                        TextColor = annotation.TextColor,
                        BackgroundColor = annotation.BackgroundColor
                    };
                    _texts.Add(clone);
                    UpdateTextDisplay(clone);
                    SelectText(clone);
                    _preDragSnapshot = null;
                    _canvasTextDragActive = true;
                    _canvasTextDragAnnotation = clone;
                    _canvasTextDragStart = e.GetPosition(_canvas);
                    _canvasTextDragOrigBounds = clone.Bounds;
                    // Track clone drag for Shift-constraint
                    _textCloneDragOriginalCenter = new Point(clone.Bounds.X + clone.Bounds.Width / 2, clone.Bounds.Y + clone.Bounds.Height / 2);
                    _canvas.CaptureMouse();
                    e.Handled = true;
                    return;
                }

                if (!_inTextMode && _selectedText != annotation)
                    SelectText(annotation);
                _preDragSnapshot = CaptureSnapshot();
                _suppressUndo = true;
                standbyText = new AnnotationText {
                    Bounds = annotation.Bounds, Text = annotation.Text, FontSize = annotation.FontSize,
                    TextColor = annotation.TextColor, BackgroundColor = annotation.BackgroundColor
                };
                _texts.Add(standbyText);
                UpdateTextDisplay(standbyText);
                _suppressUndo = false;
                SetAnnotationVisible(standbyText, (Keyboard.Modifiers & ModifierKeys.Control) != 0);
                isDragging = true;
                dragStart = e.GetPosition(_canvas);
                dragOrigBounds = annotation.Bounds;
                display.CaptureMouse();
                SquadDashTrace.Write("ImageEditor", $"TextAnnotation drag start at ({dragStart.X:F0},{dragStart.Y:F0}) captured={Mouse.Captured?.GetType().Name ?? "null"}");
                e.Handled = true;
            };
            display.MouseMove += (_, e) => {
                if (isDragging) {
                    var pt = e.GetPosition(_canvas);
                    var newX = Math.Max(0, Math.Min(dragOrigBounds.X + (pt.X - dragStart.X), _canvas.Width - 20));
                    var newY = Math.Max(0, Math.Min(dragOrigBounds.Y + (pt.Y - dragStart.Y), _canvas.Height - 16));
                    if ((Keyboard.Modifiers & ModifierKeys.Shift) != 0) {
                        var mouseDX = pt.X - dragStart.X;
                        var mouseDY = pt.Y - dragStart.Y;
                        if (Math.Abs(mouseDX) >= Math.Abs(mouseDY))
                            newY = Math.Max(0, Math.Min(dragOrigBounds.Y, _canvas.Height - 16));
                        else
                            newX = Math.Max(0, Math.Min(dragOrigBounds.X, _canvas.Width - 20));
                    }
                    annotation.Bounds = new Rect(newX, newY, annotation.Bounds.Width, annotation.Bounds.Height);
                    Canvas.SetLeft(display, newX);
                    Canvas.SetTop(display, newY);
                    if (standbyText != null)
                        SetAnnotationVisible(standbyText, (Keyboard.Modifiers & ModifierKeys.Control) != 0);
                    UpdateTextSelectionBorder();
                    e.Handled = true;
                    return;
                }
                // Body of annotation always shows the move cursor — resize handles show their own cursors.
            };
            display.MouseLeftButtonUp += (_, e) => {
                if (!isDragging) return;
                SquadDashTrace.Write("ImageEditor", $"TextAnnotation drag end at ({e.GetPosition(_canvas).X:F0},{e.GetPosition(_canvas).Y:F0}) → bounds=({annotation.Bounds.X:F0},{annotation.Bounds.Y:F0})");
                if (standbyText != null) {
                    if ((Keyboard.Modifiers & ModifierKeys.Control) != 0)
                        SetAnnotationVisible(standbyText, true);
                    else {
                        _suppressUndo = true;
                        RemoveTextAnnotation(standbyText);
                        _suppressUndo = false;
                    }
                    standbyText = null;
                }
                CommitDragUndo();
                isDragging = false;
                display.ReleaseMouseCapture();
                _canvas.Cursor = Cursors.Arrow;
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

            annotation.Display = display;
            Panel.SetZIndex(display, 20);
            _canvas.Children.Add(display);
        }

        annotation.Display.Text = annotation.Text;
        annotation.Display.FontSize = annotation.FontSize;

        // Update colors (handles re-render after color change)
        annotation.Display.Foreground = new SolidColorBrush(annotation.TextColor);
        annotation.Display.Background = annotation.BackgroundColor.A == 0
            ? Brushes.Transparent
            : new SolidColorBrush(annotation.BackgroundColor);
        annotation.Display.Padding = annotation.BackgroundColor.A > 0
            ? new Thickness(4, 1, 4, 2)
            : new Thickness(0);

        // Apply fixed-width wrapping when annotation was drag-drawn with an explicit width.
        bool hasFixedWidth = annotation.Bounds.Width > 0;
        if (hasFixedWidth) {
            annotation.Display.Width = annotation.Bounds.Width;
            annotation.Display.TextWrapping = TextWrapping.Wrap;
        }
        else {
            annotation.Display.Width = double.NaN;
            annotation.Display.TextWrapping = TextWrapping.NoWrap;
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
        annotation.Display.Visibility = Visibility.Visible;
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
        SquadDashTrace.Write("ImageEditor",
            $"[ClipboardImageEditor] CropInPlace: before={pxW}x{pxH}px sel={sel.Left:F1},{sel.Top:F1},{sel.Width:F1}x{sel.Height:F1} " +
            $"cropPx={cropX},{cropY},{cropW}x{cropH} canvasScale={_canvasScaleX:F3}x{_canvasScaleY:F3}");
        _workingImage = cropped;
        _appliedCropOffsetX += sel.Left;
        _appliedCropOffsetY += sel.Top;
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
        UpdateScrollExtentSurfaceSize();
        _scrollViewer.InvalidateMeasure();
        _scrollViewer.InvalidateArrange();
        _scrollViewer.UpdateLayout();

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

            foreach (var ml in _measureLines.ToList()) {
                var mlBounds = new Rect(
                    Math.Min(ml.StartPt.X, ml.EndPt.X),
                    Math.Min(ml.StartPt.Y, ml.EndPt.Y),
                    Math.Abs(ml.EndPt.X - ml.StartPt.X),
                    Math.Abs(ml.EndPt.Y - ml.StartPt.Y));
                if (!sel.IntersectsWith(mlBounds) && mlBounds.Width + mlBounds.Height > 0) {
                    RemoveMeasureLine(ml);
                }
                else {
                    ml.StartPt = new Point(ml.StartPt.X - dx, ml.StartPt.Y - dy);
                    ml.EndPt   = new Point(ml.EndPt.X   - dx, ml.EndPt.Y   - dy);
                    UpdateMeasureLineGeometry(ml);
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
        ApplyZoom(Math.Min(1.0, Math.Min(fitW, fitH)));
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

        foreach (var xShape in _annotXShapes)
            foreach (var h in xShape.Handles) h.Visibility = Visibility.Collapsed;

        try {
            SourceImage = _originalImage;
            AnnotationState = CaptureAnnotationState();
            Result = RenderFinalBitmap();
            if (Result is not null)
                ImageAccepted?.Invoke(Result);
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

        foreach (var xShape in _annotXShapes)
            foreach (var h in xShape.Handles) h.Visibility = Visibility.Collapsed;

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
            foreach (var xShape in _annotXShapes)
                foreach (var h in xShape.Handles) h.Visibility = Visibility.Visible;
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

        SquadDashTrace.Write("ImageEditor",
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

    // ── Annotation state serialisation (for re-editable prompt attachments) ───

    /// <summary>
    /// Captures the current annotation state as a JSON-serialisable object.
    /// Called in <see cref="DoInsertImage"/> so the caller can persist it.
    /// </summary>
    // Guards against NaN/±Infinity values that JSON cannot serialize.
    // Canvas.Width/Height and Canvas.GetLeft/GetTop return NaN when not explicitly set.
    private static double Safe(double v) => double.IsFinite(v) ? v : 0.0;

    private ClipboardAnnotationState CaptureAnnotationState() {
        bool hasCrop = _appliedCropOffsetX != 0 || _appliedCropOffsetY != 0
                       || _workingImage.PixelWidth  != _originalImage.PixelWidth
                       || _workingImage.PixelHeight != _originalImage.PixelHeight;
        var state = new ClipboardAnnotationState {
            CanvasScaleX    = Safe(_canvasScaleX),
            CanvasScaleY    = Safe(_canvasScaleY),
            HasCrop         = !_sel.IsEmpty,
            CropX           = Safe(_sel.X),
            CropY           = Safe(_sel.Y),
            CropW           = Safe(_sel.Width),
            CropH           = Safe(_sel.Height),
            HasAppliedCrop  = hasCrop,
            AppliedCropX    = Safe(_appliedCropOffsetX),
            AppliedCropY    = Safe(_appliedCropOffsetY),
            AppliedCropW    = Safe(_canvas.Width),
            AppliedCropH    = Safe(_canvas.Height),
            CursorEnabled   = _cursorEnabled,
            CursorX         = _cursorImage != null ? Safe(Canvas.GetLeft(_cursorImage)) : 0,
            CursorY         = _cursorImage != null ? Safe(Canvas.GetTop(_cursorImage))  : 0,
        };

        foreach (var a in _arrows) {
            state.Arrows.Add(new ClipboardAnnotationArrowState {
                TargetElementName = a.TargetElementName,
                TargetBoundsX     = Safe(a.TargetElementBounds.X),
                TargetBoundsY     = Safe(a.TargetElementBounds.Y),
                TargetBoundsW     = Safe(a.TargetElementBounds.Width),
                TargetBoundsH     = Safe(a.TargetElementBounds.Height),
                ArrowheadAngleDeg = Safe(a.ArrowheadAngleDeg),
                ArrowLength       = Safe(a.ArrowLength),
                TailLength        = Safe(a.TailLength),
                UserTailLength    = Safe(a.UserTailLength),
                Color             = $"#{a.ArrowColor.R:X2}{a.ArrowColor.G:X2}{a.ArrowColor.B:X2}",
                TargetCenterX     = Safe(a.TargetCenterOnCanvas.X),
                TargetCenterY     = Safe(a.TargetCenterOnCanvas.Y),
                OffsetX           = Safe(a.OffsetX),
                OffsetY           = Safe(a.OffsetY),
            });
        }

        foreach (var r in _annotRects) {
            state.Rects.Add(new ClipboardAnnotationRectState {
                X     = Safe(r.Bounds.X),
                Y     = Safe(r.Bounds.Y),
                W     = Safe(r.Bounds.Width),
                H     = Safe(r.Bounds.Height),
                Color = $"#{r.RectColor.R:X2}{r.RectColor.G:X2}{r.RectColor.B:X2}",
            });
        }

        foreach (var t in _texts) {
            state.Texts.Add(new ClipboardAnnotationTextState {
                X        = Safe(t.Bounds.X),
                Y        = Safe(t.Bounds.Y),
                W        = Safe(t.Bounds.Width),
                H        = Safe(t.Bounds.Height),
                Text     = t.Text,
                FontSize = Safe(t.FontSize),
                FgColor  = $"#{t.TextColor.R:X2}{t.TextColor.G:X2}{t.TextColor.B:X2}",
                BgColor  = t.BackgroundColor.A == 0
                    ? "#00000000"
                    : $"#{t.BackgroundColor.A:X2}{t.BackgroundColor.R:X2}{t.BackgroundColor.G:X2}{t.BackgroundColor.B:X2}",
            });
        }

        foreach (var ml in _measureLines) {
            state.MeasureLines.Add(new ClipboardAnnotationMeasureLineState {
                X1           = Safe(ml.StartPt.X),
                Y1           = Safe(ml.StartPt.Y),
                X2           = Safe(ml.EndPt.X),
                Y2           = Safe(ml.EndPt.Y),
                IsHorizontal = ml.IsHorizontal,
                Color        = $"#{ml.LineColor.R:X2}{ml.LineColor.G:X2}{ml.LineColor.B:X2}",
            });
        }

        foreach (var x in _annotXShapes) {
            state.Xs.Add(new ClipboardAnnotationXState {
                X     = Safe(x.Bounds.X),
                Y     = Safe(x.Bounds.Y),
                W     = Safe(x.Bounds.Width),
                H     = Safe(x.Bounds.Height),
                Color = $"#{x.XColor.R:X2}{x.XColor.G:X2}{x.XColor.B:X2}",
            });
        }

        return state;
    }

    /// <summary>
    /// Restores annotation state from a previously saved <see cref="ClipboardAnnotationState"/>.
    /// Called from the <c>Loaded</c> handler when the editor is opened for re-editing.
    /// </summary>
    private void RestoreAnnotationState(ClipboardAnnotationState state) {
        _suppressUndo = true;
        try {
            // If the state captured a destructive crop, it was made from a sub-region of
            // the original image.  The re-open path always passes the full original image,
            // so we restore the crop as a *selection* (the user can adjust/expand it) and
            // shift all annotation coordinates back into original-image space.
            double ox = state.HasAppliedCrop ? state.AppliedCropX : 0;
            double oy = state.HasAppliedCrop ? state.AppliedCropY : 0;

            if (state.HasAppliedCrop)
                _sel = new Rect(state.AppliedCropX, state.AppliedCropY, state.AppliedCropW, state.AppliedCropH);
            else
                _sel = state.HasCrop
                    ? new Rect(state.CropX, state.CropY, state.CropW, state.CropH)
                    : Rect.Empty;

            foreach (var a in state.Arrows) {
                var targetBounds = new Rect(a.TargetBoundsX + ox, a.TargetBoundsY + oy, a.TargetBoundsW, a.TargetBoundsH);
                var arrow = CreateArrow(targetBounds);
                arrow.ArrowheadAngleDeg    = a.ArrowheadAngleDeg;
                arrow.ArrowLength          = a.ArrowLength;
                arrow.TailLength           = a.TailLength;
                arrow.UserTailLength       = a.UserTailLength;
                arrow.ArrowColor           = ParseHexColor(a.Color, Color.FromRgb(255, 120, 20));
                arrow.TargetCenterOnCanvas = new Point(a.TargetCenterX + ox, a.TargetCenterY + oy);
                arrow.OffsetX              = a.OffsetX;
                arrow.OffsetY              = a.OffsetY;
                UpdateArrowGeometry(arrow);
            }
            SelectArrow(null);

            foreach (var r in state.Rects)
                CreateAnnotationRect(new Rect(r.X + ox, r.Y + oy, r.W, r.H), ParseHexColor(r.Color, Color.FromRgb(255, 80, 80)));
            SelectAnnotationRect(null);

            foreach (var xs in state.Xs)
                CreateAnnotationX(new Rect(xs.X + ox, xs.Y + oy, xs.W, xs.H), ParseHexColor(xs.Color, Color.FromRgb(255, 80, 80)));
            SelectAnnotationX(null);

            foreach (var t in state.Texts) {
                var at = new AnnotationText {
                    Bounds          = new Rect(t.X + ox, t.Y + oy, t.W, t.H),
                    Text            = t.Text,
                    FontSize        = t.FontSize,
                    TextColor       = ParseHexColor(t.FgColor, Colors.White),
                    BackgroundColor = ParseHexColor(t.BgColor, Colors.Black),
                };
                _texts.Add(at);
                UpdateTextDisplay(at);
            }

            foreach (var ml in state.MeasureLines)
                CreateMeasureLine(
                    new Point(ml.X1 + ox, ml.Y1 + oy), new Point(ml.X2 + ox, ml.Y2 + oy),
                    ml.IsHorizontal,
                    ParseHexColor(ml.Color, Color.FromRgb(255, 120, 20)));
            SelectMeasureLine(null);

            _cursorEnabled = state.CursorEnabled;
            if (state.CursorEnabled) {
                EnsureCursorImageCreated();
                Canvas.SetLeft(_cursorImage!, state.CursorX + ox);
                Canvas.SetTop(_cursorImage!,  state.CursorY + oy);
                _cursorImage!.Visibility = Visibility.Visible;
            }

            RefreshLayout();
            EnterMoveMode();
        }
        finally {
            _suppressUndo = false;
        }
    }

    // ── Session state capture ─────────────────────────────────────────────────

    /// <summary>Returns current window position, size, and maximized state as a tuple.</summary>
    private (double x, double y, double w, double h, bool isMaximized) CaptureWindowGeometry()
        => (Left, Top, Width, Height, WindowState == WindowState.Maximized);

    /// <summary>Returns the name of the active tool and the current zoom level.</summary>
    private (string tool, double zoom) CaptureToolState() {
        string tool = "move";
        if      (_inArrowMode)           tool = "arrow";
        else if (_inRectMode)            tool = "rect";
        else if (_inTextMode)            tool = "text";
        else if (_inCropMode)            tool = "crop";
        else if (_inMeasureLineMode)     tool = "measureLine";
        else if (_inXMode)               tool = "x";
        else if (_inCursorPlacementMode) tool = "cursor";
        else if (_inEyedropperMode)      tool = "eyedropper";
        return (tool, _zoom);
    }

    /// <summary>Builds a complete serialisable snapshot of the current editor state.</summary>
    private ClipboardEditorSessionState CaptureSessionState() {
        var (x, y, w, h, isMax) = CaptureWindowGeometry();
        var (tool, zoom)        = CaptureToolState();
        return new ClipboardEditorSessionState {
            EditorId       = _editorId,
            IsPromptMode   = _isPromptMode,
            WindowGeometry = new WindowGeometryState { X = x, Y = y, Width = w, Height = h, IsMaximized = isMax },
            ToolState      = new ToolStateInfo { SelectedTool = tool, ZoomLevel = zoom },
            AnnotationState = CaptureAnnotationState(),
            ImageMetadata  = new ImageMetadataInfo {
                OriginalImageWidth  = _originalImage.PixelWidth,
                OriginalImageHeight = _originalImage.PixelHeight,
            },
            SavedAt    = DateTime.UtcNow.ToString("O"),
            AppVersion = typeof(ClipboardImageEditorWindow).Assembly.GetName().Version?.ToString() ?? "",
        };
    }

    // ── Session state restoration ─────────────────────────────────────────────

    /// <summary>
    /// Clamps and applies window position/size from a previously saved snapshot.
    /// Ensures no part of the window lands off every connected screen.
    /// </summary>
    private void RestoreWindowGeometry(double x, double y, double w, double h, bool isMaximized) {
        const double MinVisible = 50.0;

        if (w < MinWidth) w = MinWidth;
        if (h < 200)      h = 200;

        double screenLeft   = SystemParameters.VirtualScreenLeft;
        double screenTop    = SystemParameters.VirtualScreenTop;
        double screenRight  = screenLeft + SystemParameters.VirtualScreenWidth;
        double screenBottom = screenTop  + SystemParameters.VirtualScreenHeight;

        x = Math.Max(screenLeft, Math.Min(x, screenRight  - MinVisible));
        y = Math.Max(screenTop,  Math.Min(y, screenBottom - MinVisible));

        Left   = x;
        Top    = y;
        Width  = w;
        Height = h;
        if (isMaximized)
            WindowState = WindowState.Maximized;
    }

    /// <summary>
    /// Restores all persisted editor state: window geometry, source image, annotations,
    /// tool selection, and zoom level.  Called from the <c>Loaded</c> handler when a
    /// <see cref="ClipboardEditorSessionState"/> is provided to the constructor.
    /// </summary>
    private async Task RestoreFromSessionStateAsync(ClipboardEditorSessionState state) {
        try {
            SquadDashTrace.Write("ImageEditor", $"Restoring editor {state.EditorId}");

            // Restore window geometry (UI-thread, synchronous).
            if (state.WindowGeometry is { } geo)
                RestoreWindowGeometry(geo.X, geo.Y, geo.Width, geo.Height, geo.IsMaximized);

            // Load source image from disk if the path is available and valid.
            string? imagePath = state.ImageMetadata?.SourceImagePath;
            if (!string.IsNullOrEmpty(imagePath) && File.Exists(imagePath)) {
                try {
                    // Load on a background thread; ConfigureAwait(true) brings the
                    // continuation back to the UI thread so we can update visual elements.
                    BitmapSource bmp = await Task.Run(() => {
                        var img = new BitmapImage();
                        img.BeginInit();
                        img.CacheOption = BitmapCacheOption.OnLoad;
                        img.UriSource   = new Uri(imagePath);
                        img.EndInit();
                        img.Freeze();
                        return (BitmapSource)img;
                    }).ConfigureAwait(true);

                    _workingImage        = bmp;
                    _originalImage       = bmp;
                    _imageCtrl.Source    = bmp;
                    _canvas.Width        = bmp.PixelWidth;
                    _canvas.Height       = bmp.PixelHeight;
                    _imageCtrl.Width     = bmp.PixelWidth;
                    _imageCtrl.Height    = bmp.PixelHeight;
                    UpdateScrollExtentSurfaceSize();
                    _scrollViewer.InvalidateMeasure();
                    _scrollViewer.InvalidateArrange();
                    _scrollViewer.UpdateLayout();
                    RefreshLayout();
                }
                catch (Exception ex) {
                    SquadDashTrace.Write("ImageEditor",
                        $"Image load failed from {imagePath}: {ex.Message} — keeping existing image");
                }
            }

            // Restore annotations (falls back to move mode if AnnotationState is null).
            if (state.AnnotationState != null)
                RestoreAnnotationState(state.AnnotationState);
            else
                EnterMoveMode();

            // Restore tool selection and zoom level.
            if (state.ToolState != null) {
                RestoreToolMode(state.ToolState.SelectedTool ?? "move");
                ApplyZoom(state.ToolState.ZoomLevel);
                UpdateWindowSizeForZoom();
            }

            SquadDashTrace.Write("ImageEditor", $"Editor {state.EditorId} restored");
        }
        catch (Exception ex) {
            SquadDashTrace.Write("ImageEditor",
                $"RestoreFromSessionStateAsync failed: {ex.Message}");
        }
    }

    /// <summary>Activates the named tool without triggering multi-drop mode.</summary>
    private void RestoreToolMode(string selectedTool) {
        ExitAllToolModes();
        switch (selectedTool) {
            case "arrow":       EnterArrowMode();       break;
            case "rect":        EnterRectMode();        break;
            case "text":        EnterTextMode();        break;
            case "crop":        EnterCropMode();        break;
            case "measureLine": EnterMeasureLineMode(); break;
            case "x":           EnterXMode();           break;
            default:            EnterMoveMode();        break;
        }
    }

    // ── Session state save ────────────────────────────────────────────────────

    /// <summary>
    /// Fire-and-forget save of the full editor state to disk.
    /// All UI-thread reads happen synchronously before the first <c>await</c>;
    /// the method never propagates exceptions to the UI thread.
    /// An optional <paramref name="store"/> may be supplied by the caller (e.g. MainWindow) to
    /// reuse an existing store instance; if null, a default store is created.
    /// </summary>
    internal async Task SaveStateAsync(string reason, ClipboardEditorStateStore? store = null) {
        try {
            SquadDashTrace.Write("ImageEditor",
                $"SaveStateAsync start: editor={_editorId} reason={reason}");

            // Capture all UI-thread state synchronously before any await.
            int    imgW  = _originalImage.PixelWidth;
            int    imgH  = _originalImage.PixelHeight;
            var    state = CaptureSessionState();

            // Encode original image to a byte array while still on the UI thread.
            // PngBitmapEncoder requires UI-thread access to BitmapSource.
            byte[]? imageBytes = null;
            try {
                using var ms = new MemoryStream();
                var encoder = new PngBitmapEncoder();
                encoder.Frames.Add(BitmapFrame.Create(_originalImage));
                encoder.Save(ms);
                imageBytes = ms.ToArray();
            }
            catch (Exception ex) {
                SquadDashTrace.Write("ImageEditor",
                    $"Image encode failed for {_editorId}: {ex.Message}");
            }

            // Capture editorId locally — the rest runs on a thread-pool thread.
            var editorId    = _editorId;
            var storeToUse  = store ?? new ClipboardEditorStateStore();
            string sourceImagePath = storeToUse.GetSourceImagePath(editorId);

            if (imageBytes != null) {
                try {
                    await File.WriteAllBytesAsync(sourceImagePath, imageBytes).ConfigureAwait(false);
                    state.ImageMetadata = new ImageMetadataInfo {
                        SourceImagePath     = sourceImagePath,
                        OriginalImageWidth  = imgW,
                        OriginalImageHeight = imgH,
                    };
                }
                catch (Exception ex) {
                    SquadDashTrace.Write("ImageEditor",
                        $"Image write failed for {editorId}: {ex.Message}");
                }
            }

            await storeToUse.SaveAsync(state, reason).ConfigureAwait(false);
        }
        catch (Exception ex) {
            SquadDashTrace.Write("ImageEditor",
                $"SaveStateAsync failed for {_editorId}: {ex.Message}");
        }
    }

    /// <summary>
    /// Removes all persisted state files for this editor instance.
    /// Called on every exit path (Attach Image, Update Image, window X button) so that
    /// gracefully-closed editors are never re-opened on the next app startup.
    /// </summary>
    internal async Task DeleteStateAsync(ClipboardEditorStateStore? store = null) {
        try {
            var storeToUse = store ?? new ClipboardEditorStateStore();
            await storeToUse.DeleteAsync(_editorId, isPending: false).ConfigureAwait(false);
            await storeToUse.DeleteAsync(_editorId, isPending: true).ConfigureAwait(false);

            var sourcePath = storeToUse.GetSourceImagePath(_editorId);
            try {
                if (File.Exists(sourcePath))
                    File.Delete(sourcePath);
            }
            catch (IOException ex) {
                SquadDashTrace.Write("ImageEditor", $"DeleteStateAsync: could not remove source image {sourcePath}: {ex.Message}");
            }

            SquadDashTrace.Write("ImageEditor", $"DeleteStateAsync: cleared state for editor {_editorId}");
        }
        catch (Exception ex) {
            SquadDashTrace.Write("ImageEditor", $"DeleteStateAsync failed for {_editorId}: {ex.Message}");
        }
    }

    /// <summary>Parses a WPF colour from a hex string (<c>#RGB</c>, <c>#RRGGBB</c>,
    /// <c>#AARRGGBB</c>). Returns <paramref name="fallback"/> on any parse failure.</summary>
    private static Color ParseHexColor(string hex, Color fallback) {
        try {
            return (Color)System.Windows.Media.ColorConverter.ConvertFromString(hex);
        }
        catch {
            return fallback;
        }
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
        CanvasScaleY: _canvasScaleY,
        MeasureLines: _measureLines.Select(ml => new MeasureLineSnap(ml.StartPt, ml.EndPt, ml.IsHorizontal, ml.LineColor)).ToList(),
        Xs: _annotXShapes.Select(x => new XSnap(x.Bounds, x.XColor)).ToList());

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
            SelectAnnotationX(null);
            foreach (var a in _arrows.ToList()) RemoveArrow(a);
            foreach (var r in _annotRects.ToList()) RemoveAnnotationRect(r);
            foreach (var xl in _annotXShapes.ToList()) RemoveAnnotationX(xl);
            foreach (var ml in _measureLines.ToList()) RemoveMeasureLine(ml);

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
            foreach (var xs in snap.Xs)
                CreateAnnotationX(xs.Bounds, xs.XColor);
            SelectAnnotationX(null);
            foreach (var t in _texts.ToList()) RemoveTextAnnotation(t);

            foreach (var ms in snap.MeasureLines)
                CreateMeasureLine(ms.StartPt, ms.EndPt, ms.IsHorizontal, ms.LineColor);
            SelectMeasureLine(null);

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
            UpdateScrollExtentSurfaceSize();
            _scrollViewer.InvalidateMeasure();
            _scrollViewer.InvalidateArrange();
            _scrollViewer.UpdateLayout();
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
            SquadDashTrace.Write("ImageEditor",
                $"[ClipboardImageEditor] RestoreSnapshot: preW={preRestoreW:F1} preH={preRestoreH:F1} " +
                $"snapW={snap.CanvasW:F1} snapH={snap.CanvasH:F1} " +
                $"canvasW={_canvas.Width:F1} canvasH={_canvas.Height:F1}");
            if (Math.Abs(snap.CanvasW - preRestoreW) > 0.5 || Math.Abs(snap.CanvasH - preRestoreH) > 0.5) {
                var undoWork = GetMonitorWorkAreaRect(this);
                const double UndoCropToolbarH = 110.0;
                double undoFitW = (undoWork.Width * 0.95 - 24) / snap.CanvasW;
                double undoFitH = (undoWork.Height * 0.95 - UndoCropToolbarH) / snap.CanvasH;
                ApplyZoom(Math.Min(1.0, Math.Min(undoFitW, undoFitH)));
                UpdateWindowSizeForZoom();
                SquadDashTrace.Write("ImageEditor",
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
            if (_selectedMeasureLine != null) SelectMeasureLine(null);
            if (_selectedAnnotX != null) SelectAnnotationX(null);
            _selectedCursor = false;
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
                if (!GetAnnotationPalette().Any(c => c.R == annotation.TextColor.R && c.G == annotation.TextColor.G && c.B == annotation.TextColor.B)) {
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

        var fgGrid = MakeColorPickerGrid(GetAnnotationPalette(), annotation.TextColor, picked => {
            annotation.TextColor = picked;
            _defaultTextFgColor = picked;
            SaveTextDefaults();
            UpdateTextDisplay(annotation);
            ShowColorPickerForText(annotation);
        });

        outerPanel.Children.Add(bgRow);
        outerPanel.Children.Add(fgGrid);
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
        double CanvasScaleY,
        IReadOnlyList<MeasureLineSnap> MeasureLines,
        IReadOnlyList<XSnap> Xs);

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

    private sealed record MeasureLineSnap(Point StartPt, Point EndPt, bool IsHorizontal, Color LineColor);

    private sealed record XSnap(Rect Bounds, Color XColor);

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
        || _measureLines.Count > 0
        || _annotXShapes.Count > 0
        || (_cursorEnabled && _cursorImage != null)
        || !_sel.IsEmpty;

    internal bool IsUpdateMode => _isUpdateMode;

    /// <summary>
    /// Identifies which prompt item's attachment is open in this editor.
    /// Empty string means the active draft; any other value is a queued-item ID.
    /// Used by the send-guard to skip editors that belong to a different item.
    /// </summary>
    internal string PromptItemId { get; set; } = "";

    /// <summary>
    /// Returns true if the annotation state has changed from what was loaded when the editor opened.
    /// Used by the send-guard to decide whether to prompt the user before dispatching a prompt.
    /// </summary>
    internal bool HasUnsavedChangesVsInitialState()
    {
        if (_openStateSnapshotJson == null)
            return HasChanges(); // no snapshot available: fall back to "any annotations" check
        var current = System.Text.Json.JsonSerializer.Serialize(CaptureAnnotationState());
        return current != _openStateSnapshotJson;
    }

    /// <summary>
    /// Programmatically triggers the "Update Image" action — renders the final bitmap,
    /// fires <see cref="ImageAccepted"/>, and closes the window.
    /// </summary>
    internal void TriggerInsertImage() => DoInsertImage();
}

