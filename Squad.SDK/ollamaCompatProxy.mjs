import http from "node:http";
import fs from "node:fs";
import path from "node:path";
import { pathToFileURL } from "node:url";

const host = process.env.OLLAMA_COMPAT_HOST || "127.0.0.1";
const port = Number(process.env.OLLAMA_COMPAT_PORT || 11436);
const defaultTargetBaseUrl = new URL(process.env.OLLAMA_COMPAT_TARGET || "http://127.0.0.1:11435");
const logPath = path.resolve(
    process.env.OLLAMA_COMPAT_LOG ||
    path.join(process.cwd(), "..", ".squad", "diagnostics", "ollama-compat-proxy.jsonl"));
const maxInputChars = Number(process.env.OLLAMA_COMPAT_MAX_INPUT_CHARS || 65000);
const workerConcurrency = Math.max(1, Number(process.env.OLLAMA_COMPAT_MAX_CONCURRENT || 1));
const gptOssToolingHint = [
    "[Local GPT-OSS tooling hint]",
    "For straightforward text-file edits, prefer the powershell tool.",
    "Use apply_patch only when you can emit a valid structured apply_patch tool call; do not pass raw patch text as the tool arguments.",
    "After editing a file, read it back to verify before replying."
].join(" ");

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

function normalizeRequestBody(body) {
    if (!body || typeof body !== "object")
        return body;

    const model = typeof body.model === "string" ? body.model : "";
    const isGptOss = model.toLowerCase().startsWith("gpt-oss");

    if (isGptOss) {
        body.reasoning_effort ??= "low";

        const maxTokens = Number(body.max_tokens);
        if (!Number.isFinite(maxTokens) || maxTokens < 1024)
            body.max_tokens = 1024;

        const maxCompletionTokens = Number(body.max_completion_tokens);
        if (Number.isFinite(maxCompletionTokens) && maxCompletionTokens < 1024)
            body.max_completion_tokens = 1024;

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

function summarizeRequestBody(body) {
    if (!body || typeof body !== "object")
        return undefined;

    const messages = Array.isArray(body.messages) ? body.messages : [];
    const lastMessage = messages.at(-1);
    return {
        model: body.model,
        stream: body.stream,
        reasoning_effort: body.reasoning_effort,
        max_tokens: body.max_tokens,
        max_completion_tokens: body.max_completion_tokens,
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

const server = http.createServer(async (req, res) => {
    const startedAt = Date.now();
    const shouldSchedule = shouldScheduleModelRequest(req);
    const lease = shouldSchedule
        ? await scheduler.acquire()
        : undefined;
    const targetBaseUrl = lease?.worker.url ?? scheduler.workers[0].url;
    const inboundBody = await readRequestBody(req);
    let outboundBody = inboundBody;
    let normalized = false;
    let requestSummary;

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

    const proxyReq = http.request({
        protocol: targetUrl.protocol,
        hostname: targetUrl.hostname,
        port: targetUrl.port,
        path: `${targetUrl.pathname}${targetUrl.search}`,
        method: req.method,
        headers
    }, (proxyRes) => {
        const responseChunks = [];

        res.writeHead(proxyRes.statusCode || 502, proxyRes.headers);
        proxyRes.on("data", (chunk) => {
            responseChunks.push(chunk);
            res.write(chunk);
        });
        proxyRes.on("end", () => {
            res.end();
            const responseBody = Buffer.concat(responseChunks).toString("utf8");
            appendLog({
                capturedAt: new Date().toISOString(),
                method: req.method,
                path: req.url,
                scheduled: shouldSchedule,
                worker: lease?.worker.name,
                queueWaitMs: lease?.waitMs ?? 0,
                queuedBefore: lease?.queuedBefore ?? 0,
                statusCode: proxyRes.statusCode,
                durationMs: Date.now() - startedAt,
                normalized,
                requestSummary,
                requestPreview: inboundBody.toString("utf8").slice(0, 4000),
                responsePreview: responseBody.slice(0, 4000)
            });
            lease?.release();
        });
    });

    proxyReq.on("error", (error) => {
        res.writeHead(502, { "content-type": "application/json" });
        res.end(JSON.stringify({ error: error.message }));
        appendLog({
            capturedAt: new Date().toISOString(),
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
        lease?.release();
    });

    proxyReq.end(outboundBody);
});

export {
    addGptOssToolingHint,
    LocalModelRequestScheduler,
    normalizeRequestBody,
    parseTargetWorkers,
    replaceFailedApplyPatchExchanges
};

if (process.argv[1] && import.meta.url === pathToFileURL(process.argv[1]).href) {
    server.listen(port, host, () => {
        console.log(`Ollama compatibility proxy listening on http://${host}:${port}`);
        console.log(`Forwarding to ${scheduler.workers.map((worker) => `${worker.name}=${worker.url.href}x${worker.maxConcurrent}`).join(", ")}`);
        console.log(`Writing diagnostics to ${logPath}`);
    });
}
