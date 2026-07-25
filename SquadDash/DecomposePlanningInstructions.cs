using System.IO;
using System.Linq;

namespace SquadDash;

internal static class DecomposePlanningInstructions
{
    internal const string FileName = "decompose-planning.md";

    internal static string LoadSpecification()
    {
        var assembly = typeof(DecomposePlanningInstructions).Assembly;
        var name = assembly.GetManifestResourceNames()
            .FirstOrDefault(n => n.EndsWith(FileName, StringComparison.OrdinalIgnoreCase));
        if (name is null) return string.Empty;
        using var stream = assembly.GetManifestResourceStream(name);
        using var reader = new StreamReader(stream!);
        return reader.ReadToEnd();
    }

    internal static string EnsureMaterialized(string squadFolderPath)
    {
        var directory = Path.Combine(squadFolderPath, "instructions");
        var path = Path.Combine(directory, FileName);
        var expected = LoadSpecification();
        Directory.CreateDirectory(directory);
        if (!File.Exists(path) || !string.Equals(File.ReadAllText(path), expected, StringComparison.Ordinal))
            File.WriteAllText(path, expected);
        return path;
    }

    internal static string BuildOrdinaryPromptPointer(string specificationPath) =>
        "If the user's request is too large or interdependent to implement safely in one turn, " +
        $"you MUST read `{specificationPath}` before responding, then follow its TASKS_JSON protocol. " +
        "If the user gives free-text approval or changes for a staged decomposition plan, you MUST " +
        "read the same file and follow its DECOMPOSE_DECISION_JSON protocol. Do not invent either " +
        "format from memory. Emitting TASKS_JSON proposes a plan; it does not grant execution permission.";
}
