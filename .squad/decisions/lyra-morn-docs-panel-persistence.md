# Documentation Panel State Persistence

**Date:** 2027-01-02  
**Decided by:** Lyra Morn  
**Status:** Implemented  

## Context

The documentation panel in SquadUI (introduced previously) needed to preserve its state across application restarts to maintain user context and reduce friction when returning to the application. Users expect their UI preferences to persist, including whether the panel is open, which nodes are expanded, and which topic they were viewing.

## Decision

Use the existing `ApplicationSettingsStore` / `ApplicationSettingsSnapshot` / `JsonFileStorage` pattern to persist three pieces of documentation panel state to `%LOCALAPPDATA%\SquadDash\settings.json`:

1. **DocsPanelOpen** (`bool?`): Whether the panel is open
   - `null` or `true` = open (the default state)
   - `false` = explicitly closed by the user
   - Only write `false` when user explicitly closes the panel

2. **DocsExpandedNodes** (`string[]`): Keys of expanded TreeViewItem nodes
   - Each key is the item's Tag (file path) if present, otherwise Header string
   - `null` means "not saved yet" → expand all nodes (default behavior)
   - Case-insensitive storage

3. **DocsSelectedTopic** (`string`): Tag (file path) of the selected topic
   - `null` means use first item (existing default behavior)

## Implementation Details

### Save Timing
- **On explicit close** (`SetDocumentationMode(false)` with `persistChange: true`): Save `DocsPanelOpen = false` + current expansion + current topic
- **On explicit open** (`SetDocumentationMode(true)` with `persistChange: true`): Save `DocsPanelOpen = null`, leave expansion/topic unchanged
- **On app close** (if panel is open): Save current expansion + topic + `DocsPanelOpen = null` via async shutdown task

### Restore Timing
- During startup in `RestoreDocsPanelState()` (called from `RestoreUtilityWindowVisibility()`)
- Only restore if `DocsPanelOpen != false` (i.e., null or true)
- Use `SetDocumentationMode(true, persistChange: false)` to restore without writing state

### Default Behaviors
- **Default panel state:** Open (when `DocsPanelOpen` is absent/null)
- **Default expansion:** All nodes expanded (when `DocsExpandedNodes` is null)
- **Default selection:** First item from `DocTopicsLoader` (when `DocsSelectedTopic` is null)

## Rationale

1. **Consistency:** Reuses proven pattern used by TasksWindow, TraceWindow, window placement, agent colors, etc.
2. **Simplicity:** Single JSON file, atomic writes, mutex-protected
3. **Backward compatible:** New properties are nullable; old settings files work fine
4. **Default-open semantics:** `null` = open aligns with "discoverability" UX principle (new users see docs by default)
5. **Graceful degradation:** Missing state falls back to sensible defaults (expand all, select first)

## Alternatives Considered

- **Per-workspace state:** Rejected because documentation is application-level, not workspace-specific
- **Separate JSON file:** Rejected to avoid proliferation of settings files
- **Only persist open/closed:** Rejected because users expect expansion/selection to persist too
- **Default-closed semantics:** Rejected because documentation discoverability is important for new users

## Files Modified

- `SquadDash/ApplicationSettingsStore.cs`: Added properties, `SaveDocsPanelClosed()`, `SaveDocsPanelOpen()` methods
- `SquadDash/MainWindow.xaml.cs`: Added persistence logic, restore logic, tree helper methods

## Impact

- Users' documentation panel state now persists across restarts
- Zero breaking changes (all new properties are nullable with sensible defaults)
- Settings file grows by ~3 fields, negligible size impact
- Pattern is now established for any future panel/UI state persistence needs
