namespace SquadDash;

/// <summary>
/// Explicit list of ResourceDictionary keys whose colors participate in per-workspace hue rotation.
/// Only keys present in this set are shifted; all other resources (semantic status colors,
/// system-color overrides, search highlights) are left untouched regardless of their hue.
/// </summary>
/// <remarks>
/// Excluded by design:
/// <list type="bullet">
///   <item><c>PriorityHigh</c> — amber = high-priority / warning status; shifting it to blue would
///         collide with <c>PriorityMid</c> (already blue).</item>
///   <item><c>ScreenshotAnchorUnnamed</c> — amber = semantic "unnamed anchor" annotation.</item>
///   <item><c>SearchHighlight</c>, <c>SearchHighlightCurrent</c>, <c>SearchHighlightText</c>
///         — search highlight chrome must stay visually distinct from the tinted palette.</item>
///   <item><c>{x:Static SystemColors.*}</c> keys — system resource lookups; not patchable
///         via simple string key access.</item>
/// </list>
/// When adding a new theme token: add it here only if it is a warm-neutral surface, border,
/// or text color that should shift with the workspace tint — not if it carries semantic meaning.
/// </remarks>
internal static class TintKeys
{
    /// <summary>
    /// Keys for the "active highlight" accent colors (queue-tab border, active-panel chrome).
    /// These are <em>not</em> in <see cref="All"/> — they are rotated separately in
    /// <c>ApplyTintStop</c> using a complementary offset (180°) so the accent always
    /// contrasts with the current palette rather than blending into it.
    /// </summary>
    internal static readonly IReadOnlySet<string> ActiveAccent = new HashSet<string>(StringComparer.Ordinal)
    {
        "QueueTabActiveBorder",
        "ActivePanelSurface",
        "ActivePanelBorder",
        "ActivePanelTitle",
        "ActivePanelSubtitle",
    };

    internal static readonly IReadOnlySet<string> All = new HashSet<string>(StringComparer.Ordinal)
    {
        // Surfaces
        "AppSurface",
        "ChromeSurface",
        "PanelSurface",
        "RosterPanelSurface",
        "SidebarPanelSurface",
        "SidebarPanelBorder",
        "WorkspaceSurface",
        "CardSurface",               // dark only
        "InboxBodySurface",
        "HoverSurface",              // light only
        "TranscriptLinkHoverSurface", // light only
        "ApprovalSelectedSurface",   // light only — has alpha channel; engine must preserve alpha byte
        "TranscriptSurface",
        "InputSurface",
        "FollowUpSurface",
        "QueueCardSurface",
        "AlertSurface",
        "RoleBadgeSurface",
        "ThreadBadgeSurface",
        "PopupSurface",              // dark only
        "ToastSurface",
        "ChipSurface",
        "ChipHoverSurface",
        "CodeSurface",
        "QuoteSurface",
        "TrackSurface",
        "TableHeaderSurface",
        "IntelliSenseHoverSurface",
        "TranscriptActionSurface",
        "TranscriptActionHoverSurface", // light only

        // Window chrome
        "WindowBorder",
        "CaptionButtonHover",

        // Borders / lines
        "PanelBorder",
        "RosterPanelBorder",
        "WorkspaceBorder",
        "LineColor",
        "TranscriptBorder",
        "InputBorder",
        "SubtleBorder",
        "CheckBoxBorder",
        "AlertBorder",
        "BadgeBorder",
        "HireAgentImageBorder",
        "ThreadBadgeBorder",
        "ToastBorder",
        "ChipBorder",
        "QuoteBorder",
        "TableRule",
        "TranscriptActionBorder",

        // Text
        "ImportantText",
        "LabelText",
        "BodyText",
        "CodeText",
        "SubtleText",
        "AgentRoleText",
        "AgentStatusText",
        "AgentDetailText",
        "PanelTitle",
        "AlertBodyText",
        "ThreadBadgeText",
        "ToastText",
        "ChipText",
        "QueueTabInactiveText",
        "AgentTaskStartText",
        "SelfHandledText",
        "SystemInfoText",
        "ThinkingText",
        "ThinkingMetaText",
        "TranscriptActionText",

        // Scrollbar
        "ScrollBarTrackBrush",
        "ScrollBarThumbBrush",
        "ScrollBarThumbHoverBrush",
        "ScrollBarThumbPressedBrush",

        // Editor / selection (warm-tone variants only; cool-tone selection colors excluded)
        "IntelliSenseSelectedText",  // dark only
        "DocEditorSelectionTextBrush", // dark only
    };
}
