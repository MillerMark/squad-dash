using System.Text;

namespace SquadDash;

internal static class AgentNameHumanizer {
    public static string Humanize(string agentName) {
        if (string.IsNullOrWhiteSpace(agentName))
            return string.Empty;

        var trimmed = agentName.Trim();

        // If the name has no lowercase letters (e.g. "R2D2"), skip CamelCase splitting.
        var hasLowercase = trimmed.Any(char.IsLower);
        if (!hasLowercase)
            return trimmed.Replace('_', ' ').Trim();

        var builder = new StringBuilder(trimmed.Length + 8);

        for (var index = 0; index < trimmed.Length; index++) {
            var character = trimmed[index];
            var isMcBoundary =
                index >= 2 &&
                trimmed[index - 2] == 'M' &&
                trimmed[index - 1] == 'c';
            if (index > 0 &&
                !isMcBoundary &&
                char.IsUpper(character) &&
                trimmed[index - 1] != ' ' &&
                (char.IsLower(trimmed[index - 1]) ||
                 (index + 1 < trimmed.Length && char.IsLower(trimmed[index + 1])))) {
                builder.Append(' ');
            }

            builder.Append(character);
        }

        return builder.ToString().Replace('_', ' ').Replace('-', ' ').Trim();
    }
}
