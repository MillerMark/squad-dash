# Semantic Duplication Analysis: Safety Level vs. Options Fields
## Code-Health Task Configuration Review

**Report Date:** 2025-05-11
**Repository:** MillerMark/squad-dash
**Analysis Scope:** All 18 tasks in `.squad/code-health.md`

---

## Executive Summary

This analysis identified **significant semantic duplication** between the global `safety:` level control and per-task `options:` fields in the code-health.md configuration. 

**Key Finding:** 10 out of 18 tasks (55.6%) have **`if_found` or `if_failing` options that directly overlap with execution modes controlled by the `safety:` level declaration.**

This creates a **confusing and contradictory UI pattern** where users can set conflicting choices:
- Set `safety: report-only` but choose `if_found: fix` in options
- Set `safety: branch` but choose `if_found: report` in options
- These conflicts are never resolved; behavior is undefined when settings contradict

### Recommended Action
Remove execution-mode options from tasks where `has_safety_options: true`. The `safety:` level should be the sole authority for execution mode. Task-specific options should control *parameters* (file patterns, thresholds, modes), not execution paths.

---

## Tasks Analyzed (18 total)

### DUPLICATIVE TASKS (10 tasks with execution-mode options)

These tasks offer options that **functionally duplicate or overlap with safety level choices**.

---

#### 1. **architectural-practices** ❌ DUPLICATIVE
- **ID:** `architectural-practices`
- **Current Safety:** `report-only`
- **has_safety_options:** `true`
- **Duplicative Option:** `if_found` (radio)
  - **Values:** `report`, `branch`
  - **Conflict:** Safety level is `report-only`, but option allows `if_found: branch` to make changes
  - **User Confusion:** "Can I implement fixes or not?" — safety says no, option says maybe
- **Instructions Use:** `{{#if if_found == "branch"}}` and `{{#if if_found == "report"}}`
- **Severity:** 🔴 **HIGH** — contradicts safety floor
- **Recommended Fix:** 
  - Remove `if_found` option entirely
  - Simplify instructions to always report (or conditionally implement based on global safety overrides)
  - Option should control parameters like `scope: architecture|database|api` instead

---

#### 2. **code-smells** ❌ DUPLICATIVE
- **ID:** `code-smells`
- **Current Safety:** `report-only`
- **has_safety_options:** `true`
- **Duplicative Option:** `if_found` (radio)
  - **Values:** `fix`, `branch`, `report`
  - **Conflict:** Safety is `report-only`, but option offers `fix` and `branch` modes
  - **User Confusion:** Safety level says "report only" but option offers to fix inline or on branch
- **Instructions Use:** `{{#if if_found == "fix"}}`, `{{#if if_found == "branch"}}`, `{{#if if_found == "report"}}`
- **Severity:** 🔴 **HIGH** — directly contradicts safety level
- **Recommended Fix:**
  - Remove `if_found` option
  - Keep only `report` branch behavior
  - Add genuine task parameters like `smell_categories: [readability, performance, dead-code]` or `minimum_complexity_threshold: 10`

---

#### 3. **docs-review** ❌ DUPLICATIVE
- **ID:** `docs-review`
- **Current Safety:** `report-only`
- **has_safety_options:** `true`
- **Duplicative Option:** `if_found` (radio)
  - **Values:** `report`, `fix`
  - **Conflict:** Safety is `report-only`, but option allows auto-fixing
  - **User Confusion:** "Report only or fix?" — safety declares report-only, option contradicts
- **Instructions Use:** `{{#if if_found == "report"}}` and `{{#if if_found == "fix"}}`
- **Severity:** 🔴 **HIGH** — direct contradiction
- **Recommended Fix:**
  - Remove `if_found` option
  - Align instructions to always report (given safety level)
  - Add parameters like `check_external_links: true/false`, `check_images: true/false`

---

#### 4. **eliminate-duplication** ❌ DUPLICATIVE
- **ID:** `eliminate-duplication`
- **Current Safety:** `report-only`
- **has_safety_options:** `true`
- **Duplicative Option:** `if_found` (radio)
  - **Values:** `fix`, `branch`, `report`
  - **Conflict:** Safety is `report-only`, but option offers implementation paths
  - **User Confusion:** Safety forbids changes, option allows them
- **Instructions Use:** `{{#if if_found == "fix"}}`, `{{#if if_found == "branch"}}`, `{{#if if_found == "report"}}`
- **Severity:** 🔴 **HIGH** — full duplication
- **Recommended Fix:**
  - Remove `if_found` option
  - Always report (per safety level)
  - Add parameters like `duplication_threshold: medium/high`, `scope: classes/functions/logic`

---

#### 5. **error-handling-audit** ❌ DUPLICATIVE
- **ID:** `error-handling-audit`
- **Current Safety:** `report-only`
- **has_safety_options:** `true`
- **Duplicative Option:** `if_found` (radio)
  - **Values:** `report`, `fix`
  - **Conflict:** Safety is `report-only`, but option allows auto-fixing
  - **User Confusion:** "Report only or patch?" — safety declares read-only, option contradicts
- **Instructions Use:** `{{#if if_found == "report"}}` and `{{#if if_found == "fix"}}`
- **Severity:** 🔴 **HIGH** — direct contradiction
- **Recommended Fix:**
  - Remove `if_found` option
  - Always report (per safety level)
  - Add parameters like `minimum_severity: Medium/High/Critical`, `auto_fix_unsafe_patterns: true/false`

---

#### 6. **magic-numbers** ❌ DUPLICATIVE
- **ID:** `magic-numbers`
- **Current Safety:** `report-only`
- **has_safety_options:** `true`
- **Duplicative Option:** `if_found` (radio)
  - **Values:** `extract`, `report`
  - **Conflict:** Safety is `report-only`, but option allows extracting to constants
  - **User Confusion:** "Report or extract?" — safety forbids changes, option allows them
- **Instructions Use:** `{{#if if_found == "extract"}}` and `{{#if if_found == "report"}}`
- **Severity:** 🔴 **HIGH** — direct contradiction
- **Recommended Fix:**
  - Remove `if_found` option
  - Always report (per safety level)
  - Add parameters like `check_strings: true/false`, `minimum_magic_value_count: 2`

---

#### 7. **naming-conventions** ❌ DUPLICATIVE
- **ID:** `naming-conventions`
- **Current Safety:** `report-only`
- **has_safety_options:** `true`
- **Duplicative Option:** `if_found` (radio)
  - **Values:** `fix`, `report`
  - **Conflict:** Safety is `report-only`, but option allows renaming
  - **User Confusion:** "Report or fix?" — safety forbids changes, option allows them
- **Instructions Use:** `{{#if if_found == "fix"}}` and `{{#if if_found == "report"}}`
- **Severity:** 🔴 **HIGH** — direct contradiction
- **Recommended Fix:**
  - Remove `if_found` option
  - Always report (per safety level)
  - Add parameters like `scope: variables/methods/classes`, `exclude_tests: true/false`

---

#### 8. **speed-improvements** ❌ DUPLICATIVE
- **ID:** `speed-improvements`
- **Current Safety:** `report-only`
- **has_safety_options:** `true`
- **Duplicative Option:** `if_found` (radio)
  - **Values:** `fix`, `branch`, `report`
  - **Conflict:** Safety is `report-only`, but option offers implementation paths
  - **User Confusion:** Safety forbids changes, option allows inline, branch, or report
- **Instructions Use:** `{{#if if_found == "fix"}}`, `{{#if if_found == "branch"}}`, `{{#if if_found == "report"}}`
- **Severity:** 🔴 **HIGH** — full duplication
- **Recommended Fix:**
  - Remove `if_found` option
  - Always report (per safety level)
  - Add parameters like `minimum_optimization_impact: 5%`, `focus_areas: [algorithms, allocations, caching]`

---

#### 9. **run-tests** ❌ DUPLICATIVE
- **ID:** `run-tests`
- **Current Safety:** `report-only`
- **has_safety_options:** `true`
- **Duplicative Option:** `if_failing` (radio, not `if_found`)
  - **Values:** `fix`, `report`
  - **Conflict:** Safety is `report-only`, but option allows fixing failures
  - **User Confusion:** "Report or fix?" — safety forbids changes, option allows them
  - **Note:** Uses `if_failing` instead of `if_found`, but same semantic duplication
- **Instructions Use:** `{{#if if_failing == "fix"}}` and `{{#if if_failing == "report"}}`
- **Severity:** 🔴 **HIGH** — direct contradiction
- **Recommended Fix:**
  - Remove `if_failing` option
  - Always report (per safety level)
  - Add parameters like `test_suites: ["unit", "integration"]`, `max_parallel_tests: 4`

---

#### 10. **security-audit** ❌ DUPLICATIVE
- **ID:** `security-audit`
- **Current Safety:** `report-only`
- **has_safety_options:** `true`
- **Duplicative Option:** `if_found` (radio)
  - **Values:** `report`, `fix`
  - **Conflict:** Safety is `report-only`, but option allows auto-patching
  - **User Confusion:** "Report or patch?" — safety forbids changes, option allows them
- **Instructions Use:** `{{#if if_found == "report"}}` and `{{#if if_found == "fix"}}`
- **Severity:** 🔴 **HIGH** — direct contradiction (security-sensitive!)
- **Recommended Fix:**
  - Remove `if_found` option
  - Always report (per safety level)
  - Add parameters like `vulnerability_types: [injection, secrets, crypto, xss]`, `max_severity_to_ignore: Medium`

---

### CLEAN TASKS (8 tasks with genuine parameters only)

These tasks have **no execution-mode options**. Their options control task parameters, not execution paths.

---

#### 1. **commit-review** ✅ CLEAN
- **ID:** `commit-review`
- **Current Safety:** `report-only`
- **has_safety_options:** Not set (defaults to `false`)
- **Options:** None
- **Why Clean:** Always reports; no parameterization needed
- **Recommendation:** Keep as-is; consider adding `commit_scope: since_last_run|last_N_commits` parameter if more control is needed

---

#### 2. **prune-tasks** ✅ CLEAN
- **ID:** `prune-tasks`
- **Current Safety:** `direct`
- **has_safety_options:** `false`
- **Options:** None
- **Why Clean:** Direct execution, no options needed
- **Recommendation:** Keep as-is; this is a mechanical task (remove completed items)

---

#### 3. **readme-currency** ✅ CLEAN
- **ID:** `readme-currency`
- **Current Safety:** `report-only`
- **has_safety_options:** `false`
- **Options:** None
- **Why Clean:** Always reports; no parameterization
- **Recommendation:** Consider adding `include_changelog: true/false` or `check_build_instructions: true/false` as genuine parameters

---

#### 4. **todo-fixme-scan** ✅ CLEAN
- **ID:** `todo-fixme-scan`
- **Current Safety:** `direct`
- **has_safety_options:** `false`
- **Options:** None
- **Why Clean:** Direct execution (append to tasks.md); no parameterization
- **Recommendation:** Consider adding `comment_types: [TODO, FIXME, HACK, XXX]` to allow filtering

---

#### 5. **unused-dependencies** ✅ CLEAN
- **ID:** `unused-dependencies`
- **Current Safety:** `report-only`
- **has_safety_options:** `false`
- **Options:** None
- **Why Clean:** Always reports; no parameterization
- **Recommendation:** Consider adding `dependency_manifests: [csproj, package.json, requirements.txt]` to allow filtering

---

#### 6. **xml-doc-coverage** ✅ CLEAN
- **ID:** `xml-doc-coverage`
- **Current Safety:** `report-only`
- **has_safety_options:** `false`
- **Options:** None
- **Why Clean:** Always reports; no parameterization
- **Recommendation:** Consider adding `scope: [public, internal]`, `include_properties: true/false`

---

#### 7. **thematic-compliance** ✅ CLEAN
- **ID:** `thematic-compliance`
- **Current Safety:** `report-only`
- **has_safety_options:** `false`
- **Options:** None
- **Why Clean:** Always reports; no parameterization
- **Recommendation:** Consider adding `severity_filter: High/Medium/Low`

---

#### 8. **daily-issue-pr-tracker** ✅ CLEAN (WITH GENUINE PARAMETER)
- **ID:** `daily-issue-pr-tracker`
- **Current Safety:** `report-only`
- **has_safety_options:** `false`
- **Options:** ✅ Genuine parameter: `includePullRequests` (checkbox)
  - **Type:** `checkbox`
  - **Purpose:** Controls whether to include PRs in the report (not execution mode)
  - **Why This Is Correct:** It's a *report parameter*, not an execution mode choice
- **Why Clean:** Option controls report content, not how the task executes
- **Recommendation:** Keep as-is; this is the correct pattern for task options

---

## Deep Analysis: Pattern Recognition

### Pattern 1: The "if_found" Trap
**9 out of 10 duplicative tasks** use an `if_found` option with values like:
- `report` / `fix` / `branch`
- `report` / `extract` / `branch`
- `report` / `fix`

This directly mirrors the safety level:
- `safety: report-only` = instruction: only report
- `safety: branch` = instruction: use a branch
- `safety: direct` = instruction: change the current branch

**When the option contradicts the safety level, the instructions contain conflicting {{#if}} blocks. Execution order is undefined.**

### Pattern 2: The "has_safety_options" Field
The `has_safety_options: true` field appears to mark tasks where options **should** duplicate the safety level logic. This is a design error:
- These 10 tasks set `has_safety_options: true`
- All 10 have execution-mode options
- The field suggests: "this task has options that relate to safety"
- **Actual outcome:** Conflicting UI signals to users

**Recommendation:** Repurpose or deprecate `has_safety_options`:
- If `true`: **Hide** the Options field in the UI (safety level is the authority)
- If `false`: **Show** the Options field for genuine parameters

---

### Pattern 3: Clean Task Comparison
The 8 clean tasks show the **correct pattern**:
1. **No execution-mode options** — safety level is sole authority
2. **Options are genuinely parameterization** (e.g., `includePullRequests`)
3. **Instructions assume the safety level** — they reference options only for parameters

---

## Conflicting Scenarios (Examples of User Confusion)

### Scenario 1: Security Audit
**User Configuration:**
```yaml
safety: report-only
options:
  if_found: fix
```
**What happens?**
- Safety level says: "Never change files"
- Option says: "Fix vulnerabilities"
- Conflict is unresolved; behavior undefined
- **User expectation mismatch** — they think they chose "fix" but security policy forbids it

---

### Scenario 2: Architecture Review
**User Configuration:**
```yaml
safety: branch
options:
  if_found: report
```
**What happens?**
- Safety level says: "Create a branch for changes"
- Option says: "Only report, don't change"
- Conflict; behavior undefined
- **User frustration** — they think they got a report, but maybe the AI tried to create a branch?

---

## Code-Level Findings

### In CodeHealthMdParser.cs (lines 80-90):
```csharp
bool   configured    = false;
bool   enabledOnIdle = false;
double idleTimeout   = 15;
int    maxTasks      = 5;
string safety        = "branch";  // Global safety floor

var tasks = new List<CodeHealthTaskBuilder>();
...
var optionKeys = new List<string>();
var optionBuilders = new Dictionary<string, CodeHealthOptionBuilder>(StringComparer.Ordinal);
```

**Finding:** The parser loads both `safety:` (global) and `options:` (per-task) but provides no conflict detection or resolution logic.

### In CodeHealthTaskEditorWindow.cs:
The editor shows a **safety combo dropdown** and a separate **options YAML editor**. There is **no visual warning** when options contradict the safety level.

---

## Recommendations

### Immediate Actions (High Priority)

#### 1. **Remove Duplicative Options (All 10 Tasks)**
```yaml
# BEFORE (duplicative)
- id: code-smells
  safety: report-only
  has_safety_options: true
  options:
    if_found:
      type: radio
      choices:
        - value: fix
        - value: branch
        - value: report

# AFTER (clean)
- id: code-smells
  safety: report-only
  has_safety_options: false
  options:
    severity_threshold:
      type: radio
      choices:
        - value: medium
        - value: high
```

#### 2. **Add Genuine Task Parameters** (Instead of Execution Mode)
Examples of valid options:
- `scope: [functions, classes, modules]`
- `minimum_threshold: 5` (numeric)
- `focus_areas: [readability, performance, dead_code]`
- `include_tests: true/false` (checkbox)
- `max_findings_to_report: 50` (numeric)

#### 3. **Simplify Instructions** (Remove Conflicting {{#if}} Blocks)
Instead of:
```handlebars
{{#if if_found == "fix"}}Implement fixes{{/if}}
{{#if if_found == "branch"}}Create a branch{{/if}}
{{#if if_found == "report"}}Report only{{/if}}
```

Use a single, safety-aware instruction:
```handlebars
Report findings: file path, structural anchor, severity, and suggested fix.
```

#### 4. **Define has_safety_options Semantics**
- **Current state:** `has_safety_options: true` means "I have options" (unclear)
- **Proposed state:**
  - `has_safety_options: false` (default): Task has genuine parameters; show Options field
  - `has_safety_options: true`: Task has NO meaningful options; hide Options field (safety level is sole authority)

Or rename to clarify:
- `hide_options_in_ui: true` (more explicit)

---

### Long-Term Actions (Design Level)

#### 1. **UI Safety Level Override**
When a user sets an option that contradicts the safety level:
- Show a warning: "Option 'if_found: fix' conflicts with safety level 'report-only'"
- Ask user to confirm (override safety level? or choose option value matching safety?)
- Never silently ignore the conflict

#### 2. **Instruction Template Variables**
Define a standard set of execution-mode variables that the AI/engine fills based on `safety:`:
```handlebars
{{#if execution_mode == "report"}}...{{/if}}
{{#if execution_mode == "fix"}}...{{/if}}
{{#if execution_mode == "branch"}}...{{/if}}
```
The system fills `execution_mode` from `safety:`, not from conflicting options.

#### 3. **Schema Validation**
Add validation to CodeHealthMdParser:
```csharp
// Pseudo-code
if (task.HasSafetyOptions && task.Options.ContainsExecutionModeChoice()) {
    throw new ConfigurationError(
        $"Task '{task.Id}' has options that duplicate the safety level. " +
        "Remove 'if_found' or rename to a genuine parameter.");
}
```

---

## Impact Assessment

### Current State (Broken)
- ❌ 10 tasks have confusing duplicate controls
- ❌ UI shows contradictory settings
- ❌ User expectations clash with safety policy
- ❌ Execution behavior is undefined when conflicts occur

### After Remediation
- ✅ Single source of truth for execution mode (safety level)
- ✅ Options control genuine task parameters only
- ✅ Clear, predictable UI
- ✅ Defined behavior in all configurations

---

## Appendix A: Task-by-Task Remediation Guide

### Task: architectural-practices
**Remove:**
```yaml
options:
  if_found:
    type: radio
    choices:
      - value: branch
      - value: report
```

**Add (optional, if parameterization is desired):**
```yaml
options:
  scope:
    type: radio
    value: all
    choices:
      - value: all
        tooltip: Review all layers
      - value: separations
        tooltip: Focus on separation of concerns
      - value: dependencies
        tooltip: Focus on circular dependencies
```

**Update Instructions:**
Remove `{{#if if_found == ...}}` blocks. Simplify to:
```
Report each finding with: problem description, affected files/layers, recommended fix.
```

---

### Task: code-smells
**Remove:** `if_found` option (all three branches)

**Add (optional):**
```yaml
options:
  check_categories:
    type: checkbox
    label: Check categories
    choices:
      - value: readability
        tooltip: Readability issues (long methods, unclear names)
      - value: complexity
        tooltip: Overly complex conditionals/logic
      - value: dead_code
        tooltip: Unused variables, unreachable code
```

---

### Task: docs-review
**Remove:** `if_found` option

**Add (optional):**
```yaml
options:
  check_types:
    type: checkbox
    choices:
      - value: accuracy
        tooltip: Check if docs match current codebase
      - value: links
        tooltip: Check broken internal/external links
      - value: images
        tooltip: Check missing image files
```

---

### Task: eliminate-duplication
**Remove:** `if_found` option

**Add (optional):**
```yaml
options:
  duplication_level:
    type: radio
    value: high
    choices:
      - value: medium
        tooltip: Find duplicated blocks (50+ lines)
      - value: high
        tooltip: Find exact duplicates and copy-paste patterns
```

---

### Task: error-handling-audit
**Remove:** `if_found` option

**Add (optional):**
```yaml
options:
  severity_floor:
    type: radio
    value: medium
    choices:
      - value: medium
        tooltip: Report Medium severity and above
      - value: high
        tooltip: Report only High and Critical
```

---

### Task: magic-numbers
**Remove:** `if_found` option

**Add (optional):**
```yaml
options:
  types:
    type: checkbox
    choices:
      - value: numeric
        tooltip: Scan for numeric literals
      - value: strings
        tooltip: Scan for hardcoded strings
```

---

### Task: naming-conventions
**Remove:** `if_found` option

**Add (optional):**
```yaml
options:
  scope:
    type: radio
    value: all
    choices:
      - value: variables
        tooltip: Check variable/field naming
      - value: methods
        tooltip: Check method naming
      - value: classes
        tooltip: Check class naming
      - value: all
        tooltip: Check all categories
```

---

### Task: speed-improvements
**Remove:** `if_found` option

**Add (optional):**
```yaml
options:
  focus_areas:
    type: checkbox
    choices:
      - value: algorithms
        tooltip: Check for algorithmic inefficiencies
      - value: allocations
        tooltip: Check for unnecessary allocations
      - value: io
        tooltip: Check for blocking I/O patterns
      - value: caching
        tooltip: Check for missing cache opportunities
```

---

### Task: run-tests
**Remove:** `if_failing` option

**Add (optional):**
```yaml
options:
  test_suites:
    type: radio
    value: all
    choices:
      - value: unit
        tooltip: Run unit tests only
      - value: integration
        tooltip: Run integration tests only
      - value: all
        tooltip: Run all test suites
```

---

### Task: security-audit
**Remove:** `if_found` option (HIGHEST PRIORITY — security-sensitive)

**Add (optional):**
```yaml
options:
  vulnerability_types:
    type: checkbox
    choices:
      - value: injection
        tooltip: Scan for injection risks (SQL, command, path)
      - value: secrets
        tooltip: Scan for hard-coded secrets
      - value: crypto
        tooltip: Scan for insecure cryptography
      - value: deserialization
        tooltip: Scan for unsafe deserialization
```

---

## Appendix B: Validation Checklist

After applying remediation:

- [ ] All 10 duplicative tasks have `if_found` / `if_failing` options removed
- [ ] All remaining options are genuinely task parameters (not execution modes)
- [ ] Instructions in all 10 tasks remove conflicting `{{#if}}` blocks
- [ ] `has_safety_options` field correctly reflects whether task has ANY options
- [ ] CodeHealthMdParser adds validation to prevent future duplication
- [ ] UI shows no warnings/errors when tasks are edited
- [ ] Documentation updated to explain "Options are for task parameters, safety level controls execution"
- [ ] All 8 clean tasks unchanged (they already follow the correct pattern)

---

## Conclusion

The analysis reveals a **fundamental design issue**: execution mode (report/branch/direct) is controlled simultaneously by two independent mechanisms:
1. The global `safety:` level
2. Per-task `if_found` / `if_failing` options

This creates confusion, contradictions, and undefined behavior. **The fix is clear and surgical**: remove execution-mode options and use only the safety level. Let task options control what to *check* and *how to report*, not *whether to make changes*.

---

**Next Steps:** User review and approval to proceed with remediation.
