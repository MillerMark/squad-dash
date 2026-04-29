import { spawn } from "node:child_process";
import { randomUUID } from "node:crypto";
import os from "node:os";
import readline from "node:readline";
import { SquadBridgeService } from "./squadService.js";
import { RemoteBridge } from "@bradygaster/squad-sdk";
let activeRemoteBridge = null;
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
        if (parsed.type === "run_loop")
            return tryParseRunLoopRequest(parsed);
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
        return tryParsePromptRequest(parsed);
    }
    catch {
        return null;
    }
}
function buildRunHandlers(requestId, remoteBridge) {
    let startedThinking = false;
    let rcAccumulatedContent = "";
    const rcSessionId = requestId ?? randomUUID();
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
                remoteBridge.addMessage("agent", rcAccumulatedContent);
                rcAccumulatedContent = "";
            }
        },
        onAborted() {
            emit({
                type: "aborted",
                requestId
            });
            if (remoteBridge && rcAccumulatedContent) {
                remoteBridge.addMessage("agent", rcAccumulatedContent + " [aborted]");
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
    // Use taskkill /F /T to kill the entire process tree.
    if (process.platform === "win32" && activeLoopProc.pid != null) {
        spawn("taskkill", ["/F", "/T", "/PID", String(activeLoopProc.pid)], { shell: false });
    }
    else {
        activeLoopProc.kill("SIGTERM");
    }
}
async function handlePrompt(request) {
    await bridge.runPrompt(request.prompt, buildRunHandlers(request.requestId), request);
}
async function handleDelegate(request) {
    await bridge.runDelegation(request, buildRunHandlers(request.requestId));
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
            const remoteRequestId = randomUUID();
            rcBridge.addMessage("user", text);
            try {
                await bridge.runPrompt(text, buildRunHandlers(remoteRequestId, rcBridge), { cwd: request.cwd, sessionId: request.sessionId });
            }
            catch (err) {
                rcBridge.sendError(err instanceof Error ? err.message : String(err));
            }
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
    try {
        const port = await rcBridge.start();
        activeRemoteBridge = rcBridge;
        const lanIp = getLanIp();
        emit({
            type: "rc_started",
            requestId: request.requestId,
            rcPort: port,
            rcToken: rcBridge.getSessionToken(),
            rcUrl: `http://localhost:${port}`,
            rcLanUrl: lanIp ? `http://${lanIp}:${port}` : null
        });
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
    activeRemoteBridge = null;
    emit({
        type: "rc_stopped",
        requestId: request.requestId
    });
}
async function main() {
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
        if (activePromptTask) {
            emit({
                type: "error",
                requestId: request.requestId,
                message: "The Squad bridge is already processing another prompt."
            });
            continue;
        }
        activePromptTask = (request.type === "delegate"
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
