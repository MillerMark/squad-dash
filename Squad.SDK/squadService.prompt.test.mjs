import assert from "node:assert/strict";
import { test } from "node:test";
import {
    approvePermissionRequest,
    buildNamedAgentExecutionPrompt,
    resolvePermissionApprovalKind,
    resolvePermissionApprovalKindFromSchema
} from "./squadService.js";

test("permission approval supports legacy Copilot runtime response shape", () => {
    assert.equal(resolvePermissionApprovalKind("1.0.24"), "approved");
});

test("permission approval supports current Copilot runtime response shape", () => {
    assert.equal(resolvePermissionApprovalKind("1.0.36"), "approve-once");
});

test("permission approval detects legacy Copilot schema", () => {
    const schema = JSON.stringify({
        session: {
            permissions: {
                handlePendingPermissionRequest: {
                    params: {
                        properties: {
                            result: {
                                anyOf: [
                                    { properties: { kind: { const: "approved" } } }
                                ]
                            }
                        }
                    }
                }
            }
        }
    });

    assert.equal(resolvePermissionApprovalKindFromSchema(schema), "approved");
});

test("permission approval detects current Copilot schema", () => {
    const schema = JSON.stringify({
        session: {
            permissions: {
                handlePendingPermissionRequest: {
                    params: {
                        properties: {
                            result: { $ref: "#/definitions/PermissionDecision" }
                        }
                    }
                }
            }
        },
        definitions: {
            PermissionDecision: {
                anyOf: [
                    { properties: { kind: { const: "approve-once" } } }
                ]
            }
        }
    });

    assert.equal(resolvePermissionApprovalKindFromSchema(schema), "approve-once");
});

test("permission approval returns a supported response shape", () => {
    assert.match(approvePermissionRequest().kind, /^(approved|approve-once)$/);
});

test("named-agent quick-reply prompt includes handoff in the submitted prompt", () => {
    const prompt = buildNamedAgentExecutionPrompt(
        "Sorin - implement the 3 optimizations",
        "sorin-pyre",
        [
            "SquadDash quick-reply handoff context.",
            "Source turn:",
            "1. ResolveSquadVersionAsync - cache or fire-and-forget",
            "2. OpenWorkspace - profile conversation load",
            "3. Mutex timeout on settings save during shutdown"
        ].join("\n"),
        "# Sorin Pyre\nPerformance Engineer");

    assert.match(prompt, /You are @sorin-pyre/);
    assert.match(prompt, /Visible quick reply selected by the user: "Sorin - implement the 3 optimizations"/);
    assert.match(prompt, /Treat this prompt as the complete task brief/);
    assert.match(prompt, /ResolveSquadVersionAsync - cache or fire-and-forget/);
    assert.match(prompt, /OpenWorkspace - profile conversation load/);
    assert.match(prompt, /Mutex timeout on settings save during shutdown/);
    assert.match(prompt, /# Sorin Pyre/);
});
