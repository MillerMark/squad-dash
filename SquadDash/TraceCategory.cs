namespace SquadDash;

internal enum TraceCategory
{
    Scroll,
    PromptHealth,
    UI,
    Bridge,
    Load,
    Performance,
    AgentCards,
    Routing,
    Shutdown,
    Startup,
    Threads,
    TranscriptPanels,
    Unhandled,
    Workspace,
    Sound,
    Inbox,
    Docking,      // panel docking — slot hover preview geometry, zone rect calculations
    General,      // catch-all for unknown sources
}
