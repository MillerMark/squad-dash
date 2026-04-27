using System;
using System.IO;

namespace SquadDash.Screenshots;

/// <summary>
/// Writes timestamped log lines to both <see cref="Console.Out"/> and a
/// <c>refresh-log.txt</c> file inside the screenshots directory.
/// Console output ensures the log is visible in CI pipelines and shell scripts.
/// </summary>
public sealed class ScreenshotRefreshLog : IDisposable
{
    private readonly StreamWriter? _fileWriter;

    /// <param name="screenshotsDirectory">
    ///   Directory where <c>refresh-log.txt</c> will be written.
    ///   Created if it does not already exist.
    /// </param>
    public ScreenshotRefreshLog(string screenshotsDirectory)
    {
        try
        {
            Directory.CreateDirectory(screenshotsDirectory);
            var logPath  = Path.Combine(screenshotsDirectory, "refresh-log.txt");
            _fileWriter  = new StreamWriter(logPath, append: false) { AutoFlush = true };
        }
        catch
        {
            // File logging is best-effort; console output always works.
            _fileWriter = null;
        }
    }

    /// <summary>
    /// Writes <paramref name="message"/> prefixed with a UTC ISO-8601 timestamp to
    /// <see cref="Console.Out"/> and, if the file was opened successfully, to
    /// <c>refresh-log.txt</c>.
    /// </summary>
    public void Write(string message)
    {
        var line = $"[{DateTime.UtcNow:yyyy-MM-dd HH:mm:ss.fff}Z] {message}";
        Console.WriteLine(line);
        try { _fileWriter?.WriteLine(line); }
        catch { /* best-effort */ }
    }

    /// <inheritdoc/>
    public void Dispose() => _fileWriter?.Dispose();
}
