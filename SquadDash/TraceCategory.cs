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
    Callouts,     // callout shape geometry — triangle point calculation, dangle side selection, placement angle
    ImageEditor,  // clipboard image annotation editor — zoom, scroll, layout diagnostics
    General,      // catch-all for unknown sources
}
