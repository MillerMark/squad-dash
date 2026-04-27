namespace SquadDash;

internal static class SquadCliCommands {
    public static SquadCliCommandDefinition InstallLocalCli { get; } =
        new("cmd.exe", "/c npm install --save-dev @bradygaster/squad-cli", "Install local Squad CLI");

    public static SquadCliCommandDefinition Init { get; } =
        new("cmd.exe", "/c npx @bradygaster/squad-cli init", "Install Squad");

    public static SquadCliCommandDefinition Doctor { get; } =
        new("cmd.exe", "/c npx @bradygaster/squad-cli doctor", "Run Squad Doctor");
}

internal sealed record SquadCliCommandDefinition(
    string FileName,
    string Arguments,
    string DisplayName);
