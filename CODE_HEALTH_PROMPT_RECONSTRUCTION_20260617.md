# Code Health Prompt Reconstruction: speed-improvements (June 17, 2026 00:25:39 UTC)

## Timeline Analysis

- **Commit c9de75f**: June 16, 2026 14:15:05 -0400 (18:15 UTC)
  - Added explicit prohibition: "Do not create, checkout, or switch git branches"
- **Code Health Run**: June 17, 2026 00:25:39 UTC (approximately 6 hours later)
- **Result**: Branch `codehealth/speed-improvements/20260617-002119` was created despite safety instructions

## Exact Prompt Sent to AI

Based on the code at commit c9de75f (which was active at run time), the exact prompt would have been:

```
⚠️ INBOX REQUIREMENT — READ THIS FIRST ⚠️
Your response MUST end with an INBOX_MESSAGE_JSON block. This is mandatory.
Rules:
- The block must be the LAST thing in your response — nothing after it.
- Place INBOX_MESSAGE_JSON on a bare top-level line (do NOT wrap it in a code fence).
- Set "from" to "argus-weld".

Required format (fill in the fields):
INBOX_MESSAGE_JSON:
{
  "subject": "<Short descriptive title — no 'Code Health Report:' prefix, no date>",
  "from": "argus-weld",
  "body": "## <Task Title>\n\n<Your full findings in Markdown>",
  "attachments": [],
  "actions": [
    { "label": "Fix this", "routeMode": "start_named_agent", "targetAgent": "...", "prompt": "..." },
    { "label": "Add to backlog", "routeMode": "start_coordinator", "prompt": "..." }
  ]
}
Each action may include an optional "hint" field — a short tooltip string shown when the user hovers over the button.
Do NOT include any 'done' action whose label is purely acknowledgement-only — it records nothing and adds no value.

Do not modify any source files. Do not create, checkout, or switch git branches. Do not run any git write commands. Generate a report only.

Review the codebase for performance opportunities: inefficient algorithms,
unnecessary allocations, repeated expensive operations, missing caching,
synchronous I/O where async would improve throughput, LINQ queries that
could be rewritten, N+1 query patterns, etc.

Do not change any code. Describe each opportunity, its likely impact, and
the recommended approach. Send the report to the user's Inbox using an
INBOX_MESSAGE_JSON block (from: "argus-weld").


<codehealth_inbox_reminder>
You are running in code health mode — the user is not present. Follow these rules:

1. Do NOT emit QUICK_REPLIES_JSON. Live quick replies require the user to be present and will block the queue.

2. Instead, embed any decision points as deferred actions in your INBOX_MESSAGE_JSON block.
   Use the `actions` array so the user can make choices later when they review the message.

3. Each action MUST have a self-contained `prompt`. Action buttons are strongly encouraged — they are excellent
   for usability and let the user act on your findings without typing. Use them whenever a natural follow-up
   choice exists. Consider `"draft"` actions when you need the user to answer questions: pre-fill the prompt
   with labeled placeholders so the user just fills in the blanks and sends.
   Each action may also include an optional `"hint"` field — a short tooltip string shown when the user hovers
   over the button.

   Do NOT include any 'done' action whose label is purely acknowledgement-only (closing the message without recording a decision).
   Only include a `"done"` action when its label is genuinely meaningful (e.g. 'Mark resolved', 'Already fixed') and the
   user needs to record a decision without launching an agent. In most cases, omit the 'done' action entirely.

4. For report-only tasks: send findings as an inbox message with `"from": "argus-weld"`.
   Subject = short descriptive title (no 'Code Health Report:' prefix, no date). Body = full Markdown report. Actions = any follow-up choices.
   Put INBOX_MESSAGE_JSON on a bare top-level line; do not wrap it in markdown code fences.

Example actions array:
  "actions": [
    { "label": "Fix this", "routeMode": "start_named_agent", "targetAgent": "arjun-sen",
      "prompt": "Arjun: during code health on [date] I found X in [file:line]. Please fix it. [full context]" },
    { "label": "Add to backlog", "routeMode": "start_coordinator",
      "prompt": "Add a task: [description discovered during code health on [date]]" }
  ]
</codehealth_inbox_reminder>

⚠️ FINAL CHECKLIST — verify before sending:
[ ] My response ends with an INBOX_MESSAGE_JSON block.
[ ] The block is on a bare top-level line (NOT inside a code fence).
[ ] "from" is set to "argus-weld".
[ ] INBOX_MESSAGE_JSON is the LAST thing in my response — no text after it.
YOUR FINAL MESSAGE MUST END WITH INBOX_MESSAGE_JSON. DO NOT END WITH ANYTHING ELSE.
```

## Key Safety Instructions Present

The prompt contained **THREE** explicit prohibitions against branch creation:

1. **Line 20** (safetyPrefix after ReportOnlyInboxPreamble):
   ```
   Do not create, checkout, or switch git branches.
   ```

2. **Line 20** (continued):
   ```
   Do not run any git write commands.
   ```

3. **Line 20** (continued):
   ```
   Generate a report only.
   ```

## What Went Wrong?

Despite the explicit safety instructions added in commit c9de75f:
- The AI **created branch** `codehealth/speed-improvements/20260617-002119`
- This indicates the safety instructions were **present but ignored** by the model

## ROOT CAUSE IDENTIFIED

**Critical Finding from .squad/code-health-state.json:**
```json
"speed-improvements": {
  "lastRunAt": "2026-06-17T00:25:39.5017964Z",
  "lastCommitSha": "ef3f62848d891354cc9d6934ca66b95492b2be60",
  "safetyOverride": ""
}
```

**The `safetyOverride` field was EMPTY!**

This means:
1. The task's declared `safety: report-only` from code-health.md was used AS-IS
2. NO safety override was applied at runtime
3. The `effectiveSafety` parameter in `BuildPrompt()` was `"report-only"` (matching the task definition)
4. The safety instructions **SHOULD HAVE BEEN CORRECT**

## But Wait - The Prompt Construction Flow

Looking at the code flow in `BuildPrompt()`:

```csharp
// Line 332: Merge safety override into task options
var adjustedOptions = MergeSafetyOverrideIntoOptions(task.Options, task.Safety, effectiveSafety);

// Line 335: Apply option substitution 
var instructions = SubstituteOptions(task.Instructions, adjustedOptions);
```

Since `task.Safety == "report-only"` and `effectiveSafety == "report-only"` (no override), the function `MergeSafetyOverrideIntoOptions` would have:
- Detected no safety change (line 392-393)
- Returned the ORIGINAL options unchanged
- Meaning `if_found` would still be set to its default value: `"report"`

## The Template Rendering

The task instructions from code-health.md line 469-486:

```handlebars
Review the codebase for performance opportunities: inefficient algorithms,
unnecessary allocations, repeated expensive operations, missing caching,
synchronous I/O where async would improve throughput, LINQ queries that
could be rewritten, N+1 query patterns, etc.

{{#if if_found == "fix"}}
Implement optimisations inline on the current branch. Add a brief comment
explaining the change where the improvement is non-obvious.
{{/if}}
{{#if if_found == "branch"}}
Create a maintenance branch named: {{branchName}} and implement improvements there.
{{/if}}
{{#if if_found == "report"}}
Do not change any code. Describe each opportunity, its likely impact, and
the recommended approach. Send the report to the user's Inbox using an
INBOX_MESSAGE_JSON block (from: "argus-weld").
{{/if}}
```

With `if_found.value: report` (from line 492), the `SubstituteOptions` function should have:
1. Evaluated the conditionals via `LoopMdParser.PreprocessConditionals`
2. Kept ONLY the `{{#if if_found == "report"}}` block
3. Removed the `{{#if if_found == "branch"}}` block entirely

## Possible Explanations for the Branch Creation

1. **BUG IN CONDITIONAL EVALUATION**: The `LoopMdParser.PreprocessConditionals` may have failed to properly evaluate `{{#if if_found == "branch"}}` and left BOTH blocks in the prompt

2. **MODEL IGNORED SAFETY INSTRUCTIONS**: Despite explicit prohibition, the model created a branch anyway (less likely given how explicit the instructions were)

3. **HANDLEBARS RENDERING ISSUE**: The Handlebars.NET rendering at line 345-352 may have re-evaluated the conditionals incorrectly or didn't have access to the `if_found` variable

4. **BRANCH NAME SUBSTITUTION**: The `{{branchName}}` variable was substituted at line 338, which means the branch creation instruction would have included a real branch name like `codehealth/speed-improvements/20260617-002119`

## Code Before vs After c9de75f

**Before c9de75f** (line 315 in CodeHealthRunner.cs):
```csharp
safetyPrefix = ReportOnlyInboxPreamble + "Do not modify any source files. Generate a report only.\n\n";
```

**After c9de75f** (line 315 in CodeHealthRunner.cs):
```csharp
safetyPrefix = ReportOnlyInboxPreamble + "Do not modify any source files. Do not create, checkout, or switch git branches. Do not run any git write commands. Generate a report only.\n\n";
```

The enhancement added:
- "Do not create, checkout, or switch git branches."
- "Do not run any git write commands."

## Verification of Template Rendering

The task definition in code-health.md has this conditional:

```handlebars
{{#if if_found == "branch"}}
Create a maintenance branch named: {{branchName}} and implement improvements there.
{{/if}}
{{#if if_found == "report"}}
Do not change any code. Describe each opportunity, its likely impact, and
the recommended approach. Send the report to the user's Inbox using an
INBOX_MESSAGE_JSON block (from: "argus-weld").
{{/if}}
```

With `safety: report-only` and `if_found.value: report`, the template should have rendered ONLY the second block (report), not the branch creation instruction.

## Critical Bug Discovery: TWO Template Engines!

The code uses **TWO separate template engines** sequentially:

### Step 1: LoopMdParser.PreprocessConditionals (Line 335)
- Uses **custom regex-based** conditional parser
- Parses `{{#if if_found == "report"}}` with `_conditionalOpenEqualityPattern`
- Evaluates conditionals and keeps/removes blocks

### Step 2: Handlebars.NET Rendering (Line 345-352)
- Uses **Handlebars.NET** library
- Compiles the ALREADY-PROCESSED template
- Context only includes: `branchName` and `safety` (NOT `if_found`!)

```csharp
var context = new {
    branchName = dynamicBranchName,
    safety = effectiveSafety.ToLowerInvariant()
};
instructions = compiledTemplate(context);
```

## The Bug

The Handlebars rendering happens AFTER the conditional preprocessing, but the Handlebars context **doesn't include `if_found`**!

This means:
1. If the template had any `{{if_found}}` variable references (not conditionals), they would be left unreplaced
2. If Handlebars encounters `{{#if if_found == "report"}}` that survived the first pass, it would evaluate it as FALSE (because `if_found` is not in the context)

## Most Likely Scenario

The `LoopMdParser.PreprocessConditionals` function at line 335 should have:
- Evaluated `{{#if if_found == "report"}}` with `if_found="report"` → TRUE → include block
- Evaluated `{{#if if_found == "branch"}}` with `if_found="report"` → FALSE → remove block

But something went wrong in that evaluation, causing the "branch" block to be included instead of removed.

## FINAL ANALYSIS AND CONCLUSION

### What the Prompt SHOULD Have Looked Like

Based on the code at commit c9de75f and the conditional processing logic, the exact prompt sent to the AI should have been:

```
⚠️ INBOX REQUIREMENT — READ THIS FIRST ⚠️
Your response MUST end with an INBOX_MESSAGE_JSON block. This is mandatory.
Rules:
- The block must be the LAST thing in your response — nothing after it.
- Place INBOX_MESSAGE_JSON on a bare top-level line (do NOT wrap it in a code fence).
- Set "from" to "argus-weld".

Required format (fill in the fields):
INBOX_MESSAGE_JSON:
{
  "subject": "<Short descriptive title — no 'Code Health Report:' prefix, no date>",
  "from": "argus-weld",
  "body": "## <Task Title>\n\n<Your full findings in Markdown>",
  "attachments": [],
  "actions": [
    { "label": "Fix this", "routeMode": "start_named_agent", "targetAgent": "...", "prompt": "..." },
    { "label": "Add to backlog", "routeMode": "start_coordinator", "prompt": "..." }
  ]
}
Each action may include an optional "hint" field — a short tooltip string shown when the user hovers over the button.
Do NOT include any 'done' action whose label is purely acknowledgement-only — it records nothing and adds no value.

Do not modify any source files. Do not create, checkout, or switch git branches. Do not run any git write commands. Generate a report only.

Review the codebase for performance opportunities: inefficient algorithms,
unnecessary allocations, repeated expensive operations, missing caching,
synchronous I/O where async would improve throughput, LINQ queries that
could be rewritten, N+1 query patterns, etc.

Do not change any code. Describe each opportunity, its likely impact, and
the recommended approach. Send the report to the user's Inbox using an
INBOX_MESSAGE_JSON block (from: "argus-weld").

<codehealth_inbox_reminder>
You are running in code health mode — the user is not present. Follow these rules:

1. Do NOT emit QUICK_REPLIES_JSON. Live quick replies require the user to be present and will block the queue.

2. Instead, embed any decision points as deferred actions in your INBOX_MESSAGE_JSON block.
   Use the `actions` array so the user can make choices later when they review the message.

3. Each action MUST have a self-contained `prompt`. Action buttons are strongly encouraged — they are excellent
   for usability and let the user act on your findings without typing. Use them whenever a natural follow-up
   choice exists. Consider `"draft"` actions when you need the user to answer questions: pre-fill the prompt
   with labeled placeholders so the user just fills in the blanks and sends.
   Each action may also include an optional `"hint"` field — a short tooltip string shown when the user hovers
   over the button.

   Do NOT include any 'done' action whose label is purely acknowledgement-only (closing the message without recording a decision).
   Only include a `"done"` action when its label is genuinely meaningful (e.g. 'Mark resolved', 'Already fixed') and the
   user needs to record a decision without launching an agent. In most cases, omit the 'done' action entirely.

4. For report-only tasks: send findings as an inbox message with `"from": "argus-weld"`.
   Subject = short descriptive title (no 'Code Health Report:' prefix, no date). Body = full Markdown report. Actions = any follow-up choices.
   Put INBOX_MESSAGE_JSON on a bare top-level line; do not wrap it in markdown code fences.

Example actions array:
  "actions": [
    { "label": "Fix this", "routeMode": "start_named_agent", "targetAgent": "arjun-sen",
      "prompt": "Arjun: during code health on [date] I found X in [file:line]. Please fix it. [full context]" },
    { "label": "Add to backlog", "routeMode": "start_coordinator",
      "prompt": "Add a task: [description discovered during code health on [date]]" }
  ]
</codehealth_inbox_reminder>

⚠️ FINAL CHECKLIST — verify before sending:
[ ] My response ends with an INBOX_MESSAGE_JSON block.
[ ] The block is on a bare top-level line (NOT inside a code fence).
[ ] "from" is set to "argus-weld".
[ ] INBOX_MESSAGE_JSON is the LAST thing in my response — no text after it.
YOUR FINAL MESSAGE MUST END WITH INBOX_MESSAGE_JSON. DO NOT END WITH ANYTHING ELSE.
```

### Key Points

1. **Safety instructions were present and explicit**:
   - "Do not create, checkout, or switch git branches"
   - "Do not run any git write commands"
   - "Generate a report only"

2. **The "branch" block SHOULD HAVE BEEN REMOVED** by the conditional preprocessor:
   - The `{{#if if_found == "branch"}}` block should have been excluded
   - Only the `{{#if if_found == "report"}}` block should have remained

3. **NO mention of creating a branch should have appeared** in the final prompt

### Why a Branch Was Created Anyway

Without access to the actual trace logs or session artifacts from that specific run, we can only speculate:

**Hypothesis 1: Bug in Conditional Processing** (Most Likely)
- A bug in `LoopMdParser.PreprocessConditionals` or `SubstituteOptions` caused the "branch" block to be included when it shouldn't have been
- This could have been due to:
  - Wrong value passed for `if_found` (maybe it was "branch" instead of "report"?)
  - A bug in the conditional evaluation logic for that specific case
  - The `MergeSafetyOverrideIntoOptions` failing to properly set `if_found="report"`

**Hypothesis 2: Model Ignored Safety Instructions** (Less Likely)
- Despite explicit prohibitions, the AI model created a branch anyway
- This seems unlikely given how strong and repeated the instructions were

**Hypothesis 3: Handlebars Re-evaluated Conditionals** (Unlikely)
- The Handlebars.NET rendering at line 345-352 somehow re-introduced the branch block
- But Handlebars doesn't have `if_found` in its context, so it couldn't evaluate that conditional

### Smoking Gun: No Trace Logs Found

The absence of trace logs from that specific run prevents us from seeing:
- What the actual rendered template looked like before being sent to the AI
- Whether `MergeSafetyOverrideIntoOptions` logged the safety merge
- Whether any exceptions occurred during template processing

### Recommendation

Add trace logging to capture:
1. The template AFTER `SubstituteOptions` but BEFORE Handlebars rendering
2. The final prompt sent to the AI model
3. All option values used in the substitution (especially `if_found`)
4. Any exceptions during template processing (even if caught and handled)
