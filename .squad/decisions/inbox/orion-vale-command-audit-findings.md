# Decision: AI Command System Architecture

**Proposed By:** Orion Vale (Lead Architect)  
**Date:** 2026-05-01  
**Status:** Inbox - Awaiting Review  
**Impact:** High (affects command extension, AI integration reliability)

## Problem

The SquadDash AI-to-app command system has no centralized architecture:

1. **Documentation Injection (Loop-Only)**: Commands documented only within loop execution context (LoopController.cs:160-198 BuildAugmentedPrompt), not globally available to AI
2. **Fragile Parsing**: ExtractSquadashPayload uses regex instead of System.Text.Json for command extraction
3. **No Command Registry**: No discoverable registry, no metadata structure, no scope management
4. **Single Command Per Response**: Can only extract first matching command; multiple commands not supported
5. **Scattered Implementation**: Commands hardcoded in MainWindow.xaml.cs (stop_loop, start_loop at lines 3842, 3854)

## Recommended Solution

Implement **Unified CommandRegistry Pattern**:

```csharp
public enum CommandScope { Global, LoopOnly, BatchOnly }

public class CommandDefinition
{
    public string Name { get; set; }
    public CommandScope Scope { get; set; }
    public List<CommandParameter> Parameters { get; set; }
    public string Documentation { get; set; }
}

public interface ICommandRegistry
{
    void Register(CommandDefinition definition, Func<CommandContext, Task> handler);
    IEnumerable<CommandDefinition> GetDiscoverableCommands(CommandScope scope);
    Task<IEnumerable<CommandResult>> ExtractAndExecuteCommands(string aiResponse, CommandContext context);
}
```

## Benefits

- **Discoverability**: AI can query available commands per scope at loop start
- **Robustness**: JSON-based extraction replaces fragile regex
- **Extensibility**: Unified interface for new command addition
- **Multi-Command**: Support multiple commands per AI response
- **Documentation**: Commands self-document via registry metadata

## Related Items

- Command locations: MainWindow.xaml.cs:3842, MainWindow.xaml.cs:3854
- Parser: PushNotificationService.ExtractSquadashPayload (uses regex)
- Doc injection: LoopController.cs:160-198
