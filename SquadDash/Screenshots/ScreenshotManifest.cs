using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace SquadDash.Screenshots;

// ─────────────────────────────────────────────────────────────────────────────
//  Screenshot Metadata Schema — Version 1
//
//  Every PNG capture is paired with a JSON sidecar that carries enough
//  structural information to allow automated re-capture and visual diffing.
//
//  Naming contract
//  ───────────────
//  Paired files share the same stem:
//      {name}-{theme}.png          ← image
//      {name}-{theme}.json         ← this sidecar
//
//  Edge-anchor contract
//  ────────────────────
//  Four anchors — one per edge — each identify the WPF element whose
//  *matching* edge (top→top, right→right, etc.) is the one closest to
//  the corresponding capture-region edge and lies *inside* that region.
//
//  JSON serialisation
//  ──────────────────
//  All records use [property: JsonPropertyName] to emit camelCase keys.
//  System.Text.Json on .NET 7+ resolves the primary constructor automatically;
//  no explicit [JsonConstructor] is required.
//  Use JsonSerializerOptions { WriteIndented = true } when saving.
// ─────────────────────────────────────────────────────────────────────────────

/// <summary>
/// Root metadata record written as a JSON sidecar alongside each PNG capture.
/// </summary>
/// <param name="Version">
///   Schema version — always <c>1</c> for this iteration.
///   Increment when breaking changes are made to the structure.
/// </param>
/// <param name="Name">
///   Human-readable kebab-case identifier, e.g. <c>"agent-card-selected-dark"</c>.
///   Must be lowercase, no spaces.  See <c>ScreenshotNamingHelper.SuggestName</c>.
/// </param>
/// <param name="Description">
///   Brief description of what this screenshot shows.
///   Required — callers should warn (but not throw) when this is empty.
/// </param>
/// <param name="Theme">Active UI theme at capture time: <c>"Dark"</c> or <c>"Light"</c>.</param>
/// <param name="Region">
///   Capture region label.  Use <c>"full"</c> for full-window captures or a
///   short descriptive name for sub-region captures (e.g. <c>"agent-panel"</c>).
/// </param>
/// <param name="CapturedAt">UTC timestamp of the capture.</param>
/// <param name="Bounds">Capture region geometry in logical pixels plus DPI scale factors.</param>
/// <param name="Top">Nearest named element anchored to the <b>top</b> edge of the capture region.</param>
/// <param name="Right">Nearest named element anchored to the <b>right</b> edge of the capture region.</param>
/// <param name="Bottom">Nearest named element anchored to the <b>bottom</b> edge of the capture region.</param>
/// <param name="Left">Nearest named element anchored to the <b>left</b> edge of the capture region.</param>
/// <param name="ReplayActionId">
///   The <see cref="IReplayableUiAction.ActionId"/> of the action that was replayed
///   to reproduce the UI state before this capture, or <c>null</c> if no action was used.
///   Stored so that re-capture automation can replay the same action.
/// </param>
/// <param name="FixturePath">
///   Relative path (from the repository root) to the fixture JSON file loaded
///   before this capture, or <c>null</c> if no fixture was used.
///   Example: <c>"docs/screenshots/fixtures/agent-card-selected.json"</c>.
/// </param>
public record ScreenshotManifest(
    [property: JsonPropertyName("version")]         int              Version,
    [property: JsonPropertyName("name")]            string           Name,
    [property: JsonPropertyName("description")]     string           Description,
    [property: JsonPropertyName("theme")]           string           Theme,
    [property: JsonPropertyName("region")]          string           Region,
    [property: JsonPropertyName("capturedAt")]      DateTime         CapturedAt,
    [property: JsonPropertyName("bounds")]          CaptureBounds    Bounds,
    [property: JsonPropertyName("top")]             EdgeAnchorRecord Top,
    [property: JsonPropertyName("right")]           EdgeAnchorRecord Right,
    [property: JsonPropertyName("bottom")]          EdgeAnchorRecord Bottom,
    [property: JsonPropertyName("left")]            EdgeAnchorRecord Left,
    [property: JsonPropertyName("replayActionId")]  string?          ReplayActionId = null,
    [property: JsonPropertyName("fixturePath")]     string?          FixturePath    = null
);

// ─────────────────────────────────────────────────────────────────────────────
//  Screenshot Definition Schema
//
//  A ScreenshotDefinition is the durable *recipe* for reproducing a screenshot.
//  Definitions are stored in docs/screenshots/definitions.json and managed by
//  ScreenshotDefinitionRegistry.
//
//  Distinction from ScreenshotManifest
//  ─────────────────────────────────────
//  ScreenshotManifest   → record of what WAS captured (written once, alongside PNG)
//  ScreenshotDefinition → recipe for what SHOULD be captured (upserted on every capture)
// ─────────────────────────────────────────────────────────────────────────────

/// <summary>
/// The durable recipe for reproducing a screenshot.  Stored in
/// <c>docs/screenshots/definitions.json</c> and managed by
/// <see cref="ScreenshotDefinitionRegistry"/>.
/// </summary>
/// <remarks>
/// Distinct from <see cref="ScreenshotManifest"/> (the record of what was
/// captured).  A definition describes <em>how</em> to reproduce a capture;
/// a manifest records <em>what</em> was captured at a point in time.
/// </remarks>
/// <param name="Name">
///   Kebab-case, theme-neutral identifier shared with the manifest and the PNG
///   filename stem.  Example: <c>"agent-card-selected-dark"</c>.
/// </param>
/// <param name="Description">
///   Human-readable description of what the screenshot shows.
///   Required — <see cref="ScreenshotDefinitionRegistry.AddOrUpdate"/> will
///   emit a console warning when this is empty, but will not throw.
/// </param>
/// <param name="Theme">
///   Target theme for capture.  One of <c>"Dark"</c>, <c>"Light"</c>, or
///   <c>"Both"</c>.  When <c>"Both"</c>, the command-line runner captures the
///   same region under each theme and writes two PNG + sidecar pairs.
/// </param>
/// <param name="ReplayActionId">
///   The <see cref="IReplayableUiAction.ActionId"/> of the action to replay
///   before capture, or <c>null</c> if no action setup is needed.
/// </param>
/// <param name="FixturePath">
///   Relative path (from the repository root) to the fixture JSON file to load
///   before capture, or <c>null</c> if no fixture is needed.
///   Example: <c>"docs/screenshots/fixtures/agent-card-selected.json"</c>.
/// </param>
/// <param name="Top">Anchor specifying the element whose top edge anchors the capture region's top edge.</param>
/// <param name="Right">Anchor specifying the element whose right edge anchors the capture region's right edge.</param>
/// <param name="Bottom">Anchor specifying the element whose bottom edge anchors the capture region's bottom edge.</param>
/// <param name="Left">Anchor specifying the element whose left edge anchors the capture region's left edge.</param>
/// <param name="Bounds">
///   The capture region geometry recorded at the time the definition was saved.
///   Used as a fallback when element lookup fails during automated re-capture.
/// </param>
public record ScreenshotDefinition(
    [property: JsonPropertyName("name")]            string           Name,
    [property: JsonPropertyName("description")]     string           Description,
    [property: JsonPropertyName("theme")]           string           Theme,
    [property: JsonPropertyName("replayActionId")]  string?          ReplayActionId,
    [property: JsonPropertyName("fixturePath")]     string?          FixturePath,
    [property: JsonPropertyName("top")]             EdgeAnchorRecord Top,
    [property: JsonPropertyName("right")]           EdgeAnchorRecord Right,
    [property: JsonPropertyName("bottom")]          EdgeAnchorRecord Bottom,
    [property: JsonPropertyName("left")]            EdgeAnchorRecord Left,
    [property: JsonPropertyName("bounds")]          CaptureBounds    Bounds
);

/// <summary>
/// The pixel geometry of the capture region together with the DPI scale factors
/// that were active at capture time.
/// </summary>
/// <param name="X">Left edge of the capture region, in logical pixels.</param>
/// <param name="Y">Top edge of the capture region, in logical pixels.</param>
/// <param name="Width">Width of the capture region, in logical pixels.</param>
/// <param name="Height">Height of the capture region, in logical pixels.</param>
/// <param name="DpiX">
///   Horizontal DPI scale factor — physical pixels per logical pixel on the X axis.
///   Derived from <c>VisualTreeHelper.GetDpi(window).DpiScaleX</c>.
/// </param>
/// <param name="DpiY">
///   Vertical DPI scale factor — physical pixels per logical pixel on the Y axis.
///   Derived from <c>VisualTreeHelper.GetDpi(window).DpiScaleY</c>.
/// </param>
public record CaptureBounds(
    [property: JsonPropertyName("x")]      double X,
    [property: JsonPropertyName("y")]      double Y,
    [property: JsonPropertyName("width")]  double Width,
    [property: JsonPropertyName("height")] double Height,
    [property: JsonPropertyName("dpiX")]   double DpiX,
    [property: JsonPropertyName("dpiY")]   double DpiY
);

/// <summary>
/// Describes the WPF element whose <em>matching</em> edge (top→top, right→right, etc.)
/// lies inside the capture region and is closest to one specific edge of that region.
/// </summary>
/// <param name="Edge">
///   Which edge of the capture region this anchor describes.
///   One of: <c>"Top"</c>, <c>"Right"</c>, <c>"Bottom"</c>, <c>"Left"</c>.
/// </param>
/// <param name="ElementNames">
///   All <c>x:Name</c> values of the closest named element(s).  When multiple named
///   elements are within <c>TieTolerance</c> logical pixels of the closest edge they
///   are all listed here.  An empty list means no named element was found; in that
///   case <see cref="NeedsName"/> is <c>true</c>.
/// </param>
/// <param name="NeedsName">
///   <c>true</c> when <see cref="ElementNames"/> is empty, indicating that the
///   element should be given an <c>x:Name</c> in XAML before this capture is used
///   as a stable baseline.
/// </param>
/// <param name="ElementLeft">Left edge of the element's bounding box, in logical pixels.</param>
/// <param name="ElementTop">Top edge of the element's bounding box, in logical pixels.</param>
/// <param name="ElementWidth">Width of the element's bounding box, in logical pixels.</param>
/// <param name="ElementHeight">Height of the element's bounding box, in logical pixels.</param>
/// <param name="DistanceToEdge">
///   Distance in logical pixels from the element's matching edge to the capture region's
///   corresponding edge.  E.g. for a <c>"Top"</c> anchor this is
///   <c>element.Top − captureRegion.Top</c>.  Always ≥ 0 for a valid capture.
/// </param>
public record EdgeAnchorRecord(
    [property: JsonPropertyName("edge")]            string                Edge,
    [property: JsonPropertyName("elementNames")]    IReadOnlyList<string> ElementNames,
    [property: JsonPropertyName("needsName")]       bool                  NeedsName,
    [property: JsonPropertyName("elementLeft")]     double                ElementLeft,
    [property: JsonPropertyName("elementTop")]      double                ElementTop,
    [property: JsonPropertyName("elementWidth")]    double                ElementWidth,
    [property: JsonPropertyName("elementHeight")]   double                ElementHeight,
    [property: JsonPropertyName("distanceToEdge")]  double                DistanceToEdge
);
