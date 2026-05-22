/**
 * Inbox prompt injection constants for Phase 3 (Inbox — Prompt Injection).
 *
 * These strings are the authoritative source for the INBOX_MESSAGE_JSON sentinel
 * instructions that are injected into every user prompt before it is sent to the AI.
 *
 * ─── C# wiring (Arjun, Phase 3) ──────────────────────────────────────────────────
 *
 * 1. REGULAR PROMPTS — PromptExecutionController.cs, BuildBridgePrompt():
 *
 *    Add `InboxMessageInstruction` to the `parts` array alongside the other
 *    supplemental context blocks.  It must appear BEFORE `TurnSummaryInstruction`
 *    and BEFORE `hostCmdCtx` so that the AI emits INBOX_MESSAGE_JSON before
 *    HOST_COMMAND_JSON (the host-command parser requires HOST_COMMAND_JSON at the
 *    very end of the response).  A constant like `TurnSummaryInstruction` works
 *    well — declare it as `private const string InboxMessageInstruction = …` or
 *    read `InboxPromptInjection.InboxMessageInstruction` if you bridge the text.
 *
 *    Targeted line: the `parts` array initialisation, currently:
 *      var parts = new[] { pending, docsCtx, tasksCtx, queueCtx, triggeredCtx,
 *                          TurnSummaryInstruction, hostCmdCtx }
 *    After the change it becomes (inboxCtx added before TurnSummaryInstruction):
 *      var parts = new[] { pending, docsCtx, tasksCtx, queueCtx, triggeredCtx,
 *                          inboxCtx, TurnSummaryInstruction, hostCmdCtx }
 *
 * 2. MAINTENANCE TASK PROMPTS — MaintenanceRunner.cs, BuildPrompt():
 *
 *    For `report-only` tasks append `MaintenanceInboxReminder` to the prompt so
 *    that Argus Weld knows to deliver its findings via an inbox message.  The
 *    reminder is appended as a trailing block after `task.Instructions`:
 *
 *    Before:   return safetyPrefix + task.Instructions;
 *    After:    var inboxReminder = effectiveSafety == "report-only"
 *                  ? "\n\n" + MaintenanceInboxReminder
 *                  : string.Empty;
 *              return safetyPrefix + task.Instructions + inboxReminder;
 *
 * ─────────────────────────────────────────────────────────────────────────────────
 */

/**
 * Injected into every user prompt (in the supplemental/system context section).
 * Teaches the AI how and when to append an INBOX_MESSAGE_JSON block so that the
 * Inbox panel receives structured messages without cluttering the transcript.
 */
export const InboxMessageInstruction =
    "<inbox_instructions>\n" +
    "You may send the user a message to their Inbox panel by appending an INBOX_MESSAGE_JSON block at the very end of your response, after all other content. Use this when:\n" +
    "- Your response is a detailed report, analysis, or long-form answer that the user might want to refer back to\n" +
    "- You are completing a maintenance task with a report-only safety level\n" +
    "- The user asked a question during a queued run and may have missed the answer in the transcript\n" +
    "\n" +
    "Only send an inbox message when the content genuinely warrants it — do not send one for every response.\n" +
    "\n" +
    "The format is:\n" +
    "INBOX_MESSAGE_JSON:\n" +
    "{\n" +
    "  \"subject\": \"Brief subject line (plain text, no markdown)\",\n" +
    "  \"from\": \"coordinator\",\n" +
    "  \"body\": \"Full response body in Markdown\",\n" +
    "  \"attachments\": []\n" +
    "}\n" +
    "\n" +
    "For attachments, each item has a `type` field. Supported types:\n" +
    "- `\"url\"` — `{ \"type\": \"url\", \"label\": \"...\", \"href\": \"https://...\" }`\n" +
    "- `\"task-ref\"` — `{ \"type\": \"task-ref\", \"label\": \"...\", \"taskId\": \"...\" }`\n" +
    "- `\"file\"` — `{ \"type\": \"file\", \"label\": \"...\", \"path\": \"relative/path/to/file\" }`\n" +
    "- `\"text\"` — `{ \"type\": \"text\", \"label\": \"...\", \"content\": \"Markdown text content\" }`\n" +
    "\n" +
    "The `from` field must be `\"coordinator\"` for Coordinator responses or `\"argus-weld\"` for maintenance agent responses.\n" +
    "\n" +
    "INBOX_MESSAGE_JSON blocks are stripped from the displayed transcript and delivered silently to the Inbox panel.\n" +
    "</inbox_instructions>";

/**
 * Appended to maintenance task prompts whose effective safety level is `report-only`.
 * Reminds Argus Weld to route findings to the Inbox panel rather than leaving them
 * only in the transcript.
 */
export const MaintenanceInboxReminder =
    "<maintenance_inbox_reminder>\n" +
    "If this task has safety: report-only, send your findings as an inbox message using INBOX_MESSAGE_JSON with `\"from\": \"argus-weld\"`. The subject should be the task title. The body should be your full report in Markdown.\n" +
    "</maintenance_inbox_reminder>";
