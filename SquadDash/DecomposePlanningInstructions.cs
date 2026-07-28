using System;
using System.IO;
using System.Linq;

namespace SquadDash;

internal static class DecomposePlanningInstructions
{
    internal const string FileName = "decompose-planning.md";
    private static readonly HashSet<string> MaterializedPaths = new(StringComparer.OrdinalIgnoreCase);
    private static readonly object MaterializeGate = new();

    internal static string LoadSpecification()
    {
        var assembly = typeof(DecomposePlanningInstructions).Assembly;
        var name = assembly.GetManifestResourceNames()
            .FirstOrDefault(n => n.EndsWith(FileName, StringComparison.OrdinalIgnoreCase));
        if (name is null)
        {
            SquadDashTrace.Write(TraceCategory.General,
                "Embedded decompose-planning.md resource was not found.");
            return string.Empty;
        }
        using var stream = assembly.GetManifestResourceStream(name);
        using var reader = new StreamReader(stream!);
        return reader.ReadToEnd();
    }

    internal static string EnsureMaterialized(string squadFolderPath)
    {
        var directory = Path.Combine(squadFolderPath, "instructions");
        var path = Path.Combine(directory, FileName);
        lock (MaterializeGate)
        {
            if (MaterializedPaths.Contains(path)) return path;
            var expected = LoadSpecification();
            if (string.IsNullOrWhiteSpace(expected))
                throw new InvalidOperationException("The embedded decomposition specification is unavailable.");
            Directory.CreateDirectory(directory);
            if (!File.Exists(path) || !string.Equals(File.ReadAllText(path), expected, StringComparison.Ordinal))
            {
                var tempPath = path + ".tmp";
                File.WriteAllText(tempPath, expected);
                File.Move(tempPath, path, overwrite: true);
            }
            MaterializedPaths.Add(path);
        }
        return path;
    }

    internal static string BuildOrdinaryPromptPointer(string specificationPath) =>
        "If the user's request is too large or interdependent to implement safely in one turn, " +
        "or if the user explicitly requests plan creation (using verbs like create, draft, devise, " +
        "design, write, make, propose, outline, or the /plan command), " +
        $"you MUST read `{specificationPath}` before responding, then follow its TASKS_JSON protocol. " +
        "If the user gives free-text approval or changes for a staged decomposition plan, you MUST " +
        "read the same file and follow its DECOMPOSE_DECISION_JSON protocol. Do not invent either " +
        "format from memory. If the user asks to retry or replan a blocked approved plan, follow the " +
        "file's DECOMPOSE_RECOVERY_JSON protocol. If SquadDash has paused an executing plan at a " +
        "human approval gate and the user approves in free text, follow the file's " +
        "PLAN_GATE_APPROVAL_JSON protocol. Emitting TASKS_JSON proposes a plan; it does not grant execution permission.";

    internal static string BuildPendingPlanContext(string squadFolderPath)
    {
        var plans = new PendingDecomposePlanStore(squadFolderPath).LoadAll();
        var sections = new List<string>();
        if (plans.Count > 0)
            sections.Add("\nPending decomposition plans (use the exact revision in DECOMPOSE_DECISION_JSON):\n" +
                         string.Join("\n", plans.Select(p =>
                             $"- groupId={p.Group.GroupId}; revision={p.Revision}; proposedBranch={p.Group.Branch}")));

        var tasksPath = Path.Combine(squadFolderPath, "tasks.md");
        if (File.Exists(tasksPath))
        {
            try
            {
                var parsed = TasksPanelParser.Parse(File.ReadAllLines(tasksPath));
                var blocked = parsed.OpenGroups
                    .SelectMany(group => group.Items)
                    .Where(item => item.TaskId is not null && item.DecomposeGroupId is not null &&
                                   (item.IsFailed || item.IsPartial) &&
                                   parsed.DecomposeGroups.ContainsKey(item.DecomposeGroupId))
                    .Select(item =>
                    {
                        var group = parsed.DecomposeGroups[item.DecomposeGroupId!];
                        var revision = group.HostRevision ?? PendingDecomposePlanStore.ComputeRevision(group);
                        return $"- groupId={group.GroupId}; revision={revision}; blockedTask={item.TaskId}; " +
                               "actions=retry-as-written|replan-failed-task";
                    })
                    .Distinct(StringComparer.Ordinal)
                    .ToArray();
                if (blocked.Length > 0)
                    sections.Add("\nBlocked approved plans (use DECOMPOSE_RECOVERY_JSON for explicit user intent):\n" +
                                 string.Join("\n", blocked));
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                SquadDashTrace.Write(TraceCategory.General,
                    $"Could not read blocked decomposition context: {ex.Message}");
            }
        }

        try
        {
            var awaitingGateLines = new PlanStore(squadFolderPath)
                .LoadAll()
                .Where(p => p.LifecycleStatus == PlanLifecycleStatus.AwaitingApproval)
                .SelectMany(p => p.ApprovalGates
                    .Where(g => g.Status == PlanGateStatus.AwaitingApproval)
                    .Select(g =>
                        $"- planId={p.PlanId}; revision={p.Revision}; gateId={g.GateId}; message={g.Message}"))
                .ToArray();
            if (awaitingGateLines.Length > 0)
                sections.Add(
                    "\nApproval-gate plans paused at a human gate — emit PLAN_GATE_APPROVAL_JSON when " +
                    "the user approves in free text (use the exact planId, gateId, and revision shown):\n" +
                    string.Join("\n", awaitingGateLines));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            SquadDashTrace.Write(TraceCategory.General,
                $"Could not read approval-gate plan context: {ex.Message}");
        }

        try
        {
            var interruptedLines = new PlanStore(squadFolderPath)
                .LoadAll()
                .Where(p => p.LifecycleStatus == PlanLifecycleStatus.Interrupted)
                .Select(p =>
                    $"- planId={p.PlanId}; branch={p.Branch}; " +
                    $"reason={p.InterruptionData?.Reason ?? "unknown"}; " +
                    $"lastTask={p.InterruptionData?.LastCompletedTaskId ?? "none"}; " +
                    $"recoveryState={p.InterruptionData?.RecoveryState ?? "none"}")
                .ToArray();
            if (interruptedLines.Length > 0)
            {
                sections.Add("\nInterrupted plans — do NOT independently assign or execute remaining tasks:\n" +
                    string.Join("\n", interruptedLines));
                sections.Add("\nIf the user asks to resume an interrupted plan, use the host Resume Plan button " +
                    "or emit a structured DECOMPOSE_RECOVERY_JSON decision to restart execution from the correct task.");
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            SquadDashTrace.Write(TraceCategory.General,
                $"Could not read interrupted plan context: {ex.Message}");
        }

        return string.Join(string.Empty, sections);
    }

    /// <summary>
    /// Builds a routing-context injection string for the current plan step.
    /// Reads routing.md and team.md from <paramref name="squadFolderPath"/>, resolves the
    /// most qualified active roster agent, and returns a Markdown section ready to be
    /// appended to the loop system prompt.  Returns an empty string on any IO failure.
    /// </summary>
    internal static string BuildPlanStepRoutingContext(
        string squadFolderPath,
        string stepId,
        string stepTitle,
        string stepDescription)
    {
        try
        {
            var routingPath = Path.Combine(squadFolderPath, "routing.md");
            var teamPath    = Path.Combine(squadFolderPath, "team.md");

            var routingContent = File.Exists(routingPath) ? File.ReadAllText(routingPath) : string.Empty;
            var teamContent    = File.Exists(teamPath)    ? File.ReadAllText(teamPath)    : string.Empty;

            if (string.IsNullOrWhiteSpace(routingContent))
            {
                SquadDashTrace.Write("PlanRouting",
                    $"routing.md not found or empty at '{routingPath}'; skipping agent routing for step {stepId}.");
                return string.Empty;
            }

            var rules  = PlanStepAgentResolver.ParseRoutingMd(routingContent);
            var agents = PlanStepAgentResolver.ParseTeamMd(teamContent);
            var ctx    = PlanStepRoutingContext.Resolve(
                stepId, stepTitle, stepDescription, squadFolderPath, rules, agents);

            if (ctx.Resolution.IsGenericFallback)
            {
                SquadDashTrace.Write("PlanRouting",
                    $"Step {stepId} fell back to generic worker: {ctx.Resolution.FallbackReason}");
                return
                    "## Routing Decision for This Step\n\n" +
                    $"No qualified roster agent was matched for task [{stepId}].\n" +
                    $"Reason: {ctx.Resolution.FallbackReason}\n" +
                    "You may proceed as a general-purpose engineering agent. Record the fallback " +
                    "reason in your response summary.";
            }

            SquadDashTrace.Write("PlanRouting",
                $"Step {stepId} routed to {ctx.Resolution.AgentName} ({ctx.Resolution.MatchedWorkType}).");

            const int MaxCharterChars = 1000;
            var charterSnippet = ctx.CharterContent is { Length: > 0 }
                ? ctx.CharterContent[..Math.Min(MaxCharterChars, ctx.CharterContent.Length)]
                : "(charter unavailable)";

            return
                "## Routing Decision for This Step\n\n" +
                $"You are implementing task [{stepId}] as **{ctx.Resolution.AgentName}**, " +
                $"a {ctx.Resolution.MatchedWorkType} specialist.\n" +
                "Your charter for this engagement:\n\n" +
                charterSnippet + "\n\n" +
                $"Routing basis: {ctx.Resolution.MatchedWorkType}";
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            SquadDashTrace.Write("PlanRouting",
                $"Could not build routing context for step {stepId}: {ex.Message}");
            return string.Empty;
        }
    }

    internal static string BuildOrdinaryPromptContext(string squadFolderPath)
    {
        try
        {
            var path = EnsureMaterialized(squadFolderPath);
            return BuildOrdinaryPromptPointer(path) + BuildPendingPlanContext(squadFolderPath);
        }
        catch (Exception ex)
        {
            SquadDashTrace.Write(TraceCategory.General,
                $"Could not materialize decomposition instructions in '{squadFolderPath}': {ex.Message}");
            var specification = LoadSpecification();
            return string.IsNullOrWhiteSpace(specification)
                ? "Large-task decomposition is unavailable for this turn; do not emit TASKS_JSON or DECOMPOSE_DECISION_JSON."
                : "The decomposition instruction file could not be materialized. Follow this embedded specification for this turn:\n\n" + specification;
        }
    }
}
