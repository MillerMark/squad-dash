namespace SquadDash;

internal sealed record InboxMessageFileReferenceExtraction(
    AgentArtifactReference Reference,
    string VisibleText,
    string TextBeforeBlock,
    string TrailingText,
    int MarkerIndex);

internal static class InboxMessageFileReferenceParser
{
    internal static bool TryExtract(string? text, out InboxMessageFileReferenceExtraction? extraction)
    {
        extraction = null;
        if (!StructuredJsonBlockParser.TryExtractObject<AgentArtifactReference>(
                text,
                AgentArtifactStore.InboxMessageFileMarker,
                out var block) ||
            block is null)
            return false;

        extraction = new InboxMessageFileReferenceExtraction(
            block.Payload,
            block.VisibleText,
            block.TextBeforeBlock,
            block.TrailingText,
            block.MarkerIndex);
        return true;
    }
}
