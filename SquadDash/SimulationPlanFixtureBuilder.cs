using System;
using System.Collections.Generic;

namespace SquadDash;

/// <summary>
/// Builds static Plan fixtures for guided-tour demonstrations.
/// The plans are display-only: they showcase the Plans panel UI without triggering execution.
/// </summary>
internal static class SimulationPlanFixtureBuilder
{
    internal const string ValidationShieldGalleryPlanId = "VALIDATION-SHIELD-GALLERY-20260804";

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

    /// <summary>
    /// Projects the Inbox-delivered validation gallery into a rich, non-persistent Plan model.
    /// It exists only inside the open viewer and cannot be executed or promoted into durable state.
    /// </summary>
    internal static Plan BuildValidationShieldGallery(PendingDecomposePlan pending)
    {
        if (!string.Equals(pending.Group.GroupId, ValidationShieldGalleryPlanId, StringComparison.Ordinal))
            throw new ArgumentException("The pending plan is not the validation shield gallery.", nameof(pending));

        var now = pending.CreatedAt ?? DateTimeOffset.UtcNow;
        var projected = PendingDecomposePlanAdapter.ToPlan(pending, now);
        string FakeCommit(string prefix) => prefix.PadRight(40, '0');

        var tasks = projected.Tasks.Select(task => task.TaskId switch
        {
            "SHIELD-GALLERY-001" => task with
            {
                Status = PlanTaskStatus.Complete,
                Commit = FakeCommit("a13f0c2"),
                CompletedAt = now.AddMinutes(-52),
                CompletionSummary = "Audience journeys documented and reviewed with representative organizers.",
            },
            "SHIELD-GALLERY-002" => task with
            {
                Status = PlanTaskStatus.Complete,
                Commit = FakeCommit("b74de91"),
                CompletedAt = now.AddMinutes(-45),
                CompletionSummary = "Content standards agreed for accessibility, privacy, and localization.",
            },
            "SHIELD-GALLERY-003" => task with
            {
                Status = PlanTaskStatus.Complete,
                Commit = FakeCommit("c08a4ef"),
                CompletedAt = now.AddMinutes(-31),
                CompletionSummary = "Registration paths designed for browse, enroll, confirm, and cancel scenarios.",
            },
            "SHIELD-GALLERY-004" => task with
            {
                Status = PlanTaskStatus.Failed,
                Commit = FakeCommit("d51be73"),
                CompletionSummary = "The event catalog was produced, but its privacy validation failed.",
            },
            "SHIELD-GALLERY-005" => task with
            {
                Status = PlanTaskStatus.Complete,
                Commit = FakeCommit("e629ac4"),
                CompletedAt = now.AddMinutes(-18),
                CompletionSummary = "Confirmation and reminder messages drafted for the supported channels.",
            },
            "SHIELD-GALLERY-006" => task with
            {
                Status = PlanTaskStatus.Partial,
                Commit = FakeCommit("f05c8d1"),
                CompletionSummary = "The combined attendee experience is partially assembled.",
            },
            "SHIELD-GALLERY-007" => task with
            {
                Status = PlanTaskStatus.HumanReviewRequired,
                Commit = FakeCommit("f730ab2"),
                CompletionSummary = "The accessibility review is ready for human acceptance.",
            },
            _ => task,
        }).ToArray();

        var validations = projected.Validations!.Select(validation => validation.ValidationId switch
        {
            "SHIELD-GALLERY-VAL-PASSED" => validation with
            {
                Status = PlanValidationStatus.Passed,
                StartedAt = now.AddMinutes(-43),
                CompletedAt = now.AddMinutes(-42),
                ValidatedCommit = FakeCommit("b74de91"),
                Summary = "Audience journeys and content standards use one consistent model.",
                Evidence = ["All audience roles map to a journey.", "Terminology and accessibility expectations agree."],
            },
            "SHIELD-GALLERY-VAL-STALE" => validation with
            {
                Status = PlanValidationStatus.Stale,
                StartedAt = now.AddMinutes(-30),
                CompletedAt = now.AddMinutes(-29),
                ValidatedCommit = FakeCommit("c08a4ef"),
                Summary = "Previously passed; an interaction update now requires revalidation.",
                Evidence = ["Prior keyboard-flow review is retained for audit."],
            },
            "SHIELD-GALLERY-VAL-FAILED" => validation with
            {
                Status = PlanValidationStatus.Failed,
                StartedAt = now.AddMinutes(-25),
                CompletedAt = now.AddMinutes(-24),
                Summary = "One sample listing still contains a realistic personal contact value.",
                Evidence = ["Privacy assertion failed for the neighborhood clinic sample."],
            },
            "SHIELD-GALLERY-VAL-VALIDATING" => validation with
            {
                Status = PlanValidationStatus.Validating,
                StartedAt = now.AddMinutes(-2),
                Summary = "Clarity and next-step language are being evaluated.",
                Evidence = ["Email and text-message samples are currently under review."],
            },
            "SHIELD-GALLERY-VAL-READY" => validation with
            {
                Status = PlanValidationStatus.Ready,
                Summary = "All prerequisite artifacts are available for validation.",
            },
            _ => validation with
            {
                Status = PlanValidationStatus.Pending,
                Summary = "Waiting for the launch rehearsal to complete.",
            },
        }).ToArray();

        return projected with
        {
            Source = PlanSource.Manual,
            LifecycleStatus = PlanLifecycleStatus.Approved,
            Branch = "(visualization fixture — no branch)",
            Tasks = tasks,
            ApprovalGates = projected.ApprovalGates.Select(gate => gate with
            {
                Status = PlanGateStatus.Pending,
                PresentationAnchor = "stage:1",
            }).ToArray(),
            Progress = new PlanProgress(4, tasks.Length),
            Timestamps = new PlanTimestamps(CreatedAt: now),
            Validations = validations,
        };
    }
}
