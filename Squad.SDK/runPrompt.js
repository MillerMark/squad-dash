import { spawn, spawnSync } from "node:child_process";
import { randomUUID } from "node:crypto";
import fs from "node:fs";
import http from "node:http";
import os from "node:os";
import path from "node:path";
import { fileURLToPath } from "node:url";
import readline from "node:readline";
import { SquadBridgeService } from "./squadService.js";
import { RemoteBridge, loadSubSquadsConfig, resolveSubSquad } from "@bradygaster/squad-sdk";
import { resolveGlobalSquadPath, resolvePersonalSquadDir } from "@bradygaster/squad-sdk/resolution";
import { resolvePersonalAgents } from "@bradygaster/squad-sdk/agents/personal";
import { initAgentModeTelemetry } from "@bradygaster/squad-sdk/runtime/otel-init";
const __dirname = path.dirname(fileURLToPath(import.meta.url));
let activeRemoteBridge = null;
let activeTunnelProc = null;
let activeLoopProc = null;
const bridge = new SquadBridgeService({
    onBackgroundTasksChanged(sessionId, tasks) {
        emit({
            type: "background_tasks_changed",
            sessionId,
            backgroundAgents: tasks.agents,
            backgroundShells: tasks.shells
        });
    },
    onTaskComplete(sessionId, summary) {
        emit({
            type: "task_complete",
            sessionId,
            summary
        });
    },
    onSubagentStarted(sessionId, subagent) {
        emit({
            type: "subagent_started",
            sessionId,
            toolCallId: subagent.toolCallId,
            agentId: subagent.agentId,
            agentName: subagent.agentName,
            agentDisplayName: subagent.agentDisplayName,
            agentDescription: subagent.agentDescription,
            prompt: subagent.prompt,
            model: subagent.model,
            totalToolCalls: subagent.totalToolCalls,
            totalTokens: subagent.totalTokens,
            durationMs: subagent.durationMs
        });
    },
    onSubagentCompleted(sessionId, subagent) {
        emit({
            type: "subagent_completed",
            sessionId,
            toolCallId: subagent.toolCallId,
            agentId: subagent.agentId,
            agentName: subagent.agentName,
            agentDisplayName: subagent.agentDisplayName,
            agentDescription: subagent.agentDescription,
            prompt: subagent.prompt,
            model: subagent.model,
            totalToolCalls: subagent.totalToolCalls,
            totalTokens: subagent.totalTokens,
            durationMs: subagent.durationMs
        });
    },
    onSubagentFailed(sessionId, subagent) {
        emit({
            type: "subagent_failed",
            sessionId,
            toolCallId: subagent.toolCallId,
            agentId: subagent.agentId,
            agentName: subagent.agentName,
            agentDisplayName: subagent.agentDisplayName,
            agentDescription: subagent.agentDescription,
            prompt: subagent.prompt,
            message: subagent.error,
            model: subagent.model,
            totalToolCalls: subagent.totalToolCalls,
            totalTokens: subagent.totalTokens,
            durationMs: subagent.durationMs
        });
    },
    onSubagentMessageDelta(sessionId, subagent) {
        emit({
            type: "subagent_message_delta",
            sessionId,
            parentToolCallId: subagent.parentToolCallId,
            agentId: subagent.agentId,
            agentName: subagent.agentName,
            agentDisplayName: subagent.agentDisplayName,
            agentDescription: subagent.agentDescription,
            chunk: subagent.text
        });
    },
    onSubagentMessage(sessionId, subagent) {
        emit({
            type: "subagent_message",
            sessionId,
            parentToolCallId: subagent.parentToolCallId,
            agentId: subagent.agentId,
            agentName: subagent.agentName,
            agentDisplayName: subagent.agentDisplayName,
            agentDescription: subagent.agentDescription,
            text: subagent.text,
            reasoningText: subagent.reasoningText
        });
    },
    onSubagentToolStart(sessionId, subagent, tool) {
        emit({
            type: "subagent_tool_start",
            sessionId,
            parentToolCallId: tool.parentToolCallId,
            agentId: subagent.agentId,
            agentName: subagent.agentName,
            agentDisplayName: subagent.agentDisplayName,
            agentDescription: subagent.agentDescription,
            toolCallId: tool.toolCallId,
            toolName: tool.toolName,
            startedAt: tool.startedAt,
            description: tool.description,
            command: tool.command,
            path: tool.path,
            intent: tool.intent,
            skill: tool.skill,
            args: tool.args
        });
    },
    onSubagentToolProgress(sessionId, subagent, tool) {
        emit({
            type: "subagent_tool_progress",
            sessionId,
            parentToolCallId: tool.parentToolCallId,
            agentId: subagent.agentId,
            agentName: subagent.agentName,
            agentDisplayName: subagent.agentDisplayName,
            agentDescription: subagent.agentDescription,
            toolCallId: tool.toolCallId,
            toolName: tool.toolName,
            startedAt: tool.startedAt,
            description: tool.description,
            command: tool.command,
            path: tool.path,
            intent: tool.intent,
            skill: tool.skill,
            progressMessage: tool.progressMessage,
            partialOutput: tool.partialOutput,
            args: tool.args
        });
    },
    onSubagentToolComplete(sessionId, subagent, tool) {
        emit({
            type: "subagent_tool_complete",
            sessionId,
            parentToolCallId: tool.parentToolCallId,
            agentId: subagent.agentId,
            agentName: subagent.agentName,
            agentDisplayName: subagent.agentDisplayName,
            agentDescription: subagent.agentDescription,
            toolCallId: tool.toolCallId,
            toolName: tool.toolName,
            startedAt: tool.startedAt,
            finishedAt: tool.finishedAt,
            description: tool.description,
            command: tool.command,
            path: tool.path,
            intent: tool.intent,
            skill: tool.skill,
            success: tool.success,
            outputText: tool.outputText,
            args: tool.args
        });
    },
    onWatchFleetDispatched(sessionId, info) {
        emit({
            type: "watch_fleet_dispatched",
            sessionId,
            watchCycleId: info.cycleId,
            watchFleetSize: info.fleetSize,
            prompt: info.prompt
        });
    },
    onWatchWaveDispatched(sessionId, info) {
        emit({
            type: "watch_wave_dispatched",
            sessionId,
            watchCycleId: info.cycleId,
            watchWaveIndex: info.waveIndex,
            watchWaveCount: info.waveCount,
            watchAgentCount: info.agentCount
        });
    },
    onWatchHydration(sessionId, info) {
        emit({
            type: "watch_hydration",
            sessionId,
            watchCycleId: info.cycleId,
            watchPhase: info.phase
        });
    },
    onWatchRetro(sessionId, info) {
        emit({
            type: "watch_retro",
            sessionId,
            watchCycleId: info.cycleId,
            watchRetroSummary: info.summary
        });
    },
    onWatchMonitorNotification(sessionId, info) {
        emit({
            type: "watch_monitor_notification",
            sessionId,
            watchCycleId: info.cycleId,
            watchNotificationChannel: info.channel,
            watchNotificationSent: info.sent,
            watchNotificationRecipient: info.recipient
        });
    }
});
function emit(event) {
    console.log(JSON.stringify(event));
}
function ensurePersonalSquadDir() {
    const personalDir = path.join(resolveGlobalSquadPath(), "personal-squad");
    const agentsDir = path.join(personalDir, "agents");
    if (!fs.existsSync(agentsDir)) {
        fs.mkdirSync(agentsDir, { recursive: true });
    }
    const configPath = path.join(personalDir, "config.json");
    if (!fs.existsSync(configPath)) {
        fs.writeFileSync(configPath, JSON.stringify({ defaultModel: "auto", ghostProtocol: true }, null, 2) + "\n", "utf8");
    }
    return personalDir;
}
function tryParsePromptRequest(parsed) {
    if (typeof parsed.prompt !== "string" || typeof parsed.cwd !== "string")
        return null;
    const prompt = parsed.prompt.trim();
    const cwd = parsed.cwd.trim();
    if (!prompt || !cwd)
        return null;
    const requestId = typeof parsed.requestId === "string" && parsed.requestId.trim().length > 0
        ? parsed.requestId.trim()
        : randomUUID();
    const sessionId = typeof parsed.sessionId === "string" && parsed.sessionId.trim().length > 0
        ? parsed.sessionId.trim()
        : undefined;
    const configDir = typeof parsed.configDir === "string" && parsed.configDir.trim().length > 0
        ? parsed.configDir.trim()
        : undefined;
    return {
        type: "prompt",
        requestId,
        prompt,
        cwd,
        sessionId,
        configDir
    };
}
function tryParseDelegateRequest(parsed) {
    if (typeof parsed.selectedOption !== "string" ||
        typeof parsed.targetAgent !== "string" ||
        typeof parsed.cwd !== "string" ||
        typeof parsed.sessionId !== "string") {
        return null;
    }
    const selectedOption = parsed.selectedOption.trim();
    const targetAgent = parsed.targetAgent.trim();
    const cwd = parsed.cwd.trim();
    const sessionId = parsed.sessionId.trim();
    if (!selectedOption || !targetAgent || !cwd || !sessionId)
        return null;
    const requestId = typeof parsed.requestId === "string" && parsed.requestId.trim().length > 0
        ? parsed.requestId.trim()
        : randomUUID();
    const configDir = typeof parsed.configDir === "string" && parsed.configDir.trim().length > 0
        ? parsed.configDir.trim()
        : undefined;
    return {
        type: "delegate",
        requestId,
        selectedOption,
        targetAgent,
        cwd,
        sessionId,
        configDir
    };
}
function tryParseNamedAgentRequest(parsed) {
    if (typeof parsed.targetAgent !== "string" || typeof parsed.selectedOption !== "string" || typeof parsed.cwd !== "string")
        return null;
    const targetAgent = parsed.targetAgent.trim();
    const selectedOption = parsed.selectedOption.trim();
    const cwd = parsed.cwd.trim();
    if (!targetAgent || !selectedOption || !cwd)
        return null;
    return {
        type: "named_agent",
        requestId: typeof parsed.requestId === "string" ? parsed.requestId.trim() || undefined : undefined,
        targetAgent,
        selectedOption,
        cwd,
        sessionId: typeof parsed.sessionId === "string" ? parsed.sessionId.trim() || undefined : undefined,
        configDir: typeof parsed.configDir === "string" ? parsed.configDir.trim() || undefined : undefined
    };
}
function tryParseRunLoopRequest(parsed) {
    if (typeof parsed.loopMdPath !== "string" || typeof parsed.cwd !== "string")
        return null;
    const loopMdPath = parsed.loopMdPath.trim();
    const cwd = parsed.cwd.trim();
    if (!loopMdPath || !cwd)
        return null;
    const requestId = typeof parsed.requestId === "string" && parsed.requestId.trim().length > 0
        ? parsed.requestId.trim()
        : randomUUID();
    const sessionId = typeof parsed.sessionId === "string" && parsed.sessionId.trim().length > 0
        ? parsed.sessionId.trim()
        : undefined;
    return {
        type: "run_loop",
        requestId,
        loopMdPath,
        cwd,
        sessionId
    };
}
function tryParseRcStartRequest(parsed) {
    if (typeof parsed.repo !== "string" || typeof parsed.branch !== "string" ||
        typeof parsed.machine !== "string" || typeof parsed.squadDir !== "string" ||
        typeof parsed.cwd !== "string")
        return null;
    const repo = parsed.repo.trim();
    const branch = parsed.branch.trim();
    const machine = parsed.machine.trim();
    const squadDir = parsed.squadDir.trim();
    const cwd = parsed.cwd.trim();
    if (!repo || !branch || !machine || !squadDir || !cwd)
        return null;
    return {
        type: "rc_start",
        requestId: typeof parsed.requestId === "string" && parsed.requestId.trim().length > 0
            ? parsed.requestId.trim()
            : undefined,
        port: typeof parsed.port === "number" ? parsed.port : 0,
        repo,
        branch,
        machine,
        squadDir,
        cwd,
        sessionId: typeof parsed.sessionId === "string" && parsed.sessionId.trim().length > 0
            ? parsed.sessionId.trim()
            : undefined,
        tunnelMode: parsed.tunnelMode === "ngrok" || parsed.tunnelMode === "cloudflare"
            ? parsed.tunnelMode
            : "none",
        tunnelToken: typeof parsed.tunnelToken === "string" && parsed.tunnelToken.trim().length > 0
            ? parsed.tunnelToken.trim()
            : undefined
    };
}
function tryParseRequest(line) {
    try {
        const parsed = JSON.parse(line);
        if (!parsed || typeof parsed !== "object")
            return null;
        if (parsed.type === "abort") {
            return {
                type: "abort",
                requestId: typeof parsed.requestId === "string" && parsed.requestId.trim().length > 0
                    ? parsed.requestId.trim()
                    : undefined,
                sessionId: typeof parsed.sessionId === "string" && parsed.sessionId.trim().length > 0
                    ? parsed.sessionId.trim()
                    : undefined
            };
        }
        if (parsed.type === "cancel_background_task") {
            return {
                type: "cancel_background_task",
                requestId: typeof parsed.requestId === "string" && parsed.requestId.trim().length > 0
                    ? parsed.requestId.trim()
                    : undefined,
                taskId: typeof parsed.taskId === "string"
                    ? parsed.taskId.trim()
                    : "",
                sessionId: typeof parsed.sessionId === "string" && parsed.sessionId.trim().length > 0
                    ? parsed.sessionId.trim()
                    : undefined
            };
        }
        if (parsed.type === "shutdown")
            return { type: "shutdown" };
        if (parsed.type === "delegate")
            return tryParseDelegateRequest(parsed);
        if (parsed.type === "named_agent")
            return tryParseNamedAgentRequest(parsed);
        if (parsed.type === "run_loop")
            return tryParseRunLoopRequest(parsed);
        if (parsed.type === "run_loop_stop") {
            return {
                type: "run_loop_stop",
                requestId: typeof parsed.requestId === "string" && parsed.requestId.trim().length > 0
                    ? parsed.requestId.trim()
                    : undefined
            };
        }
        if (parsed.type === "rc_start")
            return tryParseRcStartRequest(parsed);
        if (parsed.type === "rc_stop") {
            return {
                type: "rc_stop",
                requestId: typeof parsed.requestId === "string" && parsed.requestId.trim().length > 0
                    ? parsed.requestId.trim()
                    : undefined
            };
        }
        if (parsed.type === "rc_status_broadcast") {
            const req = parsed;
            const status = req.status === "busy" || req.status === "idle" ? req.status : null;
            if (!status)
                return null;
            return { type: "rc_status_broadcast", status };
        }
        if (parsed.type === "rc_agent_roster_broadcast") {
            const req = parsed;
            if (!Array.isArray(req.agents))
                return null;
            return { type: "rc_agent_roster_broadcast", agents: req.agents };
        }
        if (parsed.type === "rc_commit_broadcast") {
            const req = parsed;
            if (typeof req.sha !== "string" || !req.sha)
                return null;
            return { type: "rc_commit_broadcast", sha: req.sha, url: req.url };
        }
        if (parsed.type === "rc_prompt_broadcast") {
            const req = parsed;
            if (typeof req.text !== "string")
                return null;
            return { type: "rc_prompt_broadcast", text: req.text };
        }
        return tryParsePromptRequest(parsed);
    }
    catch {
        return null;
    }
}
function extractQuickReplies(content) {
    const idx = content.indexOf("QUICK_REPLIES_JSON:");
    if (idx === -1)
        return [];
    const jsonPart = content.slice(idx + "QUICK_REPLIES_JSON:".length).trim();
    try {
        const parsed = JSON.parse(jsonPart);
        if (Array.isArray(parsed)) {
            return parsed
                .filter((r) => r && typeof r.label === "string")
                .map(r => ({ label: r.label }));
        }
    }
    catch { /* ignore malformed */ }
    return [];
}
function stripQuickRepliesBlock(content) {
    const idx = content.indexOf("\nQUICK_REPLIES_JSON:");
    if (idx !== -1)
        return content.slice(0, idx).trimEnd();
    const idx2 = content.indexOf("QUICK_REPLIES_JSON:");
    if (idx2 !== -1)
        return content.slice(0, idx2).trimEnd();
    return content;
}
function stripRcNoise(content) {
    // Remove <system_notification>...</system_notification> blocks (machine-readable sentinels)
    return content.replace(/<system_notification>[\s\S]*?<\/system_notification>/g, "").trimEnd();
}
function stripLetMeSentences(content) {
    // Remove any sentence containing the phrase "let me" (whole-word, case-insensitive).
    // A sentence is bounded by .!? or newline; the trailing whitespace is consumed to avoid
    // double-spacing when the sentence is mid-paragraph.
    return content.replace(/[^.!?\n]*\blet\s+me\b[^.!?\n]*(?:[.!?]+\s*|\n|$)/gi, "").trimEnd();
}
function cleanForRc(content) {
    return stripLetMeSentences(stripRcNoise(stripQuickRepliesBlock(content)));
}
function buildRunHandlers(requestId, remoteBridge, agentHandle, agentDisplayName) {
    let startedThinking = false;
    let rcAccumulatedContent = "";
    let rcThinkingActive = false;
    let rcAgentContextSent = false;
    const rcSessionId = requestId ?? randomUUID();
    function maybeBroadcastAgentContext() {
        if (rcAgentContextSent || !remoteBridge)
            return;
        rcAgentContextSent = true;
        remoteBridge.broadcast({
            type: "agent_context",
            handle: agentHandle ?? "coordinator",
            displayName: agentDisplayName ?? "Coordinator"
        });
    }
    return {
        onSessionReady(session) {
            emit({
                type: "session_ready",
                requestId,
                sessionId: session.sessionId,
                sessionResumed: session.resumed,
                sessionReuseKind: session.sessionReuseKind,
                sessionAcquireDurationMs: session.sessionAcquireDurationMs,
                sessionResumeDurationMs: session.sessionResumeDurationMs,
                sessionCreateDurationMs: session.sessionCreateDurationMs,
                sessionResumeFailureMessage: session.sessionResumeFailureMessage,
                sessionAgeMs: session.sessionAgeMs,
                sessionPromptCountBeforeCurrent: session.sessionPromptCountBeforeCurrent,
                sessionPromptCountIncludingCurrent: session.sessionPromptCountIncludingCurrent,
                backgroundAgentCount: session.backgroundAgentCount,
                backgroundShellCount: session.backgroundShellCount,
                knownSubagentCount: session.knownSubagentCount,
                activeToolCount: session.activeToolCount,
                cachedAssistantChars: session.cachedAssistantChars
            });
        },
        onThinking(text, speaker) {
            if (!startedThinking) {
                emit({
                    type: "thinking_started",
                    requestId
                });
                startedThinking = true;
            }
            emit({
                type: "thinking_delta",
                requestId,
                text,
                speaker
            });
            if (remoteBridge && !rcThinkingActive) {
                rcThinkingActive = true;
                maybeBroadcastAgentContext();
                remoteBridge.broadcast({ type: "thinking_active" });
            }
        },
        onUsage(usage) {
            emit({
                type: "usage",
                requestId,
                model: usage.model,
                totalInputTokens: usage.inputTokens,
                totalOutputTokens: usage.outputTokens,
                totalTokens: usage.totalTokens
            });
        },
        onToolStart(tool) {
            emit({
                type: "tool_start",
                requestId,
                toolCallId: tool.toolCallId,
                toolName: tool.toolName,
                startedAt: tool.startedAt,
                description: tool.description,
                command: tool.command,
                path: tool.path,
                intent: tool.intent,
                skill: tool.skill,
                args: tool.args
            });
            remoteBridge?.sendToolCall("copilot", tool.toolName, tool.args ?? {}, "running");
        },
        onToolProgress(tool) {
            emit({
                type: "tool_progress",
                requestId,
                toolCallId: tool.toolCallId,
                toolName: tool.toolName,
                startedAt: tool.startedAt,
                description: tool.description,
                command: tool.command,
                path: tool.path,
                intent: tool.intent,
                skill: tool.skill,
                progressMessage: tool.progressMessage,
                partialOutput: tool.partialOutput,
                args: tool.args
            });
        },
        onToolComplete(tool) {
            emit({
                type: "tool_complete",
                requestId,
                toolCallId: tool.toolCallId,
                toolName: tool.toolName,
                startedAt: tool.startedAt,
                finishedAt: tool.finishedAt,
                description: tool.description,
                command: tool.command,
                path: tool.path,
                intent: tool.intent,
                skill: tool.skill,
                success: tool.success,
                outputText: tool.outputText,
                args: tool.args
            });
            remoteBridge?.sendToolCall("copilot", tool.toolName, tool.args ?? {}, tool.success ? "completed" : "error");
        },
        onDelta(chunk) {
            emit({
                type: "response_delta",
                requestId,
                chunk
            });
            if (remoteBridge) {
                if (rcThinkingActive) {
                    rcThinkingActive = false;
                    remoteBridge.broadcast({ type: "thinking_done" });
                }
                maybeBroadcastAgentContext();
                rcAccumulatedContent += chunk;
                remoteBridge.sendDelta(rcSessionId, "copilot", chunk);
            }
        },
        onDone() {
            emit({
                type: "done",
                requestId
            });
            if (remoteBridge && rcAccumulatedContent) {
                const quickReplies = extractQuickReplies(rcAccumulatedContent);
                const cleanContent = cleanForRc(rcAccumulatedContent);
                remoteBridge.addMessage("agent", cleanContent || rcAccumulatedContent);
                if (quickReplies.length > 0) {
                    remoteBridge.broadcast({ type: "quick_replies", replies: quickReplies });
                }
                rcAccumulatedContent = "";
            }
        },
        onAborted() {
            emit({
                type: "aborted",
                requestId
            });
            if (remoteBridge && rcAccumulatedContent) {
                const cleanContent = cleanForRc(rcAccumulatedContent);
                remoteBridge.addMessage("agent", (cleanContent || rcAccumulatedContent) + " [aborted]");
                rcAccumulatedContent = "";
            }
        }
    };
}
async function handleRunLoop(request) {
    const { requestId, sessionId, loopMdPath, cwd } = request;
    return new Promise((resolve, reject) => {
        let proc;
        let stopRequested = false;
        try {
            // On Windows, `copilot` is a .cmd script that execFile() can't find without a shell.
            // Passing --agent-cmd bypasses squad's broken preflight and routes via cmd.exe.
            proc = spawn("cmd.exe", ["/c", "npx", "squad", "loop", "--file", loopMdPath, "--agent-cmd", "cmd /c copilot"], {
                cwd,
                shell: false,
                stdio: ["ignore", "pipe", "pipe"]
            });
            activeLoopProc = proc;
        }
        catch (err) {
            emit({
                type: "loop_error",
                requestId,
                sessionId,
                loopMdPath,
                loopStatus: "error",
                message: err instanceof Error ? err.message : String(err)
            });
            reject(err);
            return;
        }
        emit({
            type: "loop_started",
            requestId,
            sessionId,
            loopMdPath,
            loopStatus: "running"
        });
        const rlOut = readline.createInterface({ input: proc.stdout, crlfDelay: Infinity });
        const rlErr = readline.createInterface({ input: proc.stderr, crlfDelay: Infinity });
        rlErr.on("line", (line) => {
            if (line.trim()) {
                emit({
                    type: "loop_output",
                    requestId,
                    sessionId,
                    loopMdPath,
                    outputLine: `[stderr] ${line}`
                });
            }
        });
        rlOut.on("line", (line) => {
            emit({
                type: "loop_output",
                requestId,
                sessionId,
                loopMdPath,
                outputLine: line
            });
            try {
                const parsed = JSON.parse(line);
                if (parsed && typeof parsed === "object" && ("iteration" in parsed || "type" in parsed)) {
                    const iterNum = typeof parsed["iteration"] === "number"
                        ? parsed["iteration"]
                        : undefined;
                    emit({
                        type: "loop_iteration",
                        requestId,
                        sessionId,
                        loopMdPath,
                        loopStatus: "running",
                        loopIteration: iterNum
                    });
                }
            }
            catch {
                // not JSON — no loop_iteration event
            }
        });
        proc.on("error", (err) => {
            emit({
                type: "loop_error",
                requestId,
                sessionId,
                loopMdPath,
                loopStatus: "error",
                message: err.message
            });
            resolve();
        });
        proc.on("close", (code) => {
            activeLoopProc = null;
            if (code === 0 || proc.killed || stopRequested) {
                emit({
                    type: "loop_stopped",
                    requestId,
                    sessionId,
                    loopMdPath,
                    loopStatus: "stopped"
                });
            }
            else {
                emit({
                    type: "loop_error",
                    requestId,
                    sessionId,
                    loopMdPath,
                    loopStatus: "error",
                    message: `squad loop exited with code ${code}`
                });
            }
            resolve();
        });
        // Expose setter so handleRunLoopStop can signal a clean stop
        proc._squadStopRequested = () => {
            stopRequested = true;
        };
    });
}
function handleRunLoopStop(request) {
    if (!activeLoopProc) {
        emit({
            type: "loop_stopped",
            requestId: request.requestId,
            loopStatus: "stopped"
        });
        return;
    }
    // Signal that the stop was user-requested so the close handler emits loop_stopped not loop_error.
    const procWithFlag = activeLoopProc;
    procWithFlag._squadStopRequested?.();
    // On Windows, cmd.exe /c spawns child processes that aren't killed by terminating cmd.exe.
    // Use taskkill /F /T to kill the entire process tree, then also call proc.kill() as backup.
    if (process.platform === "win32" && activeLoopProc.pid != null) {
        spawnSync("taskkill", ["/F", "/T", "/PID", String(activeLoopProc.pid)], { shell: false });
    }
    activeLoopProc.kill();
}
async function handlePrompt(request) {
    await bridge.runPrompt(request.prompt, buildRunHandlers(request.requestId, activeRemoteBridge ?? undefined, "coordinator", "Coordinator"), request);
}
async function handleDelegate(request) {
    const handle = request.targetAgent ?? "coordinator";
    const displayName = handle.split("-").map((w) => w.charAt(0).toUpperCase() + w.slice(1)).join(" ");
    await bridge.runDelegation(request, buildRunHandlers(request.requestId, activeRemoteBridge ?? undefined, handle, displayName));
}
function buildNamedAgentRunHandlers(requestId, toolCallId, agentHandle, agentDisplayName, coordinatorSessionId) {
    return {
        onDelta(chunk) {
            emit({
                type: "subagent_message_delta",
                sessionId: coordinatorSessionId,
                parentToolCallId: toolCallId,
                agentName: agentHandle,
                agentDisplayName,
                chunk
            });
        },
        onDone() {
            // subagent_completed is emitted by the caller after runNamedAgent resolves.
        },
        onThinking(text) {
            emit({
                type: "thinking_delta",
                requestId,
                text,
                speaker: agentHandle
            });
        },
        onToolStart(tool) {
            emit({
                type: "subagent_tool_start",
                sessionId: coordinatorSessionId,
                parentToolCallId: toolCallId,
                agentName: agentHandle,
                agentDisplayName,
                toolCallId: tool.toolCallId,
                toolName: tool.toolName,
                startedAt: tool.startedAt,
                description: tool.description,
                intent: tool.intent,
                skill: tool.skill,
                args: tool.args
            });
        },
        onToolComplete(tool) {
            emit({
                type: "subagent_tool_complete",
                sessionId: coordinatorSessionId,
                parentToolCallId: toolCallId,
                agentName: agentHandle,
                agentDisplayName,
                toolCallId: tool.toolCallId,
                toolName: tool.toolName,
                finishedAt: tool.finishedAt,
                success: tool.success,
                outputText: tool.outputText
            });
        },
        onAborted() {
            emit({ type: "aborted", requestId });
        }
    };
}
async function handleNamedAgent(request) {
    const handle = request.targetAgent.trim().replace(/^@+/, "").toLowerCase();
    const displayName = handle
        .split("-")
        .map((w) => w.charAt(0).toUpperCase() + w.slice(1))
        .join(" ");
    const namedAgentSessionId = `named-agent:${handle}`;
    let charterContent;
    const charterPath = path.join(request.cwd, ".squad", "agents", handle, "charter.md");
    try {
        charterContent = fs.readFileSync(charterPath, "utf-8");
    }
    catch { /* no charter, proceed without */ }
    const toolCallId = request.requestId ?? randomUUID();
    emit({
        type: "subagent_started",
        sessionId: request.sessionId,
        toolCallId,
        agentId: handle,
        agentName: handle,
        agentDisplayName: displayName,
        agentDescription: `Named agent: ${displayName}`,
        prompt: request.selectedOption
    });
    try {
        await bridge.runNamedAgent({
            cwd: request.cwd,
            selectedOption: request.selectedOption,
            targetAgent: handle,
            namedAgentSessionId,
            charterContent,
            configDir: request.configDir
        }, buildNamedAgentRunHandlers(request.requestId, toolCallId, handle, displayName, request.sessionId));
    }
    catch (err) {
        emit({
            type: "subagent_failed",
            sessionId: request.sessionId,
            toolCallId,
            agentId: handle,
            agentName: handle,
            agentDisplayName: displayName,
            message: err instanceof Error ? err.message : String(err)
        });
        return;
    }
    emit({
        type: "subagent_completed",
        sessionId: request.sessionId,
        toolCallId,
        agentId: handle,
        agentName: handle,
        agentDisplayName: displayName,
        prompt: request.selectedOption
    });
    // Signal the C# bridge that this request is fully complete.
    // Without this, SquadSdkProcess waits indefinitely for a "done" event.
    emit({
        type: "done",
        requestId: request.requestId
    });
}
function getLanIp() {
    const nets = os.networkInterfaces();
    for (const iface of Object.values(nets)) {
        if (!iface)
            continue;
        for (const addr of iface) {
            if (addr.family === "IPv4" && !addr.internal) {
                return addr.address;
            }
        }
    }
    return null;
}
const RC_FIREWALL_RULE_NAME = "SquadDash RC";
let activeFirewallPort = null;
function addWindowsFirewallRule(port) {
    if (process.platform !== "win32")
        return true; // non-Windows: no firewall rule needed
    try {
        // Delete any stale rule first (port may differ each session)
        spawnSync("netsh", ["advfirewall", "firewall", "delete", "rule", `name=${RC_FIREWALL_RULE_NAME}`], { timeout: 5000 });
        const result = spawnSync("netsh", [
            "advfirewall", "firewall", "add", "rule",
            `name=${RC_FIREWALL_RULE_NAME}`,
            "dir=in", "action=allow", "protocol=TCP",
            "localport=any",
            "profile=any"
        ], { timeout: 5000 });
        if (result.status === 0) {
            activeFirewallPort = port;
            return true;
        }
        return false;
    }
    catch {
        return false;
    }
}
function removeWindowsFirewallRule() {
    if (process.platform !== "win32" || activeFirewallPort === null)
        return;
    try {
        spawnSync("netsh", [
            "advfirewall", "firewall", "delete", "rule",
            `name=${RC_FIREWALL_RULE_NAME}`
        ], { timeout: 5000 });
    }
    catch { /* ignore */ }
    activeFirewallPort = null;
}
async function startTunnel(mode, token, port, requestId) {
    if (mode === "ngrok") {
        const args = ["http", String(port)];
        if (token)
            args.push("--authtoken", token);
        const proc = spawn("ngrok", args, { stdio: ["ignore", "pipe", "pipe"] });
        activeTunnelProc = proc;
        // Poll the ngrok local API for the public URL (up to 12 s)
        const url = await new Promise((resolve) => {
            let attempts = 0;
            const interval = setInterval(() => {
                attempts++;
                const req = http.get("http://localhost:4040/api/tunnels", (res) => {
                    let body = "";
                    res.on("data", (chunk) => { body += chunk.toString(); });
                    res.on("end", () => {
                        try {
                            const parsed = JSON.parse(body);
                            const httpsUrl = parsed.tunnels
                                ?.map(t => t.public_url)
                                .find(u => u?.startsWith("https://"));
                            if (httpsUrl) {
                                clearInterval(interval);
                                resolve(httpsUrl);
                            }
                        }
                        catch { /* not ready yet */ }
                    });
                });
                req.on("error", () => { });
                if (attempts >= 24) {
                    clearInterval(interval);
                    resolve(null);
                }
            }, 500);
        });
        if (url) {
            emit({ type: "rc_tunnel_started", requestId, rcTunnelUrl: url });
        }
        else {
            emit({ type: "rc_tunnel_error", requestId, message: "ngrok tunnel did not surface a public URL within 12 s. Is ngrok installed and authenticated?" });
        }
    }
    else {
        // cloudflared — ephemeral trycloudflare.com tunnel (no login required)
        const args = ["tunnel", "--url", `http://localhost:${port}`];
        const proc = spawn("cloudflared", args, { stdio: ["ignore", "pipe", "pipe"] });
        activeTunnelProc = proc;
        const url = await new Promise((resolve) => {
            let resolved = false;
            const timeout = setTimeout(() => { if (!resolved) {
                resolved = true;
                resolve(null);
            } }, 20_000);
            const handleChunk = (chunk) => {
                const text = chunk.toString();
                const match = /https:\/\/[\w-]+\.trycloudflare\.com/.exec(text);
                if (match && !resolved) {
                    resolved = true;
                    clearTimeout(timeout);
                    resolve(match[0]);
                }
            };
            proc.stdout?.on("data", handleChunk);
            proc.stderr?.on("data", handleChunk);
        });
        if (url) {
            emit({ type: "rc_tunnel_started", requestId, rcTunnelUrl: url });
        }
        else {
            emit({ type: "rc_tunnel_error", requestId, message: "cloudflared tunnel did not surface a public URL within 20 s. Is cloudflared installed?" });
        }
    }
}
async function handleRcStart(request) {
    if (activeRemoteBridge) {
        emit({
            type: "rc_error",
            requestId: request.requestId,
            message: "Remote bridge is already running. Stop it first with rc_stop."
        });
        return;
    }
    const rcBridge = new RemoteBridge({
        port: request.port ?? 0,
        maxHistory: 50,
        repo: request.repo,
        branch: request.branch,
        machine: request.machine,
        squadDir: request.squadDir,
        onPrompt: async (text) => {
            rcBridge.addMessage("user", text);
            emit({ type: "rc_prompt", text });
            // Execution is handled by SquadDash — it queues the prompt and drains
            // when the coordinator is free, preserving the normal queue ordering.
        },
        onAudioStart: (connectionId) => {
            emit({ type: "rc_audio_start", connectionId });
        },
        onAudioChunk: (data, connectionId) => {
            // Forward PCM bytes as base64-encoded NDJSON to C# side
            emit({ type: "rc_audio_chunk", connectionId, audioData: data.toString("base64") });
        },
        onAudioEnd: (connectionId) => {
            emit({ type: "rc_audio_end", connectionId });
        },
        onCommand: async (name, args) => {
            emit({
                type: "rc_command",
                requestId: request.requestId,
                name,
                args
            });
        }
    });
    // Restore the persistent token so the phone's saved QR link keeps working across restarts.
    if (request.rcToken && request.rcToken.trim().length > 0) {
        rcBridge.sessionToken = request.rcToken.trim();
    }
    // Serve the RC mobile web client from rc-client/
    const rcClientDir = path.join(__dirname, "rc-client");
    if (fs.existsSync(rcClientDir)) {
        rcBridge.setStaticHandler((req, res) => {
            const urlPath = req.url?.split("?")[0] ?? "/";
            const relativePath = urlPath === "/" ? "index.html" : urlPath.replace(/^\//, "");
            const filePath = path.join(rcClientDir, relativePath);
            // Security: ensure file is within rcClientDir
            const resolved = path.resolve(filePath);
            if (!resolved.startsWith(path.resolve(rcClientDir))) {
                res.writeHead(403);
                res.end("Forbidden");
                return;
            }
            if (!fs.existsSync(resolved)) {
                res.writeHead(404);
                res.end("Not found");
                return;
            }
            const ext = path.extname(resolved).toLowerCase();
            const mimeTypes = {
                ".html": "text/html; charset=utf-8",
                ".js": "application/javascript; charset=utf-8",
                ".css": "text/css; charset=utf-8",
                ".json": "application/json",
                ".png": "image/png",
                ".svg": "image/svg+xml",
                ".ico": "image/x-icon",
            };
            const contentType = mimeTypes[ext] ?? "application/octet-stream";
            res.writeHead(200, { "Content-Type": contentType });
            fs.createReadStream(resolved).pipe(res);
        });
    }
    try {
        const port = await rcBridge.start();
        activeRemoteBridge = rcBridge;
        const firewallRuleAdded = addWindowsFirewallRule(port);
        const lanIp = getLanIp();
        emit({
            type: "rc_started",
            requestId: request.requestId,
            rcPort: port,
            rcToken: rcBridge.getSessionToken(),
            rcUrl: `http://localhost:${port}`,
            rcLanUrl: lanIp ? `http://${lanIp}:${port}` : null,
            rcFirewallRuleAdded: firewallRuleAdded
        });
        if (request.tunnelMode && request.tunnelMode !== "none") {
            // Fire-and-forget: tunnel startup is non-blocking; errors are emitted as rc_tunnel_error
            startTunnel(request.tunnelMode, request.tunnelToken, port, request.requestId).catch((err) => {
                emit({ type: "rc_tunnel_error", requestId: request.requestId, message: err instanceof Error ? err.message : String(err) });
            });
        }
    }
    catch (err) {
        emit({
            type: "rc_error",
            requestId: request.requestId,
            message: err instanceof Error ? err.message : String(err)
        });
    }
}
async function handleRcStop(request) {
    // Kill the tunnel process if one is running
    if (activeTunnelProc) {
        try {
            activeTunnelProc.kill();
        }
        catch { /* ignore */ }
        activeTunnelProc = null;
    }
    if (!activeRemoteBridge) {
        emit({
            type: "rc_stopped",
            requestId: request.requestId
        });
        return;
    }
    try {
        await activeRemoteBridge.stop();
    }
    catch {
        // ignore stop errors — bridge may already be closed
    }
    removeWindowsFirewallRule();
    activeRemoteBridge = null;
    emit({
        type: "rc_stopped",
        requestId: request.requestId
    });
}
async function handleSubSquadsList(request) {
    try {
        const config = loadSubSquadsConfig(request.cwd);
        const resolved = resolveSubSquad(request.cwd);
        if (!config) {
            emit({
                type: "subsquads_listed",
                requestId: request.requestId,
                subsquadsConfigured: false,
                subsquadsCount: 0,
                workstreamsJson: null,
                activeSubsquadName: null,
                activeSubsquadSource: null
            });
            return;
        }
        emit({
            type: "subsquads_listed",
            requestId: request.requestId,
            subsquadsConfigured: true,
            subsquadsCount: config.workstreams.length,
            workstreamsJson: JSON.stringify(config.workstreams),
            activeSubsquadName: resolved?.name ?? null,
            activeSubsquadSource: resolved?.source ?? null
        });
    }
    catch (err) {
        emit({
            type: "subsquads_error",
            requestId: request.requestId,
            message: err instanceof Error ? err.message : String(err)
        });
    }
}
async function handleSubSquadsActivate(request) {
    try {
        const workstreamFilePath = path.join(request.cwd, ".squad-workstream");
        fs.writeFileSync(workstreamFilePath, request.subSquadName + "\n", "utf8");
        emit({
            type: "subsquads_activated",
            requestId: request.requestId,
            subSquadName: request.subSquadName
        });
    }
    catch (err) {
        emit({
            type: "subsquads_error",
            requestId: request.requestId,
            message: err instanceof Error ? err.message : String(err)
        });
    }
}
async function handlePersonalList(request) {
    try {
        const personalDir = resolvePersonalSquadDir();
        if (!personalDir) {
            emit({
                type: "personal_agents_listed",
                requestId: request.requestId,
                personalInitialized: false,
                personalAgentsCount: 0,
                personalAgentsJson: null,
                personalDir: null
            });
            return;
        }
        const agents = await resolvePersonalAgents();
        emit({
            type: "personal_agents_listed",
            requestId: request.requestId,
            personalInitialized: true,
            personalAgentsCount: agents.length,
            personalAgentsJson: JSON.stringify(agents.map(a => ({ name: a.name, role: a.role }))),
            personalDir
        });
    }
    catch (err) {
        emit({
            type: "personal_error",
            requestId: request.requestId,
            message: err instanceof Error ? err.message : String(err)
        });
    }
}
async function handlePersonalInit(request) {
    try {
        const personalDir = ensurePersonalSquadDir();
        emit({
            type: "personal_init_done",
            requestId: request.requestId,
            personalDir
        });
    }
    catch (err) {
        emit({
            type: "personal_error",
            requestId: request.requestId,
            message: err instanceof Error ? err.message : String(err)
        });
    }
}
async function main() {
    // Activate OTel if OTEL_EXPORTER_OTLP_ENDPOINT is set (e.g. by `squad aspire`).
    // No-op when env var is absent — zero runtime cost.
    const _telemetry = initAgentModeTelemetry();
    const directPrompt = process.argv.slice(2).join(" ").trim();
    if (directPrompt) {
        const directRequest = {
            type: "prompt",
            requestId: randomUUID(),
            prompt: directPrompt,
            cwd: process.cwd()
        };
        try {
            await handlePrompt(directRequest);
        }
        catch (err) {
            emit({
                type: "error",
                requestId: directRequest.requestId,
                message: err instanceof Error ? err.message : String(err)
            });
        }
        finally {
            await bridge.shutdown();
        }
        return;
    }
    const rl = readline.createInterface({
        input: process.stdin,
        output: process.stdout,
        terminal: false
    });
    let activePromptTask = null;
    let activeLoopTask = null;
    for await (const line of rl) {
        const request = tryParseRequest(line);
        if (!request)
            continue;
        if (request.type === "shutdown") {
            if (activePromptTask)
                await activePromptTask;
            await bridge.shutdown();
            return;
        }
        if (request.type === "abort") {
            try {
                await bridge.abortPrompt(request.sessionId);
            }
            catch (err) {
                emit({
                    type: "error",
                    message: err instanceof Error ? err.message : String(err)
                });
            }
            continue;
        }
        if (request.type === "cancel_background_task") {
            if (!request.taskId) {
                emit({
                    type: "error",
                    requestId: request.requestId,
                    message: "cancel_background_task requires a non-empty taskId."
                });
                continue;
            }
            try {
                const cancelled = await bridge.cancelBackgroundTask(request.taskId, request.sessionId);
                emit({
                    type: "background_task_cancelled",
                    requestId: request.requestId,
                    sessionId: request.sessionId,
                    taskId: request.taskId,
                    cancelled
                });
            }
            catch (err) {
                emit({
                    type: "error",
                    requestId: request.requestId,
                    message: err instanceof Error ? err.message : String(err)
                });
            }
            continue;
        }
        if (request.type === "run_loop") {
            if (activeLoopTask) {
                emit({
                    type: "loop_error",
                    requestId: request.requestId,
                    sessionId: request.sessionId,
                    loopMdPath: request.loopMdPath,
                    loopStatus: "error",
                    message: "A loop is already running"
                });
                continue;
            }
            activeLoopTask = handleRunLoop(request)
                .catch(err => {
                emit({
                    type: "loop_error",
                    requestId: request.requestId,
                    sessionId: request.sessionId,
                    loopMdPath: request.loopMdPath,
                    loopStatus: "error",
                    message: err instanceof Error ? err.message : String(err)
                });
            })
                .finally(() => {
                activeLoopTask = null;
            });
            continue;
        }
        if (request.type === "run_loop_stop") {
            handleRunLoopStop(request);
            continue;
        }
        if (request.type === "rc_start") {
            await handleRcStart(request);
            continue;
        }
        if (request.type === "rc_stop") {
            await handleRcStop(request);
            continue;
        }
        if (request.type === "rc_status_broadcast") {
            activeRemoteBridge?.broadcast?.({ type: "rc_status", status: request.status });
            continue;
        }
        if (request.type === "rc_agent_roster_broadcast") {
            activeRemoteBridge?.broadcast?.({ type: "agent_roster", agents: request.agents });
            continue;
        }
        if (request.type === "rc_commit_broadcast") {
            activeRemoteBridge?.broadcast?.({ type: "commit", sha: request.sha, url: request.url });
            continue;
        }
        if (request.type === "rc_prompt_broadcast") {
            activeRemoteBridge?.addMessage("user", request.text);
            continue;
        }
        if (request.type === "subsquads_list") {
            await handleSubSquadsList(request);
            continue;
        }
        if (request.type === "subsquads_activate") {
            await handleSubSquadsActivate(request);
            continue;
        }
        if (request.type === "personal_list") {
            await handlePersonalList(request);
            continue;
        }
        if (request.type === "personal_init") {
            await handlePersonalInit(request);
            continue;
        }
        if (activePromptTask) {
            emit({
                type: "error",
                requestId: request.requestId,
                message: "The Squad bridge is already processing another prompt."
            });
            continue;
        }
        activePromptTask = (request.type === "named_agent"
            ? handleNamedAgent(request)
            : request.type === "delegate"
                ? handleDelegate(request)
                : handlePrompt(request))
            .catch(err => {
            emit({
                type: "error",
                requestId: request.requestId,
                message: err instanceof Error ? err.message : String(err)
            });
        })
            .finally(() => {
            activePromptTask = null;
        });
    }
    if (activePromptTask)
        await activePromptTask;
    await bridge.shutdown();
}
main().catch(err => {
    emit({
        type: "error",
        message: err instanceof Error ? err.message : String(err)
    });
    process.exit(1);
});
