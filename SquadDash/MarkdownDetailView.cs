using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;

namespace SquadDash;

internal sealed record MarkdownDetailStateContext(MarkdownDetailIdentity Identity, MarkdownDetailStateStore Store);

/// <summary>Owns a disclosure's handlers. Hosts must detach the old document on replacement/close.</summary>
internal sealed class MarkdownDetailView : IDisposable
{
    internal Expander Expander { get; }
    internal FlowDocumentScrollViewer Viewer { get; }
    internal string Label { get; }
    internal string? Key { get; }
    private readonly MarkdownDetailStateContext? _state;
    private readonly Action<MarkdownDetailStateStore.Changed> _stateChanged;
    private bool _restoring;
    private bool _disposed;
    private FlowDocument _root;
    internal void BindRoot(FlowDocument root) => _root = root;

    internal MarkdownDetailView(FlowDocument root, FlowDocument body, string label, string? key,
        MarkdownDetailStateContext? state)
    {
        _root = root;
        _state = state;
        Label = label;
        Key = key;
        body.PagePadding = new Thickness(8, 2, 0, 2);
        Viewer = new FlowDocumentScrollViewer
        {
            Document = body,
            VerticalScrollBarVisibility = ScrollBarVisibility.Disabled,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
            IsToolBarVisible = false,
            Padding = new Thickness(0),
            Background = Brushes.Transparent,
        };
        Expander = new Expander
        {
            Header = new TextBlock { Text = label, TextWrapping = TextWrapping.Wrap },
            Content = Viewer,
            IsExpanded = key is not null && state is not null && state.Store.Load(key).IsExpanded,
            HorizontalContentAlignment = HorizontalAlignment.Stretch,
            FontSize = root.FontSize,
            Foreground = root.Foreground,
        };
        Expander.SetResourceReference(FrameworkElement.StyleProperty, "ThemedExpanderStyle");
        Expander.Expanded += OnToggled;
        Expander.Collapsed += OnToggled;
        _stateChanged = OnStateChanged;
        MarkdownDetailStateStore.Changes.Subscribe(_stateChanged);
    }

    private void OnToggled(object sender, RoutedEventArgs e)
    {
        if (!ReferenceEquals(e.OriginalSource, Expander))
            return;
        if (!_restoring && Key is not null && _state is not null)
        {
            var result = _state.Store.Save(Key, Expander.IsExpanded);
            Expander.ToolTip = result.Succeeded ? null : "Detail state could not be saved. See the trace for details.";
        }
        MarkdownVisibleContent.Invalidate(_root);
    }

    private void OnStateChanged(MarkdownDetailStateStore.Changed change)
    {
        if (_disposed || change.Key != Key ||
            !string.Equals(change.FilePath, _state?.Store.FilePath, StringComparison.OrdinalIgnoreCase))
            return;
        if (!Expander.Dispatcher.CheckAccess())
        {
            Expander.Dispatcher.BeginInvoke(() => OnStateChanged(change));
            return;
        }
        // Reload rather than replay a queued notification: the last persisted transition wins.
        var current = _state!.Store.Load(change.Key);
        if (!current.Succeeded || Expander.IsExpanded == current.IsExpanded)
            return;
        _restoring = true;
        try { Expander.IsExpanded = current.IsExpanded; }
        finally { _restoring = false; }
    }

    public void Dispose()
    {
        _disposed = true;
        MarkdownDetailStateStore.Changes.Unsubscribe(_stateChanged);
        Expander.Expanded -= OnToggled;
        Expander.Collapsed -= OnToggled;
    }
}
