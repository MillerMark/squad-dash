# Creating Guided Tours

**Guided tours** are step-by-step interactive walkthroughs that highlight UI elements and guide users through SquadDash features. Each step shows a callout pointing at a specific element, with a title and markdown description. The user advances through steps manually or via automatic triggers.

Tours are accessible to users from the **Help** menu → **Start Guided Tour** / **More Guided Tours**.

---

## Where Tours Are Stored

Tours have one editable source of truth: `SquadDash/Assets/guided-tours.json`. The file is tracked by Git and embedded into the application during the build.

When running from a source workspace, the developer editor loads and saves this tracked file directly. Installed builds load the embedded copy.

---

## Tour JSON Schema

```json
[
  {
    "id": "intro-ui",
    "name": "Introduction to the UI",
    "description": "A quick walkthrough of the main interface panels.",
    "steps": [
      {
        "title": "Welcome to the Guided Tour",
        "markdownText": "Meet SquadDash...\r\nStart by clicking the **Next** button below.",
        "targetControlId": "",
        "calloutPlacement": "Auto",
        "preAction": "None",
        "commandBefore": "",
        "commandAfter": "",
        "advanceTrigger": "",
        "targetOffsetX": 0.5,
        "targetOffsetY": 0.5
      }
    ]
  }
]
```

### Tour fields

| Field | Type | Description |
|---|---|---|
| `id` | string | Unique identifier for this tour (used internally) |
| `name` | string | Display name shown in the tour selector |
| `description` | string | Short description shown in the tour selector |
| `steps` | array | Ordered list of tour steps |

### Step fields

| Field | Type | Description |
|---|---|---|
| `title` | string | Step heading shown in the callout |
| `markdownText` | string | Body content (supports Markdown) |
| `targetControlId` | string | x:Name of the element to point at; empty = no pointer |
| `calloutPlacement` | string | `Auto`, `North`, `South`, `East`, `West` |
| `preAction` | string | `None` or `SaveLayout` (saves panel layout before tour starts) |
| `commandBefore` | string | Command to run before the step is shown (see [Commands](#tour-commands)) |
| `commandAfter` | string | Command to run after the user leaves this step |
| `advanceTrigger` | string | How the user moves to the next step (see [Advance Triggers](#advance-triggers)); empty = Next button |
| `targetOffsetX` | float | Horizontal anchor point within the target element (0 = left, 1 = right, 0.5 = centre) |
| `targetOffsetY` | float | Vertical anchor point within the target element (0 = top, 1 = bottom, 0.5 = centre) |

---

## Creating a Tour

### 1. Open the tour editor

**Developer** menu → **Edit Guided Tours** to edit existing tours, or **New Guided Tour…** to create one from scratch.

### 2. Add steps

For each step:
- Set `title` and `markdownText` (Markdown is supported).
- Set `targetControlId` to the element name you want to highlight (use **UI Reveal** — see below — to find names).
- Choose `calloutPlacement` — `Auto` usually works well.
- Optionally add a `commandBefore` or `commandAfter`.
- Optionally set an `advanceTrigger` for automatic advancement.

### 3. Find element names with UI Reveal

**Developer** → **UI Reveal** (or press `F12`) overlays every element with its x:Name. Click or hover over the element you want to target and copy its name into `targetControlId`.

Common element names from the built-in tour:

| Element Name | What it is |
|---|---|
| `PromptTextBox` | The main prompt input box |
| `RunButton` | The Send/Queue button |
| `MainTranscriptBorder` | The coordinator transcript panel |
| `ActiveAgentItemsControl` | The active agents strip |
| `InactiveAgentsScrollViewer` | The inactive agents list |
| `QueueTabStrip[1]` | The first prompt queue tab |
| `HelpMenuItem` | The Help menu item |
| `LoopPanelBorder` | The Loop panel |
| `QueuePlayPauseButton` | The queue pause button |
| `CodeHealthPanelBorder` | The Code Health panel |

### 4. Preview a step

With a tour active, use **Developer** → **Preview Current Tour Step** to re-render the callout at the current step index without advancing. Useful when adjusting `calloutPlacement` or offset values.

### 5. Save

Tours are saved automatically to `SquadDash/Assets/guided-tours.json` after edits settle. Press `Ctrl+S` to save immediately.

---

## Tour Commands

Commands are run synchronously (or asynchronously, where noted) before or after a step is displayed. The format is `CommandName` or `CommandName|arg1|arg2`.

### `TypeIntoPrompt|text|AgentName`

Animates text being typed character-by-character into the prompt box. Simulates a user composing a prompt.

```
commandBefore: "TypeIntoPrompt|What is your model?|Sim"
```

The `AgentName` parameter is optional context (accepted but currently ignored by the animation).

### `Add Dummy Queue Items`

Adds three placeholder items (`[Tour Demo Item 1]`, `[Tour Demo Item 2]`, `[Tour Demo Item 3]`) to the prompt queue. Use this to demonstrate the queue tab strip without sending real prompts.

```
commandBefore: "Add Dummy Queue Items"
```

### `Remove Dummy Queue Items`

Removes all placeholder queue items added by `Add Dummy Queue Items` and stops any active `TypeIntoPrompt` animation. Always pair with `Add Dummy Queue Items` in a `commandAfter`.

```
commandAfter: "Remove Dummy Queue Items"
```

### `InjectTranscriptText|markdown|AgentName`

Injects a completed (non-streaming) AI response into the specified agent's transcript thread. If `AgentName` is omitted, the response goes to the coordinator thread.

```
commandBefore: "InjectTranscriptText|Here is a completed response.|Aria"
```

### `InjectTranscriptTextWithReplies|text|Btn1|Btn2|…`

*(Coordinator thread only)* Injects a response with quick-reply buttons. The tour can then use a `QuickReplySelected` advance trigger to advance when the user clicks a specific button.

```
commandBefore: "InjectTranscriptTextWithReplies|Here's what a response looks like!|Got it, show me more|I want to explore on my own"
```

### `InjectTranscriptTurn|user prompt|agent response|AgentName`

Injects a full conversation turn: first shows a user prompt bubble, then streams the agent response word-by-word into the named agent's thread (or the coordinator thread if `AgentName` is omitted). Creates a new demo agent thread if the named agent doesn't exist.

```
commandBefore: "InjectTranscriptTurn|How do I pause the queue?|You can pause the queue using the pause button next to the queue strip.|Aria"
```

Use `\n` in the text fields to insert literal newlines.

### `InjectAgentResponse|response|AgentName`

Streams a response-only turn (no user prompt bubble) into a named agent's thread. Useful for simulating parallel agent activity.

```
commandBefore: "InjectAgentResponse|Analysing codebase...\nDone.|Rex"
```

---

## Advance Triggers

By default, the user clicks **Next** to advance. Set `advanceTrigger` to advance automatically on a specific application event.

### *(empty)* — Manual Next button

Leave `advanceTrigger` empty. The callout shows a **Next** button.

### `MenuOpened|MenuName`

Advances when a menu with the given name is opened. Use this to require the user to actually open a menu before continuing.

```json
"advanceTrigger": "MenuOpened|Help"
```

### `QuickReplySelected|ButtonText`

Advances when a quick-reply button matching `ButtonText` is clicked. Pair with `InjectTranscriptTextWithReplies` in `commandBefore`.

```json
"commandBefore": "InjectTranscriptTextWithReplies|What would you like to do?|Show me the queue|Skip",
"advanceTrigger": "QuickReplySelected|Show me the queue"
```

---

## Tips and Notes

- **The first tour** automatically gets a final step pointing at the Help menu: *"You can start guided tours from inside the Help menu."* This step is appended at runtime and doesn't need to be in the JSON.

- **Use UI Reveal (`F12`)** rather than guessing element names. Incorrect `targetControlId` values result in a floating callout with no pointer.

- **`calloutPlacement: "Auto"`** calculates the best side based on where the target element sits on screen. Override it (`North`, `South`, `East`, `West`) when `Auto` chooses the wrong side.

- **`targetOffsetX` / `targetOffsetY`** fine-tune where the callout pointer attaches to the element. `0.5, 0.5` centres the anchor; adjust if the default position is obscured by other content.

- **Tours survive layout changes.** If a `commandBefore` uses `preAction: "SaveLayout"`, the layout is snapshotted before the tour starts and restored when it ends.

- **Demo agent threads** created by `InjectTranscriptTurn` and `InjectAgentResponse` are automatically cleaned up when the tour ends.
