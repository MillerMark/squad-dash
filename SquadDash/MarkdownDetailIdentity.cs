using System;
using System.Collections.Generic;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace SquadDash;

internal sealed record MarkdownDetailSection(string Label, string Body);

/// <summary>
/// Durable identity shared by every renderer of a logical Markdown field.
/// Key: md-detail:v1:{context SHA-256}:{section SHA-256}:{zero-based identical occurrence}.
/// Hashes are lowercase hex over UTF-8 length-prefixed components: concatenate
/// "{UTF-8 byte count in invariant decimal}:{component}" without additional separators.
/// Context components are workspace, content kind, durable entity ID, field, in that order.
/// Workspace uses WorkspacePaths.NormalizeFolder then invariant uppercase (Windows path identity).
/// Kind and field are trimmed and invariant lowercase; entity ID is exact and case-sensitive.
/// Section components are label and body. Both convert CRLF and CR to LF; only the label is trimmed.
/// No other whitespace, case, or Unicode normalization is performed on Markdown.
/// Do not include surface, window, session, theme, font, revision, or the whole document in context.
/// </summary>
internal sealed class MarkdownDetailIdentity {
    private readonly string _prefix;

    private MarkdownDetailIdentity(string workspaceFolder, string contentKind, string entityId, string field) {
        ArgumentException.ThrowIfNullOrWhiteSpace(workspaceFolder);
        ArgumentException.ThrowIfNullOrWhiteSpace(contentKind);
        ArgumentException.ThrowIfNullOrWhiteSpace(entityId);
        ArgumentException.ThrowIfNullOrWhiteSpace(field);
        _prefix = $"md-detail:v1:{HashComponents(
            WorkspacePaths.NormalizeFolder(workspaceFolder).ToUpperInvariant(),
            contentKind.Trim().ToLowerInvariant(), entityId, field.Trim().ToLowerInvariant())}";
    }

    /// <summary>Entity IDs must be durable and unique within the workspace and content kind.</summary>
    public static MarkdownDetailIdentity Create(string workspaceFolder, string contentKind, string entityId, string field) =>
        new(workspaceFolder, contentKind, entityId, field);

    public static MarkdownDetailIdentity ForInboxMessage(string workspaceFolder, string messageId) =>
        Create(workspaceFolder, "inbox-message", messageId, "body");

    public static MarkdownDetailIdentity ForPlanDescription(string workspaceFolder, string planId) =>
        Create(workspaceFolder, "plan", planId, "description");

    /// <summary>
    /// Visualizer and inspector must both use this factory. Task IDs are scoped by their plan ID:
    /// the entity component is the length-prefixed encoding of the exact plan ID and task ID.
    /// </summary>
    public static MarkdownDetailIdentity ForTaskDescription(string workspaceFolder, string planId, string taskId) {
        ArgumentException.ThrowIfNullOrWhiteSpace(planId);
        ArgumentException.ThrowIfNullOrWhiteSpace(taskId);
        return Create(workspaceFolder, "plan-task", EncodeComponents(planId, taskId), "description");
    }

    /// <summary>
    /// Pass all detail sections in source order on each rebuild, not only visible sections.
    /// Occurrence counts are per fingerprint, never absolute section indices: unrelated insertion
    /// or reordering does not change a key. Identical duplicates remain distinct by source order.
    /// Editing label/body intentionally yields a new, initially collapsed identity.
    /// </summary>
    public IReadOnlyList<string> GetSectionKeys(IEnumerable<MarkdownDetailSection> sections) {
        ArgumentNullException.ThrowIfNull(sections);
        var occurrences = new Dictionary<string, int>(StringComparer.Ordinal);
        var keys = new List<string>();
        foreach (var section in sections) {
            ArgumentNullException.ThrowIfNull(section);
            var fingerprint = SectionFingerprint(section.Label, section.Body);
            occurrences.TryGetValue(fingerprint, out var occurrence);
            keys.Add(BuildKey(fingerprint, occurrence));
            occurrences[fingerprint] = checked(occurrence + 1);
        }
        return keys.AsReadOnly();
    }

    /// <summary>Occurrence is the count of preceding sections with the same normalized label AND body.</summary>
    public string GetSectionKey(string label, string body, int occurrence = 0) {
        ArgumentOutOfRangeException.ThrowIfNegative(occurrence);
        return BuildKey(SectionFingerprint(label, body), occurrence);
    }

    internal static bool IsSectionKey(string key) =>
        Regex.IsMatch(key, @"\Amd-detail:v1:[0-9a-f]{64}:[0-9a-f]{64}:(0|[1-9][0-9]{0,9})\z",
            RegexOptions.CultureInvariant) &&
        int.TryParse(key.AsSpan(key.LastIndexOf(':') + 1), NumberStyles.None, CultureInfo.InvariantCulture, out _);

    private string BuildKey(string fingerprint, int occurrence) =>
        $"{_prefix}:{fingerprint}:{occurrence.ToString(CultureInfo.InvariantCulture)}";

    private static string SectionFingerprint(string label, string body) {
        ArgumentNullException.ThrowIfNull(label);
        ArgumentNullException.ThrowIfNull(body);
        return HashComponents(NormalizeLineEndings(label).Trim(), NormalizeLineEndings(body));
    }

    private static string NormalizeLineEndings(string text) => text.Replace("\r\n", "\n").Replace('\r', '\n');

    private static string HashComponents(params string[] components) =>
        Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(EncodeComponents(components))));

    private static string EncodeComponents(params string[] components) {
        var encoded = new StringBuilder();
        foreach (var component in components)
            encoded.Append(Encoding.UTF8.GetByteCount(component).ToString(CultureInfo.InvariantCulture))
                .Append(':').Append(component);
        return encoded.ToString();
    }
}
