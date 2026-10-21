# AvalonEdit FlowDocument benchmark

Run from the repository root in PowerShell (Windows desktop required):

```powershell
$env:DOTNET_MSBUILD_SDK_RESOLVER_CLI_DIR="C:\Program Files\dotnet\sdk\10.0.400-preview.0.26322.102\"
dotnet run --project tools\AvalonEditBenchmark\AvalonEditBenchmark.csproj -c Release
```

Do not set `MSBuildExtensionsPath`. The window opens, runs TextBox then AvalonEdit
for **50 blocks x 200 lines** and **20 blocks x 2,000 lines**, writes `DONE`, and
closes. The console prints the absolute log path:
`tools\AvalonEditBenchmark\results\benchmark-<timestamp>-<pid>.log`.
Logs are ignored by git. Failure writes `FAILED` (or `ABORTED` on early close)
and returns a nonzero exit code.

For one custom workload (both controls):

```powershell
dotnet run --project tools\AvalonEditBenchmark\AvalonEditBenchmark.csproj -c Release -- --blocks 100 --lines 500
```

Both arguments are optional positive integers; custom defaults are 50 blocks and
200 lines. Keep the window visible during measurement.

The harness uses AvalonEdit **6.3.1.120**, the real `RegionHighlightingEngine`, and
SquadDash's bundled TypeScript grammar through a one-way project reference.
It is **not in squad-dash.slnx**, and its sources sit outside `SquadDash`'s default
compile glob. Normal app/solution builds do not build the benchmark. No production
code or behavior is changed.

Results include construction, layout/first-render time, managed memory before/after
forced GC, working/private memory, and **process-lifetime** peak working set.
Between cases, old blocks are cleared and an empty document is rendered before
taking the next memory baseline. Managed delta is **net process heap change**, not
control allocations: WPF can still defer release of prior-case state. Negative
deltas are reported unchanged; use the before/after snapshots for context.
Blocks are fully expanded, with no height cap or inner vertical scrolling.
Rendering waits for layout and two WPF composition callbacks; it measures the
first viewport, not GPU presentation or scrolling through all content.

The TextBox is an approximation of `CopyableCodeBlockBuilder.Build`: same font,
padding, read-only and scroll settings, but omits copy/sticky/context-menu chrome.
Both variants have the same Grid wrapper. This is deliberately one sequential
pass, with JIT/cache/order effects and no warmup, repetitions, or qualification
matrix. Timing excludes deterministic input generation and explicit memory GCs.
