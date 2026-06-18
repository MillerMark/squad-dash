import http from "node:http";
import fs from "node:fs";
import path from "node:path";

const host = process.env.OLLAMA_COMPAT_HOST || "127.0.0.1";
const port = Number(process.env.OLLAMA_COMPAT_PORT || 11436);
const targetBaseUrl = new URL(process.env.OLLAMA_COMPAT_TARGET || "http://127.0.0.1:11435");
const logPath = path.resolve(
    process.env.OLLAMA_COMPAT_LOG ||
    path.join(process.cwd(), "..", ".squad", "diagnostics", "ollama-compat-proxy.jsonl"));

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
    }

    if (Array.isArray(body.messages)) {
        body.messages = body.messages.map((message) => {
            if (!message || typeof message !== "object")
                return message;

            return {
                ...message,
                content: normalizeContent(message.content)
            };
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

server.listen(port, host, () => {
    console.log(`Ollama compatibility proxy listening on http://${host}:${port}`);
    console.log(`Forwarding to ${targetBaseUrl.href}`);
    console.log(`Writing diagnostics to ${logPath}`);
});
