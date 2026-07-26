namespace SquadDash;

using System;
using System.IO;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using SquadDash.Screenshots;

/// <summary>
/// Orchestrates the screenshot subsystem: definition registry caching, health checker
/// construction, definition theme sync, and doc-image description extraction.
/// WPF-heavy operations (window management, capture rendering) remain in MainWindow.
/// </summary>
internal sealed class ScreenshotService
{
    private readonly IWorkspacePaths           _workspacePaths;
    private readonly UiActionReplayRegistry    _uiActionReplayRegistry;
    private readonly FixtureLoaderRegistry     _fixtureLoaderRegistry;
    private readonly ILiveElementLocator       _elementLocator;

    public ScreenshotDefinitionRegistry? CachedDefinitionRegistry { get; set; }
    public ScreenshotHealthChecker       HealthChecker            { get; private set; } = null!;

    public ScreenshotService(
        IWorkspacePaths        workspacePaths,
        UiActionReplayRegistry uiActionReplayRegistry,
        FixtureLoaderRegistry  fixtureLoaderRegistry,
        ILiveElementLocator    elementLocator)
    {
        _workspacePaths         = workspacePaths         ?? throw new ArgumentNullException(nameof(workspacePaths));
        _uiActionReplayRegistry = uiActionReplayRegistry ?? throw new ArgumentNullException(nameof(uiActionReplayRegistry));
        _fixtureLoaderRegistry  = fixtureLoaderRegistry  ?? throw new ArgumentNullException(nameof(fixtureLoaderRegistry));
        _elementLocator         = elementLocator         ?? throw new ArgumentNullException(nameof(elementLocator));
    }

    // ── Registry warm-up ───────────────────────────────────────────────────────

    /// <summary>
    /// Loads the screenshot definition registry from disk and constructs a fresh
    /// <see cref="ScreenshotHealthChecker"/> so that right-click refresh and the
    /// health window are immediately available without an on-demand async delay.
    /// </summary>
    public async Task WarmDefinitionRegistryCacheAsync()
    {
        try
        {
            var screenshotsDir = _workspacePaths.ScreenshotsDirectory;
            CachedDefinitionRegistry = await ScreenshotDefinitionRegistry
                .LoadAsync(screenshotsDir)
                .ConfigureAwait(true);

            HealthChecker = new ScreenshotHealthChecker(
                CachedDefinitionRegistry,
                _uiActionReplayRegistry,
                _fixtureLoaderRegistry,
                _elementLocator,
                screenshotsDir);
        }
        catch (Exception ex)
        {
            SquadDashTrace.Write("Screenshot", $"WarmDefinitionRegistryCacheAsync failed: {ex.Message}");
        }
    }

    // ── Definition theme sync ──────────────────────────────────────────────────

    /// <summary>
    /// When the user replaces a doc screenshot via clipboard paste, updates the matching
    /// <see cref="ScreenshotDefinition"/> to use <paramref name="themeName"/>
    /// so that a subsequent "Refresh screenshot" captures in the same theme.
    /// </summary>
    public async Task SyncDefinitionThemeAsync(string fullDocImagePath, string themeName)
    {
        try
        {
            var screenshotsDir = _workspacePaths.ScreenshotsDirectory;
            var registry = await ScreenshotDefinitionRegistry.LoadAsync(screenshotsDir)
                                                             .ConfigureAwait(true);
            var def = registry.TryGetByDocImagePath(fullDocImagePath, screenshotsDir);
            if (def is null) return;

            registry.AddOrUpdate(def with { Theme = themeName });
            await registry.SaveAsync().ConfigureAwait(true);
            CachedDefinitionRegistry = registry;
        }
        catch (Exception ex)
        {
            SquadDashTrace.Write("Screenshot", $"SyncDefinitionTheme failed: {ex.Message}");
        }
    }

    // ── Doc-image description extraction ──────────────────────────────────────

    /// <summary>
    /// Returns a pre-fill description for the screenshot overlay by reading
    /// <paramref name="docPath"/> and extracting, in preference order:
    /// (1) the 📸 blockquote description on the line immediately after the image tag, or
    /// (2) the alt text from the image tag itself.
    /// Returns an empty string if neither is found or if the file cannot be read.
    /// </summary>
    public static string ExtractDocImageDescription(string docPath, string imagePath)
    {
        if (!File.Exists(docPath)) return string.Empty;

        string text;
        try { text = File.ReadAllText(docPath); }
        catch { return string.Empty; }

        var normalizedTarget = imagePath.Replace('\\', '/');
        var lines = text.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');

        for (var i = 0; i < lines.Length; i++)
        {
            if (!lines[i].Replace('\\', '/').Contains(normalizedTarget)) continue;

            // Prefer the 📸 blockquote description on the next line.
            if (i + 1 < lines.Length)
            {
                var next = lines[i + 1].Trim();
                if (next.Contains("📸") || next.Contains("Screenshot needed"))
                {
                    var stripped = Regex.Replace(
                        next,
                        @"^>\s*📸\s*\*?Screenshot needed:\s*",
                        "",
                        RegexOptions.IgnoreCase).TrimEnd('*').Trim();
                    if (!string.IsNullOrWhiteSpace(stripped)) return stripped;
                }
            }

            // Fall back to alt text.
            var altMatch = Regex.Match(
                lines[i],
                @"!\[([^\]]*)\]\(" + Regex.Escape(normalizedTarget) + @"\)");
            if (altMatch.Success)
                return altMatch.Groups[1].Value.Trim();

            break;
        }

        return string.Empty;
    }
}
