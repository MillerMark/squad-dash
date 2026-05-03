import assert from "node:assert/strict";
import { test } from "node:test";
import { buildNamedAgentExecutionPrompt } from "./squadService.js";

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
