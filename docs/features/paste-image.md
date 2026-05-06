---
title: Paste Image
nav_order: 6
parent: Features
---

# Paste Image

SquadDash lets you paste a clipboard image — a screenshot, bitmap, or any image copied to the clipboard — directly into the prompt input box. The image is saved locally and attached to your prompt so the Copilot CLI agent can analyse it using its `view` tool.

---

## Pasting an Image

1. Copy an image to your clipboard (e.g. **PrintScreen**, **Win+Shift+S**, or right-click → Copy Image in a browser).
2. Click inside the **prompt text box** (or ensure it has focus).
3. Press **Ctrl+V**.

If the clipboard contains an image, SquadDash intercepts the paste and:

- Saves the image as a PNG to the workspace's `pasted-images\` folder.
- Displays a **📷 Image** attachment pill below the prompt text box.

If the clipboard contains only text, **Ctrl+V** behaves normally and pastes the text.

> **Tip:** You can paste an image and then continue typing your prompt — they are submitted together.

---

## The Attachment Strip

Once an image is pasted, a **📷 Image** pill appears in the attachment strip beneath the prompt text box.

- **Click the pill** to open the image in the built-in **Image Viewer** tab, so you can verify what was captured before sending.
- You can paste multiple images; each appears as its own pill.

---

## Submitting a Prompt with an Image

When you press **Send** (or **Enter**), SquadDash automatically injects the image reference into the prompt text:

```
[Attached image: C:\Users\...\pasted-images\{id}.png]
```

This lets the Copilot CLI agent call its `view` tool on the file path and analyse the image in the same turn as your text prompt.

> **Note:** The injection is invisible in the prompt text box — it is appended at dispatch time, not shown in the editor.

---

## Transcript Links

After a prompt with an attached image dispatches, the transcript records the attachment as a clickable **📷 Image** link. Clicking it re-opens the image in the built-in viewer.

If the image has since been pruned (see [Retention & Cleanup](#retention--cleanup) below), the viewer shows:

> *This image has expired and been deleted.*

---

## Retention & Cleanup

Pasted images are stored in:

```
%LocalAppData%\SquadDash\workspaces\{workspace}\pasted-images\
```

SquadDash applies a two-tier automatic retention policy:

| Type | Retention |
|---|---|
| **Submitted** images (attached to a sent prompt) | Deleted **14 days** after submission |
| **Unsent** images (pasted but never submitted) | Deleted **30 days** after the file was created |

Pruning runs automatically in the background each time a workspace loads. No action is required.

---

## Clearing All Pasted Images Manually

To delete all pasted images for the current workspace immediately:

1. Open the **_Cleanup** menu (top menu bar).
2. Click **Clear pasted images…**
3. Confirm the dialog.

SquadDash reports how much disk space was freed.

> **Note:** This deletes all pasted images for the current workspace, including any that have not yet been sent. Existing transcript links to those images will show the "expired" message if clicked.

---

## Related

- **[Entering Prompts](entering-prompts.md)** — The prompt text box and how prompts are sent
- **[Keyboard Shortcuts](../reference/keyboard-shortcuts.md)** — Ctrl+V and other prompt shortcuts
- **[Transcripts](../concepts/transcripts.md)** — How attachments appear in the conversation history
