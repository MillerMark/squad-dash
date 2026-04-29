using System;
using System.IO;

namespace SquadDash;

internal static class LoopOutputStore
{
    private static string GetLogsDir() =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "SquadDash", "loop-logs");

    public static void SaveLog(string content)
    {
        if (string.IsNullOrWhiteSpace(content)) return;
        var dir = GetLogsDir();
        Directory.CreateDirectory(dir);
        var n = 1;
        string path;
        do { path = Path.Combine(dir, $"loop-output-{n:D3}.log"); n++; }
        while (File.Exists(path));
        File.WriteAllText(path, content);
    }
}
