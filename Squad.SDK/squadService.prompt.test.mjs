import assert from "node:assert/strict";
import fs from "node:fs";
import os from "node:os";
import path from "node:path";
import { test } from "node:test";
import {
    approvePermissionRequest,
    analyzeProviderThrottle,
    buildDelegationHiddenContext,
    buildNamedAgentExecutionPrompt,
    buildNamedAgentPrompt,
    buildProviderNeutralCliEnvironment,
    computeProviderThrottleDelayMs,
    enrichCopilotAuthenticationFailure,
    evaluatePlanDelegationLaunch,
    findCounterfeitRosterTaskHandle,
    maybeRewritePendingRestartSelfBuildToolArgs,
    maybeRewritePowerShellToolArgs,
    matchesStagedPlanRevisionCandidate,
    normalizeAssistantResponseContent,
    reconcileStreamedAndFinalResponse,
    resolvePermissionApprovalKind,
    resolvePermissionApprovalKindFromSchema,
    reserveProviderRetryTimestamp,
    SquadBridgeService,
    StagedPlanRevisionHostProtocol,
    tryParseStagedPlanRevisionRequest
} from "./squadService.js";

test("plan delegation authorization validates the active phase, branch, worktree, and roster agent", () => {
    const guard = {
        knownPlans: [{ planId: "CLIDETECT-20260814", workItemIds: ["CLIDETECT-20260814-005"] }],
        active: {
            planId: "CLIDETECT-20260814",
            revision: "revision-5",
            workItemId: "CLIDETECT-20260814-005",
            phase: "implementation",
            attemptId: "attempt-5",
            branch: "feature/cli-detection-and-selection",
            currentBranch: "feature/cli-detection-and-selection",
            workspacePath: "D:\\Drive\\Source\\SquadDash-public",
            agentHandles: ["arjun-sen"]
        }
    };
    const validArgs = {
        agent_handle: "arjun-sen",
        prompt: "Complete CLIDETECT-20260814-005.",
        plan_execution_request: {
            planId: "CLIDETECT-20260814",
            revision: "revision-5",
            workItemId: "CLIDETECT-20260814-005",
            phase: "implementation",
            attemptId: "attempt-5",
            branch: "feature/cli-detection-and-selection"
        }
    };

    assert.deepEqual(evaluatePlanDelegationLaunch(
        validArgs, guard, "D:\\Drive\\Source\\SquadDash-public"), {
        planLaunch: true,
        allowed: true
    });
    assert.match(evaluatePlanDelegationLaunch(
        {
            ...validArgs,
            prompt: "SQUADDASH_AGENT_ASSIGNMENT_JSON:\n{}",
            plan_execution_request: undefined
        },
        guard,
        "D:\\Drive\\Source\\SquadDash-public").reason, /missing/);
    assert.match(evaluatePlanDelegationLaunch(
        { ...validArgs, agent_handle: "lyra-morn" },
        guard,
        "D:\\Drive\\Source\\SquadDash-public").reason, /roster agent/);
});

test("known plan task references do not turn ordinary roster delegation into plan execution", () => {
    const guard = {
        knownPlans: [{ planId: "CLIDETECT-20260814", workItemIds: ["CLIDETECT-20260814-005"] }]
    };

    assert.deepEqual(evaluatePlanDelegationLaunch({
        agent_handle: "arjun-sen",
        prompt: "Apply a bounded manual correction to CLIDETECT-20260814-005."
    }, guard, "D:\\Drive\\Source\\SquadDash-public"), {
        planLaunch: false,
        allowed: true
    });
});

for (const toolName of ["delegate_roster_agent", "delegate_temporary_agent"]) {
test(`${toolName} problematic plan launch receives one correction before requiring human approval`, async () => {
    const decisions = [];
    const service = new SquadBridgeService();
    service.clientCwd = "D:\\Drive\\Source\\SquadDash-public";
    service.client = {
        createSession: async config => ({
            sessionId: "session-plan-guard",
            on() {},
            getBackgroundTasks: async () => [],
            sendAndWait: async () => {
                const input = {
                    toolName,
                    toolArgs: {
                        agent_handle: "arjun-sen",
                        prompt: "SQUADDASH_AGENT_ASSIGNMENT_JSON:\n{}\nResume CLIDETECT-20260814-005 now."
                    },
                    cwd: "D:\\Drive\\Source\\SquadDash-public"
                };
                decisions.push(config.hooks.onPreToolUse(input));
                decisions.push(config.hooks.onPreToolUse(input));
                return { content: "Coordinator stopped for approval" };
            }
        })
    };

    await service.runPrompt("Resume the attached plan", {}, {
        cwd: "D:\\Drive\\Source\\SquadDash-public",
        namedAgentRoutes: [{ handle: "arjun-sen", displayName: "Arjun Sen" }],
        temporaryAgentRoute: { handle: "temporary-agent", displayName: "Temporary Agent" },
        planDelegationGuard: {
            knownPlans: [{
                planId: "CLIDETECT-20260814",
                workItemIds: ["CLIDETECT-20260814-005"]
            }]
        }
    });

    assert.equal(decisions[0].permissionDecision, "deny");
    assert.match(decisions[0].permissionDecisionReason, /DECOMPOSE_RECOVERY_JSON/);
    assert.equal(decisions[1].permissionDecision, "deny");
    assert.match(decisions[1].permissionDecisionReason, /explicit human approval/);
    assert.doesNotMatch(decisions[1].permissionDecisionReason, /Retry with/);
});
}

test("temporary delegation rejects counterfeit and host-owned launches at both hook and handler boundaries", async () => {
    const configs = [];
    const started = [];
    const service = new SquadBridgeService({
        onSubagentStarted(_sessionId, agent) { started.push(agent); }
    });
    const cwd = "D:\\Drive\\Source\\SquadDash-public";
    const active = {
        planId: "PLAN-1", revision: "revision-1", workItemId: "PLAN-1-001",
        phase: "implementation", attemptId: "attempt-1",
        branch: "feature/plan", currentBranch: "feature/plan",
        workspacePath: cwd, agentHandles: ["lyra-morn"]
    };
    const planRequest = {
        planId: active.planId, revision: active.revision, workItemId: active.workItemId,
        phase: active.phase, attemptId: active.attemptId, branch: active.branch
    };
    const cases = [
        { name: "lyra-review", prompt: "Review this code." },
        { prompt: "Act as Lyra Morn and implement this change." },
        { prompt: "SQUADDASH_AGENT_ASSIGNMENT_JSON:\n{}" },
        { prompt: "Implement the task.", plan_execution_request: { ...planRequest, revision: "stale" } },
        { prompt: "Implement the task.", agent_handle: "lyra-morn", plan_execution_request: planRequest }
    ];
    service.clientCwd = cwd;
    service.client = {
        createSession: async config => {
            configs.push(config);
            return {
                sessionId: "guard-session",
                on() {},
                getBackgroundTasks: async () => [],
                sendAndWait: async () => {
                    const tool = config.tools.find(tool => tool.name === "delegate_temporary_agent");
                    for (const toolArgs of cases) {
                        const decision = config.hooks.onPreToolUse({
                            toolName: "delegate_temporary_agent", toolArgs, cwd
                        });
                        assert.equal(decision.permissionDecision, "deny");
                        await assert.rejects(tool.handler(toolArgs, {
                            sessionId: "guard-session", toolCallId: "rejected-temporary"
                        }), /SquadDash rejected/);
                    }
                    assert.equal(config.hooks.onPreToolUse({
                        toolName: "delegate_temporary_agent",
                        toolArgs: { name: "scratch-review", prompt: "Review generic code." },
                        cwd
                    }), undefined);
                    return { content: "Rejected unauthorized launches" };
                }
            };
        }
    };

    await service.runPrompt("Review assignments", {}, {
        cwd,
        namedAgentRoutes: [{ handle: "lyra-morn", displayName: "Lyra Morn" }],
        temporaryAgentRoute: { handle: "temporary-agent", displayName: "Temporary Agent" },
        planDelegationGuard: { knownPlans: [], active }
    });
    assert.equal(configs.length, 1, "rejected assignments must never create a worker session");
    assert.equal(started.length, 0, "rejected assignments must never emit worker launch events");
});

test("auth diagnostics preserve a recent Copilot service failure instead of demanding sign-in", () => {
    const root = fs.mkdtempSync(path.join(os.tmpdir(), "squaddash-copilot-auth-"));
    try {
        const nowMs = Date.now();
        const logPath = path.join(root, "process-auth.log");
        fs.writeFileSync(logPath, [
            "Failed to fetch OAuth user login (503): Failed to fetch Copilot user info: 503",
            "GitHub returned: No server is currently available to service your request"
        ].join("\n"));
        fs.utimesSync(logPath, new Date(nowMs), new Date(nowMs));

        const result = enrichCopilotAuthenticationFailure(
            new Error("Session was not created with authentication info or custom provider"),
            { logDirectory: root, nowMs });

        assert.match(result.message, /authentication lookup failed with HTTP 503/i);
        assert.match(result.message, /No server is currently available/i);
        assert.doesNotMatch(result.message, /sign in again/i);
    }
    finally {
        fs.rmSync(root, { recursive: true, force: true });
    }
});

test("auth diagnostics remain conservative when no recent provider evidence exists", () => {
    const root = fs.mkdtempSync(path.join(os.tmpdir(), "squaddash-copilot-auth-empty-"));
    try {
        const result = enrichCopilotAuthenticationFailure(
            new Error("Session was not created with authentication info or custom provider"),
            { logDirectory: root, nowMs: Date.now() });

        assert.match(result.message, /authentication could not be confirmed/i);
        assert.doesNotMatch(result.message, /sign-in is required/i);
    }
    finally {
        fs.rmSync(root, { recursive: true, force: true });
    }
});

test("runPrompt enriches authentication failures raised after session creation", async () => {
    const service = new SquadBridgeService();
    service.clientCwd = "D:\\Drive\\Source\\SquadDash-public";
    service.client = {
        createSession: async () => ({
            sessionId: "session-auth-send",
            on() {},
            getBackgroundTasks: async () => [],
            sendAndWait: async () => {
                throw new Error("Session was not created with authentication info or custom provider");
            }
        })
    };

    await assert.rejects(
        service.runPrompt("validate", {}, { cwd: "D:\\Drive\\Source\\SquadDash-public" }),
        error => {
            assert.match(error.message, /Copilot authentication/i);
            return true;
        });
});

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

test("runPrompt passes explicit reasoning effort to session config", async () => {
    let capturedConfig;
    const session = {
        sessionId: "session-reasoning",
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
            reasoningEffort: "high"
        });

    assert.equal(capturedConfig.reasoningEffort, "high");
});

test("runPrompt passes max reasoning effort to session config", async () => {
    let capturedConfig;
    const session = {
        sessionId: "session-max-reasoning",
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
            reasoningEffort: "MAX"
        });

    assert.equal(capturedConfig.reasoningEffort, "max");
});

test("runPrompt omits auto reasoning effort from session config", async () => {
    let capturedConfig;
    const session = {
        sessionId: "session-auto-reasoning",
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
            reasoningEffort: "Auto"
        });

    assert.equal(capturedConfig.reasoningEffort, undefined);
});

test("runPrompt retries without reasoning effort on downlevel CLI", async () => {
    const configs = [];
    const session = {
        sessionId: "session-reasoning-fallback",
        on() {},
        getBackgroundTasks: async () => [],
        sendAndWait: async () => ({ content: "done" })
    };
    const service = new SquadBridgeService();
    service.clientCwd = "D:\\Drive\\Source\\SquadDash-public";
    service.client = {
        createSession: async config => {
            configs.push(config);
            if (configs.length === 1)
                throw new Error("Unknown option: reasoningEffort");
            return session;
        }
    };

    await service.runPrompt(
        "status?",
        {},
        {
            cwd: "D:\\Drive\\Source\\SquadDash-public",
            reasoningEffort: "xhigh"
        });

    assert.equal(configs.length, 2);
    assert.equal(configs[0].reasoningEffort, "xhigh");
    assert.equal(configs[1].reasoningEffort, undefined);
});

test("named roster tool preserves max reasoning effort in delegated session config", async () => {
    const configs = [];
    let sessionNumber = 0;
    const service = new SquadBridgeService();
    service.clientCwd = "D:\\Drive\\Source\\SquadDash-public";
    service.client = {
        createSession: async config => {
            configs.push(config);
            sessionNumber++;
            return {
                sessionId: `session-max-route-${sessionNumber}`,
                on() {},
                getBackgroundTasks: async () => [],
                sendAndWait: async () => ({ content: sessionNumber === 1 ? "Coordinator done" : "Rune report" })
            };
        }
    };

    await service.runPrompt("Delegate the profile work", {}, {
        cwd: "D:\\Drive\\Source\\SquadDash-public",
        namedAgentRoutes: [{
            handle: "talia-rune",
            displayName: "Talia Rune",
            role: "SDK bridge",
            charterContent: "# Talia Rune\nOwn SDK bridge work.",
            model: "gpt-5",
            reasoningEffort: "MAX"
        }]
    });

    const rosterTool = configs[0].tools.find(tool => tool.name === "delegate_roster_agent");
    assert.ok(rosterTool);
    const report = await rosterTool.handler({
        agent_handle: "talia-rune",
        prompt: "Inspect reasoning effort handling.",
        description: "Review reasoning effort handling"
    }, {
        sessionId: "session-max-route-1",
        toolCallId: "tool-talia",
        toolName: "delegate_roster_agent",
        arguments: {}
    });

    assert.equal(report, "Rune report");
    assert.equal(configs[1].reasoningEffort, "max");
});

test("named roster tool launches exact agent with its own provider profile", async () => {
    const configs = [];
    const started = [];
    const completed = [];
    let sessionNumber = 0;
    const service = new SquadBridgeService({
        onSubagentStarted(sessionId, agent) {
            started.push({ sessionId, agent });
        },
        onSubagentCompleted(sessionId, agent) {
            completed.push({ sessionId, agent });
        }
    });
    service.clientCwd = "D:\\Drive\\Source\\SquadDash-public";
    service.client = {
        createSession: async config => {
            configs.push(config);
            sessionNumber++;
            return {
                sessionId: `session-${sessionNumber}`,
                on() {},
                getBackgroundTasks: async () => [],
                sendAndWait: async () => ({ content: sessionNumber === 1 ? "Coordinator done" : "Lyra report" })
            };
        }
    };

    await service.runPrompt("Delegate the profile work", {}, {
        cwd: "D:\\Drive\\Source\\SquadDash-public",
        namedAgentRoutes: [{
            handle: "lyra-morn",
            displayName: "Lyra Morn",
            role: "Model profiles",
            charterContent: "# Lyra Morn\nOwn model profile work.",
            model: "gpt-5-mini",
            reasoningEffort: "high",
            provider: {
                baseUrl: "https://api.openai.example/v1",
                type: "openai",
                wireApi: "responses",
                apiKey: "test-key"
            },
            profileAlias: "OpenAI lightweight"
        }]
    });

    const rosterTool = configs[0].tools.find(tool => tool.name === "delegate_roster_agent");
    assert.ok(rosterTool);
    const report = await rosterTool.handler({
        agent_handle: "lyra-morn",
        prompt: "Inspect profile routing.",
        description: "Review model profile routing"
    }, {
        sessionId: "session-1",
        toolCallId: "tool-lyra",
        toolName: "delegate_roster_agent",
        arguments: {}
    });

    assert.equal(report, "Lyra report");
    assert.equal(configs[1].model, "gpt-5-mini");
    assert.equal(configs[1].reasoningEffort, "high");
    assert.equal(configs[1].provider.baseUrl, "https://api.openai.example/v1");
    assert.equal(configs[1].provider.apiKey, "test-key");
    assert.equal(started[0].agent.agentName, "lyra-morn");
    assert.equal(started[0].agent.agentDisplayName, "Lyra Morn");
    assert.equal(started[0].agent.profileAlias, "OpenAI lightweight");
    assert.equal(started[0].agent.providerBaseUrl, "https://api.openai.example/v1");
    assert.equal(started[0].agent.providerType, "openai");
    assert.equal(started[0].agent.wireApi, "responses");
    assert.equal(completed[0].agent.agentName, "lyra-morn");
});

test("temporary task tool uses the separately assigned profile without limiting launches", async () => {
    const configs = [];
    const started = [];
    let sessionNumber = 0;
    const service = new SquadBridgeService({
        onSubagentStarted(_sessionId, agent) {
            started.push(agent);
        }
    });
    service.clientCwd = "D:\\Drive\\Source\\SquadDash-public";
    service.client = {
        createSession: async config => {
            configs.push(config);
            sessionNumber++;
            return {
                sessionId: `session-${sessionNumber}`,
                on() {},
                getBackgroundTasks: async () => [],
                sendAndWait: async () => ({
                    content: sessionNumber === 1 ? "Coordinator done" : "Temporary review report"
                })
            };
        }
    };

    await service.runPrompt("Review the change in parallel", {}, {
        cwd: "D:\\Drive\\Source\\SquadDash-public",
        model: "gpt-6-astra",
        temporaryAgentRoute: {
            handle: "temporary-agent",
            displayName: "Temporary Agent",
            model: "claude-opus-4.8",
            profileId: "copilot-default",
            profileAlias: "Copilot"
        }
    });

    const temporaryTool = configs[0].tools.find(tool => tool.name === "delegate_temporary_agent");
    assert.ok(temporaryTool);
    const nativeTaskDecision = configs[0].hooks.onPreToolUse({
        toolName: "task",
        toolArgs: { name: "review-batch-3", prompt: "Review batch three." }
    });
    const report = await temporaryTool.handler({
        name: "review-batch-3",
        prompt: "Review batch three.",
        mode: "background"
    }, {
        sessionId: "session-1",
        toolCallId: "tool-review-3",
        toolName: "delegate_temporary_agent",
        arguments: {}
    });

    assert.equal(report, "Temporary review report");
    assert.equal(configs[1].model, "claude-opus-4.8");
    assert.equal(started[0].agentName, "review-batch-3");
    assert.equal(started[0].profileAlias, "Copilot");
    assert.equal(nativeTaskDecision.permissionDecision, "deny");
    assert.match(nativeTaskDecision.permissionDecisionReason, /delegate_temporary_agent/);
});

test("provider throttling distinguishes transient rate limits from terminal quota failures", () => {
    const transient = analyzeProviderThrottle(Object.assign(
        new Error("HTTP 429: tokens per minute rate limit exceeded"),
        { headers: { "retry-after": "2" } }));
    const terminal = analyzeProviderThrottle(
        new Error("HTTP 429 insufficient_quota: billing credits exhausted"));
    const unrelated = analyzeProviderThrottle(new Error("HTTP 401 unauthorized"));

    assert.deepEqual(transient, {
        retryable: true,
        retryAfterMs: 2_000,
        classification: "transient-rate-limit"
    });
    assert.equal(computeProviderThrottleDelayMs(transient, 0, 0), 2_000);
    const transientWithoutRetryAfter = analyzeProviderThrottle(
        new Error("HTTP 429: provider rate limit exceeded"));
    assert.deepEqual(
        [0, 1, 2, 3, 4].map(completedRetries =>
            computeProviderThrottleDelayMs(transientWithoutRetryAfter, completedRetries, 0)),
        [30_000, 60_000, 90_000, 120_000, 120_000]);
    assert.equal(
        computeProviderThrottleDelayMs({ ...transient, retryAfterMs: 0 }, 0, 0),
        30_000,
        "a zero provider retry delay must not cause another immediate burst");
    assert.equal(
        computeProviderThrottleDelayMs({ ...transient, retryAfterMs: 10 * 60_000 }, 0, 0),
        120_000,
        "one cooldown must remain capped at two minutes");
    assert.deepEqual(terminal, {
        retryable: false,
        classification: "terminal-quota"
    });
    assert.deepEqual(unrelated, {
        retryable: false,
        classification: "not-rate-limit"
    });
});

for (const entryPoint of ["roster", "direct"]) {
for (const namedProfileId of ["profile-b", "PROFILE-A"]) {
test(`${entryPoint} parent ${namedProfileId} compares temporary A against itself, not coordinator A`, async () => {
    const configs = [];
    const started = [];
    const cwd = "D:\\Drive\\Source\\SquadDash-public";
    const service = new SquadBridgeService({
        onSubagentStarted(_sessionId, agent) { started.push(agent); }
    });
    service.clientCwd = cwd;
    service.client = {
        createSession: async config => {
            configs.push(config);
            return {
                sessionId: `profile-session-${configs.length}`,
                on() {},
                getBackgroundTasks: async () => [],
                sendAndWait: async () => ({ content: "Profile report" })
            };
        }
    };
    const temporaryAgentRoute = {
        handle: "temporary-agent", displayName: "Temporary Agent",
        profileId: "profile-a", model: "model-a", reasoningEffort: "high"
    };
    const namedRoute = {
        handle: "lyra-morn", displayName: "Lyra Morn",
        profileId: namedProfileId, model: namedProfileId === "profile-b" ? "model-b" : "model-a",
        provider: namedProfileId === "profile-b"
            ? { type: "openai", baseUrl: "https://provider-b.example/v1" }
            : undefined
    };
    await service.runPrompt("Delegate work", {}, {
        cwd, model: "model-a", profileId: "profile-a",
        namedAgentRoutes: [namedRoute], temporaryAgentRoute
    });
    assert.equal(configs[0].tools.some(tool => tool.name === "delegate_temporary_agent"), false);
    assert.equal(configs[0].hooks.onPreToolUse({
        toolName: "task", toolArgs: { prompt: "Generic work." }
    }), undefined, "coordinator A keeps the existing native same-profile path");

    if (entryPoint === "roster") {
        await configs[0].tools.find(tool => tool.name === "delegate_roster_agent").handler({
            agent_handle: "lyra-morn", prompt: "Perform a bounded review."
        }, { sessionId: "profile-session-1", toolCallId: "named-review" });
    } else {
        await service.runNamedAgent({
            cwd, targetAgent: "lyra-morn", selectedOption: "Perform a bounded review.",
            agentRoute: namedRoute, temporaryAgentRoute
        }, {});
    }
    const temporaryTool = configs[1].tools?.find(tool => tool.name === "delegate_temporary_agent");
    const nativeDecision = configs[1].hooks.onPreToolUse({
        toolName: "task", toolArgs: { name: "scratch-review", prompt: "Generic work." }
    });
    if (namedProfileId === "PROFILE-A") {
        assert.equal(temporaryTool, undefined);
        assert.equal(nativeDecision, undefined, "named A also keeps native same-profile delegation");
        return;
    }
    assert.ok(temporaryTool, "named B must receive the retained temporary assignment A");
    assert.equal(nativeDecision.permissionDecision, "deny");
    await temporaryTool.handler({ name: "scratch-review", prompt: "Generic work." }, {
        sessionId: "profile-session-2", toolCallId: "temporary-review"
    });
    assert.equal(configs[1].model, "model-b");
    assert.equal(configs[2].model, "model-a");
    assert.equal(configs[2].reasoningEffort, "high");
    assert.equal(configs[2].provider, undefined, "temporary A must not inherit parent B's BYOK provider");
    assert.equal(started.at(-1).profileId, "profile-a");
    assert.equal(configs[2].tools?.some(tool => tool.name === "delegate_temporary_agent") ?? false, false);
});
}
}

test("successful workers release only their reservations without shortening sibling backoff", async t => {
    t.mock.timers.enable({ apis: ["Date", "setTimeout"], now: 1_000_000 });
    const configs = [];
    const throttled = [];
    const resumed = [];
    const sends = new Map();
    const success = Promise.withResolvers();
    const cwd = "D:\\Drive\\Source\\SquadDash-public";
    const service = new SquadBridgeService({
        onSubagentThrottled(_sessionId, agent) { throttled.push(agent); },
        onSubagentResumed(_sessionId, agent) { resumed.push(agent.agentName); }
    });
    service.clientCwd = cwd;
    service.client = {
        createSession: async config => {
            configs.push(config);
            const number = configs.length;
            return {
                sessionId: `retry-session-${number}`,
                on() {},
                getBackgroundTasks: async () => [],
                sendAndWait: async () => {
                    const attempt = (sends.get(number) ?? 0) + 1;
                    sends.set(number, attempt);
                    if (number === 2)
                        await success.promise;
                    if (number > 2 && attempt === 1)
                        throw new Error(`HTTP 429 rate limit exceeded; retry after ${(number - 2) * 1000} ms`);
                    return { content: `Worker ${number} completed` };
                }
            };
        }
    };
    const routes = ["successful", "first-waiter", "second-waiter"].map(handle => ({
        handle, displayName: handle, profileId: "shared", model: "shared-model"
    }));
    await service.runPrompt("Run concurrent workers", {}, { cwd, namedAgentRoutes: routes });
    const tool = configs[0].tools.find(tool => tool.name === "delegate_roster_agent");
    const launches = routes.map(route => tool.handler({
        agent_handle: route.handle, prompt: "Complete bounded work."
    }, { sessionId: "retry-session-1", toolCallId: route.handle }));
    const flush = () => new Promise(resolve => setImmediate(resolve));
    await flush();
    assert.equal(throttled.length, 2);
    const key = service.buildProviderRetryKey(routes[0]);
    const firstAt = Date.parse(throttled[0].retryAt);
    const secondAt = Date.parse(throttled[1].retryAt);

    success.resolve();
    await launches[0];
    assert.deepEqual(service.providerRetryAttempts.get(key), [firstAt, secondAt],
        "success without a retry must not delete either waiting worker's reservation");
    t.mock.timers.tick(250);
    await flush();
    assert.deepEqual(resumed, [], "a sibling's success must not resume throttled workers early");

    t.mock.timers.tick(firstAt - Date.now());
    await flush();
    await launches[1];
    assert.deepEqual(resumed, ["first-waiter"]);
    assert.deepEqual(service.providerRetryAttempts.get(key), [secondAt],
        "a recovered worker releases only its own reservation");
    t.mock.timers.tick(secondAt - Date.now() - 1);
    await flush();
    assert.deepEqual(resumed, ["first-waiter"], "the last worker must wait its entire reserved delay");
    t.mock.timers.tick(1);
    await flush();
    await launches[2];
    assert.deepEqual(resumed, ["first-waiter", "second-waiter"]);
    assert.equal(service.providerRetryAttempts.has(key), false);
});

test("named roster tool waits and resumes the same session after transient throttling", async () => {
    const configs = [];
    const throttled = [];
    const failed = [];
    const completed = [];
    const namedPrompts = [];
    let sessionNumber = 0;
    let namedAttempts = 0;
    const service = new SquadBridgeService({
        onSubagentThrottled(sessionId, agent) {
            throttled.push({ sessionId, agent });
        },
        onSubagentFailed(sessionId, agent) {
            failed.push({ sessionId, agent });
        },
        onSubagentCompleted(sessionId, agent) {
            completed.push({ sessionId, agent });
        }
    });
    service.clientCwd = "D:\\Drive\\Source\\SquadDash-public";
    service.client = {
        createSession: async config => {
            configs.push(config);
            sessionNumber++;
            const sessionId = `session-${sessionNumber}`;
            return {
                sessionId,
                on() {},
                getBackgroundTasks: async () => [],
                sendAndWait: async ({ prompt }) => {
                    if (sessionId === "session-1")
                        return { content: "Coordinator done" };

                    namedPrompts.push(prompt);
                    namedAttempts++;
                    if (namedAttempts === 1)
                        throw new Error("HTTP 429 tokens per minute rate limit exceeded; retry after 1 ms");
                    return { content: "Rory resumed report" };
                }
            };
        }
    };

    await service.runPrompt("Delegate planning", {}, {
        cwd: "D:\\Drive\\Source\\SquadDash-public",
        namedAgentRoutes: [{ handle: "rory", displayName: "Rory", role: "Planning Architect" }]
    });
    const rosterTool = configs[0].tools.find(tool => tool.name === "delegate_roster_agent");
    const report = await rosterTool.handler({
        agent_handle: "rory",
        prompt: "Inspect the repository and produce the implementation plan."
    }, {
        sessionId: "session-1",
        toolCallId: "tool-rory-throttle",
        toolName: "delegate_roster_agent",
        arguments: {}
    });

    assert.equal(report, "Rory resumed report");
    assert.equal(configs.length, 2, "retry must reuse the named-agent session instead of creating another one");
    assert.equal(namedPrompts.length, 2);
    assert.match(namedPrompts[0], /Inspect the repository and produce the implementation plan/);
    assert.match(namedPrompts[1], /Continue the delegated task from this existing session/);
    assert.doesNotMatch(namedPrompts[1], /Inspect the repository and produce the implementation plan/);
    assert.equal(throttled.length, 1);
    assert.equal(throttled[0].agent.retryCount, 1);
    assert.equal(throttled[0].agent.retryDelayMs, 1);
    assert.equal(failed.length, 0);
    assert.equal(completed.length, 1);
    assert.equal(completed[0].agent.retryCount, 1);
});

test("named roster tool resets its throttle streak after meaningful resumed work", async () => {
    const configs = [];
    const throttled = [];
    const completed = [];
    const listeners = new Map();
    let sessionNumber = 0;
    let namedAttempts = 0;
    const service = new SquadBridgeService({
        onSubagentThrottled(sessionId, agent) {
            throttled.push({ sessionId, agent });
        },
        onSubagentCompleted(sessionId, agent) {
            completed.push({ sessionId, agent });
        }
    });
    service.clientCwd = "D:\\Drive\\Source\\SquadDash-public";
    service.client = {
        createSession: async config => {
            configs.push(config);
            sessionNumber++;
            const sessionId = `session-${sessionNumber}`;
            return {
                sessionId,
                on(eventName, handler) {
                    listeners.set(eventName, handler);
                },
                getBackgroundTasks: async () => [],
                sendAndWait: async () => {
                    if (sessionId === "session-1")
                        return { content: "Coordinator done" };

                    namedAttempts++;
                    if (namedAttempts === 1)
                        throw new Error("HTTP 429 rate limit exceeded; retry after 1 ms");
                    if (namedAttempts === 2) {
                        listeners.get("reasoning_delta")?.({
                            deltaContent: "Repository inspection resumed successfully."
                        });
                        throw new Error("HTTP 429 rate limit exceeded; retry after 1 ms");
                    }

                    return { content: "Rory completed after two separate throttle incidents" };
                }
            };
        }
    };

    await service.runPrompt("Delegate planning", {}, {
        cwd: "D:\\Drive\\Source\\SquadDash-public",
        namedAgentRoutes: [{ handle: "rory", displayName: "Rory", role: "Planning Architect" }]
    });
    const rosterTool = configs[0].tools.find(tool => tool.name === "delegate_roster_agent");
    const report = await rosterTool.handler({
        agent_handle: "rory",
        prompt: "Inspect the repository and produce the implementation plan."
    }, {
        sessionId: "session-1",
        toolCallId: "tool-rory-throttle-reset",
        toolName: "delegate_roster_agent",
        arguments: {}
    });

    assert.equal(report, "Rory completed after two separate throttle incidents");
    assert.equal(configs.length, 2, "both retries must reuse the original named-agent session");
    assert.deepEqual(
        throttled.map(event => event.agent.retryCount),
        [1, 1],
        "meaningful work must reset the consecutive throttle streak");
    assert.equal(completed.length, 1);
    assert.equal(completed[0].agent.retryCount, 2, "lifetime retry telemetry remains cumulative");
});

test("named roster tool keeps the same session through more than four consecutive throttle cooldowns", async () => {
    const configs = [];
    const throttled = [];
    const resumed = [];
    let sessionNumber = 0;
    let namedAttempts = 0;
    const service = new SquadBridgeService({
        onSubagentThrottled(_sessionId, agent) {
            throttled.push(agent);
        },
        onSubagentResumed(_sessionId, agent) {
            resumed.push(agent);
        }
    });
    service.reserveProviderRetry = () => ({ key: "test-provider", scheduledAt: Date.now() + 1 });
    service.clientCwd = "D:\\Drive\\Source\\SquadDash-public";
    service.client = {
        createSession: async config => {
            configs.push(config);
            sessionNumber++;
            const sessionId = `session-${sessionNumber}`;
            return {
                sessionId,
                on() {},
                getBackgroundTasks: async () => [],
                sendAndWait: async () => {
                    if (sessionId === "session-1")
                        return { content: "Coordinator done" };

                    namedAttempts++;
                    if (namedAttempts <= 6)
                        throw new Error("HTTP 429 rate limit exceeded; retry after 1 ms");
                    return { content: "Rory completed after six cooldowns" };
                }
            };
        }
    };

    await service.runPrompt("Delegate planning", {}, {
        cwd: "D:\\Drive\\Source\\SquadDash-public",
        namedAgentRoutes: [{ handle: "rory", displayName: "Rory", role: "Planning Architect" }]
    });
    const rosterTool = configs[0].tools.find(tool => tool.name === "delegate_roster_agent");
    const report = await rosterTool.handler({
        agent_handle: "rory",
        prompt: "Inspect the repository and produce the implementation plan."
    }, {
        sessionId: "session-1",
        toolCallId: "tool-rory-six-cooldowns",
        toolName: "delegate_roster_agent",
        arguments: {}
    });

    assert.equal(report, "Rory completed after six cooldowns");
    assert.equal(configs.length, 2, "all cooldowns must retain the original named-agent session");
    assert.deepEqual(throttled.map(event => event.retryCount), [1, 2, 3, 4, 5, 6]);
    assert.deepEqual(resumed.map(event => event.retryCount), [1, 2, 3, 4, 5, 6]);
});

test("staged plan revision request correlation accepts marked and unmarked routing envelopes", () => {
    const prompt = `Revise the plan.\nSTAGED_PLAN_REVISION_REQUEST_JSON:\n\`\`\`json
    {
      "groupId": "CLIDETECT-20260814",
      "baseRevision": "base-1",
      "definition": ".squad/tmp/decompose/CLIDETECT-20260814.json",
      "protocol": ".squad/instructions/decompose-planning.md",
      "outline": ".squad/tmp/decompose/outlines/CLIDETECT-20260814.json",
      "sizeClass": "large"
    }
    \`\`\``;

    assert.deepEqual(tryParseStagedPlanRevisionRequest(prompt), {
        groupId: "CLIDETECT-20260814",
        baseRevision: "base-1"
    });
    const unmarked = `Revise the plan using this routing envelope:\n{
      "groupId": "CLIDETECT-20260814",
      "baseRevision": "base-1",
      "definition": ".squad/tmp/decompose/CLIDETECT-20260814.json",
      "protocol": ".squad/instructions/decompose-planning.md",
      "outline": ".squad/tmp/decompose/outlines/CLIDETECT-20260814.json",
      "sizeClass": "large"
    }`;
    assert.deepEqual(tryParseStagedPlanRevisionRequest(unmarked), {
        groupId: "CLIDETECT-20260814",
        baseRevision: "base-1"
    });
    assert.equal(tryParseStagedPlanRevisionRequest(prompt + "\n" + prompt), undefined);
    assert.equal(tryParseStagedPlanRevisionRequest(unmarked + "\n" + unmarked), undefined);
    assert.equal(tryParseStagedPlanRevisionRequest(unmarked.replace(
        ".squad/instructions/decompose-planning.md", ".squad/instructions/other.md")), undefined);
    assert.equal(tryParseStagedPlanRevisionRequest(unmarked.replace(
        "CLIDETECT-20260814.json", "OTHER-20260814.json")), undefined);
    assert.equal(tryParseStagedPlanRevisionRequest(unmarked.replace(
        "outlines/CLIDETECT-20260814.json", "outlines/OTHER-20260814.json")), undefined);
    assert.equal(tryParseStagedPlanRevisionRequest("No routing envelope"), undefined);
});

test("staged plan revision candidate must match the routed group and base revision", () => {
    const request = { groupId: "CLIDETECT-20260814", baseRevision: "base-1" };
    const matching = `STAGED_PLAN_REVISION_JSON:\n{
      "groupId": "CLIDETECT-20260814",
      "baseRevision": "base-1",
      "summary": "Update one task",
      "operations": [{"op":"updateTask","targetId":"CLIDETECT-20260814-001","patch":{"title":"Updated"}}]
    }`;

    assert.equal(matchesStagedPlanRevisionCandidate(matching, request), true);
    assert.equal(matchesStagedPlanRevisionCandidate(
        matching.replace('"base-1"', '"base-2"'), request), false);
    assert.equal(matchesStagedPlanRevisionCandidate(matching + "\n" + matching, request), false);
});

test("verified roster revisions use structural or tool-metadata correlation and bypass coordinator JSON", async () => {
    const configs = [];
    const completed = [];
    let sessionNumber = 0;
    const revision = `STAGED_PLAN_REVISION_JSON:\n{
      "groupId": "CLIDETECT-20260814",
      "baseRevision": "base-1",
      "summary": "Update one task",
      "operations": [{"op":"updateTask","targetId":"CLIDETECT-20260814-001","patch":{"title":"Updated"}}]
    }`;
    const service = new SquadBridgeService({
        onSubagentCompleted(sessionId, agent) {
            completed.push({ sessionId, agent });
        }
    });
    service.clientCwd = "D:\\Drive\\Source\\SquadDash-public";
    service.client = {
        createSession: async config => {
            configs.push(config);
            sessionNumber++;
            return {
                sessionId: `session-${sessionNumber}`,
                on() {},
                getBackgroundTasks: async () => [],
                sendAndWait: async () => ({ content: sessionNumber === 1 ? "Coordinator done" : revision })
            };
        }
    };

    await service.runPrompt("Delegate the revision", {}, {
        cwd: "D:\\Drive\\Source\\SquadDash-public",
        namedAgentRoutes: [{ handle: "rory", displayName: "Rory", role: "Planning Architect" }]
    });
    const rosterTool = configs[0].tools.find(tool => tool.name === "delegate_roster_agent");
    const receipt = await rosterTool.handler({
        agent_handle: "rory",
        prompt: `Apply the requested changes using this routing envelope:\n{
          "groupId": "CLIDETECT-20260814",
          "baseRevision": "base-1",
          "definition": ".squad/tmp/decompose/CLIDETECT-20260814.json",
          "protocol": ".squad/instructions/decompose-planning.md",
          "outline": ".squad/tmp/decompose/outlines/CLIDETECT-20260814.json",
          "sizeClass": "large"
        }`
    }, {
        sessionId: "session-1",
        toolCallId: "tool-rory-revision",
        toolName: "delegate_roster_agent",
        arguments: {}
    });

    assert.match(receipt, /received @rory's staged-plan revision candidate/);
    assert.doesNotMatch(receipt, /"operations"/);
    assert.equal(completed[0].agent.hostProtocolVerified, true);
    assert.equal(completed[0].agent.hostProtocol, StagedPlanRevisionHostProtocol);
    assert.equal(completed[0].agent.hostProtocolGroupId, "CLIDETECT-20260814");
    assert.equal(completed[0].agent.hostProtocolBaseRevision, "base-1");
    assert.equal(completed[0].agent.hostProtocolCorrelationSource, "structural_envelope");
    assert.equal(completed[0].agent.latestResponse, revision);

    const metadataReceipt = await rosterTool.handler({
        agent_handle: "rory",
        prompt: "Apply the requested staged-plan changes.",
        staged_plan_revision_request: {
            groupId: "CLIDETECT-20260814",
            baseRevision: "base-1",
            definition: ".squad/tmp/decompose/CLIDETECT-20260814.json",
            protocol: ".squad/instructions/decompose-planning.md",
            outline: ".squad/tmp/decompose/outlines/CLIDETECT-20260814.json",
            sizeClass: "large"
        }
    }, {
        sessionId: "session-1",
        toolCallId: "tool-rory-metadata-revision",
        toolName: "delegate_roster_agent",
        arguments: {}
    });

    assert.match(metadataReceipt, /received @rory's staged-plan revision candidate/);
    assert.doesNotMatch(metadataReceipt, /"operations"/);
    assert.equal(completed[1].agent.hostProtocolVerified, true);
    assert.equal(completed[1].agent.hostProtocolCorrelationSource, "tool_metadata");
    assert.equal(completed[1].agent.latestResponse, revision);

    const projectedProtocolReceipt = await rosterTool.handler({
        agent_handle: "rory",
        prompt: "Apply the requested staged-plan changes.",
        staged_plan_revision_request: {
            groupId: "CLIDETECT-20260814",
            baseRevision: "base-1",
            definition: ".squad/tmp/decompose/CLIDETECT-20260814.json",
            protocol: "C:\\Source\\Package\\.squad\\tmp\\host-instructions\\decompose-planning\\" +
                "370ed642e6edceebd8c43d09b22b66ddd8ac561afac53af45b0544ca05a8c8ba\\decompose-planning.md",
            outline: ".squad/tmp/decompose/outlines/CLIDETECT-20260814.json",
            sizeClass: "medium"
        }
    }, {
        sessionId: "session-1",
        toolCallId: "tool-rory-projected-protocol-revision",
        toolName: "delegate_roster_agent",
        arguments: {}
    });

    assert.match(projectedProtocolReceipt, /received @rory's staged-plan revision candidate/);
    assert.equal(completed[2].agent.hostProtocolVerified, true);
    assert.equal(completed[2].agent.hostProtocolCorrelationSource, "tool_metadata");
    assert.equal(completed[2].agent.latestResponse, revision);
});

test("provider retry budget counts only retries in its recent rolling window", () => {
    const now = 1_000_000;
    const windowMs = 120_000;
    const oldRetries = [now - windowMs - 50_000, now - windowMs - 1];
    const recentRetries = [now - 50_000, now - 40_000, now - 30_000, now - 20_000, now - 10_000];

    const withOldHistory = reserveProviderRetryTimestamp(oldRetries, now, 1, windowMs, 5);
    const saturated = reserveProviderRetryTimestamp(recentRetries, now, 1, windowMs, 5);

    assert.equal(withOldHistory.scheduledAt, now + 1,
        "expired retries must never delay new work");
    assert.deepEqual(withOldHistory.attempts, [now + 1]);
    assert.equal(saturated.scheduledAt, recentRetries[0] + windowMs + 1,
        "only a provider that exhausted the recent budget should wait for a slot");
});

test("counterfeit roster detection rejects roster-like and plan-bearing generic tasks", () => {
    const routes = [{ handle: "lyra-morn", displayName: "Lyra Morn" }];

    assert.equal(findCounterfeitRosterTaskHandle({
        name: "lyra-profile-fixes",
        prompt: "Review the model profile implementation."
    }, routes), "lyra-morn");
    assert.equal(findCounterfeitRosterTaskHandle({
        name: "implementation-worker",
        prompt: "SQUADDASH_AGENT_ASSIGNMENT_JSON:\n{\"agentHandle\":\"lyra-morn\"}"
    }, routes), "host-authorized-plan-assignment");
    assert.equal(findCounterfeitRosterTaskHandle({
        name: "temporary-research",
        prompt: "Research provider SDK behavior."
    }, routes), undefined);
});

test("CLI child environment does not inherit a coordinator session provider", () => {
    const environment = buildProviderNeutralCliEnvironment({
        PATH: "C:\\Tools",
        GITHUB_TOKEN: "github-token",
        COPILOT_PROVIDER_BASE_URL: "http://localhost:11434/v1",
        COPILOT_PROVIDER_API_KEY: "provider-key",
        COPILOT_PROVIDER_MODEL_ID: "local-model",
        COPILOT_OFFLINE: "true"
    });

    assert.equal(environment.PATH, "C:\\Tools");
    assert.equal(environment.GITHUB_TOKEN, "github-token");
    assert.equal(environment.COPILOT_PROVIDER_BASE_URL, undefined);
    assert.equal(environment.COPILOT_PROVIDER_API_KEY, undefined);
    assert.equal(environment.COPILOT_PROVIDER_MODEL_ID, undefined);
    assert.equal(environment.COPILOT_OFFLINE, undefined);
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

test("reconciles stale streamed progress with the authoritative final response", () => {
    const streamed = "Gathering context. Finalizing now. TASKS_JSON:\n{";
    const final = 'TASKS_JSON:\n{"groupId":"SQUADCLI-20260826","tasks":[]}';

    assert.equal(reconcileStreamedAndFinalResponse(streamed, final), final);
});

test("runNamedAgent returns and emits the complete final response after partial streaming", async () => {
    const listeners = new Map();
    const deltas = [];
    const streamed = "Gathering context. Finalizing now. TASKS_JSON:\n{";
    const final = 'TASKS_JSON:\n{"groupId":"SQUADCLI-20260826","tasks":[]}';
    const session = {
        sessionId: "session-named-agent-partial-stream",
        on(eventName, handler) {
            listeners.set(eventName, handler);
        },
        getBackgroundTasks: async () => [],
        sendAndWait: async () => {
            listeners.get("message_delta")?.({ deltaContent: streamed });
            return { content: final };
        }
    };
    const service = new SquadBridgeService();
    service.clientCwd = "D:\\Drive\\Source\\SquadDash-public";
    service.client = {
        createSession: async () => session
    };

    const response = await service.runNamedAgent(
        {
            cwd: "D:\\Drive\\Source\\SquadDash-public",
            selectedOption: "Plan the full 0.12 upgrade",
            targetAgent: "rory"
        },
        {
            onDelta(chunk) {
                deltas.push(chunk);
            }
        });

    assert.equal(response, final);
    assert.deepEqual(deltas, [final]);
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

test("named-agent prompt includes injected approval group context", () => {
    const prompt = buildNamedAgentPrompt({
        selectedOption: "Run verification",
        targetAgent: "vesper-knox",
        approvalGroupContext: [
            "SquadDash approval group context:",
            "- Guided Tour",
            "- Developer Experience"
        ].join("\n")
    });

    assert.match(prompt, /## Approval Group Context/);
    assert.match(prompt, /SquadDash approval group context/);
    assert.match(prompt, /- Guided Tour/);
    assert.match(prompt, /- Developer Experience/);
});

test("delegation hidden context includes injected approval group context", () => {
    const context = buildDelegationHiddenContext(
        "Ask Vesper to review this",
        "vesper-knox",
        [
            "SquadDash approval group context:",
            "- Guided Tour",
            "- Developer Experience"
        ].join("\n"));

    assert.match(context, /must launch @vesper-knox/);
    assert.match(context, /## Approval Group Context/);
    assert.match(context, /- Guided Tour/);
    assert.match(context, /- Developer Experience/);
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
