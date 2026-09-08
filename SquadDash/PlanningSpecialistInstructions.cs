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
        return $$"""
            ## Planning specialist routing

            Apply these rules only when creating a new plan, correcting a staged proposal,
            or revising an accepted durable plan.

            1. Delegate every plan creation, staged-plan correction, and accepted-plan revision to the
               built-in named Planning Architect, `rory`, with `delegate_roster_agent`. Rory is a
               host-provided permanent specialist and may be absent from the user-editable rows in
               `.squad/team.md`; the named-agent route and Rory's workspace charter are authoritative.
               Do not author even a small or mechanical plan delta in the Coordinator voice, and do not
               replace Rory with a generic `task` worker. This keeps plan authorship on the configured
               spawned-named-agent model profile.
            2. Read `.squad/team.md` and `.squad/routing.md` before delegating so Rory receives the current
               roster, ownership, and workspace customizations. Rory plans work for those specialists but
               must not be assigned implementation or verification merely because Rory authored the plan.
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
               Reject any task whose only meaningful outcome is a human observation. Replace that shell
               with an AI-executable preparation task followed by a human gate. The preparation task should
               produce a Markdown runbook—plain-language, step-by-step Setup, Open, Act, Observe, and Cleanup
               instructions—and deliver or attach it through the SquadDash Inbox. Where appropriate, also
               produce a disposable fixture, sample project, sample files, test data, or diagnostics as
               declared outputs and reference them exactly from the gate.
               Classify validation using the current executor's real capability boundary. Repository,
               command, deterministic test, and genuinely available browser/service automation may be
               AI-reachable. Native or host-embedded application interaction, inaccessible build/install/
               restart flows, physical keyboard or mouse input, voice, cameras, microphones, speakers,
               and live human sight, hearing, timing, or focus are human-only. For mixed work, AI owns
               preparation and evidence packaging while the human owns only irreducible interaction and
               perception. Require exact setup, AI-prepared artifacts, human actions, pass/fail observations,
               cleanup, failure-reporting guidance, and Inbox delivery before accepting the plan.
               Treat that preparation as a manual test kit. It is not complete until the executor sends a
               self-contained Inbox message naming an exact reproducible test target. Use branch and commit
               for committed work; for uncommitted work identify the workspace, current HEAD, dirty state,
               and applicable build, package, or artifact without requiring a commit merely for identity.
               Include prerequisites and AI-prepared state, numbered Setup/Open/Act steps, expected pass/fail observations mapped to
               stable gate proof ID, exact failure-evidence capture and reporting instructions, and Cleanup.
               Put the full instructions in the Inbox body and embed a descriptive inline link beside each
               step that needs a runbook, solution, project, sample file, or folder. Use the existing
               `[Open label](app://open-path:path/from/workspace/root)` Inbox syntax. Attachments may duplicate
               useful entry points but never replace contextual inline links. Require the
               Inbox JSON file escape hatch for long or parser-sensitive content. A transcript-only promise,
               prose-only path, or action that starts another agent is not delivery. The following gate must
               depend on the preparation task and reuse its proof IDs and fixture outputs.
               Design every gate recipe for SquadDash's chronological live approval form: Setup the
               prepared build; Open and perform the exact linked file/folder and interaction; Observe the
               expected result; place one atomic checkbox question for the stable requirementId immediately
               after that observation; then Cleanup. SquadDash binds that requirement to one persisted
               checkbox shown in both the Inbox and transcript. Do not invent a disconnected prose checklist.
               Prepare fixtures primarily for new behavior or unavailable, fragile, destructive, or costly
               state. Established regression checks may use the reviewer's familiar existing files and may
               be grouped into one concise checklist. Prefer separate proof items for distinct new behavior.
               This is planning judgment, not a deterministic reason to reject or halt a plan.
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
