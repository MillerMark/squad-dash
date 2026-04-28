import { SquadClient } from "@bradygaster/squad-sdk/client";

const SessionIdleTimeoutMs = 40 * 60 * 1000;

export type SquadPromptRequest = {
    cwd: string;
    sessionId?: string;
    configDir?: string;
};

type SquadSessionRequest = SquadPromptRequest & {
    requireSameSession?: boolean;
};

export type SquadDelegationRequest = {
    cwd: string;
    sessionId: string;
    configDir?: string;
    selectedOption: string;
    targetAgent: string;
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
    lastAssistantMessageContent: string;
    hiddenAdditionalContext?: string;
};

type SessionState = {
    session: SquadSession;
    activeTools: Map<string, ActiveToolContext>;
    backgroundTasks: BackgroundTaskSnapshot;
    subagentsByToolCallId: Map<string, SubagentLifecycleInfo>;
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
        agents: tasks.agents.map(agent => ({ ...agent })),
        shells: tasks.shells.map(shell => ({ ...shell }))
    };
}

function backgroundTasksEqual(left: BackgroundTaskSnapshot, right: BackgroundTaskSnapshot): boolean {
    return JSON.stringify(left) === JSON.stringify(right);
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

function normalizeAgentHandle(value: string): string {
    return value.trim().replace(/^@+/, "").toLowerCase();
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
                requireSameSession: true
            },
            buildDelegationHiddenContext(request.selectedOption, request.targetAgent));
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
        await this.refreshBackgroundTasks(state);
        if (state.currentRequest)
            throw new Error("A prompt is already running for this Squad session.");

        const requestContext: RequestContext = {
            aborted: false,
            handlers,
            sawMessageDelta: false,
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
            const finalMessage = state.session.sendAndWait
                ? await state.session.sendAndWait({ prompt: trimmedPrompt }, SessionIdleTimeoutMs)
                : await this.client!.sendAndWait(state.session, { prompt: trimmedPrompt }, SessionIdleTimeoutMs);

            if (requestContext.aborted) {
                handlers.onAborted?.();
                return;
            }

            const finalContent =
                extractAssistantMessageContent(finalMessage) ||
                requestContext.lastAssistantMessageContent ||
                state.lastAssistantMessageContent;
            if (!requestContext.sawMessageDelta && finalContent)
                handlers.onDelta?.(finalContent);

            handlers.onDone?.({ kind: "completed" });
        }
        catch (error) {
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

        const state = sessionId
            ? this.sessions.get(sessionId)
            : Array.from(this.sessions.values()).find(value =>
                value.backgroundTasks.agents.some(agent => agent.agentId === normalizedTaskId) ||
                value.backgroundTasks.shells.some(shell => shell.shellId === normalizedTaskId));

        if (!state)
            return false;

        const backgroundTaskSession = state.session as unknown as {
            cancelBackgroundTask?: (taskId: string) => Promise<boolean>;
        };

        if (!backgroundTaskSession.cancelBackgroundTask)
            return false;

        const cancelled = await backgroundTaskSession.cancelBackgroundTask(normalizedTaskId).catch(() => false);
        await this.refreshBackgroundTasks(state).catch(() => undefined);
        return cancelled;
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

    private async refreshBackgroundTasks(state: SessionState): Promise<void> {
        const nextTasks = await this.loadBackgroundTasks(state);
        if (backgroundTasksEqual(state.backgroundTasks, nextTasks))
            return;

        state.backgroundTasks = cloneBackgroundTasks(nextTasks);
        this.bridgeHandlers.onBackgroundTasksChanged?.(
            state.session.sessionId,
            cloneBackgroundTasks(state.backgroundTasks));
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

        for (const task of tasks) {
            const taskType = getStringValue(task, "type");
            if (taskType === "agent") {
                const toolCallId = getStringValue(task, "toolCallId");
                const progress = backgroundTaskSession.getBackgroundTaskProgress
                    ? await backgroundTaskSession.getBackgroundTaskProgress(task).catch(() => undefined)
                    : undefined;
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
                const progress = backgroundTaskSession.getBackgroundTaskProgress
                    ? await backgroundTaskSession.getBackgroundTaskProgress(task).catch(() => undefined)
                    : undefined;
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
        const client = await this.ensureClient(options.cwd);
        const acquireStartedAt = Date.now();

        if (options.sessionId) {
            const existingState = this.sessions.get(options.sessionId);
            if (existingState)
                return {
                    state: existingState,
                    sessionReady: {
                        resumed: true,
                        sessionReuseKind: "bridge_cache",
                        sessionAcquireDurationMs: Date.now() - acquireStartedAt
                    }
                };
        }

        let stateRef: SessionState | undefined;
        const sessionConfig = {
            onPermissionRequest: async () => ({ kind: "approved" as const }),
            streaming: true,
            workingDirectory: options.cwd,
            configDir: options.configDir,
            hooks: {
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
                session = await client.resumeSession(options.sessionId, sessionConfig);
                sessionResumeDurationMs = Date.now() - resumeStartedAt;
                resumed = true;
                sessionReuseKind = "provider_resume";
            }
            catch (error) {
                sessionResumeFailureMessage = extractErrorMessage(error);
                if (options.requireSameSession) {
                    throw new Error(
                        `Named-agent delegation requires the existing coordinator session, but Squad could not resume session ${options.sessionId}: ${sessionResumeFailureMessage}`);
                }

                const createStartedAt = Date.now();
                session = await client.createSession(sessionConfig);
                sessionCreateDurationMs = Date.now() - createStartedAt;
                sessionReuseKind = "resume_failed_create";
            }
        }
        else {
            const createStartedAt = Date.now();
            session = await client.createSession(sessionConfig);
            sessionCreateDurationMs = Date.now() - createStartedAt;
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
                intent: overrides.intent ?? effectiveContext?.intent ?? knownSubagent?.agentDescription,
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
            const request = state.currentRequest;
            const text = getRawStringValue(event, "deltaContent") ?? "";
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
                if (!content && !extractAssistantReasoningText(event))
                    return;

                this.bridgeHandlers.onSubagentMessage?.(
                    state.session.sessionId,
                    this.buildSubagentTranscriptInfo(state, parentToolCallId, {
                        text: content,
                        reasoningText: extractAssistantReasoningText(event)
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
            request.handlers.onDelta?.(chunk);
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
