using System.Text.Json;
using System.Collections.Generic;
using System.Linq;

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
    private const string NotesEncoding = "json-notes-v1";
    private const string TranscriptRoleAttribute = "source-role";
    private const string TranscriptTurnIndexAttribute = "source-turn-index";
    private const string TranscriptTimestampAttribute = "source-timestamp";
    private const string TranscriptConversationHintAttribute = "source-conversation-hint";
    private const string TranscriptMessageIdAttribute = "source-message-id";

    /// <summary>
    /// Notes-bearing blocks have an explicitly versioned JSON body with separate Content
    /// and Notes fields. The default JSON encoder escapes all angle brackets, including
    /// those in nested content, so neither field can inject an attachment delimiter.
    /// Unannotated blocks retain their original representation.
    /// </summary>
    internal static string WithNotes(string context, string? notes)
    {
        if (string.IsNullOrEmpty(notes))
            return context;

        var attributes = ExtractAttachmentAttributes(context);
        if (!attributes.ContainsKey("type"))
            attributes["type"] = "text";
        attributes["encoding"] = NotesEncoding;
        var content = ExtractAttachmentContent(context);
        var payload = JsonSerializer.Serialize(new NotesPayload(content, notes));
        return $"{BuildOpenTag(attributes)}\n{payload}\n{LiteralCloseTag}";
    }

    internal static string? ExtractAttachmentNotes(string block) =>
        IsNotesBlock(block) ? DecodeNotesPayload(block).Notes : null;

    private sealed record NotesPayload(string Content, string Notes);

    private static bool IsNotesBlock(string block)
    {
        if (!block.StartsWith("<attachment ", StringComparison.Ordinal))
            return false;
        var end = block.IndexOf('>');
        return end >= 0 && ExtractAttribute(block[..end], "encoding") == NotesEncoding;
    }

    private static NotesPayload DecodeNotesPayload(string block)
    {
        var start = block.IndexOf('>') + 1;
        var end = block.LastIndexOf(LiteralCloseTag, StringComparison.Ordinal);
        if (end < start)
            throw new JsonException("Notes attachment is missing its closing delimiter.");
        var payload = JsonSerializer.Deserialize<NotesPayload>(block[start..end]);
        if (payload?.Content is null || payload.Notes is null)
            throw new JsonException("Notes attachment requires Content and Notes strings.");
        return payload;
    }

    private static string EncodeAttribute(string value) =>
        value.Replace("&", "&amp;", StringComparison.Ordinal)
            .Replace("<", "&lt;", StringComparison.Ordinal)
            .Replace(">", "&gt;", StringComparison.Ordinal)
            .Replace("\"", "&quot;", StringComparison.Ordinal);

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

    internal static string BuildTranscriptAttachmentBlock(
        string? title,
        string content,
        TranscriptAttachmentSource? source)
    {
        if (source is null)
            return BuildTypedAttachmentBlock("transcript", title, content);

        var attributes = new Dictionary<string, string>
        {
            ["type"] = "transcript",
            ["attribute-encoding"] = "xml-v1",
        };
        if (!string.IsNullOrWhiteSpace(title))
            attributes["title"] = title;
        if (!string.IsNullOrWhiteSpace(source.Role))
            attributes[TranscriptRoleAttribute] = source.Role!;
        if (source.TurnIndex is { } turnIndex)
            attributes[TranscriptTurnIndexAttribute] = turnIndex.ToString(System.Globalization.CultureInfo.InvariantCulture);
        if (!string.IsNullOrWhiteSpace(source.Timestamp))
            attributes[TranscriptTimestampAttribute] = source.Timestamp!;
        if (!string.IsNullOrWhiteSpace(source.ConversationHint))
            attributes[TranscriptConversationHintAttribute] = source.ConversationHint!;
        if (!string.IsNullOrWhiteSpace(source.MessageId))
            attributes[TranscriptMessageIdAttribute] = source.MessageId!;

        content = content.Replace(LiteralCloseTag, EscapedCloseTag, StringComparison.Ordinal);
        return $"{BuildOpenTag(attributes)}\n{content}\n{LiteralCloseTag}";
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

        var openTag = ExtractOpenTag(block);
        if (openTag is null)
            return (null, null);

        var type = ExtractAttribute(openTag, "type");
        var title = ExtractAttribute(openTag, "title");
        return (type, title);
    }

    internal static TranscriptAttachmentSource? ExtractTranscriptSource(string block)
    {
        var openTag = ExtractOpenTag(block);
        if (openTag is null)
            return null;
        var type = ExtractAttribute(openTag, "type");
        if (!string.Equals(type, "transcript", StringComparison.Ordinal))
            return null;

        var role = ExtractAttribute(openTag, TranscriptRoleAttribute);
        int? turnIndex = null;
        var turnIndexText = ExtractAttribute(openTag, TranscriptTurnIndexAttribute);
        if (int.TryParse(turnIndexText, System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out var parsedTurnIndex))
            turnIndex = parsedTurnIndex;
        var timestamp = ExtractAttribute(openTag, TranscriptTimestampAttribute);
        var conversationHint = ExtractAttribute(openTag, TranscriptConversationHintAttribute);
        var messageId = ExtractAttribute(openTag, TranscriptMessageIdAttribute);

        if (role is null && turnIndex is null && timestamp is null && conversationHint is null && messageId is null)
            return null;

        return new TranscriptAttachmentSource(
            Role: role,
            TurnIndex: turnIndex,
            Timestamp: timestamp,
            ConversationHint: conversationHint,
            MessageId: messageId);
    }

    /// <summary>
    /// Strips the outer <c>&lt;attachment …&gt;…&lt;/attachment&gt;</c> wrapper from a typed
    /// attachment block and returns the inner content with any escaped close tags restored.
    /// Falls back to returning the original string unchanged if the format is not recognised
    /// (e.g. old SQUADCLIP blocks).
    /// </summary>
    internal static string ExtractAttachmentContent(string block)
    {
        if (IsNotesBlock(block))
            return DecodeNotesPayload(block).Content;
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
        var marker = " " + name + "=\"";
        var start = openTag.IndexOf(marker, StringComparison.Ordinal);
        if (start < 0)
            return null;

        start += marker.Length;
        var end = openTag.IndexOf('"', start);
        if (end < 0)
            return null;

        return DecodeAttribute(openTag, openTag[start..end]);
    }

    private static string DecodeAttribute(string openTag, string value) =>
        openTag.Contains($" encoding=\"{NotesEncoding}\"", StringComparison.Ordinal) ||
        openTag.Contains(" attribute-encoding=\"xml-v1\"", StringComparison.Ordinal)
            ? System.Net.WebUtility.HtmlDecode(value)
            : value.Replace("&quot;", "\"", StringComparison.Ordinal);

    private static string? ExtractOpenTag(string block)
    {
        if (!block.StartsWith("<attachment ", StringComparison.Ordinal))
            return null;
        var closeAngle = block.IndexOf('>', StringComparison.Ordinal);
        if (closeAngle < 0)
            return null;
        return block[..closeAngle];
    }

    private static Dictionary<string, string> ExtractAttachmentAttributes(string block)
    {
        var openTag = ExtractOpenTag(block);
        var attributes = new Dictionary<string, string>(StringComparer.Ordinal);
        if (openTag is null)
            return attributes;

        var span = openTag.AsSpan();
        var index = "<attachment ".Length;
        while (index < span.Length)
        {
            while (index < span.Length && char.IsWhiteSpace(span[index]))
                index++;
            if (index >= span.Length)
                break;

            var equals = openTag.IndexOf("=\"", index, StringComparison.Ordinal);
            if (equals < 0)
                break;
            var key = openTag[index..equals].Trim();
            var valueStart = equals + 2;
            var valueEnd = openTag.IndexOf('"', valueStart);
            if (valueEnd < 0)
                break;
            attributes[key] = DecodeAttribute(openTag, openTag[valueStart..valueEnd]);
            index = valueEnd + 1;
        }

        return attributes;
    }

    private static string BuildOpenTag(Dictionary<string, string> attributes)
    {
        var type = attributes.TryGetValue("type", out var typeValue) ? typeValue : "text";
        attributes.Remove("type");
        var title = attributes.TryGetValue("title", out var titleValue) ? titleValue : null;
        if (title is not null)
            attributes.Remove("title");
        var encoding = attributes.TryGetValue("encoding", out var encodingValue) ? encodingValue : null;
        if (encoding is not null)
            attributes.Remove("encoding");

        var sb = new System.Text.StringBuilder("<attachment");
        sb.Append($" type=\"{EncodeAttribute(type)}\"");
        if (title is not null)
            sb.Append($" title=\"{EncodeAttribute(title)}\"");
        if (encoding is not null)
            sb.Append($" encoding=\"{EncodeAttribute(encoding)}\"");
        foreach (var (key, value) in attributes.OrderBy(pair => pair.Key, StringComparer.Ordinal))
            sb.Append($" {key}=\"{EncodeAttribute(value)}\"");
        sb.Append('>');
        return sb.ToString();
    }
}
