using System;
using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace SquadDash.Screenshots;

/// <summary>
/// Performs a pixel-level comparison between a freshly-captured screenshot and
/// its stored baseline, optionally writing a red-highlight diff image when
/// differences are found.
/// </summary>
internal static class ScreenshotComparator
{
    /// <summary>
    /// Compares <paramref name="refreshedPath"/> against <paramref name="baselinePath"/>
    /// pixel-by-pixel.
    /// </summary>
    /// <param name="baselinePath">Full path to the baseline PNG.</param>
    /// <param name="refreshedPath">Full path to the newly-captured PNG.</param>
    /// <returns>A <see cref="ScreenshotComparisonResult"/> describing the outcome.</returns>
    internal static ScreenshotComparisonResult Compare(string baselinePath, string refreshedPath)
    {
        // ── Guard: both files must exist ──────────────────────────────────────
        if (!File.Exists(baselinePath) || !File.Exists(refreshedPath))
        {
            return new ScreenshotComparisonResult(
                Skipped:          true,
                DimensionMismatch: false,
                TotalPixels:      0,
                DiffPixels:       0,
                MatchPercent:     0d,
                DiffImagePath:    null);
        }

        // ── Load both PNGs and normalise to Pbgra32 ───────────────────────────
        BitmapSource baseline;
        BitmapSource refreshed;

        try
        {
            baseline  = LoadAndNormalise(baselinePath);
            refreshed = LoadAndNormalise(refreshedPath);
        }
        catch (Exception)
        {
            // Unreadable / corrupt file — treat as skipped so the pipeline
            // continues rather than hard-failing the refresh pass.
            return new ScreenshotComparisonResult(
                Skipped:          true,
                DimensionMismatch: false,
                TotalPixels:      0,
                DiffPixels:       0,
                MatchPercent:     0d,
                DiffImagePath:    null);
        }

        // ── Dimension check ───────────────────────────────────────────────────
        if (baseline.PixelWidth  != refreshed.PixelWidth ||
            baseline.PixelHeight != refreshed.PixelHeight)
        {
            return new ScreenshotComparisonResult(
                Skipped:          false,
                DimensionMismatch: true,
                TotalPixels:      baseline.PixelWidth * baseline.PixelHeight,
                DiffPixels:       baseline.PixelWidth * baseline.PixelHeight,
                MatchPercent:     0d,
                DiffImagePath:    null);
        }

        int width  = baseline.PixelWidth;
        int height = baseline.PixelHeight;
        int total  = width * height;

        // stride for Pbgra32 (4 bytes per pixel, 32-bit aligned)
        int stride = (width * 4 + 3) & ~3;

        var baselineBytes  = new byte[stride * height];
        var refreshedBytes = new byte[stride * height];

        baseline.CopyPixels(baselineBytes,  stride, 0);
        refreshed.CopyPixels(refreshedBytes, stride, 0);

        // ── Pixel-level diff ──────────────────────────────────────────────────
        const int ChannelThreshold = 3; // tolerate minor anti-aliasing drift

        int diffPixels = 0;

        // We'll also build a diff mask so we can write the diff image in a
        // single pass.  Allocate lazily — only when we actually find a diff.
        byte[]? diffBytes = null;

        for (int i = 0; i < baselineBytes.Length; i += 4)
        {
            int db = Math.Abs(baselineBytes[i]     - refreshedBytes[i]);
            int dg = Math.Abs(baselineBytes[i + 1] - refreshedBytes[i + 1]);
            int dr = Math.Abs(baselineBytes[i + 2] - refreshedBytes[i + 2]);
            // ignore alpha (i+3) — UI screenshots typically have full opacity

            if (db > ChannelThreshold || dg > ChannelThreshold || dr > ChannelThreshold)
            {
                diffBytes ??= new byte[stride * height]; // lazy init
                // Mark this pixel pure-red (Pbgra32 byte order: B G R A)
                diffBytes[i]     = 0x00; // B
                diffBytes[i + 1] = 0x00; // G
                diffBytes[i + 2] = 0xFF; // R
                diffBytes[i + 3] = 0xFF; // A

                diffPixels++;
            }
        }

        double matchPercent = total > 0
            ? (double)(total - diffPixels) / total * 100.0
            : 100.0;

        // ── Write diff image ──────────────────────────────────────────────────
        string? diffImagePath = null;

        if (diffBytes is not null && diffPixels > 0)
        {
            try
            {
                // Derive the diff directory from the refreshed path's parent directory.
                // refreshedPath is typically: {screenshotsDir}/{name}.png
                var screenshotsDir = Path.GetDirectoryName(refreshedPath)
                                     ?? Path.GetTempPath();
                var diffDir        = Path.Combine(screenshotsDir, "diff");
                Directory.CreateDirectory(diffDir);

                var fileName      = Path.GetFileName(refreshedPath); // "{name}.png"
                diffImagePath     = Path.Combine(diffDir, fileName);

                var diffBitmap = new WriteableBitmap(
                    width, height,
                    96, 96,
                    PixelFormats.Pbgra32,
                    null);

                diffBitmap.WritePixels(
                    new System.Windows.Int32Rect(0, 0, width, height),
                    diffBytes,
                    stride,
                    0);

                using var stream = File.Create(diffImagePath);
                var encoder = new PngBitmapEncoder();
                encoder.Frames.Add(BitmapFrame.Create(diffBitmap));
                encoder.Save(stream);
            }
            catch (Exception)
            {
                // Diff image write is best-effort — don't fail the comparison.
                diffImagePath = null;
            }
        }

        return new ScreenshotComparisonResult(
            Skipped:          false,
            DimensionMismatch: false,
            TotalPixels:      total,
            DiffPixels:       diffPixels,
            MatchPercent:     matchPercent,
            DiffImagePath:    diffImagePath);
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static BitmapSource LoadAndNormalise(string path)
    {
        var uri     = new Uri(path, UriKind.Absolute);
        var decoder = BitmapDecoder.Create(
            uri,
            BitmapCreateOptions.None,
            BitmapCacheOption.OnLoad);

        BitmapSource source = decoder.Frames[0];

        // Normalise to Pbgra32 so CopyPixels always produces a predictable
        // 4-byte-per-pixel layout regardless of the source format.
        if (source.Format != PixelFormats.Pbgra32)
        {
            source = new FormatConvertedBitmap(
                source,
                PixelFormats.Pbgra32,
                null,
                0);
        }

        return source;
    }
}
