namespace SquadDash.Screenshots;

/// <summary>
/// Describes the outcome of a pixel-level comparison between a freshly-captured
/// screenshot and its stored baseline.
/// </summary>
public record ScreenshotComparisonResult(
    bool    Skipped,           // true if baseline didn't exist or couldn't be loaded
    bool    DimensionMismatch, // true if the two images have different sizes
    int     TotalPixels,
    int     DiffPixels,
    double  MatchPercent,      // 0–100; meaningful only when !Skipped && !DimensionMismatch
    string? DiffImagePath      // full path to the saved diff PNG, or null when no diff was written
);
