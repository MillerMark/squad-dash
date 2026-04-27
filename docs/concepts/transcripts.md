# Transcripts

The multi-transcript panel system in SquadDash, and how conversations are streamed and persisted.

---

## What is a Transcript?

A **transcript** is the full conversation history between you and an agent. It includes:
- Your prompts
- Agent responses
- Tool calls (with icons and labels)
- Thinking blocks
- Errors and warnings

---

## Opening Transcripts

**Shift-click** any agent card to open its transcript panel.

Multiple transcripts can be open simultaneously — each agent gets its own panel.

---

## Transcript Layout

Each transcript panel shows:

- **Agent name** at the top
- **Conversation history** (scrollable)
- **Current turn** (live streaming)
- **Tool call tracking** with labeled icons

![Screenshot: A transcript panel showing a live agent response](images/transcript-panel-layout.png)
> 📸 *Screenshot needed: A transcript panel mid-conversation — show the agent name header, a few tool call Thinking blocks with icons, and a streamed response.*

---

## Live Streaming

When an agent is active, its transcript updates in real-time:
- **Tool calls** appear as Thinking blocks with icons
- **Responses** stream token-by-token as the agent writes
- **Status updates** show when the agent starts or finishes

---

## Tool Call Icons

Each tool call displays with an icon and label:

| Tool | Icon | Label Source |
|---|---|---|
| `grep` | 🔎 | File path or search pattern |
| `glob` | 🔎 | Glob pattern |
| `view` | 👀 | File path (relative) |
| `edit` | ✏️ | File path (relative) |
| `create` | 📄 | File path (relative) |
| `web_fetch` | 🌍 | URL (scheme stripped) |
| `task` | 🤖 | Task description |
| `skill` | ⚡ | Skill name |
| `store_memory` | 💾 | Memory subject |
| `report_intent` | 🎯 | Intent text |
| `sql` | 🗄️ | SQL description |
| `powershell` | 💻 | Command or description |

Paths are shown **relative** to the workspace root for brevity.

---

## Conversation Persistence

All transcripts are saved to:

```
.squad/
  sessions/
    {agent-name}/
      {session-id}.json
```

When you reopen a workspace, SquadDash restores all previous conversations.

---

## Transcript Navigation

- **Scroll** to navigate history
- **Search** (if implemented) to find specific exchanges
- **Copy** — right-click to copy text from the transcript

---

## Quick-Reply Blocks

Agents can render **quick-reply** buttons in their responses:

```markdown
<quick-reply>
  <option route="lyra-morn">Fix the UI</option>
  <option route="vesper-knox">Add tests</option>
</quick-reply>
```

![Screenshot: Quick-reply buttons in a transcript](images/transcript-quick-reply.png)
> 📸 *Screenshot needed: A transcript response containing quick-reply buttons — show the button options rendered inline.*

Click any option to send that prompt to the routed agent.

---

## Background Agents

Some agents run in **background mode** (no transcript UI):
- Scribe (session logger)
- Monitor agents

Their output is logged but not displayed in the main window.

---

## Transcript Rendering

SquadDash renders markdown in transcripts:
- **Headings** (H1-H6)
- **Bold**, *italic*, `code`
- Code blocks with syntax highlighting
- Tables
- Lists (bulleted and numbered)

Rendering is handled by `MarkdownDocumentRenderer.cs`.

---

## Next

- **[Agents](agents.md)** — How agents work in Squad
- **[Documentation Panel](documentation-panel.md)** — Browse docs inside SquadDash
