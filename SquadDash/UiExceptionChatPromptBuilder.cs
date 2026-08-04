namespace SquadDash;

internal static class UiExceptionChatPromptBuilder
{
    public static string Build() =>
        "Just saw this error inside SquadDash. Analyze the attached diagnostic context and relevant trace/code. " +
        "Do not classify it as a harmless framework bug merely because the stack is framework-only; correlate " +
        "the input route, window-state transition, and SquadDash event handlers, then identify whether application code should change.";
}
