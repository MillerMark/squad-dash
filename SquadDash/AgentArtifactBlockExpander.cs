using System.Text;

namespace SquadDash;

internal static class AgentArtifactBlockExpander
{
    internal static string ExpandDisplayArtifacts(string text, string applicationRoot)
    {
        if (string.IsNullOrWhiteSpace(text) ||
            !text.Contains(AgentArtifactStore.DisplayArtifactMarker, StringComparison.Ordinal))
            return text;

        var expanded = text;
        for (var i = 0; i < 8; i++)
        {
            if (!StructuredJsonBlockParser.TryExtractObject<AgentArtifactReference>(
                    expanded,
                    AgentArtifactStore.DisplayArtifactMarker,
                    out var extraction) ||
                extraction is null)
                break;

            var replacement = BuildReplacement(applicationRoot, extraction.Payload);
            expanded = Combine(
                extraction.TextBeforeBlock,
                replacement,
                extraction.TrailingText);
        }

        return expanded;
    }

    private static string BuildReplacement(string applicationRoot, AgentArtifactReference reference)
    {
        if (!AgentArtifactStore.TryMaterialize(
                applicationRoot,
                reference,
                AgentArtifactStore.DefaultMaxDisplayBytes,
                archive: true,
                out var artifact,
                out var error) ||
            artifact is null)
        {
            return $"> Artifact could not be loaded: {error}";
        }

        var language = AgentArtifactStore.NormalizeLanguage(reference.Language);
        var fence = BuildFence(artifact.Content);
        var builder = new StringBuilder();
        if (!string.IsNullOrWhiteSpace(reference.Label))
            builder.AppendLine($"**{reference.Label.Trim()}**");

        builder.Append(fence);
        builder.Append(language);
        builder.AppendLine();
        builder.AppendLine(artifact.Content.Replace("\r\n", "\n").Replace('\r', '\n'));
        builder.Append(fence);
        return builder.ToString();
    }

    private static string BuildFence(string content)
    {
        var maxRun = 0;
        var current = 0;
        foreach (var c in content)
        {
            if (c == '`')
            {
                current++;
                maxRun = Math.Max(maxRun, current);
            }
            else
            {
                current = 0;
            }
        }

        return new string('`', Math.Max(3, maxRun + 1));
    }

    private static string Combine(string before, string replacement, string after)
    {
        var parts = new[] { before.TrimEnd(), replacement.Trim(), after.Trim() }
            .Where(part => !string.IsNullOrWhiteSpace(part));
        return string.Join("\n\n", parts);
    }
}
