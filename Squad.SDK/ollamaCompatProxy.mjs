import http from "node:http";
import fs from "node:fs";
import path from "node:path";
import { randomUUID } from "node:crypto";
import { StringDecoder } from "node:string_decoder";
import { pathToFileURL } from "node:url";

const host = process.env.OLLAMA_COMPAT_HOST || "127.0.0.1";
const port = Number(process.env.OLLAMA_COMPAT_PORT || 11436);
const defaultTargetBaseUrl = new URL(process.env.OLLAMA_COMPAT_TARGET || "http://127.0.0.1:11435");
const logPath = path.resolve(
    process.env.OLLAMA_COMPAT_LOG ||
    path.join(process.cwd(), "..", ".squad", "diagnostics", "ollama-compat-proxy.jsonl"));
const gptOssToolingHint = [
    "[Local GPT-OSS tooling hint]",
    "For straightforward text-file edits, prefer the powershell tool.",
    "Use apply_patch only when you can emit a valid structured apply_patch tool call; do not pass raw patch text as the tool arguments.",
    "After editing a file, read it back to verify before replying."
].join(" ");

const profilePresets = {
    conservative: {
        id: "conservative",
        maxInputChars: 24000,
        maxOutputTokens: 1024,
        maxConcurrent: 1
    },
    balanced: {
        id: "balanced",
        maxInputChars: 65000,
        maxOutputTokens: 2048,
        maxConcurrent: 1
    },
    maximum: {
        id: "maximum",
        maxInputChars: 130000,
        maxOutputTokens: 4096,
        maxConcurrent: 1
    }
};

function readPositiveInteger(value, fallback) {
    const parsed = Number(value);
    if (!Number.isFinite(parsed) || parsed <= 0)
        return fallback;

    return Math.max(1, Math.floor(parsed));
}

function resolveLocalProviderProfile(profileId, env = process.env) {
    const normalizedId = String(profileId || "balanced").trim().toLowerCase();
    const preset = profilePresets[normalizedId] ?? profilePresets.balanced;

    return {
        ...preset,
        maxInputChars: readPositiveInteger(env.OLLAMA_COMPAT_MAX_INPUT_CHARS, preset.maxInputChars),
        maxOutputTokens: readPositiveInteger(env.OLLAMA_COMPAT_MAX_OUTPUT_TOKENS, preset.maxOutputTokens),
        maxConcurrent: readPositiveInteger(env.OLLAMA_COMPAT_MAX_CONCURRENT, preset.maxConcurrent)
    };
}

function clampTokenLimit(value, maxTokens) {
    const parsed = Number(value);
    if (!Number.isFinite(parsed) || parsed <= 0)
        return maxTokens;

    return Math.min(maxTokens, Math.max(1, Math.floor(parsed)));
}

function applyOutputLimit(body) {
    if (!body || typeof body !== "object")
        return false;

    const beforeMaxTokens = body.max_tokens;
    const beforeMaxCompletionTokens = body.max_completion_tokens;

    if (body.max_tokens !== undefined || body.max_completion_tokens === undefined)
        body.max_tokens = clampTokenLimit(body.max_tokens, localProviderProfile.maxOutputTokens);

    if (body.max_completion_tokens !== undefined)
        body.max_completion_tokens = clampTokenLimit(
            body.max_completion_tokens,
            localProviderProfile.maxOutputTokens);

    return beforeMaxTokens !== body.max_tokens ||
        beforeMaxCompletionTokens !== body.max_completion_tokens;
}

const localProviderProfile = resolveLocalProviderProfile(process.env.OLLAMA_COMPAT_PROFILE, process.env);
const maxInputChars = localProviderProfile.maxInputChars;
const workerConcurrency = localProviderProfile.maxConcurrent;
const firstDataTimeoutMs = readPositiveInteger(process.env.OLLAMA_COMPAT_FIRST_DATA_TIMEOUT_MS, 30000);
const upstreamResponseTimeoutMs = readPositiveInteger(process.env.OLLAMA_COMPAT_UPSTREAM_RESPONSE_TIMEOUT_MS, 30000);
const truncationNotice = "\n\n[Local provider profile truncated additional context before sending to the local model.]";

function splitTargetList(value) {
    if (!value)
        return [];

    return value
        .split(/[;,]/)
        .map((item) => item.trim())
        .filter(Boolean);
}

function parseTargetDescriptor(rawTarget, index) {
    const separatorIndex = rawTarget.indexOf("=");
    const hasName = separatorIndex > 0;
    const name = hasName
        ? rawTarget.slice(0, separatorIndex).trim()
        : `local-${index + 1}`;
    const urlText = hasName
        ? rawTarget.slice(separatorIndex + 1).trim()
        : rawTarget;

    return {
        name: name || `local-${index + 1}`,
        url: new URL(urlText)
    };
}

function parseTargetWorkers(targetsValue, fallbackTarget, maxConcurrent = workerConcurrency) {
    const rawTargets = splitTargetList(targetsValue);
    const descriptors = rawTargets.length > 0
        ? rawTargets
        : [fallbackTarget.href];

    return descriptors.map((descriptor, index) => ({
        ...parseTargetDescriptor(descriptor, index),
        maxConcurrent: Math.max(1, Number(maxConcurrent) || 1),
        active: 0
    }));
}

class LocalModelRequestScheduler {
    constructor(workers) {
        if (!Array.isArray(workers) || workers.length === 0)
            throw new Error("Local model scheduler requires at least one worker.");

        this.workers = workers;
        this.queue = [];
    }

    acquire() {
        const queuedBefore = this.queue.length;
        const worker = this.tryAcquireWorker();
        if (worker)
            return Promise.resolve(this.createLease(worker, queuedBefore, 0));

        const queuedAt = Date.now();
        return new Promise((resolve) => {
            this.queue.push({ resolve, queuedAt, queuedBefore });
        });
    }

    tryAcquireWorker() {
        for (const worker of this.workers) {
            if (worker.active < worker.maxConcurrent) {
                worker.active++;
                return worker;
            }
        }

        return undefined;
    }

    createLease(worker, queuedBefore, waitMs) {
        let released = false;
        return {
            worker,
            queuedBefore,
            waitMs,
            release: () => {
                if (released)
                    return;

                released = true;
                worker.active = Math.max(0, worker.active - 1);
                this.drain();
            }
        };
    }

    drain() {
        while (this.queue.length > 0) {
            const worker = this.tryAcquireWorker();
            if (!worker)
                return;

            const next = this.queue.shift();
            next.resolve(this.createLease(
                worker,
                next.queuedBefore,
                Date.now() - next.queuedAt));
        }
    }
}

const scheduler = new LocalModelRequestScheduler(
    parseTargetWorkers(process.env.OLLAMA_COMPAT_TARGETS, defaultTargetBaseUrl));

function shouldScheduleModelRequest(req) {
    if (req.method !== "POST")
        return false;

    const urlPath = new URL(req.url || "/", defaultTargetBaseUrl).pathname.toLowerCase();
    return urlPath.endsWith("/chat/completions") ||
        urlPath.endsWith("/completions") ||
        urlPath.endsWith("/responses") ||
        urlPath.endsWith("/api/chat") ||
        urlPath.endsWith("/api/generate");
}

function normalizeContent(value) {
    if (value === null || value === undefined)
        return "";

    if (typeof value === "string")
        return value;

    if (Array.isArray(value)) {
        const parts = [];
        for (const item of value) {
            if (typeof item === "string") {
                parts.push(item);
                continue;
            }

            if (item && typeof item === "object") {
                const text = item.text ?? item.input_text ?? item.output_text;
                if (typeof text === "string")
                    parts.push(text);
            }
        }

        return parts.join("\n");
    }

    return String(value);
}

function getMessageContentLength(message) {
    if (!message || typeof message !== "object")
        return 0;

    return normalizeContent(message.content).length;
}

function normalizeToolCallArguments(argumentsValue) {
    if (typeof argumentsValue === "string") {
        try {
            JSON.parse(argumentsValue);
            return argumentsValue;
        } catch {
            return JSON.stringify({ input: argumentsValue });
        }
    }

    if (argumentsValue === undefined || argumentsValue === null)
        return "{}";

    try {
        return JSON.stringify(argumentsValue);
    } catch {
        return JSON.stringify({ input: String(argumentsValue) });
    }
}

function normalizeMessageToolCalls(message) {
    if (!message || typeof message !== "object" || !Array.isArray(message.tool_calls))
        return message;

    return {
        ...message,
        tool_calls: message.tool_calls.map((toolCall) => {
            if (!toolCall || typeof toolCall !== "object")
                return toolCall;

            const func = toolCall.function;
            if (!func || typeof func !== "object")
                return toolCall;

            return {
                ...toolCall,
                function: {
                    ...func,
                    arguments: normalizeToolCallArguments(func.arguments)
                }
            };
        })
    };
}

function getToolName(tool) {
    if (!tool || typeof tool !== "object")
        return "";

    const func = tool.function;
    if (func && typeof func === "object" && typeof func.name === "string")
        return func.name;

    return "";
}

function getToolNames(body) {
    if (!Array.isArray(body.tools))
        return new Set();

    return new Set(body.tools
        .map(getToolName)
        .filter(Boolean));
}

function hasGptOssToolingHint(messages) {
    return messages.some((message) =>
        typeof message?.content === "string" &&
        message.content.includes("[Local GPT-OSS tooling hint]"));
}

function addGptOssToolingHint(body) {
    if (!Array.isArray(body.messages) || hasGptOssToolingHint(body.messages))
        return false;

    const toolNames = getToolNames(body);
    if (!toolNames.has("apply_patch") || !toolNames.has("powershell"))
        return false;

    let insertIndex = 0;
    while (insertIndex < body.messages.length &&
        (body.messages[insertIndex]?.role === "system" ||
            body.messages[insertIndex]?.role === "developer")) {
        insertIndex++;
    }

    body.messages.splice(insertIndex, 0, {
        role: "system",
        content: gptOssToolingHint
    });
    return true;
}

function toolCallArgumentsText(toolCall) {
    const args = toolCall?.function?.arguments;
    if (typeof args === "string")
        return args;

    if (args === undefined || args === null)
        return "";

    try {
        return JSON.stringify(args);
    } catch {
        return String(args);
    }
}

function isApplyPatchToolCall(toolCall) {
    const name = getToolName(toolCall).toLowerCase();
    if (name === "apply_patch")
        return true;

    const argsText = toolCallArgumentsText(toolCall).toLowerCase();
    if (!argsText)
        return false;

    return argsText.includes("\"command\":\"apply_patch\"") ||
        argsText.includes("\"command\": \"apply_patch\"") ||
        argsText.includes("*** begin patch");
}

function isApplyPatchAssistantMessage(message) {
    return message?.role === "assistant" &&
        Array.isArray(message.tool_calls) &&
        message.tool_calls.some(isApplyPatchToolCall);
}

function isAssistantToolCallMessage(message) {
    return message?.role === "assistant" &&
        Array.isArray(message.tool_calls) &&
        message.tool_calls.length > 0;
}

function isApplyPatchParseFailure(message) {
    if (message?.role !== "tool")
        return false;

    const content = normalizeContent(message.content);
    return content.includes("Failed to parse patch");
}

function isApplyPatchSuccess(message) {
    if (message?.role !== "tool")
        return false;

    const content = normalizeContent(message.content);
    return /^Modified \d+ file\(s\):/i.test(content.trim());
}

function getApplyPatchToolSummary(message) {
    const content = normalizeContent(message.content).trim();
    if (isApplyPatchParseFailure(message)) {
        return [
            "[Local GPT-OSS recovery]",
            "The previous apply_patch attempt failed before editing files because its arguments were malformed.",
            "Continue the original request now.",
            "For this local model profile, use the powershell tool for simple text-file edits, then read the file back to verify before replying."
        ].join(" ");
    }

    if (isApplyPatchSuccess(message)) {
        return [
            "[Local GPT-OSS tool summary]",
            `The previous apply_patch tool reported success: ${content}`,
            "Continue the original request now.",
            "Read the edited file back to verify the intended change, then commit if the user requested a commit."
        ].join(" ");
    }

    return undefined;
}

function replaceFailedApplyPatchExchanges(messages) {
    const rewritten = [];
    let replacementCount = 0;

    for (const message of messages) {
        const applyPatchSummary = getApplyPatchToolSummary(message);
        if (applyPatchSummary &&
            (isApplyPatchAssistantMessage(rewritten.at(-1)) ||
                isAssistantToolCallMessage(rewritten.at(-1)))) {
            rewritten.pop();
            rewritten.push({ role: "user", content: applyPatchSummary });
            replacementCount++;
            continue;
        }

        rewritten.push(message);
    }

    return replacementCount > 0 ? rewritten : messages;
}

function isContinuationPrompt(message) {
    if (!message || typeof message !== "object")
        return false;

    if (message.role !== "user")
        return false;

    return /^please continue from where you left off\.?$/i
        .test(normalizeContent(message.content).trim());
}

function getCurrentUserMessageIndex(messages) {
    for (let i = messages.length - 1; i >= 0; i--) {
        const message = messages[i];
        if (message?.role === "user" && !isContinuationPrompt(message))
            return i;
    }

    return -1;
}

function pruneOversizedGptOssMessages(body) {
    if (!Array.isArray(body.messages) || body.messages.length <= 2)
        return false;

    const totalContentLength = body.messages.reduce(
        (sum, message) => sum + getMessageContentLength(message),
        0);
    if (!Number.isFinite(maxInputChars) ||
        maxInputChars <= 0 ||
        totalContentLength <= maxInputChars) {
        return false;
    }

    const currentUserIndex = getCurrentUserMessageIndex(body.messages);
    if (currentUserIndex < 0)
        return false;

    const leadingContext = body.messages.filter((message, index) =>
        index < currentUserIndex &&
        (message?.role === "system" || message?.role === "developer"));
    body.messages = [
        ...leadingContext,
        ...body.messages
            .slice(currentUserIndex)
            .filter((message) => !isContinuationPrompt(message))
    ];
    return true;
}

function truncateMessageContent(content, maxChars) {
    if (typeof content !== "string" || content.length <= maxChars)
        return content;

    const availableChars = Math.max(0, maxChars - truncationNotice.length);
    return `${content.slice(0, availableChars)}${truncationNotice}`;
}

function enforceInputLimit(body) {
    if (!Array.isArray(body.messages))
        return false;

    if (!Number.isFinite(maxInputChars) || maxInputChars <= 0)
        return false;

    let totalContentLength = body.messages.reduce(
        (sum, message) => sum + getMessageContentLength(message),
        0);
    if (totalContentLength <= maxInputChars)
        return false;

    const currentUserIndex = getCurrentUserMessageIndex(body.messages);
    let changed = false;
    const currentUserMinimumChars = Math.min(4000, Math.max(1000, Math.floor(maxInputChars * 0.35)));
    const otherMinimumChars = Math.min(6000, Math.max(1000, Math.floor(maxInputChars * 0.45)));

    for (let i = 0; i < body.messages.length && totalContentLength > maxInputChars; i++) {
        const message = body.messages[i];
        if (!message || typeof message.content !== "string")
            continue;

        const minimumPreservedChars = i === currentUserIndex
            ? currentUserMinimumChars
            : otherMinimumChars;
        if (message.content.length <= minimumPreservedChars)
            continue;

        const overage = totalContentLength - maxInputChars;
        const targetLength = Math.max(minimumPreservedChars, message.content.length - overage);
        const truncated = truncateMessageContent(message.content, targetLength);
        if (truncated === message.content)
            continue;

        totalContentLength -= message.content.length - truncated.length;
        message.content = truncated;
        changed = true;
    }

    return changed;
}

function normalizeRequestBody(body) {
    if (!body || typeof body !== "object")
        return body;

    const model = typeof body.model === "string" ? body.model : "";
    const isGptOss = model.toLowerCase().startsWith("gpt-oss");
    applyOutputLimit(body);

    if (isGptOss) {
        body.reasoning_effort ??= "low";

        pruneOversizedGptOssMessages(body);
        addGptOssToolingHint(body);
    }

    if (Array.isArray(body.messages)) {
        if (isGptOss)
            body.messages = replaceFailedApplyPatchExchanges(body.messages);

        body.messages = body.messages.map((message) => {
            if (!message || typeof message !== "object")
                return message;

            return normalizeMessageToolCalls({
                ...message,
                content: normalizeContent(message.content)
            });
        });

        enforceInputLimit(body);
    }

    if (Array.isArray(body.input)) {
        body.input = body.input.map((item) => {
            if (!item || typeof item !== "object")
                return item;

            if (Array.isArray(item.content)) {
                return {
                    ...item,
                    content: item.content.map((contentItem) => {
                        if (!contentItem || typeof contentItem !== "object")
                            return contentItem;

                        if ("text" in contentItem && contentItem.text == null)
                            return { ...contentItem, text: "" };

                        return contentItem;
                    })
                };
            }

            if ("content" in item && item.content == null)
                return { ...item, content: "" };

            return item;
        });
    }

    return body;
}

function sanitizeStreamingChoice(choice) {
    if (!choice || typeof choice !== "object")
        return choice;

    const sanitized = { ...choice };
    delete sanitized.message;

    if (sanitized.delta && typeof sanitized.delta === "object") {
        sanitized.delta = { ...sanitized.delta };
        if (Array.isArray(sanitized.delta.tool_calls) && sanitized.delta.tool_calls.length === 0)
            delete sanitized.delta.tool_calls;
    }

    return sanitized;
}

function sanitizeOpenAiChunk(chunk) {
    if (!chunk || typeof chunk !== "object")
        return chunk;

    const sanitized = { ...chunk };
    delete sanitized.CreatedAt;
    delete sanitized.IsDelta;
    delete sanitized.Successful;
    delete sanitized.HttpStatusCode;

    if (Array.isArray(sanitized.choices))
        sanitized.choices = sanitized.choices.map(sanitizeStreamingChoice);

    return sanitized;
}

function normalizeServerSentEvent(event) {
    let changed = false;
    let sawDone = false;

    if (typeof event !== "string" || !event.trim())
        return { body: event, normalized: false, sawDone: false };

    const lines = event.split(/\r?\n/);
    const normalizedLines = lines.map((line) => {
        if (!line.startsWith("data:"))
            return line;

        const data = line.slice("data:".length).trimStart();
        if (data === "[DONE]") {
            sawDone = true;
            return line;
        }

        try {
            const parsed = JSON.parse(data);
            const sanitized = sanitizeOpenAiChunk(parsed);
            const before = JSON.stringify(parsed);
            const after = JSON.stringify(sanitized);
            if (before !== after)
                changed = true;
            return `data: ${after}`;
        } catch {
            return line;
        }
    });

    return {
        body: normalizedLines.join("\n"),
        normalized: changed,
        sawDone
    };
}

function normalizeServerSentEvents(responseBody) {
    if (typeof responseBody !== "string" || !responseBody.includes("data:"))
        return { body: responseBody, normalized: false };

    let changed = false;
    let sawDone = false;
    const events = responseBody.split(/\r?\n\r?\n/);
    const normalizedEvents = [];

    for (const event of events) {
        if (!event.trim())
            continue;

        const result = normalizeServerSentEvent(event);
        changed ||= result.normalized;
        sawDone ||= result.sawDone;
        normalizedEvents.push(result.body);
    }

    if (!sawDone) {
        normalizedEvents.push("data: [DONE]");
        changed = true;
    }

    return {
        body: `${normalizedEvents.join("\n\n")}\n\n`,
        normalized: changed
    };
}

function summarizeRequestBody(body) {
    if (!body || typeof body !== "object")
        return undefined;

    const messages = Array.isArray(body.messages) ? body.messages : [];
    const tools = Array.isArray(body.tools) ? body.tools : [];
    const lastMessage = messages.at(-1);
    return {
        profile: localProviderProfile.id,
        profileMaxInputChars: localProviderProfile.maxInputChars,
        profileMaxOutputTokens: localProviderProfile.maxOutputTokens,
        model: body.model,
        stream: body.stream,
        reasoning_effort: body.reasoning_effort,
        max_tokens: body.max_tokens,
        max_completion_tokens: body.max_completion_tokens,
        toolChoice: body.tool_choice,
        toolCount: tools.length,
        toolNames: tools
            .map((tool) => tool?.function?.name ?? tool?.name)
            .filter(Boolean)
            .slice(0, 40),
        toolsJsonChars: tools.length > 0 ? JSON.stringify(tools).length : 0,
        responseFormat: body.response_format,
        messageCount: messages.length,
        messageRoles: messages.map((message) => message?.role),
        messageContentLengths: messages.map((message) =>
            typeof message?.content === "string" ? message.content.length : undefined),
        lastMessageRole: lastMessage?.role,
        lastMessagePreview: typeof lastMessage?.content === "string"
            ? lastMessage.content.slice(0, 500)
            : undefined
    };
}

function appendLog(record) {
    try {
        fs.mkdirSync(path.dirname(logPath), { recursive: true });
        fs.appendFileSync(logPath, `${JSON.stringify(record)}\n`, "utf8");
    } catch {
        // Diagnostics must never break the proxy path.
    }
}

function readRequestBody(req) {
    return new Promise((resolve, reject) => {
        const chunks = [];
        req.on("data", (chunk) => chunks.push(chunk));
        req.on("end", () => resolve(Buffer.concat(chunks)));
        req.on("error", reject);
    });
}

function appendPreview(preview, chunk) {
    if (preview.length >= 4000)
        return preview;

    return `${preview}${chunk}`.slice(0, 4000);
}

const server = http.createServer(async (req, res) => {
    const startedAt = Date.now();
    const proxyRequestId = randomUUID();
    const shouldSchedule = shouldScheduleModelRequest(req);
    appendLog({
        capturedAt: new Date().toISOString(),
        event: "request:received",
        requestId: proxyRequestId,
        method: req.method,
        path: req.url,
        scheduled: shouldSchedule
    });

    let lease;
    if (shouldSchedule) {
        appendLog({
            capturedAt: new Date().toISOString(),
            event: "scheduler:acquire:start",
            requestId: proxyRequestId,
            queuedBefore: scheduler.queue.length
        });
        lease = await scheduler.acquire();
        appendLog({
            capturedAt: new Date().toISOString(),
            event: "scheduler:acquire:done",
            requestId: proxyRequestId,
            worker: lease.worker.name,
            queueWaitMs: lease.waitMs ?? 0,
            queuedBefore: lease.queuedBefore ?? 0
        });
    }

    const targetBaseUrl = lease?.worker.url ?? scheduler.workers[0].url;
    appendLog({
        capturedAt: new Date().toISOString(),
        event: "request:body:start",
        requestId: proxyRequestId
    });
    const inboundBody = await readRequestBody(req);
    appendLog({
        capturedAt: new Date().toISOString(),
        event: "request:body:done",
        requestId: proxyRequestId,
        inboundBytes: inboundBody.length
    });
    let outboundBody = inboundBody;
    let normalized = false;
    let requestSummary;
    let leaseReleased = false;
    const releaseLease = () => {
        if (leaseReleased)
            return;

        leaseReleased = true;
        lease?.release();
    };

    const contentType = req.headers["content-type"] || "";
    if (inboundBody.length > 0 && String(contentType).includes("application/json")) {
        try {
            const parsed = JSON.parse(inboundBody.toString("utf8"));
            const before = JSON.stringify(parsed);
            const normalizedBody = normalizeRequestBody(parsed);
            const after = JSON.stringify(normalizedBody);
            normalized = before !== after;
            requestSummary = summarizeRequestBody(normalizedBody);
            outboundBody = Buffer.from(after, "utf8");
        } catch {
            // Forward invalid JSON as-is so the target can report the real error.
        }
    }

    const targetUrl = new URL(req.url || "/", targetBaseUrl);
    const headers = { ...req.headers };
    headers.host = targetUrl.host;
    headers["content-length"] = String(outboundBody.length);
    appendLog({
        capturedAt: new Date().toISOString(),
        event: "upstream:start",
        requestId: proxyRequestId,
        target: targetUrl.href,
        outboundBytes: outboundBody.length,
        requestSummary
    });

    const proxyReq = http.request({
        protocol: targetUrl.protocol,
        hostname: targetUrl.hostname,
        port: targetUrl.port,
        path: `${targetUrl.pathname}${targetUrl.search}`,
        method: req.method,
        headers
    }, (proxyRes) => {
        clearTimeout(upstreamResponseTimer);
        appendLog({
            capturedAt: new Date().toISOString(),
            event: "upstream:response",
            requestId: proxyRequestId,
            statusCode: proxyRes.statusCode,
            contentType: proxyRes.headers["content-type"],
            durationMs: Date.now() - startedAt
        });
        const isEventStream = String(proxyRes.headers["content-type"] || "")
            .includes("text/event-stream");
        const responseChunks = [];
        const responseHeaders = { ...proxyRes.headers };
        delete responseHeaders["content-length"];
        res.writeHead(proxyRes.statusCode || 502, responseHeaders);

        if (isEventStream) {
            const decoder = new StringDecoder("utf8");
            let eventBuffer = "";
            let responsePreview = "";
            let responseNormalized = false;
            let sawDone = false;
            let firstResponseChunkLogged = false;
            let firstDataTimer;

            const flushEvent = (event) => {
                if (!event.trim())
                    return;

                const result = normalizeServerSentEvent(event);
                responseNormalized ||= result.normalized;
                sawDone ||= result.sawDone;
                const output = `${result.body}\n\n`;
                responsePreview = appendPreview(responsePreview, output);
                res.write(output);
            };

            const timeoutMessage =
                `Local provider opened a stream but did not emit a first token within ${firstDataTimeoutMs} ms.`;
            firstDataTimer = setTimeout(() => {
                if (firstResponseChunkLogged || res.writableEnded)
                    return;

                responseNormalized = true;
                const errorEvent = `data: ${JSON.stringify({
                    object: "chat.completion.chunk",
                    choices: [{
                        index: 0,
                        delta: {
                            role: "assistant",
                            content: `[error] ${timeoutMessage}`
                        }
                    }]
                })}\n\n`;
                const doneEvent = "data: [DONE]\n\n";
                responsePreview = appendPreview(responsePreview, errorEvent);
                responsePreview = appendPreview(responsePreview, doneEvent);
                res.write(errorEvent);
                res.write(doneEvent);
                res.end();
                appendLog({
                    capturedAt: new Date().toISOString(),
                    event: "upstream:first-data-timeout",
                    requestId: proxyRequestId,
                    timeoutMs: firstDataTimeoutMs,
                    durationMs: Date.now() - startedAt,
                    requestSummary,
                    responsePreview
                });
                releaseLease();
                proxyReq.destroy(new Error(timeoutMessage));
            }, firstDataTimeoutMs);

            const flushCompleteEvents = () => {
                let match = eventBuffer.match(/\r?\n\r?\n/);
                while (match) {
                    const event = eventBuffer.slice(0, match.index);
                    eventBuffer = eventBuffer.slice(match.index + match[0].length);
                    flushEvent(event);
                    match = eventBuffer.match(/\r?\n\r?\n/);
                }
            };

            proxyRes.on("data", (chunk) => {
                if (!firstResponseChunkLogged) {
                    firstResponseChunkLogged = true;
                    clearTimeout(firstDataTimer);
                    appendLog({
                        capturedAt: new Date().toISOString(),
                        event: "upstream:first-data",
                        requestId: proxyRequestId,
                        bytes: chunk.length,
                        durationMs: Date.now() - startedAt
                    });
                }
                eventBuffer += decoder.write(chunk);
                flushCompleteEvents();
            });
            proxyRes.on("end", () => {
                clearTimeout(firstDataTimer);
                eventBuffer += decoder.end();
                flushCompleteEvents();
                flushEvent(eventBuffer);
                if (!sawDone) {
                    const doneEvent = "data: [DONE]\n\n";
                    responseNormalized = true;
                    responsePreview = appendPreview(responsePreview, doneEvent);
                    res.write(doneEvent);
                }
                res.end();
                appendLog({
                    capturedAt: new Date().toISOString(),
                    event: "request:done",
                    requestId: proxyRequestId,
                    method: req.method,
                    path: req.url,
                    scheduled: shouldSchedule,
                    worker: lease?.worker.name,
                    queueWaitMs: lease?.waitMs ?? 0,
                    queuedBefore: lease?.queuedBefore ?? 0,
                    statusCode: proxyRes.statusCode,
                    durationMs: Date.now() - startedAt,
                    normalized,
                    responseNormalized,
                    requestSummary,
                    requestPreview: inboundBody.toString("utf8").slice(0, 4000),
                    responsePreview
                });
                releaseLease();
            });
            return;
        }

        let firstResponseChunkLogged = false;
        proxyRes.on("data", (chunk) => {
            if (!firstResponseChunkLogged) {
                firstResponseChunkLogged = true;
                appendLog({
                    capturedAt: new Date().toISOString(),
                    event: "upstream:first-data",
                    requestId: proxyRequestId,
                    bytes: chunk.length,
                    durationMs: Date.now() - startedAt
                });
            }
            responseChunks.push(chunk);
        });
        proxyRes.on("end", () => {
            const responseBody = Buffer.concat(responseChunks).toString("utf8");
            const responseBuffer = Buffer.from(responseBody, "utf8");
            res.end(responseBuffer);
            appendLog({
                capturedAt: new Date().toISOString(),
                event: "request:done",
                requestId: proxyRequestId,
                method: req.method,
                path: req.url,
                scheduled: shouldSchedule,
                worker: lease?.worker.name,
                queueWaitMs: lease?.waitMs ?? 0,
                queuedBefore: lease?.queuedBefore ?? 0,
                statusCode: proxyRes.statusCode,
                durationMs: Date.now() - startedAt,
                normalized,
                responseNormalized: false,
                requestSummary,
                requestPreview: inboundBody.toString("utf8").slice(0, 4000),
                responsePreview: responseBody.slice(0, 4000)
            });
            releaseLease();
        });
    });

    const upstreamTimeoutMessage =
        `Local provider did not return response headers within ${upstreamResponseTimeoutMs} ms.`;
    const upstreamResponseTimer = setTimeout(() => {
        if (res.writableEnded)
            return;

        res.writeHead(504, { "content-type": "application/json" });
        res.end(JSON.stringify({ error: upstreamTimeoutMessage }));
        appendLog({
            capturedAt: new Date().toISOString(),
            event: "upstream:response-timeout",
            requestId: proxyRequestId,
            timeoutMs: upstreamResponseTimeoutMs,
            durationMs: Date.now() - startedAt,
            requestSummary
        });
        releaseLease();
        proxyReq.destroy(new Error(upstreamTimeoutMessage));
    }, upstreamResponseTimeoutMs);

    proxyReq.on("error", (error) => {
        clearTimeout(upstreamResponseTimer);
        if (!res.writableEnded) {
            res.writeHead(502, { "content-type": "application/json" });
            res.end(JSON.stringify({ error: error.message }));
        }
        appendLog({
            capturedAt: new Date().toISOString(),
            event: "request:error",
            requestId: proxyRequestId,
            method: req.method,
            path: req.url,
            scheduled: shouldSchedule,
            worker: lease?.worker.name,
            queueWaitMs: lease?.waitMs ?? 0,
            queuedBefore: lease?.queuedBefore ?? 0,
            statusCode: 502,
            durationMs: Date.now() - startedAt,
            normalized,
            requestSummary,
            requestPreview: inboundBody.toString("utf8").slice(0, 4000),
            error: error.message
        });
        releaseLease();
    });

    proxyReq.end(outboundBody);
});

export {
    addGptOssToolingHint,
    LocalModelRequestScheduler,
    normalizeRequestBody,
    normalizeServerSentEvents,
    parseTargetWorkers,
    replaceFailedApplyPatchExchanges,
    resolveLocalProviderProfile
};

if (process.argv[1] && import.meta.url === pathToFileURL(process.argv[1]).href) {
    server.listen(port, host, () => {
        console.log(`Ollama compatibility proxy listening on http://${host}:${port}`);
        console.log(`Local provider profile ${localProviderProfile.id}: maxInputChars=${localProviderProfile.maxInputChars}, maxOutputTokens=${localProviderProfile.maxOutputTokens}, maxConcurrent=${localProviderProfile.maxConcurrent}`);
        console.log(`Forwarding to ${scheduler.workers.map((worker) => `${worker.name}=${worker.url.href}x${worker.maxConcurrent}`).join(", ")}`);
        console.log(`Writing diagnostics to ${logPath}`);
    });
}
