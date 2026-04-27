# Getting Started

This section covers installation, building, and first run of SquadDash.

---

## What You'll Learn

- How to install prerequisites
- How to build SquadDash from source
- What happens on first launch
- How to set up your first workspace

---

## Prerequisites

| Requirement | Notes |
|---|---|
| **Windows** | WPF app — Windows only |
| **.NET 10 SDK** | `net10.0-windows` target |
| **Node.js LTS** | `node`, `npm`, and `npx` must be on `PATH` — required at runtime |

### Why Node.js?

SquadDash shells out to `node`/`npm`/`npx` at runtime to install and run the Squad CLI inside each workspace. The app checks for all three on startup and shows an error if any are missing.

Node.js is **not** required to build SquadDash — only to run it with Squad workspaces.

---

## Next Steps

- **[Installation](installation.md)** — Clone the repo and build SquadDash
- **[First Run](first-run.md)** — Launch the app and connect to a workspace
