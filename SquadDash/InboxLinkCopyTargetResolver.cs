using System.IO;

namespace SquadDash;

internal sealed record InboxLinkCopyTarget(string MenuLabel, string ClipboardText);

internal static class InboxLinkCopyTargetResolver
{
    private const string OpenPathPrefix = "app://open-path:";

    internal static InboxLinkCopyTarget? Resolve(string? target, string? workspaceFolder)
    {
        if (string.IsNullOrWhiteSpace(target))
            return null;

        var trimmed = target.Trim();
        if (trimmed.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
            trimmed.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            return new InboxLinkCopyTarget("Copy Link", trimmed);

        if (!trimmed.StartsWith(OpenPathPrefix, StringComparison.OrdinalIgnoreCase))
            return null;

        try
        {
            var rawPath = Uri.UnescapeDataString(trimmed[OpenPathPrefix.Length..]).Trim();
            if (string.IsNullOrWhiteSpace(rawPath))
                return null;

            var expanded = Environment.ExpandEnvironmentVariables(rawPath);
            var resolved = Path.GetFullPath(Path.IsPathRooted(expanded)
                ? expanded
                : Path.Combine(workspaceFolder ?? Environment.CurrentDirectory, expanded));
            var label = Directory.Exists(resolved)
                ? "Copy Folder Path"
                : File.Exists(resolved)
                    ? "Copy File Path"
                    : "Copy Path";
            return new InboxLinkCopyTarget(label, resolved);
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException or UriFormatException)
        {
            return null;
        }
    }
}
