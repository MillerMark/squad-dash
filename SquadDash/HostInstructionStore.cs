using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace SquadDash;

internal enum HostInstructionKind
{
    DecomposePlanning,
    PlanningSpecialistCharter,
    PlanExecutionLoop,
}

internal sealed record HostInstructionLocation(
    HostInstructionKind Kind,
    string FileName,
    string Sha256,
    string CentralPath,
    string WorkspaceProjectionPath,
    string? LegacyWorkspacePath);

/// <summary>
/// Owns every path and write for immutable instructions shipped by SquadDash. The central copy is
/// shared across workspaces and addressed by content hash. A matching workspace projection keeps the
/// file reachable by sandboxed agents, while fixed legacy paths remain compatibility projections only.
/// </summary>
internal static class HostInstructionStore
{
    private sealed record InstructionDefinition(
        string FileName,
        string ResourceSuffix,
        string? LegacyRelativePath);

    internal static string CentralRoot =>
        Path.Combine(SquadDashPaths.AppData, "host-instructions");

    internal static string LoadEmbedded(HostInstructionKind kind)
    {
        var definition = GetDefinition(kind);
        var assembly = typeof(HostInstructionStore).Assembly;
        var resourceName = assembly.GetManifestResourceNames().FirstOrDefault(name =>
            name.EndsWith(definition.ResourceSuffix, StringComparison.OrdinalIgnoreCase));
        if (resourceName is null)
            return string.Empty;

        using var stream = assembly.GetManifestResourceStream(resourceName);
        if (stream is null)
            return string.Empty;
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }

    internal static HostInstructionLocation EnsureAvailable(
        string squadFolderPath,
        HostInstructionKind kind,
        string? centralRoot = null)
    {
        if (string.IsNullOrWhiteSpace(squadFolderPath))
            throw new ArgumentException("Squad folder cannot be empty.", nameof(squadFolderPath));

        var definition = GetDefinition(kind);
        var content = LoadEmbedded(kind);
        if (string.IsNullOrWhiteSpace(content))
            throw new InvalidOperationException(
                $"Embedded host instruction '{definition.FileName}' is unavailable.");

        var sha256 = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(content)))
            .ToLowerInvariant();
        var normalizedSquadFolder = Path.GetFullPath(squadFolderPath);
        var normalizedCentralRoot = Path.GetFullPath(centralRoot ?? CentralRoot);
        var centralDirectory = Path.Combine(
            normalizedCentralRoot,
            GetDirectoryName(kind),
            sha256);
        var centralPath = Path.Combine(centralDirectory, definition.FileName);
        var workspaceDirectory = Path.Combine(
            normalizedSquadFolder,
            "tmp",
            "host-instructions",
            GetDirectoryName(kind),
            sha256);
        var workspaceProjectionPath = Path.Combine(workspaceDirectory, definition.FileName);

        TryWriteOptionalProjection(
            () =>
            {
                WriteIfChangedAtomic(centralPath, content);
                WriteManifestIfChanged(centralDirectory, kind, definition.FileName, sha256);
            },
            $"central {kind} instruction");
        WriteIfChangedAtomic(workspaceProjectionPath, content);
        TryWriteOptionalProjection(
            () => WriteManifestIfChanged(workspaceDirectory, kind, definition.FileName, sha256),
            $"workspace {kind} instruction manifest");

        string? legacyWorkspacePath = null;
        if (definition.LegacyRelativePath is not null)
        {
            legacyWorkspacePath = Path.Combine(normalizedSquadFolder, definition.LegacyRelativePath);
            TryWriteOptionalProjection(
                () => WriteIfChangedAtomic(legacyWorkspacePath, content),
                $"legacy {kind} instruction");
        }

        return new HostInstructionLocation(
            kind,
            definition.FileName,
            sha256,
            centralPath,
            workspaceProjectionPath,
            legacyWorkspacePath);
    }

    internal static IReadOnlyList<HostInstructionLocation> EnsureWorkspaceBundle(
        string squadFolderPath,
        string? centralRoot = null) =>
        Enum.GetValues<HostInstructionKind>()
            .Select(kind => EnsureAvailable(squadFolderPath, kind, centralRoot))
            .ToArray();

    internal static string? GetLegacyWorkspacePath(
        string squadFolderPath,
        HostInstructionKind kind)
    {
        var relativePath = GetDefinition(kind).LegacyRelativePath;
        return relativePath is null
            ? null
            : Path.Combine(Path.GetFullPath(squadFolderPath), relativePath);
    }

    private static InstructionDefinition GetDefinition(HostInstructionKind kind) => kind switch
    {
        HostInstructionKind.DecomposePlanning => new(
            "decompose-planning.md",
            "decompose-planning.md",
            Path.Combine("instructions", "decompose-planning.md")),
        HostInstructionKind.PlanningSpecialistCharter => new(
            "planning-specialist-charter.md",
            "planning-specialist-charter.md",
            null),
        HostInstructionKind.PlanExecutionLoop => new(
            "loop-executing-plan.md",
            "loop-executing-plan.md",
            "loop-executing-plan.md"),
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, null),
    };

    private static string GetDirectoryName(HostInstructionKind kind) => kind switch
    {
        HostInstructionKind.DecomposePlanning => "decompose-planning",
        HostInstructionKind.PlanningSpecialistCharter => "planning-specialist",
        HostInstructionKind.PlanExecutionLoop => "plan-execution",
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, null),
    };

    private static void WriteManifestIfChanged(
        string directory,
        HostInstructionKind kind,
        string fileName,
        string sha256)
    {
        var manifest = JsonSerializer.Serialize(new
        {
            schemaVersion = 1,
            instruction = kind.ToString(),
            fileName,
            sha256,
            source = "SquadDash embedded host instruction",
        }, new JsonSerializerOptions { WriteIndented = true });
        WriteIfChangedAtomic(Path.Combine(directory, "manifest.json"), manifest + Environment.NewLine);
    }

    private static void WriteIfChangedAtomic(string path, string content)
    {
        if (File.Exists(path) && string.Equals(File.ReadAllText(path), content, StringComparison.Ordinal))
            return;

        var directory = Path.GetDirectoryName(path)
            ?? throw new InvalidOperationException($"Instruction path has no directory: {path}");
        Directory.CreateDirectory(directory);
        var tempPath = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            File.WriteAllText(tempPath, content, Encoding.UTF8);
            File.Move(tempPath, path, overwrite: true);
        }
        finally
        {
            if (File.Exists(tempPath)) File.Delete(tempPath);
        }
    }

    private static void TryWriteOptionalProjection(Action write, string description)
    {
        try
        {
            write();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            SquadDashTrace.Write(
                TraceCategory.General,
                $"Could not refresh {description}; continuing with the workspace runtime projection: {ex.Message}");
        }
    }
}
