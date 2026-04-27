using System.IO;

namespace SquadDash.Tests;

internal sealed class TestWorkspace : IDisposable {
    public TestWorkspace() {
        RootPath = Path.Combine(
            Path.GetTempPath(),
            "SquadDashTests",
            Path.GetRandomFileName());
        Directory.CreateDirectory(RootPath);
    }

    public string RootPath { get; }

    public string GetPath(params string[] segments) {
        return Path.Combine(new[] { RootPath }.Concat(segments).ToArray());
    }

    public void CreateFile(string relativePath, string content = "") {
        var fullPath = GetPath(relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
        File.WriteAllText(fullPath, content);
    }

    public void Dispose() {
        if (Directory.Exists(RootPath))
            Directory.Delete(RootPath, recursive: true);
    }
}
