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

    public static SquadCliCommandDefinition DiscoverSquads { get; } =
        new("node", $"\"{LocalCliEntryPath}\" discover", "Discover Squads");
}

internal sealed record SquadCliCommandDefinition(
    string FileName,
    string Arguments,
    string DisplayName);
