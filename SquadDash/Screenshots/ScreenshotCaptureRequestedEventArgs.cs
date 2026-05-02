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
    /// This is the <em>stored</em> bounds recorded at capture time and is used as a fallback
    /// when live anchor resolution fails.
    /// </summary>
    public CaptureBounds? CaptureBounds { get; }

    /// <summary>
    /// Edge anchors from the definition, used by <c>MainWindow</c> to resolve the
    /// <em>current</em> live bounds of the capture region from named WPF elements.
    /// When all four anchors can be resolved, the live bounds are used instead of
    /// <see cref="CaptureBounds"/>.  May be <c>null</c> for definitions that predate
    /// the anchor system.
    /// </summary>
    public EdgeAnchorRecord? TopAnchor    { get; }
    public EdgeAnchorRecord? RightAnchor  { get; }
    public EdgeAnchorRecord? BottomAnchor { get; }
    public EdgeAnchorRecord? LeftAnchor   { get; }

    /// <param name="definitionName">Kebab-case screenshot name.</param>
    /// <param name="outputPath">Full PNG output path.</param>
    /// <param name="captureBounds">
    ///   Optional stored bounds for sub-region cropping.  Used as a fallback when live
    ///   anchor resolution fails.  Pass <c>null</c> (the default) to capture the full window.
    /// </param>
    /// <param name="topAnchor">Top edge anchor from the definition.</param>
    /// <param name="rightAnchor">Right edge anchor from the definition.</param>
    /// <param name="bottomAnchor">Bottom edge anchor from the definition.</param>
    /// <param name="leftAnchor">Left edge anchor from the definition.</param>
    public ScreenshotCaptureRequestedEventArgs(
        string           definitionName,
        string           outputPath,
        CaptureBounds?   captureBounds = null,
        EdgeAnchorRecord? topAnchor    = null,
        EdgeAnchorRecord? rightAnchor  = null,
        EdgeAnchorRecord? bottomAnchor = null,
        EdgeAnchorRecord? leftAnchor   = null)
    {
        DefinitionName = definitionName ?? throw new ArgumentNullException(nameof(definitionName));
        OutputPath     = outputPath     ?? throw new ArgumentNullException(nameof(outputPath));
        CaptureBounds  = captureBounds;
        TopAnchor      = topAnchor;
        RightAnchor    = rightAnchor;
        BottomAnchor   = bottomAnchor;
        LeftAnchor     = leftAnchor;
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
