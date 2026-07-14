namespace SquadDash;

using System;
using System.Collections.Generic;
using System.IO;

/// <summary>
/// Lightweight on-disk cache mapping commit SHA → AI-assigned feature group.
/// Used by CommitActivityGraphWindow to remember AI-assigned categories across sessions.
/// </summary>
internal sealed class CommitCategoryCache
{
    private const string FileName = "commit-category-cache.json";
    private readonly string _filePath;
    private Dictionary<string, string> _data;

    public CommitCategoryCache(string workspaceStateDirectory)
    {
        Directory.CreateDirectory(workspaceStateDirectory);
        _filePath = Path.Combine(workspaceStateDirectory, FileName);
        _data = JsonFileStorage.ReadOrDefault<Dictionary<string, string>>(
            _filePath,
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase));
    }

    public bool TryGetGroup(string sha, out string? group)
    {
        if (_data.TryGetValue(sha, out group)) return true;
        // Try prefix match (short SHA vs full SHA)
        foreach (var kv in _data)
        {
            if (kv.Key.StartsWith(sha, StringComparison.OrdinalIgnoreCase) ||
                sha.StartsWith(kv.Key, StringComparison.OrdinalIgnoreCase))
            {
                group = kv.Value;
                return true;
            }
        }
        group = null;
        return false;
    }

    public void SetGroup(string sha, string group)
    {
        _data[sha] = group;
    }

    public int MergeGroups(IReadOnlyList<(string Source, string Target)> merges)
    {
        var mergeMap = merges.ToDictionary(m => m.Source, m => m.Target, StringComparer.OrdinalIgnoreCase);
        var changed = 0;
        foreach (var sha in _data.Keys.ToList())
        {
            if (!mergeMap.TryGetValue(_data[sha], out var target)) continue;
            _data[sha] = target;
            changed++;
        }
        return changed;
    }

    public void Save()
    {
        JsonFileStorage.SafeWrite(_filePath, _data, "CommitCategoryCache", "Save");
    }
}
