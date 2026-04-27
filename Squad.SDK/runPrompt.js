import { randomUUID } from "node:crypto";
import readline from "node:readline";
import { SquadBridgeService } from "./squadService.js";
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
        return tryParsePromptRequest(parsed);
    }
    catch {
        return null;
    }
}
function buildRunHandlers(requestId) {
    let startedThinking = false;
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
        },
        onDelta(chunk) {
            emit({
                type: "response_delta",
                requestId,
                chunk
            });
        },
        onDone() {
            emit({
                type: "done",
                requestId
            });
        },
        onAborted() {
            emit({
                type: "aborted",
                requestId
            });
        }
    };
}
async function handlePrompt(request) {
    await bridge.runPrompt(request.prompt, buildRunHandlers(request.requestId), request);
}
async function handleDelegate(request) {
    await bridge.runDelegation(request, buildRunHandlers(request.requestId));
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
