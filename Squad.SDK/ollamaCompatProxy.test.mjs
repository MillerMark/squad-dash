import assert from "node:assert/strict";
import test from "node:test";

import { normalizeRequestBody } from "./ollamaCompatProxy.mjs";

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
