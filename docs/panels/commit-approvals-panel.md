---
title: Commit Approvals Panel
nav_order: 3
---

# Commit Approvals Panel

The Commit Approvals Panel provides a running list of every git commit an agent has made during the session. Each entry starts in the **Needs Approval** section; you check it off to move it to **Approved**. The panel lets you review agent commits at your own pace, jump directly to the transcript turn where the commit happened, and open the commit on GitHub — all without leaving SquadDash.

---

## Opening the Panel

**View** menu → **Commit Approvals** (toggles visibility). The menu item has a checkmark when the panel is open. Visibility is persisted per workspace.

Close the panel with its **×** button.

---

## How Entries Are Added

SquadDash populates the panel automatically. When an agent's turn ends with a push notification that contains a git commit SHA, SquadDash:

1. Extracts the short SHA from the notification.
2. Constructs a GitHub URL (`<workspaceGitHubUrl>/commit/<sha>`) when a GitHub remote is configured.
3. Derives a short description (≤ ~10 words) from the notification summary, or falls back to the first 8 words of the original prompt.
4. Adds the entry to **Needs Approval**.

Entries persist across SquadDash restarts (stored in `commit-approvals.json` in the workspace state directory). The store retains the **200 most recent** entries.

---

## Panel Layout

```
┌─ Commit Approvals ───────────────────────────────[×]─┐
│                                                       │
│  Needs Approval                                       │
│  ☐  Fix login timeout logic          abc1234          │
│  ☐  Add dark mode preference key     def5678          │
│                                                       │
│  Approved                                    [Clear All]
│  ☑  Update README screenshots        9ab0cd2          │
│                                                       │
└───────────────────────────────────────────────────────┘
```

Each row contains:

| Element | Description |
|---|---|
| **Checkbox** | Unchecked = Needs Approval; checked = Approved. Toggling moves the row between sections. |
| **Description** | Short summary derived from the notification or prompt. Click to jump to the transcript turn. |
| **SHA link** | First 7 characters of the commit SHA, underlined. Click to open the full commit on GitHub. Only shown when a GitHub remote URL is configured. |

---

## Approving a Commit

Check the checkbox next to an entry. The row moves from **Needs Approval** to **Approved** immediately. Uncheck it to move it back.

State is saved to `commit-approvals.json` after every toggle.

---

## Jumping to the Transcript Turn

Click the **description text** of any entry to scroll the coordinator transcript to the prompt that produced that commit. SquadDash switches to the coordinator transcript if another thread is currently selected.

This is useful for reviewing exactly what the agent was asked and what it did before approving.

---

## Clearing Approved Entries

Click **Clear All** in the Approved section to remove all approved entries permanently. Only approved entries are removed; pending entries are unaffected.

---

## Tips

- The SHA link only appears when `workspaceGitHubUrl` is configured. If you see no SHA links, check your workspace GitHub URL setting in Preferences.
- The panel is per-workspace. Switching workspaces replaces the entry list with that workspace's `commit-approvals.json`.
- Entries survive SquadDash restarts — you can review and approve commits from a previous session.
- If the panel is cluttered, approve everything you've already reviewed and then use **Clear All** to reset it.

---

## Related

- **[Loop Panel](loop-panel.md)** — Commit entries accumulate quickly during loop runs; the approvals panel is the review checkpoint
- **[Transcripts](../concepts/transcripts.md)** — Jump-to-turn lands in the coordinator transcript
- **[Configuration](../reference/configuration.md)** — Setting the workspace GitHub URL
