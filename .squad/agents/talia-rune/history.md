# Talia Rune — History & Learnings

## Core Context

**Project:** SquadUI — WPF dashboard for Squad CLI AI agent management  
**Stack:** C# / WPF / .NET 10, NUnit 4.4+, TypeScript SDK  
**Key paths:**
- `SquadDash/` — main application
- `Squad.SDK/` — TypeScript SDK bridge
- `SquadDash.Tests/` — NUnit test suite
- `.squad/decisions.md` — architectural decision log

---

## Learnings

📌 Team update (2026-04-18T17-38): DEL-1 (test coverage for extracted classes) has been reassigned from Talia Rune to Vesper Knox to align with routing. Talia Rune has no outstanding delegated items as of this session. — decided by Coordinator

### Notification Event Hooks Verification (2026-04-28)

**Confirmed three events for push notification hooks** — all exist in `runPrompt.ts` and handled in C# `MainWindow.HandleEvent`:

1. **`"done"`** — Assistant turn complete  
   - Emitted at: `runPrompt.ts` line 582 via `onDone()` callback  
   - When: AI finishes streaming the full response  
   - C# handler: line 1403 in `MainWindow.xaml.cs`

2. **`"loop_stopped"`** — Loop termination  
   - Emitted at: `runPrompt.ts` lines 686 (subprocess close) and 711 (stop when no active loop)  
   - When: Loop subprocess exits cleanly (code 0 or killed) OR stop requested with no active loop  
   - C# handler: line 1359 in `MainWindow.xaml.cs`

3. **`"rc_stopped"`** — Remote connection drop  
   - Emitted at: `runPrompt.ts` lines 809 (no active bridge) and 823 (after shutdown)  
   - When: Remote bridge stops (either already inactive or after successful cleanup)  
   - C# handler: line 1395 in `MainWindow.xaml.cs`

**Key files for the NDJSON event stream protocol:**
- **Event emitter:** `Squad.SDK/runPrompt.ts` — `emit()` function sends NDJSON over stdout
- **Event receiver:** `SquadDash/SquadSdkProcess.cs` — reads stdout, deserializes, fires .NET events
- **Event model:** `SquadDash/SquadSdkEvent.cs` — C# event type definitions with all optional properties
- **Event router:** `SquadDash/MainWindow.xaml.cs` — `HandleEvent` switch statement (line 1229) routes to handlers

No code changes needed — all three events already exist and fire at the correct moments for Arjun Sen's `PushNotificationService` to hook into.
