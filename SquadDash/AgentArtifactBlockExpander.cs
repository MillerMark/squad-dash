using System.Text;

namespace SquadDash;

internal static class AgentArtifactBlockExpander
{
    private const string XmlDisplayArtifactStartTag = "<SQUADDASH_ARTIFACT_JSON>";
    private const string XmlDisplayArtifactEndTag = "</SQUADDASH_ARTIFACT_JSON>";

    internal static string ExpandDisplayArtifacts(string text, string workspaceRoot)
    {
        if (string.IsNullOrWhiteSpace(text) ||
            !ContainsDisplayArtifactReference(text))
            return text;

        var expanded = text;
        for (var i = 0; i < 8; i++)
        {
            if (!TryExtractDisplayArtifactReference(expanded, out var extraction) || extraction is null)
                break;

            var replacement = BuildReplacement(workspaceRoot, extraction.Payload);
            expanded = Combine(
                extraction.TextBeforeBlock,
                replacement,
                extraction.TrailingText);
        }

        return expanded;
    }

    private static bool ContainsDisplayArtifactReference(string text) =>
        text.Contains(AgentArtifactStore.DisplayArtifactMarker, StringComparison.Ordinal) ||
        text.Contains(XmlDisplayArtifactStartTag, StringComparison.Ordinal);

    private static bool TryExtractDisplayArtifactReference(
        string text,
        out StructuredJsonBlockExtraction<AgentArtifactReference>? extraction)
    {
        if (StructuredJsonBlockParser.TryExtractObject(
                text,
                AgentArtifactStore.DisplayArtifactMarker,
                out extraction))
            return extraction is not null;

        if (!StructuredJsonBlockParser.TryExtractObject<AgentArtifactReference>(
                text,
                XmlDisplayArtifactStartTag,
                out var xmlExtraction) ||
            xmlExtraction is null)
        {
            extraction = null;
            return false;
        }

        var trailing = StripLeadingXmlEndTag(xmlExtraction.TrailingText);
        extraction = xmlExtraction with
        {
            TrailingText = trailing,
            VisibleText = CombineVisibleText(xmlExtraction.TextBeforeBlock, trailing),
        };
        return true;
    }

    private static string BuildReplacement(string workspaceRoot, AgentArtifactReference reference)
    {
        if (!AgentArtifactStore.TryMaterialize(
                workspaceRoot,
                reference,
                AgentArtifactStore.DefaultMaxDisplayBytes,
                archive: true,
                out var artifact,
                out var error) ||
            artifact is null)
        {
            return $"> Artifact could not be loaded: {error}";
        }

        // A plan is a host-owned protocol payload, never a transcript deliverable. Older
        // model responses sometimes used SQUADDASH_ARTIFACT_JSON with display=code_block for
        // plans; suppress those payloads here while TaskPlanResponseResolver still consumes
        // them through its backward-compatible transport path.
        if (TaskPlanResponseResolver.LooksLikeTaskPlanArtifactContent(artifact.Content))
            return string.Empty;

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

    private static string StripLeadingXmlEndTag(string text)
    {
        var trimmed = text.TrimStart();
        if (!trimmed.StartsWith(XmlDisplayArtifactEndTag, StringComparison.Ordinal))
            return text;

        return trimmed[XmlDisplayArtifactEndTag.Length..].TrimStart();
    }

    private static string CombineVisibleText(string before, string after)
    {
        if (string.IsNullOrWhiteSpace(before))
            return after.Trim();
        if (string.IsNullOrWhiteSpace(after))
            return before.TrimEnd();
        return before.TrimEnd() + "\n\n" + after.Trim();
    }

    private static string Combine(string before, string replacement, string after)
    {
        var parts = new[] { before.TrimEnd(), replacement.Trim(), after.Trim() }
            .Where(part => !string.IsNullOrWhiteSpace(part));
        return string.Join("\n\n", parts);
    }
}
