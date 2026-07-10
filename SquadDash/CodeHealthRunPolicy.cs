namespace SquadDash;

internal static class CodeHealthRunPolicy {
    internal static bool CanStart(CodeHealthMdConfig config, bool isManual) =>
        isManual || config.EnabledOnIdle;
}
