import fs from "node:fs";
import path from "node:path";
import { SquadBridgeService, type SessionReadyInfo, type ToolLifecycleEvent } from "./squadService.js";

type Args = {
    model: string;
    baseUrl: string;
    wireApi?: string;
    cwd: string;
    timeoutMs: number;
    keepArtifacts: boolean;
};

type EventRecord = Record<string, unknown> & { type: string };

type PromptRunResult = {
    name: string;
    prompt: string;
    responseText: string;
    events: EventRecord[];
    error?: string;
    durationMs: number;
};

type CheckResult = {
    name: string;
    passed: boolean;
    details: Record<string, unknown>;
};

const DefaultTimeoutMs = 180_000;

function parseArgs(argv: string[]): Args {
    const args: Partial<Args> = {};
    for (let i = 0; i < argv.length; i++) {
        const arg = argv[i];
        switch (arg) {
            case "--model":
                args.model = argv[++i];
                break;
            case "--base-url":
                args.baseUrl = argv[++i];
                break;
            case "--wire-api":
                args.wireApi = argv[++i];
                break;
            case "--cwd":
                args.cwd = argv[++i];
                break;
            case "--timeout-ms":
                args.timeoutMs = Number(argv[++i]);
                break;
            case "--keep-artifacts":
                args.keepArtifacts = true;
                break;
            default:
                if (!args.model && !arg.startsWith("--"))
                    args.model = arg;
                break;
        }
    }

    return {
        model: args.model ?? process.env.COPILOT_PROVIDER_MODEL_ID ?? "lfm2.5:8b",
        baseUrl: args.baseUrl ?? process.env.COPILOT_PROVIDER_BASE_URL ?? "http://127.0.0.1:11435/v1",
        wireApi: args.wireApi ?? process.env.COPILOT_PROVIDER_WIRE_API,
        cwd: path.resolve(args.cwd ?? process.cwd()),
        timeoutMs: Number.isFinite(args.timeoutMs) && args.timeoutMs! > 0
            ? args.timeoutMs!
            : DefaultTimeoutMs,
        keepArtifacts: args.keepArtifacts ?? false
    };
}

function configureProvider(args: Args): void {
    process.env.COPILOT_PROVIDER_BASE_URL = args.baseUrl;
    process.env.COPILOT_PROVIDER_MODEL_ID = args.model;
    process.env.COPILOT_PROVIDER_TYPE = process.env.COPILOT_PROVIDER_TYPE || "openai";
    if (args.wireApi)
        process.env.COPILOT_PROVIDER_WIRE_API = args.wireApi;
    process.env.COPILOT_OFFLINE = process.env.COPILOT_OFFLINE || "true";
}

async function runPromptCheck(
    name: string,
    prompt: string,
    args: Args
): Promise<PromptRunResult> {
    const bridge = new SquadBridgeService();
    const events: EventRecord[] = [];
    const responseChunks: string[] = [];
    let sessionId: string | undefined;
    let timeout: ReturnType<typeof setTimeout> | undefined;
    const startedAt = Date.now();

    const task = bridge.runPrompt(prompt, {
        onSessionReady(session: SessionReadyInfo) {
            sessionId = session.sessionId;
            events.push({
                type: "session_ready",
                sessionId: session.sessionId,
                resumed: session.resumed,
                sessionReuseKind: session.sessionReuseKind
            });
        },
        onThinking(text, speaker) {
            events.push({ type: "thinking_delta", text, speaker });
        },
        onUsage(usage) {
            events.push({ type: "usage", ...usage });
        },
        onToolStart(tool: ToolLifecycleEvent) {
            events.push({
                type: "tool_start",
                toolCallId: tool.toolCallId,
                toolName: tool.toolName,
                command: tool.command,
                path: tool.path,
                args: tool.args
            });
        },
        onToolProgress(tool: ToolLifecycleEvent) {
            events.push({
                type: "tool_progress",
                toolCallId: tool.toolCallId,
                toolName: tool.toolName,
                progressMessage: tool.progressMessage,
                partialOutput: tool.partialOutput
            });
        },
        onToolComplete(tool: ToolLifecycleEvent) {
            events.push({
                type: "tool_complete",
                toolCallId: tool.toolCallId,
                toolName: tool.toolName,
                success: tool.success,
                outputText: tool.outputText
            });
        },
        onToolArgsRewritten(rewrite) {
            events.push({ type: "tool_args_rewritten", ...rewrite });
        },
        onDelta(chunk) {
            responseChunks.push(chunk);
            events.push({ type: "response_delta", chunk });
        },
        onDone(finalMessage) {
            events.push({ type: "done", finalMessage });
        },
        onAborted() {
            events.push({ type: "aborted" });
        }
    }, {
        cwd: args.cwd,
        model: args.model
    });

    const timeoutTask = new Promise<never>((_, reject) => {
        timeout = setTimeout(() => {
            void bridge.abortPrompt(sessionId).catch(() => undefined);
            reject(new Error(`Timed out after ${args.timeoutMs} ms.`));
        }, args.timeoutMs);
    });

    let error: string | undefined;
    try {
        await Promise.race([task, timeoutTask]);
    }
    catch (err) {
        error = err instanceof Error ? err.message : String(err);
        events.push({ type: "error", message: error });
    }
    finally {
        if (timeout)
            clearTimeout(timeout);

        try {
            await bridge.shutdown();
        }
        catch {
            // Shutdown is best-effort in conformance runs; the error was already captured above.
        }
    }

    return {
        name,
        prompt,
        responseText: responseChunks.join(""),
        events,
        error,
        durationMs: Date.now() - startedAt
    };
}

function markerJson<T>(text: string, marker: string, array: boolean): T | undefined {
    const normalized = text.replace(/\r\n/g, "\n").replace(/\r/g, "\n");
    const idx = normalized.lastIndexOf(marker);
    if (idx < 0)
        return undefined;

    const jsonStart = normalized.indexOf(array ? "[" : "{", idx + marker.length);
    if (jsonStart < 0)
        return undefined;

    const jsonText = extractBalancedJson(normalized, jsonStart, array);
    if (!jsonText)
        return undefined;

    try {
        return JSON.parse(jsonText) as T;
    }
    catch {
        return undefined;
    }
}

function extractBalancedJson(text: string, start: number, array: boolean): string | undefined {
    const open = array ? "[" : "{";
    const close = array ? "]" : "}";
    if (text[start] !== open)
        return undefined;

    let depth = 0;
    let inString = false;
    let escaped = false;

    for (let i = start; i < text.length; i++) {
        const ch = text[i];
        if (escaped) {
            escaped = false;
            continue;
        }

        if (inString && ch === "\\") {
            escaped = true;
            continue;
        }

        if (ch === "\"") {
            inString = !inString;
            continue;
        }

        if (inString)
            continue;

        if (ch === open)
            depth++;
        else if (ch === close) {
            depth--;
            if (depth === 0)
                return text.slice(start, i + 1);
        }
    }

    return undefined;
}

function hasReportWrapper(text: string): boolean {
    return /"name"\s*:\s*"report(?:_intent)?"/i.test(text);
}

function scorePlain(run: PromptRunResult, expectedToken: string): CheckResult {
    const passed = !run.error && run.responseText.includes(expectedToken);
    return {
        name: "plain_response",
        passed,
        details: {
            error: run.error,
            expectedToken,
            responseText: run.responseText,
            leakedReportWrapper: hasReportWrapper(run.responseText),
            durationMs: run.durationMs
        }
    };
}

function scoreTool(run: PromptRunResult, filePath: string, expectedText: string): CheckResult {
    const toolStarts = run.events.filter(event => event.type === "tool_start");
    const toolCompletes = run.events.filter(event => event.type === "tool_complete");
    const fileExists = fs.existsSync(filePath);
    const fileText = fileExists ? fs.readFileSync(filePath, "utf8").trim() : "";
    const passed = !run.error && toolStarts.length > 0 && fileText === expectedText;

    return {
        name: "tool_execution",
        passed,
        details: {
            error: run.error,
            toolStarts: toolStarts.length,
            toolCompletes: toolCompletes.length,
            toolStartEvents: toolStarts,
            toolCompleteEvents: toolCompletes,
            filePath,
            fileExists,
            expectedText,
            fileText,
            responseText: run.responseText,
            durationMs: run.durationMs
        }
    };
}

function scoreQuickReplies(run: PromptRunResult): CheckResult {
    type QuickReply = { label?: unknown; routeMode?: unknown; targetAgent?: unknown; reason?: unknown; prompt?: unknown };
    const replies = markerJson<QuickReply[]>(run.responseText, "QUICK_REPLIES_JSON:", true);
    const valid = Array.isArray(replies) &&
        replies.length >= 2 &&
        replies.every(reply =>
            reply &&
            typeof reply === "object" &&
            typeof reply.label === "string" &&
            reply.label.trim().length > 0 &&
            (reply.routeMode === undefined || typeof reply.routeMode === "string") &&
            (reply.targetAgent === undefined || typeof reply.targetAgent === "string") &&
            (reply.reason === undefined || typeof reply.reason === "string") &&
            (reply.prompt === undefined || typeof reply.prompt === "string"));

    return {
        name: "quick_replies_json",
        passed: !run.error && valid,
        details: {
            error: run.error,
            parsedCount: Array.isArray(replies) ? replies.length : 0,
            responseText: run.responseText,
            durationMs: run.durationMs
        }
    };
}

function scoreInbox(run: PromptRunResult): CheckResult {
    type Inbox = { subject?: unknown; from?: unknown; body?: unknown; attachments?: unknown; actions?: unknown };
    const inbox = markerJson<Inbox>(run.responseText, "INBOX_MESSAGE_JSON:", false);
    const valid = !!inbox &&
        typeof inbox.subject === "string" &&
        inbox.subject.trim().length > 0 &&
        typeof inbox.from === "string" &&
        typeof inbox.body === "string" &&
        Array.isArray(inbox.attachments) &&
        (inbox.actions === undefined || Array.isArray(inbox.actions));

    return {
        name: "inbox_message_json",
        passed: !run.error && valid,
        details: {
            error: run.error,
            parsedSubject: typeof inbox?.subject === "string" ? inbox.subject : undefined,
            responseText: run.responseText,
            durationMs: run.durationMs
        }
    };
}

function scoreHostCommand(run: PromptRunResult): CheckResult {
    type HostCommand = { command?: unknown; parameters?: unknown };
    const commands = markerJson<HostCommand[]>(run.responseText, "HOST_COMMAND_JSON:", true);
    const valid = Array.isArray(commands) &&
        commands.length > 0 &&
        commands.every(command =>
            command &&
            typeof command === "object" &&
            typeof command.command === "string" &&
            command.command.trim().length > 0 &&
            (command.parameters === undefined ||
                (!!command.parameters && typeof command.parameters === "object" && !Array.isArray(command.parameters))));

    return {
        name: "host_command_json",
        passed: !run.error && valid,
        details: {
            error: run.error,
            parsedCount: Array.isArray(commands) ? commands.length : 0,
            responseText: run.responseText,
            durationMs: run.durationMs
        }
    };
}

async function main(): Promise<void> {
    const args = parseArgs(process.argv.slice(2));
    configureProvider(args);

    const runId = Date.now().toString(36);
    const plainToken = `LFM_PLAIN_OK_${runId}`;
    const toolToken = `LFM_TOOL_OK_${runId}`;
    const toolFilePath = path.join(args.cwd, `.ollama-tool-test-${runId}.txt`);
    if (fs.existsSync(toolFilePath))
        fs.unlinkSync(toolFilePath);

    const runs: PromptRunResult[] = [];
    const checks: CheckResult[] = [];

    const plain = await runPromptCheck(
        "plain_response",
        `Reply with exactly this text and nothing else: ${plainToken}`,
        args);
    runs.push(plain);
    checks.push(scorePlain(plain, plainToken));

    const tool = await runPromptCheck(
        "tool_execution",
        [
            "Use the PowerShell tool now. Do not use apply_patch. Do not describe the steps.",
            "Use Set-Content to write the file and Get-Content to read it back.",
            `Create the file ${JSON.stringify(toolFilePath)} containing exactly ${toolToken}.`,
            "Then read the file back and report whether the file content matched."
        ].join("\n"),
        args);
    runs.push(tool);
    checks.push(scoreTool(tool, toolFilePath, toolToken));

    const quickReplies = await runPromptCheck(
        "quick_replies_json",
        [
            "Reply with exactly the following text and no code fences:",
            "",
            "Choose a next action.",
            "",
            "QUICK_REPLIES_JSON:",
            "[",
            "  { \"label\": \"Run verification\", \"routeMode\": \"start_named_agent\", \"targetAgent\": \"vesper-knox\", \"reason\": \"Run the test specialist.\" },",
            "  { \"label\": \"Draft summary\", \"routeMode\": \"coordinator\", \"reason\": \"Summarize the result.\" }",
            "]"
        ].join("\n"),
        args);
    runs.push(quickReplies);
    checks.push(scoreQuickReplies(quickReplies));

    const inbox = await runPromptCheck(
        "inbox_message_json",
        [
            "Reply with exactly the following text and no code fences:",
            "",
            "I saved this for later.",
            "",
            "INBOX_MESSAGE_JSON:",
            "{",
            "  \"subject\": \"Local model conformance\",",
            "  \"from\": \"coordinator\",",
            "  \"body\": \"The local model emitted a valid inbox message.\",",
            "  \"attachments\": []",
            "}"
        ].join("\n"),
        args);
    runs.push(inbox);
    checks.push(scoreInbox(inbox));

    const hostCommand = await runPromptCheck(
        "host_command_json",
        [
            "Reply with exactly the following text and no code fences:",
            "",
            "Opening the approvals panel.",
            "",
            "HOST_COMMAND_JSON:",
            "[",
            "  { \"command\": \"open_panel\", \"parameters\": { \"panel\": \"approvals\" } }",
            "]"
        ].join("\n"),
        args);
    runs.push(hostCommand);
    checks.push(scoreHostCommand(hostCommand));

    const cleanup = {
        toolFileRemoved: false,
        toolFileKept: false
    };
    if (fs.existsSync(toolFilePath)) {
        if (args.keepArtifacts) {
            cleanup.toolFileKept = true;
        }
        else {
            fs.unlinkSync(toolFilePath);
            cleanup.toolFileRemoved = true;
        }
    }

    const summary = {
        model: args.model,
        baseUrl: args.baseUrl,
        wireApi: args.wireApi,
        cwd: args.cwd,
        runId,
        passed: checks.filter(check => check.passed).length,
        total: checks.length,
        checks,
        cleanup,
        runs: runs.map(run => ({
            name: run.name,
            error: run.error,
            durationMs: run.durationMs,
            eventTypes: run.events.map(event => event.type),
            responseText: run.responseText
        }))
    };

    console.log(JSON.stringify(summary, null, 2));
    process.exitCode = checks.every(check => check.passed) ? 0 : 1;
}

main().catch(err => {
    console.error(err instanceof Error ? err.stack ?? err.message : String(err));
    process.exitCode = 1;
});
