using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using ICSharpCode.AvalonEdit;

namespace SquadDash;

/// <summary>Transcript-only policy and lifetime; shared Inbox/plan code blocks do not opt in.</summary>
internal sealed class TranscriptCodeBlockPreview : ICopyable
{
    internal const double MaximumHeight = 850;
    // The benchmark cost ~47 ms/200 lines and ~675 ms/2,000 lines. Limit a single
    // deferred upgrade to 300 lines/32,768 characters (target: tens of ms, not
    // hundreds). Both bounds matter: one enormous minified line is not cheap.
    // These are highlight budgets only; the plain surface/copy retain ALL content.
    internal const int MaximumLines = 300;
    internal const int MaximumCharacters = 32_768;

    private static readonly ConditionalWeakTable<Dispatcher, TranscriptHighlightingPool> Pools = new();
    private static readonly ConditionalWeakTable<ScrollViewer, ViewportObserver> Observers = new();
    private static readonly DependencyProperty PreviewProperty = DependencyProperty.RegisterAttached(
        "Preview", typeof(TranscriptCodeBlockPreview), typeof(TranscriptCodeBlockPreview));
    private readonly Grid _container;
    private readonly TextBox _plain;
    private readonly FrameworkElement _copyHost;
    private readonly string? _grammar;
    private readonly TextBlock? _label;
    private TextEditor? _editor;
    private ViewportObserver? _observer;
    private TranscriptHighlightingPool? _pool;
    private bool _syncingSelection;

    internal Control Surface => (Control?)_editor ?? _plain;
    internal TextBox PlainTextBox => _plain;
    internal int SelectionLength => _editor?.SelectionLength ?? _plain.SelectionLength;

    internal TranscriptCodeBlockPreview(Grid container, TextBox plain, FrameworkElement copyHost, string? language)
    {
        _container = container;
        _plain = plain;
        _copyHost = copyHost;
        _grammar = ResolveGrammar(language, plain.Text);
        container.Tag = this;
        plain.SetValue(PreviewProperty, this);
        plain.SelectionChanged += (_, _) =>
        {
            if (_editor is null || _syncingSelection)
                return;
            _syncingSelection = true;
            try { _editor.Select(_plain.SelectionStart, _plain.SelectionLength); }
            finally { _syncingSelection = false; }
        };
        if (!string.IsNullOrWhiteSpace(language))
        {
            _label = new TextBlock
            {
                Text = language,
                FontFamily = SystemFonts.MessageFontFamily,
                FontSize = plain.FontSize / 0.9 * 0.7,
                Margin = new Thickness(10, 7, copyHost.Width + 12, 0),
                HorizontalAlignment = HorizontalAlignment.Left,
                VerticalAlignment = VerticalAlignment.Top,
                IsHitTestVisible = false,
                TextTrimming = TextTrimming.CharacterEllipsis,
            };
            _label.SetResourceReference(TextBlock.ForegroundProperty, "SubtleText");
            container.Children.Add(_label);
        }

        UpdateCodePadding();
        container.PreviewMouseWheel += OnMouseWheel;
        if (_grammar is null)
            return;

        // Reserve the bounded footprint while still cheap, before entering the viewport.
        // The editor measures its own content against the same cap when upgraded.
        plain.MaxHeight = MaximumHeight;
        plain.VerticalScrollBarVisibility = ScrollBarVisibility.Auto;
        container.Loaded += OnLoaded;
        container.Unloaded += OnUnloaded;
    }

    internal static string? ResolveGrammar(string? language, string text, RegionHighlightingEngine? engine = null)
    {
        if (string.IsNullOrWhiteSpace(language) || text.Length > MaximumCharacters ||
            RegionLanguagePicker.GetMode(language) != RegionLanguageMode.ExplicitLanguage)
            return null;
        var lines = 1;
        for (var i = 0; i < text.Length; i++)
        {
            if (text[i] == '\n' || (text[i] == '\r' && (i + 1 == text.Length || text[i + 1] != '\n')))
                if (++lines > MaximumLines)
                    return null;
        }
        // Never call Resolve here: an absent transcript language must not auto-detect.
        return (engine ?? RegionHighlightingEngine.Default).ResolveFenceToken(language);
    }

    public string GetCopyText() => GetCopyText(false);

    internal string GetCopyText(bool selectionOnly)
    {
        var visible = CopyableCodeBlockBuilder.GetCopyText(_plain, false);
        if (visible.Length == 0)
            return "";
        if (selectionOnly && _editor is { SelectionLength: > 0 })
            return _editor.SelectedText;
        return CopyableCodeBlockBuilder.GetCopyText(_plain, selectionOnly && _editor is null);
    }

    internal void SelectAll()
    {
        if (_editor is not null) _editor.SelectAll();
        else _plain.SelectAll();
    }

    internal void UpdateFontSize(double baseFontSize)
    {
        _plain.FontSize = baseFontSize * 0.9;
        _copyHost.Width = _copyHost.Height = baseFontSize * 1.5;
        UpdateCodePadding();
        if (_label is not null)
        {
            _label.FontSize = baseFontSize * 0.7;
            _label.Margin = new Thickness(10, 7, _copyHost.Width + 12, 0);
        }
        if (_editor is not null)
        {
            _editor.FontSize = _plain.FontSize;
            _editor.Padding = _plain.Padding;
            RecomputeEditorHeight(_editor);
        }
    }

    private void UpdateCodePadding()
    {
        // At 14px base, the 7px label inset leaves ~3.6px instead of ~6px (60%).
        // Keep 0.6px below the copy host's 2px sticky inset (Attach clears its Margin).
        _plain.Padding = new Thickness(10, Math.Max(23.6, _copyHost.Height + 2.6), 10, 8);
    }

    private void RecomputeEditorHeight(TextEditor editor)
    {
        // Release the previous zoom's explicit height. Measuring the real editor includes
        // its padding and any horizontal scrollbar, unlike the detached plain proxy.
        editor.Height = double.NaN;
        editor.Measure(new Size(Math.Max(1, _container.ActualWidth), double.PositiveInfinity));
        editor.Height = Math.Min(MaximumHeight, editor.DesiredSize.Height);
    }

    internal static TranscriptCodeBlockPreview? Find(DependencyObject? source)
    {
        for (var current = source; current is not null;)
        {
            if (current.GetValue(PreviewProperty) is TranscriptCodeBlockPreview attached)
                return attached;
            if (current is Grid { Tag: TranscriptCodeBlockPreview preview })
                return preview;
            current = current is Visual
                ? VisualTreeHelper.GetParent(current)
                : (current as FrameworkContentElement)?.Parent;
        }
        return null;
    }

    internal static void ApplyTheme(Dispatcher dispatcher, RegionEditorPalette palette)
    {
        dispatcher.VerifyAccess();
        if (Pools.TryGetValue(dispatcher, out var pool))
            pool.ApplyPalette(palette);
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        FrameworkElement? viewport = null;
        for (DependencyObject? current = _container; current is not null; current = VisualTreeHelper.GetParent(current))
        {
            if (current is ScrollContentPresenter presenter)
                viewport ??= presenter;
            if (current is not ScrollViewer viewer)
                continue;
            _observer = Observers.GetValue(viewer, sv => new ViewportObserver(sv, viewport ?? sv));
            _observer.Add(this);
            break;
        }
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        _observer?.Remove(this);
        _observer = null;
        if (_editor is not null)
        {
            var editor = _editor;
            _pool?.Editors.Remove(editor);
            editor.TextArea.SelectionChanged -= OnEditorSelectionChanged;
            _plain.Select(editor.SelectionStart, editor.SelectionLength);
            _container.Children.Remove(editor);
            _editor = null;
            editor.ContextMenu = null;
            editor.ClearValue(PreviewProperty);
            editor.SyntaxHighlighting = null;
            editor.Document = null;
            _container.Children.Insert(0, _plain);
        }
        _pool = null;
    }

    private bool Intersects(FrameworkElement viewport)
    {
        if (!_container.IsLoaded || !_container.IsVisible || _container.ActualHeight <= 0 ||
            !_container.IsDescendantOf(viewport))
            return false;
        var bounds = _container.TransformToAncestor(viewport)
            .TransformBounds(new Rect(_container.RenderSize));
        var intersection = Rect.Intersect(bounds, new Rect(viewport.RenderSize));
        return !intersection.IsEmpty && intersection.Width > 0 && intersection.Height > 0;
    }

    private void Upgrade()
    {
        if (_editor is not null || _grammar is null)
            return;
        _pool = Pools.GetValue(_container.Dispatcher,
            _ => new TranscriptHighlightingPool(RegionHighlightingEngine.Default,
                new ThemeResourcePaletteSource().CreatePalette()));
        var editor = new TextEditor
        {
            IsReadOnly = true,
            FontFamily = _plain.FontFamily,
            FontSize = _plain.FontSize,
            Padding = _plain.Padding,
            BorderThickness = new Thickness(0),
            MaxHeight = MaximumHeight,
            WordWrap = false,
            ShowLineNumbers = false,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            Tag = "codeblock",
            ContextMenu = _plain.ContextMenu,
        };
        editor.Options.EnableHyperlinks = false;
        editor.Options.EnableEmailHyperlinks = false;
        editor.Document.UndoStack.SizeLimit = 0;
        editor.Text = _plain.Text;
        editor.SyntaxHighlighting = _pool.GetDefinition(_grammar);
        editor.SetResourceReference(Control.BackgroundProperty, "CodeSurface");
        editor.Foreground = _pool.Palette.Text;
        ReadOnlyTextInputGuard.DisableInputMethod(editor);
        AvalonEditClipboardGuard.Attach(editor);
        editor.SetValue(PreviewProperty, this);
        editor.TextArea.SelectionChanged += OnEditorSelectionChanged;
        CommandManager.AddPreviewExecutedHandler(editor.TextArea, (_, args) =>
        {
            if (args.Command != ApplicationCommands.Copy)
                return;
            CopyableCodeBlockBuilder.TrySetClipboard(GetCopyText(true));
            args.Handled = true;
        });
        var selectionStart = _plain.SelectionStart;
        var selectionLength = _plain.SelectionLength;
        var horizontalOffset = _plain.HorizontalOffset;
        var verticalOffset = _plain.VerticalOffset;
        _editor = editor;
        _pool.Editors.Add(editor);
        _container.Children.Remove(_plain);
        _container.Children.Insert(0, editor);
        RecomputeEditorHeight(editor);
        editor.Select(selectionStart, selectionLength);
        editor.ScrollToHorizontalOffset(horizontalOffset);
        editor.ScrollToVerticalOffset(verticalOffset);
    }

    private void OnEditorSelectionChanged(object? sender, EventArgs e)
    {
        if (_syncingSelection || _editor is null)
            return;
        _syncingSelection = true;
        try { _plain.Select(_editor.SelectionStart, _editor.SelectionLength); }
        finally { _syncingSelection = false; }
    }

    private void OnMouseWheel(object sender, MouseWheelEventArgs e)
    {
        if (e.Handled || Keyboard.Modifiers != ModifierKeys.None || e.Delta == 0)
            return;
        var offset = _editor?.VerticalOffset ?? _plain.VerticalOffset;
        var extent = _editor?.ExtentHeight ?? _plain.ExtentHeight;
        var viewport = _editor?.ViewportHeight ?? _plain.ViewportHeight;
        e.Handled = true;
        if (!AvalonEditRegionEditor.ShouldBubbleMouseWheel(e.Delta, offset, extent, viewport))
        {
            var lines = SystemParameters.WheelScrollLines;
            var distance = lines < 0 ? viewport : lines * Surface.FontSize * 1.3;
            var target = offset - e.Delta / (double)Mouse.MouseWheelDeltaForOneLine * distance;
            if (_editor is not null) _editor.ScrollToVerticalOffset(target);
            else _plain.ScrollToVerticalOffset(target);
            return;
        }
        // Start outside BOTH child ScrollViewers. Mark the original handled so only this
        // single bubbling event reaches the transcript. Ctrl+wheel stays with host zoom.
        _container.RaiseEvent(new MouseWheelEventArgs(e.MouseDevice, e.Timestamp, e.Delta)
        {
            RoutedEvent = UIElement.MouseWheelEvent,
            Source = _container,
        });
    }

    private sealed class ViewportObserver(ScrollViewer viewer, FrameworkElement viewport)
    {
        private readonly HashSet<TranscriptCodeBlockPreview> _blocks = [];
        private DispatcherOperation? _pending;

        internal void Add(TranscriptCodeBlockPreview block)
        {
            if (!_blocks.Add(block))
                return;
            if (_blocks.Count == 1)
            {
                viewer.ScrollChanged += OnScrollChanged;
                viewport.SizeChanged += OnSizeChanged;
                viewport.IsVisibleChanged += OnVisibilityChanged;
            }
            Schedule();
        }

        internal void Remove(TranscriptCodeBlockPreview block)
        {
            _blocks.Remove(block);
            if (_blocks.Count != 0)
                return;
            viewer.ScrollChanged -= OnScrollChanged;
            viewport.SizeChanged -= OnSizeChanged;
            viewport.IsVisibleChanged -= OnVisibilityChanged;
            _pending?.Abort();
            _pending = null;
        }

        private void OnScrollChanged(object sender, ScrollChangedEventArgs e)
        {
            if (ReferenceEquals(e.OriginalSource, viewer))
                Schedule();
        }
        private void OnSizeChanged(object sender, SizeChangedEventArgs e) => Schedule();
        private void OnVisibilityChanged(object sender, DependencyPropertyChangedEventArgs e) => Schedule();

        private void Schedule()
        {
            if (_pending is not null)
                return;
            // Let transcript append/scroll-to-end and layout settle first; one upgrade per
            // dispatcher turn leaves input responsive even when many small blocks enter.
            _pending = viewer.Dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() =>
            {
                _pending = null;
                var next = _blocks.FirstOrDefault(block => block._editor is null && block.Intersects(viewport));
                if (next is null)
                    return;
                next.Upgrade();
                Schedule();
            }));
        }
    }
}

/// <summary>UI-thread-owned grammar clones; document/highlighter state is never pooled.</summary>
internal sealed class TranscriptHighlightingPool(RegionHighlightingEngine engine, RegionEditorPalette palette)
{
    private readonly Dictionary<string, RegionHighlightingDefinition> _definitions = new(StringComparer.OrdinalIgnoreCase);
    internal RegionEditorPalette Palette { get; private set; } = palette;
    internal HashSet<TextEditor> Editors { get; } = [];

    internal RegionHighlightingDefinition GetDefinition(string canonicalGrammar)
    {
        if (!_definitions.TryGetValue(canonicalGrammar, out var definition))
            _definitions.Add(canonicalGrammar, definition = engine.CreateDefinition(canonicalGrammar, Palette));
        return definition;
    }

    internal void ApplyPalette(RegionEditorPalette palette)
    {
        Palette = palette;
        foreach (var definition in _definitions.Values)
            definition.ApplyPalette(palette);
        foreach (var editor in Editors)
        {
            editor.Foreground = palette.Text;
            editor.TextArea.TextView.Redraw();
        }
    }
}
