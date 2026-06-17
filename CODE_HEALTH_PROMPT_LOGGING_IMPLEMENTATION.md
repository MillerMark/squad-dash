# Code Health Prompt Diagnostic Logging - Implementation Summary

## Overview
Added diagnostic logging functionality that saves fully-evaluated code health task prompts to disk for debugging purposes. This helps diagnose issues where we need to see exactly what prompt text was sent to the AI after all template processing.

## Implementation Details

### New Files Created
1. **SquadDash/CodeHealthPromptLogger.cs**
   - Handles saving prompts to `.squad/diagnostics/prompts/` directory
   - Only operates in developer mode (checks `SquadDashEnvironment.IsDeveloperMode`)
   - Sanitizes task names for safe file naming
   - Includes optional metadata header (timestamp, task ID, workspace path, etc.)

2. **SquadDash.Tests/CodeHealthPromptLoggerTests.cs**
   - Unit tests for the logger
   - Tests developer mode behavior, file naming sanitization, error handling
   - All 5 tests pass

### Modified Files
1. **SquadDash/SquadSdkProcess.cs**
   - Added logging call in `RunNamedAgentDirectAsync` method
   - Logs just before sending prompt to AI bridge
   - Wrapped in try-catch to prevent logging failures from breaking prompt execution
   - Added `using System;` for DateTime support

2. **SquadDash/MainWindow.xaml.cs**
   - Fixed unrelated compilation error (`_currentWorkspaceIssue` → `ClearRuntimeIssue()`)

3. **SquadDash.Tests/SquadDash.Tests.csproj**
   - Added `CodeHealthPromptLogger.cs` to test project compilation

## File Naming Convention
Pattern: `{task-name}_{YYYYMMDD-HHMMSS}.txt`

Example: `speed-improvements_20260617-143052.txt`

## File Location
- **Production**: No files created (developer mode check)
- **Development**: `.squad/diagnostics/prompts/` in the workspace directory
- Directory is created automatically if it doesn't exist

## File Contents
Each log file contains:
1. **Metadata header** (if provided):
   - Task name/handle
   - Timestamp
   - Workspace path
   - Session ID
   - Handoff context length
   - Prompt length
2. **Separator line**
3. **Full evaluated prompt text** (after all template processing)

## Safety Features
- **Developer mode only**: Uses `SquadDashEnvironment.IsDeveloperMode` check
- **Error handling**: Wrapped in try-catch with trace logging
- **Non-blocking**: Logging failures don't prevent prompt execution
- **File name sanitization**: Invalid characters replaced with dashes

## Testing
- Created 5 unit tests covering:
  - Developer mode behavior
  - File name sanitization
  - Metadata formatting
  - Edge cases (empty names, null metadata)
- All tests pass
- Full test suite: 2451 tests pass

## Usage Example
When a code health task runs in developer mode, a file is automatically created:

**File**: `.squad/diagnostics/prompts/speed-improvements_20260617-143052.txt`

```
================================================================================
PROMPT DIAGNOSTIC METADATA
================================================================================
Task: speed-improvements
Timestamp: 2026-06-17 14:30:52
Workspace: D:\MyProject
Session ID: abc123def456
Handoff Context Length: 0 chars
Prompt Length: 4523 chars

================================================================================
EVALUATED PROMPT TEXT
================================================================================

[Full prompt text after all template processing, Handlebars evaluation, etc.]
```

## Integration Points
The logging happens at the lowest level before the prompt is sent to the AI:
1. User triggers code health task
2. Task instructions are loaded and processed through templates
3. PromptExecutionController calls bridge
4. **→ Logging happens here in SquadSdkProcess.RunNamedAgentDirectAsync**
5. Prompt sent to AI

This ensures we capture the exact text that goes to the AI, including all:
- LoopMdParser processing
- Handlebars template evaluation
- Conditional logic
- Variable substitution

## Future Enhancements (Optional)
- Add rotation/cleanup policy for old diagnostic files
- Include safety level in metadata
- Add option to log handoff context separately
- Create diagnostics viewer UI in dev mode
