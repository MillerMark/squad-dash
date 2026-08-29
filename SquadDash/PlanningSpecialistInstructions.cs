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

            Apply these rules only when creating a new plan, correcting a staged proposal,
            or revising an accepted durable plan.

            1. Read `.squad/team.md` and `.squad/routing.md`. If an active roster member is explicitly
               assigned plan creation or revision work (for example, a Planning Architect), delegate
               plan authorship to that member with `delegate_roster_agent`. The member's workspace
               charter is authoritative so user customizations are preserved. For a small mechanical
               staged correction whose exact patch is already established by the user, the Coordinator
               may author the compact delta directly instead of paying for a second planning pass.
            2. If no active roster planning specialist exists, launch exactly one temporary worker with
               the generic `task` tool as the planning specialist. Do not give that worker Rory's name or
               any roster identity. {{charterSource}}
            3. Give the specialist the user's complete request, the exact live planning or revision
               protocol path, relevant repository and decision context, and the current team and routing
               files. Require the specialist to read the live protocol directly; a Coordinator-authored
               summary is supplemental context and must never be presented as the authoritative schema.
               For a staged correction, infer casual references such as "this plan" from the conversation
               and pending-plan context; never require the user to provide an internal ID or protocol.
               Pass the compact STAGED_PLAN_REVISION_REQUEST_JSON routing envelope from the pending-plan
               context, copy its identity, outline, and size fields into the tool's structured
               `staged_plan_revision_request` argument, include the user's requested changes, and add no
               invented operation schema; the specialist expands that request by reading the host-owned
               protocol. Require the specialist to read the compact revision-locked outline first, use
               its short T/G/V references and source selectors to retrieve only affected entities, and
               avoid printing the complete definition into model context. Prefer exact-match text-edit
               operations for clause-level corrections instead of replacing complete long descriptions,
               assertions, or nested text properties.
               Calibrate plan detail to the approval decision: capture observable outcomes, constraints,
               cross-task contracts, risks, and validation boundaries. Leave local implementation choices
               to the executing agent unless a verified compatibility or safety boundary requires them.
               Do not prescribe speculative class names, helper breakdowns, file-by-file edits, exact test
               counts, or exhaustive test-case inventories; describe the behaviors and failure modes that
               must be proven. Treat each task as a bounded execution contract, not a pseudo-implementation.
               Require `STAGED_PLAN_REVISION_JSON` for a targeted correction to an unapproved staged
               proposal. Apply the injected size-aware revisionStrategy: a small plan may be replaced for
               a broad rewrite affecting most of its graph, while medium and large plans strongly prefer
               deltas and a large plan is replaced only for a genuine near-total rewrite. Require
               `PLAN_REVISION_JSON` only when revising an accepted durable
               plan and SquadDash supplied the live revision protocol. Do not substitute one protocol
               for another, and do not implement changes.
            4. A verified `delegate_roster_agent` staged-revision response is collected, validated, and
               applied directly by SquadDash when its request envelope and returned identity match. If
               the tool returns a direct-collection receipt, do not run shell extraction, copy the JSON,
               or re-emit the marker; report the short receipt and allow the host to present approval
               controls. For new plans, accepted-plan revisions, generic workers, and any fallback response
               without that receipt, the Coordinator remains responsible for checking the payload against
               the live host protocol before emitting the required marker at the top level. Check exact
               field names and these parser-critical contracts: task IDs are
               `{groupId}-NNN`; validation IDs use the `validationId` field and
               `{groupId}-VAL-NNN`; command or hybrid validations use `commands`; proof requirements
               use `requirementId`, `proofType`, and `description`. Also check dependency references,
               validation boundaries, and any declared audit frontier. Confirm that completion proof reuses
               task-level evidence and that a final verification or audit is present only for a material
               remaining gap, never as overlapping ceremonial work. Limit each human review checkpoint to
               at most seven atomic checks. For every human-only proof, declare accessMode as
               directly-reachable, recipe, or fixture. A recipe must tell the reviewer: Setup (how the
               required state is produced), Open (the exact menu, screen, command, or interaction), Observe
               (what to confirm), and Cleanup (how the verification state is removed). Fixture access must
               use explicit fixtureTaskId and fixtureOutputId references to an upstream declared output;
               never rely on heuristic inference from prose. If ordinary product UI cannot reproduce the
               state, add the simulation, fixture, shortcut, or developer entry point before the review gate.
               Before submitting any plan, inspect every promised user-visible, interactive, live-UI,
               restart, timing, focus, window-state, notification, and other human-observable outcome.
               Every outcome that cannot be truthfully established by code inspection or an automated test
               must have a matching human-only proof requirement at an approval checkpoint after the work
               that produces it and before the final audit. AI assessment, source inspection, screenshots
               taken without the prescribed interaction, and headless tests do not substitute for that
               checkpoint. Omission is a plan validation error: correct the plan rather than allowing an
               inevitably failing completion audit.
               Do not merely summarize or paraphrase
               a payload that still requires top-level submission.
               Describe the final handoff as submitting or proposing the plan. Never claim that the plan
               was staged, added to the Plans panel, delivered to Inbox, or made available for approval;
               only the host may report those outcomes after durable persistence and validation.
            5. Provider throttling and rate-limit waits are not delegation unavailability. While the host
               is waiting or resuming the same specialist session, do not take over the specialist's work.
               If delegation reaches a terminal failure after the host exhausts its bounded recovery,
               apply the shipped Planning Architect charter yourself and still emit the required top-level
               payload. Never drop the
               structured-output requirement because a roster specialist is absent.
            6. If the delegated candidate is invalid, re-engage the same specialist with the complete
               rejected candidate, the exact validation diagnostics, and the live protocol path. Require
               one complete corrected payload of the same protocol type. A corrected staged delta remains
               `STAGED_PLAN_REVISION_JSON`; do not turn it into a full graph unless the rewrite is sweeping.
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
