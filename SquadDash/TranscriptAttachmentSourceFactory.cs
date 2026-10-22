using System.Globalization;
using System.Windows;
using System.Windows.Documents;

namespace SquadDash;

internal static class TranscriptAttachmentSourceFactory
{
    private static readonly DependencyProperty RoleProperty = DependencyProperty.RegisterAttached(
        "Role", typeof(string), typeof(TranscriptAttachmentSourceFactory));

    internal static void SetRole(TextElement element, string role) => element.SetValue(RoleProperty, role);

    internal static TranscriptAttachmentSource? FromSelection(
        TranscriptThreadState thread, FlowDocument sourceDocument, TextPointer selectionStart, TextPointer selectionEnd)
    {
        if (!ReferenceEquals(thread.Document, sourceDocument))
            return null;

        var sources = new List<TranscriptAttachmentSource>();
        try
        {
            if (selectionStart.CompareTo(selectionEnd) >= 0)
                return null;
            Visit(sourceDocument.Blocks, null);
        }
        catch (ArgumentException)
        {
            // Hosted controls may supply pointers from a different text tree.
            return null;
        }

        if (sources.Count == 0)
            return null;

        var first = sources[0];
        var source = new TranscriptAttachmentSource(
            Role: sources.All(s => s.Role == first.Role) ? first.Role : null,
            TurnIndex: sources.All(s => s.TurnIndex == first.TurnIndex) ? first.TurnIndex : null,
            Timestamp: sources.All(s => s.Timestamp == first.Timestamp) ? first.Timestamp : null,
            ConversationHint: sources.All(s => s.ConversationHint == first.ConversationHint) ? first.ConversationHint : null);
        return source == new TranscriptAttachmentSource() ? null : source;

        void Visit(BlockCollection blocks, string? inheritedRole)
        {
            foreach (var block in blocks)
            {
                if (block.ContentStart.CompareTo(selectionEnd) >= 0 ||
                    block.ContentEnd.CompareTo(selectionStart) <= 0)
                    continue;

                var role = block.GetValue(RoleProperty) as string ?? inheritedRole;
                if (block is Section section)
                {
                    Visit(section.Blocks, role);
                    continue;
                }
                if (block is System.Windows.Documents.List list)
                {
                    foreach (var item in list.ListItems)
                        Visit(item.Blocks, role);
                    continue;
                }

                PromptEntry? anchor = null;
                int? index = null;
                for (var i = 0; i < thread.PromptParagraphs.Count; i++)
                {
                    var entry = thread.PromptParagraphs[i];
                    if (entry.Paragraph.Parent is null)
                        continue;
                    if (entry.Paragraph.ContentStart.CompareTo(block.ContentStart) > 0)
                        break;
                    anchor = entry;
                    index = i;
                    if (ReferenceEquals(entry.Paragraph, block))
                        role ??= "user";
                }

                sources.Add(new TranscriptAttachmentSource(
                    Role: role,
                    TurnIndex: index,
                    Timestamp: anchor?.Timestamp.ToString("O", CultureInfo.InvariantCulture),
                    ConversationHint: string.IsNullOrWhiteSpace(anchor?.Title) ? null : anchor.Title));
            }
        }
    }
}
