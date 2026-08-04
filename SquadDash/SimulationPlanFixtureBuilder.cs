using System;
using System.Collections.Generic;

namespace SquadDash;

/// <summary>
/// Builds static Plan fixtures for guided-tour demonstrations.
/// The plans are display-only: they showcase the Plans panel UI without triggering execution.
/// </summary>
internal static class SimulationPlanFixtureBuilder
{
    /// <summary>
    /// Builds a representative demo plan with tasks in various states
    /// to showcase the Plans panel during guided tours.
    /// </summary>
    internal static Plan BuildDemoPlan()
    {
        var now = DateTimeOffset.UtcNow;

        var tasks = new List<PlanTask>
        {
            new PlanTask(
                TaskId: "sim-task-001",
                Title: "Set up project scaffolding",
                Description: "Create initial project structure with configuration files and dependencies.",
                DependsOn: [],
                Priority: "high",
                Status: PlanTaskStatus.Complete,
                CompletedAt: now.AddMinutes(-45),
                CompletionSummary: "Project structure created with all required config files."),

            new PlanTask(
                TaskId: "sim-task-002",
                Title: "Implement data access layer",
                Description: "Build repository pattern with Entity Framework Core for database operations.",
                DependsOn: ["sim-task-001"],
                Priority: "high",
                Status: PlanTaskStatus.Complete,
                CompletedAt: now.AddMinutes(-20),
                CompletionSummary: "Data layer implemented with full CRUD operations."),

            new PlanTask(
                TaskId: "sim-task-003",
                Title: "Build REST API endpoints",
                Description: "Create controller layer with authentication, validation, and error handling.",
                DependsOn: ["sim-task-002"],
                Priority: "high",
                Status: PlanTaskStatus.Executing),

            new PlanTask(
                TaskId: "sim-task-004",
                Title: "Add integration tests",
                Description: "Write end-to-end tests covering all API endpoints with test fixtures.",
                DependsOn: ["sim-task-003"],
                Priority: "medium",
                Status: PlanTaskStatus.Pending),
        };

        var validations = new List<PlanValidationNode>
        {
            new PlanValidationNode(
                ValidationId: "sim-val-001",
                Title: "API contract validation",
                Description: "Verify all endpoints match the OpenAPI spec.",
                AfterTaskIds: ["sim-task-003"],
                BeforeTaskIds: ["sim-task-004"],
                Assertions: ["All routes respond with correct status codes", "Response schemas match spec"],
                OutputIds: null,
                Mode: "automatic",
                Commands: ["dotnet test --filter Category=Contract"],
                RevalidateAtCompletion: true,
                Status: PlanValidationStatus.Pending),
        };

        return new Plan(
            PlanId: "sim-plan-demo-001",
            Revision: "sim-rev-001",
            Source: PlanSource.Manual,
            LifecycleStatus: PlanLifecycleStatus.Approved,
            Title: "Demo: API Service Build-out",
            Branch: "feature/demo-api-service",
            Summary: "A demonstration plan showcasing a typical multi-task API build workflow.",
            Tasks: tasks,
            ApprovalGates: [],
            Progress: new PlanProgress(CompletedCount: 2, TotalCount: 4, ExecutingTaskId: "sim-task-003"),
            Timestamps: new PlanTimestamps(CreatedAt: now.AddHours(-1), AcceptedAt: now.AddMinutes(-50)),
            Validations: validations);
    }
}
