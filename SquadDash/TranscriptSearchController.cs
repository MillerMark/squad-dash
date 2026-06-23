using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;

namespace SquadDash;

/// <summary>
/// Encapsulates all transcript search state and logic, extracted from MainWindow.
/// Owns search fields, adorner lifecycle, match navigation, and search UI updates.
/// </summary>
internal sealed class TranscriptSearchController
{
    // ── UI elements ──────────────────────────────────────────────────────────
    private readonly TextBox _searchBox;
    private readonly Action _focusPromptTextBox;
    private readonly ButtonBase _findPrevButton;
    private readonly ButtonBase _findNextButton;
    private readonly TextBlock _searchMatchCountText;
    private readonly ButtonBase _clearSearchButton;
    private readonly RichTextBox _outputTextBox;

    // ── Services ─────────────────────────────────────────────────────────────
    private readonly Dispatcher _dispatcher;
    private readonly TranscriptConversationManager _conversationManager;
    private readonly AgentThreadRegistry _agentThreadRegistry;

    // ── MainWindow callbacks ──────────────────────────────────────────────────
    private readonly Func<TranscriptThreadState?> _getSelectedThread;
    private readonly Func<TranscriptThreadState> _getCoordinatorThread;
    private readonly Action<TranscriptThreadState> _selectTranscriptThread;
    private readonly Func<RichTextBox> _getActiveTranscriptBox;
    private readonly Func<TranscriptScrollController> _getActiveScrollController;
    private readonly Func<ScrollViewer?> _getTranscriptScrollViewer;
    private readonly Action<TranscriptThreadState> _flashGlowHighlightForThread;
    private readonly Action _syncPromptNavButtons;
    private readonly Action<string, Exception> _handleException;

    // ── Transcript search state ─────────────────────────────────────────────────
    private IReadOnlyList<TurnSearchMatch> _searchMatches = [];
    private int _searchMatchCursor = -1;
    private CancellationTokenSource? _searchCts;
    private DispatcherTimer? _searchDebounceTimer;
    private SearchHighlightAdorner? _searchAdorner;
    private ScrollbarMarkerAdorner? _scrollbarAdorner;
    // Pointer cache — built on first RefreshAdornerHighlights after a search, reused on Next/Prev.
    private List<(TextPointer Start, TextPointer End, string Text)>? _cachedSearchPointers;
    private int[] _cachedMatchToCursor = [];  // match i → index in _cachedSearchPointers, -1 if BUC/skip
    private TextPointer?[] _cachedMatchScrollPointer = [];  // match i → pointer to scroll to
    private TextBlock?[] _cachedMatchBucCell = [];  // match i → BUC table cell, null if not a BUC match
    // TextBlocks inside table cells that currently carry a search-highlight background.
    private readonly HashSet<TextBlock> _bucHighlightedCells = [];
    private ScrollBar? _transcriptScrollBar;

    /// <summary>Disposes search resources. Call on window close.</summary>
    internal void Dispose() => _searchAdorner?.Dispose();

    // ── Properties ───────────────────────────────────────────────────────────

    /// <summary>Set true while navigating to a match in a different thread to suppress search-state clear.</summary>
    public bool IsSearchNavigating { get; internal set; }

    /// <summary>True when a search query is active (text entered or nav buttons visible).</summary>
    public bool IsSearchActive => _searchBox.Text.Length > 0 || _findNextButton.Visibility == Visibility.Visible;

    /// <summary>True when the search box has keyboard focus.</summary>
    public bool IsSearchBoxFocused => _searchBox?.IsKeyboardFocusWithin == true;

    // ── Constructor ───────────────────────────────────────────────────────────

    internal TranscriptSearchController(
        Dispatcher dispatcher,
        TextBox searchBox,
        Action focusPromptTextBox,
        ButtonBase findPrevButton,
        ButtonBase findNextButton,
        TextBlock searchMatchCountText,
        ButtonBase clearSearchButton,
        RichTextBox outputTextBox,
        TranscriptConversationManager conversationManager,
        AgentThreadRegistry agentThreadRegistry,
        Func<TranscriptThreadState?> getSelectedThread,
        Func<TranscriptThreadState> getCoordinatorThread,
        Action<TranscriptThreadState> selectTranscriptThread,
        Func<RichTextBox> getActiveTranscriptBox,
        Func<TranscriptScrollController> getActiveScrollController,
        Func<ScrollViewer?> getTranscriptScrollViewer,
        Action<TranscriptThreadState> flashGlowHighlightForThread,
        Action syncPromptNavButtons,
        Action<string, Exception> handleException)
    {
        _dispatcher = dispatcher;
        _searchBox = searchBox;
        _focusPromptTextBox = focusPromptTextBox;
        _findPrevButton = findPrevButton;
        _findNextButton = findNextButton;
        _searchMatchCountText = searchMatchCountText;
        _clearSearchButton = clearSearchButton;
        _outputTextBox = outputTextBox;
        _conversationManager = conversationManager;
        _agentThreadRegistry = agentThreadRegistry;
        _getSelectedThread = getSelectedThread;
        _getCoordinatorThread = getCoordinatorThread;
        _selectTranscriptThread = selectTranscriptThread;
        _getActiveTranscriptBox = getActiveTranscriptBox;
        _getActiveScrollController = getActiveScrollController;
        _getTranscriptScrollViewer = getTranscriptScrollViewer;
        _flashGlowHighlightForThread = flashGlowHighlightForThread;
        _syncPromptNavButtons = syncPromptNavButtons;
        _handleException = handleException;
    }

    // ── Public methods ────────────────────────────────────────────────────────

    /// <summary>Wires search box and navigation button event handlers.</summary>
    internal void WireEventHandlers()
    {
        _searchBox.TextChanged += (_, _) =>
        {
            try
            {
                _searchDebounceTimer?.Stop();
                _searchDebounceTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(250) };
                _searchDebounceTimer.Tick += async (_, _) =>
                {
                    try
                    {
                        _searchDebounceTimer?.Stop();
                        _searchDebounceTimer = null;
                        await ExecuteSearchAsync(_searchBox.Text);
                    }
                    catch (Exception ex)
                    {
                        _handleException("SearchDebounceTimer.Tick", ex);
                    }
                };
                _searchDebounceTimer.Start();
            }
            catch (Exception ex)
            {
                _handleException("SearchBox.TextChanged", ex);
            }
        };

        _searchBox.KeyDown += async (_, e) =>
        {
            try
            {
                if (e.Key == Key.Enter)
                {
                    await NavigateToMatchAsync(_searchMatchCursor + 1);
                    e.Handled = true;
                }
                else if (e.Key == Key.Escape)
                {
                    ClearSearch();
                    _ = _dispatcher.BeginInvoke(DispatcherPriority.Input, () => _focusPromptTextBox());
                    e.Handled = true;
                }
            }
            catch (Exception ex)
            {
                _handleException("SearchBox.KeyDown", ex);
            }
        };

        _findPrevButton.Click += async (_, _) =>
        {
            try
            {
                await NavigateToMatchAsync(_searchMatchCursor - 1);
            }
            catch (Exception ex)
            {
                _handleException("FindPrevButton.Click", ex);
            }
        };

        _findNextButton.Click += async (_, _) =>
        {
            try
            {
                await NavigateToMatchAsync(_searchMatchCursor + 1);
            }
            catch (Exception ex)
            {
                _handleException("FindNextButton.Click", ex);
            }
        };
    }

    /// <summary>
    /// Initialises the search highlight adorner and scrollbar marker adorner onto
    /// <c>OutputTextBox</c>.  Idempotent — safe to call multiple times.
    /// </summary>
    internal void EnsureAdornersInitialized()
    {
        if (_searchAdorner is null)
        {
            var adornerLayer = AdornerLayer.GetAdornerLayer(_outputTextBox);
            if (adornerLayer is not null)
            {
                _searchAdorner = new SearchHighlightAdorner(_outputTextBox);
                adornerLayer.Add(_searchAdorner);
            }
        }

        if (_scrollbarAdorner is null)
        {
            var outputScrollViewer = FindScrollViewer(_outputTextBox);
            if (outputScrollViewer is not null)
            {
                _transcriptScrollBar =
                    outputScrollViewer.Template?.FindName("PART_VerticalScrollBar", outputScrollViewer) as ScrollBar
                    ?? FindVerticalScrollBar(outputScrollViewer);

                if (_transcriptScrollBar is not null)
                {
                    var sbLayer = AdornerLayer.GetAdornerLayer(_transcriptScrollBar);
                    if (sbLayer is not null)
                    {
                        _scrollbarAdorner = new ScrollbarMarkerAdorner(_transcriptScrollBar);
                        sbLayer.Add(_scrollbarAdorner);
                    }
                }
            }
        }
    }

    /// <summary>Forces the search highlight adorner to repaint.</summary>
    internal void InvalidateAdornerHighlights() => _searchAdorner?.InvalidateHighlights();

    /// <summary>Forces the scrollbar marker adorner to repaint.</summary>
    internal void InvalidateScrollbarAdorner() => _scrollbarAdorner?.InvalidateVisual();

    /// <summary>Navigates to the previous search match.</summary>
    internal void NavigatePrev() => _ = NavigateToMatchAsync(_searchMatchCursor - 1);

    /// <summary>Navigates to the next search match.</summary>
    internal void NavigateNext() => _ = NavigateToMatchAsync(_searchMatchCursor + 1);

    /// <summary>Clears the active search and resets all search state.</summary>
    internal void ClearSearch()
    {
        _searchCts?.Cancel();
        _searchDebounceTimer?.Stop();
        _searchMatches = [];
        _searchMatchCursor = 0;
        _searchBox.Text = string.Empty;
        _searchAdorner?.Clear();
        _scrollbarAdorner?.Clear();
        _cachedSearchPointers = null;
        ClearBucCellHighlights();
        UpdateSearchUi();
    }

    /// <summary>
    /// Clears search state when the active transcript thread is switched.
    /// No-op while <see cref="IsSearchNavigating"/> is true (the controller
    /// owns the switch in that case and will restore state itself).
    /// </summary>
    internal void ClearSearchStateOnThreadSwitch()
    {
        if (IsSearchNavigating) return;
        _searchMatches = [];
        _searchMatchCursor = -1;
        _searchAdorner?.Clear();
        _scrollbarAdorner?.Clear();
        _cachedSearchPointers = null;
        ClearBucCellHighlights();
        if (!string.IsNullOrEmpty(_searchBox.Text))
            _searchBox.Text = string.Empty;
        UpdateSearchUi();
    }

    /// <summary>Focuses the search box and selects all text.</summary>
    internal void FocusAndSelectAll()
    {
        _searchBox?.Focus();
        _searchBox?.SelectAll();
    }

    /// <summary>
    /// Recomputes proportional positions for the scrollbar marker adorner using
    /// the cached search pointer list and the current transcript scroll viewer extent.
    /// </summary>
    internal void RefreshScrollbarMarkerPositions()
    {
        if (_scrollbarAdorner is null || _getTranscriptScrollViewer() is not { } sv)
            return;

        var pointers = _cachedSearchPointers;
        if (pointers is null || pointers.Count == 0)
        {
            _scrollbarAdorner.Clear();
            return;
        }

        var totalHeight = sv.ExtentHeight;
        if (totalHeight <= 0)
        {
            _scrollbarAdorner.Clear();
            return;
        }

        var positions = new List<double>(pointers.Count);
        foreach (var (s, _, _) in pointers)
        {
            if (s is null) continue;
            var rect = s.GetCharacterRect(LogicalDirection.Forward);
            if (rect.IsEmpty) continue;
            var docY = rect.Top + sv.VerticalOffset;
            positions.Add(docY / totalHeight);
        }
        _scrollbarAdorner.SetPositions(positions);
    }

    // ── Private search methods ────────────────────────────────────────────────

    /// <summary>
    /// Runs a search against the active transcript, updates match state, and
    /// navigates to the first result.  Cancels any in-flight search first so
    /// rapid typing does not stack results.
    /// </summary>
    private async System.Threading.Tasks.Task ExecuteSearchAsync(string query)
    {
        var previous = _searchCts;
        _searchCts = null;
        previous?.Cancel();
        previous?.Dispose();

        if (string.IsNullOrEmpty(query) || query.Length < 3)
        {
            _searchMatches = [];
            _searchMatchCursor = -1;
            _searchAdorner?.Clear();
            _scrollbarAdorner?.Clear();
            _cachedSearchPointers = null;
            ClearBucCellHighlights();
            UpdateSearchUi();
            return;
        }

        var cts = new CancellationTokenSource();
        _searchCts = cts;
        _cachedSearchPointers = null;

        try
        {
            var coordinatorMatches = await _conversationManager.SearchTurnsAsync(query, cts.Token);
            if (cts.IsCancellationRequested) return;

            _searchMatches = coordinatorMatches;
            _searchMatchCursor = coordinatorMatches.Count > 0 ? 0 : -1;
            UpdateSearchUi();

            var allMatches = new List<TurnSearchMatch>(coordinatorMatches);
            foreach (var agentThread in _agentThreadRegistry.ThreadOrder)
            {
                if (cts.IsCancellationRequested) return;
                var agentMatches = SearchAgentThread(agentThread, query, agentThread);
                allMatches.AddRange(agentMatches);
            }

            if (cts.IsCancellationRequested) return;

            allMatches.Sort((a, b) =>
            {
                DateTimeOffset TimestampOf(TurnSearchMatch m)
                {
                    if (m.Thread is null)
                        return _conversationManager.GetCoordinatorTurnStartedAt(m.TurnIndex) ?? DateTimeOffset.MinValue;
                    var savedTurns = m.Thread.SavedTurns;
                    return m.TurnIndex >= 0 && m.TurnIndex < savedTurns.Count
                        ? savedTurns[m.TurnIndex].StartedAt
                        : m.Thread.StartedAt;
                }
                return TimestampOf(a).CompareTo(TimestampOf(b));
            });

            _searchMatches = allMatches;
            _searchMatchCursor = allMatches.Count > 0 ? 0 : -1;
            UpdateSearchUi();

            if (allMatches.Count > 0)
                await NavigateToMatchAsync(0);
        }
        catch (OperationCanceledException)
        {
            // Expected when superseded by a newer search — safe to ignore.
        }
        catch (Exception ex)
        {
            SquadDashTrace.Write("Search", $"ExecuteSearchAsync failed: {ex.Message}");
        }
    }

    /// <summary>
    /// Searches <paramref name="thread"/>.SavedTurns for <paramref name="query"/>
    /// using the same case-insensitive excerpt format as
    /// <see cref="TranscriptConversationManager.SearchTurnsAsync"/>.
    /// </summary>
    private static IReadOnlyList<TurnSearchMatch> SearchAgentThread(
        TranscriptThreadState thread, string query, TranscriptThreadState? sourceThread = null)
    {
        if (string.IsNullOrEmpty(query))
            return [];

        var results = new List<TurnSearchMatch>();
        const StringComparison cmp = StringComparison.OrdinalIgnoreCase;
        const int MaxExcerptLength = 120;
        const int ExcerptPad = 40;

        for (var i = 0; i < thread.SavedTurns.Count; i++)
        {
            var turn = thread.SavedTurns[i];
            ScanSearchField(turn.Prompt ?? string.Empty, "user", i, query, cmp, MaxExcerptLength, ExcerptPad, results, sourceThread);
            ScanSearchField(turn.ResponseText ?? string.Empty, "assistant", i, query, cmp, MaxExcerptLength, ExcerptPad, results, sourceThread);
        }

        return results;
    }

    private static void ScanSearchField(
        string text,
        string role,
        int turnIndex,
        string query,
        StringComparison cmp,
        int maxExcerptLength,
        int excerptPad,
        List<TurnSearchMatch> results,
        TranscriptThreadState? thread = null)
    {
        var searchFrom = 0;
        while (searchFrom < text.Length)
        {
            var offset = text.IndexOf(query, searchFrom, cmp);
            if (offset < 0) break;

            var excerptStart = Math.Max(0, offset - excerptPad);
            var excerptEnd = Math.Min(text.Length, offset + query.Length + excerptPad);
            var rawExcerpt = text[excerptStart..excerptEnd];

            string excerpt;
            if (rawExcerpt.Length > maxExcerptLength)
            {
                excerpt = rawExcerpt[..maxExcerptLength] + "…";
            }
            else
            {
                var prefix = excerptStart > 0 ? "…" : string.Empty;
                var suffix = excerptEnd < text.Length ? "…" : string.Empty;
                excerpt = prefix + rawExcerpt + suffix;
            }

            results.Add(new TurnSearchMatch(turnIndex, role, excerpt, offset, thread));
            searchFrom = offset + query.Length;
        }
    }

    /// <summary>
    /// Navigates to the match at <paramref name="index"/> (wraps around),
    /// ensuring the turn is rendered and scrolling the match into view.
    /// </summary>
    private async System.Threading.Tasks.Task NavigateToMatchAsync(int index)
    {
        if (_searchMatches.Count == 0) return;

        index = ((index % _searchMatches.Count) + _searchMatches.Count) % _searchMatches.Count;
        _searchMatchCursor = index;
        UpdateSearchUi();

        var match = _searchMatches[index];
        var matchThread = match.Thread;

        var activeThread = _getSelectedThread() ?? _getCoordinatorThread();
        var targetThread = matchThread ?? _getCoordinatorThread();
        if (!ReferenceEquals(activeThread, targetThread))
        {
            IsSearchNavigating = true;
            try
            {
                _selectTranscriptThread(targetThread);
                _cachedSearchPointers = null;
                await _dispatcher.BeginInvoke(DispatcherPriority.Loaded, static () => { }).Task;
            }
            finally
            {
                IsSearchNavigating = false;
            }
            activeThread = targetThread;

            _flashGlowHighlightForThread(targetThread);
        }

        if (_cachedSearchPointers is not null
            && (activeThread.Kind != TranscriptThreadKind.Coordinator
                || _conversationManager.IsTurnRendered(match.TurnIndex)))
        {
            var cursorInList = index < _cachedMatchToCursor.Length ? _cachedMatchToCursor[index] : -1;
            _searchAdorner?.UpdateCurrentIndex(cursorInList);
            UpdateBucActiveHighlight(index);
            ScrollToMatchPointerIfNeeded(
                index < _cachedMatchScrollPointer.Length ? _cachedMatchScrollPointer[index] : null);
            _syncPromptNavButtons();
            return;
        }

        if (activeThread.Kind == TranscriptThreadKind.Coordinator && matchThread is null)
        {
            if (!_conversationManager.IsTurnRendered(match.TurnIndex))
                _cachedSearchPointers = null;

            var savedNavigating = IsSearchNavigating;
            IsSearchNavigating = true;
            try
            {
                await _conversationManager.EnsureTurnRenderedAsync(match.TurnIndex);
            }
            finally
            {
                IsSearchNavigating = savedNavigating;
            }
        }

        _ = _dispatcher.BeginInvoke(DispatcherPriority.Loaded, RefreshAdornerHighlights);
    }

    /// <summary>
    /// Walks the FlowDocument forward from <paramref name="start"/>, returning a
    /// <see cref="TextRange"/> spanning the first case-insensitive occurrence of
    /// <paramref name="searchText"/>, or <c>null</c> if not found.
    /// </summary>
    private static TextRange? FindTextFromPointer(TextPointer start, string searchText)
    {
        if (string.IsNullOrEmpty(searchText)) return null;

        var navigator = start;
        while (navigator != null)
        {
            if (navigator.GetPointerContext(LogicalDirection.Forward) == TextPointerContext.Text)
            {
                var runText = navigator.GetTextInRun(LogicalDirection.Forward);
                var idx = runText.IndexOf(searchText, StringComparison.OrdinalIgnoreCase);
                if (idx >= 0)
                {
                    var matchStart = navigator.GetPositionAtOffset(idx, LogicalDirection.Forward);
                    var matchEnd = matchStart?.GetPositionAtOffset(searchText.Length, LogicalDirection.Forward);
                    if (matchStart != null && matchEnd != null)
                        return new TextRange(matchStart, matchEnd);
                }
            }
            navigator = navigator.GetNextContextPosition(LogicalDirection.Forward);
        }
        return null;
    }

    /// <summary>
    /// Stateful forward-walking search helper that correctly handles
    /// <see cref="BlockUIContainer"/> elements (rendered code blocks and tables).
    /// </summary>
    private sealed class SearchWalker
    {
        private TextPointer? _cursor;
        private int _pendingBucCount;
        private TextPointer? _pendingBucEnd;
        private BlockUIContainer? _lastBucElement;
        private int _lastBucOccurrenceIndex;
        private int _lastBucTotalCount;

        public BlockUIContainer? LastBucElement => _lastBucElement;
        public int LastBucOccurrenceIndex => _lastBucOccurrenceIndex;

        public SearchWalker(TextPointer start) => _cursor = start;

        /// <summary>
        /// Returns the <see cref="TextRange"/> of the next occurrence of
        /// <paramref name="searchText"/>, or <c>null</c> when exhausted.
        /// A <b>zero-length</b> range (Start == End) signals a match inside a
        /// <see cref="BlockUIContainer"/> — the cursor is advanced correctly
        /// but the range cannot be drawn by the adorner.
        /// </summary>
        public TextRange? FindNext(string searchText)
        {
            if (_pendingBucCount > 0)
            {
                _pendingBucCount--;
                _lastBucOccurrenceIndex = _lastBucTotalCount - _pendingBucCount - 1;
                return _pendingBucEnd is not null
                    ? new TextRange(_pendingBucEnd, _pendingBucEnd)
                    : null;
            }

            if (_cursor is null) return null;

            var nav = _cursor;
            while (nav is not null)
            {
                var ctx = nav.GetPointerContext(LogicalDirection.Forward);

                if (ctx == TextPointerContext.Text)
                {
                    var runText = nav.GetTextInRun(LogicalDirection.Forward);
                    var idx = runText.IndexOf(searchText, StringComparison.OrdinalIgnoreCase);
                    if (idx >= 0)
                    {
                        var matchStart = nav.GetPositionAtOffset(idx, LogicalDirection.Forward);
                        var matchEnd = matchStart?.GetPositionAtOffset(searchText.Length, LogicalDirection.Forward);
                        if (matchStart is not null && matchEnd is not null)
                        {
                            _cursor = matchEnd;
                            return new TextRange(matchStart, matchEnd);
                        }
                    }
                }
                else if (ctx == TextPointerContext.ElementStart)
                {
                    var elem = nav.GetAdjacentElement(LogicalDirection.Forward);
                    if (elem is BlockUIContainer buc)
                    {
                        var bucText = GetBlockUIContainerText(buc);
                        if (!string.IsNullOrEmpty(bucText))
                        {
                            var count = CountOccurrences(bucText, searchText);
                            if (count > 0)
                            {
                                var bucEnd = buc.ContentEnd;
                                _cursor = bucEnd;
                                _pendingBucEnd = bucEnd;
                                _pendingBucCount = count - 1;
                                _lastBucElement = buc;
                                _lastBucOccurrenceIndex = 0;
                                _lastBucTotalCount = count;
                                return new TextRange(bucEnd, bucEnd);
                            }
                        }
                    }
                }

                nav = nav.GetNextContextPosition(LogicalDirection.Forward);
            }

            _cursor = null;
            return null;
        }

        private static string? GetBlockUIContainerText(BlockUIContainer buc) =>
            buc.Child switch
            {
                StackPanel { Tag: string tableText } => tableText,
                TextBox tb => tb.Text,
                _ => null,
            };

        private static int CountOccurrences(string text, string search)
        {
            var count = 0;
            var from = 0;
            while (true)
            {
                var idx = text.IndexOf(search, from, StringComparison.OrdinalIgnoreCase);
                if (idx < 0) break;
                count++;
                from = idx + search.Length;
            }
            return count;
        }
    }

    /// <summary>
    /// Rebuilds the adorner highlight list from all currently-rendered matches and
    /// updates the current-match index.
    /// </summary>
    private void RefreshAdornerHighlights()
    {
        if (_searchAdorner is null) return;

        var query = _searchBox.Text;
        if (_searchMatches.Count == 0 || string.IsNullOrEmpty(query) || query.Length < 3)
        {
            _searchAdorner.Clear();
            _scrollbarAdorner?.Clear();
            _cachedSearchPointers = null;
            ClearBucCellHighlights();
            return;
        }

        var activeThread = _getSelectedThread() ?? _getCoordinatorThread();
        var pointers = new List<(TextPointer Start, TextPointer End, string Text)>(_searchMatches.Count);
        var cursorInList = -1;

        var matchToCursor = new int[_searchMatches.Count];
        var matchScrollPointer = new TextPointer?[_searchMatches.Count];
        var matchBucCell = new TextBlock?[_searchMatches.Count];
        Array.Fill(matchToCursor, -1);

        ClearBucCellHighlights();

        var walkerByKey = new Dictionary<(int TurnIndex, string Role), SearchWalker>();
        TextPointer? currentMatchPointer = null;

        for (var i = 0; i < _searchMatches.Count; i++)
        {
            var match = _searchMatches[i];
            var key = (match.TurnIndex, match.TurnRole);

            if (!walkerByKey.TryGetValue(key, out var walker))
            {
                var matchThread = match.Thread ?? _getCoordinatorThread();
                if (!ReferenceEquals(matchThread, activeThread))
                {
                    matchToCursor[i] = -1;
                    continue;
                }
                var searchFrom = GetSearchFromPointerSync(match, activeThread);
                if (searchFrom is null)
                {
                    SquadDashTrace.Write(TraceCategory.UI,
                        $"SEARCH_HIGHLIGHT[{i}] turn={match.TurnIndex} role={match.TurnRole} SKIPPED(unrendered)");
                    continue;
                }
                walker = new SearchWalker(searchFrom);
                walkerByKey[key] = walker;
            }

            var range = walker.FindNext(query);
            if (range is null)
            {
                SquadDashTrace.Write(TraceCategory.UI,
                    $"SEARCH_HIGHLIGHT[{i}] turn={match.TurnIndex} role={match.TurnRole} SKIPPED(walker_exhausted) cursor={i == _searchMatchCursor}");
                continue;
            }

            matchScrollPointer[i] = range.Start;

            if (range.Start.CompareTo(range.End) == 0)
            {
                SquadDashTrace.Write(TraceCategory.UI,
                    $"SEARCH_HIGHLIGHT[{i}] turn={match.TurnIndex} role={match.TurnRole} BUC_MATCH cursor={i == _searchMatchCursor}");
                if (walker.LastBucElement is not null)
                {
                    var bucCell = GetTableCellByOccurrence(walker.LastBucElement, walker.LastBucOccurrenceIndex, query);
                    if (bucCell is not null)
                    {
                        _bucHighlightedCells.Add(bucCell);
                        matchBucCell[i] = bucCell;
                    }
                }
                if (i == _searchMatchCursor)
                    currentMatchPointer = range.Start;
                continue;
            }

            if (i == _searchMatchCursor)
            {
                cursorInList = pointers.Count;
                currentMatchPointer = range.Start;
            }

            matchToCursor[i] = pointers.Count;
            var actualText = new TextRange(range.Start, range.End).Text;
            SquadDashTrace.Write(TraceCategory.UI,
                $"SEARCH_HIGHLIGHT[{i}] turn={match.TurnIndex} role={match.TurnRole} TEXT_MATCH listIdx={pointers.Count} cursor={i == _searchMatchCursor} text='{actualText}'");
            pointers.Add((range.Start, range.End, string.IsNullOrEmpty(actualText) ? query : actualText));
        }

        var bucInactiveBg = GetThemeBrush("SearchHighlight", Color.FromRgb(98, 84, 44));
        var bucActiveBg = GetThemeBrush("SearchHighlightCurrent", Color.FromRgb(255, 229, 122));
        var bucInactiveFg = GetThemeBrush("SearchHighlightText", Color.FromRgb(18, 13, 0));
        var bucActiveFg = GetThemeBrush("SearchHighlightTextCurrent", Color.FromRgb(0, 0, 0));
        foreach (var cell in _bucHighlightedCells)
        {
            cell.Background = bucInactiveBg;
            cell.Foreground = bucInactiveFg;
        }
        if (_searchMatchCursor >= 0 && _searchMatchCursor < matchBucCell.Length
            && matchBucCell[_searchMatchCursor] is { } activeBucCell)
        {
            activeBucCell.Background = bucActiveBg;
            activeBucCell.Foreground = bucActiveFg;
        }

        _searchAdorner.SetMatches(pointers, cursorInList);

        _cachedSearchPointers = pointers;
        _cachedMatchToCursor = matchToCursor;
        _cachedMatchScrollPointer = matchScrollPointer;
        _cachedMatchBucCell = matchBucCell;

        RefreshScrollbarMarkerPositions();

        if (currentMatchPointer is null && pointers.Count > 0)
            currentMatchPointer = pointers[0].Start;
        ScrollToMatchPointerIfNeeded(currentMatchPointer);

        _syncPromptNavButtons();
    }

    private void ScrollToMatchPointerIfNeeded(TextPointer? pointer)
    {
        if (pointer is null) return;
        var activeBox = _getActiveTranscriptBox();
        var sv = activeBox.Template?.FindName("PART_ContentHost", activeBox) as ScrollViewer;
        if (sv is null) return;

        var rect = pointer.GetCharacterRect(LogicalDirection.Forward);
        if (rect.IsEmpty)
        {
            var para = pointer.Paragraph;
            if (para is not null)
                rect = para.ContentStart.GetCharacterRect(LogicalDirection.Forward);
        }

        if (rect.IsEmpty) return;

        var isFullyVisible = rect.Top >= 0 && rect.Bottom <= sv.ViewportHeight;
        SquadDashTrace.Write(TraceCategory.UI,
            $"SEARCH_SCROLL cursor={_searchMatchCursor} rectTop={rect.Top:F0} rectBottom={rect.Bottom:F0} vp={sv.ViewportHeight:F0} offset={sv.VerticalOffset:F0} fullyVisible={isFullyVisible}");
        if (!isFullyVisible)
            _getActiveScrollController().ScrollToOffset(sv.VerticalOffset + rect.Top);
    }

    private void UpdateBucActiveHighlight(int matchIndex)
    {
        var bucInactiveBg = GetThemeBrush("SearchHighlight", Color.FromRgb(98, 84, 44));
        var bucActiveBg = GetThemeBrush("SearchHighlightCurrent", Color.FromRgb(255, 229, 122));
        var bucInactiveFg = GetThemeBrush("SearchHighlightText", Color.FromRgb(18, 13, 0));
        var bucActiveFg = GetThemeBrush("SearchHighlightTextCurrent", Color.FromRgb(0, 0, 0));
        foreach (var cell in _bucHighlightedCells)
        {
            cell.Background = bucInactiveBg;
            cell.Foreground = bucInactiveFg;
        }
        if (_cachedMatchBucCell is not null
            && matchIndex >= 0 && matchIndex < _cachedMatchBucCell.Length
            && _cachedMatchBucCell[matchIndex] is { } active)
        {
            active.Background = bucActiveBg;
            active.Foreground = bucActiveFg;
        }
    }

    private void ClearBucCellHighlights()
    {
        foreach (var cell in _bucHighlightedCells)
        {
            cell.Background = null;
            cell.ClearValue(TextBlock.ForegroundProperty);
        }
        _bucHighlightedCells.Clear();
    }

    private static TextBlock? GetTableCellByOccurrence(BlockUIContainer buc, int occurrenceIndex, string query)
    {
        if (buc.Child is not StackPanel sp) return null;
        var count = 0;
        foreach (var rowChild in sp.Children)
        {
            if (rowChild is not Grid grid) continue;
            foreach (var colChild in grid.Children)
            {
                if (colChild is not Border border || border.Child is not TextBlock tb) continue;
                var cellText = GetTextBlockContent(tb);
                var cellCount = CountSubstringOccurrences(cellText, query);
                if (count + cellCount > occurrenceIndex)
                    return tb;
                count += cellCount;
            }
        }
        return null;
    }

    private static string GetTextBlockContent(TextBlock tb)
    {
        if (tb.Inlines.Count == 0)
            return tb.Text;
        var sb = new StringBuilder();
        AppendInlineText(tb.Inlines, sb);
        return sb.ToString();
    }

    private static void AppendInlineText(InlineCollection inlines, StringBuilder sb)
    {
        foreach (var inline in inlines)
        {
            switch (inline)
            {
                case Run run:
                    sb.Append(run.Text);
                    break;
                case Span span:
                    AppendInlineText(span.Inlines, sb);
                    break;
            }
        }
    }

    private static int CountSubstringOccurrences(string text, string query)
    {
        if (string.IsNullOrEmpty(text) || string.IsNullOrEmpty(query)) return 0;
        var count = 0;
        var idx = 0;
        while ((idx = text.IndexOf(query, idx, StringComparison.OrdinalIgnoreCase)) >= 0)
        {
            count++;
            idx += query.Length;
        }
        return count;
    }

    private static SolidColorBrush MakeFrozenBrush(Color c)
    {
        var b = new SolidColorBrush(c);
        b.Freeze();
        return b;
    }

    private static Brush GetThemeBrush(string key, Color fallback)
        => Application.Current?.Resources[key] as Brush ?? new SolidColorBrush(fallback);

    /// <summary>
    /// Returns the TextPointer from which to begin scanning for <paramref name="match"/>,
    /// using only already-rendered paragraphs (no async rendering).
    /// Returns <c>null</c> if the turn is not yet in the document.
    /// </summary>
    private TextPointer? GetSearchFromPointerSync(TurnSearchMatch match, TranscriptThreadState thread)
    {
        if (thread.Kind == TranscriptThreadKind.Coordinator)
        {
            var startedAt = _conversationManager.GetCoordinatorTurnStartedAt(match.TurnIndex);
            if (!startedAt.HasValue) return null;
            var entry = _getCoordinatorThread().PromptParagraphs.FirstOrDefault(e => e.Timestamp == startedAt.Value);
            if (entry is null) return null;
            return match.TurnRole == "assistant" ? entry.Paragraph.ContentEnd : entry.Paragraph.ContentStart;
        }
        else
        {
            if (match.TurnIndex < 0 || match.TurnIndex >= thread.PromptParagraphs.Count) return null;
            var entry = thread.PromptParagraphs[match.TurnIndex];
            return match.TurnRole == "assistant" ? entry.Paragraph.ContentEnd : entry.Paragraph.ContentStart;
        }
    }

    /// <summary>
    /// Updates the visibility and text of the FindPrev / FindNext buttons and the
    /// match-count label based on current search state.
    /// Must be called on the UI thread.
    /// </summary>
    private void UpdateSearchUi()
    {
        if (string.IsNullOrEmpty(_searchBox.Text))
        {
            _findPrevButton.Visibility = Visibility.Collapsed;
            _findNextButton.Visibility = Visibility.Collapsed;
            _searchMatchCountText.Visibility = Visibility.Collapsed;
            _clearSearchButton.Visibility = Visibility.Collapsed;
        }
        else if (_searchBox.Text.Length < 3)
        {
            _findPrevButton.Visibility = Visibility.Collapsed;
            _findNextButton.Visibility = Visibility.Collapsed;
            _searchMatchCountText.Visibility = Visibility.Visible;
            _searchMatchCountText.Text = "Type at least 3 characters";
            _clearSearchButton.Visibility = Visibility.Visible;
        }
        else if (_searchMatches.Count == 0)
        {
            _findPrevButton.Visibility = Visibility.Collapsed;
            _findNextButton.Visibility = Visibility.Collapsed;
            _searchMatchCountText.Visibility = Visibility.Visible;
            _searchMatchCountText.Text = "No matches";
            _clearSearchButton.Visibility = Visibility.Visible;
        }
        else
        {
            _findPrevButton.Visibility = Visibility.Visible;
            _findNextButton.Visibility = Visibility.Visible;
            _searchMatchCountText.Visibility = Visibility.Visible;
            _searchMatchCountText.Text = $"{_searchMatchCursor + 1} of {_searchMatches.Count}";
            _clearSearchButton.Visibility = Visibility.Visible;
        }
    }

    // ── Private static helpers (copies from MainWindow — used only by search) ──

    private static ScrollViewer? FindScrollViewer(DependencyObject parent)
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i);
            if (child is ScrollViewer sv) return sv;
            var found = FindScrollViewer(child);
            if (found is not null) return found;
        }
        return null;
    }

    private static ScrollBar? FindVerticalScrollBar(DependencyObject parent)
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i);
            if (child is ScrollBar sb && sb.Orientation == Orientation.Vertical) return sb;
            var found = FindVerticalScrollBar(child);
            if (found is not null) return found;
        }
        return null;
    }
}
