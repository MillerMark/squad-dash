using System;

namespace SquadDash;

/// <summary>
/// Coordinates the multi-channel notification for plan gate approval requests.
/// During the early window (gate just activated): Inbox + one-time Ultimate Callout + sound + push.
/// When fully blocked: transcript card rendering via <see cref="TranscriptApprovalCardBuilder"/>.
/// </summary>
internal sealed class ApprovalCardNotificationCoordinator
{
    private readonly DurableApprovalRequestManager _durableManager;
    private readonly SoundNotificationService _sound;
    private readonly PushNotificationService _push;

    internal ApprovalCardNotificationCoordinator(
        DurableApprovalRequestManager durableManager,
        SoundNotificationService sound,
        PushNotificationService push)
    {
        _durableManager = durableManager ?? throw new ArgumentNullException(nameof(durableManager));
        _sound = sound ?? throw new ArgumentNullException(nameof(sound));
        _push = push ?? throw new ArgumentNullException(nameof(push));
    }

    /// <summary>
    /// Fires the early-window notification channels: sound + push notification.
    /// The callout and inbox are managed by the caller (UI thread dependent).
    /// Returns whether the notification was sent (false if already notified for this version).
    /// </summary>
    internal async System.Threading.Tasks.Task<bool> NotifyEarlyWindowAsync(
        Plan plan,
        PlanApprovalGate gate,
        System.Threading.CancellationToken cancellationToken = default)
    {
        var shouldNotify = await _durableManager.TryMarkNotifiedAsync(plan.PlanId, cancellationToken)
            .ConfigureAwait(false);

        if (!shouldNotify)
            return false;

        _sound.Play(SoundEvent.ApprovalNeeded);

        var title = "Plan Approval Required";
        var message = $"{plan.Title} — {plan.Progress.CompletedCount}/{plan.Progress.TotalCount} steps complete. Checkpoint: {gate.Message}";

        _ = _push.NotifyEventAsync("plan_gate_approval_required", title, message);

        return true;
    }

    /// <summary>
    /// Builds the scope-aware label for the approve button based on active gate count.
    /// </summary>
    internal static string BuildApproveLabel(int activeGateCount) => activeGateCount switch
    {
        2 => "Approve both checkpoints and continue",
        > 2 => "Approve all checkpoints and continue",
        _ => "Approve checkpoint and continue",
    };
}
