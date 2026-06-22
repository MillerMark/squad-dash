namespace SquadDash;

internal static class SquadCliCommands {
    public const string LocalCliEntryPath = "node_modules\\@bradygaster\\squad-cli\\dist\\cli-entry.js";

    public static SquadCliCommandDefinition InstallLocalCli { get; } =
        new("cmd.exe", "/c npm install --save-dev @bradygaster/squad-cli", "Install local Squad CLI");

    public static SquadCliCommandDefinition Init { get; } =
        new("cmd.exe", "/c npx @bradygaster/squad-cli init", "Install Squad");

    public static SquadCliCommandDefinition Doctor { get; } =
        new("cmd.exe", "/c npx @bradygaster/squad-cli doctor", "Run Squad Doctor");

    public static SquadCliCommandDefinition WatchHealth { get; } =
        new("node", $"\"{LocalCliEntryPath}\" watch --health", "Check Squad Watch Health");

    public static SquadCliCommandDefinition StartWatch(
        int intervalMinutes,
        bool execute,
        bool verbose,
        string? notifyLevel) {
        var interval = Math.Clamp(intervalMinutes, 1, 1440);
        var arguments = $"\"{LocalCliEntryPath}\" watch --interval {interval}";
        if (execute)
            arguments += " --execute";
        if (verbose)
            arguments += " --verbose";
        if (!string.IsNullOrWhiteSpace(notifyLevel) &&
            notifyLevel is "all" or "important" or "none") {
            arguments += $" --notify-level {notifyLevel}";
        }

        return new SquadCliCommandDefinition("node", arguments, "Start Squad Watch");
    }

    public static SquadCliCommandDefinition DiscoverSquads { get; } =
        new("node", $"\"{LocalCliEntryPath}\" discover", "Discover Squads");
}

internal sealed record SquadCliCommandDefinition(
    string FileName,
    string Arguments,
    string DisplayName);
