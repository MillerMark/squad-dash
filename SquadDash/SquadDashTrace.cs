using System;
using System.IO;
using System.Text;

namespace SquadDash;

internal static class SquadDashTrace {
    private static readonly object Gate = new();
    private static string _logPath = BuildGlobalLogPath();
    private const long MaxLogBytes = 32L * 1024L * 1024L;
    private const int MaxMessageChars = 16_000;
    private static long _approxLogBytes = GetLogLength(_logPath);

    /// <summary>Full path to the active trace log file.</summary>
    internal static string CurrentLogPath => _logPath;

    /// <summary>
    /// Switches the trace log to a per-workspace file inside
    /// <paramref name="workspaceStateDirectory"/>.  Any subsequent writes go to
    /// <c>trace.log</c> in that directory.  Call once after a workspace is loaded.
    /// </summary>
    internal static void SetWorkspace(string workspaceStateDirectory) {
        var newPath = Path.Combine(workspaceStateDirectory, "trace.log");
        lock (Gate) {
            if (string.Equals(_logPath, newPath, StringComparison.OrdinalIgnoreCase))
                return;
            _logPath = newPath;
            _approxLogBytes = GetLogLength(newPath);
        }
    }

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
        message = TrimMessageForTrace(message);
        try {
            var line = $"{DateTimeOffset.Now:yyyy-MM-dd HH:mm:ss.fff zzz} [{category}] {message}";
            lock (Gate) {
                AppendLineToLog(line);
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
        message = TrimMessageForTrace(message);
        try {
            var line = $"{DateTimeOffset.Now:yyyy-MM-dd HH:mm:ss.fff zzz} [{source}] {message}";
            lock (Gate) {
                AppendLineToLog(line);
            }
        }
        catch {
        }
        windowTarget?.AddEntry(MapSourceToCategory(source), message);   // outside lock
    }

    private static string TrimMessageForTrace(string message) {
        if (message.Length <= MaxMessageChars)
            return message;

        return message[..MaxMessageChars] + $"... [trace message truncated; originalChars={message.Length}]";
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
        "Sound"             => TraceCategory.Sound,
        _              => TraceCategory.General,
    };

    private static string BuildGlobalLogPath() {
        var directory = SquadDashPaths.AppData;
        Directory.CreateDirectory(directory);
        return Path.Combine(directory, "trace.log");
    }

    private static long GetLogLength(string path) {
        try {
            return File.Exists(path) ? new FileInfo(path).Length : 0;
        }
        catch {
            return 0;
        }
    }

    private static void AppendLineToLog(string line) {
        var path = _logPath;
        var payload = line + Environment.NewLine;
        var byteCount = Encoding.UTF8.GetByteCount(payload);
        RotateIfNeeded(path, byteCount);
        File.AppendAllText(path, payload, Encoding.UTF8);
        _approxLogBytes += byteCount;
    }

    private static void RotateIfNeeded(string path, int nextWriteBytes) {
        if (_approxLogBytes + nextWriteBytes <= MaxLogBytes)
            return;

        var archivePath = Path.Combine(Path.GetDirectoryName(path)!, "trace.1.log");
        try {
            if (File.Exists(archivePath))
                File.Delete(archivePath);
            if (File.Exists(path))
                File.Move(path, archivePath);
            _approxLogBytes = 0;
        }
        catch {
            // If rotation loses a race with another process, keep tracing rather than
            // surfacing diagnostics failures to the app.
            _approxLogBytes = File.Exists(path) ? new FileInfo(path).Length : 0;
        }
    }
}
