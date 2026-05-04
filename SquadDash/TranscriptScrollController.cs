using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media.Animation;
using System.Windows.Threading;

namespace SquadDash;

/// <summary>
/// Centralizes all auto-scroll logic for <c>OutputTextBox</c> (the transcript RichTextBox).
///
/// <para>
/// <b>Motivation.</b> Before this class, scroll requests were scattered across six call
/// sites — <c>AppendLine</c>, <c>AppendText</c>, <c>AppendThinkingText</c>,
/// <c>ScrollTranscriptThread</c>, <c>ScrollToPromptParagraph</c>, and the
/// <c>scrollOutputToEnd</c> delegate passed to <c>TranscriptConversationManager</c>.
/// Each site called <c>Dispatcher.BeginInvoke(DispatcherPriority.Loaded, () =&gt; ScrollToEnd())</c>
/// independently, producing hundreds of stacked dispatcher items per streaming turn.
/// Because those items execute after the layout pass, several could fire against intermediate
/// layout states (e.g., before newly added blocks are measured), causing the scroll thumb to
/// jump to unexpected positions.
/// </para>
///
/// <para>
/// <b>Design contract.</b>
/// <list type="bullet">
///   <item>Exactly one <c>Dispatcher.BeginInvoke</c> is ever queued at a time
///         (<see cref="_pendingScrollRequest"/> debounce flag).</item>
///   <item>Auto-scroll is suppressed while the user has scrolled away from the bottom
///         (<see cref="IsUserScrolledAway"/> gate).</item>
///   <item>Auto-scroll silently resumes the moment the user scrolls back within
///         <see cref="NearBottomThreshold"/> pixels of the bottom — no "Jump to bottom"
///         button is shown. The transcript is a continuous stream, not a chat log of
///         discrete messages, so a button would be visual noise. Silent resume (matching
///         the behaviour of VS Code's terminal and most log viewers) is the right call here.
///   </item>
///   <item>Programmatic scrolls (initiated by this class itself) are bracketed with
///         <see cref="_isProgrammaticScroll"/> so the <c>ScrollChanged</c> handler does
///         not misclassify them as user interaction.</item>
/// </list>
/// </para>
///
/// <para>
/// <b>Threading.</b> All methods must be called on the UI thread. The class holds no
/// cross-thread state.
/// </para>
/// </summary>
internal sealed class TranscriptScrollController
{
    // -------------------------------------------------------------------------
    // Constants
    // -------------------------------------------------------------------------

    /// <summary>
    /// Distance from the bottom (in device-independent pixels) within which the viewport
    /// is considered "at the bottom" for auto-scroll purposes.
    /// </summary>
    private const double NearBottomThreshold = 50.0;

    /// <summary>
    /// Seconds of scroll inactivity after which the floating scroll-to-bottom button
    /// auto-hides itself.
    /// </summary>
    private const double ScrollButtonAutoHideSeconds = 10.0;

    // -------------------------------------------------------------------------
    // Fields
    // -------------------------------------------------------------------------

    private readonly RichTextBox _outputTextBox;
    private readonly Dispatcher _dispatcher;

    /// <summary>
    /// The inner <see cref="ScrollViewer"/> obtained from the
    /// <c>PART_ContentHost</c> template part of <see cref="_outputTextBox"/>.
    /// Null until the first time the template is applied (<see cref="OnOutputTextBoxLoaded"/>).
    /// </summary>
    private ScrollViewer? _sv;

    /// <summary>
    /// True while a <see cref="Dispatcher.BeginInvoke"/> scroll-to-end call is already
    /// queued. Set to <c>true</c> when a request is enqueued, cleared to <c>false</c>
    /// when the queued lambda executes. Prevents multiple concurrent dispatcher items.
    /// </summary>
    private bool _pendingScrollRequest;

    /// <summary>
    /// True when this class itself initiated a programmatic scroll, so that the
    /// <see cref="OnScrollChanged"/> handler does not interpret the resulting
    /// <c>ScrollChanged</c> event as user interaction.
    /// </summary>
    private bool _isProgrammaticScroll;

    /// <summary>
    /// True while the initial transcript history is being loaded from disk into the
    /// <see cref="FlowDocument"/>. When set, <see cref="RequestScrollToEnd"/> and the
    /// extent-grow re-anchor inside <see cref="OnScrollChanged"/> are suppressed so that
    /// O(N) per-turn scroll operations do not fight layout during the load loop.
    /// Cleared by <see cref="EndLoad"/>, which issues exactly one
    /// <see cref="RequestScrollToEnd"/> after all turns have been appended.
    /// </summary>
    private bool _isLoadingTranscript;

    /// <summary>
    /// Optional reference to the floating "scroll to bottom" button overlaid on the
    /// transcript. Set by <see cref="SetScrollToBottomButton"/> after the XAML visual
    /// tree is constructed. Null until wired; all button show/hide calls are silent no-ops
    /// while null.
    /// </summary>
    private Button? _scrollToBottomButton;

    /// <summary>
    /// Fires <see cref="ScrollButtonAutoHideSeconds"/> after the last call to
    /// <see cref="ShowScrollButton"/> to auto-hide the floating button during periods
    /// of user inactivity.
    /// </summary>
    private readonly DispatcherTimer _autoHideTimer;

    /// <summary>
    /// The last distance-from-bottom recorded during a genuine user scroll gesture.
    /// Persists across WPF FlowDocument shrink/re-expand cycles so that
    /// <see cref="OnScrollChanged"/> can restore the viewport to the user's chosen
    /// reading position after the content returns to its original size.
    /// Negative means "not yet recorded" (e.g. user has never scrolled away).
    /// </summary>
    private double _savedDistanceFromBottom = -1;

    /// <summary>
    /// True after a locked viewport has gone through an extent shrink. The next
    /// extent grow should restore the saved reading position. Plain bottom-appends
    /// should not trigger this, otherwise the viewport drifts downward while new
    /// content arrives below the user's current view.
    /// </summary>
    private bool _pendingLockedViewportReanchor;

    /// <summary>
    /// Counts genuine user-scroll events that passed all gates in
    /// <see cref="OnScrollChanged"/>. Used to log the first few scroll events to
    /// the persistent file log for diagnosing scroll-button issues on remote/VM
    /// sessions where the live trace window may not be available.
    /// </summary>
    private int _scrollEventCount;

    /// <summary>
    /// Counts <see cref="OnScrollChanged"/> calls blocked by Gate 2
    /// (<c>ExtentHeightChange != 0</c>). Logged every 20th block to the
    /// file log so we can detect if content-change events are swallowing user scrolls.
    /// </summary>
    private int _gate2BlockCount;

    // -------------------------------------------------------------------------
    // Constructor
    // -------------------------------------------------------------------------

    /// <summary>
    /// Initialises the controller and wires scroll-event handlers.
    /// </summary>
    /// <param name="outputTextBox">
    /// The transcript <see cref="RichTextBox"/>. Must not be null.
    /// The controller subscribes to <c>outputTextBox.Loaded</c> to obtain the inner
    /// <see cref="ScrollViewer"/> after the control template is applied, and then
    /// subscribes to <c>ScrollViewer.ScrollChanged</c> to detect user interaction.
    /// </param>
    /// <param name="dispatcher">
    /// The UI dispatcher used to post deferred scroll operations. Typically
    /// <c>Application.Current.Dispatcher</c> or the window's <c>Dispatcher</c>.
    /// </param>
    public TranscriptScrollController(RichTextBox outputTextBox, Dispatcher dispatcher)
    {
        _outputTextBox = outputTextBox ?? throw new ArgumentNullException(nameof(outputTextBox));
        _dispatcher = dispatcher ?? throw new ArgumentNullException(nameof(dispatcher));

        _autoHideTimer = new DispatcherTimer(DispatcherPriority.Normal, _dispatcher)
        {
            Interval = TimeSpan.FromSeconds(ScrollButtonAutoHideSeconds),
        };
        _autoHideTimer.Tick += OnAutoHideTimerTick;

        // Subscribe to Loaded persistently (not one-shot). WPF re-fires Loaded each time
        // the control's template is re-applied (e.g. after an RDP session reconnect that
        // causes the visual tree to be rebuilt). That gives us a new PART_ContentHost
        // ScrollViewer we must re-subscribe to, or all subsequent scroll events are lost.
        _outputTextBox.Loaded += OnOutputTextBoxLoaded;

        // If the control is already in the visual tree, wire immediately so we don't
        // wait for the next Loaded event.
        if (_outputTextBox.IsLoaded)
            WireScrollViewer();
    }

    // -------------------------------------------------------------------------
    // Public state
    // -------------------------------------------------------------------------

    /// <summary>
    /// Gets a value indicating whether the user has manually scrolled away from the
    /// bottom of the transcript.
    /// When <c>true</c>, <see cref="RequestScrollToEnd"/> is a no-op so that the
    /// user's reading position is not disturbed by incoming streamed content.
    /// Automatically reverts to <c>false</c> when the viewport is scrolled back
    /// within <see cref="NearBottomThreshold"/> pixels of the bottom.
    /// </summary>
    public bool IsUserScrolledAway { get; private set; }

    /// <summary>
    /// Optional sink for scroll-event trace entries.  Assign a <see cref="ILiveTraceTarget"/>
    /// (typically the <c>TraceWindow</c>) to start capturing events; set back to
    /// <c>null</c> (or let the window's <c>Closed</c> handler do it) to stop.
    /// All <see cref="ScrollTrace"/> calls are zero-overhead no-ops while this is null.
    /// </summary>
    public ILiveTraceTarget? TraceTarget { get; set; }

    /// <summary>
    /// Gets a value indicating whether the initial transcript-history load is in progress.
    /// While <c>true</c>, <see cref="RequestScrollToEnd"/> and the extent-grow re-anchor
    /// inside <c>OnScrollChanged</c> are no-ops. Call <see cref="BeginLoad"/> before
    /// starting a history load and <see cref="EndLoad"/> after the last turn has been
    /// appended.
    /// </summary>
    public bool IsLoadingTranscript => _isLoadingTranscript;

    // -------------------------------------------------------------------------
    // Public API — load suppression
    // -------------------------------------------------------------------------

    /// <summary>
    /// Marks the start of an initial transcript-history load. Suppresses per-turn scroll
    /// operations until <see cref="EndLoad"/> is called.
    /// </summary>
    /// <remarks>
    /// Resets <see cref="IsUserScrolledAway"/> and the pending-scroll debounce flag so
    /// the controller starts the load from a clean state.
    /// </remarks>
    public void BeginLoad()
    {
        _isLoadingTranscript = true;
        IsUserScrolledAway   = false;
        _pendingScrollRequest = false;
        _pendingLockedViewportReanchor = false;
        ScrollTrace("LOAD BEGIN", "transcript history load started — per-turn scroll suppressed");
    }

    /// <summary>
    /// Marks the end of an initial transcript-history load. Clears the suppression flag
    /// and issues exactly one <see cref="RequestScrollToEnd"/> so the user lands at the
    /// bottom after all turns have been rendered.
    /// </summary>
    public void EndLoad()
    {
        _isLoadingTranscript  = false;
        IsUserScrolledAway    = false;
        _pendingScrollRequest = false;
        _pendingLockedViewportReanchor = false;
        ScrollTrace("LOAD END", "transcript history load complete — scrolling to bottom once");
        RequestScrollToEnd();
    }

    // -------------------------------------------------------------------------
    // Public API — called from MainWindow
    // -------------------------------------------------------------------------

    /// <summary>
    /// Requests a scroll to the end of the transcript, if the user has not scrolled away.
    ///
    /// <para>
    /// <b>Replaces all former <c>ScrollToEndIfAtBottom(thread)</c> call sites</b> in
    /// <c>AppendLine</c> (×2), <c>AppendText</c>, <c>AppendThinkingText</c>,
    /// and the <c>scrollOutputToEnd</c> delegate passed to
    /// <c>TranscriptConversationManager</c>.
    /// </para>
    ///
    /// <para>
    /// Only one <see cref="Dispatcher.BeginInvoke"/> item is ever pending at a time.
    /// Subsequent calls while a request is already queued are silently dropped — the
    /// queued item will do the right thing once layout settles.
    /// </para>
    /// </summary>
    public void RequestScrollToEnd()
    {
        if (IsUserScrolledAway)
        {
            ScrollTrace("SKIPPED", "IsUserScrolledAway=true");
            return;
        }

        if (_isLoadingTranscript)
        {
            ScrollTrace("SKIPPED", "IsLoadingTranscript=true — load in progress");
            return;
        }

        if (_pendingScrollRequest)
            return;

        ScrollTrace("SCROLL → END", "auto-scroll to bottom");
        _pendingScrollRequest = true;
        _dispatcher.BeginInvoke(DispatcherPriority.ContextIdle, ExecutePendingScrollToEnd);
    }

    /// <summary>
    /// Called from <c>SelectTranscriptThread</c> (line ~2837 in MainWindow.xaml.cs)
    /// immediately after <c>OutputTextBox.Document</c> is replaced with the new
    /// thread's document.
    ///
    /// <para>
    /// Switching threads always resets <see cref="IsUserScrolledAway"/> to
    /// <c>false</c> — the user explicitly chose to view this thread, so auto-scroll
    /// should be live from the start.
    /// </para>
    ///
    /// <para>
    /// <b>Replaces the <c>ScrollTranscriptThread</c> call</b> at the end of
    /// <c>SelectTranscriptThread</c>.
    /// </para>
    /// </summary>
    /// <param name="scrollToStart">
    /// When <c>true</c>, scroll to the top of the document (used when navigating to
    /// a thread for the first time to show the beginning of the transcript).
    /// When <c>false</c>, scroll to the end (resume live tail).
    /// </param>
    public void OnThreadSelected(bool scrollToStart)
    {
        ScrollTrace("THREAD SWITCH", $"scrollToStart={scrollToStart} → IsUserScrolledAway reset to false");

        // Thread switch always re-enables auto-scroll regardless of previous state.
        IsUserScrolledAway = false;
        _pendingScrollRequest = false;
        _pendingLockedViewportReanchor = false;

        // Hide the button immediately — no fade; the transcript content is changing
        // entirely so the old scroll position is irrelevant.
        HideScrollButton(immediate: true);

        if (scrollToStart)
        {
            _dispatcher.BeginInvoke(DispatcherPriority.Loaded, ExecuteScrollToStart);
        }
        else
        {
            // Unconditional scroll to end on explicit thread selection — bypass the
            // IsUserScrolledAway guard by going directly through RequestScrollToEnd
            // (which is now false) rather than calling the private executor.
            RequestScrollToEnd();
        }
    }

    /// <summary>
    /// Scrolls the viewport to the specified absolute vertical offset, or to the end
    /// if there is not enough content below <paramref name="targetOffset"/> to fill
    /// the viewport.
    ///
    /// <para>
    /// <b>Replaces the body of <c>ScrollToPromptParagraph</c></b> (lines ~4182–4197).
    /// The caller is responsible for computing <paramref name="targetOffset"/> from the
    /// paragraph's <c>ContentStart.GetCharacterRect()</c> + <c>sv.VerticalOffset</c>
    /// exactly as before; this method takes over the actual scroll operation so that
    /// <see cref="_isProgrammaticScroll"/> is set correctly and the
    /// <see cref="IsUserScrolledAway"/> flag is respected / updated.
    /// </para>
    ///
    /// <para>
    /// This method always executes the scroll regardless of
    /// <see cref="IsUserScrolledAway"/> — prompt navigation is an explicit user
    /// action and should never be blocked. It also clears
    /// <see cref="IsUserScrolledAway"/> because the prompt-nav buttons position the
    /// user intentionally above the bottom.
    /// </para>
    /// </summary>
    /// <param name="targetOffset">
    /// The desired <c>VerticalOffset</c> to scroll to. Typically computed as
    /// <c>sv.VerticalOffset + paragraph.ContentStart.GetCharacterRect(...).Top</c>.
    /// </param>
    public void ScrollToOffset(double targetOffset)
    {
        var sv = EnsureScrollViewer();
        if (sv is null)
            return;

        // Prompt-nav positions the user above the bottom; that is intentional, so
        // suppress auto-scroll until the user explicitly returns to the bottom.
        IsUserScrolledAway = true;

        _isProgrammaticScroll = true;
        try
        {
            // Clamp to the scrollable range so the last prompt scrolls as close to
            // the viewport top as the content allows (rather than jumping to the end).
            var clampedOffset = Math.Min(targetOffset, sv.ScrollableHeight);
            sv.ScrollToVerticalOffset(clampedOffset);

            // If we ended up at the very bottom, re-enable auto-scroll.
            if (clampedOffset >= sv.ScrollableHeight)
                IsUserScrolledAway = false;
        }
        finally
        {
            _isProgrammaticScroll = false;
        }
    }

    // -------------------------------------------------------------------------
    // Public API — scroll-to-bottom button
    // -------------------------------------------------------------------------

    /// <summary>
    /// Wires the floating scroll-to-bottom button overlay to this controller.
    /// Call once from <c>MainWindow</c> after <c>InitializeComponent()</c>, passing
    /// the named XAML button. Thereafter the controller owns show/hide/animation for
    /// that element; <c>MainWindow</c> only needs to forward click and transcript
    /// mouse-down events.
    /// </summary>
    public void SetScrollToBottomButton(Button button)
    {
        _scrollToBottomButton = button;
    }

    /// <summary>
    /// Scrolls immediately to the bottom, resumes auto-scroll, and hides the button.
    /// Call from the button's <c>Click</c> handler in <c>MainWindow</c>.
    /// </summary>
    public void ScrollToBottom()
    {
        IsUserScrolledAway = false;
        _pendingScrollRequest = false;
        _pendingLockedViewportReanchor = false;
        HideScrollButton(immediate: true);
        RequestScrollToEnd();
    }

    /// <summary>
    /// Forces an unconditional scroll to the end of the transcript, overriding
    /// <see cref="IsUserScrolledAway"/>.  Use when a user-initiated event (e.g.
    /// prompt submission) must always bring the new content into view regardless
    /// of the current scroll position.
    ///
    /// <para>Emits a <c>"PROMPT SUBMITTED"</c> trace entry so the action is
    /// visible in the <see cref="LiveTraceWindow"/>.</para>
    /// </summary>
    public void ForceScrollToBottom()
    {
        ScrollTrace("PROMPT SUBMITTED", "forced scroll to bottom (overrides lock)");
        IsUserScrolledAway = false;
        _pendingScrollRequest = false;
        _pendingLockedViewportReanchor = false;
        RequestScrollToEnd();
    }

    /// <summary>
    /// Optional callback invoked when the user scrolls within 400 px of the top of the
    /// transcript and there may be older turns to prepend.  Set by <c>MainWindow</c>
    /// after both the scroll controller and the conversation manager are constructed.
    /// The callback is fire-and-forget safe — it is responsible for its own re-entrancy
    /// guard (<c>_prependInProgress</c> in <c>TranscriptConversationManager</c>).
    /// </summary>
    public Action? RequestPrependOlderTurns { get; set; }

    /// <summary>
    /// Returns the current <c>ScrollableHeight</c> of the inner <see cref="ScrollViewer"/>,
    /// or 0 if the viewer is not yet available.
    /// Used by <c>TranscriptConversationManager.PrependOlderTurnsAsync</c> to measure
    /// how much the extent grew after a prepend batch so it can restore the viewport.
    /// </summary>
    public double GetScrollableHeight()
    {
        var sv = EnsureScrollViewer();
        return sv?.ScrollableHeight ?? 0;
    }

    /// <summary>
    /// Returns the current <c>VerticalOffset</c> of the inner <see cref="ScrollViewer"/>,
    /// or 0 if the viewer is not yet available.
    /// </summary>
    public double GetVerticalOffset()
    {
        var sv = EnsureScrollViewer();
        return sv?.VerticalOffset ?? 0;
    }

    /// <summary>
    /// Scrolls the inner <see cref="ScrollViewer"/> to an absolute vertical offset,
    /// bracketed in <see cref="_isProgrammaticScroll"/> so the
    /// <see cref="OnScrollChanged"/> handler does not classify the resulting event as
    /// user interaction.  Used by <c>PrependOlderTurnsAsync</c> to restore the viewport
    /// after prepending older turns so visible content does not jump.
    /// </summary>
    public void ScrollToAbsoluteOffset(double target)
    {
        var sv = EnsureScrollViewer();
        if (sv is null) return;
        _isProgrammaticScroll = true;
        try { sv.ScrollToVerticalOffset(target); }
        finally { _isProgrammaticScroll = false; }
    }

    /// <summary>
    /// Hides the floating button with a short fade-out. Call when the user clicks
    /// anywhere in the main transcript area (transcript <c>PreviewMouseDown</c>).
    /// </summary>
    public void DismissScrollButton() => HideScrollButton(immediate: false);

    public void ScrollPageUp()
    {
        var sv = EnsureScrollViewer();
        sv?.PageUp();
    }

    public void ScrollPageDown()
    {
        var sv = EnsureScrollViewer();
        sv?.PageDown();
    }

    /// <summary>
    /// Re-synchronises the scroll-button visibility to match the current scroll position.
    /// Call from <c>MainWindow.Window_Activated</c> so that an RDP session reconnect —
    /// which can leave the viewport scrolled to the top without firing the events that
    /// normally show/hide the button — is corrected the moment the user sees the window.
    /// </summary>
    public void SyncScrollState()
    {
        var sv = EnsureScrollViewer();
        if (sv is null) return;

        double distFromBottom = sv.ScrollableHeight - sv.VerticalOffset;
        bool wasScrolledAway = IsUserScrolledAway;
        IsUserScrolledAway = distFromBottom > NearBottomThreshold;

        SquadDashTrace.Write(TraceCategory.Scroll,
            $"SyncScrollState (window activated): distFromBottom={distFromBottom:0.#}px" +
            $"  scrollableH={sv.ScrollableHeight:0.#}px  locked={IsUserScrolledAway}" +
            (wasScrolledAway != IsUserScrolledAway ? "  *** corrected ***" : ""));

        if (IsUserScrolledAway && !wasScrolledAway)
            ShowScrollButton();
        else if (!IsUserScrolledAway && wasScrolledAway)
            HideScrollButton(immediate: true);
    }

    /// <summary>
    /// The lambda enqueued by <see cref="RequestScrollToEnd"/>. Executes after the
    /// layout pass at <see cref="DispatcherPriority.Loaded"/>, ensuring all newly
    /// added blocks have been measured before the viewport is moved.
    /// </summary>
    private void ExecutePendingScrollToEnd()
    {
        _pendingScrollRequest = false;

        if (IsUserScrolledAway)
            return;

        var sv = EnsureScrollViewer();
        if (sv is null)
            return;

        // Force the deferred WPF layout pass to complete now, so ScrollableHeight is
        // already computed before we move the viewport. This turns the implicit
        // synchronous layout that ScrollToEnd() triggers into an explicit one that we
        // control — and avoids it being counted as scroll overhead in perf traces.
        sv.UpdateLayout();

        _isProgrammaticScroll = true;
        try
        {
            sv.ScrollToVerticalOffset(sv.ScrollableHeight);
        }
        finally
        {
            _isProgrammaticScroll = false;
        }
    }

    /// <summary>
    /// Scrolls to the very beginning of the document. Used by
    /// <see cref="OnThreadSelected"/> when <c>scrollToStart</c> is <c>true</c>.
    /// </summary>
    private void ExecuteScrollToStart()
    {
        var sv = EnsureScrollViewer();
        if (sv is null)
        {
            // Fallback: use the RichTextBox API which does not require the inner ScrollViewer.
            _outputTextBox.CaretPosition = _outputTextBox.Document.ContentStart;
            _outputTextBox.ScrollToHome();
            return;
        }

        _isProgrammaticScroll = true;
        try
        {
            sv.ScrollToTop();
        }
        finally
        {
            _isProgrammaticScroll = false;
        }
    }

    // -------------------------------------------------------------------------
    // Private — ScrollViewer wiring
    // -------------------------------------------------------------------------

    private void OnOutputTextBoxLoaded(object sender, RoutedEventArgs e)
    {
        // Keep the subscription alive (do NOT unsubscribe here). WPF can re-fire
        // Loaded after an RDP session reconnect when the visual tree is rebuilt.
        WireScrollViewer();
    }

    /// <summary>
    /// Obtains the inner <see cref="ScrollViewer"/> from <c>PART_ContentHost</c> and
    /// subscribes to its <see cref="ScrollViewer.ScrollChanged"/> event.
    /// Safe to call multiple times: if the template produced a new ScrollViewer (e.g.
    /// after an RDP reconnect that rebuilt the visual tree), the old subscription is
    /// removed and a new one is established.  Also corrects <see cref="IsUserScrolledAway"/>
    /// to match the actual scroll position so the button shows immediately if needed.
    /// </summary>
    private void WireScrollViewer()
    {
        var newSv = _outputTextBox.Template?.FindName("PART_ContentHost", _outputTextBox) as ScrollViewer;

        if (newSv is null)
        {
            SquadDashTrace.Write(TraceCategory.Scroll, "WireScrollViewer: FAILED — PART_ContentHost not found (template not yet applied?).");
            return;
        }

        if (ReferenceEquals(newSv, _sv))
        {
            // Same instance — already wired, nothing to do.
            SquadDashTrace.Write(TraceCategory.Scroll, "WireScrollViewer: same ScrollViewer instance, no rewire needed.");
            return;
        }

        // Unsubscribe from the old instance (if any) to avoid duplicate events.
        if (_sv is not null)
        {
            _sv.ScrollChanged -= OnScrollChanged;
            SquadDashTrace.Write(TraceCategory.Scroll, "WireScrollViewer: old ScrollViewer instance detached (template was re-applied).");
        }

        _sv = newSv;
        _sv.ScrollChanged += OnScrollChanged;

        // After re-wiring, correct IsUserScrolledAway to match the actual scroll
        // position. An RDP reconnect can leave the viewport at the top while our flag
        // still reflects the pre-disconnect state (false = at bottom). Without this
        // correction the button never appears on the reconnected session.
        double distFromBottom = _sv.ScrollableHeight - _sv.VerticalOffset;
        bool wasScrolledAway = IsUserScrolledAway;
        IsUserScrolledAway = distFromBottom > NearBottomThreshold;

        SquadDashTrace.Write(TraceCategory.Scroll,
            $"WireScrollViewer: ScrollViewer wired. distFromBottom={distFromBottom:0.#}px" +
            $"  scrollableH={_sv.ScrollableHeight:0.#}px  verticalOffset={_sv.VerticalOffset:0.#}px" +
            $"  IsUserScrolledAway={IsUserScrolledAway}" +
            (wasScrolledAway != IsUserScrolledAway ? "  *** state corrected ***" : ""));

        if (IsUserScrolledAway && !wasScrolledAway)
        {
            // Reconnect left us scrolled away — show the button immediately.
            ShowScrollButton();
        }
        else if (!IsUserScrolledAway && wasScrolledAway)
        {
            HideScrollButton(immediate: true);
        }
    }

    /// <summary>
    /// Lazily resolves the inner <see cref="ScrollViewer"/> in case
    /// <see cref="WireScrollViewer"/> was called before the template was ready.
    /// Returns <c>null</c> if the template is still unavailable.
    /// </summary>
    private ScrollViewer? EnsureScrollViewer()
    {
        if (_sv is null)
            WireScrollViewer();
        return _sv;
    }

    // -------------------------------------------------------------------------
    // Private — user-scroll detection
    // -------------------------------------------------------------------------

    /// <summary>
    /// Handles <c>ScrollViewer.ScrollChanged</c> to detect whether the user has
    /// manually scrolled away from the bottom.
    ///
    /// <para>
    /// <b>Algorithm (three-gate design):</b>
    /// <list type="number">
    ///   <item><b>Programmatic guard.</b>  If <see cref="_isProgrammaticScroll"/> is
    ///         set, this scroll was initiated by us (e.g. <c>ScrollToEnd</c> from
    ///         <see cref="ExecutePendingScrollToEnd"/>). Ignore it entirely.</item>
    ///   <item><b>Content-change guard.</b>  If <c>e.ExtentHeightChange != 0</c>,
    ///         the content itself grew or shrank — a new paragraph was added, a
    ///         tool-entry's <c>DetailTextBox.Text</c> was updated, a thinking block
    ///         expanded, etc.  This is <em>not</em> a user gesture; skip the event.
    ///         <para>This guard prevents two failure modes:
    ///         (a) content growth while the user is scrolled away shifts the
    ///         extent-based arithmetic but must not change the lock state; and
    ///         (b) content <em>shrinkage</em> can cause WPF to clamp
    ///         <c>VerticalOffset</c> downward, producing a spurious negative
    ///         <c>VerticalChange</c> that would otherwise be mis-classified as
    ///         the user dragging the scroll thumb upward.</para>
    ///         <para>When extent <em>grows</em> and the user is not scrolled away,
    ///         a re-anchor scroll is issued via <see cref="RequestScrollToEnd"/>.
    ///         This handles the WPF <see cref="FlowDocument"/> layout
    ///         collapse/re-expand cycle: the document briefly shrinks (clamping
    ///         <c>VerticalOffset</c> to the smaller <c>ScrollableHeight</c>) then
    ///         re-expands, but WPF does <em>not</em> automatically restore
    ///         <c>VerticalOffset</c> during the grow phase.  Without re-anchoring,
    ///         the viewport is left stranded partway up the document even though
    ///         <see cref="IsUserScrolledAway"/> is still <c>false</c>.</para></item>
    ///   <item><b>No-movement guard.</b>  If <c>e.VerticalChange == 0</c>, nothing
    ///         moved vertically (could be a horizontal scroll, a background
    ///         re-layout pass, or a programmatic scroll that was already at its
    ///         target). No action needed.</item>
    ///   <item><b>User-scroll classification.</b>  All three gates passed — this is
    ///         a genuine user drag or mousewheel event.  Update
    ///         <see cref="IsUserScrolledAway"/> from the current distance to the
    ///         bottom: <c>distanceFromBottom = ScrollableHeight − VerticalOffset</c>.
    ///         If ≤ <see cref="NearBottomThreshold"/> the user has returned to (or
    ///         stayed at) the bottom → clear the flag, auto-scroll resumes silently.
    ///         If &gt; threshold the user has scrolled away → set the flag, lock
    ///         position.  Both directions are handled by a single assignment so that
    ///         scrolling down <em>but still above the threshold</em> correctly keeps
    ///         the lock engaged.</item>
    /// </list>
    /// </para>
    /// </summary>
    private void OnScrollChanged(object sender, ScrollChangedEventArgs e)
    {
        // Gate 1: programmatic scroll — we caused this, ignore.
        if (_isProgrammaticScroll)
        {
            ScrollTrace("IGNORED", "programmatic scroll (our own)");
            return;
        }

        // Gate 2: content grew or shrank — not a user gesture.
        // Skipping here prevents (a) false locks from content additions and
        // (b) spurious upward-scroll detection when extent shrinkage clamps
        // VerticalOffset, which produces a negative VerticalChange that is
        // indistinguishable from a real upward scroll without this guard.
        // When extent grows and the user is not locked away, re-anchor to
        // bottom — this recovers from the WPF FlowDocument collapse/re-expand
        // cycle where VerticalOffset is clamped during the shrink phase but is
        // not automatically restored during the re-expand phase.
        if (e.ExtentHeightChange != 0)
        {
            _gate2BlockCount++;
            if (_gate2BlockCount <= 5 || _gate2BlockCount % 20 == 0)
            {
                SquadDashTrace.Write(TraceCategory.Scroll,
                    $"OnScrollChanged Gate2 block #{_gate2BlockCount}: ExtentHeightChange={e.ExtentHeightChange:+0.#;-0.#}px" +
                    $"  VerticalChange={e.VerticalChange:+0.#;-0.#}px  locked={IsUserScrolledAway}");
            }
            if (e.ExtentHeightChange > 0 && !IsUserScrolledAway)
            {
                if (_isLoadingTranscript)
                {
                    ScrollTrace("EXTENT GROW (loading)", $"content grew +{e.ExtentHeightChange:0.#}px \u2014 load in progress, re-anchor suppressed");
                    return;
                }
                ScrollTrace("EXTENT GROW \u2192 RE-ANCHOR", $"content grew +{e.ExtentHeightChange:0.#}px, IsUserScrolledAway=False \u2014 re-issuing scroll to end");
                RequestScrollToEnd();
            }
            else if (e.ExtentHeightChange > 0)
            {
                // Content grew while the user is scrolled away. Most of the time that
                // just means new content was appended below the viewport, so we should
                // preserve the current VerticalOffset exactly. Only restore to the saved
                // distance-from-bottom after a preceding extent shrink told us WPF
                // clamped the viewport during a collapse / re-expand cycle.
                if (_pendingLockedViewportReanchor && _savedDistanceFromBottom >= 0)
                {
                    var sv2 = (ScrollViewer)sender;
                    double targetOffset = Math.Clamp(sv2.ScrollableHeight - _savedDistanceFromBottom, 0, sv2.ScrollableHeight);
                    _isProgrammaticScroll = true;
                    try { sv2.ScrollToVerticalOffset(targetOffset); }
                    finally { _isProgrammaticScroll = false; }
                    _pendingLockedViewportReanchor = false;
                    ScrollTrace("EXTENT GROW (locked)", $"content grew +{e.ExtentHeightChange:0.#}px \u2014 restored to distFromBottom={_savedDistanceFromBottom:0.#}px (offset={targetOffset:0.#})");
                }
                else
                {
                    ScrollTrace("EXTENT GROW (locked)", $"content grew +{e.ExtentHeightChange:0.#}px, IsUserScrolledAway=True \u2014 preserving current viewport");
                }
            }
            else
            {
                if (IsUserScrolledAway)
                    _pendingLockedViewportReanchor = true;
                ScrollTrace("EXTENT SHRINK", $"content shrank {e.ExtentHeightChange:0.#}px \u2014 clamping VerticalOffset, will re-anchor on re-expand");
            }
            return;
        }

        // Gate 3: nothing moved vertically — no action needed.
        if (e.VerticalChange == 0)
        {
            ScrollTrace("IGNORED", "VerticalChange=0");
            return;
        }

        // Genuine user drag / mousewheel: classify by distance from bottom.
        // ScrollableHeight == ExtentHeight - ViewportHeight, so
        // ScrollableHeight - VerticalOffset == ExtentHeight - ViewportHeight - VerticalOffset,
        // which is the number of pixels of content below the visible viewport.
        var sv = (ScrollViewer)sender;
        double distanceFromBottom = sv.ScrollableHeight - sv.VerticalOffset;

        bool wasScrolledAway = IsUserScrolledAway;
        IsUserScrolledAway = distanceFromBottom > NearBottomThreshold;
        _pendingLockedViewportReanchor = false;

        ScrollTrace("USER SCROLL", $"VerticalChange={e.VerticalChange:+0.#;-0.#}px, distFromBottom={distanceFromBottom:0.#}px, locked={IsUserScrolledAway}");

        // Log the first 10 genuine user-scroll events and all lock-state transitions
        // to the persistent file so scroll-button issues can be diagnosed on VM/remote
        // sessions where the live trace window isn't open.
        _scrollEventCount++;
        bool lockStateChanged = IsUserScrolledAway != wasScrolledAway;
        if (_scrollEventCount <= 10 || lockStateChanged)
        {
            SquadDashTrace.Write(TraceCategory.Scroll,
                $"OnScrollChanged #{_scrollEventCount}: VerticalChange={e.VerticalChange:+0.#;-0.#}px" +
                $"  distFromBottom={distanceFromBottom:0.#}px" +
                $"  scrollableH={sv.ScrollableHeight:0.#}px" +
                $"  viewportH={sv.ViewportHeight:0.#}px" +
                $"  locked={IsUserScrolledAway}" +
                (lockStateChanged ? "  *** LOCK STATE CHANGED ***" : ""));
        }

        if (IsUserScrolledAway)
        {
            _savedDistanceFromBottom = distanceFromBottom;
            // Show (or refresh the 10 s auto-hide timer) whenever the user is scrolled
            // away — this covers both the initial scroll-up AND continued scrolling while
            // already away from the bottom.
            ShowScrollButton();
        }
        else if (wasScrolledAway)
        {
            _savedDistanceFromBottom = -1;
            // User scrolled back to the bottom: hide the button.
            HideScrollButton();
        }

        // Near-top detection: when the user has scrolled within 400 px of the very
        // top of the transcript, trigger a virtual prepend of the next batch of older
        // turns.  The callback (_conversationManager.PrependOlderTurnsAsync) is
        // fire-and-forget safe and guards against re-entrancy internally.
        if (sv.VerticalOffset < 400 && RequestPrependOlderTurns is { } prepend)
        {
            ScrollTrace("NEAR TOP", $"VerticalOffset={sv.VerticalOffset:0.#}px — requesting prepend of older turns");
            prepend();
        }
    }

    // -------------------------------------------------------------------------
    // Private — floating scroll-to-bottom button animations
    // -------------------------------------------------------------------------

    /// <summary>
    /// Makes the button visible (if not already) and fades it in to full opacity,
    /// then starts (or restarts) the 10-second auto-hide timer.
    /// Safe to call repeatedly while the button is already visible — it just resets
    /// the timer, keeping the button alive as long as the user keeps scrolling.
    /// </summary>
    private void ShowScrollButton()
    {
        if (_scrollToBottomButton is null)
        {
            SquadDashTrace.Write(TraceCategory.Scroll, "ShowScrollButton: SKIPPED — _scrollToBottomButton is null (SetScrollToBottomButton not called?).");
            return;
        }

        ScrollTrace("BUTTON SHOWN", "scroll-to-bottom button made visible");
        SquadDashTrace.Write(TraceCategory.Scroll, "ShowScrollButton: fading button in.");
        _autoHideTimer.Stop();

        var btn = _scrollToBottomButton;
        btn.Visibility = Visibility.Visible;

        var fadeIn = new DoubleAnimation(1.0, TimeSpan.FromMilliseconds(250))
        {
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut },
        };
        btn.BeginAnimation(UIElement.OpacityProperty, fadeIn);

        _autoHideTimer.Start();
    }

    /// <summary>
    /// Fades the button out and collapses it once the fade completes.
    /// </summary>
    /// <param name="immediate">
    /// When <c>true</c>, skips the animation and hides the button instantly (e.g.,
    /// on thread switch where the visual state should reset without transition).
    /// </param>
    private void HideScrollButton(bool immediate = false)
    {
        if (_scrollToBottomButton is null)
            return;

        ScrollTrace("BUTTON HIDDEN", immediate ? "immediate (thread switch or forced scroll)" : "fade-out");
        SquadDashTrace.Write(TraceCategory.Scroll, $"HideScrollButton: {(immediate ? "immediate" : "fade-out")}.");
        _autoHideTimer.Stop();

        var btn = _scrollToBottomButton;

        if (immediate)
        {
            // Remove any running animation and snap to hidden state synchronously.
            btn.BeginAnimation(UIElement.OpacityProperty, null);
            btn.Opacity = 0.0;
            // Close any open tooltip directly — works even when mouse is still hovering.
            if (btn.ToolTip is ToolTip tt) tt.IsOpen = false;
            btn.Visibility = Visibility.Collapsed;
            return;
        }

        var fadeOut = new DoubleAnimation(0.0, TimeSpan.FromMilliseconds(300));
        fadeOut.Completed += (_, _) =>
        {
            // Guard against a ShowScrollButton() call arriving during the fade-out:
            // if opacity has been driven back up by a new animation, do not collapse.
            if (btn.Opacity < 0.05)
            {
                btn.BeginAnimation(UIElement.OpacityProperty, null);
                // Close any open tooltip directly — works even when mouse is still hovering.
                if (btn.ToolTip is ToolTip tt2) tt2.IsOpen = false;
                btn.Visibility = Visibility.Collapsed;
            }
        };
        btn.BeginAnimation(UIElement.OpacityProperty, fadeOut);
    }

    /// <summary>
    /// Handles <see cref="DispatcherTimer.Tick"/> for <see cref="_autoHideTimer"/>.
    /// Fires once after <see cref="ScrollButtonAutoHideSeconds"/> seconds of inactivity
    /// (no user scrolling and no button interaction) to auto-hide the floating button.
    /// </summary>
    private void OnAutoHideTimerTick(object? sender, EventArgs e)
    {
        _autoHideTimer.Stop();
        HideScrollButton();
    }

    // -------------------------------------------------------------------------
    // Private — trace
    // -------------------------------------------------------------------------

    /// <summary>
    /// Emits a scroll trace entry to <see cref="TraceTarget"/>, if one is registered.
    /// All calls are zero-overhead no-ops while <see cref="TraceTarget"/> is null (i.e.
    /// while the <see cref="TraceWindow"/> is closed).
    /// </summary>
    private void ScrollTrace(string eventName, string detail = "") =>
        TraceTarget?.AddEntry(TraceCategory.Scroll, $"{eventName,-24}  {detail}".TrimEnd());
}
