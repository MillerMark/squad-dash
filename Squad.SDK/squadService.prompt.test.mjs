import assert from "node:assert/strict";
import { test } from "node:test";
import {
    approvePermissionRequest,
    buildNamedAgentExecutionPrompt,
    maybeRewritePendingRestartSelfBuildToolArgs,
    maybeRewritePowerShellToolArgs,
    normalizeAssistantResponseContent,
    resolvePermissionApprovalKind,
    resolvePermissionApprovalKindFromSchema,
    SquadBridgeService
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

test("runPrompt passes explicit model to session config", async () => {
    let capturedConfig;
    const session = {
        sessionId: "session-model",
        on() {},
        getBackgroundTasks: async () => [],
        sendAndWait: async () => ({ content: "done" })
    };
    const service = new SquadBridgeService();
    service.clientCwd = "D:\\Drive\\Source\\SquadDash-public";
    service.client = {
        createSession: async config => {
            capturedConfig = config;
            return session;
        }
    };

    await service.runPrompt(
        "status?",
        {},
        {
            cwd: "D:\\Drive\\Source\\SquadDash-public",
            model: "  claude-sonnet-4.6  "
        });

    assert.equal(capturedConfig.model, "claude-sonnet-4.6");
});

test("normalizes raw report tool JSON into assistant body text", () => {
    assert.equal(
        normalizeAssistantResponseContent(
            '{"name":"report","arguments":{"subject":"Hello World","from":"coordinator","body":"Hello, world!"}}'),
        "Hello, world!");
});

test("suppresses raw report_intent tool JSON when it appears as assistant text", () => {
    assert.equal(
        normalizeAssistantResponseContent(
            '{"name":"report_intent","arguments":{"intent":"Exploring codebase"}}'),
        undefined);
});

test("normalizes raw report_progress message JSON into assistant text", () => {
    assert.equal(
        normalizeAssistantResponseContent(
            '{"name":"report_progress","arguments":{"message":"Hello world!"}}'),
        "Hello world!");
});

test("suppresses raw report_model and task tool JSON when they appear as assistant text", () => {
    assert.equal(
        normalizeAssistantResponseContent(
            '{"name":"report_model","arguments":{}}'),
        undefined);

    assert.equal(
        normalizeAssistantResponseContent(
            '{"name":"task","arguments":{"name":"squad-tasks","description":"List all open tasks."}}'),
        undefined);
});

test("runPrompt buffers streamed raw report JSON and emits assistant body text", async () => {
    const listeners = new Map();
    const deltas = [];
    const session = {
        sessionId: "session-report-json",
        on(eventName, handler) {
            listeners.set(eventName, handler);
        },
        getBackgroundTasks: async () => [],
        sendAndWait: async () => {
            const chunks = [
                '{"name":"report","arguments":{"subject":"Hello World",',
                '"from":"coordinator","body":"Hello, world!"}}'
            ];
            for (const chunk of chunks)
                listeners.get("message_delta")?.({ deltaContent: chunk });

            return { content: chunks.join("") };
        }
    };
    const service = new SquadBridgeService();
    service.clientCwd = "D:\\Drive\\Source\\SquadDash-public";
    service.client = {
        createSession: async () => session
    };

    await service.runPrompt(
        "Can you say hello world?",
        {
            onDelta(chunk) {
                deltas.push(chunk);
            }
        },
        {
            cwd: "D:\\Drive\\Source\\SquadDash-public"
        });

    assert.deepEqual(deltas, ["Hello, world!"]);
});

test("runPrompt routes streamed local think tags as thinking deltas", async () => {
    const listeners = new Map();
    const thinkingDeltas = [];
    const deltas = [];
    const chunks = [
        "<thi",
        "nk>\nThinking ",
        "live.</thi",
        "nk>\nI am qwen3-14b-cuda-gpu."
    ];
    const session = {
        sessionId: "session-local-think",
        on(eventName, handler) {
            listeners.set(eventName, handler);
        },
        getBackgroundTasks: async () => [],
        sendAndWait: async () => {
            for (const chunk of chunks)
                listeners.get("message_delta")?.({ deltaContent: chunk });

            return { content: chunks.join("") };
        }
    };
    const service = new SquadBridgeService();
    service.clientCwd = "D:\\Drive\\Source\\SquadDash-public";
    service.client = {
        createSession: async () => session
    };

    await service.runPrompt(
        "What model are you?",
        {
            onThinking(chunk) {
                thinkingDeltas.push(chunk);
            },
            onDelta(chunk) {
                deltas.push(chunk);
            }
        },
        {
            cwd: "D:\\Drive\\Source\\SquadDash-public"
        });

    assert.deepEqual(thinkingDeltas, ["\nThinking ", "live."]);
    assert.deepEqual(deltas, ["I am qwen3-14b-cuda-gpu."]);
});

test("pending restart self-build hook disables run-slot deployment", () => {
    const rewrite = maybeRewritePendingRestartSelfBuildToolArgs(
        "powershell",
        JSON.stringify({
            command: "dotnet build .\\SquadDash\\SquadDash.csproj -c Debug",
            description: "Verify build"
        }),
        "D:\\Drive\\Source\\SquadDash-public",
        {
            SQUADDASH_APP_ROOT: "D:\\Drive\\Source\\SquadDash-public",
            SQUADDASH_RESTART_REQUEST_PATH: "D:\\restart-request.json"
        },
        filePath => filePath === "D:\\restart-request.json");

    assert.ok(rewrite);
    assert.equal(rewrite.reason, "restart-request-pending");
    assert.equal(rewrite.modifiedArgs.description, "Verify build");
    assert.match(rewrite.modifiedArgs.command, /EnableRunSlotDeployment='false'/);
    assert.match(rewrite.modifiedArgs.command, /restart request is already pending/);
    assert.match(rewrite.modifiedArgs.command, /dotnet build .\\SquadDash\\SquadDash.csproj -c Debug/);
});

test("powershell hook enables run-slot deployment for implicit release self-build", () => {
    const rewrite = maybeRewritePowerShellToolArgs(
        "powershell",
        {
            command: "cd 'D:\\Drive\\Source\\SquadDash-public' && dotnet build -c Release 2>&1 | Select-Object -First 100",
            description: "Build Release"
        },
        "D:\\Drive\\Source\\SquadDash-public",
        {
            SQUADDASH_APP_ROOT: "D:\\Drive\\Source\\SquadDash-public",
            SQUADDASH_RESTART_REQUEST_PATH: "D:\\restart-request.json"
        },
        () => false);

    assert.ok(rewrite);
    assert.equal(rewrite.reason, "self-build-deployment-enabled,native-exit-code-preserved");
    assert.match(rewrite.modifiedArgs.command, /EnableRunSlotDeployment='true'/);
    assert.match(rewrite.modifiedArgs.command, /Run-slot deployment enabled/);
    assert.match(rewrite.modifiedArgs.command, /\$LASTEXITCODE/);
});

test("powershell hook suppresses implicit self-build when restart is already pending", () => {
    const rewrite = maybeRewritePowerShellToolArgs(
        "powershell",
        {
            command: "dotnet build -c Release"
        },
        "D:\\Drive\\Source\\SquadDash-public",
        {
            SQUADDASH_APP_ROOT: "D:\\Drive\\Source\\SquadDash-public",
            SQUADDASH_RESTART_REQUEST_PATH: "D:\\restart-request.json"
        },
        filePath => filePath === "D:\\restart-request.json");

    assert.ok(rewrite);
    assert.equal(rewrite.reason, "restart-request-pending");
    assert.match(rewrite.modifiedArgs.command, /EnableRunSlotDeployment='false'/);
});

test("powershell hook preserves dotnet pipeline exit codes without enabling deployment for tests", () => {
    const rewrite = maybeRewritePowerShellToolArgs(
        "powershell",
        {
            command: "dotnet test --no-build -c Release 2>&1 | Select-Object -Last 100"
        },
        "D:\\Drive\\Source\\SquadDash-public",
        {
            SQUADDASH_APP_ROOT: "D:\\Drive\\Source\\SquadDash-public",
            SQUADDASH_RESTART_REQUEST_PATH: "D:\\restart-request.json"
        },
        () => false);

    assert.ok(rewrite);
    assert.equal(rewrite.reason, "native-exit-code-preserved");
    assert.doesNotMatch(rewrite.modifiedArgs.command, /EnableRunSlotDeployment='true'/);
    assert.match(rewrite.modifiedArgs.command, /\$LASTEXITCODE/);
    assert.match(rewrite.modifiedArgs.command, /exit \$LASTEXITCODE/);
});

test("powershell hook does not enable deployment for other explicit project builds", () => {
    const rewrite = maybeRewritePowerShellToolArgs(
        "powershell",
        {
            command: "dotnet build SquadConsole\\SquadConsole.csproj -c Release"
        },
        "D:\\Drive\\Source\\SquadDash-public",
        {
            SQUADDASH_APP_ROOT: "D:\\Drive\\Source\\SquadDash-public",
            SQUADDASH_RESTART_REQUEST_PATH: "D:\\restart-request.json"
        },
        () => false);

    assert.equal(rewrite, undefined);
});

test("pending restart self-build hook preserves explicit run-slot deployment choice", () => {
    const rewrite = maybeRewritePendingRestartSelfBuildToolArgs(
        "powershell",
        {
            command: "dotnet build .\\SquadDash\\SquadDash.csproj -p:EnableRunSlotDeployment=true"
        },
        "D:\\Drive\\Source\\SquadDash-public",
        {
            SQUADDASH_APP_ROOT: "D:\\Drive\\Source\\SquadDash-public",
            SQUADDASH_RESTART_REQUEST_PATH: "D:\\restart-request.json"
        },
        () => true);

    assert.equal(rewrite, undefined);
});

test("pending restart self-build hook ignores work outside the app root", () => {
    const rewrite = maybeRewritePendingRestartSelfBuildToolArgs(
        "powershell",
        {
            command: "dotnet build .\\SquadDash\\SquadDash.csproj -c Debug"
        },
        "D:\\Drive\\Source\\SomeOtherRepo",
        {
            SQUADDASH_APP_ROOT: "D:\\Drive\\Source\\SquadDash-public",
            SQUADDASH_RESTART_REQUEST_PATH: "D:\\restart-request.json"
        },
        () => true);

    assert.equal(rewrite, undefined);
});

test("pending restart self-build hook requires an existing restart request", () => {
    const rewrite = maybeRewritePendingRestartSelfBuildToolArgs(
        "powershell",
        {
            command: "dotnet build .\\SquadDash\\SquadDash.csproj -c Debug"
        },
        "D:\\Drive\\Source\\SquadDash-public",
        {
            SQUADDASH_APP_ROOT: "D:\\Drive\\Source\\SquadDash-public",
            SQUADDASH_RESTART_REQUEST_PATH: "D:\\restart-request.json"
        },
        () => false);

    assert.equal(rewrite, undefined);
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

test("background task cancellation retries paired tool call id when agent id is rejected", async () => {
    const service = new SquadBridgeService();
    const attempts = [];
    service.sessions.set("session-1", {
        session: {
            sessionId: "session-1",
            cancelBackgroundTask: async taskId => {
                attempts.push(taskId);
                return taskId === "tool-call-1";
            },
            getBackgroundTasks: async () => []
        },
        backgroundTasks: {
            agents: [
                {
                    agentId: "agent-1",
                    toolCallId: "tool-call-1"
                }
            ],
            shells: []
        }
    });

    const cancelled = await service.cancelBackgroundTask("agent-1");

    assert.equal(cancelled, true);
    assert.deepEqual(attempts, ["agent-1", "tool-call-1"]);
});

test("background task cancellation retries remembered task name for tool call id", async () => {
    const service = new SquadBridgeService();
    const attempts = [];
    service.sessions.set("session-1", {
        session: {
            sessionId: "session-1",
            cancelBackgroundTask: async taskId => {
                attempts.push(taskId);
                return taskId === "queue-held-restart-bug";
            },
            getBackgroundTasks: async () => []
        },
        backgroundTasks: {
            agents: [],
            shells: []
        },
        backgroundTaskIdsByToolCallId: new Map([
            ["toolu_bdrk_014NnSZxJapiCigA8z7DfSS3", "queue-held-restart-bug"]
        ])
    });

    const cancelled = await service.cancelBackgroundTask("toolu_bdrk_014NnSZxJapiCigA8z7DfSS3");

    assert.equal(cancelled, true);
    assert.deepEqual(attempts, [
        "toolu_bdrk_014NnSZxJapiCigA8z7DfSS3",
        "queue-held-restart-bug"
    ]);
});

test("background task cancel diagnostics include remembered mapping and snapshot matches", () => {
    const service = new SquadBridgeService();
    service.sessions.set("session-1", {
        session: {
            sessionId: "session-1"
        },
        backgroundTasks: {
            agents: [
                {
                    agentId: "queue-held-restart-bug",
                    toolCallId: "toolu_bdrk_014NnSZxJapiCigA8z7DfSS3"
                }
            ],
            shells: []
        },
        backgroundTaskIdsByToolCallId: new Map([
            ["toolu_bdrk_014NnSZxJapiCigA8z7DfSS3", "queue-held-restart-bug"]
        ])
    });

    const diagnostics = service.describeBackgroundCancelState("toolu_bdrk_014NnSZxJapiCigA8z7DfSS3");

    assert.match(diagnostics, /requested=toolu_bdrk_014NnSZxJapiCigA8z7DfSS3/);
    assert.match(diagnostics, /mappedTaskId=queue-held-restart-bug/);
    assert.match(diagnostics, /matchingAgents=queue-held-restart-bug\/toolu_bdrk_014NnSZxJapiCigA8z7DfSS3/);
});

test("subagent reasoning deltas stream as thinking without duplicating final reasoning", () => {
    const thinkingDeltas = [];
    const messages = [];
    const service = new SquadBridgeService({
        onSubagentThinkingDelta(sessionId, subagent) {
            thinkingDeltas.push({ sessionId, subagent });
        },
        onSubagentMessage(sessionId, subagent) {
            messages.push({ sessionId, subagent });
        }
    });
    const listeners = new Map();
    const session = {
        sessionId: "session-1",
        on(eventName, callback) {
            listeners.set(eventName, callback);
        }
    };
    const state = {
        session,
        activeTools: new Map(),
        backgroundTasks: { agents: [], shells: [] },
        subagentsByToolCallId: new Map([
            ["tool-call-1", {
                toolCallId: "tool-call-1",
                agentName: "wanda-review",
                agentDisplayName: "Wanda Review"
            }]
        ]),
        backgroundTaskIdsByToolCallId: new Map(),
        subagentReasoningByToolCallId: new Map(),
        lastAssistantMessageContent: "",
        createdAt: Date.now(),
        completedPromptCount: 0
    };

    service.attachSessionListeners(state);

    listeners.get("reasoning_delta")({
        parentToolCallId: "tool-call-1",
        deltaContent: "Checking "
    });
    listeners.get("reasoning_delta")({
        data: { deltaContent: "the failure path." },
        parentToolCallId: "tool-call-1"
    });
    listeners.get("message")({
        parentToolCallId: "tool-call-1",
        content: "Done.",
        reasoningText: "Checking the failure path."
    });

    assert.equal(thinkingDeltas.length, 2);
    assert.deepEqual(thinkingDeltas.map(item => item.subagent.reasoningText), [
        "Checking ",
        "the failure path."
    ]);
    assert.equal(messages.length, 1);
    assert.equal(messages[0].subagent.text, "Done.");
    assert.equal(messages[0].subagent.reasoningText, undefined);
});
