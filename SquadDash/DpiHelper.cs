using System;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace SquadDash;

internal static class DpiHelper
{
    /// <summary>
    /// Converts a physical screen pixel coordinate (from PointToScreen) to WPF logical DIPs
    /// suitable for assigning to Window.Left / Window.Top.
    /// </summary>
    internal static Point PhysicalToLogical(Visual visual, Point physicalPoint)
    {
        var source = PresentationSource.FromVisual(visual);
        if (source?.CompositionTarget == null)
            return physicalPoint;
        return source.CompositionTarget.TransformFromDevice.Transform(physicalPoint);
    }

    /// <summary>
    /// Returns a <see cref="BitmapSource"/> whose DPI metadata is exactly 96×96.
    /// Physical pixel content is copied 1:1 — no resampling occurs.
    /// </summary>
    /// <remarks>
    /// <para>
    /// GDI <c>BitBlt</c> captures and <c>RenderTargetBitmap</c> renders at the
    /// monitor's physical DPI (e.g. 144 at 150 % scale).  Saving those bitmaps
    /// directly to PNG embeds the monitor DPI in the metadata, causing the images
    /// to appear at the wrong visual size in documentation viewers and WPF Image
    /// controls that honour the embedded DPI.
    /// </para>
    /// <para>
    /// Normalising to 96 DPI means every saved screenshot has a predictable,
    /// self-consistent size: the pixel dimensions ARE the logical (design-time) size
    /// at standard scaling, while the extra pixels on high-DPI monitors provide
    /// Retina-quality detail.
    /// </para>
    /// <para>
    /// If <paramref name="src"/> already reports 96 DPI (within ±0.5 DPI tolerance)
    /// it is returned unchanged without any copy.
    /// </para>
    /// </remarks>
    internal static BitmapSource NormalizeTo96Dpi(BitmapSource src)
    {
        if (Math.Abs(src.DpiX - 96.0) < 0.5 && Math.Abs(src.DpiY - 96.0) < 0.5)
            return src;

        var stride = (src.PixelWidth * src.Format.BitsPerPixel + 7) / 8;
        var pixels = new byte[stride * src.PixelHeight];
        src.CopyPixels(pixels, stride, 0);

        var normalized = BitmapSource.Create(
            src.PixelWidth, src.PixelHeight,
            96.0, 96.0,
            src.Format,
            src.Palette,
            pixels,
            stride);
        normalized.Freeze();
        return normalized;
    }
}
