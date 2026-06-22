namespace SquadDash;

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

internal sealed class FeatureGroupStore {
    private const string FileName = "feature-groups.json";
    private readonly string _filePath;

    internal static readonly IReadOnlyList<string> Defaults = [
        "UI & UX",
        "Performance",
        "Bug Fixes",
        "Code Maintenance",
        "Testing",
        "Security",
        "Documentation",
        "Infrastructure",
        "API & Integrations",
        "Data & Storage",
        "Developer Experience",
        "Localization",
        "Experimental",
    ];

    public FeatureGroupStore(string workspaceStateDirectory) {
        Directory.CreateDirectory(workspaceStateDirectory);
        _filePath = Path.Combine(workspaceStateDirectory, FileName);
    }

    /// <summary>
    /// Returns the current list, seeding with defaults if file absent or empty. Never throws.
    /// </summary>
    public List<string> Load() {
        if (!File.Exists(_filePath))
            return [.. Defaults];
        var items = JsonFileStorage.ReadOrDefault<List<string>>(_filePath, []);
        return items.Count > 0 ? items : [.. Defaults];
    }

    /// <summary>
    /// Atomic write.
    /// </summary>
    public void Save(IReadOnlyList<string> groups) {
        JsonFileStorage.SafeWrite(_filePath, groups, "FeatureGroupStore", "Save");
    }

    /// <summary>
    /// Adds <paramref name="groupName"/> if not already present (case-insensitive),
    /// saves, and returns the updated list.
    /// </summary>
    public List<string> EnsureGroup(string groupName) {
        var groups = Load();
        if (!groups.Any(g => string.Equals(g, groupName, StringComparison.OrdinalIgnoreCase))) {
            groups.Add(groupName);
            Save(groups);
        }
        return groups;
    }
}
