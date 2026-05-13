---
title: AI
nav_order: 7
parent: Options
---

# AI

The AI tab configures AI-assisted shortcuts available while editing documents in SquadDash.

![Screenshot: AI tab showing the Quick Cleanup prompt text box](images/options-ai.png)
> 📸 *Screenshot needed: The Options window on the AI tab — show the "Quick Cleanup prompt" label and the multiline text box with a sample cleanup instruction.*

---

## Quick Cleanup Prompt

**Keyboard shortcut:** `Ctrl+Shift+C`

The Quick Cleanup prompt is a reusable AI instruction that fires whenever you press **Ctrl+Shift+C** with text selected in a markdown source editor.

### How it works

1. Select any text in a markdown editor pane.
2. Press **Ctrl+Shift+C**.
3. SquadDash sends your selection to the AI along with the Quick Cleanup prompt you've configured here.
4. When the AI returns its revised version, the original selection is **replaced in-place** with the cleaned-up text.
5. You can continue working elsewhere in the document while the AI processes — the replacement lands automatically when it's ready.

### What to put here

Write whatever instruction you want the AI to follow when cleaning up your text. Common examples:

- *"Fix spelling and grammar. Keep the original tone and meaning. Do not add or remove content."*
- *"Rewrite as concise bullet points. Use British English."*
- *"Improve clarity and remove filler words."*

The prompt you enter here is sent verbatim as the instruction; the selected text is provided as the content to clean up.

---

## Related

- **[Text Formatting Shortcuts](../features/text-formatting.md)** — Other keyboard shortcuts for markdown editing
