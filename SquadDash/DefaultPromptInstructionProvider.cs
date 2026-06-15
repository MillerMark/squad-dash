namespace SquadDash;

/// <summary>
/// Returns the built-in AI behavioural policy strings.
/// Registered as a singleton in <see cref="App"/>.
/// </summary>
internal sealed class DefaultPromptInstructionProvider : IPromptInstructionProvider {

    private static readonly PromptInstructionSet Instance = new(
        TurnSummary:
            "At the very end of your response, on its own line, append a machine-readable turn summary in this exact format " +
            "(it is stripped from the displayed transcript and is never shown to the user):\n" +
            "<system_notification>{\"notification\": \"one short sentence — 10 words or fewer — describing what you did or answered. If you made or reported a git commit, the description must name what was committed.\"}</system_notification>",
        InboxMessage:
            "<inbox_instructions>\n" +
            "You may send the user a message to their Inbox panel by appending an INBOX_MESSAGE_JSON block at the very end of your response, after all other content. Use this when:\n" +
            "- Your response is a detailed report, analysis, or long-form answer that the user might want to refer back to\n" +
            "- You are completing a code health task with a report-only safety level\n" +
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
            "An optional `actions` array adds deferred quick-reply buttons to the message. " +
            "Action buttons are useful when the user should choose a later follow-up without typing, especially during maintenance when the user is away.\n" +
            "Inbox actions are deferred user choices, not immediate delegation. If you decide that a named agent should start now, launch that agent with the native delegation/tool path instead of writing an inbox action that promises the handoff. Do not say an agent is starting or being routed unless the launch actually happens.\n" +
            "Each action:\n" +
            "- `\"label\"` — button text shown to the user\n" +
            "- `\"routeMode\"` — `\"start_named_agent\"`, `\"start_coordinator\"`, `\"draft\"`, or `\"done\"`\n" +
            "- `\"targetAgent\"` — agent handle (required when routeMode is `\"start_named_agent\"`)\n" +
            "- `\"prompt\"` — **fully self-contained** prompt injected when the user clicks the button. " +
            "Must include all context (file paths, symptoms, findings) — no conversation history will be available. " +
            "For `\"draft\"` actions, `\"prompt\"` is the pre-fill text placed into the user's input box without sending. " +
            "Best use: list questions as labeled placeholders so the user fills them in before sending — " +
            "e.g. `\"Here are my answers:\\n\\n1. (Q: Priority?) \\n2. (Q: Target branch?) \"`. " +
            "This is ideal when you need the user to answer questions — they fill in the blanks and send.\n" +
            "\n" +
            "Do NOT include any 'done' action whose label is purely acknowledgement-only (closing or acknowledging the message without recording a decision). " +
            "Only include a `\"done\"` action when its label records a meaningful user decision (e.g. 'Mark resolved', 'Already fixed') " +
            "and the user genuinely needs a way to record that decision without launching an agent. In most cases, omit the 'done' action entirely.\n"+
            "\n" +
            "The `from` field must be `\"coordinator\"` for Coordinator responses or `\"argus-weld\"` for code health agent responses.\n" +
            "\n" +
            "INBOX_MESSAGE_JSON blocks are stripped from the displayed transcript and delivered silently to the Inbox panel.\n" +
            "</inbox_instructions>",
        QuickReply:
            "When you offer quick replies, append a machine-readable block exactly in this format:\nQUICK_REPLIES_JSON:\n[\n  {\n    \"label\": \"Option A\",\n    \"routeMode\": \"continue_current_agent\",\n    \"reason\": \"One short routing reason.\"\n  },\n  {\n    \"label\": \"Option B\",\n    \"routeMode\": \"start_named_agent\",\n    \"targetAgent\": \"orion-vale\",\n    \"reason\": \"One short routing reason.\"\n  }\n]\nOnly emit quick replies when the user can act on them immediately. Do not emit quick replies while background agents are still working, while you are only reporting progress, or while the next step is blocked on unfinished work. Do not emit quick replies in the same response where you launch, assign, queue, delegate, or hand off new background work. If you tell the user that an agent is starting, is running, will continue in the background, that you will report back later, or that they should use `/tasks` for status, emit no quick replies at all in that response. Quick replies are only allowed after the relevant agent work has finished and the user can immediately choose the next real step. Each quick reply must include `label` and `routeMode`. `routeMode` must be one of `continue_current_agent`, `start_named_agent`, `start_coordinator`, `fanout_team`, `draft`, or `done`. Include `targetAgent` only when `routeMode` is `start_named_agent`, using a roster handle from `.squad/team.md`. Use `draft` when clicking the button should pre-fill the user's input box with a prompt template without sending it — include a `prompt` field with the pre-fill text; the user can edit it before sending. This is useful when the response contains questions or a form that the user should answer themselves. The best use case is when you have a set of questions: the draft prompt lists each question as a labeled placeholder so the user just moves their caret to each one and fills in the answer. Example: `{ \"label\": \"Answer these questions\", \"routeMode\": \"draft\", \"prompt\": \"Here are my answers:\\n\\n1. (Q: What is the priority?) \\n2. (Q: Which branch should this go on?) \\n3. (Q: Any blockers I should know about?) \" }`. Use `continue_current_agent` only when the next step should stay with the same agent who produced the current response. Use `.squad/team.md` and `.squad/routing.md` to choose the correct owner. Keep the label and metadata aligned: if the button says to run, ask, hand off to, or start a different agent, utility agent, or specialist, do not use `continue_current_agent`; use `start_named_agent` with the correct `targetAgent` instead. In particular, if the next step is to run Scribe, Ralph, or any agent other than the one who produced the current response, the quick reply must use `start_named_agent` and name that agent explicitly. When a quick reply names or implies an owner for follow-up work, delegated work, backlog items, reviews, or test work, keep that owner aligned with `.squad/routing.md` instead of assigning by convenience. Do not assign testing, QA, verification, or coverage work to a non-testing specialist unless `.squad/routing.md` explicitly gives them that ownership or you clearly describe the work as collaboration under the testing lead. Never include no-op buttons — every quick reply must cause something meaningful to happen. Do not include a lone \"Done\" button when it would just send an empty acknowledgement. Do not include a \"No\" or \"Cancel\" button on a yes/no question unless clicking it would actually trigger a useful action; if declining means doing nothing, omit it entirely. If the only honest reply is \"you're finished\", emit no quick replies at all. Do NOT emit buttons like \"Looks good — what's next?\", \"Looks good\", \"What's next?\", \"All done\", or any variant that is just an acknowledgement or a vague invitation to continue — these are no-ops because clicking them gives the AI nothing actionable to act on. A quick reply is only valid if clicking it causes a specific, identifiable action: routing to a named agent, starting a named task, or asking a concrete question. If there is no active task list and no specific next step you can name, emit no quick replies at all.",
        CoordinatorDelegationAccountability:
            """
            Coordinator delegation accountability:
            Before doing implementation, investigation, testing, review, documentation, or performance work yourself, decide whether a roster agent should own it according to `.squad/team.md` and `.squad/routing.md`.

            If you keep the work in the Coordinator instead of launching an appropriate agent, include one short sentence at the start of your visible response:
            "Doing this myself because <reason>."

            Valid reasons are narrow: quick factual answer, the task is quick/trivial, user explicitly asked the Coordinator to handle it, no clear specialist exists, or launching an agent is somehow blocked. Otherwise, launch the appropriate agent instead of doing the work inline.

            If a delegated-agent tool reports a durable failure such as `Agent not found`, `Maximum concurrent agent limit`, or another repeated identical failure, do not retry the same tool call with the same arguments in a loop. Change strategy: use any agent results already available in the transcript, wait for currently running agents, summarize the blocker, or ask the user how to proceed.
            """);

    public PromptInstructionSet Get() => Instance;
}

