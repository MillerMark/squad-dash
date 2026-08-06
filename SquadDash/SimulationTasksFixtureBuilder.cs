namespace SquadDash;

/// <summary>
/// Builds static TaskItem fixtures for guided-tour demonstrations.
/// The tasks are display-only: they appear in the Tasks panel without writing to tasks.md.
/// </summary>
internal static class SimulationTasksFixtureBuilder
{
    /// <summary>
    /// Builds a high-priority task demonstrating an in-progress item.
    /// </summary>
    internal static TaskItem BuildAuthEndpointTask() => new(
        Text: "Implement JWT authentication endpoints",
        Owner: "arjun",
        IsUserOwned: false,
        IsChecked: false,
        Emoji: "🔴",
        RawLine: "- [ ] 🔴 Implement JWT authentication endpoints @arjun",
        Description: "Create login, logout, and token refresh endpoints with bcrypt password hashing.",
        TaskId: "sim-task-auth-001");

    /// <summary>
    /// Builds a medium-priority task demonstrating a pending item.
    /// </summary>
    internal static TaskItem BuildDatabaseMigrationTask() => new(
        Text: "Add database migration for user profiles",
        Owner: "lyra",
        IsUserOwned: false,
        IsChecked: false,
        Emoji: "🟡",
        RawLine: "- [ ] 🟡 Add database migration for user profiles @lyra",
        Description: "Create EF Core migration adding profile fields to the users table.",
        TaskId: "sim-task-db-002");

    /// <summary>
    /// Builds a low-priority task demonstrating a backlog item.
    /// </summary>
    internal static TaskItem BuildDocumentationTask() => new(
        Text: "Write API documentation for public endpoints",
        Owner: null,
        IsUserOwned: false,
        IsChecked: false,
        Emoji: "🟢",
        RawLine: "- [ ] 🟢 Write API documentation for public endpoints",
        Description: "Generate OpenAPI spec and developer guide for all REST endpoints.",
        TaskId: "sim-task-docs-003");
}
