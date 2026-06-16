using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using HandlebarsDotNet;

namespace SquadDash;

/// <summary>Orchestrates maintenance task execution against the configured task list.</summary>
internal sealed class CodeHealthRunner {

    private readonly Func<string, CancellationToken, Task<int>> _executePromptAsync;
    private readonly Func<string, CancellationToken, Task<(int anchorIndex, string responseText)>>? _executePromptAndCaptureAsync;
    private readonly CodeHealthStateStore                       _stateStore;
    private readonly Action<string>                              _onTaskStarted;
    private readonly Action<string, string, int, DateTimeOffset, TimeSpan> _onTaskCompleted;
    private readonly Action<CodeHealthReport>                   _onCompleted;
    private readonly Func<string, CancellationToken, Task<string?>> _getCommitShaAsync;
    private readonly Func<DateTimeOffset, bool>?                 _wasInboxSavedSince;
    private readonly Action<DecomposedTaskGroup>?                _onDecomposeGroupReady;

    private volatile bool _isRunning;

    public bool IsRunning => _isRunning;

    public CodeHealthRunner(
        Func<string, CancellationToken, Task<int>> executePromptAsync,
        CodeHealthStateStore                       stateStore,
        Action<string>                              onTaskStarted,
        Action<string, string, int, DateTimeOffset, TimeSpan> onTaskCompleted,
        Action<CodeHealthReport>                   onCompleted,
        Func<string, CancellationToken, Task<string?>>? getCommitShaAsync = null,
        Func<DateTimeOffset, bool>?                     wasInboxSavedSince = null,
        Func<string, CancellationToken, Task<(int, string)>>? executePromptAndCaptureAsync = null,
        Action<DecomposedTaskGroup>?                                  onDecomposeGroupReady = null) {

        _executePromptAsync            = executePromptAsync;
        _executePromptAndCaptureAsync  = executePromptAndCaptureAsync;
        _stateStore                    = stateStore;
        _onTaskStarted                 = onTaskStarted;
        _onTaskCompleted               = onTaskCompleted;
        _onCompleted                   = onCompleted;
        _getCommitShaAsync             = getCommitShaAsync ?? TryGetCommitShaAsync;
        _wasInboxSavedSince            = wasInboxSavedSince;
        _onDecomposeGroupReady         = onDecomposeGroupReady;
    }

    /// <summary>
    /// Runs eligible maintenance tasks in order. Awaitable — completes when all tasks have run.
    /// </summary>
    public async Task StartAsync(
        CodeHealthMdConfig      config,
        string                   workspacePath,
        CancellationToken        ct,
        IReadOnlySet<string>?    forceTaskIds = null) {

        SquadInstallerService.EnsureCodeHealthStateInGitIgnore(workspacePath);
        _isRunning = true;
        var startedAt = DateTimeOffset.UtcNow;
        var ranIds     = new List<string>();
        var skippedIds = new List<string>();
        var results    = new List<CodeHealthTaskResult>();

        try {
            var tasks = config.Tasks ?? [];

            // When forceTaskIds is provided, only check those tasks for commit-SHA needs.
            var tasksForCommitCheck = forceTaskIds is { Count: > 0 }
                ? (IEnumerable<CodeHealthTask>)tasks.Where(t => forceTaskIds.Contains(t.Id))
                : tasks;

            string? commitSha = NeedsCommitSha(tasksForCommitCheck)
                ? await _getCommitShaAsync(workspacePath, ct).ConfigureAwait(false)
                : null;

            int runCount = 0;

            foreach (var task in tasks) {
                if (ct.IsCancellationRequested)
                    break;

                if (!task.Enabled)
                    continue;

                // When forceTaskIds is provided (manual single-task run), skip any task not in the set.
                if (forceTaskIds is { Count: > 0 } && !forceTaskIds.Contains(task.Id)) {
                    skippedIds.Add(task.Id);
                    continue;
                }

                var isForced = forceTaskIds?.Contains(task.Id) == true;
                if (!isForced && !await _stateStore.IsEligibleAsync(task.Id, task.Frequency, commitSha, workspacePath).ConfigureAwait(false)) {
                    skippedIds.Add(task.Id);
                    continue;
                }

                if (runCount >= config.MaxTasksPerSession)
                    break;

                _onTaskStarted(task.Title);
                runCount++;

                var taskStartedAt = DateTimeOffset.UtcNow;
                var taskStart = Stopwatch.GetTimestamp();
                string? safetyOverrideNote = null;
                try {
                    // Read safety override from state store (set by user via UI)
                    var safetyOverride = _stateStore.GetSafetyOverride(task.Id);
                    
                    // Determine effective safety: override takes precedence, then task default, then global floor
                    var baseSafety = safetyOverride ?? task.Safety;
                    var effectiveSafety = ApplySafetyFloor(config.Safety, baseSafety);
                    
                    if (!string.Equals(effectiveSafety, task.Safety, StringComparison.OrdinalIgnoreCase)) {
                        string reason = safetyOverride != null
                            ? $"overridden by user to '{safetyOverride}'"
                            : $"downgraded by global floor ('{config.Safety}')";
                        safetyOverrideNote = $"Safety '{reason}'.";
                        SquadDashTrace.Write(TraceCategory.General,
                            $"CodeHealthRunner: task '{task.Id}' effective safety '{effectiveSafety}' (declared '{task.Safety}', override '{safetyOverride}', floor '{config.Safety}').");
                    }

                    var lastReviewedSha = _stateStore.GetLastCommitSha(task.Id);
                    var newCommitCount  = NeedsCommitCount(task.Frequency) && commitSha is not null
                        ? await _stateStore.GetCommitCountSinceAsync(task.Id, workspacePath).ConfigureAwait(false)
                        : 0;
                    
                    // Generate dynamic branch name: codehealth/{taskId}/{YYYYMMDD-HHmmss}
                    var dynamicBranchName = $"codehealth/{task.Id}/{taskStartedAt:yyyyMMdd-HHmmss}";
                    
                    var prompt = BuildPrompt(task, config.Safety, effectiveSafety, dynamicBranchName, 
                        taskStartedAt, lastReviewedSha, newCommitCount);
                    int anchorIndex;
                    string? responseText = null;

                    // When a capture delegate is available, use it to also collect the response
                    // text so we can scan for a TASKS_JSON decompose block.
                    if (_executePromptAndCaptureAsync is not null) {
                        (anchorIndex, responseText) =
                            await _executePromptAndCaptureAsync(prompt, ct).ConfigureAwait(false);
                    } else {
                        anchorIndex = await _executePromptAsync(prompt, ct).ConfigureAwait(false);
                    }

                    // Fix 2: post-completion fallback — if this was a report-only task and no inbox
                    // message was saved during the turn, send a short recovery prompt to recover it.
                    if (string.Equals(effectiveSafety, "report-only", StringComparison.OrdinalIgnoreCase)
                        && _wasInboxSavedSince is not null
                        && !_wasInboxSavedSince(taskStartedAt)) {

                        SquadDashTrace.Write(TraceCategory.General,
                            $"CodeHealthRunner: report-only task '{task.Id}' produced no inbox message — sending recovery prompt.");
                        await _executePromptAsync(BuildInboxRecoveryPrompt(task.Title), ct).ConfigureAwait(false);
                    }

                    // Check response for a TASKS_JSON decompose block.
                    if (responseText is not null
                        && responseText.Contains("TASKS_JSON:", StringComparison.Ordinal)
                        && TasksJsonParser.TryParse(responseText, out var decomposeGroup)
                        && decomposeGroup is not null
                        && _onDecomposeGroupReady is not null) {

                        SquadDashTrace.Write(TraceCategory.General,
                            $"CodeHealthRunner: TASKS_JSON found for group '{decomposeGroup.GroupId}' — notifying caller.");
                        _onDecomposeGroupReady(decomposeGroup);
                    }

                    var elapsed = Stopwatch.GetElapsedTime(taskStart);
                    _stateStore.RecordRun(task.Id, commitSha);
                    ranIds.Add(task.Id);
                    results.Add(new CodeHealthTaskResult(
                        Id:                 task.Id,
                        Title:              task.Title,
                        Outcome:            CodeHealthTaskOutcome.Completed,
                        Duration:           elapsed,
                        SafetyOverrideNote: safetyOverrideNote));
                    _onTaskCompleted(task.Id, task.Title, anchorIndex, taskStartedAt, elapsed);
                }
                catch (OperationCanceledException) {
                    break;
                }
                catch (Exception ex) {
                    var elapsed = Stopwatch.GetElapsedTime(taskStart);
                    ranIds.Add(task.Id);
                    results.Add(new CodeHealthTaskResult(
                        Id:                 task.Id,
                        Title:              task.Title,
                        Outcome:            CodeHealthTaskOutcome.Error,
                        Duration:           elapsed,
                        ErrorMessage:       ex.Message,
                        SafetyOverrideNote: safetyOverrideNote));
                    _onTaskCompleted(task.Id, task.Title, -1, taskStartedAt, elapsed);
                }
            }

            var report = new CodeHealthReport {
                RanTaskIds     = ranIds,
                SkippedTaskIds = skippedIds,
                TaskResults    = results,
                StartedAt      = startedAt,
                CompletedAt    = DateTimeOffset.UtcNow,
            };

            _onCompleted(report);
        }
        finally {
            _isRunning = false;
        }
    }

    // ── Constants ──────────────────────────────────────────────────────────────

    /// <summary>
    /// Prepended to report-only prompts so the INBOX_MESSAGE_JSON requirement is the first thing
    /// the model reads, before any task instructions that might bury it.
    /// </summary>
    private const string ReportOnlyInboxPreamble =
        "⚠️ INBOX REQUIREMENT — READ THIS FIRST ⚠️\n" +
        "Your response MUST end with an INBOX_MESSAGE_JSON block. This is mandatory.\n" +
        "Rules:\n" +
        "- The block must be the LAST thing in your response — nothing after it.\n" +
        "- Place INBOX_MESSAGE_JSON on a bare top-level line (do NOT wrap it in a code fence).\n" +
        "- Set \"from\" to \"argus-weld\".\n\n" +
        "Required format (fill in the fields):\n" +
        "INBOX_MESSAGE_JSON:\n" +
        "{\n" +
        "  \"subject\": \"<Short descriptive title — no 'Code Health Report:' prefix, no date>\",\n" +
        "  \"from\": \"argus-weld\",\n" +
        "  \"body\": \"## <Task Title>\\n\\n<Your full findings in Markdown>\",\n" +
        "  \"attachments\": [],\n" +
        "  \"actions\": [\n" +
        "    { \"label\": \"Fix this\", \"routeMode\": \"start_named_agent\", \"targetAgent\": \"...\", \"prompt\": \"...\" },\n" +
        "    { \"label\": \"Add to backlog\", \"routeMode\": \"start_coordinator\", \"prompt\": \"...\" }\n" +
        "  ]\n" +
        "}\n" +
        "Each action may include an optional \"hint\" field — a short tooltip string shown when the user hovers over the button.\n" +
        "Do NOT include any 'done' action whose label is purely acknowledgement-only — it records nothing and adds no value.\n\n";

    /// <summary>
    /// Appended to report-only prompts as a final reminder checklist so the model cannot
    /// miss the INBOX_MESSAGE_JSON requirement even after reading long task instructions.
    /// </summary>
    private const string ReportOnlyMandatoryChecklist =
        "\n\n" +
        "⚠️ FINAL CHECKLIST — verify before sending:\n" +
        "[ ] My response ends with an INBOX_MESSAGE_JSON block.\n" +
        "[ ] The block is on a bare top-level line (NOT inside a code fence).\n" +
        "[ ] \"from\" is set to \"argus-weld\".\n" +
        "[ ] INBOX_MESSAGE_JSON is the LAST thing in my response — no text after it.\n" +
        "YOUR FINAL MESSAGE MUST END WITH INBOX_MESSAGE_JSON. DO NOT END WITH ANYTHING ELSE.";

    private const string MaintenanceInboxReminder =
        "<codehealth_inbox_reminder>\n" +
        "You are running in code health mode — the user is not present. Follow these rules:\n" +
        "\n" +
        "1. Do NOT emit QUICK_REPLIES_JSON. Live quick replies require the user to be present and will block the queue.\n" +
        "\n" +
        "2. Instead, embed any decision points as deferred actions in your INBOX_MESSAGE_JSON block.\n" +
        "   Use the `actions` array so the user can make choices later when they review the message.\n" +
        "\n" +
        "3. Each action MUST have a self-contained `prompt`. Action buttons are strongly encouraged — they are excellent\n" +
        "   for usability and let the user act on your findings without typing. Use them whenever a natural follow-up\n" +
        "   choice exists. Consider `\"draft\"` actions when you need the user to answer questions: pre-fill the prompt\n" +
        "   with labeled placeholders so the user just fills in the blanks and sends.\n" +
        "   Each action may also include an optional `\"hint\"` field — a short tooltip string shown when the user hovers\n" +
        "   over the button.\n" +
        "\n" +
        "   Do NOT include any 'done' action whose label is purely acknowledgement-only (closing the message without recording a decision).\n" +
        "   Only include a `\"done\"` action when its label is genuinely meaningful (e.g. 'Mark resolved', 'Already fixed') and the\n" +
        "   user needs to record a decision without launching an agent. In most cases, omit the 'done' action entirely.\n"+
        "\n" +
        "4. For report-only tasks: send findings as an inbox message with `\"from\": \"argus-weld\"`.\n" +
        "   Subject = short descriptive title (no 'Code Health Report:' prefix, no date). Body = full Markdown report. Actions = any follow-up choices.\n" +
        "   Put INBOX_MESSAGE_JSON on a bare top-level line; do not wrap it in markdown code fences.\n" +
        "\n" +
        "Example actions array:\n" +
        "  \"actions\": [\n" +
        "    { \"label\": \"Fix this\", \"routeMode\": \"start_named_agent\", \"targetAgent\": \"arjun-sen\",\n" +
        "      \"prompt\": \"Arjun: during code health on [date] I found X in [file:line]. Please fix it. [full context]\" },\n" +
        "    { \"label\": \"Add to backlog\", \"routeMode\": \"start_coordinator\",\n" +
        "      \"prompt\": \"Add a task: [description discovered during code health on [date]]\" }\n" +
        "  ]\n"+
        "</codehealth_inbox_reminder>";

    // ── Helpers ────────────────────────────────────────────────────────────────

    /// <summary>
    /// Strips maintenance boilerplate (inbox preamble, inbox reminder, final checklist) from a
    /// prompt so that only the user-relevant task instructions remain for transcript display.
    /// </summary>
    internal static string StripPreambleForDisplay(string prompt) {
        if (prompt.StartsWith(ReportOnlyInboxPreamble, StringComparison.Ordinal))
            prompt = prompt[ReportOnlyInboxPreamble.Length..];

        // Strip the maintenance inbox reminder (and everything after it, which includes the
        // optional mandatory checklist suffix) from the display text.
        const string reminderTag = "\n\n<codehealth_inbox_reminder>";
        var reminderIdx = prompt.IndexOf(reminderTag, StringComparison.Ordinal);
        if (reminderIdx >= 0)
            prompt = prompt[..reminderIdx];

        return prompt.Trim();
    }

    private static string BuildPrompt(CodeHealthTask task, string globalSafety, string effectiveSafety, 
        string dynamicBranchName, DateTimeOffset runDate, string? lastReviewedSha = null, int newCommitCount = 0) {

        string safetyPrefix;
        string suffix;

        if (string.Equals(effectiveSafety, "report-only", StringComparison.OrdinalIgnoreCase)) {
            // Fix 1: for report-only tasks the INBOX requirement goes at the very top and
            // a mandatory checklist is appended at the end so it cannot be lost in the middle.
            safetyPrefix = ReportOnlyInboxPreamble + "Do not modify any source files. Do not create, checkout, or switch git branches. Do not run any git write commands. Generate a report only.\n\n";
            suffix       = ReportOnlyMandatoryChecklist;
        }
        else {
            safetyPrefix = effectiveSafety switch {
                "branch" => $"Create branch `{dynamicBranchName}` before making any code changes. Commit to that branch only.\n\n",
                "direct" => "You may commit directly to the current branch.\n\n",
                _        => string.Empty,
            };
            suffix = string.Empty;
        }

        var inboxReminder = "\n\n" + MaintenanceInboxReminder;
        
        // Merge safety override into task options before template substitution.
        // When effectiveSafety differs from task.Safety, we need to sync compatible options
        // (e.g., if_found, execution_mode) so the template conditionals respect the override.
        var adjustedOptions = MergeSafetyOverrideIntoOptions(task.Options, task.Safety, effectiveSafety);
        
        // First apply option substitution and legacy substitutions
        var instructions  = SubstituteOptions(task.Instructions, adjustedOptions);
        
        // Apply legacy {{branch}} substitution for backward compatibility
        instructions = instructions.Replace("{{branch}}", dynamicBranchName, StringComparison.OrdinalIgnoreCase);

        // Apply commit-range substitutions for commit-frequency tasks
        instructions = instructions.Replace("{{last_reviewed_sha}}", lastReviewedSha ?? string.Empty, StringComparison.OrdinalIgnoreCase);
        instructions = instructions.Replace("{{new_commit_count}}", newCommitCount.ToString(), StringComparison.OrdinalIgnoreCase);
        
        // Render instructions with Handlebars using dynamic variables
        try {
            var handlebarsEngine = HandlebarsDotNet.Handlebars.Create();
            var compiledTemplate = handlebarsEngine.Compile(instructions);
            var context = new {
                branchName = dynamicBranchName,
                safety = effectiveSafety.ToLowerInvariant()
            };
            instructions = compiledTemplate(context);
        }
        catch (Exception ex) {
            // If Handlebars rendering fails, log and continue with unrendered instructions
            SquadDashTrace.Write(TraceCategory.General,
                $"CodeHealthRunner: Handlebars rendering failed for task '{task.Id}': {ex.Message}. Using unrendered instructions.");
        }

        return safetyPrefix + instructions + inboxReminder + suffix;
    }

    private static string BuildInboxRecoveryPrompt(string taskTitle) =>
        $"Your previous response contained a maintenance report for \"{taskTitle}\" but no " +
        "INBOX_MESSAGE_JSON block was detected. Please resend your findings now using ONLY an " +
        "INBOX_MESSAGE_JSON block — no other text before or after it. " +
        "The block must appear on a bare top-level line, not inside a code fence.\n\n" +
        "INBOX_MESSAGE_JSON:\n" +
        "{\n" +
        "  \"subject\": \"<Short descriptive title — no 'Code Health Report:' prefix, no date>\",\n" +
        "  \"from\": \"argus-weld\",\n" +
        "  \"body\": \"<your full findings in Markdown>\",\n" +
        "  \"attachments\": [],\n" +
        "  \"actions\": [\n" +
        "    { \"label\": \"Fix this\", \"routeMode\": \"start_named_agent\", \"targetAgent\": \"...\", \"prompt\": \"...\" },\n" +
        "    { \"label\": \"Add to backlog\", \"routeMode\": \"start_coordinator\", \"prompt\": \"...\" }\n" +
        "  ]\n" +
        "}\n" +
        "Each action may include an optional \"hint\" field — a short tooltip shown when the user hovers over the button.\n" +
        "Do NOT include any 'done' action whose label is purely acknowledgement-only — it records nothing and adds no value.";

    /// <summary>
    /// When effectiveSafety differs from the task's declared safety level due to a user override,
    /// adjusts compatible task options to reflect the override so template conditionals work correctly.
    /// For example, if effectiveSafety is "report-only" but the task offers "if_found" with choices
    /// [branch, report], the option is set to "report" to ensure the template renders correctly.
    /// </summary>
    private static IReadOnlyList<CodeHealthOption>? MergeSafetyOverrideIntoOptions(
        IReadOnlyList<CodeHealthOption>? options, string taskSafety, string effectiveSafety) {
        
        // If safety hasn't been overridden, return options unchanged
        if (string.Equals(taskSafety, effectiveSafety, StringComparison.OrdinalIgnoreCase))
            return options;
        
        if (options is null or { Count: 0 })
            return options;
        
        // Create adjusted options list with safety-related options updated
        var adjusted = new List<CodeHealthOption>();
        foreach (var opt in options) {
            // Check if this option controls execution mode/branch creation
            // These options should be synchronized with the safety override
            var isExecutionOption = string.Equals(opt.Key, "if_found", StringComparison.OrdinalIgnoreCase) ||
                                    string.Equals(opt.Key, "execution_mode", StringComparison.OrdinalIgnoreCase);
            
            if (!isExecutionOption) {
                adjusted.Add(opt);
                continue;
            }
            
            // Map effectiveSafety to the best matching option choice
            string targetValue = effectiveSafety switch {
                "report-only" => FindMatchingChoice(opt, ["report", "no-change", "report-only"]) ?? opt.RawValue,
                "branch"      => FindMatchingChoice(opt, ["branch", "create-branch"]) ?? opt.RawValue,
                "direct"      => FindMatchingChoice(opt, ["fix", "direct", "inline"]) ?? opt.RawValue,
                _             => opt.RawValue
            };
            
            // Create a new option with the adjusted value
            var adjustedOpt = new CodeHealthOption(
                Key:      opt.Key,
                RawValue: targetValue ?? opt.RawValue,
                Type:     opt.Type,
                Label:    opt.Label,
                Tooltip:  opt.Tooltip,
                Choices:  opt.Choices);
            adjusted.Add(adjustedOpt);
            
            SquadDashTrace.Write(TraceCategory.General,
                $"CodeHealthRunner: Merged safety override into option '{opt.Key}': '{opt.RawValue}' → '{targetValue}'");
        }
        
        return adjusted;
    }
    
    /// <summary>Finds a choice in the option that matches one of the target values.</summary>
    private static string? FindMatchingChoice(CodeHealthOption option, string[] targets) {
        if (option.Choices is null or { Count: 0 })
            return null;
        
        foreach (var target in targets) {
            var match = option.Choices.FirstOrDefault(c => 
                string.Equals(c.Value, target, StringComparison.OrdinalIgnoreCase));
            if (match is not null)
                return match.Value;
        }
        return null;
    }

    /// <summary>
    /// Evaluates <c>{{#if}}</c>/<c>{{#unless}}</c> conditional blocks and replaces
    /// <c>{{key}}</c> placeholders in <paramref name="instructions"/> with the current
    /// option values parsed from code-health.md. Unrecognised placeholders are left as-is.
    /// </summary>
    internal static string SubstituteOptions(string instructions, IReadOnlyList<CodeHealthOption>? options) {
        var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (options is not null)
            foreach (var opt in options)
                values[opt.Key] = opt.RawValue ?? string.Empty;

        var result = LoopMdParser.PreprocessConditionals(instructions, (IReadOnlyDictionary<string, string>)values);

        foreach (var kvp in values)
            result = result.Replace($"{{{{{kvp.Key}}}}}", kvp.Value, StringComparison.OrdinalIgnoreCase);

        return result;
    }

    private static string ApplySafetyFloor(string globalSafety, string taskSafety) {
        static int Rank(string s) => s switch {
            "report-only" => 2,
            "branch"      => 1,
            "direct"      => 0,
            _             => 0,
        };
        return Rank(globalSafety) >= Rank(taskSafety) ? globalSafety : taskSafety;
    }

    private static bool NeedsCommitSha(IEnumerable<CodeHealthTask> tasks) =>
        tasks.Any(task =>
            task.Enabled && (
                string.Equals(task.Frequency, "after-commits", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(task.Frequency, "per-commit",    StringComparison.OrdinalIgnoreCase) ||
                (task.Frequency.StartsWith("every-", StringComparison.OrdinalIgnoreCase) &&
                 task.Frequency.EndsWith("-commits", StringComparison.OrdinalIgnoreCase))
            ));

    private static bool NeedsCommitCount(string frequency) =>
        string.Equals(frequency, "after-commits", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(frequency, "per-commit",    StringComparison.OrdinalIgnoreCase) ||
        (frequency.StartsWith("every-", StringComparison.OrdinalIgnoreCase) &&
         frequency.EndsWith("-commits", StringComparison.OrdinalIgnoreCase));

    private static async Task<string?> TryGetCommitShaAsync(string workspacePath, CancellationToken ct) {
        try {
            var psi = new System.Diagnostics.ProcessStartInfo("git", "rev-parse HEAD") {
                WorkingDirectory       = workspacePath,
                RedirectStandardOutput = true,
                RedirectStandardError  = true,
                UseShellExecute        = false,
                CreateNoWindow         = true,
            };
            using var proc = Process.Start(psi);
            if (proc is null) return null;

            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeoutCts.CancelAfter(TimeSpan.FromSeconds(3));

            try {
                var outputTask = proc.StandardOutput.ReadToEndAsync(timeoutCts.Token);
                var errorTask  = proc.StandardError.ReadToEndAsync(timeoutCts.Token);
                await proc.WaitForExitAsync(timeoutCts.Token).ConfigureAwait(false);

                var sha = (await outputTask.ConfigureAwait(false)).Trim();
                _ = await errorTask.ConfigureAwait(false);
                if (proc.ExitCode != 0)
                    return null;

                return string.IsNullOrEmpty(sha) ? null : sha;
            }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested) {
                TryKillProcess(proc);
                return null;
            }
        }
        catch (OperationCanceledException) {
            return null;
        }
        catch (Exception ex) {
            SquadDashTrace.Write("Code Health", $"GetCurrentSha failed: {ex.Message}");
            return null;
        }
    }

    private static void TryKillProcess(Process proc) {
        try {
            if (!proc.HasExited)
                proc.Kill(entireProcessTree: true);
        }
        catch {
            // Best-effort cleanup only; commit SHA lookup must never fail maintenance.
        }
    }
}


