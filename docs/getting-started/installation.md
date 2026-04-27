# Installation

How to clone and build SquadUI from source.

---

## Prerequisites

Make sure you have:
- **Windows** (10 or 11)
- **.NET 10 SDK** — [Download from Microsoft](https://dotnet.microsoft.com/download/dotnet/10.0)
- **Node.js LTS** — [Download from nodejs.org](https://nodejs.org/)

Verify Node.js is installed:

```bash
node --version
npm --version
npx --version
```

All three must be accessible on your `PATH`.

---

## Clone the Repository

```bash
git clone <repo-url>
cd SquadUI
```

---

## Install Root Dependencies

The root `package.json` contains Squad CLI dev tooling (used for development, not required for running the app):

```bash
npm install
```

---

## Build

Build the entire solution with:

```bash
dotnet build SquadUI.slnx
```

Or open `SquadUI.slnx` in **Visual Studio 2022+** and press **F5**.

---

## Project Structure

| Folder | Description |
|---|---|
| `SquadDash/` | Main WPF application (`net10.0-windows`). Entry point: `App.xaml`. |
| `SquadDashLauncher/` | Thin launcher that manages hot-swappable runtime slots. Handles deploy-and-restart during Debug builds. |
| `Squad.SDK/` | TypeScript SDK project for Node.js-side Squad interaction. |
| `SquadDash.Tests/` | NUnit test suite. Run with `dotnet test`. |

---

## Run

Launch the app:

```bash
dotnet run --project SquadDash
```

Or from Visual Studio: **F5**.

On first launch, you'll be prompted to select a workspace folder.

---

## Run Tests

```bash
dotnet test SquadUI.slnx
```

Tests use NUnit 4.4+. All tests should pass on a clean build.

---

## Next

- **[First Run](first-run.md)** — What to expect when you launch SquadUI for the first time
