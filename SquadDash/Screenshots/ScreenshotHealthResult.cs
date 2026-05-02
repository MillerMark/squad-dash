using System;
using System.Collections.Generic;
using System.Linq;

namespace SquadDash.Screenshots;

public enum ScreenshotHealthStatus { Pass, Warning, Error, NotCaptured, Stale }
public enum ScreenshotIssueSeverity { Error, Warning, Info }

public record ScreenshotIssue(
    ScreenshotIssueSeverity Severity,
    string                  Code,
    string                  Message,
    string?                 AnchorEdge   = null,
    string?                 ElementName  = null
);

public record ScreenshotHealthResult(
    string                         DefinitionName,
    ScreenshotHealthStatus         Status,
    IReadOnlyList<ScreenshotIssue> Issues,
    DateTime                       CheckedAt,
    ScreenshotComparisonResult?    PixelDiff = null
);

public record ScreenshotHealthReport(
    IReadOnlyList<ScreenshotHealthResult> Results,
    DateTime                              GeneratedAt
)
{
    public int PassCount        => Results.Count(r => r.Status == ScreenshotHealthStatus.Pass);
    public int WarningCount     => Results.Count(r => r.Status == ScreenshotHealthStatus.Warning);
    public int ErrorCount       => Results.Count(r => r.Status == ScreenshotHealthStatus.Error);
    public int NotCapturedCount => Results.Count(r => r.Status == ScreenshotHealthStatus.NotCaptured);
}
