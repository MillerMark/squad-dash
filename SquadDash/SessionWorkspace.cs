using System;
using System.IO;
using System.Linq;

namespace SquadDash;

internal sealed record SessionWorkspace(
    string FolderPath,
    string? SolutionPath,
    string? SolutionName) {

    public string SquadFolderPath => Path.Combine(FolderPath, ".squad");

    public static SessionWorkspace Create(string folderPath) {
        var normalizedFolder = Path.GetFullPath(folderPath)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

        var solutionPath = Directory
            .EnumerateFiles(normalizedFolder, "*.sln", SearchOption.TopDirectoryOnly)
            .OrderBy(Path.GetFileName)
            .FirstOrDefault()
            ?? Directory
                .EnumerateFiles(normalizedFolder, "*.slnx", SearchOption.TopDirectoryOnly)
                .OrderBy(Path.GetFileName)
                .FirstOrDefault();

        return new SessionWorkspace(
            normalizedFolder,
            solutionPath,
            solutionPath is null ? null : Path.GetFileName(solutionPath));
    }
}
