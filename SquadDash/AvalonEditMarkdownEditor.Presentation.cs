using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using ICSharpCode.AvalonEdit;
using ICSharpCode.AvalonEdit.Document;
using ICSharpCode.AvalonEdit.Rendering;

namespace SquadDash;

internal sealed partial class AvalonEditMarkdownEditor
{
    private static readonly DependencyProperty EnvironmentFontSizeProperty = DependencyProperty.Register(
        "EnvironmentFontSize", typeof(double), typeof(AvalonEditMarkdownEditor), new PropertyMetadata(14.0));
    private static readonly DependencyProperty ZoomMultiplierProperty = DependencyProperty.Register(
        "ZoomMultiplier", typeof(double), typeof(AvalonEditMarkdownEditor), new PropertyMetadata(1.0));
    private static event Action<double>? ZoomChanged;
    private readonly ApplicationSettingsStore _settingsStore;
    private SourceHighlightRenderer _sourceHighlight = null!;
    private DispatcherTimer _sourceHighlightTimer = null!;

    private void InitializeZoom()
    {
        SetResourceReference(EnvironmentFontSizeProperty, "FontSizeBody");
        ApplyZoom(_settingsStore.Load().MarkdownEditorZoomMultiplier);
        var binding = new MultiBinding { Converter = new FontMultiplierConverter() };
        binding.Bindings.Add(new Binding { Source = this, Path = new PropertyPath(EnvironmentFontSizeProperty) });
        binding.Bindings.Add(new Binding { Source = this, Path = new PropertyPath(ZoomMultiplierProperty) });
        Editor.SetBinding(TextEditor.FontSizeProperty, binding);
        Editor.PreviewMouseWheel += Editor_PreviewMouseWheel;
        Loaded += Editor_Loaded;
        ZoomChanged += ApplyZoom;
    }

    private void Editor_Loaded(object sender, RoutedEventArgs e) =>
        ApplyZoom(_settingsStore.Load().MarkdownEditorZoomMultiplier);

    private void ApplyZoom(double multiplier) => SetValue(ZoomMultiplierProperty,
        ApplicationSettingsSnapshot.NormalizeMarkdownEditorZoomMultiplier(multiplier));

    private void Editor_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        if ((Keyboard.Modifiers & ModifierKeys.Control) == 0 || e.Delta == 0)
            return;
        e.Handled = true;
        ZoomByWheelDelta(e.Delta);
    }

    internal void ZoomByWheelDelta(int delta)
    {
        ThrowIfDisposed();
        var multiplier = Math.Clamp((double)GetValue(ZoomMultiplierProperty) * Math.Pow(1.1, delta / 120.0), 0.5, 3.0);
        var saved = _settingsStore.SaveMarkdownEditorZoomMultiplier(multiplier);
        ZoomChanged?.Invoke(saved.MarkdownEditorZoomMultiplier);
    }

    private void DisposeZoom()
    {
        ZoomChanged -= ApplyZoom;
        Loaded -= Editor_Loaded;
        Editor.PreviewMouseWheel -= Editor_PreviewMouseWheel;
    }

    private sealed class FontMultiplierConverter : IMultiValueConverter
    {
        public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture) =>
            values[0] is double font && values[1] is double zoom ? font * zoom : DependencyProperty.UnsetValue;

        public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture) =>
            throw new NotSupportedException();
    }

    private void InitializeHighlights()
    {
        _sourceHighlight = new SourceHighlightRenderer(this);
        Editor.TextArea.TextView.BackgroundRenderers.Add(_sourceHighlight);
        _sourceHighlightTimer = new DispatcherTimer(DispatcherPriority.Background, Dispatcher) {
            Interval = TimeSpan.FromSeconds(1)
        };
        _sourceHighlightTimer.Tick += SourceHighlightTimer_Tick;
        Unloaded += Highlight_Unloaded;
        Editor.TextArea.ReadOnlySectionProvider = new RevisionReadOnlyProvider(this);
    }

    public void HighlightSourceLine(string lineHint)
    {
        ClearSourceHighlight();
        if (!int.TryParse(lineHint, NumberStyles.Integer, CultureInfo.InvariantCulture, out var line)
            || line < 1 || line > Editor.Document.LineCount || _isDisposed)
            return;
        _sourceHighlight.Line = line;
        Editor.TextArea.TextView.InvalidateLayer(KnownLayer.Background);
        _sourceHighlightTimer.Start();
    }

    public void ClearSourceHighlight()
    {
        _sourceHighlightTimer.Stop();
        _sourceHighlight.Line = null;
        Editor.TextArea.TextView.InvalidateLayer(KnownLayer.Background);
    }

    private void SourceHighlightTimer_Tick(object? sender, EventArgs e) => ClearSourceHighlight();
    private void Highlight_Unloaded(object sender, RoutedEventArgs e) => ClearSourceHighlight();

    private void DisposeHighlights()
    {
        ClearSourceHighlight();
        _sourceHighlightTimer.Tick -= SourceHighlightTimer_Tick;
        Unloaded -= Highlight_Unloaded;
        Editor.TextArea.TextView.BackgroundRenderers.Remove(_sourceHighlight);
    }

    private sealed class SourceHighlightRenderer(AvalonEditMarkdownEditor owner) : IBackgroundRenderer
    {
        internal int? Line { get; set; }
        public KnownLayer Layer => KnownLayer.Background;

        public void Draw(TextView textView, DrawingContext drawingContext)
        {
            if (!textView.VisualLinesValid)
                return;
            var brush = new SolidColorBrush(AgentStatusCard.IsDarkTheme
                ? Color.FromArgb(60, 255, 220, 80) : Color.FromArgb(50, 100, 180, 255));
            if (Line is int number && number <= textView.Document.LineCount)
                DrawSegment(textView.Document.GetLineByNumber(number), brush);
            foreach (var revision in owner._revisions)
                if (revision.IsPending && revision.TryGetRange(out var start, out var length))
                    DrawSegment(new TextSegment { StartOffset = start, Length = length }, brush);

            void DrawSegment(ISegment segment, Brush fill)
            {
                foreach (var rectangle in BackgroundGeometryBuilder.GetRectsForSegment(textView, segment))
                    drawingContext.DrawRectangle(fill, null, rectangle);
            }
        }
    }
}
