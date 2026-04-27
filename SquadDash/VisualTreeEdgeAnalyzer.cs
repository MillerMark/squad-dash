using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Media;

namespace SquadDash;

// ── EdgeAnchor ───────────────────────────────────────────────────────────────

/// <summary>
/// Describes the WPF element whose corresponding edge is closest to (and inside)
/// a capture-region edge.  One instance is produced per edge direction.
/// </summary>
public record EdgeAnchor(
    /// <summary>"Top" | "Right" | "Bottom" | "Left"</summary>
    string     Edge,
    /// <summary>The best-matching element, or <c>null</c> if none qualifies.</summary>
    UIElement? Element,
    /// <summary>
    /// All named elements whose matching edge is within <c>TieTolerance</c> logical pixels
    /// of the closest named element's edge.  Empty when no named element qualifies.
    /// </summary>
    IReadOnlyList<string> UniqueNames,
    /// <summary>Element bounds in the root visual's logical coordinate space.</summary>
    Rect       ElementBounds,
    /// <summary>
    /// Logical-pixel distance from the element's matching edge to the
    /// capture-region edge.  0.0 when no element was found.
    /// </summary>
    double     DistanceToEdge,
    /// <summary><c>true</c> when an element was found but has no unique name.</summary>
    bool       NeedsName
);

// ── VisualTreeEdgeAnalyzer ───────────────────────────────────────────────────

/// <summary>
/// Walks the WPF visual tree rooted at a <see cref="Visual"/> and identifies,
/// for each edge of a rectangular capture region, the <see cref="UIElement"/>
/// whose matching edge lies inside the region and is geometrically closest to
/// that region edge.
/// </summary>
internal static class VisualTreeEdgeAnalyzer
{
    // ── Public API ───────────────────────────────────────────────────────────

    /// <summary>
    /// Analyses <paramref name="captureRegion"/> against the visual tree rooted
    /// at <paramref name="rootVisual"/> and returns four <see cref="EdgeAnchor"/>
    /// instances in fixed order: [0] Top, [1] Right, [2] Bottom, [3] Left.
    /// </summary>
    /// <param name="captureRegion">
    ///   Selection rectangle expressed in <paramref name="rootVisual"/>'s logical
    ///   coordinate space — the same system used by
    ///   <c>element.TransformToAncestor(rootVisual)</c>.
    /// </param>
    /// <param name="rootVisual">
    ///   Visual root to walk; typically the <see cref="System.Windows.Window"/>
    ///   that owns the capture overlay.
    /// </param>
    internal static EdgeAnchor[] Analyze(Rect captureRegion, Visual rootVisual)
    {
        var candidates = new List<(UIElement Element, Rect Bounds)>();
        WalkTree(rootVisual, rootVisual, captureRegion, candidates);

        return
        [
            FindAnchor("Top",    captureRegion, candidates),
            FindAnchor("Right",  captureRegion, candidates),
            FindAnchor("Bottom", captureRegion, candidates),
            FindAnchor("Left",   captureRegion, candidates),
        ];
    }

    // ── Visual tree traversal ────────────────────────────────────────────────

    private static void WalkTree(
        Visual                       node,
        Visual                       root,
        Rect                         captureRegion,
        List<(UIElement, Rect)>      results)
    {
        var childCount = VisualTreeHelper.GetChildrenCount(node);
        for (var i = 0; i < childCount; i++)
        {
            var child = VisualTreeHelper.GetChild(node, i);

            if (child is UIElement el)
            {
                TryAddCandidate(el, root, captureRegion, results);
                WalkTree(el, root, captureRegion, results);
            }
            else if (child is Visual v)
            {
                WalkTree(v, root, captureRegion, results);
            }
        }
    }

    private static void TryAddCandidate(
        UIElement               el,
        Visual                  root,
        Rect                    captureRegion,
        List<(UIElement, Rect)> results)
    {
        // Skip collapsed or zero-size elements
        if (el.Visibility == Visibility.Collapsed) return;
        if (el.RenderSize.Width <= 0 || el.RenderSize.Height <= 0) return;

        try
        {
            var xform  = el.TransformToAncestor(root);
            var origin = xform.Transform(new Point(0, 0));
            var bounds = new Rect(origin, el.RenderSize);

            // Clip bounds against any ancestor that clips its children (e.g. ScrollViewer).
            // Elements that are scrolled out of view report logical bounds that extend
            // into regions they're not visually rendered in — we must exclude them.
            var ancestor = VisualTreeHelper.GetParent(el) as Visual;
            while (ancestor != null && ancestor != root)
            {
                if (ancestor is UIElement ancestorEl && ancestorEl.ClipToBounds)
                {
                    var aXform  = ancestorEl.TransformToAncestor(root);
                    var aOrigin = aXform.Transform(new Point(0, 0));
                    var aRect   = new Rect(aOrigin, ancestorEl.RenderSize);
                    bounds.Intersect(aRect);
                    if (bounds.IsEmpty) return;
                }
                ancestor = VisualTreeHelper.GetParent(ancestor) as Visual;
            }

            // Skip elements whose (clipped) bounding box lies entirely outside the region
            if (bounds.Right  <= captureRegion.Left  ||
                bounds.Left   >= captureRegion.Right  ||
                bounds.Bottom <= captureRegion.Top    ||
                bounds.Top    >= captureRegion.Bottom)
                return;

            results.Add((el, bounds));
        }
        catch
        {
            // TransformToAncestor throws if el is not in root's visual tree.
        }
    }

    // ── Per-edge best-match search ───────────────────────────────────────────

    /// <summary>
    /// For a given edge direction, finds the candidate whose corresponding edge
    /// sits inside the capture region and is closest to that region edge.
    ///
    /// Delegates geometry work to <see cref="FindAnchorFromCandidates"/> and
    /// re-associates the winning <see cref="UIElement"/> for callers.
    /// </summary>
    private static EdgeAnchor FindAnchor(
        string                                 edge,
        Rect                                   captureRegion,
        List<(UIElement Element, Rect Bounds)> candidates)
    {
        // Project to the shape expected by the pure-geometry helper.
        // Compose the name as "{xName}({uniqueName})" when both an x:Name and an
        // IHaveUniqueName DataContext are present (e.g. "AgentCardBorder(orion-vale)").
        // When only one is available, fall back to that one alone.
        var projected = candidates.Select(static c =>
        {
            var fe         = c.Element as FrameworkElement;
            var xName      = fe?.GetValue(FrameworkElement.NameProperty) as string;
            var uniqueName = fe?.DataContext as IHaveUniqueName;
            string? name;
            if (!string.IsNullOrEmpty(xName) && uniqueName != null)
                name = $"{xName}({uniqueName.UniqueName})";
            else if (!string.IsNullOrEmpty(xName))
                name = xName;
            else
                name = uniqueName?.UniqueName;
            return (c.Bounds, name);
        });

        var (names, bounds, dist, needsName, found) =
            FindAnchorFromCandidates(projected, captureRegion, edge);

        // Re-associate the UIElement for callers that need it.
        UIElement? bestEl = null;
        if (found)
        {
            foreach (var c in candidates)
            {
                if (c.Bounds == bounds) { bestEl = c.Element; break; }
            }
        }

        return new EdgeAnchor(
            Edge:           edge,
            Element:        bestEl,
            UniqueNames:    names,
            ElementBounds:  found ? bounds : default,
            DistanceToEdge: found ? dist   : 0.0,
            NeedsName:      needsName);
    }

    /// <summary>
    /// Pure-geometry counterpart of <see cref="FindAnchor"/> — testable without a
    /// WPF visual tree.  Callers supply plain (Rect, name?) pairs; the method runs
    /// the same two-pass named-preferred search and returns a value tuple.
    ///
    /// Qualification per edge:
    ///   Top    — element.Top    in [region.Top,    region.Bottom] + horizontal overlap
    ///   Bottom — element.Bottom in [region.Top,    region.Bottom] + horizontal overlap
    ///   Left   — element.Left   in [region.Left,   region.Right]  + vertical   overlap
    ///   Right  — element.Right  in [region.Left,   region.Right]  + vertical   overlap
    ///
    /// Two-pass strategy: named elements are preferred over unnamed ones.
    /// When multiple named elements tie within <c>TieTolerance</c> logical pixels,
    /// all tied names are returned in <c>Names</c>.
    /// </summary>
    /// <returns>
    /// A tuple whose <c>Found</c> field is <c>false</c> when no element qualifies.
    /// When <c>Found</c> is <c>true</c>: <c>Names</c> contains all tied named-element
    /// names (or is empty for an anonymous fallback), <c>Bounds</c> is the closest
    /// winner's bounding rect, <c>Distance</c> the logical-pixel edge distance, and
    /// <c>NeedsName</c> <c>true</c> when the winner was unnamed.
    /// </returns>
    internal static (IReadOnlyList<string> Names, Rect Bounds, double Distance, bool NeedsName, bool Found)
        FindAnchorFromCandidates(
            IEnumerable<(Rect Bounds, string? Name)> candidates,
            Rect                                      captureRegion,
            string                                    edge)
    {
        // Named elements within TieTolerance of the closest named element are all
        // considered winners and their names are all returned.
        const double TieTolerance = 0.5;

        // Pass 1 — named elements only; pass 2 — any element.
        // Named qualifiers are collected for tie-group evaluation after the loop.
        var    namedQualified = new List<(string Name, Rect Bounds, double Dist)>();
        bool   foundAny       = false; Rect bestAnyBounds = default; double bestAnyDist = double.MaxValue;

        foreach (var (bounds, name) in candidates)
        {
            double elEdgeValue, regionEdgeValue;
            bool   qualifies;

            switch (edge)
            {
                case "Top":
                    qualifies       = bounds.Top    >= captureRegion.Top    &&
                                      bounds.Top    <= captureRegion.Bottom &&
                                      bounds.Right  >  captureRegion.Left   &&
                                      bounds.Left   <  captureRegion.Right;
                    elEdgeValue     = bounds.Top;
                    regionEdgeValue = captureRegion.Top;
                    break;

                case "Bottom":
                    qualifies       = bounds.Bottom >= captureRegion.Top    &&
                                      bounds.Bottom <= captureRegion.Bottom &&
                                      bounds.Right  >  captureRegion.Left   &&
                                      bounds.Left   <  captureRegion.Right;
                    elEdgeValue     = bounds.Bottom;
                    regionEdgeValue = captureRegion.Bottom;
                    break;

                case "Left":
                    qualifies       = bounds.Left   >= captureRegion.Left   &&
                                      bounds.Left   <= captureRegion.Right  &&
                                      bounds.Bottom >  captureRegion.Top    &&
                                      bounds.Top    <  captureRegion.Bottom;
                    elEdgeValue     = bounds.Left;
                    regionEdgeValue = captureRegion.Left;
                    break;

                case "Right":
                    qualifies       = bounds.Right  >= captureRegion.Left   &&
                                      bounds.Right  <= captureRegion.Right  &&
                                      bounds.Bottom >  captureRegion.Top    &&
                                      bounds.Top    <  captureRegion.Bottom;
                    elEdgeValue     = bounds.Right;
                    regionEdgeValue = captureRegion.Right;
                    break;

                default: continue;
            }

            if (!qualifies) continue;

            var dist = Math.Abs(elEdgeValue - regionEdgeValue);

            // Track best-any (pass 2 fallback)
            if (dist < bestAnyDist)
            {
                bestAnyDist   = dist;
                bestAnyBounds = bounds;
                foundAny      = true;
            }

            // Collect all named qualifiers (pass 1 preferred)
            if (!string.IsNullOrEmpty(name))
                namedQualified.Add((name!, bounds, dist));
        }

        if (!foundAny)
            return (Array.Empty<string>(), default, 0.0, false, false);

        if (namedQualified.Count == 0)
            return (Array.Empty<string>(), bestAnyBounds, bestAnyDist, true, true);

        // Find the minimum distance among named candidates, then collect all that
        // fall within TieTolerance of that minimum — those are the "tied" winners.
        var bestNamedDist = namedQualified.Min(static c => c.Dist);
        var tied = namedQualified
            .Where(c => c.Dist <= bestNamedDist + TieTolerance)
            .OrderBy(static c => c.Dist)
            .ToList();

        // If any tied winner has a composed name (from IHaveUniqueName),
        // prefer those over plain x:Name entries to reduce DataTemplate noise.
        var composedTied = tied.Where(static c => c.Name.Contains('(')).ToList();
        if (composedTied.Count > 0)
            tied = composedTied;

        IReadOnlyList<string> names = tied.Select(static c => c.Name).ToList();

        return (
            Names:     names,
            Bounds:    tied[0].Bounds,
            Distance:  bestNamedDist,
            NeedsName: false,
            Found:     true
        );
    }
}
