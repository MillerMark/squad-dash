using System;
using System.IO;
using System.Linq;

namespace SquadDash;

internal static class PlanningSpecialistInstructions
{
    internal const string FileName = "planning-specialist-charter.md";

    internal static string LoadCharter()
    {
        var assembly = typeof(PlanningSpecialistInstructions).Assembly;
        var resourceName = assembly.GetManifestResourceNames()
            .FirstOrDefault(name => name.EndsWith(FileName, StringComparison.OrdinalIgnoreCase));
        if (resourceName is not null)
        {
            using var stream = assembly.GetManifestResourceStream(resourceName);
            if (stream is not null)
            {
                using var reader = new StreamReader(stream);
                return reader.ReadToEnd();
            }
        }

        var packagedPath = GetPackagedCharterPath();
        if (File.Exists(packagedPath))
            return File.ReadAllText(packagedPath);

        SquadDashTrace.Write(TraceCategory.General,
            "Built-in planning-specialist charter resource was not found.");
        return string.Empty;
    }

    internal static string GetPackagedCharterPath() =>
        Path.Combine(AppContext.BaseDirectory, "Assets", FileName);

    internal static string BuildDelegationGuidance()
    {
        var packagedPath = GetPackagedCharterPath();
        var charterSource = File.Exists(packagedPath)
            ? $"Read the shipped fallback charter at `{packagedPath}` and inject its complete contents into the temporary worker prompt."
            : "Inject the complete shipped fallback charter below into the temporary worker prompt:\n\n" + LoadCharter();

        return $$"""
            ## Planning specialist routing

            Apply these rules only when creating a new plan or revising an existing plan.

            1. Read `.squad/team.md` and `.squad/routing.md`. If an active roster member is explicitly
               assigned plan creation or plan revision (for example, a Planning Architect), delegate
               plan authorship to that member with `delegate_roster_agent`. The member's workspace
               charter is authoritative so user customizations are preserved.
            2. If no active roster planning specialist exists, launch exactly one temporary worker with
               the generic `task` tool as the planning specialist. Do not give that worker Rory's name or
               any roster identity. {{charterSource}}
            3. Give the specialist the user's complete request, the exact live planning or revision
               protocol path, relevant repository and decision context, and the current team and routing
               files. Require the specialist to read the live protocol directly; a Coordinator-authored
               summary is supplemental context and must never be presented as the authoritative schema.
               Require `TASKS_JSON` for new plan creation or `PLAN_REVISION_JSON` for plan revision,
               without implementing changes.
            4. After the specialist returns, the Coordinator remains responsible for checking the payload
               against the live host protocol before emitting that same required marker at the top level.
               Check exact field names and these parser-critical contracts: task IDs are
               `{groupId}-NNN`; validation IDs use the `validationId` field and
               `{groupId}-VAL-NNN`; command or hybrid validations use `commands`; proof requirements
               use `requirementId`, `proofType`, and `description`. Also check dependency references,
               validation boundaries, and the final audit frontier. A nested agent response alone cannot
               be collected by SquadDash. Do not merely summarize or paraphrase the structured payload.
               Describe the final handoff as submitting or proposing the plan. Never claim that the plan
               was staged, added to the Plans panel, delivered to Inbox, or made available for approval;
               only the host may report those outcomes after durable persistence and validation.
            5. If delegation is unavailable after one reasonable attempt, apply the shipped Planning
               Architect charter yourself and still emit the required top-level payload. Never drop the
               structured-output requirement because a roster specialist is absent.
            6. If the delegated candidate is invalid, re-engage the same specialist with the complete
               rejected candidate, the exact validation diagnostics, and the live protocol path. Require
               one complete corrected payload rather than a patch or a partial list of substitutions.
            """;
    }

    internal static string BuildHireCharterGuidance(string universeName, string agentName)
    {
        if (!string.Equals(universeName, SquadInstallerService.SquadDashUniverseName,
                StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(agentName, "Rory", StringComparison.OrdinalIgnoreCase))
        {
            return string.Empty;
        }

        var charter = LoadCharter();
        return string.IsNullOrWhiteSpace(charter)
            ? string.Empty
            : "Use the following shipped Planning Architect charter as Rory's baseline role contract. " +
              "Personalize the title and identity for Rory, preserve the operational requirements, and " +
              "allow the user-provided hiring fields above to refine it.\n\n" + charter;
    }
}
