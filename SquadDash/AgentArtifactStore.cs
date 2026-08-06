using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Serialization;

namespace SquadDash;

internal sealed class AgentArtifactReference
{
    [JsonPropertyName("path")]
    public string Path { get; init; } = string.Empty;

    [JsonPropertyName("label")]
    public string? Label { get; init; }

    [JsonPropertyName("language")]
    public string? Language { get; init; }

    [JsonPropertyName("display")]
    public string? Display { get; init; }

    [JsonPropertyName("sha256")]
    public string? Sha256 { get; init; }
}

internal sealed record AgentArtifactMaterialization(
    AgentArtifactReference Reference,
    string SourcePath,
    string SourceRelativePath,
    string? ArchivedPath,
    string? ArchivedRelativePath,
    string Content,
    string Sha256,
    long ByteLength);

internal static class AgentArtifactStore
{
    internal const string DisplayArtifactMarker = "SQUADDASH_ARTIFACT_JSON:";
    internal const string InboxMessageFileMarker = "INBOX_MESSAGE_JSON_FILE:";
    internal const long DefaultMaxDisplayBytes = 256 * 1024;
    internal const long DefaultMaxInboxBytes = 512 * 1024;
    internal static readonly TimeSpan ArchiveRetention = TimeSpan.FromDays(14);

    private static readonly string[] AllowedRelativeRoots =
    [
        Path.Combine(".squad", "tmp", "agent-artifacts"),
        Path.Combine(".squad", "archive", "agent-artifacts"),
    ];

    internal static bool TryMaterialize(
        string workspaceRoot,
        AgentArtifactReference reference,
        long maxBytes,
        bool archive,
        out AgentArtifactMaterialization? materialization,
        out string error)
    {
        materialization = null;
        error = string.Empty;

        if (string.IsNullOrWhiteSpace(reference.Path))
        {
            error = "Artifact manifest did not include a path.";
            return false;
        }

        if (!TryResolveAllowedPath(workspaceRoot, reference.Path, out var root, out var fullPath, out var relativePath, out error))
            return false;

        if (!File.Exists(fullPath))
        {
            error = $"Artifact file was not found: {ToDisplayPath(relativePath)}";
            return false;
        }

        var fileInfo = new FileInfo(fullPath);
        if (fileInfo.Length > maxBytes)
        {
            error = $"Artifact file is too large: {fileInfo.Length:N0} bytes, max {maxBytes:N0}.";
            return false;
        }

        var bytes = File.ReadAllBytes(fullPath);
        var hash = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
        if (!string.IsNullOrWhiteSpace(reference.Sha256) &&
            !string.Equals(NormalizeHash(reference.Sha256), hash, StringComparison.OrdinalIgnoreCase))
        {
            error = "Artifact file hash did not match the manifest sha256.";
            return false;
        }

        var content = DecodeUtf8(bytes);
        string? archivedPath = null;
        string? archivedRelativePath = null;
        if (archive && IsUnderRoot(fullPath, GetTempRoot(root)))
        {
            archivedPath = CopyToArchive(root, fullPath, hash);
            archivedRelativePath = Path.GetRelativePath(root, archivedPath);
        }

        materialization = new AgentArtifactMaterialization(
            reference,
            fullPath,
            relativePath,
            archivedPath,
            archivedRelativePath,
            content,
            hash,
            fileInfo.Length);
        return true;
    }

    internal static void CleanupExpiredArchives(
        string workspaceRoot,
        DateTimeOffset now,
        TimeSpan retention)
    {
        var root = NormalizeRoot(workspaceRoot);
        var archiveRoot = GetArchiveRoot(root);
        if (!Directory.Exists(archiveRoot))
            return;

        var cutoff = now - retention;
        foreach (var file in Directory.EnumerateFiles(archiveRoot, "*", SearchOption.AllDirectories))
        {
            try
            {
                var info = new FileInfo(file);
                var modifiedAt = new DateTimeOffset(info.LastWriteTimeUtc, TimeSpan.Zero);
                if (modifiedAt < cutoff)
                    info.Delete();
            }
            catch (IOException ex) { SquadDashTrace.Write("AgentArtifacts", $"CleanupExpiredArchives: could not delete file {file}: {ex.Message}"); }
            catch (UnauthorizedAccessException ex) { SquadDashTrace.Write("AgentArtifacts", $"CleanupExpiredArchives: access denied deleting file {file}: {ex.Message}"); }
        }

        foreach (var directory in Directory.EnumerateDirectories(archiveRoot, "*", SearchOption.AllDirectories)
                     .OrderByDescending(path => path.Length))
        {
            try
            {
                if (!Directory.EnumerateFileSystemEntries(directory).Any())
                    Directory.Delete(directory);
            }
            catch (IOException ex) { SquadDashTrace.Write("AgentArtifacts", $"CleanupExpiredArchives: could not remove empty directory {directory}: {ex.Message}"); }
            catch (UnauthorizedAccessException ex) { SquadDashTrace.Write("AgentArtifacts", $"CleanupExpiredArchives: access denied removing directory {directory}: {ex.Message}"); }
        }
    }

    internal static string BuildPromptInstruction() =>
        """
        <artifact_file_instructions>
        If the response is a deliverable the user will use as a standalone artifact — a prompt, escalation prompt, document, template, report, or config block — do NOT write its full content into the response body. Write it to a file under `.squad/tmp/agent-artifacts/` and reference it with a small manifest instead.

        For transcript display, append:
        SQUADDASH_ARTIFACT_JSON:
        {"path":".squad/tmp/agent-artifacts/<file>","language":"json","display":"code_block","label":"optional label","sha256":"optional sha256"}

        Inline fenced code is fine when the code snippet is a supporting detail inside a conversational answer — not when the response itself is the deliverable. Also use artifact files when the content includes nested JSON/Markdown/code fences or markers such as `INBOX_MESSAGE_JSON:`, `HOST_COMMAND_JSON:`, `QUICK_REPLIES_JSON:`, or `<system_notification>`.

        For complex inbox messages, write the complete inbox JSON object to `.squad/tmp/agent-artifacts/<file>.json` and append:
        INBOX_MESSAGE_JSON_FILE:
        {"path":".squad/tmp/agent-artifacts/<file>.json","sha256":"optional sha256"}

        Never reference files outside `.squad/tmp/agent-artifacts/`. Keep the manifest small and valid JSON.
        </artifact_file_instructions>
        """;

    internal static string NormalizeLanguage(string? language)
    {
        if (string.IsNullOrWhiteSpace(language))
            return string.Empty;

        var builder = new StringBuilder(language.Length);
        foreach (var c in language.Trim())
        {
            if (char.IsLetterOrDigit(c) || c is '+' or '#' or '-' or '_')
                builder.Append(c);
        }

        return builder.ToString();
    }

    internal static string ResolveActiveWorkspaceRoot(string? workspaceFolderPath, string applicationRoot) =>
        string.IsNullOrWhiteSpace(workspaceFolderPath)
            ? Path.GetFullPath(applicationRoot)
            : Path.GetFullPath(workspaceFolderPath);

    private static bool TryResolveAllowedPath(
        string workspaceRoot,
        string path,
        out string root,
        out string fullPath,
        out string relativePath,
        out string error)
    {
        root = NormalizeRoot(workspaceRoot);
        fullPath = string.Empty;
        relativePath = string.Empty;
        error = string.Empty;

        var candidate = path.Replace('/', Path.DirectorySeparatorChar);
        fullPath = Path.GetFullPath(Path.IsPathRooted(candidate)
            ? candidate
            : Path.Combine(root, candidate));

        if (!IsUnderRoot(fullPath, root))
        {
            error = "Artifact path must stay inside the active workspace.";
            return false;
        }

        relativePath = Path.GetRelativePath(root, fullPath);
        var allowed = false;
        foreach (var allowedRelativeRoot in AllowedRelativeRoots)
        {
            var allowedRoot = Path.GetFullPath(Path.Combine(root, allowedRelativeRoot));
            if (!IsUnderRoot(fullPath, allowedRoot))
                continue;

            allowed = true;
            break;
        }

        if (!allowed)
        {
            error = "Artifact path must be under .squad/tmp/agent-artifacts or .squad/archive/agent-artifacts.";
            return false;
        }

        return true;
    }

    private static string CopyToArchive(string root, string sourcePath, string hash)
    {
        var tempRoot = GetTempRoot(root);
        var archiveRoot = GetArchiveRoot(root);
        var relativeFromTemp = Path.GetRelativePath(tempRoot, sourcePath);
        var datedArchiveRoot = Path.Combine(archiveRoot, DateTimeOffset.Now.ToString("yyyy-MM-dd"));
        var destination = Path.GetFullPath(Path.Combine(datedArchiveRoot, relativeFromTemp));

        if (!IsUnderRoot(destination, datedArchiveRoot))
            destination = Path.Combine(datedArchiveRoot, Path.GetFileName(sourcePath));

        Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
        if (File.Exists(destination))
        {
            var existingHash = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(destination))).ToLowerInvariant();
            if (!string.Equals(existingHash, hash, StringComparison.OrdinalIgnoreCase))
            {
                var directory = Path.GetDirectoryName(destination)!;
                var fileName = Path.GetFileNameWithoutExtension(destination);
                var extension = Path.GetExtension(destination);
                destination = Path.Combine(directory, $"{fileName}-{hash[..8]}{extension}");
            }
        }

        File.Copy(sourcePath, destination, overwrite: true);
        return destination;
    }

    private static string GetTempRoot(string root) =>
        Path.GetFullPath(Path.Combine(root, ".squad", "tmp", "agent-artifacts"));

    private static string GetArchiveRoot(string root) =>
        Path.GetFullPath(Path.Combine(root, ".squad", "archive", "agent-artifacts"));

    private static bool IsUnderRoot(string path, string root)
    {
        var normalizedPath = EnsureTrailingSeparator(Path.GetFullPath(path));
        var normalizedRoot = EnsureTrailingSeparator(Path.GetFullPath(root));
        return normalizedPath.StartsWith(normalizedRoot, StringComparison.OrdinalIgnoreCase) ||
               string.Equals(Path.GetFullPath(path), Path.GetFullPath(root), StringComparison.OrdinalIgnoreCase);
    }

    private static string EnsureTrailingSeparator(string path) =>
        path.EndsWith(Path.DirectorySeparatorChar) ? path : path + Path.DirectorySeparatorChar;

    private static string NormalizeRoot(string workspaceRoot) =>
        Path.GetFullPath(workspaceRoot);

    private static string ToDisplayPath(string path) =>
        path.Replace(Path.DirectorySeparatorChar, '/').Replace(Path.AltDirectorySeparatorChar, '/');

    private static string NormalizeHash(string hash) =>
        hash.Trim().Replace("sha256:", string.Empty, StringComparison.OrdinalIgnoreCase);

    private static string DecodeUtf8(byte[] bytes)
    {
        if (bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF)
            return Encoding.UTF8.GetString(bytes, 3, bytes.Length - 3);

        return Encoding.UTF8.GetString(bytes);
    }
}
