import { existsSync, readFileSync } from "node:fs";
import path from "node:path";
import { fileURLToPath } from "node:url";
import { SquadClient } from "@bradygaster/squad-sdk/client";
import type { SquadSessionConfig } from "@bradygaster/squad-sdk/client";

const SessionIdleTimeoutMs = 60 * 60 * 1000;

export type SquadPromptRequest = {
    cwd: string;
    sessionId?: string;
    configDir?: string;
    model?: string;
};

type SquadSessionRequest = SquadPromptRequest & {
    requireSameSession?: boolean;
};

export type SquadDelegationRequest = {
    cwd: string;
    sessionId: string;
    configDir?: string;
    model?: string;
    selectedOption: string;
    targetAgent: string;
};

export type SquadNamedAgentRequest = {
    cwd: string;
    selectedOption: string;
    handoffContext?: string;
    targetAgent: string;
    namedAgentSessionId?: string;
    charterContent?: string;
    configDir?: string;
    model?: string;
};

export type SessionReadyInfo = {
    sessionId: string;
    resumed: boolean;
    sessionReuseKind?: string;
    sessionAcquireDurationMs?: number;
    sessionResumeDurationMs?: number;
    sessionCreateDurationMs?: number;
    sessionResumeFailureMessage?: string;
    sessionAgeMs?: number;
    sessionPromptCountBeforeCurrent?: number;
    sessionPromptCountIncludingCurrent?: number;
    backgroundAgentCount?: number;
    backgroundShellCount?: number;
    knownSubagentCount?: number;
    activeToolCount?: number;
    cachedAssistantChars?: number;
};

export type ToolLifecycleEvent = {
    parentToolCallId?: string;
    toolCallId: string;
    toolName: string;
    args?: unknown;
    startedAt: string;
    finishedAt?: string;
    description?: string;
    command?: string;
    path?: string;
    intent?: string;
    skill?: string;
    progressMessage?: string;
    partialOutput?: string;
    outputText?: string;
    success?: boolean;
};

export type ToolArgsRewriteEvent = {
    toolName: string;
    reason: string;
    originalCommand: string;
    modifiedCommand: string;
};

export type BackgroundAgentInfo = {
    agentId: string;
    toolCallId?: string;
    agentType?: string;
    status?: string;
    description?: string;
    prompt?: string;
    error?: string;
    startedAt?: string;
    completedAt?: string;
    latestResponse?: string;
    latestIntent?: string;
    recentActivity?: string[];
    agentName?: string;
    agentDisplayName?: string;
    model?: string;
    totalToolCalls?: number;
    totalInputTokens?: number;
    totalOutputTokens?: number;
};

export type BackgroundShellInfo = {
    shellId: string;
    status?: string;
    description?: string;
    command?: string;
    startedAt?: string;
    completedAt?: string;
    recentOutput?: string;
    pid?: number;
};

export type BackgroundTaskSnapshot = {
    agents: BackgroundAgentInfo[];
    shells: BackgroundShellInfo[];
};

export type SubagentLifecycleInfo = {
    toolCallId?: string;
    agentId?: string;
    agentName: string;
    agentDisplayName?: string;
    agentDescription?: string;
    prompt?: string;
    error?: string;
    model?: string;
    totalToolCalls?: number;
    totalTokens?: number;
    durationMs?: number;
};

export type SubagentTranscriptInfo = {
    parentToolCallId: string;
    agentId?: string;
    agentName?: string;
    agentDisplayName?: string;
    agentDescription?: string;
    text?: string;
    reasoningText?: string;
};

export type WatchFleetInfo = {
    cycleId?: string;
    fleetSize?: number;
    prompt?: string;
};

export type WatchWaveInfo = {
    cycleId?: string;
    waveIndex?: number;
    waveCount?: number;
    agentCount?: number;
};

export type WatchHydrationInfo = {
    cycleId?: string;
    phase?: string;
};

export type WatchRetroInfo = {
    cycleId?: string;
    summary?: string;
};

export type WatchMonitorInfo = {
    cycleId?: string;
    channel?: string;
    sent?: boolean;
    recipient?: string;
};

export type SquadRunHandlers = {
    onSessionReady?: (session: SessionReadyInfo) => void;
    onThinking?: (text: string, speaker?: string) => void;
    onUsage?: (usage: {
        model?: string;
        inputTokens?: number;
        outputTokens?: number;
        totalTokens?: number;
    }) => void;
    onToolStart?: (tool: ToolLifecycleEvent) => void;
    onToolProgress?: (tool: ToolLifecycleEvent) => void;
    onToolComplete?: (tool: ToolLifecycleEvent) => void;
    onToolArgsRewritten?: (rewrite: ToolArgsRewriteEvent) => void;
    onDelta?: (markdownChunk: string) => void;
    onDone?: (finalMessage: unknown) => void;
    onAborted?: () => void;
};

export type SquadBridgeHandlers = {
    onBackgroundTasksChanged?: (sessionId: string, tasks: BackgroundTaskSnapshot) => void;
    onTaskComplete?: (sessionId: string, summary?: string) => void;
    onSubagentStarted?: (sessionId: string, subagent: SubagentLifecycleInfo) => void;
    onSubagentCompleted?: (sessionId: string, subagent: SubagentLifecycleInfo) => void;
    onSubagentFailed?: (sessionId: string, subagent: SubagentLifecycleInfo) => void;
    onSubagentMessageDelta?: (sessionId: string, subagent: SubagentTranscriptInfo) => void;
    onSubagentThinkingDelta?: (sessionId: string, subagent: SubagentTranscriptInfo) => void;
    onSubagentMessage?: (sessionId: string, subagent: SubagentTranscriptInfo) => void;
    onSubagentToolStart?: (sessionId: string, subagent: SubagentLifecycleInfo, tool: ToolLifecycleEvent) => void;
    onSubagentToolProgress?: (sessionId: string, subagent: SubagentLifecycleInfo, tool: ToolLifecycleEvent) => void;
    onSubagentToolComplete?: (sessionId: string, subagent: SubagentLifecycleInfo, tool: ToolLifecycleEvent) => void;
    // Watch lifecycle handlers — stubbed pending SDK stabilization of watch.* events
    onWatchFleetDispatched?: (sessionId: string, info: WatchFleetInfo) => void;
    onWatchWaveDispatched?: (sessionId: string, info: WatchWaveInfo) => void;
    onWatchHydration?: (sessionId: string, info: WatchHydrationInfo) => void;
    onWatchRetro?: (sessionId: string, info: WatchRetroInfo) => void;
    onWatchMonitorNotification?: (sessionId: string, info: WatchMonitorInfo) => void;
};

type ActiveToolContext = Omit<
    ToolLifecycleEvent,
    "toolCallId" | "finishedAt" | "progressMessage" | "partialOutput" | "outputText" | "success"
>;

type SquadSession = Awaited<ReturnType<SquadClient["createSession"]>>;

type RequestContext = {
    aborted: boolean;
    handlers: SquadRunHandlers;
    sawMessageDelta: boolean;
    streamedMessageContent: string;
    lastAssistantMessageContent: string;
    hiddenAdditionalContext?: string;
};

type SessionState = {
    session: SquadSession;
    activeTools: Map<string, ActiveToolContext>;
    backgroundTasks: BackgroundTaskSnapshot;
    subagentsByToolCallId: Map<string, SubagentLifecycleInfo>;
    backgroundTaskIdsByToolCallId: Map<string, string>;
    subagentReasoningByToolCallId: Map<string, string>;
    currentRequest?: RequestContext;
    lastAssistantMessageContent: string;
    createdAt: number;
    completedPromptCount: number;
};

const GenericIdentityKeys = new Set([
    "agent",
    "backgroundagent",
    "default",
    "general",
    "generalpurpose",
    "purpose",
    "squad",
    "task",
    "worker"
]);

function normalizeOptionalString(value: string | undefined): string | undefined {
    const trimmed = value?.trim();
    return trimmed && trimmed.length > 0 ? trimmed : undefined;
}

export function normalizeAssistantResponseContent(content: string | undefined): string | undefined {
    const trimmed = content?.trim();
    if (!trimmed)
        return undefined;

    const jsonText = unwrapJsonCodeFence(trimmed);
    let parsed: unknown;
    try {
        parsed = JSON.parse(jsonText);
    }
    catch {
        return content;
    }

    if (!parsed || typeof parsed !== "object" || Array.isArray(parsed))
        return content;

    const record = parsed as Record<string, unknown>;
    if (record.name === "report_intent" ||
        record.name === "report_model" ||
        record.name === "task")
        return undefined;

    if (record.name === "report_progress")
        return normalizeReportProgressContent(record) ?? undefined;

    if (record.name !== "report")
        return content;

    const args = record.arguments;
    if (!args || typeof args !== "object" || Array.isArray(args))
        return content;

    const body = (args as Record<string, unknown>).body;
    return typeof body === "string" && body.trim().length > 0
        ? body
        : content;
}

function normalizeReportProgressContent(record: Record<string, unknown>): string | undefined {
    const args = record.arguments;
    if (!args || typeof args !== "object" || Array.isArray(args))
        return undefined;

    const message = (args as Record<string, unknown>).message;
    return typeof message === "string" && message.trim().length > 0
        ? message
        : undefined;
}

function unwrapJsonCodeFence(text: string): string {
    const match = /^```(?:json)?\s*([\s\S]*?)\s*```$/i.exec(text);
    return match ? match[1].trim() : text;
}

type ProviderType = NonNullable<NonNullable<SquadSessionConfig["provider"]>["type"]>;
type WireApi = NonNullable<NonNullable<SquadSessionConfig["provider"]>["wireApi"]>;

function normalizeProviderType(value: string | undefined): ProviderType | undefined {
    const normalized = normalizeOptionalString(value)?.toLowerCase();
    return normalized === "openai" || normalized === "azure" || normalized === "anthropic"
        ? normalized
        : undefined;
}

function normalizeWireApi(value: string | undefined): WireApi | undefined {
    const normalized = normalizeOptionalString(value)?.toLowerCase();
    return normalized === "completions" || normalized === "responses"
        ? normalized
        : undefined;
}

function buildCustomProviderFromEnv(): SquadSessionConfig["provider"] | undefined {
    const baseUrl = normalizeOptionalString(process.env.COPILOT_PROVIDER_BASE_URL);
    if (!baseUrl)
        return undefined;

    const provider: NonNullable<SquadSessionConfig["provider"]> = { baseUrl };
    const type = normalizeProviderType(process.env.COPILOT_PROVIDER_TYPE);
    const wireApi = normalizeWireApi(process.env.COPILOT_PROVIDER_WIRE_API);
    const apiKey = normalizeOptionalString(process.env.COPILOT_PROVIDER_API_KEY);
    const bearerToken = normalizeOptionalString(process.env.COPILOT_PROVIDER_BEARER_TOKEN);
    const azureApiVersion = normalizeOptionalString(process.env.COPILOT_PROVIDER_AZURE_API_VERSION);

    if (type)
        provider.type = type;

    if (wireApi)
        provider.wireApi = wireApi;

    if (apiKey)
        provider.apiKey = apiKey;

    if (bearerToken)
        provider.bearerToken = bearerToken;

    if (azureApiVersion)
        provider.azure = { apiVersion: azureApiVersion };

    return provider;
}

function resolveSessionModel(optionsModel: string | undefined): string | undefined {
    return normalizeOptionalString(optionsModel) ??
        normalizeOptionalString(process.env.COPILOT_MODEL) ??
        normalizeOptionalString(process.env.COPILOT_PROVIDER_MODEL_ID) ??
        normalizeOptionalString(process.env.COPILOT_PROVIDER_WIRE_MODEL);
}

const PendingRestartDeploymentEnv = {
    appRoot: "SQUADDASH_APP_ROOT",
    restartRequestPath: "SQUADDASH_RESTART_REQUEST_PATH"
} as const;

const RunSlotDeploymentSuppressedMessage =
    "[SquadDash] Run-slot deployment disabled for this self-build because a restart request is already pending.";

const RunSlotDeploymentEnabledMessage =
    "[SquadDash] Run-slot deployment enabled for this self-build.";

function bridgeDiagnostic(message: string, fields: Record<string, unknown> = {}) {
    if (process.env.SQUADDASH_BRIDGE_DIAGNOSTICS !== "true")
        return;

    console.error(`[SquadDash bridge diagnostic] ${JSON.stringify({
        at: new Date().toISOString(),
        message,
        ...fields
    })}`);
}

type ToolArgsRewriteResult = {
    modifiedArgs: Record<string, unknown>;
    reason: string;
    originalCommand: string;
    modifiedCommand: string;
};

function parseToolArgs(toolArgs: unknown): Record<string, unknown> | undefined {
    if (!toolArgs)
        return undefined;

    if (typeof toolArgs === "string") {
        try {
            const parsed = JSON.parse(toolArgs);
            return parsed && typeof parsed === "object" && !Array.isArray(parsed)
                ? parsed as Record<string, unknown>
                : undefined;
        }
        catch {
            return undefined;
        }
    }

    return typeof toolArgs === "object" && !Array.isArray(toolArgs)
        ? toolArgs as Record<string, unknown>
        : undefined;
}

function normalizePathForCompare(value: string): string {
    return path.resolve(value)
        .trim()
        .toLowerCase();
}

function isPathInsideOrEqual(candidate: string, root: string): boolean {
    const normalizedCandidate = normalizePathForCompare(candidate);
    const normalizedRoot = normalizePathForCompare(root);
    const relative = path.relative(normalizedRoot, normalizedCandidate);
    return relative.length === 0 ||
        (!!relative && !relative.startsWith("..") && !path.isAbsolute(relative));
}

function commandMentionsPath(command: string, targetPath: string): boolean {
    const normalizedCommand = command.replace(/\\/g, "/").toLowerCase();
    const normalizedPath = path.resolve(targetPath).replace(/\\/g, "/").toLowerCase();
    return normalizedCommand.includes(normalizedPath);
}

function commandInvokesDotNetBuildOrTest(command: string): boolean {
    return /\bdotnet(?:\.exe)?\s+(?:build|test)\b/i.test(command);
}

function commandInvokesDotNetBuild(command: string): boolean {
    return /\bdotnet(?:\.exe)?\s+build\b/i.test(command);
}

function commandTargetsSquadDashProject(command: string): boolean {
    return /(?:^|[\\/\s"'`])squaddash\.csproj(?:$|[\s"'`;&|)])/i.test(command);
}

function commandTargetsSquadDashSolution(command: string): boolean {
    return /(?:^|[\\/\s"'`])(?:squad-dash|squaddash)\.slnx?(?:$|[\s"'`;&|)])/i.test(command);
}

function commandExplicitlyControlsRunSlotDeployment(command: string): boolean {
    return command.toLowerCase().includes("enablerunslotdeployment");
}

function toPowerShellSingleQuotedLiteral(value: string): string {
    return `'${value.replace(/'/g, "''")}'`;
}

function buildPowerShellRunSlotDeploymentCommand(command: string, enabled: boolean): string {
    const value = enabled ? "true" : "false";
    const message = enabled
        ? RunSlotDeploymentEnabledMessage
        : RunSlotDeploymentSuppressedMessage;

    return `$env:EnableRunSlotDeployment='${value}'; Write-Output ${toPowerShellSingleQuotedLiteral(message)}; ${command}`;
}

function buildPowerShellDeploymentSuppressionCommand(command: string): string {
    return buildPowerShellRunSlotDeploymentCommand(command, false);
}

function buildPowerShellDeploymentEnabledCommand(command: string): string {
    return buildPowerShellRunSlotDeploymentCommand(command, true);
}

function tokenizeCommand(command: string): string[] {
    const tokens: string[] = [];
    let current = "";
    let quote: "'" | "\"" | undefined;

    const pushCurrent = () => {
        if (current.length > 0) {
            tokens.push(current);
            current = "";
        }
    };

    for (let i = 0; i < command.length; i++) {
        const ch = command[i];

        if (quote) {
            if (ch === quote) {
                quote = undefined;
                continue;
            }

            current += ch;
            continue;
        }

        if (ch === "'" || ch === "\"") {
            quote = ch;
            continue;
        }

        if (/\s/.test(ch)) {
            pushCurrent();
            continue;
        }

        if (ch === "|" || ch === ";" || ch === "&") {
            pushCurrent();
            if (ch === "&" && command[i + 1] === "&") {
                tokens.push("&&");
                i++;
            }
            else if (ch === "|" && command[i + 1] === "|") {
                tokens.push("||");
                i++;
            }
            else {
                tokens.push(ch);
            }
            continue;
        }

        current += ch;
    }

    pushCurrent();
    return tokens;
}

function isCommandSeparatorToken(token: string): boolean {
    return token === "|" || token === ";" || token === "&" || token === "&&" || token === "||";
}

const DotNetOptionsWithValues = new Set([
    "-a",
    "--arch",
    "-c",
    "--configuration",
    "-f",
    "--framework",
    "-o",
    "--output",
    "-r",
    "--runtime",
    "-v",
    "--verbosity",
    "--artifacts-path",
    "--os",
    "--tl",
    "--logger",
    "--filter",
    "--settings",
    "--test-adapter-path",
    "--blame-crash-dump-type",
    "--blame-hang-dump-type",
    "--blame-hang-timeout",
    "--collect",
    "--diag",
    "--environment",
    "--results-directory"
]);

function isDotNetOptionToken(token: string): boolean {
    return token.startsWith("-") || token.startsWith("/");
}

function optionConsumesNextToken(token: string): boolean {
    if (token.includes(":") || token.includes("="))
        return false;

    return DotNetOptionsWithValues.has(token.toLowerCase());
}

function isRedirectionToken(token: string): boolean {
    return /^\d?>/.test(token) || /^<\d?/.test(token);
}

function getDotNetInvocationTargets(command: string, verb: "build" | "test"): Array<string | undefined> {
    const tokens = tokenizeCommand(command);
    const targets: Array<string | undefined> = [];

    for (let i = 0; i < tokens.length - 1; i++) {
        if (!/^dotnet(?:\.exe)?$/i.test(tokens[i]) || tokens[i + 1].toLowerCase() !== verb)
            continue;

        let target: string | undefined;
        let skipNext = false;
        for (let j = i + 2; j < tokens.length; j++) {
            const token = tokens[j];
            if (isCommandSeparatorToken(token))
                break;

            if (skipNext) {
                skipNext = false;
                continue;
            }

            if (isRedirectionToken(token))
                continue;

            if (isDotNetOptionToken(token)) {
                skipNext = optionConsumesNextToken(token);
                continue;
            }

            target = token;
            break;
        }

        targets.push(target);
    }

    return targets;
}

function commandTargetsImplicitAppBuild(command: string, cwd: string | undefined, appRoot: string): boolean {
    const inAppWorkspace = cwd ? isPathInsideOrEqual(cwd, appRoot) : false;
    if (!inAppWorkspace && !commandMentionsPath(command, appRoot))
        return false;

    return getDotNetInvocationTargets(command, "build").some(target => {
        if (!target || target === ".")
            return true;

        const resolvedTarget = path.resolve(cwd ?? appRoot, target);
        return isPathInsideOrEqual(resolvedTarget, appRoot) &&
            path.basename(resolvedTarget).toLowerCase() === path.basename(appRoot).toLowerCase();
    });
}

function commandTargetsSquadDashSelfBuild(command: string, cwd: string | undefined, appRoot: string): boolean {
    if (!commandInvokesDotNetBuild(command))
        return false;

    if (commandTargetsSquadDashProject(command) || commandTargetsSquadDashSolution(command))
        return true;

    return commandTargetsImplicitAppBuild(command, cwd, appRoot);
}

function commandShouldPreserveNativeExitCode(command: string): boolean {
    if (!command.includes("|") || !commandInvokesDotNetBuildOrTest(command))
        return false;

    if (/\$LASTEXITCODE/i.test(command))
        return false;

    return true;
}

function appendPowerShellNativeExitCodeCheck(command: string): string {
    const message = "[SquadDash] Native command failed with exit code ";
    return `${command}; if ($LASTEXITCODE -is [int] -and $LASTEXITCODE -ne 0) { Write-Output (${toPowerShellSingleQuotedLiteral(message)} + $LASTEXITCODE + '.'); exit $LASTEXITCODE }`;
}

export function maybeRewritePowerShellToolArgs(
    toolName: string,
    toolArgs: unknown,
    cwd: string | undefined,
    env: NodeJS.ProcessEnv = process.env,
    fileExists: (filePath: string) => boolean = existsSync
): ToolArgsRewriteResult | undefined {
    if (toolName.toLowerCase() !== "powershell")
        return undefined;

    const args = parseToolArgs(toolArgs);
    const command = typeof args?.command === "string"
        ? args.command.trim()
        : "";
    if (!args || !command)
        return undefined;

    const appRoot = env[PendingRestartDeploymentEnv.appRoot]?.trim();
    const restartRequestPath = env[PendingRestartDeploymentEnv.restartRequestPath]?.trim();
    const restartRequestPending = !!restartRequestPath && fileExists(restartRequestPath);

    let modifiedCommand = command;
    const reasons: string[] = [];

    const inAppWorkspace = appRoot && cwd ? isPathInsideOrEqual(cwd, appRoot) : false;
    const commandInAppContext = !!appRoot && (inAppWorkspace || commandMentionsPath(command, appRoot));

    if (appRoot &&
        commandInAppContext &&
        commandTargetsSquadDashSelfBuild(command, cwd, appRoot) &&
        !commandExplicitlyControlsRunSlotDeployment(command)) {
        modifiedCommand = restartRequestPending
            ? buildPowerShellDeploymentSuppressionCommand(modifiedCommand)
            : buildPowerShellDeploymentEnabledCommand(modifiedCommand);
        reasons.push(restartRequestPending
            ? "restart-request-pending"
            : "self-build-deployment-enabled");
    }

    if (commandShouldPreserveNativeExitCode(command)) {
        modifiedCommand = appendPowerShellNativeExitCodeCheck(modifiedCommand);
        reasons.push("native-exit-code-preserved");
    }

    if (reasons.length === 0)
        return undefined;

    return {
        modifiedArgs: {
            ...args,
            command: modifiedCommand
        },
        reason: reasons.join(","),
        originalCommand: command,
        modifiedCommand
    };
}

export function maybeRewritePendingRestartSelfBuildToolArgs(
    toolName: string,
    toolArgs: unknown,
    cwd: string | undefined,
    env: NodeJS.ProcessEnv = process.env,
    fileExists: (filePath: string) => boolean = existsSync
): ToolArgsRewriteResult | undefined {
    if (toolName.toLowerCase() !== "powershell")
        return undefined;

    const restartRequestPath = env[PendingRestartDeploymentEnv.restartRequestPath]?.trim();
    if (!restartRequestPath || !fileExists(restartRequestPath))
        return undefined;

    const appRoot = env[PendingRestartDeploymentEnv.appRoot]?.trim();
    if (!appRoot)
        return undefined;

    const args = parseToolArgs(toolArgs);
    const command = typeof args?.command === "string"
        ? args.command.trim()
        : "";
    if (!command)
        return undefined;

    if (commandExplicitlyControlsRunSlotDeployment(command))
        return undefined;

    if (!commandTargetsSquadDashSelfBuild(command, cwd, appRoot))
        return undefined;

    const inAppWorkspace = cwd ? isPathInsideOrEqual(cwd, appRoot) : false;
    if (!inAppWorkspace && !commandMentionsPath(command, appRoot))
        return undefined;

    const modifiedCommand = buildPowerShellDeploymentSuppressionCommand(command);
    return {
        modifiedArgs: {
            ...args,
            command: modifiedCommand
        },
        reason: "restart-request-pending",
        originalCommand: command,
        modifiedCommand
    };
}

type SessionAcquireTelemetry = {
    resumed: boolean;
    sessionReuseKind?: string;
    sessionAcquireDurationMs?: number;
    sessionResumeDurationMs?: number;
    sessionCreateDurationMs?: number;
    sessionResumeFailureMessage?: string;
};

const EmptyBackgroundTasks = (): BackgroundTaskSnapshot => ({
    agents: [],
    shells: []
});

function getObjectValue(source: unknown, propertyName: string): unknown {
    if (!source || typeof source !== "object")
        return undefined;

    return (source as Record<string, unknown>)[propertyName];
}

function getStringValue(source: unknown, propertyName: string): string | undefined {
    const value = getObjectValue(source, propertyName);
    return typeof value === "string" && value.trim().length > 0
        ? value.trim()
        : undefined;
}

function getRawStringValue(source: unknown, propertyName: string): string | undefined {
    const value = getObjectValue(source, propertyName);
    return typeof value === "string"
        ? value
        : undefined;
}

function getNumberValue(source: unknown, propertyName: string): number | undefined {
    const value = getObjectValue(source, propertyName);
    return typeof value === "number" && Number.isFinite(value)
        ? value
        : undefined;
}

function getBooleanValue(source: unknown, propertyName: string): boolean | undefined {
    const value = getObjectValue(source, propertyName);
    return typeof value === "boolean"
        ? value
        : undefined;
}

function getEventStringValue(event: unknown, propertyName: string): string | undefined {
    return getStringValue(event, propertyName) ??
        getStringValue(getObjectValue(event, "data"), propertyName);
}

function getEventRawStringValue(event: unknown, propertyName: string): string | undefined {
    return getRawStringValue(event, propertyName) ??
        getRawStringValue(getObjectValue(event, "data"), propertyName);
}

function getEventNumberValue(event: unknown, propertyName: string): number | undefined {
    return getNumberValue(event, propertyName) ??
        getNumberValue(getObjectValue(event, "data"), propertyName);
}

function getEventBooleanValue(event: unknown, propertyName: string): boolean | undefined {
    return getBooleanValue(event, propertyName) ??
        getBooleanValue(getObjectValue(event, "data"), propertyName);
}

function buildToolContext(
    toolName: string,
    args: unknown,
    parentToolCallId?: string
): ActiveToolContext {
    return {
        parentToolCallId,
        toolName,
        args,
        startedAt: new Date().toISOString(),
        description: getStringValue(args, "description"),
        command: getStringValue(args, "command"),
        path: getStringValue(args, "path"),
        intent: getStringValue(args, "intent"),
        skill: getStringValue(args, "skill")
    };
}

function extractBlockText(contents: unknown): string {
    if (!Array.isArray(contents))
        return "";

    const parts: string[] = [];

    for (const block of contents) {
        if (!block || typeof block !== "object")
            continue;

        const type = getStringValue(block, "type");
        if (type !== "text" && type !== "terminal")
            continue;

        const text = getStringValue(block, "text");
        if (text)
            parts.push(text);
    }

    return parts.join("\n\n").trim();
}

function extractToolOutput(result: unknown): string {
    if (!result || typeof result !== "object")
        return "";

    const detailedContent = getStringValue(result, "detailedContent");
    if (detailedContent)
        return detailedContent;

    const blockText = extractBlockText(getObjectValue(result, "contents"));
    if (blockText)
        return blockText;

    return getStringValue(result, "content") ?? "";
}

function extractToolError(error: unknown): string {
    if (!error || typeof error !== "object")
        return "";

    const message = getStringValue(error, "message");
    const code = getStringValue(error, "code");
    if (message && code)
        return `${message}\nCode: ${code}`;

    return message ?? "";
}

function extractAssistantMessageContent(value: unknown): string {
    if (!value || typeof value !== "object")
        return "";

    const content = getStringValue(value, "content");
    if (content)
        return content;

    return getStringValue(getObjectValue(value, "data"), "content") ?? "";
}

function extractThinkingSpeaker(event: unknown): string | undefined {
    return getStringValue(event, "agentName") ??
        getStringValue(event, "speaker") ??
        getStringValue(getObjectValue(event, "speaker"), "name") ??
        getStringValue(getObjectValue(event, "agent"), "name") ??
        getStringValue(getObjectValue(event, "actor"), "name");
}

function extractSubagentError(event: unknown): string | undefined {
    return getEventStringValue(event, "error") ??
        getStringValue(getObjectValue(event, "error"), "message") ??
        getStringValue(getObjectValue(getObjectValue(event, "data"), "error"), "message");
}

function normalizeIdentityKey(value: string | undefined): string {
    if (!value)
        return "";

    return value
        .trim()
        .toLowerCase()
        .replace(/[^a-z0-9]/g, "");
}

function isGenericLooseIdentity(value: string | undefined): boolean {
    const normalized = normalizeIdentityKey(value);
    return normalized.length > 0 && GenericIdentityKeys.has(normalized);
}

function preferSpecificIdentity(primary: string | undefined, fallback: string | undefined): string | undefined {
    if (!isGenericLooseIdentity(primary))
        return primary;

    if (!isGenericLooseIdentity(fallback))
        return fallback;

    return primary ?? fallback;
}

function normalizeSubagentLifecycle(event: unknown): SubagentLifecycleInfo | null {
    const agentName = getEventStringValue(event, "agentName");
    if (!agentName)
        return null;

    return {
        toolCallId: getEventStringValue(event, "toolCallId"),
        agentId: getEventStringValue(event, "agentId"),
        agentName,
        agentDisplayName: getEventStringValue(event, "agentDisplayName"),
        agentDescription: getEventStringValue(event, "agentDescription"),
        prompt: getEventStringValue(event, "prompt"),
        error: extractSubagentError(event),
        model: getEventStringValue(event, "model"),
        totalToolCalls: getEventNumberValue(event, "totalToolCalls"),
        totalTokens: getEventNumberValue(event, "totalTokens"),
        durationMs: getEventNumberValue(event, "durationMs")
    };
}

function normalizeBackgroundAgent(value: unknown): BackgroundAgentInfo | null {
    const agentId = getStringValue(value, "agentId");
    if (!agentId)
        return null;

    return {
        agentId,
        toolCallId: getStringValue(value, "toolCallId"),
        agentType: getStringValue(value, "agentType"),
        status: getStringValue(value, "status"),
        description: getStringValue(value, "description"),
        prompt: getStringValue(value, "prompt"),
        error: getStringValue(value, "error"),
        startedAt: getStringValue(value, "startedAt"),
        completedAt: getStringValue(value, "completedAt"),
        latestResponse: getStringValue(value, "latestResponse"),
        latestIntent: getStringValue(value, "latestIntent"),
        recentActivity: Array.isArray(getObjectValue(value, "recentActivity"))
            ? (getObjectValue(value, "recentActivity") as unknown[])
                .map(normalizeProgressLine)
                .filter((entry): entry is string => entry !== null)
            : undefined,
        agentName: getStringValue(value, "agentName"),
        agentDisplayName: getStringValue(value, "agentDisplayName"),
        model: getStringValue(value, "model"),
        totalToolCalls: getNumberValue(value, "totalToolCalls"),
        totalInputTokens: getNumberValue(value, "totalInputTokens"),
        totalOutputTokens: getNumberValue(value, "totalOutputTokens")
    };
}

function normalizeBackgroundShell(value: unknown): BackgroundShellInfo | null {
    const shellId = getStringValue(value, "shellId");
    if (!shellId)
        return null;

    return {
        shellId,
        status: getStringValue(value, "status"),
        description: getStringValue(value, "description"),
        command: getStringValue(value, "command"),
        startedAt: getStringValue(value, "startedAt"),
        completedAt: getStringValue(value, "completedAt"),
        recentOutput: getStringValue(value, "recentOutput"),
        pid: getNumberValue(value, "pid")
    };
}

function normalizeBackgroundTasks(event: unknown): BackgroundTaskSnapshot {
    const backgroundTasks = getObjectValue(event, "backgroundTasks");
    const agentsRaw = getObjectValue(backgroundTasks, "agents");
    const shellsRaw = getObjectValue(backgroundTasks, "shells");

    return {
        agents: Array.isArray(agentsRaw)
            ? agentsRaw
                .map(normalizeBackgroundAgent)
                .filter((value): value is BackgroundAgentInfo => value !== null)
            : [],
        shells: Array.isArray(shellsRaw)
            ? shellsRaw
                .map(normalizeBackgroundShell)
                .filter((value): value is BackgroundShellInfo => value !== null)
            : []
    };
}

function cloneBackgroundTasks(tasks: BackgroundTaskSnapshot): BackgroundTaskSnapshot {
    return {
        agents: tasks.agents.map(agent => ({
            ...agent,
            recentActivity: agent.recentActivity ? [...agent.recentActivity] : undefined
        })),
        shells: tasks.shells.map(shell => ({ ...shell }))
    };
}

function stringArrayEqual(left?: string[], right?: string[]): boolean {
    if (!left || left.length === 0)
        return !right || right.length === 0;

    if (!right || left.length !== right.length)
        return false;

    for (let i = 0; i < left.length; i++) {
        if (left[i] !== right[i])
            return false;
    }

    return true;
}

function backgroundTasksContainTask(tasks: BackgroundTaskSnapshot, taskId: string): boolean {
    return tasks.agents.some(agent =>
        agent.agentId === taskId ||
        agent.toolCallId === taskId) ||
        tasks.shells.some(shell => shell.shellId === taskId);
}

function backgroundTaskCancelIds(
    tasks: BackgroundTaskSnapshot,
    taskId: string,
    backgroundTaskIdsByToolCallId?: Map<string, string>
): string[] {
    const ids: string[] = [];
    const add = (value?: string) => {
        const normalized = value?.trim();
        if (normalized && !ids.includes(normalized))
            ids.push(normalized);
    };

    add(taskId);
    add(backgroundTaskIdsByToolCallId?.get(taskId));
    for (const agent of tasks.agents) {
        if (agent.agentId === taskId || agent.toolCallId === taskId) {
            add(agent.toolCallId);
            add(agent.agentId);
        }
    }

    return ids;
}

function agentInfoEqual(left: BackgroundAgentInfo, right: BackgroundAgentInfo): boolean {
    return left.agentId === right.agentId &&
        left.toolCallId === right.toolCallId &&
        left.agentType === right.agentType &&
        left.status === right.status &&
        left.description === right.description &&
        left.prompt === right.prompt &&
        left.error === right.error &&
        left.startedAt === right.startedAt &&
        left.latestIntent === right.latestIntent &&
        left.latestResponse === right.latestResponse &&
        left.completedAt === right.completedAt &&
        left.agentName === right.agentName &&
        left.agentDisplayName === right.agentDisplayName &&
        left.model === right.model &&
        left.totalToolCalls === right.totalToolCalls &&
        left.totalInputTokens === right.totalInputTokens &&
        left.totalOutputTokens === right.totalOutputTokens &&
        stringArrayEqual(left.recentActivity, right.recentActivity);
}

function shellInfoEqual(left: BackgroundShellInfo, right: BackgroundShellInfo): boolean {
    return left.shellId === right.shellId &&
        left.status === right.status &&
        left.description === right.description &&
        left.command === right.command &&
        left.startedAt === right.startedAt &&
        left.completedAt === right.completedAt &&
        left.recentOutput === right.recentOutput &&
        left.pid === right.pid;
}

function backgroundTasksEqual(left: BackgroundTaskSnapshot, right: BackgroundTaskSnapshot): boolean {
    if (left.agents.length !== right.agents.length || left.shells.length !== right.shells.length)
        return false;

    for (let i = 0; i < left.agents.length; i++) {
        if (!agentInfoEqual(left.agents[i], right.agents[i]))
            return false;
    }

    for (let i = 0; i < left.shells.length; i++) {
        if (!shellInfoEqual(left.shells[i], right.shells[i]))
            return false;
    }

    return true;
}

function extractTaskCompletionSummary(event: unknown): string | undefined {
    return getStringValue(event, "summary") ??
        getStringValue(getObjectValue(event, "data"), "summary");
}

function extractParentToolCallId(event: unknown): string | undefined {
    return getEventStringValue(event, "parentToolCallId");
}

function extractAssistantReasoningText(event: unknown): string | undefined {
    return getEventStringValue(event, "reasoningText");
}

function toIsoTimestamp(value: unknown): string | undefined {
    if (typeof value !== "number" || !Number.isFinite(value))
        return undefined;

    return new Date(value).toISOString();
}

function normalizeProgressLine(value: unknown): string | null {
    const message = getStringValue(value, "message");
    if (message)
        return message;

    return null;
}

function normalizeProgressLines(progress: unknown): string[] {
    const recentActivity = getObjectValue(progress, "recentActivity");
    if (!Array.isArray(recentActivity))
        return [];

    return recentActivity
        .map(normalizeProgressLine)
        .filter((value): value is string => value !== null);
}

function isRecoverableSessionReset(message: string | undefined): boolean {
    if (!message)
        return false;

    return message.includes("CAPIError: 400") ||
        (message.includes("CAPIError") && message.includes("Bad Request"));
}

function extractErrorMessage(error: unknown): string {
    if (error instanceof Error)
        return error.message;

    return String(error);
}

function summarizeErrorForDiagnostics(error: unknown, depth = 0): Record<string, unknown> {
    if (depth > 2)
        return { truncated: true };

    if (!(error instanceof Error)) {
        return {
            type: typeof error,
            value: String(error)
        };
    }

    const errorWithFields = error as Error & {
        cause?: unknown;
        code?: unknown;
        status?: unknown;
        type?: unknown;
        requestID?: unknown;
        request_id?: unknown;
        error?: unknown;
    };

    const summary: Record<string, unknown> = {
        name: error.name,
        constructorName: error.constructor?.name,
        message: error.message,
        stackTop: error.stack?.split(/\r?\n/).slice(0, 6).join("\n")
    };

    for (const key of ["code", "status", "type", "requestID", "request_id"] as const) {
        const value = errorWithFields[key];
        if (value !== undefined)
            summary[key] = value;
    }

    if (errorWithFields.error !== undefined) {
        try {
            summary.error = JSON.stringify(errorWithFields.error).slice(0, 2000);
        } catch {
            summary.error = String(errorWithFields.error);
        }
    }

    if (errorWithFields.cause)
        summary.cause = summarizeErrorForDiagnostics(errorWithFields.cause, depth + 1);

    return summary;
}

function normalizeAgentHandle(value: string): string {
    return value.trim().replace(/^@+/, "").toLowerCase();
}

function rememberTaskToolLaunch(
    state: SessionState,
    toolCallId: string,
    toolName: string,
    args: unknown
) {
    if (!toolCallId || toolName !== "task")
        return;

    const taskName = getStringValue(args, "name");
    if (taskName)
        state.backgroundTaskIdsByToolCallId.set(toolCallId, taskName);
}

function buildNamedAgentHiddenContext(targetAgent: string, charterContent?: string): string {
    const normalizedHandle = normalizeAgentHandle(targetAgent);
    const lines = [
        `You are @${normalizedHandle}. SquadDash has launched you directly for a quick-reply task.`,
        "",
        "Treat this prompt as the complete task brief. Do not depend on prior session memory unless it appears below.",
        "Keep the work scoped to the selected quick reply and source context unless that context explicitly asks for a full sweep.",
        "Begin working immediately. Do not narrate your launch."
    ];

    if (charterContent?.trim())
        lines.push("", "## Agent Charter", charterContent.trim());

    return lines.join("\n");
}

export function buildNamedAgentExecutionPrompt(
    selectedOption: string,
    targetAgent: string,
    handoffContext?: string,
    charterContent?: string
): string {
    const normalizedHandle = normalizeAgentHandle(targetAgent);
    const trimmedOption = selectedOption.trim();
    const lines = [
        `You are @${normalizedHandle}. SquadDash has launched you directly for a quick-reply task.`,
        `Visible quick reply selected by the user: "${trimmedOption}"`,
        "",
        "Treat this prompt as the complete task brief. Do not depend on prior session memory unless it appears below.",
        "Use the handoff/source turn below as authoritative scope for references such as \"this\", \"that\", \"the 3 optimizations\", \"run verification\", or named files.",
        "Keep the work scoped to the selected quick reply and source context unless that context explicitly asks for a full sweep.",
        "Begin working immediately. Do not narrate your launch."
    ];

    if (charterContent?.trim())
        lines.push("", "## Agent Charter", charterContent.trim());

    if (handoffContext?.trim())
        lines.push("", "## Quick-Reply Handoff Context", handoffContext.trim());

    lines.push("", "## Selected User Action", trimmedOption);
    return lines.join("\n");
}

export function buildNamedAgentPrompt(request: Pick<SquadNamedAgentRequest, "selectedOption" | "targetAgent" | "handoffContext" | "charterContent">): string {
    const selectedOption = request.selectedOption.trim();
    const sections = [
        selectedOption,
        "",
        "## Named Agent Launch Context",
        buildNamedAgentHiddenContext(request.targetAgent, request.charterContent)
    ];

    const handoffContext = request.handoffContext?.trim();
    if (handoffContext) {
        sections.push(
            "",
            "## Quick-Reply Handoff Context",
            handoffContext,
            "",
            "Use this handoff context to resolve references, pronouns, and intended scope in the selected quick reply. Carry out the selected quick reply now. Do not ask the user to restate context unless this handoff is empty or contradictory."
        );
    }

    return sections.join("\n");
}

type LegacyPermissionApproval = { kind: "approved" };
type PermissionApprovalKind = "approved" | "approve-once";

export function approvePermissionRequest(): LegacyPermissionApproval {
    return { kind: resolveRuntimePermissionApprovalKind() } as unknown as LegacyPermissionApproval;
}

export function resolvePermissionApprovalKind(copilotVersion = resolveCopilotPackageVersion()): PermissionApprovalKind {
    return isCopilotUserPermissionDecisionVersion(copilotVersion)
        ? "approve-once"
        : "approved";
}

function resolveRuntimePermissionApprovalKind(): PermissionApprovalKind {
    return resolveCopilotSchemaApprovalKind() ??
        resolvePermissionApprovalKind(resolveCopilotPackageVersion());
}

export function resolvePermissionApprovalKindFromSchema(schemaText: string): PermissionApprovalKind | undefined {
    try {
        const schema = JSON.parse(schemaText);
        const resultSchema = schema?.session?.permissions?.handlePendingPermissionRequest?.params?.properties?.result;
        const constValues = collectSchemaConstValues(resultSchema, schema);

        if (constValues.has("approve-once"))
            return "approve-once";

        if (constValues.has("approved"))
            return "approved";
    }
    catch {
    }

    return undefined;
}

function isCopilotUserPermissionDecisionVersion(version?: string): boolean {
    if (!version)
        return false;

    const parsed = version.split(/[.-]/).map(part => Number.parseInt(part, 10));
    const major = Number.isFinite(parsed[0]) ? parsed[0] : 0;
    const minor = Number.isFinite(parsed[1]) ? parsed[1] : 0;
    const patch = Number.isFinite(parsed[2]) ? parsed[2] : 0;

    return major > 1 ||
        (major === 1 && (minor > 0 || patch >= 36));
}

function resolveCopilotPackageVersion(): string | undefined {
    try {
        const packageRoot = resolveCopilotPackageRoot();
        if (!packageRoot)
            return undefined;

        const packageJsonPath = path.join(packageRoot, "package.json");
        const packageJson = JSON.parse(readFileSync(packageJsonPath, "utf8")) as { version?: unknown };
        return typeof packageJson.version === "string"
            ? packageJson.version
            : undefined;
    }
    catch {
        return undefined;
    }
}

function resolveCopilotSchemaApprovalKind(): PermissionApprovalKind | undefined {
    try {
        const packageRoot = resolveCopilotPackageRoot();
        if (!packageRoot)
            return undefined;

        const schemaPath = path.join(packageRoot, "schemas", "api.schema.json");
        if (!existsSync(schemaPath))
            return undefined;

        return resolvePermissionApprovalKindFromSchema(readFileSync(schemaPath, "utf8"));
    }
    catch {
        return undefined;
    }
}

function resolveCopilotPackageRoot(): string | undefined {
    const copilotSdkRoot = findPackageRoot(fileURLToPath(import.meta.resolve("@github/copilot-sdk")));
    return copilotSdkRoot
        ? findCopilotPackageRoot(copilotSdkRoot)
        : undefined;
}

function findPackageRoot(startPath: string): string | undefined {
    let directory = path.dirname(startPath);
    while (true) {
        if (existsSync(path.join(directory, "package.json")))
            return directory;

        const parent = path.dirname(directory);
        if (parent === directory)
            return undefined;

        directory = parent;
    }
}

function findCopilotPackageRoot(copilotSdkRoot: string): string | undefined {
    const candidates = [
        path.join(copilotSdkRoot, "node_modules", "@github", "copilot"),
        path.join(path.dirname(copilotSdkRoot), "copilot")
    ];

    for (const candidate of candidates) {
        if (existsSync(path.join(candidate, "package.json")))
            return candidate;
    }

    let directory = path.dirname(copilotSdkRoot);
    while (true) {
        const candidate = path.join(directory, "node_modules", "@github", "copilot");
        if (existsSync(path.join(candidate, "package.json")))
            return candidate;

        const parent = path.dirname(directory);
        if (parent === directory)
            return undefined;

        directory = parent;
    }
}

function collectSchemaConstValues(node: unknown, root: unknown, values = new Set<string>(), seenRefs = new Set<string>()): Set<string> {
    if (!node || typeof node !== "object")
        return values;

    const record = node as Record<string, unknown>;
    if (typeof record.const === "string")
        values.add(record.const);

    if (typeof record.$ref === "string" && record.$ref.startsWith("#/") && !seenRefs.has(record.$ref)) {
        seenRefs.add(record.$ref);
        collectSchemaConstValues(resolveJsonPointer(root, record.$ref), root, values, seenRefs);
    }

    for (const value of Object.values(record)) {
        if (Array.isArray(value)) {
            for (const item of value)
                collectSchemaConstValues(item, root, values, seenRefs);
        }
        else if (value && typeof value === "object") {
            collectSchemaConstValues(value, root, values, seenRefs);
        }
    }

    return values;
}

function resolveJsonPointer(root: unknown, pointer: string): unknown {
    return pointer
        .slice(2)
        .split("/")
        .reduce<unknown>((current, part) => {
            if (!current || typeof current !== "object")
                return undefined;

            const key = part.replace(/~1/g, "/").replace(/~0/g, "~");
            return (current as Record<string, unknown>)[key];
        }, root);
}

function buildDelegationHiddenContext(selectedOption: string, targetAgent: string): string {
    const normalizedTargetAgent = normalizeAgentHandle(targetAgent);
    const trimmedOption = selectedOption.trim();

    return [
        "SquadDash bridge instruction: this turn is a named-agent delegation commit triggered by a quick reply.",
        `The user clicked the quick reply label: "${trimmedOption}".`,
        `The quick reply explicitly targets @${normalizedTargetAgent}.`,
        "You are continuing inside the same coordinator session that produced the quick reply.",
        "Use the current session context, recent turn history, prior agent reports, tool results, and workspace files as the authoritative handoff context.",
        "You may think and inspect context before launching if needed.",
        `You must launch @${normalizedTargetAgent} using the native subagent/tool path instead of answering inline in the coordinator voice.`,
        "Do not narrate or promise a handoff unless the launch actually happens.",
        "If launching the target agent is impossible in this exact session, explain the concrete blocker instead of silently doing the work yourself."
    ].join("\n");
}

export class SquadBridgeService {
    private readonly bridgeHandlers: SquadBridgeHandlers;
    private client: SquadClient | null = null;
    private clientCwd: string | null = null;
    private readonly sessions = new Map<string, SessionState>();

    public constructor(bridgeHandlers: SquadBridgeHandlers = {}) {
        this.bridgeHandlers = bridgeHandlers;
    }

    public async runPrompt(
        prompt: string,
        handlers: SquadRunHandlers,
        request: SquadPromptRequest | string
    ) {
        const options = typeof request === "string"
            ? { cwd: request }
            : request;

        await this.runSessionRequest(prompt, handlers, options);
    }

    public async runDelegation(
        request: SquadDelegationRequest,
        handlers: SquadRunHandlers
    ) {
        await this.runSessionRequest(
            request.selectedOption,
            handlers,
            {
                cwd: request.cwd,
                sessionId: request.sessionId,
                configDir: request.configDir,
                model: request.model,
                requireSameSession: true
            },
            buildDelegationHiddenContext(request.selectedOption, request.targetAgent));
    }

    public async runNamedAgent(
        request: SquadNamedAgentRequest,
        handlers: SquadRunHandlers
    ) {
        await this.runSessionRequest(
            buildNamedAgentPrompt(request),
            handlers,
            {
                cwd: request.cwd,
                sessionId: request.namedAgentSessionId,
                configDir: request.configDir,
                model: request.model,
                requireSameSession: false
            });
    }

    private async runSessionRequest(
        prompt: string,
        handlers: SquadRunHandlers,
        options: SquadSessionRequest,
        hiddenAdditionalContext?: string
    ) {
        const trimmedPrompt = prompt.trim();
        if (!trimmedPrompt)
            throw new Error("Prompt cannot be empty.");

        const { state, sessionReady } = await this.getOrCreateSession(options);
        bridgeDiagnostic("runSessionRequest:session-ready", {
            sessionId: state.session.sessionId,
            resumed: sessionReady.resumed,
            sessionReuseKind: sessionReady.sessionReuseKind,
            sessionAcquireDurationMs: sessionReady.sessionAcquireDurationMs
        });
        bridgeDiagnostic("runSessionRequest:refresh-background:start", {
            sessionId: state.session.sessionId
        });
        await this.refreshBackgroundTasks(state);
        bridgeDiagnostic("runSessionRequest:refresh-background:done", {
            sessionId: state.session.sessionId,
            backgroundAgentCount: state.backgroundTasks.agents.length,
            backgroundShellCount: state.backgroundTasks.shells.length
        });
        if (state.currentRequest)
            throw new Error("A prompt is already running for this Squad session.");

        const requestContext: RequestContext = {
            aborted: false,
            handlers,
            sawMessageDelta: false,
            streamedMessageContent: "",
            lastAssistantMessageContent: state.lastAssistantMessageContent,
            hiddenAdditionalContext
        };

        state.currentRequest = requestContext;

        handlers.onSessionReady?.(this.buildSessionReadyInfo(state, sessionReady));

        if (state.backgroundTasks.agents.length > 0 || state.backgroundTasks.shells.length > 0) {
            this.bridgeHandlers.onBackgroundTasksChanged?.(
                state.session.sessionId,
                cloneBackgroundTasks(state.backgroundTasks));
        }

        let requestSubmitted = false;

        try {
            requestSubmitted = true;
            bridgeDiagnostic("runSessionRequest:sendAndWait:start", {
                sessionId: state.session.sessionId,
                promptChars: trimmedPrompt.length,
                model: options.model ?? "(default)"
            });
            const finalMessage = state.session.sendAndWait
                ? await state.session.sendAndWait({ prompt: trimmedPrompt }, SessionIdleTimeoutMs)
                : await this.client!.sendAndWait(state.session, { prompt: trimmedPrompt }, SessionIdleTimeoutMs);
            bridgeDiagnostic("runSessionRequest:sendAndWait:done", {
                sessionId: state.session.sessionId
            });

            if (requestContext.aborted) {
                handlers.onAborted?.();
                return;
            }

            const finalContent =
                extractAssistantMessageContent(finalMessage) ||
                requestContext.lastAssistantMessageContent ||
                state.lastAssistantMessageContent;
            const responseContent = requestContext.streamedMessageContent || finalContent;
            const normalizedContent = normalizeAssistantResponseContent(responseContent);
            if (normalizedContent)
                handlers.onDelta?.(normalizedContent);

            handlers.onDone?.({ kind: "completed" });
        }
        catch (error) {
            bridgeDiagnostic("runSessionRequest:sendAndWait:error", {
                sessionId: state.session.sessionId,
                promptChars: trimmedPrompt.length,
                model: options.model ?? "(default)",
                error: summarizeErrorForDiagnostics(error)
            });

            if (requestContext.aborted) {
                handlers.onAborted?.();
                return;
            }

            if (options.sessionId && isRecoverableSessionReset(extractErrorMessage(error)))
                await this.disposeSession(state.session.sessionId);

            throw error;
        }
        finally {
            if (requestSubmitted)
                state.completedPromptCount++;

            if (state.currentRequest === requestContext)
                state.currentRequest = undefined;
        }
    }

    public async abortPrompt(sessionId?: string): Promise<boolean> {
        const state = sessionId
            ? this.sessions.get(sessionId)
            : Array.from(this.sessions.values()).find(value => value.currentRequest !== undefined);

        if (!state?.currentRequest)
            return false;

        state.currentRequest.aborted = true;
        await state.session.abort?.();
        return true;
    }

    public async cancelBackgroundTask(taskId: string, sessionId?: string): Promise<boolean> {
        const normalizedTaskId = taskId.trim();
        if (!normalizedTaskId)
            return false;

        const allStates = Array.from(this.sessions.values());
        const preferredState = sessionId ? this.sessions.get(sessionId) : undefined;
        const matchingStates = allStates.filter(value =>
            value !== preferredState &&
            backgroundTasksContainTask(value.backgroundTasks, normalizedTaskId));
        const fallbackStates = allStates.filter(value =>
            value !== preferredState &&
            !matchingStates.includes(value));
        const candidates = [
            ...(preferredState ? [preferredState] : []),
            ...matchingStates,
            ...fallbackStates
        ];

        for (const state of candidates) {
            const backgroundTaskSession = state.session as unknown as {
                cancelBackgroundTask?: (taskId: string) => Promise<boolean>;
            };

            if (!backgroundTaskSession.cancelBackgroundTask)
                continue;

            for (const cancelId of backgroundTaskCancelIds(
                state.backgroundTasks,
                normalizedTaskId,
                state.backgroundTaskIdsByToolCallId)) {
                const cancelled = await backgroundTaskSession.cancelBackgroundTask(cancelId).catch(err => { console.error("cancelBackgroundTask failed:", err); return false; });
                await this.refreshBackgroundTasks(state).catch(err => { console.error("refreshBackgroundTasks failed:", err); return undefined; });
                if (cancelled)
                    return true;
            }
        }

        return false;
    }

    public describeBackgroundCancelState(taskId: string, sessionId?: string): string {
        const normalizedTaskId = taskId.trim();
        if (!normalizedTaskId)
            return "requested=(empty)";

        const allStates = Array.from(this.sessions.values());
        const preferredState = sessionId ? this.sessions.get(sessionId) : undefined;
        const matchingStates = allStates.filter(value =>
            value !== preferredState &&
            backgroundTasksContainTask(value.backgroundTasks, normalizedTaskId));
        const fallbackStates = allStates.filter(value =>
            value !== preferredState &&
            !matchingStates.includes(value));
        const candidates = [
            ...(preferredState ? [preferredState] : []),
            ...matchingStates,
            ...fallbackStates
        ];

        const stateSummaries = candidates.map(state => {
            const mappedTaskId = state.backgroundTaskIdsByToolCallId?.get(normalizedTaskId);
            const matchingAgents = state.backgroundTasks.agents
                .filter(agent => agent.agentId === normalizedTaskId || agent.toolCallId === normalizedTaskId)
                .map(agent => `${agent.agentId || "(no-agent-id)"}/${agent.toolCallId || "(no-tool-call-id)"}`)
                .join(",");
            const matchingShells = state.backgroundTasks.shells
                .filter(shell => shell.shellId === normalizedTaskId)
                .map(shell => shell.shellId)
                .join(",");

            return [
                `session=${state.session.sessionId ?? "(unknown)"}`,
                `agents=${state.backgroundTasks.agents.length}`,
                `shells=${state.backgroundTasks.shells.length}`,
                `mappedTaskId=${mappedTaskId ?? "(none)"}`,
                `matchingAgents=${matchingAgents || "(none)"}`,
                `matchingShells=${matchingShells || "(none)"}`
            ].join(" ");
        });

        return [
            `requested=${normalizedTaskId}`,
            `preferred=${sessionId?.trim() || "(auto)"}`,
            `candidateSessions=${candidates.length}`,
            ...stateSummaries
        ].join("; ");
    }

    public async shutdown(): Promise<void> {
        const sessionIds = Array.from(this.sessions.keys());
        for (const sessionId of sessionIds)
            await this.disposeSession(sessionId);

        if (this.client !== null) {
            await this.client.disconnect();
            this.client = null;
            this.clientCwd = null;
        }
    }

    private mergeSubagentInfo(
        state: SessionState,
        subagent: SubagentLifecycleInfo
    ): SubagentLifecycleInfo {
        const toolCallId = subagent.toolCallId?.trim();
        if (!toolCallId)
            return subagent;

        const existing = state.subagentsByToolCallId.get(toolCallId);
        const merged: SubagentLifecycleInfo = {
            toolCallId,
            agentId: subagent.agentId ?? existing?.agentId,
            agentName: preferSpecificIdentity(subagent.agentName, existing?.agentName) ?? "agent",
            agentDisplayName: preferSpecificIdentity(subagent.agentDisplayName, existing?.agentDisplayName),
            agentDescription: subagent.agentDescription ?? existing?.agentDescription,
            prompt: subagent.prompt ?? existing?.prompt,
            error: subagent.error ?? existing?.error,
            model: subagent.model ?? existing?.model,
            totalToolCalls: subagent.totalToolCalls ?? existing?.totalToolCalls,
            totalTokens: subagent.totalTokens ?? existing?.totalTokens,
            durationMs: subagent.durationMs ?? existing?.durationMs
        };

        state.subagentsByToolCallId.set(toolCallId, merged);
        return merged;
    }

    private buildSubagentTranscriptInfo(
        state: SessionState,
        parentToolCallId: string,
        fields: Omit<SubagentTranscriptInfo, "parentToolCallId"> = {}
    ): SubagentTranscriptInfo {
        const known = state.subagentsByToolCallId.get(parentToolCallId);
        return {
            parentToolCallId,
            agentId: fields.agentId ?? known?.agentId,
            agentName: fields.agentName ?? known?.agentName,
            agentDisplayName: fields.agentDisplayName ?? known?.agentDisplayName,
            agentDescription: fields.agentDescription ?? known?.agentDescription,
            text: fields.text,
            reasoningText: fields.reasoningText
        };
    }

    private recordSubagentReasoningDelta(
        state: SessionState,
        parentToolCallId: string,
        text: string
    ): void {
        const existing = state.subagentReasoningByToolCallId.get(parentToolCallId) ?? "";
        state.subagentReasoningByToolCallId.set(parentToolCallId, existing + text);
    }

    private takeUnstreamedSubagentFinalReasoning(
        state: SessionState,
        parentToolCallId: string,
        reasoningText: string | undefined
    ): string | undefined {
        const streamed = state.subagentReasoningByToolCallId.get(parentToolCallId);
        if (streamed !== undefined)
            state.subagentReasoningByToolCallId.delete(parentToolCallId);

        if (!reasoningText)
            return undefined;
        if (streamed === undefined)
            return reasoningText;
        if (reasoningText.startsWith(streamed)) {
            const suffix = reasoningText.slice(streamed.length);
            return suffix.trim().length > 0 ? suffix : undefined;
        }
        return undefined;
    }

    private async refreshBackgroundTasks(state: SessionState): Promise<void> {
        const nextTasks = await this.loadBackgroundTasks(state);
        if (backgroundTasksEqual(state.backgroundTasks, nextTasks))
            return;

        state.backgroundTasks = nextTasks;
        this.bridgeHandlers.onBackgroundTasksChanged?.(
            state.session.sessionId,
            cloneBackgroundTasks(nextTasks));
    }

    private async loadBackgroundTasks(state: SessionState): Promise<BackgroundTaskSnapshot> {
        let tasks: unknown[] = [];
        const backgroundTaskSession = state.session as unknown as {
            getBackgroundTasks?: () => Promise<unknown[]>;
            getBackgroundTaskProgress?: (task: unknown) => Promise<unknown>;
        };
        try {
            if (!backgroundTaskSession.getBackgroundTasks)
                return EmptyBackgroundTasks();

            tasks = await backgroundTaskSession.getBackgroundTasks();
        }
        catch {
            return EmptyBackgroundTasks();
        }

        const agents: BackgroundAgentInfo[] = [];
        const shells: BackgroundShellInfo[] = [];

        // Fetch all progress data in parallel rather than sequentially.
        const progressResults = await Promise.all(
            tasks.map(task =>
                backgroundTaskSession.getBackgroundTaskProgress
                    ? backgroundTaskSession.getBackgroundTaskProgress(task).catch(err => { console.error("getBackgroundTaskProgress failed:", err); return undefined; })
                    : Promise.resolve(undefined)
            )
        );

        for (let taskIndex = 0; taskIndex < tasks.length; taskIndex++) {
            const task = tasks[taskIndex];
            const progress = progressResults[taskIndex];
            const taskType = getStringValue(task, "type");
            if (taskType === "agent") {
                const toolCallId = getStringValue(task, "toolCallId");
                const known = toolCallId
                    ? state.subagentsByToolCallId.get(toolCallId)
                    : undefined;

                const agent: BackgroundAgentInfo = {
                    agentId: getStringValue(task, "id") ?? "",
                    toolCallId,
                    agentType: getStringValue(task, "agentType"),
                    status: getStringValue(task, "status"),
                    description: getStringValue(task, "description"),
                    prompt: getStringValue(task, "prompt"),
                    error: getStringValue(task, "error"),
                    startedAt: toIsoTimestamp(getNumberValue(task, "startedAt")),
                    completedAt: toIsoTimestamp(getNumberValue(task, "completedAt")),
                    latestResponse: getStringValue(task, "latestResponse"),
                    latestIntent: getStringValue(progress, "latestIntent") ?? getStringValue(getObjectValue(task, "progress"), "latestIntent"),
                    recentActivity: normalizeProgressLines(progress),
                    agentName: preferSpecificIdentity(
                        getStringValue(task, "agentName"),
                        known?.agentName),
                    agentDisplayName: preferSpecificIdentity(
                        getStringValue(task, "agentDisplayName"),
                        known?.agentDisplayName),
                    model: getStringValue(getObjectValue(task, "progress"), "resolvedModel"),
                    totalToolCalls: getNumberValue(getObjectValue(task, "progress"), "toolCallsCompleted"),
                    totalInputTokens: getNumberValue(getObjectValue(task, "progress"), "totalInputTokens"),
                    totalOutputTokens: getNumberValue(getObjectValue(task, "progress"), "totalOutputTokens")
                };

                if (toolCallId && agent.agentId) {
                    this.mergeSubagentInfo(state, {
                        toolCallId,
                        agentId: agent.agentId,
                        agentName: preferSpecificIdentity(known?.agentName, undefined) ?? agent.agentId ?? agent.agentType ?? "agent",
                        agentDisplayName: preferSpecificIdentity(known?.agentDisplayName, undefined),
                        agentDescription: known?.agentDescription ?? agent.description,
                        prompt: agent.prompt
                    });
                }

                if (agent.agentId)
                    agents.push(agent);
                continue;
            }

            if (taskType === "shell") {
                const shell: BackgroundShellInfo = {
                    shellId: getStringValue(task, "id") ?? "",
                    status: getStringValue(task, "status"),
                    description: getStringValue(task, "description"),
                    command: getStringValue(task, "command"),
                    startedAt: toIsoTimestamp(getNumberValue(task, "startedAt")),
                    completedAt: toIsoTimestamp(getNumberValue(task, "completedAt")),
                    recentOutput: getStringValue(progress, "recentOutput"),
                    pid: getNumberValue(progress, "pid")
                };

                if (shell.shellId)
                    shells.push(shell);
            }
        }

        return { agents, shells };
    }

    private async ensureClient(cwd: string): Promise<SquadClient> {
        if (this.client && this.clientCwd === cwd)
            return this.client;

        await this.shutdown();

        this.client = new SquadClient({ cwd });
        await this.client.connect();
        this.clientCwd = cwd;
        return this.client;
    }

    private async getOrCreateSession(options: SquadSessionRequest): Promise<{ state: SessionState; sessionReady: SessionAcquireTelemetry; }> {
        bridgeDiagnostic("getOrCreateSession:start", {
            cwd: options.cwd,
            sessionId: options.sessionId ?? "(new)",
            model: options.model ?? "(default)"
        });
        const client = await this.ensureClient(options.cwd);
        const acquireStartedAt = Date.now();

        if (options.sessionId) {
            const existingState = this.sessions.get(options.sessionId);
            if (existingState) {
                bridgeDiagnostic("getOrCreateSession:bridge-cache", {
                    sessionId: options.sessionId,
                    durationMs: Date.now() - acquireStartedAt
                });
                return {
                    state: existingState,
                    sessionReady: {
                        resumed: true,
                        sessionReuseKind: "bridge_cache",
                        sessionAcquireDurationMs: Date.now() - acquireStartedAt
                    }
                };
            }
        }

        let stateRef: SessionState | undefined;
        const customProvider = buildCustomProviderFromEnv();
        const sessionConfig = {
            onPermissionRequest: async () => approvePermissionRequest(),
            streaming: true,
            workingDirectory: options.cwd,
            configDir: options.configDir,
            model: resolveSessionModel(options.model),
            provider: customProvider,
            hooks: {
                onPreToolUse: (input: { toolName: string; toolArgs: unknown; cwd?: string }) => {
                    const rewrite = maybeRewritePowerShellToolArgs(
                        input.toolName,
                        input.toolArgs,
                        input.cwd,
                        process.env,
                        existsSync);
                    if (!rewrite)
                        return undefined;

                    stateRef?.currentRequest?.handlers.onToolArgsRewritten?.({
                        toolName: input.toolName,
                        reason: rewrite.reason,
                        originalCommand: rewrite.originalCommand,
                        modifiedCommand: rewrite.modifiedCommand
                    });

                    return {
                        permissionDecision: "allow" as const,
                        modifiedArgs: rewrite.modifiedArgs,
                        additionalContext: `SquadDash rewrote this PowerShell tool call before execution (${rewrite.reason}).`
                    };
                },
                onUserPromptSubmitted: () => {
                    const additionalContext = stateRef?.currentRequest?.hiddenAdditionalContext;
                    return additionalContext
                        ? { additionalContext }
                        : undefined;
                }
            }
        };

        let resumed = false;
        let session: SquadSession;
        let sessionReuseKind: string | undefined;
        let sessionResumeDurationMs: number | undefined;
        let sessionCreateDurationMs: number | undefined;
        let sessionResumeFailureMessage: string | undefined;

        if (options.sessionId) {
            try {
                const resumeStartedAt = Date.now();
                bridgeDiagnostic("getOrCreateSession:resume:start", {
                    sessionId: options.sessionId
                });
                session = await client.resumeSession(options.sessionId, sessionConfig);
                sessionResumeDurationMs = Date.now() - resumeStartedAt;
                bridgeDiagnostic("getOrCreateSession:resume:done", {
                    sessionId: options.sessionId,
                    durationMs: sessionResumeDurationMs
                });
                resumed = true;
                sessionReuseKind = "provider_resume";
            }
            catch (error) {
                sessionResumeFailureMessage = extractErrorMessage(error);
                bridgeDiagnostic("getOrCreateSession:resume:failed", {
                    sessionId: options.sessionId,
                    message: sessionResumeFailureMessage
                });
                if (options.requireSameSession) {
                    throw new Error(
                        `Named-agent delegation requires the existing coordinator session, but Squad could not resume session ${options.sessionId}: ${sessionResumeFailureMessage}`);
                }

                const createStartedAt = Date.now();
                bridgeDiagnostic("getOrCreateSession:create-after-resume-failure:start", {
                    model: sessionConfig.model ?? "(default)"
                });
                session = await client.createSession(sessionConfig);
                sessionCreateDurationMs = Date.now() - createStartedAt;
                bridgeDiagnostic("getOrCreateSession:create-after-resume-failure:done", {
                    sessionId: session.sessionId,
                    durationMs: sessionCreateDurationMs
                });
                sessionReuseKind = "resume_failed_create";
            }
        }
        else {
            const createStartedAt = Date.now();
            bridgeDiagnostic("getOrCreateSession:create:start", {
                model: sessionConfig.model ?? "(default)"
            });
            session = await client.createSession(sessionConfig);
            sessionCreateDurationMs = Date.now() - createStartedAt;
            bridgeDiagnostic("getOrCreateSession:create:done", {
                sessionId: session.sessionId,
                durationMs: sessionCreateDurationMs
            });
            sessionReuseKind = "provider_create";
        }

        const existing = this.sessions.get(session.sessionId);
        if (existing)
            return {
                state: existing,
                sessionReady: {
                    resumed,
                    sessionReuseKind: sessionReuseKind ?? "bridge_cache",
                    sessionAcquireDurationMs: Date.now() - acquireStartedAt,
                    sessionResumeDurationMs,
                    sessionCreateDurationMs,
                    sessionResumeFailureMessage
                }
            };

        const state: SessionState = {
            session,
            activeTools: new Map<string, ActiveToolContext>(),
            backgroundTasks: EmptyBackgroundTasks(),
            subagentsByToolCallId: new Map<string, SubagentLifecycleInfo>(),
            backgroundTaskIdsByToolCallId: new Map<string, string>(),
            subagentReasoningByToolCallId: new Map<string, string>(),
            lastAssistantMessageContent: "",
            createdAt: Date.now(),
            completedPromptCount: 0
        };

        stateRef = state;
        this.attachSessionListeners(state);
        this.sessions.set(session.sessionId, state);
        return {
            state,
            sessionReady: {
                resumed,
                sessionReuseKind,
                sessionAcquireDurationMs: Date.now() - acquireStartedAt,
                sessionResumeDurationMs,
                sessionCreateDurationMs,
                sessionResumeFailureMessage
            }
        };
    }

    private buildSessionReadyInfo(
        state: SessionState,
        telemetry: SessionAcquireTelemetry
    ): SessionReadyInfo {
        return {
            sessionId: state.session.sessionId,
            resumed: telemetry.resumed,
            sessionReuseKind: telemetry.sessionReuseKind,
            sessionAcquireDurationMs: telemetry.sessionAcquireDurationMs,
            sessionResumeDurationMs: telemetry.sessionResumeDurationMs,
            sessionCreateDurationMs: telemetry.sessionCreateDurationMs,
            sessionResumeFailureMessage: telemetry.sessionResumeFailureMessage,
            sessionAgeMs: Math.max(0, Date.now() - state.createdAt),
            sessionPromptCountBeforeCurrent: state.completedPromptCount,
            sessionPromptCountIncludingCurrent: state.completedPromptCount + 1,
            backgroundAgentCount: state.backgroundTasks.agents.length,
            backgroundShellCount: state.backgroundTasks.shells.length,
            knownSubagentCount: state.subagentsByToolCallId.size,
            activeToolCount: state.activeTools.size,
            cachedAssistantChars: state.lastAssistantMessageContent.length
        };
    }

    private attachSessionListeners(state: SessionState) {
        const emitSubagentToolEvent = (
            handlerName: "onSubagentToolStart" | "onSubagentToolProgress" | "onSubagentToolComplete",
            event: unknown,
            overrides: Partial<ToolLifecycleEvent> = {}
        ) => {
            const toolCallId = getEventStringValue(event, "toolCallId") ?? "";
            const context = toolCallId
                ? state.activeTools.get(toolCallId)
                : undefined;
            const parentToolCallId = extractParentToolCallId(event) ?? context?.parentToolCallId;
            const toolName = getEventStringValue(event, "toolName") ?? context?.toolName ?? "";
            if (!parentToolCallId || !toolCallId || !toolName)
                return false;

            if (!context) {
                state.activeTools.set(toolCallId, {
                    parentToolCallId,
                    toolName,
                    args: overrides.args ?? getObjectValue(event, "arguments"),
                    startedAt: overrides.startedAt ?? new Date().toISOString(),
                    description: overrides.description,
                    command: overrides.command,
                    path: overrides.path,
                    intent: overrides.intent,
                    skill: overrides.skill
                });
            }

            const handlers = this.bridgeHandlers[handlerName];
            if (!handlers)
                return true;

            const subagent = this.buildSubagentTranscriptInfo(state, parentToolCallId);
            const knownSubagent = state.subagentsByToolCallId.get(parentToolCallId);
            const effectiveContext = state.activeTools.get(toolCallId) ?? context;
            const toolEvent: ToolLifecycleEvent = {
                parentToolCallId,
                toolCallId,
                toolName,
                args: overrides.args ?? effectiveContext?.args ?? getObjectValue(event, "arguments"),
                startedAt: overrides.startedAt ?? effectiveContext?.startedAt ?? new Date().toISOString(),
                finishedAt: overrides.finishedAt,
                description: overrides.description ?? effectiveContext?.description,
                command: overrides.command ?? effectiveContext?.command,
                path: overrides.path ?? effectiveContext?.path,
                intent: overrides.intent ?? effectiveContext?.intent,
                skill: overrides.skill ?? effectiveContext?.skill,
                progressMessage: overrides.progressMessage,
                partialOutput: overrides.partialOutput,
                outputText: overrides.outputText,
                success: overrides.success
            };

            handlers(
                state.session.sessionId,
                {
                    toolCallId: parentToolCallId,
                    agentId: subagent.agentId,
                    agentName: subagent.agentName ?? knownSubagent?.agentName ?? "agent",
                    agentDisplayName: subagent.agentDisplayName ?? knownSubagent?.agentDisplayName,
                    agentDescription: subagent.agentDescription ?? knownSubagent?.agentDescription
                },
                toolEvent);
            return true;
        };

        state.session.on("reasoning_delta", (event: unknown) => {
            const text = getEventRawStringValue(event, "deltaContent") ?? "";
            const parentToolCallId = extractParentToolCallId(event);
            if (parentToolCallId) {
                if (text.length === 0)
                    return;

                this.recordSubagentReasoningDelta(state, parentToolCallId, text);
                this.bridgeHandlers.onSubagentThinkingDelta?.(
                    state.session.sessionId,
                    this.buildSubagentTranscriptInfo(state, parentToolCallId, {
                        reasoningText: text
                    }));
                return;
            }

            const request = state.currentRequest;
            if (request && text.length > 0)
                request.handlers.onThinking?.(text, extractThinkingSpeaker(event));
        });

        state.session.on("usage", (event: unknown) => {
            const request = state.currentRequest;
            if (!request)
                return;

            const model = getEventStringValue(event, "model") ?? getStringValue(event, "model");
            const inputTokens = getEventNumberValue(event, "inputTokens") ?? getNumberValue(event, "inputTokens");
            const outputTokens = getEventNumberValue(event, "outputTokens") ?? getNumberValue(event, "outputTokens");
            const totalTokens =
                inputTokens !== undefined || outputTokens !== undefined
                    ? (inputTokens ?? 0) + (outputTokens ?? 0)
                    : undefined;

            if (!model && inputTokens === undefined && outputTokens === undefined)
                return;

            request.handlers.onUsage?.({
                model,
                inputTokens,
                outputTokens,
                totalTokens
            });
        });

        state.session.on("message", (event: unknown) => {
            const content = extractAssistantMessageContent(event);
            const parentToolCallId = extractParentToolCallId(event);
            if (parentToolCallId) {
                const reasoningText = this.takeUnstreamedSubagentFinalReasoning(
                    state,
                    parentToolCallId,
                    extractAssistantReasoningText(event));

                if (!content && !reasoningText)
                    return;

                this.bridgeHandlers.onSubagentMessage?.(
                    state.session.sessionId,
                    this.buildSubagentTranscriptInfo(state, parentToolCallId, {
                        text: content,
                        reasoningText
                    }));
                return;
            }

            if (!content)
                return;

            state.lastAssistantMessageContent = content;
            if (state.currentRequest)
                state.currentRequest.lastAssistantMessageContent = content;
        });

        state.session.on("message_delta", (event: unknown) => {
            const chunk = getEventRawStringValue(event, "deltaContent") ?? "";
            const parentToolCallId = extractParentToolCallId(event);
            if (parentToolCallId) {
                if (chunk.length === 0)
                    return;

                this.bridgeHandlers.onSubagentMessageDelta?.(
                    state.session.sessionId,
                    this.buildSubagentTranscriptInfo(state, parentToolCallId, {
                        text: chunk
                    }));
                return;
            }

            const request = state.currentRequest;
            if (!request || chunk.length === 0)
                return;

            request.sawMessageDelta = true;
            request.streamedMessageContent += chunk;
        });

        state.session.on("tool.execution_start", (event: unknown) => {
            const toolCallId = getEventStringValue(event, "toolCallId") ?? "";
            const toolName = getEventStringValue(event, "toolName") ?? "";
            if (!toolCallId || !toolName)
                return;

            const context = buildToolContext(
                toolName,
                getObjectValue(event, "arguments"),
                extractParentToolCallId(event));
            state.activeTools.set(toolCallId, context);
            rememberTaskToolLaunch(state, toolCallId, toolName, context.args);

            if (emitSubagentToolEvent("onSubagentToolStart", event, {
                startedAt: context.startedAt,
                description: context.description,
                command: context.command,
                path: context.path,
                intent: context.intent,
                skill: context.skill
            })) {
                return;
            }

            state.currentRequest?.handlers.onToolStart?.({
                toolCallId,
                ...context
            });
        });

        state.session.on("tool.execution_progress", (event: unknown) => {
            const toolCallId = getEventStringValue(event, "toolCallId") ?? "";
            if (!toolCallId)
                return;

            const context = state.activeTools.get(toolCallId);
            if (emitSubagentToolEvent("onSubagentToolProgress", event, {
                startedAt: context?.startedAt ?? new Date().toISOString(),
                description: context?.description,
                command: context?.command,
                path: context?.path,
                intent: context?.intent,
                skill: context?.skill,
                progressMessage: getEventStringValue(event, "progressMessage") ?? ""
            })) {
                return;
            }

            state.currentRequest?.handlers.onToolProgress?.({
                toolCallId,
                toolName: context?.toolName ?? "",
                args: context?.args,
                startedAt: context?.startedAt ?? new Date().toISOString(),
                description: context?.description,
                command: context?.command,
                path: context?.path,
                intent: context?.intent,
                skill: context?.skill,
                progressMessage: getStringValue(event, "progressMessage") ?? ""
            });
        });

        state.session.on("tool.execution_partial_result", (event: unknown) => {
            const toolCallId = getEventStringValue(event, "toolCallId") ?? "";
            if (!toolCallId)
                return;

            const context = state.activeTools.get(toolCallId);
            if (emitSubagentToolEvent("onSubagentToolProgress", event, {
                startedAt: context?.startedAt ?? new Date().toISOString(),
                description: context?.description,
                command: context?.command,
                path: context?.path,
                intent: context?.intent,
                skill: context?.skill,
                partialOutput: getEventRawStringValue(event, "partialOutput") ?? ""
            })) {
                return;
            }

            state.currentRequest?.handlers.onToolProgress?.({
                toolCallId,
                toolName: context?.toolName ?? "",
                args: context?.args,
                startedAt: context?.startedAt ?? new Date().toISOString(),
                description: context?.description,
                command: context?.command,
                path: context?.path,
                intent: context?.intent,
                skill: context?.skill,
                partialOutput: getRawStringValue(event, "partialOutput") ?? ""
            });
        });

        state.session.on("tool.execution_complete", (event: unknown) => {
            const toolCallId = getEventStringValue(event, "toolCallId") ?? "";
            if (!toolCallId)
                return;

            const context = state.activeTools.get(toolCallId);
            const outputText = extractToolOutput(getObjectValue(event, "result")) || extractToolError(getObjectValue(event, "error"));
            const success = getEventBooleanValue(event, "success") ?? false;
            if (emitSubagentToolEvent("onSubagentToolComplete", event, {
                startedAt: context?.startedAt ?? new Date().toISOString(),
                finishedAt: new Date().toISOString(),
                description: context?.description,
                command: context?.command,
                path: context?.path,
                intent: context?.intent,
                skill: context?.skill,
                success,
                outputText
            })) {
                state.activeTools.delete(toolCallId);
                return;
            }

            state.currentRequest?.handlers.onToolComplete?.({
                toolCallId,
                toolName: context?.toolName ?? "",
                args: context?.args,
                startedAt: context?.startedAt ?? new Date().toISOString(),
                finishedAt: new Date().toISOString(),
                description: context?.description,
                command: context?.command,
                path: context?.path,
                intent: context?.intent,
                skill: context?.skill,
                success,
                outputText
            });
            state.activeTools.delete(toolCallId);
        });

        state.session.on("session.background_tasks_changed", () => {
            void this.refreshBackgroundTasks(state);
        });

        state.session.on("idle", (event: unknown) => {
            const nextTasks = normalizeBackgroundTasks(event);
            if (nextTasks.agents.length > 0 || nextTasks.shells.length > 0) {
                if (!backgroundTasksEqual(state.backgroundTasks, nextTasks)) {
                    state.backgroundTasks = cloneBackgroundTasks(nextTasks);
                    this.bridgeHandlers.onBackgroundTasksChanged?.(
                        state.session.sessionId,
                        cloneBackgroundTasks(state.backgroundTasks));
                }
                return;
            }

            void this.refreshBackgroundTasks(state);
        });

        state.session.on("session.task_complete", (event: unknown) => {
            this.bridgeHandlers.onTaskComplete?.(
                state.session.sessionId,
                extractTaskCompletionSummary(event));
        });

        state.session.on("subagent.started", async (event: unknown) => {
            const subagent = normalizeSubagentLifecycle(event);
            if (!subagent)
                return;

            const merged = this.mergeSubagentInfo(state, subagent);
            await this.refreshBackgroundTasks(state);
            const enriched = merged.toolCallId
                ? state.subagentsByToolCallId.get(merged.toolCallId) ?? merged
                : merged;
            this.bridgeHandlers.onSubagentStarted?.(
                state.session.sessionId,
                enriched);
        });

        state.session.on("subagent.completed", (event: unknown) => {
            const subagent = normalizeSubagentLifecycle(event);
            if (!subagent)
                return;

            const merged = this.mergeSubagentInfo(state, subagent);
            this.bridgeHandlers.onSubagentCompleted?.(
                state.session.sessionId,
                merged);
            void this.refreshBackgroundTasks(state);
        });

        state.session.on("subagent.failed", (event: unknown) => {
            const subagent = normalizeSubagentLifecycle(event);
            if (!subagent)
                return;

            const merged = this.mergeSubagentInfo(state, subagent);
            this.bridgeHandlers.onSubagentFailed?.(
                state.session.sessionId,
                merged);
            void this.refreshBackgroundTasks(state);
        });

        // Watch lifecycle stubs — these event names are expected once the SDK stabilizes
        // watch.* events in a future Squad SDK release. Registered now so SquadDash
        // automatically forwards them when the SDK starts emitting them.

        state.session.on("watch.fleet_dispatched", (event: unknown) => {
            this.bridgeHandlers.onWatchFleetDispatched?.(
                state.session.sessionId,
                {
                    cycleId: getEventStringValue(event, "cycleId"),
                    fleetSize: getEventNumberValue(event, "fleetSize"),
                    prompt: getEventStringValue(event, "prompt")
                });
        });

        state.session.on("watch.wave_dispatched", (event: unknown) => {
            this.bridgeHandlers.onWatchWaveDispatched?.(
                state.session.sessionId,
                {
                    cycleId: getEventStringValue(event, "cycleId"),
                    waveIndex: getEventNumberValue(event, "waveIndex"),
                    waveCount: getEventNumberValue(event, "waveCount"),
                    agentCount: getEventNumberValue(event, "agentCount")
                });
        });

        state.session.on("watch.hydration", (event: unknown) => {
            this.bridgeHandlers.onWatchHydration?.(
                state.session.sessionId,
                {
                    cycleId: getEventStringValue(event, "cycleId"),
                    phase: getEventStringValue(event, "phase")
                });
        });

        state.session.on("watch.retro", (event: unknown) => {
            this.bridgeHandlers.onWatchRetro?.(
                state.session.sessionId,
                {
                    cycleId: getEventStringValue(event, "cycleId"),
                    summary: getEventStringValue(event, "summary")
                });
        });

        state.session.on("watch.monitor_notification", (event: unknown) => {
            this.bridgeHandlers.onWatchMonitorNotification?.(
                state.session.sessionId,
                {
                    cycleId: getEventStringValue(event, "cycleId"),
                    channel: getEventStringValue(event, "channel"),
                    sent: getEventBooleanValue(event, "sent"),
                    recipient: getEventStringValue(event, "recipient")
                });
        });
    }

    private async disposeSession(sessionId: string): Promise<void> {
        const state = this.sessions.get(sessionId);
        if (!state)
            return;

        this.sessions.delete(sessionId);
        try {
            await state.session.close();
        }
        catch {
        }
    }
}

export async function runPrompt(
    prompt: string,
    handlers: SquadRunHandlers,
    request: SquadPromptRequest | string
) {
    const bridge = new SquadBridgeService();
    try {
        await bridge.runPrompt(prompt, handlers, request);
    }
    finally {
        await bridge.shutdown();
    }
}

export async function runDelegation(
    request: SquadDelegationRequest,
    handlers: SquadRunHandlers
) {
    const bridge = new SquadBridgeService();
    try {
        await bridge.runDelegation(request, handlers);
    }
    finally {
        await bridge.shutdown();
    }
}
