namespace SquadDash;

/// <summary>
/// Formats host-provided recovery commit references without exposing this pure transformation
/// through the WPF window solely for tests.
/// </summary>
internal static class PlanRecoveryCommitLogFormatter
{
    internal static string Format(
        string logOutput,
        IReadOnlyList<PlanRecoveryCommitReference>? commitReferences)
    {
        if (commitReferences is not { Count: > 0 })
            return logOutput.Trim();

        var idByCommit = commitReferences.ToDictionary(
            reference => reference.Commit,
            reference => reference.Id,
            StringComparer.OrdinalIgnoreCase);
        return string.Join(
            "\n",
            logOutput
                .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(line =>
                {
                    var separator = line.IndexOf('\t');
                    var commit = separator < 0 ? line : line[..separator];
                    return idByCommit.TryGetValue(commit, out var id)
                        ? $"{id}\t{line}"
                        : line;
                }));
    }
}
