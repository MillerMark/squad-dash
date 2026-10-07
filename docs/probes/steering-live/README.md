# Live steering verification

This read-only probe verifies that an in-progress agent using a selected model
profile receives a steering message after its current tool completes. It does
not edit the workspace.

## Before starting

Assign the agent under test to the provider profile you want to verify. For an
Azure check, use a clearly named alias such as `Azure steering verification`.
Directly invoke that named agent so its provider assignment and transcript are
unambiguous. Avoid running unrelated agents during the probe.

## Initial prompt

Send this to the agent assigned to the profile under test:

```text
This is a read-only live steering probe. From the repository root, run this
command as one tool call and wait for it to finish:

powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\tools\steering-live-probe.ps1 -Seconds 30

Do not edit any files. After the command finishes, reply with exactly:
BASELINE-NO-STEER
```

## Steering prompt

While the target transcript/card shows the tool as running, invent a short token
that was not present in the initial prompt (for example, `AZURE-STEERED-4827`).
Enter the following in the SquadDash composer, replacing `<NEW-TOKEN>` with that
token, and press **Ctrl+Enter**. Tool output may be buffered, so use the running
tool indicator rather than waiting to see individual heartbeat lines:

```text
Change the required final response. After the current tool finishes, reply with
exactly <NEW-TOKEN> and do not include BASELINE-NO-STEER.
```

If the target picker appears, select the Azure-backed agent and press **Enter**.

## Passing observations

All of the following should occur:

1. The chosen agent's transcript gains a `↪️ Steering` entry containing the
   steering prompt. This entry is added only after the runtime accepts it.
2. The committed probe tool continues through `STEERING-PROBE TOOL-FINISHED`;
   steering does not abort it.
3. The agent's final response is exactly the new token and does not contain
   `BASELINE-NO-STEER`. Because the token was created only after the tool began,
   this is the evidence that the model consumed the steering message.
4. The completed transcript footer identifies the Azure profile alias selected
   for that agent.

The accepted entry proves that SquadDash and the SDK admitted the message to the
target session. The changed final response proves that the model subsequently
received and followed it. The profile footer proves which SquadDash route was
bound to that session. Provider-side Azure request metrics are the additional
independent check when proof of the remote endpoint itself is required.
