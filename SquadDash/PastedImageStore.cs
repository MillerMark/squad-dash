using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Media.Imaging;

namespace SquadDash;

/// <summary>
/// Manages the <c>pasted-images\</c> subfolder of the per-workspace state directory.
/// Images are saved when pasted and pruned 14 days after <see cref="SetSubmittedAt"/> is called.
/// </summary>
internal sealed class PastedImageStore
{
    private static readonly TimeSpan RetentionAfterSubmission = TimeSpan.FromDays(14);
    private const string SubfolderName = "pasted-images";

    private readonly string _rootDirectory; // %LocalAppData%\SquadDash\workspaces\

    public PastedImageStore()
        : this(Path.Combine(SquadDashPaths.AppData, "workspaces")) { }

    internal PastedImageStore(string rootDirectory)
    {
        _rootDirectory = rootDirectory;
    }

    /// <summary>
    /// Returns the <c>pasted-images\</c> directory for a given workspace folder.
    /// The directory path mirrors how <see cref="WorkspaceConversationStore"/> names workspace directories.
    /// </summary>
    public string GetImageDirectory(string workspaceFolder)
    {
        var normalized = NormalizeWorkspaceFolder(workspaceFolder);
        var dirName    = BuildWorkspaceDirectoryName(normalized);
        return Path.Combine(_rootDirectory, dirName, SubfolderName);
    }

    /// <summary>
    /// Saves a WPF <see cref="BitmapSource"/> as PNG and returns the full path.
    /// </summary>
    public string SaveImage(BitmapSource bitmap, string workspaceFolder)
    {
        var dir = GetImageDirectory(workspaceFolder);
        Directory.CreateDirectory(dir);

        var timestamp = DateTime.UtcNow.ToString("yyyyMMdd-HHmmss");
        var unique    = Guid.NewGuid().ToString("N")[..8];
        var filePath  = Path.Combine(dir, $"{timestamp}-{unique}.png");

        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var stream = File.OpenWrite(filePath);
        encoder.Save(stream);

        return filePath;
    }

    /// <summary>
    /// Writes a <c>.submitted</c> sidecar file next to the image recording the UTC submission time.
    /// Call this at prompt dispatch, not at paste time.
    /// </summary>
    public void SetSubmittedAt(string imagePath, DateTime submittedAtUtc)
    {
        try
        {
            File.WriteAllText(imagePath + ".submitted", submittedAtUtc.ToString("O"));
        }
        catch { /* best-effort */ }
    }

    /// <summary>
    /// Fire-and-forget: deletes images whose submission sidecar is older than 14 days,
    /// and orphaned images (no sidecar) older than 30 days by file creation time.
    /// Never throws.
    /// </summary>
    public Task PruneAsync(string workspaceFolder) => Task.Run(() =>
    {
        try
        {
            var dir = GetImageDirectory(workspaceFolder);
            if (!Directory.Exists(dir)) return;

            var now = DateTime.UtcNow;

            foreach (var png in Directory.EnumerateFiles(dir, "*.png"))
            {
                try
                {
                    var sidecar = png + ".submitted";
                    if (File.Exists(sidecar))
                    {
                        var text = File.ReadAllText(sidecar).Trim();
                        if (DateTime.TryParse(text, out var submittedAt)
                            && now - submittedAt >= RetentionAfterSubmission)
                        {
                            File.Delete(png);
                            File.Delete(sidecar);
                        }
                    }
                    else
                    {
                        var created = File.GetCreationTimeUtc(png);
                        if (now - created >= TimeSpan.FromDays(30))
                            File.Delete(png);
                    }
                }
                catch { /* skip individual file errors */ }
            }
        }
        catch { /* swallow directory-level errors */ }
    });

    /// <summary>
    /// Deletes ALL images in the pasted-images folder for the given workspace.
    /// Returns the number of bytes freed.
    /// </summary>
    public long DeleteAll(string workspaceFolder)
    {
        var dir = GetImageDirectory(workspaceFolder);
        if (!Directory.Exists(dir)) return 0;

        long freed = 0;
        foreach (var f in Directory.EnumerateFiles(dir))
        {
            try
            {
                freed += new FileInfo(f).Length;
                File.Delete(f);
            }
            catch { /* best-effort */ }
        }
        return freed;
    }

    // Mirrors WorkspaceConversationStore.NormalizeWorkspaceFolder — must stay in sync.
    private static string NormalizeWorkspaceFolder(string workspaceFolder)
    {
        if (string.IsNullOrWhiteSpace(workspaceFolder))
            return "workspace";

        return Path.GetFullPath(workspaceFolder)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
    }

    // Mirrors WorkspaceConversationStore.BuildWorkspaceDirectoryName — must stay in sync.
    private static string BuildWorkspaceDirectoryName(string normalizedWorkspace)
    {
        var name = Path.GetFileName(normalizedWorkspace);
        if (string.IsNullOrWhiteSpace(name))
            name = "workspace";

        var sanitized = new string(
            System.Linq.Enumerable.Select(name, c => char.IsLetterOrDigit(c) ? c : '-')
                .ToArray())
            .Trim('-');

        if (string.IsNullOrWhiteSpace(sanitized))
            sanitized = "workspace";

        var hash = ComputeHash(normalizedWorkspace);
        return $"{sanitized}-{hash[..12]}";
    }

    private static string ComputeHash(string value)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(value));
        var sb    = new StringBuilder(bytes.Length * 2);
        foreach (var b in bytes)
            sb.Append(b.ToString("x2"));
        return sb.ToString();
    }
}
