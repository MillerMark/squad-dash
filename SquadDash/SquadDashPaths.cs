using System.IO;

namespace SquadDash;

internal static class SquadDashPaths
{
    public static string AppData =>
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "SquadDash");
}
