using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;

namespace SquadDash.Screenshots;

/// <summary>
/// Mode A structural health checker for screenshot definitions.
/// Validates baseline existence, fixture integrity, action registration,
/// anchor completeness, and element-inventory presence — without performing
/// any pixel capture.
/// </summary>
public sealed class ScreenshotHealthChecker
{
    private static readonly JsonSerializerOptions s_jsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    private readonly ScreenshotDefinitionRegistry _definitions;
    private readonly UiActionReplayRegistry       _actions;
    private readonly FixtureLoaderRegistry        _fixtures;
    private readonly ILiveElementLocator          _locator;
    private readonly string                       _screenshotsDirectory;

    public ScreenshotHealthChecker(
        ScreenshotDefinitionRegistry definitions,
        UiActionReplayRegistry       actions,
        FixtureLoaderRegistry        fixtures,
        ILiveElementLocator          locator,
        string                       screenshotsDirectory)
    {
        _definitions          = definitions          ?? throw new ArgumentNullException(nameof(definitions));
        _actions              = actions              ?? throw new ArgumentNullException(nameof(actions));
        _fixtures             = fixtures             ?? throw new ArgumentNullException(nameof(fixtures));
        _locator              = locator              ?? throw new ArgumentNullException(nameof(locator));
        _screenshotsDirectory = screenshotsDirectory ?? throw new ArgumentNullException(nameof(screenshotsDirectory));
    }

    // ── Public API ─────────────────────────────────────────────────────────

    /// <summary>
    /// Runs a structural health check against every registered definition.
    /// </summary>
    public async Task<ScreenshotHealthReport> CheckAllAsync(CancellationToken ct = default)
    {
        var results = new List<ScreenshotHealthResult>();

        foreach (var definition in _definitions.All)
        {
            ct.ThrowIfCancellationRequested();
            results.Add(await CheckOneAsync(definition.Name, ct).ConfigureAwait(false));
        }

        return new ScreenshotHealthReport(results, DateTime.UtcNow);
    }

    /// <summary>
    /// Runs a structural health check for a single named definition.
    /// </summary>
    /// <param name="definitionName">Kebab-case name as stored in definitions.json.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <exception cref="KeyNotFoundException">No definition with that name exists.</exception>
    public Task<ScreenshotHealthResult> CheckOneAsync(string definitionName, CancellationToken ct = default)
    {
        var definition = _definitions.TryGet(definitionName)
            ?? throw new KeyNotFoundException($"No screenshot definition named '{definitionName}'.");

        return Task.FromResult(CheckDefinition(definition));
    }

    // ── Core logic ─────────────────────────────────────────────────────────

    private ScreenshotHealthResult CheckDefinition(ScreenshotDefinition definition)
    {
        var issues = new List<ScreenshotIssue>();

        // 1. Baseline existence
        var baselinePath = Path.Combine(_screenshotsDirectory, $"{definition.Name}.png");
        var hasBaseline  = File.Exists(baselinePath);
        if (!hasBaseline)
        {
            issues.Add(new ScreenshotIssue(
                ScreenshotIssueSeverity.Warning,
                "no-baseline",
                "No baseline PNG found."));
        }

        // 2. Fixture file existence
        if (definition.FixturePath is not null)
        {
            var fixturePath = Path.IsPathRooted(definition.FixturePath)
                ? definition.FixturePath
                : Path.GetFullPath(Path.Combine(_screenshotsDirectory, definition.FixturePath));

            if (!File.Exists(fixturePath))
            {
                issues.Add(new ScreenshotIssue(
                    ScreenshotIssueSeverity.Error,
                    "fixture-missing",
                    $"Fixture file not found: {fixturePath}"));
            }
            else
            {
                // 3. Fixture key recognition
                try
                {
                    using var stream  = File.OpenRead(fixturePath);
                    var       fixture = JsonSerializer.Deserialize<Dictionary<string, object?>>(stream, s_jsonOptions);

                    if (fixture is not null)
                    {
                        var knownKeys = _fixtures.AllKnownKeys;
                        foreach (var key in fixture.Keys)
                        {
                            if (!knownKeys.Contains(key))
                            {
                                issues.Add(new ScreenshotIssue(
                                    ScreenshotIssueSeverity.Warning,
                                    "fixture-key-unknown",
                                    $"Fixture key '{key}' is not recognized by any IFixtureLoader."));
                            }
                        }
                    }
                }
                catch (JsonException)
                {
                    // Malformed fixture is a separate concern — skip key validation
                }
            }
        }

        // 4. Action registration
        if (definition.ReplayActionId is not null &&
            !_actions.TryGet(definition.ReplayActionId, out _))
        {
            issues.Add(new ScreenshotIssue(
                ScreenshotIssueSeverity.Error,
                "action-unregistered",
                $"ReplayActionId '{definition.ReplayActionId}' is not registered."));
        }

        // 5. Anchor needs-name
        CheckAnchorNeedsName(definition.Top,    "Top",    issues);
        CheckAnchorNeedsName(definition.Right,  "Right",  issues);
        CheckAnchorNeedsName(definition.Bottom, "Bottom", issues);
        CheckAnchorNeedsName(definition.Left,   "Left",   issues);

        // 6. Element inventory check (UI thread required for locator calls)
        if (definition.ElementInventory is { Count: > 0 } inventory)
        {
            Application.Current.Dispatcher.Invoke(() =>
            {
                foreach (var item in inventory)
                {
                    var element = _locator.FindByName(item.Name);
                    if (element is null)
                    {
                        var severity = string.Equals(item.Role, "primary-anchor", StringComparison.OrdinalIgnoreCase)
                            ? ScreenshotIssueSeverity.Error
                            : ScreenshotIssueSeverity.Warning;

                        issues.Add(new ScreenshotIssue(
                            severity,
                            "element-not-found",
                            $"Element '{item.Name}' not found in visual tree.",
                            ElementName: item.Name));
                    }
                }
            });
        }

        // Status derivation
        var status = DeriveStatus(issues, hasBaseline);

        return new ScreenshotHealthResult(
            DefinitionName: definition.Name,
            Status:         status,
            Issues:         issues,
            CheckedAt:      DateTime.UtcNow);
    }

    private static void CheckAnchorNeedsName(
        EdgeAnchorRecord    anchor,
        string              edge,
        List<ScreenshotIssue> issues)
    {
        if (anchor.NeedsName)
        {
            issues.Add(new ScreenshotIssue(
                ScreenshotIssueSeverity.Warning,
                "needs-name",
                $"Anchor '{edge}' has no named element.",
                AnchorEdge: edge));
        }
    }

    private static ScreenshotHealthStatus DeriveStatus(
        List<ScreenshotIssue> issues,
        bool                  hasBaseline)
    {
        foreach (var issue in issues)
        {
            if (issue.Severity == ScreenshotIssueSeverity.Error)
                return ScreenshotHealthStatus.Error;
        }

        foreach (var issue in issues)
        {
            if (issue.Severity == ScreenshotIssueSeverity.Warning)
                return ScreenshotHealthStatus.Warning;
        }

        if (!hasBaseline)
            return ScreenshotHealthStatus.NotCaptured;

        return ScreenshotHealthStatus.Pass;
    }
}
