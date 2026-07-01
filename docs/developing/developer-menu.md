# Developer Menu

The **Developer** menu appears in the SquadDash menu bar when the current workspace contains a `squad-dash.slnx` file (i.e., when SquadDash is running against its own source repository). It is hidden in all other workspaces.

The menu provides access to diagnostics, simulation tools, theme explorers, hint authoring, and guided tour editing.

---

## Menu Items

### UI Reveal (`F12`)

Toggles an overlay that draws layout boundaries around every visible UI element. Hover over any element to see its name, type, and dimensions.

**Use this to find `targetControlId` values** when authoring guided tour steps — the element's x:Name shown in the overlay is the value to put in the step's `targetControlId` field.

### Theme Reveal (`Ctrl+F11`)

Toggles an overlay that annotates UI elements with the theme resource keys used to colour them. Useful when building or debugging colour themes.

### Show Trace Window

Opens the floating **Trace Window** — a live stream of internal application events. See [Using the Trace Log](trace-log.md) for details.

### Squad Bridge Diagnostics

Checkable. When enabled, logs verbose diagnostics about the Squad bridge connection to the trace log. Useful when debugging communication issues between SquadDash and the Squad process.

---

### Simulation

A submenu for simulating error and startup states without actually triggering them in production:

**Bridge Disposed** — simulates the Squad bridge being unexpectedly disposed.

**Startup Issue Preview** — previews startup error screens:
- *None* — clears any active simulation
- *Missing Node.js tooling*
- *Squad not installed*
- *Partial Squad install*

**Runtime Failure** — previews runtime failure screens:
- *None* — clears any active simulation
- *Copilot auth required*
- *Bundled SDK repair*
- *Build temp files*
- *Generic runtime failure*

---

### Docking Tools

| Item | Shortcut | Description |
|---|---|---|
| Record Docking Test Case | `F10` | Captures a docking interaction as a test case |
| Docking Test Playback | — | Replays a previously recorded docking test |
| Capture Docking Snapshot | `Ctrl+Alt+Shift+D` | Saves a snapshot of the current docking layout |
| Folders → Docking Test Cases | — | Opens the folder containing recorded test cases |

---

### Theme Explorer…

Opens a floating window listing all theme colour resources with live previews. Useful when designing or tuning colour themes.

### Font Size Explorer…

Opens a floating window showing all font size resources used in the UI.

---

### Hint Authoring (`F6`)

Toggles **Hint Authoring Mode** — a mode for creating and editing contextual hints (tooltip-style callouts that appear as the user interacts with the UI). Also toggled globally with `F6` when the Developer menu is visible.

### Trigger Idle Hint Cycle

Manually fires the idle hint cycle as if the user had been inactive for the configured idle duration. Useful for testing hint timing without waiting.

---

### Guided Tours

| Item | Description |
|---|---|
| Edit Guided Tours | Opens the tour editor to modify existing tours in the current workspace |
| New Guided Tour… | Creates a new tour and opens it in the editor |
| Preview Current Tour Step | Re-renders the callout for the currently active tour step (useful while editing) |

See [Creating Guided Tours](guided-tours.md) for the full authoring workflow.
