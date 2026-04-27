using System;
using System.Threading.Tasks;

namespace SquadDash.Screenshots;

/// <summary>
/// Event-args raised by <see cref="ScreenshotRefreshRunner.CaptureRequested"/> to
/// signal that <c>MainWindow</c> should perform an immediate
/// <see cref="System.Windows.Media.Imaging.RenderTargetBitmap"/> capture and save
/// the PNG to <see cref="OutputPath"/>.
/// </summary>
/// <remarks>
/// The runner awaits <see cref="WaitAsync"/> after raising the event, so
/// <c>MainWindow</c>'s handler must call either <see cref="SignalSaved"/> or
/// <see cref="SignalFailed"/> before returning — otherwise the runner will block
/// indefinitely.  Both methods are idempotent; only the first call has any effect.
/// </remarks>
public sealed class ScreenshotCaptureRequestedEventArgs : EventArgs
{
    private readonly TaskCompletionSource<string?> _tcs =
        new(TaskCreationOptions.RunContinuationsAsynchronously);

    /// <summary>Kebab-case name of the definition being captured.</summary>
    public string DefinitionName { get; }

    /// <summary>Full path where the PNG should be saved, including file name.</summary>
    public string OutputPath { get; }

    /// <summary>
    /// Optional sub-region to crop from the full-window render.
    /// When <c>null</c> the subscriber should save the full window (backward-compatible behaviour).
    /// </summary>
    public CaptureBounds? CaptureBounds { get; }

    /// <param name="definitionName">Kebab-case screenshot name.</param>
    /// <param name="outputPath">Full PNG output path.</param>
    /// <param name="captureBounds">
    ///   Optional bounds for sub-region cropping.  Pass <c>null</c> (the default) to
    ///   capture the full window.
    /// </param>
    public ScreenshotCaptureRequestedEventArgs(
        string        definitionName,
        string        outputPath,
        CaptureBounds? captureBounds = null)
    {
        DefinitionName = definitionName ?? throw new ArgumentNullException(nameof(definitionName));
        OutputPath     = outputPath     ?? throw new ArgumentNullException(nameof(outputPath));
        CaptureBounds  = captureBounds;
    }

    /// <summary>
    /// Called by the subscriber (<c>MainWindow</c>) after the PNG has been saved
    /// successfully.  Unblocks the runner to proceed to the next definition.
    /// </summary>
    public void SignalSaved() => _tcs.TrySetResult(null);

    /// <summary>
    /// Called by the subscriber (<c>MainWindow</c>) when capture fails.
    /// <paramref name="reason"/> is logged as the failure cause.
    /// </summary>
    public void SignalFailed(string reason) =>
        _tcs.TrySetResult(reason ?? "Unknown capture error");

    /// <summary>
    /// Awaited by <see cref="ScreenshotRefreshRunner"/> after it fires the event.
    /// Returns <c>null</c> on success or an error string on failure.
    /// </summary>
    internal Task<string?> WaitAsync() => _tcs.Task;
}
