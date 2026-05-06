# Clipboard Paste-Image: Storage, Retention, and Cleanup Architecture

**Date:** 2026-05-13  
**Decided by:** Mira Quill (Documentation & Memory Specialist)  
**Status:** Implemented  
**Commit:** 9078bb8  

## Context

The Clipboard Paste-Image feature lets users press **Ctrl+V** in the prompt input box to intercept a clipboard image (screenshot, bitmap) and attach it to an outgoing prompt. The Copilot CLI agent can then analyse the image via its `view` tool by inspecting an `[Attached image: path]` injection in the prompt text.

This ADR captures four architectural decisions made during implementation: how images are persisted, when the retention clock starts, how cleanup is triggered, and how submission state is tracked.

---

## Decision 1 — Store images as files on disk, not base64-embedded in settings/state

**Decision:** Save each pasted image as a PNG file under  
`%LocalAppData%\SquadDash\workspaces\{ws}\pasted-images\`.  
Do **not** encode images as base64 strings in any JSON settings or state file.

**Rationale:**

- **Size.** A single screenshot can be several hundred KB to multiple MB. Embedding even a handful of base64-encoded images in a JSON settings file would balloon the file size and make every read/write of that file expensive.
- **Copilot CLI `view` tool contract.** The CLI agent's `view` tool expects a filesystem path. A base64 blob cannot be passed directly; it would need a temporary file anyway, making base64 storage a two-step process with no benefit.
- **Viewer simplicity.** `PromptAttachmentViewerWindow` can load the PNG with `BitmapImage(Uri(path))` — straightforward WPF image loading. No decode step needed.
- **Expiry is file-system-native.** Pruning expired images is a `File.Delete` call per image directory entry — no JSON parsing or mutation required.

**Alternatives considered:**

- *Base64 in `ApplicationSettingsStore`*: Rejected. Settings file mutation is mutex-protected and synchronous on the UI path; large blobs would introduce visible latency and corrupt the settings file pattern that is designed for small scalar values.
- *SQLite/database*: Rejected as disproportionate infrastructure for a per-workspace scratch folder.

---

## Decision 2 — Start the 14-day retention clock at prompt submission, not paste time

**Decision:** The retention period begins when the user **submits the prompt** containing the image attachment, not when the image is pasted. This is tracked by writing a zero-byte `.submitted` sidecar file alongside the PNG at submission time.

**Rationale:**

- **Relevance window.** An image is useful as long as there is an ongoing conversation about it. A user may paste an image, refine the prompt text for minutes or hours, then submit. Starting the clock at paste time would silently expire images that the user is actively working with.
- **"Sent" semantics.** Expiry policies in messaging systems (e.g., email trash, chat attachments) conventionally measure age from send time, not draft-creation time. This matches user intuition: "the image I sent 14 days ago".
- **Unsent images are handled separately** (30-day creation-time prune) so no image accumulates forever — the two-tier policy covers both cases without a single ambiguous clock.

**Alternatives considered:**

- *Clock starts at paste time*: Rejected. A long-lived draft with an attachment would expire before the user ever submits it.
- *Clock starts at application close*: Rejected. Too implementation-specific and unpredictable; a background prune would have no reliable anchor.
- *No expiry / manual-only*: Rejected. Without automatic cleanup, `pasted-images\` becomes a permanent disk-space sink.

---

## Decision 3 — Cleanup is fire-and-forget on workspace load

**Decision:** On workspace load, `PastedImageStore.PruneAsync()` is called without `await` and without any UI feedback. Prune failures are silently swallowed.

**Rationale:**

- **Startup latency.** Pruning involves directory enumeration and file deletion. Awaiting this on the UI thread (or blocking workspace-load completion) would add perceptible delay before the user can type.
- **Low criticality.** Expired images are stale assets; a failed prune on one load will succeed on the next. There is no correctness risk in leaving an expired PNG on disk for an extra session.
- **No user decision required.** Unlike "clear all images" (an explicit menu action with a confirmation dialog), pruning old files is a maintenance task the user should never have to think about.
- **Consistent with established pattern.** SquadDash already uses fire-and-forget background tasks for other housekeeping (e.g., workspace state saves, loop output archiving).

**Alternatives considered:**

- *Await prune before workspace UI appears*: Rejected — startup latency visible to user.
- *Prune on application shutdown*: Rejected — shutdown path is time-constrained; I/O failures during teardown are harder to handle.
- *Background `Task.Run` with error reporting*: Rejected — adds complexity (error toast, retry logic) for a non-critical housekeeping operation.

---

## Decision 4 — Use a `.submitted` sidecar file rather than renaming or moving the PNG

**Decision:** Submission state is stored as a zero-byte `{guid}.submitted` file alongside the original `{guid}.png`. The PNG filename does not change on submission.

**Rationale:**

- **Path stability.** The PNG path is embedded in the prompt text as `[Attached image: path]` and recorded in the transcript. If the file were renamed on submission, the stored path would become stale and the viewer would fail to open it. A sidecar preserves the original path unchanged.
- **Atomic creation.** Writing a new zero-byte sidecar file is atomic at the filesystem level (create-new semantics). Renaming a file can fail if the file is open (e.g., the viewer window has it loaded), requiring retry logic.
- **Queryability.** `PastedImageStore.PruneAsync()` finds submitted images with a single `Directory.EnumerateFiles("*.submitted")` pass; the matching PNG is the same stem. No name-parsing needed for either PNG or sidecar.
- **Separation of concerns.** The PNG is the image data; the sidecar is metadata about when it entered the retention window. Keeping these in separate files mirrors the JSON-sidecar pattern already used for screenshot manifests in this codebase.

**Alternatives considered:**

- *Rename `{guid}.png` → `{guid}.submitted.png` on submission*: Rejected — breaks all stored transcript links to the original path.
- *Write submission timestamp into a central JSON manifest*: Rejected — introduces a shared mutable file requiring locking; a per-image sidecar is self-contained.
- *Store submission time in the PNG's `DateModified`*: Rejected — modifying file timestamps is fragile, not portable across filesystems, and semantically confusing.

---

## Files Added / Modified

- `SquadDash/PastedImageStore.cs` — new; manages save, prune, and delete for pasted images
- `SquadDash/FollowUpAttachment.cs` — added `ImagePath`, `ImageSubmittedAt` fields
- `SquadDash/MainWindow.xaml.cs` — Ctrl+V intercept, attachment pill, prompt injection, transcript link, prune call, "Clear pasted images…" handler
- `SquadDash/MainWindow.xaml` — "Clear pasted images…" menu item under `_Cleanup`
- `SquadDash/PromptAttachmentViewerWindow.cs` — inline image viewer tab
