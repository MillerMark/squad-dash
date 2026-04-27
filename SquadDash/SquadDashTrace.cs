using System;
using System.IO;
using System.Text;

namespace SquadDash;

internal static class SquadDashTrace {
    private static readonly object Gate = new();
    private static readonly string LogPath = BuildLogPath();

    /// <summary>
    /// When non-null, receives every trace entry in real time via
    /// <see cref="ILiveTraceTarget.AddEntry"/>.  Set by the live trace window
    /// when it opens; cleared when it closes so all routing calls become no-ops.
    /// </summary>
    internal static ILiveTraceTarget? TraceTarget { get; set; }

    /// <summary>
    /// Writes a trace entry using a pre-resolved <see cref="TraceCategory"/>.
    /// This is the canonical internal path; the string overload maps to it via
    /// <see cref="MapSourceToCategory"/>.
    /// </summary>
    internal static void Write(TraceCategory category, string message) {
        var windowTarget = TraceTarget;   // capture before lock — prevents dispatcher
                                          // callbacks from holding the file-write mutex
        try {
            var line = $"{DateTimeOffset.Now:yyyy-MM-dd HH:mm:ss.fff zzz} [{category}] {message}";
            lock (Gate) {
                File.AppendAllText(LogPath, line + Environment.NewLine, Encoding.UTF8);
            }
        }
        catch {
        }
        windowTarget?.AddEntry(category, message);   // outside lock
    }

    /// <summary>
    /// Writes a trace entry using a free-form source string.  The source tag is
    /// preserved verbatim in the file log (keeping the existing format); the
    /// window receives the mapped <see cref="TraceCategory"/>.
    /// </summary>
    public static void Write(string source, string message) {
        var windowTarget = TraceTarget;   // capture before lock
        try {
            var line = $"{DateTimeOffset.Now:yyyy-MM-dd HH:mm:ss.fff zzz} [{source}] {message}";
            lock (Gate) {
                File.AppendAllText(LogPath, line + Environment.NewLine, Encoding.UTF8);
            }
        }
        catch {
        }
        windowTarget?.AddEntry(MapSourceToCategory(source), message);   // outside lock
    }

    private static TraceCategory MapSourceToCategory(string source) => source switch {
        "UI"           => TraceCategory.UI,
        "Startup"      => TraceCategory.Startup,
        "AgentCards"   => TraceCategory.AgentCards,
        "Routing"      => TraceCategory.Routing,
        "Workspace"    => TraceCategory.Workspace,
        "Shutdown"     => TraceCategory.Shutdown,
        "SDK"          => TraceCategory.Bridge,
        "Bridge"       => TraceCategory.Bridge,
        "PERF"         => TraceCategory.Performance,
        "PromptHealth" => TraceCategory.PromptHealth,
        "Threads"           => TraceCategory.Threads,
        "TranscriptPanels"  => TraceCategory.TranscriptPanels,
        "Unhandled"         => TraceCategory.Unhandled,
        _              => TraceCategory.General,
    };

    private static string BuildLogPath() {
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var directory = Path.Combine(appData, "SquadDash");
        Directory.CreateDirectory(directory);
        return Path.Combine(directory, "trace.log");
    }
}
