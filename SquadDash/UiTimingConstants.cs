using System;

namespace SquadDash;

/// <summary>
/// Named timing constants used for Task.Delay, Thread.Sleep, and
/// DispatcherTimer intervals throughout MainWindow. Eliminates magic
/// numeric literals and makes intent clear at each call site.
///
/// Integer constants are milliseconds (for Task.Delay(int) /
/// Thread.Sleep(int) overloads). TimeSpan fields are for
/// DispatcherTimer.Interval and TimeSpan-overload APIs.
/// </summary>
internal static class UiTimingConstants
{
    // ── Task.Delay / Thread.Sleep (int ms) ───────────────────────────────────

    /// <summary>Clipboard COM retry: linear-backoff unit (Thread.Sleep(ClipboardRetryBackoffUnitMs * attempt)).</summary>
    public const int ClipboardRetryBackoffUnitMs = 50;

    /// <summary>Tour menu: brief pause after a menu closes before the next open attempt.</summary>
    public const int TourBetweenAttemptsMs = 80;

    /// <summary>Tour menu: settling pause before verifying the popup has reached a rendered-open state.</summary>
    public const int TourMenuSettleMs = 60;

    /// <summary>Tour: per-step animation tick used in typing/highlight sequences.</summary>
    public const int TourStepAnimationMs = 100;

    /// <summary>Tour: pause after a callout appears before checking element visibility.</summary>
    public const int TourCalloutVisibilitySettleMs = 250;

    /// <summary>Push-to-talk: pause after Ctrl release to let dictation text land in the prompt box.</summary>
    public const int PttSendSettleMs = 220;

    /// <summary>Remote bridge: OS pause to release a TCP port before rebinding.</summary>
    public const int BridgePortReleaseMs = 500;

    /// <summary>Watch health: pause after stopping the watcher before re-querying health.</summary>
    public const int WatchHealthStopSettleMs = 500;

    /// <summary>Watch health: pause after starting the watcher before re-querying health.</summary>
    public const int WatchHealthStartSettleMs = 1000;

    /// <summary>Tour menu: maximum wait for a menu to reach the rendered-open state.</summary>
    public const int TourMenuOpenTimeoutMs = 500;

    /// <summary>Tour quick-reply panel: poll timeout waiting for the panel to appear in the visual tree.</summary>
    public const int TourPanelPollTimeoutMs = 2000;

    /// <summary>Docs file-change debounce delay before refreshing the docs tree.</summary>
    public const int DocsRefreshDebounceMs = 150;

    /// <summary>Routing repair: poll interval while waiting for repair to complete.</summary>
    public const int RoutingRepairPollMs = 250;

    // ── DispatcherTimer / Task.Delay(TimeSpan) intervals ─────────────────────

    /// <summary>Watch health panel auto-refresh cadence.</summary>
    public static readonly TimeSpan WatchHealthAutoRefreshInterval = TimeSpan.FromSeconds(15);

    /// <summary>History hint auto-dismiss delay.</summary>
    public static readonly TimeSpan HistoryHintDismissInterval = TimeSpan.FromSeconds(5);

    /// <summary>Shortcuts hint periodic rebuild cadence.</summary>
    public static readonly TimeSpan HintRefreshInterval = TimeSpan.FromMinutes(1);

    /// <summary>Tool-spinner animation tick.</summary>
    public static readonly TimeSpan ToolSpinnerInterval = TimeSpan.FromMilliseconds(150);

    /// <summary>Prompt health check cadence.</summary>
    public static readonly TimeSpan PromptHealthCheckInterval = TimeSpan.FromSeconds(5);

    /// <summary>Status-bar presentation refresh cadence.</summary>
    public static readonly TimeSpan StatusPresentationInterval = TimeSpan.FromSeconds(1);

    /// <summary>Team-file debounce interval before triggering a team refresh.</summary>
    public static readonly TimeSpan TeamRefreshDebounceInterval = TimeSpan.FromMilliseconds(350);

    /// <summary>UI-responsiveness watchdog poll cadence.</summary>
    public static readonly TimeSpan UiResponsivenessCheckInterval = TimeSpan.FromMilliseconds(500);

    /// <summary>Queue priority-feedback label clear delay.</summary>
    public static readonly TimeSpan QueueFeedbackClearDelay = TimeSpan.FromSeconds(3);

    /// <summary>Abort watchdog: fires this long after issuing an abort if the prompt hasn't cleaned up.</summary>
    public static readonly TimeSpan AbortWatchdogDelay = TimeSpan.FromSeconds(3);

    /// <summary>Loop countdown timer tick.</summary>
    public static readonly TimeSpan LoopCountdownInterval = TimeSpan.FromSeconds(1);

    /// <summary>Font-scale commit debounce timer tick.</summary>
    public static readonly TimeSpan FontScaleCommitInterval = TimeSpan.FromMilliseconds(80);

    /// <summary>PTT Ctrl-release detection poll cadence.</summary>
    public static readonly TimeSpan PttCtrlPollInterval = TimeSpan.FromMilliseconds(100);

    /// <summary>Transcript glow hold duration before starting the fade-out.</summary>
    public static readonly TimeSpan TranscriptGlowHoldInterval = TimeSpan.FromSeconds(1);

    /// <summary>Tour highlight-Z animation tick.</summary>
    public static readonly TimeSpan TourHighlightZInterval = TimeSpan.FromMilliseconds(150);

    /// <summary>Loop-preview filter debounce interval.</summary>
    public static readonly TimeSpan LoopPreviewFilterDebounceInterval = TimeSpan.FromSeconds(1.5);

    /// <summary>Doc-preview auto-refresh cadence.</summary>
    public static readonly TimeSpan DocPreviewRefreshInterval = TimeSpan.FromMilliseconds(300);

    /// <summary>Doc-source auto-save debounce interval.</summary>
    public static readonly TimeSpan DocSourceSaveInterval = TimeSpan.FromMilliseconds(400);

    /// <summary>Doc-source hover tooltip delay.</summary>
    public static readonly TimeSpan DocSourceHoverInterval = TimeSpan.FromSeconds(1);

    /// <summary>Doc-source search debounce interval.</summary>
    public static readonly TimeSpan DocSourceFindDebounceInterval = TimeSpan.FromMilliseconds(150);

    /// <summary>Periodic UI refresh (footer timestamp, transcript title).</summary>
    public static readonly TimeSpan PeriodicRefreshInterval = TimeSpan.FromSeconds(60);

    /// <summary>Coordinator-intent scan debounce interval.</summary>
    public static readonly TimeSpan CoordinatorIntentDebounceInterval = TimeSpan.FromMilliseconds(250);

    /// <summary>Slider input debounce interval.</summary>
    public static readonly TimeSpan SliderDebounceInterval = TimeSpan.FromMilliseconds(250);

    /// <summary>Postpone-reminder timer interval.</summary>
    public static readonly TimeSpan PostponeReminderInterval = TimeSpan.FromMinutes(2);

    /// <summary>Tint-commit animation tick.</summary>
    public static readonly TimeSpan TintCommitInterval = TimeSpan.FromMilliseconds(40);

    /// <summary>Queue "copied" feedback label clear delay.</summary>
    public static readonly TimeSpan QueueCopiedFeedbackClearDelay = TimeSpan.FromSeconds(2);

    /// <summary>Commit categorization debounce interval.</summary>
    public static readonly TimeSpan CategorizationDebounceInterval = TimeSpan.FromSeconds(2);

    /// <summary>Plan row attention animation duration after collection.</summary>
    public static readonly TimeSpan PlanRowAttentionDuration = TimeSpan.FromMilliseconds(1000);

    /// <summary>Code health banner auto-dismiss interval.</summary>
    public static readonly TimeSpan CodeHealthBannerInterval = TimeSpan.FromSeconds(12);

    /// <summary>"Copied to clipboard" tooltip auto-dismiss interval.</summary>
    public static readonly TimeSpan CopiedTooltipDismissInterval = TimeSpan.FromSeconds(1.5);

    /// <summary>Install progress elapsed-time display refresh cadence.</summary>
    public static readonly TimeSpan InstallProgressRefreshInterval = TimeSpan.FromSeconds(1);

    /// <summary>Transcript tab countdown tick interval.</summary>
    public static readonly TimeSpan TabCountdownInterval = TimeSpan.FromSeconds(1);
    /// <summary>Braille-dot spinner frames used for tool activity and plan execution indicators.</summary>
    internal static readonly string[] ToolSpinnerFrames = ["⠋", "⠙", "⠹", "⠸", "⠼", "⠴", "⠦", "⠧", "⠇", "⠏"];
}
