namespace SquadDash;

using SquadDash.Hints;
using Microsoft.Win32;
using System.Windows.Input;

// ── Bridge ───────────────────────────────────────────────────────────────────

internal sealed record BridgeEventReceivedMessage(SquadSdkEvent SdkEvent);
internal sealed record BridgeErrorReceivedMessage(string ErrorText);

// ── Win32 system events ──────────────────────────────────────────────────────
// sender is non-nullable for Win32 system events.
internal sealed record UserPreferenceChangedMessage(object Sender, UserPreferenceChangedEventArgs Args);
internal sealed record PowerModeChangedMessage(object Sender, PowerModeChangedEventArgs Args);

// ── Input manager ────────────────────────────────────────────────────────────

internal sealed record PreProcessInputMessage(object Sender, PreProcessInputEventArgs Args);

// ── HintEngine (singleton) ───────────────────────────────────────────────────
// sender is nullable per EventHandler<T> convention.
internal sealed record HintRequestedMessage(object? Sender, HintDefinition Hint);

// ── MarkdownDocumentWindow (static event) ────────────────────────────────────

internal sealed record DocRevisionCompletedMessage;

// ── TranscriptSelectionController ───────────────────────────────────────────

internal sealed record OpenPanelRequestedMessage(AgentStatusCard Card, TranscriptThreadState Thread, bool IsAuto);
internal sealed record ClosePanelRequestedMessage(AgentStatusCard Card, TranscriptThreadState Thread);
internal sealed record ShowMainTranscriptMessage;
internal sealed record HideMainTranscriptMessage;
