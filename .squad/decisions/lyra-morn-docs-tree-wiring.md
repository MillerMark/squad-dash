# Documentation Tree Wiring — Architecture Decisions

**Date:** 2027-01-02  
**Author:** Lyra Morn (WPF & UI Specialist)  
**Context:** Wiring `DocTopicsTreeView` to real `docs/` content

---

## Decision 1: SUMMARY.md Parser with Folder Scan Fallback

**Choice:** Created `DocTopicsLoader.cs` that parses `docs/SUMMARY.md` (GitBook format) as primary source, with automatic fallback to folder scan if SUMMARY.md is missing or unparsable.

**Rationale:**
- `docs/SUMMARY.md` already exists and follows GitBook's nested bullet format (`* [Title](path)`)
- Parsing SUMMARY.md ensures documentation order and structure match author intent
- Folder scan fallback provides graceful degradation — prevents blank tree if SUMMARY.md is deleted or malformed
- Title extraction from markdown's first `# Heading` gives better UX than raw filenames

**Alternatives considered:**
- Folder scan only: Loses author-defined structure and ordering
- SUMMARY.md only (no fallback): Brittle; breaks if file is missing
- Hybrid (always show both): Confusing; duplicates content in tree

---

## Decision 2: TreeViewItem.Tag for File Path Storage

**Choice:** Store full resolved file path in `TreeViewItem.Tag` property. Parent nodes (sections with no file) get `Tag = null`.

**Rationale:**
- Clean separation: `Header` is display string, `Tag` is data payload
- WPF convention: `Tag` is standard property for attaching metadata to UI elements
- Null-check on `Tag` naturally distinguishes clickable documents from section headers
- No need for custom data structures or attached properties

**Alternatives considered:**
- Custom `DocTreeNode` class with `FilePath` property: Overkill; `Tag` is sufficient
- Store relative path and resolve on click: More complex; error-prone
- Attached dependency property: Over-engineered for this use case

---

## Decision 3: AppDomain.BaseDirectory Walk-Up for docs/ Discovery

**Choice:** Start from `AppDomain.CurrentDomain.BaseDirectory`, walk up 5 levels max looking for `docs/` folder or `.git` (repository root marker).

**Rationale:**
- Works in both dev (run from bin/Debug/) and deployed scenarios (SquadDash.exe in root)
- `.git` is a reliable repository root marker
- 5-level depth limit prevents infinite loops on malformed directory structures
- Simple and predictable; no dependency on external services or configuration

**Alternatives considered:**
- `Directory.GetCurrentDirectory()`: Unreliable; can be changed at runtime
- Hardcoded path: Breaks in deployed builds
- IWorkspacePaths service: Overkill; docs are part of the app, not the workspace

---

## Decision 4: MarkdownHtmlBuilder.Build() Image Resolution

**Choice:** Pass `filePath` parameter to `MarkdownHtmlBuilder.Build()`. Existing logic emits `<base href="file:///path/to/dir/">` tag, resolving relative image paths (`images/screenshot.png`) correctly.

**Rationale:**
- `MarkdownHtmlBuilder` already had this feature; no new code needed
- WebBrowser control's `<base>` tag support is native and reliable
- Markdown authors write natural relative paths (e.g., `![](images/logo.png)`)
- Works for nested docs (e.g., `concepts/agents.md` referencing `concepts/images/diagram.png`)

**Alternatives considered:**
- Rewrite image URLs in markdown before rendering: Fragile; regex parsing unreliable
- Copy images to temp folder: Wasteful; breaks on file locks
- Absolute paths in markdown: Bad authoring experience; breaks portability

---

## Decision 5: Auto-Expand First Section, Auto-Select First Document

**Choice:** `LoadTopics()` returns the first child item (first document with a file path). Caller (`PopulateDocumentationTopics`) expands first top-level section and selects returned item, triggering markdown load.

**Rationale:**
- Prevents blank viewer on first open — users see content immediately
- Discoverability: Expanded section shows available topics
- Matches convention in help systems (Windows Help, VS Code docs panel)
- Single return value keeps loader stateless; caller controls UI behavior

**Alternatives considered:**
- Select first top-level item: Shows section header, not document (less useful)
- Don't auto-select: Blank viewer on open; poor UX
- Auto-select last-viewed document: Requires persistence; out of scope

---

## Non-Decisions (Deferred)

- **Refresh on file change:** Not implemented. Docs are static; reload requires toggling panel or restarting app.
- **Search/filter in tree:** Future enhancement. Current tree size (~15 docs) is manageable without search.
- **Bookmarks/favorites:** Out of scope for initial implementation.

---

## Files Modified

- `SquadDash/DocTopicsLoader.cs` — new file (SUMMARY.md parser + folder scan fallback)
- `SquadDash/MainWindow.xaml.cs` — replaced `PopulateDocumentationTopics()`, added `DocTopicsTreeView_SelectedItemChanged()` handler

---

## Verification

✅ Build: 0 errors, 0 warnings  
✅ Commit: `63db33f` — feat: wire DocTopicsTreeView to real docs/ content via SUMMARY.md parser  
✅ Image resolution: `MarkdownHtmlBuilder.Build()` already supported `filePath` parameter with `<base>` tag emission
