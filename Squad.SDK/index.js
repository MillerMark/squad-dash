import { runPrompt } from "./squadService.js";
function emit(event) {
    console.log(JSON.stringify(event));
}
async function main() {
    const prompt = process.argv.slice(2).join(" ").trim();
    if (!prompt) {
        emit({
            type: "error",
            message: "No prompt provided."
        });
        process.exit(1);
    }
    let startedThinking = false;
    await runPrompt(prompt, {
        onThinking(text) {
            if (!startedThinking) {
                emit({ type: "thinking_started" });
                startedThinking = true;
            }
            emit({
                type: "thinking_delta",
                text
            });
        },
        onToolStart(tool) {
            emit({
                type: "tool_start",
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
                chunk
            });
        },
        onDone() {
            emit({
                type: "done"
            });
        }
    }, process.cwd());
}
main().catch(err => {
    emit({
        type: "error",
        message: err instanceof Error ? err.message : String(err)
    });
    process.exit(1);
});
