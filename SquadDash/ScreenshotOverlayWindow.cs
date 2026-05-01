using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using System.Threading.Tasks;
using System.Windows.Threading;

namespace SquadDash;

/// <summary>
/// Transparent owned window that lets the user drag-select a region of the
/// MainWindow content and captures it as a PNG via <see cref="RenderTargetBitmap"/>.
///
/// Pattern: code-behind only (no XAML), all colours via SetResourceReference —
/// consistent with <see cref="PushToTalkWindow"/> and <see cref="AgentInfoWindow"/>.
///
/// DPI correctness: physical pixel dimensions come from
/// <c>VisualTreeHelper.GetDpi(_mainWindow)</c>.  The selection rect is stored and
/// compared in logical units; conversion to physical pixels happens only at
/// capture time.
/// </summary>
internal sealed class ScreenshotOverlayWindow : Window
{
    // ── Events ───────────────────────────────────────────────────────────────

    /// <summary>
    /// Raised on the UI thread after the PNG is saved.
    /// Args carry the provisional PNG path, the captured selection rect,
    /// the four <see cref="EdgeAnchor"/> results, and a full-window flag.
    /// </summary>
    internal event EventHandler<ScreenshotSavedEventArgs>? ScreenshotSaved;

    /// <summary>Raised on the UI thread when capture or save fails. Argument = error message.</summary>
    internal event EventHandler<string>? ScreenshotFailed;

    // ── Hit zones ────────────────────────────────────────────────────────────

    private enum HitZone { None, Move, NW, N, NE, E, SE, S, SW, W, Draw }

    // ── Constants ────────────────────────────────────────────────────────────

    private const double HandleSize       = 9.0;   // side length of each resize handle square
    private const double HitPad           = 5.0;   // extra hit-test tolerance around handles
    private const double MinSize          = 24.0;  // minimum selection dimension in logical units
    private const double ToolbarMargin    = 8.0;   // logical px gap between toolbar and selection edge
    private const double ToolbarThreshold = 52.0;  // min logical px of clear space to float toolbar outside
    
    // ── State ────────────────────────────────────────────────────────────────

    private readonly Window _mainWindow;
    private readonly string _saveDirectory;
    private readonly string _themeName;

    private Rect     _sel;                             // selection in logical coords relative to this window
    private HitZone  _activeZone = HitZone.None;
    private Point    _dragStart;
    private Rect     _dragOriginal;

    // ── Visual elements ──────────────────────────────────────────────────────

    private readonly Canvas    _canvas;
    private readonly Rectangle _dimTop, _dimBottom, _dimLeft, _dimRight;

    // Selection border — single 2px rect placed exactly on the selection edge.
    private readonly Rectangle _selBorderRect;

    // Handles: index order = NW(0) N(1) NE(2) E(3) SE(4) S(5) SW(6) W(7)
    private readonly Rectangle[] _handles = new Rectangle[8];

    // Floating capture/cancel toolbar — repositioned live around _sel
    private readonly Border _toolbarBorder;

    // Unified capture-edit panel: name box + button row + description box.
    private readonly Border  _editBorder;
    private readonly TextBox _nameTextBox;        // name field (validated on Save)
    private          TextBox? _descriptionBox;    // description field

    // Arrow button — stored so ExitArrowTargetMode can reset its label via Esc.
    private Button? _addArrowBtn;

    // Near-zero-alpha rectangle covering _sel in annotation mode; forces the OS
    // compositor to route mouse events to this overlay instead of the window below.
    // (AllowsTransparency=true layered windows only receive OS mouse events where
    //  the composited pixel alpha > 0; inside _sel the canvas is fully transparent.)
    private Rectangle _selHitRect = null!;

    // Marching-ants element-highlight — two overlaid 1px dashed rects
    // with a RectangleGeometry clip so they don't bleed outside _sel.
    private readonly Rectangle         _highlightWhite;
    private readonly Rectangle         _highlightBlack;
    private readonly RectangleGeometry _highlightClip = new();

    // Mode-hint overlay (shown when entering arrow-target or cursor-placement mode).
    private Border?    _modeHintBorder;
    private TextBlock? _modeHintText;

    // Edge-anchor indicator labels: index order = Top(0) Right(1) Bottom(2) Left(3)
    private readonly Border[] _anchorLabels = new Border[4];

    // ── Capture-flow state ───────────────────────────────────────────────────
    // Snapshotted when user clicks Capture, consumed in DoAnnotationSaveAsync().

    private Rect         _capturedSel;
    private EdgeAnchor[] _capturedAnchors       = Array.Empty<EdgeAnchor>();
    private bool         _capturedIsFullWindow;

    // ── Annotation-mode state ────────────────────────────────────────────────
    // Active from Capture click through to Save.

    private bool         _inAnnotationMode;
    private string       _acceptedName          = "";
    private bool         _inArrowTargetMode;          // ↗ Arrow button pressed → click to target
    private bool         _multiDropArrowMode;         // Shift+click arrow btn → continuous drop mode
    private bool         _inCursorPlacementMode;      // ⌖ Cursor ON → click to place

    // Per-canvas annotation objects
    private readonly List<AnnotationArrow> _arrows = new();
    private AnnotationArrow?               _draggingArrow;
    private AnnotationArrow?               _selectedArrow;

    // Color picker state
    private StackPanel?      _colorPickerPanel;
    private AnnotationArrow? _colorPickerArrow;

    // ── Arrow creation defaults (updated as user edits arrows; persisted) ────
    private Color  _defaultArrowColor    = Color.FromRgb(255, 120, 20);   // orange
    private double _defaultArrowAngleDeg = 225.0;
    private double _defaultArrowLength   = 15.0;
    private double _defaultTailLength    = -1.0;   // -1 = auto-fill to sel edge

    // Cursor overlay
    private Image?  _cursorImage;
    private bool    _cursorEnabled;
    private Point   _cursorLogicalPos;   // offset from anchor top-left (or _sel.TopLeft when no anchor)
    private bool    _draggingCursor;
    private string? _cursorAnchorName;   // null = sel-relative fallback
    private Rect    _cursorAnchorBounds; // anchor bounds in mainWindow logical coords

    // Tail-handle drag state (arrow length adjustment, axis-constrained)
    private bool   _tailDragging;
    private double _tailDragInitialLength;
    private Point  _tailDragStartMouse;

    // Body drag state (translates the arrow's pivot point)
    private bool   _bodyDragging;
    private Point  _bodyDragStartMouse;
    private double _bodyDragStartOffsetX;
    private double _bodyDragStartOffsetY;

    // Element under pointer (used for highlight + arrow targeting)
    private FrameworkElement? _highlightedElement;

    // Description-box voice state
    private SpeechRecognitionService? _descVoiceService;
    private PushToTalkWindow?         _descPttWindow;
    private bool                      _descVoiceStopOnCtrlRelease;
    private readonly CtrlDoubleTapGestureTracker _descPttGesture =
        new(maxTapHoldMs: DescPttMaxTapHoldMs, doubleTapGapMs: DescPttDoubleClickMs);

    // ── Undo / redo ──────────────────────────────────────────────────────────

    private readonly Stack<OverlaySnapshot> _undoStack = new();
    private readonly Stack<OverlaySnapshot> _redoStack = new();

    /// <summary>Snapshot captured at drag-start (MouseDown); pushed to _undoStack on MouseUp.</summary>
    private OverlaySnapshot? _preDragSnapshot;

    /// <summary>When true, PushUndo() and CommitDragUndo() are no-ops (used during RestoreSnapshot).</summary>
    private bool _suppressUndo;

    // Double-tap Ctrl PTT state machine for the description box.
    private const int    DescPttMaxTapHoldMs  = 250;
    private const int    DescPttDoubleClickMs = 350;

    // ────────────────────────────────────────────────────────────────────────
    // Constructor
    // ────────────────────────────────────────────────────────────────────────

    private readonly string _speechRegion;

    internal ScreenshotOverlayWindow(Window mainWindow, string saveDirectory, string themeName, string speechRegion = "")
    {
        _mainWindow    = mainWindow;
        _saveDirectory = saveDirectory;
        _themeName     = string.IsNullOrWhiteSpace(themeName) ? "light" : themeName;
        _speechRegion  = speechRegion;

        // ── Window chrome ────────────────────────────────────────────────────
        Owner           = mainWindow;
        WindowStyle     = WindowStyle.None;
        ResizeMode      = ResizeMode.NoResize;
        ShowInTaskbar   = false;
        AllowsTransparency = true;
        Background      = Brushes.Transparent;
        Topmost         = true;
        ShowActivated   = true;

        SyncBoundsToOwner();

        mainWindow.LocationChanged += Owner_GeometryChanged;
        mainWindow.SizeChanged     += Owner_SizeChanged;

        // Default selection = centered 1/3 of the window
        double selW = mainWindow.ActualWidth  / 3.0;
        double selH = mainWindow.ActualHeight / 3.0;
        _sel = new Rect((mainWindow.ActualWidth - selW) / 2.0, (mainWindow.ActualHeight - selH) / 2.0, selW, selH);

        // ── Canvas ───────────────────────────────────────────────────────────
        _canvas = new Canvas
        {
            Width      = mainWindow.ActualWidth,
            Height     = mainWindow.ActualHeight,
            Background = Brushes.Transparent,   // must be non-null for canvas to receive mouse events
            Focusable  = true
        };

        // ── Dim strips ───────────────────────────────────────────────────────
        _dimTop    = CreateDimRect();
        _dimBottom = CreateDimRect();
        _dimLeft   = CreateDimRect();
        _dimRight  = CreateDimRect();
        _canvas.Children.Add(_dimTop);
        _canvas.Children.Add(_dimBottom);
        _canvas.Children.Add(_dimLeft);
        _canvas.Children.Add(_dimRight);

        // ── Selection border — single 2px rect on the selection edge ────────
        _selBorderRect = new Rectangle
        {
            StrokeThickness  = 2,
            Fill             = Brushes.Transparent,
            IsHitTestVisible = false
        };
        _selBorderRect.SetResourceReference(Shape.StrokeProperty, "DocumentLinkText");
        _canvas.Children.Add(_selBorderRect);

        // ── Annotation-mode mouse-capture rectangle ──────────────────────────
        // Alpha=1/255: invisible to the eye but opaque enough that the OS DirectX
        // compositor routes mouse events to this window instead of the one behind.
        // Must be added before the highlight rects (lower Z) and start Collapsed.
        _selHitRect = new Rectangle
        {
            IsHitTestVisible = true,
            Fill             = new SolidColorBrush(Color.FromArgb(1, 0, 0, 0)),
            Stroke           = null,
            StrokeThickness  = 0,
            Visibility       = Visibility.Collapsed
        };
        Panel.SetZIndex(_selHitRect, 1);
        _canvas.Children.Add(_selHitRect);

        // ── Marching-ants element-highlight — two overlaid 1px dashed rects ──
        // White dashes (offset 0) + black dashes (offset 4) at 50 % opacity each.
        // Both share _highlightClip so they are cropped to the capture selection.
        _highlightWhite = new Rectangle
        {
            IsHitTestVisible = false,
            StrokeThickness  = 1,
            Fill             = Brushes.Transparent,
            Visibility       = Visibility.Collapsed,
            Opacity          = 0.5,
            Stroke           = new SolidColorBrush(Color.FromArgb(255, 255, 255, 255)),
            StrokeDashOffset = 0,
            Clip             = _highlightClip
        };
        _highlightWhite.StrokeDashArray = new DoubleCollection { 4, 4 };
        _canvas.Children.Add(_highlightWhite);

        _highlightBlack = new Rectangle
        {
            IsHitTestVisible = false,
            StrokeThickness  = 1,
            Fill             = Brushes.Transparent,
            Visibility       = Visibility.Collapsed,
            Opacity          = 0.5,
            Stroke           = new SolidColorBrush(Color.FromArgb(255, 0, 0, 0)),
            StrokeDashOffset = 4,
            Clip             = _highlightClip
        };
        _highlightBlack.StrokeDashArray = new DoubleCollection { 4, 4 };
        _canvas.Children.Add(_highlightBlack);

        // ── Resize handles ───────────────────────────────────────────────────
        for (var i = 0; i < 8; i++)
        {
            var h = new Rectangle
            {
                Width           = HandleSize,
                Height          = HandleSize,
                StrokeThickness = 1,
                RadiusX         = 1.5,
                RadiusY         = 1.5
            };
            h.SetResourceReference(Shape.FillProperty,   "DocumentLinkText");
            h.SetResourceReference(Shape.StrokeProperty, "AppSurface");
            _handles[i] = h;
            _canvas.Children.Add(h);
        }

        // ── Instruction pill ─────────────────────────────────────────────────
        var instrText = new TextBlock
        {
            Text             = "Drag handles to adjust region  ·  Enter to capture  ·  Esc to cancel",
            FontSize         = 11,
            Padding          = new Thickness(8, 3, 8, 3),
            IsHitTestVisible = false
        };
        instrText.SetResourceReference(TextBlock.ForegroundProperty, "LabelText");

        var instrPill = new Border
        {
            BorderThickness  = new Thickness(1),
            CornerRadius     = new CornerRadius(4),
            Child            = instrText,
            IsHitTestVisible = false
        };
        instrPill.SetResourceReference(Border.BackgroundProperty, "PopupSurface");
        instrPill.SetResourceReference(Border.BorderBrushProperty, "PopupBorder");
        _canvas.Children.Add(instrPill);

        instrPill.Loaded += (_, _) =>
        {
            Canvas.SetLeft(instrPill, Math.Max(0, (ActualWidth  - instrPill.ActualWidth)  / 2));
            Canvas.SetTop (instrPill, 10);
        };

        // ── Toolbar (Capture + Cancel) ────────────────────────────────────────
        var captureBtn = new Button { Content = "Capture", Width = 84, Height = 28 };
        captureBtn.SetResourceReference(Control.StyleProperty, "ThemedButtonStyle");
        captureBtn.Click += (_, _) => EnterAnnotationMode();

        var cancelBtn = new Button
        {
            Content = "Cancel",
            Width   = 70,
            Height  = 28,
            Margin  = new Thickness(8, 0, 0, 0)
        };
        cancelBtn.SetResourceReference(Control.StyleProperty, "ThemedButtonStyle");
        cancelBtn.Click += (_, _) => Close();

        var toolbarPanel = new StackPanel { Orientation = Orientation.Horizontal };
        toolbarPanel.Children.Add(captureBtn);
        toolbarPanel.Children.Add(cancelBtn);

        _toolbarBorder = new Border
        {
            BorderThickness = new Thickness(1),
            CornerRadius    = new CornerRadius(6),
            Padding         = new Thickness(10, 6, 10, 6),
            Child           = toolbarPanel
        };
        _toolbarBorder.SetResourceReference(Border.BackgroundProperty, "PopupSurface");
        _toolbarBorder.SetResourceReference(Border.BorderBrushProperty, "PopupBorder");
        _canvas.Children.Add(_toolbarBorder);
        Panel.SetZIndex(_toolbarBorder, 200);
        _toolbarBorder.Visibility = Visibility.Collapsed;

        // Toolbar is repositioned via PositionToolbar() on every layout update;
        // the Loaded hook gives us the first measured size.
        _toolbarBorder.Loaded += (_, _) => PositionToolbar();

        // ── Edge-anchor indicator labels (one per selection border edge) ──────
        // Background (#80000000) and foreground (#FFFFFFFF) are intentionally
        // hard-coded and never change with the active theme.
        for (var i = 0; i < 4; i++)
        {
            var labelTb = new TextBlock
            {
                FontSize          = 11,
                FontFamily        = new FontFamily("Segoe UI"),
                Foreground        = new SolidColorBrush(Color.FromArgb(0xFF, 0xFF, 0xFF, 0xFF)),
                VerticalAlignment = VerticalAlignment.Center,
                IsHitTestVisible  = false
            };

            var label = new Border
            {
                Background       = new SolidColorBrush(Color.FromArgb(0x80, 0x00, 0x00, 0x00)),
                CornerRadius     = new CornerRadius(3),
                Padding          = new Thickness(4, 2, 4, 2),
                Child            = labelTb,
                IsHitTestVisible = false
            };
            _anchorLabels[i] = label;
            _canvas.Children.Add(label);
        }

        // ── Unified annotation toolbar (Name · Buttons · Description) ─────────
        // Shown immediately when the user clicks Capture; stays visible through save.

        // Row 0 — Name
        var nameLabel = new TextBlock
        {
            Text              = "Name:",
            FontSize          = 12,
            VerticalAlignment = VerticalAlignment.Center,
            Margin            = new Thickness(0, 0, 6, 0)
        };
        nameLabel.SetResourceReference(TextBlock.ForegroundProperty, "LabelText");

        _nameTextBox = new TextBox
        {
            Width                    = 220,
            Height                   = 26,
            FontSize                 = 12,
            IsHitTestVisible         = true,
            Focusable                = true,
            VerticalContentAlignment = VerticalAlignment.Center,
            Padding                  = new Thickness(4, 0, 4, 0),
            BorderThickness          = new Thickness(1.5)
        };
        _nameTextBox.SetResourceReference(TextBox.BackgroundProperty,  "PopupSurface");
        _nameTextBox.SetResourceReference(TextBox.BorderBrushProperty, "PopupBorder");
        _nameTextBox.SetResourceReference(TextBox.ForegroundProperty,  "LabelText");
        _nameTextBox.SetResourceReference(TextBox.CaretBrushProperty,  "LabelText");

        var nameRow = new StackPanel
        {
            Orientation       = Orientation.Horizontal,
            VerticalAlignment = VerticalAlignment.Center
        };
        nameRow.Children.Add(nameLabel);
        nameRow.Children.Add(_nameTextBox);

        // Row 1 — Buttons
        var cursorBtn = new Button { Content = "⌖ Cursor", Width = 80, Height = 28 };
        cursorBtn.SetResourceReference(Control.StyleProperty, "ThemedButtonStyle");
        cursorBtn.Click += (_, _) =>
        {
            if (!_inAnnotationMode) EnterAnnotationMode();
            _cursorEnabled = !_cursorEnabled;
            if (_cursorEnabled)
            {
                _inCursorPlacementMode = true;
                cursorBtn.Content = "✓ ⌖ Cursor";
                ShowModeHint("Click the element to place the cursor");
            }
            else
            {
                _inCursorPlacementMode = false;
                cursorBtn.Content = "⌖ Cursor";
                PushUndo();
                ToggleCursorOverlay(false);
                HideModeHint();
                CollapseHighlight();
            }
        };

        var space4Btn = new Button { Content = "Space 4px", Width = 80, Height = 28, Margin = new Thickness(4, 0, 0, 0) };
        space4Btn.SetResourceReference(Control.StyleProperty, "ThemedButtonStyle");
        space4Btn.Click += (_, _) =>
        {
            if (!_inAnnotationMode) EnterAnnotationMode();
            RunSpace4px();
        };

        var addArrowBtn = new Button { Content = "↗ Arrow", Width = 72, Height = 28, Margin = new Thickness(4, 0, 0, 0) };
        addArrowBtn.SetResourceReference(Control.StyleProperty, "ThemedButtonStyle");
        _addArrowBtn = addArrowBtn;
        addArrowBtn.Click += (_, _) =>
        {
            if (!_inAnnotationMode) EnterAnnotationMode();
            bool shiftHeld = (Keyboard.Modifiers & ModifierKeys.Shift) != 0;

            if (_inArrowTargetMode)
            {
                // Clicking while already in target mode exits it (and multi-drop if active)
                _multiDropArrowMode = false;
                ExitArrowTargetMode();
                return;
            }

            _multiDropArrowMode = shiftHeld;
            EnterArrowTargetMode();
            addArrowBtn.Content = shiftHeld ? "✦ ↗ Arrow" : "✓ ↗ Arrow";
            if (shiftHeld)
                ShowModeHint("Shift-drop mode: click elements to drop arrows (Esc to stop)");
        };

        var annotSaveBtn = new Button { Content = "Capture", Width = 80, Height = 28, Margin = new Thickness(4, 0, 0, 0) };
        annotSaveBtn.SetResourceReference(Control.StyleProperty, "ThemedButtonStyle");
        annotSaveBtn.Click += async (_, _) =>
        {
            if (!_inAnnotationMode) EnterAnnotationMode();
            await DoAnnotationSaveAsync();
        };

        var annotCancelBtn = new Button { Content = "Cancel", Width = 70, Height = 28, Margin = new Thickness(4, 0, 0, 0) };
        annotCancelBtn.SetResourceReference(Control.StyleProperty, "ThemedButtonStyle");
        annotCancelBtn.Click += (_, _) => Close();

        var annotButtonRow = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Margin      = new Thickness(0, 6, 0, 0)
        };
        annotButtonRow.Children.Add(cursorBtn);
        annotButtonRow.Children.Add(addArrowBtn);
        annotButtonRow.Children.Add(space4Btn);
        annotButtonRow.Children.Add(annotSaveBtn);
        annotButtonRow.Children.Add(annotCancelBtn);

        // Row 2 — Description
        _descriptionBox = new TextBox
        {
            AcceptsReturn                = true,
            TextWrapping                 = TextWrapping.Wrap,
            Height                       = 56,          // ~3 lines at FontSize 12
            FontSize                     = 12,
            Margin                       = new Thickness(0, 6, 0, 0),
            Padding                      = new Thickness(4, 3, 4, 3),
            BorderThickness              = new Thickness(1.5),
            VerticalScrollBarVisibility  = ScrollBarVisibility.Auto,
            HorizontalAlignment          = HorizontalAlignment.Stretch
        };
        _descriptionBox.SetResourceReference(TextBox.BackgroundProperty,  "PopupSurface");
        _descriptionBox.SetResourceReference(TextBox.BorderBrushProperty, "PopupBorder");
        _descriptionBox.SetResourceReference(TextBox.ForegroundProperty,  "LabelText");
        _descriptionBox.SetResourceReference(TextBox.CaretBrushProperty,  "LabelText");

        // Placeholder via opacity swap (no native WPF TextBox placeholder)
        _descriptionBox.Text    = _descriptionPlaceholder;
        _descriptionBox.Opacity = 0.45;
        _descriptionBox.GotFocus += (_, _) =>
        {
            if (_descriptionBox.Text == _descriptionPlaceholder)
            {
                _descriptionBox.Text    = "";
                _descriptionBox.Opacity = 1.0;
            }

            ActivateOverlayForKeyboard(_descriptionBox, "DescriptionBox.GotFocus");
            // Bug 2 fix: hide all arrow handles while user is typing in the description box
            foreach (var a in _arrows)
            {
                a.TipHandle.Visibility  = Visibility.Hidden;
                a.TailHandle.Visibility = Visibility.Hidden;
            }
            HideColorPicker();
        };
        _descriptionBox.LostFocus += (_, _) =>
        {
            if (string.IsNullOrWhiteSpace(_descriptionBox.Text))
            {
                _descriptionBox.Text    = _descriptionPlaceholder;
                _descriptionBox.Opacity = 0.45;
            }
        };

        // Grid: 3 rows — name, buttons, description
        var annotGrid = new Grid();
        annotGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        annotGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        annotGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        Grid.SetRow(nameRow,        0);
        Grid.SetRow(annotButtonRow, 1);
        annotGrid.Children.Add(nameRow);
        annotGrid.Children.Add(annotButtonRow);

        // Row 2 — description box (full width; voice triggered by double-tap Ctrl)
        var descRow = new Grid();
        descRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        descRow.Children.Add(_descriptionBox);

        Grid.SetRow(descRow, 2);
        annotGrid.Children.Add(descRow);

        _editBorder = new Border
        {
            BorderThickness = new Thickness(1),
            CornerRadius    = new CornerRadius(6),
            Padding         = new Thickness(10, 6, 10, 6),
            Child           = annotGrid,
            Visibility      = Visibility.Visible,
            MinWidth        = 420
        };
        _editBorder.SetResourceReference(Border.BackgroundProperty,  "PopupSurface");
        _editBorder.SetResourceReference(Border.BorderBrushProperty, "PopupBorder");
        _canvas.Children.Add(_editBorder);
        Panel.SetZIndex(_editBorder, 200);

        _editBorder.SizeChanged += (_, _) => PositionToolbar();

        Content = _canvas;

        // ── Events ───────────────────────────────────────────────────────────
        _canvas.MouseDown += Canvas_MouseDown;
        _canvas.MouseMove += Canvas_MouseMove;
        _canvas.MouseUp   += Canvas_MouseUp;
        KeyDown           += Overlay_KeyDown;
        PreviewKeyDown    += Overlay_PreviewKeyDown;
        PreviewKeyUp      += Overlay_PreviewKeyUp;
        InputManager.Current.PreProcessInput += Overlay_PreProcessInput;

        // Load persisted arrow defaults before the window opens
        LoadArrowDefaults();

        Loaded += (_, _) =>
        {
            ActivateOverlayForKeyboard(_canvas, "Overlay.Loaded");
            UpdateLayout();
        };
    }

    // ── Owner geometry sync ──────────────────────────────────────────────────

    private void SyncBoundsToOwner()
    {
        // When _mainWindow is maximized, Window.Left/Top return the restore (non-maximized)
        // position rather than the actual screen position.  Use GetWindowRect via NativeMethods
        // to get the true screen bounds regardless of window state.
        var bounds = NativeMethods.GetActualWindowBoundsLogical(_mainWindow);
        Left   = bounds.Left;
        Top    = bounds.Top;
        Width  = bounds.Width;
        Height = bounds.Height;
    }

    private void Owner_GeometryChanged(object? sender, EventArgs e) => SyncBoundsToOwner();

    private void Owner_SizeChanged(object? sender, SizeChangedEventArgs e)
    {
        var bounds     = NativeMethods.GetActualWindowBoundsLogical(_mainWindow);
        Left           = bounds.Left;
        Top            = bounds.Top;
        Width          = bounds.Width;
        Height         = bounds.Height;
        _canvas.Width  = bounds.Width;
        _canvas.Height = bounds.Height;
        _sel           = ClampRect(_sel, _canvas.Width, _canvas.Height);
        UpdateLayout();
    }

    // ── Visual layout ────────────────────────────────────────────────────────

    /// <summary>
    /// Repaints the four dim strips, the selection border, and all 8 resize handles
    /// to reflect the current <see cref="_sel"/> value.
    /// </summary>
    private new void UpdateLayout()
    {
        var w = _canvas.Width;
        var h = _canvas.Height;
        var s = _sel;

        // Four dimming strips that surround the selection
        PlaceRect(_dimTop,    0,       0,       w,           s.Top);
        PlaceRect(_dimBottom, 0,       s.Bottom, w,           h - s.Bottom);
        PlaceRect(_dimLeft,   0,       s.Top,   s.Left,      s.Height);
        PlaceRect(_dimRight,  s.Right, s.Top,   w - s.Right, s.Height);

        // Selection border — single 2px border on the selection edge
        Canvas.SetLeft(_selBorderRect, s.Left);
        Canvas.SetTop (_selBorderRect, s.Top);
        _selBorderRect.Width  = s.Width;
        _selBorderRect.Height = s.Height;

        // Handles: NW N NE E SE S SW W
        var cx = s.Left + s.Width  / 2;
        var cy = s.Top  + s.Height / 2;
        var hh = HandleSize / 2;

        PlaceHandle(0, s.Left  - hh, s.Top    - hh); // NW
        PlaceHandle(1, cx      - hh, s.Top    - hh); // N
        PlaceHandle(2, s.Right - hh, s.Top    - hh); // NE
        PlaceHandle(3, s.Right - hh, cy       - hh); // E
        PlaceHandle(4, s.Right - hh, s.Bottom - hh); // SE
        PlaceHandle(5, cx      - hh, s.Bottom - hh); // S
        PlaceHandle(6, s.Left  - hh, s.Bottom - hh); // SW
        PlaceHandle(7, s.Left  - hh, cy       - hh); // W

        PositionToolbar();
        UpdateAnchorIndicators();
        if (_modeHintBorder?.Visibility == Visibility.Visible)
            PositionModeHint();
        PositionSelHitRect();
    }

    private static void PlaceRect(Rectangle r, double left, double top, double width, double height)
    {
        Canvas.SetLeft(r, left);
        Canvas.SetTop (r, top);
        r.Width  = Math.Max(0, width);
        r.Height = Math.Max(0, height);
    }

    private void PlaceHandle(int i, double left, double top)
    {
        Canvas.SetLeft(_handles[i], left);
        Canvas.SetTop (_handles[i], top);
    }

    // ── Toolbar floating position ────────────────────────────────────────────

    /// <summary>
    /// Moves the active toolbar (<see cref="_renameBorder"/> in rename mode,
    /// <see cref="_toolbarBorder"/> otherwise) to float outside the selection
    /// when space allows, or falls back inside the selection.
    ///
    /// All math is in logical units; canvas children are placed in logical
    /// pixels, so no <c>VisualTreeHelper.GetDpi</c> conversion is needed here —
    /// DPI scaling is only applied at capture time.
    /// </summary>
    private void PositionToolbar()
    {
        var active = (FrameworkElement)_editBorder;

        if (active == null || !active.IsLoaded || active.ActualWidth <= 0)
            return;

        var tbW     = active.ActualWidth;
        var tbH     = active.ActualHeight;
        var s       = _sel;
        var canvasW = _canvas.Width;
        var canvasH = _canvas.Height;

        double tbLeft, tbTop;

        if (_sel.Width < 10 || _sel.Height < 10)
        {
            // No real selection yet — place at top-right corner of canvas
            tbLeft = Math.Max(0, canvasW - tbW - 12);
            tbTop  = 12;
        }
        else
        {
            var spaceBelow = canvasH - s.Bottom;
            var spaceAbove = s.Top;

            if (spaceBelow >= ToolbarThreshold)
            {
                // Preferred: below the selection, centered on its horizontal midpoint
                tbLeft = s.Left + (s.Width - tbW) / 2;
                tbTop  = s.Bottom + ToolbarMargin;
            }
            else if (spaceAbove >= ToolbarThreshold)
            {
                // Fallback: above the selection, centered on its horizontal midpoint
                tbLeft = s.Left + (s.Width - tbW) / 2;
                tbTop  = s.Top - tbH - ToolbarMargin;
            }
            else
            {
                // Last resort: inside the selection, bottom-right area (clear of dim)
                tbLeft = s.Right  - tbW - ToolbarMargin;
                tbTop  = s.Bottom - tbH - ToolbarMargin;
            }
        }

        // Clamp so the toolbar never leaves the canvas
        tbLeft = Math.Max(0, Math.Min(tbLeft, canvasW - tbW));
        tbTop  = Math.Max(0, Math.Min(tbTop,  canvasH - tbH));

        Canvas.SetLeft(active, tbLeft);
        Canvas.SetTop (active, tbTop);
    }

    // ── Edge-anchor indicators (dots + labels) ───────────────────────────────

    /// <summary>
    /// Runs <see cref="VisualTreeEdgeAnalyzer.Analyze"/> for the current
    /// selection and updates both the four border-edge dots and the four
    /// edge-anchor labels.  Everything is hidden while in rename mode.
    /// </summary>
    private void UpdateAnchorIndicators()
    {
        // In annotation mode, hide all indicators — they distract.
        if (_inAnnotationMode)
        {
            for (var i = 0; i < 4; i++)
                _anchorLabels[i].Visibility = Visibility.Collapsed;
            return;
        }

        var anchors = VisualTreeEdgeAnalyzer.Analyze(_sel, _mainWindow);
        var s       = _sel;

        // Midpoints of the four selection border edges — [0] Top  [1] Right  [2] Bottom  [3] Left

        for (var i = 0; i < 4; i++)
        {
            var anchor = anchors[i];

            // ── Label ─────────────────────────────────────────────────────────
            var label = _anchorLabels[i];
            label.Visibility = Visibility.Visible;

            string labelText = anchor.Element is null
                ? "X"
                : anchor.NeedsName
                    ? $"? - {anchor.DistanceToEdge:F0}px"
                    : $"{string.Join(", ", anchor.UniqueNames)} - {anchor.DistanceToEdge:F0}px";

            ((TextBlock)label.Child).Text = labelText;

            // Measure label before positioning so DesiredSize is valid.
            label.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            var labelW = label.DesiredSize.Width;
            var labelH = label.DesiredSize.Height;

            double labelX, labelY;
            switch (i)
            {
                case 0: // Top edge — prefer above, fall back inside
                    labelX = s.Left + s.Width / 2 - labelW / 2;
                    labelY = s.Top >= labelH + 6
                        ? s.Top - labelH - 4
                        : s.Top + 6;
                    break;

                case 2: // Bottom edge — prefer below, fall back inside
                    labelX = s.Left + s.Width / 2 - labelW / 2;
                    labelY = _canvas.Height - (s.Top + s.Height) >= labelH + 6
                        ? s.Top + s.Height + 4
                        : s.Top + s.Height - labelH - 6;
                    break;

                case 3: // Left edge — prefer to the left, fall back inside
                    labelX = s.Left >= labelW + 6
                        ? s.Left - labelW - 4
                        : s.Left + 6;
                    labelY = s.Top + s.Height / 2 - labelH / 2;
                    break;

                default: // case 1: Right edge — prefer to the right, fall back inside
                    labelX = _canvas.Width - (s.Left + s.Width) >= labelW + 6
                        ? s.Left + s.Width + 4
                        : s.Left + s.Width - labelW - 6;
                    labelY = s.Top + s.Height / 2 - labelH / 2;
                    break;
            }

            Canvas.SetLeft(label, labelX);
            Canvas.SetTop (label, labelY);
        }
    }

    // ── Mouse ────────────────────────────────────────────────────────────────

    private void Canvas_MouseDown(object sender, MouseButtonEventArgs e)
    {
        if (_inAnnotationMode)
        {
            if (e.LeftButton == MouseButtonState.Pressed)
            {
                SelectArrow(null);
                var clickPt = e.GetPosition(_canvas);

                // Check for resize-zone hit FIRST — allow resizing even in annotation mode
                var zone = HitTest(clickPt);
                if (zone == HitZone.NW || zone == HitZone.N  || zone == HitZone.NE ||
                    zone == HitZone.E  || zone == HitZone.SE || zone == HitZone.S  ||
                    zone == HitZone.SW || zone == HitZone.W)
                {
                    // Resize: fall through to normal resize handling below
                    _activeZone   = zone;
                    _dragStart    = clickPt;
                    _dragOriginal = _sel;
                    _preDragSnapshot = CaptureSnapshot();
                    _canvas.CaptureMouse();
                    e.Handled = true;
                    return;
                }

                if (!_sel.Contains(clickPt))
                {
                    // Start a new rubber-band selection draw
                    _activeZone = HitZone.Draw;
                    _dragStart  = clickPt;
                    _preDragSnapshot = CaptureSnapshot();
                    _canvas.CaptureMouse();
                    Cursor = Cursors.Cross;
                    e.Handled = true;
                    return;
                }
                if (_inCursorPlacementMode)
                {
                    PlaceCursorAtPoint(clickPt);
                    e.Handled = true;
                }
                else if (_inArrowTargetMode && _highlightedElement != null)
                {
                    LockArrowTarget();
                    e.Handled = true;
                }
                else
                {
                    // Click inside selection with no special mode active — start a move drag
                    _activeZone   = HitZone.Move;
                    _dragStart    = clickPt;
                    _dragOriginal = _sel;
                    _preDragSnapshot = CaptureSnapshot();
                    _canvas.CaptureMouse();
                    Cursor = Cursors.SizeAll;
                    e.Handled = true;
                }
            }
            return;
        }

        if (e.LeftButton != MouseButtonState.Pressed)
            return;

        var pt = e.GetPosition(_canvas);
        _activeZone   = HitTest(pt);
        if (_activeZone == HitZone.None)
            _activeZone = HitZone.Draw;   // click outside selection starts a new rubber-band draw
        _dragStart    = pt;
        _dragOriginal = _sel;
        _preDragSnapshot = CaptureSnapshot();
        if (_activeZone == HitZone.Draw)
            Cursor = Cursors.Cross;
        _canvas.CaptureMouse();
        e.Handled = true;
    }

    private void Canvas_MouseMove(object sender, MouseEventArgs e)
    {
        if (_inAnnotationMode)
        {
            // 1a. Active rubber-band draw (click outside selection) — handle before resize
            if (_activeZone == HitZone.Draw)
            {
                var drawPt = e.GetPosition(_canvas);
                _sel = ClampRect(NormalizeRect(_dragStart, drawPt), _canvas.Width, _canvas.Height);
                PositionSelHitRect();
                UpdateLayout();
                e.Handled = true;
                return;
            }

            // 1b. Active resize drag (set up in Canvas_MouseDown) — handle it first
            if (_activeZone != HitZone.None)
            {
                var annPt = e.GetPosition(_canvas);
                var annDx = annPt.X - _dragStart.X;
                var annDy = annPt.Y - _dragStart.Y;
                var annW  = _canvas.Width;
                var annH  = _canvas.Height;
                _sel = _activeZone switch
                {
                    HitZone.Move => ClampRect(new Rect(_dragOriginal.X + annDx, _dragOriginal.Y + annDy, _dragOriginal.Width, _dragOriginal.Height), annW, annH),
                    HitZone.NW => ApplyEdges(_dragOriginal, annDx, annDy, Edge.Left | Edge.Top,             annW, annH),
                    HitZone.N  => ApplyEdges(_dragOriginal, annDx, annDy, Edge.Top,                          annW, annH),
                    HitZone.NE => ApplyEdges(_dragOriginal, annDx, annDy, Edge.Right | Edge.Top,             annW, annH),
                    HitZone.E  => ApplyEdges(_dragOriginal, annDx, annDy, Edge.Right,                        annW, annH),
                    HitZone.SE => ApplyEdges(_dragOriginal, annDx, annDy, Edge.Right | Edge.Bottom,          annW, annH),
                    HitZone.S  => ApplyEdges(_dragOriginal, annDx, annDy, Edge.Bottom,                       annW, annH),
                    HitZone.SW => ApplyEdges(_dragOriginal, annDx, annDy, Edge.Left | Edge.Bottom,           annW, annH),
                    HitZone.W  => ApplyEdges(_dragOriginal, annDx, annDy, Edge.Left,                         annW, annH),
                    _          => _sel
                };
                PositionSelHitRect();  // keep the mouse-capture rect in sync with selection
                UpdateLayout();
                e.Handled = true;
                return;
            }

            // 2. Cursor-image and tip-handle drags are handled by their own captured-mouse
            // events.  Outside of those, keep the highlight rect up to date and show the
            // correct resize cursor near handles.
            if (!_draggingCursor && _draggingArrow == null && !_bodyDragging)
            {
                var mousePt = e.GetPosition(_canvas);

                // Show resize cursor when hovering near handles (even in annotation mode)
                var hoverZone = HitTest(mousePt);
                if (hoverZone != HitZone.None && hoverZone != HitZone.Move)
                {
                    Cursor = ZoneCursor(hoverZone);
                    CollapseHighlight();
                    return;
                }

                // Normal annotation mode hovering
                if (_inArrowTargetMode || _inCursorPlacementMode)
                {
                    Cursor = Cursors.Arrow;
                    UpdateHighlightForPoint(mousePt);
                    if (_inCursorPlacementMode)
                        MoveCursorImageToPoint(mousePt);   // Fix 5: live cursor preview
                }
                else if (hoverZone == HitZone.Move)
                {
                    Cursor = Cursors.SizeAll;   // hovering inside selection — ready to move
                    CollapseHighlight();
                }
                else
                {
                    Cursor = Cursors.Arrow;
                    CollapseHighlight();   // not in a targeting mode — keep highlight hidden
                }
            }
            return;
        }

        var pt = e.GetPosition(_canvas);

        if (_activeZone == HitZone.None)
        {
            Cursor = ZoneCursor(HitTest(pt));
            return;
        }

        var dx = pt.X - _dragStart.X;
        var dy = pt.Y - _dragStart.Y;
        var w  = _canvas.Width;
        var h  = _canvas.Height;

        _sel = _activeZone switch
        {
            HitZone.Draw => ClampRect(NormalizeRect(_dragStart, pt), w, h),
            HitZone.Move => ClampRect(new Rect(_dragOriginal.X + dx, _dragOriginal.Y + dy, _dragOriginal.Width, _dragOriginal.Height), w, h),
            HitZone.NW   => ApplyEdges(_dragOriginal, dx, dy, Edge.Left | Edge.Top,             w, h),
            HitZone.N    => ApplyEdges(_dragOriginal, dx, dy, Edge.Top,                          w, h),
            HitZone.NE   => ApplyEdges(_dragOriginal, dx, dy, Edge.Right | Edge.Top,             w, h),
            HitZone.E    => ApplyEdges(_dragOriginal, dx, dy, Edge.Right,                        w, h),
            HitZone.SE   => ApplyEdges(_dragOriginal, dx, dy, Edge.Right | Edge.Bottom,          w, h),
            HitZone.S    => ApplyEdges(_dragOriginal, dx, dy, Edge.Bottom,                       w, h),
            HitZone.SW   => ApplyEdges(_dragOriginal, dx, dy, Edge.Left | Edge.Bottom,           w, h),
            HitZone.W    => ApplyEdges(_dragOriginal, dx, dy, Edge.Left,                         w, h),
            _            => _sel
        };

        UpdateLayout();
        e.Handled = true;
    }

    private void Canvas_MouseUp(object sender, MouseButtonEventArgs e)
    {
        // Release resize drag (works in both normal and annotation modes)
        if (_activeZone != HitZone.None)
        {
            CommitDragUndo();
            _canvas.ReleaseMouseCapture();
            _activeZone = HitZone.None;
            // In annotation mode, update the captured selection snapshot so the
            // name suggestion and save logic use the current (post-resize) selection
            if (_inAnnotationMode)
            {
                _capturedSel     = _sel;
                _capturedAnchors = VisualTreeEdgeAnalyzer.Analyze(_sel, _mainWindow);
                UpdateAnchorIndicators();
                PositionSelHitRect();
            }
            Cursor = ZoneCursor(HitTest(e.GetPosition(_canvas)));
            e.Handled = true;
            return;
        }
        if (_inAnnotationMode) return;   // per-element capture handles annotation drags
        e.Handled = true;
    }

    // ── Hit testing ──────────────────────────────────────────────────────────

    private HitZone HitTest(Point pt)
    {
        var s  = _sel;
        var cx = s.Left + s.Width  / 2;
        var cy = s.Top  + s.Height / 2;

        // Corners first (larger priority zone)
        if (InHandleZone(pt, s.Left,  s.Top))    return HitZone.NW;
        if (InHandleZone(pt, s.Right, s.Top))    return HitZone.NE;
        if (InHandleZone(pt, s.Right, s.Bottom)) return HitZone.SE;
        if (InHandleZone(pt, s.Left,  s.Bottom)) return HitZone.SW;

        // Edge midpoints
        if (InHandleZone(pt, cx,      s.Top))    return HitZone.N;
        if (InHandleZone(pt, s.Right, cy))       return HitZone.E;
        if (InHandleZone(pt, cx,      s.Bottom)) return HitZone.S;
        if (InHandleZone(pt, s.Left,  cy))       return HitZone.W;

        // Edge-band hit zones: anywhere along a selection edge within ±8px
        // gives that edge's resize cursor, covering the full 8px visual border.
        const double EdgeBand = 8.0;
        if (pt.Y >= s.Top    - EdgeBand && pt.Y <= s.Top    + EdgeBand && pt.X > s.Left && pt.X < s.Right) return HitZone.N;
        if (pt.Y >= s.Bottom - EdgeBand && pt.Y <= s.Bottom + EdgeBand && pt.X > s.Left && pt.X < s.Right) return HitZone.S;
        if (pt.X >= s.Left   - EdgeBand && pt.X <= s.Left   + EdgeBand && pt.Y > s.Top  && pt.Y < s.Bottom) return HitZone.W;
        if (pt.X >= s.Right  - EdgeBand && pt.X <= s.Right  + EdgeBand && pt.Y > s.Top  && pt.Y < s.Bottom) return HitZone.E;

        // Interior
        if (s.Contains(pt))
            return HitZone.Move;

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
        HitZone.N  or HitZone.S  => Cursors.SizeNS,
        HitZone.E  or HitZone.W  => Cursors.SizeWE,
        HitZone.Move             => Cursors.SizeAll,
        _                        => Cursors.Arrow
    };

    // ── Resize math ──────────────────────────────────────────────────────────

    [Flags]
    private enum Edge { Left = 1, Top = 2, Right = 4, Bottom = 8 }

    private static Rect ApplyEdges(Rect orig, double dx, double dy, Edge edges, double maxW, double maxH)
    {
        var l = orig.Left;
        var t = orig.Top;
        var r = orig.Right;
        var b = orig.Bottom;

        if ((edges & Edge.Left)   != 0) l = Math.Min(l + dx, r - MinSize);
        if ((edges & Edge.Top)    != 0) t = Math.Min(t + dy, b - MinSize);
        if ((edges & Edge.Right)  != 0) r = Math.Max(r + dx, l + MinSize);
        if ((edges & Edge.Bottom) != 0) b = Math.Max(b + dy, t + MinSize);

        // Clamp to canvas
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
        var t = Math.Max(0, Math.Min(rect.Top,  maxH - rect.Height));
        return new Rect(l, t,
            Math.Min(rect.Width,  maxW),
            Math.Min(rect.Height, maxH));
    }

    private static Rect NormalizeRect(Point p1, Point p2) =>
        new Rect(Math.Min(p1.X, p2.X), Math.Min(p1.Y, p2.Y),
                 Math.Abs(p2.X - p1.X), Math.Abs(p2.Y - p1.Y));

    // ── Keyboard ─────────────────────────────────────────────────────────────

    private void Overlay_KeyDown(object sender, KeyEventArgs e)
    {
        if (_inAnnotationMode)
        {
            if (e.Key == Key.Escape)
            {
                if (_inArrowTargetMode && _multiDropArrowMode)
                {
                    // First Escape exits multi-drop mode; stays in annotation mode
                    _multiDropArrowMode = false;
                    ExitArrowTargetMode();
                }
                else if (_inArrowTargetMode)
                    ExitArrowTargetMode();   // Esc cancels arrow-target mode; second Esc closes
                else if (_inCursorPlacementMode)
                {
                    _inCursorPlacementMode = false;
                    _cursorEnabled        = false;
                    HideModeHint();
                }
                else
                    Close();
                e.Handled = true;
            }
            return;
        }
        // Selection mode — Enter captures, Esc cancels
        if (e.Key == Key.Escape)
        { Close(); e.Handled = true; }
        else if (e.Key is Key.Return or Key.Enter)
        { EnterAnnotationMode(); e.Handled = true; }
    }

    // ── Annotation flow — entry ──────────────────────────────────────────────

    /// <summary>
    /// Called when the user clicks "Capture" or presses Enter in selection mode.
    /// Snapshots the selection, analyses the visual tree, suggests a name, then
    /// shows the unified annotation panel (name + buttons + description) so the
    /// user can annotate and save in one step.
    /// </summary>
    private void EnterAnnotationMode()
    {
        if (_inAnnotationMode) return;   // already active — idempotent

        // Snapshot selection + run analyzer while the visual tree is unchanged.
        _capturedSel     = _sel;
        _capturedAnchors = VisualTreeEdgeAnalyzer.Analyze(_capturedSel, _mainWindow);
        _capturedIsFullWindow =
            _capturedSel.Left  < 1 &&
            _capturedSel.Top   < 1 &&
            Math.Abs(_capturedSel.Width  - _mainWindow.ActualWidth)  < 1 &&
            Math.Abs(_capturedSel.Height - _mainWindow.ActualHeight) < 1;

        // Convert EdgeAnchor[] → EdgeAnchorRecord[] for SuggestName.
        var anchorRecords = _capturedAnchors
            .Select(a => new Screenshots.EdgeAnchorRecord(
                Edge:           a.Edge,
                ElementNames:   a.UniqueNames,
                NeedsName:      a.NeedsName,
                ElementLeft:    a.ElementBounds.Left,
                ElementTop:     a.ElementBounds.Top,
                ElementWidth:   a.ElementBounds.Width,
                ElementHeight:  a.ElementBounds.Height,
                DistanceToEdge: a.DistanceToEdge))
            .ToArray();

        if (_nameTextBox != null)
            _nameTextBox.Text = Screenshots.ScreenshotNamingHelper.SuggestName(_themeName, anchorRecords);

        _inAnnotationMode = true;

        PositionSelHitRect();   // size the rect immediately (UpdateLayout hasn't run yet)

        // Show unified panel, hide capture toolbar
        _toolbarBorder.Visibility = Visibility.Collapsed;
        _editBorder.Visibility    = Visibility.Visible;

        // Hide anchor indicator labels
        UpdateAnchorIndicators();

        // Position panel
        Dispatcher.InvokeAsync(PositionToolbar, DispatcherPriority.Loaded);

        // Enable keyboard + focus name field
        IsHitTestVisible = true;
        Focusable        = true;
        ActivateOverlayForKeyboard(_nameTextBox, "EnterAnnotationMode");
        _nameTextBox?.SelectAll();
    }

    /// <summary>Kebab-case rule: one or more lowercase-alphanumeric segments joined by hyphens.</summary>
    private static bool IsValidKebabName(string name) =>
        !string.IsNullOrEmpty(name) && _kebabRegex.IsMatch(name);

    private static readonly Regex _kebabRegex =
        new(@"^[a-z0-9]+(-[a-z0-9]+)*$", RegexOptions.Compiled);

    private const string _descriptionPlaceholder = "Description (double-tap Ctrl for voice)";

    // ── Cleanup ──────────────────────────────────────────────────────────────

    protected override void OnClosed(EventArgs e)
    {
        _ = StopDescVoiceAsync();
        InputManager.Current.PreProcessInput -= Overlay_PreProcessInput;
        base.OnClosed(e);
        _mainWindow.LocationChanged -= Owner_GeometryChanged;
        _mainWindow.SizeChanged     -= Owner_SizeChanged;
    }

    // ── Description-box voice input ──────────────────────────────────────────

    private async Task StartDescVoiceAsync()
    {
        var key    = Environment.GetEnvironmentVariable("SQUAD_SPEECH_KEY",    EnvironmentVariableTarget.User);
        var region = string.IsNullOrWhiteSpace(_speechRegion)
            ? (Environment.GetEnvironmentVariable("SQUAD_SPEECH_REGION", EnvironmentVariableTarget.User)
               ?? Environment.GetEnvironmentVariable("SQUAD_SPEECH_REGION", EnvironmentVariableTarget.Machine)
               ?? Environment.GetEnvironmentVariable("SQUAD_SPEECH_REGION"))
            : _speechRegion;
        if (string.IsNullOrWhiteSpace(key) || string.IsNullOrWhiteSpace(region))
        {
            SquadDashTrace.Write(
                "OverlayVoice",
                $"StartDescVoiceAsync skipped: missing speech config keyPresent={!string.IsNullOrWhiteSpace(key)} regionPresent={!string.IsNullOrWhiteSpace(region)}");
            return;
        }

        SquadDashTrace.Write(
            "OverlayVoice",
            $"StartDescVoiceAsync starting: overlayActive={IsActive} focusWithin={IsKeyboardFocusWithin} focusedElement={DescribeFocusedElement()}");

        _descVoiceStopOnCtrlRelease = true;
        _descVoiceService = new SpeechRecognitionService();

        _descVoiceService.PhraseRecognized += (_, text) =>
            Dispatcher.BeginInvoke(() =>
            {
                if (_descriptionBox == null) return;
                var current = _descriptionBox.Text == _descriptionPlaceholder ? "" : _descriptionBox.Text;
                var processed = VoiceInsertionHeuristics.Apply(current, text);
                _descriptionBox.Text       = (current.Length > 0 ? current + " " : "") + processed;
                _descriptionBox.Opacity    = 1.0;
                _descriptionBox.CaretIndex = _descriptionBox.Text.Length;
            });

        _descVoiceService.VolumeChanged += (_, level) =>
            Dispatcher.BeginInvoke(() =>
            {
                if (_descPttWindow is not null)
                    _descPttWindow.VolumeBar.Height = Math.Max(2, level * 36);
            });

        _descVoiceService.RecognitionError += (_, _) =>
            Dispatcher.BeginInvoke(() => _ = StopDescVoiceAsync());

        await Dispatcher.InvokeAsync(() =>
        {
            _descPttWindow = new PushToTalkWindow(this, showHint: false);
            if (_descriptionBox is not null)
            {
                var pt = _descriptionBox.PointToScreen(new System.Windows.Point(0, -40));
                pt = DpiHelper.PhysicalToLogical(_descriptionBox, pt);
                _descPttWindow.Left = pt.X;
                _descPttWindow.Top  = pt.Y;
            }
            _descPttWindow.Show();
            _descriptionBox?.Focus();   // Fix 1: keep description box focused during voice input
        });

        try
        {
            await _descVoiceService.StartAsync(key, region).ConfigureAwait(false);
            SquadDashTrace.Write("OverlayVoice", "StartDescVoiceAsync: speech recognition started");
        }
        catch (Exception ex)
        {
            await Dispatcher.InvokeAsync(() =>
            {
                _descPttWindow?.Close();
                _descPttWindow = null;
            });
            SquadDashTrace.Write("OverlayVoice", $"StartDescVoiceAsync failed: {ex.Message}");
            _descVoiceStopOnCtrlRelease = false;
            _descVoiceService?.Dispose();
            _descVoiceService = null;
        }
    }

    private async Task StopDescVoiceAsync()
    {
        SquadDashTrace.Write("OverlayVoice", "StopDescVoiceAsync stopping");
        await Dispatcher.InvokeAsync(() =>
        {
            _descPttWindow?.Close();
            _descPttWindow = null;
        });
        if (_descVoiceService != null)
        {
            try { await _descVoiceService.StopAsync().ConfigureAwait(false); } catch { }
            _descVoiceService.Dispose();
            _descVoiceService = null;
        }

        _descVoiceStopOnCtrlRelease = false;
        _descPttGesture.Reset();
    }

    private void Overlay_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        // Ctrl+Z — undo
        if (_inAnnotationMode &&
            e.Key == Key.Z &&
            (Keyboard.Modifiers & ModifierKeys.Control) != 0 &&
            (Keyboard.Modifiers & ModifierKeys.Shift) == 0)
        {
            PerformUndo();
            e.Handled = true;
            return;
        }

        // Ctrl+Y or Ctrl+Shift+Z — redo
        if (_inAnnotationMode &&
            ((e.Key == Key.Y && (Keyboard.Modifiers & ModifierKeys.Control) != 0) ||
             (e.Key == Key.Z && (Keyboard.Modifiers & ModifierKeys.Control) != 0 &&
              (Keyboard.Modifiers & ModifierKeys.Shift) != 0)))
        {
            PerformRedo();
            e.Handled = true;
            return;
        }

        // Fix 3: Delete with an arrow selected — tunnel here before any TextBox can eat it
        if (_inAnnotationMode && e.Key == Key.Delete && _selectedArrow != null)
        {
            var toRemove = _selectedArrow;
            _selectedArrow = null;
            HideColorPicker();
            RemoveArrow(toRemove);
            e.Handled = true;
            return;
        }

        HandleDescriptionPttKeyDown(e, "PreviewKeyDown");
    }

    private void Overlay_PreviewKeyUp(object sender, KeyEventArgs e)
    {
        HandleDescriptionPttKeyUp(e, "PreviewKeyUp");
    }

    private void Overlay_PreProcessInput(object? sender, PreProcessInputEventArgs e)
    {
        if (IsActive || !_inAnnotationMode || _descriptionBox is null || !IsVisible)
            return;

        if (e.StagingItem.Input is not KeyEventArgs keyEventArgs)
            return;

        if (keyEventArgs.RoutedEvent == Keyboard.PreviewKeyDownEvent)
        {
            HandleDescriptionPttKeyDown(keyEventArgs, "PreProcessInput");
            return;
        }

        if (keyEventArgs.RoutedEvent == Keyboard.PreviewKeyUpEvent)
            HandleDescriptionPttKeyUp(keyEventArgs, "PreProcessInput");
    }

    private void HandleDescriptionPttKeyDown(KeyEventArgs e, string source)
    {
        if (!_inAnnotationMode || _descriptionBox is null)
            return;

        if (!CtrlDoubleTapGestureTracker.IsCtrlKey(e.Key) && IsIgnorableDescPttNoiseKey(e.Key))
            return;

        var nowUtc = DateTime.UtcNow;
        var stateBefore = _descPttGesture.State;
        var action = _descPttGesture.HandleKeyDown(e.Key, e.IsRepeat, nowUtc);
        if (CtrlDoubleTapGestureTracker.IsCtrlKey(e.Key) ||
            stateBefore != CtrlDoubleTapGestureTracker.GestureState.Idle ||
            _descPttGesture.State != CtrlDoubleTapGestureTracker.GestureState.Idle)
        {
            SquadDashTrace.Write(
                "OverlayVoice",
                $"HandleDescriptionPttKeyDown source={source} key={e.Key} repeat={e.IsRepeat} stateBefore={stateBefore} stateAfter={_descPttGesture.State} overlayActive={IsActive} focusWithin={IsKeyboardFocusWithin} focusedElement={DescribeFocusedElement()}");
        }

        if (action != CtrlDoubleTapGestureAction.Triggered)
            return;

        SquadDashTrace.Write("OverlayVoice", $"Description PTT gesture triggered via {source}");
        if (_descVoiceService is null)
        {
            _descVoiceStopOnCtrlRelease = true;
            _ = StartDescVoiceAsync();
        }
        else
            _ = StopDescVoiceAsync();
        e.Handled = true;
    }

    private void HandleDescriptionPttKeyUp(KeyEventArgs e, string source)
    {
        if (!_inAnnotationMode || _descriptionBox is null)
            return;

        if (!CtrlDoubleTapGestureTracker.IsCtrlKey(e.Key))
            return;

        SquadDashTrace.Write(
            "OverlayVoice",
            $"HandleDescriptionPttKeyUp source={source} key={e.Key} stopOnRelease={_descVoiceStopOnCtrlRelease} serviceActive={_descVoiceService is not null} windowVisible={_descPttWindow is not null}");

        if (_descVoiceStopOnCtrlRelease && (_descVoiceService is not null || _descPttWindow is not null))
        {
            SquadDashTrace.Write(
                "OverlayVoice",
                $"HandleDescriptionPttKeyUp source={source} key={e.Key} stopping active description voice on Ctrl release");
            _ = StopDescVoiceAsync();
            e.Handled = true;
            return;
        }

        var stateBefore = _descPttGesture.State;
        _descPttGesture.HandleKeyUp(e.Key, DateTime.UtcNow);
        SquadDashTrace.Write(
            "OverlayVoice",
            $"HandleDescriptionPttKeyUp source={source} key={e.Key} stateBefore={stateBefore} stateAfter={_descPttGesture.State} overlayActive={IsActive} focusWithin={IsKeyboardFocusWithin} focusedElement={DescribeFocusedElement()}");
    }

    private void ActivateOverlayForKeyboard(IInputElement? focusTarget, string source)
    {
        Dispatcher.BeginInvoke(new Action(() =>
        {
            if (!IsVisible)
                return;

            try
            {
                Activate();
                Focus();
                if (focusTarget is not null)
                    Keyboard.Focus(focusTarget);
                SquadDashTrace.Write(
                    "OverlayVoice",
                    $"ActivateOverlayForKeyboard source={source} overlayActive={IsActive} focusWithin={IsKeyboardFocusWithin} focusedElement={DescribeFocusedElement()}");
            }
            catch (Exception ex)
            {
                SquadDashTrace.Write("OverlayVoice", $"ActivateOverlayForKeyboard failed in {source}: {ex.Message}");
            }
        }), DispatcherPriority.Input);
    }

    private static bool IsIgnorableDescPttNoiseKey(Key key) =>
        key is Key.System or Key.ImeProcessed or Key.DeadCharProcessed or Key.None;

    private string DescribeFocusedElement()
    {
        var focused = Keyboard.FocusedElement;
        return focused switch
        {
            FrameworkElement element when !string.IsNullOrWhiteSpace(element.Name) => $"{element.GetType().Name}({element.Name})",
            FrameworkElement element => element.GetType().Name,
            not null => focused.GetType().Name,
            _ => "(none)"
        };
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    private static Rectangle CreateDimRect()
    {
        var r = new Rectangle { IsHitTestVisible = false };
        r.SetResourceReference(Shape.FillProperty, "OverlayDimFill");
        return r;
    }

    // ── Annotation mode — cursor overlay ─────────────────────────────────────

    /// <summary>
    /// Hides (enabled=false) the cursor overlay image and resets drag state.
    /// The enabled=true path is handled via PlaceCursorAtElement() after the user
    /// clicks a target element in cursor-placement mode.
    /// </summary>
    private void ToggleCursorOverlay(bool enabled)
    {
        _cursorEnabled = enabled;
        if (!enabled)
        {
            if (_cursorImage != null)
                _cursorImage.Visibility = Visibility.Collapsed;
            _draggingCursor = false;
        }
    }

    /// <summary>Creates <see cref="_cursorImage"/> with drag handlers if not yet built.</summary>
    private void EnsureCursorImageCreated()
    {
        if (_cursorImage != null) return;

        _cursorImage = new Image
        {
            Width            = 24,
            Height           = 29,
            Source           = BuildCursorDrawing(),
            Cursor           = Cursors.SizeAll,
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
            var x  = Math.Max(_sel.Left, Math.Min(pt.X, _sel.Right  - 20));
            var y  = Math.Max(_sel.Top,  Math.Min(pt.Y, _sel.Bottom - 24));
            var clampedPt = new Point(x, y);
            Canvas.SetLeft(_cursorImage!, x);
            Canvas.SetTop (_cursorImage!, y);
            UpdateCursorAnchor(clampedPt);
            UpdateHighlightForPoint(clampedPt);   // show named-element highlight during drag
            e.Handled = true;
        };
        _cursorImage.MouseLeftButtonUp += (_, e) =>
        {
            if (!_draggingCursor) return;
            CommitDragUndo();
            _draggingCursor = false;
            _cursorImage!.ReleaseMouseCapture();
            CollapseHighlight();
            e.Handled = true;
        };

        _canvas.Children.Add(_cursorImage);
    }

    /// <summary>
    /// Shows the cursor image at <paramref name="pt"/> during cursor-placement mode —
    /// live preview before the user clicks to finalise.  Updates
    /// <see cref="_cursorLogicalPos"/> so a subsequent click lands at the right spot.
    /// </summary>
    private void MoveCursorImageToPoint(Point pt)
    {
        EnsureCursorImageCreated();
        Canvas.SetLeft(_cursorImage!, pt.X);
        Canvas.SetTop (_cursorImage!, pt.Y);
        _cursorImage!.Visibility = Visibility.Visible;
        UpdateCursorAnchor(pt);
    }

    /// <summary>
    /// Places the cursor image at the centre of <paramref name="element"/>'s bounds
    /// (translated to overlay coords, clamped to <see cref="_sel"/>), then exits
    /// cursor-placement mode and hides the status text.
    /// </summary>
    private void PlaceCursorAtElement(FrameworkElement element)
    {
        Rect bounds;
        try
        {
            bounds = element.TransformToAncestor(_mainWindow)
                             .TransformBounds(new Rect(element.RenderSize));
        }
        catch { return; }

        // Centre of element, clamped to _sel (leaving room for image size)
        var cx = bounds.Left + bounds.Width  / 2;
        var cy = bounds.Top  + bounds.Height / 2;
        cx = Math.Max(_sel.Left, Math.Min(cx, _sel.Right  - 20));
        cy = Math.Max(_sel.Top,  Math.Min(cy, _sel.Bottom - 24));

        EnsureCursorImageCreated();
        Canvas.SetLeft(_cursorImage!, cx);
        Canvas.SetTop (_cursorImage!, cy);
        _cursorImage!.Visibility = Visibility.Visible;
        UpdateCursorAnchor(new Point(cx, cy));

        _inCursorPlacementMode = false;
        CollapseHighlight();
        HideModeHint();
    }

    /// <summary>
    /// Places the cursor image at the exact <paramref name="overlayPt"/> (clamped to
    /// <see cref="_sel"/>), computes the anchor via <see cref="UpdateCursorAnchor"/>,
    /// and exits cursor-placement mode.
    /// </summary>
    private void PlaceCursorAtPoint(Point overlayPt)
    {
        if (!_sel.Contains(overlayPt)) return;

        PushUndo();

        var x = Math.Max(_sel.Left, Math.Min(overlayPt.X, _sel.Right  - 20));
        var y = Math.Max(_sel.Top,  Math.Min(overlayPt.Y, _sel.Bottom - 24));
        var clampedPt = new Point(x, y);

        EnsureCursorImageCreated();
        Canvas.SetLeft(_cursorImage!, x);
        Canvas.SetTop (_cursorImage!, y);
        _cursorImage!.Visibility = Visibility.Visible;

        UpdateCursorAnchor(clampedPt);

        _inCursorPlacementMode = false;
        CollapseHighlight();
        HideModeHint();
    }

    /// <summary>
    /// Updates <see cref="_cursorAnchorName"/>, <see cref="_cursorAnchorBounds"/>, and
    /// <see cref="_cursorLogicalPos"/> atomically for a given overlay point.
    /// When a named anchor is found, the offset is relative to its top-left;
    /// otherwise it falls back to being relative to <see cref="_sel"/>'s top-left.
    /// </summary>
    private void UpdateCursorAnchor(Point overlayPt)
    {
        var (anchorName, anchorBounds) = FindNamedAnchorForPoint(overlayPt);
        _cursorAnchorName   = anchorName;
        _cursorAnchorBounds = anchorBounds;

        var origin = anchorName != null ? anchorBounds.TopLeft : _sel.TopLeft;
        _cursorLogicalPos = new Point(overlayPt.X - origin.X, overlayPt.Y - origin.Y);
    }

    /// <summary>
    /// Hit-tests <paramref name="overlayPt"/> into <see cref="_mainWindow"/>'s visual
    /// tree and walks up to find the <b>smallest-area named</b>
    /// <see cref="FrameworkElement"/> that contains the point.
    /// "Named" = has a non-empty <c>x:Name</c> OR a <see cref="IHaveUniqueName"/>
    /// <c>DataContext</c>.
    /// </summary>
    /// <returns>
    /// The composed name string and bounds in main-window logical coordinates,
    /// or <c>(null, default)</c> when no named element is found.
    /// </returns>
    private (string? AnchorName, Rect AnchorBounds) FindNamedAnchorForPoint(Point overlayPt)
    {
        if (!_sel.Contains(overlayPt)) return (null, default);

        HitTestResult? hit = null;
        try { hit = VisualTreeHelper.HitTest(_mainWindow, overlayPt); }
        catch { }
        if (hit?.VisualHit == null) return (null, default);

        string? bestName   = null;
        Rect    bestBounds = default;
        double  bestArea   = double.MaxValue;
        var     node       = hit.VisualHit as DependencyObject;

        while (node != null)
        {
            if (node is FrameworkElement fe)
            {
                try
                {
                    var xName      = fe.Name;
                    var uniqueName = fe.DataContext as IHaveUniqueName;
                    var hasName    = !string.IsNullOrEmpty(xName) || uniqueName != null;

                    if (hasName)
                    {
                        var bounds = fe.TransformToAncestor(_mainWindow)
                                       .TransformBounds(new Rect(fe.RenderSize));
                        if (bounds.Width > 0 && bounds.Height > 0 && bounds.Contains(overlayPt))
                        {
                            var area = bounds.Width * bounds.Height;
                            if (area < bestArea)
                            {
                                string name;
                                if (!string.IsNullOrEmpty(xName) && uniqueName != null)
                                    name = $"{xName}({uniqueName.UniqueName})";
                                else if (!string.IsNullOrEmpty(xName))
                                    name = xName;
                                else
                                    name = uniqueName!.UniqueName;

                                bestArea   = area;
                                bestName   = name;
                                bestBounds = bounds;
                            }
                        }
                    }
                }
                catch { }
            }
            node = VisualTreeHelper.GetParent(node);
        }

        return (bestName, bestBounds);
    }

    /// <summary>
    /// Programmatically builds a 24×24 <see cref="DrawingImage"/> that resembles
    /// the Windows arrow cursor — white fill with a black outline.
    /// This is an intentional literal-color usage (cursor rendering, not a theme colour).
    /// </summary>
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
        // clip height to 24 display pixels — matches the XAML Viewbox Height="42" proportions.
        const double displayHeight = 29.0;
        const double clipHeight    = 259.0;
        const double s             = displayHeight / clipHeight;
        var xf = new TransformGroup();
        xf.Children.Add(new TranslateTransform(-59, -22));
        xf.Children.Add(new ScaleTransform(s, s));
        group.Transform = xf;

        return new DrawingImage(group);
    }

    // ── Annotation mode — Space 4px ──────────────────────────────────────────

    /// <summary>
    /// Snaps the selection rect to the innermost element edges computed fresh from
    /// the current <see cref="_sel"/>, then expands by 4 px on each side.
    /// Updates <see cref="_sel"/> and redraws.
    /// </summary>
    private void RunSpace4px()
    {
        // Always compute fresh anchors for the current _sel — don't rely on the
        // snapshot in _capturedAnchors which may be stale or not yet computed.
        var anchors = VisualTreeEdgeAnalyzer.Analyze(_sel, _mainWindow);
        if (anchors.Length < 4) return;

        // Order: [0] Top, [1] Right, [2] Bottom, [3] Left  (see VisualTreeEdgeAnalyzer)
        var topA    = anchors[0];
        var rightA  = anchors[1];
        var bottomA = anchors[2];
        var leftA   = anchors[3];

        // Need all four anchors to have a real element
        if (topA.Element == null || rightA.Element  == null ||
            bottomA.Element == null || leftA.Element == null)
            return;

        // Inner edges: the matching edge of each anchor element in mainWindow logical coords
        // (overlay canvas coords == mainWindow logical coords since they share the same origin)
        var topInner    = topA.ElementBounds.Top;
        var rightInner  = rightA.ElementBounds.Right;
        var bottomInner = bottomA.ElementBounds.Bottom;
        var leftInner   = leftA.ElementBounds.Left;

        var newLeft   = leftInner  - 4;
        var newTop    = topInner   - 4;
        var newRight  = rightInner + 4;
        var newBottom = bottomInner + 4;

        // Clamp to canvas bounds
        newLeft   = Math.Max(0, newLeft);
        newTop    = Math.Max(0, newTop);
        newRight  = Math.Min(_mainWindow.ActualWidth,  newRight);
        newBottom = Math.Min(_mainWindow.ActualHeight, newBottom);

        if (newRight - newLeft < MinSize || newBottom - newTop < MinSize) return;

        _sel = new Rect(newLeft, newTop, newRight - newLeft, newBottom - newTop);

        // If already in annotation mode, keep _capturedSel and _capturedAnchors in sync,
        // reposition the mouse-capture rect, refresh indicators, and re-suggest the name.
        if (_inAnnotationMode)
        {
            _capturedSel     = _sel;
            _capturedAnchors = anchors;
            PositionSelHitRect();
            UpdateAnchorIndicators();
            if (_nameTextBox != null)
            {
                var anchorRecords = _capturedAnchors
                    .Select(a => new Screenshots.EdgeAnchorRecord(
                        Edge:           a.Edge,
                        ElementNames:   a.UniqueNames,
                        NeedsName:      a.NeedsName,
                        ElementLeft:    a.ElementBounds.Left,
                        ElementTop:     a.ElementBounds.Top,
                        ElementWidth:   a.ElementBounds.Width,
                        ElementHeight:  a.ElementBounds.Height,
                        DistanceToEdge: a.DistanceToEdge))
                    .ToArray();
                _nameTextBox.Text = Screenshots.ScreenshotNamingHelper.SuggestName(_themeName, anchorRecords);
            }
        }

        UpdateLayout();
    }

    // ── Annotation mode — arrow target selection ─────────────────────────────

    private void EnterArrowTargetMode()
    {
        _inArrowTargetMode  = true;
        Cursor              = Cursors.Cross;
        CollapseHighlight();
        _highlightedElement = null;
        ShowModeHint("Click the element you want to point to");
    }

    private void ExitArrowTargetMode()
    {
        _inArrowTargetMode  = false;
        _multiDropArrowMode = false;
        Cursor              = Cursors.Arrow;
        CollapseHighlight();
        _highlightedElement = null;
        HideModeHint();
        if (_addArrowBtn != null) _addArrowBtn.Content = "↗ Arrow";
    }

    // ── Mode-hint overlay helpers ─────────────────────────────────────────────

    /// <summary>
    /// Lazily creates the mode-hint overlay (dark semi-transparent pill) and
    /// shows <paramref name="text"/> anchored near the top of <see cref="_sel"/>.
    /// Background is an intentional literal design constant (#AA000000).
    /// </summary>
    private void ShowModeHint(string text)
    {
        if (_modeHintBorder == null)
        {
            _modeHintText = new TextBlock
            {
                FontSize         = 11,
                FontFamily       = new FontFamily("Segoe UI"),
                Foreground       = new SolidColorBrush(Color.FromArgb(0xFF, 0xFF, 0xFF, 0xFF)),
                TextAlignment    = TextAlignment.Center,
                IsHitTestVisible = false
            };
            _modeHintBorder = new Border
            {
                Background       = new SolidColorBrush(Color.FromArgb(0xAA, 0x00, 0x00, 0x00)),
                CornerRadius     = new CornerRadius(4),
                Padding          = new Thickness(6, 4, 6, 4),
                Child            = _modeHintText,
                IsHitTestVisible = false,
                Visibility       = Visibility.Collapsed,
                MinWidth         = 180
            };
            Panel.SetZIndex(_modeHintBorder, 150);
            _canvas.Children.Add(_modeHintBorder);
            // Re-position whenever the border's layout size changes (e.g. first measure
            // after the element is added to the live tree).
            _modeHintBorder.SizeChanged += (_, _) => PositionModeHint();
        }
        _modeHintText!.Text        = text;
        _modeHintBorder.Visibility = Visibility.Visible;
        PositionModeHint();
        // Deferred reposition: on first display DesiredSize may be (0,0) before the
        // WPF layout system completes its first full measure/arrange pass.
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
        var w  = _modeHintBorder.DesiredSize.Width;
        var cx = _sel.Left + _sel.Width / 2;
        Canvas.SetLeft(_modeHintBorder, Math.Max(_sel.Left, cx - w / 2));
        Canvas.SetTop (_modeHintBorder, _sel.Top + 8);
    }

    /// <summary>
    /// Sizes and positions <see cref="_selHitRect"/> to exactly cover <see cref="_sel"/>,
    /// and shows or hides it based on whether a non-empty selection exists.
    /// Works in both initial-selection and annotation modes; the near-zero-alpha fill
    /// ensures the OS routes mouse events into the selection interior in all modes.
    /// </summary>
    private void PositionSelHitRect()
    {
        if (_selHitRect == null) return;
        if (_sel.Width <= 0 || _sel.Height <= 0)
        {
            _selHitRect.Visibility = Visibility.Collapsed;
            return;
        }
        _selHitRect.Visibility = Visibility.Visible;
        Canvas.SetLeft(_selHitRect, _sel.Left);
        Canvas.SetTop (_selHitRect, _sel.Top);
        _selHitRect.Width  = _sel.Width;
        _selHitRect.Height = _sel.Height;
    }

    // ── Annotation mode — element highlight ──────────────────────────────────

    /// <summary>Collapses both marching-ants highlight rectangles.</summary>
    private void CollapseHighlight()
    {
        if (_highlightWhite != null) _highlightWhite.Visibility = Visibility.Collapsed;
        if (_highlightBlack != null) _highlightBlack.Visibility = Visibility.Collapsed;
    }

    /// <summary>
    /// Positions both marching-ants rectangles at <paramref name="r"/> and makes
    /// them visible.  Also updates <see cref="_highlightClip"/> to the current
    /// <see cref="_sel"/> so the rects are always cropped to the capture region.
    /// </summary>
    private void SetHighlightRect(Rect r)
    {
        // Keep the clip geometry in sync with the current selection
        _highlightClip.Rect = _sel;

        foreach (var rect in new[] { _highlightWhite, _highlightBlack })
        {
            Canvas.SetLeft(rect, r.Left);
            Canvas.SetTop (rect, r.Top);
            rect.Width      = r.Width;
            rect.Height     = r.Height;
            rect.Visibility = Visibility.Visible;
        }
    }

    /// <summary>
    /// Hit-tests <paramref name="overlayPt"/> into <see cref="_mainWindow"/>'s visual
    /// tree, finds the deepest named element whose bounds intersect <see cref="_sel"/>,
    /// clips the highlight to <see cref="_sel"/>, and draws the marching-ants effect.
    /// </summary>
    private void UpdateHighlightForPoint(Point overlayPt)
    {
        if (_highlightWhite == null || _highlightBlack == null) return;

        // Reject pointer positions outside the capture selection — an element that
        // merely overlaps _sel must not be highlighted when the pointer is elsewhere.
        if (!_sel.Contains(overlayPt))
        {
            CollapseHighlight();
            _highlightedElement = null;
            return;
        }

        // Overlay canvas coords == mainWindow logical coords (same origin, same size).
        var mwPt = overlayPt;

        HitTestResult? hit = null;
        try { hit = VisualTreeHelper.HitTest(_mainWindow, mwPt); }
        catch { /* HitTest throws when tree is not ready */ }

        if (hit?.VisualHit == null)
        {
            CollapseHighlight();
            _highlightedElement = null;
            return;
        }

        // Walk up the visual tree. Take the smallest-area FrameworkElement
        // whose bounds contain mwPt AND intersect _sel.
        // We don't require a Name here — unnamed elements are valid highlight targets.
        // Arrow creation will fall back to an empty name if the element is unnamed.
        FrameworkElement? best    = null;
        double            bestArea = double.MaxValue;
        var node = hit.VisualHit as DependencyObject;

        while (node != null)
        {
            if (node is FrameworkElement fe)
            {
                try
                {
                    var bounds = fe.TransformToAncestor(_mainWindow)
                                   .TransformBounds(new Rect(fe.RenderSize));
                    if (bounds.Width > 0 && bounds.Height > 0 &&
                        bounds.Contains(mwPt) && bounds.IntersectsWith(_sel))
                    {
                        var area = bounds.Width * bounds.Height;
                        if (area < bestArea)
                        {
                            bestArea = area;
                            best     = fe;
                        }
                    }
                }
                catch { /* element not in shared tree */ }
            }
            node = VisualTreeHelper.GetParent(node);
        }

        if (best != null)
        {
            _highlightedElement = best;
            try
            {
                var bounds = best.TransformToAncestor(_mainWindow)
                                 .TransformBounds(new Rect(best.RenderSize));

                // Clip to _sel so the highlight never extends outside the selection
                var clipped = Rect.Intersect(bounds, _sel);
                if (clipped.IsEmpty)
                {
                    CollapseHighlight();
                    _highlightedElement = null;
                }
                else
                {
                    SetHighlightRect(clipped);
                }
            }
            catch
            {
                CollapseHighlight();
                _highlightedElement = null;
            }
        }
        else
        {
            CollapseHighlight();
            _highlightedElement = null;
        }
    }

    // ── Annotation mode — arrow creation ─────────────────────────────────────

    /// <summary>
    /// Locks the currently highlighted element as the arrow target and creates
    /// a new <see cref="AnnotationArrow"/> pointing at it.
    /// Must only be called when <see cref="_highlightedElement"/> is non-null.
    /// </summary>
    private void LockArrowTarget()
    {
        if (_highlightedElement == null) return;
        try
        {
            var bounds = _highlightedElement
                             .TransformToAncestor(_mainWindow)
                             .TransformBounds(new Rect(_highlightedElement.RenderSize));
            var name = _highlightedElement.Name;

            if (_multiDropArrowMode)
            {
                // Create arrow, then re-enter targeting mode for the next drop
                CreateArrow(name, bounds);
                _highlightedElement = null;
                EnterArrowTargetMode();
                if (_addArrowBtn != null) _addArrowBtn.Content = "✦ ↗ Arrow";
                ShowModeHint("Shift-drop mode: click elements to drop arrows (Esc to stop)");
            }
            else
            {
                ExitArrowTargetMode();
                CreateArrow(name, bounds);
            }
        }
        catch
        {
            _multiDropArrowMode = false;
            ExitArrowTargetMode();
        }
    }

    /// <summary>
    /// Creates a new <see cref="AnnotationArrow"/> pointing at <paramref name="targetBounds"/>
    /// and adds its visual elements to <see cref="_canvas"/>.
    /// </summary>
    private AnnotationArrow CreateArrow(string targetElementName, Rect targetBounds)
    {
        if (!_suppressUndo) PushUndo();

        var DefaultAngleDeg        = _defaultArrowAngleDeg;   // Fix 4: use persisted default
        var DefaultArrowheadOffset = _defaultArrowLength;      // Fix 4: use persisted default

        var center = new Point(
            targetBounds.Left + targetBounds.Width  / 2,
            targetBounds.Top  + targetBounds.Height / 2);

        // Fix 4: use user's saved tail-length preference when set; otherwise auto-fill to sel edge
        double initialTailLength;
        if (_defaultTailLength > 0)
            initialTailLength = _defaultTailLength;
        else
            initialTailLength = ComputeInitialTailLength(center, DefaultAngleDeg, DefaultArrowheadOffset);

        // Shadow shapes — drawn first so they appear behind the main arrow visuals.
        var shadowLine = new Polyline
        {
            Stroke           = new SolidColorBrush(Color.FromArgb(102, 0, 0, 0)),
            StrokeThickness  = 2.5,
            IsHitTestVisible = false
        };
        var shadowHead = new Polygon
        {
            Fill             = new SolidColorBrush(Color.FromArgb(102, 0, 0, 0)),
            IsHitTestVisible = false
        };

        // Main arrow visuals — colour comes from the persisted default (initially orange).
        var orange = new SolidColorBrush(_defaultArrowColor);   // Fix 4: use persisted default

        var line = new Line
        {
            StrokeThickness  = 2.5,
            Stroke           = orange,
            IsHitTestVisible = true,
            Cursor           = Cursors.Arrow
        };

        var head = new Polygon
        {
            Fill             = orange,
            IsHitTestVisible = true,
            Cursor           = Cursors.Arrow
        };

        var tipHandle = new Ellipse
        {
            Width           = 8,
            Height          = 8,
            Fill            = orange,
            Stroke          = Brushes.White,
            StrokeThickness = 1.5,
            Cursor          = Cursors.SizeAll,
            Visibility      = Visibility.Hidden
        };

        // Tail handle at target-centre end — drag to adjust arrow length
        var tailHandle = new Ellipse
        {
            Width           = 8,
            Height          = 8,
            Fill            = orange,
            Stroke          = Brushes.White,
            StrokeThickness = 1.5,
            Cursor          = Cursors.SizeAll,
            Visibility      = Visibility.Hidden
        };

        _canvas.Children.Add(shadowLine);
        _canvas.Children.Add(shadowHead);
        _canvas.Children.Add(line);
        _canvas.Children.Add(head);
        _canvas.Children.Add(tailHandle);
        _canvas.Children.Add(tipHandle);   // tip on top of tail
        Panel.SetZIndex(tipHandle,  10);   // Bug 1 fix: must be above _selHitRect (ZIndex=1)
        Panel.SetZIndex(tailHandle, 10);
        Panel.SetZIndex(line, 5);          // above _selHitRect (1), below handles (10)
        Panel.SetZIndex(head, 5);
        Panel.SetZIndex(shadowLine, 2);    // below main arrow shapes
        Panel.SetZIndex(shadowHead, 2);

        var arrow = new AnnotationArrow
        {
            TargetElementName    = targetElementName,
            TargetElementBounds  = targetBounds,
            ArrowheadAngleDeg    = DefaultAngleDeg,
            ArrowLength          = DefaultArrowheadOffset,
            TailLength           = initialTailLength,
            UserTailLength       = _defaultTailLength,      // Fix 4: -1 = auto, or user's saved preference
            ArrowColor           = _defaultArrowColor,      // Fix 4: use persisted default colour
            Line                 = line,
            Head                 = head,
            TipHandle            = tipHandle,
            TailHandle           = tailHandle,
            TargetCenterOnCanvas = center,
            ShadowLine           = shadowLine,
            ShadowHead           = shadowHead
        };

        // ── Tip-handle drag — changes angle; auto-fills length to sel edge ────
        tipHandle.MouseLeftButtonDown += (_, e) =>
        {
            _preDragSnapshot = CaptureSnapshot();
            _draggingArrow = arrow;
            _tailDragging  = false;
            tipHandle.CaptureMouse();
            e.Handled = true;
        };
        tipHandle.MouseMove += (_, e) =>
        {
            if (_draggingArrow != arrow || _tailDragging || _bodyDragging) return;
            var pivot = new Point(arrow.TargetCenterOnCanvas.X + arrow.OffsetX,
                                  arrow.TargetCenterOnCanvas.Y + arrow.OffsetY);
            var pt   = e.GetPosition(_canvas);
            var dx   = pt.X - pivot.X;
            var dy   = pt.Y - pivot.Y;
            var dist = Math.Sqrt(dx * dx + dy * dy);
            var newAngle = Math.Atan2(dx, -dy) * 180.0 / Math.PI;
            if (newAngle < 0) newAngle += 360;
            arrow.ArrowheadAngleDeg = newAngle;
            // ArrowLength = how far arrowhead is from pivot; clamp min 5px
            arrow.ArrowLength = Math.Max(5.0, Math.Min(dist, ComputeMaxArrowheadOffset(pivot, newAngle)));
            // Use user-set tail length when available, otherwise auto-fill to sel edge
            double maxFromArrowhead = ComputeMaxArrowheadOffset(pivot, newAngle) - arrow.ArrowLength;
            if (arrow.UserTailLength > 0)
                arrow.TailLength = Math.Max(64, Math.Min(arrow.UserTailLength, maxFromArrowhead));
            else
                arrow.TailLength = ComputeInitialTailLength(pivot, newAngle, arrow.ArrowLength);
            UpdateArrowGeometry(arrow);
            e.Handled = true;
        };
        tipHandle.MouseLeftButtonUp += (_, e) =>
        {
            if (_draggingArrow != arrow || _tailDragging) return;
            // Fix 4: save new angle and arrow length as defaults for next arrow
            _defaultArrowAngleDeg = arrow.ArrowheadAngleDeg;
            _defaultArrowLength   = arrow.ArrowLength;
            SaveArrowDefaults();
            CommitDragUndo();
            _draggingArrow = null;
            tipHandle.ReleaseMouseCapture();
            e.Handled = true;
        };

        // ── Tail-handle drag — full rotation + length (arrowhead pivot stays fixed) ─
        tailHandle.MouseLeftButtonDown += (_, e) =>
        {
            _preDragSnapshot = CaptureSnapshot();
            _draggingArrow         = arrow;
            _tailDragging          = true;
            _tailDragInitialLength = arrow.TailLength;   // snapshot kept; not used in new move logic
            _tailDragStartMouse    = e.GetPosition(_canvas);
            tailHandle.CaptureMouse();
            e.Handled = true;
        };
        tailHandle.MouseMove += (_, e) =>
        {
            if (_draggingArrow != arrow || !_tailDragging) return;
            var pt    = e.GetPosition(_canvas);
            var pivot = new Point(arrow.TargetCenterOnCanvas.X + arrow.OffsetX,
                                  arrow.TargetCenterOnCanvas.Y + arrow.OffsetY);

            // Direction FROM pivot TO tail position (tail is in the away-from-element direction)
            var dx = pt.X - pivot.X;
            var dy = pt.Y - pivot.Y;
            var dist = Math.Sqrt(dx * dx + dy * dy);
            if (dist < 1) { e.Handled = true; return; }

            // Angle is direction from pivot toward tail (same convention as ArrowheadAngleDeg)
            var newAngle = Math.Atan2(dx, -dy) * 180.0 / Math.PI;
            if (newAngle < 0) newAngle += 360;

            arrow.ArrowheadAngleDeg = newAngle;

            // ArrowLength stays fixed; TailLength = total - ArrowLength, min 64
            const double MinTailLength = 64.0;
            var totalLen  = Math.Max(arrow.ArrowLength + MinTailLength, dist);
            arrow.TailLength = Math.Max(MinTailLength, totalLen - arrow.ArrowLength);

            UpdateArrowGeometry(arrow);
            e.Handled = true;
        };
        tailHandle.MouseLeftButtonUp += (_, e) =>
        {
            if (_draggingArrow != arrow || !_tailDragging) return;
            arrow.UserTailLength  = arrow.TailLength;         // Fix 2: persist user-set tail length
            _defaultTailLength    = arrow.TailLength;         // Fix 4: save as default for next arrow
            _defaultArrowAngleDeg = arrow.ArrowheadAngleDeg;  // Fix 4: save angle while we're here
            SaveArrowDefaults();
            CommitDragUndo();
            _draggingArrow = null;
            _tailDragging  = false;
            tailHandle.ReleaseMouseCapture();
            e.Handled = true;
        };

        // ── Click/drag line or arrowhead to select + move; right-click to delete ─
        line.MouseLeftButtonDown += (_, e) =>
        {
            if (_draggingArrow != null) return;
            SelectArrow(arrow);
            _preDragSnapshot = CaptureSnapshot();
            _draggingArrow        = arrow;
            _bodyDragging         = true;
            _bodyDragStartMouse   = e.GetPosition(_canvas);
            _bodyDragStartOffsetX = arrow.OffsetX;
            _bodyDragStartOffsetY = arrow.OffsetY;
            line.CaptureMouse();
            e.Handled = true;
        };
        line.MouseMove += (_, e) =>
        {
            if (_draggingArrow != arrow || !_bodyDragging) return;
            var pt    = e.GetPosition(_canvas);
            arrow.OffsetX = _bodyDragStartOffsetX + (pt.X - _bodyDragStartMouse.X);
            arrow.OffsetY = _bodyDragStartOffsetY + (pt.Y - _bodyDragStartMouse.Y);
            UpdateArrowGeometry(arrow);
            if (_colorPickerArrow == arrow) ShowColorPicker(arrow);
            e.Handled = true;
        };
        line.MouseLeftButtonUp += (_, e) =>
        {
            if (_draggingArrow != arrow || !_bodyDragging) return;
            CommitDragUndo();
            _draggingArrow = null;
            _bodyDragging  = false;
            line.ReleaseMouseCapture();
            e.Handled = true;
        };
        head.MouseLeftButtonDown += (_, e) =>
        {
            if (_draggingArrow != null) return;
            SelectArrow(arrow);
            _preDragSnapshot = CaptureSnapshot();
            _draggingArrow        = arrow;
            _bodyDragging         = true;
            _bodyDragStartMouse   = e.GetPosition(_canvas);
            _bodyDragStartOffsetX = arrow.OffsetX;
            _bodyDragStartOffsetY = arrow.OffsetY;
            head.CaptureMouse();
            e.Handled = true;
        };
        head.MouseMove += (_, e) =>
        {
            if (_draggingArrow != arrow || !_bodyDragging) return;
            var pt    = e.GetPosition(_canvas);
            arrow.OffsetX = _bodyDragStartOffsetX + (pt.X - _bodyDragStartMouse.X);
            arrow.OffsetY = _bodyDragStartOffsetY + (pt.Y - _bodyDragStartMouse.Y);
            UpdateArrowGeometry(arrow);
            if (_colorPickerArrow == arrow) ShowColorPicker(arrow);
            e.Handled = true;
        };
        head.MouseLeftButtonUp += (_, e) =>
        {
            if (_draggingArrow != arrow || !_bodyDragging) return;
            CommitDragUndo();
            _draggingArrow = null;
            _bodyDragging  = false;
            head.ReleaseMouseCapture();
            e.Handled = true;
        };
        line.MouseRightButtonDown += (_, e) =>
        {
            RemoveArrow(arrow);
            if (_selectedArrow == arrow) { _selectedArrow = null; HideColorPicker(); }
            e.Handled = true;
        };
        head.MouseRightButtonDown += (_, e) =>
        {
            RemoveArrow(arrow);
            if (_selectedArrow == arrow) { _selectedArrow = null; HideColorPicker(); }
            e.Handled = true;
        };
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
        SelectArrow(arrow);   // auto-select so handles and color picker appear immediately
        return arrow;
    }

    /// <summary>
    /// Recomputes the <see cref="Line"/>, arrowhead <see cref="Polygon"/>, and
    /// both handle positions for <paramref name="arrow"/> from its current
    /// <see cref="AnnotationArrow.ArrowheadAngleDeg"/>,
    /// <see cref="AnnotationArrow.ArrowLength"/>, and
    /// <see cref="AnnotationArrow.TailLength"/>.
    ///
    /// Arrow model: arrowhead tip is at ArrowLength from target centre (near element);
    /// tail end is at ArrowLength + TailLength from centre (far from element).
    /// The shaft runs tail → arrowhead; the triangle tip points toward the element.
    /// </summary>
    private static void UpdateArrowGeometry(AnnotationArrow arrow)
    {
        // Apply current colour to all main shapes
        var brush = new SolidColorBrush(arrow.ArrowColor);
        arrow.Line.Stroke = brush;
        arrow.Head.Fill   = brush;
        arrow.Head.Stroke = brush;

        var center = new Point(arrow.TargetCenterOnCanvas.X + arrow.OffsetX,
                               arrow.TargetCenterOnCanvas.Y + arrow.OffsetY);
        var rad    = arrow.ArrowheadAngleDeg * Math.PI / 180.0;
        var ux     = Math.Sin(rad);
        var uy     = -Math.Cos(rad);   // unit vector FROM center TOWARD arrowhead

        // Arrowhead tip — at ArrowLength from center
        var ahX = center.X + ux * arrow.ArrowLength;
        var ahY = center.Y + uy * arrow.ArrowLength;

        // Tail end — ArrowLength + TailLength from center
        var tailX = center.X + ux * (arrow.ArrowLength + arrow.TailLength);
        var tailY = center.Y + uy * (arrow.ArrowLength + arrow.TailLength);

        // Shaft: tail → arrowhead tip
        arrow.Line.X1 = tailX;
        arrow.Line.Y1 = tailY;
        arrow.Line.X2 = ahX;
        arrow.Line.Y2 = ahY;

        // Arrowhead triangle: tip at ahX/ahY, base 16px AWAY from center.
        // The triangle points TOWARD center (tip is the near-element vertex). ✓
        const double HeadLen  = 16.0;
        const double HeadHalf =  6.0;
        var baseX = ahX + ux * HeadLen;   // base is away from center
        var baseY = ahY + uy * HeadLen;
        var px    = -uy;                   // perpendicular
        var py    =  ux;

        arrow.Head.Points = new PointCollection
        {
            new Point(ahX, ahY),                                        // tip (near element)
            new Point(baseX + px * HeadHalf, baseY + py * HeadHalf),    // base wing 1
            new Point(baseX - px * HeadHalf, baseY - py * HeadHalf)     // base wing 2
        };

        // TipHandle (arrowhead handle) — centred on arrowhead tip
        Canvas.SetLeft(arrow.TipHandle,  ahX  - 4);
        Canvas.SetTop (arrow.TipHandle,  ahY  - 4);

        // TailHandle — centred on tail end
        Canvas.SetLeft(arrow.TailHandle, tailX - 4);
        Canvas.SetTop (arrow.TailHandle, tailY - 4);

        // Shadow — same geometry offset by (+2, +2)
        arrow.ShadowLine.Points = new PointCollection(new[]
        {
            new Point(tailX + 2, tailY + 2),
            new Point(ahX  + 2, ahY  + 2)
        });
        arrow.ShadowHead.Points = new PointCollection(
            arrow.Head.Points.Select(p => p + new Vector(2, 2)));
    }

    /// <summary>
    /// Computes the tail extension so the arrow fills from the arrowhead to the
    /// nearest <see cref="_sel"/> edge in the given direction, capped at 128 px.
    /// </summary>
    private double ComputeInitialTailLength(Point targetCenter, double angleDeg, double arrowheadOffset)
    {
        var rad = angleDeg * Math.PI / 180.0;
        var dx  = Math.Sin(rad);
        var dy  = -Math.Cos(rad);
        // Start from arrowhead position, not from center
        var ahX = targetCenter.X + dx * arrowheadOffset;
        var ahY = targetCenter.Y + dy * arrowheadOffset;
        var s   = _sel;

        double tMin = double.MaxValue;
        if (Math.Abs(dx) > 1e-9)
        {
            var t = dx > 0 ? (s.Right  - ahX) / dx : (s.Left  - ahX) / dx;
            if (t > 0) tMin = Math.Min(tMin, t);
        }
        if (Math.Abs(dy) > 1e-9)
        {
            var t = dy > 0 ? (s.Bottom - ahY) / dy : (s.Top   - ahY) / dy;
            if (t > 0) tMin = Math.Min(tMin, t);
        }
        return tMin < double.MaxValue ? Math.Max(64.0, Math.Min(128.0, tMin * 0.85)) : 80.0;
    }

    /// <summary>
    /// Returns the distance from <paramref name="targetCenter"/> to the nearest
    /// <see cref="_sel"/> edge in <paramref name="angleDeg"/> direction, so the
    /// arrowhead handle cannot be dragged outside the selection.
    /// </summary>
    private double ComputeMaxArrowheadOffset(Point targetCenter, double angleDeg)
    {
        var rad = angleDeg * Math.PI / 180.0;
        var dx  = Math.Sin(rad);
        var dy  = -Math.Cos(rad);
        var s   = _sel;
        double tMin = double.MaxValue;
        if (Math.Abs(dx) > 1e-9)
        {
            var t = dx > 0 ? (s.Right  - targetCenter.X) / dx : (s.Left  - targetCenter.X) / dx;
            if (t > 0) tMin = Math.Min(tMin, t);
        }
        if (Math.Abs(dy) > 1e-9)
        {
            var t = dy > 0 ? (s.Bottom - targetCenter.Y) / dy : (s.Top   - targetCenter.Y) / dy;
            if (t > 0) tMin = Math.Min(tMin, t);
        }
        return tMin < double.MaxValue ? tMin : 80.0;
    }

    // ── Annotation mode — arrow selection ────────────────────────────────────

    private void SelectArrow(AnnotationArrow? arrow)
    {
        // Hide handles on previously selected arrow
        if (_selectedArrow != null && _selectedArrow != arrow)
        {
            _selectedArrow.TipHandle.Visibility  = Visibility.Hidden;
            _selectedArrow.TailHandle.Visibility = Visibility.Hidden;
        }
        _selectedArrow = arrow;
        if (arrow != null)
        {
            arrow.TipHandle.Visibility  = Visibility.Visible;
            arrow.TailHandle.Visibility = Visibility.Visible;
            ShowColorPicker(arrow);
        }
        else
        {
            HideColorPicker();
        }
    }

    // ── Annotation mode — colour picker ──────────────────────────────────────

    private static Color[] GetArrowPalette(bool isDark) => isDark
        ? new[]
          {
              Color.FromRgb(255,  80,  80),   // red
              Color.FromRgb(255, 160,  40),   // orange
              Color.FromRgb(255, 230,  60),   // yellow
              Color.FromRgb( 80, 220,  80),   // green
              Color.FromRgb( 80, 160, 255),   // blue
              Color.FromRgb(255, 255, 255),   // white (dark theme only)
          }
        : new[]
          {
              Color.FromRgb(180,  30,  30),   // dark red
              Color.FromRgb(180,  80,   0),   // dark orange
              Color.FromRgb(140, 120,   0),   // olive/dark yellow
              Color.FromRgb( 20, 130,  20),   // dark green
              Color.FromRgb( 20,  80, 200),   // dark blue
              Color.FromRgb(  0,   0,   0),   // black (light theme only)
          };

    private void ShowColorPicker(AnnotationArrow arrow)
    {
        HideColorPicker();
        _colorPickerArrow = arrow;
        bool isDark  = _themeName.IndexOf("dark", StringComparison.OrdinalIgnoreCase) >= 0;
        var  palette = GetArrowPalette(isDark);

        _colorPickerPanel = new StackPanel { Orientation = Orientation.Horizontal };
        Panel.SetZIndex(_colorPickerPanel, 300);

        foreach (var color in palette)
        {
            var c   = color;   // capture for closure
            var dot = new Ellipse
            {
                Width           = 16,
                Height          = 16,
                Fill            = new SolidColorBrush(c),
                Stroke          = c == arrow.ArrowColor
                                      ? (isDark ? Brushes.White : Brushes.Black)
                                      : Brushes.Transparent,
                StrokeThickness = 2,
                Margin          = new Thickness(3, 0, 3, 0),
                Cursor          = Cursors.Hand
            };
            dot.MouseLeftButtonDown += (_, e) =>
            {
                arrow.ArrowColor = c;
                _defaultArrowColor = c;   // Fix 4: new colour becomes default for next arrow
                SaveArrowDefaults();
                UpdateArrowGeometry(arrow);
                ShowColorPicker(arrow);   // refresh ring
                e.Handled = true;
            };
            _colorPickerPanel.Children.Add(dot);
        }

        _canvas.Children.Add(_colorPickerPanel);

        // Position above tip handle
        double cx = Canvas.GetLeft(arrow.TipHandle) + 4;
        double cy = Canvas.GetTop (arrow.TipHandle) + 4;
        _colorPickerPanel.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        double pw = _colorPickerPanel.DesiredSize.Width;
        Canvas.SetLeft(_colorPickerPanel, Math.Max(0, cx - pw / 2));
        Canvas.SetTop (_colorPickerPanel, Math.Max(0, cy - 30));
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

    // ── Annotation mode — save ───────────────────────────────────────────────

    /// <summary>
    /// Hides the overlay, renders <see cref="_mainWindow"/> to a
    /// <see cref="RenderTargetBitmap"/>, crops it to the selection, saves the PNG
    /// to a provisional path, writes the <c>.annotations.json</c> sidecar, then
    /// raises <see cref="ScreenshotSaved"/> so <c>MainWindow</c> can rename and
    /// register the capture.
    /// </summary>
    private async System.Threading.Tasks.Task DoAnnotationSaveAsync()
    {
        // Validate name from the unified panel's name field
        var name = (_nameTextBox?.Text ?? string.Empty).Trim();
        if (!IsValidKebabName(name))
        {
            if (TryFindResource("ScreenshotAnchorMissing") is Brush redBrush && _nameTextBox != null)
                _nameTextBox.BorderBrush = redBrush;

            var flashTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(300) };
            flashTimer.Tick += (_, _) =>
            {
                flashTimer.Stop();
                _nameTextBox?.SetResourceReference(TextBox.BorderBrushProperty, "PopupBorder");
                _nameTextBox?.Focus();
            };
            flashTimer.Start();
            _nameTextBox?.Focus();
            return;
        }

        _acceptedName = name;

        Hide();   // hide overlay before rendering the main window
        await Dispatcher.InvokeAsync(() => { }, DispatcherPriority.Render);
        await Dispatcher.InvokeAsync(() => { }, DispatcherPriority.Render);

        try
        {
            var dpi = VisualTreeHelper.GetDpi(_mainWindow);
            var pxW = (int)Math.Round(_mainWindow.ActualWidth  * dpi.DpiScaleX);
            var pxH = (int)Math.Round(_mainWindow.ActualHeight * dpi.DpiScaleY);

            var rtb = new RenderTargetBitmap(
                pxW, pxH,
                dpi.PixelsPerInchX, dpi.PixelsPerInchY,
                PixelFormats.Pbgra32);
            rtb.Render(_mainWindow);
            rtb.Freeze();

            BitmapSource bmp = rtb;

            if (!_capturedIsFullWindow)
            {
                var cropX = (int)Math.Round(_capturedSel.Left   * dpi.DpiScaleX);
                var cropY = (int)Math.Round(_capturedSel.Top    * dpi.DpiScaleY);
                var cropW = (int)Math.Round(_capturedSel.Width  * dpi.DpiScaleX);
                var cropH = (int)Math.Round(_capturedSel.Height * dpi.DpiScaleY);

                cropX = Math.Max(0, cropX);
                cropY = Math.Max(0, cropY);
                cropW = Math.Max(1, Math.Min(cropW, pxW - cropX));
                cropH = Math.Max(1, Math.Min(cropH, pxH - cropY));

                var cropped = new CroppedBitmap(rtb, new Int32Rect(cropX, cropY, cropW, cropH));
                cropped.Freeze();
                bmp = cropped;
            }

            Directory.CreateDirectory(_saveDirectory);

            var theme    = _themeName.ToLowerInvariant();
            var pngName  = $"_pending-{theme}.png";
            var fullPath = System.IO.Path.Combine(_saveDirectory, pngName);

            using (var stream = File.Create(fullPath))
            {
                var encoder = new PngBitmapEncoder();
                encoder.Frames.Add(BitmapFrame.Create(bmp));
                encoder.Save(stream);
            }

            // ── Annotation sidecar JSON ──────────────────────────────────────
            var rawDesc = _descriptionBox?.Text ?? "";
            if (rawDesc == _descriptionPlaceholder) rawDesc = "";

            var sidecar = new AnnotationSidecar(
                Version:        1,
                Description:    string.IsNullOrWhiteSpace(rawDesc) ? null : rawDesc,
                RawDescription: string.IsNullOrWhiteSpace(rawDesc) ? null : rawDesc,
                Cursor:         BuildCursorSidecar(),
                Arrows:         BuildArrowSidecars());

            var sidecarPath = System.IO.Path.Combine(
                _saveDirectory, $"_pending-{theme}.annotations.json");
            File.WriteAllText(sidecarPath,
                JsonSerializer.Serialize(sidecar, new JsonSerializerOptions { WriteIndented = true }));

            ScreenshotSaved?.Invoke(this, new ScreenshotSavedEventArgs(
                pngPath:       fullPath,
                selectionRect: _capturedSel,
                anchors:       _capturedAnchors,
                isFullWindow:  _capturedIsFullWindow,
                acceptedName:  _acceptedName));
        }
        catch (Exception ex)
        {
            ScreenshotFailed?.Invoke(this, ex.Message);
        }
        finally
        {
            Close();
        }
    }

    private CursorAnnotation? BuildCursorSidecar()
    {
        if (!_cursorEnabled || _cursorImage == null) return null;
        BoundsRecord? anchorBounds = _cursorAnchorName != null
            ? new BoundsRecord(
                X:      _cursorAnchorBounds.X,
                Y:      _cursorAnchorBounds.Y,
                Width:  _cursorAnchorBounds.Width,
                Height: _cursorAnchorBounds.Height)
            : null;
        return new CursorAnnotation(
            X:            _cursorLogicalPos.X,
            Y:            _cursorLogicalPos.Y,
            Type:         "arrow",
            AnchorName:   _cursorAnchorName,
            AnchorBounds: anchorBounds);
    }

    private List<ArrowAnnotation> BuildArrowSidecars() =>
        _arrows
            .Select(a => new ArrowAnnotation(
                TargetElementName:   a.TargetElementName,
                TargetElementBounds: new BoundsRecord(
                    X:      a.TargetElementBounds.Left,
                    Y:      a.TargetElementBounds.Top,
                    Width:  a.TargetElementBounds.Width,
                    Height: a.TargetElementBounds.Height),
                ArrowheadAngleDeg: a.ArrowheadAngleDeg,
                ArrowLength:       a.ArrowLength,
                TailLength:        a.TailLength,
                Color:             $"#{a.ArrowColor.R:X2}{a.ArrowColor.G:X2}{a.ArrowColor.B:X2}",
                OffsetX:           a.OffsetX,
                OffsetY:           a.OffsetY))
            .ToList();

    // ── Undo / redo ──────────────────────────────────────────────────────────

    /// <summary>Captures the current annotation state into a snapshot record.</summary>
    private OverlaySnapshot CaptureSnapshot() => new(
        Sel:             _sel,
        Arrows:          _arrows.Select(a => new ArrowSnapshot(
                             TargetElementName:    a.TargetElementName,
                             TargetElementBounds:  a.TargetElementBounds,
                             ArrowheadAngleDeg:    a.ArrowheadAngleDeg,
                             ArrowLength:          a.ArrowLength,
                             TailLength:           a.TailLength,
                             UserTailLength:       a.UserTailLength,
                             ArrowColor:           a.ArrowColor,
                             TargetCenterOnCanvas: a.TargetCenterOnCanvas,
                             OffsetX:              a.OffsetX,
                             OffsetY:              a.OffsetY)).ToList(),
        CursorEnabled:   _cursorEnabled,
        CursorCanvasPos: _cursorImage != null
                             ? new Point(Canvas.GetLeft(_cursorImage), Canvas.GetTop(_cursorImage))
                             : default,
        CursorAnchorName: _cursorAnchorName);

    /// <summary>
    /// Pushes the current state onto the undo stack and clears the redo stack.
    /// No-op when <see cref="_suppressUndo"/> is true.
    /// Capped at 50 entries.
    /// </summary>
    private void PushUndo()
    {
        if (_suppressUndo) return;
        _undoStack.Push(CaptureSnapshot());
        _redoStack.Clear();
        TrimUndoStack();
    }

    /// <summary>
    /// Pushes the pre-drag snapshot (captured at drag-start) onto the undo stack.
    /// Called at MouseUp to commit the drag operation as an undoable step.
    /// No-op when <see cref="_suppressUndo"/> is true or no pre-drag snapshot was captured.
    /// </summary>
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
        var items = _undoStack.ToArray();   // index 0 = most recently pushed
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

    /// <summary>
    /// Restores the full annotation state from <paramref name="snap"/>.
    /// Removes all current arrow visuals and re-creates them from the snapshot.
    /// </summary>
    private void RestoreSnapshot(OverlaySnapshot snap)
    {
        _suppressUndo = true;
        try
        {
            // ── Arrows ───────────────────────────────────────────────────────
            // Deselect and hide color picker before removing arrows
            SelectArrow(null);

            foreach (var arrow in _arrows.ToList())
                RemoveArrow(arrow);

            foreach (var arrowSnap in snap.Arrows)
                AddArrowFromSnapshot(arrowSnap);

            // Clear selection after restoring arrows (CreateArrow auto-selects the last one)
            SelectArrow(null);

            // ── Selection rectangle ──────────────────────────────────────────
            _sel = snap.Sel;
            if (_inAnnotationMode)
            {
                _capturedSel = _sel;
                _capturedAnchors = VisualTreeEdgeAnalyzer.Analyze(_sel, _mainWindow);
                UpdateAnchorIndicators();
            }

            // ── Cursor overlay ───────────────────────────────────────────────
            _cursorEnabled    = snap.CursorEnabled;
            _cursorAnchorName = snap.CursorAnchorName;

            if (snap.CursorEnabled)
            {
                EnsureCursorImageCreated();
                Canvas.SetLeft(_cursorImage!, snap.CursorCanvasPos.X);
                Canvas.SetTop (_cursorImage!, snap.CursorCanvasPos.Y);
                _cursorImage!.Visibility = Visibility.Visible;
                // Re-derive logical pos and anchor bounds from the canvas position
                UpdateCursorAnchor(snap.CursorCanvasPos);
            }
            else if (_cursorImage != null)
            {
                _cursorImage.Visibility = Visibility.Collapsed;
                _draggingCursor = false;
            }

            UpdateLayout();
            if (_inAnnotationMode) PositionSelHitRect();
        }
        finally
        {
            _suppressUndo = false;
        }
    }

    /// <summary>
    /// Creates an arrow from a snapshot record, applying the snapshot's data values
    /// over the defaults that <see cref="CreateArrow"/> would otherwise use.
    /// </summary>
    private void AddArrowFromSnapshot(ArrowSnapshot snap)
    {
        // CreateArrow adds the arrow to _arrows and canvas; _suppressUndo prevents a nested PushUndo.
        var arrow = CreateArrow(snap.TargetElementName, snap.TargetElementBounds);

        // Override every mutable property with the snapshotted values.
        arrow.ArrowheadAngleDeg    = snap.ArrowheadAngleDeg;
        arrow.ArrowLength          = snap.ArrowLength;
        arrow.TailLength           = snap.TailLength;
        arrow.UserTailLength       = snap.UserTailLength;
        arrow.ArrowColor           = snap.ArrowColor;
        arrow.TargetCenterOnCanvas = snap.TargetCenterOnCanvas;
        arrow.OffsetX              = snap.OffsetX;
        arrow.OffsetY              = snap.OffsetY;

        UpdateArrowGeometry(arrow);
    }

    // ── Arrow defaults — persist/load ────────────────────────────────────────

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
            var json = JsonSerializer.Serialize(new
            {
                color    = $"#{_defaultArrowColor.R:X2}{_defaultArrowColor.G:X2}{_defaultArrowColor.B:X2}",
                angleDeg = _defaultArrowAngleDeg,
                length   = _defaultArrowLength,
                tailLen  = _defaultTailLength
            });
            File.WriteAllText(ArrowDefaultsPath, json);
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
                col.GetString() is { Length: 7 } hex &&
                hex[0] == '#')
            {
                var r = Convert.ToByte(hex[1..3], 16);
                var g = Convert.ToByte(hex[3..5], 16);
                var b = Convert.ToByte(hex[5..7], 16);
                _defaultArrowColor = Color.FromRgb(r, g, b);
            }
            if (root.TryGetProperty("angleDeg", out var ang)) _defaultArrowAngleDeg = ang.GetDouble();
            if (root.TryGetProperty("length",   out var len)) _defaultArrowLength   = len.GetDouble();
            if (root.TryGetProperty("tailLen",  out var tl))  _defaultTailLength    = tl.GetDouble();
        }
        catch { /* non-critical */ }
    }

    // ── Snapshot records (undo/redo) ─────────────────────────────────────────

    private sealed record OverlaySnapshot(
        Rect                         Sel,
        IReadOnlyList<ArrowSnapshot> Arrows,
        bool                         CursorEnabled,
        Point                        CursorCanvasPos,
        string?                      CursorAnchorName);

    private sealed record ArrowSnapshot(
        string  TargetElementName,
        Rect    TargetElementBounds,
        double  ArrowheadAngleDeg,
        double  ArrowLength,
        double  TailLength,
        double  UserTailLength,
        Color   ArrowColor,
        Point   TargetCenterOnCanvas,
        double  OffsetX,
        double  OffsetY);

}
