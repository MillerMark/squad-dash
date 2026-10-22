using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using ICSharpCode.AvalonEdit;
using SquadDash;

namespace AvalonEditBenchmark;

internal static class Program
{
    private const string RunCommand =
        @"dotnet run --project tools\AvalonEditBenchmark\AvalonEditBenchmark.csproj -c Release";
    private const double MiB = 1024 * 1024;

    [STAThread]
    private static int Main(string[] args)
    {
        CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
        var results = Path.Combine(FindProjectDirectory(), "results");
        Directory.CreateDirectory(results);
        var logPath = Path.Combine(results, $"benchmark-{DateTime.Now:yyyyMMdd-HHmmss-fff}-{Environment.ProcessId}.log");
        using var log = new StreamWriter(logPath) { AutoFlush = true };
        log.WriteLine($"Run from repo root: {RunCommand}{(args.Length == 0 ? "" : " -- " + string.Join(" ", args))}");
        log.WriteLine($"Started: {DateTimeOffset.Now:O}");
        log.WriteLine($"Runtime: {Environment.Version}; OS: {Environment.OSVersion}; 64-bit: {Environment.Is64BitProcess}");
        log.WriteLine("AvalonEdit 6.3.1.120; SquadDash RegionHighlightingEngine; bundled TypeScript grammar, light palette.");
        log.WriteLine("TextBox approximation: CopyableCodeBlockBuilder text/font/padding/scroll settings; no copy button, sticky behavior, or context menu. Same Grid wrapper for both controls.");
        log.WriteLine("Both controls: no height cap, no wrapping, no inner vertical scrolling; outer FlowDocumentScrollViewer scrolls.");
        log.WriteLine("Build includes document/control construction, text assignment and per-editor highlighting definition. Input generation is excluded.");
        log.WriteLine("Layout/render includes attachment, Measure/Arrange/UpdateLayout, two distinct CompositionTarget.Rendering callbacks and a ContextIdle dispatcher fence.");
        log.WriteLine("This measures first-viewport WPF rendering, NOT GPU presentation or scrolling every line into view.");
        log.WriteLine("Single process, TextBox then AvalonEdit for each workload; no warmup or repetitions. JIT/cache/order effects are included; not a cold/warm qualification.");
        log.WriteLine("Memory MB = MiB. Managed before/after use GC.GetTotalMemory(true), outside timing; input strings remain live for both cases.");
        log.WriteLine("Between cases the old blocks are cleared and an empty document is rendered before taking the next memory baseline.");
        log.WriteLine("Managed delta is the NET PROCESS heap change, not control allocations. WPF can defer releasing prior-case state despite cleanup; negative deltas are reported unchanged.");
        log.WriteLine("Working/private are process snapshots before forced after-GC; peak working set is process-lifetime, NOT per-case.");

        var workloads = ParseWorkloads(args);
        if (workloads is null)
        {
            const string error = "Expected positive integers: [--blocks N] [--lines N]. No arguments runs 50x200 and 20x2000.";
            log.WriteLine($"FAILED: {error}");
            Console.Error.WriteLine(error);
            Console.WriteLine(logPath);
            return 2;
        }

        var app = new Application();
        var viewer = new FlowDocumentScrollViewer
        {
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            IsToolBarVisible = false,
        };
        var window = new Window
        {
            Title = "AvalonEdit benchmark - running automatically",
            Width = 1100,
            Height = 800,
            Content = viewer,
            Background = Brushes.White,
        };
        var completed = false;
        var failed = false;
        app.DispatcherUnhandledException += (_, e) =>
        {
            failed = true;
            log.WriteLine($"FAILED: {e.Exception}");
            Console.Error.WriteLine(e.Exception);
            e.Handled = true;
            app.Shutdown(1);
        };
        app.Exit += (_, e) =>
        {
            if (!completed && !failed)
            {
                log.WriteLine("ABORTED: window closed before completion.");
                e.ApplicationExitCode = 1;
            }
            Console.WriteLine(logPath);
        };
        window.ContentRendered += Run;
        return app.Run(window);

        async void Run(object? sender, EventArgs e)
        {
            window.ContentRendered -= Run;
            log.WriteLine($"Viewport: {viewer.ActualWidth:F0} x {viewer.ActualHeight:F0} DIPs; DPI scale: {VisualTreeHelper.GetDpi(viewer).DpiScaleX:F2}; render tier: {RenderCapability.Tier >> 16}");
            log.WriteLine("Control | Blocks | Lines/block | Total lines | Build ms | Layout/render ms | Total ms | Managed before MB | Managed after MB | Managed delta MB | Working MB | Private MB | Lifetime peak working MB");
            foreach (var workload in workloads)
            {
                var samples = Enumerable.Range(0, workload.Blocks)
                    .Select(block => GenerateCode(block, workload.Lines)).ToArray();
                foreach (var avalon in new[] { false, true })
                {
                    window.Title = $"{(avalon ? "AvalonEdit" : "TextBox")} - {workload.Blocks} blocks x {workload.Lines} lines";
                    await Measure(viewer, samples, workload, avalon, log);
                    // WPF can retain the old page visual until a replacement document has rendered.
                    viewer.Document.Blocks.Clear();
                    viewer.Document = new FlowDocument();
                    viewer.UpdateLayout();
                    // Finalizers/weak-event cleanup can enqueue dispatcher work after a collection.
                    GC.Collect();
                    GC.WaitForPendingFinalizers();
                    await WaitForRender(viewer);
                    await viewer.Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ContextIdle);
                }
                GC.KeepAlive(samples);
            }
            log.WriteLine($"DONE: {DateTimeOffset.Now:O}; all {workloads.Length * 2} cases completed.");
            completed = true;
            app.Shutdown(0);
        }
    }

    private static async Task Measure(
        FlowDocumentScrollViewer viewer, string[] samples, Workload workload, bool avalon, StreamWriter log)
    {
        var managedBefore = GC.GetTotalMemory(true);
        var timer = Stopwatch.StartNew();
        var document = new FlowDocument
        {
            PagePadding = new Thickness(10),
            ColumnWidth = double.PositiveInfinity,
            Background = Brushes.White,
        };
        RegionEditorPalette? palette = avalon
            ? new RegionEditorPalette(Brushes.WhiteSmoke, Brushes.Black,
                RegionHighlightingEngine.Default.CreatePaletteColors(isDark: false))
            : null;
        foreach (var sample in samples)
        {
            Control control;
            if (avalon)
            {
                var editor = new TextEditor
                {
                    IsReadOnly = true,
                    WordWrap = false,
                    ShowLineNumbers = false,
                    HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
                    VerticalScrollBarVisibility = ScrollBarVisibility.Disabled,
                    SyntaxHighlighting = RegionHighlightingEngine.Default.CreateDefinition("TypeScript", palette!),
                    Text = sample,
                };
                editor.Options.EnableHyperlinks = false;
                editor.Options.EnableEmailHyperlinks = false;
                control = editor;
            }
            else
            {
                control = new TextBox
                {
                    IsReadOnly = true,
                    AcceptsReturn = true,
                    TextWrapping = TextWrapping.NoWrap,
                    HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
                    VerticalScrollBarVisibility = ScrollBarVisibility.Disabled,
                    Text = sample,
                };
            }
            control.FontFamily = new FontFamily("Consolas");
            control.FontSize = 14 * 0.9;
            control.BorderThickness = new Thickness(0);
            control.Padding = new Thickness(10, 30, 10, 8);
            control.Background = Brushes.WhiteSmoke;
            control.Foreground = Brushes.Black;
            InputMethod.SetIsInputMethodEnabled(control, false);
            InputMethod.SetPreferredImeState(control, InputMethodState.Off);
            var grid = new Grid();
            grid.Children.Add(control);
            document.Blocks.Add(new BlockUIContainer(grid) { Margin = new Thickness(0, 2, 0, 10) });
        }
        var buildMs = timer.Elapsed.TotalMilliseconds;
        timer.Restart();
        viewer.Document = document;
        var size = new Size(viewer.ActualWidth, viewer.ActualHeight);
        viewer.Measure(size);
        viewer.Arrange(new Rect(size));
        viewer.UpdateLayout();
        await WaitForRender(viewer);
        await viewer.Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ContextIdle);
        var renderMs = timer.Elapsed.TotalMilliseconds;
        timer.Stop();

        using var process = Process.GetCurrentProcess();
        process.Refresh();
        var working = process.WorkingSet64;
        var privateBytes = process.PrivateMemorySize64;
        var peak = process.PeakWorkingSet64;
        var managedAfter = GC.GetTotalMemory(true);
        GC.KeepAlive(viewer.Document);
        log.WriteLine($"{(avalon ? "AvalonEdit" : "TextBox")} | {workload.Blocks} | {workload.Lines} | {(long)workload.Blocks * workload.Lines} | {buildMs:F2} | {renderMs:F2} | {buildMs + renderMs:F2} | {managedBefore / MiB:F2} | {managedAfter / MiB:F2} | {(managedAfter - managedBefore) / MiB:F2} | {working / MiB:F2} | {privateBytes / MiB:F2} | {peak / MiB:F2}");
    }

    private static async Task WaitForRender(UIElement visual)
    {
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        TimeSpan? firstFrame = null;
        void OnRendering(object? sender, EventArgs args)
        {
            var time = ((RenderingEventArgs)args).RenderingTime;
            if (firstFrame is null)
                firstFrame = time;
            else if (time != firstFrame)
                completion.TrySetResult();
        }
        CompositionTarget.Rendering += OnRendering;
        try
        {
            visual.InvalidateVisual();
            await completion.Task.WaitAsync(TimeSpan.FromSeconds(30));
        }
        finally
        {
            CompositionTarget.Rendering -= OnRendering;
        }
    }

    private static string GenerateCode(int block, int lines)
    {
        var code = new StringBuilder();
        for (var line = 0; line < lines; line++)
        {
            if (line > 0)
                code.Append('\n');
            code.Append(line % 5 == 0
                ? $"// deterministic block {block}, line {line}: sample computation"
                : $"const value_{block}_{line}: number = Math.max({line}, {block}) + 42;");
        }
        return code.ToString();
    }

    private static Workload[]? ParseWorkloads(string[] args)
    {
        if (args.Length == 0)
            return [new(50, 200), new(20, 2000)];
        var blocks = 50;
        var lines = 200;
        var seen = new HashSet<string>(StringComparer.Ordinal);
        for (var i = 0; i < args.Length; i += 2)
        {
            if (i + 1 >= args.Length || !seen.Add(args[i]) ||
                !int.TryParse(args[i + 1], NumberStyles.None, CultureInfo.InvariantCulture, out var value) || value <= 0)
                return null;
            switch (args[i])
            {
                case "--blocks": blocks = value; break;
                case "--lines": lines = value; break;
                default: return null;
            }
        }
        return [new(blocks, lines)];
    }

    private static string FindProjectDirectory()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
            if (File.Exists(Path.Combine(directory.FullName, "AvalonEditBenchmark.csproj")))
                return directory.FullName;
        throw new DirectoryNotFoundException("Run this throwaway benchmark from its repository build output; AvalonEditBenchmark.csproj was not found.");
    }

    private sealed record Workload(int Blocks, int Lines);
}
