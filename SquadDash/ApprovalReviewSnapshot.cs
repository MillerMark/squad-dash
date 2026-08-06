using System.Collections.Generic;

namespace SquadDash;

// ─── Link models ──────────────────────────────────────────────────────────────

/// <summary>
/// Describes a commit presented by an approval review. Runtime UI surfaces open its full SHA
/// through the same GitHub remote/push-check path used by transcript commit links.
/// </summary>
internal sealed record CommitLink(
    /// <summary>Short (7-char) SHA displayed in the UI.</summary>
    string ShortSha,
    /// <summary>Full SHA for precise resolution.</summary>
    string FullSha,
    /// <summary>First line of the commit message (subject).</summary>
    string Subject)
{
    /// <summary>Legacy routing token retained for persisted/test compatibility; it is not an external URL.</summary>
    internal string InternalUri => $"app://commit-diff:{FullSha}";
}

/// <summary>
/// Describes how to open a changed file — either at a specific commit (reviewed version)
/// or in the current workspace (live version).
/// </summary>
internal sealed record FileLink(
    string FilePath,
    /// <summary>Full SHA of the commit this file was reviewed at.</summary>
    string CommitSha)
{
    /// <summary>Opens the file at the reviewed commit version (or diff at that commit).</summary>
    internal string ReviewedVersionUri => $"app://file-at-commit:{CommitSha}:{FilePath}";

    /// <summary>Opens the current workspace file in its registered viewer.</summary>
    internal string WorkspaceFileUri => $"app://open-workspace-file:{FilePath}";
}

// ─── Changed-file evidence ────────────────────────────────────────────────────

/// <summary>Git-level status of a changed file.</summary>
internal enum FileChangeStatus { Added, Modified, Deleted, Renamed, Copied, Unknown }

/// <summary>A single file changed by a commit, with diff statistics.</summary>
internal sealed record ChangedFileEntry(
    string FilePath,
    FileChangeStatus Status,
    int Insertions,
    int Deletions,
    /// <summary>The commit that introduced this change.</summary>
    string CommitSha,
    FileLink Link);

// ─── Per-commit evidence ──────────────────────────────────────────────────────

/// <summary>Commit evidence for a completed task.</summary>
internal sealed record ReviewCommitEntry(
    CommitLink Link,
    /// <summary>Whether verification (build/test) passed for this commit.</summary>
    bool? VerificationPassed,
    IReadOnlyList<ChangedFileEntry> ChangedFiles);

// ─── Per-task snapshot ────────────────────────────────────────────────────────

/// <summary>Snapshot of a completed task within the review boundary.</summary>
internal sealed record ReviewTaskEntry(
    string TaskId,
    string Title,
    string? CompletionSummary,
    IReadOnlyList<ReviewCommitEntry> Commits,
    string? VerificationSummary = null);

// ─── Downstream released work ─────────────────────────────────────────────────

/// <summary>A task downstream of the gate that would be unblocked by approval.</summary>
internal sealed record DownstreamTaskEntry(
    string TaskId,
    string Title,
    string Status);

// ─── Independent work (early-window updates) ──────────────────────────────────

/// <summary>
/// Work completed independently (not gated) during the early review window.
/// Labeled separately from the gated review content.
/// </summary>
internal sealed record IndependentWorkEntry(
    string TaskId,
    string Title,
    string? CompletionSummary,
    IReadOnlyList<ReviewCommitEntry> Commits);

// ─── Root snapshot ────────────────────────────────────────────────────────────

/// <summary>
/// Pure, immutable snapshot of all evidence for a human approval-gate review.
/// Built by <see cref="ApprovalReviewSnapshotBuilder"/> from a <see cref="Plan"/>
/// and <see cref="PlanApprovalGate"/>. No WPF or IO dependencies.
/// </summary>
internal sealed record ApprovalReviewSnapshot(
    // ── Plan progress ──
    string PlanId,
    string PlanTitle,
    int CompletedTaskCount,
    int TotalTaskCount,
    string? CurrentStage,

    // ── Gate boundary ──
    string GateId,
    string GateReason,
    IReadOnlyList<string> AfterTaskIds,
    IReadOnlyList<string> BeforeTaskIds,

    // ── Completed work under review ──
    IReadOnlyList<ReviewTaskEntry> CompletedTasks,

    // ── Downstream work released by approval ──
    IReadOnlyList<DownstreamTaskEntry> DownstreamTasks,

    // ── Expandable changed-files section (all commits flattened) ──
    IReadOnlyList<ChangedFileEntry> AllChangedFiles,

    // ── Independent work completed outside the gate during the early window ──
    IReadOnlyList<IndependentWorkEntry> IndependentWork,

    /// <summary>UTC timestamp when this snapshot was built.</summary>
    DateTimeOffset BuiltAt);
