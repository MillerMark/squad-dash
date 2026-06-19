import assert from "node:assert/strict";
import test from "node:test";

import {
    applyCapabilityProfile,
    applyLastUserPrefix,
    applyPromptProfile,
    convertNonStreamingCompletionToSse,
    LocalModelRequestScheduler,
    normalizeRequestBody,
    normalizeServerSentEvents,
    parseFoundryServerStatusUrl,
    parseTargetWorkers,
    resolveFoundryServerUrl,
    resolveLocalCapabilityProfile,
    resolveLocalProviderProfile,
    resolveTargetBaseUrl
} from "./ollamaCompatProxy.mjs";

test("Local provider profile defaults to balanced capacity", () => {
    const profile = resolveLocalProviderProfile(undefined, {});

    assert.deepEqual(profile, {
        id: "balanced",
        maxInputChars: 65000,
        maxOutputTokens: 2048,
        maxConcurrent: 1
    });
});

test("Local provider profile accepts conservative preset and explicit overrides", () => {
    const profile = resolveLocalProviderProfile("conservative", {
        OLLAMA_COMPAT_MAX_INPUT_CHARS: "12000",
        OLLAMA_COMPAT_MAX_OUTPUT_TOKENS: "512",
        OLLAMA_COMPAT_MAX_CONCURRENT: "2"
    });

    assert.deepEqual(profile, {
        id: "conservative",
        maxInputChars: 12000,
        maxOutputTokens: 512,
        maxConcurrent: 2
    });
});

test("Foundry server status parser reads active web URL", () => {
    const url = parseFoundryServerStatusUrl(JSON.stringify({
        running: true,
        state: "ready",
        webUrls: ["http://127.0.0.1:55824"]
    }));

    assert.equal(url, "http://127.0.0.1:55824");
});

test("Foundry server status parser rejects stopped server", () => {
    assert.throws(
        () => parseFoundryServerStatusUrl(JSON.stringify({
            running: false,
            state: "not_running",
            webUrls: ["http://127.0.0.1:55824"]
        })),
        /not running/);
});

test("Foundry target resolver uses server status URL", () => {
    const url = resolveFoundryServerUrl((fileName, args) => {
        assert.equal(fileName, "foundry");
        assert.deepEqual(args, ["server", "status", "-o", "json"]);
        return JSON.stringify({ webUrls: ["http://127.0.0.1:55824"] });
    });

    assert.equal(url, "http://127.0.0.1:55824");
});

test("Target resolver accepts Foundry local target alias", () => {
    const url = resolveTargetBaseUrl("foundry", () =>
        JSON.stringify({ webUrls: ["http://127.0.0.1:55824"] }));

    assert.equal(url.href, "http://127.0.0.1:55824/");
});

test("Default Foundry target remains refreshable after worker parsing", () => {
    const workers = parseTargetWorkers(
        undefined,
        {
            name: "foundry",
            url: new URL("http://127.0.0.1:55824"),
            targetKind: "foundry",
            targetResolvedAt: 123
        });

    assert.equal(workers[0].url.href, "http://127.0.0.1:55824/");
    assert.equal(workers[0].targetKind, "foundry");
    assert.equal(workers[0].targetResolvedAt, 123);
});

test("Local capability profile defaults to full tool access", () => {
    const profile = resolveLocalCapabilityProfile(undefined, {});

    assert.deepEqual(profile, {
        id: "full",
        allowedTools: undefined
    });
});

test("Local-lite capability profile keeps only read-only coordination tools", () => {
    const profile = resolveLocalCapabilityProfile("local-lite", {});
    const body = {
        tool_choice: {
            type: "function",
            function: { name: "powershell" }
        },
        tools: [
            { type: "function", function: { name: "powershell" } },
            { type: "function", function: { name: "view" } },
            { type: "function", function: { name: "grep" } },
            { type: "function", function: { name: "glob" } },
            { type: "function", function: { name: "report_intent" } },
            { type: "function", function: { name: "report_probe" } },
            { type: "function", function: { name: "task" } }
        ]
    };

    const changed = applyCapabilityProfile(body, profile);

    assert.equal(changed, true);
    assert.deepEqual(
        body.tools.map((tool) => tool.function.name),
        ["view", "grep", "glob", "report_intent", "report_probe"]);
    assert.equal(body.tool_choice, undefined);
});

test("Local-lite capability profile preserves forced model probe tool choice", () => {
    const profile = resolveLocalCapabilityProfile("local-lite", {});
    const body = {
        tool_choice: {
            type: "function",
            function: { name: "report_probe" }
        },
        tools: [
            { type: "function", function: { name: "report_probe" } }
        ]
    };

    const changed = applyCapabilityProfile(body, profile);

    assert.equal(changed, false);
    assert.deepEqual(
        body.tool_choice,
        {
            type: "function",
            function: { name: "report_probe" }
        });
});

test("Text-only capability profile removes tool declarations", () => {
    const profile = resolveLocalCapabilityProfile("text-only", {});
    const body = {
        tool_choice: "auto",
        tools: [
            { type: "function", function: { name: "report_intent" } }
        ]
    };

    const changed = applyCapabilityProfile(body, profile);

    assert.equal(changed, true);
    assert.equal(body.tools, undefined);
    assert.equal(body.tool_choice, undefined);
});

test("Last user prefix applies only to the current non-continuation user message", () => {
    const body = {
        messages: [
            { role: "system", content: "system" },
            { role: "user", content: "older" },
            { role: "assistant", content: "reply" },
            { role: "user", content: "Please continue from where you left off." },
            { role: "user", content: "What time is it?" }
        ]
    };

    const changed = applyLastUserPrefix(body, "/no_think ");

    assert.equal(changed, true);
    assert.equal(body.messages[1].content, "older");
    assert.equal(body.messages[3].content, "Please continue from where you left off.");
    assert.equal(body.messages[4].content, "/no_think What time is it?");
});

test("Foundry simple prompt profile keeps only compact system and latest user prompt", () => {
    const body = {
        model: "qwen3-8b-cuda-gpu:2",
        messages: [
            { role: "system", content: "Copilot-shaped system prompt ".repeat(100) },
            {
                role: "user",
                content: [
                    "<current_datetime>2026-06-19T08:38:05-04:00</current_datetime>",
                    "",
                    "Can you send me an inbox message that says hello?",
                    "",
                    "## Open Tasks (from .squad/tasks.md)",
                    "- [ ] Do not leak this task into simple Foundry prompt"
                ].join("\n")
            }
        ],
        tools: [
            { type: "function", function: { name: "view" } }
        ],
        tool_choice: "auto"
    };

    const changed = applyPromptProfile(body, "foundry-simple");

    assert.equal(changed, true);
    assert.equal(body.messages.length, 2);
    assert.equal(body.messages[0].role, "system");
    assert.ok(body.messages[0].content.includes("SquadDash local assistant"));
    assert.ok(body.messages[0].content.includes("INBOX_MESSAGE_JSON:"));
    assert.equal(body.messages[1].content, "Can you send me an inbox message that says hello?");
    assert.equal(body.tools, undefined);
    assert.equal(body.tool_choice, undefined);
});

test("Non-streaming completion with tool calls converts to OpenAI SSE", () => {
    const completion = {
        id: "chat.id.1",
        model: "qwen3-14b-cuda-gpu:2",
        choices: [{
            index: 0,
            message: {
                role: "assistant",
                content: "<think>\n\n</think>\n<tool_call>{\"name\":\"get_time\",\"arguments\":{}}</tool_call>",
                tool_calls: [{
                    id: "call_1",
                    type: "function",
                    function: {
                        name: "get_time",
                        arguments: {}
                    }
                }]
            },
            finish_reason: "tool_calls"
        }]
    };

    const converted = convertNonStreamingCompletionToSse(JSON.stringify(completion), {
        model: "qwen3-14b-cuda-gpu:2"
    });

    assert.equal(converted.normalized, true);
    assert.ok(converted.body.includes("data: "));
    assert.ok(converted.body.includes("data: [DONE]"));
    assert.ok(converted.body.includes('"finish_reason":"tool_calls"'));
    assert.ok(converted.body.includes('"tool_calls"'));
    assert.ok(converted.body.includes('"name":"get_time"'));
    assert.ok(!converted.body.includes("<tool_call>"));
});

test("Non-streaming textual tool call converts to OpenAI SSE tool call", () => {
    const completion = {
        id: "chat.id.1",
        model: "qwen3-14b-cuda-gpu:2",
        choices: [{
            index: 0,
            message: {
                role: "assistant",
                content: "<tool_call>\n{\"name\":\"view\",\"arguments\":{\"path\":\".squad/tasks.md\"}}}\n</tool_call>"
            },
            finish_reason: "stop"
        }]
    };

    const converted = convertNonStreamingCompletionToSse(JSON.stringify(completion), {
        model: "qwen3-14b-cuda-gpu:2"
    });
    const streamedJson = converted.body
        .split(/\r?\n/)
        .find((line) => line.startsWith("data: {"))
        .slice("data: ".length);
    const streamed = JSON.parse(streamedJson);
    const toolCall = streamed.choices[0].delta.tool_calls[0];

    assert.equal(converted.normalized, true);
    assert.equal(streamed.choices[0].finish_reason, "tool_calls");
    assert.equal(toolCall.function.name, "view");
    assert.equal(toolCall.function.arguments, "{\"path\":\".squad/tasks.md\"}");
    assert.ok(!converted.body.includes("<tool_call>"));
});

test("Non-streaming textual tool call accepts parameters as arguments", () => {
    const completion = {
        id: "chat.id.1",
        model: "qwen3-14b-cuda-gpu:2",
        choices: [{
            message: {
                role: "assistant",
                content: "<tool_call>{\"name\":\"report_probe\",\"parameters\":{\"status\":\"ok\"}}</tool_call>"
            }
        }]
    };

    const converted = convertNonStreamingCompletionToSse(JSON.stringify(completion), {});
    const streamedJson = converted.body
        .split(/\r?\n/)
        .find((line) => line.startsWith("data: {"))
        .slice("data: ".length);
    const streamed = JSON.parse(streamedJson);
    const toolCall = streamed.choices[0].delta.tool_calls[0];

    assert.equal(toolCall.function.name, "report_probe");
    assert.equal(toolCall.function.arguments, "{\"status\":\"ok\"}");
});

test("Non-streaming text completion drops only empty leading think block", () => {
    const completion = {
        id: "chat.id.1",
        model: "qwen3-14b-cuda-gpu:2",
        choices: [{
            message: {
                role: "assistant",
                content: "<think>\n\n</think>\n\nThe current time is 8:25 AM."
            },
            finish_reason: "stop"
        }]
    };

    const converted = convertNonStreamingCompletionToSse(JSON.stringify(completion), {});

    assert.equal(converted.normalized, true);
    assert.ok(converted.body.includes("The current time is 8:25 AM."));
    assert.ok(!converted.body.includes("<think>"));
});

test("Non-streaming text completion repairs one surplus inbox JSON closing brace", () => {
    const completion = {
        id: "chat.id.1",
        model: "qwen3-8b-cuda-gpu:2",
        choices: [{
            message: {
                role: "assistant",
                content: "INBOX_MESSAGE_JSON:\n{\"subject\":\"Foundry Simple Test\",\"from\":\"coordinator\",\"body\":\"Hello\",\"priority\":\"normal\",\"attachments\":[],\"actions\":[]}}"
            },
            finish_reason: "stop"
        }]
    };

    const converted = convertNonStreamingCompletionToSse(JSON.stringify(completion), {});
    const streamedJson = converted.body
        .split(/\r?\n/)
        .find((line) => line.startsWith("data: {"))
        .slice("data: ".length);
    const streamed = JSON.parse(streamedJson);
    const content = streamed.choices[0].delta.content;

    assert.equal(converted.normalized, true);
    assert.ok(content.includes("INBOX_MESSAGE_JSON:"));
    assert.ok(content.includes('"actions":[]}'));
    assert.ok(!content.includes('"actions":[]}}'));
});

test("Local provider profile clamps OpenAI output budgets", () => {
    const body = normalizeRequestBody({
        model: "qwen2.5-coder:7b",
        max_tokens: 99999,
        messages: [
            { role: "user", content: "hello" }
        ]
    });

    assert.equal(body.max_tokens, 2048);
});

test("Local provider profile truncates oversized non-GPT local requests", () => {
    const body = normalizeRequestBody({
        model: "qwen2.5-0.5b-instruct-generic-cpu:4",
        messages: [
            { role: "system", content: "s".repeat(70000) },
            { role: "user", content: `What model are you?\n${"u".repeat(10000)}` }
        ]
    });
    const total = body.messages.reduce((sum, message) => sum + message.content.length, 0);

    assert.ok(total <= 65000);
    assert.ok(body.messages[1].content.startsWith("What model are you?"));
    assert.ok(body.messages.some((message) =>
        message.content.includes("Local provider profile truncated additional context")));
});

test("Foundry SSE chunks are normalized to plain OpenAI stream chunks", () => {
    const foundryStream = [
        'data: {"model":"qwen","choices":[{"delta":{"role":"assistant","content":"OK","tool_calls":[]},"message":{"role":"assistant","content":"OK","tool_calls":[]},"index":0}],"CreatedAt":"now","IsDelta":false,"Successful":true,"HttpStatusCode":0,"object":"chat.completion.chunk"}',
        "",
        "data: [DONE]",
        ""
    ].join("\n");

    const result = normalizeServerSentEvents(foundryStream);

    assert.equal(result.normalized, true);
    assert.ok(result.body.includes('"content":"OK"'));
    assert.ok(!result.body.includes('"message"'));
    assert.ok(!result.body.includes('"tool_calls":[]'));
    assert.ok(!result.body.includes('"Successful"'));
    assert.ok(result.body.includes('"finish_reason":"stop"'));
    assert.ok(result.body.indexOf('"finish_reason":"stop"') < result.body.indexOf("data: [DONE]"));
    assert.ok(result.body.includes("data: [DONE]"));
});

test("Foundry SSE normalization preserves existing finish reason chunks", () => {
    const foundryStream = [
        'data: {"model":"qwen","choices":[{"delta":{},"finish_reason":"stop","index":0}],"object":"chat.completion.chunk"}',
        "",
        "data: [DONE]",
        ""
    ].join("\n");

    const result = normalizeServerSentEvents(foundryStream);
    const finishReasonMatches = result.body.match(/"finish_reason":"stop"/g) ?? [];

    assert.equal(finishReasonMatches.length, 1);
    assert.ok(result.body.includes("data: [DONE]"));
});

test("Foundry SSE normalization keeps assistant role only once", () => {
    const foundryStream = [
        'data: {"model":"qwen","id":"chat.id.1","choices":[{"delta":{"role":"assistant","content":"Hel"},"index":0}],"object":"chat.completion.chunk"}',
        "",
        'data: {"model":"qwen","id":"chat.id.1","choices":[{"delta":{"role":"assistant","content":"lo"},"index":0}],"object":"chat.completion.chunk"}',
        "",
        "data: [DONE]",
        ""
    ].join("\n");

    const result = normalizeServerSentEvents(foundryStream);
    const roleMatches = result.body.match(/"role":"assistant"/g) ?? [];

    assert.equal(roleMatches.length, 1);
    assert.ok(result.body.includes('"content":"Hel"'));
    assert.ok(result.body.includes('"content":"lo"'));
    assert.ok(result.body.includes('"id":"chat.id.1"'));
    assert.ok(result.body.includes('"finish_reason":"stop"'));
});

test("Local model scheduler queues a second request for a single worker", async () => {
    const scheduler = new LocalModelRequestScheduler([
        {
            name: "ai-box-1",
            url: new URL("http://127.0.0.1:11434"),
            maxConcurrent: 1,
            active: 0
        }
    ]);

    const first = await scheduler.acquire();
    let secondResolved = false;
    const secondPromise = scheduler.acquire().then((lease) => {
        secondResolved = true;
        return lease;
    });

    await new Promise((resolve) => setImmediate(resolve));
    assert.equal(secondResolved, false);

    first.release();
    const second = await secondPromise;

    assert.equal(secondResolved, true);
    assert.equal(second.worker.name, "ai-box-1");
    assert.equal(second.queuedBefore, 0);
    second.release();
});

test("Local model scheduler uses multiple workers before queueing", async () => {
    const scheduler = new LocalModelRequestScheduler([
        {
            name: "ai-box-1",
            url: new URL("http://127.0.0.1:11434"),
            maxConcurrent: 1,
            active: 0
        },
        {
            name: "ai-box-2",
            url: new URL("http://127.0.0.2:11434"),
            maxConcurrent: 1,
            active: 0
        }
    ]);

    const first = await scheduler.acquire();
    const second = await scheduler.acquire();

    assert.equal(first.worker.name, "ai-box-1");
    assert.equal(second.worker.name, "ai-box-2");

    first.release();
    second.release();
});

test("Local model target parser accepts named semicolon-separated workers", () => {
    const workers = parseTargetWorkers(
        "fast=http://ai-box-1:11434;deep=http://ai-box-2:11434",
        new URL("http://fallback:11434"),
        1);

    assert.deepEqual(
        workers.map((worker) => [worker.name, worker.url.href, worker.maxConcurrent]),
        [
            ["fast", "http://ai-box-1:11434/", 1],
            ["deep", "http://ai-box-2:11434/", 1]
        ]);
});

test("GPT-OSS requests replace failed raw apply_patch history with recovery guidance", () => {
    const body = normalizeRequestBody({
        model: "gpt-oss:20b",
        messages: [
            { role: "system", content: "You are a coding assistant." },
            { role: "user", content: "Remove the completed task from .squad/tasks.md." },
            {
                role: "assistant",
                content: "",
                tool_calls: [{
                    id: "call_view",
                    type: "function",
                    function: {
                        name: "view",
                        arguments: JSON.stringify({ path: ".squad/tasks.md" })
                    }
                }]
            },
            {
                role: "tool",
                tool_call_id: "call_view",
                name: "view",
                content: "- [ ] [TEST] Verify critical priority icon rendering in Tasks panel and Markdown preview"
            },
            {
                role: "assistant",
                content: "",
                tool_calls: [{
                    id: "call_patch",
                    type: "function",
                    function: {
                        name: "apply_patch",
                        arguments: "*** Begin Patch\n*** Update File: .squad/tasks.md\n@@\n-- [ ] [TEST] Verify critical priority icon rendering in Tasks panel and Markdown preview\n*** End Patch"
                    }
                }]
            },
            {
                role: "tool",
                tool_call_id: "call_patch",
                name: "apply_patch",
                content: "Failed to parse patch: The first line of the patch must be '*** Begin Patch'."
            }
        ],
        tools: [
            {
                type: "function",
                function: {
                    name: "apply_patch",
                    parameters: {
                        type: "object",
                        properties: {
                            command: { type: "string" },
                            actions: { type: "array", items: { type: "object" } }
                        },
                        required: ["command", "actions"]
                    }
                }
            },
            {
                type: "function",
                function: {
                    name: "powershell",
                    parameters: {
                        type: "object",
                        properties: {
                            command: { type: "string" },
                            description: { type: "string" }
                        },
                        required: ["command", "description"]
                    }
                }
            }
        ]
    });

    assert.equal(
        body.messages.some((message) =>
            message.role === "assistant" &&
            message.tool_calls?.some((toolCall) => toolCall.function?.name === "apply_patch")),
        false);
    assert.equal(
        body.messages.some((message) =>
            message.role === "tool" &&
            String(message.content).includes("Failed to parse patch")),
        false);
    assert.ok(body.messages.some((message) =>
        message.role === "system" &&
        String(message.content).includes("[Local GPT-OSS tooling hint]")));
    assert.ok(body.messages.some((message) =>
        message.role === "user" &&
        String(message.content).includes("[Local GPT-OSS recovery]")));
});

test("GPT-OSS apply_patch recovery handles aliased assistant tool calls", () => {
    const body = normalizeRequestBody({
        model: "gpt-oss:20b",
        messages: [
            { role: "system", content: "You are a coding assistant." },
            { role: "user", content: "Remove the completed task from .squad/tasks.md." },
            {
                role: "assistant",
                content: "",
                tool_calls: [{
                    id: "call_edit",
                    type: "function",
                    function: {
                        name: "tool",
                        arguments: JSON.stringify({ text: "raw patch text" })
                    }
                }]
            },
            {
                role: "tool",
                tool_call_id: "call_edit",
                name: "tool",
                content: "Failed to parse patch: The first line of the patch must be '*** Begin Patch'."
            }
        ],
        tools: [
            {
                type: "function",
                function: {
                    name: "apply_patch",
                    parameters: {
                        type: "object",
                        properties: {
                            command: { type: "string" },
                            actions: { type: "array", items: { type: "object" } }
                        },
                        required: ["command", "actions"]
                    }
                }
            },
            {
                type: "function",
                function: {
                    name: "powershell",
                    parameters: {
                        type: "object",
                        properties: {
                            command: { type: "string" },
                            description: { type: "string" }
                        },
                        required: ["command", "description"]
                    }
                }
            }
        ]
    });

    assert.equal(
        body.messages.some((message) =>
            message.role === "assistant" &&
            message.tool_calls?.some((toolCall) => toolCall.id === "call_edit")),
        false);
    assert.equal(
        body.messages.at(-1).role,
        "user");
    assert.ok(String(body.messages.at(-1).content).includes("[Local GPT-OSS recovery]"));
});

test("GPT-OSS apply_patch recovery summarizes successful patch tool results", () => {
    const body = normalizeRequestBody({
        model: "gpt-oss:20b",
        messages: [
            { role: "system", content: "You are a coding assistant." },
            { role: "user", content: "Remove the completed task and commit the change." },
            {
                role: "assistant",
                content: "",
                tool_calls: [{
                    id: "call_patch",
                    type: "function",
                    function: {
                        name: "apply_patch",
                        arguments: "*** Begin Patch\n*** Update File: .squad/tasks.md\n@@\n-- [ ] [TEST] Verify critical priority icon rendering in Tasks panel and Markdown preview\n*** End Patch"
                    }
                }]
            },
            {
                role: "tool",
                tool_call_id: "call_patch",
                name: "apply_patch",
                content: "Modified 1 file(s): D:\\Drive\\Source\\SquadDash-public\\.squad\\tasks.md"
            }
        ],
        tools: [
            {
                type: "function",
                function: {
                    name: "apply_patch",
                    parameters: {
                        type: "object",
                        properties: {
                            command: { type: "string" },
                            actions: { type: "array", items: { type: "object" } }
                        },
                        required: ["command", "actions"]
                    }
                }
            },
            {
                type: "function",
                function: {
                    name: "powershell",
                    parameters: {
                        type: "object",
                        properties: {
                            command: { type: "string" },
                            description: { type: "string" }
                        },
                        required: ["command", "description"]
                    }
                }
            }
        ]
    });

    assert.equal(
        body.messages.some((message) =>
            message.role === "assistant" &&
            message.tool_calls?.some((toolCall) => toolCall.function?.name === "apply_patch")),
        false);
    assert.equal(
        body.messages.some((message) =>
            message.role === "tool" &&
            String(message.content).includes("Modified 1 file")),
        false);
    assert.equal(body.messages.at(-1).role, "user");
    assert.ok(String(body.messages.at(-1).content).includes("[Local GPT-OSS tool summary]"));
    assert.ok(String(body.messages.at(-1).content).includes("Modified 1 file(s)"));
});

test("GPT-OSS apply_patch recovery handles alternate patch parser failures", () => {
    const body = normalizeRequestBody({
        model: "gpt-oss:20b",
        messages: [
            { role: "system", content: "You are a coding assistant." },
            { role: "user", content: "Remove the completed task and commit the change." },
            {
                role: "assistant",
                content: "",
                tool_calls: [{
                    id: "call_patch",
                    type: "function",
                    function: {
                        name: "apply_patch",
                        arguments: JSON.stringify({ text: "malformed structured patch" })
                    }
                }]
            },
            {
                role: "tool",
                tool_call_id: "call_patch",
                name: "apply_patch",
                content: "Failed to parse patch: Expected update hunk to start with a @@ context marker, got: '> Update status inline (`- [ ]` -> `- [x]`). AI agents read this file for context.' (line 8)."
            }
        ],
        tools: [
            {
                type: "function",
                function: {
                    name: "apply_patch",
                    parameters: {
                        type: "object",
                        properties: {
                            command: { type: "string" },
                            actions: { type: "array", items: { type: "object" } }
                        },
                        required: ["command", "actions"]
                    }
                }
            },
            {
                type: "function",
                function: {
                    name: "powershell",
                    parameters: {
                        type: "object",
                        properties: {
                            command: { type: "string" },
                            description: { type: "string" }
                        },
                        required: ["command", "description"]
                    }
                }
            }
        ]
    });

    assert.equal(
        body.messages.some((message) =>
            message.role === "tool" &&
            String(message.content).includes("@@ context marker")),
        false);
    assert.equal(body.messages.at(-1).role, "user");
    assert.ok(String(body.messages.at(-1).content).includes("[Local GPT-OSS recovery]"));
});
