namespace SquadDash;

/// <summary>
/// Pure-static helpers for building and parsing typed XML attachment blocks.
/// Format: <c>&lt;attachment type="…" title="…"&gt;\ncontent\n&lt;/attachment&gt;</c>.
/// </summary>
internal static class AttachmentBlockFormatter
{
    // Sentinel used to escape the close tag inside content so the parser never
    // sees a premature </attachment>.
    private const string EscapedCloseTag = "&lt;/attachment&gt;";
    private const string LiteralCloseTag = "</attachment>";

    /// <summary>
    /// Builds a typed XML-style attachment block.
    /// Any occurrence of <c>&lt;/attachment&gt;</c> in <paramref name="content"/> is
    /// escaped to <c>&amp;lt;/attachment&amp;gt;</c> so the block close tag cannot
    /// collide with content.
    /// </summary>
    internal static string BuildTypedAttachmentBlock(string type, string? title, string content)
    {
        content = content.Replace(LiteralCloseTag, EscapedCloseTag, StringComparison.Ordinal);
        var openTag = title is not null
            ? $"<attachment type=\"{type}\" title=\"{title.Replace("\"", "&quot;")}\">"
            : $"<attachment type=\"{type}\">";
        return $"{openTag}\n{content}\n{LiteralCloseTag}";
    }

    /// <summary>
    /// Given a <paramref name="prompt"/> that starts with a recognized attachment/follow-up
    /// header, returns the index of the first character of the user's actual message.
    /// Returns -1 if the header section cannot be parsed.
    /// </summary>
    internal static int StripTypedAttachmentHeaders(string prompt)
    {
        int pos = 0;
        var sawHeader = false;

        // Skip optional preamble (single line).
        const string Preamble = "The user has attached the following context items. Please refer to them as needed.";
        if (prompt.StartsWith(Preamble, StringComparison.Ordinal))
        {
            pos = Preamble.Length;
            sawHeader = true;
        }

        // Scan past attachment blocks and known one-line follow-up headers.
        const string AttachOpen  = "<attachment ";
        while (pos < prompt.Length)
        {
            // Blank line is the terminal separator between attachments and user text.
            if (sawHeader)
            {
                var next = pos;
                if (TryConsumeBlankLine(prompt, ref next))
                {
                    pos = next;
                    return pos;
                }

                next = pos;
                if (TryConsumeLineBreak(prompt, ref next))
                {
                    pos = next;
                    continue;
                }
            }

            if (TryConsumeRecognizedHeaderLine(prompt, ref pos))
            {
                sawHeader = true;
                continue;
            }

            // XML typed block: <attachment ...>...</attachment>
            // Content with literal </attachment> is escaped, so IndexOf finds only the real close tag.
            if (pos + AttachOpen.Length <= prompt.Length &&
                prompt.AsSpan(pos, AttachOpen.Length).SequenceEqual(AttachOpen.AsSpan()))
            {
                var closeIdx = prompt.IndexOf(LiteralCloseTag, pos, StringComparison.Ordinal);
                if (closeIdx < 0) return -1;
                pos = closeIdx + LiteralCloseTag.Length;
                sawHeader = true;
                continue;
            }

            return -1;
        }
        return -1;
    }

    private static bool TryConsumeRecognizedHeaderLine(string prompt, ref int pos)
    {
        var lineEnd = FindLineEnd(prompt, pos);
        var line = prompt[pos..lineEnd];
        if (line.StartsWith("[Attached image: ", StringComparison.Ordinal) ||
            line.StartsWith("[Follow-up on ", StringComparison.Ordinal) ||
            line.StartsWith("Regarding this section of the transcript:", StringComparison.Ordinal))
        {
            pos = lineEnd;
            return true;
        }

        return false;
    }

    private static int FindLineEnd(string text, int start)
    {
        var cr = text.IndexOf('\r', start);
        var lf = text.IndexOf('\n', start);
        return (cr, lf) switch
        {
            (< 0, < 0) => text.Length,
            (< 0, _)   => lf,
            (_, < 0)   => cr,
            _          => Math.Min(cr, lf),
        };
    }

    private static bool TryConsumeLineBreak(string text, ref int pos)
    {
        if (pos >= text.Length)
            return false;

        if (text[pos] == '\r')
        {
            pos++;
            if (pos < text.Length && text[pos] == '\n')
                pos++;
            return true;
        }

        if (text[pos] == '\n')
        {
            pos++;
            return true;
        }

        return false;
    }

    private static bool TryConsumeBlankLine(string text, ref int pos)
    {
        var original = pos;
        if (!TryConsumeLineBreak(text, ref pos))
            return false;

        if (TryConsumeLineBreak(text, ref pos))
            return true;

        pos = original;
        return false;
    }

    /// <summary>
    /// Counts the number of typed XML attachment blocks (<c>&lt;attachment …&gt;</c>) in
    /// <paramref name="text"/>.  Used to display the correct "📎 N attachments" count when
    /// re-rendering persisted turns where the live <see cref="FollowUpAttachment"/> list is
    /// no longer available.
    /// </summary>
    internal static int CountAttachmentBlocks(string text)
    {
        const string AttachOpenTag = "<attachment ";
        int count = 0;
        int pos   = 0;
        while ((pos = text.IndexOf(AttachOpenTag, pos, StringComparison.Ordinal)) >= 0)
        {
            count++;
            pos += AttachOpenTag.Length;
        }
        return count;
    }

    internal static IReadOnlyList<string> ExtractAttachmentBlocks(string text)
    {
        const string AttachOpenTag = "<attachment ";
        var blocks = new List<string>();
        int pos = 0;
        while ((pos = text.IndexOf(AttachOpenTag, pos, StringComparison.Ordinal)) >= 0)
        {
            var closeIdx = text.IndexOf(LiteralCloseTag, pos, StringComparison.Ordinal);
            if (closeIdx < 0)
                break;

            var end = closeIdx + LiteralCloseTag.Length;
            blocks.Add(text[pos..end]);
            pos = end;
        }

        return blocks;
    }

    internal static (string? Type, string? Title) ExtractAttachmentMetadata(string block)
    {
        if (!block.StartsWith("<attachment ", StringComparison.Ordinal))
            return (null, null);

        var closeAngle = block.IndexOf('>', StringComparison.Ordinal);
        if (closeAngle < 0)
            return (null, null);

        var openTag = block[..closeAngle];
        return (ExtractAttribute(openTag, "type"), ExtractAttribute(openTag, "title"));
    }

    /// <summary>
    /// Strips the outer <c>&lt;attachment …&gt;…&lt;/attachment&gt;</c> wrapper from a typed
    /// attachment block and returns the inner content with any escaped close tags restored.
    /// Falls back to returning the original string unchanged if the format is not recognised
    /// (e.g. old SQUADCLIP blocks).
    /// </summary>
    internal static string ExtractAttachmentContent(string block)
    {
        if (!block.StartsWith("<attachment ", StringComparison.Ordinal))
            return block;
        var closeAngle = block.IndexOf('>', StringComparison.Ordinal);
        if (closeAngle < 0) return block;
        var closeIdx = block.LastIndexOf(LiteralCloseTag, StringComparison.Ordinal);
        if (closeIdx < 0) return block;
        // Content is between the '>' of the open tag and the start of </attachment>.
        var start = closeAngle + 1;
        if (start < block.Length && block[start] == '\n') start++; // skip leading newline
        var content = block[start..closeIdx].TrimEnd();
        return content.Replace(EscapedCloseTag, LiteralCloseTag, StringComparison.Ordinal);
    }

    private static string? ExtractAttribute(string openTag, string name)
    {
        var marker = name + "=\"";
        var start = openTag.IndexOf(marker, StringComparison.Ordinal);
        if (start < 0)
            return null;

        start += marker.Length;
        var end = openTag.IndexOf('"', start);
        if (end < 0)
            return null;

        return openTag[start..end].Replace("&quot;", "\"", StringComparison.Ordinal);
    }
}
