using System;
using System.IO;

namespace SquadDash;

internal static class JumpListLaunchPathResolver
{
    internal static string Resolve(
        string applicationRoot,
        string appBaseDirectory,
        string currentProcessPath,
        Func<string, bool>? fileExists = null)
    {
        fileExists ??= File.Exists;

        var rootLauncher = Path.Combine(applicationRoot, "SquadDash.exe");
        if (fileExists(rootLauncher))
            return rootLauncher;

        var adjacentLauncher = Path.Combine(appBaseDirectory, "SquadDash.exe");
        if (fileExists(adjacentLauncher))
            return adjacentLauncher;

        return currentProcessPath;
    }
}
