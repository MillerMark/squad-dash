using System;

namespace SquadDash;

/// <summary>
/// Builds static CommitApprovalItem fixtures for guided-tour demonstrations.
/// The approvals are display-only: approve/reject actions are inert for simulated items.
/// </summary>
internal static class SimulationApprovalsFixtureBuilder
{
    /// <summary>
    /// Builds a simulated approval for a feature commit awaiting review.
    /// </summary>
    internal static CommitApprovalItem BuildFeatureCommitApproval() => new(
        Id: "sim-approval-feat-001",
        CommitSha: "a3f8c12",
        CommitUrl: null,
        Description: "Add user profile API endpoint",
        TurnStartedAt: DateTimeOffset.UtcNow.AddMinutes(-20),
        TurnPromptHint: "Implement the user profile REST endpoint with validation",
        IsApproved: false,
        OriginalPrompt: "Implement the user profile REST endpoint with input validation and error handling.",
        FeatureGroup: "User Management");

    /// <summary>
    /// Builds a simulated approval for a refactoring commit.
    /// </summary>
    internal static CommitApprovalItem BuildRefactorCommitApproval() => new(
        Id: "sim-approval-refactor-002",
        CommitSha: "b7d4e09",
        CommitUrl: null,
        Description: "Extract shared validation helpers",
        TurnStartedAt: DateTimeOffset.UtcNow.AddMinutes(-10),
        TurnPromptHint: "Refactor duplicated validation logic into a shared helper",
        IsApproved: false,
        OriginalPrompt: "Refactor the duplicated validation logic in UserController and OrderController into a shared ValidationHelper class.",
        FeatureGroup: "Code Quality");

    /// <summary>
    /// Builds a simulated approval for a documentation commit.
    /// </summary>
    internal static CommitApprovalItem BuildDocsCommitApproval() => new(
        Id: "sim-approval-docs-003",
        CommitSha: "c91be55",
        CommitUrl: null,
        Description: "Update API reference documentation",
        TurnStartedAt: DateTimeOffset.UtcNow.AddMinutes(-5),
        TurnPromptHint: "Update the API docs to reflect new profile endpoints",
        IsApproved: false,
        OriginalPrompt: "Update the API reference documentation to include the new user profile endpoints and their request/response schemas.",
        FeatureGroup: "Documentation");
}
