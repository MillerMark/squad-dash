using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace SquadDash;

internal enum WorkspaceUpgradeFileCategory
{
    SafeAdditionOrRefresh,
    TrackedLocalChange,
    GeneratedFile
}

internal sealed record WorkspaceUpgradeFilePreview(
    string DisplayPath,
    string CurrentPath,
    string? ProposedTemplatePath,
    string DiffText,
    WorkspaceUpgradeFileCategory Category,
    bool IsExactTemplateCopy,
    string? UnavailableReason = null)
{
    public bool IsAvailable => string.IsNullOrWhiteSpace(UnavailableReason);
}

internal static class WorkspaceUpgradeFilePreviewService
{
    internal static async Task<WorkspaceUpgradeFilePreview> CreateAsync(
        ResolvedCliCandidate? candidate,
        string workspaceDirectory,
        string reportedPath,
        WorkspaceUpgradeFileCategory category)
    {
        var displayPath = NormalizeDisplayPath(reportedPath);
        var currentPath = ResolveCurrentPath(workspaceDirectory, displayPath);
        if (currentPath is null)
        {
            return Unavailable(
                displayPath,
                workspaceDirectory,
                category,
                "The reported path resolves outside this workspace, so it cannot be previewed safely.");
        }

        var packageRoot = ResolvePackageRoot(candidate, workspaceDirectory);
        if (packageRoot is null)
        {
            return Unavailable(
                displayPath,
                currentPath,
                category,
                "The active Squad installation does not expose its template directory.");
        }

        var templatesRoot = Path.Combine(packageRoot, "templates");
        var templateRelativePath = ResolveTemplateRelativePath(workspaceDirectory, currentPath, displayPath);
        if (templateRelativePath is null)
        {
            return Unavailable(
                displayPath,
                currentPath,
                category,
                "Squad did not expose an incoming template mapping for this generated path.");
        }

        var proposedPath = Path.GetFullPath(Path.Combine(templatesRoot, templateRelativePath));
        if (!IsWithin(proposedPath, templatesRoot) || !File.Exists(proposedPath))
        {
            return Unavailable(
                displayPath,
                currentPath,
                category,
                "The incoming Squad template for this file could not be found.");
        }

        try
        {
            var currentContent = File.Exists(currentPath)
                ? await File.ReadAllTextAsync(currentPath).ConfigureAwait(false)
                : string.Empty;
            var proposedContent = await File.ReadAllTextAsync(proposedPath).ConfigureAwait(false);
            var isExactTemplateCopy = IsExactTemplateCopy(displayPath);

            if (IsCoordinatorAgentPath(displayPath) && !string.IsNullOrWhiteSpace(candidate?.Version))
                proposedContent = StampCoordinatorVersion(proposedContent, candidate.Version!);

            var diff = await BuildUnifiedDiffAsync(currentContent, proposedContent).ConfigureAwait(false);
            return new WorkspaceUpgradeFilePreview(
                displayPath,
                currentPath,
                proposedPath,
                diff,
                category,
                isExactTemplateCopy);
        }
        catch (Exception ex)
        {
            SquadDashTrace.Write("Workspace", $"Workspace upgrade preview failed for '{displayPath}': {ex.Message}");
            return Unavailable(displayPath, currentPath, category, ex.Message);
        }
    }

    private static WorkspaceUpgradeFilePreview Unavailable(
        string displayPath,
        string currentPath,
        WorkspaceUpgradeFileCategory category,
        string reason) =>
        new(displayPath, currentPath, null, string.Empty, category, false, reason);

    private static string NormalizeDisplayPath(string path) =>
        path.Trim().Replace('\\', '/');

    private static string? ResolveCurrentPath(string workspaceDirectory, string displayPath)
    {
        var workspaceRoot = Path.GetFullPath(workspaceDirectory)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var layout = SquadWorkspaceLayoutResolver.Resolve(workspaceRoot);
        var squadRoot = layout?.ProjectSquadFolderPath ?? Path.Combine(workspaceRoot, ".squad");
        var normalized = displayPath.Replace('/', Path.DirectorySeparatorChar);

        string candidate;
        if (normalized.StartsWith($"..{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
            candidate = Path.GetFullPath(Path.Combine(squadRoot, normalized));
        else if (normalized.StartsWith($".github{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase) ||
                 normalized.StartsWith($".squad{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase) ||
                 normalized.StartsWith($".ai-team{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase))
            candidate = Path.GetFullPath(Path.Combine(workspaceRoot, normalized));
        else
            candidate = Path.GetFullPath(Path.Combine(squadRoot, normalized));

        return IsWithin(candidate, workspaceRoot) ? candidate : null;
    }

    private static string? ResolveTemplateRelativePath(
        string workspaceDirectory,
        string currentPath,
        string displayPath)
    {
        var workspaceRelative = Path.GetRelativePath(workspaceDirectory, currentPath).Replace('\\', '/');
        if (IsCoordinatorAgentPath(workspaceRelative))
            return "squad.agent.md.template";
        if (workspaceRelative.StartsWith(".github/skills/", StringComparison.OrdinalIgnoreCase))
            return "skills/" + workspaceRelative[".github/skills/".Length..];
        if (workspaceRelative.StartsWith(".github/workflows/", StringComparison.OrdinalIgnoreCase))
            return "workflows/" + workspaceRelative[".github/workflows/".Length..];

        var squadRelative = displayPath;
        if (squadRelative.StartsWith("../", StringComparison.Ordinal))
            return null;
        if (squadRelative.StartsWith(".squad/", StringComparison.OrdinalIgnoreCase))
            squadRelative = squadRelative[".squad/".Length..];
        else if (squadRelative.StartsWith(".ai-team/", StringComparison.OrdinalIgnoreCase))
            squadRelative = squadRelative[".ai-team/".Length..];

        if (squadRelative.StartsWith("templates/", StringComparison.OrdinalIgnoreCase))
            return squadRelative["templates/".Length..];

        return squadRelative.ToLowerInvariant() switch
        {
            "casting/registry.json" => "casting-registry.json",
            "casting/policy.json" => "casting-policy.json",
            "casting/history.json" => "casting-history.json",
            _ => squadRelative
        };
    }

    private static string? ResolvePackageRoot(ResolvedCliCandidate? candidate, string workspaceDirectory)
    {
        var starts = new List<string>();
        if (!string.IsNullOrWhiteSpace(candidate?.EntryScriptPath))
            starts.Add(Path.GetDirectoryName(Path.GetFullPath(candidate.EntryScriptPath))!);
        if (!string.IsNullOrWhiteSpace(candidate?.ExecutablePath))
            starts.Add(Path.GetDirectoryName(Path.GetFullPath(candidate.ExecutablePath))!);
        starts.Add(Path.Combine(workspaceDirectory, "node_modules", "@bradygaster", "squad-cli"));

        foreach (var start in starts.Where(Directory.Exists))
        {
            var current = new DirectoryInfo(start);
            for (var depth = 0; current is not null && depth < 6; depth++, current = current.Parent)
            {
                if (IsSquadCliPackage(current.FullName))
                    return current.FullName;
            }

            var siblingPackage = Path.GetFullPath(Path.Combine(start, "..", "@bradygaster", "squad-cli"));
            if (IsSquadCliPackage(siblingPackage))
                return siblingPackage;
        }

        return null;
    }

    private static bool IsSquadCliPackage(string directory)
    {
        var packageJson = Path.Combine(directory, "package.json");
        var templates = Path.Combine(directory, "templates");
        if (!File.Exists(packageJson) || !Directory.Exists(templates))
            return false;

        try
        {
            using var document = JsonDocument.Parse(File.ReadAllText(packageJson));
            return document.RootElement.TryGetProperty("name", out var name) &&
                   string.Equals(name.GetString(), "@bradygaster/squad-cli", StringComparison.Ordinal);
        }
        catch
        {
            return false;
        }
    }

    private static bool IsExactTemplateCopy(string displayPath)
    {
        var normalized = NormalizeDisplayPath(displayPath);
        return !IsCoordinatorAgentPath(normalized) &&
               !normalized.Contains(".github/workflows/", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsCoordinatorAgentPath(string path) =>
        NormalizeDisplayPath(path).EndsWith(".github/agents/squad.agent.md", StringComparison.OrdinalIgnoreCase);

    private static string StampCoordinatorVersion(string content, string version)
    {
        content = Regex.Replace(
            content,
            @"<!-- version: [^>]+ -->",
            $"<!-- version: {version} -->",
            RegexOptions.CultureInvariant);
        content = Regex.Replace(
            content,
            @"- \*\*Version:\*\* [0-9.]+(?:-[a-z]+(?:\.\d+)?)?",
            $"- **Version:** {version}",
            RegexOptions.CultureInvariant);
        return content.Replace("`Squad v{version}`", $"`Squad v{version}`", StringComparison.Ordinal);
    }

    private static async Task<string> BuildUnifiedDiffAsync(string currentContent, string proposedContent)
    {
        if (string.Equals(NormalizeLines(currentContent), NormalizeLines(proposedContent), StringComparison.Ordinal))
            return string.Empty;

        var tempRoot = Path.Combine(Path.GetTempPath(), "SquadDash", "workspace-upgrade-preview", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempRoot);
        var currentFile = Path.Combine(tempRoot, "current.txt");
        var proposedFile = Path.Combine(tempRoot, "proposed.txt");

        try
        {
            await File.WriteAllTextAsync(currentFile, currentContent, new UTF8Encoding(false)).ConfigureAwait(false);
            await File.WriteAllTextAsync(proposedFile, proposedContent, new UTF8Encoding(false)).ConfigureAwait(false);

            var startInfo = new ProcessStartInfo("git")
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
                StandardOutputEncoding = Encoding.UTF8
            };
            foreach (var argument in new[]
                     {
                         "diff", "--no-index", "--no-ext-diff", "--no-color", "--unified=3", "--",
                         currentFile, proposedFile
                     })
                startInfo.ArgumentList.Add(argument);

            using var process = Process.Start(startInfo) ??
                                throw new InvalidOperationException("Git could not be started for the preview diff.");
            var outputTask = process.StandardOutput.ReadToEndAsync();
            var errorTask = process.StandardError.ReadToEndAsync();
            await process.WaitForExitAsync().ConfigureAwait(false);
            var output = await outputTask.ConfigureAwait(false);
            var error = await errorTask.ConfigureAwait(false);
            if (process.ExitCode is not (0 or 1))
                throw new InvalidOperationException(string.IsNullOrWhiteSpace(error) ? "Git diff failed." : error.Trim());
            return output;
        }
        finally
        {
            try
            {
                Directory.Delete(tempRoot, recursive: true);
            }
            catch
            {
                // Preview cleanup is best-effort; stale temp content is non-authoritative.
            }
        }
    }

    private static string NormalizeLines(string value) =>
        value.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n');

    private static bool IsWithin(string candidate, string root)
    {
        var normalizedCandidate = Path.GetFullPath(candidate)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var normalizedRoot = Path.GetFullPath(root)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        return string.Equals(normalizedCandidate, normalizedRoot, StringComparison.OrdinalIgnoreCase) ||
               normalizedCandidate.StartsWith(normalizedRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
    }
}
