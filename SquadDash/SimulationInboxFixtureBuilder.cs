using System;
using System.Collections.Generic;

namespace SquadDash;

/// <summary>
/// Builds static InboxMessage fixtures for guided-tour demonstrations.
/// The messages are display-only: they appear in the Inbox panel without writing to <see cref="InboxStore"/>.
/// </summary>
internal static class SimulationInboxFixtureBuilder
{
    /// <summary>
    /// Builds a simulated high-priority message from a code-review agent.
    /// </summary>
    internal static InboxMessage BuildCodeReviewMessage() => new()
    {
        Id = "sim-inbox-codereview-001",
        Subject = "Review complete: auth endpoint hardening",
        From = "arjun-sen",
        Timestamp = DateTimeOffset.UtcNow.AddMinutes(-15),
        Read = false,
        Body = "I've completed the security review of the JWT authentication endpoints. " +
               "Found 2 issues that need attention:\n\n" +
               "1. Token expiry is set to 24h — recommend reducing to 1h with refresh tokens.\n" +
               "2. Password reset endpoint lacks rate limiting.\n\n" +
               "Both fixes are straightforward. Let me know if you'd like me to implement them.",
        Attachments = [],
        Actions = [],
        Priority = "high"
    };

    /// <summary>
    /// Builds a simulated medium-priority message with a plan proposal.
    /// </summary>
    internal static InboxMessage BuildPlanProposalMessage() => new()
    {
        Id = "sim-inbox-plan-002",
        Subject = "Proposal: database migration for user profiles",
        From = "talia-rune",
        Timestamp = DateTimeOffset.UtcNow.AddMinutes(-45),
        Read = false,
        Body = "I've drafted a migration plan for adding profile fields to the users table. " +
               "The plan includes:\n\n" +
               "- Add `display_name`, `avatar_url`, `bio` columns\n" +
               "- Create index on `display_name` for search\n" +
               "- Backfill existing users with defaults\n\n" +
               "Estimated completion: 2 iterations.",
        Attachments = [],
        Actions = [],
        Priority = "mid"
    };

    /// <summary>
    /// Builds a simulated low-priority informational message.
    /// </summary>
    internal static InboxMessage BuildStatusUpdateMessage() => new()
    {
        Id = "sim-inbox-status-003",
        Subject = "Documentation generation complete",
        From = "lyra-morn",
        Timestamp = DateTimeOffset.UtcNow.AddHours(-2),
        Read = true,
        Body = "The API reference documentation has been generated successfully. " +
               "All 14 public endpoints are now documented with request/response schemas.",
        Attachments = [],
        Actions = [],
        Priority = "low"
    };
}
