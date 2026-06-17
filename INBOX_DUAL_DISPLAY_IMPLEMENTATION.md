# Inbox Action Dual-Display Implementation

## Overview

This implementation adds dual-display functionality for inbox-launched named agents. When an inbox action launches a subagent using `routeMode: "start_named_agent"`, the response now appears in **both** locations:

1. **Agent thread card** (existing behavior)
2. **Coordinator transcript** (new behavior)

## Implementation Details

### Changes Made

#### 1. TranscriptThreadState.cs
Added a new property to track whether a thread was launched from an inbox action:

```csharp
public bool LaunchedFromInbox { get; set; }
```

#### 2. MainWindow.xaml.cs

**a) Added tracking collection** (line ~273):
```csharp
private readonly HashSet<string> _inboxLaunchedAgentHandles = new(StringComparer.OrdinalIgnoreCase);
```

**b) Modified `DispatchInboxAction`** (line ~32061):
- When dispatching a `start_named_agent` inbox action, the target agent handle is added to `_inboxLaunchedAgentHandles`
- This tracking happens before calling `ExecuteNamedAgentDirectAsync`

**c) Modified `HandleSubagentStarted`** (line ~4934):
- When a subagent starts, checks if its agent name matches a tracked inbox-launched handle
- If matched, sets `thread.LaunchedFromInbox = true`
- Removes the handle from tracking set (one-time use)
- Logs the inbox-launch detection for diagnostics

**d) Modified `HandleSubagentCompleted`** (line ~5178):
- After finalizing the thread, checks if `thread.LaunchedFromInbox` is true
- If true AND response is non-empty:
  - Adds attribution line to Coordinator: `**{AgentName} responded:**`
  - Appends the full agent response to Coordinator transcript
  - Logs the dual-display action for diagnostics

## Flow Diagram

```
User clicks inbox action
  ↓
DispatchInboxAction (routeMode: "start_named_agent")
  ↓
Track agent handle in _inboxLaunchedAgentHandles
  ↓
ExecuteNamedAgentDirectAsync → Bridge starts agent
  ↓
subagent_started event
  ↓
HandleSubagentStarted
  - Creates/gets thread
  - Detects inbox launch via tracking set
  - Sets thread.LaunchedFromInbox = true
  - Removes from tracking set
  ↓
Agent executes and streams response to agent thread card
  ↓
subagent_completed event
  ↓
HandleSubagentCompleted
  - Finalizes agent thread card (existing behavior)
  - IF thread.LaunchedFromInbox:
      * Add attribution line to Coordinator transcript
      * Add full response to Coordinator transcript
  - Continue normal completion flow
```

## Benefits

1. **Immediate visibility**: Users see responses directly in the main transcript without needing to switch to the agent card
2. **Preserves detail**: Full agent thread with tools/progress remains available in the agent card
3. **Clear attribution**: Response is clearly labeled with the agent name in the Coordinator transcript
4. **Minimal overhead**: Only affects inbox-launched agents, not background agents from other sources
5. **Non-breaking**: Existing behavior is preserved; this only adds new display functionality

## Design Decisions

### Why track by handle instead of thread ID?
- Thread/agent ID is not available until the `subagent_started` event fires
- Inbox dispatch happens before the thread is created
- Tracking by handle (available at dispatch time) allows early registration

### Why remove from tracking set after match?
- Prevents memory leaks from accumulating handles
- Each inbox action should trigger exactly one agent start
- Clean slate for subsequent inbox actions

### Why show full response instead of summary?
- Provides complete context in the Coordinator view
- Users can still access thread card for detailed tool usage
- Consistent with how other messages appear in Coordinator

### Why only inbox-launched agents?
- User request specifically mentioned inbox actions
- Other background agents (from task tool, loop decompose, etc.) have different expectations
- Keeps change focused and testable
- Can be extended to other agent sources later if needed

## Testing Recommendations

1. **Happy path**: Click inbox action with `routeMode: "start_named_agent"` → verify response appears in both locations
2. **Attribution**: Verify agent name is correctly displayed in Coordinator attribution line
3. **Empty response**: Test with agent that returns empty/null response → should not add to Coordinator
4. **Multiple agents**: Launch multiple inbox agents in sequence → verify each is tracked independently
5. **Non-inbox agents**: Verify regular background agents (from task tool) are NOT dual-displayed
6. **Deferred dispatch**: Test when agent launch is deferred due to coordinator being busy

## Trace Diagnostics

The implementation includes trace logging at key points:

- **Inbox dispatch**: `[Inbox] DispatchInboxAction: direct named-agent launch target={agent}`
- **Thread marking**: `[Inbox] Marked agent thread as inbox-launched: {agent}`
- **Dual display**: `[Inbox] Added inbox-launched agent response to Coordinator transcript: {agent} ({chars} chars)`

These traces can be viewed in the trace window to verify correct operation.

## Future Enhancements

Potential improvements for future consideration:

1. **Collapsible responses**: Add UI affordance to collapse long responses in Coordinator
2. **Link to thread card**: Add clickable link from Coordinator message to agent thread card
3. **Summary mode**: Option to show summary instead of full response in Coordinator
4. **Extend to other sources**: Apply dual-display to other agent launch sources (e.g., task tool agents)
5. **Persistence**: Save `LaunchedFromInbox` flag to conversation store for session restoration

## Compatibility

- ✅ Backward compatible: No breaking changes to existing functionality
- ✅ Forward compatible: New field in TranscriptThreadState defaults to false
- ✅ No database changes required
- ✅ No configuration changes required

## Build Status

✅ Build succeeded with no warnings or errors (verified 2025-01-XX)
