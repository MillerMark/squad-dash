using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;

namespace SquadDash;

internal enum DocApprovalStatus { NeedsReview, Approved }

internal sealed class DocStatusStore
{
    private readonly string _docsRoot;
    private readonly string _jsonPath;
    // Key: relative path with forward slashes; Value: "Approved" (only approved entries stored)
    private Dictionary<string, string> _data;
    // Tracks all paths that have ever been in the JSON (for "was ever approved" check)
    private readonly HashSet<string> _everTracked;

    private DocStatusStore(string docsRoot, Dictionary<string, string> data)
    {
        _docsRoot = docsRoot;
        _jsonPath = Path.Combine(docsRoot, ".doc-status.json");
        _data = data;
        _everTracked = new HashSet<string>(data.Keys, StringComparer.OrdinalIgnoreCase);
    }

    public static DocStatusStore Load(string docsRoot)
    {
        var jsonPath = Path.Combine(docsRoot, ".doc-status.json");
        if (File.Exists(jsonPath))
        {
            try
            {
                var json = File.ReadAllText(jsonPath);
                var data = JsonSerializer.Deserialize<Dictionary<string, string>>(json)
                           ?? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                return new DocStatusStore(docsRoot, new Dictionary<string, string>(data, StringComparer.OrdinalIgnoreCase));
            }
            catch { }
        }
        return new DocStatusStore(docsRoot, new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase));
    }

    public DocApprovalStatus GetStatus(string filePath)
    {
        var key = GetKey(filePath);
        if (_data.TryGetValue(key, out var val) && val == "Approved")
            return DocApprovalStatus.Approved;
        return DocApprovalStatus.NeedsReview;
    }

    public void SetApproved(string filePath)
    {
        var key = GetKey(filePath);
        _data[key] = "Approved";
        _everTracked.Add(key);
        Save();
    }

    public void SetNeedsReview(string filePath)
    {
        var key = GetKey(filePath);
        // Keep in dict but mark NeedsReview so we know it was previously tracked
        _data[key] = "NeedsReview";
        _everTracked.Add(key);
        Save();
    }

    /// <summary>Returns true if this path has ever been approved (appears in JSON, even if now NeedsReview).</summary>
    public bool HasBeenTracked(string filePath)
    {
        var key = GetKey(filePath);
        return _everTracked.Contains(key);
    }

    /// <summary>Scan file for screenshot placeholders: lines matching "![Screenshot:" pattern.</summary>
    public static bool HasScreenshotPlaceholders(string filePath)
    {
        try
        {
            foreach (var line in File.ReadLines(filePath))
            {
                if (line.Contains("![Screenshot:", StringComparison.OrdinalIgnoreCase))
                    return true;
            }
        }
        catch { }
        return false;
    }

    private string GetKey(string filePath)
    {
        // Make relative to docsRoot, normalize to forward slashes, lowercase
        var rel = Path.GetRelativePath(_docsRoot, filePath);
        return rel.Replace('\\', '/');
    }

    private void Save()
    {
        try
        {
            var options = new JsonSerializerOptions { WriteIndented = true };
            var json = JsonSerializer.Serialize(_data, options);
            File.WriteAllText(_jsonPath, json);
        }
        catch { }
    }
}
