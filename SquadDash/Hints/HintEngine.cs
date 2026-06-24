using System.Threading;

namespace SquadDash.Hints;

/// <summary>
/// Singleton service that drives the Discoverability (Hints) system.
/// Call <see cref="Initialize"/> once at startup after loading the workspace.
/// Subscribe to <see cref="HintRequested"/> to display hints in the UI.
/// </summary>
internal sealed class HintEngine {
    public static HintEngine Instance { get; } = new();

    private readonly HintPersistence _persistence = new();
    private List<HintDefinition> _registry = new();
    private readonly Dictionary<string, Func<bool>> _conditions =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, List<Action<string>>> _actionCallbacks =
        new(StringComparer.OrdinalIgnoreCase);
    private Timer? _idleTimer;
    private ApplicationSettingsStore? _settingsStore;

    public event EventHandler<HintDefinition>? HintRequested;

    private HintEngine() {
        RegisterCondition("always", () => true);
    }

    // ── Initialization ────────────────────────────────────────────────────────

    public void Initialize(string workspaceRoot) {
        _settingsStore = new ApplicationSettingsStore();
        _registry = _persistence.LoadRegistry(workspaceRoot);
        _persistence.LoadHistory();
        _idleTimer?.Dispose();
        _idleTimer = new Timer(_ => EvaluateIdle(), null,
            TimeSpan.FromSeconds(60), TimeSpan.FromSeconds(60));
    }

    /// <summary>
    /// Immediately runs the idle evaluation, bypassing the minimum-gap guard.
    /// Intended for developer use (e.g. a Dev menu trigger) to test hints without waiting.
    /// </summary>
    public void TriggerIdleCycle() => EvaluateIdle(force: true);

    // ── Action trigger registration ───────────────────────────────────────────

    public void RegisterActionTrigger(string actionId, Action<string> callback) {
        if (!_actionCallbacks.TryGetValue(actionId, out var list)) {
            list = new List<Action<string>>();
            _actionCallbacks[actionId] = list;
        }
        list.Add(callback);
    }

    // ── Condition registry ────────────────────────────────────────────────────

    public void RegisterCondition(string conditionId, Func<bool> predicate) {
        _conditions[conditionId] = predicate;
    }

    // ── Fire action ───────────────────────────────────────────────────────────

    /// <summary>
    /// Fires all Action-triggered hints matching <paramref name="actionId"/>
    /// that have not yet reached their <see cref="HintDefinition.MaxShowCount"/>.
    /// Action-triggered hints bypass the global throttle gap.
    /// </summary>
    public void FireAction(string actionId) {
        var history = _persistence.LoadHistory();
        var eligible = _registry
            .Where(h => h.Trigger == HintTrigger.Action &&
                        string.Equals(h.ActionId, actionId, StringComparison.OrdinalIgnoreCase))
            .Where(h => {
                var rec = history.FirstOrDefault(r => r.HintId == h.HintId);
                return rec?.UserDismissed != true && (rec?.SeenCount ?? 0) < h.MaxShowCount;
            })
            .ToList();

        foreach (var hint in eligible)
            HintRequested?.Invoke(this, hint);
    }

    // ── Seen / shown tracking ─────────────────────────────────────────────────

    public void RecordShown(string hintId) {
        var record = _persistence.GetRecord(hintId) ?? new HintRecord { HintId = hintId };
        record.LastShown = DateTime.UtcNow;
        _persistence.UpdateRecord(record);
    }

    public void RecordSeen(string hintId) {
        var record = _persistence.GetRecord(hintId) ?? new HintRecord { HintId = hintId };
        record.SeenCount++;
        record.LastSeen = DateTime.UtcNow;
        _persistence.UpdateRecord(record);
    }

    /// <summary>
    /// The hint's target disappeared before the qualifying read duration elapsed.
    /// Does NOT count as seen; no persistence update.
    /// </summary>
    /// <summary>Called when the user explicitly closes the hint via the × button. Permanently suppresses this hint (resets only by ClearHistory).</summary>
    public void RecordDismissed(string hintId) {
        var record = _persistence.GetRecord(hintId) ?? new HintRecord { HintId = hintId };
        record.UserDismissed = true;
        record.LastSeen      = DateTime.UtcNow;
        record.SeenCount++;
        _persistence.UpdateRecord(record);
    }

    /// <summary>
    /// The hint's target disappeared before the qualifying read duration elapsed.
    /// Does NOT count as seen; no persistence update.
    /// </summary>
    public void RecordEphemeralClose(string hintId) { }

    public void ClearHistory() =>
        _persistence.SaveHistory(new List<HintRecord>());

    // ── Qualifying duration helper ────────────────────────────────────────────

    /// <summary>Returns 25 ms × word count of <paramref name="markdownText"/>.</summary>
    public static TimeSpan GetQualifyingDuration(string markdownText) {
        var wordCount = markdownText.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).Length;
        return TimeSpan.FromMilliseconds(25.0 * wordCount);
    }

    // ── Idle evaluation loop ──────────────────────────────────────────────────

    private void EvaluateIdle(bool force = false) {
        var settings = GetCurrentSettings();
        if (!settings.HintsEnabled) return;

        var history = _persistence.LoadHistory();

        if (!force) {
            var minGap = TimeSpan.FromMinutes(settings.MinGapMinutes);
            var lastShown = history.Count > 0
                ? history.Max(r => r.LastShown)
                : DateTime.MinValue;

            if (DateTime.UtcNow - lastShown < minGap) return;
        }

        var eligible = _registry
            .Where(h => h.Trigger == HintTrigger.Idle)
            .Where(h => {
                var rec = history.FirstOrDefault(r => r.HintId == h.HintId);
                return rec?.UserDismissed != true && (rec?.SeenCount ?? 0) < h.MaxShowCount;
            })
            .Where(h => PassesCondition(h.ConditionId))
            .OrderBy(h => h.Priority)
            .FirstOrDefault();

        if (eligible is not null)
            HintRequested?.Invoke(this, eligible);
    }

    private bool PassesCondition(string? conditionId) {
        if (conditionId is null) return true;
        return _conditions.TryGetValue(conditionId, out var predicate) && predicate();
    }

    private HintSettings GetCurrentSettings() {
        if (_settingsStore is null) return new HintSettings();
        try   { return HintSettings.FromSnapshot(_settingsStore.Load()); }
        catch { return new HintSettings(); }
    }
}
