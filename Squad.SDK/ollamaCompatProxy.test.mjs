import assert from "node:assert/strict";
import test from "node:test";

import {
    applyCapabilityProfile,
    LocalModelRequestScheduler,
    normalizeRequestBody,
    normalizeServerSentEvents,
    parseTargetWorkers,
    resolveLocalCapabilityProfile,
    resolveLocalProviderProfile
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
            { type: "function", function: { name: "task" } }
        ]
    };

    const changed = applyCapabilityProfile(body, profile);

    assert.equal(changed, true);
    assert.deepEqual(
        body.tools.map((tool) => tool.function.name),
        ["view", "grep", "glob", "report_intent"]);
    assert.equal(body.tool_choice, undefined);
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
    assert.ok(result.body.includes("data: [DONE]"));
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
