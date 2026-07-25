using System.IO;
using System.Linq;

namespace SquadDash;

internal static class DecomposePlanningInstructions
{
    internal const string FileName = "decompose-planning.md";
    private static readonly HashSet<string> MaterializedPaths = new(StringComparer.OrdinalIgnoreCase);
    private static readonly object MaterializeGate = new();

    internal static string LoadSpecification()
    {
        var assembly = typeof(DecomposePlanningInstructions).Assembly;
        var name = assembly.GetManifestResourceNames()
            .FirstOrDefault(n => n.EndsWith(FileName, StringComparison.OrdinalIgnoreCase));
        if (name is null)
        {
            SquadDashTrace.Write(TraceCategory.General,
                "Embedded decompose-planning.md resource was not found.");
            return string.Empty;
        }
        using var stream = assembly.GetManifestResourceStream(name);
        using var reader = new StreamReader(stream!);
        return reader.ReadToEnd();
    }

    internal static string EnsureMaterialized(string squadFolderPath)
    {
        var directory = Path.Combine(squadFolderPath, "instructions");
        var path = Path.Combine(directory, FileName);
        lock (MaterializeGate)
        {
            if (MaterializedPaths.Contains(path)) return path;
            var expected = LoadSpecification();
            if (string.IsNullOrWhiteSpace(expected))
                throw new InvalidOperationException("The embedded decomposition specification is unavailable.");
            Directory.CreateDirectory(directory);
            if (!File.Exists(path) || !string.Equals(File.ReadAllText(path), expected, StringComparison.Ordinal))
            {
                var tempPath = path + ".tmp";
                File.WriteAllText(tempPath, expected);
                File.Move(tempPath, path, overwrite: true);
            }
            MaterializedPaths.Add(path);
        }
        return path;
    }

    internal static string BuildOrdinaryPromptPointer(string specificationPath) =>
        "If the user's request is too large or interdependent to implement safely in one turn, " +
        $"you MUST read `{specificationPath}` before responding, then follow its TASKS_JSON protocol. " +
        "If the user gives free-text approval or changes for a staged decomposition plan, you MUST " +
        "read the same file and follow its DECOMPOSE_DECISION_JSON protocol. Do not invent either " +
        "format from memory. Emitting TASKS_JSON proposes a plan; it does not grant execution permission.";

    internal static string BuildPendingPlanContext(string squadFolderPath)
    {
        var plans = new PendingDecomposePlanStore(squadFolderPath).LoadAll();
        if (plans.Count == 0) return string.Empty;
        return "\nPending decomposition plans (use the exact revision in DECOMPOSE_DECISION_JSON):\n" +
               string.Join("\n", plans.Select(p =>
                   $"- groupId={p.Group.GroupId}; revision={p.Revision}; proposedBranch={p.Group.Branch}"));
    }

    internal static string BuildOrdinaryPromptContext(string squadFolderPath)
    {
        try
        {
            var path = EnsureMaterialized(squadFolderPath);
            return BuildOrdinaryPromptPointer(path) + BuildPendingPlanContext(squadFolderPath);
        }
        catch (Exception ex)
        {
            SquadDashTrace.Write(TraceCategory.General,
                $"Could not materialize decomposition instructions in '{squadFolderPath}': {ex.Message}");
            var specification = LoadSpecification();
            return string.IsNullOrWhiteSpace(specification)
                ? "Large-task decomposition is unavailable for this turn; do not emit TASKS_JSON or DECOMPOSE_DECISION_JSON."
                : "The decomposition instruction file could not be materialized. Follow this embedded specification for this turn:\n\n" + specification;
        }
    }
}
