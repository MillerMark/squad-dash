import assert from "node:assert/strict";
import { test } from "node:test";
import { SteerRequestDispatcher, tryParsePromptAttachment, tryParseSteerRequest } from "./steeringProtocol.js";

test("steer protocol parses all SDK-supported attachment forms", () => {
    assert.deepEqual(tryParseSteerRequest({
        type: "steer",
        requestId: " steer-1 ",
        sessionId: " session-1 ",
        prompt: " Use these references. ",
        attachments: [
            { type: "file", path: " D:\\work\\notes.md ", displayName: " Notes " },
            { type: "directory", path: " D:\\work\\src " },
            {
                type: "selection",
                filePath: " D:\\work\\app.ts ",
                displayName: " app.ts:3 ",
                selection: {
                    start: { line: 2, character: 0 },
                    end: { line: 4, character: 12 }
                },
                text: " selected text "
            }
        ]
    }), {
        type: "steer",
        requestId: "steer-1",
        sessionId: "session-1",
        prompt: "Use these references.",
        attachments: [
            { type: "file", path: "D:\\work\\notes.md", displayName: "Notes" },
            { type: "directory", path: "D:\\work\\src" },
            {
                type: "selection",
                filePath: "D:\\work\\app.ts",
                displayName: "app.ts:3",
                selection: {
                    start: { line: 2, character: 0 },
                    end: { line: 4, character: 12 }
                },
                text: " selected text "
            }
        ]
    });
});

test("steer protocol rejects malformed requests and attachments", () => {
    assert.equal(tryParseSteerRequest({
        type: "steer",
        requestId: "steer-1",
        sessionId: "session-1",
        prompt: "   "
    }), null);
    assert.equal(tryParseSteerRequest({
        type: "steer",
        requestId: "steer-1",
        sessionId: "session-1",
        prompt: "Go",
        attachments: [{ type: "file", path: "" }]
    }), null);
    assert.equal(tryParsePromptAttachment({
        type: "selection",
        filePath: "app.ts",
        displayName: "app.ts",
        selection: {
            start: { line: -1, character: 0 },
            end: { line: 2, character: 0 }
        }
    }), null);
});

test("a hung steer dispatch does not block a command for another session", async () => {
    const firstDelivery = Promise.withResolvers();
    const results = [];
    const dispatcher = new SteerRequestDispatcher(
        request => request.requestId === "steer-hung" ? firstDelivery.promise : undefined,
        result => results.push(result));
    const firstRequest = {
        type: "steer",
        requestId: "steer-hung",
        sessionId: "provider-session",
        prompt: "Change direction."
    };

    dispatcher.dispatch(firstRequest);
    dispatcher.dispatch({
        ...firstRequest,
        requestId: "steer-next",
        sessionId: "other-provider-session",
        prompt: "Also keep the tests."
    });

    await new Promise(resolve => setImmediate(resolve));
    assert.deepEqual(results, [{
        type: "steer_result",
        requestId: "steer-next",
        sessionId: "other-provider-session",
        accepted: true
    }]);

    firstDelivery.resolve();
    await new Promise(resolve => setImmediate(resolve));
    assert.deepEqual(results[1], {
        type: "steer_result",
        requestId: "steer-hung",
        sessionId: "provider-session",
        accepted: true
    });
});

test("steer dispatch reports late rejection as an isolated steer result", async () => {
    const delivery = Promise.withResolvers();
    const results = [];
    const dispatcher = new SteerRequestDispatcher(
        () => delivery.promise,
        result => results.push(result));
    dispatcher.dispatch({
        type: "steer",
        requestId: "steer-late",
        sessionId: "provider-session",
        prompt: "Too late."
    });

    delivery.reject(new Error("target finished"));
    await new Promise(resolve => setImmediate(resolve));

    assert.deepEqual(results, [{
        type: "steer_result",
        requestId: "steer-late",
        sessionId: "provider-session",
        accepted: false,
        message: "target finished"
    }]);
});
