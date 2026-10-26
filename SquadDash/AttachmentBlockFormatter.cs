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
        var end = FindOpenTagEnd(block, 0);
        return end >= 0 && ExtractAttribute(block[..end], "encoding") == NotesEncoding;
    }

    private static NotesPayload DecodeNotesPayload(string block)
    {
        var start = FindOpenTagEnd(block, 0) + 1;
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
    internal static int StripTypedAttachmentHeaders(string prompt) =>
        ParseAttachmentHeaders(prompt)?.BodyStart ?? -1;

    internal sealed record AttachmentHeader(
        int BodyStart,
        IReadOnlyList<string> Items)
    {
        internal IReadOnlyList<string> Blocks => Items.Where(IsTypedHeader).ToArray();
        internal IReadOnlyList<string> Lines => Items.Where(item => !IsTypedHeader(item)).ToArray();
        internal int Count => Items.Count;
    }

    private static bool IsTypedHeader(string text) => text.StartsWith("<attachment ", StringComparison.Ordinal);

    internal static AttachmentHeader? ParseAttachmentHeaders(string prompt)
    {
        var pos = 0;
        var items = new List<string>();
        const string Preamble = "The user has attached the following context items. Please refer to them as needed.";
        if (prompt.StartsWith(Preamble, StringComparison.Ordinal))
        {
            pos = Preamble.Length;
            if (!TryConsumeLineBreak(prompt, ref pos))
                return null;
        }

        // Only scan the leading protocol section. Never search the user's body for
        // attachment-looking examples (including fenced code after the separator).
        while (pos < prompt.Length)
        {
            var start = pos;
            if (prompt.AsSpan(pos).StartsWith("<attachment ", StringComparison.Ordinal))
            {
                if (!TryReadAttachmentBlock(prompt, pos, out pos, forDisplay: true))
                    return null;
            }
            else if (!TryConsumeRecognizedHeaderLine(prompt, ref pos))
            {
                if (IsHeaderStart(prompt.AsSpan(pos)))
                    return null;
                break;
            }
            items.Add(prompt[start..pos]);

            if (TryConsumeBlankLine(prompt, ref pos))
                break;
            if (prompt.AsSpan(pos).StartsWith("<attachment ", StringComparison.Ordinal))
                continue;
            if (!TryConsumeLineBreak(prompt, ref pos))
                break;
        }

        // EOF and a single line break before prose are valid boundaries too.
        // A preamble alone is not evidence of an attachment.
        return items.Count > 0
            ? new AttachmentHeader(pos, items)
            : null;
    }

    private static bool TryReadAttachmentBlock(string text, int start, out int end, bool forDisplay = false)
    {
        end = start;
        var openEnd = FindOpenTagEnd(text, start);
        if (openEnd < 0 || string.IsNullOrWhiteSpace(ExtractAttribute(text[start..openEnd], "type")))
            return false;
        var close = text.IndexOf(LiteralCloseTag, openEnd + 1, StringComparison.Ordinal);
        if (close < 0)
            return false;
        if (forDisplay)
        {
            // Emitted multiline blocks close on their own line. Do not borrow an
            // inline delimiter from a later XML example after an unterminated block.
            var bodyStart = openEnd + 1;
            var multiline = TryConsumeLineBreak(text, ref bodyStart);
            if (multiline
                ? close > bodyStart && text[close - 1] is not '\r' and not '\n'
                : close > FindLineEnd(text, bodyStart))
                return false;
            if (HasUnclosedNestedAttachment(text.AsSpan(bodyStart, close - bodyStart)) ||
                IsInsideClosedCodeFence(text, bodyStart, close))
                return false;

            if (IsNotesBlock(text[start..(close + LiteralCloseTag.Length)]))
            {
                try
                {
                    DecodeNotesPayload(text[start..(close + LiteralCloseTag.Length)]);
                }
                catch (JsonException)
                {
                    // Invalid protocol-looking user text is not a display attachment.
                    return false;
                }
            }
        }
        end = close + LiteralCloseTag.Length;
        return true;
    }

    private static bool HasUnclosedNestedAttachment(ReadOnlySpan<char> content)
    {
        // The emitter escapes every content close tag, including literal XML examples.
        // An unmatched nested opener must not borrow the outer delimiter: it could be
        // a later user example following a truncated transport block.
        var depth = 0;
        for (var i = 0; i < content.Length; i++)
        {
            if (content[i..].StartsWith("<attachment", StringComparison.Ordinal) &&
                i + 11 < content.Length && (char.IsWhiteSpace(content[i + 11]) || content[i + 11] == '>'))
                depth++;
            else if (content[i..].StartsWith(EscapedCloseTag, StringComparison.Ordinal) && depth > 0)
                depth--;
        }
        return depth > 0;
    }

    private static bool IsInsideClosedCodeFence(string text, int start, int position)
    {
        char fence = '\0';
        var fenceLength = 0;
        for (var lineStart = start; lineStart < text.Length;)
        {
            var lineEnd = FindLineEnd(text, lineStart);
            // A quote wrapper may follow the closing fence on the same line.
            var contentEnd = position >= lineStart && position < lineEnd ? position : lineEnd;
            var line = text.AsSpan(lineStart, contentEnd - lineStart).Trim();
            if (!line.IsEmpty && line[0] is '`' or '~')
            {
                var length = 1;
                while (length < line.Length && line[length] == line[0])
                    length++;
                if (fence == '\0' && length >= 3)
                {
                    if (lineStart > position)
                        return false;
                    fence = line[0];
                    fenceLength = length;
                }
                else if (line[0] == fence && length >= fenceLength && line[length..].IsWhiteSpace())
                {
                    if (lineStart > position)
                        return true;
                    fence = '\0';
                }
            }
            if (lineEnd >= position && fence == '\0')
                return false;
            lineStart = lineEnd;
            if (!TryConsumeLineBreak(text, ref lineStart))
                break;
        }
        return false;
    }

    private static bool IsHeaderStart(ReadOnlySpan<char> text) =>
        text.StartsWith("<attachment ", StringComparison.Ordinal) ||
        text.StartsWith("[Attached image: ", StringComparison.Ordinal) ||
        text.StartsWith("[Follow-up on ", StringComparison.Ordinal) ||
        text.StartsWith("Regarding this section of the transcript:", StringComparison.Ordinal);

    private static bool TryConsumeRecognizedHeaderLine(string prompt, ref int pos)
    {
        var lineEnd = FindLineEnd(prompt, pos);
        var line = prompt[pos..lineEnd];
        const string quotePrefix = "Regarding this section of the transcript: \"";
        if (line.StartsWith(quotePrefix, StringComparison.Ordinal))
            return TryConsumeTranscriptQuote(prompt, ref pos);
        if (line.StartsWith("[Attached image: ", StringComparison.Ordinal))
        {
            if (!line.EndsWith(']'))
                return false;
            pos = lineEnd;
            return true;
        }
        if (!line.StartsWith("[Follow-up on ", StringComparison.Ordinal))
            return false;

        // Follow-up summaries can span lines, but cannot consume the blank separator
        // or a subsequent attachment when their terminator is missing.
        while (!line.EndsWith(']'))
        {
            var next = lineEnd;
            if (TryConsumeBlankLine(prompt, ref next) ||
                !TryConsumeLineBreak(prompt, ref next) ||
                next >= prompt.Length)
                return false;
            lineEnd = FindLineEnd(prompt, next);
            line = prompt[next..lineEnd];
            if (IsHeaderStart(line) ||
                line.TrimStart().StartsWith("```", StringComparison.Ordinal) ||
                line.TrimStart().StartsWith("~~~", StringComparison.Ordinal))
                return false;
        }

        pos = lineEnd;
        return true;
    }

    private static bool TryConsumeTranscriptQuote(string prompt, ref int pos)
    {
        const string prefix = "Regarding this section of the transcript: \"";
        var contentStart = pos + prefix.Length;
        var lineStart = pos;
        var quoteCount = 0;
        var needsSeparator = false;
        var simpleEnd = -1;
        while (lineStart < prompt.Length)
        {
            var lineEnd = FindLineEnd(prompt, lineStart);
            var line = prompt[lineStart..lineEnd];
            if (simpleEnd >= 0 && (line.AsSpan().IsWhiteSpace() ||
                IsHeaderStart(line.AsSpan().TrimStart()) ||
                line.TrimStart().StartsWith("```", StringComparison.Ordinal) ||
                line.TrimStart().StartsWith("~~~", StringComparison.Ordinal)))
            {
                pos = simpleEnd;
                return true;
            }
            if (lineStart != pos && IsHeaderStart(line.AsSpan().TrimStart()))
                return false;
            quoteCount += line.Count(c => c == '"');
            var separatorEnd = lineEnd;
            var hasSeparator = TryConsumeBlankLine(prompt, ref separatorEnd);
            var next = lineEnd;
            var hasNextHeader = TryConsumeLineBreak(prompt, ref next) &&
                IsHeaderStart(prompt.AsSpan(next));

            // Legacy content quotes are not escaped: parity cannot identify the wrapper.
            // Complex selections require the emitted separator or a following header;
            // only a simple quotation supports the historical EOF/single-line boundary.
            if (line.EndsWith('"') && (hasSeparator || hasNextHeader))
            {
                if (simpleEnd >= 0 || IsInsideClosedCodeFence(prompt, contentStart, lineEnd - 1))
                    return false;
                pos = lineEnd;
                return true;
            }
            // Check ambiguity only through this paragraph. If another closing quote
            // fits the emitted framing, neither interpretation can safely hide text.
            if (line.EndsWith('"') && !needsSeparator && quoteCount == 2)
                simpleEnd = lineEnd;

            var contentLine = lineStart == pos ? prompt[contentStart..lineEnd] : line;
            needsSeparator |= hasSeparator ||
                contentLine.TrimStart().StartsWith("```", StringComparison.Ordinal) ||
                contentLine.TrimStart().StartsWith("~~~", StringComparison.Ordinal);
            lineStart = lineEnd;
            if (!TryConsumeLineBreak(prompt, ref lineStart))
                break;
        }
        if (simpleEnd >= 0)
        {
            pos = simpleEnd;
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

        while (pos < text.Length && text[pos] is ' ' or '\t')
            pos++;
        if (TryConsumeLineBreak(text, ref pos))
            return true;

        pos = original;
        return false;
    }

    /// <summary>
    /// Counts typed XML attachment blocks (<c>&lt;attachment …&gt;</c>) in the leading
    /// header, excluding examples in the user's body. Used for "📎 N attachments" when
    /// re-rendering persisted turns where the live <see cref="FollowUpAttachment"/> list is
    /// no longer available.
    /// </summary>
    internal static int CountAttachmentBlocks(string text) =>
        ParseAttachmentHeaders(text)?.Blocks.Count ?? 0;

    internal static IReadOnlyList<string> ExtractAttachmentBlocks(string text)
    {
        const string AttachOpenTag = "<attachment ";
        var blocks = new List<string>();
        int pos = 0;
        while ((pos = text.IndexOf(AttachOpenTag, pos, StringComparison.Ordinal)) >= 0)
        {
            if (!TryReadAttachmentBlock(text, pos, out var end))
                break;

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
        var closeAngle = FindOpenTagEnd(block, 0);
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
        return TryReadOpenTag(openTag + ">", 0, out _, out var attributes) &&
            attributes.TryGetValue(name, out var value)
                ? DecodeAttribute(attributes, value)
                : null;
    }

    private static string DecodeAttribute(IReadOnlyDictionary<string, string> attributes, string value) =>
        (attributes.TryGetValue("encoding", out var encoding) && encoding == NotesEncoding) ||
        (attributes.TryGetValue("attribute-encoding", out var attributeEncoding) && attributeEncoding == "xml-v1")
            ? System.Net.WebUtility.HtmlDecode(value)
            : value.Replace("&quot;", "\"", StringComparison.Ordinal);

    private static string? ExtractOpenTag(string block)
    {
        if (!block.StartsWith("<attachment ", StringComparison.Ordinal))
            return null;
        var closeAngle = FindOpenTagEnd(block, 0);
        if (closeAngle < 0)
            return null;
        return block[..closeAngle];
    }

    private static int FindOpenTagEnd(string text, int start) =>
        TryReadOpenTag(text, start, out var end, out _) ? end : -1;

    private static bool TryReadOpenTag(
        string text, int start, out int end, out Dictionary<string, string> attributes)
    {
        end = -1;
        attributes = new Dictionary<string, string>(StringComparer.Ordinal);
        if (!text.AsSpan(start).StartsWith("<attachment ", StringComparison.Ordinal))
            return false;

        var pos = start + "<attachment".Length;
        while (pos < text.Length)
        {
            if (text[pos] == '>' && attributes.Count > 0)
            {
                end = pos;
                return true;
            }
            if (text[pos] is not ' ' and not '\t')
                return false;
            while (pos < text.Length && text[pos] is ' ' or '\t')
                pos++;

            var nameStart = pos;
            while (pos < text.Length && (char.IsAsciiLetterOrDigit(text[pos]) || text[pos] is '-' or '_' or ':' or '.'))
                pos++;
            if (pos == nameStart || !text.AsSpan(pos).StartsWith("=\"", StringComparison.Ordinal))
                return false;

            var name = text[nameStart..pos];
            pos += 2;
            var valueStart = pos;
            pos = text.IndexOf('"', pos);
            if (pos < 0 || !attributes.TryAdd(name, text[valueStart..pos]))
                return false;
            // Newlines are content only inside a quoted value. After it, require an
            // actual attribute or the emitter's immediate '>', not arbitrary prose.
            pos++;
        }
        return false;
    }

    private static Dictionary<string, string> ExtractAttachmentAttributes(string block)
    {
        return TryReadOpenTag(block, 0, out _, out var attributes)
            ? attributes.ToDictionary(pair => pair.Key, pair => DecodeAttribute(attributes, pair.Value), StringComparer.Ordinal)
            : new Dictionary<string, string>(StringComparer.Ordinal);
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
