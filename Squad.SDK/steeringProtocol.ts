import type { SquadPromptAttachment } from "./squadService.js";

export type SteerRequest = {
    type: "steer";
    requestId: string;
    sessionId: string;
    prompt: string;
    attachments?: SquadPromptAttachment[];
};

export type SteerResult = {
    type: "steer_result";
    requestId: string;
    sessionId: string;
    accepted: boolean;
    message?: string;
};

/**
 * Starts delivery without making the bridge input loop wait for the provider
 * transport. Both synchronous and asynchronous failures are reported through
 * the steering-specific response shape so they cannot be mistaken for prompt
 * failures by the host.
 */
export class SteerRequestDispatcher {
    private readonly pendingBySession = new Map<string, Promise<void>>();

    public constructor(
        private readonly deliver: (request: SteerRequest) => void | Promise<void>,
        private readonly emit: (result: SteerResult) => void
    ) { }

    public dispatch(request: SteerRequest): void {
        const previous = this.pendingBySession.get(request.sessionId) ?? Promise.resolve();
        const delivery = previous
            .then(() => this.deliver(request))
            .then(
                () => this.emit({
                    type: "steer_result",
                    requestId: request.requestId,
                    sessionId: request.sessionId,
                    accepted: true
                }),
                error => this.emit({
                    type: "steer_result",
                    requestId: request.requestId,
                    sessionId: request.sessionId,
                    accepted: false,
                    message: error instanceof Error ? error.message : String(error)
                }))
            .finally(() => {
                if (this.pendingBySession.get(request.sessionId) === delivery)
                    this.pendingBySession.delete(request.sessionId);
            });
        this.pendingBySession.set(request.sessionId, delivery);
    }
}

function nonEmptyString(value: unknown): string | undefined {
    if (typeof value !== "string")
        return undefined;
    const trimmed = value.trim();
    return trimmed.length > 0 ? trimmed : undefined;
}

function optionalString(value: unknown): string | undefined {
    return value === undefined ? undefined : nonEmptyString(value);
}

function parsePosition(value: unknown): { line: number; character: number } | undefined {
    if (!value || typeof value !== "object" || Array.isArray(value))
        return undefined;
    const record = value as Record<string, unknown>;
    if (!Number.isInteger(record.line) || !Number.isInteger(record.character) ||
        (record.line as number) < 0 || (record.character as number) < 0) {
        return undefined;
    }
    return { line: record.line as number, character: record.character as number };
}

function parseSelection(value: unknown): {
    start: { line: number; character: number };
    end: { line: number; character: number };
} | undefined {
    if (!value || typeof value !== "object" || Array.isArray(value))
        return undefined;
    const record = value as Record<string, unknown>;
    const start = parsePosition(record.start);
    const end = parsePosition(record.end);
    return start && end ? { start, end } : undefined;
}

export function tryParsePromptAttachment(value: unknown): SquadPromptAttachment | null {
    if (!value || typeof value !== "object" || Array.isArray(value))
        return null;
    const record = value as Record<string, unknown>;
    const displayName = optionalString(record.displayName);

    if (record.type === "file" || record.type === "directory") {
        const path = nonEmptyString(record.path);
        if (!path)
            return null;
        return {
            type: record.type,
            path,
            ...(displayName ? { displayName } : {})
        };
    }

    if (record.type !== "selection")
        return null;
    const filePath = nonEmptyString(record.filePath);
    if (!filePath || !displayName)
        return null;

    let selection: ReturnType<typeof parseSelection>;
    if (record.selection !== undefined) {
        selection = parseSelection(record.selection);
        if (!selection)
            return null;
    }
    const text = record.text;
    if (text !== undefined && typeof text !== "string")
        return null;
    return {
        type: "selection",
        filePath,
        displayName,
        ...(selection ? { selection } : {}),
        ...(text !== undefined ? { text } : {})
    };
}

export function tryParseSteerRequest(value: unknown): SteerRequest | null {
    if (!value || typeof value !== "object" || Array.isArray(value))
        return null;
    const record = value as Record<string, unknown>;
    if (record.type !== "steer")
        return null;

    const requestId = nonEmptyString(record.requestId);
    const sessionId = nonEmptyString(record.sessionId);
    const prompt = nonEmptyString(record.prompt);
    if (!requestId || !sessionId || !prompt)
        return null;

    let attachments: SquadPromptAttachment[] | undefined;
    if (record.attachments !== undefined) {
        if (!Array.isArray(record.attachments))
            return null;
        const parsed = record.attachments.map(tryParsePromptAttachment);
        if (parsed.some(attachment => attachment === null))
            return null;
        attachments = parsed as SquadPromptAttachment[];
    }

    return {
        type: "steer",
        requestId,
        sessionId,
        prompt,
        ...(attachments && attachments.length > 0 ? { attachments } : {})
    };
}
