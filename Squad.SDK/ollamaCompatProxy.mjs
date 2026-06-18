import http from "node:http";
import fs from "node:fs";
import path from "node:path";
import { pathToFileURL } from "node:url";

const host = process.env.OLLAMA_COMPAT_HOST || "127.0.0.1";
const port = Number(process.env.OLLAMA_COMPAT_PORT || 11436);
const targetBaseUrl = new URL(process.env.OLLAMA_COMPAT_TARGET || "http://127.0.0.1:11435");
const logPath = path.resolve(
    process.env.OLLAMA_COMPAT_LOG ||
    path.join(process.cwd(), "..", ".squad", "diagnostics", "ollama-compat-proxy.jsonl"));
const maxInputChars = Number(process.env.OLLAMA_COMPAT_MAX_INPUT_CHARS || 65000);
const gptOssToolingHint = [
    "[Local GPT-OSS tooling hint]",
    "For straightforward text-file edits, prefer the powershell tool.",
    "Use apply_patch only when you can emit a valid structured apply_patch tool call; do not pass raw patch text as the tool arguments.",
    "After editing a file, read it back to verify before replying."
].join(" ");

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

function isApplyPatchParseFailure(message) {
    if (message?.role !== "tool")
        return false;

    const content = normalizeContent(message.content);
    return content.includes("Failed to parse patch") &&
        content.includes("*** Begin Patch");
}

function replaceFailedApplyPatchExchanges(messages) {
    const rewritten = [];
    let replacementCount = 0;

    for (const message of messages) {
        if (isApplyPatchParseFailure(message) &&
            isApplyPatchAssistantMessage(rewritten.at(-1))) {
            rewritten.pop();
            rewritten.push({
                role: "user",
                content: [
                    "[Local GPT-OSS recovery]",
                    "The previous apply_patch attempt failed before editing files because its arguments were malformed.",
                    "Continue the original request now.",
                    "For this local model profile, use the powershell tool for simple text-file edits, then read the file back to verify before replying."
                ].join(" ")
            });
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
                statusCode: proxyRes.statusCode,
                durationMs: Date.now() - startedAt,
                normalized,
                requestSummary,
                requestPreview: inboundBody.toString("utf8").slice(0, 4000),
                responsePreview: responseBody.slice(0, 4000)
            });
        });
    });

    proxyReq.on("error", (error) => {
        res.writeHead(502, { "content-type": "application/json" });
        res.end(JSON.stringify({ error: error.message }));
        appendLog({
            capturedAt: new Date().toISOString(),
            method: req.method,
            path: req.url,
            statusCode: 502,
            durationMs: Date.now() - startedAt,
            normalized,
            requestSummary,
            requestPreview: inboundBody.toString("utf8").slice(0, 4000),
            error: error.message
        });
    });

    proxyReq.end(outboundBody);
});

export {
    addGptOssToolingHint,
    normalizeRequestBody,
    replaceFailedApplyPatchExchanges
};

if (process.argv[1] && import.meta.url === pathToFileURL(process.argv[1]).href) {
    server.listen(port, host, () => {
        console.log(`Ollama compatibility proxy listening on http://${host}:${port}`);
        console.log(`Forwarding to ${targetBaseUrl.href}`);
        console.log(`Writing diagnostics to ${logPath}`);
    });
}
