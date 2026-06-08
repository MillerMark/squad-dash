using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace SquadDash;

internal static class SquadDashPaths
{
    public static string AppData =>
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "SquadDash");

    /// <summary>
    /// Returns the per-workspace state directory under <c>%LocalAppData%\SquadDash\workspaces\</c>.
    /// Uses the same naming convention as <see cref="PastedImageStore"/> and
    /// <see cref="WorkspaceConversationStore"/>: sanitized folder name + first 12 chars of SHA-256
    /// hash of the normalized <paramref name="applicationRoot"/> path.
    /// </summary>
    public static string WorkspaceStateDirectory(string applicationRoot)
    {
        var normalized = Path.GetFullPath(applicationRoot)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

        var name = Path.GetFileName(normalized);
        if (string.IsNullOrWhiteSpace(name)) name = "workspace";

        var sanitized = new string(
            System.Linq.Enumerable.Select(name, c => char.IsLetterOrDigit(c) ? c : '-').ToArray())
            .Trim('-');
        if (string.IsNullOrWhiteSpace(sanitized)) sanitized = "workspace";

        var hashBytes = SHA256.HashData(Encoding.UTF8.GetBytes(normalized));
        var hashSb = new StringBuilder(hashBytes.Length * 2);
        foreach (var b in hashBytes) hashSb.Append(b.ToString("x2"));
        var hash = hashSb.ToString()[..12];

        return Path.Combine(AppData, "workspaces", $"{sanitized}-{hash}");
    }
}
